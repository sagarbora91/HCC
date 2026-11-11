// FX-RPT-C · rpt.money.closing-records on a disposable HccTest_* database: the current closing record of each closed date of the financial year, newest first; a record replaced by
// a re-close is history and not listed; cash difference = counted - expected (0 without a count); corrections = reopens of the date; PERM-CLS-001 + money gate PERM-RPT-002;
// housekeeping refused at the hub (DEC-061).
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ClosingRecordsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    // 21 Oct closed by OWN (no count stored), 22 Oct closed twice (first record superseded by the re-close after one reopen), 23 Oct closed by MGR. FY 2025-26 holds 31 Mar 2026.
    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            var (p, own, mgr) = (db.PropertyId, db.Users["OWN"], db.Users["MGR"]);
            await db.ScalarAsync($"""
                DECLARE @p INT = {p}, @h BINARY(32) = HASHBYTES('SHA2_256', 'x');
                INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc) VALUES
                  (@p, '2026-03-31', 'r0', 'r0', 'Completed', 'done', 1, {mgr}, '2026-03-31 17:00', {mgr}, '2026-03-31 17:00'),
                  (@p, '2026-10-21', 'r21', 'r21', 'Completed', 'done', 1, {own}, '2026-10-21 17:00', {own}, '2026-10-21 17:00'),
                  (@p, '2026-10-22', 'r22', 'r22', 'Completed', 'done', 1, {mgr}, '2026-10-22 17:00', {mgr}, '2026-10-22 17:00'),
                  (@p, '2026-10-23', 'r23', 'r23', 'Completed', 'done', 1, {mgr}, '2026-10-23 17:00', {mgr}, '2026-10-23 17:00');
                INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc)
                  SELECT property_id, business_date, run_id, {mgr}, '2026-10-22 17:30' FROM cls.close_run WHERE property_id = @p;
                INSERT cls.closing_snapshot(property_id, business_date, run_id, closed_by, closed_utc, net_revenue_paise, tax_paise, net_receipts_paise, cash_expected_paise, cash_counted_paise,
                    occupied_room_nights, sellable_room_nights, record_json, record_sha256)
                  SELECT @p, d.business_date, d.run_id, d.closed_by, d.closed_utc, v.rev, 0, v.rec, v.exp, v.cnt, 1, 10, N'[]', @h
                    FROM cls.business_day d JOIN (VALUES ('2026-03-31', 100000, 90000, 50000, 50000), ('2026-10-21', 800000, 700000, 300000, NULL), ('2026-10-22', 100000, 100000, 100000, 100000),
                         ('2026-10-23', 1200000, 1100000, 1245000, 1245000)) v(dt, rev, rec, exp, cnt) ON CAST(v.dt AS DATE) = d.business_date WHERE d.property_id = @p;
                DECLARE @s22 BIGINT = (SELECT snapshot_id FROM cls.closing_snapshot WHERE property_id = @p AND business_date = '2026-10-22');
                INSERT cls.day_reopen(property_id, business_date, reopened_by, reopened_utc, reason, prior_snapshot_id, cash_book_locked, tally_reexport) VALUES (@p, '2026-10-22', {own}, '2026-10-23 03:00', N'Correct a receipt', @s22, 0, 0);
                DECLARE @ro BIGINT = SCOPE_IDENTITY();
                UPDATE cls.closing_snapshot SET superseded_utc = '2026-10-23 04:00', superseded_reopen_id = @ro WHERE snapshot_id = @s22;
                INSERT cls.closing_snapshot(property_id, business_date, run_id, closed_by, closed_utc, net_revenue_paise, tax_paise, net_receipts_paise, cash_expected_paise, cash_counted_paise,
                    occupied_room_nights, sellable_room_nights, record_json, record_sha256, reopen_id)
                  SELECT @p, business_date, run_id, {own}, '2026-10-23 04:00', 1950000, 0, 1800000, 1400000, 1395000, 1, 10, N'[]', @h, @ro FROM cls.closing_snapshot WHERE snapshot_id = @s22;
                UPDATE cls.day_reopen SET reclosed_by = {own}, reclosed_utc = '2026-10-23 04:00', snapshot_id = SCOPE_IDENTITY() WHERE reopen_id = @ro;
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Hcc.Reporting.ReportRunner Runner() => CuBRig.Runner(db, new ClosingRecordsReport(db.Platform));

    [Fact]
    public async Task Current_records_newest_first_with_difference_and_corrections_to_the_paise()
    {
        var r = await Run(db, Runner(), "OWN", ClosingRecordsReport.Id, ("fy", "2026-27"));
        Assert.Equal(["2026-10-23", "2026-10-22", "2026-10-21"], r.Rows.Select(x => S(x, "date")));
        Assert.Equal(["MGR", "OWN", "MGR"], r.Rows.Select(x => S(x, "closedBy")));
        Assert.Equal([1200000L, 1950000L, 800000L], r.Rows.Select(x => L(x, "netRevenue")));
        Assert.Equal([1100000L, 1800000L, 700000L], r.Rows.Select(x => L(x, "netReceipts")));
        Assert.Equal([0L, -5000L, 0L], r.Rows.Select(x => L(x, "cashDifference"))); // 22 Oct: 1,395,000 - 1,400,000; 21 Oct: no count stored
        Assert.Equal([0L, 1L, 0L], r.Rows.Select(x => L(x, "corrections")));
        Assert.Equal("2026-10-23T09:30", S(r.Rows[1], "closedAt")); // 04:00 UTC = 09:30 hotel time
        Assert.DoesNotContain(r.Rows, x => x.Kind != ReportRowKind.Row);
    }

    [Fact]
    public async Task The_financial_year_picks_its_own_dates()
    {
        var r = await Run(db, Runner(), "ACC", ClosingRecordsReport.Id, ("fy", "2025-26"));
        var row = Assert.Single(r.Rows);
        Assert.Equal(("2026-03-31", 100000L, 90000L), (S(row, "date"), L(row, "netRevenue"), L(row, "netReceipts")));
    }

    [Fact]
    public async Task Owner_manager_accountant_may_run_it_everyone_else_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ClosingRecordsReport.Id, ("fy", "2026-27"))));
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, ClosingRecordsReport.Id, ("fy", "2026-27"))));
        var svc = Service(db, new ClosingRecordsReport(db.Platform));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users["HKP"]), ClosingRecordsReport.Id, new Dictionary<string, string?>(), null, 50, true, Ct)));
    }
}
