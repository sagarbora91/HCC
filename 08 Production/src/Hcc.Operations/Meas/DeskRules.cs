using System.Globalization;

namespace Hcc.Operations.Meas;

/// <summary>
/// F12 MEAS cap-desk rules, the server twin of <c>03 Prototype/src/domain/streams/turnaway.ts</c> and <c>services/mock/handlers/meas-desk.ts</c>
/// (MEAS-CONTRACT §2). Pure and stateless: the command service, SQL store and endpoints call these before writing <c>meas.*</c> rows.
/// Money is integer paise; GST on the folio items comes from the dated GST master when the existing add-line command posts them.
/// </summary>
public static class DeskRules
{
    /// <summary>Error code carried in the reply when a switched-off capture is refused (the data is kept).</summary>
    public const string MeasOff = "MEAS_OFF";

    // Switch keys (MEAS-CONTRACT §1.2) of the five desk captures.
    public const string KeyTurnaway = "meas.daily.regrets";
    public const string KeyWalked = "meas.rev.overbooking";
    public const string KeyAllowance = "meas.daily.adjustments";
    public const string KeyExtra = "meas.guest.upsell";
    public const string KeyPreference = "meas.guest.preferences";

    /// <summary>Folio items posted through the existing add-line command (owner defaults M-1): amounts are pre-GST paise.</summary>
    public const string ItemUpgrade = "UPG";
    public const string ItemLate1 = "LCO1";
    public const string ItemLate2 = "LCO2";
    public const long Late1Paise = 30000;
    public const long Late2Paise = 60000;
    public static readonly TimeOnly Late1From = new(14, 0);
    public static readonly TimeOnly Late2From = new(18, 0);

    public static readonly IReadOnlyList<string> TurnawayReasons = ["full", "price", "dates", "room-type", "policy", "other"];

    // Role codes (plat.role): OWN, MGR, REC, ...
    public static bool MayLogTurnaway(IReadOnlyCollection<string> roles) => roles.Any(r => r is "OWN" or "MGR" or "REC");
    public static bool MayLogWalked(IReadOnlyCollection<string> roles) => roles.Any(r => r is "OWN" or "MGR");
    public static bool MaySaveAllowance(IReadOnlyCollection<string> roles) => roles.Contains("OWN");
    public static bool MaySetExtra(IReadOnlyCollection<string> roles) => roles.Any(r => r is "OWN" or "MGR" or "REC");
    public static bool MaySetPreference(IReadOnlyCollection<string> roles) => roles.Any(r => r is "OWN" or "MGR" or "REC");
    public static bool MayBlacklist(IReadOnlyCollection<string> roles) => roles.Any(r => r is "OWN" or "MGR");

    /// <summary>Late check-out item for a local time: LCO2 from 18:00, LCO1 from 14:00, otherwise none.</summary>
    public static string? LateCheckoutItem(TimeOnly at) => at >= Late2From ? ItemLate2 : at >= Late1From ? ItemLate1 : null;

    /// <summary>The fixed price of a late check-out item in paise (null for any other item).</summary>
    public static long? LateCheckoutPaise(string item) => item switch { ItemLate1 => Late1Paise, ItemLate2 => Late2Paise, _ => null };

    /// <summary>Lead time of a turn-away in whole days (arrival minus the business date, never negative).</summary>
    public static int LeadDays(DateOnly businessDate, DateOnly arrival) => Math.Max(0, arrival.DayNumber - businessDate.DayNumber);

    public sealed record TurnawayInput(string Reason, DateOnly Arrival, int Nights, string RoomType, int Rooms, long? QuotedRatePaise, string Channel, string? Note);

    /// <summary>Validates a turn-away; returns the first problem or null.</summary>
    public static string? ValidateTurnaway(TurnawayInput t)
    {
        ArgumentNullException.ThrowIfNull(t);
        if (!TurnawayReasons.Contains(t.Reason)) return "Choose why the guest could not book.";
        if (t.Nights is < 1 or > 30) return "Nights must be 1–30.";
        if (string.IsNullOrWhiteSpace(t.RoomType)) return "Choose the room type asked for.";
        if (t.Rooms is < 1 or > 20) return "Rooms must be 1–20.";
        if (t.QuotedRatePaise is < 0 or > 100_000_000) return "Quoted rate must be an amount in paise.";
        if (string.IsNullOrWhiteSpace(t.Channel)) return "Choose the channel.";
        if (t.Reason == "other" && string.IsNullOrWhiteSpace(t.Note)) return "Write a short note for \"Other\".";
        return null;
    }

    /// <summary>Walked guest: a reason of at least 3 letters and a cost, if any, in range.</summary>
    public static string? ValidateWalked(string? reason, long? costPaise)
    {
        if ((reason ?? string.Empty).Trim().Length < 3) return "Write why the guest was walked.";
        if (costPaise is < 0 or > 100_000_000) return "Cost must be an amount in paise.";
        return null;
    }

    /// <summary>Normalises an allowance-reason code (upper case, letters / digits / dash) or returns null when it is not 2–24 characters.</summary>
    public static string? NormaliseCode(string? code)
    {
        var c = new string((code ?? string.Empty).Trim().ToUpperInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray());
        return c.Length is >= 2 and <= 24 ? c : null;
    }

    /// <summary>Waiving a late check-out charge needs a Manager (or the Owner) and a reason of at least 3 letters.</summary>
    public static string? ValidateWaiver(IReadOnlyCollection<string> roles, string? reason)
    {
        if (!MayBlacklist(roles)) return "Waiving a late check-out charge needs a Manager.";
        return (reason ?? string.Empty).Trim().Length < 3 ? "Write the reason for the waiver." : null;
    }

    /// <summary>A free upgrade is approved by the actor when a Manager / Owner, otherwise by another enabled Manager / Owner.</summary>
    public static bool FreeUpgradeApproved(IReadOnlyCollection<string> actorRoles, int actor, int? approver, IReadOnlyCollection<string>? approverRoles) =>
        MayBlacklist(actorRoles) || (approver is { } a && a != actor && approverRoles is not null && MayBlacklist(approverRoles));

    /// <summary>Blacklist: a reason of at least 3 letters; the Owner approves directly, a Manager needs another user who is the Owner.</summary>
    public static string? ValidateBlacklist(IReadOnlyCollection<string> actorRoles, int actor, string? reason, int? approver, IReadOnlyCollection<string>? approverRoles)
    {
        if (!MayBlacklist(actorRoles)) return "Only a Manager or the Owner can blacklist or clear a guest.";
        if ((reason ?? string.Empty).Trim().Length < 3) return "Write the reason for the blacklist.";
        if (actorRoles.Contains("OWN")) return null;
        return approver is { } a && a != actor && approverRoles is not null && approverRoles.Contains("OWN") ? null : "A Manager needs the Owner’s approval to blacklist a guest.";
    }

    /// <summary>The audit line of a turn-away, same shape as the prototype's detail text.</summary>
    public static string TurnawayAudit(string roomType, int rooms, int nights, DateOnly arrival, string reason, long? quotedPaise) =>
        string.Create(CultureInfo.InvariantCulture, $"{roomType} × {rooms} · {nights} night(s) from {arrival:yyyy-MM-dd} · {reason}") + (quotedPaise is { } q ? $" · quoted {q / 100m:0.##}" : string.Empty);
}
