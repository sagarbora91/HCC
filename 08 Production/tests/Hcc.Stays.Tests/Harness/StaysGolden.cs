using System.Text.Json;

namespace Hcc.Stays.Tests.Harness;

/// <summary>Prototype figures exported by <c>Golden/export-stays-golden.mjs</c> (regenerate after a prototype fixture change).</summary>
public sealed record StaysGolden(string GeneratedBy, bool FictionalDataOnly, GoldenBase Base, GoldenJourney Journey, GoldenCases Cases)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<StaysGolden> Instance = new(() =>
        JsonSerializer.Deserialize<StaysGolden>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "stays-golden.json")), Options)
        ?? throw new InvalidOperationException("Empty golden file."));

    public static StaysGolden Load() => Instance.Value;
}

public sealed record GoldenRoom(string RoomNo, string Type, string Hk, bool Blocked, string? BlockFrom, string? BlockTo, string? BlockReason);

public sealed record GoldenBooking(string Id, string Room, string Status, string Arrival, string Departure, string Guest, bool Registered);

public sealed record GoldenTask(string Id, string Room, string? Booking, string Assignee, string Status, string Kind);

public sealed record GoldenBaseExpected(Dictionary<string, string> RoomStatus, int Occupied, int Available, int Sellable, string CurrentGuest101, List<string> Arrivals);

public sealed record GoldenBase(string BusinessDate, bool InspectionRequired, List<GoldenRoom> Rooms, List<GoldenBooking> Bookings, List<GoldenTask> Tasks, GoldenBaseExpected Expected);

public sealed record GoldenStep(string Step, string RoomHk, string RoomStatus, string? BookingStatus, string? TaskStatus, int TasksForBooking);

public sealed record GoldenLine(string Kind, string Date, long AmountPaise, int TaxBps);

public sealed record GoldenReceipt(string Kind, string Method, long AmountPaise);

public sealed record GoldenFolio(List<GoldenLine> Lines, List<GoldenReceipt> Receipts, long BalanceDuePaise, long BalanceAtCheckoutPaise);

public sealed record GoldenJourneyExpected(string UnpaidCheckout, bool ReplaySame, string SecondCheckoutWithoutKey, string HkStartOther, string CompleteEarly, string HkInspect, string RejectNoReason);

public sealed record GoldenJourney(string Room, string Arrival, string Departure, string TaskAssignee, GoldenFolio Folio, List<GoldenStep> Steps, GoldenJourneyExpected Expected);

public sealed record GoldenCode(string? Room, string Code);

public sealed record GoldenDirty(string Room, string RoomHk, string Code);

public sealed record GoldenUnpaid(string Booking, long BalancePaise, string Code);

public sealed record GoldenKeepsBlock(string Room, string HkAfter, bool BlockedAfter, string StatusAfter, string BlockTo);

public sealed record GoldenCases(GoldenDirty DirtyRoomCheckin, GoldenUnpaid UnpaidDeparture, GoldenKeepsBlock InspectionKeepsBlock,
    GoldenCode BlockOverlappingInHouse, GoldenCode BlockAfterDeparture, GoldenCode BlockInPast, GoldenCode HousekeeperBlock);
