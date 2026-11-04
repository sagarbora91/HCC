using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.LfMoney;

/// <summary>SQL adapter of <see cref="IBillCorrectionStore"/> (LF A2: 4680 revision, 4681 ivc.invoice_supersession, 4682 ivc.bill_correction + changes).</summary>
public sealed class SqlBillCorrectionStore(SqlConnectionFactory connections) : IBillCorrectionStore, IInvoiceSupersessionReader
{
    /// <summary>LF-int: the print note (prototype bill-correction.ts blcPrintNote parity).</summary>
    public async Task<string?> NoteAsync(int propertyId, long invoiceId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT (SELECT TOP (1) o.invoice_no FROM ivc.invoice_supersession s JOIN ivc.invoice o ON o.invoice_id = s.invoice_id
                     WHERE s.superseded_by_invoice_id = @i ORDER BY s.utc DESC),
                   (SELECT TOP (1) n.invoice_no FROM ivc.invoice_supersession s JOIN ivc.invoice n ON n.invoice_id = s.superseded_by_invoice_id
                     WHERE s.invoice_id = @i ORDER BY s.utc DESC),
                   (SELECT TOP (1) bc.credit_note_no FROM ivc.invoice_supersession s JOIN ivc.bill_correction bc ON bc.correction_id = s.correction_id
                     WHERE s.invoice_id = @i ORDER BY s.utc DESC)
            WHERE EXISTS (SELECT 1 FROM ivc.invoice WHERE invoice_id = @i AND property_id = @p);
            """, conn);
        P(cmd, "@i", SqlDbType.BigInt, invoiceId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        var parts = new List<string>();
        if (!r.IsDBNull(0)) parts.Add("Supersedes " + r.GetString(0));
        if (!r.IsDBNull(1)) parts.Add("Superseded by " + r.GetString(1) + (r.IsDBNull(2) ? "" : $" (credit note {r.GetString(2)})"));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The bill-correction store needs the SQL command transaction.")).Command(sql);

    private static void P(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static object? D(DateOnly? d) => d is { } x ? x.ToDateTime(TimeOnly.MinValue) : null;

    public async Task<long?> InvoiceIdByNoAsync(ICommandTransaction tx, int propertyId, string invoiceNo, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT invoice_id FROM ivc.invoice WHERE property_id = @p AND invoice_no = @n;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@n", SqlDbType.VarChar, invoiceNo);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }

    public async Task<bool> SupersededAsync(ICommandTransaction tx, long invoiceId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM ivc.invoice_supersession WHERE invoice_id = @i;");
        P(cmd, "@i", SqlDbType.BigInt, invoiceId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<bool> PendingForFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT COUNT(*) FROM ivc.bill_correction c WITH (UPDLOCK, HOLDLOCK) JOIN bill.approval a ON a.approval_id = c.approval_id
            WHERE c.folio_id = @f AND c.status = 'Pending' AND a.expires_utc > SYSUTCDATETIME();
            """);
        P(cmd, "@f", SqlDbType.BigInt, folioId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, BillCorrectionRow r, DateTime utc, IReadOnlyList<StoredBillChange> changes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(changes);
        long id;
        await using (var cmd = Cmd(tx, """
            INSERT ivc.bill_correction(property_id, folio_id, old_invoice_id, approval_id, reason, requested_by, requested_utc, status, old_total_paise, new_total_paise, delta_paise)
            OUTPUT INSERTED.correction_id VALUES (@p, @f, @i, @a, @r, @by, @u, 'Pending', @o, @n, @d);
            """))
        {
            P(cmd, "@p", SqlDbType.Int, r.PropertyId);
            P(cmd, "@f", SqlDbType.BigInt, r.FolioId);
            P(cmd, "@i", SqlDbType.BigInt, r.OldInvoiceId);
            P(cmd, "@a", SqlDbType.BigInt, r.ApprovalId);
            P(cmd, "@r", SqlDbType.NVarChar, r.Reason);
            P(cmd, "@by", SqlDbType.Int, r.RequestedBy);
            P(cmd, "@u", SqlDbType.DateTime2, utc);
            P(cmd, "@o", SqlDbType.BigInt, r.OldTotalPaise);
            P(cmd, "@n", SqlDbType.BigInt, r.NewTotalPaise);
            P(cmd, "@d", SqlDbType.BigInt, r.DeltaPaise);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var c in changes)
        {
            await using var ins = Cmd(tx, """
                INSERT ivc.bill_correction_change(correction_id, seq, kind, night, old_rate_paise, new_rate_paise, charge_id, label, amount_paise, gst_category, service_date)
                VALUES (@c, @s, @k, @n, @or, @nr, @ch, @l, @a, @g, @sd);
                """);
            P(ins, "@c", SqlDbType.BigInt, id);
            P(ins, "@s", SqlDbType.Int, c.Seq);
            P(ins, "@k", SqlDbType.VarChar, c.Kind);
            P(ins, "@n", SqlDbType.Date, D(c.Night));
            P(ins, "@or", SqlDbType.BigInt, c.OldRatePaise);
            P(ins, "@nr", SqlDbType.BigInt, c.NewRatePaise);
            P(ins, "@ch", SqlDbType.BigInt, c.ChargeId);
            P(ins, "@l", SqlDbType.NVarChar, c.Label);
            P(ins, "@a", SqlDbType.BigInt, c.AmountPaise);
            P(ins, "@g", SqlDbType.VarChar, c.GstCategory);
            P(ins, "@sd", SqlDbType.Date, D(c.ServiceDate));
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task<(BillCorrectionRow Row, IReadOnlyList<StoredBillChange> Changes)?> ByApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct)
    {
        BillCorrectionRow row;
        await using (var cmd = Cmd(tx, """
            SELECT correction_id, property_id, folio_id, old_invoice_id, approval_id, reason, requested_by, status, old_total_paise, new_total_paise, delta_paise
            FROM ivc.bill_correction WITH (UPDLOCK) WHERE approval_id = @a;
            """))
        {
            P(cmd, "@a", SqlDbType.BigInt, approvalId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            row = new BillCorrectionRow(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetInt64(4), rd.GetString(5), rd.GetInt32(6), rd.GetString(7),
                rd.GetInt64(8), rd.GetInt64(9), rd.GetInt64(10));
        }

        var list = new List<StoredBillChange>();
        await using (var cmd = Cmd(tx, """
            SELECT seq, kind, night, old_rate_paise, new_rate_paise, charge_id, label, amount_paise, gst_category, service_date
            FROM ivc.bill_correction_change WHERE correction_id = @c ORDER BY seq;
            """))
        {
            P(cmd, "@c", SqlDbType.BigInt, row.CorrectionId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            static DateOnly? Dt(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
            static long? L(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
            static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new StoredBillChange(rd.GetInt32(0), rd.GetString(1), Dt(rd, 2), L(rd, 3), L(rd, 4), L(rd, 5), S(rd, 6), L(rd, 7), S(rd, 8), Dt(rd, 9)));
        }

        return (row, list);
    }

    public async Task DecideAsync(ICommandTransaction tx, long correctionId, string status, int decidedBy, DateTime utc, string? creditNoteNo, long? newInvoiceId, string? settlement,
        CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE ivc.bill_correction SET status = @s, approved_by = CASE WHEN @s = 'Issued' THEN @by ELSE approved_by END, decided_utc = @u,
              credit_note_no = @cn, new_invoice_id = @ni, settlement = @st WHERE correction_id = @c;
            """);
        P(cmd, "@s", SqlDbType.VarChar, status);
        P(cmd, "@by", SqlDbType.Int, decidedBy);
        P(cmd, "@u", SqlDbType.DateTime2, utc);
        P(cmd, "@cn", SqlDbType.VarChar, creditNoteNo);
        P(cmd, "@ni", SqlDbType.BigInt, newInvoiceId);
        P(cmd, "@st", SqlDbType.VarChar, settlement);
        P(cmd, "@c", SqlDbType.BigInt, correctionId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertSupersessionAsync(ICommandTransaction tx, long oldInvoiceId, long newInvoiceId, long correctionId, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "INSERT ivc.invoice_supersession(invoice_id, superseded_by_invoice_id, correction_id, utc) VALUES (@o, @n, @c, @u);");
        P(cmd, "@o", SqlDbType.BigInt, oldInvoiceId);
        P(cmd, "@n", SqlDbType.BigInt, newInvoiceId);
        P(cmd, "@c", SqlDbType.BigInt, correctionId);
        P(cmd, "@u", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BillCorrectionListRow>> ListAsync(int propertyId, CancellationToken ct)
    {
        // AF-CTL02 (CTL-02): one batch, one id set — the register rows and their changes come from the same fixed set of corrections.
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            DECLARE @ids TABLE(correction_id BIGINT PRIMARY KEY);
            INSERT @ids SELECT correction_id FROM ivc.bill_correction WHERE property_id = @p;
            SELECT c.correction_id, o.invoice_no, c.credit_note_no, n.invoice_no, c.status, c.old_total_paise, c.new_total_paise, c.delta_paise, c.settlement, c.reason, c.requested_utc,
                   c.requested_by, c.approval_id, a.decided_by, a.decided_utc
            FROM ivc.bill_correction c JOIN @ids i ON i.correction_id = c.correction_id
            JOIN ivc.invoice o ON o.invoice_id = c.old_invoice_id LEFT JOIN ivc.invoice n ON n.invoice_id = c.new_invoice_id
            LEFT JOIN bill.approval a ON a.approval_id = c.approval_id
            ORDER BY c.correction_id DESC;
            SELECT h.correction_id, h.seq, h.kind, h.night, h.old_rate_paise, h.new_rate_paise, h.charge_id, h.label, h.amount_paise, h.gst_category, h.service_date
            FROM ivc.bill_correction_change h JOIN @ids i ON i.correction_id = h.correction_id ORDER BY h.correction_id, h.seq;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var heads = new List<(long Id, string Old, string? Cn, string? New, string Status, long OldT, long NewT, long Delta, string? Settle, string Reason, DateTime Req, int By, long Appr, int? DBy, DateTime? DUtc)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetString(4),
                rd.GetInt64(5), rd.GetInt64(6), rd.GetInt64(7), rd.IsDBNull(8) ? null : rd.GetString(8), rd.GetString(9), rd.GetDateTime(10),
                rd.GetInt32(11), rd.GetInt64(12), rd.IsDBNull(13) ? null : rd.GetInt32(13), rd.IsDBNull(14) ? null : rd.GetDateTime(14)));
        var byId = new Dictionary<long, List<StoredBillChange>>();
        if (await rd.NextResultAsync(ct).ConfigureAwait(false))
        {
            static DateOnly? Dt(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
            static long? L(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
            static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt64(0);
                if (!byId.TryGetValue(id, out var l)) byId[id] = l = [];
                l.Add(new StoredBillChange(rd.GetInt32(1), rd.GetString(2), Dt(rd, 3), L(rd, 4), L(rd, 5), L(rd, 6), S(rd, 7), L(rd, 8), S(rd, 9), Dt(rd, 10)));
            }
        }

        return heads.Select(h => new BillCorrectionListRow(h.Id, h.Old, h.Cn, h.New, h.Status, h.OldT, h.NewT, h.Delta, h.Settle, h.Reason, h.Req, h.By, h.Appr, h.DBy, h.DUtc,
            byId.TryGetValue(h.Id, out var ch) ? ch : [])).ToList();
    }
}
