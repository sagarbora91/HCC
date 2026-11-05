using Hcc.Application.Commands;
using Hcc.Application.MoneyMasters;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.MoneyMasters;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// AF2-PAYMETHODS (verifier note O8): folio receipts with the real server composition take the method from the Payment methods master — a retired
/// method is refused (MASTER_RETIRED) with nothing written, a renamed method still works by its earlier name and by its code (stored under its
/// current name), a method that needs a reference asks for one and one that does not posts without, and a split payment with mixed methods
/// (a till-settling method + an Owner-added wallet) moves only the drawer row into the shift while the PAY-08 guard still works per tender.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PaymentMethodFolioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PaymentMethodView> AddMethodAsync(Hotel h, string code, string name, string kind, string settlesTo, bool reference, string[] at) =>
        (await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, code, name, kind, settlesTo, reference, at), Ct)).Value;

    private static Task<CommandResult<ReceiptResult>> ReceiptAsync(Hotel h, long folio, string tender, long amount, string? reference, int? till = null, bool confirm = false) =>
        h.Billing.RecordReceiptAsync(h.As(h.Cashier), Hotel.Key(), new RecordReceiptRequest(folio, "payment", tender, amount, reference, till, ConfirmDuplicate: confirm), Ct);

    private static Task<long> ReceiptCountAsync(Hotel h, long folio) => h.LongAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {folio}", Ct);

    [Fact]
    public async Task A_retired_non_cash_method_is_refused_with_MASTER_RETIRED_and_nothing_is_written()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var wallet = await AddMethodAsync(h, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"]);
        var (_, _, folio) = await h.SeedInHouseAsync("101", "2026-10-23", "2026-10-26", ct: Ct);
        Assert.Equal("Paytm wallet", (await ReceiptAsync(h, folio, "Paytm wallet", 10000, "PTM-1")).Value.Tender);

        await h.Get<MoneyMastersCommands>().RetirePaymentMethodAsync(h.Ctx(h.Owner, "RetirePaymentMethod"), new PaymentMethodIdRequest(wallet.PaymentMethodId, wallet.Version), Ct);
        var before = await ReceiptCountAsync(h, folio);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReceiptAsync(h, folio, "Paytm wallet", 20000, "PTM-2"));
        Assert.Equal("MASTER_RETIRED", ex.Details!["reason"].GetString());
        Assert.Equal("Paytm wallet is retired — choose another payment method.", ex.Message);
        Assert.Equal(before, await ReceiptCountAsync(h, folio));
        // By its code too.
        Assert.Equal("MASTER_RETIRED", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReceiptAsync(h, folio, "WALLET", 20000, "PTM-3"))).Details!["reason"].GetString());
    }

    [Fact]
    public async Task A_renamed_method_still_works_by_its_earlier_name_and_its_code_and_is_stored_under_its_current_name()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var wallet = await AddMethodAsync(h, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"]);
        var (_, _, folio) = await h.SeedInHouseAsync("102", "2026-10-23", "2026-10-26", ct: Ct);
        await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(wallet.PaymentMethodId, "WALLET", "Paytm", "wallet", "BANK-1", true, ["frontdesk"], ExpectedVersion: wallet.Version), Ct);

        var byOld = (await ReceiptAsync(h, folio, "Paytm wallet", 11000, "PTM-11")).Value;
        Assert.Equal("Paytm", byOld.Tender);
        Assert.Equal("Paytm", await h.ScalarAsync($"SELECT method FROM pay.receipt WHERE receipt_id = {byOld.ReceiptId}", Ct));
        Assert.Equal("Paytm", (await ReceiptAsync(h, folio, "WALLET", 12000, "PTM-12")).Value.Tender);
        Assert.Equal("Paytm", (await ReceiptAsync(h, folio, "Paytm", 13000, "PTM-13")).Value.Tender);
        Assert.Equal("Enter the transaction reference for a non-cash payment.",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => ReceiptAsync(h, folio, "Paytm wallet", 14000, null))).Message);

        // The renamed till row: "Cash" renamed to "Front till" is still drawer cash by its earlier name.
        var methods = await h.Get<IPaymentMethodSource>().MethodsAsync(null, h.PropertyId, Ct);
        Assert.Equal(["Paytm wallet"], methods.Single(m => m.Code == "WALLET").FormerNames!);
        var cash = (await h.Get<MoneyMastersQueries>().PaymentMethodsAsync(h.PropertyId, h.Owner, ct: Ct)).Items.Single(m => m.Code == "CASH");
        await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(cash.PaymentMethodId, "CASH", "Front till", cash.Kind, cash.SettlesTo, cash.NeedsReference, cash.UsableAt, Ledger: cash.Ledger, ExpectedVersion: cash.Version), Ct);
        var (till, shift) = await h.OpenTillAsync(100000, Ct);
        Assert.Equal("Choose the till that receives the cash.", (await Expect.Fails(HccErrorCode.VALIDATION, () => ReceiptAsync(h, folio, "Cash", 15000, null))).Message);
        var r = (await ReceiptAsync(h, folio, "Cash", 15000, null, till)).Value;
        Assert.Equal(("Front till", shift.ShiftId), (r.Tender, r.ShiftId));
        Assert.Equal(115000, await h.ExpectedAsync(shift.ShiftId, Ct));
    }

    [Fact]
    public async Task A_method_that_needs_a_reference_asks_for_one_and_a_method_that_does_not_posts_without()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await AddMethodAsync(h, "VOUCHER", "Corporate voucher", "wallet", "BANK-1", false, ["frontdesk"]);
        await AddMethodAsync(h, "NEFT", "NEFT", "bank", "BANK-1", true, ["frontdesk"]);
        var (_, _, folio) = await h.SeedInHouseAsync("103", "2026-10-23", "2026-10-26", ct: Ct);

        foreach (var blank in new string?[] { null, "", "  " })
            Assert.Equal("Enter the transaction reference for a non-cash payment.", (await Expect.Fails(HccErrorCode.VALIDATION, () => ReceiptAsync(h, folio, "NEFT", 10000, blank))).Message);
        var v = (await ReceiptAsync(h, folio, "Corporate voucher", 10000, null)).Value;
        Assert.Equal("Corporate voucher", v.Tender);
        Assert.Null(v.ShiftId);
        Assert.Equal("NEFT", (await ReceiptAsync(h, folio, "neft", 10001, "UTR-1")).Value.Tender);
    }

    [Fact]
    public async Task A_split_payment_with_mixed_methods_moves_only_the_drawer_row_and_the_duplicate_guard_stays_per_tender()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await AddMethodAsync(h, "CASH-COUNTER", "Counter cash", "cash", "TILL", false, ["frontdesk"]);
        await AddMethodAsync(h, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"]);
        var (_, _, folio) = await h.SeedInHouseAsync("104", "2026-10-23", "2026-10-26", ct: Ct);
        var (till, shift) = await h.OpenTillAsync(100000, Ct);

        // One split = one receipt per tender row (the client sends t1, t2, t3 under their own keys).
        var t1 = (await ReceiptAsync(h, folio, "Counter cash", 30000, null, till)).Value;
        var t2 = (await ReceiptAsync(h, folio, "Paytm wallet", 50000, "PTM-21")).Value;
        var t3 = (await ReceiptAsync(h, folio, "UPI", 20000, "UPI-21")).Value;
        Assert.Equal((shift.ShiftId, (long?)null, (long?)null), (t1.ShiftId!.Value, t2.ShiftId, t3.ShiftId));
        Assert.Equal(130000, await h.ExpectedAsync(shift.ShiftId, Ct));
        Assert.Equal(3, await ReceiptCountAsync(h, folio));

        // PAY-08: an equal wallet row within 10 minutes needs the confirmation; the same amount by another method does not.
        var dup = await Expect.Fails(HccErrorCode.VALIDATION, () => ReceiptAsync(h, folio, "Paytm wallet", 50000, "PTM-22"));
        Assert.Equal("DUPLICATE_SUSPECTED", dup.Details!["reason"].GetString());
        Assert.Equal(t2.ReceiptId, dup.Details["receiptId"].GetInt64());
        Assert.Equal("Card", (await ReceiptAsync(h, folio, "Card", 50000, "CARD-22")).Value.Tender);
        Assert.Equal("Paytm wallet", (await ReceiptAsync(h, folio, "Paytm wallet", 50000, "PTM-22", confirm: true)).Value.Tender);
        Assert.Equal(5, await ReceiptCountAsync(h, folio));
    }
}
