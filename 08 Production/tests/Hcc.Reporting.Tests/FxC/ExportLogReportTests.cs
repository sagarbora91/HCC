// FX-RPT-C · rpt.admin.export-log on a disposable HccTest_* database: rows from rpt.export_log, hotel-local time (UTC + 05:30), newest first, report title lookup (the id when the
// report is not registered), parameter text, report and user filters, Owner / Manager only (Accountant refused although the permission opens the entry), others refused.
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Sql;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ExportLogReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    private sealed class Sp(IReportCatalogue cat) : IServiceProvider
    {
        public object? GetService(Type t) => t == typeof(IReportCatalogue) ? cat : null;
    }

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            var (p, own, mgr) = (db.PropertyId, db.Users["OWN"], db.Users["MGR"]);
            // L1 OWN 10:00 local 23 Oct (dictionary, xlsx, 12 rows); L2 MGR 00:30 local 24 Oct (the log itself, print, 5 rows); L3 OWN 23 Oct 12:00 local, a report no longer registered;
            // L4 OWN 20 Oct (outside the range).
            await db.ScalarAsync($$"""
                DECLARE @h CHAR(64) = REPLICATE('a', 64);
                INSERT rpt.export_log(property_id, at_utc, principal_id, report_id, params_hash, params, format, row_count, file_name, provisional) VALUES
                  ({{p}}, '2026-10-23 04:30', {{own}}, 'rpt.admin.report-dictionary', @h, N'{"from":"2026-10-22","to":"2026-10-22","legacy":"false"}', 'xlsx', 12, N'a.xlsx', 0),
                  ({{p}}, '2026-10-23 19:00', {{mgr}}, 'rpt.admin.export-log', @h, N'{"from":"2026-10-23","to":"2026-10-24"}', 'print', 5, NULL, 0),
                  ({{p}}, '2026-10-23 06:30', {{own}}, 'rpt.gone.unknown', @h, N'{}', 'pdf', 0, N'c.pdf', 0),
                  ({{p}}, '2026-10-20 04:30', {{own}}, 'rpt.admin.report-dictionary', @h, N'{}', 'csv', 12, N'd.csv', 0);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner()
    {
        var log = new ExportLogReport(db.Platform, new SqlReportPrincipalRoles(db.Platform.Connections, new FixedClock(Rig.Now)), null!);
        var dict = new ReportDictionaryReport();
        var holder = new Sp(new ReportCatalogue([log, dict]));
        var withCatalogue = new ExportLogReport(db.Platform, new SqlReportPrincipalRoles(db.Platform.Connections, new FixedClock(Rig.Now)), holder);
        return CuBRig.Runner(db, withCatalogue, dict);
    }

    [Fact]
    public async Task Rows_are_newest_first_in_hotel_time_with_titles_and_parameter_text()
    {
        var r = await Run(db, Runner(), "OWN", ExportLogReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"));
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(["2026-10-24 00:30", "2026-10-23 12:00", "2026-10-23 10:00"], r.Rows.Select(x => S(x, "at")));
        Assert.Equal(["MGR", "OWN", "OWN"], r.Rows.Select(x => S(x, "user")));
        Assert.Equal(["Export and print log", "rpt.gone.unknown", "Report dictionary"], r.Rows.Select(x => S(x, "report")));
        Assert.Equal(["print", "pdf", "xlsx"], r.Rows.Select(x => S(x, "format")));
        Assert.Equal([5L, 0L, 12L], r.Rows.Select(x => L(x, "rows")));
        Assert.Equal("from=2026-10-23, to=2026-10-24", S(r.Rows[0], "params"));
        Assert.Null(r.Rows[1].Cells["params"]);
        Assert.Equal("from=2026-10-22, to=2026-10-22", S(r.Rows[2], "params")); // "legacy" is not shown
    }

    [Fact]
    public async Task Report_and_user_filters_narrow_the_rows()
    {
        var byReport = await Run(db, Runner(), "MGR", ExportLogReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("report", "DICTION"));
        Assert.Equal([12L, 12L], byReport.Rows.Select(x => L(x, "rows")));
        var byId = await Run(db, Runner(), "OWN", ExportLogReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("report", "rpt.gone"));
        Assert.Single(byId.Rows);
        var byUser = await Run(db, Runner(), "OWN", ExportLogReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("user", db.Users["MGR"].ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("MGR", S(Assert.Single(byUser.Rows), "user"));
        var nobody = await Run(db, Runner(), "OWN", ExportLogReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("user", "9999"));
        Assert.Empty(nobody.Rows);
    }

    [Fact]
    public async Task Only_owner_and_manager_may_run_it()
    {
        var runner = Runner();
        foreach (var role in new[] { "ACC", "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ExportLogReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"))));
        foreach (var role in new[] { "OWN", "MGR" })
            Assert.Null(await Code(() => Run(db, runner, role, ExportLogReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"))));
    }
}
