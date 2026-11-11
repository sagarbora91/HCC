// CU-RPT-A · rpt.money.cash-book — one row per known business date (opening, receipts, payments, book closing, counted, over / short) and a second block with every drawer entry
// and its running balance. Server twin of built/money.ts `cashBook`. Money gate PERM-RPT-002; date class "short" (62 days). Figures = CashBookCalculator, the same arithmetic as
// the cash book page (CashBookQueries). The booking and guest columns of the entry block are blank: the server's drawer movement carries no booking.
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Reporting.Sql;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.CuA;

public sealed class CashBookReport(ICashBookSource source, IClosingStore closing, IMoneyReportSource names) : IReportSelector
{
    public const string Id = "rpt.money.cash-book";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        Dt("date", "Business date"), T("status", "Status"), M("opening", "Opening"), M("receipts", "Receipts"), M("payments", "Payments (incl. banking)"), M("closing", "Closing (book)"),
        M("counted", "Counted"), M("overShort", "Over / short"),
    ];

    public static readonly IReadOnlyList<ReportColumn> EntryColumns =
    [
        Dt("date", "Business date"), T("ref", "Reference"), T("entry", "Entry"), T("detail", "Detail"), T("booking", "Booking"), T("guest", "Guest", ReportMask.GuestName),
        M("in", "Receipt"), M("out", "Payment"), M("balance", "Balance"), T("by", "By"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Cash book", "money", "P1", "built", true, CuASql.Finance, [CuASql.Finance], [], "PERM-RPT-003", ReportDateClass.Short,
        [CuASql.From(ReportDateClass.Short), CuASql.To(ReportDateClass.Short)], Columns,
        "One row per known business date: opening = the closing float, receipts and payments = the cashier-shift movements of that date (plus banking after the count), closing = book balance, counted and over / short from the closing record. Second block: every entry with the running balance.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    /// <summary>The two blocks from the calculator's range view (pure).</summary>
    public static (List<ReportRow> Days, List<ReportRow> Entries) Blocks(CashBookRangeView r, IReadOnlyDictionary<int, string> userNames)
    {
        ArgumentNullException.ThrowIfNull(r);
        var rows = r.Days.Select(d => CuASql.Row(ReportRowKind.Row, ("date", d.BusinessDate), ("status", d.Status + (d.Locked ? " · locked" : string.Empty)), ("opening", d.OpeningPaise),
            ("receipts", d.ReceiptsPaise), ("payments", d.PaymentsPaise), ("closing", d.ClosingPaise), ("counted", d.CountedPaise), ("overShort", d.DifferencePaise))).ToList();
        if (r.Days.Count > 0)
            rows.Add(CuASql.Row(ReportRowKind.Total, ("date", "Range total"), ("status", null), ("opening", r.OpeningPaise), ("receipts", r.ReceiptsPaise), ("payments", r.PaymentsPaise),
                ("closing", r.ClosingPaise), ("counted", null), ("overShort", r.DifferencesPaise)));
        var entries = new List<ReportRow>();
        foreach (var d in r.Days)
        {
            var bal = d.OpeningPaise;
            entries.Add(CuASql.Row(ReportRowKind.Row, ("date", d.BusinessDate), ("ref", null), ("entry", "Opening balance"), ("detail", null), ("booking", null), ("guest", null), ("in", null), ("out", null),
                ("balance", bal), ("by", null)));
            foreach (var e in d.Entries)
            {
                bal += e.InPaise - e.OutPaise;
                entries.Add(CuASql.Row(ReportRowKind.Row, ("date", d.BusinessDate), ("ref", "CM-" + e.MovementId), ("entry", e.Label), ("detail", e.Reference), ("booking", null), ("guest", null),
                    ("in", e.InPaise == 0 ? null : e.InPaise), ("out", e.OutPaise == 0 ? null : e.OutPaise), ("balance", bal), ("by", NameOf(userNames, e.PrincipalId))));
            }
        }

        return (rows, entries);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var bd = await closing.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await source.LoadAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var view = CashBookCalculator.Compute(from, to, bd, data);
        var users = await names.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var (days, entries) = Blocks(view, users);
        return new ReportResult(Id, Definition.Title, parameters, Columns, days, About(Definition, parameters), null, 0, 0, 0,
            [new ReportTable("days", "By business date", Columns, days), new ReportTable("entries", "Entries with running balance", EntryColumns, entries)]);
    }
}
