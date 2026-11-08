// F8-RPT-P2-A · rpt.guests.company-business — Business from company (A12). Server twin of p2-a/company-business.ts.
// Attribution (SF-CONTRACT R8): the whole stay of a reservation billed to the company account. The company-folio-only rule for split stays needs the folio payer
// tables of sf-server (sfCompanyLines twin); until they merge the whole stay is used (listed in the DONE file under Integration needed).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class CompanyBusinessReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.company-business";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("company", "Company"), I("guests", "Guests"), I("roomNights", "Room-nights"), M("tariff", "Tariff"), M("other", "Other charges"), M("total", "Total business")];

    public ReportDefinition Definition { get; } = new(Id, "Business from company", "guests", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial), new("company", "Company", "text")],
        Columns, "Charges posted in the range (business date of the line) on stays billed to a company account; on a split stay only the company's own folio. Tariff = room-night charges, other charges = every other line (discounts and corrections included), both before GST; total business = tariff + other charges. Room-nights = room nights charged and not reversed.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Summary, false, ["A12"]);

    private const string Sql = """
        SELECT co.company_id, co.name, COALESCE(CAST(reg.guest_id AS NVARCHAR(30)), r.guest_name), c.kind, c.amount_paise,
               CASE WHEN c.kind = 'room' AND c.amount_paise > 0 AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id) THEN 1 ELSE 0 END
          FROM bill.charge c
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          JOIN grp.company_account co ON co.company_id = r.company_id
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
         WHERE r.property_id = @p AND c.service_date BETWEEN @from AND @to;
        """;

    private sealed class Acc { public string Name = ""; public readonly HashSet<string> Guests = []; public long Nights, Tariff, Other; }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var q = (parameters.Values.GetValueOrDefault("company") ?? "").Trim();
        var acc = new Dictionary<long, Acc>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt64(0);
                var name = rd.GetString(1);
                if (q.Length > 0 && !name.Contains(q, StringComparison.OrdinalIgnoreCase) && !id.ToString(System.Globalization.CultureInfo.InvariantCulture).Equals(q, StringComparison.Ordinal)) continue;
                if (!acc.TryGetValue(id, out var a)) acc[id] = a = new Acc { Name = name };
                a.Guests.Add(GuestsSql.Str(rd, 2));
                var amount = rd.GetInt64(4);
                if (rd.GetString(3) == "room") a.Tariff += amount; else a.Other += amount;
                a.Nights += Convert.ToInt64(rd.GetValue(5));
            }
        }

        var rows = new List<ReportRow>();
        long tg = 0, tn = 0, tt = 0, to = 0;
        foreach (var a in acc.Values.OrderBy(x => x.Name, StringComparer.CurrentCulture))
        {
            var c = Cells();
            c["company"] = a.Name; c["guests"] = (long)a.Guests.Count; c["roomNights"] = a.Nights; c["tariff"] = a.Tariff; c["other"] = a.Other; c["total"] = a.Tariff + a.Other;
            rows.Add(Row(ReportRowKind.Row, c, a.Name));
            tg += a.Guests.Count; tn += a.Nights; tt += a.Tariff; to += a.Other;
        }

        var t = Cells();
        t["company"] = $"Total · {Plural(rows.Count, "company", "companies")}"; t["guests"] = tg; t["roomNights"] = tn; t["tariff"] = tt; t["other"] = to; t["total"] = tt + to;
        rows.Add(Row(ReportRowKind.Total, t));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
