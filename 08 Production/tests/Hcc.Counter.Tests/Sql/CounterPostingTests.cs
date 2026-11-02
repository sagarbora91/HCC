using Hcc.Application.Accounting;
using Hcc.Application.Counter;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF A7 counter bills: the Sales voucher and its credit-note mirror balance (SourceRules.Counter); on a disposable HccTest_* hotel a bill is
/// issued with an INV tax invoice (source 'counter'), settled by UPI or on Udhaar (receivable linked, 4691), voided only through the Owner's
/// 'counter-credit' approval → credit note, status Credited, receivable void; a discount above the caller's limit is refused.</summary>
public sealed class CounterPostingTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Sale_and_credit_note_are_balanced_mirrors()
    {
        var e = new CounterBillPostedEvent(5, "INV/2627/0007", new DateOnly(2026, 10, 24), "udhaar", null, "Walk-in (fictional)", "9820001234", null,
            FoodPaise: 50000, RoomPaise: 0, ServicePaise: 20000, DiscountPaise: 5000, CgstPaise: 1625, SgstPaise: 1625, TotalPaise: 68250);
        var sale = AccountingEventMapper.CounterBill(e, credit: false);
        var cn = AccountingEventMapper.CounterBill(e with { CreditNoteNo = "CRN/2627/0001" }, credit: true);
        Assert.Equal(sale.Debit, sale.Credit);
        Assert.Equal(cn.Debit, cn.Credit);
        Assert.Equal(68250 + 5000, sale.Debit);
        var dr = Assert.Single(sale.Entries, x => x.MapKey == SourceRules.UdhaarKey);
        Assert.Equal((68250L, "UD-9820001234"), (dr.DebitPaise, dr.Party));
        Assert.Equal(68250, Assert.Single(cn.Entries, x => x.MapKey == SourceRules.UdhaarKey).CreditPaise);
        Assert.Equal(("counter", "counter"), (sale.Kind, sale.Sub));
        Assert.Equal("counter-credit", cn.Sub);
    }

    [Fact]
    public async Task Issue_settle_and_void_through_the_owner()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var c = h.Get<CounterBillService>();

        var upi = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in A (fictional)"),
            [new("Laundry · shirts", 3, 5000, "laundry"), new("Tea", 2, 2500, "food")], new("paid", "UPI", "UPI-REF-1")), Ct);
        Assert.Equal(20000, upi.Value.NetPaise);
        Assert.Equal(upi.Value.NetPaise + upi.Value.TaxPaise, upi.Value.TotalPaise);
        Assert.Equal(upi.Value.TaxPaise, upi.Value.CgstPaise + upi.Value.SgstPaise);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.invoice WHERE source_kind = 'counter' AND source_id = {upi.Value.BillId} AND invoice_no = '{upi.Value.InvoiceNo}'", Ct));

        var tooMuch = await Assert.ThrowsAsync<HccException>(() => c.IssueAsync(h.Ctx(h.Cashier, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in B"),
            [new("Dinner", 1, 100000, "food")], new("paid", "UPI", "R2"), DiscountPaise: 1000, DiscountReason: "regular"), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, tooMuch.Code);

        var ud = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in C (fictional)", "+91 98200 05555"),
            [new("Dinner", 2, 40000, "food")], new("udhaar")), Ct);
        Assert.NotNull(ud.Value.UdhaarId);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE receivable_id = {ud.Value.UdhaarId} AND counter_bill_id = {ud.Value.BillId} AND mobile = '9820005555' AND kind = 'restaurant' AND amount_paise = {ud.Value.TotalPaise}", Ct));

        var req = await c.RequestVoidAsync(h.Ctx(h.Reception, CounterBillService.OpRequestVoid), new(ud.Value.BillId, "Billed twice"), Ct);
        var mgr = await Assert.ThrowsAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct));
        Assert.Contains(mgr.Code, new[] { HccErrorCode.FORBIDDEN, HccErrorCode.APPROVAL_REQUIRED });
        var ok = await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct);
        Assert.Equal("Executed", ok.Value.Status);
        Assert.Equal("Credited", await h.ScalarAsync($"SELECT status FROM ctr.bill WHERE bill_id = {ud.Value.BillId}", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ivc.credit_note n JOIN ctr.bill b ON b.credit_note_no = n.credit_note_no WHERE b.bill_id = {ud.Value.BillId}", Ct));
        Assert.Equal(true, await h.ScalarAsync($"SELECT is_void FROM ops.udhaar_receivable WHERE receivable_id = {ud.Value.UdhaarId}", Ct));
        await Assert.ThrowsAsync<HccException>(() => c.RequestVoidAsync(h.Ctx(h.Reception, CounterBillService.OpRequestVoid), new(ud.Value.BillId, "again"), Ct));
    }
}
