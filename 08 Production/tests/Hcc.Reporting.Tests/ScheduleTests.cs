// F7-R1 · scheduled runs: due rules, relative tokens, run as the owner (never wider), money recipients, delivery held while the internet is off,
// print / export log rows.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Reporting.Tests;

public sealed class MemoryStores : IReportScheduleStore, IReportRunStore, IReportExportLog, IReportFavourites, IReportPrincipalRoles, IReportExportLogReader
{
    public Task<IReadOnlyList<ReportExportLogRecord>> ExportLogAsync(int propertyId, int take, int? principalId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReportExportLogRecord>>(Log.AsEnumerable().Reverse().Where(r => principalId is null || r.PrincipalId == principalId).Take(take).ToList());

    public readonly List<ReportScheduleRecord> Schedules = [];
    public readonly List<ReportRunRecord> Runs = [];
    public readonly List<ReportExportLogRecord> Log = [];
    private readonly HashSet<(int, string)> _fav = [];

    public Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(int propertyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportScheduleRecord>>(Schedules.ToList());

    public Task<long> SaveAsync(int propertyId, ReportScheduleRecord schedule, CancellationToken ct = default)
    {
        var id = schedule.Id == 0 ? Schedules.Count + 1 : schedule.Id;
        Schedules.RemoveAll(s => s.Id == id);
        Schedules.Add(schedule with { Id = id });
        return Task.FromResult(id);
    }

    public Task DeleteAsync(int propertyId, long id, CancellationToken ct = default) { Schedules.RemoveAll(s => s.Id == id); return Task.CompletedTask; }

    public Task<long> StartAsync(int propertyId, ReportRunRecord run, CancellationToken ct = default) { Runs.Add(run with { Id = Runs.Count + 1 }); return Task.FromResult((long)Runs.Count); }

    public Task FinishAsync(int propertyId, long id, string status, int? rows, string? fileRef, string? error, CancellationToken ct = default)
    {
        var i = (int)id - 1;
        Runs[i] = Runs[i] with { Status = status, Rows = rows, FileRef = fileRef, Error = error };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReportRunRecord>> ListAsync(int propertyId, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportRunRecord>>(Runs.ToList());

    public Task<long> AppendAsync(int propertyId, ReportExportLogRecord entry, CancellationToken ct = default) { Log.Add(entry); return Task.FromResult((long)Log.Count); }

    public Task<bool> ToggleAsync(int propertyId, int principalId, string reportId, CancellationToken ct = default) =>
        Task.FromResult(_fav.Add((principalId, reportId)) || !_fav.Remove((principalId, reportId)));

    public Task<IReadOnlyList<string>> FavouritesAsync(int propertyId, int principalId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_fav.Where(f => f.Item1 == principalId).Select(f => f.Item2).ToList());

    public Task<IReadOnlyList<string>> RolesAsync(int principalId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(FakePolicy.Roles.TryGetValue(principalId, out var r) ? [r] : []);
}

public sealed class FakeDelivery : IReportDelivery
{
    public bool InternetOn { get; set; }
    public readonly List<long> Queued = [];

    public Task<bool> EnqueueAsync(int propertyId, long runId, IReadOnlyList<int> recipients, string fileRef, CancellationToken ct = default) { Queued.Add(runId); return Task.FromResult(!InternetOn); }
}

public sealed class ScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ReportService S, MemoryStores M, FakeDelivery D, FakePolicy P, FixedClock C) Rig2()
    {
        var p = new FakePolicy();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero)); // 07:30 IST
        var prop = new FakeProperty(new DateOnly(2026, 10, 24));
        var cat = new ReportCatalogue([new DemoSelector()]);
        var runner = new ReportRunner(cat, p, prop, [new FixedCodeEngine()], clock);
        var m = new MemoryStores();
        var d = new FakeDelivery();
        var s = new ReportService(runner, cat, [new XlsxRenderer(), new PdfRenderer(), new CsvRenderer(), new PrintHtmlRenderer()], prop, m, m, m, m, m, p, new ReferenceOnlyFileStore(), d, clock, m);
        return (s, m, d, p, clock);
    }

    private static readonly ScheduleRequest Daily = new("rpt.admin.engine-test", new Dictionary<string, string> { ["range"] = "yesterday" }, "xlsx", "daily", null, "07:00", [1, 5]);

    [Fact]
    public void Due_and_tokens()
    {
        var s = new ReportScheduleRecord(1, "x", new Dictionary<string, string>(), ReportFormat.Csv, "weekly", 0, new TimeOnly(7, 0), 1, [1], true);
        Assert.True(ReportService.Due(s, new DateOnly(2026, 10, 25), new TimeOnly(8, 0)));
        Assert.False(ReportService.Due(s, new DateOnly(2026, 10, 25), new TimeOnly(6, 59)));
        Assert.False(ReportService.Due(s with { Cadence = "monthly", Day = 1 }, new DateOnly(2026, 10, 25), new TimeOnly(8, 0)));
        Assert.False(ReportService.Due(s with { LastRun = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero) }, new DateOnly(2026, 10, 25), new TimeOnly(8, 0)));
        var t = ReportService.ResolveTokens(new Dictionary<string, string> { ["range"] = "month-to-date", ["asOf"] = "yesterday" }, new DateOnly(2026, 10, 24));
        Assert.Equal(("2026-10-01", "2026-10-24", "2026-10-23"), (t["from"], t["to"], t["asOf"]));
    }

