// CU-RPT-A · rpt.admin.audit-log on the server: SQL-backed rows, order, hotel-local time, ids, filters, detail rules (Owner / Manager / Accountant only), DEC-061 money mask,
// permission (PERM-ADM-004 refused for Cashier and Housekeeping), screen = Excel = PDF = print. Disposable HccTest_* database. Fictional data.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

[Trait("Category", "Sql")]
public sealed class AuditLogReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakePolicy _policy = CuARig.Policy();

    public ValueTask InitializeAsync() => new(CuARig.SeedOnce(db, "audit", async () =>
    {
        var (p, own, mgr) = (db.PropertyId, db.Users["OWN"], db.Users["MGR"]);
        // Hotel-local time = UTC + 05:30: A 10:00 on 23 Oct, B 23:50 on 23 Oct, C 01:00 on 24 Oct (still 23 Oct in UTC), E 08:00 on 24 Oct (System), D outside the range.
        await db.ScalarAsync($"""
            INSERT plat.audit_event(property_id, principal_id, action, target, detail, at_utc) VALUES
              ({p}, {own}, 'DAY_CLOSE', 'cls:2026-10-22', N'Day closed · net revenue ₹19,500 · cash counted exactly', '2026-10-23 04:30'),
              ({p}, {mgr}, 'DISCOUNT_APPROVE', 'bill:77', N'Discount ₹400 approved · HCC-1041', '2026-10-23 18:20'),
              ({p}, {mgr}, 'ROOM_BLOCK', 'room', N'Room 105 blocked for repair', '2026-10-23 19:30'),
              ({p}, 0, 'SCENARIO', 'sys', N'Scenario loaded FY26', '2026-10-24 02:30'),
              ({p}, {own}, 'DAY_CLOSE', 'cls:2026-10-19', N'Day closed · old', '2026-10-20 04:30');
            SELECT 1;
            """);
    }));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<ReportResult> Run(int principal, params (string K, string V)[] extra) =>
        CuARig.Run(db, _policy, principal, AuditLogReport.Id, [("from", "2026-10-23"), ("to", "2026-10-24"), .. extra]);

    [Fact]
    public async Task Owner_sees_the_range_newest_first_in_hotel_time_with_ids_and_the_whole_detail()
    {
        var d = CuARig.Data(await Run(db.Users["OWN"]));
        Assert.Equal(["2026-10-24 08:00", "2026-10-24 01:00", "2026-10-23 23:50", "2026-10-23 10:00"], d.Select(r => CuARig.S(r, "at")));
        Assert.Equal(["System", "MGR", "MGR", "OWN"], d.Select(r => CuARig.S(r, "actor")));
        Assert.Equal(["SCENARIO", "ROOM_BLOCK", "DISCOUNT_APPROVE", "DAY_CLOSE"], d.Select(r => CuARig.S(r, "action")));
        var sysId = Convert.ToString(await db.ScalarAsync($"SELECT audit_id FROM plat.audit_event WHERE property_id = {db.PropertyId} AND target = 'sys';"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal([sysId, "105", "77", "2026-10-22"], d.Select(r => CuARig.S(r, "id")));
        Assert.Equal("Day closed · net revenue ₹19,500 · cash counted exactly", CuARig.S(d[3], "detail"));
        Assert.Equal("Discount ₹400 approved · HCC-1041", CuARig.S(d[2], "detail"));
    }

    [Fact]
    public async Task Range_is_by_hotel_local_date_and_other_rows_stay_out()
    {
        var d = CuARig.Data(await CuARig.Run(db, _policy, db.Users["OWN"], AuditLogReport.Id, ("from", "2026-10-20"), ("to", "2026-10-20")));
        var only = Assert.Single(d);
        Assert.Equal("Day closed · old", CuARig.S(only, "detail"));
        Assert.Empty(CuARig.Data(await CuARig.Run(db, _policy, db.Users["OWN"], AuditLogReport.Id, ("from", "2026-10-21"), ("to", "2026-10-22"))));
    }

    [Fact]
    public async Task Filters_actor_action_and_text()
    {
        Assert.Equal(["ROOM_BLOCK", "DISCOUNT_APPROVE"], CuARig.Data(await Run(db.Users["OWN"], ("actor", "MGR"))).Select(r => CuARig.S(r, "action")));
        Assert.Equal(["DAY_CLOSE"], CuARig.Data(await Run(db.Users["OWN"], ("action", "DAY_CLOSE"))).Select(r => CuARig.S(r, "action")));
        Assert.Equal(["DISCOUNT_APPROVE"], CuARig.Data(await Run(db.Users["OWN"], ("text", "approved"))).Select(r => CuARig.S(r, "action")));
        Assert.Equal(["SCENARIO"], CuARig.Data(await Run(db.Users["OWN"], ("actor", "System"))).Select(r => CuARig.S(r, "action")));
    }

    [Fact]
    public async Task Money_in_a_detail_is_masked_for_a_reader_without_the_finance_right()
    {
        var mgr = db.Users["MGR"];
        _policy.Denied.Add((mgr, "PERM-RPT-002"));
        var d = CuARig.Data(await Run(mgr));
        Assert.Equal("Discount ₹ •• approved · HCC-1041", CuARig.S(d[2], "detail"));
        Assert.Equal("Day closed · net revenue ₹ •• · cash counted exactly", CuARig.S(d[3], "detail"));
    }

    [Fact]
    public async Task A_reader_outside_owner_manager_accountant_gets_no_detail_and_cannot_search_inside_it()
    {
        var rec = db.Users["REC"];
        var d = CuARig.Data(await Run(rec));
        Assert.Equal(4, d.Count);
        Assert.All(d, r => Assert.Null(r.Cells["detail"]));
        Assert.Empty(CuARig.Data(await Run(rec, ("text", "approved")))); // the word sits only in a detail
        Assert.Single(CuARig.Data(await Run(rec, ("text", "DISCOUNT"))));    // the action name is visible
        Assert.Equal(4, CuARig.Data(await Run(db.Users["ACC"])).Count(r => r.Cells["detail"] is string));
    }

    [Fact]
    public async Task Cashier_and_housekeeping_are_refused()
    {
        foreach (var who in new[] { db.Users["CSH"], 7 })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => Run(who))).Code);
    }

    [Fact]
    public async Task The_hub_lists_the_audit_log_only_for_roles_that_hold_the_view_right()
    {
        var s = CuARig.Service(db, _policy);
        Assert.Contains((await s.CatalogueAsync(Rig.C(db.Users["OWN"]), Ct)), e => e.Definition.Id == AuditLogReport.Id);
        Assert.DoesNotContain((await s.CatalogueAsync(Rig.C(db.Users["CSH"]), Ct)), e => e.Definition.Id == AuditLogReport.Id);
    }

    [Fact]
    public void Id_falls_back_from_the_target_to_the_detail_to_the_row_number()
    {
        Assert.Equal("12", CuASql.AuditId(new AuditFact(9, "", "", "", "user:12", "x HCC-1")));
        Assert.Equal("HCC-1041", CuASql.AuditId(new AuditFact(9, "", "", "", "bill", "Bill of HCC-1041")));
        Assert.Equal("gst.room.lowBps", CuASql.AuditId(new AuditFact(9, "", "", "", "setting", "Setting gst.room.lowBps 5 → 12")));
        Assert.Equal("9", CuASql.AuditId(new AuditFact(9, "", "", "", "sys", "Scenario loaded")));
    }
}
