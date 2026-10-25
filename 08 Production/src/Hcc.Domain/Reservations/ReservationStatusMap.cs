using Hcc.Contracts;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Reservations;

/// <summary>
/// Reservation status machine for the P2 commands (A1 H01 map; prototype CONFIRM / CANCEL / CHANGE_DATES guards and messages).
/// Held → Confirmed | Cancelled | Expired; Confirmed → Cancelled | NoShow (and InHouse via P3 check-in). A lapsed hold is
/// treated as Expired even before a sweep persists it.
/// </summary>
public static class ReservationStatusMap
{
    public static bool HoldRunning(string status, DateTime? holdUntilUtc, DateTime nowUtc)
        => status == ReservationStatuses.Held && (holdUntilUtc is null || nowUtc < holdUntilUtc.Value);

    /// <summary>Status as shown to users: a lapsed hold reads Expired (prototype resStatus).</summary>
    public static string Effective(string status, DateTime? holdUntilUtc, DateTime nowUtc)
        => status == ReservationStatuses.Held && !HoldRunning(status, holdUntilUtc, nowUtc) ? ReservationStatuses.Expired : status;

    public static void EnsureConfirmable(string status, DateTime? holdUntilUtc, DateTime nowUtc, string holdUntilLocal)
    {
        if (status is not (ReservationStatuses.Held or ReservationStatuses.Expired))
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a held reservation can be confirmed.");
        if (!HoldRunning(status, holdUntilUtc, nowUtc))
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This hold expired{holdUntilLocal}. Check availability and create a new booking.");
    }

    /// <summary>Cancel: Confirmed or a running hold. No-show: Confirmed only, on/after the arrival date.</summary>
    public static void EnsureCancellable(string status, DateTime? holdUntilUtc, DateTime nowUtc, bool noShow, DateOnly arrival, DateOnly businessDate)
    {
        var held = HoldRunning(status, holdUntilUtc, nowUtc);
        if (status != ReservationStatuses.Confirmed && !held)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a confirmed reservation may be cancelled or marked no-show.");
        if (!noShow) return;
        if (held) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A hold cannot be marked no-show. Cancel the hold instead.");
        if (arrival > businessDate) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A no-show can be recorded only on or after the arrival date.");
    }

    public static void EnsureAmendable(string status, DateTime? holdUntilUtc, DateTime nowUtc)
    {
        if (status != ReservationStatuses.Confirmed && !HoldRunning(status, holdUntilUtc, nowUtc))
            throw new HccException(HccErrorCode.INVALID_TRANSITION,
                "Only a confirmed or held reservation can change dates here. In-house stays use the stay move dialog.");
    }
}
