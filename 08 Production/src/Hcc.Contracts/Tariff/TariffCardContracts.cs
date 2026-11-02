namespace Hcc.Contracts.Tariff;

/// <summary>F4-T1 card line on the wire (prototype TRCLine). Money in paise; EnteredIncl set = the user typed the inclusive price.</summary>
public sealed record TariffCardLineDto(int LineNo, string Kind, string? RoomType = null, string? Plan = null, string? OccKind = null, string? MealPlan = null,
    string? Extra = null, string? Hall = null, string DaySet = "all", long? RateExcl = null, long? EnteredIncl = null, int? MinGuests = null, int? MaxGuests = null,
    int? AgeFrom = null, int? AgeTo = null, string? Note = null, int TaxBps = 0, long? DisplayIncl = null);

/// <summary>SaveTariffCardDraft K: create (VersionId null) or edit a draft / proposed version (an edit withdraws the proposal).</summary>
public sealed record SaveTariffCardDraftRequest(long? VersionId, string Label, string EffectiveFrom, IReadOnlyList<TariffCardLineDto> Lines, IReadOnlyList<int>? WeekendDays = null, string? Notes = null);

/// <summary>ProposeTariffCard / PublishTariffCard / DiscardTariffCard K.</summary>
public sealed record TariffCardIdRequest(long VersionId);

/// <summary>RejectTariffCard K — reason 3–200 characters.</summary>
public sealed record RejectTariffCardRequest(long VersionId, string Reason);

/// <summary>SetTariffCardWeekend K — VersionId null = the default for new cards.</summary>
public sealed record SetTariffCardWeekendRequest(long? VersionId, IReadOnlyList<int> Days);

public sealed record TariffCardWriteDto(string Master, string Ref, long? Before, long After, string Label);

public sealed record TariffCardVersionView(long VersionId, string Code, string Label, string EffectiveFrom, string Status, IReadOnlyList<int> WeekendDays,
    IReadOnlyList<TariffCardLineDto> Lines, int CreatedBy, string CreatedUtc, int? ProposedBy, int? PublishedBy, string? PublishedUtc, long? SupersededBy, string? Notes,
    IReadOnlyList<TariffCardWriteDto> PublishLog);

public sealed record TariffCardList(IReadOnlyList<TariffCardVersionView> Versions, IReadOnlyList<int> WeekendDaysDefault);
