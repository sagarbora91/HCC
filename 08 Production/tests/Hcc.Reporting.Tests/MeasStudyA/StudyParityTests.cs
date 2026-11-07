// F12 MEAS · meas-study-a · parity of the server selectors with the prototype on the FY26 fixture (+ a synthetic legacy history). The prototype test
// (03 Prototype/tests/stream-meas-study-a.mjs) writes fy26-parity.json: the AnInput and the expected tables of all nine selectors. Section keys, column keys,
// row kinds and every cell must equal. Also pins the legacy-core arithmetic and the report definitions.
using System.Text.Json;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Analytics;
using Hcc.Reporting.Reports.MeasStudyA;

namespace Hcc.Reporting.Tests.MeasStudyA;

public sealed class StudyParityTests
{
    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MeasStudyA", "fy26-parity.json")));

    private static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static AnInput ReadInput(JsonElement i)
    {
        var rooms = i.GetProperty("rooms").EnumerateArray().Select(r => new AnRoom(S(r, "id")!, S(r, "type")!, S(r, "inServiceFrom")!, S(r, "inServiceTo"), r.TryGetProperty("suite", out var su) && su.GetBoolean())).ToList();
        var stays = i.GetProperty("stays").EnumerateArray().Select(s => new AnStay(S(s, "id")!, S(s, "guestKey")!, S(s, "room")!, S(s, "from")!, S(s, "to")!, s.GetProperty("nights").GetInt32(), s.GetProperty("ratePaise").GetInt64(),
            S(s, "source")!, S(s, "channel"), S(s, "company"), S(s, "purpose"), S(s, "status")!, s.TryGetProperty("complimentary", out var c) && c.GetBoolean(), s.GetProperty("legacy").GetBoolean())).ToList();
        var bills = i.GetProperty("bills").EnumerateArray().Select(b => new AnBill(S(b, "id")!, S(b, "stay")!, S(b, "date")!, b.GetProperty("roomChargePaise").GetInt64(), b.GetProperty("fnbPaise").GetInt64(),
            b.GetProperty("otherPaise").GetInt64(), b.GetProperty("discountPaise").GetInt64(), b.GetProperty("gstPaise").GetInt64(), b.GetProperty("totalPaise").GetInt64(), b.GetProperty("chargedNights").GetInt32(),
            b.GetProperty("paidPaise").GetInt64(), b.GetProperty("legacy").GetBoolean())).ToList();
        var receipts = i.GetProperty("receipts").EnumerateArray().Select(r => new AnReceipt(S(r, "id")!, S(r, "bill")!, S(r, "date")!, S(r, "method")!, r.GetProperty("amountPaise").GetInt64(), r.GetProperty("legacy").GetBoolean())).ToList();
        var cv = i.GetProperty("covidWindow");
        return new AnInput(rooms, stays, bills, receipts, S(i, "asOf")!, S(i, "legacyUntil")!, S(cv, "from")!, S(cv, "to")!);
    }

    private static readonly Dictionary<string, Func<AnInput, AnParams, ReportResult>> Selectors = new()
    {
        ["rpt.study.revenue-monthly"] = LegacyRevenue.RevenueMonthly, ["rpt.study.year-comparison"] = LegacyRevenue.YearComparison, ["rpt.study.index-vs-base"] = LegacyRevenue.IndexVsBase,
        ["rpt.study.fy-deep-dive"] = LegacyRevenue.FyDeepDive, ["rpt.study.occupancy-history"] = LegacyOccupancy.OccupancyHistory, ["rpt.study.operations-patterns"] = LegacyOccupancy.OperationsPatterns,
        ["rpt.study.rooms-and-rates"] = LegacyOccupancy.RoomsAndRates, ["rpt.study.zero-rate-complimentary"] = LegacyZero.ZeroRateComplimentary, ["rpt.study.receivables-bad-debt"] = LegacyZero.ReceivablesBadDebt,
    };

