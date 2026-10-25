using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;

namespace Hcc.Application.Inventory;

public sealed record PropertyInfo(int PropertyId, DateOnly BusinessDate, string TimeZone);

/// <summary>
/// One room's hold on inventory: the room nights [Arrival, Departure) of one room type, optionally picked up from a group
/// block (explicit block context, contracts §4 rule 2).
/// </summary>
public sealed record StayAllocation(int RoomId, int RoomTypeId, DateOnly Arrival, DateOnly Departure, long? GroupBlockId = null);

/// <summary>
/// Exclusive inventory writer (contracts §4 rules 1–2). Changes one reservation's allocation inside the caller's command
/// transaction: locks the group-block rows, applies the room-type night counts (<c>sold + held ≤ sellable</c>) in ascending
/// order, then deletes/inserts <c>inv.room_night</c> rows in ascending night order. Throws NOT_AVAILABLE when a night is
/// full or a room night is taken; the whole transaction is then rolled back (original allocation untouched).
/// Also the port P3 uses for stay moves/extensions and early departures (pass the shortened or moved allocation).
/// </summary>
public interface IInventoryAllocator
{
    /// <param name="from">Current allocation (null for a new reservation).</param>
    /// <param name="to">New allocation (null to release everything).</param>
    /// <param name="returnToBlock">When releasing a group pickup from an open block, give the room back to the block's allotment.</param>
    Task ChangeAsync(ICommandTransaction tx, int propertyId, long reservationId, StayAllocation? from, StayAllocation? to,
        bool returnToBlock = true, CancellationToken ct = default);
}

/// <summary>
/// Out-of-service blocks on rooms (inventory side of P3 maintenance blocks). Placing a block reduces the room type's
/// sellable count per night and fails NOT_AVAILABLE while the room has live room nights in the window.
/// </summary>
public interface IRoomBlockPort
{
    Task<long> PlaceAsync(ICommandTransaction tx, int propertyId, int roomId, DateOnly? from, DateOnly? to, string reason, string source,
        long? sourceRef, CancellationToken ct = default);

    Task RemoveAsync(ICommandTransaction tx, int propertyId, long roomBlockId, CancellationToken ct = default);
}

/// <summary>Tax rate (basis points) effective on a service date. STUB in Wave 1 — the tax schedule belongs to P4/admin configuration.</summary>
public interface ITaxRateSource
{
    Task<int> TaxBpsOnAsync(int propertyId, DateOnly date, CancellationToken ct = default);
}

/// <summary>Wave 1 stand-in: one synthetic tax rate (prototype settings.taxBps = 1000, 10 %, no GST claim) until P4 provides the schedule.</summary>
public sealed class ConfiguredTaxRateSource(int taxBps = 1000) : ITaxRateSource
{
    public Task<int> TaxBpsOnAsync(int propertyId, DateOnly date, CancellationToken ct = default) => Task.FromResult(taxBps);
}

public sealed record QuoteDraft(int PropertyId, int PrincipalId, int RoomTypeId, int? RoomId, DateOnly Arrival, DateOnly Departure,
    int Adults, int Children, string RatePlan, IReadOnlyList<PricedNight> Nights, DateTime CreatedUtc, DateTime ExpiresUtc);

public sealed record QuoteRecord(long QuoteId, int RoomTypeId, int? RoomId, DateOnly Arrival, DateOnly Departure, int Adults, int Children,
    string RatePlan, IReadOnlyList<PricedNight> Nights, long NetPaise, long TaxPaise, long TotalPaise, DateTime ExpiresUtc,
    long? UsedByReservationId, byte[] RowVersion);

public sealed record GroupBlockDraft(int PropertyId, string Code, string Name, DateOnly Arrival, DateOnly Departure, DateOnly Cutoff,
    IReadOnlyList<GroupBlockAllotment> Allotments);

/// <summary>Reads and writes of the inventory module (SQL adapter in Hcc.Infrastructure.SqlServer/Inventory).</summary>
public interface IInventoryStore
{
    Task<PropertyInfo> GetPropertyAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default);

    /// <summary>
    /// Rooms, types, blocks, live stays, open group blocks and restrictions for [from, to), widened to every open group block's
    /// window so group holds can be assigned exactly as the prototype does. <paramref name="tx"/> = read inside a command.
    /// </summary>
    Task<InventorySnapshot> LoadSnapshotAsync(int propertyId, DateOnly from, DateOnly to, DateTime nowUtc, ICommandTransaction? tx = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<(DateOnly EffectiveFrom, long RatePaise)>> GetTariffsAsync(int roomTypeId, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<QuoteRecord> InsertQuoteAsync(ICommandTransaction tx, QuoteDraft draft, CancellationToken ct = default);

    Task<QuoteRecord?> GetQuoteForUpdateAsync(ICommandTransaction tx, int propertyId, long quoteId, CancellationToken ct = default);

    Task MarkQuoteUsedAsync(ICommandTransaction tx, long quoteId, long reservationId, CancellationToken ct = default);

    /// <summary>
    /// Persists time-based state in its own short transaction before a command: lapsed holds become Expired (room nights and
    /// counts released) and Open blocks past their cut-off are released. Returns the number of records changed.
    /// </summary>
    Task<int> SweepAsync(int propertyId, int principalId, DateTime nowUtc, CancellationToken ct = default);

    Task<GroupBlockResult> CreateGroupBlockAsync(ICommandTransaction tx, GroupBlockDraft draft, CancellationToken ct = default);

    Task<GroupBlockResult> ReleaseGroupBlockAsync(ICommandTransaction tx, int propertyId, long groupBlockId, byte[] expectedVersion,
        CancellationToken ct = default);
}

/// <summary>Helpers shared by the P2 services.</summary>
public static class InventoryErrors
{
    public static IReadOnlyDictionary<string, JsonElement> Details(string key, object value)
        => new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value, CanonicalJson.SerializerOptions) };

    public static HccException NotFound(string what) => new(HccErrorCode.NOT_FOUND, $"{what} not found.");

    public static HccException NotAvailable(string message, IReadOnlyList<string>? reasons = null)
        => new(HccErrorCode.NOT_AVAILABLE, message, reasons is null ? null : Details("reasons", reasons));

    public static HccException Restricted(IReadOnlyList<StayViolation> violations)
        => new(HccErrorCode.RESTRICTED,
            string.Join(" ", violations.Select(v => v.Message)) + " Choose other dates, or ask an approver to override with a reason.",
            Details("violations", violations));

    /// <summary>Byte comparison of a stored rowversion with the wire token.</summary>
    public static bool VersionEquals(byte[] stored, byte[] expected) => stored.AsSpan().SequenceEqual(expected);

    /// <summary>Wire <c>expectedVersion</c> (base64 of the 8-byte rowversion) → bytes; malformed → VALIDATION.</summary>
    public static byte[] ParseVersion(string? wire, string field = "expectedVersion")
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(wire ?? string.Empty);
        }
        catch (FormatException)
        {
            throw HccException.Validation($"{field} is not a valid version token.");
        }

        if (bytes.Length != 8) throw HccException.Validation($"{field} is not a valid version token.");
        return bytes;
    }

    public static string VersionToWire(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    public static HccException Stale(string what) => new(HccErrorCode.STALE_VERSION, $"{what} changed after you opened it. Reopen it and try again.");
}
