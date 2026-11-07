using Hcc.Domain.Reporting;

namespace Hcc.Closing.Tests.Unit;

/// <summary>F12 meas-digest: the Owner digest block "Measurement" (on/off, subscription, empty case, own-switch rule). Fictional data.</summary>
public sealed class MeasurementDigestTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static IReadOnlyDictionary<string, IReadOnlyList<MeasRedRow>> Rows() => new Dictionary<string, IReadOnlyList<MeasRedRow>>
    {
        ["meas.daily.high-balance"] = [new("meas.daily.high-balance", "rpt.money.high-balance", "Room 105 owes Rs 7,000")],
        ["meas.daily.regrets"] = [new("meas.daily.regrets", "rpt.guests.regrets-denials", "4 price regrets", "amber"), new("meas.daily.flash", "x", "foreign")],
        ["meas.daily.cashier"] = [],
    };

    [Fact]
    public void Lists_rows_red_first_and_ignores_foreign_keys()
    {
        var b = MeasurementDigest.Compose(D, Rows(), _ => true)!;
        Assert.Equal(3, b.Checked);
        Assert.Equal(["rpt.money.high-balance", "rpt.guests.regrets-denials"], b.Rows.Select(r => r.ReportId));
        Assert.Equal("Measurement exceptions: 2", MeasurementDigest.EmailLines(b)[0]);
        Assert.Equal(["Measurement exceptions 2"], MeasurementDigest.WhatsAppLines(b));
    }

    [Fact]
    public void Off_and_unsubscribed_items_are_absent()
    {
        var b = MeasurementDigest.Compose(D, Rows(), k => k != "meas.daily.high-balance", k => k != "meas.daily.regrets")!;
        Assert.Empty(b.Rows);
        Assert.Equal("  none today", MeasurementDigest.EmailLines(b)[1]);
    }

    [Fact]
    public void Everything_off_gives_no_block()
    {
        Assert.Null(MeasurementDigest.Compose(D, Rows(), _ => false));
        Assert.Empty(MeasurementDigest.EmailLines(null));
        Assert.Empty(MeasurementDigest.WhatsAppLines(null));
    }
}
