// F7-R4 · money-a reports (batch 1): the reads the existing queries do not give (receipt detail with guest / room / bill, shifts with their
// movements and hand-over, credit raised, user names). One class, plain SELECTs on the tables the commands write; no new tables.
using System.Data;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Reporting.Reports.Money;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Sql;

/// <summary>A cash shift with the sums of its drawer movements: <see cref="Receipts"/> = payments + deposits + POS sales (into the drawer); <see cref="Other"/> = every other movement, signed.</summary>
public sealed record ShiftFact(long ShiftId, string Till, DateOnly Date, string Status, long Opening, long? StoredExpected, long? Counted, int UserId, long Receipts, long Other,
    string? HandKind, string? HandBank, string? HandTill, DateTime? HandUtc);

/// <summary>One collection line before the report filters. <see cref="Kind"/> = pay.receipt kind, or counter / counter-credit / company-payment / udhaar-payment.</summary>
public sealed record CollectionFact(DateOnly Date, string ReceiptNo, string Guest, string Room, string BillNo, string Tender, long Amount, long Tds, int By, string Source, string Kind);

/// <summary>Credit raised: a company-credit or guest-credit receipt of a stay, or a company counter bill. <see cref="Account"/> = C:&lt;company id&gt; or G:&lt;guest key&gt;.</summary>
public sealed record CreditFact(DateOnly Date, long Order, string BillNo, string Party, string Room, long Credit, long Limit, string Account);

/// <summary>A payment received against a credit account (company payment amount + TDS, or a guest Udhaar payment).</summary>
public sealed record CreditPaymentFact(string Account, DateOnly Date, long Amount);

public interface IMoneyReportSource
{
    Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<ShiftFact>> ShiftsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<CollectionFact>> CollectionsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>All credit raised up to <paramref name="to"/> (the running balance needs the earlier bills) and all payments up to it.</summary>
    Task<(IReadOnlyList<CreditFact> Credit, IReadOnlyList<CreditPaymentFact> Payments)> CreditAsync(int propertyId, DateOnly to, CancellationToken ct = default);
}

public sealed class SqlMoneyReportSource(SqlConnectionFactory connections) : IMoneyReportSource
{
    private static void Add(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static DateOnly D(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);

    private static void Range(SqlCommand c, int p, DateOnly from, DateOnly to)
    {
        Add(c, "@p", SqlDbType.Int, p); Add(c, "@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)); Add(c, "@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
    }

    public async Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT user_id, display_name FROM plat.app_user WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var map = new Dictionary<int, string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetInt32(0)] = rd.GetString(1);
        return map;
    }

