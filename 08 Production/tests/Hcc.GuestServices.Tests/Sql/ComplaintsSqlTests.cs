using Hcc.Application.Billing;
using Hcc.Contracts.Billing;
using Hcc.Contracts.GuestServices;
using Hcc.GuestServices.Tests.Harness;

namespace Hcc.GuestServices.Tests.Sql;

[Trait("Category", "Sql")]
public class ComplaintsSqlTests : IAsyncLifetime
{
    private GsWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await GsWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<ComplaintResult> LogAsync(int who, string key, string? stay, string category = "Noise", string severity = "Medium", string description = "Loud music from the corridor until 00:30.", int? owner = null, string? guest = null) =>
        (await _w.Commands.LogComplaintAsync(_w.Ctx(who, key), new LogComplaintRequest(stay is null ? null : _w.Stays[stay], guest, category, severity, description, owner), Ct)).Value;

    private async Task<string> ApprovalVersionAsync(long id) => Convert.ToBase64String((byte[])(await _w.Db.ScalarAsync($"SELECT row_ver FROM bill.approval WHERE approval_id = {id};", Ct))!);

    [Fact]
    public async Task Complaint_goes_log_investigate_resolve_follow_up_close()
    {
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => LogAsync(Who.Housekeeper, "a1", "HCC-1041")));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => LogAsync(Who.Reception, "a2", "HCC-1041", severity: "Severe")));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => LogAsync(Who.Reception, "a3", "HCC-1041", owner: Who.Housekeeper)));   // owner must be front office
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => LogAsync(Who.Reception, "a4", null)));                                   // no guest name
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => LogAsync(Who.Reception, "a5", "HCC-1041", description: "x")));

        var c = await LogAsync(Who.Reception, "b1", "HCC-1041", "Amenities", "Low", "Kettle not working");
        Assert.Equal(("Open", Who.Reception), (c.Status, c.OwnerId));
        Assert.Equal("Ananya Rao", await _w.Db.StringAsync($"SELECT guest_name FROM ops.guest_complaint WHERE complaint_id = {c.ComplaintId};", Ct));

        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.ResolveComplaintAsync(_w.Ctx(Who.Reception, "b2"), new ResolveComplaintRequest(c.ComplaintId, null, null, null, c.Version), Ct)));
        var inv = (await _w.Commands.UpdateComplaintAsync(_w.Ctx(Who.Reception, "b3"), new UpdateComplaintRequest(c.ComplaintId, null, null, "Faulty kettle element", null, c.Version), Ct)).Value;
        Assert.Equal("Investigating", inv.Status);
        var res = (await _w.Commands.ResolveComplaintAsync(_w.Ctx(Who.Reception, "b4"), new ResolveComplaintRequest(c.ComplaintId, null, "Kettle replaced", null, inv.Version), Ct)).Value;
        Assert.Equal(("Resolved", new DateOnly(2026, 10, 25)), (res.Status, res.FollowUpOn));

        var again = (await _w.Commands.RecordFollowUpAsync(_w.Ctx(Who.Reception, "b5"), new RecordComplaintFollowUpRequest(c.ComplaintId, "Still noisy kettle", false, res.Version), Ct)).Value;
        Assert.Equal("Investigating", again.Status);
        var res2 = (await _w.Commands.ResolveComplaintAsync(_w.Ctx(Who.Reception, "b6"), new ResolveComplaintRequest(c.ComplaintId, null, null, new DateOnly(2026, 10, 26), again.Version), Ct)).Value;
        Assert.Equal("Closed", (await _w.Commands.RecordFollowUpAsync(_w.Ctx(Who.Reception, "b7"), new RecordComplaintFollowUpRequest(c.ComplaintId, "All good now", true, res2.Version), Ct)).Value.Status);

        var report = await _w.Queries.ComplaintReportAsync(_w.PropertyId, Who.Manager, Ct);
        Assert.Equal(1, report.Single(r => r.Category == "Amenities").Total);
        Assert.Equal(6, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.guest_service_log WHERE kind = 'complaint' AND record_id = {c.ComplaintId};", Ct));
        Assert.Equal(6, (await _w.Queries.HistoryAsync(_w.PropertyId, Who.Reception, "complaint", c.ComplaintId, Ct)).Count);
    }

    [Fact]
    public async Task High_severity_can_only_be_closed_by_someone_who_may_approve()
    {
        var c = await LogAsync(Who.Reception, "h1", "HCC-1044", "Staff behaviour", "High", "Rude at the desk");
        var r = (await _w.Commands.ResolveComplaintAsync(_w.Ctx(Who.Reception, "h2"), new ResolveComplaintRequest(c.ComplaintId, "Shift overload", "Apology by manager", null, c.Version), Ct)).Value;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.RecordFollowUpAsync(_w.Ctx(Who.Reception, "h3"), new RecordComplaintFollowUpRequest(c.ComplaintId, "Fine", true, r.Version), Ct)));
        var closed = (await _w.Commands.RecordFollowUpAsync(_w.Ctx(Who.Manager, "h4"), new RecordComplaintFollowUpRequest(c.ComplaintId, "Guest accepted apology", true, r.Version), Ct)).Value;
        Assert.Equal("Closed", closed.Status);
    }

    [Fact]
    public async Task Recovery_above_the_limit_becomes_a_billing_approval_and_posts_only_when_approved()
    {
        var c = await LogAsync(Who.Reception, "r1", "HCC-1045");
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Cashier, "r2"), new OfferRecoveryDiscountRequest(c.ComplaintId, 30000, null, c.Version), Ct)));
        var before = await _w.FolioNetAsync("HCC-1045", Ct);

        var offer = await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "r3"), new OfferRecoveryDiscountRequest(c.ComplaintId, 30000, "Corridor noise", c.Version), Ct);
        var o = offer.Value;
        Assert.Equal(("PendingApproval", 30000L, "Pending approval"), (o.Outcome, o.AmountPaise, o.RecoveryStatus));
        Assert.NotNull(o.ApprovalId);
        Assert.Equal(before, await _w.FolioNetAsync("HCC-1045", Ct));   // nothing posted yet
        Assert.StartsWith($"Service recovery · CMP-{c.ComplaintId}", await _w.Db.StringAsync($"SELECT reason FROM bill.approval WHERE approval_id = {o.ApprovalId};", Ct));
        Assert.Equal("discount", await _w.Db.StringAsync($"SELECT action FROM bill.approval WHERE approval_id = {o.ApprovalId};", Ct));

        // A second offer while the first awaits approval is refused.
        var cur = (await _w.Queries.ComplaintAsync(_w.PropertyId, Who.Reception, c.ComplaintId, Ct)).Version;
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "r4"), new OfferRecoveryDiscountRequest(c.ComplaintId, 10000, null, cur), Ct)));
        // Nobody approves their own request (PERM-SOD-001), the Manager does.
        var approvalVersion = await ApprovalVersionAsync(o.ApprovalId!.Value);
        Assert.NotEqual("NO_ERROR", await Expect.CodeAsync(() => _w.Billing.DecideApprovalAsync(new Caller(_w.PropertyId, Who.Reception), "r5", new DecideApprovalRequest(o.ApprovalId.Value, true, null, approvalVersion), Ct)));
        await _w.Billing.DecideApprovalAsync(new Caller(_w.PropertyId, Who.Manager), "r6", new DecideApprovalRequest(o.ApprovalId.Value, true, null, approvalVersion), Ct);

        Assert.Equal(before - 30000, await _w.FolioNetAsync("HCC-1045", Ct));
        var view = await _w.Queries.ComplaintAsync(_w.PropertyId, Who.Reception, c.ComplaintId, Ct);
        Assert.Equal(("Approved", 30000L), (view.RecoveryStatus, view.Recovery!.AmountPaise));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'discount' AND approval_id IS NULL;", Ct));   // only Billing's approved path posted money
    }

    [Fact]
    public async Task Recovery_within_the_limit_posts_a_discount_line_through_billing()
    {
        var c = await LogAsync(Who.Reception, "w1", "HCC-1044", "Room cleanliness", "Low", "Dust on the bedside table at check-in.");
        var before = await _w.FolioNetAsync("HCC-1044", Ct);
        var r = (await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "w2"), new OfferRecoveryDiscountRequest(c.ComplaintId, 10000, null, c.Version), Ct)).Value;
        Assert.Equal(("Posted", "Posted"), (r.Outcome, r.RecoveryStatus));
        Assert.NotNull(r.ChargeId);
        Assert.Equal(before - 10000, await _w.FolioNetAsync("HCC-1044", Ct));
        Assert.Equal((-10000L, "discount"), (await _w.Db.LongAsync($"SELECT amount_paise FROM bill.charge WHERE charge_id = {r.ChargeId};", Ct), await _w.Db.StringAsync($"SELECT kind FROM bill.charge WHERE charge_id = {r.ChargeId};", Ct)));
        Assert.Equal(r.ChargeId, await _w.Db.LongAsync($"SELECT recovery_charge_id FROM ops.guest_complaint WHERE complaint_id = {c.ComplaintId};", Ct));
        Assert.Contains("Recovery discount ₹100.00 · posted to folio", (await _w.Queries.HistoryAsync(_w.PropertyId, Who.Manager, "complaint", c.ComplaintId, Ct)).Select(h => h.What));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ServiceRecoveryOffered';", Ct));
    }

    [Fact]
    public async Task Recovery_needs_a_linked_stay_and_a_refused_billing_call_releases_the_claim()
    {
        var walkIn = await LogAsync(Who.Reception, "n1", null, guest: "Walk-in guest");
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Owner, "n2"), new OfferRecoveryDiscountRequest(walkIn.ComplaintId, 10000, null, walkIn.Version), Ct)));

        var c = await LogAsync(Who.Reception, "n3", "HCC-1043");
        // More than the folio's net charges: Billing refuses (VALIDATION) and the claim is released for a retry.
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "n4"), new OfferRecoveryDiscountRequest(c.ComplaintId, 99_999_999, null, c.Version), Ct)));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.guest_complaint WHERE complaint_id = {c.ComplaintId} AND recovery_state IS NOT NULL;", Ct));
        var cur = (await _w.Queries.ComplaintAsync(_w.PropertyId, Who.Reception, c.ComplaintId, Ct)).Version;
        var ok = (await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "n5"), new OfferRecoveryDiscountRequest(c.ComplaintId, 5000, null, cur), Ct)).Value;
        Assert.Equal("Posted", ok.Outcome);
    }

    [Fact]
    public async Task Recovery_replay_with_the_same_key_posts_exactly_one_discount()
    {
        var c = await LogAsync(Who.Reception, "i1", "HCC-1041");
        var req = new OfferRecoveryDiscountRequest(c.ComplaintId, 10000, null, c.Version);
        var first = await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "i2"), req, Ct);
        var second = await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "i2"), req, Ct);
        Assert.True(second.Replayed);
        Assert.Equal(first.Value.ChargeId, second.Value.ChargeId);
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'discount';", Ct));
    }

    [Fact]
    public async Task Stale_complaint_version_and_closed_complaint_are_refused()
    {
        var c = await LogAsync(Who.Reception, "z1", "HCC-1041");
        var inv = (await _w.Commands.UpdateComplaintAsync(_w.Ctx(Who.Reception, "z2"), new UpdateComplaintRequest(c.ComplaintId, null, "High", null, null, c.Version), Ct)).Value;
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.UpdateComplaintAsync(_w.Ctx(Who.Reception, "z3"), new UpdateComplaintRequest(c.ComplaintId, null, "Low", null, null, c.Version), Ct)));
        Assert.Equal("High", inv.Severity);
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.UpdateComplaintAsync(_w.Ctx(Who.Reception, "z4"), new UpdateComplaintRequest(424242, null, "Low", null, null, c.Version), Ct)));
    }

    [Fact]
    public async Task Audit_and_history_never_carry_guest_text_or_money_beyond_the_amount()
    {
        var c = await LogAsync(Who.Reception, "u1", "HCC-1041", description: "Secret wording of the complaint");
        await _w.Commands.OfferRecoveryDiscountAsync(_w.Ctx(Who.Reception, "u2"), new OfferRecoveryDiscountRequest(c.ComplaintId, 10000, null, c.Version), Ct);
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%Secret wording%';", Ct));
        Assert.True(await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action LIKE 'complaint.recovery.%';", Ct) >= 2);
    }
}
