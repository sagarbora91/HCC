using Hcc.Contracts.Closing;
using Hcc.Domain.Closing;

namespace Hcc.Closing.Tests.Unit;

/// <summary>Cash-book arithmetic (prototype cbDay/cbRange F4: the range foots when a count difference occurred; carry-forward; over/short reason).</summary>
public sealed class CashBookCalculatorTests
{
    private static readonly DateOnly D1 = new(2026, 10, 24);
    private static readonly DateOnly D2 = new(2026, 10, 25);
    private static readonly DateTime T = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<DateOnly, IReadOnlyList<CashBookEventView>> NoEvents = new Dictionary<DateOnly, IReadOnlyList<CashBookEventView>>();

    private static CashBookMovement M(long id, long shift, DateOnly d, string kind, long amt) => new(id, shift, "FD-1", d, kind, amt, kind, "ref", 1, T);

    [Fact]
    public void Short_count_is_its_own_line_carries_forward_and_the_range_foots()
    {
        // Day 1: float 5,000.00, +2,000.00 cash, -300.00 expense -> expected 6,700.00; cashier counts 6,650.00 (50.00 short).
        // Day 2: opening carries 6,650.00; +100.00 cash; counted exactly.
        var shifts = new[]
        {
            new CashBookShift(1, "FD-1", D1, "Closed", 500000, 670000, 665000, 7, 8, "Short by fifty rupees"),
            new CashBookShift(2, "FD-1", D2, "Closed", 665000, 675000, 675000, 7, null, null),
        };
        var moves = new[] { M(1, 1, D1, "payment", 200000), M(2, 1, D1, "expense", -30000), M(3, 2, D2, "payment", 10000) };
        var data = new CashBookData(shifts, moves, new Dictionary<string, long>(), [], NoEvents, new HashSet<DateOnly> { D1, D2 });
        var r = CashBookCalculator.Compute(D1, D2, D2, data);
        Assert.Equal(2, r.Days.Count);
        var d1 = r.Days[0];
        Assert.Equal((500000L, 200000L, 30000L, 670000L), (d1.OpeningPaise, d1.ReceiptsPaise, d1.PaymentsPaise, d1.ClosingPaise));
        Assert.Equal((670000L, 665000L, -5000L, 665000L), (d1.ExpectedPaise!.Value, d1.CountedPaise!.Value, d1.DifferencePaise!.Value, d1.CarriedPaise));
        Assert.Equal("Short by fifty rupees", d1.DifferenceReason);
        Assert.Equal((8, true), (d1.ApprovedBy!.Value, d1.Reconciles));
        Assert.Equal(665000, r.Days[1].OpeningPaise);
        Assert.Equal((-5000L, 0L, 675000L, 675000L), (r.DifferencesPaise, r.GapsPaise, r.ClosingPaise, r.BookClosingPaise));
        Assert.Equal(r.ClosingPaise, r.OpeningPaise + r.ReceiptsPaise - r.PaymentsPaise + r.DifferencesPaise + r.GapsPaise);
    }

    [Fact]
    public void Uncounted_open_day_carries_the_book_balance_and_an_earlier_carry_opens_a_day_without_a_shift()
    {
        var data = new CashBookData([], [], new Dictionary<string, long> { ["FD-1"] = 123400 }, [], NoEvents, new HashSet<DateOnly>());
        var r = CashBookCalculator.Compute(D1, D2, D2, data);
        var day = Assert.Single(r.Days);
        Assert.Equal(("Open", 123400L, 123400L, (long?)null, (long?)null), (day.Status, day.OpeningPaise, day.CarriedPaise, day.CountedPaise, day.DifferencePaise));
        Assert.Empty(CashBookCalculator.Compute(D2.AddDays(1), D2.AddDays(5), D2, data).Days); // future dates are not listed
    }

    [Fact]
    public void Banking_and_a_reversed_expense_land_on_the_right_side()
    {
        var shifts = new[] { new CashBookShift(1, "FD-1", D1, "Open", 100000, null, null, null, null, null) };
        var moves = new[] { M(1, 1, D1, "banking", -60000), M(2, 1, D1, "expense-reversal", 5000) };
        var data = new CashBookData(shifts, moves, new Dictionary<string, long>(), [], NoEvents, new HashSet<DateOnly>());
        var day = CashBookCalculator.Compute(D1, D1, D1, data).Days.Single();
        Assert.Equal((5000L, 60000L, 60000L, 45000L), (day.ReceiptsPaise, day.PaymentsPaise, day.BankedPaise, day.ClosingPaise));
        Assert.Equal(("receipt", "payment"), (day.Entries[1].Group, day.Entries[0].Group));
    }
}
