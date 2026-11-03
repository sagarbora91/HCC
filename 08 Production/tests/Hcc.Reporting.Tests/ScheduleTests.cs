// F7-R1 · scheduled runs: due rules, relative tokens, run as the owner (never wider), money recipients, delivery held while the internet is off,
// print / export log rows.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Reporting.Tests;

public sealed class MemoryStores : IReportScheduleStore, IReportRunStore, IReportExportLog, IReportFavourites, IReportPrincipalRoles
{
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
        var s = new ReportService(runner, cat, [new XlsxRenderer(), new PdfRenderer(), new CsvRenderer(), new PrintHtmlRenderer()], prop, m, m, m, m, m, p, new ReferenceOnlyFileStore(), d, clock);
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
    }
}
