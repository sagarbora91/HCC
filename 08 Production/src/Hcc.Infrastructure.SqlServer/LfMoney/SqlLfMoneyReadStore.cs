using System.Data;
using Hcc.Application.Counter;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.LfMoney;

/// <summary>SQL adapter of <see cref="ILfMoneyReadStore"/> (read-only: ctr.bill, gst.guest_credit_account, gst.stay_rate_change).</summary>
public sealed class SqlLfMoneyReadStore(SqlConnectionFactory connections) : ILfMoneyReadStore
{
    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long? L(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    public async Task<IReadOnlyList<CounterBillListRow>> CounterBillsAsync(int propertyId, int take, string? invoiceNo, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        // AF-CTR-READS (CON-01): one batch, one id set. The id set is fixed once, so a bill committed meanwhile can neither add a header without lines nor push a header out.
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            DECLARE @ids TABLE(bill_id BIGINT PRIMARY KEY);
            INSERT @ids SELECT TOP (@n) bill_id FROM ctr.bill
              WHERE property_id = @p
                AND (@inv IS NULL OR RTRIM(invoice_no) = @inv)
                AND (@from IS NULL OR business_date >= @from) AND (@to IS NULL OR business_date <= @to)
              ORDER BY bill_id DESC;
            SELECT l.bill_id, l.seq, l.label, l.extra_id, l.menu_item_id, l.qty, l.unit_paise, l.amount_paise, l.gst_category, l.tax_bps, l.sac
            FROM ctr.bill_line l JOIN @ids i ON i.bill_id = l.bill_id ORDER BY l.bill_id, l.seq;
            SELECT b.bill_id, b.invoice_no, b.business_date, b.issued_utc, b.issued_by, b.party_name, b.party_phone, b.party_gstin, b.party_address, b.guest_id, b.company_id, b.discount_paise, b.discount_reason,
              b.net_paise, b.tax_paise, b.cgst_paise, b.sgst_paise, b.total_paise, b.remark, b.settlement_mode, b.method, b.reference, b.udhaar_id, b.status, b.credit_note_no, b.credit_note_reason, b.void_approval_id
            FROM ctr.bill b JOIN @ids i ON i.bill_id = b.bill_id ORDER BY b.bill_id DESC;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = take;
        cmd.Parameters.Add("@inv", SqlDbType.NVarChar, 40).Value = (object?)invoiceNo ?? DBNull.Value;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from is null ? DBNull.Value : from.Value.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to is null ? DBNull.Value : to.Value.ToDateTime(TimeOnly.MinValue);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var lines = new Dictionary<long, List<CounterBillLineRow>>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = r.GetInt64(0);
            if (!lines.TryGetValue(id, out var l)) lines[id] = l = [];
            l.Add(new CounterBillLineRow(r.GetInt32(1), r.GetString(2), S(r, 3), S(r, 4), r.GetInt32(5), r.GetInt64(6), r.GetInt64(7), r.GetString(8), r.GetInt32(9), r.GetString(10).Trim()));
        }

        await r.NextResultAsync(ct).ConfigureAwait(false);
        var list = new List<CounterBillListRow>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = r.GetInt64(0);
            list.Add(new CounterBillListRow(id, r.GetString(1).Trim(), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetDateTime(3), r.GetInt32(4), r.GetString(5), S(r, 6), S(r, 7)?.Trim(), S(r, 8), L(r, 9), L(r, 10),
                r.GetInt64(11), S(r, 12), r.GetInt64(13), r.GetInt64(14), r.GetInt64(15), r.GetInt64(16), r.GetInt64(17), S(r, 18), r.GetString(19), S(r, 20), S(r, 21), L(r, 22), r.GetString(23),
                S(r, 24), S(r, 25), L(r, 26), lines.TryGetValue(id, out var ls) ? ls : []));
        }

        return list;
    }

    public async Task<IReadOnlyList<CreditGuestRow>> CreditGuestsAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT a.guest_id, g.full_name, g.phone, a.limit_paise, a.active, a.customer_key, a.note,
              COALESCE(o.open_paise, 0), o.oldest
            FROM gst.guest_credit_account a JOIN gst.guest g ON g.guest_id = a.guest_id
            OUTER APPLY (SELECT SUM(r.amount_paise - COALESCE(st.paid, 0)) AS open_paise, MIN(r.credit_date) AS oldest
                         FROM ops.udhaar_receivable r
                         OUTER APPLY (SELECT SUM(x.amount_paise) AS paid FROM ops.udhaar_settlement x WHERE x.receivable_id = r.receivable_id) st
                         WHERE r.property_id = a.property_id AND r.is_void = 0 AND (r.guest_id = a.guest_id OR (LEN(a.customer_key) = 10 AND r.mobile = a.customer_key))
                           AND r.amount_paise - COALESCE(st.paid, 0) > 0) o
            WHERE a.property_id = @p ORDER BY a.guest_id;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var list = new List<CreditGuestRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CreditGuestRow(r.GetInt64(0), r.GetString(1), S(r, 2), r.GetInt64(3), r.GetBoolean(4), r.GetString(5), S(r, 6), r.GetInt64(7), r.IsDBNull(8) ? null : DateOnly.FromDateTime(r.GetDateTime(8))));
        return list;
    }

    public async Task<IReadOnlyList<StayRateChangeRow>> RateChangesAsync(int propertyId, int take, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) c.change_id, c.stay_id, c.reservation_id, rm.room_no, c.business_date, c.changed_utc, c.changed_by, c.approval_id, c.approved_by, c.scope, c.reason,
              c.old_rate_paise, c.new_rate_paise, c.old_people, c.new_people, c.old_children, c.new_children, c.effective_from, c.reposted_json
            FROM gst.stay_rate_change c JOIN inv.room rm ON rm.room_id = c.room_id WHERE c.property_id = @p ORDER BY c.change_id DESC;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = take;
        var list = new List<StayRateChangeRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new StayRateChangeRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), DateOnly.FromDateTime(r.GetDateTime(4)), r.GetDateTime(5), r.GetInt32(6),
                L(r, 7), r.IsDBNull(8) ? null : r.GetInt32(8), r.GetString(9), r.GetString(10), r.GetInt64(11), r.GetInt64(12), r.GetByte(13), r.GetByte(14), r.GetByte(15), r.GetByte(16),
                DateOnly.FromDateTime(r.GetDateTime(17)), r.GetString(18)));
        return list;
    }
}
