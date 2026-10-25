using Hcc.Application.Commands;
using Hcc.Application.Handover;
using Hcc.Contracts;
using Hcc.Contracts.Handover;
using Hcc.Domain.Errors;
using Hcc.Handover.Tests.Harness;

namespace Hcc.Handover.Tests.Sql;

/// <summary>
/// Prototype golden figures (tests/stream-handover.mjs) on SQL Server with the real access policy: fixture of 6 templates, 8 runs and 3 notes on
/// 24 Oct 2026 08:00 IST; opening run 2/7 = 29 %, 1 overdue; dashboard Front desk 25 items / 2 done / 8 %, Housekeeping 14 %.
/// </summary>
[Trait("Category", "Sql")]
public class HandoverSqlTests : IAsyncLifetime
{
    private HandoverWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await HandoverWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private long T(string code) => _w.Tpl[code];

    private static string Key() => Guid.NewGuid().ToString("N");

    private Task<CommandResult<ChecklistRunView>> Tick(string user, string tpl, string item, bool? done = null, string? note = null, string? photo = null, string? key = null)
        => _w.Commands.TickAsync(_w.Ctx(user, key ?? Key()), new TickChecklistItemRequest(T(tpl), item, done, note, photo), Ct);

    private Task<CommandResult<HandoverNoteView>> Ack(string user, string noteCode = "HOVN-FRONT-2026-10-23-N", string? key = null)
        => _w.Commands.AcknowledgeNoteAsync(_w.Ctx(user, key ?? Key()), new AcknowledgeHandoverNoteRequest(_w.Notes[noteCode]), Ct);

    private Task<CommandResult<ChecklistRunView>> Submit(string user, string tpl) => _w.Commands.SubmitRunAsync(_w.Ctx(user, Key()), new SubmitChecklistRunRequest(T(tpl)), Ct);

