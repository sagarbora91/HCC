// FX-RPT-C · rpt.admin.report-dictionary — the static dictionary of the closing-report figures as the Financial Rulebook (section 6, REQ-RPT-006) defines them.
// Server twin of built/admin.ts `reportDictionaryReport`; the twelve rows are the prototype's `reportDictionary` (streams/closing.ts) word for word except four glyphs the PDF text check cannot round-trip (arrow, sigma, minus sign and the curly apostrophe become "->", "Sum of", "-" and "'"). No data, no money, no parameters.
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ReportDictionaryReport : IReportSelector
{
    public const string Id = "rpt.admin.report-dictionary";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("metric", "Metric"), T("definition", "Definition"), T("zero", "Zero handling")];

    public static readonly IReadOnlyList<(string Metric, string Definition, string Zero)> Entries =
    [
        ("Sellable rooms", "Rooms not blocked on the business date (sellable room-nights over a range)", "—"),
        ("Occupied rooms", "Stays in house (or since departed) whose stay interval [arrival, departure) covers the date", "—"),
        ("Occupancy %", "Occupied ÷ sellable × 100, rounded half-up to 1 dp", "sellable = 0 -> \"—\""),
        ("Room revenue", "Sum of room-night lines with service date in range (discounts are reported separately)", "—"),
        ("ADR", "Room revenue ÷ occupied room-nights", "0 nights -> \"—\""),
        ("RevPAR", "Room revenue ÷ sellable room-nights", "0 sellable -> \"—\""),
        ("Receipts", "Sum of payment receipts - refunds by receipt date; deposits shown separately", "—"),
        ("Deposits held", "Sum of deposits - deposit returns across all folios, at the current moment", "—"),
        ("Outstanding", "Sum of positive folio balances of open stays", "—"),
        ("Expenses", "Sum of posted expenses by business date, split by funding source; a reversal is a negative record on its own date", "—"),
        ("Drawer cash flows", "Cash in = guest payments + deposits + POS cash sales; cash out = negative refunds + deposit returns + POS cash voids. Filtered by receipt date; cash expenses are separate.",
            "Opening float + cash in + signed cash out - net cash expenses = expected cash for that day, before banking"),
        ("GST (CGST + SGST)", "Per-line GST at the line's snapshot rate from the dated GST master, rounded half away from zero to the paisa; CGST + SGST halves split per line (illustrative — CA to confirm)", "—"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Report dictionary", "admin", "P2", "built", false, null, ["PERM-RPT-001", "PERM-RPT-002"], [], "PERM-RPT-003", null, [], Columns,
        "Static: the figures of the closing reports as the Financial Rulebook (section 6, REQ-RPT-006) defines them.", [ReportFormat.Screen, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var rows = Entries.Select(e => FxCSupport.Row(ReportRowKind.Row, ("metric", e.Metric), ("definition", e.Definition), ("zero", e.Zero))).ToList();
        return Task.FromResult(new ReportResult(Id, Definition.Title, parameters, Columns, rows, FxCSupport.About(Definition, parameters)));
    }
}
