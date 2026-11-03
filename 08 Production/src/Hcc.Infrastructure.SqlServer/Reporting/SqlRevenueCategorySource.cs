using System.Data;
using Hcc.Application.Reporting;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>F4-T2: rooms with their type code and the stay segments of the stays that touch [from, to] (gst.stay_segment, migration 0300), with the folio of each stay.</summary>
public sealed class SqlRevenueCategorySource(SqlConnectionFactory connections) : IRevenueCategorySource
{
    private const string Sql = """
        SELECT r.room_id, t.code FROM inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE r.property_id = @p ORDER BY r.room_id;
        SELECT sg.stay_id, s.folio_id, sg.room_id, t.code, sg.from_date, sg.to_date
          FROM gst.stay_segment sg JOIN gst.stay s ON s.stay_id = sg.stay_id
          JOIN inv.room r ON r.room_id = sg.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id
         WHERE s.property_id = @p AND sg.from_date <= @to AND (sg.to_date IS NULL OR sg.to_date >= @from)
         ORDER BY sg.stay_id, sg.seq;
        """;

    public async Task<RevenueCategoryData> LoadAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rooms = new List<CategoryRoom>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) rooms.Add(new CategoryRoom(rd.GetInt32(0), rd.GetString(1)));
        var segs = new List<CategorySegment>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            segs.Add(new CategorySegment(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), DateOnly.FromDateTime(rd.GetDateTime(4)), rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5))));
        return new RevenueCategoryData(rooms, segs);
    }

    private const string BalancesSql = """
        SELECT (SELECT ISNULL(SUM(c.amount_paise + c.tax_paise), 0) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
                 WHERE r.property_id = @p AND c.service_date <= @d)
             - (SELECT ISNULL(SUM(CASE WHEN x.kind IN ('payment','deposit-apply','company-credit','ota-credit','guest-credit') THEN x.amount_paise WHEN x.kind = 'refund' THEN -x.amount_paise ELSE 0 END), 0)
                  FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.property_id = @p AND x.business_date <= @d),
               (SELECT ISNULL(SUM(CASE WHEN x.kind = 'deposit' THEN x.amount_paise WHEN x.kind IN ('deposit-return','deposit-apply') THEN -x.amount_paise ELSE 0 END), 0)
                  FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.property_id = @p AND x.business_date <= @d);
        """;

    public async Task<FolioBalances> BalancesAsync(int propertyId, DateOnly asOf, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(BalancesSql, c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = asOf.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new FolioBalances(rd.GetInt64(0), rd.GetInt64(1));
    }

    private const string LegacySql = """
        SELECT t.code, b.room_charge_paise, b.extra_paise, b.fb_paise, b.laundry_paise, b.other_paise, b.discount_paise, b.room_gst_paise, b.fb_gst_paise, b.laundry_gst_paise, b.other_gst_paise
          FROM lgc.bill b LEFT JOIN inv.room r ON r.property_id = b.property_id AND r.room_no = b.room_no LEFT JOIN inv.room_type t ON t.room_type_id = r.room_type_id
         WHERE b.property_id = @p AND b.bill_date BETWEEN @from AND @to;
        SELECT t.code, x.kind, x.method, x.amount_paise
          FROM lgc.receipt x LEFT JOIN lgc.stay s ON s.stay_id = x.stay_id LEFT JOIN inv.room r ON r.property_id = x.property_id AND r.room_no = s.room_no LEFT JOIN inv.room_type t ON t.room_type_id = r.room_type_id
         WHERE x.property_id = @p AND x.received_date BETWEEN @from AND @to;
        """;

    public async Task<RevenueLegacyData?> LegacyAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(LegacySql, c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var bills = new List<LegacyBillRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            bills.Add(new LegacyBillRow(rd.IsDBNull(0) ? null : rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetInt64(4), rd.GetInt64(5), rd.GetInt64(6), rd.GetInt64(7), rd.GetInt64(8), rd.GetInt64(9), rd.GetInt64(10)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var receipts = new List<LegacyReceiptRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            receipts.Add(new LegacyReceiptRow(rd.IsDBNull(0) ? null : rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetInt64(3)));
        return bills.Count == 0 && receipts.Count == 0 ? null : new RevenueLegacyData(bills, receipts);
    }
}
