// F7-R7 · the document loaders that need a join the module read models do not carry: the folio header (guest, room, dates) and the quotation of a reservation. Read-only SQL; the
// caller's right is checked first (PERM-FOL-001 is already enforced by the folio read; the quotation needs PERM-RES-001), so a document never widens a role.
using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Documents;

public sealed class DocumentSql(SqlPlatform platform, IAccessPolicy policy)
{
    private const string HeaderSql = """
        SELECT r.public_no, r.guest_name, COALESCE(rm.room_no, ''), r.arrival, r.departure
          FROM bill.folio f
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
         WHERE f.folio_id = @f AND r.property_id = @p;
        """;

    /// <summary>Header facts of the folio's reservation (the folio itself is read, with its right, by BillingQueries).</summary>
    public async Task<FolioHeader> FolioHeaderAsync(Caller c, long folioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(HeaderSql, conn);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = c.PropertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "No such folio.");
        return new FolioHeader(rd.GetString(1), rd.GetString(2), rd.GetString(0), DateOnly.FromDateTime(rd.GetDateTime(3)), DateOnly.FromDateTime(rd.GetDateTime(4)));
    }

    private const string QuoteSql = """
        SELECT r.public_no, r.guest_name, COALESCE(rm.room_no, ''), r.arrival, r.departure, r.adults + r.children, r.source, r.nightly_rate_paise,
               COALESCE((SELECT SUM(CASE WHEN pr.kind = 'payment' THEN pr.amount_paise ELSE 0 END) FROM bill.folio f JOIN pay.receipt pr ON pr.folio_id = f.folio_id WHERE f.reservation_id = r.reservation_id), 0)
          FROM inv.reservation r
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
         WHERE r.property_id = @p AND r.public_no = @no;
        """;

    /// <summary>Room GST per night on the agreed rate: 5 % up to 7,500 a night, 18 % above (the dated GST master's current slabs), half away from zero.</summary>
    public static long NightTax(long ratePaise) => (ratePaise * (ratePaise <= 750000 ? 500 : 1800) + 5000) / 10000;

    public async Task<QuotationInput> QuotationAsync(Caller c, string bookingNo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, "PERM-RES-001", ct: ct).ConfigureAwait(false);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(QuoteSql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = c.PropertyId;
        cmd.Parameters.Add("@no", SqlDbType.VarChar, 20).Value = bookingNo;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "No such booking.");
        if (rd.IsDBNull(7)) throw HccException.Validation("This booking has no agreed rate yet.");
        var rate = rd.GetInt64(7);
        var arrival = DateOnly.FromDateTime(rd.GetDateTime(3));
        var departure = DateOnly.FromDateTime(rd.GetDateTime(4));
        var nights = Math.Max(1, departure.DayNumber - arrival.DayNumber);
        return new QuotationInput(rd.GetString(0), rd.GetString(1), null, rd.GetString(2), arrival, departure, Convert.ToInt32(rd.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
            rd.GetString(6), "Per house rules", "Per house rules", rate, NightTax(rate) * nights, rd.GetInt64(8), null);
    }
}
