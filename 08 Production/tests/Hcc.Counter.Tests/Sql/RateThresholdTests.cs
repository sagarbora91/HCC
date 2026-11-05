using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Stays;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Configuration;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;
using Hcc.Domain.Reporting;
using Hcc.Domain.Stays;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Counter.Tests.Sql;

/// <summary>
/// AF2-CTL01 (audit CTL-01, migration 4920): a rate reduction during a stay is judged against the stay's AGREED rate as well as the ₹2,000 Deluxe tariff
/// (the stricter wins) with the Owner's threshold <c>rate.agreedReductionBps</c> (default 15 %); every change beyond it is recorded (over_threshold,
/// audit RATE_REDUCTION_OVER_THRESHOLD) and listed in the owner digest's e-mail draft with who / what / how much (counts only on WhatsApp).
/// </summary>
public sealed class RateThresholdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<CommandResult<StayRateChangeResult>> Change(Hotel h, int who, long stay, long rate, string reason = "Corporate") =>
        h.Get<StayRateCommands>().ChangeAsync(h.Ctx(who, StayRateCommands.OpChangeStayRate), new(stay, rate, null, null, "all", reason), Ct);

    private static async Task<CommandResult<ApprovalResult>> Decide(Hotel h, int who, long approvalId)
    {
        var v = await h.VersionAsync($"SELECT row_ver FROM bill.approval WHERE approval_id = {approvalId}", Ct);
        return await h.Billing.DecideApprovalAsync(h.As(who), Hotel.Key(), new DecideApprovalRequest(approvalId, true, "ok", v), Ct);
    }

    [Fact]
    public async Task At_the_threshold_below_the_agreed_rate_the_manager_applies_one_paisa_more_needs_the_owner()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, a, _) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-27", 250000, Ct);
        var (_, b, _) = await h.SeedInHouseAsync("102", "2026-10-24", "2026-10-27", 250000, Ct);

        var at = await Change(h, h.Manager, a, 212500); // exactly 15 % below the agreed ₹2,500
        Assert.Equal("Applied", at.Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {a} AND over_threshold = 0 AND agreed_rate_paise = 250000 AND agreed_after_paise = 250000 AND tariff_paise = 200000 AND threshold_bps = 1500", Ct));

        var over = await Change(h, h.Manager, b, 212499);
        Assert.Equal("PendingApproval", over.Value.Status);
        var id = over.Value.ApprovalId!.Value;
        var m2 = await Assert.ThrowsAsync<HccException>(() => Decide(h, h.Manager2, id));
        Assert.Equal(HccErrorCode.FORBIDDEN, m2.Code);
        Assert.Contains("below the agreed rate", m2.Message, StringComparison.Ordinal);
        Assert.Equal("Executed", (await Decide(h, h.Owner, id)).Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {b} AND over_threshold = 1 AND approved_by = {h.Owner} AND agreed_after_paise = 212499", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RATE_REDUCTION_OVER_THRESHOLD' AND target = 'stay-{b}'", Ct));
    }

    [Fact]
    public async Task The_audit_case_2500_to_1900_against_a_2000_tariff_needs_the_owner()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (res, stay, _) = await h.SeedInHouseAsync("103", "2026-10-24", "2026-10-26", 250000, Ct);
        var r = await Change(h, h.Manager, stay, 190000); // within 15 % of the tariff, 24 % below the agreed rate
        Assert.Equal("PendingApproval", r.Value.Status);
        Assert.Equal(250000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'STAY_RATE_CHANGE_REQUESTED' AND target = 'stay-{stay}' AND detail LIKE N'%over the 1500 bps threshold%'", Ct));
    }

    [Fact]
    public async Task An_agreed_rate_below_the_master_tariff_keeps_the_tariff_limit()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, a, _) = await h.SeedInHouseAsync("104", "2026-10-24", "2026-10-27", 180000, Ct);
        var (_, b, _) = await h.SeedInHouseAsync("105", "2026-10-24", "2026-10-27", 180000, Ct);
        Assert.Equal("Applied", (await Change(h, h.Manager, a, 170000)).Value.Status); // tariff floor ₹1,700; 5.6 % below the agreed ₹1,800
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {a} AND over_threshold = 0 AND agreed_rate_paise = 180000", Ct));
        Assert.Equal("PendingApproval", (await Change(h, h.Manager, b, 169999)).Value.Status); // below the tariff floor
    }

    [Fact]
    public async Task A_rate_changed_mid_stay_is_still_measured_against_the_agreed_rate_until_the_owner_moves_it()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (res, stay, _) = await h.SeedInHouseAsync("106", "2026-10-24", "2026-10-28", 250000, Ct);
        Assert.Equal("Applied", (await Change(h, h.Manager, stay, 220000, "Step one")).Value.Status);         // 12 % below agreed
        Assert.Equal("PendingApproval", (await Change(h, h.Manager, stay, 210000, "Step two")).Value.Status); // 16 % below the agreed ₹2,500
        // The Owner sets ₹2,000 directly (20 % below agreed): applied, flagged, and ₹2,000 becomes the agreed rate.
        Assert.Equal("Applied", (await Change(h, h.Owner, stay, 200000, "Owner rate")).Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {stay} AND new_rate_paise = 200000 AND over_threshold = 1 AND agreed_rate_paise = 250000 AND agreed_after_paise = 200000 AND approval_id IS NULL", Ct));
        // Now 15 % below the new agreed ₹2,000 (= the tariff floor ₹1,700) is within the Manager's limits.
        Assert.Equal("Applied", (await Change(h, h.Manager, stay, 170000, "Step three")).Value.Status);
        Assert.Equal(170000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", Ct));
    }

    // ── af2-fix finding 1: move room / extend stay with a new rate go through the same agreed-rate threshold ──

    private static async Task<CommandResult<ChangeStayDatesResult>> Extend(Hotel h, int who, long stay, string to, long rate) =>
        await h.Get<StayChangeCommands>().ExtendStayAsync(h.Ctx(who, StayChangeCommands.OpExtendStay),
            new ExtendStayRequest(stay, await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct), to, "Corporate rate", "change", rate), Ct);

    private static async Task<CommandResult<MoveRoomResult>> Move(Hotel h, int who, long stay, string room, long rate) =>
        await h.Get<StayChangeCommands>().MoveRoomAsync(h.Ctx(who, StayChangeCommands.OpMoveRoom),
            new MoveRoomRequest(stay, await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct), h.Rooms[room], "Guest asked", "change", rate), Ct);

    [Fact]
    public async Task A_manager_cannot_extend_or_move_beyond_the_agreed_rate_threshold_inside_the_rupee_limit_and_cannot_lower_the_baseline()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (res, a, _) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-25", 250000, Ct);

        // 2 un-posted nights × ₹400 = ₹800 given up (inside the Manager's ₹1,000 PERM-APR-001 limit) but 16 % below the agreed ₹2,500: the Owner decides.
        var over = await Assert.ThrowsAsync<HccException>(() => Extend(h, h.Manager, a, "2026-10-26", 210000));
        Assert.Equal(HccErrorCode.FORBIDDEN, over.Code);
        Assert.Equal(StayRateRules.OwnerDecidesText(new Hcc.Domain.Money.Paise(250000), 1500), over.Message);
        Assert.Equal(250000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", Ct));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {a}", Ct));
        var mv = await Assert.ThrowsAsync<HccException>(() => Move(h, h.Manager, a, "102", 210000));
        Assert.Equal(HccErrorCode.FORBIDDEN, mv.Code);

        // Within the threshold: applied and recorded, and the agreed rate stays ₹2,500 (a Manager's rate never becomes the baseline).
        Assert.Equal(215000, (await Extend(h, h.Manager, a, "2026-10-26", 215000)).Value.NightlyRatePaise);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {a} AND old_rate_paise = 250000 AND new_rate_paise = 215000 AND scope = 'today-next' AND over_threshold = 0 AND agreed_rate_paise = 250000 AND agreed_after_paise = 250000 AND tariff_paise = 200000 AND threshold_bps = 1500 AND changed_by = {h.Manager}", Ct));
        Assert.Equal(215000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", Ct));

        // 212,499 is only 1.2 % below the current ₹2,150 but more than 15 % below the agreed ₹2,500: refused on a move, a request on ChangeStayRate.
        var down = await Assert.ThrowsAsync<HccException>(() => Move(h, h.Manager, a, "102", 212499));
        Assert.Equal(HccErrorCode.FORBIDDEN, down.Code);
        Assert.Contains("below the agreed rate", down.Message, StringComparison.Ordinal);
        Assert.Equal("PendingApproval", (await Change(h, h.Manager, a, 212499)).Value.Status);
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RATE_REDUCTION_OVER_THRESHOLD' AND target = 'stay-{a}'", Ct));
    }

    [Fact]
    public async Task The_owner_moves_or_extends_beyond_the_threshold_it_is_flagged_audited_listed_and_becomes_the_agreed_rate()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, b, _) = await h.SeedInHouseAsync("103", "2026-10-24", "2026-10-25", 250000, Ct);
        Assert.Equal(190000, (await Move(h, h.Owner, b, "104", 190000)).Value.NightlyRatePaise); // 24 % below the agreed ₹2,500
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change c JOIN inv.room r ON r.room_id = c.room_id WHERE c.stay_id = {b} AND r.room_no = '104' AND c.over_threshold = 1 AND c.agreed_rate_paise = 250000 AND c.agreed_after_paise = 190000 AND c.approved_by IS NULL", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RATE_REDUCTION_OVER_THRESHOLD' AND target = 'stay-{b}' AND detail LIKE N'%2400 bps below the agreed rate%'", Ct));

        await using (var conn = await h.Platform.Connections.OpenAsync(Ct))
        {
            var line = Assert.Single(await SqlRateReductionDigest.LinesAsync(h.Platform.Connections, conn, null, h.PropertyId, Hotel.Day1, Ct));
            Assert.Equal(("104", 250000L, 190000L, 2400, "Sagar (Owner)", (string?)null), (line.RoomNo, line.AgreedPaise, line.NewPaise, line.ReductionBps, line.ChangedBy, line.ApprovedBy));
        }

        // The Owner's ₹1,900 is now the agreed rate: a Manager may go 15 % below it (₹1,615 floor) within the rupee limit.
        Assert.Equal(170000, (await Extend(h, h.Manager, b, "2026-10-26", 170000)).Value.NightlyRatePaise);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {b} AND new_rate_paise = 170000 AND over_threshold = 0 AND agreed_rate_paise = 190000 AND agreed_after_paise = 190000", Ct));
    }

    // ── af2-fix finding 2: a change that does not move the rate is not blocked by the agreed-rate threshold ──

    [Fact]
    public async Task A_guest_count_only_change_on_a_stay_already_below_the_agreed_floor_is_applied_by_a_manager()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (res, stay, _) = await h.SeedInHouseAsync("105", "2026-10-24", "2026-10-26", 250000, Ct);
        Assert.Equal("Applied", (await Change(h, h.Manager, stay, 215000)).Value.Status); // 14 % below the agreed ₹2,500
        // The Owner tightens the threshold to 10 %: ₹2,150 is now below the agreed floor ₹2,250.
        await h.Get<ConfigurationService>().SetSettingAsync(h.Ctx(h.Owner, ConfigurationService.OpSet),
            new SetSettingRequest(RateControlSettings.ThresholdKey, JsonSerializer.SerializeToElement(1000), null, "CTL-01 test"), Ct);
        var guests = await h.Get<StayRateCommands>().ChangeAsync(h.Ctx(h.Manager, StayRateCommands.OpChangeStayRate), new(stay, null, 1, null, "today-next", "One guest left"), Ct);
        Assert.Equal("Applied", guests.Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {stay} AND old_rate_paise = 215000 AND new_rate_paise = 215000 AND new_people = 1 AND over_threshold = 0", Ct));
        Assert.Equal(215000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", Ct));
        // A rate that does move is still judged: ₹2,140 is below the ₹2,250 floor → a request.
        Assert.Equal("PendingApproval", (await Change(h, h.Manager, stay, 214000)).Value.Status);
    }

    [Fact]
    public async Task The_owner_sets_the_threshold_in_master_configuration()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var cfg = h.Get<ConfigurationService>();
        var refused = await Assert.ThrowsAsync<HccException>(() => cfg.SetSettingAsync(h.Ctx(h.Manager, ConfigurationService.OpSet),
            new SetSettingRequest(RateControlSettings.ThresholdKey, JsonSerializer.SerializeToElement(2500), null, "CTL-01"), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, refused.Code);
        await cfg.SetSettingAsync(h.Ctx(h.Owner, ConfigurationService.OpSet), new SetSettingRequest(RateControlSettings.ThresholdKey, JsonSerializer.SerializeToElement(2500), null, "CTL-01 test"), Ct);
        var (_, stay, _) = await h.SeedInHouseAsync("107", "2026-10-24", "2026-10-26", 250000, Ct);
        Assert.Equal("Applied", (await Change(h, h.Manager, stay, 190000)).Value.Status); // 24 % ≤ 25 %
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {stay} AND threshold_bps = 2500 AND over_threshold = 0", Ct));
    }

    [Fact]
    public async Task The_owner_digest_lists_the_changes_beyond_the_threshold_with_who_what_and_how_much()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, a, _) = await h.SeedInHouseAsync("108", "2026-10-24", "2026-10-26", 250000, Ct);
        var (_, b, _) = await h.SeedInHouseAsync("109", "2026-10-24", "2026-10-26", 250000, Ct);
        var req = await Change(h, h.Manager, a, 190000, "Corporate rate");
        await Decide(h, h.Owner, req.Value.ApprovalId!.Value);
        await Change(h, h.Manager, b, 230000, "Small cut"); // within the threshold: not listed

        await using var conn = await h.Platform.Connections.OpenAsync(Ct);
        var lines = await SqlRateReductionDigest.LinesAsync(h.Platform.Connections, conn, null, h.PropertyId, Hotel.Day1, Ct);
        var line = Assert.Single(lines);
        Assert.Equal(("108", 250000L, 190000L, 200000L, 2400, 1500), (line.RoomNo, line.AgreedPaise, line.NewPaise, line.TariffPaise, line.ReductionBps, line.ThresholdBps));
        Assert.Equal(("Meera (Manager)", "Sagar (Owner)", "Corporate rate"), (line.ChangedBy, line.ApprovedBy, line.Reason));

        // The day close's projector writes the drafts: the e-mail names the line, WhatsApp only counts it.
        await using (var tx = conn.BeginTransaction())
        {
            var evt = new OutboxEvent(1, DigestDraftProjector.BusinessDayClosed, h.PropertyId, JsonSerializer.Serialize(new { propertyId = h.PropertyId, businessDate = "2026-10-24" }), DateTime.UtcNow);
            await h.Get<DigestDraftProjector>().ApplyAsync(evt, conn, tx, Ct);
            tx.Commit();
        }

        var email = (string)(await h.ScalarAsync($"SELECT body FROM digest.draft WHERE property_id = {h.PropertyId} AND business_date = '2026-10-24' AND channel = 'Email'", Ct))!;
        Assert.Contains("Rate reductions beyond the agreed-rate threshold: 1", email, StringComparison.Ordinal);
        Assert.Contains(DigestBodies.RateLine(line), email, StringComparison.Ordinal);
        Assert.Contains("24% below; threshold 15%", email, StringComparison.Ordinal);
        Assert.Contains("by Meera (Manager) · approved by Sagar (Owner)", email, StringComparison.Ordinal);
        Assert.DoesNotContain("Room 109", email, StringComparison.Ordinal);
        var wa = (string)(await h.ScalarAsync($"SELECT body FROM digest.draft WHERE property_id = {h.PropertyId} AND business_date = '2026-10-24' AND channel = 'WhatsApp'", Ct))!;
        Assert.Contains("Rate cuts beyond threshold 1", wa, StringComparison.Ordinal);
        Assert.DoesNotContain("₹", wa, StringComparison.Ordinal);
        Assert.DoesNotContain("Meera", wa, StringComparison.Ordinal);
    }
}
