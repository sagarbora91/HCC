using Hcc.Application.Billing;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Import.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Import.Tests.Sql;

/// <summary>
/// N5 golden: the imported opening trial balance (samples/opening-balances.csv, ₹16,20,000 each side, as of 25 Oct 2026) appears in
/// Accounting's cumulative trial balance through the Import read port (IOpeningBalanceSource), never in a day movement, and not before
/// its own date. Read as the Accountant (PERM-ACC-001 only; no Import permission needed).
/// </summary>
[Trait("Category", "Sql")]
public class OpeningTrialBalanceTests : IAsyncLifetime
{
    private ImportWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private AccountingQueries Queries() => new(new SqlAccountingStore(_w.Platform.Connections), _w.Policy, _w.Service);

    private Task<TrialBalanceView> TbAsync(DateOnly date, bool cumulative) =>
        Queries().TrialBalanceAsync(new Caller(_w.PropertyId, _w.Users["accountant"]), date, cumulative, Ct);

    [Fact]
    public async Task Imported_opening_balances_are_in_the_cumulative_trial_balance_and_it_balances_at_16_20_000()
    {
        await _w.ImportSampleAsync("ledgers");
        await _w.ImportSampleAsync("opening-balances", "2026-10-25");

        var tb = await TbAsync(new DateOnly(2026, 10, 26), cumulative: true);
        Assert.True(tb.OpeningIncluded);
        Assert.Equal(new DateOnly(2026, 10, 25), tb.OpeningAsOf);
        Assert.Equal(162000000, tb.DebitPaise);
        Assert.Equal(162000000, tb.CreditPaise);
        Assert.True(tb.Balanced);
        // chart accounts first (chart order), then the imported ledgers without an HCC account (code order)
        Assert.Equal(["1000", "1030", "1110", "2200", "CAP01", "FURN01", "LOAN01"], tb.Rows.Select(r => r.Account));
        var cash = tb.Rows.Single(r => r.Account == "1000");
        Assert.Equal(("Cash in hand", "Asset", 2500000L, 0L, 2500000L), (cash.Name, cash.Group, cash.DebitPaise, cash.CreditPaise, cash.BalancePaise));
        var bank = tb.Rows.Single(r => r.Account == "1030");   // SBI01 is mapped to the HCC bank account
        Assert.Equal(("Bank account", 35000000L), (bank.Name, bank.DebitPaise));
        var cap = tb.Rows.Single(r => r.Account == "CAP01");
        Assert.Equal(("Owner's capital", "Equity", -105800000L), (cap.Name, cap.Group, cap.BalancePaise));
        var loan = tb.Rows.Single(r => r.Account == "LOAN01");
        Assert.Equal(("Term loan (fictional bank)", "Liability", 50000000L), (loan.Name, loan.Group, loan.CreditPaise));
        Assert.Equal(120000000, tb.Rows.Single(r => r.Account == "FURN01").DebitPaise);
        Assert.Equal(6200000, tb.Rows.Single(r => r.Account == "2200").CreditPaise);
        Assert.Equal(tb.Rows, tb.Opening);   // no vouchers yet: the balance is the opening alone

        // the day movement never carries the opening
        var day = await TbAsync(new DateOnly(2026, 10, 26), cumulative: false);
        Assert.Empty(day.Rows);
        Assert.False(day.OpeningIncluded);
        Assert.Equal(0, day.DebitPaise);

        // on the opening date it is included; before it, only listed (for a client that folds earlier balances)
        Assert.True((await TbAsync(new DateOnly(2026, 10, 25), cumulative: true)).OpeningIncluded);
        var before = await TbAsync(new DateOnly(2026, 10, 24), cumulative: true);
        Assert.False(before.OpeningIncluded);
        Assert.Empty(before.Rows);
        Assert.Equal(7, before.Opening!.Count);
        Assert.Equal(162000000, before.Opening.Sum(r => r.CreditPaise));
    }

    [Fact]
    public async Task Without_imported_opening_balances_the_cumulative_trial_balance_is_unchanged()
    {
        var tb = await TbAsync(new DateOnly(2026, 10, 26), cumulative: true);
        Assert.Empty(tb.Rows);
        Assert.False(tb.OpeningIncluded);
        Assert.Null(tb.OpeningAsOf);
        Assert.Empty(tb.Opening!);
        Assert.True(tb.Balanced);
    }
}
