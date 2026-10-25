/*
 * HCC rate adjustments — length-of-stay tiers and occupancy bands (C# port of the lab H02 adaptation).
 *
 * Derived from HAIP, file apps/api/src/modules/rate-plan/rate-pricing.util.ts
 *   repository https://github.com/TelivityAI/haip.git, commit 3a2f1c89139e8d7d915528abac706a512af16970
 *   source SHA-256 3dcac4f4d29e800654c8f6f84fb6ac0d78670e15c13fcda7534dbcd18fac7c57
 * Licensed under the Apache License, Version 2.0 — see ../ThirdParty/LICENSE-HAIP-Apache-2.0.txt (copy of the HAIP root LICENSE).
 *
 * Modified by HCC (Apache-2.0 §4(b) notice of changes):
 *  24 Oct 2026 (lab, 07 Integration Lab/qualifications/H02-los-tier-pricing/src/rate-adjustments.ts):
 *  - money is integer paise; percentages are integer basis points (HCC DEC-023);
 *  - tiers and bands are validated (positive integer nights, unique tiers, half-open non-overlapping bands);
 *  - occupancy bands are half-open [fromPct, toPct) so 50 % can never match two bands; a band ending at 100 includes 100;
 *  - LOS and occupancy bps are summed and rounded ONCE (half away from zero, symmetric for negatives), then fixed amounts are added;
 *  - a negative nightly result is an error, never silently clamped;
 *  - donor nightsBetween() dropped (HCC uses validated stay dates).
 *  25 Oct 2026 (production P0): ported from TypeScript to C# (long paise, checked arithmetic instead of the JS
 *  safe-integer check); behaviour unchanged. Recorded in 08 Production/third-party/source-reuse.json.
 * Not yet used by a production use case (LOS discounts await owner approval, A8 / module 13).
 */

using Hcc.Domain.Money;

namespace Hcc.Domain.Pricing;

public sealed record LosTier(int MinNights, int AdjBps);

public sealed record OccupancyBand(int FromPct, int ToPct, int AdjBps);

public enum RateRuleErrorCode
{
    InvalidRule,
    InvalidInput,
    NegativeRate,
}

public sealed class RateRuleException : Exception
{
    public RateRuleException(RateRuleErrorCode code, string message) : base(message) => Code = code;

    public RateRuleException() : this(RateRuleErrorCode.InvalidInput, "Invalid rate input.") { }

    public RateRuleException(string message) : this(RateRuleErrorCode.InvalidInput, message) { }

    public RateRuleException(string message, Exception innerException) : base(message, innerException) => Code = RateRuleErrorCode.InvalidInput;

    public RateRuleErrorCode Code { get; }
}

public static class RateAdjustments
{
    private const int MaxBps = 10_000; // ±100 %

    public static void ValidateLosTiers(IReadOnlyCollection<LosTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        var seen = new HashSet<int>();
        foreach (var t in tiers)
        {
            if (t.MinNights < 1)
                throw new RateRuleException(RateRuleErrorCode.InvalidRule, $"LOS tier minNights must be a positive whole number (got {t.MinNights}).");
            if (Math.Abs(t.AdjBps) > MaxBps)
                throw new RateRuleException(RateRuleErrorCode.InvalidRule, $"LOS tier adjustment must be whole basis points within ±{MaxBps}.");
            if (!seen.Add(t.MinNights))
                throw new RateRuleException(RateRuleErrorCode.InvalidRule, $"Two LOS tiers start at {t.MinNights} nights.");
        }
    }

    /// <summary>Highest qualifying tier (21+ beats 14+ beats 7+); null when none applies.</summary>
    public static LosTier? SelectLosTier(IReadOnlyCollection<LosTier> tiers, int nights)
    {
        if (nights < 1) throw new RateRuleException(RateRuleErrorCode.InvalidInput, "Nights must be a positive whole number.");
        ValidateLosTiers(tiers);
        return tiers.Where(t => nights >= t.MinNights).OrderByDescending(t => t.MinNights).FirstOrDefault();
    }

    public static void ValidateOccupancyBands(IReadOnlyCollection<OccupancyBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var sorted = bands.OrderBy(b => b.FromPct).ToList();
        foreach (var b in sorted)
        {
            if (b.FromPct < 0 || b.ToPct > 100 || b.FromPct >= b.ToPct)
                throw new RateRuleException(RateRuleErrorCode.InvalidRule, $"Occupancy band {b.FromPct}–{b.ToPct} % is invalid (whole percent, 0 ≤ from < to ≤ 100).");
            if (Math.Abs(b.AdjBps) > MaxBps)
                throw new RateRuleException(RateRuleErrorCode.InvalidRule, "Band adjustment must be whole basis points within ±10000.");
        }

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].FromPct < sorted[i - 1].ToPct)
                throw new RateRuleException(RateRuleErrorCode.InvalidRule,
                    $"Occupancy bands {sorted[i - 1].FromPct}–{sorted[i - 1].ToPct} and {sorted[i].FromPct}–{sorted[i].ToPct} overlap.");
        }
    }

    /// <summary>Band containing pct: fromPct ≤ pct &lt; toPct, or pct = 100 for a band ending at 100.</summary>
    public static OccupancyBand? SelectOccupancyBand(IReadOnlyCollection<OccupancyBand> bands, int pct)
    {
        if (pct < 0 || pct > 100) throw new RateRuleException(RateRuleErrorCode.InvalidInput, "Occupancy must be a whole percent from 0 to 100.");
        ValidateOccupancyBands(bands);
        return bands.FirstOrDefault(b => (pct >= b.FromPct && pct < b.ToPct) || (pct == 100 && b.ToPct == 100));
    }

    /// <summary>base × (10000 + bps) / 10000, rounded half away from zero (symmetric for reversals).</summary>
    public static long ApplyBps(long basePaise, int bps)
    {
        if (basePaise < 0) throw new RateRuleException(RateRuleErrorCode.InvalidInput, "Base rate must be non-negative integer paise.");
        long num;
        try
        {
            num = checked(basePaise * (MoneyMath.BpsScale + (long)bps));
        }
        catch (OverflowException)
        {
            throw new RateRuleException(RateRuleErrorCode.InvalidInput, "Rate too large.");
        }

        return MoneyMath.DivideRoundHalfAwayFromZero(num, MoneyMath.BpsScale);
    }

    /// <summary>One night: (LOS bps + occupancy bps) applied once, then fixed amounts. Throws NegativeRate rather than clamping.</summary>
    public static long PriceNight(long basePaise, LosTier? los = null, OccupancyBand? band = null, IEnumerable<long>? fixedPaise = null)
    {
        var bps = (los?.AdjBps ?? 0) + (band?.AdjBps ?? 0);
        var rate = ApplyBps(basePaise, bps);
        foreach (var f in fixedPaise ?? [])
            rate = checked(rate + f);
        if (rate < 0) throw new RateRuleException(RateRuleErrorCode.NegativeRate, "Adjustments would make the nightly rate negative.");
        return rate;
    }
}
