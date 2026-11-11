// F7-R5 · shared pieces of the tax reports: the invoices and credit notes of a period as one list (stored GST of each document — never recomputed),
// with room, status and the payment split read in ONE extra query; server twin of 03 Prototype/src/domain/reports/tax/shared.ts.
using System.Data;
using System.Globalization;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Invoices;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Tax;

internal sealed record TaxLine(string Description, string Sac, int Bps, long Taxable, long Cgst, long Sgst);

internal sealed record TaxEntry(string No, bool IsNote, string Series, DateOnly Date, string Party, string Gstin, string Room, IReadOnlyList<TaxLine> Lines, string Status,
    long Cash, long CardUpi, long Credit)
{
    /// <summary>A credit note's lines are the note's positive value; entries show them negative.</summary>
    public int Sign => IsNote ? -1 : 1;

    public long Taxable => Lines.Sum(l => l.Taxable) * Sign;

    public long Cgst => Lines.Sum(l => l.Cgst) * Sign;

    public long Sgst => Lines.Sum(l => l.Sgst) * Sign;

    public long Gst => Cgst + Sgst;

    public long Total => Taxable + Gst;
}

internal static class TaxSql
{
    public const string RoomSac = "996311";

    public static readonly int[] RateBps = [0, 500, 1200, 1800, 2800];

    public static int SeriesOrder(string series) => series switch { "LGC" => 0, "INV" => 1, "RST" => 2, "BOS" => 3, _ => 4 };

    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportColumn B(string key, string label) => new(key, label, ReportColumnType.Bool, false);

