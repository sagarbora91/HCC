using Hcc.Application.Accounting;
using Hcc.Application.Expenses;
using Hcc.Application.MoneyMasters;
using Hcc.Application.Pos;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.MoneyMasters;
using Hcc.Contracts.Pos;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// MA-11 review: receipts, outlet bills and expenses take the methods of the Payment methods master (mst.payment_method) with the real
/// server composition — an Owner-added method is accepted end to end (migration 2890 relaxed the fixed CHECK lists), a method that settles
/// to a till is drawer cash whatever its name, a retired method is refused, a method not usable at the place is refused.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PaymentMethodMasterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PaymentMethodView> AddMethodAsync(Hotel h, string code, string name, string kind, string settlesTo, bool reference, string[] at, string? ledger = null) =>
        (await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, code, name, kind, settlesTo, reference, at, Ledger: ledger), Ct)).Value;

    private static async Task<(PosService Pos, PosQueries Q, PosCaller Caller, int Outlet, int Item)> PosAsync(Hotel h)
    {
        var pos = h.Get<PosService>();
        var caller = new PosCaller(h.PropertyId, h.Owner);
        var outlet = (await pos.CreateOutletAsync(caller, Hotel.Key(), new CreateOutletRequest("REST", "Restaurant", "table", ["T1", "T2"]), Ct)).Value.OutletId;
        var item = (await pos.SaveMenuItemAsync(caller, Hotel.Key(), new SaveMenuItemRequest(null, "THALI", "Thali", "Main", 42000, true, [outlet], null), Ct)).Value.ItemId;
        return (pos, h.Get<PosQueries>(), caller, outlet, item);
    }

    private static async Task<(long OrderId, string Version)> SentCheckAsync(PosService pos, PosQueries q, PosCaller caller, int outlet, int item, string table)
    {
        var o = (await pos.OpenCheckAsync(caller, Hotel.Key(), new OpenCheckRequest(outlet, table, null, 2, "Guest"), Ct)).Value;
        await pos.AddItemAsync(caller, Hotel.Key(), new AddCheckItemRequest(o.OrderId, item, 1, null), Ct);
        await pos.SendKotAsync(caller, Hotel.Key(), new SendKotRequest(o.OrderId, null), Ct);
        return (o.OrderId, (await q.GetCheckAsync(caller, o.OrderId, Ct)).Version);
    }

    [Fact]
    public async Task An_owner_added_wallet_method_is_accepted_for_a_folio_receipt_an_outlet_bill_and_an_expense()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await AddMethodAsync(h, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk", "pos", "expenses"], "1010");
        var (_, _, folio) = await h.SeedInHouseAsync("101", "2026-10-23", "2026-10-26", ct: Ct);

        var r = (await h.PayAsync(folio, 50000, "Paytm wallet", ct: Ct)).Value;
        Assert.Equal("Paytm wallet", r.Tender);
        Assert.Null(r.ShiftId);   // not drawer cash
        Assert.Equal("Paytm wallet", await h.ScalarAsync($"SELECT method FROM pay.receipt WHERE receipt_id = {r.ReceiptId}", Ct));
        // By code too, stored under its name; a reference is required (needs reference).
        Assert.Equal("Paytm wallet", (await h.PayAsync(folio, 1000, "WALLET", ct: Ct)).Value.Tender);
        var noRef = await Expect.Fails(HccErrorCode.VALIDATION, () => h.Billing.RecordReceiptAsync(h.As(h.Cashier), Hotel.Key(),
            new RecordReceiptRequest(folio, "payment", "Paytm wallet", 1000, null, null), Ct));
        Assert.Equal("Enter the transaction reference for a non-cash payment.", noRef.Message);

        var (pos, q, caller, outlet, item) = await PosAsync(h);
        var (order, version) = await SentCheckAsync(pos, q, caller, outlet, item, "T1");
        var settled = (await pos.SettleCheckAsync(caller, Hotel.Key(), new SettleCheckRequest(order, "outlet", "Paytm wallet", "W-001", null, null, version), Ct)).Value;
        Assert.Null(settled.CashMovementId);
        Assert.Equal("Paytm wallet", await h.ScalarAsync($"SELECT tender FROM pos.bill WHERE bill_id = {settled.BillId}", Ct));
        var day = await q.DaySummaryAsync(caller, null, Ct);
        Assert.Equal(settled.TotalPaise, day.OtherPaise);    // no longer counted as a room posting
        Assert.Equal(0, day.RoomFolioPaise);
        Assert.Equal(0, day.OutletCashPaise);

        var exp = (await h.Get<ExpenseCommands>().SubmitAsync(h.Ctx(h.Reception, ExpenseCommands.OpSubmit),
            new SubmitExpenseRequest("Milk vendor (fictional)", "Supplies", 25000, "Paytm wallet"), Ct)).Value.Expense;
        Assert.Equal("Paytm wallet", exp.Method);
        Assert.Contains("Paytm wallet", (await h.Get<ExpenseQueries>().OptionsAsync(h.PropertyId, h.Owner, Ct)).Methods);

        // Accounting: the method's master ledger maps its receipts / bills / expenses (no Suspense line for a new method).
        var map = await h.Get<IAccountingStore>().EffectiveMappingAsync(null, h.PropertyId, Ct);
        Assert.Equal(("1010", "1010", "1000"), (map["method:Paytm wallet"], map["funding:Paytm wallet"], map["method:Cash"]));
    }

    [Fact]
    public async Task A_method_that_settles_to_the_till_is_drawer_cash_whatever_its_name_and_a_retired_method_is_refused()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var petty = await AddMethodAsync(h, "CASH-COUNTER", "Counter cash", "cash", "TILL", false, ["frontdesk", "pos", "expenses"]);
        var (_, _, folio) = await h.SeedInHouseAsync("102", "2026-10-23", "2026-10-26", ct: Ct);
        var (till, shift) = await h.OpenTillAsync(100000, Ct);

        var noTill = await Expect.Fails(HccErrorCode.VALIDATION, () => h.PayAsync(folio, 30000, "Counter cash", ct: Ct));
        Assert.Equal("Choose the till that receives the cash.", noTill.Message);
        var r = (await h.PayAsync(folio, 30000, "Counter cash", till, ct: Ct)).Value;
        Assert.Equal(shift.ShiftId, r.ShiftId);
        Assert.Equal(130000, await h.ExpectedAsync(shift.ShiftId, Ct));

        // Outlet: the till-settling method goes into the drawer too, and its void comes out of it.
        var (pos, q, caller, outlet, item) = await PosAsync(h);
        var (order, version) = await SentCheckAsync(pos, q, caller, outlet, item, "T2");
        var settled = (await pos.SettleCheckAsync(caller, Hotel.Key(), new SettleCheckRequest(order, "outlet", "Counter cash", null, till, null, version), Ct)).Value;
        Assert.NotNull(settled.CashMovementId);
        Assert.Equal(130000 + settled.TotalPaise, await h.ExpectedAsync(shift.ShiftId, Ct));
        Assert.Equal(settled.TotalPaise, (await q.DaySummaryAsync(caller, null, Ct)).CashPaise);
        Assert.Equal(settled.TotalPaise, (await q.DaySummaryAsync(caller, null, Ct)).OutletCashPaise);

        // A bank-transfer method is not usable at the outlet: refused with the outlet's list.
        var (order2, version2) = await SentCheckAsync(pos, q, caller, outlet, item, "T1");
        var notHere = await Expect.Fails(HccErrorCode.VALIDATION, () => pos.SettleCheckAsync(caller, Hotel.Key(),
            new SettleCheckRequest(order2, "outlet", "Bank transfer", "NEFT1", null, null, version2), Ct));
        Assert.Equal("Select Cash, UPI, Card or Counter cash.", notHere.Message);

        // Retired: refused with MASTER_RETIRED.
        await h.Get<MoneyMastersCommands>().RetirePaymentMethodAsync(h.Ctx(h.Owner, "RetirePaymentMethod"), new PaymentMethodIdRequest(petty.PaymentMethodId, petty.Version), Ct);
        var retired = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.PayAsync(folio, 1000, "Counter cash", till, ct: Ct));
        Assert.Equal("MASTER_RETIRED", retired.Details!["reason"].GetString());
    }

    [Fact]
    public async Task A_cash_expense_paid_by_a_till_settling_method_moves_the_drawer_and_the_summary_counts_it_as_cash()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await AddMethodAsync(h, "CASH-COUNTER", "Counter cash", "cash", "TILL", false, ["expenses"]);
        var (till, shift) = await h.OpenTillAsync(100000, Ct);
        var cmds = h.Get<ExpenseCommands>();
        var e = (await cmds.SubmitAsync(h.Ctx(h.Reception, ExpenseCommands.OpSubmit), new SubmitExpenseRequest("Tea stall (fictional)", "Petty cash", 12000, "Counter cash"), Ct)).Value.Expense;
        var noTill = await Expect.Fails(HccErrorCode.VALIDATION, () => cmds.DecideAsync(h.Ctx(h.Owner, ExpenseCommands.OpDecide), new DecideExpenseRequest(e.ExpenseId, e.Version, true), Ct));
        Assert.Equal("Choose the till that pays this cash expense.", noTill.Message);
        var posted = (await cmds.DecideAsync(h.Ctx(h.Owner, ExpenseCommands.OpDecide), new DecideExpenseRequest(e.ExpenseId, e.Version, true, null, till), Ct)).Value;
        Assert.NotNull(posted.CashMovementId);
        Assert.Equal(88000, await h.ExpectedAsync(shift.ShiftId, Ct));
        var sum = await h.Get<ExpenseQueries>().SummaryAsync(h.PropertyId, h.Owner, "2026-10-24", Ct);
        Assert.Equal((12000L, 0L, 0L), (sum.CashPaise, sum.NonCashPaise, sum.VendorCreditPaise));
    }
}
