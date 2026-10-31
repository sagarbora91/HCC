using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Accounting;

// ACC-2 · lane acc2-tds — the dated TDS-rate table (02 Planning/agents/ACC2-POSTING-CONTRACT.md §5; rulebook §2.9, C9, A9; DEC-146).
// Prototype twin: 03 Prototype/src/domain/tds.ts (TDS_SEED, tdsValueOn, tdsRateBps) — same keys, same seed figures, same rounding.
// Every row "CA to confirm" (rulebook §9 OP-10: the Income-tax Act 2025 s.393 rows are VERIFY items, stored as text, never as logic).
// Owner overrides live in acc.tds_rate (migration 4243) as dated versions; ValueOn = the latest override with effective_from ≤ date, else
// the seed (whose first version also covers earlier dates). Money in paise, rates in basis points.

/// <summary>How a row's threshold is tested: per calendar month (rent), per single bill and per financial year (contractor), per financial year.</summary>
public enum TdsBasis
{
    Month,
    SingleAndYear,
    Year,
}

/// <summary>The rate column (contract §4: the firm profile's <c>tdsDeductorType</c>): individual / HUF or other.</summary>
public enum TdsColumn
{
    Individual,
    Other,
}

/// <summary>A row variant (vendor <c>tds_section</c> <c>&lt;row&gt;:&lt;variant&gt;</c>) with its own rate field.</summary>
public sealed record TdsVariant(string Id, string Label, string Field);

/// <summary>One row of the TDS table: its s.393 text, the old 1961-Act section, how the threshold is tested and the source.</summary>
public sealed record TdsSectionRow(string Id, string Label, string Section, string Old, TdsBasis Basis, string Source, IReadOnlyList<TdsVariant> Variants)
{
    public string Note => TdsRates.CaNote;
}

/// <summary>An Owner override of one key from a date (acc.tds_rate).</summary>
public sealed record TdsRateVersion(string Key, DateOnly EffectiveFrom, long Value);

/// <summary>The TDS-rate table: seed, keys, value in force on a date, rate of a vendor section.</summary>
public static class TdsRates
{
    public const string CaNote = "CA to confirm";
    public const string Illustrative = "Illustrative — CA to confirm";

    /// <summary>Income-tax Act 2025 in force; the first version also covers earlier dates (the same figures applied from 1 Apr 2025).</summary>
    public static readonly DateOnly SeedFrom = new(2026, 4, 1);

    public static readonly IReadOnlyList<TdsSectionRow> Sections =
    [
        new("rent", "Rent of premises", "s.393(1) Sl. 2(ii)", "194-I", TdsBasis.Month,
            "LAW S-13 (Income-tax Act 2025 s.393 table, old 194-I; Finance Act 2025: ₹50,000 per month or part of a month) — VERIFY",
            [new("plant", "Rent of plant, machinery or equipment", "bpsPlant")]),
        new("contractor", "Payments to contractors", "s.393(1)", "194C", TdsBasis.SingleAndYear,
            "LAW S-13 (Income-tax Act 2025 s.393 table, old 194C: ₹30,000 single / ₹1,00,000 a year) — VERIFY", []),
        new("professional", "Professional or technical fees", "s.393(1)", "194J", TdsBasis.Year,
            "LAW S-13 (Income-tax Act 2025 s.393 table, old 194J; Finance Act 2025: ₹50,000 a year) — VERIFY",
            [new("technical", "Fees for technical services", "bpsTechnical")]),
        new("commission", "Commission or brokerage", "s.393(1)", "194H", TdsBasis.Year,
            "LAW S-13 (Income-tax Act 2025 s.393 table, old 194H; Finance Act 2025: ₹20,000 a year) — VERIFY", []),
    ];

