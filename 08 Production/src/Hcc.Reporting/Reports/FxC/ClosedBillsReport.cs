// FX-RPT-C · rpt.rest.closed-bills — the checks settled on a business date, in closing order (pos.bill, status Settled; a voided bill is left out). Server twin of built/rest.ts
// `restClosedBills`. Money gate PERM-POS-004; an own-shift Cashier (PERM-POS-004 at shift scope) sees only the bills they settled. Check = the tax invoice number of the bill when
// one was issued (ivc.invoice, source pos), else the bill number. Items = pieces on lines not voided. Total row only when there are bills.
using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ClosedBillsReport(SqlPlatform platform, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.rest.closed-bills";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("check", "Check"), T("outlet", "Outlet"), T("table", "Table"), T("closedAt", "Closed at"), I("items", "Items"), M("total", "Total"), T("tender", "Tender"), T("server", "Server")];

    public ReportDefinition Definition { get; } = new(Id, "Closed bills", "rest", "P2", "built", true, "PERM-POS-004", ["PERM-POS-004"], [], "PERM-RPT-003", ReportDateClass.Day,
        [new("from", "Business date", "date", ReportDateClass.Day)], Columns,
        "Checks settled on the business date, in closing order. A Cashier sees only the bills they settled.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes, OwnShift: true);

    public sealed record Bill(string Check, string Outlet, string Spot, string ClosedAt, long Items, long Total, string? Tender, string Server, int SettledBy);

    public static List<ReportRow> Rows(IEnumerable<Bill> bills)
    {
        var rows = bills.Select(b => FxCSupport.Row(ReportRowKind.Row, ("check", b.Check), ("outlet", b.Outlet), ("table", b.Spot), ("closedAt", b.ClosedAt), ("items", b.Items), ("total", b.Total),
            ("tender", b.Tender), ("server", b.Server))).ToList();
        if (rows.Count > 0)
            rows.Add(FxCSupport.Row(ReportRowKind.Total, ("check", "Total"), ("outlet", null), ("table", null), ("closedAt", null), ("items", bills.Sum(b => b.Items)), ("total", bills.Sum(b => b.Total)),
                ("tender", null), ("server", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var all = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, "PERM-POS-004", new AccessScope(AccessScopeKinds.Property, 0), ct).ConfigureAwait(false);
        var names = await FxCSupport.NamesAsync(platform, caller.PropertyId, ct).ConfigureAwait(false);
        var bills = new List<Bill>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand("""
                SELECT COALESCE(iv.invoice_no, b.bill_no), o.name, c.table_no, r.room_no, b.settled_utc,
                       (SELECT COALESCE(SUM(l.qty), 0) FROM pos.order_line l WHERE l.order_id = c.order_id AND l.voided = 0),
                       b.total_paise, b.mode, b.tender, c.opened_by, b.settled_by, b.bill_id
                  FROM pos.bill b JOIN pos.check_order c ON c.order_id = b.order_id JOIN pos.outlet o ON o.outlet_id = c.outlet_id
                  LEFT JOIN inv.room r ON r.room_id = c.room_id
                  OUTER APPLY (SELECT TOP (1) i.invoice_no FROM ivc.invoice i WHERE i.property_id = b.property_id AND i.source_kind = 'pos' AND i.source_id = b.bill_id ORDER BY i.revision DESC, i.invoice_id DESC) iv
                 WHERE b.property_id = @p AND b.status = 'Settled' AND b.business_date = @d
                 ORDER BY b.settled_utc, b.bill_id;
                """, conn);
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
            cmd.Parameters.Add("@d", SqlDbType.Date).Value = parameters.From!.Value.ToDateTime(TimeOnly.MinValue);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var settledBy = rd.GetInt32(10);
                if (!all && settledBy != caller.PrincipalId) continue;
                var spot = rd.IsDBNull(2) ? "Room " + (rd.IsDBNull(3) ? string.Empty : rd.GetString(3)) : "Table " + rd.GetString(2);
                var tender = rd.GetString(7) == "folio" ? "Room folio" : rd.IsDBNull(8) ? null : rd.GetString(8);
                bills.Add(new Bill(rd.GetString(0), rd.GetString(1), spot, IstText(rd.GetDateTime(4)), Convert.ToInt64(rd.GetValue(5), System.Globalization.CultureInfo.InvariantCulture), rd.GetInt64(6),
                    tender, FxCSupport.NameOf(names, rd.GetInt32(9)), settledBy));
            }
        }

        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(bills), FxCSupport.About(Definition, parameters));
    }
}
