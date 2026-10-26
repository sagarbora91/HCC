using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.FrontDeskMasters;
using Hcc.Domain.Invoices;

namespace Hcc.FrontDeskMasters.Tests.Kernel;

/// <summary>MA-5 rules without a database: defaults (the prototype fixture values and today's invoice numbers), field rules, numbering lock
/// and start rules, policy fit, display values.</summary>
public class FrontDeskMastersRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static string Field(HccException e) => e.Details!["field"].GetString()!;

    [Fact]
    public void Defaults_are_the_prototype_values_and_reproduce_todays_invoice_numbers()
    {
        Assert.Equal(new[] { "WALKIN", "PHONE", "WHATSAPP", "DIRECT", "WEBSITE", "OTA", "AGENT", "CORPORATE" }, FrontDeskMastersRules.Defaults(FrontDeskMasterIds.BookingSources).Select(d => d.Code));
        var ids = FrontDeskMastersRules.Defaults(FrontDeskMasterIds.IdTypes);
        Assert.Equal(7, ids.Count);
        Assert.Equal((true, true), ((bool)ids[^1].Fields["needsPhoto"]!, (bool)ids[^1].Fields["foreignerOnly"]!));
        var series = FrontDeskMastersRules.Defaults(FrontDeskMasterIds.NumberSeries);
        Assert.Equal(new[] { "INV", "BOS", "CRN", "RCP", "KOT", "REG" }, series.Select(d => d.Code));
        string Number(FrontDeskDefault d, int seq) => FrontDeskMastersRules.Format((string)d.Fields["prefix"]!, (string)d.Fields["reset"]!, (int)d.Fields["padding"]!, new DateOnly(2026, 10, 24), seq);
        Assert.Equal(InvoiceNumbering.Format("INV", "2627", 1), Number(series[0], 1));
        Assert.Equal(InvoiceNumbering.Format("CN", "2627", 12), Number(series[2], 12));
        Assert.Equal("RCP/00007", Number(series[3], 7));
        Assert.Equal("HOTEL1/2627/0042", Number(FrontDeskMastersRules.SeriesDefaults("hotel1")[0], 42));
        Assert.Equal("INV", FrontDeskMastersRules.SeriesDefaults("TOO-LONG-1")[0].Fields["prefix"]);
        var p = Assert.Single(FrontDeskMastersRules.Defaults(FrontDeskMasterIds.Policies));
        Assert.Equal(("DEFAULT", 0, 0, 0), (p.Code, (int)p.Fields["cancelChargeBps"]!, (int)p.Fields["noShowChargeBps"]!, (int)p.Fields["depositBps"]!));
        Assert.True(FrontDeskMastersRules.IsMoneyList(FrontDeskMasterIds.NumberSeries) && FrontDeskMastersRules.IsMoneyList(FrontDeskMasterIds.Policies));
        Assert.False(FrontDeskMastersRules.IsMoneyList(FrontDeskMasterIds.BookingSources) || FrontDeskMastersRules.IsMoneyList(FrontDeskMasterIds.IdTypes));
    }

    [Fact]
    public void Booking_source_fields_are_checked()
    {
        var ok = FrontDeskMastersRules.BookingSource("ota", " BookNest ", 1800);
        Assert.Equal(("OTA", "BookNest", 1800), ((string)ok["segment"]!, (string)ok["channel"]!, (int)ok["commissionBps"]!));
        Assert.Equal("segment", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.BookingSource("NOPE", null, 0))));
        Assert.Equal("channel", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.BookingSource("DIRECT", "BookNest", 0))));
        var e = Assert.Throws<HccException>(() => FrontDeskMastersRules.BookingSource("AGENT", null, 5001));
        Assert.Equal((HccErrorCode.VALIDATION, "MASTER_INVALID", "commissionBps"), (e.Code, Reason(e), Field(e)));
    }

    [Fact]
    public void Number_series_fields_follow_the_GST_rules()
    {
        var f = FrontDeskMastersRules.NumberSeries("inv", "hcc", 101, 5, "fy", "2027-04-01");
        Assert.Equal(("INV", "HCC", "FY", new DateOnly(2027, 4, 1)), ((string)f["document"]!, (string)f["prefix"]!, (string)f["reset"]!, (DateOnly)f["activeFrom"]!));
        Assert.Contains("restart every financial year", Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("INV", "HCC", 1, 5, "NEVER", "2027-04-01")).Message);
        Assert.Equal("prefix", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("CRN", "ABCDEFG", 1, 1, "FY", "2027-04-01"))));
        Assert.Contains("at most 16", Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("BOS", "ABCDEF", 1, 5, "FY", "2027-04-01")).Message);
        Assert.Equal("activeFrom", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("INV", "HCC", 1, 5, "FY", "01-04-2027"))));
        Assert.Equal("startNo", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("RCP", "RCP", 0, 5, "NEVER", "2027-04-01"))));
        Assert.Equal("prefix", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.NumberSeries("RCP", "R C", 1, 5, "NEVER", "2027-04-01"))));
        Assert.Equal("NEVER", FrontDeskMastersRules.NumberSeries("KOT", "KITCHEN1", 1, 10, "never", "2026-11-01")["reset"]);
    }

    [Fact]
    public void A_series_with_documents_keeps_its_numbering_and_a_new_one_starts_on_a_date_without_documents()
    {
        var before = FrontDeskMastersRules.NumberSeries("INV", "INV", 1, 7, "FY", "2000-04-01");
        var renamedOnly = FrontDeskMastersRules.NumberSeries("INV", "INV", 1, 7, "FY", "2000-04-01");
        FrontDeskMastersRules.EnsureSeriesUnchanged("INV", 12, before, renamedOnly);
        FrontDeskMastersRules.EnsureSeriesUnchanged("INV", 0, before, FrontDeskMastersRules.NumberSeries("INV", "HCC", 1, 7, "FY", "2000-04-01"));
        var e = Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsureSeriesUnchanged("INV", 12, before, FrontDeskMastersRules.NumberSeries("INV", "HCC", 1, 7, "FY", "2000-04-01")));
        Assert.Equal(("MASTER_INVALID", "prefix"), (Reason(e), Field(e)));
        Assert.Contains("has issued 12 numbers", e.Message, StringComparison.Ordinal);
        var today = new DateOnly(2026, 10, 24);
        Assert.Contains("today's business date", Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsureStartFree("INV", "FY", today.AddDays(-1), today, null, [])).Message);
        Assert.Contains("after 2026-10-24", Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsureStartFree("INV", "FY", today, today, today, ["2627"])).Message);
        Assert.Contains("can start from 2027-04-01", Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsureStartFree("INV", "FY", today.AddDays(30), today, today, ["2627"])).Message);
        FrontDeskMastersRules.EnsureStartFree("INV", "FY", new DateOnly(2027, 4, 1), today, today, ["2627"]);
        FrontDeskMastersRules.EnsureStartFree("RCP", "NEVER", today, today, null, []);
    }

    [Fact]
    public void Invoice_format_takes_the_series_digits_within_16_characters()
    {
        Assert.Equal("HCC/2627/00101", InvoiceNumbering.Format("HCC", "2627", 101, 5));
        Assert.Equal(InvoiceNumbering.Format("INV", "2627", 9), InvoiceNumbering.Format("INV", "2627", 9, null));
        Assert.Equal("INV/2627/0000009", InvoiceNumbering.Format("INV", "2627", 9, 12));
        Assert.Equal("HCC/2627/123456", InvoiceNumbering.Format("HCC", "2627", 123456, 4));
        Assert.Equal("REG/2627/0001", FrontDeskMastersRules.Format("REG", "FY", 4, new DateOnly(2027, 3, 31), 1));
        Assert.Equal("REG/2728/0001", FrontDeskMastersRules.Format("REG", "FY", 4, new DateOnly(2027, 4, 1), 1));
    }

    [Fact]
    public void Policies_have_one_default_and_each_source_follows_one_policy()
    {
        var f = FrontDeskMastersRules.Policy(48, 10000, 10000, 2500, ["ota-booknest", "OTA", "OTA"]);
        Assert.Equal(new[] { "OTA-BOOKNEST", "OTA" }, (IReadOnlyList<string>)f["sources"]!);
        Assert.Equal("freeCancelHours", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.Policy(721, 0, 0, 0, []))));
        Assert.Equal("depositBps", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.Policy(0, 0, 0, 10001, []))));
        Assert.Equal("sources", Field(Assert.Throws<HccException>(() => FrontDeskMastersRules.Policy(0, 0, 0, 0, ["no spaces"]))));
        (string, string, bool, IReadOnlyList<string>)[] others = [("DEFAULT", "Default", true, []), ("OTA-STD", "OTA standard", true, ["OTA"]), ("OLD", "Old", false, ["PHONE"])];
        Assert.Contains("already the default policy", Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsurePolicyFits("P2", [], others)).Message);
        Assert.Contains("OTA already follows OTA standard", Assert.Throws<HccException>(() => FrontDeskMastersRules.EnsurePolicyFits("P2", ["OTA"], others)).Message);
        FrontDeskMastersRules.EnsurePolicyFits("P2", ["PHONE"], others);
        FrontDeskMastersRules.EnsurePolicyFits("DEFAULT", [], others);
    }

    [Fact]
    public void History_values_read_as_people_read_them()
    {
        Assert.Equal("Yes", FrontDeskMastersRules.Show("accepted", true));
        Assert.Equal("18 %", FrontDeskMastersRules.Show("commissionBps", 1800));
        Assert.Equal("12.5 %", FrontDeskMastersRules.Show("depositBps", 1250));
        Assert.Equal("48", FrontDeskMastersRules.Show("freeCancelHours", 48));
        Assert.Equal("2027-04-01", FrontDeskMastersRules.Show("activeFrom", new DateOnly(2027, 4, 1)));
        Assert.Equal("OTA, PHONE", FrontDeskMastersRules.Show("sources", new List<string> { "OTA", "PHONE" }));
        Assert.Null(FrontDeskMastersRules.Show("channel", null));
    }
}