    /// <summary>The seed (paise / bps), key order as the prototype's TDS_SEED.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, long>> Seed =
    [
        new("tds.rent.threshold", 5_000_000), new("tds.rent.bpsIndividual", 1000), new("tds.rent.bpsOther", 1000), new("tds.rent.bpsPlant", 200),
        new("tds.contractor.threshold", 3_000_000), new("tds.contractor.thresholdYear", 10_000_000),
        new("tds.contractor.bpsIndividual", 100), new("tds.contractor.bpsOther", 200),
        new("tds.professional.threshold", 5_000_000), new("tds.professional.bpsIndividual", 1000), new("tds.professional.bpsOther", 1000),
        new("tds.professional.bpsTechnical", 200),
        new("tds.commission.threshold", 2_000_000), new("tds.commission.bpsIndividual", 200), new("tds.commission.bpsOther", 200),
    ];

    private static readonly Dictionary<string, long> SeedByKey = Seed.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);

    public static IReadOnlyList<string> Keys { get; } = Seed.Select(k => k.Key).ToList();

    public static bool IsKey(string? key) => key is not null && SeedByKey.ContainsKey(key);

    /// <summary>A rate key (basis points) — otherwise a threshold in paise.</summary>
    public static bool IsRate(string key) => key is not null && key.Split('.')[^1].StartsWith("bps", StringComparison.Ordinal);

    /// <summary>VALIDATION when the key is unknown or the value out of range (rates 0–30 %, thresholds ₹0–₹10 lakh); same ranges as the hub rows.</summary>
    public static void ValidateValue(string? key, long value)
    {
        if (!IsKey(key)) throw HccException.Validation("Unknown TDS setting. Choose one of the TDS table settings.");
        if (IsRate(key!) ? value is < 0 or > 3000 : value is < 0 or > 100_000_000)
            throw HccException.Validation(IsRate(key!) ? "A TDS rate is 0 % to 30 %." : "A TDS threshold is ₹0 to ₹10,00,000.");
    }

    public static TdsSectionRow? Row(string id) => Sections.FirstOrDefault(s => s.Id == id);

    /// <summary>Base row of a vendor's section code ('rent:plant' → 'rent'); null for an unknown code or variant.</summary>
    public static string? BaseSection(string? code)
    {
        var parts = (code ?? "").Split(':');
        var row = Row(parts[0]);
        if (row is null || parts.Length > 2) return null;
        if (parts.Length == 2 && !row.Variants.Any(v => v.Id == parts[1])) return null;
        return row.Id;
    }

    /// <summary>Value in force on <paramref name="date"/>: the latest override with EffectiveFrom ≤ date (a later entry wins a tie), else the seed.</summary>
    public static long ValueOn(string key, DateOnly date, IReadOnlyList<TdsRateVersion>? overrides = null)
    {
        if (!SeedByKey.TryGetValue(key, out var seed)) throw new ArgumentException("Unknown TDS key " + key, nameof(key));
        TdsRateVersion? hit = null;
        foreach (var v in overrides ?? [])
            if (v.Key == key && v.EffectiveFrom <= date && (hit is null || v.EffectiveFrom >= hit.EffectiveFrom)) hit = v;
        return hit?.Value ?? seed;
    }

    /// <summary>Rate (bps) of a vendor section code in a column; a variant takes its own rate. 0 = unknown code.</summary>
    public static int RateBps(string? code, DateOnly date, TdsColumn column, IReadOnlyList<TdsRateVersion>? overrides = null)
    {
        var b = BaseSection(code);
        if (b is null) return 0;
        var parts = code!.Split(':');
        var field = parts.Length == 2 ? Row(b)!.Variants.Single(v => v.Id == parts[1]).Field : column == TdsColumn.Individual ? "bpsIndividual" : "bpsOther";
        return checked((int)ValueOn($"tds.{b}.{field}", date, overrides));
    }

    /// <summary>"10 %" (prototype tdsPct).</summary>
    public static string Pct(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %";

    /// <summary>1 April … 31 March of the financial year containing <paramref name="date"/>, with its label "2026-27".</summary>
    public static (DateOnly From, DateOnly To, string Label) Fy(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return (new DateOnly(start, 4, 1), new DateOnly(start + 1, 3, 31), $"{start}-{((start + 1) % 100).ToString("00", CultureInfo.InvariantCulture)}");
    }
}
