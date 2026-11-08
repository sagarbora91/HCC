// F7-R7 · the document loaders that need a join the module read models do not carry: the folio header (guest, room, dates) and the quotation of a reservation. Read-only SQL; the
// caller's right is checked first (PERM-FOL-001 is already enforced by the folio read; the quotation needs PERM-RES-001), so a document never widens a role.
using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Documents;

public sealed class DocumentSql(SqlPlatform platform, IAccessPolicy policy, IGstRateResolver gst)
{
    // F8D-B1 · the buyer and the invoice of THIS folio (SF-CONTRACT §2.2, §5): once the folio's tax invoice is issued (ivc.invoice, source 'folio' =
    // folio_id, latest revision) the print shows that invoice's number, buyer and buyer GSTIN; before that, the folio payer (payer_name / payer_gstin,
    // the same columns FolioPartyAsync gives the invoice at checkout: company = name + GSTIN, Udhaar = the customer's name, guest = the guest).
    private const string HeaderSql = """
        SELECT r.public_no, r.guest_name, COALESCE(rm.room_no, ''), r.arrival, r.departure,
               i.invoice_no, COALESCE(i.buyer_name, f.payer_name), CASE WHEN i.invoice_no IS NULL THEN f.payer_gstin ELSE i.buyer_gstin END
          FROM bill.folio f
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          OUTER APPLY (SELECT TOP (1) iv.invoice_no, iv.buyer_name, iv.buyer_gstin FROM ivc.invoice iv
                        WHERE iv.property_id = r.property_id AND iv.source_kind = 'folio' AND iv.source_id = f.folio_id ORDER BY iv.revision DESC) i
         WHERE f.folio_id = @f AND r.property_id = @p;
        """;

    private static string? Text(SqlDataReader rd, int i) => rd.IsDBNull(i) || rd.GetString(i).Trim() is not { Length: > 0 } s ? null : s;

    /// <summary>Header facts of the folio's reservation (the folio itself is read, with its right, by BillingQueries), with the folio's own payer and issued invoice.</summary>
    public async Task<FolioHeader> FolioHeaderAsync(Caller c, long folioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(HeaderSql, conn);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = c.PropertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "No such folio.");
        return new FolioHeader(rd.GetString(1), rd.GetString(2), rd.GetString(0), DateOnly.FromDateTime(rd.GetDateTime(3)), DateOnly.FromDateTime(rd.GetDateTime(4)),
            Text(rd, 5), Text(rd, 6), Text(rd, 7));
    }

    private const string QuoteSql = """
        SELECT r.public_no, r.guest_name, COALESCE(rm.room_no, ''), r.arrival, r.departure, r.adults + r.children, r.source, r.nightly_rate_paise,
               COALESCE((SELECT SUM(CASE WHEN pr.kind = 'payment' THEN pr.amount_paise ELSE 0 END) FROM bill.folio f JOIN pay.receipt pr ON pr.folio_id = f.folio_id WHERE f.reservation_id = r.reservation_id), 0)
          FROM inv.reservation r
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
         WHERE r.property_id = @p AND r.public_no = @no;
        """;

    /// <summary>Room GST of the stay from the dated GST master (DEC-069): each night at the slab in force on that night, CGST + SGST on the night, half away from zero.</summary>
    public static long StayTax(IEnumerable<(DateOnly Night, GstRate Rate)> nights, long ratePaise) =>
        nights.Sum(n => { var tax = (ratePaise * n.Rate.Bps + 5000) / 10000; var sgst = tax / 2; return (tax - sgst) + sgst; });

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
        var nights = await gst.RoomNightsAsync(null, c.PropertyId, arrival, departure, new Paise(rate), ct).ConfigureAwait(false);
        return new QuotationInput(rd.GetString(0), rd.GetString(1), null, rd.GetString(2), arrival, departure, Convert.ToInt32(rd.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
            rd.GetString(6), "Per house rules", "Per house rules", rate, StayTax(nights, rate), rd.GetInt64(8), null);
    }
}