    public static ReportAbout About(ReportDefinition d, BoundParams p) => new(DateTimeOffset.MinValue, "", d.DateBasis, false, p.Legacy, p.From, p.To);

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) =>
        new(kind, cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal));

    private sealed record Extra(string Room, bool Corrected, bool Cancelled, long Cash, long CardUpi, long Credit);

    // CU-PAY-B: "cash" = a payment method that settles to a till (mst.payment_method -> mst.bank_account kind 'till'), by its current name or an earlier name
    // (mst.master_history); a property without a methods list keeps the literal Cash. Records store the method NAME, which changes when the Owner renames the row.
    private const string ExtraSql = """
        WITH cash_names(n) AS (
            SELECT m.name FROM mst.payment_method m JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id AND a.kind = 'till' WHERE m.property_id = @p
            UNION SELECT h.old_value FROM mst.master_history h JOIN mst.payment_method m ON m.payment_method_id = h.row_id JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id AND a.kind = 'till'
                   WHERE h.property_id = @p AND h.master = 'paymentMethods' AND h.field = 'name' AND h.old_value IS NOT NULL
            UNION SELECT N'Cash' WHERE NOT EXISTS (SELECT 1 FROM mst.payment_method WHERE property_id = @p))
        SELECT i.invoice_id,
               CASE WHEN i.source_kind = 'folio' THEN ISNULL((SELECT TOP (1) rm.room_no FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id
                                                              JOIN inv.room rm ON rm.room_id = r.room_id WHERE f.folio_id = i.source_id), '') ELSE '' END,
               CASE WHEN EXISTS (SELECT 1 FROM ivc.bill_correction c WHERE c.old_invoice_id = i.invoice_id AND c.status = 'Issued') THEN 1 ELSE 0 END,
               CASE WHEN (i.source_kind = 'pos' AND EXISTS (SELECT 1 FROM pos.bill b WHERE b.bill_id = i.source_id AND b.status = 'Void'))
                      OR (i.source_kind = 'counter' AND EXISTS (SELECT 1 FROM ctr.bill b WHERE b.invoice_id = i.invoice_id AND b.status = 'Credited')) THEN 1 ELSE 0 END,
               CASE i.source_kind
                 WHEN 'folio' THEN CASE WHEN i.revision = (SELECT MAX(x.revision) FROM ivc.invoice x WHERE x.property_id = i.property_id AND x.source_kind = i.source_kind AND x.source_id = i.source_id)
                      THEN ISNULL((SELECT SUM(CASE WHEN cn.n IS NOT NULL AND r.kind IN ('payment','deposit-apply') THEN r.amount_paise WHEN cn.n IS NOT NULL AND r.kind = 'refund' THEN -r.amount_paise ELSE 0 END)
                                     FROM pay.receipt r LEFT JOIN cash_names cn ON cn.n = r.method WHERE r.folio_id = i.source_id), 0) ELSE 0 END
                 WHEN 'pos' THEN ISNULL((SELECT SUM(CASE WHEN cn.n IS NOT NULL THEN b.total_paise ELSE 0 END) FROM pos.bill b LEFT JOIN cash_names cn ON cn.n = b.tender WHERE b.bill_id = i.source_id), 0)
                 WHEN 'counter' THEN ISNULL((SELECT SUM(CASE WHEN b.settlement_mode = 'paid' AND cn.n IS NOT NULL THEN b.total_paise ELSE 0 END) FROM ctr.bill b LEFT JOIN cash_names cn ON cn.n = b.method WHERE b.invoice_id = i.invoice_id), 0)
                 ELSE 0 END,
               CASE i.source_kind
                 WHEN 'folio' THEN CASE WHEN i.revision = (SELECT MAX(x.revision) FROM ivc.invoice x WHERE x.property_id = i.property_id AND x.source_kind = i.source_kind AND x.source_id = i.source_id)
                      THEN ISNULL((SELECT SUM(CASE WHEN (cn.n IS NULL AND r.method IS NOT NULL) AND r.kind IN ('payment','deposit-apply') THEN r.amount_paise WHEN (cn.n IS NULL AND r.method IS NOT NULL) AND r.kind = 'refund' THEN -r.amount_paise ELSE 0 END)
                                     FROM pay.receipt r LEFT JOIN cash_names cn ON cn.n = r.method WHERE r.folio_id = i.source_id), 0) ELSE 0 END
                 WHEN 'pos' THEN ISNULL((SELECT SUM(CASE WHEN (cn.n IS NULL AND b.tender IS NOT NULL) THEN b.total_paise ELSE 0 END) FROM pos.bill b LEFT JOIN cash_names cn ON cn.n = b.tender WHERE b.bill_id = i.source_id), 0)
                 WHEN 'counter' THEN ISNULL((SELECT SUM(CASE WHEN b.settlement_mode = 'paid' AND (cn.n IS NULL AND b.method IS NOT NULL) THEN b.total_paise ELSE 0 END) FROM ctr.bill b LEFT JOIN cash_names cn ON cn.n = b.method WHERE b.invoice_id = i.invoice_id), 0)
                 ELSE 0 END,
               CASE i.source_kind
                 WHEN 'folio' THEN CASE WHEN i.revision = (SELECT MAX(x.revision) FROM ivc.invoice x WHERE x.property_id = i.property_id AND x.source_kind = i.source_kind AND x.source_id = i.source_id)
                      THEN ISNULL((SELECT SUM(CASE WHEN r.kind IN ('company-credit','ota-credit','guest-credit') THEN r.amount_paise ELSE 0 END) FROM pay.receipt r WHERE r.folio_id = i.source_id), 0) ELSE 0 END
                 WHEN 'counter' THEN ISNULL((SELECT SUM(CASE WHEN b.settlement_mode <> 'paid' THEN b.total_paise ELSE 0 END) FROM ctr.bill b WHERE b.invoice_id = i.invoice_id), 0)
                 ELSE 0 END
          FROM ivc.invoice i
         WHERE i.property_id = @p AND i.invoice_date BETWEEN @from AND @to;
        """;

    private static async Task<Dictionary<long, Extra>> ExtrasAsync(SqlConnectionFactory connections, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var map = new Dictionary<long, Extra>();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(ExtraSql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            map[rd.GetInt64(0)] = new Extra(rd.GetString(1), rd.GetInt32(2) == 1, rd.GetInt32(3) == 1, Convert.ToInt64(rd.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToInt64(rd.GetValue(5), CultureInfo.InvariantCulture), Convert.ToInt64(rd.GetValue(6), CultureInfo.InvariantCulture));
        return map;
    }

    /// <summary>Every invoice and credit note dated from…to (invoices then notes).</summary>
    public static async Task<IReadOnlyList<TaxEntry>> EntriesAsync(IInvoiceStore store, SqlConnectionFactory connections, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var invoices = await store.InvoicesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var notes = await store.CreditNotesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var extras = await ExtrasAsync(connections, propertyId, from, to, ct).ConfigureAwait(false);
        var credited = notes.GroupBy(n => n.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(n => n.Figures.Total.Value));
        var list = new List<TaxEntry>();
        foreach (var i in invoices)
        {
            var x = extras.GetValueOrDefault(i.InvoiceId) ?? new Extra("", false, false, 0, 0, 0);
            var series = InvoiceNumbering.Code(i.DocType);
            // a restaurant invoice reversed in full by its credit note counts as cancelled (as Gstr1 table 13)
            var cancelled = x.Cancelled || (i.SourceKind == PosInvoiceSource.PosKind && credited.TryGetValue(i.InvoiceId, out var c) && c == i.Figures.Total.Value);
            var lines = i.Lines.Select(l => new TaxLine(l.Description, l.Sac, l.Bps, l.Taxable.Value, l.Cgst.Value, l.Sgst.Value)).ToList();
            list.Add(new TaxEntry(i.InvoiceNo, false, series, i.InvoiceDate, i.Recipient.Name ?? "", i.Recipient.Gstin ?? "", x.Room, lines,
                cancelled ? "Cancelled" : x.Corrected ? "Corrected" : "Issued", x.Cash, x.CardUpi, x.Credit));
        }

        var originals = new Dictionary<long, InvoiceRecord>();
        foreach (var n in notes)
        {
            if (!originals.TryGetValue(n.InvoiceId, out var o))
                originals[n.InvoiceId] = o = (await store.ReadInvoiceAsync(null, propertyId, n.InvoiceId, ct).ConfigureAwait(false))!;
            var room = extras.TryGetValue(n.InvoiceId, out var ox) ? ox.Room : (await RoomOfAsync(connections, n.InvoiceId, ct).ConfigureAwait(false));
            var lines = n.Lines.Select(l => new TaxLine("Credit", l.Sac, l.Bps, l.Taxable.Value, l.Cgst.Value, l.Sgst.Value)).ToList();
            list.Add(new TaxEntry(n.CreditNoteNo, true, "CRN", n.NoteDate, o.Recipient.Name ?? "", o.Recipient.Gstin ?? "", room, lines, "Issued", 0, 0, 0));
        }

        return list;
    }

    private static async Task<string> RoomOfAsync(SqlConnectionFactory connections, long invoiceId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT ISNULL((SELECT TOP (1) rm.room_no FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id JOIN inv.room rm ON rm.room_id = r.room_id
                            WHERE f.folio_id = i.source_id), '') FROM ivc.invoice i WHERE i.invoice_id = @i AND i.source_kind = 'folio';
            """, conn);
        cmd.Parameters.Add("@i", SqlDbType.BigInt).Value = invoiceId;
        return (await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) as string ?? "";
    }
}
