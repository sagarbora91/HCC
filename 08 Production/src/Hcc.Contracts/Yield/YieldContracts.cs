namespace Hcc.Contracts.Yield;

/// <summary>
/// PERM ids used by yield &amp; tariffs (no new ids; RBAC matrix v1 + DEC-063). View = report.operations (Owner, Manager, Accountant);
/// Propose = approval.decide (Owner, Manager: the prototype's "approve"); Manage and Decide = admin.configuration (Owner only;
/// the Technical administrator's view-only cell does not satisfy this ACTION permission).
/// </summary>
public static class YieldPermissions
{
    public const string View = "PERM-RPT-001";
    public const string Propose = "PERM-APR-001";
    public const string Manage = "PERM-ADM-003";
    public const string Decide = "PERM-ADM-003";
}

/// <summary>Domain events written to the outbox.</summary>
public static class YieldEvents
{
    public const string TariffsPublished = "YieldTariffsPublished";
    public const string ProposalRaised = "YieldProposalRaised";
    public const string ProposalDecided = "YieldProposalDecided";
}

// ───────────── commands (every one needs Idempotency-Key; expectedVersion = base64 rowversion of the row shown) ─────────────
public sealed record SaveYieldPlanRequest(string PlanCode, string ExpectedVersion, int? AdjBps, int? MinNights, bool? Active, int? CloseAtOcc, string? Reason);

public sealed record SaveYieldSeasonRequest(int? SeasonId, string? ExpectedVersion, bool Remove, string? Name, string? From, string? To, int AdjBps,
    IReadOnlyList<int>? RoomTypeIds, int? Priority, string? Reason);

public sealed record SetYieldDayOfWeekRequest(IReadOnlyList<int> Dow, string ExpectedVersion, string? Reason);

public sealed record SaveYieldOccupancyRuleRequest(int? RuleId, string? ExpectedVersion, bool Remove, string? Op, int Occ, int AdjBps, string? Reason);

public sealed record SetYieldGuardrailsRequest(int FloorBps, int CeilBps, int RoundToPaise, string ExpectedVersion, string? Reason);

public sealed record SaveYieldRestrictionRequest(int? RestrictionId, string? ExpectedVersion, bool Remove, int? RoomTypeId, string? From, string? To,
    int MinStay, bool ClosedToArrival, string? Note, string? Reason);

/// <summary>Set (RatePaise given) or clear (null) the manual rate for one room type and night; a reason is mandatory.</summary>
public sealed record SetYieldOverrideRequest(int RoomTypeId, string Night, long? RatePaise, string Reason);

public sealed record YieldForecastNight(string Night, int RoomsSold);

public sealed record SetYieldForecastRequest(IReadOnlyList<YieldForecastNight> Nights);

public sealed record PublishYieldRequest(string From, string To, IReadOnlyList<int> RoomTypeIds, string? Reason);

public sealed record ProposeYieldPublishRequest(string From, string To, IReadOnlyList<int> RoomTypeIds, string? Reason);

public sealed record DecideYieldProposalRequest(long ProposalId, string ExpectedVersion, bool Approve, string? Note);

// ───────────── results ─────────────
public sealed record YieldChangeResult(string Summary, string? Version);

public sealed record YieldPublishResult(long PublishId, int ChangedNights, string From, string To, string Summary, long? ProposalId = null);

public sealed record YieldProposalResult(long ProposalId, string Status, int ChangedNights, string ExpectedVersion, string ExpiresUtc, string Summary);

public sealed record YieldDecisionResult(long ProposalId, string Status, long? PublishId, int ChangedNights, string Summary);

// ───────────── queries ─────────────
public sealed record YieldStepView(string Label, int Bps, string Detail);

public sealed record YieldOccupancyView(int OnBooks, int Forecast, int Sellable, int Pct, bool Simulated);

public sealed record YieldNightView(int RoomTypeId, string RoomType, string Date, long BasePaise, string BaseSource, IReadOnlyList<YieldStepView> Steps,
    int RawBps, int AppliedBps, string? Clamped, long ComputedPaise, long? OverridePaise, long SuggestedPaise, long LivePaise, YieldOccupancyView Occupancy,
    IReadOnlyList<string> RestrictionCodes, string Explain);

public sealed record YieldSummaryView(long AverageBar7Paise, int HighDemandNights, int PendingNights, int PendingProposals);

public sealed record YieldCalendarResult(string From, int Nights, IReadOnlyList<YieldNightView> Cells, YieldSummaryView Summary);

public sealed record YieldPlanView(string PlanCode, string Name, int AdjBps, int MinNights, IReadOnlyList<int>? SellOnDays, string? DayUseFrom,
    string? DayUseTo, int? CloseAtOcc, bool Active, string Note, string Version);

public sealed record YieldSeasonView(int SeasonId, string Name, string From, string To, int AdjBps, IReadOnlyList<int> RoomTypeIds, int Priority, string Version);

public sealed record YieldOccRuleView(int RuleId, string Op, int Occ, int AdjBps, string Version);

public sealed record YieldRestrictionView(int RestrictionId, string Code, int? RoomTypeId, string From, string To, int MinStay, bool ClosedToArrival, string Note, string Version);

public sealed record YieldOverrideView(int RoomTypeId, string Night, long RatePaise, string Reason, int ActorId, string SetUtc);

public sealed record YieldHistoryView(long HistoryId, string AtUtc, int ActorId, string Area, string Before, string After, string Reason);

public sealed record YieldConfigResult(IReadOnlyList<YieldPlanView> Plans, IReadOnlyList<YieldSeasonView> Seasons, IReadOnlyList<int> Dow, int FloorBps,
    int CeilBps, int RoundToPaise, string SettingsVersion, IReadOnlyList<YieldOccRuleView> OccupancyRules, IReadOnlyList<YieldRestrictionView> Restrictions,
    IReadOnlyList<YieldOverrideView> Overrides, IReadOnlyList<YieldForecastNight> Forecast, IReadOnlyList<YieldHistoryView> History);

public sealed record YieldQuoteRow(string Date, long RatePaise, long BarPaise);

public sealed record YieldQuoteResult(string PlanCode, int RoomTypeId, string Arrival, int Nights, IReadOnlyList<YieldQuoteRow> Rows, long TotalPaise, IReadOnlyList<string> Issues);

public sealed record YieldProposalView(long ProposalId, string Status, string From, string To, IReadOnlyList<int> RoomTypeIds, int ChangedNights, int ProposedBy,
    string ProposedUtc, string ExpiresUtc, string Reason, int? DecidedBy, string? DecidedUtc, string? DecisionNote, long? PublishId, string Version);

public sealed record YieldPublishRowView(int RoomTypeId, string Night, long BeforePaise, long AfterPaise, bool Closing);

public sealed record YieldPublishView(long PublishId, string From, string To, int ActorId, string PublishedUtc, string Reason, long? ProposalId,
    IReadOnlyList<YieldPublishRowView> Rows);