    [Fact]
    public async Task Schedule_save_rules_money_recipients_owner_only_changes_and_PERM_RPT_004()
    {
        var (s, m, _, _, _) = Rig2();
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.SaveScheduleAsync(Rig.C(2), null, Daily with { Recipients = [3] }, Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.SaveScheduleAsync(Rig.C(4), null, Daily, Ct))).Code);
        await Assert.ThrowsAsync<HccException>(() => s.SaveScheduleAsync(Rig.C(2), null, Daily with { Format = "xml" }, Ct));
        await Assert.ThrowsAsync<HccException>(() => s.SaveScheduleAsync(Rig.C(2), null, Daily with { Cadence = "weekly", Day = 9 }, Ct));
        var id = await s.SaveScheduleAsync(Rig.C(2), null, Daily, Ct);
        Assert.Equal(2, m.Schedules.Single().OwnerPrincipalId);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.DeleteScheduleAsync(Rig.C(5), id, Ct))).Code);
        Assert.Empty(await s.SchedulesAsync(Rig.C(5), Ct));
        Assert.Single(await s.SchedulesAsync(Rig.C(1), Ct));
        await s.DeleteScheduleAsync(Rig.C(1), id, Ct);
        Assert.Empty(m.Schedules);
    }

    [Fact]
    public async Task Run_due_runs_as_the_owner_is_held_while_internet_off_once_per_day_and_fails_when_the_owner_lost_the_report()
    {
        var (s, m, d, p, _) = Rig2();
        await s.SaveScheduleAsync(Rig.C(2), null, Daily, Ct);
        Assert.Equal((0, 1, 0), await s.RunDueAsync(1, Ct));
        var run = m.Runs.Single();
        Assert.Equal(("held", 2), (run.Status, run.PrincipalId));
        Assert.Contains("\"from\":\"2026-10-23\"", run.ParamsJson, StringComparison.Ordinal);
        Assert.StartsWith("rpt/1/HPE-rpt.admin.engine-test-2026-10-23-2026-10-23", run.FileRef, StringComparison.Ordinal);
        Assert.Equal((0, 0, 0), await s.RunDueAsync(1, Ct));
        m.Schedules[0] = m.Schedules[0] with { LastRun = null };
        p.Denied.Add((2, "PERM-RPT-001"));
        Assert.Equal((0, 0, 1), await s.RunDueAsync(1, Ct));
        Assert.Equal("failed", m.Runs[^1].Status);
        Assert.Single(d.Queued);
    }

    [Fact]
    public async Task Export_and_print_write_log_rows_cashier_cannot_print_a_report_it_cannot_view()
    {
        var (s, m, _, _, _) = Rig2();
        var f = await s.ExportAsync(Rig.C(5), "rpt.admin.engine-test", "csv", new Dictionary<string, string?>(), Ct);
        Assert.EndsWith(".csv", f.FileName, StringComparison.Ordinal);
        await s.PrintLogAsync(Rig.C(1), "rpt.admin.engine-test", new PrintLogRequest(null, 2), Ct);
        Assert.Equal([ReportFormat.Csv, ReportFormat.Print], m.Log.Select(l => l.Format));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.PrintLogAsync(Rig.C(4), "rpt.admin.engine-test", new PrintLogRequest(null, 1), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ExportAsync(Rig.C(3), "rpt.admin.engine-test", "xlsx", new Dictionary<string, string?>(), Ct))).Code);
        Assert.True(await s.ToggleFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", Ct));
        Assert.False(await s.ToggleFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", Ct));
        // AF-FIX2 F6: a wanted state is set, not flipped - a repeat gives the same answer (default interface SetAsync on the memory store).
        Assert.True(await s.SetFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", true, Ct));
        Assert.True(await s.SetFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", true, Ct));
        Assert.False(await s.SetFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", false, Ct));
        Assert.False(await s.SetFavouriteAsync(Rig.C(1), "rpt.admin.engine-test", false, Ct));
    }

    /// <summary>A no-money report with one money column (amt) and a computed column over it (avg); everything else as the demo report.</summary>
    private static readonly ReportDefinition PlainDef = new DemoSelector().Definition with { Id = "rpt.admin.plain-test", MoneyFlag = false, MoneyGate = null };

    private static (ReportService S, MemoryStores M, FakePolicy P) HubRig()
    {
        var p = new FakePolicy();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero));
        var prop = new FakeProperty(new DateOnly(2026, 10, 24));
        var cat = new ReportCatalogue([new DemoSelector(), new DemoSelector(def: PlainDef)]);
        var m = new MemoryStores();
        var s = new ReportService(new ReportRunner(cat, p, prop, [new FixedCodeEngine()], clock), cat, [new XlsxRenderer(), new PdfRenderer(), new CsvRenderer(), new PrintHtmlRenderer()],
            prop, m, m, m, m, m, p, new ReferenceOnlyFileStore(), new FakeDelivery(), clock, m);
        return (s, m, p);
    }

    [Fact]
    public async Task AF2_DEC061_housekeeping_never_gets_a_money_report_or_column_even_with_a_mis_seeded_grant()
    {
        var (s, m, p) = HubRig();
        foreach (var perm in new[] { "PERM-RPT-001", "PERM-RPT-002", "PERM-RPT-003" }) p.Grants[perm].Add("HKP"); // mis-seeded: view, money gate, export
        var owner = await s.CatalogueAsync(Rig.C(1), Ct);
        Assert.Equal(["rpt.admin.engine-test", "rpt.admin.plain-test"], owner.Select(e => e.Definition.Id));
        Assert.True(owner[0].Money);
        var hk = await s.CatalogueAsync(Rig.C(7), Ct);
        Assert.Equal(["rpt.admin.plain-test"], hk.Select(e => e.Definition.Id));
        Assert.False(hk[0].Money);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.RunAsync(Rig.C(7), "rpt.admin.engine-test", Rig.NoParams, null, 100, Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ExportAsync(Rig.C(7), "rpt.admin.engine-test", "csv", Rig.NoParams, Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.PrintLogAsync(Rig.C(7), "rpt.admin.engine-test", new PrintLogRequest(null, 1), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.SetFavouriteAsync(Rig.C(7), "rpt.admin.engine-test", true, Ct))).Code);
        // The plain report runs, but its money column and the computed column over it are removed (not blanked), for screen and file alike.
        var r = await s.RunAsync(Rig.C(7), "rpt.admin.plain-test", Rig.NoParams, null, 100, true, Ct);
        Assert.DoesNotContain(r.Columns, c => c.Key is "amt" or "avg");
        Assert.All(r.Rows, row => Assert.False(row.Cells.ContainsKey("amt")));
        Assert.Equal(["amt", "avg"], r.Removed!.Order(StringComparer.Ordinal));
        var f = await s.ExportAsync(Rig.C(7), "rpt.admin.plain-test", "csv", Rig.NoParams, Ct);
        var csv = System.Text.Encoding.UTF8.GetString(f.Bytes);
        Assert.Contains("Nights", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Amount", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Per night", csv, StringComparison.Ordinal);
        Assert.Equal(7, m.Log.Single().PrincipalId);
        // The Owner keeps the money column on the same report.
        Assert.Contains((await s.RunAsync(Rig.C(1), "rpt.admin.plain-test", Rig.NoParams, null, 100, true, Ct)).Columns, c => c.Key == "amt");
    }

    [Fact]
    public async Task AF2_all_rows_print_log_file_formats_need_export_and_export_log_is_own_rows_unless_owner_manager_accountant()
    {
        var (s, m, _) = HubRig();
        var all = await s.RunAsync(Rig.C(1), "rpt.admin.engine-test", Rig.NoParams, null, 100, true, Ct);
        Assert.Equal((0, 2), (all.PageSize, all.Rows.Count));
        Assert.Empty(m.Log); // a screen run writes nothing
        // A device-built Excel file logged by the Accountant (view + export); a Manager denied export may log a print but not a file.
        await s.PrintLogAsync(Rig.C(5), "rpt.admin.engine-test", new PrintLogRequest(null, 2, false, "xlsx"), Ct);
        var (s2, m2, p2) = HubRig();
        p2.Denied.Add((2, "PERM-RPT-003"));
        await s2.PrintLogAsync(Rig.C(2), "rpt.admin.engine-test", new PrintLogRequest(null, 2), Ct); // print: view is enough
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s2.PrintLogAsync(Rig.C(2), "rpt.admin.engine-test", new PrintLogRequest(null, 2, false, "xlsx"), Ct))).Code);
        Assert.Single(m2.Log);
        await s.ExportAsync(Rig.C(2), "rpt.admin.engine-test", "csv", Rig.NoParams, Ct);
        await s.PrintLogAsync(Rig.C(1), "rpt.admin.engine-test", new PrintLogRequest(null, 2), Ct);
        Assert.Equal(3, (await s.ExportLogAsync(Rig.C(1), 200, Ct)).Count);
        Assert.Equal([ReportFormat.Print, ReportFormat.Csv, ReportFormat.Xlsx], (await s.ExportLogAsync(Rig.C(5), 200, Ct)).Select(e => e.Format)); // accountant: all, newest first
        var mine = await s.ExportLogAsync(Rig.C(2), 200, Ct); // Manager: all as well
        Assert.Equal(3, mine.Count);
        Assert.Empty(await s.ExportLogAsync(Rig.C(3), 200, Ct)); // Reception: only its own rows (none)
    }
}
