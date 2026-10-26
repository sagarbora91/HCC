using System.Text.Json;

namespace Hcc.Contracts.Configuration;

// Master configuration wire contracts (W2-1; DEC-057/058). Values travel as JSON (string, integer paise/bps/minutes, boolean).
// Commands are POST /api/v1/commands/<Name> with an Idempotency-Key; queries are GET /api/v1/configuration/...

/// <summary>POST /commands/SetSetting (K V). <c>expectedVersion</c> = the setting's version from the group query (null when it has none yet).</summary>
public sealed record SetSettingRequest(string Key, JsonElement? Value, string? EffectiveFrom = null, string? Reason = null, string? ExpectedVersion = null);

/// <summary>Status: <c>applied</c> (in force now), <c>scheduled</c> (future effective date) or <c>requested</c> (waits for approval).</summary>
public sealed record SetSettingResult(string Status, string Key, long? ChangeId, long? RequestId, string EffectiveFrom, string Before, string After, string? Version, string Message);

/// <summary>POST /commands/DecideSettingRequest (K V): approve or reject a Tech admin's change (never your own request).</summary>
public sealed record DecideSettingRequestRequest(long RequestId, bool Approve, string? Reason = null, string? ExpectedVersion = null);

public sealed record DecideSettingRequestResult(long RequestId, string Status, string Key, long? ChangeId, string? EffectiveFrom, string Message);

/// <summary>POST /commands/WithdrawScheduledSetting (K): withdraw a future-dated change before its date (history kept).</summary>
public sealed record WithdrawScheduledSettingRequest(long ChangeId, string? Reason = null);

public sealed record WithdrawScheduledSettingResult(long ChangeId, string Key, string EffectiveFrom, string Message);

/// <summary>POST /commands/RevertSetting (K): a normal change back to the value before <c>changeId</c> (latest applied change only).</summary>
public sealed record RevertSettingRequest(long ChangeId, string? EffectiveFrom = null, string? Reason = null);

/// <summary>POST /commands/ImportConfiguration (K): a Master configuration export file (secrets are never imported).</summary>
public sealed record ImportConfigurationRequest(JsonElement Config);

public sealed record ImportSkip(string Key, string Reason);

public sealed record ImportConfigurationResult(int Changed, int Unchanged, IReadOnlyList<ImportSkip> Skipped, string Message);

/// <summary>Dry run of an import for the caller (POST /configuration/import-plan; no write).</summary>
public sealed record ImportPlanView(IReadOnlyList<ImportPlanItem> Apply, IReadOnlyList<ImportSkip> Skipped, IReadOnlyList<string> Errors, int Unchanged);

public sealed record ImportPlanItem(string Key, string Label, string After, string EffectiveFrom);

/// <summary>GET /configuration/export (JSON, no secrets, nothing personal).</summary>
public sealed record ConfigurationExport(string Format, int Version, int Schema, string ExportedAt, string BusinessDate, string Property,
    IReadOnlyDictionary<string, JsonElement> Values, IReadOnlyList<ExportScheduled> Scheduled, IReadOnlyList<string> Excluded);

public sealed record ExportScheduled(string Key, JsonElement Value, string EffectiveFrom);

public sealed record ConfigGroupView(string Id, string Label, string Section, bool Money, string Blurb, string Summary, bool ReadOnly, string? Reason);

public sealed record ConfigGroupsResult(string BusinessDate, IReadOnlyList<ConfigGroupView> Groups);

public sealed record ChangeDecisionView(string Mode, string Reason, string? Approver);

public sealed record ScheduledValueView(long ChangeId, string EffectiveFrom, string Display);

/// <summary>One setting for the viewer. <c>value</c> is omitted when the value is secret or hidden from this viewer.</summary>
public sealed record ConfigSettingView(string Key, string Label, string Group, string Type, string Unit, string Help, bool Editable, string Approval,
    string Effective, string EffectiveMode, string Approver, bool Money, bool Secret, bool Dated, bool Illustrative, string Affects,
    JsonElement? Value, string Display, string? Version, ChangeDecisionView CanChange, IReadOnlyList<ScheduledValueView> Scheduled,
    long? PendingRequestId, IReadOnlyList<JsonElement>? Options, long? Min, long? Max, string? Suffix);

public sealed record ConfigGroupDetail(ConfigGroupView Group, IReadOnlyList<ConfigSettingView> Settings);

public sealed record ConfigSearchHit(string GroupId, string GroupLabel, bool GroupMatch, IReadOnlyList<string> Keys);

public sealed record ConfigSearchResult(string Query, IReadOnlyList<ConfigSearchHit> Hits);

public sealed record SettingHistoryEntry(long ChangeId, string Key, string Label, string Before, string After, string EffectiveFrom, string Status,
    string ChangedBy, string? ApprovedBy, string? Reason, string Source, long? RevertOf, string At, bool CanRevert, string? RevertBlock);

public sealed record SettingHistoryPage(int Page, int PageSize, int Total, IReadOnlyList<SettingHistoryEntry> Items);

public sealed record SettingRequestView(long RequestId, string Key, string Label, string Summary, string? Reason, string Approver, string RequestedBy,
    string RequestedAt, string ExpiresAt, string Status, string? DecidedBy, string? DecisionReason, string EffectiveFrom, string Version, string? DecideBlock);

public sealed record SettingRequestsResult(IReadOnlyList<SettingRequestView> Items);

public sealed record ModuleStateView(string Id, string Label, string Note, bool On);

/// <summary>GET /configuration/modules (any signed-in user: menus, tiles, search and phone tabs hide switched-off modules).</summary>
public sealed record ModulesResult(IReadOnlyList<ModuleStateView> Modules, IReadOnlyList<string> Core);

/// <summary>
/// GET /api/v1/configuration/values (W4-5; any signed-in user): the values in force on the business date that every screen reads
/// (property name, check-in/out times, discount threshold, inspection, module switches …) plus future-dated values. Secrets and
/// Owner-only-visible values are left out for viewers without full configuration authority; fixed pointer lines are never included.
/// <c>Versions</c> = the setting row version of every key that has been saved at least once (the <c>expectedVersion</c> SetSetting needs;
/// a key without a row is absent and is saved with no expectedVersion). A version carries no value, so secrets are listed too.
/// </summary>
public sealed record ConfigurationValuesResult(string BusinessDate, IReadOnlyDictionary<string, JsonElement> Values, IReadOnlyList<ExportScheduled> Scheduled,
    IReadOnlyDictionary<string, string>? Versions = null);
