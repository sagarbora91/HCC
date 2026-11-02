// ACC-4 · F5-A1 · carry-over C6 (ACC4-CONTRACT §3): the TDS held back on a bill reads the rate in force on the VOUCHER date (dated Owner
// overrides are live, not display-only). Prototype twin: tdsForBill → tdsRateBps(s, code, bill.date, …) (stream-acc4-year.mjs has none; see
// F5-A1-DONE.md for the other carry-overs).
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Carry;

public sealed class Acc4CarryTests
{
    [Fact]
    public void C6_tds_rate_follows_the_voucher_date()
    {
        var overrides = new[] { new TdsRateVersion("tds.contractor.bpsOther", new DateOnly(2026, 10, 1), 300) };
        var before = TdsRates.RateBps("contractor", new DateOnly(2026, 9, 30), TdsColumn.Other, overrides);
        var after = TdsRates.RateBps("contractor", new DateOnly(2026, 10, 1), TdsColumn.Other, overrides);
        Assert.NotEqual(300, before);
        Assert.Equal(300, after);
        var vendor = new TdsVendor("Vendor (fictional)", true, "contractor");
        var sep = TdsCalculator.ForBill(new TdsBill(10_000_000, 0, new DateOnly(2026, 9, 30)), vendor, [], TdsSettings.Default, overrides);
        var oct = TdsCalculator.ForBill(new TdsBill(10_000_000, 0, new DateOnly(2026, 10, 1)), vendor, [], TdsSettings.Default, overrides);
        Assert.Equal(before, sep.Bps);
        Assert.Equal(300, oct.Bps);
        Assert.True(oct.Paise > sep.Paise);
    }
}
