// F7-R5 · rpt.tax.bill-corrections — Bill corrections (catalogue A4, LF A2; ivc.bill_correction). Issued corrections by the date of the re-issued invoice: old and new
// invoice, amounts (incl. GST), GST of each, who and why. Server twin of tax/bill-corrections.ts. The money gate is the report's own permission PERM-RPT-002.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class BillCorrectionsReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.tax.bill-corrections";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("correctedAt", "Corrected at"), T("oldNo", "Old invoice no"), T("newNo", "New invoice no"), T("guest", "Guest", ReportMask.GuestName), T("room", "Room"), M("oldAmount", "Old amount"),
        M("newAmount", "New amount"), M("difference", "Difference"), M("oldGst", "Old GST"), M("newGst", "New GST"), T("reason", "Reason"), T("correctedBy", "Corrected by"), T("approvedBy", "Approved by"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Bill corrections", "tax", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("correctedBy", "Corrected by", "user")],
        Columns,
        "Issued corrections by the date the Owner approved them (range up to 92 days). Amounts include GST; old GST = GST of the credited invoice, new GST = GST of the re-issued invoice.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, ["A4"]);

    private const string Sql = """
        SELECT c.decided_utc, oi.invoice_no, ni.invoice_no, COALESCE(g.full_name, r.guest_name), rm.room_no, c.old_total_paise, c.new_total_paise,
               oi.cgst_paise + oi.sgst_paise + oi.igst_paise, ni.cgst_paise + ni.sgst_paise + ni.igst_paise, c.reason,
               COALESCE(ur.display_name, CAST(c.requested_by AS NVARCHAR(20))), COALESCE(ua.display_name, CAST(c.approved_by AS NVARCHAR(20))), c.correction_id
          FROM ivc.bill_correction c
          JOIN ivc.invoice oi ON oi.invoice_id = c.old_invoice_id
          JOIN ivc.invoice ni ON ni.invoice_id = c.new_invoice_id
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN gst.stay s ON s.folio_id = f.folio_id
          LEFT JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN plat.app_user ur ON ur.user_id = c.requested_by
          LEFT JOIN plat.app_user ua ON ua.user_id = c.approved_by
         WHERE c.property_id = @p AND c.status = 'Issued' AND ni.invoice_date BETWEEN @from AND @to
           AND (@by = '' OR CAST(c.requested_by AS NVARCHAR(20)) = @by OR LOWER(ur.login_name) = LOWER(@by))
         ORDER BY c.decided_utc, c.correction_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        if (to.DayNumber - from.DayNumber + 1 > 92) throw HccException.Validation("The range is longer than 92 days. Narrow the range.");
        var rows = new List<ReportRow>();
        long o = 0, n = 0, og = 0, ng = 0;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
            GuestsSql.Add(cmd, "@by", SqlDbType.NVarChar, parameters.Values.GetValueOrDefault("correctedBy") ?? "");
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                long oldAmt = rd.GetInt64(5), newAmt = rd.GetInt64(6), oldGst = rd.GetInt64(7), newGst = rd.GetInt64(8);
                o += oldAmt;
                n += newAmt;
                og += oldGst;
                ng += newGst;
                rows.Add(Row(ReportRowKind.Row, ("correctedAt", GuestsSql.Local(clock, rd.GetDateTime(0))), ("oldNo", rd.GetString(1)), ("newNo", rd.GetString(2)), ("guest", GuestsSql.Str(rd, 3)),
                    ("room", GuestsSql.Str(rd, 4)), ("oldAmount", oldAmt), ("newAmount", newAmt), ("difference", newAmt - oldAmt), ("oldGst", oldGst), ("newGst", newGst), ("reason", rd.GetString(9)),
                    ("correctedBy", GuestsSql.Str(rd, 10)), ("approvedBy", GuestsSql.Str(rd, 11))));
            }
        }

        var k = rows.Count;
        rows.Add(Row(ReportRowKind.Total, ("correctedAt", $"Total · {k} correction{(k == 1 ? "" : "s")}"), ("oldAmount", o), ("newAmount", n), ("difference", n - o), ("oldGst", og), ("newGst", ng)));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
