namespace Hcc.Contracts.Import;

// W4-13 masters & opening balances import. Flow per batch: UploadImportBatch (CSV text → stored batch + validation report)
// → GET /import/batches/{id} (fresh validation + preview against the current data) → CommitImportBatch (one transaction;
// dryRun = everything runs and is rolled back). Money in reports is integer paise; dates YYYY-MM-DD.

/// <summary>
/// Upload one CSV file for one master. <see cref="Master"/>: room-types, rooms, tariffs, seasons, staff, ledgers, opening-balances,
/// till-cash, guests. <see cref="AsOfDate"/> (YYYY-MM-DD) is required for opening-balances only.
/// </summary>
public sealed record UploadImportBatchRequest(string Master, string FileName, string CsvText, string? AsOfDate = null);

/// <summary>Commit a validated batch. <see cref="DryRun"/> runs every write and rolls it back (nothing is kept, not even the audit).</summary>
public sealed record CommitImportBatchRequest(long BatchId, bool DryRun = false);

public sealed record ImportRowError(int Row, string Column, string Message);

/// <summary>One planned change. Action: Create, Update, Unchanged, Remove. Row 0 = the file as a whole.</summary>
public sealed record ImportPlanRow(int Row, string Action, string Key, string Detail);

public sealed record ImportCounts(int Create, int Update, int Unchanged, int Remove);

/// <summary>Validation + preview of a batch. <see cref="CommitAllowed"/> is false while there are errors or when the batch was committed.</summary>
public sealed record ImportBatchReport(
    long BatchId,
    string Master,
    string FileName,
    string Status,
    DateOnly? AsOfDate,
    int Rows,
    int ErrorCount,
    IReadOnlyList<ImportRowError> Errors,
    IReadOnlyList<ImportRowError> Warnings,
    ImportCounts Counts,
    IReadOnlyList<ImportPlanRow> Plan,
    long? DebitPaise,
    long? CreditPaise,
    bool CommitAllowed,
    string? CommitBlockedReason,
    string UploadedUtc,
    int UploadedBy,
    string? CommittedUtc,
    int? CommittedBy);

public sealed record ImportBatchSummary(long BatchId, string Master, string FileName, string Status, int Rows, int ErrorCount, string UploadedUtc, int UploadedBy,
    string? CommittedUtc, int? CommittedBy);

public sealed record ImportBatchList(IReadOnlyList<ImportBatchSummary> Batches);

/// <summary>A staff user created without a credential: the Owner sets the first password/PIN with the Access admin SetCredential (one-time reset).</summary>
public sealed record ImportCreatedUser(int UserId, string LoginName, string DisplayName, string Version, string CredentialKind);

public sealed record ImportCommitResult(
    long BatchId,
    string Master,
    bool DryRun,
    bool Committed,
    ImportCounts Counts,
    IReadOnlyList<ImportPlanRow> Plan,
    IReadOnlyList<ImportCreatedUser> UsersNeedingCredential,
    long? DebitPaise,
    long? CreditPaise);

public sealed record OpeningBalanceRowView(string LedgerCode, string LedgerName, string Group, string? HccAccount, long DebitPaise, long CreditPaise);

public sealed record OpeningBalancesView(DateOnly? AsOfDate, long? BatchId, long DebitPaise, long CreditPaise, bool Balanced, IReadOnlyList<OpeningBalanceRowView> Rows);

public sealed record ImportLedgerView(string Code, string Name, string Group, string? HccAccount);

public sealed record ImportLedgersView(IReadOnlyList<ImportLedgerView> Ledgers);

public sealed record ImportColumnView(string Name, bool Required, string Meaning, string Example);

public sealed record ImportMasterView(string Master, string Title, bool OwnerOnlyCommit, bool SetupStateOnly, string DependsOn, IReadOnlyList<ImportColumnView> Columns);

public sealed record ImportMastersView(bool SetupState, DateOnly BusinessDate, IReadOnlyList<ImportMasterView> Masters);