    private async Task<ChecklistRunView> Card(string user, string tpl)
    {
        var l = await _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users[user], null, Ct);
        return l.Runs.Single(r => r.TemplateId == T(tpl));
    }

    private static async Task<HccException> Fails(HccErrorCode code, Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<HccException>(action);
        Assert.Equal(code, e.Code);
        return e;
    }

    private async Task OpeningDone()
    {
        await Ack("reception");
        foreach (var i in new[] { "fo1", "fo4" }) await Tick("reception", "FD-OPEN", i, true);
        await Tick("reception", "FD-OPEN", "fo6", photo: "IMG_20261024_0810.jpg");
        await Tick("reception", "FD-OPEN", "fo6", true);
    }

    [Fact]
    public async Task Fixture_has_6_templates_8_runs_3_notes_and_no_new_permissions()
    {
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM ops.hov_template;", Ct));
        Assert.Equal(8, await _w.CountAsync("SELECT COUNT(*) FROM ops.hov_run;", Ct));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM ops.hov_note;", Ct));
        // No handover PERM ids (the module reuses matrix permissions); other Wave 2 modules seed theirs in *_rbac_seed.sql migrations.
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id LIKE 'PERM-HOV%' OR resource_action LIKE 'handover%';", Ct));
    }

    [Fact]
    public async Task Opening_run_at_0800_is_2_of_7_done_29_percent_with_one_overdue_item()
    {
        var c = await Card("reception", "FD-OPEN");
        Assert.Equal((7, 2, 29, 5, 1, false), (c.Progress.Total, c.Progress.Done, c.Progress.Pct, c.Progress.Mandatory, c.Progress.Overdue, c.Progress.Late));
        Assert.True(c.Items.Single(i => i.Key == "fo1").Overdue);
        var dash = await _w.Queries.DashboardAsync(_w.PropertyId, _w.Users["manager"], null, "desk", Ct);
        Assert.Single(dash.Overdue);
        Assert.Equal(1, dash.UnacknowledgedNotes);
    }

    [Fact]
    public async Task My_checklists_are_filtered_by_role_and_those_without_access_are_refused()
    {
        async Task<string[]> Names(string u) => (await _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users[u], null, Ct)).Runs.Select(r => r.Name).Order().ToArray();
        Assert.Equal(["Cashier handover", "Front desk closing", "Front desk opening", "Night audit pre-checks"], await Names("reception"));
        Assert.Equal(["Housekeeping floor check"], await Names("housekeeper"));
        Assert.Equal(["Maintenance rounds"], await Names("maint"));
        Assert.Equal(["Cashier handover"], await Names("cashier"));
        Assert.Equal(6, (await Names("owner")).Length);
        Assert.Equal(6, (await Names("manager")).Length);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users["accountant"], null, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users["techadmin"], null, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => Tick("accountant", "FD-OPEN", "fo1", true));
    }

    [Fact]
    public async Task First_tick_on_a_not_started_template_creates_the_run_with_a_snapshot()
    {
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM ops.hov_run WHERE template_id = {T("MNT-RND")} AND business_date = '2026-10-24';", Ct));
        var r = (await Tick("maint", "MNT-RND", "mr1", true)).Value;
        Assert.Equal((1, "Open", true, _w.Users["maint"]), (r.TemplateVersion, r.Status, r.Items.Single(i => i.Key == "mr1").Done, r.Items.Single(i => i.Key == "mr1").DoneBy));
        Assert.Equal(6, r.Items.Count);
    }

    [Fact]
    public async Task Template_edit_bumps_version_existing_run_keeps_its_items_and_stale_edits_are_refused()
    {
        var list = await _w.Queries.TemplatesAsync(_w.PropertyId, _w.Users["manager"], Ct);
        var t = list.Templates.Single(x => x.TemplateId == T("FD-OPEN"));
        var items = t.Items.Take(3).Select(i => new HandoverItemInput(i.Text, i.Mandatory, i.Due, i.Photo, i.Link)).Append(new HandoverItemInput("New item added by manager", true)).ToList();
        var req = new SaveHandoverTemplateRequest(t.TemplateId, t.Name, t.Desk, t.Shift, t.Roles, items, t.Version);
        var saved = (await _w.Commands.SaveTemplateAsync(_w.Ctx("manager", Key()), req, Ct)).Value;
        Assert.Equal((2, 4), (saved.TemplateVersion, saved.Items.Count));
        Assert.Equal(7, (await Card("reception", "FD-OPEN")).Items.Count);
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("manager", Key()), req, Ct));
        await Fails(HccErrorCode.VALIDATION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("manager", Key()), req with { ExpectedVersion = null }, Ct));
    }

    [Fact]
    public async Task Only_owner_or_manager_edit_templates_and_validation_applies()
    {
        var t = new SaveHandoverTemplateRequest(null, "Pool deck check", "Maintenance", "Evening", ["MNT"], [new HandoverItemInput("Skim the pool", true, "16:00")]);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.SaveTemplateAsync(_w.Ctx("reception", Key()), t, Ct));
        var list = await _w.Queries.TemplatesAsync(_w.PropertyId, _w.Users["owner"], Ct);
        var open = list.Templates.Single(x => x.TemplateId == T("FD-OPEN"));
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.ToggleTemplateAsync(_w.Ctx("supervisor", Key()), new ToggleHandoverTemplateRequest(open.TemplateId, open.Version), Ct));
        Assert.Contains("belong to the Maintenance desk", (await Fails(HccErrorCode.VALIDATION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("owner", Key()), t with { Roles = ["CSH"] }, Ct))).Message);
        Assert.Contains("HH:mm", (await Fails(HccErrorCode.VALIDATION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("owner", Key()), t with { Items = [new HandoverItemInput("Skim", false, "25:00")] }, Ct))).Message);
        Assert.Contains("1–25 items", (await Fails(HccErrorCode.VALIDATION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("owner", Key()), t with { Items = [] }, Ct))).Message);
        Assert.Contains("already exists", (await Fails(HccErrorCode.VALIDATION, () => _w.Commands.SaveTemplateAsync(_w.Ctx("owner", Key()), t with { Name = "Front desk opening" }, Ct))).Message);
        await _w.Commands.SaveTemplateAsync(_w.Ctx("owner", Key()), t, Ct);
        Assert.Equal(7, (await _w.Queries.TemplatesAsync(_w.PropertyId, _w.Users["owner"], Ct)).Templates.Count);
        Assert.Contains("Pool deck check", (await _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users["maint"], null, Ct)).Runs.Select(r => r.Name));
    }

    [Fact]
    public async Task Deactivated_template_hides_from_today_and_cannot_be_started()
    {
        var m = (await _w.Queries.TemplatesAsync(_w.PropertyId, _w.Users["manager"], Ct)).Templates.Single(x => x.TemplateId == T("MNT-RND"));
        var off = (await _w.Commands.ToggleTemplateAsync(_w.Ctx("manager", Key()), new ToggleHandoverTemplateRequest(m.TemplateId, m.Version), Ct)).Value;
        Assert.False(off.Active);
        Assert.Empty((await _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users["maint"], null, Ct)).Runs);
        Assert.Contains("inactive", (await Fails(HccErrorCode.INVALID_TRANSITION, () => Tick("maint", "MNT-RND", "mr1", true))).Message);
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.ToggleTemplateAsync(_w.Ctx("manager", Key()), new ToggleHandoverTemplateRequest(m.TemplateId, m.Version), Ct));
    }

    [Fact]
    public async Task Wrong_role_cannot_tick_another_roles_checklist()
    {
        await Fails(HccErrorCode.FORBIDDEN, () => Tick("housekeeper", "FD-OPEN", "fo1", true));
        await Fails(HccErrorCode.FORBIDDEN, () => Tick("cashier", "FD-OPEN", "fo1", true));
        await Fails(HccErrorCode.NOT_FOUND, () => Tick("reception", "FD-OPEN", "zz9", true));
    }

    [Fact]
    public async Task Photo_item_needs_a_photo_file_name_and_notes_are_limited()
    {
        Assert.Contains("photo file name", (await Fails(HccErrorCode.VALIDATION, () => Tick("reception", "FD-OPEN", "fo6", true))).Message);
        await Fails(HccErrorCode.VALIDATION, () => Tick("reception", "FD-OPEN", "fo6", photo: "lobby.exe"));
        Assert.Contains("200", (await Fails(HccErrorCode.VALIDATION, () => Tick("reception", "FD-OPEN", "fo7", note: new string('x', 201)))).Message);
        await Tick("reception", "FD-OPEN", "fo6", note: "Flowers replaced", photo: "IMG_20261024_0810.jpg");
        var r = (await Tick("reception", "FD-OPEN", "fo6", true)).Value;
        var i = r.Items.Single(x => x.Key == "fo6");
        Assert.Equal(("IMG_20261024_0810.jpg", "Flowers replaced", true), (i.PhotoName, i.Note, i.Done));
    }

    [Fact]
    public async Task Submit_is_blocked_by_mandatory_items_then_by_the_incoming_note_and_a_submitted_run_is_locked()
    {
        var e = await Fails(HccErrorCode.INVALID_TRANSITION, () => Submit("reception", "FD-OPEN"));
        Assert.Matches("3 mandatory items", e.Message);
        foreach (var i in new[] { "fo1", "fo4" }) await Tick("reception", "FD-OPEN", i, true);
        await Tick("reception", "FD-OPEN", "fo6", photo: "IMG_20261024_0810.jpg");
        await Tick("reception", "FD-OPEN", "fo6", true);
        Assert.Contains("Acknowledge the incoming Front desk handover", (await Fails(HccErrorCode.INVALID_TRANSITION, () => Submit("reception", "FD-OPEN"))).Message);
        await Ack("reception");
        var r = (await Submit("reception", "FD-OPEN")).Value;
        Assert.Equal("Submitted", r.Status);
        Assert.False(r.Progress.Late);
        Assert.Equal(0, r.Progress.Overdue);
        Assert.Contains("submitted", (await Fails(HccErrorCode.INVALID_TRANSITION, () => Tick("reception", "FD-OPEN", "fo7", true))).Message);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => Submit("reception", "FD-OPEN"));
    }

    [Fact]
    public async Task Reopen_is_manager_only_and_needs_a_reason()
    {
        await OpeningDone();
        await Submit("reception", "FD-OPEN");
        var req = new ReopenChecklistRunRequest(T("FD-OPEN"), "Redo");
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.ReopenRunAsync(_w.Ctx("reception", Key()), req, Ct));
        await Fails(HccErrorCode.VALIDATION, () => _w.Commands.ReopenRunAsync(_w.Ctx("manager", Key()), req with { Reason = "" }, Ct));
        var r = (await _w.Commands.ReopenRunAsync(_w.Ctx("manager", Key()), req with { Reason = "Lobby photo blurred" }, Ct)).Value;
        Assert.Equal(("Open", "Lobby photo blurred"), (r.Status, r.ReopenReason));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.ReopenRunAsync(_w.Ctx("manager", Key()), req with { Reason = "Again" }, Ct));
    }

    [Fact]
    public async Task Acknowledge_rules_own_note_other_desk_idempotent_replay_once_only()
    {
        Assert.Contains("own handover", (await Fails(HccErrorCode.FORBIDDEN, () => Ack("manager"))).Message);
        await Fails(HccErrorCode.FORBIDDEN, () => Ack("housekeeper"));
        var rec = await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["reception"], Ct);
        Assert.Equal([_w.Notes["HOVN-FRONT-2026-10-23-N"]], rec.IncomingNoteIds);
        Assert.DoesNotContain(_w.Notes["HOVN-FRONT-2026-10-23-N"], (await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["manager"], Ct)).IncomingNoteIds);
        var k = Key();
        var first = await Ack("reception", key: k);
        var replay = await Ack("reception", key: k);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value.NoteId, replay.Value.NoteId);
        Assert.Equal(("Acknowledged", _w.Users["reception"]), (first.Value.Status, first.Value.AckBy));
        Assert.Contains("already acknowledged", (await Fails(HccErrorCode.INVALID_TRANSITION, () => Ack("cashier"))).Message);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE operation = 'AcknowledgeHandoverNote';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'HandoverNoteAcknowledged';", Ct));
    }

    [Fact]
    public async Task Handover_note_desk_membership_cash_rules_and_replace_until_acknowledged()
    {
        Task<CommandResult<HandoverNoteView>> Note(string user, SubmitHandoverNoteRequest r) => _w.Commands.SubmitNoteAsync(_w.Ctx(user, Key()), r, Ct);
        await Fails(HccErrorCode.FORBIDDEN, () => Note("housekeeper", new("Front desk", Issues: "Test note")));
        Assert.Contains("only on the Front desk", (await Fails(HccErrorCode.VALIDATION, () => Note("supervisor", new("Housekeeping", Issues: "Linen short", Cash: new HandoverCashInput(1000))))).Message);
        Assert.Contains("Nothing to report", (await Fails(HccErrorCode.VALIDATION, () => Note("reception", new("Front desk", Issues: "")))).Message);
        Assert.Contains("Up to 8", (await Fails(HccErrorCode.VALIDATION, () => Note("reception", new("Front desk", Nothing: true, Carry: Enumerable.Repeat("Carry item", 9).ToList())))).Message);
        await Fails(HccErrorCode.FORBIDDEN, () => Note("maint", new("Front desk", Nothing: true)));
        var n = (await Note("cashier", new("Front desk", Issues: "UPI settlement short by one entry", Cash: new HandoverCashInput(1020000, "Drawer"), Carry: ["Recheck UPI DEMO-104"]))).Value;
        Assert.Equal(("HOVN-FRONT-2026-10-24-M", "Evening", 1020000L, 1), (n.Ref, n.ToShift, n.Cash!.AmountPaise, n.Carry.Count));
        var again = (await Note("cashier", new("Front desk", Issues: "Corrected note"))).Value;
        Assert.Equal(n.NoteId, again.NoteId);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.hov_note WHERE desk = 'Front desk' AND business_date = '2026-10-24' AND shift = 'Morning';", Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => Note("reception", new("Front desk", Issues: "Overwrite attempt")));
        await _w.Commands.AcknowledgeNoteAsync(_w.Ctx("reception", Key()), new AcknowledgeHandoverNoteRequest(n.NoteId), Ct);
        Assert.Contains("already acknowledged", (await Fails(HccErrorCode.INVALID_TRANSITION, () => Note("cashier", new("Front desk", Issues: "Too late")))).Message);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE operation = 'SubmitHandoverNote' AND detail LIKE '%1020000%';", Ct));
    }

    [Fact]
    public async Task Housekeepers_never_see_money_or_front_desk_notes_and_money_readers_see_the_float()
    {
        var hk = await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["housekeeper"], Ct);
        Assert.All(hk.Notes, n => Assert.Equal("Housekeeping", n.Desk));
        Assert.All(hk.Notes, n => Assert.Null(n.Cash));
        var sup = await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["supervisor"], Ct);
        Assert.Equal(["Housekeeping", "Maintenance"], sup.Notes.Select(n => n.Desk).Distinct().Order().ToArray());
        var cashier = (await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["cashier"], Ct)).Notes.Single(n => n.Desk == "Front desk");
        Assert.Equal(500000L, cashier.Cash!.AmountPaise);
        Assert.Equal(500000L, (await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["manager"], Ct)).Notes.Single(n => n.Desk == "Front desk").Cash!.AmountPaise);
        Assert.Equal(("Housekeeping", "Maintenance"), (hk.Notes.Single().Desk, (await _w.Queries.NotesAsync(_w.PropertyId, _w.Users["maint"], Ct)).Notes.Single().Desk));
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.NotesAsync(_w.PropertyId, _w.Users["accountant"], Ct));
    }

    [Fact]
    public async Task Carry_forward_items_close_once_by_desk_members()
    {
        var open = (await _w.Queries.OpenCarryAsync(_w.PropertyId, _w.Users["maint"], "Maintenance", Ct)).Items;
        Assert.Equal(2, open.Count);
        var c1 = open.Single(i => i.Text.Contains("308", StringComparison.Ordinal));
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.CompleteCarryAsync(_w.Ctx("reception", Key()), new CompleteCarryItemRequest(c1.NoteId, c1.CarryId), Ct));
        var done = (await _w.Commands.CompleteCarryAsync(_w.Ctx("maint", Key()), new CompleteCarryItemRequest(c1.NoteId, c1.CarryId), Ct)).Value;
        Assert.True(done.Carry.Single(c => c.CarryId == c1.CarryId).Done);
        Assert.Single((await _w.Queries.OpenCarryAsync(_w.PropertyId, _w.Users["maint"], "Maintenance", Ct)).Items);
        Assert.Contains("already done", (await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.CompleteCarryAsync(_w.Ctx("maint", Key()), new CompleteCarryItemRequest(c1.NoteId, c1.CarryId), Ct))).Message);
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Commands.CompleteCarryAsync(_w.Ctx("maint", Key()), new CompleteCarryItemRequest(c1.NoteId, 999999), Ct));
    }

    [Fact]
    public async Task The_property_clock_drives_overdue_and_late_flags()
    {
        _w.Clock.SetLocal("2026-10-24T10:01");
        Assert.Equal(2, (await Card("manager", "HK-FLOOR")).Progress.Overdue); // hk2 09:00, hk4 10:00
        _w.Clock.SetLocal("2026-10-25T06:11");
        Assert.True((await Card("manager", "FD-OPEN")).Progress.Late);
        Assert.Equal("Night", (await _w.Queries.MyChecklistsAsync(_w.PropertyId, _w.Users["manager"], null, Ct)).CurrentShift);
    }

    [Fact]
    public async Task Dashboard_completion_by_desk_and_role_matches_the_prototype()
    {
        var byDesk = (await _w.Queries.DashboardAsync(_w.PropertyId, _w.Users["manager"], "2026-10-24", "desk", Ct)).Rows.ToDictionary(r => r.Key);
        var fd = byDesk["Front desk"];
        Assert.Equal((4, 2, 25, 8, 3), (fd.Runs, fd.Done, fd.Items, fd.Pct, fd.NotStarted));
        Assert.Equal(14, byDesk["Housekeeping"].Pct);
        Assert.Equal(1, byDesk["Maintenance"].NotStarted);
        var byRole = (await _w.Queries.DashboardAsync(_w.PropertyId, _w.Users["manager"], "2026-10-23", "role", Ct)).Rows.ToDictionary(r => r.Key);
        Assert.Equal(1, byRole["MNT"].Submitted);
        Assert.Equal((4, 3, 1), (byRole["REC"].Runs, byRole["REC"].Submitted, byRole["REC"].Late));
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.DashboardAsync(_w.PropertyId, _w.Users["reception"], null, "desk", Ct));
    }

    [Fact]
    public async Task Two_people_starting_the_same_checklist_together_share_one_run()
    {
        var a = Tick("maint", "MNT-RND", "mr1", true);
        var b = Tick("manager", "MNT-RND", "mr2", true);
        await Task.WhenAll(a, b);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM ops.hov_run WHERE template_id = {T("MNT-RND")} AND business_date = '2026-10-24';", Ct));
        var c = await Card("manager", "MNT-RND");
        Assert.Equal(2, c.Progress.Done);
    }
}
