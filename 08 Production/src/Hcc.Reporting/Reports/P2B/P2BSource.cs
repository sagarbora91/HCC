// F8-RPT-P2-B · reads of the batch-B money reports that the existing queries do not give: the count grid of a shift, counter bills with their lines, refunds with
// reason and approver, discount lines. Plain SELECTs on the tables the commands write; no new tables. Facts are plain records so the unit tests drive the
// builders without a database. Prototype twins: 03 Prototype/src/domain/reports/p2-b/*.ts.
using System.Data;
using System.Text.Json;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.P2B;

/// <summary>The note grid of the latest count of a shift (notes by rupee value, coins in paise); null grid = the count kept no breakdown.</summary>
public sealed record CountGridFact(long ShiftId, IReadOnlyDictionary<int, int>? Notes, long? CoinsPaise);

public sealed record CounterLineFact(string Label, int Qty, long UnitPaise, long AmountPaise);

/// <summary>A counter bill: <see cref="NetPaise"/> is the net after the bill discount, <see cref="Tender"/> the settlement text (method, Udhaar, Company credit) with the credit note when credited.</summary>
public sealed record CounterBillFact(DateOnly Date, string No, string Customer, string Tender, long NetPaise, long TaxPaise, long DiscountPaise, string? DiscountReason,
    IReadOnlyList<CounterLineFact> Lines);

public sealed record RefundFact(DateOnly Date, string RefundNo, string Guest, string BillNo, long AmountPaise, string Tender, string Reason, int ApprovedBy);

public sealed record DiscountFact(DateOnly Date, string Ref, string Guest, string Reason, long AmountPaise, int ApprovedBy);