    [Fact]
    public void All_nine_selectors_equal_the_prototype_tables_on_the_FY26_fixture()
    {
        using var doc = Load();
        var input = ReadInput(doc.RootElement.GetProperty("input"));
        Assert.Equal(9, Selectors.Count);
        foreach (var (id, fn) in Selectors)
        {
            var p = id == "rpt.study.index-vs-base" ? new AnParams(Fy: "2026-27", BaseFy: "2018-19") : new AnParams(Fy: "2026-27");
            var got = fn(input, p).Sections!;
            var want = doc.RootElement.GetProperty("expected").GetProperty(id).EnumerateArray().ToList();
            Assert.Equal(want.Select(w => S(w, "key")), got.Select(g => g.Key));
            for (var k = 0; k < want.Count; k++)
            {
                var w = want[k]; var g = got[k];
                Assert.Equal(w.GetProperty("columns").EnumerateArray().Select(c => c.GetString()), g.Columns.Select(c => c.Key));
                var wr = w.GetProperty("rows").EnumerateArray().ToList();
                Assert.True(wr.Count == g.Rows.Count, $"{id}/{g.Key}: rows {wr.Count} vs {g.Rows.Count}");
                for (var r = 0; r < wr.Count; r++)
                {
                    Assert.Equal(S(wr[r], "kind"), g.Rows[r].Kind.ToString().ToLowerInvariant());
                    var cells = wr[r].GetProperty("cells");
                    foreach (var col in g.Columns)
                    {
                        var has = cells.TryGetProperty(col.Key, out var wv);
                        g.Rows[r].Cells.TryGetValue(col.Key, out var gv);
                        var where = $"{id}/{g.Key} row {r} col {col.Key}";
                        if (!has || wv.ValueKind == JsonValueKind.Null) { Assert.True(gv is null || (gv is string s0 && s0.Length == 0), where); continue; }
                        if (wv.ValueKind == JsonValueKind.String) Assert.Equal(wv.GetString(), (string?)gv);
                        else Assert.True(Math.Abs(wv.GetDouble() - Convert.ToDouble(gv)) < 1e-9, $"{where}: {wv.GetDouble()} vs {gv}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Integer_arithmetic_matches_the_prototype()
    {
        Assert.Equal(33.3, LegacyCore.Pct1(1, 3)); Assert.Equal(50.0, LegacyCore.Pct1(1, 2)); Assert.Equal(0.0, LegacyCore.Pct1(5, 0));
        Assert.Equal(3L, LegacyCore.RDiv(5, 2)); Assert.Equal(0L, LegacyCore.RDiv(5, 0)); Assert.Equal(-2L, LegacyCore.RDiv(-5, 2));
        Assert.Equal("2026-27", LegacyCore.FyOf("2026-04-01")); Assert.Equal("2025-26", LegacyCore.FyOf("2026-03-31")); Assert.Equal("2018-19", LegacyCore.FyPrev("2019-20"));
        Assert.Equal(5, LegacyCore.Dow("2026-10-23")); Assert.Equal(6, LegacyCore.Dow("2026-10-24"));
    }

    [Fact]
    public void Covid_window_boundary_and_ageing_buckets()
    {
        var i = new AnInput([], [], [], [], "2026-10-24", "2026-10-30", "2020-03-01", "2021-03-31");
        Assert.False(LegacyCore.InCovid(i, "2020-02")); Assert.True(LegacyCore.InCovid(i, "2020-03")); Assert.True(LegacyCore.InCovid(i, "2021-03")); Assert.False(LegacyCore.InCovid(i, "2021-04"));
        Assert.Equal("0-30", LegacyOccupancy.Bucket(30)); Assert.Equal("31-90", LegacyOccupancy.Bucket(31)); Assert.Equal("366-730", LegacyOccupancy.Bucket(730)); Assert.Equal("731+", LegacyOccupancy.Bucket(731));
    }

    [Fact]
    public void Receivables_legacy_dues_are_bad_debt_and_the_cutover_adds_up()
    {
        var rooms = new List<AnRoom> { new("101", "Deluxe", "2000-01-01") };
        AnStay St(string id, string from, bool legacy, string? company = null) => new(id, "g" + id, "101", from, LegacyCore.AddDays(from, 1), 1, 100000, "Walk-in", null, company, null, "Departed", false, legacy);
        AnBill Bl(string id, string stay, string date, long paid, bool legacy) => new(id, stay, date, 100000, 0, 0, 0, 5000, 105000, 1, paid, legacy);
        var i = new AnInput(rooms, [St("L1", "2019-03-01", true, "Acme"), St("V1", "2026-10-01", false)], [Bl("BL1", "L1", "2019-03-02", 20000, true), Bl("BV1", "V1", "2026-10-02", 0, false)], [], "2026-10-24", "2026-10-30", "2020-03-01", "2021-03-31");
        var r = LegacyZero.ReceivablesBadDebt(i, new AnParams());
        var cut = r.Sections!.First(s => s.Key == "cutover-comparison").Rows;
        Assert.Equal(85000L, cut[0].Cells["due"]); Assert.Equal(105000L, cut[1].Cells["due"]);
        Assert.Equal("Verify first", r.Sections!.First(s => s.Key == "follow-up-list").Rows.First(x => (string?)x.Cells["bill"] == "BL1").Cells["action"]);
    }

    [Fact]
    public void Definitions_are_study_reports_with_a_measurement_key_and_the_money_gate()
    {
        var all = new StudyReport[]
        {
            new RevenueMonthlyReport(null!, null!), new YearComparisonReport(null!, null!), new IndexVsBaseReport(null!, null!), new FyDeepDiveReport(null!, null!), new OccupancyHistoryReport(null!, null!),
            new OperationsPatternsReport(null!, null!), new RoomsAndRatesReport(null!, null!), new ZeroRateComplimentaryReport(null!, null!), new ReceivablesBadDebtReport(null!, null!),
        };
        Assert.Equal(9, all.Select(a => a.Definition.Id).Distinct().Count());
        foreach (var r in all)
        {
            Assert.Equal("study", r.Definition.Area); Assert.StartsWith("meas.study.", ((IMeasuredReport)r).MeasKey);
            if (r.Definition.MoneyFlag) Assert.Equal("PERM-RPT-002", r.Definition.MoneyGate);
        }
        Assert.False(new OccupancyHistoryReport(null!, null!).Definition.MoneyFlag); Assert.False(new OperationsPatternsReport(null!, null!).Definition.MoneyFlag);
    }
}
