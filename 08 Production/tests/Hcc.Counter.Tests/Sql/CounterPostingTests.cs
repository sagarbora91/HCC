using Hcc.Application.Accounting;
using Hcc.Application.Counter;
using Hcc.Application.MoneyMasters;
using Hcc.Contracts.MoneyMasters;
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

    [Fact]
    public async Task Catalogue_price_when_none_given_and_the_cash_receipt_limit()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        await h.ScalarAsync($"INSERT bill.extra_item(property_id, code, name, category, gst_category, unit, department, sort_order, active, updated_by, updated_utc) VALUES ({h.PropertyId}, 'DRYCLEAN', N'Dry cleaning', 'laundry', 'laundry', 'piece', 'laundry', 20, 1, 0, SYSUTCDATETIME()); "
            + $"INSERT bill.extra_item_price(extra_item_id, effective_from, amount_paise) SELECT extra_item_id, '2025-04-01', 25000 FROM bill.extra_item WHERE property_id = {h.PropertyId} AND code = 'DRYCLEAN';", Ct);
        var c = h.Get<CounterBillService>();
        // LF-int: a line naming a catalogue extra without a price takes the master's price on the business date (DRYCLEAN seed ₹250) and its name.
        var dry = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in D (fictional)"),
            [new("", 2, 0, ExtraId: "DRYCLEAN")], new("paid", "UPI", "UPI-REF-9")), Ct);
        Assert.Equal(50000, dry.Value.NetPaise);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ctr.bill_line WHERE bill_id = {dry.Value.BillId} AND unit_paise = 25000", Ct));
        var given = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in E (fictional)"),
            [new("Dry cleaning · silk", 1, 40000, ExtraId: "DRYCLEAN")], new("paid", "UPI", "UPI-REF-10")), Ct);
        Assert.Equal(40000, given.Value.NetPaise);
        var unknown = await Assert.ThrowsAsync<HccException>(() => c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in F"),
            [new("", 1, 0, ExtraId: "NOSUCH")], new("paid", "UPI", "R")), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, unknown.Code);
        // ACC-2 §7 B9: one counter bill of ₹2,00,000 or more in cash is refused (CASH_LIMIT) before the drawer is touched.
        var (till, _) = await h.OpenTillAsync(0, Ct);
        var big = await Assert.ThrowsAsync<HccException>(() => c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in G (fictional)"),
            [new("Banquet hall · wedding", 1, 19_000_000, "banquet")], new("paid", "Cash", TillId: till)), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, big.Code);
        Assert.Equal(CashGuard.Reason, big.Details!["reason"].GetString());
    }

    // ── AF-CTR-SVC (BIL-02 / Missed E): the counter takes its tender from the payment-methods master, front-desk place ──

    private static readonly string[] WrittenTables = ["ctr.bill", "pay.cash_movement", "ivc.invoice", "plat.outbox"];

    private static async Task<long[]> CountsAsync(Hotel h) =>
        [.. await Task.WhenAll(WrittenTables.Select(t => h.LongAsync($"SELECT COUNT(*) FROM {t}", Ct)))];

    private static IssueCounterBillRequest Paid(string? method, string? reference = null, int? till = null) =>
        new(new("Walk-in (fictional)"), [new("Tea", 2, 2500, "food")], new("paid", method, reference, till));

    private static async Task<HccException> RefusedAsync(Hotel h, IssueCounterBillRequest req, HccErrorCode code)
    {
        var before = await CountsAsync(h);
        var ex = await Assert.ThrowsAsync<HccException>(() => h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), req, Ct));
        Assert.Equal(code, ex.Code);
        Assert.Equal(before, await CountsAsync(h));
        return ex;
    }

    [Fact]
    public async Task Counter_cash_without_till_is_refused_and_nothing_is_written()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var ex = await RefusedAsync(h, Paid("Cash"), HccErrorCode.VALIDATION);
        Assert.Equal("Choose the till that receives the cash.", ex.Message);
    }

    [Fact]
    public async Task Counter_misspelled_tender_Cashh_with_reference_is_refused_and_nothing_is_written()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var ex = await RefusedAsync(h, Paid("Cashh", "R1"), HccErrorCode.VALIDATION);
        Assert.StartsWith("Select a payment method (", ex.Message);
    }

    [Fact]
    public async Task Counter_upi_without_reference_is_refused()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var ex = await RefusedAsync(h, Paid("UPI"), HccErrorCode.VALIDATION);
        Assert.Equal("Enter the transaction reference for a non-cash payment.", ex.Message);
    }

    [Fact]
    public async Task Counter_retired_method_is_refused_MASTER_RETIRED()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var m = (await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk", "pos"], Ledger: "1010"), Ct)).Value;
        await h.Get<MoneyMastersCommands>().RetirePaymentMethodAsync(h.Ctx(h.Owner, "RetirePaymentMethod"), new PaymentMethodIdRequest(m.PaymentMethodId, m.Version), Ct);
        var ex = await RefusedAsync(h, Paid("Paytm wallet", "W-1"), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal("MASTER_RETIRED", ex.Details!["reason"].GetString());
    }

    [Fact]
    public async Task Counter_lower_case_upi_is_accepted_and_stored_as_UPI()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var r = await h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), Paid("upi", "UPI-REF-77"), Ct);
        Assert.Equal("UPI", await h.ScalarAsync($"SELECT method FROM ctr.bill WHERE bill_id = {r.Value.BillId}", Ct));
        Assert.Equal("UPI", await h.ScalarAsync("SELECT TOP 1 JSON_VALUE(payload, '$.tender') FROM plat.outbox WHERE event_type = 'CounterBillIssued' ORDER BY outbox_id DESC", Ct));
    }

    [Fact]
    public async Task Counter_cash_with_open_shift_till_posts_one_bill_one_movement_one_outbox_event()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var (till, _) = await h.OpenTillAsync(0, Ct);
        var bills = await h.LongAsync("SELECT COUNT(*) FROM ctr.bill", Ct);
        var moves = await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement", Ct);
        var events = await h.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CounterBillIssued'", Ct);
        var r = await h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), Paid("cash", null, till), Ct);
        Assert.Equal(bills + 1, await h.LongAsync("SELECT COUNT(*) FROM ctr.bill", Ct));
        Assert.Equal(moves + 1, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM pay.cash_movement WHERE kind = 'pos-sale' AND source_type = 'counter-bill' AND source_id = {r.Value.BillId}", Ct));
        Assert.Equal(events + 1, await h.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CounterBillIssued'", Ct));
        Assert.Equal("Cash", await h.ScalarAsync($"SELECT method FROM ctr.bill WHERE bill_id = {r.Value.BillId}", Ct));
    }

    [Fact]
    public async Task Counter_same_key_replay_posts_once()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var c = h.Get<CounterBillService>();
        var key = Hotel.Key();
        var req = Paid("UPI", "UPI-REF-88");
        var first = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue, key), req, Ct);
        Assert.False(first.Replayed);
        var before = await CountsAsync(h);
        var again = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue, key), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.BillId, again.Value.BillId);
        Assert.Equal(before, await CountsAsync(h));
    }
}
