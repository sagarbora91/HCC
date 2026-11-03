// F7 report module — contract types (02 Planning/agents/F7-CONTRACT.md §1, RPT-ARCHITECTURE.md). Declared by the setup commit; lanes implement,
// never redeclare. rpt-engine (R1) may add members; nobody renames. Money = long paise. Mirrors 03 Prototype/src/domain/reports/types.ts.
using Hcc.Application.Billing;

namespace Hcc.Application.Reporting.Rpt;

/// <summary>Catalogue §1.4 date classes.</summary>
public enum ReportDateClass { Day, Short, Business, Financial, Month, FinancialYear, AsOf, Now, Forward }

public enum ReportFormat { Screen, Xlsx, Pdf, Csv, Zip, Xml, Print }

public enum ReportPhone { Yes, Summary, No }

public enum ReportColumnType { Text, Int, Money, Date, Percent, Bool }

/// <summary>Catalogue §1.6 masking kinds.</summary>
public enum ReportMask { None, GuestName, IdNumber, BankAccount }

/// <summary>OP-13 hook: <paramref name="Expr"/> names a fixed-code function today (for example <c>pct(occupied,sellable)</c>).</summary>
public sealed record ReportComputed(string Expr, IReadOnlyList<string> Deps);

public sealed record ReportColumn(string Key, string Label, ReportColumnType Type, bool Money, ReportMask Mask = ReportMask.None, ReportComputed? Computed = null);

public sealed record ReportParam(string Key, string Label, string Type, ReportDateClass? DateClass = null, IReadOnlyList<string>? Options = null, string? Default = null, bool Required = false);

public sealed record ReportDefinition(
    string Id, string Title, string Area, string Priority, string Status,
    bool MoneyFlag, string? MoneyGate, IReadOnlyList<string> ViewPerms, IReadOnlyList<string> DeskPerms, string ExportPerm,
    ReportDateClass? DateClass, IReadOnlyList<ReportParam> Params, IReadOnlyList<ReportColumn> Columns, string DateBasis,
    IReadOnlyList<ReportFormat> Formats, ReportPhone Phone, bool DocumentLayout = false, IReadOnlyList<string>? LegacyIds = null,
    bool OwnShift = false); // F7-R1: scoped grants (Cashier V·S) count only for own-shift reports

/// <summary>Parameters after binding (defaults filled from the business date, date class checked).</summary>
public sealed record BoundParams(IReadOnlyDictionary<string, string> Values, DateOnly? From = null, DateOnly? To = null, bool Legacy = false);

public enum ReportRowKind { Row, Subtotal, Total }

/// <summary>Cells keyed by column key: null / string / long (paise or count) / DateOnly / double (percent with one decimal, 49.4 = 49.4 %, as the prototype) / bool.</summary>
public sealed record ReportRow(ReportRowKind Kind, IReadOnlyDictionary<string, object?> Cells, string? Group = null, string? Drill = null);

public sealed record ReportAbout(DateTimeOffset RunAt, string User, string DateBasis, bool Provisional, bool Legacy, DateOnly? From = null, DateOnly? To = null);

public sealed record ReportResult(string Id, string Title, BoundParams Params, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows, ReportAbout About,
    IReadOnlyList<string>? Removed = null, int PageIndex = 0, int PageSize = 0, int TotalRows = 0,
    IReadOnlyList<ReportTable>? Sections = null); // F7-R1: several blocks; Columns / Rows = Sections[0] when present

/// <summary>F7-R1: one block of a multi-block report (for example revenue A / collections B / bridge C).</summary>
public sealed record ReportTable(string Key, string? Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);

public sealed record RenderContext(string PropertyCode, string PropertyName, ReportFormat Format);

public sealed record RenderedFile(string FileName, string ContentType, byte[] Bytes);

public sealed record ReportScheduleRecord(long Id, string ReportId, IReadOnlyDictionary<string, string> Params, ReportFormat Format,
    string Cadence, int? Day, TimeOnly Time, int OwnerPrincipalId, IReadOnlyList<int> Recipients, bool Enabled, DateTimeOffset? LastRun = null);

public sealed record ReportRunRecord(long Id, long? ScheduleId, string ReportId, string ParamsJson, int PrincipalId, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, string Status, int? Rows = null, string? FileRef = null, string? Error = null);

public sealed record ReportExportLogRecord(long Id, DateTimeOffset At, int PrincipalId, string ReportId, string ParamsJson, ReportFormat Format,
    int Rows, string? FileName, bool Provisional);

/// <summary>One report's data selector: the single query; screen, Excel and PDF all render its result.</summary>
public interface IReportSelector
{
    ReportDefinition Definition { get; }
    Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default);
}

public interface IReportRenderer
{
    ReportFormat Format { get; }
    RenderedFile Render(ReportResult result, RenderContext context);
}

public interface IReportCatalogue
{
    IReadOnlyList<ReportDefinition> All { get; }
    IReportSelector? Find(string id);
}

/// <summary>OP-13 computed-column hook (fixed code until the calculation engine lands).</summary>
public interface IComputedColumnEngine
{
    bool CanEvaluate(ReportComputed computed);
    object? Evaluate(ReportComputed computed, IReadOnlyDictionary<string, object?> cells);
}

public interface IReportScheduleStore
{
    Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(int propertyId, CancellationToken ct = default);
    Task<long> SaveAsync(int propertyId, ReportScheduleRecord schedule, CancellationToken ct = default);
    Task DeleteAsync(int propertyId, long id, CancellationToken ct = default);
}

public interface IReportRunStore
{
    Task<long> StartAsync(int propertyId, ReportRunRecord run, CancellationToken ct = default);
    Task FinishAsync(int propertyId, long id, string status, int? rows, string? fileRef, string? error, CancellationToken ct = default);
    Task<IReadOnlyList<ReportRunRecord>> ListAsync(int propertyId, int take, CancellationToken ct = default);
}

public interface IReportExportLog
{
    Task<long> AppendAsync(int propertyId, ReportExportLogRecord entry, CancellationToken ct = default);
}
