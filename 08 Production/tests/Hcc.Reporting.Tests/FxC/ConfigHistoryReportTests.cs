// FX-RPT-C · rpt.admin.config-history on a disposable HccTest_* database: rows from cfg.setting_change in hotel time, newest first, range by the day of the change, effective date,
// DEC-061 money mask for a reader without PERM-RPT-002 (the Tech admin), PERM-CFG-001 (Owner, Tech admin) only.
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ConfigHistoryReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await AddUser(db, "TAD");
            var (p, own, tad) = (db.PropertyId, db.Users["OWN"], db.Users["TAD"]);
            await db.ScalarAsync($"""
                DECLARE @p INT = {p};
                INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES (@p, 'money.roomCap', '2026-10-01', '2026-10-24'), (@p, 'frontdesk.checkout', '2026-10-01', '2026-10-24');
                INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, reason, source, changed_by, changed_utc) VALUES
                  (@p, 'money.roomCap', '2026-10-24', N'750000', N'700000', N'₹7,000', N'₹7,500', N'Owner review', 'change', {own}, '2026-10-23 04:30'),
                  (@p, 'frontdesk.checkout', '2026-10-25', N'"11:00"', N'"12:00"', N'12:00', N'11:00', NULL, 'change', {tad}, '2026-10-23 19:00'),
                  (@p, 'money.roomCap', '2026-10-01', N'700000', N'650000', N'₹6,500', N'₹7,000', N'Old', 'change', {own}, '2026-10-20 04:30');
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Hcc.Reporting.ReportRunner Runner() =>
        CuBRig.Runner(db, new ConfigHistoryReport(db.Platform, new SqlAccessPolicy(db.Platform.Connections, new FixedClock(Rig.Now), [])));

    [Fact]
    public async Task Owner_sees_the_changes_newest_first_in_hotel_time()
    {
        var r = await Run(db, Runner(), "OWN", ConfigHistoryReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"));
        Assert.Equal(["2026-10-24 00:30", "2026-10-23 10:00"], r.Rows.Select(x => S(x, "at")));
        Assert.Equal(["TAD", "OWN"], r.Rows.Select(x => S(x, "actor")));
        Assert.Equal(["frontdesk.checkout", "money.roomCap"], r.Rows.Select(x => S(x, "key")));
        Assert.Equal(("₹7,000", "₹7,500", "2026-10-24", "Owner review"), (S(r.Rows[1], "before"), S(r.Rows[1], "after"), S(r.Rows[1], "effective"), S(r.Rows[1], "reason")));
        Assert.Null(r.Rows[0].Cells["reason"]);
    }

    [Fact]
    public async Task A_reader_without_the_finance_right_gets_rupee_amounts_masked()
    {
        var r = await Run(db, Runner(), "TAD", ConfigHistoryReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"));
        Assert.Equal(("₹ ••", "₹ ••"), (S(r.Rows[1], "before"), S(r.Rows[1], "after")));
        Assert.Equal(("12:00", "11:00"), (S(r.Rows[0], "before"), S(r.Rows[0], "after")));
    }

    [Fact]
    public async Task The_range_follows_the_day_of_the_change()
    {
        var r = await Run(db, Runner(), "OWN", ConfigHistoryReport.Id, ("from", "2026-10-20"), ("to", "2026-10-20"));
        Assert.Equal("₹6,500", S(Assert.Single(r.Rows), "before"));
        Assert.Empty((await Run(db, Runner(), "OWN", ConfigHistoryReport.Id, ("from", "2026-10-21"), ("to", "2026-10-22"))).Rows);
    }

    [Fact]
    public async Task Only_owner_and_tech_admin_may_run_it()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC", "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ConfigHistoryReport.Id)));
        foreach (var role in new[] { "OWN", "TAD" })
            Assert.Null(await Code(() => Run(db, runner, role, ConfigHistoryReport.Id)));
    }
}
