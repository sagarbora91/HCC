using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Payroll;

/// <summary>The fields a statutory-rates command changes (null = keep the value in force on the new version's date).</summary>
public sealed record RateChange(int? BasicBps = null, int? HraBps = null, int? WashBps = null, int? PfEeBps = null, int? PfErBps = null, long? PfWageCap = null,
    int? EsicEeBps = null, int? EsicErBps = null, long? EsicCeiling = null, long? PtMaleExempt = null, long? PtMaleMid = null, long? PtMaleMidAmt = null,
    long? PtStd = null, long? PtFeb = null, long? PtFemaleExempt = null, int? OtBps = null, int? LateBlock = null, StatutoryRounding? Rounding = null,
    bool? EsicPeriod = null);

/// <summary>Result of saving a rate version: the new list, the fields that changed, and what was carried into later versions (F-05).</summary>
public sealed record RateSaveOutcome(IReadOnlyList<PayrollRates> Versions, PayrollRates Saved, IReadOnlyList<string> Changed, IReadOnlyList<string> Carried,
    IReadOnlyList<string> Kept, IReadOnlyList<PayrollRates> Later);

/// <summary>Dated statutory rate versions (prototype hrRatesFor / HR_STAT_RATES_SET after review F-05 and F-06).</summary>
public static class RateVersions
{
    private sealed record Field(string Name, Func<PayrollRates, object> Get, Func<PayrollRates, object, PayrollRates> Set, Func<RateChange, object?> FromChange);

    private static readonly Field[] Fields =
    [
        new("basicBps", r => r.BasicBps, (r, v) => r with { BasicBps = (int)v }, c => c.BasicBps),
        new("hraBps", r => r.HraBps, (r, v) => r with { HraBps = (int)v }, c => c.HraBps),
        new("washBps", r => r.WashBps, (r, v) => r with { WashBps = (int)v }, c => c.WashBps),
        new("pfEeBps", r => r.PfEeBps, (r, v) => r with { PfEeBps = (int)v }, c => c.PfEeBps),
        new("pfErBps", r => r.PfErBps, (r, v) => r with { PfErBps = (int)v }, c => c.PfErBps),
        new("pfWageCap", r => r.PfWageCap, (r, v) => r with { PfWageCap = (long)v }, c => c.PfWageCap),
        new("esicEeBps", r => r.EsicEeBps, (r, v) => r with { EsicEeBps = (int)v }, c => c.EsicEeBps),
        new("esicErBps", r => r.EsicErBps, (r, v) => r with { EsicErBps = (int)v }, c => c.EsicErBps),
        new("esicCeiling", r => r.EsicCeiling, (r, v) => r with { EsicCeiling = (long)v }, c => c.EsicCeiling),
        new("ptMaleExempt", r => r.PtMaleExempt, (r, v) => r with { PtMaleExempt = (long)v }, c => c.PtMaleExempt),
        new("ptMaleMid", r => r.PtMaleMid, (r, v) => r with { PtMaleMid = (long)v }, c => c.PtMaleMid),
        new("ptMaleMidAmt", r => r.PtMaleMidAmt, (r, v) => r with { PtMaleMidAmt = (long)v }, c => c.PtMaleMidAmt),
        new("ptStd", r => r.PtStd, (r, v) => r with { PtStd = (long)v }, c => c.PtStd),
        new("ptFeb", r => r.PtFeb, (r, v) => r with { PtFeb = (long)v }, c => c.PtFeb),
        new("ptFemaleExempt", r => r.PtFemaleExempt, (r, v) => r with { PtFemaleExempt = (long)v }, c => c.PtFemaleExempt),
        new("otBps", r => r.OtBps, (r, v) => r with { OtBps = (int)v }, c => c.OtBps),
        new("lateBlock", r => r.LateBlock, (r, v) => r with { LateBlock = (int)v }, c => c.LateBlock),
        new("rounding", r => r.Rounding, (r, v) => r with { Rounding = (StatutoryRounding)v }, c => c.Rounding),
        new("esicPeriod", r => r.EsicPeriod, (r, v) => r with { EsicPeriod = (bool)v }, c => c.EsicPeriod),
    ];

    /// <summary>Rate version in force for a month (latest effective-from on or before the 1st; the earliest version before any).</summary>
    public static PayrollRates For(IReadOnlyList<PayrollRates> versions, PayMonth ym)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0) return PayrollRates.Defaults();
        var sorted = versions.OrderBy(v => v.From).ToList();
        var r = sorted[0];
        foreach (var v in sorted) if (v.From <= ym.First) r = v;
        return r;
    }

    /// <summary>F-06: default effective date = max(current month, month after the last locked run).</summary>
    public static DateOnly DefaultFrom(DateOnly businessDate, PayMonth? lastLocked)
    {
        var cur = PayMonth.Of(businessDate);
        var next = lastLocked is { } l ? l.Next : cur;
        return (next > cur ? next : cur).First;
    }

    /// <summary>
    /// Builds a new version from the version IN FORCE ON ITS OWN DATE (F-05) with only the changed fields, and carries each changed field
    /// into later versions that had inherited the old value; a later version that set its own value keeps it (and stops the carry).
    /// Locked months never change (<paramref name="lastLocked"/>).
    /// </summary>
    public static RateSaveOutcome Save(IReadOnlyList<PayrollRates> versions, DateOnly from, RateChange change, PayMonth? lastLocked)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(change);
        if (from.Day != 1) throw HccException.Validation("Rates take effect from the 1st of a month.");
        if (lastLocked is { } last && PayMonth.Of(from) <= last)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Rates for {last.Label} and earlier are frozen in locked pay-runs. Choose a later month.");
        var list = versions.Count == 0 ? [PayrollRates.Defaults()] : versions.OrderBy(v => v.From).ToList();
        var baseVersion = For(list, PayMonth.Of(from));
        var r = baseVersion with { From = from };
        foreach (var f in Fields)
            if (f.FromChange(change) is { } v) r = f.Set(r, v);
        r.Validate();
        var changed = Fields.Where(f => !Equals(f.Get(r), f.Get(baseVersion))).ToList();
        var carried = new List<string>();
        var kept = new List<string>();
        var live = changed.ToList();
        var later = new List<PayrollRates>();
        foreach (var v0 in list.Where(v => v.From > from))
        {
            var v = v0;
            foreach (var f in live.ToList())
            {
                if (Equals(f.Get(v), f.Get(baseVersion)))
                {
                    v = f.Set(v, f.Get(r));
                    carried.Add($"{f.Name} → {v.From:yyyy-MM-dd}");
                }
                else
                {
                    live.Remove(f);
                    kept.Add($"{v.From:yyyy-MM-dd} keeps its own {f.Name}");
                }
            }

            v.Validate($" (version from {v.From:yyyy-MM-dd})");
            later.Add(v);
        }

        var all = list.Where(v => v.From < from).Append(r).Concat(later).OrderBy(v => v.From).ToList();
        return new RateSaveOutcome(all, r, changed.Select(f => f.Name).ToList(), carried, kept, later);
    }
}
