using Hcc.Contracts.GuestServices;
using Hcc.GuestServices.Tests.Harness;

namespace Hcc.GuestServices.Tests.Sql;

[Trait("Category", "Sql")]
public class RequestsSqlTests : IAsyncLifetime
{
    private GsWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await GsWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<ServiceRequestResult> Log(int who, string key, string stay, string type, string? detail = null, string? dept = null, string? priority = null, string? time = null, int? assignee = null) =>
        (await _w.Commands.LogRequestAsync(_w.Ctx(who, key), new LogServiceRequestRequest(_w.Stays[stay], type, detail, dept, priority, time, assignee), Ct)).Value;

    [Fact]
    public async Task Log_sets_the_due_time_from_the_SLA_and_urgent_halves_it()
    {
        var r = await Log(Who.Reception, "k1", "HCC-1043", "Extra towels", "2 towels");
        Assert.Equal(("Open", "Housekeeping", "Normal"), (r.Status, r.Dept, r.Priority));
        Assert.Equal("2026-10-24T08:20:00.000Z", r.DueUtc);   // created 08:00 + 20 min
        Assert.Equal("104", await _w.Db.StringAsync($"SELECT rm.room_no FROM ops.guest_request q JOIN inv.room rm ON rm.room_id = q.room_id WHERE q.request_id = {r.RequestId};", Ct));
        var u = await Log(Who.Reception, "k2", "HCC-1043", "Taxi", priority: "Urgent");
        Assert.Equal(("Front desk", "2026-10-24T08:15:00.000Z"), (u.Dept, u.DueUtc));
        Assert.Equal(2, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.guest_service_log WHERE kind = 'request' AND what = 'Logged';", Ct));
    }

    [Fact]
    public async Task Log_validates_stay_type_time_permission_and_assignee()
    {
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => Log(Who.Reception, "a", "HCC-1042", "Extra towels")));   // departed
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.LogRequestAsync(_w.Ctx(Who.Reception, "b"), new LogServiceRequestRequest(999999, "Extra towels", null, null, null, null, null), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Log(Who.Reception, "c", "HCC-1041", "Spa")));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Log(Who.Reception, "d", "HCC-1041", "Wake-up call", time: "7am")));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Log(Who.Reception, "e", "HCC-1041", "Other")));   // needs a description
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Log(Who.Housekeeper, "f", "HCC-1041", "Extra towels")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Log(Who.Cashier, "g", "HCC-1041", "Extra towels")));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Log(Who.Reception, "h", "HCC-1041", "Extra towels", assignee: Who.Maint)));   // maintenance cannot take housekeeping
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.guest_request;", Ct));
    }

    [Fact]
    public async Task Wake_up_call_already_past_today_is_scheduled_for_tomorrow_in_property_time()
    {
        // 08:00Z = 13:30 IST on 2026-10-24; 06:00 IST has passed -> 2026-10-25 06:00 IST = 00:30Z; due 5 min later.
        var w = await Log(Who.Reception, "w1", "HCC-1041", "Wake-up call", time: "06:00");
        Assert.Equal(("2026-10-25T00:30:00.000Z", "2026-10-25T00:35:00.000Z"), (w.ScheduledUtc, w.DueUtc));
        // 18:00 IST is still ahead today = 12:30Z.
        var later = await Log(Who.Reception, "w2", "HCC-1041", "Wake-up call", time: "18:00");
        Assert.Equal("2026-10-24T12:30:00.000Z", later.ScheduledUtc);
    }

    [Fact]
    public async Task Housekeeper_works_only_the_own_assignment_and_completion_needs_a_guest_note()
    {
        var r = await Log(Who.Reception, "s1", "HCC-1045", "Drinking water", "4 bottles", assignee: Who.Housekeeper);
        Assert.Equal(("Assigned", Who.Housekeeper), (r.Status, r.AssigneeId));
        var v = r.Version;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.StartRequestAsync(_w.Ctx(Who.Housekeeper2, "x1"), new StartServiceRequestRequest(r.RequestId, v), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.StartRequestAsync(_w.Ctx(Who.Maint, "x2"), new StartServiceRequestRequest(r.RequestId, v), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.StartRequestAsync(_w.Ctx(Who.Accountant, "x3"), new StartServiceRequestRequest(r.RequestId, v), Ct)));
        var started = (await _w.Commands.StartRequestAsync(_w.Ctx(Who.Housekeeper, "x4"), new StartServiceRequestRequest(r.RequestId, v), Ct)).Value;
        Assert.Equal("InProgress", started.Status);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.CompleteRequestAsync(_w.Ctx(Who.Housekeeper, "x5"), new CompleteServiceRequestRequest(r.RequestId, "", started.Version), Ct)));
        _w.Clock.Now = _w.Clock.Now.AddMinutes(7);
        var done = await _w.Commands.CompleteRequestAsync(_w.Ctx(Who.Housekeeper, "x6"), new CompleteServiceRequestRequest(r.RequestId, "Four bottles delivered.", started.Version), Ct);
        Assert.Equal("Done", done.Value.Status);
        var replay = await _w.Commands.CompleteRequestAsync(_w.Ctx(Who.Housekeeper, "x6"), new CompleteServiceRequestRequest(r.RequestId, "Four bottles delivered.", started.Version), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.CompleteRequestAsync(_w.Ctx(Who.Housekeeper, "x7"), new CompleteServiceRequestRequest(r.RequestId, "again", done.Value.Version), Ct)));
        Assert.Equal("Four bottles delivered.", await _w.Db.StringAsync($"SELECT guest_note FROM ops.guest_request WHERE request_id = {r.RequestId};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ServiceRequestDone';", Ct));
    }

    [Fact]
    public async Task Stale_version_is_refused()
    {
        var r = await Log(Who.Reception, "v1", "HCC-1044", "Extra towels");
        var old = r.Version;
        await _w.Commands.AssignRequestAsync(_w.Ctx(Who.Supervisor, "v2"), new AssignServiceRequestRequest(r.RequestId, Who.Housekeeper2, null, old), Ct);
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.CancelRequestAsync(_w.Ctx(Who.Reception, "v3"), new CancelServiceRequestRequest(r.RequestId, "Guest found towels", old), Ct)));
    }

    [Fact]
    public async Task Assignment_pools_by_department_and_supervisor_cannot_touch_front_desk_requests()
    {
        var r = await Log(Who.Reception, "p1", "HCC-1044", "Extra towels");
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.AssignRequestAsync(_w.Ctx(Who.Reception, "p2"), new AssignServiceRequestRequest(r.RequestId, Who.Maint, null, r.Version), Ct)));
        var a = (await _w.Commands.AssignRequestAsync(_w.Ctx(Who.Supervisor, "p3"), new AssignServiceRequestRequest(r.RequestId, Who.Housekeeper2, null, r.Version), Ct)).Value;
        Assert.Equal(("Assigned", Who.Housekeeper2), (a.Status, a.AssigneeId));
        var taxi = await Log(Who.Reception, "p4", "HCC-1044", "Taxi");
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.AssignRequestAsync(_w.Ctx(Who.Supervisor, "p5"), new AssignServiceRequestRequest(taxi.RequestId, Who.Reception, null, taxi.Version), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.CancelRequestAsync(_w.Ctx(Who.Reception, "p6"), new CancelServiceRequestRequest(a.RequestId, "", a.Version), Ct)));
        var c = (await _w.Commands.CancelRequestAsync(_w.Ctx(Who.Reception, "p7"), new CancelServiceRequestRequest(a.RequestId, "Guest found spare towels", a.Version), Ct)).Value;
        Assert.Equal("Cancelled", c.Status);
    }

    [Fact]
    public async Task Queries_scope_by_role_and_the_report_matches_the_rules()
    {
        var done = await Log(Who.Reception, "q1", "HCC-1041", "Taxi", assignee: Who.Reception);   // front desk, within SLA
        await _w.Commands.CompleteRequestAsync(_w.Ctx(Who.Reception, "q1c"), new CompleteServiceRequestRequest(done.RequestId, "Taxi confirmed for 09:30.", done.Version), Ct);
        var hk = await Log(Who.Reception, "q2", "HCC-1043", "Extra towels", assignee: Who.Housekeeper);
        await Log(Who.Reception, "q3", "HCC-1044", "Extra towels");   // unassigned housekeeping
        _w.Clock.Now = _w.Clock.Now.AddMinutes(30);   // all open requests are now overdue

        var own = await _w.Queries.RequestsAsync(_w.PropertyId, Who.Housekeeper, ct: Ct);
        Assert.True(own.OwnOnly);
        Assert.Equal(hk.RequestId, Assert.Single(own.Items).RequestId);
        Assert.Equal("Overdue · 10 min", own.Items[0].Sla.Label);

        var sup = await _w.Queries.RequestsAsync(_w.PropertyId, Who.Supervisor, ct: Ct);
        Assert.DoesNotContain(sup.Items, i => i.Dept == "Front desk");
        Assert.Equal(2, sup.Items.Count);
        Assert.Equal(3, (await _w.Queries.RequestsAsync(_w.PropertyId, Who.Reception, ct: Ct)).Total);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.RequestsAsync(_w.PropertyId, Who.Cashier, ct: Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.RequestAsync(_w.PropertyId, Who.Housekeeper2, hk.RequestId, Ct)));

        var report = await _w.Queries.ResponseReportAsync(_w.PropertyId, Who.Accountant, byDept: true, Ct);
        Assert.Equal((1, 1, 100, 2), (report.Totals.Done, report.Totals.Within, report.Totals.Pct, report.Totals.Overdue));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ResponseReportAsync(_w.PropertyId, Who.Housekeeper, true, Ct)));
    }

    [Fact]
    public async Task Settings_change_the_SLA_and_are_bounded_and_versioned()
    {
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Reception, "s0"), new SaveGuestServiceSettingsRequest(null, 60, null, null), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Manager, "s1"), new SaveGuestServiceSettingsRequest(new Dictionary<string, int> { ["Extra towels"] = 2 }, null, null, null), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Manager, "s2"), new SaveGuestServiceSettingsRequest(null, 400, null, null), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Manager, "s3"), new SaveGuestServiceSettingsRequest(null, 90, 60, null), Ct)));
        var saved = (await _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Manager, "s4"), new SaveGuestServiceSettingsRequest(new Dictionary<string, int> { ["Extra towels"] = 30 }, 30, 60, null), Ct)).Value;
        Assert.Equal((30, 30, 60), (saved.SlaMinutes["Extra towels"], saved.RetentionDays, saved.ValuableRetentionDays));
        Assert.Equal(10, saved.SlaMinutes["Drinking water"]);   // defaults kept for the rest
        var r = await Log(Who.Reception, "s5", "HCC-1043", "Extra towels");
        Assert.Equal("2026-10-24T08:30:00.000Z", r.DueUtc);
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Owner, "s6"), new SaveGuestServiceSettingsRequest(null, 45, null, Convert.ToBase64String(new byte[8])), Ct)));
        Assert.Equal(saved.RetentionDays, (await _w.Queries.SettingsAsync(_w.PropertyId, Who.Manager, Ct)).RetentionDays);
        Assert.Equal("30", (await _w.Db.ScalarAsync("SELECT retention_days FROM ops.guest_service_setting;", Ct))!.ToString());
    }
}
