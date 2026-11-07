// F12 MEAS · meas-study-pack (server): ChartBlock encoding, bar parity (the same values in the table cells, the Excel native chart cache and the
// PDF bar shapes), line / stacked / heat, rule M on a chart, pack summary arithmetic (= prototype packSummary), meas.study.packSchedule.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Reporting.Charts;
using Hcc.Reporting.Reports.StudyPack;

namespace Hcc.Reporting.Tests;

public sealed class MeasStudyPackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ChartBlock Bar = new("t-bar", "Test bar", ChartKind.Bar, ["Apr 25", "May 25", "Jun 25"],
        [new ChartSeries("rev", "Revenue", ReportColumnType.Money, [12345600, 0, 98765400]), new ChartSeries("n", "Nights", ReportColumnType.Int, [40, 12, 77])], "Month");

    private static ReportResult Result(params ReportTable[] sections) => new("rpt.study.chart-test", "Chart test", new BoundParams(new Dictionary<string, string>()),
        sections[0].Columns, sections[0].Rows, new ReportAbout(new DateTimeOffset(2026, 10, 24, 4, 30, 0, TimeSpan.Zero), "Owner", "Test", false, false), Sections: sections);

    private static Dictionary<string, string> Unzip(byte[] b)
    {
        using var z = new ZipArchive(new MemoryStream(b));
        return z.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }

    [Fact]
    public void Section_and_back_keep_kind_labels_values_and_the_legacy_marker()
    {
        var t = ChartBlocks.Section(Bar with { Legacy = [true, false, false] });
        Assert.Equal("chart:t-bar", t.Key);
        Assert.Equal("chart.bar", t.Columns[0].Key);
        Assert.True(t.Columns[1].Money);
        Assert.Equal("legacy", t.Rows[0].Group);
        Assert.Equal(12345600L, t.Rows[0].Cells["rev"]);
        var c = ChartBlocks.Of(t)!;
        Assert.Equal(ChartKind.Bar, c.Kind);
        Assert.Equal(Bar.Labels, c.Labels);
        Assert.Equal([12345600d, 0d, 98765400d], c.Series[0].Values.Select(v => v!.Value));
        Assert.Equal([true, false, false], c.Legacy);
        Assert.Null(ChartBlocks.Of(new ReportTable("x", null, [new ReportColumn("x", "X", ReportColumnType.Text, false)], [])));
        Assert.Throws<ArgumentException>(() => ChartBlocks.Section(Bar with { Series = [new ChartSeries("a", "A", ReportColumnType.Int, [1])] }));
    }

    [Fact]
    public void Bar_parity_the_same_values_in_the_cells_the_Excel_chart_and_the_PDF_shapes()
    {
        var r = Result(ChartBlocks.Section(Bar));
        var ctx = new RenderContext("HPE", "Test Hotel", ReportFormat.Xlsx);
        // Excel: the sheet cells and the chart cache hold the same numbers (money in rupees)
        var xlsx = new XlsxRenderer().Render(r, ctx).Bytes;
        var files = Unzip(xlsx);
        var chart = files["xl/charts/chart1.xml"];
        Assert.Contains("<c:barChart>", chart, StringComparison.Ordinal);
        var caches = Regex.Matches(chart, "<c:numCache>(.*?)</c:numCache>").Select(m => Regex.Matches(m.Groups[1].Value, "<c:v>([^<]*)</c:v>").Select(x => double.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).ToList()).ToList();
        Assert.Equal([123456d, 0d, 987654d], caches[0]);
        Assert.Equal([40d, 12d, 77d], caches[1]);
        var cells = RendererTests.SheetCells(xlsx, 1);
        Assert.Equal([123456m, 0m, 987654m], cells.Skip(1).Select(row => Convert.ToDecimal(row[1], CultureInfo.InvariantCulture)));
        Assert.Contains("<drawing xmlns:r=", files["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);
        Assert.Contains("../drawings/drawing1.xml", files["xl/worksheets/_rels/sheet1.xml.rels"], StringComparison.Ordinal);
        Assert.Contains("/xl/charts/chart1.xml", files["[Content_Types].xml"], StringComparison.Ordinal);
        Assert.Contains("Test bar", files["xl/workbook.xml"], StringComparison.Ordinal);
        // PDF: one filled rectangle per point per series; widths proportional to the drawn value; the table text is still there
        var pdf = new PdfRenderer().Render(r, ctx with { Format = ReportFormat.Pdf }).Bytes;
        var text = Encoding.Latin1.GetString(pdf);
        var rects = Regex.Matches(text, @"([\d.]+ [\d.]+ [\d.]+) rg ([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f").Select(m => (Fill: m.Groups[1].Value, W: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(6, rects.Count);
        var rev = rects.Where(x => x.Fill == rects[0].Fill).Select(x => x.W).ToList();
        Assert.Equal(3, rev.Count);
        Assert.Equal(0, rev[1]);
        Assert.InRange(rev[0] / rev[2], 123456.0 / 987654 - 0.002, 123456.0 / 987654 + 0.002);
        var marks = ChartBlocks.Marks(ChartBlocks.Of(r.Sections![0])!, 300, 559, 700, 11);
        Assert.Equal([123456d, 0d, 987654d], marks.Where(m => m.Series == "rev").Select(m => m.Value));
        var lines = PdfText.Extract(pdf).Lines;
        Assert.Contains("Rs.1,23,456", lines); // MinimalPdfWriter prints ₹ as "Rs." (WinAnsi)
        Assert.Contains("77", lines);
    }

    [Fact]
    public void Line_stacked_and_heat()
    {
        var line = ChartBlocks.Marks(Bar with { Kind = ChartKind.Line }, 0, 100, 500, 11);
        Assert.Equal([40d, 12d, 77d], line.Where(m => m.Series == "n").Select(m => m.Value));
        var st = Bar with { Kind = ChartKind.Stacked, Series = [new ChartSeries("a", "A", ReportColumnType.Int, [1, 2, 3]), new ChartSeries("b", "B", ReportColumnType.Int, [3, 2, 1])] };
        var sm = ChartBlocks.Marks(st, 0, 100, 500, 11);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(100, sm.Where(m => m.Index == i).Sum(m => m.W), 6)); // every row sums to 4 = the max
        var ctx = new RenderContext("HPE", "Test Hotel", ReportFormat.Xlsx);
        Assert.Contains("<c:grouping val=\"stacked\"/>", Unzip(new XlsxRenderer().Render(Result(ChartBlocks.Section(st)), ctx).Bytes)["xl/charts/chart1.xml"], StringComparison.Ordinal);
        Assert.Contains("<c:lineChart>", Unzip(new XlsxRenderer().Render(Result(ChartBlocks.Section(Bar with { Kind = ChartKind.Line })), ctx).Bytes)["xl/charts/chart1.xml"], StringComparison.Ordinal);
        var heat = Result(ChartBlocks.Section(Bar with { Kind = ChartKind.Heat }));
        Assert.DoesNotContain(Unzip(new XlsxRenderer().Render(heat, ctx).Bytes).Keys, k => k.StartsWith("xl/charts/", StringComparison.Ordinal));
        Assert.DoesNotContain(" re f", Encoding.Latin1.GetString(new PdfRenderer().Render(heat, ctx).Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_M_a_money_series_removed_with_its_column_is_not_drawn()
    {
        var t = ChartBlocks.Section(Bar);
        var noMoney = ReportService.StripMoney(Result(t));
        var c = ChartBlocks.Of(ReportRunner.Tables(noMoney)[0])!;
        Assert.Equal(["n"], c.Series.Select(s => s.Key));
        Assert.DoesNotContain("Revenue", Unzip(new XlsxRenderer().Render(noMoney, new RenderContext("HPE", "T", ReportFormat.Xlsx)).Bytes)["xl/charts/chart1.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_summary_by_the_contract_definitions_same_numbers_as_the_prototype()
    {
        DateOnly from = new(2025, 4, 1), to = new(2025, 5, 31);
        RoomNightFact F(string room, int m, int d, long amt) => new(room, new DateOnly(2025, m, d), amt);
        // two lines on the same room-night (a correction) count once for occupancy but twice as charged nights
        var facts = new[] { F("101", 4, 1, 200000), F("101", 4, 1, 50000), F("102", 4, 2, 180000), F("101", 5, 31, 210000) };
        var (months, fy) = PackRules.Summary(10, facts, from, to);
        Assert.Equal(["Apr 25", "May 25"], months.Select(m => m.Label));
        Assert.Equal(2, months[0].RoomNights);
        Assert.Equal(3, months[0].ChargedNights);
        Assert.Equal(430000, months[0].RoomRevenue);
        Assert.Equal(300, months[0].Capacity);
        Assert.Equal(0.7, months[0].Occupancy);           // 2 / 300 = 0.67 % → 0.7
        Assert.Equal(143333, months[0].Adr);               // 430000 / 3
        Assert.Equal(3, fy.RoomNights);
        Assert.Equal(610, fy.Capacity);
        Assert.Equal(160000, fy.Adr);                      // 640000 / 4
        Assert.Equal(0.5, fy.Occupancy);
        var tables = PackRules.SummaryTables("2025-26", 10, months, fy, 35, 205000);
        Assert.Equal(["summary", "scorecard", "chart:pack-revenue", "chart:pack-occupancy"], tables.Select(t => t.Key));
        Assert.Equal(ReportRowKind.Total, tables[0].Rows[^1].Kind);
        Assert.Equal("Below target", tables[1].Rows[0].Cells["status"]);
        Assert.Equal(21, PackRules.StudyIds.Count);
        Assert.Contains("Legacy to 30 Oct 2026 / HCC live", PackRules.Definition.DateBasis, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_schedule_due_rule()
    {
        Assert.True(StudyPackSchedule.Due("monthly", new DateOnly(2026, 11, 1)));
        Assert.False(StudyPackSchedule.Due("monthly", new DateOnly(2026, 11, 2)));
        Assert.Equal([true, false, true, true, false], new[] { (2026, 10), (2026, 11), (2027, 1), (2027, 4), (2027, 2) }.Select(x => StudyPackSchedule.Due("quarterly", new DateOnly(x.Item1, x.Item2, 1))));
        Assert.False(StudyPackSchedule.Due("off", new DateOnly(2026, 11, 1)));
        Assert.Equal("monthly", StudyPackSchedule.Parse(null));
    }

    private sealed class Settings(string? schedule) : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) =>
            Task.FromResult(key == StudyPackSchedule.SettingKey && schedule is not null ? ConfigValue.Of(schedule) : ConfigValue.Null);

        public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => GetAsync(propertyId, key, on, ct);
    }

    private sealed class Prop(DateOnly d) : IReportPropertyInfo
    {
        public Task<ReportProperty> GetAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(new ReportProperty("HPE", "Test Hotel", d));
    }

    private sealed class Gate(bool on) : IMeasurementGate
    {
        public Task<bool> IsOnAsync(int propertyId, string key, CancellationToken ct = default) => Task.FromResult(on);

        public Task EnsureOnAsync(int propertyId, string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("monthly", 2026, 11, true, true)]
    [InlineData("off", 2026, 11, true, false)]
    [InlineData("quarterly", 2026, 11, true, false)]
    [InlineData("quarterly", 2027, 1, true, true)]
    [InlineData("monthly", 2026, 11, false, false)]
    [InlineData(null, 2026, 12, true, true)]
    public async Task The_setting_drives_the_F7_schedule_and_its_due_rule(string? setting, int y, int m, bool itemOn, bool runs)
    {
        var stores = new MemoryStores();
        var pack = new ReportScheduleRecord(0, PackRules.Id, new Dictionary<string, string> { ["fy"] = "2026-27" }, ReportFormat.Pdf, "monthly", 1, new TimeOnly(6, 0), 1, [1, 2], Enabled: !runs);
        var other = new ReportScheduleRecord(0, "rpt.money.shift-report", new Dictionary<string, string>(), ReportFormat.Pdf, "daily", null, new TimeOnly(6, 0), 1, [1], true);
        await stores.SaveAsync(1, pack, Ct);
        await stores.SaveAsync(1, other, Ct);
        var first = new DateOnly(y, m, 1);
        var changed = await new StudyPackScheduler(stores, new Settings(setting), new Prop(first.AddDays(3)), new Gate(itemOn)).SyncAsync(1, Ct);
        Assert.Equal(1, changed);
        var saved = stores.Schedules.Single(s => s.ReportId == PackRules.Id);
        Assert.Equal(runs, saved.Enabled);
        Assert.Equal(runs, ReportService.Due(saved, first, new TimeOnly(6, 30)));
        Assert.True(stores.Schedules.Single(s => s.ReportId != PackRules.Id).Enabled);
    }
}
