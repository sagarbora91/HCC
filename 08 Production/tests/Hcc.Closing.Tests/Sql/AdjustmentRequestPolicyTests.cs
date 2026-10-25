using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// RequestAdjustment with the REAL P1 policy and the DEC-063 seed (fix for the bug P6 found): a requester whose grant has an amount
/// limit ("L") or is request-only ("R") must get an approval request, not APPROVAL_REQUIRED. Reception discount limit ₹250, Manager
/// ₹1,000 (also the Manager's PERM-APR-001 approval limit, G1); Cashier holds no discount permission (FORBIDDEN).
/// </summary>
[Trait("Category", "Sql")]
public sealed class AdjustmentRequestPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Hotel H, long Folio)> FolioWithOneNightAsync()
    {
        var h = await Hotel.CreateAsync(Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync("105", "2026-10-24", "2026-10-26", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, Hotel.Day1), Ct); // net ₹2,000
        return (h, folio);
    }

    private static async Task<string> VersionAsync(Hotel h, long folio) => (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Version;

    private static async Task<AdjustmentResult> RequestAsync(Hotel h, int who, string action, long folio, long? target, long amount, string reason)
        => (await h.Billing.RequestAdjustmentAsync(h.As(who), Hotel.Key(), new RequestAdjustmentRequest(action, folio, target, amount, reason, await VersionAsync(h, folio)), Ct)).Value;

    [Fact]
    public async Task Reception_500_discount_goes_to_pending_approval_and_the_Manager_approves_it()
    {
        var (h, folio) = await FolioWithOneNightAsync();
        await using var _ = h;

        // Within Reception's ₹250 limit: posted directly.
        var small = await RequestAsync(h, h.Reception, "discount", folio, null, 20000, "Late check-in goodwill");
        Assert.Equal(("Posted", (long?)null), (small.Outcome, small.ApprovalId));

        // ₹500 is above Reception's limit: an approval request is created (was APPROVAL_REQUIRED).
        var req = await RequestAsync(h, h.Reception, "discount", folio, null, 50000, "Service delay goodwill");
        Assert.Equal("PendingApproval", req.Outcome);
        Assert.Equal(("Pending", (long)h.Reception), ((string)(await h.ScalarAsync($"SELECT status FROM bill.approval WHERE approval_id = {req.ApprovalId}", Ct))!,
            await h.LongAsync($"SELECT requested_by FROM bill.approval WHERE approval_id = {req.ApprovalId}", Ct)));

        // The Manager approves (≤ ₹1,000): the discount line posts at the room-night GST rate.
        var decided = (await h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(req.ApprovalId!.Value, true, "OK", req.ApprovalVersion!), Ct)).Value;
        Assert.Equal(("Executed", 50000L), (decided.Status, decided.AmountPaise));
        var totals = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Totals;
        Assert.Equal(200000 - 20000 - 50000, totals.NetPaise);
    }

    [Fact]
    public async Task Discount_above_1000_is_requested_but_the_Manager_cannot_approve_it_the_Owner_can()
    {
        var (h, folio) = await FolioWithOneNightAsync();
        await using var _ = h;
        var req = await RequestAsync(h, h.Reception, "discount", folio, null, 150000, "Long-stay goodwill");
        Assert.Equal("PendingApproval", req.Outcome);
        await Expect.Fails(HccErrorCode.APPROVAL_REQUIRED, () => h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(),
            new DecideApprovalRequest(req.ApprovalId!.Value, true, null, req.ApprovalVersion!), Ct));
        var decided = (await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.ApprovalId!.Value, true, null, req.ApprovalVersion!), Ct)).Value;
        Assert.Equal("Executed", decided.Status);

        // A Manager's own discount within ₹1,000 still posts directly.
        Assert.Equal("Posted", (await RequestAsync(h, h.Manager, "discount", folio, null, 10000, "Manager goodwill")).Outcome);
    }

    [Fact]
    public async Task Cashier_discount_is_refused_per_DEC_063_G1()
    {
        var (h, folio) = await FolioWithOneNightAsync();
        await using var _ = h;
        var v = await VersionAsync(h, folio);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Billing.RequestAdjustmentAsync(h.As(h.Cashier), Hotel.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 10000, "Goodwill", v), Ct));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM bill.approval WHERE folio_id = {folio}", Ct));
    }

    [Fact]
    public async Task Request_only_staff_create_refund_and_correction_requests()
    {
        var (h, folio) = await FolioWithOneNightAsync();
        await using var _ = h;
        var receipt = (await h.PayAsync(folio, 100000, "UPI", who: h.Reception, ct: Ct)).Value;
        var laundry = (await h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(folio, "Laundry service", 50000), Ct)).Value;

        // Reception: PERM-PAY-003 refund.request = R, PERM-FOL-005 correction = R; Cashier: correction = R.
        var refund = await RequestAsync(h, h.Reception, "refund", folio, receipt.ReceiptId, 20000, "Overpaid advance");
        var correction = await RequestAsync(h, h.Reception, "correction", folio, laundry.ChargeId, 50000, "Posted to the wrong folio");
        Assert.Equal(("PendingApproval", "PendingApproval"), (refund.Outcome, correction.Outcome));
        Assert.Equal(2, await h.LongAsync($"SELECT COUNT(*) FROM bill.approval WHERE folio_id = {folio} AND status = 'Pending' AND requested_by = {h.Reception}", Ct));

        var cashierRefund = await RequestAsync(h, h.Cashier, "refund", folio, receipt.ReceiptId, 10000, "Second refund request");
        Assert.Equal("PendingApproval", cashierRefund.Outcome);

        // The Owner approves the correction: the reversal copies the original laundry line's GST.
        var decided = (await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(correction.ApprovalId!.Value, true, null, correction.ApprovalVersion!), Ct)).Value;
        Assert.Equal("Executed", decided.Status);
        Assert.Equal((-9000L, "laundry"), (await h.LongAsync($"SELECT tax_paise FROM bill.charge WHERE reverses_charge_id = {laundry.ChargeId}", Ct),
            (string)(await h.ScalarAsync($"SELECT g.category FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.reverses_charge_id = {laundry.ChargeId}", Ct))!));
    }
}
