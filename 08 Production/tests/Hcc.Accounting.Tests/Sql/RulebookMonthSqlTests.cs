using System.Text.Json;
using Hcc.Accounting.Tests.Golden;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 (lane acc1-tests): the rulebook golden month through the real outbox → accounting consumer → vouchers → cumulative trial balance
/// (PERM-ACC-001), on a disposable database with the real P1 policy. The events and the expectation are those of
/// <c>Golden/rulebook-month.json</c> (rulebook §8, April 2026, fictional); the month's flow is checked first (<c>expected.flow</c>), then
/// (ACC-1 integration) the §8.1 opening goes through the opening service and the cumulative trial balance equals <c>expected.closing</c>. Not driven: T16 (an Owner drawing from the bank, rulebook B6 — ACC-2) and T4 T6 T7b T11 T14 T17 T18 T19.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RulebookMonthSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static JsonDocument Month => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-month.json")));

    [Fact]
    [Trait("Lane", "acc1-chart")]
    public async Task The_months_events_become_balanced_vouchers_and_the_cumulative_trial_balance_equals_the_rulebook_flow()
    {
        using var month = Month;
        var root = month.RootElement;
        var events = root.GetProperty("events").EnumerateArray().ToList();

        // ── publish every event of the month (dates inside April 2026, closed days of the harness property) and run the consumer once ──
        var id = 0L;
        foreach (var ev in events)
            await db.PublishAsync(ev.GetProperty("type").GetString()!, ++id, ev.GetProperty("payload").Clone());
        Assert.Equal(events.Count, await db.ProjectAsync());   // exactly one source per event
        Assert.Equal(0, await db.ProjectAsync());              // and exactly once

        // ── the Owner's mapping of the month (Owner only, DEC-063 G11): the advance tender to 2000, folio restaurant lines to 4200 (F&B revenue: needs lane acc1-chart) ──
        foreach (var o in root.GetProperty("mappingOverrides").EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Reverse())
            await db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest(o.Name, o.Value.GetString()!), Ct);

        // ── the day views: every voucher balances, no error on any date that has vouchers, the bank deposit and the cash shortage sit on their rulebook dates ──
        var dates = events.Select(e => DateOnly.Parse(e.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToList();
        foreach (var d in dates)
        {
            var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
            Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
            Assert.Equal(0, day.Errors);
            Assert.Equal("Closed", day.Status);   // April is before the harness business date (24 Oct 2026)
        }

        Assert.Equal("bank-deposit", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 25), Ct)).Vouchers.Single().Sub);
        Assert.Equal("cash-difference", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 26), Ct)).Vouchers.Single().Sub);
        Assert.Equal("company-payment", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 10), Ct)).Vouchers.Single().Sub);

        // ── cumulative trial balance at 30 April = the expectation worked out from the rulebook (control ledgers; parties sum into them) ──
        var tb = await db.Queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(0, tb.Rows.Sum(r => r.BalancePaise));
        var got = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in tb.Rows) got[RulebookMonthTests.Control(r.Account)] = got.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in got.Where(x => x.Value == 0).Select(x => x.Key).ToList()) got.Remove(k);
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("flow").GetProperty("control").EnumerateObject()) want[p.Name] = p.Value.GetInt64();
        Assert.False(got.ContainsKey("9999"), "something posted to 9999 Suspense");
        Assert.Equal(want, got);

        // ── ACC-1 integration (lanes acc1-opening / acc1-statements / acc1-parties): the §8.1 opening through the opening service, the lock
        //    gate of coordinator ruling 5, and the cumulative / six-column trial balance with the opening ──
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var openingStore = new SqlOpeningStore(db.Platform.Connections);
        var opening = new OpeningBalanceService(db.Platform.Commands, openingStore, db.Store, policy, db.Clock);
        var gated = new AccountingService(db.Platform.Commands, db.Store, policy, db.Clock, openingStore);
        var queries = new AccountingQueries(db.Store, policy, null, null, db.Clock, openingStore);
        var reports = new AccountingReportQueries(db.Store, policy, null, new OpeningReportSource(openingStore), new SqlReportPartyNames(db.Platform.Connections));

        // no entry at all: days still lock (DEC-148, ruling 5) and the six-column trial balance says "opening balances pending"
        Assert.Equal("Locked", (await gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 2)), Ct)).Value.Status);
        Assert.True((await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), false, Ct)).OpeningPending);

        var asOf = DateOnly.Parse(root.GetProperty("asOf").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var lines = root.GetProperty("opening").EnumerateArray()
            .Select(x => new OpeningLineRequest(RulebookMonthTests.ServerLedger(x.GetProperty("ledger").GetString()!), x.GetProperty("debitPaise").GetInt64(), x.GetProperty("creditPaise").GetInt64()))
            .ToList();
        var saved = (await opening.SaveAsync(db.As("MGR"), Key(), new SaveOpeningBalancesRequest(asOf, lines, "Rulebook §8.1 (fictional)"), Ct)).Value;
        Assert.Equal(("draft", 0L, 774_000_000L), (saved.Status, saved.DifferencePaise, saved.DebitPaise));

        // a draft gates every lock on or after asOf
        var refused = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 3)), Ct));
        Assert.StartsWith("Opening balances pending", refused.Message, StringComparison.Ordinal);
        await opening.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest(null), Ct);
        Assert.Equal("Locked", (await gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 3)), Ct)).Value.Status);

        // cumulative trial balance at 30 April = the rulebook closing (opening included), control level
        var cum = await queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(cum.Balanced);
        var closing = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in cum.Rows) closing[RulebookMonthTests.Control(r.Account)] = closing.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in closing.Where(x => x.Value == 0).Select(x => x.Key).ToList()) closing.Remove(k);
        var wantClosing = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("closing").GetProperty("control").EnumerateObject()) wantClosing[p.Name] = p.Value.GetInt64();
        Assert.Equal(wantClosing, closing);

        // the six-column trial balance of April (the opening journal dated 1 April is the opening): balanced in every column pair, no longer pending
        foreach (var detail in new[] { false, true })
        {
            var tb6 = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), detail, Ct);
            Assert.True(tb6.Balanced, "detail " + detail);
            Assert.False(tb6.OpeningPending);
            Assert.Equal((774_000_000L, 774_000_000L), (tb6.Totals.OpeningDebitPaise, tb6.Totals.OpeningCreditPaise));
        }

        // a party row of the opening (2200/EV-MORNING) sits under its control in the detail view
        var detailTb = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), true, Ct);
        Assert.Contains(detailTb.Rows, r => r.Account == "2200/EV-MORNING" && r.Control == "2200");
    }
}
