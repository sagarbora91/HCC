using Hcc.Application.Commands;
using Hcc.Application.MoneyMasters;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.MoneyMasters;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// CU-PAY-B (PAY-08 across a rename): a receipt stored under a method's earlier name matches an equal one entered after the rename, because the guard
/// compares the master ROW (id) and not the label. A different row with the same paise is still not a duplicate.
/// </summary>
[Trait("Category", "Sql")]
public sealed class CuPayBRenameDuplicateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<CommandResult<ReceiptResult>> ReceiptAsync(Hotel h, long folio, string tender, long amount, bool confirm = false) =>
        h.Billing.RecordReceiptAsync(h.As(h.Cashier), Hotel.Key(), new RecordReceiptRequest(folio, "payment", tender, amount, "REF-CU", null, ConfirmDuplicate: confirm), Ct);

    [Fact]
    public async Task An_equal_receipt_after_a_rename_is_a_duplicate_of_the_one_stored_under_the_earlier_name()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var cmds = h.Get<MoneyMastersCommands>();
        var wallet = (await cmds.SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"]), Ct)).Value;
        var (_, _, folio) = await h.SeedInHouseAsync("103", "2026-10-23", "2026-10-26", ct: Ct);

        var first = (await ReceiptAsync(h, folio, "Paytm wallet", 25000)).Value;
        Assert.Equal("Paytm wallet", first.Tender);
        await cmds.SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(wallet.PaymentMethodId, "WALLET", "Paytm", "wallet", "BANK-1", true, ["frontdesk"], ExpectedVersion: wallet.Version), Ct);

        // The stored label is still the old one; the new receipt (new name, code, old name) is the SAME master row, so it is refused as a duplicate.
        Assert.Equal("Paytm wallet", await h.ScalarAsync($"SELECT method FROM pay.receipt WHERE receipt_id = {first.ReceiptId}", Ct));
        foreach (var tender in new[] { "Paytm", "WALLET", "Paytm wallet" })
        {
            var ex = await Expect.Fails(HccErrorCode.VALIDATION, () => ReceiptAsync(h, folio, tender, 25000));
            Assert.Equal("DUPLICATE_SUSPECTED", ex.Details!["reason"].GetString());
            Assert.Equal(first.ReceiptId, ex.Details["receiptId"].GetInt64());
        }

        // Another amount, or another row with the same paise, is not a duplicate; a confirmed second payment is accepted under the current name.
        Assert.Equal("Paytm", (await ReceiptAsync(h, folio, "Paytm", 25001)).Value.Tender);
        Assert.Equal("UPI", (await ReceiptAsync(h, folio, "UPI", 25000)).Value.Tender);
        Assert.Equal("Paytm", (await ReceiptAsync(h, folio, "Paytm", 25000, confirm: true)).Value.Tender);
    }
}
