using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Application.MoneyMasters;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.MoneyMasters;
using Hcc.Groups.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Groups;
using Hcc.Infrastructure.SqlServer.Tax;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// AF2-PAYMETHODS: master-folio receipts (RecordGroupReceipt) and company payments (RecordCompanyPayment) take the method from the Payment methods
/// master (front desk) instead of the fixed Cash / UPI / Card / Bank transfer list: a retired method is refused with MASTER_RETIRED, a method that
/// needs a reference asks for one, a method that settles to a till is refused whatever its name, an Owner-added or renamed method passes the check.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PaymentMethodMasterGroupTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private sealed class OwnerMethods : IPaymentMethodSource
    {
        public Task<IReadOnlyList<TenderMethod>> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenderMethod>>(
            [
                new("CASH", "Front till", "cash", true, false, true, true, true, true, "1000", ["Cash"]),
                new("UPI", "PhonePe UPI", "upi", false, true, true, true, true, true, "1010", ["UPI"]),
                new("CARD", "Card", "card", false, true, true, true, false, false, "1020"),
                new("BANK", "Bank transfer", "bank", false, true, true, false, true, true, "1030"),
                new("VOUCHER", "Corporate voucher", "wallet", false, false, true, false, false, true, "1010"),
            ]);
    }

    private (CompanyCredit Credit, GroupCommands Groups) Services()
    {
        var c = _w.Platform.Connections;
        var gst = new GstRateResolver(new SqlGstMasterStore(c));
        var methods = new OwnerMethods();
        var credit = new CompanyCredit(_w.Platform.Commands, _w.CompanyStore, new SqlCompanyCreditStore(c), _w.Store, _w.Policy, _w.Clock, new SqlBusinessDayGuard(), methods);
        var groups = new GroupCommands(_w.Platform.Commands, _w.Store, _w.Policy, new BillingGroupFolioPort(_w.BillingStore), gst, _w.Clock, new SqlBusinessDayGuard(), credit, methods);
        return (credit, groups);
    }

    [Fact]
    public async Task Company_payment_takes_the_method_from_the_master()
    {
        var (credit, _) = Services();
        var id = (await _w.Companies.SaveAsync(_w.Ctx("owner"), new SaveCompanyRequest(null, null, "Method Test Pvt Ltd (fictional)", CreditLimitPaise: 100_000))).Value.CompanyId;
        var pay = new RecordCompanyPaymentRequest(id, "Bank transfer", 10_000, "NEFT-DEMO-1");
        var before = await _w.LongAsync("SELECT COUNT(*) FROM grp.company_payment;");

        var retired = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Card" }));
        Assert.Equal("MASTER_RETIRED", retired.Details!["reason"].GetString());
        Assert.Equal("Card is retired — choose another payment method.", retired.Message);
        var till = await Expect.Fails(HccErrorCode.VALIDATION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Front till" }));
        Assert.StartsWith("A company pays by UPI, card or bank transfer", till.Message);
        Assert.StartsWith("A company pays by UPI, card or bank transfer",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Cash" }))).Message); // earlier name of the till row
        Assert.Equal("Select a payment method (Front till, PhonePe UPI, Bank transfer or Corporate voucher).",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Cheque" }))).Message);
        Assert.Equal("Enter the transaction reference for a non-cash payment.",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "UPI", Reference = " " }))).Message);

        // Accepted by the method check (renamed UPI by its earlier name; an Owner-added method without a reference): the company owes nothing yet.
        Assert.Contains("owes nothing", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "UPI" }))).Message);
        Assert.Contains("owes nothing", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Corporate voucher", Reference = "" }))).Message);
        Assert.Equal(before, await _w.LongAsync("SELECT COUNT(*) FROM grp.company_payment;"));
    }

    [Fact]
    public async Task Master_folio_receipt_takes_the_method_from_the_master()
    {
        var (_, groups) = Services();
        var r = new RecordGroupReceiptRequest(999_999, "payment", "Bank transfer", 10_000, "NEFT-GRP-9");

        Assert.Equal("MASTER_RETIRED", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => groups.RecordReceiptAsync(_w.Ctx("reception"), r with { Tender = "Card" }))).Details!["reason"].GetString());
        Assert.StartsWith("The master folio takes UPI, card or bank transfer",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => groups.RecordReceiptAsync(_w.Ctx("reception"), r with { Tender = "Front till" }))).Message);
        Assert.Equal("Enter the transaction reference for a non-cash payment.",
            (await Expect.Fails(HccErrorCode.VALIDATION, () => groups.RecordReceiptAsync(_w.Ctx("reception"), r with { Tender = "PhonePe UPI", Reference = "" }))).Message);
        // Past the method check (renamed method by its code; a method without a reference): the unknown group is what refuses it.
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => groups.RecordReceiptAsync(_w.Ctx("reception"), r with { Tender = "upi" }));
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => groups.RecordReceiptAsync(_w.Ctx("reception"), r with { Tender = "Corporate voucher", Reference = "" }));
    }
}
