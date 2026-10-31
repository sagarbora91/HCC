using Hcc.Application.Commands;
using Hcc.Domain.Legacy;

namespace Hcc.Application.Legacy;

/// <summary>PERM id of the legacy importer (migration 4601, contract §10): Owner only.</summary>
public static class LegacyPermissions
{
    public const string Import = "PERM-LGC-001";
}

/// <summary>A <c>gst.guest</c> row as the matcher sees it (never an ID column).</summary>
public sealed record LegacyGuestCandidate(long GuestId, string FullName, string Phone, long? LegacyGuestId);

public sealed record LgcGuestMapRow(long LegacyGuestId, long? GuestId, string CustomerKey, string? PhoneKey, bool PhoneValid, string NameSnapshot, string NameFold,
    bool Deleted, string SourceFy, string? GuestName = null, string? GuestPhone = null, bool IsPrimary = false, DateOnly? Dob = null);

public sealed record LgcStayRow(string Fy, long LegacyBookingId, long LegacyGuestId, string CustomerKey, string GuestNameSnapshot, DateOnly ArrivalDate, DateTime ArrivalUtc,
    DateOnly? DepartureDate, DateTime? DepartureUtc, DateOnly? ExpectedDepartureDate, int Nights, int Adults, int Children, string RoomNo, int RoomCount, string? MealPlan,
    int? GrcNo, string? Purpose, string? SourceTag, string Status, string? PaidFlag, int? LegacyBillNo, string? Notes);

public sealed record LgcSegmentRow(long LegacyBookingId, int Seq, string RoomNo, string? RoomTypeCode, DateOnly? FromDate, DateOnly? ToDate, int? Adults, int? Children,
    long? RatePaise, long? ExtraPaise, string Kind);

public sealed record LgcBillRow(string Fy, int LegacyBillNo, long LegacyTransactionId, long? StayBookingId, long? LegacyGuestId, string? CustomerKey, DateOnly BillDate,
    DateTime BillUtc, string RoomNo, int? Nights, long RoomChargePaise, long ExtraPaise, long FbPaise, long LaundryPaise, long OtherPaise, long DiscountPaise, int RoomGstBps,
    long RoomGstPaise, long FbGstPaise, long LaundryGstPaise, long OtherGstPaise, long AdvancePaise, long NetPaise, long BalancePaise, long RefundPaise, string DataFlag,
    string PaidFlag, string? GstinSnapshot, bool B2b, bool OutOfFy, string? Notes);

public sealed record LgcReceiptRow(string Fy, string LegacySource, long LegacyRowId, long? StayBookingId, int? BillNo, long? LegacyGuestId, string? CustomerKey,
    DateOnly ReceivedDate, DateTime ReceivedUtc, string Kind, string Method, long AmountPaise, string? Reference, string? BankName, string? Notes);

/// <summary>One FY's rows as read from the run folder.</summary>
public sealed record LgcFyData(string Fy, IReadOnlyList<LgcStayRow> Stays, IReadOnlyList<LgcSegmentRow> Segments, IReadOnlyList<LgcBillRow> Bills, IReadOnlyList<LgcReceiptRow> Receipts);

/// <summary>Counts of one FY written by <see cref="ILegacyStore.ReplaceFyAsync"/>.</summary>
public sealed record LgcFyWritten(int Stays, int Segments, int Bills, int Receipts, int UnresolvedStayRefs, int UnresolvedBillRefs);

public sealed record LegacyRunRecord(long RunId, string SourceFolder, string ExtractorVersion, bool DryRun, string Parts, DateTime StartedUtc, DateTime? FinishedUtc,
    string Status, int RunBy, string? ResultJson);

/// <summary>Persistence of the legacy importer (<c>lgc.*</c>, migration 4600) and the few <c>gst.guest</c> / <c>crm</c> / <c>grp</c> reads and writes it needs.</summary>
public interface ILegacyStore
{
    /// <summary>Transaction-owned application lock <c>lgc:import:&lt;property&gt;</c> (one legacy import at a time).</summary>
    Task LockAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>True once any business day of the property is closed (key-range locked inside the transaction).</summary>
    Task<bool> AnyDayClosedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<LegacyGuestCandidate>> GuestsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Sets <c>legacy_guest_id</c> and (when given) <c>dob</c> of a guest; returns false when another guest already holds that legacy id.</summary>
    Task<bool> SetGuestLegacyAsync(ICommandTransaction tx, int propertyId, long guestId, long legacyGuestId, DateOnly? dob, CancellationToken ct = default);

    /// <summary>Adds a <c>crm.customer_tag</c> row unless present; true when added.</summary>
    Task<bool> AddTagAsync(ICommandTransaction tx, int propertyId, string customerKey, string tag, int actor, DateTime utc, CancellationToken ct = default);

    /// <summary>Deletes the property's <c>lgc.guest_map</c> and bulk-inserts <paramref name="rows"/>.</summary>
    Task ReplaceGuestMapAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<LgcGuestMapRow> rows, CancellationToken ct = default);

    Task SavepointAsync(ICommandTransaction tx, string name, CancellationToken ct = default);

    Task RollbackToSavepointAsync(ICommandTransaction tx, string name, CancellationToken ct = default);

    /// <summary>Deletes the FY's <c>lgc.stay / room_segment / bill / receipt</c> rows and inserts <paramref name="data"/>, resolving stay / bill refs.</summary>
    Task<LgcFyWritten> ReplaceFyAsync(ICommandTransaction tx, int propertyId, LgcFyData data, CancellationToken ct = default);

    /// <summary>The §6 figures of the FY computed from the stored rows.</summary>
    Task<LegacyFigures> FiguresAsync(ICommandTransaction tx, int propertyId, string fy, CancellationToken ct = default);

    /// <summary>Company accounts of the property by GSTIN (upper case).</summary>
    Task<IReadOnlyDictionary<string, long>> CompaniesByGstinAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>
    /// Inserts a confirmed <c>doc.id_capture</c> row of kind <c>legacy</c> (the documents store's own insert needs an OCR number; a legacy image may
    /// come without one, LG-R2). The sealed image goes through <c>IDocumentImageStore</c>. Returns the capture id.
    /// </summary>
    Task<long> InsertCaptureAsync(ICommandTransaction tx, int propertyId, long guestId, Guid captureRef, string contentType, int sizeBytes, string ocrDocKind, string? ocrLast4,
        string? ocrSealed, DateOnly capturedDate, DateTime utc, int actor, CancellationToken ct = default);

    Task<long> InsertRunAsync(ICommandTransaction tx, int propertyId, string folder, string extractorVersion, bool dryRun, string parts, DateTime startedUtc, DateTime finishedUtc,
        string status, int runBy, string? resultJson, CancellationToken ct = default);

    Task UpdateRunResultAsync(ICommandTransaction tx, long runId, string resultJson, CancellationToken ct = default);

    Task<IReadOnlyList<LegacyRunRecord>> ListRunsAsync(int propertyId, int limit, CancellationToken ct = default);
}
