using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-2 manual vouchers on SQL (migrations 4238–4241) with the real P1 policy: who enters (PERM-ACC-010: Owner approved at once, Accountant
/// pending) and who decides / voids (PERM-ACC-011: Owner), the guards against the chart, parties and locks, ManualVoucherApproved booked by
/// the accounting consumer as manual:&lt;id&gt; (Payment / Contra numbering), void on an unlocked day (posts nothing) and on a locked day
/// (reversing voucher dated the business date, snapshot kept, no drift), Add loan and a loan EMI to the loan party, the GET view.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ManualVoucherSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D = AccountingDb.Day;            // 2026-10-24
    private const string Bank = "1030/BANK-1";
    private const string Till = "1000/TILL";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static long R(long rupees) => rupees * 100;

    private static ManualLineRequest L(string account, long debit, long credit) => new(account, debit, credit);

    private static SaveManualVoucherRequest Charges(DateOnly date, long? id = null)
        => new(id, date, "Payment", "Bank charges October", "STMT-OCT", "bank-charges", [L("5080", R(590), 0), L(Bank, 0, R(590))]);

    private async Task<IReadOnlyList<Hcc.Domain.Accounting.Voucher>> ManualLiveAsync(DateOnly date)
        => (await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, date, Ct)).Live.Where(v => v.SourceKind == "manual").ToList();

    [Fact]
    public async Task Enter_approve_reject_void_unlocked_and_locked_loans_and_the_books()
    {
        foreach (var (code, kind) in new[] { ("BANK-1", "bank"), ("TILL", "till") })
            await db.ScalarAsync("""
                INSERT mst.bank_account(property_id, code, name, sort_order, kind, updated_by, updated_utc) VALUES (@p, @c, @n, 10, @k, @u, '2026-10-01');
                """, ("@p", SqlDbType.Int, db.PropertyId), ("@c", SqlDbType.VarChar, code), ("@n", SqlDbType.NVarChar, "Fictional " + code), ("@k", SqlDbType.VarChar, kind),
                ("@u", SqlDbType.Int, db.Users["OWN"]));
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var store = new SqlManualVoucherStore(db.Platform.Connections);
        var svc = new ManualVoucherService(db.Platform.Commands, store, db.Store, policy, db.Clock);
        var reads = new ManualVoucherQueries(store, db.Store, policy);
        var accounting = new AccountingService(db.Platform.Commands, db.Store, policy, db.Clock);

        // ── roles: enter = Owner, Accountant (PERM-ACC-010); bad debt Owner only (A18) ──
        foreach (var role in new[] { "MGR", "REC", "CSH", "SUP", "HKP", "MNT", "TAD", "RST" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.SaveAsync(db.As(role), Key(), Charges(D), Ct));
        var bad = new SaveManualVoucherRequest(null, D, "Journal", "Write-off", null, "bad-debt", [L("5300", R(1000), 0), L("1110/CMP-1", 0, R(1000))]);
        var refusedBad = await Fails(HccErrorCode.FORBIDDEN, () => svc.SaveAsync(db.As("ACC"), Key(), bad, Ct));
        Assert.Equal(ManualVoucherRules.MsgBadDebt, refusedBad.Message);

        // ── guards on the server: same messages ──
        Assert.Equal(ManualVoucherRules.MsgFuture, (await Fails(HccErrorCode.VALIDATION, () => svc.SaveAsync(db.As("ACC"), Key(), Charges(D.AddDays(1)), Ct))).Message);
        Assert.Equal(ManualVoucherRules.MsgLineControl(2, "1030"), (await Fails(HccErrorCode.VALIDATION, () => svc.SaveAsync(db.As("ACC"), Key(),
            Charges(D) with { Lines = [L("5080", R(590), 0), L("1030", 0, R(590))] }, Ct))).Message);
        Assert.Equal(ManualVoucherRules.MsgLineUnknown(2, "1030/BANK-9"), (await Fails(HccErrorCode.VALIDATION, () => svc.SaveAsync(db.As("ACC"), Key(),
            Charges(D) with { Lines = [L("5080", R(590), 0), L("1030/BANK-9", 0, R(590))] }, Ct))).Message);
        Assert.Equal(ManualVoucherRules.MsgContra, (await Fails(HccErrorCode.VALIDATION, () => svc.SaveAsync(db.As("OWN"), Key(),
            Charges(D) with { Type = "Contra" }, Ct))).Message);

        // ── the Accountant's voucher is pending (nothing booked); the Owner approves ──
        var key = Key();
        var pending = (await svc.SaveAsync(db.As("ACC"), key, Charges(D), Ct)).Value;
        Assert.Equal((await svc.SaveAsync(db.As("ACC"), key, Charges(D), Ct)).Value, pending); // idempotent replay
        Assert.Equal("pending", pending.Status);
        await db.ProjectAsync();
        Assert.Empty(await ManualLiveAsync(D));
        foreach (var role in new[] { "ACC", "MGR" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.DecideAsync(db.As(role), Key(), new DecideManualVoucherRequest(pending.VoucherId, true, null), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(pending.VoucherId, true, null, "AAAAAAAAAAE="), Ct));
        var approved = (await svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(pending.VoucherId, true, null, pending.Version), Ct)).Value;
        Assert.Equal("approved", approved.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(pending.VoucherId, true, null), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SaveAsync(db.As("ACC"), Key(), Charges(D, pending.VoucherId), Ct));
        await db.ProjectAsync();
        var live = Assert.Single(await ManualLiveAsync(D));
        Assert.Equal(("PMT/20261024/001", "manual:" + pending.VoucherId, "bank-charges"), (live.Number, live.SourceKey, live.Sub));
        Assert.Equal([new Hcc.Domain.Accounting.VoucherEntry("5080", R(590), 0), new Hcc.Domain.Accounting.VoucherEntry(Bank, 0, R(590))], live.Entries);
        Assert.Equal($"Bank charges October · STMT-OCT · manual {pending.VoucherId}", live.Narration);

        // ── reject (reason needed), then the maker corrects it → pending again ──
        var second = (await svc.SaveAsync(db.As("ACC"), Key(), new SaveManualVoucherRequest(null, D, "Receipt", "Interest credited", null, "interest-income",
            [L(Bank, R(120), 0), L("4960", 0, R(120))]), Ct)).Value;
        await Fails(HccErrorCode.VALIDATION, () => svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(second.VoucherId, false, " "), Ct));
        Assert.Equal("rejected", (await svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(second.VoucherId, false, "Wrong amount"), Ct)).Value.Status);
        var corrected = (await svc.SaveAsync(db.As("ACC"), Key(), new SaveManualVoucherRequest(second.VoucherId, D, "Receipt", "Interest credited", null, "interest-income",
            [L(Bank, R(125), 0), L("4960", 0, R(125))]), Ct)).Value;
        Assert.Equal((second.VoucherId, "pending"), (corrected.VoucherId, corrected.Status));

        // ── the Owner's own voucher is approved at once; voided on an unlocked day it posts nothing ──
        var contra = (await svc.SaveAsync(db.As("OWN"), Key(), new SaveManualVoucherRequest(null, D, "Contra", "Float topped up", null, "contra",
            [L("1005", R(500), 0), L(Till, 0, R(500))]), Ct)).Value;
        Assert.Equal("approved", contra.Status);
        await db.ProjectAsync();
        Assert.Equal(["PMT/20261024/001", "CTR/20261024/001"], (await ManualLiveAsync(D)).Select(v => v.Number));
        await Fails(HccErrorCode.FORBIDDEN, () => svc.VoidAsync(db.As("ACC"), Key(), new VoidManualVoucherRequest(contra.VoucherId, "Duplicate"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.VoidAsync(db.As("OWN"), Key(), new VoidManualVoucherRequest(contra.VoucherId, ""), Ct));
        var voided = (await svc.VoidAsync(db.As("OWN"), Key(), new VoidManualVoucherRequest(contra.VoucherId, "Duplicate entry"), Ct)).Value;
        Assert.Equal(("void", (long?)null), (voided.Status, voided.ReversalId));
        await db.ProjectAsync();
        Assert.Equal(["PMT/20261024/001"], (await ManualLiveAsync(D)).Select(v => v.Number));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.VoidAsync(db.As("OWN"), Key(), new VoidManualVoucherRequest(contra.VoucherId, "Again"), Ct));
        // The rejected-then-pending voucher must leave the queue before the day closes: reject it for good.
        await svc.DecideAsync(db.As("OWN"), Key(), new DecideManualVoucherRequest(second.VoucherId, false, "Bank statement differs"), Ct);

        // ── lock the day; then a voucher dated it is refused and the void becomes a reversal on the business date ──
        await db.SetBusinessDateAsync(D.AddDays(1));
        Assert.Equal("Locked", (await accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct)).Value.Status);
        var late = await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SaveAsync(db.As("OWN"), Key(), Charges(D), Ct));
        Assert.Equal(ManualVoucherRules.MsgLocked(D), late.Message);
        var reversed = (await svc.VoidAsync(db.As("OWN"), Key(), new VoidManualVoucherRequest(pending.VoucherId, "Bank refunded the charge"), Ct)).Value;
        Assert.NotNull(reversed.ReversalId);
        await db.ProjectAsync();
        var rev = Assert.Single(await ManualLiveAsync(D.AddDays(1)));
        Assert.Equal(("PMT/20261025/001", "manual:" + reversed.ReversalId), (rev.Number, rev.SourceKey));
        Assert.Equal([new Hcc.Domain.Accounting.VoucherEntry("5080", 0, R(590)), new Hcc.Domain.Accounting.VoucherEntry(Bank, R(590), 0)], rev.Entries);
        var day = await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, D, Ct);
        Assert.Contains(day.Lock!.Vouchers, v => v.SourceKey == "manual:" + pending.VoucherId);
        Assert.DoesNotContain(day.Issues, i => i.Kind == "drift");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.VoidAsync(db.As("OWN"), Key(), new VoidManualVoucherRequest(reversed.ReversalId!.Value, "Undo"), Ct));

        // ── Add loan (Owner) and the loan received / EMI to the loan party ──
        await Fails(HccErrorCode.FORBIDDEN, () => svc.AddLoanAsync(db.As("ACC"), Key(), new AddLoanRequest("HDFC", "HDFC term loan", "HDFC Bank (fictional)", null, null), Ct));
        var loan = (await svc.AddLoanAsync(db.As("OWN"), Key(), new AddLoanRequest("hdfc term", "HDFC term loan", "HDFC Bank (fictional)", "7788", null), Ct)).Value;
        Assert.Equal("2400/LN-HDFC-TERM", loan.Account);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.AddLoanAsync(db.As("OWN"), Key(), new AddLoanRequest("HDFC-TERM", "Second loan", "HDFC", null, null), Ct));
        var b = D.AddDays(1);
        await svc.SaveAsync(db.As("OWN"), Key(), new SaveManualVoucherRequest(null, b, "Receipt", "Term loan disbursed", null, "loan-received",
            [L(Bank, R(5_00_000), 0), L(loan.Account, 0, R(5_00_000))]), Ct);
        await svc.SaveAsync(db.As("OWN"), Key(), new SaveManualVoucherRequest(null, b, "Payment", "Loan EMI", null, "loan-emi",
            [L(loan.Account, R(15_000), 0), L("5090", R(5_000), 0), L(Bank, 0, R(20_000))]), Ct);
        await db.ProjectAsync();
        var loanEntries = (await ManualLiveAsync(b)).SelectMany(v => v.Entries).Where(e => e.Account == loan.Account).ToList();
        Assert.Equal((R(15_000), R(5_00_000)), (loanEntries.Sum(e => e.DebitPaise), loanEntries.Sum(e => e.CreditPaise)));

        // ── GET /accounting/manual: newest first, names, loans; the Cashier sees no books (DEC-079) ──
        var view = await reads.GetAsync(db.As("ACC"), Ct);
        Assert.Equal(6, view.Vouchers.Count);
        Assert.Equal(["approved", "approved", "approved", "void", "rejected", "void"], view.Vouchers.Select(v => v.Status));
        Assert.Equal(reversed.ReversalId, view.Vouchers.Single(v => v.VoidOf == pending.VoucherId).VoucherId);
        Assert.Equal("Bank loans (secured) · HDFC term loan", view.Vouchers[0].Lines[0].Name);
        Assert.Equal(("2400/LN-HDFC-TERM", "HDFC Bank (fictional)", "7788"), (view.Loans.Single().Account, view.Loans.Single().Lender, view.Loans.Single().AccountLast4));
        await Fails(HccErrorCode.FORBIDDEN, () => reads.GetAsync(db.As("CSH"), Ct));

        // ── audit rows; the mapping table never lists manual lines ──
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_LOAN_ADDED';"));
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_MANUAL_VOIDED';"));
        Assert.DoesNotContain((await db.Store.KeyUsesAsync(null, db.PropertyId, Ct)).Keys, k => k.StartsWith("ledger:", StringComparison.Ordinal));
    }
}
