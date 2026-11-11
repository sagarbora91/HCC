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
            await db.ScalarAsync($$"""
                DECLARE @p INT = {{p}};
                INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES (@p, 'money.roomCap', '2026-10-01', '2026-10-24'), (@p, 'frontdesk.checkout', '2026-10-01', '2026-10-24');
                INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, reason, source, changed_by, changed_utc) VALUES
                  (@p, 'money.roomCap', '2026-10-24', N'750000', N'700000', N'₹7,000', N'₹7,500', N'Owner review', 'change', {{own}}, '2026-10-23 04:30'),
                  (@p, 'frontdesk.checkout', '2026-10-25', N'"11:00"', N'"12:00"', N'12:00', N'11:00', NULL, 'change', {{tad}}, '2026-10-23 19:00'),
                  (@p, 'money.roomCap', '2026-10-01', N'700000', N'650000', N'₹6,500', N'₹7,000', N'Old', 'change', {{own}}, '2026-10-20 04:30');
                INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, reason, source, changed_by, changed_utc, withdrawn_utc, withdrawn_by) VALUES
                  (@p, 'frontdesk.checkout', '2026-10-26', N'"10:00"', N'"11:00"', N'11:00', N'10:00', N'Trial', 'change', {{own}}, '2026-10-23 07:00', '2026-10-23 08:00', {{own}});
                -- GST master: a seed version in range (statutory, never listed), an Owner change 18% -> 20% and an Owner food-rate change that was withdrawn.
                INSERT tax.gst_setting_version(property_id, setting_key, effective_from, value, source, status, reason, changed_by, changed_utc, withdrawn_by, withdrawn_utc) VALUES
                  (NULL, 'gst.food.bps', '2026-10-22', 500, 'seed', 'active', NULL, NULL, '2026-10-23 04:45', NULL, NULL),
                  (@p, 'gst.room.highBps', '2026-10-25', 2000, 'owner', 'active', N'Budget review', {{own}}, '2026-10-23 05:00', NULL, NULL),
                  (@p, 'gst.food.bps', '2026-10-26', 1800, 'owner', 'withdrawn', N'Mistake', {{own}}, '2026-10-23 06:00', {{own}}, '2026-10-23 06:30');
                -- another property: its setting change and GST version must never show
                DECLARE @p2 INT = (SELECT TOP (1) property_id FROM (SELECT property_id FROM inv.property WHERE code = 'ZZ2') x);
                IF @p2 IS NULL BEGIN INSERT inv.property(code, name, time_zone, business_date) VALUES ('ZZ2', N'Other Hotel', 'Asia/Kolkata', '2026-10-24'); SET @p2 = SCOPE_IDENTITY(); END
                INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES (@p2, 'money.roomCap', '2026-10-01', '2026-10-24');
                INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, reason, source, changed_by, changed_utc) VALUES
                  (@p2, 'money.roomCap', '2026-10-24', N'1', N'0', N'other-before', N'other-after', NULL, 'change', {{own}}, '2026-10-23 05:10');
                INSERT tax.gst_setting_version(property_id, setting_key, effective_from, value, source, status, reason, changed_by, changed_utc) VALUES
                  (@p2, 'gst.room.highBps', '2026-10-25', 2800, 'owner', 'active', N'Other', {{own}}, '2026-10-23 05:20');
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
        Assert.Equal(["2026-10-24 00:30", "2026-10-23 12:30", "2026-10-23 11:30", "2026-10-23 10:30", "2026-10-23 10:00"], r.Rows.Select(x => S(x, "at")));
        Assert.Equal(["TAD", "OWN", "OWN", "OWN", "OWN"], r.Rows.Select(x => S(x, "actor")));
        Assert.Equal(["frontdesk.checkout", "frontdesk.checkout", "gst.food.bps", "gst.room.highBps", "money.roomCap"], r.Rows.Select(x => S(x, "key")));
        var cap = r.Rows[4];
        Assert.Equal(("₹7,000", "₹7,500", "2026-10-24", "Owner review"), (S(cap, "before"), S(cap, "after"), S(cap, "effective"), S(cap, "reason")));
        Assert.Null(r.Rows[0].Cells["reason"]);
        // a setting change that was withdrawn is marked
        Assert.Equal(("11:00", "10:00 (withdrawn)"), (S(r.Rows[1], "before"), S(r.Rows[1], "after")));
        // the Owner's GST versions, text as the client builds it: seed versions are not listed; before = the version in force before; withdrawn marker
        Assert.Equal(("5%", "18% (withdrawn)", "2026-10-26", "Mistake"), (S(r.Rows[2], "before"), S(r.Rows[2], "after"), S(r.Rows[2], "effective"), S(r.Rows[2], "reason")));
        Assert.Equal(("18%", "20%", "2026-10-25", "Budget review"), (S(r.Rows[3], "before"), S(r.Rows[3], "after"), S(r.Rows[3], "effective"), S(r.Rows[3], "reason")));
        Assert.DoesNotContain(r.Rows, x => S(x, "after").Contains("other-after", StringComparison.Ordinal) || S(x, "after") == "28%"); // another property never leaks in
    }

    [Fact]
    public async Task A_reader_without_the_finance_right_gets_rupee_amounts_masked()
    {
        var r = await Run(db, Runner(), "TAD", ConfigHistoryReport.Id, ("from", "2026-10-23"), ("to", "2026-10-24"));
        Assert.Equal(("₹ ••", "₹ ••"), (S(r.Rows[4], "before"), S(r.Rows[4], "after")));
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
    public void Gst_values_are_shown_like_the_client_formats_them()
    {
        Assert.Equal(["18%", "12.5%", "₹7,500", "₹1,23,456.50", "Available", "Not available", "Declared (opt-in)", "Not declared"],
            [ConfigHistoryReport.GstShow("gst.room.highBps", "percent", 1800), ConfigHistoryReport.GstShow("gst.x", "percent", 1250), ConfigHistoryReport.GstShow("gst.room.cap", "money", 750000),
             ConfigHistoryReport.GstShow("gst.room.cap", "money", 12345650), ConfigHistoryReport.GstShow("gst.room.lowItc", "boolean", 1), ConfigHistoryReport.GstShow("gst.room.lowItc", "boolean", 0),
             ConfigHistoryReport.GstShow("gst.food.optIn", "boolean", 1), ConfigHistoryReport.GstShow("gst.food.optIn", "boolean", 0)]);
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