    public async Task<IReadOnlyList<ShiftFact>> ShiftsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.shift_id, t.name, s.business_date, s.status, s.opening_float_paise, s.expected_paise, s.counted_paise, COALESCE(s.submitted_by, s.opened_by),
                   ISNULL(SUM(CASE WHEN m.kind IN ('payment','deposit','pos-sale') THEN m.amount_paise END), 0),
                   ISNULL(SUM(CASE WHEN m.kind NOT IN ('payment','deposit','pos-sale') THEN m.amount_paise END), 0)
              FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id LEFT JOIN pay.cash_movement m ON m.shift_id = s.shift_id
             WHERE t.property_id = @p AND s.business_date BETWEEN @from AND @to
             GROUP BY s.shift_id, t.name, s.business_date, s.status, s.opening_float_paise, s.expected_paise, s.counted_paise, s.submitted_by, s.opened_by
             ORDER BY s.business_date, s.shift_id;
            SELECT x.from_shift_id, x.kind, x.bank, tt.name, x.recorded_utc
              FROM pay.cash_transfer x LEFT JOIN pay.till tt ON tt.till_id = x.to_till_id
             WHERE x.property_id = @p AND x.business_date BETWEEN @from AND @to AND x.status <> 'Rejected' ORDER BY x.transfer_id;
            """, conn);
        Range(cmd, propertyId, from, to);
        var rows = new List<(long Id, string Till, DateOnly Date, string Status, long Opening, long? Exp, long? Cnt, int User, long Rec, long Oth)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            rows.Add((rd.GetInt64(0), rd.GetString(1), D(rd, 2), rd.GetString(3), rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetInt64(5), rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.GetInt32(7), rd.GetInt64(8), rd.GetInt64(9)));
        var hand = new Dictionary<long, (string Kind, string? Bank, string? Till, DateTime Utc)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            hand.TryAdd(rd.GetInt64(0), (rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetDateTime(4)));
        return rows.Select(x => hand.TryGetValue(x.Id, out var h)
            ? new ShiftFact(x.Id, x.Till, x.Date, x.Status, x.Opening, x.Exp, x.Cnt, x.User, x.Rec, x.Oth, h.Kind, h.Bank, h.Till, h.Utc)
            : new ShiftFact(x.Id, x.Till, x.Date, x.Status, x.Opening, x.Exp, x.Cnt, x.User, x.Rec, x.Oth, null, null, null, null)).ToList();
    }

    public async Task<IReadOnlyList<CollectionFact>> CollectionsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT x.business_date, x.receipt_id, x.kind, x.method, x.amount_paise, r.guest_name,
                   ISNULL((SELECT TOP 1 rm.room_no FROM inv.room_night n JOIN inv.room rm ON rm.room_id = n.room_id WHERE n.reservation_id = r.reservation_id ORDER BY n.night, rm.room_no), ''),
                   ISNULL((SELECT TOP 1 i.invoice_no FROM ivc.invoice i WHERE i.property_id = @p AND i.source_kind = 'folio' AND i.source_id = x.folio_id ORDER BY i.invoice_id DESC), r.public_no),
                   ISNULL(x.principal_id, 0)
              FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
             WHERE r.property_id = @p AND x.business_date BETWEEN @from AND @to ORDER BY x.business_date, x.receipt_id;
            SELECT b.business_date, b.invoice_no, b.party_name, b.total_paise, b.method, b.issued_by, b.status, b.credit_date, b.credit_note_no, b.bill_id
              FROM ctr.bill b WHERE b.property_id = @p AND b.settlement_mode = 'paid'
               AND (b.business_date BETWEEN @from AND @to OR b.credit_date BETWEEN @from AND @to) ORDER BY b.bill_id;
            SELECT p.business_date, p.payment_id, c.name, p.tender, p.invoice_no, p.amount_paise, p.tds_paise, p.principal_id
              FROM grp.company_payment p JOIN grp.company_account c ON c.company_id = p.company_id
             WHERE c.property_id = @p AND p.business_date BETWEEN @from AND @to ORDER BY p.payment_id;
            SELECT s.settle_date, s.settlement_id, r.customer_name, r.receivable_id, s.mode, s.amount_paise, s.recorded_by
              FROM ops.udhaar_settlement s JOIN ops.udhaar_receivable r ON r.receivable_id = s.receivable_id
             WHERE r.property_id = @p AND s.settle_date BETWEEN @from AND @to ORDER BY s.settlement_id;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<CollectionFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CollectionFact(D(rd, 0), MoneyRules.ReceiptNo(rd.GetInt64(1)), S(rd, 5), S(rd, 6), S(rd, 7), rd.GetString(3), rd.GetInt64(4), 0, rd.GetInt32(8), "stay", rd.GetString(2)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var date = D(rd, 0);
            if (date >= from && date <= to)
                list.Add(new CollectionFact(date, S(rd, 1), S(rd, 2), string.Empty, S(rd, 1), S(rd, 4), rd.GetInt64(3), 0, rd.GetInt32(5), "counter bill", "counter"));
            if (!rd.IsDBNull(7) && D(rd, 7) is var cd && cd >= from && cd <= to && S(rd, 6) == "Credited")
                list.Add(new CollectionFact(cd, S(rd, 8), S(rd, 2), string.Empty, S(rd, 1), S(rd, 4), -rd.GetInt64(3), 0, rd.GetInt32(5), "counter bill", "counter-credit"));
        }

        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CollectionFact(D(rd, 0), "GCP-" + rd.GetInt64(1), S(rd, 2), string.Empty, S(rd, 4), S(rd, 3), rd.GetInt64(5), rd.GetInt64(6), rd.GetInt32(7), "credit settlement", "company-payment"));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CollectionFact(D(rd, 0), "UDP-" + rd.GetInt64(1), S(rd, 2), string.Empty, "UD-" + rd.GetInt64(3), S(rd, 4), rd.GetInt64(5), 0, rd.GetInt32(6), "credit settlement", "udhaar-payment"));
        return list;
    }

    public async Task<(IReadOnlyList<CreditFact> Credit, IReadOnlyList<CreditPaymentFact> Payments)> CreditAsync(int propertyId, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT x.business_date, x.receipt_id, x.kind, x.amount_paise, r.guest_name,
                   ISNULL((SELECT TOP 1 rm.room_no FROM inv.room_night n JOIN inv.room rm ON rm.room_id = n.room_id WHERE n.reservation_id = r.reservation_id ORDER BY n.night, rm.room_no), ''),
                   ISNULL((SELECT TOP 1 i.invoice_no FROM ivc.invoice i WHERE i.property_id = @p AND i.source_kind = 'folio' AND i.source_id = x.folio_id ORDER BY i.invoice_id DESC), r.public_no),
                   c.company_id, c.name, ISNULL(c.credit_limit_paise, 0), g.guest_id, ISNULL(a.limit_paise, 0), a.customer_key
              FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              LEFT JOIN grp.company_account c ON c.company_id = COALESCE(x.company_id, r.company_id)
              LEFT JOIN gst.stay st ON st.reservation_id = r.reservation_id
              LEFT JOIN gst.guest g ON g.guest_id = st.guest_id
              LEFT JOIN gst.guest_credit_account a ON a.guest_id = g.guest_id
             WHERE r.property_id = @p AND x.kind IN ('company-credit','guest-credit') AND x.business_date <= @to ORDER BY x.business_date, x.receipt_id;
            SELECT b.business_date, b.bill_id, b.invoice_no, b.party_name, b.total_paise, b.company_id, ISNULL(c.name, b.party_name), ISNULL(c.credit_limit_paise, 0)
              FROM ctr.bill b LEFT JOIN grp.company_account c ON c.company_id = b.company_id
             WHERE b.property_id = @p AND b.settlement_mode = 'company' AND b.status = 'Issued' AND b.business_date <= @to ORDER BY b.business_date, b.bill_id;
            SELECT p.company_id, p.business_date, p.amount_paise + p.tds_paise
              FROM grp.company_payment p JOIN grp.company_account c ON c.company_id = p.company_id WHERE c.property_id = @p AND p.business_date <= @to;
            SELECT a.customer_key, s.settle_date, s.amount_paise
              FROM ops.udhaar_settlement s JOIN ops.udhaar_receivable r ON r.receivable_id = s.receivable_id
              JOIN gst.guest_credit_account a ON a.property_id = r.property_id AND a.customer_key = r.mobile
             WHERE r.property_id = @p AND r.is_void = 0 AND s.settle_date <= @to;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        var credit = new List<CreditFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var company = rd.GetString(2) == "company-credit";
            var key = company ? "C:" + (rd.IsDBNull(7) ? S(rd, 4) : rd.GetInt64(7).ToString(System.Globalization.CultureInfo.InvariantCulture))
                              : "G:" + (rd.IsDBNull(12) ? (rd.IsDBNull(10) ? S(rd, 4) : rd.GetInt64(10).ToString(System.Globalization.CultureInfo.InvariantCulture)) : S(rd, 12));
            credit.Add(new CreditFact(D(rd, 0), credit.Count, S(rd, 6), company ? (rd.IsDBNull(8) ? S(rd, 4) : rd.GetString(8)) : S(rd, 4), S(rd, 5), rd.GetInt64(3),
                company ? rd.GetInt64(9) : rd.GetInt64(11), key));
        }

        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            credit.Add(new CreditFact(D(rd, 0), credit.Count, S(rd, 2), S(rd, 6), string.Empty, rd.GetInt64(4), rd.GetInt64(7), "C:" + (rd.IsDBNull(5) ? S(rd, 3) : rd.GetInt64(5).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var pays = new List<CreditPaymentFact>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) pays.Add(new CreditPaymentFact("C:" + rd.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture), D(rd, 1), rd.GetInt64(2)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) pays.Add(new CreditPaymentFact("G:" + rd.GetString(0), D(rd, 1), rd.GetInt64(2)));
        return (credit, pays);
    }
}
