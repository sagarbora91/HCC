// F12 MEAS · meas-study-b · parity of the twelve ported study reports: the prototype (03 Prototype/tests/stream-meas-study-b.mjs) writes Goldens/study-b-an.json
// (a deterministic AnInput, the parameters and every section of every report); the server selectors must reproduce every cell exactly.
using System.Text.Json;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Analytics;
using Hcc.Reporting.Reports.MeasStudyB;

namespace Hcc.Reporting.Tests;

public sealed class StudyBParityTests
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    private static JsonElement Golden() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Goldens", "study-b-an.json"))).RootElement;

    private static AnParams ParamsOf(JsonElement p)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var extras = new BeyondExtras();
        foreach (var kv in p.EnumerateObject())
            if (kv.Name == "extras") extras = JsonSerializer.Deserialize<BeyondExtras>(kv.Value.GetRawText(), Opt)!;
            else values[kv.Name] = kv.Value.GetString()!;
        return new AnParams { Values = values, Extras = extras };
    }

    [Fact]
    public void Twelve_study_reports_are_registered_with_the_prototype_ids()
    {
        var ids = StudyBReport.Specs.Select(s => s.Id).ToList();
        Assert.Equal(12, ids.Count);
        Assert.All(ids, id => Assert.StartsWith("rpt.study.", id));
        var g = Golden().GetProperty("expected");
        Assert.Equal(g.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        foreach (var r in StudyBReport.All(null)) Assert.Equal("meas.study." + ((StudyBReport)r).MeasKey["meas.study.".Length..], ((StudyBReport)r).MeasKey);
    }

    [Fact]
    public void Every_report_reproduces_the_prototype_golden_cell_for_cell()
    {
        var root = Golden(); var input = JsonSerializer.Deserialize<AnInput>(root.GetProperty("input").GetRawText(), Opt)!;
        foreach (var spec in StudyBReport.Specs)
        {
            var result = spec.Select(input, ParamsOf(root.GetProperty("params").GetProperty(spec.Id)));
            var want = root.GetProperty("expected").GetProperty(spec.Id);
            Assert.Equal(want.GetProperty("legacy").GetBoolean(), result.Legacy);
            var ws = want.GetProperty("sections").EnumerateArray().ToList();
            Assert.True(ws.Count == result.Sections.Count, $"{spec.Id}: sections {result.Sections.Count} vs {ws.Count}");
            for (var i = 0; i < ws.Count; i++)
            {
                var w = ws[i]; var s = result.Sections[i]; var where = $"{spec.Id}/{s.Key}";
                Assert.Equal(w.GetProperty("key").GetString(), s.Key);
                Assert.Equal(w.GetProperty("columns").EnumerateArray().Select(x => x.GetString()), s.Columns.Select(c => c.Key));
                var wr = w.GetProperty("rows").EnumerateArray().ToList();
                Assert.True(wr.Count == s.Rows.Count, $"{where}: rows {s.Rows.Count} vs {wr.Count}");
                for (var r = 0; r < wr.Count; r++)
                {
                    Assert.Equal(wr[r].GetProperty("kind").GetString(), s.Rows[r].Kind == ReportRowKind.Total ? "total" : "row");
                    foreach (var c in s.Columns)
                    {
                        var exp = wr[r].GetProperty("cells").GetProperty(c.Key); var got = s.Rows[r].Cells[c.Key]; var at = $"{where} row {r} {c.Key}";
                        switch (exp.ValueKind)
                        {
                            case JsonValueKind.Null: Assert.True(got is null, $"{at}: expected null, got {got}"); break;
                            case JsonValueKind.Number: Assert.True(got is not null && Convert.ToDouble(got, System.Globalization.CultureInfo.InvariantCulture) == exp.GetDouble(), $"{at}: expected {exp.GetDouble()}, got {got}"); break;
                            default: Assert.True(got is string str && str == exp.GetString(), $"{at}: expected '{exp.GetString()}', got '{got}'"); break;
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Definitions_are_money_flagged_and_the_gate_is_the_finance_gate()
    {
        foreach (var r in StudyBReport.All(null))
        {
            var d = r.Definition;
            Assert.Equal("study", d.Area);
            Assert.Equal(d.Columns.Any(c => c.Money) || d.MoneyFlag, d.MoneyFlag);
            Assert.Equal(d.MoneyFlag ? "PERM-RPT-002" : null, d.MoneyGate);
        }
        Assert.Equal(ReportPhone.Yes, StudyBReport.All(null).Single(x => x.Definition.Id == "rpt.study.owner-dashboard").Definition.Phone);
    }
}
