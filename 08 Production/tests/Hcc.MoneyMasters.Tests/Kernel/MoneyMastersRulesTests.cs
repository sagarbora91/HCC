using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.MoneyMasters;
using R = Hcc.Domain.MoneyMasters.MoneyMastersRules;

namespace Hcc.MoneyMasters.Tests.Kernel;

/// <summary>Money-master rules without a database (prototype money-master-specs.ts parity).</summary>
public class MoneyMastersRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static MoneyRow Row(int id, string code, string name, bool active = true, params (string, object?)[] fields) =>
        new(id, code, name, id * 10, active, active ? null : new DateOnly(2026, 10, 20), DateTime.UtcNow, [0, 0, 0, 0, 0, 0, 0, (byte)id], fields.ToDictionary(f => f.Item1, f => f.Item2));

    private static readonly IReadOnlyList<MoneyRow> Accounts =
    [
        Row(1, "TILL", "Front-desk cash drawer", true, (R.Kind, "till")),
        Row(2, "BANK-1", "Hotel current account", true, (R.Kind, "bank"), (R.DefaultFor, new List<string> { "upi", "card" })),
        Row(3, "OLD-BANK", "Old account", false, (R.Kind, "bank")),
    ];

    private static SavePaymentMethodRequest Pm(string kind, string? settles, params string[] at) => new(null, "X1", "X one", kind, settles, true, at);

    [Fact]
    public void Payment_method_is_normalised_code_capitals_places_in_canonical_order_surcharge_in_range()
    {
        var d = R.PaymentMethod(new SavePaymentMethodRequest(null, " paytm ", " Paytm wallet ", "WALLET", "bank-1", true, ["expenses", "FRONTDESK", "frontdesk"], 150, "1010"));
        Assert.Equal(("PAYTM", "Paytm wallet"), (d.Row.Code, d.Row.Name));
        Assert.Equal("wallet", d.Fields[R.Kind]);
        Assert.Equal("BANK-1", d.Fields[R.SettlesTo]);
        Assert.Equal(["frontdesk", "expenses"], (IReadOnlyList<string>)d.Fields[R.UsableAt]!);
        Assert.Equal(150, d.Fields[R.SurchargePct]);
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => R.PaymentMethod(new SavePaymentMethodRequest(null, "X", "X", "upi", "BANK-1", true, ["pos"], 1500)))));
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => R.PaymentMethod(new SavePaymentMethodRequest(null, "X", "X", "cheque", null, false, ["pos"])))));
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => R.PaymentMethod(new SavePaymentMethodRequest(null, "X", "X", "upi", "BANK-1", false, ["bar"])))));
        var ledger = Assert.Throws<HccException>(() => R.PaymentMethod(new SavePaymentMethodRequest(null, "X", "X", "upi", "BANK-1", false, ["pos"], 0, "5000")));
        Assert.Equal("ledger", ledger.Details!["field"].GetString());
    }

    [Fact]
    public void Cash_settles_to_a_till_credit_to_nothing_and_never_at_the_desk_the_rest_to_a_bank_account()
    {
        R.CheckPaymentMethod(R.PaymentMethod(Pm("cash", "TILL", "frontdesk")), null, 0, Accounts);
        R.CheckPaymentMethod(R.PaymentMethod(Pm("upi", "BANK-1", "pos")), null, 0, Accounts);
        R.CheckPaymentMethod(R.PaymentMethod(Pm("credit", null, "expenses")), null, 0, Accounts);
        Assert.Contains("till", Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("cash", "BANK-1", "frontdesk")), null, 0, Accounts)).Message, StringComparison.Ordinal);
        Assert.Contains("no account", Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("credit", "BANK-1", "expenses")), null, 0, Accounts)).Message, StringComparison.Ordinal);
        Assert.Contains("not a guest payment", Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("credit", null, "frontdesk")), null, 0, Accounts)).Message, StringComparison.Ordinal);
        Assert.Contains("bank account", Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("card", "TILL", "pos")), null, 0, Accounts)).Message, StringComparison.Ordinal);
        Assert.Equal(MasterReasons.Invalid, Reason(Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("upi", "NOPE", "pos")), null, 0, Accounts))));
        Assert.Equal(MasterReasons.Retired, Reason(Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("upi", "OLD-BANK", "pos")), null, 0, Accounts))));
    }

    [Fact]
    public void A_used_method_keeps_kind_and_settles_to_an_unused_one_may_change()
    {
        var old = Row(5, "X1", "X one", true, (R.Kind, "upi"), (R.SettlesTo, "BANK-1"), (R.UsableAt, new List<string> { "pos" }));
        var e = Assert.Throws<HccException>(() => R.CheckPaymentMethod(R.PaymentMethod(Pm("wallet", "BANK-1", "pos")), old, 3, Accounts));
        Assert.Equal((HccErrorCode.VALIDATION, MasterReasons.Invalid), (e.Code, Reason(e)));
        Assert.Contains("3 records", e.Message, StringComparison.Ordinal);
        R.CheckPaymentMethod(R.PaymentMethod(Pm("wallet", "BANK-1", "pos")), old, 0, Accounts);
        // A retired account already on the row stays valid; choosing it newly is refused.
        var onOld = Row(6, "X1", "X one", true, (R.Kind, "upi"), (R.SettlesTo, "OLD-BANK"));
        R.CheckPaymentMethod(R.PaymentMethod(Pm("upi", "OLD-BANK", "pos")), onOld, 2, Accounts);
    }

    [Fact]
    public void Retire_rules_last_front_desk_method_and_accounts_with_active_methods()
    {
        var cash = Row(1, "CASH", "Cash", true, (R.UsableAt, new List<string> { "frontdesk", "pos" }), (R.SettlesTo, "TILL"));
        var upi = Row(2, "UPI", "UPI", true, (R.UsableAt, new List<string> { "frontdesk" }), (R.SettlesTo, "BANK-1"));
        R.EnsureMethodMayRetire(cash, [cash, upi]);
        Assert.Equal(MasterReasons.InUse, Reason(Assert.Throws<HccException>(() => R.EnsureMethodMayRetire(cash, [cash, upi with { Active = false }]))));
        var e = Assert.Throws<HccException>(() => R.EnsureAccountMayRetire(Accounts[0], [cash, upi]));
        Assert.Equal((MasterReasons.InUse, true), (Reason(e), e.Message.Contains("Cash", StringComparison.Ordinal)));
        R.EnsureAccountMayRetire(Accounts[0], [cash with { Active = false }, upi]);
    }

    [Fact]
    public void Bank_accounts_IFSC_last_four_digits_till_shape_and_one_default_per_kind()
    {
        var d = R.BankAccount(new SaveBankAccountRequest(null, "b2", "Second", "BANK", "Demo", "9876", "abcd0123456", 500000, ["wallet"]));
        Assert.Equal(("B2", "ABCD0123456", 500000L), (d.Row.Code, (string)d.Fields[R.Ifsc]!, (long)d.Fields[R.OpeningBalance]!));
        Assert.Contains("IFSC", Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank", "Demo", null, "ABC123"))).Message, StringComparison.Ordinal);
        Assert.Contains("last 4", Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank", "Demo", "12ab"))).Message, StringComparison.Ordinal);
        Assert.Contains("till has no bank", Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "T2", "Till 2", "till", "Demo"))).Message, StringComparison.Ordinal);
        Assert.Contains("bank name", Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank"))).Message, StringComparison.Ordinal);
        Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank", "Demo", null, null, 0, ["cash"])));
        Assert.Throws<HccException>(() => R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank", "Demo", null, null, -1)));
        var dup = R.BankAccount(new SaveBankAccountRequest(null, "B2", "Second", "bank", "Demo", null, null, 0, ["upi"]));
        Assert.Contains("already the default", Assert.Throws<HccException>(() => R.CheckBankAccount(dup, null, 0, 0, Accounts)).Message, StringComparison.Ordinal);
        R.CheckBankAccount(dup, Accounts[1], 0, 0, Accounts); // the account itself
    }

    [Fact]
    public void Opening_balance_is_setup_only_and_type_is_fixed_while_methods_settle()
    {
        var old = Row(2, "BANK-1", "Hotel current account", true, (R.Kind, "bank"), (R.Bank, "Demo"), (R.OpeningBalance, 0L), (R.DefaultFor, new List<string>()));
        var edit = R.BankAccount(new SaveBankAccountRequest(2, "BANK-1", "Hotel current account", "bank", "Demo", null, null, 100000));
        R.CheckBankAccount(edit, old, 3, 0, Accounts);
        Assert.Contains("setup-only", Assert.Throws<HccException>(() => R.CheckBankAccount(edit, old, 3, 7, Accounts)).Message, StringComparison.Ordinal);
        var till = R.BankAccount(new SaveBankAccountRequest(2, "BANK-1", "Hotel current account", "till"));
        Assert.Contains("Type cannot change", Assert.Throws<HccException>(() => R.CheckBankAccount(till, old, 1, 0, Accounts)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expense_category_needs_an_expense_ledger_and_a_non_negative_limit()
    {
        var d = R.ExpenseCategory(new SaveExpenseCategoryRequest(null, "gas", "Kitchen gas", "5050", true, 100000));
        Assert.Equal(("GAS", "5050", true, 100000L), (d.Row.Code, (string)d.Fields[R.Ledger]!, (bool)d.Fields[R.NeedsBill]!, (long)d.Fields[R.ApprovalLimit]!));
        Assert.Equal("ledger", Assert.Throws<HccException>(() => R.ExpenseCategory(new SaveExpenseCategoryRequest(null, "G", "G", "1000"))).Details!["field"].GetString());
        Assert.Throws<HccException>(() => R.ExpenseCategory(new SaveExpenseCategoryRequest(null, "G", "G", "9999")));
        Assert.Throws<HccException>(() => R.ExpenseCategory(new SaveExpenseCategoryRequest(null, "G", "G", "")));
        Assert.Throws<HccException>(() => R.ExpenseCategory(new SaveExpenseCategoryRequest(null, "G", "G", "5000", false, -5)));
    }

    [Fact]
    public void Expense_vendor_GSTIN_PAN_TDS_rules_and_capitals()
    {
        var d = R.ExpenseVendor(new SaveExpenseVendorRequest(null, "v-acme", "Acme Linen", "LAUNDRY", "27abcde1234f1z5", "abcde1234f", true, " Ravi ", "15 days"));
        Assert.Equal(("V-ACME", "laundry", "27ABCDE1234F1Z5", "ABCDE1234F", "Ravi"), (d.Row.Code, (string)d.Fields[R.Type]!, (string)d.Fields[R.Gstin]!, (string)d.Fields[R.Pan]!, (string)d.Fields[R.Contact]!));
        Assert.Contains("GSTIN", Assert.Throws<HccException>(() => R.ExpenseVendor(new SaveExpenseVendorRequest(null, "V", "V", "supplier", "27ABCDE1234F1Z"))).Message, StringComparison.Ordinal);
        Assert.Contains("PAN", Assert.Throws<HccException>(() => R.ExpenseVendor(new SaveExpenseVendorRequest(null, "V", "V", "supplier", null, "ABCD1234F"))).Message, StringComparison.Ordinal);
        Assert.Contains("does not match", Assert.Throws<HccException>(() => R.ExpenseVendor(new SaveExpenseVendorRequest(null, "V", "V", "supplier", "27ABCDE1234F1Z5", "ZZZZZ9999Z"))).Message, StringComparison.Ordinal);
        Assert.Contains("PAN when TDS", Assert.Throws<HccException>(() => R.ExpenseVendor(new SaveExpenseVendorRequest(null, "V", "V", "supplier", null, null, true))).Message, StringComparison.Ordinal);
        Assert.Throws<HccException>(() => R.ExpenseVendor(new SaveExpenseVendorRequest(null, "V", "V", "contractor")));
    }

    [Fact]
    public void Stored_values_resolve_by_name_code_earlier_name_or_case_and_count_onto_rows()
    {
        IReadOnlyList<MoneyRow> rows = [Row(1, "CASH", "Cash"), Row(2, "UPI", "UPI (GPay)"), Row(3, "BANK", "Bank transfer", false)];
        var renames = new Dictionary<string, int> { ["UPI"] = 2 };
        Assert.Equal(1, R.Resolve(rows, renames, "Cash")!.Id);
        Assert.Equal(1, R.Resolve(rows, renames, "cash")!.Id);
        Assert.Equal(2, R.Resolve(rows, renames, "UPI")!.Id);      // earlier name
        Assert.Equal(3, R.Resolve(rows, renames, "BANK")!.Id);     // code; retired rows resolve
        Assert.Null(R.Resolve(rows, renames, "Bitcoin"));
        var counts = R.CountsByRow(rows, renames, [("Cash", 4), ("UPI", 2), ("UPI (GPay)", 1), ("Bitcoin", 9)]);
        Assert.Equal((4, 3), (counts[1], counts[2]));
        Assert.False(counts.ContainsKey(3));
    }

    [Fact]
    public void Diff_shows_values_as_people_read_them()
    {
        var old = Row(2, "BANK-1", "Main", true, (R.Kind, "bank"), (R.Bank, "Demo"), (R.AccountLast4, null), (R.Ifsc, null), (R.OpeningBalance, 0L), (R.DefaultFor, new List<string> { "upi" }));
        var d = R.BankAccount(new SaveBankAccountRequest(2, "BANK-1", "Main account", "bank", "Demo", "4321", null, 250050, ["upi", "card"]));
        var diff = R.Diff(MoneyMasterIds.BankAccounts, old, d, 20);
        Assert.Equal([("name", "Main", "Main account"), ("accountLast4", null, "4321"), ("openingBalance", "₹0", "₹2,500.5"), ("defaultFor", "upi", "upi, card")], diff);
        Assert.Equal("1.5 %", R.Show(R.SurchargePct, 150));
        Assert.Equal("Yes", R.Show(R.NeedsBill, true));
        Assert.Equal(("Payment methods", "payment method"), R.Describe(MoneyMasterIds.PaymentMethods));
    }
}
