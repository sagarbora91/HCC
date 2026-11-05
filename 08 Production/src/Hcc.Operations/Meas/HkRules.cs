using System.Globalization;

namespace Hcc.Operations.Meas;

/// <summary>Result of a measurement-capture rule: ok, or a platform error code (<c>VALIDATION</c>, <c>MEAS_READING_NOT_MONOTONIC</c>) with a message.</summary>
public readonly record struct HkRuleResult(bool Ok, string? Code, string? Message)
{
    public static readonly HkRuleResult Pass = new(true, null, null);
    public static HkRuleResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>A stored meter reading as the rules see it (date and value only).</summary>
public readonly record struct HkReading(DateOnly Date, decimal Value);

/// <summary>
/// F12 MEAS meas-cap-hk: the housekeeping / engineering capture rules, mirrored from the prototype (streams/maintenance.ts measReadingError,
/// streams/housekeeping.ts, handlers/meas-hk.ts). Pure and database-free; the tables are in migration 4815 (schema <c>meas</c>) and the same
/// rules back the HTTP endpoints once meas-settings' <c>IMeasurementGate</c> is merged. Money is integer paise (DEC-061: Housekeeping and
/// Maintenance never read the replacement cost).
/// </summary>
public static class HkRules
{
    public const string ErrValidation = "VALIDATION";
    public const string ErrNotMonotonic = "MEAS_READING_NOT_MONOTONIC";

    public static readonly IReadOnlyList<string> OfflineReasons = ["OOO", "OOS", "house-use", "renovation"];
    public static readonly IReadOnlyList<string> MeterKinds = ["electricity", "water", "lpg", "diesel", "dg-hours"];
    public static readonly IReadOnlyList<string> ConditionGrades = ["A", "B", "C", "D"];

    /// <summary>Measurement keys (contract §1.2) that gate each capture; <c>MeasurementCatalogue</c> is the authority.</summary>
    public const string KeyOoo = "meas.hk.ooo", KeyUtilities = "meas.hk.utilities", KeyAssets = "meas.hk.assets", KeyLinen = "meas.hk.linen", KeyProductivity = "meas.hk.productivity";

    /// <summary>
    /// A new reading must be a non-negative number with at most two decimals, not dated after today, not below the latest reading on or before its
    /// date and not above the earliest reading after it. An equal value is allowed (no consumption that day). <paramref name="existing"/> is any order.
    /// </summary>
    public static HkRuleResult CheckReading(IEnumerable<HkReading> existing, DateOnly date, decimal value, DateOnly today, bool meterActive = true)
    {
        if (!meterActive) return HkRuleResult.Fail(ErrValidation, "This meter is switched off.");
        if (date > today) return HkRuleResult.Fail(ErrValidation, "A reading cannot be dated in the future.");
        if (value < 0 || decimal.Round(value, 2) != value) return HkRuleResult.Fail(ErrValidation, "Enter the meter reading (zero or more, at most two decimals).");
        var list = existing.OrderBy(r => r.Date).ToList();
        HkReading? before = null, after = null;
        foreach (var r in list)
        {
            if (r.Date <= date) before = r;
            else if (after is null) after = r;
        }
        if (before is { } p && value < p.Value)
            return HkRuleResult.Fail(ErrNotMonotonic, string.Create(CultureInfo.InvariantCulture, $"The reading is below the last one ({p.Value} on {p.Date:yyyy-MM-dd}). Meter readings only go up."));
        if (after is { } n && value > n.Value)
            return HkRuleResult.Fail(ErrNotMonotonic, string.Create(CultureInfo.InvariantCulture, $"The reading is above a later one ({n.Value} on {n.Date:yyyy-MM-dd}). Meter readings only go up."));
        return HkRuleResult.Pass;
    }

    /// <summary>Consumption between consecutive readings (dated like the later one), oldest first.</summary>
    public static IReadOnlyList<(DateOnly Date, decimal Used)> Consumption(IEnumerable<HkReading> readings)
    {
        var l = readings.OrderBy(r => r.Date).ToList();
        return [.. l.Skip(1).Select((r, i) => (r.Date, r.Value - l[i].Value))];
    }

    /// <summary>Room offline: reason from the list, an expected return that is not in the past, a short note.</summary>
    public static HkRuleResult CheckOffline(string reason, DateOnly? expectedReturn, string? note, DateOnly today)
    {
        if (!OfflineReasons.Contains(reason)) return HkRuleResult.Fail(ErrValidation, "Choose OOO, OOS, house use or renovation.");
        if (expectedReturn is { } d && d < today) return HkRuleResult.Fail(ErrValidation, "Enter an expected return date that is not in the past.");
        if (note is { Length: > 200 }) return HkRuleResult.Fail(ErrValidation, "Keep the note under 200 characters.");
        return HkRuleResult.Pass;
    }

    /// <summary>Asset life: at least one field; life 1 to 600 months; grade A to D; replacement cost in whole paise, zero or more.</summary>
    public static HkRuleResult CheckAssetLife(int? lifeMonths, string? grade, long? replacementCostPaise)
    {
        if (lifeMonths is null && grade is null && replacementCostPaise is null) return HkRuleResult.Fail(ErrValidation, "Enter at least one of expected life, condition grade or replacement cost.");
        if (lifeMonths is { } m && (m < 1 || m > 600)) return HkRuleResult.Fail(ErrValidation, "Expected life is 1 to 600 months.");
        if (grade is not null && !ConditionGrades.Contains(grade)) return HkRuleResult.Fail(ErrValidation, "Condition grade is A, B, C or D.");
        if (replacementCostPaise is < 0) return HkRuleResult.Fail(ErrValidation, "Enter the replacement cost in whole paise.");
        return HkRuleResult.Pass;
    }

    /// <summary>Linen discard: item 2 to 60 characters, quantity 1 to 1000, reason 3 to 60 characters, date not in the future.</summary>
    public static HkRuleResult CheckDiscard(string item, int qty, string reason, DateOnly date, DateOnly today)
    {
        if (item.Trim().Length is < 2 or > 60) return HkRuleResult.Fail(ErrValidation, "Enter the item (2 to 60 characters).");
        if (qty is < 1 or > 1000) return HkRuleResult.Fail(ErrValidation, "Enter a quantity from 1 to 1000.");
        if (reason.Trim().Length is < 3 or > 60) return HkRuleResult.Fail(ErrValidation, "Choose or enter a reason.");
        if (date > today) return HkRuleResult.Fail(ErrValidation, "Enter a valid date that is not in the future.");
        return HkRuleResult.Pass;
    }

    /// <summary>Roles that may write each capture (prototype handler twins). Owner is in every list.</summary>
    public static bool CanSetOffline(string role) => role is "Owner" or "Manager" or "Supervisor";
    public static bool CanSaveMeter(string role) => role is "Owner" or "Manager";
    public static bool CanAddReading(string role) => role is "Owner" or "Manager" or "Maintenance"; // PERM-MEAS-002
    public static bool CanSetAssetLife(string role) => role is "Owner" or "Accountant";
    public static bool CanAddDiscard(string role) => role is "Owner" or "Manager" or "Supervisor";
    /// <summary>DEC-061: the replacement cost is hotel money; Housekeeping and Maintenance (and Supervisor) never read it.</summary>
    public static bool CanSeeAssetCost(string role) => role is "Owner" or "Manager" or "Accountant";
}
