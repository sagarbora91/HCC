using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Stays;

/// <summary>Reservation status spellings in <c>inv.reservation.status</c> (0002 CHECK constraint).</summary>
public static class ReservationStatus
{
    public const string Held = "Held";
    public const string Confirmed = "Confirmed";
    public const string InHouse = "InHouse";
    public const string Departed = "Departed";
}

/// <summary>Stay status in <c>gst.stay.status</c>.</summary>
public static class StayStatus
{
    public const string InHouse = "InHouse";
    public const string CheckedOut = "CheckedOut";
}

/// <summary>What check-in needs to know about the reservation and its room (loaded under lock).</summary>
public sealed record CheckInFacts(
    string ReservationStatus,
    DateOnly Arrival,
    DateOnly Departure,
    DateOnly BusinessDate,
    bool Registered,
    string? RoomHk,
    bool RoomBlockedDuringStay,
    bool RoomOccupied,
    bool AllocationIntact);

/// <summary>Check-in / checkout preconditions ported from the prototype handlers (CHECKIN in handlers/guests.ts, CHECKOUT in handlers/folio.ts).</summary>
public static class StayRules
{
    /// <summary>Throws the prototype's typed error and message for the first failed precondition (WF-STAY-001).</summary>
    public static void AssertCanCheckIn(CheckInFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (f.ReservationStatus != ReservationStatus.Confirmed)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a confirmed reservation can check in.");
        if (!(f.Arrival <= f.BusinessDate && f.Departure > f.BusinessDate))
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Check-in must fall within the booked dates.");
        if (!f.Registered) throw HccException.Validation("Verify the guest registration first.");
        if (f.RoomHk is null) throw new HccException(HccErrorCode.NOT_AVAILABLE, "The reservation has no room allocated.");
        if (f.RoomBlockedDuringStay || f.RoomHk != "Ready" || f.RoomOccupied)
            throw new HccException(HccErrorCode.NOT_AVAILABLE, "The room must be vacant, ready and unblocked.");
        if (!f.AllocationIntact)
            throw new HccException(HccErrorCode.NOT_AVAILABLE, "The room allocation conflicts with another booking.");
    }

    /// <summary>Checkout requires an in-house stay; a second checkout is INVALID_TRANSITION (WF-CHECKOUT-001 step 4).</summary>
    public static void AssertCanCheckOut(string stayStatus)
    {
        if (stayStatus != StayStatus.InHouse) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay is not in house.");
    }
}
