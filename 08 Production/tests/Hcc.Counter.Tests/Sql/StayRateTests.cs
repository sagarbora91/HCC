using Hcc.Application.Stays;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF A1 ChangeStayRate on a disposable HccTest_* hotel (business date 24 Oct 2026, Deluxe tariff ₹2,000): direct change by the Owner re-posts
/// tonight's posted night (reversal + new line, 4686 index lets the second room line in); Reception requests and a Manager executes within the
/// 15 % limit; a Manager cannot decide a rate beyond the limit, the Owner can; the requester never decides.</summary>
public sealed class StayRateTests
{
    [Fact]
    public async Task Owner_change_reposts_tonight_and_moves_the_booking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Hotel.CreateAsync(ct);
        var (res, stay, folio) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-27", 200000, ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, Hotel.Day1), ct);
        var c = h.Get<StayRateCommands>();

        var r = await c.ChangeAsync(h.Ctx(h.Owner, StayRateCommands.OpChangeStayRate), new(stay, 180000, null, null, "today-next", "Regular guest"), ct);
        Assert.Equal("Applied", r.Value.Status);
        Assert.Equal(Hotel.Day1, r.Value.EffectiveFrom);
        var night = Assert.Single(r.Value.Reposted);
        Assert.Equal(Hotel.Day1, night.Night);

        // Live room lines: one per night, at the new rate; the reversal nets the old line to zero.
        Assert.Equal(180000, await h.LongAsync($"SELECT SUM(amount_paise) FROM bill.charge WHERE folio_id = {folio}", ct));
        Assert.Equal(night.ReversalChargeId, await h.LongAsync($"SELECT reversed_by_charge_id FROM bill.charge WHERE folio_id = {folio} AND kind = 'room' AND amount_paise = 200000", ct));
        Assert.Equal(180000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {stay} AND old_rate_paise = 200000 AND new_rate_paise = 180000 AND approval_id IS NULL", ct));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM inv.reservation_night WHERE reservation_id = {res} AND night >= '2026-10-24' AND rate_paise <> 180000", ct));

        // Re-posting the same night again is idempotent through the night audit: the live line is found, nothing new.
        var again = await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, Hotel.Day1), ct);
        Assert.False(again.Value.Created);

        // The append-only trigger still refuses any other edit of a charge.
        await Assert.ThrowsAnyAsync<Exception>(() => h.ScalarAsync($"UPDATE bill.charge SET label = N'x' WHERE folio_id = {folio}", ct));
    }

    [Fact]
    public async Task Reception_requests_and_manager_decides_within_the_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Hotel.CreateAsync(ct);
        var (res, stay, _) = await h.SeedInHouseAsync("102", "2026-10-24", "2026-10-27", 200000, ct);
        var c = h.Get<StayRateCommands>();

        var refused = await Assert.ThrowsAsync<HccException>(() => c.ChangeAsync(h.Ctx(h.Reception, StayRateCommands.OpChangeStayRate), new(stay, 200000, 2, 0, "next", "same"), ct));
        Assert.Equal(HccErrorCode.VALIDATION, refused.Code);

        var req = await c.ChangeAsync(h.Ctx(h.Reception, StayRateCommands.OpChangeStayRate), new(stay, 175000, null, null, "next", "Long stay"), ct);
        Assert.Equal("PendingApproval", req.Value.Status);
        var approvalId = req.Value.ApprovalId!.Value;
        await Assert.ThrowsAsync<HccException>(() => c.ChangeAsync(h.Ctx(h.Reception, StayRateCommands.OpChangeStayRate), new(stay, 170000, null, null, "next", "again"), ct));

        var v = await h.VersionAsync($"SELECT row_ver FROM bill.approval WHERE approval_id = {approvalId}", ct);
        var own = await Assert.ThrowsAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Reception), Hotel.Key(), new DecideApprovalRequest(approvalId, true, null, v), ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, own.Code);
        var done = await h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(approvalId, true, "ok", v), ct);
        Assert.Equal("Executed", done.Value.Status);
        Assert.Equal(175000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM gst.stay_rate_change WHERE stay_id = {stay} AND approval_id = {approvalId} AND approved_by = {h.Manager} AND effective_from = '2026-10-25'", ct));
    }

    [Fact]
    public async Task Beyond_the_manager_limit_only_the_owner_decides()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Hotel.CreateAsync(ct);
        var (res, stay, _) = await h.SeedInHouseAsync("103", "2026-10-24", "2026-10-26", 200000, ct);
        var c = h.Get<StayRateCommands>();
        var req = await c.ChangeAsync(h.Ctx(h.Manager, StayRateCommands.OpChangeStayRate), new(stay, 150000, null, null, "all", "Corporate"), ct); // 25 % below
        Assert.Equal("PendingApproval", req.Value.Status);
        var id = req.Value.ApprovalId!.Value;
        var v = await h.VersionAsync($"SELECT row_ver FROM bill.approval WHERE approval_id = {id}", ct);
        var beyond = await Assert.ThrowsAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Manager2), Hotel.Key(), new DecideApprovalRequest(id, true, null, v), ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, beyond.Code);
        var ok = await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(id, true, null, v), ct);
        Assert.Equal("Executed", ok.Value.Status);
        Assert.Equal(150000, await h.LongAsync($"SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = {res}", ct));

        // A raise of guests beyond the room's capacity (Deluxe: 3) is refused.
        var cap = await Assert.ThrowsAsync<HccException>(() => c.ChangeAsync(h.Ctx(h.Owner, StayRateCommands.OpChangeStayRate), new(stay, null, 3, 1, "next", "family"), ct));
        Assert.Equal(HccErrorCode.VALIDATION, cap.Code);
    }
}
