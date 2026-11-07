// F12 MEAS · f12-int-cap: wire contracts of the measurement capture commands (POST /api/v1/commands/<Name>) and the shared read
// GET /api/v1/meas/captures (prototype slice s.measX, 03 Prototype/src/domain/streams/measure-types.ts). Field names follow the prototype sync
// mappers (03 Prototype/src/clients/sync/registry.ts F12 blocks, meas-guest.ts, meas-people.ts, meas-hk.ts). Money = integer paise.
using System.Text.Json.Serialization;

namespace Hcc.Contracts.Meas;

/// <summary>Reply of every capture command: the record id and the audit line.</summary>
public sealed record MeasCaptureResult(string Id, string Message);

// ── desk (cap-desk) ──
public sealed record LogTurnawayRequest(string? Reason, string? Arrival, int? Nights, string? RoomType, int? Rooms, long? QuotedRatePaise, string? Channel, string? Note);
public sealed record LogWalkedGuestRequest(long? ReservationId, string? Night, string? Reason, long? CostPaise);
public sealed record SaveAllowanceReasonRequest(string? Code, string? Label, bool? Active);
public sealed record SetBookingExtraRequest(long? ReservationId, string? BookedType, string? Item, string? Upgrade, long? AmountPaise, string? Time, bool? Waive, string? Reason, string? Approver);
public sealed record SetCustomerPreferenceRequest(string? CustomerKey, string? Floor, string? RoomKind, string? Meal, string? Pillow, string? BillToCompany, bool? DoNotDisturb, string? Note,
    string? BlacklistReason, bool? ClearBlacklist, string? ApprovedBy, string? Reason);

// ── guest (cap-guest) ──
public sealed record MeasSurveyAddRequest(string? ClientId, string? Via, string? Booking, int? Overall, int? Cleanliness, int? Staff, int? Value, int? Breakfast, int? Recommend, string? Comment, bool Consent);
public sealed record MeasReviewSaveRequest(string? Month, string? Platform, int? Count, decimal? AvgScore, decimal? Score12m, int? Responded, string? Themes);
public sealed record MeasRootCauseRowIn(string? Code, string? Label, bool Active);
public sealed record MeasRootCauseListSaveRequest(IReadOnlyList<MeasRootCauseRowIn>? Rows);
public sealed record MeasRootCauseSetRequest(string? Complaint, string? Code);
public sealed record MeasLoyaltySetRequest(string? Customer, string? Tag);

// ── people (cap-people) ──
public sealed record SaveMeasTrainingRequest(string? TrainingId, long? StaffId, string? Course, string? Date, decimal? Hours, string? CertificateNo, string? ValidTo, string? EvidenceRef, bool Mandatory);
public sealed record SetMeasExitRequest(long? StaffId, string? Reason, string? ExitDate);
/// <summary>Anonymous: no user or staff member on purpose (PeopleRules.PulseHasNoIdentity).</summary>
public sealed record AddMeasPulseRequest(string? DepartmentId, int? Workload, int? Respect, int? Tools, int? PayFairness, int? Recommend);

// ── housekeeping / engineering (cap-hk) ──
public sealed record MeasOfflineSetRequest(string? Room, string? Reason, string? ExpectedReturn, string? WorkOrder, string? Note, bool? Clear);
public sealed record MeasMeterSaveRequest(string? Id, string? Name, string? Kind, string? Unit, bool? Active);
public sealed record MeasReadingAddRequest(string? Meter, decimal? Value, string? Date, string? PhotoRef);
public sealed record MeasAssetLifeSetRequest(string? Asset, int? ExpectedLifeMonths, string? ConditionGrade, long? ReplacementCostPaise);
public sealed record MeasDiscardAddRequest(string? Item, int? Qty, string? Reason, string? Date);

// ── revenue (forecast) ──
public sealed record MeasEventSaveRequest(string? Id, string? Name, string? From, string? To, string? Kind, int? ImpactPct, string? Note);
public sealed record MeasEventDeleteRequest(string? Id);
public sealed record MeasOtbSnapshotRequest(string? TakenOn, string? BackfillFrom);
public sealed record MeasCompetitorSaveRequest(string? Id, string? Name, bool? Active);
public sealed record MeasRateShopAddRequest(string? ClientId, string? StayDate, string? Competitor, string? RoomType, long? RatePaise, bool WithBreakfast, string? Source);

