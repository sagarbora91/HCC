using System.Text.RegularExpressions;
using Hcc.Domain.Compliance;

namespace Hcc.Compliance.Tests.Domain;

/// <summary>
/// SCC decisions wave (27 Oct 2026), no database: DEC-125 (the compliance FY follows the business date on 1 April; a pick holds until the next
/// 1 April) and DEC-133 (FSSAI and lodging licence renewal cards from the firm profile's expiry dates). Prototype twin: tests/stream-compliance-dec.mjs.
/// </summary>
public partial class SccDecisionsTests
{
    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void EffectiveFy_follows_the_business_date_on_1_April_and_a_pick_holds_until_the_next_1_April()
    {
        // Picked 2026–27 in Oct 2026 → stays; 1 Apr 2027 → 2027–28.
        Assert.Equal(2026, SccTaxRules.EffectiveFy(2026, 2026, D("2026-10-24")));
        Assert.Equal(2026, SccTaxRules.EffectiveFy(2026, 2026, D("2027-03-31")));
        Assert.Equal(2027, SccTaxRules.EffectiveFy(2026, 2026, D("2027-04-01")));
        // Picked 2026–27 on 2 Apr 2027 (chosen in FY 2027) → holds until 31 Mar 2028, then 2028–29.
        var chosen = SccTaxRules.FyOfDate(D("2027-04-02"));
        Assert.Equal(2027, chosen);
        foreach (var d in new[] { "2027-04-02", "2027-12-31", "2028-03-31" }) Assert.Equal(2026, SccTaxRules.EffectiveFy(2026, chosen, D(d)));
        Assert.Equal(2028, SccTaxRules.EffectiveFy(2026, chosen, D("2028-04-01")));
        // No pick: the business date's FY, clamped to SCC's list 2026–27 … 2032–33.
        foreach (var (d, fy) in new[] { ("2025-06-01", 2026), ("2026-03-31", 2026), ("2026-04-01", 2026), ("2029-04-01", 2029), ("2033-03-31", 2032), ("2033-04-01", 2032), ("2040-01-01", 2032) })
            Assert.Equal(fy, SccTaxRules.EffectiveFy(2029, null, D(d)));
        // A pick before April 2026 holds only until 31 Mar 2026; a pick after the list until the next 1 April too; a stored pick is clamped.
        Assert.Equal((2028, 2026), (SccTaxRules.EffectiveFy(2028, 2025, D("2026-03-31")), SccTaxRules.EffectiveFy(2028, 2025, D("2026-04-01"))));
        Assert.Equal((2030, 2032), (SccTaxRules.EffectiveFy(2030, 2034, D("2035-03-31")), SccTaxRules.EffectiveFy(2030, 2034, D("2035-04-01"))));
        Assert.Equal(2032, SccTaxRules.EffectiveFy(2040, 2026, D("2026-10-24")));
        Assert.Equal((2027, (int?)null), (SccTaxRules.PickInForce(2027, D("2027-06-01")), SccTaxRules.PickInForce(2026, D("2027-06-01"))));
    }

    private static CmpProfile Firm(string? fssai = null, string? lodging = null, bool emp = true) =>
        new("F", "", "", "proprietorship", "regular", emp, false, "", "DEMO-FSSAI-0001", fssai is null ? null : D(fssai), null, lodging is null ? null : D(lodging));

    private static SccCalendar Cal(CmpProfile? p, IEnumerable<string>? archived = null) => new(p, [], [], archived ?? [], [], []);

