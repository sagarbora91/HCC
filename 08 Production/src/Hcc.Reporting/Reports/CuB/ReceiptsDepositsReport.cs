// CU-RPT-B · rpt.money.receipts-deposits — Receipts and deposits (catalogue C2.PreviousAdvance). Server twin of the prototype's closing-pack tab "Receipts & deposits"
// (03 Prototype/src/domain/reports/built/pack.ts). Payments minus refunds by receipt date, one column per tender; deposits received and returned by receipt date;
// company credit in the range and deposits held now as a second block. Rule M: every figure is money (PERM-RPT-002).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Money;
using Microsoft.Data.SqlClient;
using ReportReceipt = Hcc.Domain.Reporting.ReportReceipt;

namespace Hcc.Reporting.Reports.CuB;

public sealed class ReceiptsDepositsReport(IReportSource source, SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.money.receipts-deposits";

    /// <summary>The fixed columns; one tender column per method (key <c>t:&lt;name&gt;</c>) sits between the date and the net.</summary>
    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MoneyRules.Dt("date", "Receipt date"), MoneyRules.M("net", "Net receipts"), MoneyRules.M("depositsIn", "Deposits in"), MoneyRules.M("depositsOut", "Deposits out"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Receipts and deposits", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Financial, [MoneyRules.Range(ReportDateClass.Financial), MoneyRules.RangeTo(ReportDateClass.Financial)], Columns,
        "Payments minus refunds by receipt date, one column per tender; deposits received and returned by receipt date. Totals per tender; deposits held now is a point-in-time figure in the second block.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, ["C2.PreviousAdvance"]);

    private const string HeldSql = """
        SELECT ISNULL(SUM(CASE x.kind WHEN 'deposit' THEN x.amount_paise WHEN 'deposit-return' THEN -x.amount_paise
                                      WHEN 'deposit-apply' THEN CASE WHEN x.method = @opening THEN 0 ELSE -x.amount_paise END ELSE 0 END), 0)
          FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
         WHERE r.property_id = @p;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var data = await source.LoadAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var methods = await CuBSupport.MethodNamesAsync(platform, caller.PropertyId, CuBSupport.Place.FrontDesk, ct).ConfigureAwait(false);
        if (methods.Count == 0) methods = Tenders.All; // a property whose master has no desk method yet: the seed list
        var receipts = data.Receipts.Where(r => r.BusinessDate >= from && r.BusinessDate <= to).ToList();
        var found = receipts.Where(r => r.Kind is ReceiptKind.Payment or ReceiptKind.Refund).Select(r => r.Tender).Distinct().Where(t => !methods.Contains(t)).Order(StringComparer.Ordinal);
        var tenders = methods.Concat(found).ToList();

        long Net(IEnumerable<ReportReceipt> rs) => rs.Sum(r => r.Kind == ReceiptKind.Payment ? r.Amount.Value : -r.Amount.Value);
        long Kind(IEnumerable<ReportReceipt> rs, ReceiptKind k) => rs.Where(r => r.Kind == k).Sum(r => r.Amount.Value);
        IEnumerable<ReportReceipt> PayRefund(IEnumerable<ReportReceipt> rs) => rs.Where(r => r.Kind is ReceiptKind.Payment or ReceiptKind.Refund);

        var cols = new List<ReportColumn> { Columns[0] };
        cols.AddRange(tenders.Select(t => MoneyRules.M("t:" + t, t)));
        cols.AddRange(Columns.Skip(1));

        var rows = new List<ReportRow>();
        foreach (var d in CuBSupport.Dates(from, to))
        {
            var day = receipts.Where(r => r.BusinessDate == d).ToList();
            var cells = new List<(string, object?)> { ("date", d) };
            cells.AddRange(tenders.Select(t => ("t:" + t, (object?)Net(PayRefund(day).Where(r => r.Tender == t)))));
            cells.Add(("net", Net(PayRefund(day))));
            cells.Add(("depositsIn", Kind(day, ReceiptKind.Deposit)));
            cells.Add(("depositsOut", Kind(day, ReceiptKind.DepositReturn)));
            rows.Add(CuBSupport.Row(ReportRowKind.Row, [.. cells]));
        }

        var total = new List<(string, object?)> { ("date", "Total") };
        total.AddRange(tenders.Select(t => ("t:" + t, (object?)Net(PayRefund(receipts).Where(r => r.Tender == t)))));
        total.Add(("net", Net(PayRefund(receipts))));
        total.Add(("depositsIn", Kind(receipts, ReceiptKind.Deposit)));
        total.Add(("depositsOut", Kind(receipts, ReceiptKind.DepositReturn)));
        rows.Add(CuBSupport.Row(ReportRowKind.Total, [.. total]));

        var held = await HeldNowAsync(caller.PropertyId, ct).ConfigureAwait(false);
        ReportColumn[] pit = [MoneyRules.T("item", "Point in time"), MoneyRules.M("amount", "Amount")];
        var now = new List<ReportRow>
        {
            CuBSupport.Row(ReportRowKind.Row, ("item", "Company credit (city ledger) in the range"), ("amount", Kind(receipts, ReceiptKind.CompanyCredit))),
            CuBSupport.Row(ReportRowKind.Row, ("item", "Deposits held now · all open folios"), ("amount", held)),
        };
        var a = new ReportTable("days", "By receipt date", cols, rows);
        var b = new ReportTable("now", "Credit and deposits held", pit, now);
        return new ReportResult(Id, Definition.Title, parameters, a.Columns, a.Rows, CuBSupport.About(Definition, parameters), Sections: [a, b]);
    }

    /// <summary>Deposits held now over every folio: received − returned − applied (an advance carried in the opening balance never sat in a deposit receipt).</summary>
    private async Task<long> HeldNowAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(HeldSql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@opening", SqlDbType.NVarChar, 120).Value = FolioTotals.OpeningAdvanceTender;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}