// ── read model (GET /api/v1/meas/captures). Masked fields are left out of the JSON (null + WhenWritingNull), never blanked (DEC-061). ──
public sealed record MeasTurnawayView(string Id, string At, string By, string Reason, string Arrival, int Nights, string RoomType, int Rooms,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? QuotedRate, string Channel, int LeadDays,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);
public sealed record MeasWalkedView(string Id, string At, string By, string Booking, string Night, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CostPaise, string ApprovedBy);
public sealed record MeasAllowanceReasonView(string Code, string Label, bool Active);
public sealed record MeasBookingExtraView(string Booking, string? BookedType, string? Upgrade, string? UpgradeApprover);
public sealed record MeasBlacklistView(string Reason, string ApprovedBy, string At);
public sealed record MeasPreferenceView(string Customer, string? Floor, string? RoomKind, string? Meal, string? Pillow, string? BillToCompany, bool DoNotDisturb, string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeasBlacklistView? Blacklist, string? LoyaltyTag);
public sealed record MeasTaskTimesView(string TaskId, string? StartedAt, string? FinishedAt, string? InspectedAt, string? InspectResult, string? RejectReason, string? Attendant);
public sealed record MeasOfflineView(string Room, string Reason, string? ExpectedReturn, string? WorkOrder, string? Note);
public sealed record MeasMeterView(string Id, string Name, string Kind, string Unit, bool Active);
public sealed record MeasReadingView(string Id, string Meter, string Date, decimal Value, string By, string? PhotoRef);
public sealed record MeasAssetLifeView(string Asset, int? ExpectedLifeMonths, string? ConditionGrade,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ReplacementCostPaise);
public sealed record MeasDiscardView(string Id, string Item, int Qty, string Reason, string Date, string By);
public sealed record MeasSurveyView(string Id, string At, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Booking, string? RoomType, string? Channel,
    int Overall, int Cleanliness, int Staff, int Value, int? Breakfast, int Recommend, string? Comment, bool Consent, string Via,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EnteredBy);
public sealed record MeasReviewView(string Id, string Month, string Platform, int Count, decimal AvgScore, decimal? Score12m, int Responded, string? Themes);
public sealed record MeasRootCauseView(string Code, string Label, bool Active);
public sealed record MeasRootCauseSetView(string Complaint, string Code);
public sealed record MeasTrainingView(string Id, string Staff, string Course, string Date, decimal Hours, string? CertificateNo, string? ValidTo, string? EvidenceRef, bool Mandatory);
public sealed record MeasExitView(string Staff, string Reason, string ExitDate);
/// <summary>No identity member on purpose (anonymous pulse).</summary>
public sealed record MeasPulseView(string Id, string Quarter, string Dept, int Workload, int Respect, int Tools, int PayFairness, int Recommend);
public sealed record MeasEventView(string Id, string Name, string From, string To, string Kind, int ImpactPct, string? Note);
public sealed record MeasOtbView(string Id, string TakenOn, string Night, int Rooms, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? RevenuePaise);
public sealed record MeasCompetitorView(string Id, string Name, bool Active);
public sealed record MeasRateShopView(string Id, string Date, string StayDate, string Competitor, string RoomType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? RatePaise, bool WithBreakfast, string Source, string By);
public sealed record MeasRestrictionView(string Night, string Kind, int Refused, int Reshaped);

/// <summary>Everything the prototype slice <c>s.measX</c> holds that has a server table. Lists the caller may not read are empty.</summary>
public sealed record MeasCapturesView(
    IReadOnlyList<MeasTurnawayView> Turnaway, IReadOnlyList<MeasWalkedView> Walked, IReadOnlyList<MeasAllowanceReasonView> AllowanceReasons,
    IReadOnlyList<MeasBookingExtraView> BookingExtra, IReadOnlyList<MeasPreferenceView> Preference,
    IReadOnlyList<MeasTaskTimesView> TaskTimes, IReadOnlyList<MeasOfflineView> Offline, IReadOnlyList<MeasMeterView> Meters, IReadOnlyList<MeasReadingView> Readings,
    IReadOnlyList<MeasAssetLifeView> AssetLife, IReadOnlyList<MeasDiscardView> Discards,
    IReadOnlyList<MeasSurveyView> Surveys, IReadOnlyList<MeasReviewView> Reviews, IReadOnlyList<MeasRootCauseView> RootCauseList, IReadOnlyList<MeasRootCauseSetView> RootCause,
    IReadOnlyList<MeasTrainingView> Training, IReadOnlyList<MeasExitView> Exit, IReadOnlyList<MeasPulseView> Pulse,
    IReadOnlyList<MeasEventView> Events, IReadOnlyList<MeasOtbView> Otb, IReadOnlyList<MeasCompetitorView> Competitors, IReadOnlyList<MeasRateShopView> Rateshop,
    IReadOnlyList<MeasRestrictionView> Restrictions);
