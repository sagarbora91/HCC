using Hcc.Domain.Errors;

namespace Hcc.Domain.Money;

/// <summary>
/// Integer-paise arithmetic with basis points. One rounding rule everywhere: half away from zero, symmetric for
/// negative amounts (contracts §4 rule 3; lab H02 <c>applyBps</c>).
/// </summary>
public static class MoneyMath
{
    public const int BpsScale = 10_000;

    /// <summary>numerator / denominator rounded half away from zero. The denominator must be positive.</summary>
    public static long DivideRoundHalfAwayFromZero(long numerator, long denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
        var negative = numerator < 0;
        var abs = negative ? checked(-numerator) : numerator;
        var q = abs / denominator;
        if ((abs % denominator) * 2 >= denominator) q++;
        return negative ? -q : q;
    }

    /// <summary>amount × bps / 10000, rounded half away from zero (tax on a line, a percentage discount).</summary>
    public static Paise PercentOf(Paise amount, int bps)
    {
        if (bps < 0 || bps > BpsScale) throw HccException.Validation("Rate must be 0–10000 basis points.");
        return new Paise(DivideRoundHalfAwayFromZero(checked(amount.Value * bps), BpsScale));
    }

    /// <summary>Tax for one line at its snapshot rate (charges carry their tax snapshot, contracts §4 rule 5).</summary>
    public static Paise TaxOf(Paise amount, int taxBps) => PercentOf(amount, taxBps);
}
