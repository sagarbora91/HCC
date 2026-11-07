// F12 MEAS · f12-int-cap: ports of the measurement capture module (MEAS-CONTRACT §2 "Server mirror", schema meas, migrations 4810-4830).
// The service lives in Hcc.Operations/Meas/MeasCaptureService.cs because the pure rules (DeskRules, GuestRules, HkRules, PeopleRules) live there
// and Hcc.Application does not reference Hcc.Operations. One SQL adapter (Hcc.Infrastructure.SqlServer/Meas/SqlMeasCaptureStore.cs) implements
// every port; the ports are split per lane so tests fake only what they use.
using Hcc.Application.Commands;
using Hcc.Contracts.Meas;

namespace Hcc.Application.Meas;

/// <summary>Who the caller is and which business day it is (simulated clock: inv.property.business_date).</summary>
public interface IMeasCaptureCommon
{
    Task<IReadOnlyList<string>> RoleCodesAsync(int propertyId, int principalId, DateTime utcNow, CancellationToken ct = default);
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);
}

public sealed record MeasTurnawayRow(string Reason, DateOnly Arrival, int Nights, string RoomType, int Rooms, long? QuotedRatePaise, string Channel, int LeadDays, string? Note, DateOnly BusinessDate);
public sealed record MeasPreferenceRow(string? Floor, string? RoomKind, string? Meal, string? Pillow, string? BillToCompany, string? Note, bool DoNotDisturb, string? BlacklistReason, int? BlacklistApprovedBy, DateTime? BlacklistUtc, string? LoyaltyTag);

public interface IMeasDeskStore
{
    /// <summary>Insert unless the client id exists; returns the row id and whether it already existed.</summary>
    Task<(long Id, bool Existed)> InsertTurnawayAsync(ICommandTransaction tx, string clientId, MeasTurnawayRow row, CancellationToken ct = default);
    Task<bool> ReservationExistsAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);
    /// <summary>Room type code of the reservation's assigned room (or its booked type), null when none.</summary>
    Task<string?> ReservationRoomTypeAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);
    Task<bool> RoomTypeExistsAsync(ICommandTransaction tx, string code, CancellationToken ct = default);
    Task<(long Id, bool Existed)> InsertWalkedAsync(ICommandTransaction tx, string clientId, long reservationId, DateOnly night, string reason, long? costPaise, int approvedBy, CancellationToken ct = default);
    Task<MeasAllowanceReasonView?> AllowanceReasonAsync(ICommandTransaction tx, string code, CancellationToken ct = default);
    Task UpsertAllowanceReasonAsync(ICommandTransaction tx, string code, string label, bool active, CancellationToken ct = default);
    Task<MeasBookingExtraView?> BookingExtraAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);
    Task UpsertBookingExtraAsync(ICommandTransaction tx, long reservationId, string? bookedType, string? upgrade, int? approver, CancellationToken ct = default);
    /// <summary>Append-only log row; false when the client id was already logged.</summary>
    Task<bool> InsertBookingExtraLogAsync(ICommandTransaction tx, string clientId, long reservationId, string? item, bool waived, string? waiveReason, CancellationToken ct = default);
    Task<MeasPreferenceRow?> PreferenceAsync(ICommandTransaction tx, string customerKey, CancellationToken ct = default);
    Task UpsertPreferenceAsync(ICommandTransaction tx, string customerKey, MeasPreferenceRow row, CancellationToken ct = default);
    Task<bool> InsertPreferenceLogAsync(ICommandTransaction tx, string clientId, string customerKey, string action, string? detail, int? approvedBy, CancellationToken ct = default);
    /// <summary>Active role codes of another user of the property (an approver); empty when unknown or disabled.</summary>
    Task<IReadOnlyList<string>> ApproverRolesAsync(ICommandTransaction tx, int userId, DateTime utcNow, CancellationToken ct = default);
}

public sealed record MeasSurveyRow(string ClientId, DateTime AtUtc, string Via, long? ReservationId, string? RoomType, string? Channel, int Overall, int Cleanliness, int Staff, int Value,
    int? Breakfast, int Recommend, string? Comment, int? EnteredBy);

public interface IMeasGuestStore
{
    Task<bool> SurveyExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> QrSurveyTimesAsync(ICommandTransaction tx, DateTime sinceUtc, CancellationToken ct = default);
    Task<(bool Found, string? RoomType, string? Channel)> SurveyBookingAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);
    Task InsertSurveyAsync(ICommandTransaction tx, MeasSurveyRow row, CancellationToken ct = default);
    /// <summary>Upsert by (month, platform); true when a row was replaced.</summary>
    Task<bool> UpsertReviewAsync(ICommandTransaction tx, MeasReviewView row, int by, CancellationToken ct = default);
    Task<IReadOnlyList<MeasRootCauseView>> RootCausesAsync(ICommandTransaction tx, CancellationToken ct = default);
    Task<IReadOnlyList<string>> UsedRootCausesAsync(ICommandTransaction tx, CancellationToken ct = default);
    Task ReplaceRootCausesAsync(ICommandTransaction tx, IReadOnlyList<MeasRootCauseView> rows, CancellationToken ct = default);
    Task<bool> ComplaintExistsAsync(ICommandTransaction tx, long complaintId, CancellationToken ct = default);
    Task<string?> RootCauseOfAsync(ICommandTransaction tx, string complaintId, CancellationToken ct = default);
    Task SetRootCauseAsync(ICommandTransaction tx, string complaintId, string? code, int by, CancellationToken ct = default);
    Task SetLoyaltyTagAsync(ICommandTransaction tx, string customerKey, string? tag, CancellationToken ct = default);
}

