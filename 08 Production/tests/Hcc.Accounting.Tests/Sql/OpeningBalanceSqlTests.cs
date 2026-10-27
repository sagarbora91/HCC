using System.Data;
using Hcc.Accounting.Tests.Domain;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 opening balances on SQL (migrations 4220 / 4221) with the real P1 policy: who enters / approves / reopens (PERM-ACC-008 / 009),
/// the 1400 balancing line, the period lock refused while the opening is pending and allowed after approval, the reopen rule, the
/// approved entry in the cumulative trial balance (rulebook §8.1, ₹77,40,000 both sides), stale versions and idempotent replay.
/// </summary>
[Trait("Category", "Sql")]
public sealed class OpeningBalanceSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D = AccountingDb.Day;            // 2026-10-24
    private static readonly DateOnly Fy = new(2026, 4, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static long R(long rupees) => rupees * 100;

    [Fact]
    public async Task Enter_approve_lock_gate_reopen_and_the_cumulative_trial_balance()
    {
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var store = new SqlOpeningStore(db.Platform.Connections);
        var svc = new OpeningBalanceService(db.Platform.Commands, store, db.Store, policy, db.Clock);
        var reads = new OpeningBalanceQueries(store, db.Store, policy);
        var accounting = new AccountingService(db.Platform.Commands, db.Store, policy, db.Clock, store);
        var queries = new AccountingQueries(db.Store, policy, null, null, db.Clock, store);
        var save = (string role, IReadOnlyList<OpeningLineRequest> lines) => svc.SaveAsync(db.As(role), Key(), new SaveOpeningBalancesRequest(Fy, lines, null), Ct);

        // ── nothing entered: pending, nothing in the trial balance ──
        var none = await reads.GetAsync(db.As("ACC"), Ct);
        Assert.Equal((Fy, true, (OpeningEntryView?)null), (none.FyStart, none.Pending, none.Entry));
        await Fails(HccErrorCode.FORBIDDEN, () => reads.GetAsync(db.As("CSH"), Ct)); // DEC-079: no books for the Cashier

        // ── permissions: enter = Owner, Manager, Accountant (PERM-ACC-008) ──
        foreach (var role in new[] { "REC", "CSH", "SUP", "HKP", "MNT", "TAD", "RST" })
            await Fails(HccErrorCode.FORBIDDEN, () => save(role, OpeningBalanceTests.Rulebook));
        await Fails(HccErrorCode.VALIDATION, () => svc.SaveAsync(db.As("MGR"), Key(), new SaveOpeningBalancesRequest(new DateOnly(2025, 4, 1), OpeningBalanceTests.Rulebook, null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => save("MGR", [new("1000", 100, 100)]));

        // ── an unbalanced draft by the Accountant parks the gap in 1400 ──
        var draft = (await save("ACC", OpeningBalanceTests.WithoutCapital)).Value;
        Assert.Equal((Fy, "draft", 10, R(77_40_000), R(77_40_000), -R(57_05_000)), (draft.AsOf, draft.Status, draft.Lines, draft.DebitPaise, draft.CreditPaise, draft.DifferencePaise));
        var view = (await reads.GetAsync(db.As("MGR"), Ct)).Entry!;
        Assert.Equal(("1400", "Opening-balance difference", 0L, R(57_05_000)), (view.Lines[^1].Account, view.Lines[^1].Name, view.Lines[^1].DebitPaise, view.Lines[^1].CreditPaise));
        Assert.Equal("Company receivables (city ledger) · CMP-DECCAN", view.Lines.Single(l => l.Account == "1110/CMP-DECCAN").Name);
        Assert.Equal(db.Users["ACC"], view.EnteredBy);

        // ── a draft never counts in the trial balance; approve = Owner only ──
        var tbDraft = await queries.TrialBalanceAsync(db.As("ACC"), D, cumulative: true, Ct);
        Assert.False(tbDraft.OpeningIncluded);
        Assert.DoesNotContain(tbDraft.Rows, r => r.Account == "1500");
        foreach (var role in new[] { "MGR", "ACC", "CSH" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.ApproveAsync(db.As(role), Key(), new ApproveOpeningBalancesRequest(null), Ct));

        // ── the period lock of a closed date on or after asOf waits while the opening is a draft ──
        await db.SetBusinessDateAsync(D.AddDays(1));
        var refused = await Fails(HccErrorCode.INVALID_TRANSITION, () => accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct));
        Assert.StartsWith("Opening balances pending: the opening entry as of 2026-04-01 is a draft", refused.Message, StringComparison.Ordinal);

        // ── approved with 1400 ≠ 0: allowed, still pending, the lock still waits ──
        await Fails(HccErrorCode.STALE_VERSION, () => svc.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest("AAAAAAAAAAE="), Ct));
        var approved = (await svc.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest(view.Version), Ct)).Value;
        Assert.Equal("approved", approved.Status);
        Assert.True((await reads.GetAsync(db.As("OWN"), Ct)).Pending);
        var diff = await Fails(HccErrorCode.INVALID_TRANSITION, () => accounting.LockAsync(db.As("MGR"), Key(), new LockPeriodRequest(D), Ct));
        Assert.Contains("sits in 1400 Opening-balance difference", diff.Message, StringComparison.Ordinal);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => save("MGR", OpeningBalanceTests.Rulebook)); // read-only once approved

        // ── reopen (Owner, reason), correct, approve ──
        await Fails(HccErrorCode.FORBIDDEN, () => svc.ReopenAsync(db.As("MGR"), Key(), new ReopenOpeningBalancesRequest("Capital missed", null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.ReopenAsync(db.As("OWN"), Key(), new ReopenOpeningBalancesRequest(" ", null), Ct));
        Assert.Equal("draft", (await svc.ReopenAsync(db.As("OWN"), Key(), new ReopenOpeningBalancesRequest("Owner capital missed", null), Ct)).Value.Status);
        var key = Key();
        var saved = await svc.SaveAsync(db.As("MGR"), key, new SaveOpeningBalancesRequest(Fy, OpeningBalanceTests.Rulebook, "Rulebook §8.1"), Ct);
        var replay = await svc.SaveAsync(db.As("MGR"), key, new SaveOpeningBalancesRequest(Fy, OpeningBalanceTests.Rulebook, "Rulebook §8.1"), Ct);
        Assert.Equal(saved.Value, replay.Value);
        Assert.Equal((11, 0L), (saved.Value.Lines, saved.Value.DifferencePaise));
        await svc.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest(null), Ct);
        var state = await reads.GetAsync(db.As("ACC"), Ct);
        Assert.Equal((false, "approved", db.Users["OWN"], "Rulebook §8.1"), (state.Pending, state.Entry!.Status, state.Entry.ApprovedBy, state.Entry.Note));

        // ── the cumulative trial balance carries the approved opening from asOf on ──
        var tb = await queries.TrialBalanceAsync(db.As("ACC"), D, cumulative: true, Ct);
        Assert.Equal((Fy, true, true), (tb.OpeningAsOf, tb.OpeningIncluded, tb.Balanced));
        Assert.Equal(R(77_40_000), tb.DebitPaise);
        Assert.Equal(R(60_00_000), tb.Rows.Single(r => r.Account == "1500").DebitPaise);
        // ACC-1 integration: the (non-detail) cumulative trial balance folds party opening rows into their control (contract §3).
        Assert.Equal(R(40_000), tb.Rows.Single(r => r.Account == "1110").DebitPaise);
        Assert.Equal(R(20_00_000), tb.Rows.Single(r => r.Account == "2400").CreditPaise);
        Assert.DoesNotContain(tb.Rows, r => r.Account.Contains('/', StringComparison.Ordinal));
        Assert.Equal(R(20_000), tb.Rows.Single(r => r.Account == "1000").DebitPaise);
        var before = await queries.TrialBalanceAsync(db.As("ACC"), Fy.AddDays(-1), cumulative: true, Ct);
        Assert.False(before.OpeningIncluded);
        Assert.Equal(11, before.Opening!.Count);

        // ── now the lock goes through; afterwards the opening cannot be reopened ──
        Assert.Equal("Locked", (await accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct)).Value.Status);
        Assert.Equal(D, (await reads.GetAsync(db.As("OWN"), Ct)).LockedFrom);
        var late = await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.ReopenAsync(db.As("OWN"), Key(), new ReopenOpeningBalancesRequest("Late correction", null), Ct));
        Assert.Contains("locked on or after 2026-04-01 (2026-10-24)", late.Message, StringComparison.Ordinal);

        // ── audit rows and the stored reopen reason ──
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_OPENING_SAVED';"));
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_OPENING_APPROVED';"));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_OPENING_REOPENED';"));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.opening_entry WHERE property_id = @p AND reopen_reason = N'Owner capital missed';", ("@p", SqlDbType.Int, db.PropertyId)));
    }

    [Fact]
    public async Task The_tables_refuse_a_two_sided_line_a_bad_account_and_an_entry_not_dated_1_April()
    {
        // A second fictional property, so the other test's entry is untouched whatever the order.
        var other = Convert.ToInt32(await db.ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEM2', N'HCC Demo Annexe', 'Asia/Kolkata', '2026-10-24');
            """), System.Globalization.CultureInfo.InvariantCulture);
        var p = ("@p", SqlDbType.Int, (object)other);
        await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() => db.ScalarAsync(
            "INSERT acc.opening_entry(property_id, as_of, status, entered_by, entered_utc) VALUES (@p, '2026-05-01', 'draft', 1, '2026-10-24');", p));
        await db.ScalarAsync("INSERT acc.opening_entry(property_id, as_of, status, entered_by, entered_utc) VALUES (@p, '2026-04-01', 'draft', 1, '2026-10-24');", p);
        await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() => db.ScalarAsync(
            "INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES (@p, 900, '1000', 5, 5);", p));
        await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() => db.ScalarAsync(
            "INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES (@p, 901, 'CASH', 5, 0);", p));
        await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() => db.ScalarAsync(
            "UPDATE acc.opening_entry SET status = 'approved' WHERE property_id = @p;", p)); // approved needs who and when
        Assert.Equal(3, await db.LongAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-ACC-008' AND role_code IN ('OWN','MGR','ACC');"));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-ACC-009';"));
    }
}
