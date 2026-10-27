using System.Globalization;
using System.Text.Json;
using Hcc.Domain.Compliance;

namespace Hcc.Compliance.Tests.Domain;

/// <summary>
/// SCC tax port (DEC-116), no database: the server computes exactly what SCC's tax module computes. The oracle is
/// <c>03 Prototype/tests/_scc-tax-golden.json</c> — written by tools/scc-tax-catalogue.mjs by running SCC's OWN functions on SCC's OWN catalogue
/// (the prototype test also runs them verbatim, tests/_scc-tax-oracle.mjs).
/// </summary>
public class SccTaxGoldenTests
{
    private static readonly Lazy<JsonElement> Golden = new(() =>
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "03 Prototype", "tests", "_scc-tax-golden.json");
            if (File.Exists(f)) return JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone();
        }

        throw new FileNotFoundException("03 Prototype/tests/_scc-tax-golden.json not found above " + AppContext.BaseDirectory);
    });

    private static JsonElement G => Golden.Value;

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly DateOnly Today = new(2026, 10, 24);

    private static SccCalendar Cal(string? type = null, bool emp = true, bool qrmp = false, IEnumerable<string>? archived = null, IEnumerable<string>? cats = null, IEnumerable<CmpCustom>? custom = null) =>
        new(type is null ? null : new CmpProfile("F", "", "", "proprietorship", type, emp, qrmp, ""), [], custom ?? [], archived ?? [], cats ?? [], []);

    [Fact]
    public void Catalogue_is_SCCs_D_verbatim_113_cards_30_keys()
    {
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SccTaxCatalogue.Json)));
        Assert.Equal(G.GetProperty("sha256").GetString(), sha);
        Assert.Equal(113, SccTaxCatalogue.All().Count());
        Assert.Equal(30, SccTaxCatalogue.BuiltInObligations().Count);
        Assert.Equal(SccTaxCatalogue.Months, SccTaxCatalogue.D.Keys);
    }

    [Fact]
    public void All_113_due_dates_for_FY_2026_27_to_2032_33_equal_SCCs_parseDueDate()
    {
        var cards = SccTaxCatalogue.All().ToList();
        var n = 0;
        for (var fy = 2026; fy <= 2032; fy++)
        {
            var rows = G.GetProperty("fy").GetProperty(fy.ToString(CultureInfo.InvariantCulture)).EnumerateArray().ToList();
            Assert.Equal(113, rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i].EnumerateArray().Select(x => x.GetString()!).ToList();
                Assert.Equal((r[0], r[1], r[2]), (cards[i].Month, cards[i].Card.Id, cards[i].Card.Due));
                Assert.Equal(D(r[3]), SccTaxRules.ParseDue(cards[i].Card.Due, fy));
                n++;
            }
        }

        Assert.Equal(791, n);
        Assert.Equal(D("2026-06-30"), SccTaxRules.ParseDue("30 Jun", 2026));        // GSTR-4 on the April page
        Assert.Equal(D("2028-02-29"), SccTaxRules.ParseDue("Last day of Feb", 2027));
        Assert.Equal(D("2027-02-28"), SccTaxRules.ParseDue("By end of Feb", 2026));
        Assert.Null(SccTaxRules.ParseDue("someday", 2026));
        Assert.Null(SccTaxRules.ParseDue("", 2026));
    }

    [Fact]
    public void Per_card_key_payroll_risk_cash_responsible_reviewer_buffer_payable_field_and_gst_mode_equal_SCC()
    {
        var byId = SccTaxCatalogue.All().ToDictionary(c => c.Card.Id);
        foreach (var row in G.GetProperty("cardRules").EnumerateArray())
        {
            var r = row.EnumerateArray().ToList();
            var c = byId[r[0].GetString()!].Card;
            var p = SccTaxRules.ControlProfile(c, 2026, Today);
            Assert.Equal(r[1].GetString(), SccTaxRules.ObligationKey(c));
            Assert.Equal(r[2].GetInt32() == 1, SccTaxRules.IsPayrollItem(c));
            Assert.Equal((r[3].GetString(), r[4].GetString(), r[5].GetString(), r[6].GetString(), r[7].GetInt32()), (p.Risk, p.CashFlow, p.Responsible, p.Reviewer, p.Buffer));
            Assert.Equal(r[8].ValueKind == JsonValueKind.Null ? null : r[8].GetString(), SccTaxRules.TaxPayableField(c.Name));
            Assert.Equal(r[9].ValueKind == JsonValueKind.Null ? null : r[9].GetString(), SccTaxRules.GstFilingMode(c));
        }
    }

    [Fact]
    public void On_24_Oct_2026_days_left_days_tag_internal_deadline_and_escalation_of_every_card_equal_SCC()
    {
        var byId = SccTaxCatalogue.All().ToDictionary(c => c.Card.Id);
        foreach (var row in G.GetProperty("control").GetProperty("rows").EnumerateArray())
        {
            var r = row.EnumerateArray().ToList();
            var c = byId[r[0].GetString()!].Card;
            var days = SccTaxRules.DaysLeft(c.Due, 2026, Today);
            var p = SccTaxRules.ControlProfile(c, 2026, Today);
            Assert.Equal(r[1].GetInt32(), days);
            Assert.Equal(r[2].GetString(), SccTaxRules.DaysTag(days, false)?.Text);
            Assert.Equal(D(r[3].GetString()!), p.InternalDate);
            Assert.Equal(r[4].GetString(), p.Escalation.Label);
        }

        Assert.Equal("✓ Filed", SccTaxRules.DaysTag(3, true)!.Text);
        Assert.Equal(["urgent-days", "soon", "soon", "ok"], new[] { 7, 8, 15, 16 }.Select(d => SccTaxRules.DaysTag(d, false)!.Cls));
        Assert.Null(SccTaxRules.DaysTag(null, false));
    }

    [Fact]
    public void Applicability_gst_type_times_employees_and_no_firm_show_exactly_SCCs_cards()
    {
        var all = SccTaxCatalogue.All().Select(c => c.Card).ToList();
        Assert.Equal(G.GetProperty("visible").GetProperty("none").GetString(), string.Join(' ', Cal().Visible(all).Select(c => c.Id)));
        foreach (var type in SccTaxRules.GstTypes)
            foreach (var emp in new[] { true, false })
                foreach (var qrmp in new[] { true, false })
                    Assert.Equal(G.GetProperty("visible").GetProperty($"{type}|{(emp ? "emp" : "noemp")}").GetString(),
                        string.Join(' ', Cal(type, emp, qrmp && type is "regular" or "both").Visible(all).Select(c => c.Id)));
    }

    [Fact]
    public void Qrmp_advisory_texts_equal_SCC()
    {
        foreach (var type in new[] { "regular", "both" })
            foreach (var qrmp in new[] { true, false })
            {
                var expected = G.GetProperty("qrmp").GetProperty($"{type}|{(qrmp ? "qrmp" : "monthly")}").EnumerateArray().Select(r => (r[0].GetString()!, r[1].GetString())).ToList();
                var got = SccTaxCatalogue.All().Select(c => (c.Card.Id, SccTaxRules.QrmpAdvisory(c.Card, type, qrmp, true)?.Text)).Where(x => x.Text is not null).ToList();
                Assert.Equal(expected, got);
            }

        Assert.Null(SccTaxRules.QrmpAdvisory(SccTaxCatalogue.BuiltIn("April")[2], "regular", true, false));
    }

    [Fact]
    public void FY_text_rebasing_and_Income_tax_Act_2025_renames_of_every_text_equal_SCC()
    {
        foreach (var fy in new[] { 2026, 2027, 2028 })
        {
            var t = G.GetProperty("text").GetProperty(fy.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(t.GetProperty("sha256").GetString(), SccTaxRules.RenderedTextHash(fy));
            Assert.Equal(t.GetProperty("aprilNote").GetString(), SccTaxRules.RenderFyText(SccTaxCatalogue.Note("April"), fy));
            Assert.Equal(t.GetProperty("q4").GetString(), SccTaxRules.RenderFyText("January – March 2025", fy));
            foreach (var id in new[] { "ap4", "my2", "jn3", "dc5" })
                Assert.Equal(t.GetProperty(id).GetString(), SccTaxRules.RenderFyText(SccTaxCatalogue.All().Single(c => c.Card.Id == id).Card.Name, fy));
            Assert.Equal(t.GetProperty("ap1Penalty").GetString(), SccTaxRules.RenderFyText(SccTaxCatalogue.BuiltIn("April")[0].Penalty, fy));
        }

        Assert.Equal("TDS Return – Q4 (January–March) | Form 138 & 26Q", SccTaxRules.RenderFyText("TDS Return – Q4 (January–March) | Form 24Q & 26Q", 2026));
        Assert.Equal("Section 392 / u/s 392 / Sec. 392", SccTaxRules.RenderNewActForms("Section 200 / u/s 192 / Sec. 192"));
        Assert.Equal("393, 393, u/s 424, u/s 425, u/s 427", SccTaxRules.RenderNewActForms("194I, 194J, u/s 234B, u/s 234C, u/s 234E"));
    }

    [Fact]
    public void Custom_obligations_expand_as_SCC_due_day_0_last_day_31_April_rolls_to_1_May_never_urgent()
    {
        var c = new CmpCustom(1, "lab", "PF inspection", "Monthly", 31, ["April", "May", "February"], "Portal", "", "", "", "", "", "", null, null, [], [], []);
        var b = new CmpCustom(2, "it", "Board minutes", "Annual", 0, ["February"], "", "", "", "", "", "CA", "Owner", "Low", 12, ["a"], [], ["b"]);
        var cal = Cal("regular", custom: [c, b]);
        Assert.Equal(["cust-1-Apr"], cal.ExpandCustom("April").Select(x => x.Id));
        Assert.Equal(D("2026-05-01"), SccTaxRules.ParseDue(cal.ExpandCustom("April")[0].Due, 2026));
        Assert.Equal(D("2027-03-03"), SccTaxRules.ParseDue(cal.ExpandCustom("February")[0].Due, 2026));
        var feb = cal.ExpandCustom("February")[1];
        Assert.Equal(("Last day of Feb", false, "cust-2"), (feb.Due, feb.Urgent, SccTaxRules.ObligationKey(feb)));
        Assert.Equal(D("2028-02-29"), SccTaxRules.ParseDue(feb.Due, 2027));
        var p = SccTaxRules.ControlProfile(feb, 2027, Today);
        Assert.Equal(("Low", "CA", "Owner", 12, D("2028-02-17")), (p.Risk, p.Responsible, p.Reviewer, p.Buffer, p.InternalDate!.Value));
        Assert.Equal(["a"], p.Checklist);
        Assert.Equal(["b"], p.Evidence);
    }

    [Fact]
    public void Archive_keys_as_SCC_EPF_Contribution_leaves_Aprils_card_LWF_has_two_keys_category_hides_custom_too()
    {
        var all = SccTaxCatalogue.All().Select(c => c.Card).ToList();
        var vis = Cal("both", archived: ["EPF Contribution", "Labour Welfare Fund (LWF)"]).Visible(all).Select(c => c.Id).ToList();
        Assert.Contains("ap5", vis);
        Assert.DoesNotContain("my5", vis);
        Assert.DoesNotContain("jl11", vis);
        Assert.Contains("jn_10", vis);
        var keys = SccTaxCatalogue.BuiltInObligations().Select(o => o.Key).ToList();
        foreach (var k in new[] { "EPF Contribution Deposit", "EPF Contribution", "ESIC Contribution Deposit", "ESIC Contribution", "Labour Welfare Fund (LWF)", "Labour Welfare Fund" })
            Assert.Contains(k, keys);
        var custom = new CmpCustom(9, "lab", "X", "Monthly", 5, SccTaxCatalogue.Months, "", "", "", "", "", "", "", null, null, [], [], []);
        var cal = Cal("regular", cats: ["lab"], custom: [custom]);
        Assert.All(SccTaxCatalogue.Months, m => Assert.DoesNotContain(cal.MonthCards(m), x => x.Cat == "lab"));
    }

    [Fact]
    public void Risk_classes_by_name_substring_as_SCC()
    {
        string Risk(string id) => SccTaxRules.ControlProfile(SccTaxCatalogue.All().Single(c => c.Card.Id == id).Card, 2026, Today).Risk;
        Assert.All(new[] { "jl3", "oc3", "mr4", "ap4", "sp3" }, id => Assert.Equal("Low", Risk(id)));
        Assert.All(new[] { "fb3", "mr3" }, id => Assert.Equal("High", Risk(id)));
        Assert.All(new[] { "ap2", "dc5", "dc6" }, id => Assert.Equal("Medium", Risk(id)));
    }

    [Fact]
    public void Csv_cell_formula_guard_and_file_names_as_SCC()
    {
        Assert.Equal("'=1+1", SccTaxRules.CsvCell("=1+1"));
        Assert.Equal("' -3", SccTaxRules.CsvCell(" -3"));
        Assert.Equal("'-177", SccTaxRules.CsvCell("-177"));
        Assert.Equal("'@x", SccTaxRules.CsvCell("@x"));
        Assert.Equal("\"a,b\"", SccTaxRules.CsvCell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", SccTaxRules.CsvCell("say \"hi\""));
        Assert.Equal("Compliance_HCC_Demo_Residency_FY_2026_27", SccTaxRules.Sanitize("Compliance_HCC Demo Residency_FY 2026–27"));
        Assert.Equal("a,b", SccTaxRules.ZipCell("a,b").Trim('"'));
    }

    [Fact]
    public void Payable_reads_the_month_before_the_page_and_the_panel_the_current_month_else_the_newest()
    {
        var feed = new Dictionary<string, CmpPayableMonth>
        {
            ["2026-08"] = new("2026-08", 2432605, 518100, 180000, 0, []),
            ["2026-09"] = new("2026-09", 0, 0, 0, 9123460, []),
        };
        Assert.Equal(24326, SccCalendar.PayableAmount(feed, "EPF Contribution – August Wages", "September", 2026));
        Assert.Equal(91235, SccCalendar.PayableAmount(feed, "GSTR-3B – September (Monthly Filers)", "October", 2026));
        Assert.Null(SccCalendar.PayableAmount(feed, "GSTR-3B – Q2 (Jul–Sep) | QRMP Quarterly Filers", "October", 2026));
        Assert.Null(SccCalendar.PayableAmount(feed, "EPF Contribution – September Wages", "October", 2026));
        Assert.Equal("2026-12", SccTaxRules.PayableYm("January", 2026));
        var sp = SccCalendar.StatutoryPayable(feed, Today)!.Value;
        Assert.Equal(("2026-09", false), (sp.Rec.Ym, sp.IsCurrent));
        Assert.True(SccCalendar.StatutoryPayable(feed, new DateOnly(2026, 9, 5))!.Value.IsCurrent);
    }

    [Fact]
    public void The_W05C_calendar_maps_onto_SCC_cards()
    {
        Assert.Equal((2026, "ap1"), ComplianceRules.LegacyCard("TDS", "2026-03"));
        Assert.Equal((2026, "oc1"), ComplianceRules.LegacyCard("TDS", "2026-09"));
        Assert.Equal((2026, "jn_1"), ComplianceRules.LegacyCard("TDS", "2026-12"));
        Assert.Equal((2026, "mr1"), ComplianceRules.LegacyCard("TDS", "2027-02"));
        Assert.Equal((2026, "dc5"), ComplianceRules.LegacyCard("GSTR-9", "2025-26"));
        Assert.Equal(["ap7", "my8", "jn10", "jl12", "ag7", "sp8", "oc11", "nv8", "dc9", "jn_12", "fb8", "mr10"],
            new[] { "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12", "2027-01", "2027-02" }
                .Select(p => ComplianceRules.LegacyCard("PT", p)!.Value.ItemId));
        Assert.Null(ComplianceRules.LegacyCard("XYZ", "2026-04"));
        foreach (var (kind, cards) in ComplianceRules.LegacyCards)
            foreach (var id in cards)
                Assert.NotNull(SccTaxCatalogue.BuiltInMonth(id));
    }
}
