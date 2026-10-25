using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Billing.Tests.Domain;

public class BillingDomainTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Expected_cash_parts_follow_the_prototype_shiftExpected_formula()
    {
        // Till decision pack worked example, option A (DEC-052): 5,000 + 3,000 − 500 + 1,200 restaurant = 8,700; UPI and room charges never enter.
        var mv = new List<CashMovement>
        {
            CashDrawer.ForReceipt(ReceiptKind.Payment, Paise.FromRupees(3000)),
            CashDrawer.ForReceipt(ReceiptKind.Refund, Paise.FromRupees(500)),
            new(CashMovementKind.PosSale, Paise.FromRupees(1200)),
        };
        var e = CashDrawer.Expected(Paise.FromRupees(5000), mv);
        Assert.Equal(Paise.FromRupees(8700), e.Expected);
        Assert.Equal(Paise.FromRupees(500), e.Refunds);
        Assert.Equal(Paise.FromRupees(1200), e.Restaurant);
        Assert.True(e.Consistent);
    }

    [Fact]
    public void Deposit_returns_and_expenses_leave_the_drawer_and_reversals_come_back()
    {
        var mv = new List<CashMovement>
        {
            CashDrawer.ForReceipt(ReceiptKind.Deposit, Paise.FromRupees(500)),
            CashDrawer.ForReceipt(ReceiptKind.DepositReturn, Paise.FromRupees(500)),
            new(CashMovementKind.Expense, Paise.FromRupees(-1800)),
            new(CashMovementKind.ExpenseReversal, Paise.FromRupees(1800)),
            new(CashMovementKind.PosVoid, Paise.FromRupees(-200)),
        };
        var e = CashDrawer.Expected(Paise.FromRupees(1000), mv);
        Assert.Equal(Paise.FromRupees(800), e.Expected);
        Assert.Equal(Paise.Zero, e.Expenses); // posted −1,800 and its reversal +1,800 net to zero (prototype expenseCounts)
        Assert.Equal(Paise.FromRupees(500), e.Returns);
    }

    [Fact]
    public void Denomination_grid_totals_in_paise()
    {
        var notes = new Dictionary<string, int> { ["500"] = 20, ["200"] = 3, ["10"] = 5 };
        Assert.Equal(new Paise(1_065_000 + 1_250), CashDrawer.DenominationTotal(notes, 1_250));
        Assert.Throws<HccException>(() => CashDrawer.DenominationTotal(new Dictionary<string, int> { ["2000"] = 1 }, 0));
        Assert.Throws<HccException>(() => CashDrawer.DenominationTotal(new Dictionary<string, int> { ["500"] = -1 }, 0));
    }

    [Fact]
    public void Approval_expiry_matches_folioApprovalExpired()
    {
        var exp = T0 + ApprovalRules.TimeToLive;
        var later = exp.AddMinutes(1);
        Assert.False(ApprovalRules.IsExpired(ApprovalStatus.Pending, AdjustmentAction.Discount, exp, exp));
        Assert.True(ApprovalRules.IsExpired(ApprovalStatus.Pending, AdjustmentAction.Discount, exp, later));
        Assert.False(ApprovalRules.IsExpired(ApprovalStatus.Approved, AdjustmentAction.Discount, exp, later)); // posted at approval
        Assert.True(ApprovalRules.IsExpired(ApprovalStatus.Approved, AdjustmentAction.Refund, exp, later));    // must execute in time
        Assert.False(ApprovalRules.IsExpired(ApprovalStatus.Executed, AdjustmentAction.Refund, exp, later));
        Assert.False(ApprovalRules.IsOutstanding(ApprovalStatus.Approved, AdjustmentAction.Refund, exp, later));
    }

    [Fact]
    public void Refund_remainder_and_departed_limit()
    {
        Assert.Equal(new Paise(25_000), RefundRules.Remainder(new Paise(100_000), new Paise(50_000), new Paise(25_000)));
        Assert.Equal(Paise.Zero, RefundRules.Remainder(new Paise(100_000), new Paise(75_000), new Paise(75_000)));
        Assert.Equal(new Paise(20_000), RefundRules.DepartedLimit(new Paise(-30_000), new Paise(10_000)));
        Assert.Equal(Paise.Zero, RefundRules.DepartedLimit(new Paise(5_000), Paise.Zero));
    }

    /// <summary>
    /// Rounding: production rounds half away from zero (contracts §4 rule 3); the prototype uses JavaScript Math.round, which
    /// rounds halves toward +∞. They differ only on negative half-paisa taxes (discount/correction lines whose tax ends in .5):
    /// −5 paise at 10 % → prototype −0 (0), production −1; −15 paise → prototype −1, production −2. Positive lines and every
    /// BASE / Room 105 figure are identical. Recorded for the owner/lead (P4 report).
    /// </summary>
    [Theory]
    [InlineData(-5, 1000, 0, -1)]
    [InlineData(-15, 1000, -1, -2)]
    [InlineData(5, 1000, 1, 1)]
    [InlineData(-50_000, 1000, -5_000, -5_000)]
    public void Negative_half_paisa_tax_differs_from_the_prototype_Math_round(long amount, int bps, long prototype, long production)
    {
        var js = (long)Math.Floor(amount * bps / 10000.0 + 0.5); // Math.round semantics
        Assert.Equal(prototype, js == 0 ? 0 : js);
        Assert.Equal(production, MoneyMath.TaxOf(new Paise(amount), bps).Value);
    }

    [Fact]
    public void Folio_statement_wraps_the_P0_kernel_and_rejects_a_bad_tax_snapshot()
    {
        var d = new DateOnly(2026, 10, 24);
        var lines = new List<PostedLine>
        {
            new(1, ChargeKind.Room, d, "Room night · 2026-10-24", new Paise(200_000), 1000, new Paise(20_000), null),
            new(2, ChargeKind.Discount, d, "Discount", new Paise(-50_000), 1000, new Paise(-5_000), null),
        };
        var receipts = new List<PostedReceipt>
        {
            new(1, ReceiptKind.Payment, "UPI", new Paise(100_000), d, null, "R"),
            new(2, ReceiptKind.Deposit, "Cash", new Paise(50_000), d, null, ""),
            new(3, ReceiptKind.Refund, "UPI", new Paise(10_000), d, 1, ""),
            new(4, ReceiptKind.CompanyCredit, "Company credit", new Paise(20_000), d, null, ""),
        };
        var f = FolioStatement.Compute(lines, receipts);
        Assert.Equal((150_000L, 15_000L, 165_000L, 90_000L, 50_000L, 20_000L, 55_000L),
            (f.Net.Value, f.Tax.Value, f.Total.Value, f.Paid.Value, f.Deposit.Value, f.CompanyCredit.Value, f.Balance.Value));
        Assert.Equal(new Paise(10_000), FolioStatement.RefundedAgainst(receipts, 1));
        var bad = new List<PostedLine> { lines[0] with { Tax = new Paise(19_999) } };
        Assert.Throws<InvalidOperationException>(() => FolioStatement.Compute(bad, receipts));
    }

    [Fact]
    public void Tenders_require_a_reference_except_cash()
    {
        Assert.Equal("Cash", Tenders.Validate("Cash", null));
        Assert.Equal("UPI", Tenders.Validate("UPI", "DEMO-UPI-1"));
        Assert.Throws<HccException>(() => Tenders.Validate("UPI", " "));
        Assert.Throws<HccException>(() => Tenders.Validate("Cheque", "x"));
        Assert.Throws<HccException>(() => Tenders.Validate("Company credit", "x"));
    }
}
