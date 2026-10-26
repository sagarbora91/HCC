using Hcc.Contracts;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;
using Hcc.Domain.MoneyMasters;
using Hcc.Domain.Payments;

namespace Hcc.MoneyMasters.Tests.Kernel;

/// <summary>MA-11: method resolution for receipts, outlet bills and expenses (PaymentMethodBook) and the master ledgers in the mapping.</summary>
public class PaymentMethodBookTests
{
    private static readonly IReadOnlyList<TenderMethod> Owner =
    [
        .. PaymentMethodBook.Defaults,
        new("WALLET", "Paytm wallet", "wallet", false, true, true, true, true, true, "1010"),
        new("CASH-2", "Counter cash", "cash", true, false, true, false, true, true, "1000"),
        new("CHEQUE", "Cheque", "bank", false, true, true, false, false, false, "1030"),
    ];

    [Fact]
    public void Empty_list_uses_the_defaults_with_the_old_messages()
    {
        Assert.Equal("Select a payment method (Cash, UPI, Card or Bank transfer).",
            Assert.Throws<HccException>(() => PaymentMethodBook.Pick([], "Wallet", MoneyMasterValues.FrontDesk, "x")).Message);
        Assert.Equal("Select Cash, UPI or Card.", Assert.Throws<HccException>(() => PaymentMethodBook.Pick(null, "Bank transfer", MoneyMasterValues.Pos, "x")).Message);
        Assert.Equal("Choose a funding source.", Assert.Throws<HccException>(() => PaymentMethodBook.Pick(null, "Card", MoneyMasterValues.Expenses, null, false)).Message);
        Assert.Equal(["Cash", "UPI", "Bank transfer", "Vendor credit"], PaymentMethodBook.NamesAt(null, MoneyMasterValues.Expenses));
        Assert.True(PaymentMethodBook.Pick(null, "cash", MoneyMasterValues.FrontDesk, null).SettlesToTill);
    }

    [Fact]
    public void Owner_methods_resolve_by_name_or_code_and_follow_place_reference_and_retirement()
    {
        Assert.Equal("Paytm wallet", PaymentMethodBook.Pick(Owner, "WALLET", MoneyMasterValues.Pos, "W1").Name);
        Assert.Equal("Enter the transaction reference for a non-cash payment.",
            Assert.Throws<HccException>(() => PaymentMethodBook.Pick(Owner, "Paytm wallet", MoneyMasterValues.FrontDesk, " ")).Message);
        Assert.Equal("Paytm wallet", PaymentMethodBook.Pick(Owner, "Paytm wallet", MoneyMasterValues.Expenses, null, requireReference: false).Name);
        Assert.Equal("Select Cash, UPI, Card or Paytm wallet.", Assert.Throws<HccException>(() => PaymentMethodBook.Pick(Owner, "Counter cash", MoneyMasterValues.Pos, null)).Message);
        var retired = Assert.Throws<HccException>(() => PaymentMethodBook.Pick(Owner, "Cheque", MoneyMasterValues.FrontDesk, "C1"));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, retired.Code);
        Assert.Equal("MASTER_RETIRED", retired.Details!["reason"].GetString());
        Assert.Equal("Cheque", PaymentMethodBook.Pick(Owner, "Cheque", MoneyMasterValues.FrontDesk, "C1", allowRetired: true).Name); // refund to the original method
    }

    [Fact]
    public void Drawer_cash_is_settles_to_a_till_not_the_word_cash()
    {
        Assert.True(PaymentMethodBook.SettlesToTill(Owner, "Counter cash"));
        Assert.False(PaymentMethodBook.SettlesToTill(Owner, "Paytm wallet"));
        Assert.True(PaymentMethodBook.SettlesToTill(Owner, "Cash"));
        Assert.True(PaymentMethodBook.SettlesToTill([new("CASH", "Cash", "cash", true, false, true, true, true)], "Cash"));
        Assert.False(PaymentMethodBook.SettlesToTill(null, "Something old"));
        Assert.Equal(new CashMovement(CashMovementKind.Expense, new Paise(-500)), ExpenseRules.DrawerMovement(true, new Paise(500), false));
        Assert.Null(ExpenseRules.DrawerMovement(false, new Paise(500), false));

        var d = new DateOnly(2026, 10, 24);
        ExpenseFact F(long id, string m, long a) => new(id, "V", new Paise(a), d, m, ExpenseState.Posted, false);
        var t = ExpenseRules.Totals([F(1, "Counter cash", 100), F(2, "Paytm wallet", 200), F(3, "Vendor credit", 300), F(4, "Cash", 400)], d, Owner);
        Assert.Equal((500L, 200L, 300L, 1000L, 4), (t.Cash.Value, t.NonCash.Value, t.VendorCredit.Value, t.Total.Value, t.Count));
        var old = ExpenseRules.Totals([F(1, "Cash", 100), F(2, "UPI", 200), F(3, "Bank transfer", 50), F(4, "Vendor credit", 300)], d);
        Assert.Equal((100L, 250L, 300L), (old.Cash.Value, old.NonCash.Value, old.VendorCredit.Value));
    }

    [Fact]
    public void Master_ledgers_sit_between_the_defaults_and_the_owner_overrides()
    {
        var master = new Dictionary<string, string> { ["method:Paytm wallet"] = "1010", ["funding:Paytm wallet"] = "1010", ["expense:Linen"] = "5010", ["expense:Bad"] = "9999", ["expense:Gone"] = "7777" };
        var m = ChartOfAccounts.Effective(new Dictionary<string, string> { ["expense:Linen"] = "5050" }, master);
        Assert.Equal("1010", m["method:Paytm wallet"]);
        Assert.Equal("1010", m["funding:Paytm wallet"]);
        Assert.Equal("5050", m["expense:Linen"]);              // the explicit override wins
        Assert.False(m.ContainsKey("expense:Bad"));            // never Suspense
        Assert.False(m.ContainsKey("expense:Gone"));           // only codes of the chart
        Assert.Equal("1000", m["method:Cash"]);                // defaults unchanged
        Assert.Equal(ChartOfAccounts.DefaultMapping.Count, ChartOfAccounts.Effective(null).Count);
    }
}
