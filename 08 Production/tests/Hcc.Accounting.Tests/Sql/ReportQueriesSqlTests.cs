using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Import;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 acc1-statements over SQL (contract §6, PERM-ACC-001): statement, six-column trial balance and day book read the projected
/// vouchers (a locked date as its snapshot), the imported opening (N5) or — superseding it — the approved opening entry, with the real
/// P1 policy (Cashier and Housekeeping refused, DEC-079 / DEC-061).
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReportQueriesSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly A = new(2026, 8, 3);
    private static readonly DateOnly B = new(2026, 8, 7);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Imported(OpeningBalancesView view) : IOpeningBalanceSource
    {
        public Task<OpeningBalancesView> GetAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(view);
    }

    private sealed class Entry(ReportOpening? entry) : IReportOpeningSource
    {
        public Task<ReportOpening?> GetAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(entry);
    }

    private AccountingReportQueries Queries(IOpeningBalanceSource? imported = null, IReportOpeningSource? entry = null) =>
        new(db.Store, new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]), imported, entry);

    [Fact]
    public async Task Statement_trial_balance_and_day_book_read_the_vouchers_with_the_opening_before_from_and_a_locked_date_as_its_snapshot()
    {
        await db.PublishAsync(AccountingEvents.ExpensePosted, 8101, new ExpensePostedEvent(8101, A, "Supplies", "Cash", 30_000, "Demo Stores (fictional)", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 8102, new ExpensePostedEvent(8102, B, "Laundry", "Cash", 12_500, "Demo Linen (fictional)", null));
        await db.ProjectAsync();
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(A), Ct);
        var q = Queries();

        var st = await q.StatementAsync(db.As("ACC"), "1000", new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 20), Ct);
        Assert.Equal(-30_000, st.OpeningPaise); // the locked 3 Aug, before "from"
        var row = Assert.Single(st.Rows);
        Assert.Equal((B, 0L, 12_500L, -42_500L, ChartOfAccounts.NameOf("5010")), (row.Date, row.DebitPaise, row.CreditPaise, row.RunningPaise, row.Counter));
        Assert.Equal((-42_500L, ChartOfAccounts.NameOf("1000")), (st.ClosingPaise, st.Name));

        var tb = await q.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 20), false, Ct);
        Assert.True(tb.Balanced);
        Assert.True(tb.OpeningPending); // no opening entry composed
        var r5000 = Assert.Single(tb.Rows, r => r.Account == "5000");
        Assert.Equal((30_000L, 0L, 0L, 30_000L), (r5000.OpeningDebitPaise, r5000.DebitPaise, r5000.CreditPaise, r5000.ClosingDebitPaise));
        var r1000 = Assert.Single(tb.Rows, r => r.Account == "1000");
        Assert.Equal((30_000L, 12_500L, 42_500L), (r1000.OpeningCreditPaise, r1000.CreditPaise, r1000.ClosingCreditPaise));
        Assert.Equal(tb.Rows.Count, (await q.TrialBalanceAsync(db.As("MGR"), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 20), true, Ct)).Rows.Count); // no parties yet

        var book = await q.DayBookAsync(db.As("ACC"), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), Ct);
        Assert.Equal(2, book.Vouchers);
        Assert.Equal(4, book.Rows.Count);
        Assert.Equal([A, A, B, B], book.Rows.Select(r => r.Date));
        Assert.True(book.Balanced);
        Assert.Equal(42_500, book.DebitPaise);
        Assert.Equal("expense:8101", book.Rows[0].Source);

        await Fails(HccErrorCode.FORBIDDEN, () => q.StatementAsync(db.As("CSH"), "1000", A, B, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => q.DayBookAsync(db.As("HKP"), A, B, Ct));
        await Fails(HccErrorCode.VALIDATION, () => q.TrialBalanceAsync(db.As("ACC"), B, A, false, Ct));
        Assert.True((await q.DayBookAsync(db.As("ACC"), B.AddDays(-400), B, Ct)).Balanced); // ACC-2 parity (contract §8): any range
        await Fails(HccErrorCode.VALIDATION, () => q.StatementAsync(db.As("ACC"), " ", A, B, Ct));
    }

    [Fact]
    public async Task The_imported_opening_is_the_opening_until_an_approved_opening_entry_supersedes_it()
    {
        var imported = new Imported(new OpeningBalancesView(Apr1, 1, 900_000, 900_000, true,
        [
            new OpeningBalanceRowView("CASH", "Cash", "Asset", "1000", 900_000, 0),
            new OpeningBalanceRowView("CAP", "Proprietor's capital (fictional)", "Equity", null, 0, 900_000),
        ]));
        var tb = await Queries(imported).TrialBalanceAsync(db.As("ACC"), Apr1, new DateOnly(2026, 4, 30), false, Ct);
        Assert.True(tb.Balanced);
        Assert.True(tb.OpeningPending);
        var cap = Assert.Single(tb.Rows, r => r.Account == "CAP");
        Assert.Equal(("Proprietor's capital (fictional)", "Equity", 900_000L), (cap.Name, cap.Group, cap.OpeningCreditPaise));

        var entry = new Voucher("JRN/20260401/000", Apr1, VoucherType.Journal, "Opening balances", "opening", "opening", "opening-entry",
            [new VoucherEntry("1000", 500_000, 0), new VoucherEntry("3100", 0, 500_000)], []);
        var q = Queries(imported, new Entry(new ReportOpening(entry, true)));
        var tb2 = await q.TrialBalanceAsync(db.As("ACC"), Apr1, new DateOnly(2026, 4, 30), false, Ct);
        Assert.False(tb2.OpeningPending);
        Assert.DoesNotContain(tb2.Rows, r => r.Account == "CAP");
        Assert.Equal(500_000, Assert.Single(tb2.Rows, r => r.Account == "3100").OpeningCreditPaise);
        Assert.Equal(-500_000, (await q.StatementAsync(db.As("ACC"), "3100", Apr1, new DateOnly(2026, 4, 30), Ct)).OpeningPaise);
        Assert.Empty((await q.DayBookAsync(db.As("ACC"), Apr1, new DateOnly(2026, 4, 30), Ct)).Rows);
        Assert.Single((await q.DayBookAsync(db.As("ACC"), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), Ct)).Rows, r => r.Account == "3100");
        Assert.True((await Queries(imported, new Entry(new ReportOpening(entry, false))).TrialBalanceAsync(db.As("ACC"), Apr1, Apr1, false, Ct)).OpeningPending);
    }
}
