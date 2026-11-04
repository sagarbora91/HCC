using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>AF-VERIFY (wave 4): hostile attempt V-REG-01 against the correction register row (CTL-02): three corrections (issued / rejected / pending) with different change counts must not bleed into each other.</summary>
[Trait("Category", "Sql")]
public class AuditFixVerifyRegisterTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> DepartAsync(Hotel h, string room)
    {
        var (_, stay, folio) = await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        await h.PayAsync(folio, 210000, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, version), Ct);
        var r = await h.Get<InvoiceService>().IssueInvoiceAsync(h.Ctx(h.Owner, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", folio), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        return r.Value.InvoiceNo;
    }

    [Fact]
    public async Task V_REG_01_issued_rejected_and_pending_rows_keep_their_own_decider_approval_and_changes()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)', business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var inv1 = await DepartAsync(h, "101");
        var inv2 = await DepartAsync(h, "102");
        var inv3 = await DepartAsync(h, "104");
        var svc = h.Get<BillCorrectionService>();
        var a = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest), new(inv1, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 150000)], "Agreed corporate rate"), Ct);
        var b = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest), new(inv2, [
            new BillChangeIn("add-line", Label: "Laundry A", AmountPaise: 30000, GstCategory: "laundry", ServiceDate: new DateOnly(2026, 10, 23)),
            new BillChangeIn("add-line", Label: "Laundry B", AmountPaise: 20000, GstCategory: "laundry", ServiceDate: new DateOnly(2026, 10, 23))], "Laundry slips"), Ct);
        var c = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest), new(inv3, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 180000)], "Rate typo"), Ct);
        await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(a.Value.ApprovalId, true, null, a.Value.ApprovalVersion), Ct);
        await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(b.Value.ApprovalId, false, "Not a hotel charge", b.Value.ApprovalVersion), Ct);

        var rows = (await svc.ListAsync(h.PropertyId, h.Owner, Ct)).ToDictionary(r => r.CorrectionId);
        Assert.Equal(3, rows.Count); // the LEFT JOINs add no duplicate rows
        Assert.Equal(["Issued"], [rows[a.Value.CorrectionId].Status]);
        Assert.Single(rows[a.Value.CorrectionId].Changes);
        Assert.Equal(2, rows[b.Value.CorrectionId].Changes.Count);
        Assert.Equal(["Laundry A", "Laundry B"], rows[b.Value.CorrectionId].Changes.Select(x => x.Label));
        Assert.Single(rows[c.Value.CorrectionId].Changes);
        Assert.Equal(150000L, rows[a.Value.CorrectionId].Changes[0].NewRatePaise);
        Assert.Equal(180000L, rows[c.Value.CorrectionId].Changes[0].NewRatePaise);
        Assert.Equal(h.Owner, rows[a.Value.CorrectionId].DecidedBy);
        Assert.Equal(h.Owner, rows[b.Value.CorrectionId].DecidedBy);
        Assert.NotEqual("Issued", rows[b.Value.CorrectionId].Status);
        Assert.Null(rows[c.Value.CorrectionId].DecidedBy);
        Assert.Null(rows[c.Value.CorrectionId].DecidedUtc);
        Assert.Equal(a.Value.ApprovalId, rows[a.Value.CorrectionId].ApprovalId);
        Assert.Equal(b.Value.ApprovalId, rows[b.Value.CorrectionId].ApprovalId);
        Assert.Equal(c.Value.ApprovalId, rows[c.Value.CorrectionId].ApprovalId);
        Assert.All(rows.Values, r => Assert.Equal(h.Reception, r.RequestedBy));
        Assert.Equal(rows.Keys.OrderByDescending(k => k), (await svc.ListAsync(h.PropertyId, h.Owner, Ct)).Select(r => r.CorrectionId)); // newest first
    }
}
