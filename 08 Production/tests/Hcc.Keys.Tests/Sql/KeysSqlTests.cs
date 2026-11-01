using Hcc.Application.Commands;
using Hcc.Application.Keys;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Domain.Errors;
using Hcc.Domain.Keys;
using Hcc.Keys.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Keys.Tests.Sql;

/// <summary>Staff commands, the agent contract (fake agent), job states, counts, log, offline fallback, 106, permissions, mismatch warning, check-out hook.</summary>
[Trait("Category", "Sql")]
public class KeysSqlTests : IAsyncLifetime
{
    private KeysWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await KeysWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<CommandResult<KeyJobView>> Req(string user, string booking, string ev, string? reason = null, string? validTo = null, string? key = null) =>
        _w.Commands.RequestKeyAsync(_w.Ctx(user, key), new RequestKeyRequest(_w.Bookings[booking], ev, reason, validTo), Ct);

    private async Task<HccException> Refused(Func<Task> act) => await Assert.ThrowsAsync<HccException>(act);

    [Fact]
    public async Task Issue_runs_through_the_agent_states_to_done_and_creates_an_active_card_with_the_counts()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        var job = (await Req("reception", "BK-1", "issued")).Value;
        Assert.Equal(("queued", "issued", "issue_guest", "201"), (job.State, job.Event, job.Kind, job.RoomNo));
        var step = (await agent.StepAsync(Ct))!.Value;
        Assert.Equal("201", step.Job.Room);
        Assert.Equal("2026-11-02T14:00:00", step.Job.ValidFrom);   // server time, never the PC clock
        Assert.Equal("2026-11-04T12:00:00", step.Job.ValidTo);     // BIS departs at 12:00
        Assert.Equal("done", step.Result.State);
        var panel = await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["reception"], _w.Bookings["BK-1"], Ct);
        Assert.Equal(1, panel.BookingCounts.Issued);
        Assert.Equal(1, panel.RoomCounts.Issued);
        var card = Assert.Single(panel.ActiveCards);
        Assert.Equal(("issued", "active"), (card.Event, card.Status));
        Assert.Equal("201", card.RoomNo);
        Assert.Null(await agent.StepAsync(Ct));   // queue empty
    }

    [Fact]
    public async Task Job_states_picked_waiting_card_done_are_visible_to_the_tablet()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        var job = (await Req("reception", "BK-1", "issued")).Value;
        var next = (await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct))!;
        Assert.Equal("picked", (await _w.Queries.JobAsync(_w.PropertyId, _w.Users["reception"], job.JobId, Ct)).State);
        await _w.Agent.SetStateAsync(agent.EncoderId, agent.Token, next.JobId, new AgentStateRequest("waiting_card"), Ct);
        Assert.Equal("waiting_card", (await _w.Queries.JobAsync(_w.PropertyId, _w.Users["reception"], job.JobId, Ct)).State);
        var bad = await Refused(() => _w.Agent.SetStateAsync(agent.EncoderId, agent.Token, next.JobId, new AgentStateRequest("done"), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, bad.Code);
        var done = await _w.Agent.ResultAsync(agent.EncoderId, agent.Token, next.JobId, new AgentResultRequest("Ok", "ok", "111", "111", "t"), Ct);
        Assert.Equal("done", done.State);
        var late = await Refused(() => _w.Agent.SetStateAsync(agent.EncoderId, agent.Token, next.JobId, new AgentStateRequest("waiting_card"), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, late.Code);
    }

    [Fact]
    public async Task A_result_posted_twice_changes_nothing_and_the_job_is_executed_once()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        var (job, view) = (await agent.StepAsync(Ct))!.Value;
        Assert.Equal("done", view.State);
        var again = await _w.Agent.ResultAsync(agent.EncoderId, agent.Token, job.JobId, new AgentResultRequest("WriteFailed", "late", null, null, "t"), Ct);
        Assert.Equal("done", again.State);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_card;", Ct));
    }

    [Fact]
    public async Task Failure_codes_mark_the_job_failed_without_a_card_and_do_not_count()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        agent.Encoder.CardPresent = false;
        await Req("reception", "BK-1", "issued");
        var r1 = (await agent.StepAsync(Ct))!.Value.Result;
        Assert.Equal(("failed", "NoCard"), (r1.State, r1.ErrorCode));
        agent.Encoder.CardPresent = true;
        agent.Encoder.FailNextWrite = true;
        await Req("reception", "BK-1", "issued");
        Assert.Equal("WriteFailed", (await agent.StepAsync(Ct))!.Value.Result.ErrorCode);
        await Req("reception", "BK-1", "issued");
        Assert.Equal("done", (await agent.StepAsync(Ct))!.Value.Result.State);   // retry after failure works
        var panel = await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["reception"], _w.Bookings["BK-1"], Ct);
        Assert.Equal(1, panel.BookingCounts.Issued);   // only the successful write counts
        Assert.Equal(3, panel.Jobs.Count);
        Assert.Single(panel.ActiveCards);
    }

    [Fact]
    public async Task Reissue_and_reactivate_need_a_reason_and_a_lost_card_marks_the_old_card_lost()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await agent.StepAsync(Ct);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "reissued"))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "reactivated"))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "reissued", "because"))).Code);
        await Req("reception", "BK-1", "reissued", "lost");
        await agent.StepAsync(Ct);
        await Req("reception", "BK-1", "reissued", "damaged");
        await agent.StepAsync(Ct);
        agent.Encoder.SupportsExtend = true;
        await Req("manager", "BK-1", "extended", "extension", "2026-11-05");
        Assert.Equal("done", (await agent.StepAsync(Ct))!.Value.Result.State);
        await Req("manager", "BK-1", "reactivated", "other");
        await agent.StepAsync(Ct);
        var panel = await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["manager"], _w.Bookings["BK-1"], Ct);
        Assert.Equal((1, 2, 1, 1), (panel.BookingCounts.Issued, panel.BookingCounts.Reissued, panel.BookingCounts.Extended, panel.BookingCounts.Reactivated));
        Assert.Equal(2, panel.BookingCounts.ReactivatedTotal);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_card WHERE status = 'lost';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_card WHERE status = 'active';", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_job WHERE reason IN ('lost', 'damaged');", Ct));
    }

    [Fact]
    public async Task Extend_is_not_supported_by_the_real_adapter_yet_and_the_hint_says_to_reissue()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await agent.StepAsync(Ct);
        await Req("reception", "BK-1", "extended", "extension", "2026-11-06");
        var result = (await agent.StepAsync(Ct))!.Value.Result;
        Assert.Equal(("failed", "NotSupported"), (result.State, result.ErrorCode));
        Assert.Contains("re-issue", result.Hint!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["manager"], _w.Bookings["BK-1"], Ct)).BookingCounts.Extended);
    }

    [Fact]
    public async Task Room_106_and_a_room_without_a_BIS_door_have_no_lock_until_the_Manager_maps_it()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        var j106 = (await Req("reception", "BK-2", "issued")).Value;
        Assert.Equal(("failed", "NoLockRoom"), (j106.State, j106.ErrorCode));
        Assert.Contains("manual key", j106.Hint!, StringComparison.OrdinalIgnoreCase);
        var jx = (await Req("reception", "BK-3", "issued")).Value;
        Assert.Equal("NoLockRoom", jx.ErrorCode);
        Assert.Null(await agent.StepAsync(Ct));   // nothing was queued for the agent
        var locks = await _w.Queries.LocksAsync(_w.PropertyId, _w.Users["manager"], Ct);
        Assert.False(locks.Locks.Single(l => l.RoomNo == "106").HasLock);
        Assert.False(locks.Locks.Single(l => l.RoomNo == "X99").HasLock);
        Assert.True(locks.Locks.Single(l => l.RoomNo == "201").HasLock);
        // The Manager maps the 36th room (editable in settings); a stale version is refused; the next issue is queued.
        var x = locks.Locks.Single(l => l.RoomNo == "X99");
        await _w.Commands.SaveLockAsync(_w.Ctx("manager"), new SaveKeyLockRequest(x.RoomId, true, 36, "000099", "X99", "mapped by the Manager", x.Version), Ct);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Refused(() => _w.Commands.SaveLockAsync(_w.Ctx("manager"), new SaveKeyLockRequest(x.RoomId, false, null, null, null, null, x.Version), Ct))).Code);
        Assert.Equal("queued", (await Req("reception", "BK-3", "issued")).Value.State);
    }

    [Fact]
    public async Task Offline_encoder_fails_the_request_at_once_and_the_manual_key_record_counts_like_the_event()
    {
        var offline = (await Req("reception", "BK-1", "issued")).Value;   // no encoder registered at all
        Assert.Equal(("failed", "EncoderOffline"), (offline.State, offline.ErrorCode));
        Assert.Contains("BIS Hotel manually", offline.Hint!, StringComparison.Ordinal);
        var manual = (await _w.Commands.RecordManualKeyAsync(_w.Ctx("reception"), new RecordManualKeyRequest(_w.Bookings["BK-1"], "issued", null, "made in BIS"), Ct)).Value;
        Assert.Equal(("done", "manual"), (manual.State, manual.Kind));
        var panel = await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["reception"], _w.Bookings["BK-1"], Ct);
        Assert.Equal(1, panel.BookingCounts.Issued);
        Assert.Equal("manual", Assert.Single(panel.ActiveCards).Status);
        Assert.False(panel.Encoder.Online);
        // A heartbeat older than 30 s is offline again.
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        Assert.True((await _w.Queries.EncoderAsync(_w.PropertyId, _w.Users["reception"], Ct)).Online);
        _w.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False((await _w.Queries.EncoderAsync(_w.PropertyId, _w.Users["reception"], Ct)).Online);
        Assert.Equal("EncoderOffline", (await Req("reception", "BK-1", "reissued", "lost")).Value.ErrorCode);
        Assert.NotNull(agent);
    }

    [Fact]
    public async Task A_queued_job_expires_after_two_minutes_and_an_abandoned_pick_fails_with_UiTimeout()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        var job = (await Req("reception", "BK-1", "issued")).Value;
        _w.Clock.Advance(TimeSpan.FromMinutes(3));
        await agent.HeartbeatAsync(true, Ct);
        Assert.Null(await agent.StepAsync(Ct));
        var expired = await _w.Queries.JobAsync(_w.PropertyId, _w.Users["reception"], job.JobId, Ct);
        Assert.Equal(("expired", "Expired"), (expired.State, expired.ErrorCode));
        // Picked and never reported (agent restarted mid-job): failed UiTimeout, the staff re-reads the card.
        var job2 = (await Req("reception", "BK-1", "issued")).Value;
        Assert.NotNull(await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct));
        _w.Clock.Advance(TimeSpan.FromMinutes(3));
        var failed = await _w.Queries.JobAsync(_w.PropertyId, _w.Users["reception"], job2.JobId, Ct);
        Assert.Equal(("failed", "UiTimeout"), (failed.State, failed.ErrorCode));
    }

    [Fact]
    public async Task One_job_at_a_time_and_oldest_first()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        var a = (await Req("reception", "BK-1", "issued")).Value;
        _w.Clock.Advance(TimeSpan.FromSeconds(1));   // oldest first is by request time
        var b = (await Req("reception", "BK-4", "issued")).Value;
        var first = (await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct))!;
        Assert.Equal(a.JobId, first.JobId);
        Assert.Null(await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct));   // second waits while the first is picked
        await _w.Agent.ResultAsync(agent.EncoderId, agent.Token, first.JobId, new AgentResultRequest("Ok", "ok", "1", "1", "t"), Ct);
        Assert.Equal(b.JobId, (await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct))!.JobId);
    }

    [Fact]
    public async Task The_same_idempotency_key_returns_the_same_job_and_a_different_body_is_refused()
    {
        await _w.OnlineAgentAsync(ct: Ct);
        var one = (await Req("reception", "BK-1", "issued", key: "same")).Value;
        var two = await Req("reception", "BK-1", "issued", key: "same");
        Assert.True(two.Replayed);
        Assert.Equal(one.JobId, two.Value.JobId);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_job;", Ct));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, (await Refused(() => Req("reception", "BK-4", "issued", key: "same"))).Code);
    }

    [Fact]
    public async Task Card_expiry_vs_folio_checkout_mismatch_is_a_visible_warning_on_the_job_and_the_panel()
    {
        await _w.OnlineAgentAsync(ct: Ct);
        var job = (await Req("reception", "BK-1", "issued")).Value;
        Assert.Contains("12:00", job.Warning!, StringComparison.Ordinal);   // default standard checkout 11:00
        var panel = await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["reception"], _w.Bookings["BK-1"], Ct);
        Assert.Equal(job.Warning, panel.Warning);
        Assert.Equal("11:00", panel.FolioCheckout);
        Assert.Equal("2026-11-04T12:00", panel.CardExpiry);
        await _w.ExecAsync($"""
            INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES ({_w.PropertyId}, 'checkout', '2026-10-01', '2026-10-01');
            INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, source, changed_by, changed_utc)
            VALUES ({_w.PropertyId}, 'checkout', '2026-10-01', '"12:00"', '"11:00"', '11:00', '12:00', 'change', {_w.Users["owner"]}, '2026-10-01');
            """, Ct);
        Assert.Null((await Req("reception", "BK-4", "issued")).Value.Warning);
        Assert.Null((await _w.Queries.PanelAsync(_w.PropertyId, _w.Users["reception"], _w.Bookings["BK-4"], Ct)).Warning);
    }

    [Fact]
    public async Task Permissions_manager_has_all_keys_reception_cannot_map_locks_housekeeping_and_accounting_nothing()
    {
        await _w.OnlineAgentAsync(ct: Ct);
        foreach (var u in new[] { "housekeeper", "accountant" })
        {
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => Req(u, "BK-1", "issued"))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Commands.CancelKeyAsync(_w.Ctx(u), new CancelKeyRequest(_w.Bookings["BK-1"]), Ct))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Queries.PanelAsync(_w.PropertyId, _w.Users[u], _w.Bookings["BK-1"], Ct))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Queries.LogAsync(_w.PropertyId, _w.Users[u], null, null, null, null, null, null, Ct))).Code);
        }

        foreach (var perm in new[] { KeyPermissions.Read, KeyPermissions.Issue, KeyPermissions.Cancel, KeyPermissions.Configure })
        {
            await _w.Policy.RequireAsync(_w.PropertyId, _w.Users["manager"], perm, null, null, Ct);
            await _w.Policy.RequireAsync(_w.PropertyId, _w.Users["owner"], perm, null, null, Ct);
        }

        Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Queries.LocksAsync(_w.PropertyId, _w.Users["reception"], Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Commands.RegisterEncoderAsync(_w.Ctx("reception"), new RegisterKeyEncoderRequest("FRONT-2", "PC"), Ct))).Code);
        await _w.Queries.LocksAsync(_w.PropertyId, _w.Users["manager"], Ct);
        var job = (await _w.Commands.ReadKeyAsync(_w.Ctx("reception"), new ReadKeyRequest(null), Ct)).Value;
        Assert.Equal(("read", "queued"), (job.Event, job.State));
    }

    [Fact]
    public async Task Cancel_and_read_events_are_logged_and_counted_and_a_cancel_checks_the_card_out()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await agent.StepAsync(Ct);
        await _w.Commands.ReadKeyAsync(_w.Ctx("reception"), new ReadKeyRequest(null), Ct);
        Assert.Equal("done", (await agent.StepAsync(Ct))!.Value.Result.State);
        await _w.Commands.CancelKeyAsync(_w.Ctx("reception"), new CancelKeyRequest(_w.Bookings["BK-1"]), Ct);
        Assert.Equal("done", (await agent.StepAsync(Ct))!.Value.Result.State);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_card WHERE status = 'checked_out';", Ct));
        var log = await _w.Queries.LogAsync(_w.PropertyId, _w.Users["manager"], null, null, null, null, null, null, Ct);
        Assert.Equal((1, 0, 0, 0, 1, 1), (log.Totals.Issued, log.Totals.Reissued, log.Totals.Extended, log.Totals.Reactivated, log.Totals.Cancelled, log.Totals.Read));
        Assert.Equal(3, log.Total);
    }

    [Fact]
    public async Task Check_out_hook_queues_a_cancel_never_throws_and_leaves_a_failed_job_when_the_encoder_is_offline()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await agent.StepAsync(Ct);
        Assert.True(await _w.Commands.CancelForDepartureAsync(_w.PropertyId, _w.Bookings["BK-1"], _w.Users["reception"], "keys:checkout:1", Ct));
        Assert.True(await _w.Commands.CancelForDepartureAsync(_w.PropertyId, _w.Bookings["BK-1"], _w.Users["reception"], "keys:checkout:1", Ct));   // replay: one job
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_job WHERE kind = 'cancel';", Ct));
        Assert.Equal("done", (await agent.StepAsync(Ct))!.Value.Result.State);
        // A room whose card was already cancelled / never issued queues nothing; a bad reservation or user never throws.
        Assert.True(await _w.Commands.CancelForDepartureAsync(_w.PropertyId, _w.Bookings["BK-4"], _w.Users["reception"], "keys:checkout:2", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.key_job WHERE kind = 'cancel';", Ct));
        Assert.True(await _w.Commands.CancelForDepartureAsync(_w.PropertyId, 99999, _w.Users["reception"], "keys:checkout:3", Ct));
        await _w.Commands.CancelForDepartureAsync(_w.PropertyId, _w.Bookings["BK-1"], 424242, "keys:checkout:4", Ct);   // never throws, whatever the outcome
        // Encoder goes offline: a card exists, the cancel job is recorded as failed EncoderOffline (the reconciliation task) and check-out is unaffected.
        await Req("reception", "BK-4", "issued");
        await agent.StepAsync(Ct);
        _w.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await _w.Commands.CancelForDepartureAsync(_w.PropertyId, _w.Bookings["BK-4"], _w.Users["reception"], "keys:checkout:5", Ct));
        Assert.Equal("EncoderOffline", await _w.ScalarAsync("SELECT TOP 1 error_code FROM ops.key_job WHERE kind = 'cancel' ORDER BY requested_utc DESC, job_id;", Ct));
    }

    [Fact]
    public async Task Key_log_filters_by_date_room_booking_user_and_event_with_totals_and_the_reason_is_kept()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await agent.StepAsync(Ct);
        _w.Clock.Advance(TimeSpan.FromDays(1));
        await agent.HeartbeatAsync(true, Ct);
        await Req("manager", "BK-1", "reissued", "damaged");
        await agent.StepAsync(Ct);
        await Req("manager", "BK-4", "issued");
        await agent.StepAsync(Ct);
        Task<KeyLogView> Log(string? from = null, string? to = null, string? room = null, string? booking = null, int? user = null, string? ev = null) =>
            _w.Queries.LogAsync(_w.PropertyId, _w.Users["owner"], from, to, room, booking, user, ev, Ct);
        Assert.Equal(3, (await Log()).Total);
        Assert.Equal(2, (await Log(room: "201")).Total);
        Assert.Equal(2, (await Log(booking: "BK-1")).Total);
        Assert.Single((await Log(booking: "BK-4")).Rows);
        Assert.Equal(2, (await Log(user: _w.Users["manager"])).Total);
        var re = await Log(ev: "reissued");
        Assert.Equal("damaged", Assert.Single(re.Rows).Reason);
        Assert.Equal(1, re.Totals.Reissued);
        Assert.Equal(1, (await Log(from: "2026-11-02", to: "2026-11-02")).Total);
        Assert.Equal(2, (await Log(from: "2026-11-03")).Total);
        Assert.Equal(("manager", "201"), ((await Log(ev: "reissued")).Rows[0].RequestedByName, (await Log(ev: "reissued")).Rows[0].RoomNo));
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Log(ev: "bogus"))).Code);
    }

    [Fact]
    public async Task The_log_is_append_only_the_reason_check_holds_and_agents_need_their_own_token()
    {
        var agent = await _w.OnlineAgentAsync(ct: Ct);
        await Req("reception", "BK-1", "issued");
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync("UPDATE ops.key_job SET event_type = 'read';", Ct));
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync("DELETE ops.key_job;", Ct));
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync($"UPDATE ops.key_job SET requested_by = {_w.Users["owner"]};", Ct));
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync($"INSERT ops.key_job(job_id, property_id, kind, event_type, payload_json, requested_by, requested_utc, state, timeout_utc) VALUES (NEWID(), {_w.PropertyId}, 'issue_guest', 'reissued', '{{}}', 1, SYSUTCDATETIME(), 'queued', SYSUTCDATETIME());", Ct));
        var wrong = await Refused(() => _w.Agent.NextJobAsync(agent.EncoderId, "not-the-token", Ct));
        Assert.Equal(HccErrorCode.UNAUTHENTICATED, wrong.Code);
        var other = await _w.Commands.RegisterEncoderAsync(_w.Ctx("manager"), new RegisterKeyEncoderRequest("FRONTDESK-2", "OTHER-PC"), Ct);
        var job = (await _w.Agent.NextJobAsync(agent.EncoderId, agent.Token, Ct))!;
        await _w.Agent.HeartbeatAsync("FRONTDESK-2", other.Value.Token, new AgentHeartbeatRequest("mock", true, null, null), Ct);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Refused(() => _w.Agent.ResultAsync("FRONTDESK-2", other.Value.Token, job.JobId, new AgentResultRequest("Ok", null, "1", "1", null), Ct))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => _w.Commands.RegisterEncoderAsync(_w.Ctx("manager"), new RegisterKeyEncoderRequest("FRONTDESK-2", "OTHER-PC"), Ct))).Code);
    }

    [Fact]
    public async Task Requests_need_an_in_house_booking_a_valid_end_date_and_the_encoder_count_limits()
    {
        await _w.OnlineAgentAsync(ct: Ct);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "issued", validTo: "2026-11-02"))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "issued", validTo: "2027-06-01"))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Refused(() => Req("reception", "BK-1", "issued", validTo: "04/11/2026"))).Code);
        await _w.ExecAsync($"UPDATE inv.reservation SET status = 'Confirmed' WHERE reservation_id = {_w.Bookings["BK-4"]};", Ct);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, (await Refused(() => Req("reception", "BK-4", "issued"))).Code);
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Refused(() => _w.Commands.RequestKeyAsync(_w.Ctx("reception"), new RequestKeyRequest(999999, "issued", null, null), Ct))).Code);
    }
}