public sealed record MeasPulseRow(string QuarterKey, string DeptCode, int Workload, int Respect, int Tools, int PayFairness, int Recommend);

public interface IMeasPeopleStore
{
    /// <summary>Joining date of the staff member, null when not found.</summary>
    Task<DateOnly?> StaffJoinedAsync(ICommandTransaction tx, long staffId, CancellationToken ct = default);
    Task<long?> TrainingStaffAsync(ICommandTransaction tx, long trainingId, CancellationToken ct = default);
    Task<(long Id, bool Existed)> InsertTrainingAsync(ICommandTransaction tx, Guid clientId, MeasTrainingView row, CancellationToken ct = default);
    Task UpdateTrainingAsync(ICommandTransaction tx, long trainingId, MeasTrainingView row, CancellationToken ct = default);
    Task<bool> SetExitAsync(ICommandTransaction tx, long staffId, string? reason, DateOnly? exitDate, CancellationToken ct = default);
    Task<bool> DepartmentExistsAsync(int propertyId, string deptCode, CancellationToken ct = default);
    /// <summary>
    /// Anonymous pulse: own transaction (no command runner, so no idempotency row keyed by the principal); the audit row carries principal 0 and
    /// no name. Returns the row id and whether the client id already existed.
    /// </summary>
    Task<(long Id, bool Existed)> AddPulseAsync(int propertyId, Guid clientId, MeasPulseRow row, DateTime utc, CancellationToken ct = default);
}

public interface IMeasHkStore
{
    Task<int?> RoomIdAsync(ICommandTransaction tx, string roomNo, CancellationToken ct = default);
    Task<bool> WorkOrderExistsAsync(ICommandTransaction tx, long workOrderId, CancellationToken ct = default);
    Task<bool> OfflineExistsAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default);
    Task SetOfflineAsync(ICommandTransaction tx, int roomId, string clientId, string reason, DateOnly? expectedReturn, string? workOrder, string? note, CancellationToken ct = default);
    Task ClearOfflineAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default);
    Task<IReadOnlyList<MeasMeterView>> MetersAsync(ICommandTransaction tx, CancellationToken ct = default);
    Task<bool> MeterHasReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default);
    Task UpsertMeterAsync(ICommandTransaction tx, MeasMeterView meter, string clientId, CancellationToken ct = default);
    Task<IReadOnlyList<(DateOnly Date, decimal Value)>> ReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default);
    Task<(long Id, bool Existed)> InsertReadingAsync(ICommandTransaction tx, string clientId, string meterId, DateOnly date, decimal value, string? photoRef, CancellationToken ct = default);
    Task<bool> AssetExistsAsync(ICommandTransaction tx, int assetId, CancellationToken ct = default);
    Task UpsertAssetLifeAsync(ICommandTransaction tx, string assetId, int? lifeMonths, string? grade, long? costPaise, string clientId, CancellationToken ct = default);
    Task<(long Id, bool Existed)> InsertDiscardAsync(ICommandTransaction tx, string clientId, string item, int qty, string reason, DateOnly date, CancellationToken ct = default);
}

public interface IMeasRevenueStore
{
    /// <summary>The event (including a deleted one, flagged).</summary>
    Task<(MeasEventView Event, bool Deleted)?> EventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default);
    Task UpsertEventAsync(ICommandTransaction tx, MeasEventView e, CancellationToken ct = default);
    Task DeleteEventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default);
    /// <summary>Replaces the snapshot of <paramref name="takenOn"/> with rooms / revenue on the books for <paramref name="nights"/> nights; returns the row count.</summary>
    Task<int> SnapshotOtbAsync(ICommandTransaction tx, DateOnly takenOn, int nights, CancellationToken ct = default);
    /// <summary>Keeps the newest <paramref name="keep"/> snapshot days.</summary>
    Task PruneOtbAsync(ICommandTransaction tx, int keep, CancellationToken ct = default);
    Task<IReadOnlyList<MeasCompetitorView>> CompetitorsAsync(ICommandTransaction tx, CancellationToken ct = default);
    Task UpsertCompetitorAsync(ICommandTransaction tx, MeasCompetitorView c, CancellationToken ct = default);
    Task<bool> RateShopExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default);
    Task<int?> RoomTypeIdAsync(ICommandTransaction tx, string code, CancellationToken ct = default);
    Task InsertRateShopAsync(ICommandTransaction tx, string clientId, DateOnly shopDate, DateOnly stayDate, string competitorId, int roomTypeId, long ratePaise, bool withBreakfast, string source, CancellationToken ct = default);
}

/// <summary>The unmasked read of every capture table (the service masks per caller).</summary>
public interface IMeasCaptureReads
{
    Task<MeasCapturesView> CapturesAsync(int propertyId, CancellationToken ct = default);
}