public interface IP2BReportSource
{
    Task<IReadOnlyList<CountGridFact>> CountGridsAsync(int propertyId, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<CounterBillFact>> CounterBillsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<RefundFact>> RefundsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<DiscountFact>> DiscountsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed class SqlP2BReportSource(SqlConnectionFactory connections) : IP2BReportSource
{
    private static void Add(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static DateOnly D(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);

    private static void Range(SqlCommand c, int p, DateOnly from, DateOnly to)
    {
        Add(c, "@p", SqlDbType.Int, p); Add(c, "@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)); Add(c, "@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
    }

    public static CountGridFact ParseGrid(long shiftId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new CountGridFact(shiftId, null, null);
        using var doc = JsonDocument.Parse(json);
        var notes = new Dictionary<int, int>();
        if (doc.RootElement.TryGetProperty("notes", out var n))
            foreach (var p in n.EnumerateObject())
                if (int.TryParse(p.Name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rupees)) notes[rupees] = p.Value.GetInt32();
        long? coins = doc.RootElement.TryGetProperty("coinsPaise", out var c) ? c.GetInt64() : null;
        return new CountGridFact(shiftId, notes, coins);
    }

    public async Task<IReadOnlyList<CountGridFact>> CountGridsAsync(int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.shift_id, c.denominations
              FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
              JOIN pay.shift_count c ON c.count_id = (SELECT MAX(x.count_id) FROM pay.shift_count x WHERE x.shift_id = s.shift_id)
             WHERE t.property_id = @p AND s.business_date = @from;
            """, conn);
        Range(cmd, propertyId, date, date);
        var list = new List<CountGridFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ParseGrid(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetString(1)));
        return list;
    }

    public async Task<IReadOnlyList<CounterBillFact>> CounterBillsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT b.bill_id, b.business_date, b.invoice_no, b.party_name, b.settlement_mode, b.method, b.status, b.credit_note_no, b.net_paise, b.tax_paise, b.discount_paise, b.discount_reason
              FROM ctr.bill b WHERE b.property_id = @p AND b.business_date BETWEEN @from AND @to ORDER BY b.business_date, b.invoice_no;
            SELECT l.bill_id, l.label, l.qty, l.unit_paise, l.amount_paise
              FROM ctr.bill_line l JOIN ctr.bill b ON b.bill_id = l.bill_id WHERE b.property_id = @p AND b.business_date BETWEEN @from AND @to ORDER BY l.bill_id, l.seq;
            """, conn);
        Range(cmd, propertyId, from, to);
        var heads = new List<(long Id, DateOnly Date, string No, string Party, string Mode, string Method, string Status, string Cn, long Net, long Tax, long Disc, string? Reason)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), D(rd, 1), rd.GetString(2), rd.GetString(3), rd.GetString(4), S(rd, 5), rd.GetString(6), S(rd, 7), rd.GetInt64(8), rd.GetInt64(9), rd.GetInt64(10),
                rd.IsDBNull(11) ? null : rd.GetString(11)));
        var lines = new Dictionary<long, List<CounterLineFact>>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!lines.TryGetValue(rd.GetInt64(0), out var l)) lines[rd.GetInt64(0)] = l = [];
            l.Add(new CounterLineFact(rd.GetString(1), rd.GetInt32(2), rd.GetInt64(3), rd.GetInt64(4)));
        }

        return [.. heads.Select(h => new CounterBillFact(h.Date, h.No, h.Party, Tender(h.Mode, h.Method, h.Status, h.Cn), h.Net, h.Tax, h.Disc, h.Reason,
            lines.TryGetValue(h.Id, out var ls) ? ls : []))];
    }

    /// <summary>Same wording as the prototype's ctrTender.</summary>
    public static string Tender(string mode, string method, string status, string creditNoteNo) =>
        (mode == "paid" ? (method.Length > 0 ? method : "Paid") : mode == "udhaar" ? "Udhaar (credit)" : "Company credit") + (status == "Credited" ? " · credited " + creditNoteNo : string.Empty);

    public async Task<IReadOnlyList<RefundFact>> RefundsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT x.business_date, x.receipt_id, r.guest_name,
                   ISNULL((SELECT TOP 1 i.invoice_no FROM ivc.invoice i WHERE i.property_id = @p AND i.source_kind = 'folio' AND i.source_id = x.folio_id ORDER BY i.invoice_id DESC), r.public_no),
                   x.amount_paise, x.method, a.reason, a.decided_by
              FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              LEFT JOIN bill.approval a ON a.approval_id = x.approval_id
             WHERE r.property_id = @p AND x.kind = 'refund' AND x.business_date BETWEEN @from AND @to ORDER BY x.business_date, x.receipt_id;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<RefundFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RefundFact(D(rd, 0), Money.MoneyRules.ReceiptNo(rd.GetInt64(1)), S(rd, 2), S(rd, 3), rd.GetInt64(4), rd.GetString(5), S(rd, 6), rd.IsDBNull(7) ? 0 : rd.GetInt32(7)));
        return list;
    }

    public async Task<IReadOnlyList<DiscountFact>> DiscountsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT c.service_date, r.public_no, r.guest_name, c.label, -c.amount_paise, a.decided_by, dr.name
              FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              LEFT JOIN bill.approval a ON a.approval_id = c.approval_id
              LEFT JOIN bill.approval_discount_reason adr ON adr.approval_id = a.approval_id
              LEFT JOIN bill.discount_reason dr ON dr.discount_reason_id = adr.discount_reason_id
             WHERE r.property_id = @p AND c.kind = 'discount' AND c.reverses_charge_id IS NULL AND c.service_date BETWEEN @from AND @to
               AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id)
             ORDER BY c.service_date, c.charge_id;
            SELECT b.business_date, b.invoice_no, b.party_name, b.discount_reason, b.discount_paise
              FROM ctr.bill b WHERE b.property_id = @p AND b.discount_paise > 0 AND b.business_date BETWEEN @from AND @to ORDER BY b.business_date, b.bill_id;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<DiscountFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var label = S(rd, 3);
            var reason = rd.IsDBNull(6) ? (label.StartsWith("Discount · ", StringComparison.Ordinal) ? label["Discount · ".Length..] : label) : rd.GetString(6);
            list.Add(new DiscountFact(D(rd, 0), S(rd, 1), S(rd, 2), reason, rd.GetInt64(4), rd.IsDBNull(5) ? 0 : rd.GetInt32(5)));
        }

        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new DiscountFact(D(rd, 0), S(rd, 1), S(rd, 2), S(rd, 3), rd.GetInt64(4), 0));
        return list;
    }
}
