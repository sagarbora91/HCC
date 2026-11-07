// F7-R1 · report engine (server): catalogue cross-check with the prototype, parameters, rule V / M (DEC-061), masks, OP-13 hook, paging / RPT_TOO_LARGE.
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Tests;

public sealed class EngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); return null; }
        catch (HccException e) { return e.Code; }
    }

    private static readonly Dictionary<ReportDateClass, string> Dc = new()
    {
        [ReportDateClass.Day] = "D-DAY", [ReportDateClass.Short] = "D-S", [ReportDateClass.Business] = "D-B", [ReportDateClass.Financial] = "D-F", [ReportDateClass.Month] = "D-M",
        [ReportDateClass.FinancialYear] = "D-FY", [ReportDateClass.AsOf] = "D-ASOF", [ReportDateClass.Now] = "D-NOW", [ReportDateClass.Forward] = "D-FWD",
    };

    private static readonly Dictionary<ReportColumnType, string> Ty = new()
    {
        [ReportColumnType.Text] = "text", [ReportColumnType.Int] = "int", [ReportColumnType.Money] = "money", [ReportColumnType.Date] = "date", [ReportColumnType.Percent] = "pct", [ReportColumnType.Bool] = "bool",
    };

    /// <summary>Every report registered on the server: what AddReportingModule + the F7-RPT lines register.</summary>
    public static IReadOnlyList<ReportDefinition> ServerCatalogue() => new ReportCatalogue(
    [
        new RevenueByCategoryTenderReport(null!),
        .. F7R3Reports(),
        .. F7R5Reports(),
        .. F7R6Reports(), // F7-R6
        // F12-RPT-PEOPLE
        new Hcc.Reporting.Reports.MeasPeople.StaffProductivityReport(null!), new Hcc.Reporting.Reports.MeasPeople.AttritionReport(null!), new Hcc.Reporting.Reports.MeasPeople.AgentScorecardReport(null!, null!),
        new Hcc.Reporting.Reports.MeasPeople.TrainingReport(null!), new Hcc.Reporting.Reports.MeasPeople.StaffPulseReport(null!, null!),
        // F12-RPT-PEOPLE end
        // F7-R4
        new ShiftReport(null!, null!), new CollectionDetailReport(null!, null!), new CreditBillsReport(null!), new ExpenseRegisterReport(null!), new ExpensesByHeadReport(null!),
        // F7-R4 end
    ]).All;

    // F7-R3
    private static IEnumerable<IReportSelector> F7R3Reports() =>
        [new Hcc.Reporting.Reports.Guests.MovementRegisterReport(null!), new Hcc.Reporting.Reports.Guests.GuestRegisterReport(null!), new Hcc.Reporting.Reports.Guests.FormCRegisterReport(null!),
         new Hcc.Reporting.Reports.Guests.RateChangesReport(null!), new Hcc.Reporting.Reports.Rooms.RoomStatusReport(null!, null!)];
    // F7-R3 end

    // F7-R5
    private static IEnumerable<IReportSelector> F7R5Reports() =>
        [new Hcc.Reporting.Reports.Tax.SacSummaryReport(null!, null!), new Hcc.Reporting.Reports.Tax.DocumentsIssuedReport(null!), new Hcc.Reporting.Reports.Tax.GstByBillReport(null!, null!),
         new Hcc.Reporting.Reports.Tax.InvoiceRegisterReport(null!, null!), new Hcc.Reporting.Reports.Tax.BillCorrectionsReport(null!), new Hcc.Reporting.Reports.Tax.GstSummaryReport(null!)];
    // F7-R5 end

    // F7-R6
    private static IEnumerable<IReportSelector> F7R6Reports() =>
        [new Hcc.Reporting.Reports.Acc.TrialBalanceReport(null!), new Hcc.Reporting.Reports.Acc.StatementReport(null!), new Hcc.Reporting.Reports.Acc.DayBookReport(null!), new Hcc.Reporting.Reports.Acc.PlReport(null!), new Hcc.Reporting.Reports.Acc.BalanceSheetReport(null!), new Hcc.Reporting.Reports.Acc.CashRatioReport(null!), new Hcc.Reporting.Reports.Acc.AgeingReport(null!), new Hcc.Reporting.Reports.Acc.TallyXmlReport(null!)];
    // F7-R6 end

    [Fact]
    public void Catalogue_json_from_the_prototype_equals_the_server_registry()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
        var server = ServerCatalogue().ToDictionary(d => d.Id);
        // F7-int: "side": "prototype" marks reports with no server mirror yet (R8's moved built reports); the server must not register them.
        var all = doc.RootElement.EnumerateArray().ToList();
        static bool Mirrored(JsonElement e) => !e.TryGetProperty("side", out var s) || s.GetString() != "prototype";
        var protoOnly = all.Where(e => !Mirrored(e)).Select(e => e.GetProperty("id").GetString()!).ToList();
        Assert.DoesNotContain(protoOnly, server.ContainsKey);
        var ids = all.Where(Mirrored).Select(e => e.GetProperty("id").GetString()!).ToList();
        Assert.Equal(server.Keys.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        foreach (var e in all.Where(Mirrored))
        {
            var d = server[e.GetProperty("id").GetString()!];
            Assert.Equal(e.GetProperty("title").GetString(), d.Title);
            Assert.Equal(e.GetProperty("area").GetString(), d.Area);
            Assert.Equal(e.GetProperty("status").GetString(), d.Status);
            Assert.Equal(e.GetProperty("priority").GetString(), d.Priority);
            Assert.Equal(e.GetProperty("moneyFlag").GetBoolean(), d.MoneyFlag);
            Assert.Equal(e.GetProperty("moneyGate").GetString(), d.MoneyGate);
            Assert.Equal(e.GetProperty("viewPerms").EnumerateArray().Select(x => x.GetString()), d.ViewPerms);
            Assert.Equal(e.GetProperty("deskPerms").EnumerateArray().Select(x => x.GetString()), d.DeskPerms);
            Assert.Equal(e.GetProperty("exportPerm").GetString(), d.ExportPerm);
            Assert.Equal(e.GetProperty("dateClass").GetString(), d.DateClass is { } dc ? Dc[dc] : null);
            Assert.Equal(e.GetProperty("params").EnumerateArray().Select(x => x.GetString()), d.Params.Select(p => p.Key));
            Assert.Equal(e.GetProperty("formats").EnumerateArray().Select(x => x.GetString()), d.Formats.Select(f => f.ToString().ToLowerInvariant()));
            Assert.Equal(e.GetProperty("phone").GetString(), d.Phone.ToString().ToLowerInvariant());
            Assert.Equal(e.GetProperty("layout").GetString(), d.DocumentLayout ? "document" : "table");
            var cols = e.GetProperty("columns").EnumerateArray().ToList();
            Assert.Equal(cols.Count, d.Columns.Count);
            for (var i = 0; i < cols.Count; i++)
            {
                var c = d.Columns[i];
                Assert.Equal(cols[i].GetProperty("key").GetString(), c.Key);
                Assert.Equal(cols[i].GetProperty("type").GetString(), Ty[c.Type]);
                Assert.Equal(cols[i].GetProperty("money").GetBoolean(), c.Money);
                Assert.Equal(cols[i].GetProperty("mask").GetString(), c.Mask == ReportMask.None ? null : char.ToLowerInvariant(c.Mask.ToString()[0]) + c.Mask.ToString()[1..]);
                Assert.Equal(cols[i].GetProperty("computed").GetString(), c.Computed?.Expr);
            }
        }
    }

    [Fact]
    public void Registry_refuses_bad_ids_areas_duplicates_and_unknown_computed_deps()
    {
        var d = new DemoSelector().Definition;
        Assert.Throws<InvalidOperationException>(() => ReportCatalogue.Validate(d with { Id = "report.bad" }));
        Assert.Throws<InvalidOperationException>(() => ReportCatalogue.Validate(d with { Id = "rpt.money.x" }));
        Assert.Throws<InvalidOperationException>(() => ReportCatalogue.Validate(d with { Columns = [new("a", "A", ReportColumnType.Int, false, ReportMask.None, new("add(b)", ["b"]))] }));
        Assert.Throws<InvalidOperationException>(() => ReportCatalogue.Validate(d with { MoneyGate = null }));
        Assert.Throws<InvalidOperationException>(() => new ReportCatalogue([new DemoSelector(), new DemoSelector()]));
    }

    [Fact]
    public void Parameters_bind_by_date_class_like_the_prototype()
    {
        var today = new DateOnly(2026, 10, 24);
        BoundParams B(ReportDateClass? dc, params (string, string)[] raw) =>
            ParamBinder.Bind(new DemoSelector().Definition with { DateClass = dc, Params = [] }, raw.ToDictionary(x => x.Item1, x => (string?)x.Item2), today);
        Assert.Equal((today, today), (B(ReportDateClass.Business).From!.Value, B(ReportDateClass.Business).To!.Value));
        foreach (var (dc, raw) in new (ReportDateClass, (string, string)[])[]
                 {
                     (ReportDateClass.Business, [("from", "2026-10-24"), ("to", "2026-10-20")]), (ReportDateClass.Business, [("from", "2026-10-24"), ("to", "2026-10-25")]),
                     (ReportDateClass.Day, [("from", "2026-10-20"), ("to", "2026-10-21")]), (ReportDateClass.Short, [("from", "2026-01-01"), ("to", "2026-10-24")]),
                     (ReportDateClass.Business, [("from", "2026-1-1")]), (ReportDateClass.Month, [("month", "2026-13")]), (ReportDateClass.FinancialYear, [("fy", "2026-28")]),
                 })
            Assert.Throws<HccException>(() => B(dc, raw));
        Assert.Equal(new DateOnly(2026, 10, 30), B(ReportDateClass.Forward, ("from", "2026-10-24"), ("to", "2026-10-30")).To);
        Assert.Equal((new DateOnly(2026, 10, 1), today), (B(ReportDateClass.Month, ("month", "2026-10")).From!.Value, B(ReportDateClass.Month, ("month", "2026-10")).To!.Value));
        Assert.Equal(new DateOnly(2026, 9, 30), B(ReportDateClass.Month, ("month", "2026-09")).To);
        var fy = B(ReportDateClass.FinancialYear, ("fy", "2025-26"));
        Assert.Equal((new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31), true), (fy.From!.Value, fy.To!.Value, fy.Legacy));
        Assert.Equal("2026-27", B(ReportDateClass.FinancialYear).Values["fy"]);
        Assert.False(B(ReportDateClass.Business).Legacy);
        Assert.Equal(today, B(ReportDateClass.AsOf).To);
        Assert.Throws<HccException>(() => ParamBinder.Bind(new DemoSelector().Definition, new Dictionary<string, string?> { ["tag"] = "z" }, today));
    }

    [Fact]
    public void Fixed_code_engine_rounds_half_away_from_zero_and_pct_has_one_decimal()
    {
        Assert.Equal([3L, -3L, 2L, -2L, 0L], new[] { FixedCodeEngine.DivRound(5, 2), FixedCodeEngine.DivRound(-5, 2), FixedCodeEngine.DivRound(7, 3), FixedCodeEngine.DivRound(-7, 3), FixedCodeEngine.DivRound(1, 0) });
        var e = new FixedCodeEngine();
        Assert.Equal(33.3, e.Evaluate(new("pct(a,b)", ["a", "b"]), new Dictionary<string, object?> { ["a"] = 1L, ["b"] = 3L }));
        Assert.Equal(66.7, e.Evaluate(new("pct(a,b)", ["a", "b"]), new Dictionary<string, object?> { ["a"] = 2L, ["b"] = 3L }));
        Assert.Equal(5L, e.Evaluate(new("sub(a,b,c)", []), new Dictionary<string, object?> { ["a"] = 10L, ["b"] = 3L, ["c"] = 2L }));
        Assert.Null(e.Evaluate(new("add(a,b)", []), new Dictionary<string, object?> { ["a"] = 1L, ["b"] = null }));
        Assert.False(e.CanEvaluate(new("formula(a)", [])));
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(99)]
    public async Task Rule_V_refuses_reception_cashier_housekeeping_maintenance_and_unknown(int principal)
    {
        var run = Rig.Runner(null, new DemoSelector());
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => run.RunAsync(Rig.C(principal), "rpt.admin.engine-test", Rig.NoParams, null, Ct)));
    }

    [Fact]
    public async Task Rule_V_owner_manager_accountant_view_and_export_an_export_deny_keeps_view()
    {
        var p = new FakePolicy();
        var run = Rig.Runner(p, new DemoSelector());
        foreach (var u in new[] { 1, 2, 5 })
        {
            Assert.NotNull(await run.RunAsync(Rig.C(u), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(Export: true), Ct));
            Assert.Contains(await run.CatalogueAsync(Rig.C(u), Ct), e => e.Definition.Id == "rpt.admin.engine-test" && e.CanExport && e.Money);
        }

        Assert.Empty(await run.CatalogueAsync(Rig.C(3), Ct));
        p.Denied.Add((5, "PERM-RPT-003"));
        Assert.NotNull(await run.RunAsync(Rig.C(5), "rpt.admin.engine-test", Rig.NoParams, null, Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => run.RunAsync(Rig.C(5), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(Export: true), Ct)));
        Assert.Equal(HccErrorCode.NOT_FOUND, await Code(() => run.RunAsync(Rig.C(1), "rpt.money.nope", Rig.NoParams, null, Ct)));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => run.RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(Export: true, ExportFormat: "Xml"), Ct)));
    }

    [Fact]
    public async Task Rule_M_removes_money_and_dependent_computed_columns_all_money_is_forbidden()
    {
        var p = new FakePolicy();
        p.Denied.Add((2, "PERM-RPT-002"));
        var run = Rig.Runner(p, new DemoSelector());
        var r = await run.RunAsync(Rig.C(2), "rpt.admin.engine-test", Rig.NoParams, null, Ct);
        Assert.Equal(["guest", "id", "n", "note"], r.Columns.Select(c => c.Key));
        Assert.Equal(["amt", "avg"], r.Removed!.Order(StringComparer.Ordinal));
        Assert.All(r.Rows, x => Assert.False(x.Cells.ContainsKey("amt") || x.Cells.ContainsKey("avg")));
        var owner = await run.RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, null, Ct);
        Assert.Equal(33334L, owner.Rows[0].Cells["avg"]);
        Assert.Null(owner.Removed);
        var onlyMoney = new DemoSelector(def: new DemoSelector().Definition with { Id = "rpt.admin.only-money", Columns = [new("amt", "Amount", ReportColumnType.Money, true)] });
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Rig.Runner(p, onlyMoney).RunAsync(Rig.C(2), "rpt.admin.only-money", Rig.NoParams, null, Ct)));
    }

    [Fact]
    public async Task DEC_061_no_housekeeping_maintenance_role_holds_a_report_money_or_export_code_in_any_migration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var codes = ServerCatalogue().SelectMany(d => new[] { d.MoneyGate, d.ExportPerm }).Append("PERM-RPT-002").Append("PERM-RPT-003").Append("PERM-RPT-004").OfType<string>().ToHashSet();
        foreach (var f in Directory.GetFiles(Path.Combine(dir!.FullName, "database", "migrations"), "*.sql"))
            foreach (Match m in Regex.Matches(await File.ReadAllTextAsync(f, Ct), @"\('(SUP|HKP|MNT|STK)',\s*'(PERM-[A-Z]+-\d{3})'"))
                Assert.False(codes.Contains(m.Groups[2].Value), $"{Path.GetFileName(f)} grants {m.Groups[2].Value} to {m.Groups[1].Value}");
        // and the runner: a housekeeping-page report shows rows, never money; names masked without reservations / billing
        var hk = new DemoSelector(def: new DemoSelector().Definition with { Id = "rpt.admin.hk-test", ViewPerms = ["PERM-HK-001"] });
        var r = await Rig.Runner(null, hk).RunAsync(Rig.C(7), "rpt.admin.hk-test", Rig.NoParams, null, Ct);
        Assert.DoesNotContain(r.Columns, c => c.Money || c.Key == "avg");
        Assert.Equal("J••• T•••", r.Rows[0].Cells["guest"]);
        Assert.Contains("HKP", MoneyMasker.NoMoneyRoles);
    }

    [Fact]
    public async Task Masks_id_numbers_always_guest_names_visible_with_reservations()
    {
        var r = await Rig.Runner(null, new DemoSelector()).RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(All: true), Ct);
        Assert.Equal("XXXX-XXXX-9012", r.Rows[0].Cells["id"]);
        Assert.Equal("Journey Test", r.Rows[0].Cells["guest"]);
        Assert.Equal("XXXXXX7890", MoneyMasker.Mask(ReportMask.BankAccount, "001234567890"));
    }

    [Fact]
    public async Task Paging_100_and_RPT_TOO_LARGE_above_50000_file_rows_provisional_on_the_open_day()
    {
        var run = Rig.Runner(null, new DemoSelector(250));
        var pg = await run.RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(Page: 2), Ct);
        Assert.Equal((50, 2, 100, 250), (pg.Rows.Count, pg.PageIndex, pg.PageSize, pg.TotalRows));
        Assert.True(pg.About.Provisional);
        var old = await run.RunAsync(Rig.C(1), "rpt.admin.engine-test", new Dictionary<string, string?> { ["from"] = "2026-10-20", ["to"] = "2026-10-21" }, null, Ct);
        Assert.False(old.About.Provisional);
        var e = await Assert.ThrowsAsync<HccException>(() => Rig.Runner(null, new DemoSelector(50_001)).RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(All: true), Ct));
        Assert.StartsWith("RPT_TOO_LARGE", e.Message, StringComparison.Ordinal);
    }
}
