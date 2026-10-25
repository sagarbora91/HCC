using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Expenses.Tests.Kernel;

/// <summary>
/// Golden figures from the prototype (tests/stream-f.mjs BL-028/BL-029, BASE fixture): drawer 1,072,500 paise with EXP-101 (Cash, 180,000 paise Posted)
/// inside it; a 30,000 paise cash expense lowers the drawer once and its reversal restores it; vendor credit has no drawer effect.
/// </summary>
public class ExpenseRulesGoldenTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);

    private static ExpenseFact F(long id, string vendor, long amt, string method, string status, bool reversal = false, DateOnly? date = null)
        => new(id, vendor, new Paise(amt), date ?? Day, method, status, reversal);

    // BASE: opening float 500,000 + payments 700,000 + restaurant (POS-F01 cash incl. 10 % tax) 52,500 - expense EXP-101 180,000 = 1,072,500.
    private static List<CashMovement> Base() =>
    [
        new(CashMovementKind.Payment, new Paise(700_000)),
        new(CashMovementKind.PosSale, new Paise(52_500)),
        new(CashMovementKind.Expense, new Paise(-180_000)),
    ];

    [Fact]
    public void Base_drawer_is_1_072_500_with_expenses_180_000()
    {
        var e = CashDrawer.Expected(new Paise(500_000), Base());
        Assert.Equal(1_072_500, e.Expected.Value);
        Assert.Equal(180_000, e.Expenses.Value);
        Assert.True(e.Consistent);
    }

    [Fact]
    public void Cash_expense_then_reversal_counts_the_drawer_exactly_once_and_nets_to_zero()
    {
        var d0 = CashDrawer.Expected(new Paise(500_000), Base()).Expected.Value;
        var posted = ExpenseRules.DrawerMovement("Cash", new Paise(30_000), reversal: false)!;
        Assert.Equal(new CashMovement(CashMovementKind.Expense, new Paise(-30_000)), posted);
        var m = Base();
        m.Add(posted);
        Assert.Equal(d0 - 30_000, CashDrawer.Expected(new Paise(500_000), m).Expected.Value);
        m.Add(ExpenseRules.DrawerMovement("Cash", new Paise(30_000), reversal: true)!);
        var after = CashDrawer.Expected(new Paise(500_000), m);
        Assert.Equal(d0, after.Expected.Value);
        Assert.Equal(180_000, after.Expenses.Value);
    }

    [Theory]
    [InlineData("UPI")]
    [InlineData("Bank transfer")]
    [InlineData("Vendor credit")]
    public void Only_cash_moves_the_drawer(string method)
    {
        Assert.Null(ExpenseRules.DrawerMovement(method, new Paise(600_000), reversal: false));
        Assert.Null(ExpenseRules.DrawerMovement(method, new Paise(600_000), reversal: true));
    }

    [Fact]
    public void Report_figures_net_of_reversals_split_by_funding_source()
    {
        // EXP-101 cash 180,000 posted; EXP-102 UPI 85,000 pending (not counted); a 30,000 cash expense posted then reversed.
        var facts = new[]
        {
            F(101, "Fresh Linen Services", 180_000, "Cash", "Posted"),
            F(102, "Morning Supplies", 85_000, "UPI", "Pending"),
            F(103, "Aqua Water Cans", 30_000, "Cash", "Reversed"),
            F(104, "Aqua Water Cans", -30_000, "Cash", "Posted", reversal: true),
            F(105, "Paint Depot", 600_000, "Vendor credit", "Posted"),
            F(106, "Other day", 99_000, "Cash", "Posted", date: Day.AddDays(1)),
        };
        var t = ExpenseRules.Totals(facts, Day);
        Assert.Equal(180_000, t.Cash.Value);          // the reversed pair nets to zero (prototype reportPack 'cash')
        Assert.Equal(0, t.NonCash.Value);             // the pending UPI item is not posted
        Assert.Equal(600_000, t.VendorCredit.Value);
        Assert.Equal(780_000, t.Total.Value);
        Assert.Equal(-30_000, t.Reversals.Value);
        Assert.Equal(4, t.Count);
    }

    [Fact]
    public void Duplicate_warning_ignores_case_rejected_items_and_reversal_records()
    {
        var facts = new[]
        {
            F(101, "Fresh Linen Services", 180_000, "Cash", "Posted"),
            F(102, "Morning Supplies", 85_000, "UPI", "Rejected"),
            F(103, "Fresh Linen Services", -180_000, "Cash", "Posted", reversal: true),
        };
        Assert.Equal([101L], ExpenseRules.Duplicates(facts, "fresh linen services", new Paise(180_000), Day));
        Assert.Empty(ExpenseRules.Duplicates(facts, "Morning Supplies", new Paise(85_000), Day));   // rejected items do not warn
        Assert.Empty(ExpenseRules.Duplicates(facts, "Fresh Linen Services", new Paise(180_000), Day, exceptId: 101));
        Assert.Empty(ExpenseRules.Duplicates(facts, "Fresh Linen Services", new Paise(180_000), Day.AddDays(1)));
    }

    [Fact]
    public void Validation_follows_the_prototype()
    {
        Assert.Throws<HccException>(() => ExpenseRules.NormalizeNew("", "Supplies", "Cash", 100, null, null));       // Enter a vendor.
        Assert.Throws<HccException>(() => ExpenseRules.NormalizeNew("X", "", "Cash", 100, null, null));              // Choose a category.
        var bad = Assert.Throws<HccException>(() => ExpenseRules.NormalizeNew("X", "Supplies", "Bitcoin", 100, null, null));
        Assert.Equal(HccErrorCode.VALIDATION, bad.Code);                                                              // Choose a funding source.
        Assert.Throws<HccException>(() => ExpenseRules.NormalizeNew("X", "Supplies", "Cash", 0, null, null));
        Assert.Throws<HccException>(() => ExpenseRules.NormalizeNew("X", "Supplies", "Cash", -5, null, null));
        var ok = ExpenseRules.NormalizeNew("  Aqua Water Cans ", "Supplies", "Cash", 30_000, " 20 cans ", new string('r', 200));
        Assert.Equal(("Aqua Water Cans", "20 cans", 120), (ok.Vendor, ok.Notes, ok.Receipt!.Length));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => ExpenseRules.RequiredReversalReason("  ")).Code);
    }

    [Fact]
    public void Transitions_follow_the_prototype()
    {
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanSubmitDraft("Pending", 4, 4)).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanSubmitDraft("Draft", 4, 3)).Code);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanDecide("Draft", 4, 2)).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanDecide("Pending", 2, 2)).Code);
        ExpenseRules.EnsureCanDecide("Pending", 3, 2);
        ExpenseRules.EnsureCanReverse("Posted", false);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanReverse("Reversed", false)).Code);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseRules.EnsureCanReverse("Posted", true)).Code);
    }
}
