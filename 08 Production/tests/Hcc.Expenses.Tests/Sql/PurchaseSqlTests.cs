using System.Data;
using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Expenses.Tests.Harness;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>
/// ACC-2 · lane acc2-purchase on SQL Server (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3; migrations 4230–4235): the purchase fields of a bill
/// (GST kind from the vendor's state, RCM stamped at approval, ITC refused on a blocked category — DEC-143), capital purchases and the
/// fixed-asset register, vendor payments allocated oldest first (advance = the rest), cash payments through the drawer, voids, returns
/// (Manager requests, Owner approves), ageing, and the events Accounting books (payloads mapped through <see cref="AccountingEventMapper"/>).
/// </summary>
[Trait("Category", "Sql")]
public class PurchaseSqlTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<JsonElement> PayloadAsync(string type, long aggregateId)
    {
        var json = (string)(await db.ScalarAsync("SELECT TOP (1) payload FROM plat.outbox WHERE event_type = @t AND aggregate_id = @i ORDER BY outbox_id DESC",
            ("@t", SqlDbType.VarChar, type), ("@i", SqlDbType.BigInt, aggregateId)))!;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static T Parse<T>(JsonElement e) => e.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>A test category (seeded money masters + one row) with the ACC-2 flags.</summary>
    private async Task<string> CategoryAsync(string ledger = "5000", bool rcm = false, int? rcmBps = null, bool capital = false, bool itcBlocked = false)
    {
        await db.ExecAsync("EXEC mst.usp_seed_money_masters @p", ("@p", SqlDbType.Int, db.PropertyId));
        var tag = Tag();
        var name = "Head " + tag;
        await db.ExecAsync($"""
            INSERT ops.expense_category(property_id, code, name, sort_order, active, ledger, needs_bill, updated_by, updated_utc, rcm, rcm_bps, capital, itc_blocked)
            VALUES (@p, 'C-{tag}', @n, 900, 1, '{ledger}', 0, 1, SYSUTCDATETIME(), {(rcm ? 1 : 0)}, {(rcmBps is null ? "NULL" : rcmBps.ToString())}, {(capital ? 1 : 0)}, {(itcBlocked ? 1 : 0)});
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name));
        return name;
    }

    private async Task<(string Code, string Name)> VendorAsync(string? state = "MH")
    {
        await db.ExecAsync("EXEC mst.usp_seed_money_masters @p", ("@p", SqlDbType.Int, db.PropertyId));
        var tag = Tag();
        var (code, name) = ("V-" + tag, "Vendor " + tag + " (fictional)");
        await db.ExecAsync($"""
            INSERT ops.expense_vendor(property_id, code, name, sort_order, vendor_type, tds, updated_by, updated_utc, gst_state)
            VALUES (@p, @c, @n, 900, 'supplier', 0, 1, SYSUTCDATETIME(), {(state is null ? "NULL" : "'" + state + "'")});
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@c", SqlDbType.VarChar, code), ("@n", SqlDbType.NVarChar, name));
        return (code, name);
    }

    private async Task<ExpenseView> BillAsync(string vendor, string category, long amount, string method = "Vendor credit", long? gst = null, string? itc = null, int? rcmBps = null,
        string? gstKind = null)
    {
        var e = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest(vendor, category, amount, method, Description: "Bill " + Tag(),
            GstRateBps: gst is null ? null : 1800, GstPaise: gst, Itc: itc, RcmBps: rcmBps, GstKind: gstKind), Ct)).Value.Expense;
        return (await db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(e.ExpenseId, e.Version, true), Ct)).Value.Expense;
    }

    [Fact]
    public async Task A_bill_on_vendor_credit_publishes_a_credit_purchase_and_an_inter_state_vendor_books_IGST()
    {
        var cat = await CategoryAsync();
        var (_, ka) = await VendorAsync("KA");
        var b = await BillAsync(ka, cat, 118_000, gst: 18_000, itc: "allowed");
        Assert.Equal("inter", b.GstKind);
        var p = await PayloadAsync(AccountingEvents.ExpensePosted, b.ExpenseId);
        Assert.True(p.GetProperty("credit").GetBoolean());
        Assert.Equal("inter", p.GetProperty("gstKind").GetString());
        var src = AccountingEventMapper.Expense(Parse<ExpensePostedEvent>(p));
        Assert.Equal(VoucherType.Purchase, src.Type);
        Assert.Equal(new[] { ("expense:" + cat, 100_000L, 0L), (SourceRules.GstInputIgstKey, 18_000L, 0L), ("funding:Vendor credit", 0L, 118_000L) },
            src.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
        // An intra-state vendor splits CGST / SGST; the screen may also say intra explicitly for the KA vendor.
        var (_, mh) = await VendorAsync();
        Assert.Null((await BillAsync(mh, cat, 11_800, gst: 1_800, itc: "allowed")).GstKind);
        Assert.Equal("intra", (await BillAsync(ka, cat, 11_800, gst: 1_800, itc: "allowed", gstKind: "intra")).GstKind);
    }

    [Fact]
    public async Task Dec143_ITC_is_refused_on_a_blocked_category_and_a_capital_vehicle()
    {
        var food = await CategoryAsync("5120", itcBlocked: true);
        var ex = await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            new SubmitExpenseRequest(null, food, 10_500, "UPI", Description: "Staff lunch " + Tag(), GstRateBps: 500, GstPaise: 500, Itc: "allowed"), Ct));
        Assert.Contains("DEC-143", ex.Message, StringComparison.Ordinal);
        var car = await CategoryAsync("1540", capital: true);
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            new SubmitExpenseRequest(null, car, 118_000, "UPI", Description: "Van " + Tag(), GstRateBps: 1800, GstPaise: 18_000, Itc: "allowed"), Ct));
    }

    [Fact]
    public async Task Reverse_charge_refuses_vendor_GST_and_publishes_the_RCM_GST_at_approval()
    {
        var sec = await CategoryAsync("5050", rcm: true, rcmBps: 1800);
        var (_, v) = await VendorAsync();
        var ex = await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            new SubmitExpenseRequest(v, sec, 100_000, "Vendor credit", Description: "Guards " + Tag(), GstRateBps: 1800, GstPaise: 15_254), Ct));
        Assert.Contains("reverse-charge bill carries no vendor GST", ex.Message, StringComparison.Ordinal);
        var b = await BillAsync(v, sec, 4_000_000);
        Assert.True(b.Rcm);
        Assert.Equal(1800, b.RcmBps);
        var src = AccountingEventMapper.Rcm(Parse<ExpenseRcmPostedEvent>(await PayloadAsync(PurchaseEvents.ExpenseRcmPosted, b.ExpenseId)));
        Assert.Equal((SourceKinds.Tax, "rcm", VoucherType.Journal), (src.Kind, src.Sub, src.Type));
        Assert.Equal(new[] { ("expense:" + sec, 720_000L, 0L), (SourceRules.RcmPayableKey, 0L, 720_000L) }, src.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
        // ITC allowed on the RCM GST (no vendor GST on the bill) and a rate typed on the bill.
        var c = await BillAsync(v, sec, 100_000, itc: "allowed", rcmBps: 500);
        var src2 = AccountingEventMapper.Rcm(Parse<ExpenseRcmPostedEvent>(await PayloadAsync(PurchaseEvents.ExpenseRcmPosted, c.ExpenseId)));
        Assert.Equal(new[] { ("tax:gst-input", 2_500L, 0L), ("tax:gst-input-sgst", 2_500L, 0L), (SourceRules.RcmPayableKey, 0L, 5_000L) },
            src2.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
        // The reversal reverses the RCM GST too.
        var rev = (await db.Commands.ReverseAsync(db.Ctx(Who.Owner), new ReverseExpenseRequest(b.ExpenseId, b.Version, "Duplicate bill"), Ct)).Value.Reversal;
        var src3 = AccountingEventMapper.Rcm(Parse<ExpenseRcmPostedEvent>(await PayloadAsync(PurchaseEvents.ExpenseRcmPosted, rev.ExpenseId)));
        Assert.Equal(new[] { ("expense:" + sec, 0L, 720_000L), (SourceRules.RcmPayableKey, 720_000L, 0L) }, src3.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
    }

    [Fact]
    public async Task A_capital_purchase_enters_the_fixed_asset_register_at_cost_less_ITC_and_a_reversal_marks_it_disposed()
    {
        var furn = await CategoryAsync("1510", capital: true);
        var (_, ka) = await VendorAsync("KA");
        var b = await BillAsync(ka, furn, 236_000, gst: 36_000, itc: "allowed");
        Assert.True(b.Capital);
        Assert.Matches("^FA-2026-\\d{3}$", b.AssetNo!);
        var a = (await db.VendorQueries.AssetsAsync(db.PropertyId, Who.Owner, Ct)).Items.Single(x => x.ExpenseId == b.ExpenseId);
        Assert.Equal(("1510", 200_000L, 36_000L, "2026-10-24", b.AssetNo), (a.Ledger, a.CostPaise, a.ItcPaise, a.PurchaseDate, a.AssetNo));
        await db.Commands.ReverseAsync(db.Ctx(Who.Owner), new ReverseExpenseRequest(b.ExpenseId, b.Version, "Wrong category"), Ct);
        var d = (await db.VendorQueries.AssetsAsync(db.PropertyId, Who.Owner, Ct)).Items.Single(x => x.ExpenseId == b.ExpenseId);
        Assert.Equal(("2026-10-24", 0L), (d.DisposedDate, d.DisposedAmountPaise));
        // The category master refuses a capital flag on an expense ledger (CHECK + rules).
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecAsync("UPDATE ops.expense_category SET capital = 1 WHERE property_id = @p AND ledger = '5000'", ("@p", SqlDbType.Int, db.PropertyId)));
    }

    [Fact]
    public async Task A_vendor_payment_is_allocated_oldest_first_the_rest_is_an_advance_and_ageing_reads_the_allocations()
    {
        var cat = await CategoryAsync();
        var (code, v) = await VendorAsync();
        var a = await BillAsync(v, cat, 3_000_000);
        var b = await BillAsync(v, cat, 1_200_000);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Vendor.PayAsync(db.Ctx(Who.Reception), new RecordVendorPaymentRequest(code, 100, "UPI", "U-1"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(code, 100, "Bank transfer"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(code, 100, "Vendor credit", "x"), Ct));
        var r = (await db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(v, 3_500_000, "Bank transfer", "NEFT-778"), Ct)).Value;
        Assert.Equal(new[] { new VendorAllocationDto(a.ExpenseId, 3_000_000), new VendorAllocationDto(b.ExpenseId, 500_000) }, r.Payment.Allocations);
        Assert.Equal(0, r.AdvancePaise);
        var src = AccountingEventMapper.VendorPayment(Parse<VendorPaymentEvent>(await PayloadAsync(PurchaseEvents.VendorPaymentRecorded, r.Payment.PaymentId)),
            new PartyContext(Vendor: code));
        Assert.Equal((SourceKinds.Vendor, "vendor-payment", VoucherType.Payment), (src.Kind, src.Sub, src.Type));
        Assert.Equal(new[] { (SourceRules.VendorPayableKey, 3_500_000L, 0L, (string?)code), ("method:Bank transfer", 0L, 3_500_000L, null) },
            src.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise, e.Party)).ToList());
        var ag = (await db.VendorQueries.AgeingAsync(db.PropertyId, Who.Accountant, null, code, Ct)).Rows.Single();
        Assert.Equal((700_000L, 0L, 700_000L, 700_000L), (ag.OpenPaise, ag.AdvancePaise, ag.NetPaise, ag.Buckets["0–30"]));
        var adv = (await db.Vendor.PayAsync(db.Ctx(Who.Accountant), new RecordVendorPaymentRequest(code, 1_000_000, "UPI", "UPI-55"), Ct)).Value;
        Assert.Equal(300_000, adv.AdvancePaise);
        ag = (await db.VendorQueries.AgeingAsync(db.PropertyId, Who.Owner, null, code, Ct)).Rows.Single();
        Assert.Equal((0L, 300_000L, -300_000L), (ag.OpenPaise, ag.AdvancePaise, ag.NetPaise));
        // An explicit allocation is checked against the open bills.
        var c = await BillAsync(v, cat, 200_000);
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.PayAsync(db.Ctx(Who.Manager),
            new RecordVendorPaymentRequest(code, 100_000, "UPI", "u", Allocations: [new VendorAllocationDto(c.ExpenseId, 150_000)]), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.PayAsync(db.Ctx(Who.Manager),
            new RecordVendorPaymentRequest(code, 100_000, "UPI", "u", Allocations: [new VendorAllocationDto(a.ExpenseId, 1)]), Ct));
    }

    [Fact]
    public async Task A_cash_vendor_payment_leaves_the_drawer_and_a_same_day_void_by_the_Owner_puts_it_back()
    {
        var (till, shift) = await db.OpenTillAsync(500_000);
        var cat = await CategoryAsync();
        var (code, v) = await VendorAsync();
        await BillAsync(v, cat, 300_000);
        var before = await db.ExpectedCashAsync(shift.ShiftId);
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(code, 120_000, "Cash"), Ct));
        var p = (await db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(code, 120_000, "Cash", TillId: till), Ct)).Value.Payment;
        Assert.Equal(before - 120_000, await db.ExpectedCashAsync(shift.ShiftId));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Vendor.VoidAsync(db.Ctx(Who.Manager), new VoidVendorPaymentRequest(p.PaymentId, "Wrong vendor", till), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.VoidAsync(db.Ctx(Who.Owner), new VoidVendorPaymentRequest(p.PaymentId, "x", till), Ct));
        var u = (await db.Vendor.VoidAsync(db.Ctx(Who.Owner), new VoidVendorPaymentRequest(p.PaymentId, "Wrong vendor", till), Ct)).Value.Payment;
        Assert.True(u.Voided);
        Assert.Equal(before, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Null(AccountingEventMapper.VendorPaymentVoid(Parse<VendorPaymentEvent>(await PayloadAsync(PurchaseEvents.VendorPaymentVoided, p.PaymentId))));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Vendor.VoidAsync(db.Ctx(Who.Owner), new VoidVendorPaymentRequest(p.PaymentId, "again", till), Ct));
        // A later-day void is a reversing voucher on the void date (pure mapping).
        var later = AccountingEventMapper.VendorPaymentVoid(new VendorPaymentEvent(9, new DateOnly(2026, 10, 20), code, v, "UPI", 50_000, 0, VoidDate: ExpensesDb.Day, VoidReason: "Bounced"))!;
        Assert.Equal((ExpensesDb.Day, "vendor-payment-void", "vendor-payment:9/void"), (later.BusinessDate, later.Sub, later.SourceKey));
        Assert.Equal(new[] { (SourceRules.VendorPayableKey, 0L, 50_000L), ("method:UPI", 50_000L, 0L) }, later.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
    }

    [Fact]
    public async Task A_return_is_requested_by_the_Manager_approved_only_by_the_Owner_and_reverses_the_ITC_taken()
    {
        var cat = await CategoryAsync();
        var (code, v) = await VendorAsync();
        var b = await BillAsync(v, cat, 118_000, gst: 18_000, itc: "allowed");
        await Fails(HccErrorCode.FORBIDDEN, () => db.Vendor.ReturnAsync(db.Ctx(Who.Reception), new RecordExpenseReturnRequest(b.ExpenseId, 59_000, 9_000, "Damaged"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.ReturnAsync(db.Ctx(Who.Manager), new RecordExpenseReturnRequest(b.ExpenseId, 200_000, 0, "Damaged"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.ReturnAsync(db.Ctx(Who.Manager), new RecordExpenseReturnRequest(b.ExpenseId, 59_000, 19_000, "Damaged"), Ct));
        var r = (await db.Vendor.ReturnAsync(db.Ctx(Who.Manager), new RecordExpenseReturnRequest(b.ExpenseId, 59_000, 9_000, "Damaged crates"), Ct)).Value.Expense;
        Assert.Equal(("Pending", -59_000L, (long?)b.ExpenseId), (r.Status, r.AmountPaise, r.ReturnOf));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Accountant), new DecideExpenseRequest(r.ExpenseId, r.Version, true), Ct));
        var posted = (await db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(r.ExpenseId, r.Version, true), Ct)).Value.Expense;
        Assert.Equal("Posted", posted.Status);
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ExpensePosted' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, r.ExpenseId)));
        var src = AccountingEventMapper.ExpenseReturn(Parse<ExpenseReturnedEvent>(await PayloadAsync(PurchaseEvents.ExpenseReturned, r.ExpenseId)));
        Assert.Equal((VoucherType.Purchase, "return"), (src.Type, src.Sub));
        Assert.Equal(new[] { ("expense:" + cat, 0L, 50_000L), ("tax:gst-input", 0L, 4_500L), ("tax:gst-input-sgst", 0L, 4_500L), ("funding:Vendor credit", 59_000L, 0L) },
            src.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)).ToList());
        var ag = (await db.VendorQueries.AgeingAsync(db.PropertyId, Who.Owner, null, code, Ct)).Rows.Single();
        Assert.Equal(59_000, ag.OpenPaise);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.ReverseAsync(db.Ctx(Who.Owner), new ReverseExpenseRequest(posted.ExpenseId, posted.Version, "oops"), Ct));
        // The Owner's own return posts at once (and its event is published).
        var own = (await db.Vendor.ReturnAsync(db.Ctx(Who.Owner), new RecordExpenseReturnRequest(b.ExpenseId, 10_000, 0, "Short supply"), Ct)).Value.Expense;
        Assert.Equal("Posted", own.Status);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ExpenseReturned' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, own.ExpenseId)));
    }
}
