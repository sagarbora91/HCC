using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-2 parity (contract §8; ACC1-DONE §7 item 8; migration 4257): the aggregated opening read. A closed month whose every day with sources is
/// locked is stored once in acc.ledger_month(_balance) from the lock snapshots; the reports (statement, six-column trial balance, day book)
/// read the stored months + the days after them and answer exactly what the day-by-day derivation answers; an unlock / re-lock rebuilds the
/// month (fingerprint); the reports accept any range.
/// </summary>
[Trait("Category", "Sql")]
public sealed class MonthBalanceTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly Jun10 = new(2026, 6, 10), Jul5 = new(2026, 7, 5), Jul20 = new(2026, 7, 20), Aug3 = new(2026, 8, 3), Sep15 = new(2026, 9, 15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqlAccessPolicy Policy => new(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);

    /// <summary>With the month rows (as production composes it) and without (every day since the first source).</summary>
    private (AccountingReportQueries Months, AccountingReportQueries Days) Queries() =>
        (new AccountingReportQueries(db.Store, Policy, months: new SqlLedgerMonthBalanceStore(db.Platform.Connections), clock: db.Clock), new AccountingReportQueries(db.Store, Policy));

    private async Task SameAsync(DateOnly from, DateOnly to)
    {
        var (m, d) = Queries();
        foreach (var account in new[] { "1000", "5000", "5010", "1030" })
        {
            var a = await m.StatementAsync(db.As("ACC"), account, from, to, Ct);
            var b = await d.StatementAsync(db.As("ACC"), account, from, to, Ct);
            Assert.Equal((b.OpeningPaise, b.DebitPaise, b.CreditPaise, b.ClosingPaise), (a.OpeningPaise, a.DebitPaise, a.CreditPaise, a.ClosingPaise));
            Assert.Equal(b.Rows, a.Rows);
        }

        foreach (var detail in new[] { false, true })
        {
            var a = await m.TrialBalanceAsync(db.As("ACC"), from, to, detail, Ct);
            var b = await d.TrialBalanceAsync(db.As("ACC"), from, to, detail, Ct);
            Assert.Equal(b.Rows, a.Rows);
            Assert.Equal(b.Totals, a.Totals);
            Assert.True(a.Balanced);
        }

        Assert.Equal((await d.DayBookAsync(db.As("ACC"), from, to, Ct)).Rows, (await m.DayBookAsync(db.As("ACC"), from, to, Ct)).Rows);
    }

    private async Task<List<(DateOnly Month, int Count, long Sum)>> StoredAsync()
    {
        var list = new List<(DateOnly, int, long)>();
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT month_start, lock_count, lock_sum FROM acc.ledger_month WHERE property_id = @p ORDER BY month_start;", conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = db.PropertyId;
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        while (await rd.ReadAsync(Ct)) list.Add((DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetInt32(1), rd.GetInt64(2)));
        return list;
    }

    private async Task<long> LockIdAsync(DateOnly d) => (await db.Store.CurrentLockAsync(null, db.PropertyId, d, Ct))!.LockId;

    [Fact]
    public async Task Locked_months_are_stored_once_and_the_reports_answer_as_the_day_by_day_derivation_through_unlock_and_relock()
    {
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9101, new ExpensePostedEvent(9101, Jun10, "Supplies", "Cash", 30_000, "Demo Stores (fictional)", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9102, new ExpensePostedEvent(9102, Jul5, "Laundry", "Cash", 12_500, "Demo Linen (fictional)", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9103, new ExpensePostedEvent(9103, Jul20, "Supplies", "Bank transfer", 5_000, "Demo Stores (fictional)", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9104, new ExpensePostedEvent(9104, Aug3, "Supplies", "Cash", 7_000, "Demo Stores (fictional)", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9105, new ExpensePostedEvent(9105, Sep15, "Laundry", "Cash", 2_000, "Demo Linen (fictional)", null));
        await db.ProjectAsync();
        foreach (var d in new[] { Jun10, Jul5, Jul20 }) await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(d), Ct);

        // a report from September: June and July are stored (every day with sources locked); August has an unlocked day → read day by day
        await SameAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 24));
        var stored = await StoredAsync();
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)], stored.Select(x => x.Month));
        Assert.Equal((1, await LockIdAsync(Jun10)), (stored[0].Count, stored[0].Sum));
        Assert.Equal((2, await LockIdAsync(Jul5) + await LockIdAsync(Jul20)), (stored[1].Count, stored[1].Sum));
        var (m, _) = Queries();
        var st = await m.StatementAsync(db.As("ACC"), "1000", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 24), Ct);
        Assert.Equal(-49_500, st.OpeningPaise); // June 30,000 + July 12,500 (stored) + August 7,000 (day by day)

        // idempotent: a second read rebuilds nothing; a report from mid-July takes June only
        db.Clock.Now = db.Clock.Now.AddHours(1);
        await SameAsync(new DateOnly(2026, 7, 10), new DateOnly(2026, 8, 31));
        Assert.Equal(stored, await StoredAsync());
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(DISTINCT built_utc) FROM acc.ledger_month WHERE property_id = @p;", ("@p", SqlDbType.Int, db.PropertyId)));

        // locking August stores it too (a later report)
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(Aug3), Ct);
        await SameAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24));
        Assert.Equal(3, (await StoredAsync()).Count);

        // unlock 20 July: July has a source day without a lock → the reads stop storing at July and stay exact
        var lk = (await db.Store.CurrentLockAsync(null, db.PropertyId, Jul20, Ct))!;
        await db.Accounting.UnlockAsync(db.As("OWN"), AccountingDb.Key(), new UnlockPeriodRequest(Jul20, "Late bank charge (fictional)", lk.Version), Ct);
        await SameAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24));
        await SameAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        // re-lock: a new lock id → July's fingerprint changed → rebuilt on the next read; figures unchanged
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(Jul20), Ct);
        await SameAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24));
        var july = (await StoredAsync()).Single(x => x.Month == new DateOnly(2026, 7, 1));
        Assert.Equal((2, await LockIdAsync(Jul5) + await LockIdAsync(Jul20)), (july.Count, july.Sum));
        Assert.NotEqual(lk.LockId, await LockIdAsync(Jul20));

        // any range: a report over more than a year is answered (no 366-day limit), the same both ways
        await SameAsync(new DateOnly(2025, 4, 1), new DateOnly(2026, 10, 24));
        // the month rows balance and equal the locked vouchers of the month
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM acc.ledger_month WHERE property_id = @p AND debit_paise <> credit_paise;", ("@p", SqlDbType.Int, db.PropertyId)));
        Assert.Equal(12_500 + 5_000, await db.LongAsync("SELECT debit_paise FROM acc.ledger_month WHERE property_id = @p AND month_start = '2026-07-01';", ("@p", SqlDbType.Int, db.PropertyId)));
    }

    [Fact]
    public void Plan_uses_the_months_before_the_first_unlocked_one_and_rebuilds_missing_or_stale_fingerprints()
    {
        var jun = new DateOnly(2026, 6, 1);
        var jul = new DateOnly(2026, 7, 1);
        var aug = new DateOnly(2026, 8, 1);
        var sep = new DateOnly(2026, 9, 1);
        var current = new Dictionary<DateOnly, MonthLockPrint> { [jun] = new(1, 5), [jul] = new(2, 13) };
        var stored = new Dictionary<DateOnly, MonthLockPrint> { [jun] = new(1, 5), [jul] = new(2, 9) };
        static void Is((DateOnly Cut, IReadOnlyList<DateOnly> Build) got, DateOnly cut, params DateOnly[] build)
        {
            Assert.Equal(cut, got.Cut);
            Assert.Equal(build, got.Build);
        }

        Is(LedgerMonthBalances.Plan(new DateOnly(2026, 6, 10), sep, new HashSet<DateOnly>(), current, stored), sep, jul, aug); // July stale, August never stored
        Is(LedgerMonthBalances.Plan(new DateOnly(2026, 6, 10), sep, new HashSet<DateOnly> { aug }, current, stored), aug, jul);
        Is(LedgerMonthBalances.Plan(new DateOnly(2026, 6, 10), sep, new HashSet<DateOnly> { jun }, current, stored), jun);
        Is(LedgerMonthBalances.Plan(null, sep, new HashSet<DateOnly>(), current, stored), sep);
        Assert.Equal(jul, LedgerMonthBalances.Limit(new DateOnly(2026, 7, 20), new DateOnly(2026, 10, 24)));
        Assert.Equal(new DateOnly(2026, 10, 1), LedgerMonthBalances.Limit(new DateOnly(2026, 12, 2), new DateOnly(2026, 10, 24)));
        Assert.Null(LedgerMonthBalances.Voucher(new MonthOpening(jul, [], 1)));
        var v = LedgerMonthBalances.Voucher(new MonthOpening(jul, [new("1000", 0, 100), new("5000", 100, 0)], 1))!;
        Assert.Equal((new DateOnly(2026, 6, 30), "opening", LedgerMonthBalances.Sub), (v.Date, v.SourceKind, v.Sub));
        Assert.True(AccountingReports.IsOpening(v));
    }
}
