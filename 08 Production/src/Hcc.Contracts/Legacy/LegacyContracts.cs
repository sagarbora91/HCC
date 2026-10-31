namespace Hcc.Contracts.Legacy;

// W1a LG · legacy "Lodge" data importer (02 Planning/agents/LG-HISTORY-CONTRACT.md §5). Command POST /commands/legacy.import (Owner only,
// PERM-LGC-001) reads a run folder written by tools/legacy/extract.py on the server's own disk; GET /legacy/runs lists the runs.
// Money is integer paise; dates YYYY-MM-DD; FY keys 'YYYY-YY'.

/// <summary>
/// <see cref="Folder"/> = absolute local path of the run folder (no UNC); <see cref="Parts"/> ⊂ guests, history, cutover;
/// <see cref="ExpectedTotals"/> = the text of totals.json the Owner expects (null → the folder's own totals.json).
/// </summary>
public sealed record LegacyImportRequest(string Folder, IReadOnlyList<string> Parts, bool DryRun = true, string? ExpectedTotals = null);

public sealed record LegacyGuestsResult(int CsvRows, int Created, int MatchedExisting, int SkippedInvalidPhone, int Merged, int SecretsSealed, int DobSet,
    int PhotosCopied, int IdProofsCopied, int TagsAdded, IReadOnlyList<string> Errors);

/// <summary>One reconciliation difference: the figure name, the expected value (totals.json) and the value computed from the inserted rows.</summary>
public sealed record LegacyDifference(string Figure, long Expected, long Actual);

public sealed record LegacyReconciliation(long BillsCount, long NetPaise, long RoomGstPaise, long FbPaise, long ReceiptsCount, long ReceiptsPaise, long RefundsCount,
    long RefundsPaise, bool Match, IReadOnlyList<LegacyDifference> Differences);

public sealed record LegacyFyResult(string Fy, int Stays, int Segments, int Bills, int Receipts, int DuplicatesDropped, int OutOfFy, LegacyReconciliation Reconciliation);

public sealed record LegacyHistoryResult(IReadOnlyList<LegacyFyResult> PerFy, IReadOnlyList<string> Errors);

public sealed record LegacyCutoverResult(int CustomerOpeningRows, int ReceivablesRows, int AdvancesRows, int InhouseRows, string Note);

public sealed record LegacyImportResult(
    long RunId,
    bool DryRun,
    IReadOnlyList<string> Parts,
    string ExtractorVersion,
    LegacyGuestsResult? Guests,
    LegacyHistoryResult? History,
    LegacyCutoverResult? Cutover,
    IReadOnlyList<string> Warnings,
    bool Committed);

/// <summary>A row of <c>lgc.import_run</c> (GET /legacy/runs, newest first). <see cref="Result"/> = the stored result (null while running).</summary>
public sealed record LegacyRunView(long RunId, string SourceFolder, string ExtractorVersion, bool DryRun, string Parts, string StartedUtc, string? FinishedUtc,
    string Status, int RunBy, LegacyImportResult? Result);

public sealed record LegacyRunList(IReadOnlyList<LegacyRunView> Runs);