    [Fact]
    public void Licence_cards_only_while_the_expiry_is_set_on_the_FY_page_of_the_expiry_month_due_on_it_risk_High_SCC_control_layer()
    {
        var cal = Cal(Firm("2027-05-31", "2027-12-31"));
        var f = Assert.Single(cal.MonthCards("May", 2027), c => c.Licence is not null);
        Assert.Equal(("lic_fssai", "FSSAI Licence – Renewal", "31 May", "lab", "fssai"), (f.Id, f.Name, f.Due, f.Cat, f.Licence));
        Assert.Equal("FSSAI licence renewal (FoSCoS portal) · Licence no. DEMO-FSSAI-0001", f.Form);
        Assert.Equal(D("2027-05-31"), SccTaxRules.ParseDue(f.Due, 2027));
        var ctl = SccTaxRules.ControlProfile(f, 2027, D("2027-05-01"));
        Assert.Equal(("High", 5, D("2027-05-26"), "Owner / General Manager", "Owner"), (ctl.Risk, ctl.Buffer, ctl.InternalDate, ctl.Responsible, ctl.Reviewer));
        Assert.Equal("Lodging House Licence – Renewal", Assert.Single(cal.MonthCards("December", 2027), c => c.Licence is not null).Name);
        Assert.Equal(["May:lic_fssai", "December:lic_lodging"],
            SccTaxCatalogue.Months.SelectMany(m => cal.MonthCards(m, 2027).Where(c => c.Licence is not null).Select(c => m + ":" + c.Id)));
        foreach (var fy in new[] { 2026, 2028 }) Assert.DoesNotContain(SccTaxCatalogue.Months.SelectMany(m => cal.MonthCards(m, fy)), c => c.Licence is not null);
        Assert.DoesNotContain(cal.MonthCards("May"), c => c.Licence is not null);
        Assert.Equal(("May", "lic_fssai"), (cal.ItemMonth("lic_fssai"), cal.FindCard("lic_fssai", 2027)!.Id));
        Assert.Null(cal.FindCard("lic_fssai", 2026));
        Assert.False(SccTaxRules.IsPayrollItem(f));
        Assert.Single(Cal(Firm("2027-05-31", emp: false)).MonthCards("May", 2027), c => c.Licence is not null);
        Assert.DoesNotContain(Cal(Firm("2027-05-31"), archived: ["FSSAI Licence"]).MonthCards("May", 2027), c => c.Licence is not null);
        Assert.DoesNotContain(SccTaxCatalogue.Months.SelectMany(m => Cal(Firm()).MonthCards(m, 2027)), c => c.Licence is not null);
        Assert.Null(Cal(Firm()).FindCard("lic_fssai"));
        Assert.Equal([("FSSAI Licence", "lab"), ("Lodging House Licence", "lab")], LicenceCards.Obligations().Select(o => (o.Key, o.Cat)));
        // In FY 2026–27 within 30 days: the Action Center and the CA pack count it.
        var near = Cal(Firm("2026-11-15"));
        Assert.Contains(near.ActionCenter(2026, D("2026-10-24")), r => r.ItemId == "lic_fssai" && r.Days == 22 && r.Month == "November");
        Assert.Contains(near.CsvRows(2026, D("2026-10-24")), r => r[4] == "FSSAI Licence – Renewal" && r[6] == "Labour Law" && r[9] == "High" && r[12] == "22");
    }

    [GeneratedRegex(@"CMP_LICENCE_DEFS:CMPLicenceDef\[\]=(\[.*\]);\s*$", RegexOptions.Singleline)]
    private static partial Regex PrototypeDefs();

    [Fact]
    public void Licence_texts_equal_the_prototype_and_leave_lead_time_and_penalty_amounts_to_the_CA()
    {
        string? text = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && text is null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "03 Prototype", "src", "domain", "streams", "compliance-licences.ts");
            if (File.Exists(f)) text = File.ReadAllText(f);
        }

        Assert.NotNull(text);
        Assert.Equal(LicenceCards.Json, PrototypeDefs().Match(text).Groups[1].Value);
        Assert.Equal(["fssai", "lodging"], LicenceCards.Defs.Select(d => d.Kind));
        Assert.All(LicenceCards.Defs, d =>
        {
            Assert.Contains("CA to confirm", d.Penalty, StringComparison.Ordinal);
            Assert.Contains("CA to confirm", d.What, StringComparison.Ordinal);
            Assert.DoesNotContain("₹", d.Who + d.What + d.Why + d.Penalty, StringComparison.Ordinal);
            Assert.Equal("High", d.Risk);
        });
    }
}
