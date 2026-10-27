namespace Hcc.Contracts.Accounting;

// ACC-1 · The chart of accounts as an Owner-only master (posting contract §1, §2, §7; prototype ACC_LEDGER_UPSERT / ACC_LEDGER_RETIRE /
// ACC_GROUP_UPSERT, s.accountingX.chart). Commands need PERM-ACC-007 (Owner); reads PERM-ACC-001. Money = paise.

/// <summary>
/// UpsertLedger: <see cref="Create"/> = add an Owner ledger (an unused 4-digit code); otherwise edit the ledger <see cref="Code"/> (a built-in:
/// name, group within the same Tally nature, GST flag, Tally name, note). Null fields keep their value; <see cref="Active"/> true reactivates.
/// <see cref="Gst"/> = none | input | output.
/// </summary>
public sealed record UpsertLedgerRequest(bool Create, string Code, string? Name, string? GroupId, string? Gst, string? TallyName, string? Note, bool? Active);

/// <summary>RetireLedger: refused (MASTER_IN_USE) while a mapping key points at the ledger or it or one of its parties has a balance.</summary>
public sealed record RetireLedgerRequest(string Code, string? Reason);

/// <summary>UpsertLedgerGroup: no <see cref="Id"/> = a new custom sub-group <c>g-&lt;slug&gt;</c> under <see cref="Parent"/>; otherwise rename /
/// re-parent a custom sub-group (Tally's 28 groups are fixed).</summary>
public sealed record UpsertLedgerGroupRequest(string? Id, string? Name, string? Parent);

public sealed record LedgerChangeResult(string Code, string Name, bool Created, bool Active, IReadOnlyList<ChartChangeView> Changes);

public sealed record LedgerGroupResult(string Id, string Name, string Parent, bool Created, IReadOnlyList<ChartChangeView> Changes);

/// <summary>One chart history row (field level). <see cref="Kind"/> = ledger | group; <see cref="Code"/> = ledger code or group id.</summary>
public sealed record ChartChangeView(long ChangeId, DateTime AtUtc, int Actor, string Kind, string Code, string Field, string Before, string After);

public sealed record LedgerGroupView(string Id, string Name, string? Parent, string Nature, string Side, bool Custom, string LegacyGroup, IReadOnlyList<string> Path);

/// <summary>A ledger of the effective chart. <see cref="Group"/> = the legacy group (Asset / Liability / Income / Expense / Suspense).</summary>
public sealed record LedgerView(string Code, string Name, string GroupId, string Group, string? Nature, bool System, bool Control, string Gst, string? TallyName,
    bool Active, string? Note, bool Overridden, IReadOnlyList<string> MappedBy);

public sealed record ChartProblemView(string Id, string Severity, string? Code, string Title, string Detail);

/// <summary>GET /accounting/chart/master: the property's groups, ledgers, chart problems and whether the caller may edit (PERM-ACC-007).</summary>
public sealed record ChartMasterView(IReadOnlyList<LedgerGroupView> Groups, IReadOnlyList<LedgerView> Ledgers, IReadOnlyList<ChartProblemView> Problems, bool CanEdit);
