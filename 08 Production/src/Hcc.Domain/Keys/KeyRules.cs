using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Keys;

/// <summary>Event types of the key log (coordinator requirement): the counts are derived from these rows, never edited.</summary>
public static class KeyEvents
{
    public const string Issued = "issued";
    public const string Reissued = "reissued";
    public const string Extended = "extended";
    public const string Reactivated = "reactivated";
    public const string Cancelled = "cancelled";
    public const string Read = "read";

    public static readonly IReadOnlyList<string> All = [Issued, Reissued, Extended, Reactivated, Cancelled, Read];

    /// <summary>Events that need a reason (re-issue, extension / reactivation).</summary>
    public static bool NeedsReason(string ev) => ev is Reissued or Extended or Reactivated;
}

public static class KeyReasons
{
    public const string Extension = "extension";
    public const string Lost = "lost";
    public const string Damaged = "damaged";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Extension, Lost, Damaged, Other];
}

public static class KeyKinds
{
    public const string IssueGuest = "issue_guest";
    public const string Extend = "extend";
    public const string Cancel = "cancel";
    public const string Read = "read";
    public const string Manual = "manual";
}

public static class KeyStates
{
    public const string Queued = "queued";
    public const string Picked = "picked";
    public const string WaitingCard = "waiting_card";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Expired = "expired";

    public static bool IsTerminal(string s) => s is Done or Failed or Expired;
}

/// <summary>Result codes of the KeyAgent spec (§3).</summary>
public static class KeyCodes
{
    public const string Ok = "Ok";
    public const string EncoderOffline = "EncoderOffline";
    public const string NoCard = "NoCard";
    public const string ForeignCard = "ForeignCard";
    public const string WriteFailed = "WriteFailed";
    public const string VerifyFailed = "VerifyFailed";
    public const string NoLockRoom = "NoLockRoom";
    public const string InvalidRequest = "InvalidRequest";
    public const string LoginRequired = "LoginRequired";
    public const string UiTimeout = "UiTimeout";
    public const string NotSupported = "NotSupported";
    public const string NotFound = "NotFound";
    public const string Expired = "Expired";

    public static readonly IReadOnlyList<string> All =
        [Ok, EncoderOffline, NoCard, ForeignCard, WriteFailed, VerifyFailed, NoLockRoom, InvalidRequest, LoginRequired, UiTimeout, NotSupported, NotFound];
}

public static class KeyRules
{
    /// <summary>A queued job waits at most two minutes (spec §6.2).</summary>
    public static readonly TimeSpan QueueTtl = TimeSpan.FromMinutes(2);

    /// <summary>An encoder with no heartbeat for 30 seconds is offline (spec §6.1).</summary>
    public static readonly TimeSpan HeartbeatStale = TimeSpan.FromSeconds(30);

    /// <summary>BIS Speed Check-in fixes the departure time at its DepartureDefaultTime (12:00).</summary>
    public static readonly TimeOnly CardDepartureTime = new(12, 0);

    public static readonly TimeOnly DefaultFolioCheckout = new(11, 0);

    /// <summary>The BIS doors (door name = room number): 101-107, 201-214, 301-314 = 35 doors; room 106 is the owner's manual-key room.</summary>
    public static bool BisDoorExists(string roomNo) => BisDoors.Contains(roomNo);

    public static bool DefaultHasLock(string roomNo) => BisDoors.Contains(roomNo) && roomNo != "106";

    private static readonly HashSet<string> BisDoors = new(Enumerable.Range(101, 7).Concat(Enumerable.Range(201, 14)).Concat(Enumerable.Range(301, 14))
        .Select(r => r.ToString(CultureInfo.InvariantCulture)), StringComparer.Ordinal);

    public static string KindFor(string ev) => ev switch
    {
        KeyEvents.Issued or KeyEvents.Reissued or KeyEvents.Reactivated => KeyKinds.IssueGuest,
        KeyEvents.Extended => KeyKinds.Extend,
        KeyEvents.Cancelled => KeyKinds.Cancel,
        KeyEvents.Read => KeyKinds.Read,
        _ => throw HccException.Validation("Unknown key event."),
    };

    /// <summary>Validates event and reason: re-issue, extend and reactivate need one of extension / lost / damaged / other.</summary>
    public static void ValidateEvent(string? ev, string? reason)
    {
        if (ev is null || !KeyEvents.All.Contains(ev)) throw HccException.Validation("Choose a key event: issued, reissued, extended, reactivated, cancelled or read.");
        if (KeyEvents.NeedsReason(ev) && (reason is null || !KeyReasons.All.Contains(reason)))
            throw HccException.Validation("A reason is required: extension, lost, damaged or other.");
        if (reason is not null && !KeyReasons.All.Contains(reason)) throw HccException.Validation("Reason must be extension, lost, damaged or other.");
    }

    /// <summary>The BIS card expires on its last day at 12:00.</summary>
    public static DateTime CardExpiry(DateOnly lastDay) => lastDay.ToDateTime(CardDepartureTime);

    /// <summary>Whole staying days for the UI adapter: ceil(date(validTo) - today), at least 1.</summary>
    public static int StayingDays(DateOnly today, DateOnly validTo) => Math.Max(1, validTo.DayNumber - today.DayNumber);

    /// <summary>
    /// Speed Check-in fixes the departure at 12:00; when the folio's check-out time differs the card may stop working before (earlier card expiry)
    /// or after the guest has to leave. Returns the visible warning text, or null when both agree.
    /// </summary>
    public static string? MismatchWarning(DateOnly departure, TimeOnly folioCheckout)
    {
        if (folioCheckout == CardDepartureTime) return null;
        var side = folioCheckout < CardDepartureTime ? "after" : "before";
        return string.Create(CultureInfo.InvariantCulture,
            $"Card expires {departure:yyyy-MM-dd} 12:00 but the folio check-out is {folioCheckout:HH:mm}: the card will work {(side == "after" ? "past" : "only until")} the folio check-out ({side}).");
    }

    /// <summary>Lifecycle (spec §4): queued → picked → waiting_card → done / failed / expired; a terminal state never moves.</summary>
    public static bool CanMove(string from, string to) => from switch
    {
        KeyStates.Queued => to is KeyStates.Picked or KeyStates.Expired or KeyStates.Failed,
        KeyStates.Picked => to is KeyStates.WaitingCard or KeyStates.Done or KeyStates.Failed,
        KeyStates.WaitingCard => to is KeyStates.Done or KeyStates.Failed,
        _ => false,
    };

    public static string ManualOnly(string code) => code switch
    {
        KeyCodes.NoLockRoom => "This room has no electronic lock: issue the manual key.",
        KeyCodes.EncoderOffline or KeyCodes.LoginRequired => "Key service offline - use BIS Hotel manually, then record \"key issued manually\".",
        KeyCodes.NotSupported => "This encoder cannot do that yet: re-issue a card instead.",
        _ => string.Empty,
    };
}
