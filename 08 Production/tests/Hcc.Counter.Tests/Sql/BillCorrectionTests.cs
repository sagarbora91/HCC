using Hcc.Application.Accounting;
using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF A2 on a disposable HccTest_* hotel: a departed, invoiced stay; RequestBillCorrection (Reception) → only the Owner decides → full credit note,
/// corrected lines, a revision-2 invoice of exactly the corrected lines, supersession, settlement refund-due / balance-due; a balance-due is paid on the
/// departed folio for exactly that balance; a superseded invoice cannot be corrected again.</summary>
public sealed class BillCorrectionTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static async Task<string> InvoiceOfAsync(Hotel h, long folio)
    {
        var existing = await h.ScalarAsync($"SELECT TOP 1 invoice_no FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {folio} ORDER BY revision DESC", Ct);
        if (existing is string s) return s;
        var r = await h.Get<InvoiceService>().IssueInvoiceAsync(h.Ctx(h.Owner, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", folio), Ct);
        return r.Value.InvoiceNo;
    }

    [Fact]
    public async Task Lower_night_rent_credits_reissues_and_leaves_a_refund_due()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)', business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync("101", "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        await h.PayAsync(folio, 210000, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, version), Ct);
        var oldNo = await InvoiceOfAsync(h, folio);
        var svc = h.Get<BillCorrectionService>();

        var req = await svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest),
            new(oldNo, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 150000)], "Agreed corporate rate"), Ct);
        Assert.Equal((210000L, 157500L, -52500L, "refund-due"), (req.Value.OldTotalPaise, req.Value.NewTotalPaise, req.Value.DeltaPaise, req.Value.Settlement));
        await Assert.ThrowsAsync<HccException>(() => svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest),
            new(oldNo, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 140000)], "again"), Ct));
        var mgr = await Assert.ThrowsAnyAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct));
        Assert.NotEqual(HccErrorCode.VALIDATION, mgr.Code);

        var ok = await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct);
        Assert.Equal("Executed", ok.Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {folio} AND revision = 2 AND total_paise = 157500", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.invoice_supersession s JOIN ivc.invoice o ON o.invoice_id = s.invoice_id WHERE o.invoice_no = '{oldNo}'", Ct));
        // LF-int: the print notes on both documents.
        var oldId = await h.LongAsync($"SELECT invoice_id FROM ivc.invoice WHERE invoice_no = '{oldNo}'", Ct);
        var newId = await h.LongAsync($"SELECT invoice_id FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {folio} AND revision = 2", Ct);
        var newNo = (string)(await h.ScalarAsync($"SELECT invoice_no FROM ivc.invoice WHERE invoice_id = {newId}", Ct))!;
        var q = h.Get<InvoiceQueries>();
        Assert.Contains("Superseded by " + newNo + " (credit note ", (await q.RenderInvoiceAsync(h.PropertyId, h.Owner, oldId, "text", Ct)).Content, StringComparison.Ordinal);
        Assert.Contains("Supersedes " + oldNo, (await q.RenderInvoiceAsync(h.PropertyId, h.Owner, newId, "text", Ct)).Content, StringComparison.Ordinal);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.bill_correction WHERE folio_id = {folio} AND status = 'Issued' AND settlement = 'refund-due' AND credit_note_no IS NOT NULL", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.credit_note n JOIN ivc.invoice o ON o.invoice_id = n.invoice_id WHERE o.invoice_no = '{oldNo}' AND n.total_paise = 210000", Ct));
        // Folio: original 2,100 + credit −2,100 + corrected 1,575 = 1,575 charged; 2,100 paid → 525 owed back to the guest.
        Assert.Equal(150000, await h.LongAsync($"SELECT SUM(amount_paise) FROM bill.charge WHERE folio_id = {folio}", Ct));
        Assert.Equal(-52500, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Totals.BalancePaise);
        await Assert.ThrowsAsync<HccException>(() => svc.RequestAsync(h.Ctx(h.Reception, BillCorrectionService.OpRequest),
            new(oldNo, [new BillChangeIn("room-rate", Night: new DateOnly(2026, 10, 23), NewRatePaise: 160000)], "superseded"), Ct));
        Assert.Single(await svc.ListAsync(h.PropertyId, h.Owner, Ct));
    }

    [Fact]
    public async Task Added_charge_leaves_a_balance_paid_on_the_departed_folio()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)', business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync("102", "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        await h.PayAsync(folio, 210000, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, version), Ct);
        var oldNo = await InvoiceOfAsync(h, folio);
        var svc = h.Get<BillCorrectionService>();
        var req = await svc.RequestAsync(h.Ctx(h.Cashier, BillCorrectionService.OpRequest),
            new(oldNo, [new BillChangeIn("add-line", Label: "Laundry missed at check-out", AmountPaise: 30000, GstCategory: "laundry", ServiceDate: new DateOnly(2026, 10, 23))], "Laundry slip found"), Ct);
        Assert.Equal("balance-due", req.Value.Settlement);
        await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct);
        var due = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Totals.BalancePaise;
        Assert.Equal(req.Value.DeltaPaise, due);
        await Assert.ThrowsAsync<HccException>(() => h.PayAsync(folio, due - 1, "UPI", ct: Ct));
        await h.PayAsync(folio, due, "UPI", ct: Ct);
        Assert.Equal(0, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Totals.BalancePaise);
    }

    [Fact]
    public void Credit_note_and_corrected_lines_carry_only_the_delta()
    {
        // CN reversal of a ₹2,000 night at 5 % and the corrected ₹1,500 night: both post to the adjustments ledger; each voucher balances.
        var cn = AccountingEventMapper.Charge(9, 101, "correction", new DateOnly(2026, 10, 24), -200000, -5000, -5000);
        var fix = AccountingEventMapper.Charge(9, 102, "correction", new DateOnly(2026, 10, 24), 150000, 3750, 3750);
        Assert.Equal(cn.Debit, cn.Credit);
        Assert.Equal(fix.Debit, fix.Credit);
        long Adj(Hcc.Domain.Accounting.AccountingSource s) => s.Entries.Where(e => e.MapKey == "line:adjustment").Sum(e => e.CreditPaise - e.DebitPaise);
        Assert.Equal(-50000, Adj(cn) + Adj(fix));
    }
}
