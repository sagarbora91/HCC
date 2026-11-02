using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Invoices.Tests.Domain;

/// <summary>LF A2 kernel rules (LF-CONTRACT §4) — the prototype's tests/stream-lf-bill.mjs figures: 2 nights × ₹2,000 at 5 % + laundry ₹500 at 18 %.</summary>
public class BillCorrectionTests
{
    private static readonly DateOnly A = new(2026, 10, 24), D = new(2026, 10, 26);
    private static readonly BillLine[] Live = [new(1, "room", A, new Paise(200_000), 500), new(2, "room", A.AddDays(1), new Paise(200_000), 500), new(3, "service", A, new Paise(50_000), 1800)];
    private static int RoomBps(DateOnly d, Paise p) => p.Value > 750_000 ? 1800 : 500;
    private static int ChargeBps(DateOnly d, GstCategory c) => c == GstCategory.Food ? 500 : 1800;

    [Fact]
    public void Preview_lowers_a_night_removes_a_line_adds_a_charge()
    {
        var (p, r) = BillCorrectionRules.Preview(A, D, Live, [new BillChange.RoomRate(A, new Paise(150_000)), new BillChange.RemoveLine(3), new BillChange.AddLine("Tea", new Paise(10_000), GstCategory.Food, A.AddDays(1))], RoomBps, ChargeBps);
        Assert.Null(r);
        Assert.Equal(new Paise(479_000), p!.OldTotal); // 400000 + 20000 + 50000 + 9000
        Assert.Equal(new Paise(378_000), p.NewTotal); // 150000+7500 + 200000+10000 + 10000+500
        Assert.Equal(new Paise(-101_000), p.Delta);
        Assert.Equal(BillCorrectionRules.RefundDue, p.Settlement);
        Assert.Equal(new Paise(150_000), p.NewLines.Single(l => l.Kind == "room" && l.Night == A).Amount);
    }

    [Fact]
    public void A_corrected_night_takes_its_new_price_slab_and_the_credit_note_reverses_the_whole_invoice_tax()
    {
        var (p, _) = BillCorrectionRules.Preview(A, D, Live, [new BillChange.RoomRate(A.AddDays(1), new Paise(900_000))], RoomBps, ChargeBps);
        Assert.Equal(1800, p!.NewLines.Single(l => l.Night == A.AddDays(1) && l.Kind == "room").Bps);
        Assert.Equal(BillCorrectionRules.BalanceDue, p.Settlement);
        var credit = BillCorrectionRules.CreditLines(Live);
        var creditTax = credit.Aggregate(Paise.Zero, (a, l) => a + GstMath.LineTax(l.Amount, l.Bps));
        Assert.Equal(-Live.Aggregate(Paise.Zero, (a, l) => a + GstMath.LineTax(l.Amount, l.Bps)), creditTax);
    }

    [Fact]
    public void Refusals_and_the_s34_window()
    {
        Assert.Equal("Add at least one change to the bill.", BillCorrectionRules.Preview(A, D, Live, [], RoomBps, ChargeBps).Refusal);
        Assert.Equal("Choose a night of the stay.", BillCorrectionRules.Preview(A, D, Live, [new BillChange.RoomRate(D, new Paise(1))], RoomBps, ChargeBps).Refusal);
        Assert.StartsWith("Change a night", BillCorrectionRules.Preview(A, D, Live, [new BillChange.RemoveLine(1)], RoomBps, ChargeBps).Refusal);
        Assert.StartsWith("This line is not", BillCorrectionRules.Preview(A, D, Live, [new BillChange.RemoveLine(9)], RoomBps, ChargeBps).Refusal);
        Assert.Equal(new DateOnly(2027, 11, 30), BillCorrectionRules.Deadline(new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2027, 11, 30), BillCorrectionRules.Deadline(new DateOnly(2027, 2, 1)));
        Assert.Null(BillCorrectionRules.WindowRefusal(D, new DateOnly(2027, 11, 30), "HPE/2627/0001"));
        Assert.Contains("s.34(2)", BillCorrectionRules.WindowRefusal(D, new DateOnly(2027, 12, 1), "HPE/2627/0001"));
        Assert.Equal("Superseded by HPE/2627/0031 (credit note CRN/0004)", BillCorrectionRules.SupersededByNote("HPE/2627/0031", "CRN/0004"));
    }
}
