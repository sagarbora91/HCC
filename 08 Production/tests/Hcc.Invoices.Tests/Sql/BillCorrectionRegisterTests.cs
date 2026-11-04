using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>AF-CTL02 (CTL-02): the correction register carries the requester, the approval, the decider and the stored changes (additive over the old row).</summary>
[Trait("Category", "Sql")]
public class BillCorrectionRegisterTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Hotel H, long Folio, string InvoiceNo)> DepartedAsync(string room)
    {
        var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)', business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        await h.PayAsync(folio, 210000, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, version), Ct);
        var r = await h.Get<InvoiceService>().IssueInvoiceAsync(h.Ctx(h.Owner, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", folio), Ct);
        return (h, folio, r.Value.InvoiceNo);
    }

    [Fact]
    public async Task Correction_register_row_carries_requester_approval_decider_and_changes()
    {
        var (h, _, invoiceNo) = await DepartedAsync("101");
        await using var _h = h;
        var svc = h.Get<BillCorrectionService>();
        var req = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest),
            new(invoiceNo, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 150000)], "Agreed corporate rate"), Ct);
        await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct);

        var row = Assert.Single(await svc.ListAsync(h.PropertyId, h.Owner, Ct));
        Assert.Equal(req.Value.CorrectionId, row.CorrectionId);
        Assert.Equal(h.Reception, row.RequestedBy);
        Assert.Equal(req.Value.ApprovalId, row.ApprovalId);
        Assert.Equal(h.Owner, row.DecidedBy);
        Assert.NotNull(row.DecidedUtc);
        var change = Assert.Single(row.Changes);
        Assert.Equal(("room-rate", new DateOnly(2026, 10, 23), 150000L), (change.Kind, change.Night, change.NewRatePaise));
        Assert.Equal(200000L, change.OldRatePaise);
    }

    [Fact]
    public async Task Pending_correction_has_null_decider()
    {
        var (h, _, invoiceNo) = await DepartedAsync("102");
        await using var _h = h;
        var svc = h.Get<BillCorrectionService>();
        var req = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest),
            new(invoiceNo, [new BillChangeIn("add-line", Label: "Laundry missed at check-out", AmountPaise: 30000, GstCategory: "laundry", ServiceDate: new DateOnly(2026, 10, 23))], "Laundry slip"), Ct);

        var row = Assert.Single(await svc.ListAsync(h.PropertyId, h.Owner, Ct));
        Assert.Equal("Pending", row.Status);
        Assert.Equal(h.Reception, row.RequestedBy);
        Assert.Equal(req.Value.ApprovalId, row.ApprovalId);
        Assert.Null(row.DecidedBy);
        Assert.Null(row.DecidedUtc);
        Assert.Single(row.Changes);
    }
}
