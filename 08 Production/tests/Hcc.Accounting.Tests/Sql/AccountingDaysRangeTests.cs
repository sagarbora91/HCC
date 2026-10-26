using Hcc.Accounting.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// W4-7 <c>GET /accounting/days?from&amp;to</c>: the production accounting screens read a window of evaluated days in one request.
/// Only dates with vouchers or a lock are returned, plus <c>to</c>; each day equals <see cref="Hcc.Application.Accounting.AccountingQueries.DayAsync"/>.
/// </summary>
[Trait("Category", "Sql")]
public sealed class AccountingDaysRangeTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly A = new(2026, 8, 3);
    private static readonly DateOnly B = new(2026, 8, 7);
    private static readonly DateOnly To = new(2026, 8, 20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Range_returns_active_dates_and_the_end_date_each_as_the_single_day_read()
    {
        await db.PublishAsync(AccountingEvents.ExpensePosted, 7001, new ExpensePostedEvent(7001, A, "Supplies", "Cash", 30_000, "Demo Stores", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 7002, new ExpensePostedEvent(7002, B, "Laundry", "UPI", 12_500, "Demo Linen", null));
        await db.ProjectAsync();

        var days = await db.Queries.DaysAsync(db.As("ACC"), new DateOnly(2026, 8, 1), To, Ct);
        Assert.Equal([A, B, To], days.Select(d => d.Date));
        var single = await db.Queries.DayAsync(db.As("ACC"), B, Ct);
        var b = days[1];
        Assert.Equal(single.Status, b.Status);
        Assert.Equal(single.Vouchers.Select(v => (v.Number, v.SourceKey, v.DebitPaise)), b.Vouchers.Select(v => (v.Number, v.SourceKey, v.DebitPaise)));
        Assert.Equal(12_500, b.DebitPaise);
        Assert.Equal("Closed", b.Status); // before the business date (24 Oct)
        Assert.Empty(days[2].Vouchers);

        await Fails(HccErrorCode.VALIDATION, () => db.Queries.DaysAsync(db.As("ACC"), To, A, Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Queries.DaysAsync(db.As("ACC"), To.AddDays(-62), To, Ct));
        Assert.Equal(To, (await db.Queries.DaysAsync(db.As("OWN"), To.AddDays(-61), To, Ct))[^1].Date); // 62 days is the limit
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DaysAsync(db.As("HKP"), A, To, Ct));
    }
}
