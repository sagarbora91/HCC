using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Invoices;

/// <summary>
/// SQL persistence of the invoices module (schema <c>ivc</c>, migration 2400). Inside a command the command's transaction is used; queries
/// open their own read-committed connection. Reads of other modules' tables (inv.property, inv.reservation, gst.stay) are read-only.
/// </summary>
public sealed class SqlInvoiceStore(SqlConnectionFactory connections) : IInvoiceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The invoice store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateOnly D(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private static DateOnly? Dn(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : D(rd, i);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static object Date(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    /// <summary>Runs <paramref name="work"/> on the transaction's connection, or on a fresh read-committed connection when there is none.</summary>
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, string sql, Func<SqlCommand, Task<T>> work, CancellationToken ct)
    {
        if (tx is not null)
        {
            await using var cmd = Sql(tx).Command(sql);
            return await work(cmd).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        return await work(c2).ConfigureAwait(false);
    }

    public async Task<SupplierInfo> SupplierAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("SELECT name, gstin, state_name, address, business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        return new SupplierInfo(rd.GetString(0), S(rd, 1), rd.GetString(2), S(rd, 3), D(rd, 4));
    }

    public async Task<FolioParty?> FolioPartyAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT r.property_id, r.public_no, r.guest_name, COALESCE(a.room_no, b.room_no), r.arrival, r.departure
              FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              OUTER APPLY (SELECT TOP 1 rm.room_no FROM gst.stay s JOIN gst.stay_segment sg ON sg.stay_id = s.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
                            WHERE s.reservation_id = r.reservation_id ORDER BY sg.seq DESC) a
              OUTER APPLY (SELECT TOP 1 rm.room_no FROM inv.room rm WHERE rm.room_id = r.room_id) b
             WHERE f.folio_id = @f;
            """);
        P(cmd, "@f", SqlDbType.BigInt, folioId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new FolioParty(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), S(rd, 3), D(rd, 4), D(rd, 5));
    }

    public async Task<long?> InvoiceIdForSourceAsync(ICommandTransaction tx, int propertyId, string sourceKind, long sourceId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("SELECT invoice_id FROM ivc.invoice WHERE property_id = @p AND source_kind = @k AND source_id = @s;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@k", SqlDbType.VarChar, sourceKind, 10);
        P(cmd, "@s", SqlDbType.BigInt, sourceId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }

    public Task<long?> FindInvoiceForSourceAsync(int propertyId, string sourceKind, long sourceId, CancellationToken ct) =>
        WithAsync<long?>(null, "SELECT invoice_id FROM ivc.invoice WHERE property_id = @p AND source_kind = @k AND source_id = @s;", async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@k", SqlDbType.VarChar, sourceKind, 10);
            P(cmd, "@s", SqlDbType.BigInt, sourceId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
        }, ct);

    public async Task<(string Prefix, int Seq)> NextNumberAsync(ICommandTransaction tx, int propertyId, string fyCode, InvoiceDocType type, string prefixIfNew, DateTime utc, CancellationToken ct)
    {
        // An exclusive transaction-scoped app lock per series serializes numbering (also the first use of a series, where two key-range
        // locks would otherwise deadlock on the insert); then check-then-insert or a single-row advance (gap-free within the transaction).
        await using var cmd = Sql(tx).Command("""
            DECLARE @prefix VARCHAR(6), @seq INT, @lock INT;
            DECLARE @res NVARCHAR(255) = CONCAT(N'ivc.series:', @p, N':', @fy, N':', @t);
            EXEC @lock = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @lock < 0 THROW 52406, 'The numbering series is busy; try again.', 1;
            SELECT @prefix = prefix, @seq = last_seq FROM ivc.series WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND fy_code = @fy AND doc_type = @t;
            IF @prefix IS NULL
            BEGIN
              INSERT ivc.series(property_id, fy_code, doc_type, prefix, last_seq, created_utc) VALUES (@p, @fy, @t, @np, 1, @utc);
              SELECT @np, 1;
            END
            ELSE
            BEGIN
              UPDATE ivc.series SET last_seq = last_seq + 1 WHERE property_id = @p AND fy_code = @fy AND doc_type = @t;
              SELECT @prefix, @seq + 1;
            END
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@fy", SqlDbType.Char, fyCode, 4);
        P(cmd, "@t", SqlDbType.Char, InvoiceNumbering.Code(type), 3);
        P(cmd, "@np", SqlDbType.VarChar, prefixIfNew, 6);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetString(0), rd.GetInt32(1));
    }

    public async Task<long> InsertInvoiceAsync(ICommandTransaction tx, InvoiceRecord i, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(i);
        var s = Sql(tx);
        long id;
        await using (var cmd = s.Command("""
            INSERT ivc.invoice(property_id, doc_type, invoice_no, fy_code, seq, invoice_date, source_kind, source_id, source_ref, period_from, period_to,
              supply_type, buyer_name, buyer_gstin, buyer_address, place_of_supply, supplier_name, supplier_gstin, supplier_state, supplier_address,
              inter_state, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, issued_utc)
            OUTPUT INSERTED.invoice_id
            VALUES (@p, @t, @no, @fy, @seq, @d, @sk, @sid, @ref, @pf, @pt, @st, @bn, @bg, @ba, @pos, @sn, @sg, @ss, @sa, @inter, @tx, @cg, @sg2, @ig, @tot, @w, @by, @utc);
            """))
        {
            P(cmd, "@p", SqlDbType.Int, i.PropertyId);
            P(cmd, "@t", SqlDbType.Char, InvoiceNumbering.Code(i.DocType), 3);
            P(cmd, "@no", SqlDbType.VarChar, i.InvoiceNo, 16);
            P(cmd, "@fy", SqlDbType.Char, i.FyCode, 4);
            P(cmd, "@seq", SqlDbType.Int, i.Seq);
            P(cmd, "@d", SqlDbType.Date, Date(i.InvoiceDate));
            P(cmd, "@sk", SqlDbType.VarChar, i.SourceKind, 10);
            P(cmd, "@sid", SqlDbType.BigInt, i.SourceId);
            P(cmd, "@ref", SqlDbType.NVarChar, i.SourceRef, 40);
            P(cmd, "@pf", SqlDbType.Date, i.PeriodFrom is { } pf ? Date(pf) : null);
            P(cmd, "@pt", SqlDbType.Date, i.PeriodTo is { } pt ? Date(pt) : null);
            P(cmd, "@st", SqlDbType.VarChar, i.Recipient.Type.ToString(), 3);
            P(cmd, "@bn", SqlDbType.NVarChar, i.Recipient.Name, 120);
            P(cmd, "@bg", SqlDbType.Char, i.Recipient.Gstin, 15);
            P(cmd, "@ba", SqlDbType.NVarChar, i.Recipient.Address, 200);
            P(cmd, "@pos", SqlDbType.Char, i.Recipient.PlaceOfSupply, 2);
            P(cmd, "@sn", SqlDbType.NVarChar, i.SupplierName, 120);
            P(cmd, "@sg", SqlDbType.Char, i.SupplierGstin, 15);
            P(cmd, "@ss", SqlDbType.Char, i.SupplierState, 2);
            P(cmd, "@sa", SqlDbType.NVarChar, i.SupplierAddress, 200);
            P(cmd, "@inter", SqlDbType.Bit, i.Recipient.InterState);
            P(cmd, "@tx", SqlDbType.BigInt, i.Figures.Taxable.Value);
            P(cmd, "@cg", SqlDbType.BigInt, i.Figures.Cgst.Value);
            P(cmd, "@sg2", SqlDbType.BigInt, i.Figures.Sgst.Value);
            P(cmd, "@ig", SqlDbType.BigInt, i.Figures.Igst.Value);
            P(cmd, "@tot", SqlDbType.BigInt, i.Figures.Total.Value);
            P(cmd, "@w", SqlDbType.NVarChar, i.AmountWords, 300);
            P(cmd, "@by", SqlDbType.Int, i.IssuedBy);
            P(cmd, "@utc", SqlDbType.DateTime2, i.IssuedUtc);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var l in i.Lines)
        {
            await using var cmd = s.Command("""
                INSERT ivc.invoice_line(invoice_id, line_no, source_line_id, kind, service_date, description, category, sac, taxable_paise, tax_bps, tax_paise, cgst_paise, sgst_paise, igst_paise)
                VALUES (@i, @n, @src, @k, @d, @desc, @cat, @sac, @tx, @bps, @tax, @cg, @sg, @ig);
                """);
            P(cmd, "@i", SqlDbType.BigInt, id);
            P(cmd, "@n", SqlDbType.Int, l.LineNo);
            P(cmd, "@src", SqlDbType.BigInt, l.SourceLineId);
            P(cmd, "@k", SqlDbType.VarChar, l.Kind, 12);
            P(cmd, "@d", SqlDbType.Date, Date(l.ServiceDate));
            P(cmd, "@desc", SqlDbType.NVarChar, l.Description.Length > 200 ? l.Description[..200] : l.Description, 200);
            P(cmd, "@cat", SqlDbType.VarChar, GstCategories.Code(l.Category), 8);
            P(cmd, "@sac", SqlDbType.Char, l.Sac, 6);
            P(cmd, "@tx", SqlDbType.BigInt, l.Taxable.Value);
            P(cmd, "@bps", SqlDbType.Int, l.Bps);
            P(cmd, "@tax", SqlDbType.BigInt, l.Tax.Value);
            P(cmd, "@cg", SqlDbType.BigInt, l.Cgst.Value);
            P(cmd, "@sg", SqlDbType.BigInt, l.Sgst.Value);
            P(cmd, "@ig", SqlDbType.BigInt, l.Igst.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    private const string InvoiceColumns = """
        invoice_id, property_id, doc_type, invoice_no, fy_code, seq, invoice_date, source_kind, source_id, source_ref, period_from, period_to,
        supply_type, buyer_name, buyer_gstin, buyer_address, place_of_supply, supplier_name, supplier_gstin, supplier_state, supplier_address,
        inter_state, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, issued_utc
        """;

    private const string LineColumns = "l.invoice_id, l.line_no, l.source_line_id, l.kind, l.service_date, l.description, l.category, l.sac, l.taxable_paise, l.tax_bps, l.tax_paise, l.cgst_paise, l.sgst_paise, l.igst_paise";

    private static InvoiceRecord ReadHeader(SqlDataReader rd) => new(
        rd.GetInt64(0), rd.GetInt32(1), InvoiceNumbering.Parse(rd.GetString(2)), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), D(rd, 6), rd.GetString(7), rd.GetInt64(8),
        rd.GetString(9), Dn(rd, 10), Dn(rd, 11),
        new Recipient(rd.GetString(12) == "B2B" ? SupplyType.B2B : SupplyType.B2C, S(rd, 13), S(rd, 14), S(rd, 15), rd.GetString(16), rd.GetBoolean(21)),
        rd.GetString(17), rd.GetString(18), rd.GetString(19), S(rd, 20), [],
        new InvoiceFigures(new Paise(rd.GetInt64(22)), new Paise(rd.GetInt64(23)), new Paise(rd.GetInt64(24)), new Paise(rd.GetInt64(25)), new Paise(rd.GetInt64(26))),
        rd.GetString(27), rd.GetInt32(28), Utc(rd, 29));

    private static (long InvoiceId, InvoiceLine Line) ReadLine(SqlDataReader rd) => (rd.GetInt64(0), new InvoiceLine(rd.GetInt32(1), rd.IsDBNull(2) ? null : rd.GetInt64(2),
        rd.GetString(3), D(rd, 4), rd.GetString(5), GstCategories.Parse(rd.GetString(6)), rd.GetString(7), new Paise(rd.GetInt64(8)), rd.GetInt32(9),
        new Paise(rd.GetInt64(10)), new Paise(rd.GetInt64(11)), new Paise(rd.GetInt64(12)), new Paise(rd.GetInt64(13))));

    private static async Task<List<InvoiceRecord>> ReadInvoicesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var headers = new List<InvoiceRecord>();
        var lines = new List<(long, InvoiceLine)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) headers.Add(ReadHeader(rd));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) lines.Add(ReadLine(rd));
        var byInvoice = lines.GroupBy(l => l.Item1).ToDictionary(g => g.Key, g => (IReadOnlyList<InvoiceLine>)g.Select(x => x.Item2).OrderBy(x => x.LineNo).ToList());
        return headers.Select(h => h with { Lines = byInvoice.TryGetValue(h.InvoiceId, out var ls) ? ls : [] }).ToList();
    }

    public Task<InvoiceRecord?> ReadInvoiceAsync(ICommandTransaction? tx, int propertyId, long invoiceId, CancellationToken ct) =>
        WithAsync(tx, $"""
            SELECT {InvoiceColumns} FROM ivc.invoice WHERE invoice_id = @i AND property_id = @p;
            SELECT {LineColumns} FROM ivc.invoice_line l WHERE l.invoice_id = @i AND EXISTS (SELECT 1 FROM ivc.invoice x WHERE x.invoice_id = @i AND x.property_id = @p) ORDER BY line_no;
            """, async cmd =>
        {
            P(cmd, "@i", SqlDbType.BigInt, invoiceId);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            return (await ReadInvoicesAsync(cmd, ct).ConfigureAwait(false)).FirstOrDefault();
        }, ct);

    public Task<IReadOnlyList<InvoiceRecord>> InvoicesInRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        WithAsync<IReadOnlyList<InvoiceRecord>>(null, $"""
            SELECT {InvoiceColumns} FROM ivc.invoice WHERE property_id = @p AND invoice_date BETWEEN @f AND @t ORDER BY invoice_date, invoice_no;
            SELECT {LineColumns} FROM ivc.invoice_line l JOIN ivc.invoice i ON i.invoice_id = l.invoice_id
             WHERE i.property_id = @p AND i.invoice_date BETWEEN @f AND @t;
            """, async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@f", SqlDbType.Date, Date(from));
            P(cmd, "@t", SqlDbType.Date, Date(to));
            return await ReadInvoicesAsync(cmd, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyDictionary<int, (Paise Taxable, Paise Tax)>> CreditedAsync(ICommandTransaction? tx, long invoiceId, CancellationToken ct) =>
        WithAsync<IReadOnlyDictionary<int, (Paise Taxable, Paise Tax)>>(tx,
            "SELECT invoice_line_no, SUM(taxable_paise), SUM(tax_paise) FROM ivc.credit_note_line WHERE invoice_id = @i GROUP BY invoice_line_no;", async cmd =>
        {
            P(cmd, "@i", SqlDbType.BigInt, invoiceId);
            var d = new Dictionary<int, (Paise, Paise)>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) d[rd.GetInt32(0)] = (new Paise(rd.GetInt64(1)), new Paise(rd.GetInt64(2)));
            return d;
        }, ct);

    // ── credit-note requests ──

    private const string RequestColumns = """
        request_id, property_id, invoice_id, mode, amount_paise, lines_json, reason, status, direct, requested_by, requested_utc, expires_utc,
        decided_by, decision_reason, credit_note_id, row_ver
        """;

    private sealed record LineJson(int InvoiceLineNo, long TaxablePaise);

    private static CreditNoteRequestRow ReadRequest(SqlDataReader rd)
    {
        var lines = rd.IsDBNull(5) ? [] : (JsonSerializer.Deserialize<List<LineJson>>(rd.GetString(5), Json) ?? []);
        return new CreditNoteRequestRow(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), CreditNoteRules.ParseMode(rd.GetString(3)),
            rd.IsDBNull(4) ? null : new Paise(rd.GetInt64(4)), lines.Select(l => new RequestedCreditLine(l.InvoiceLineNo, new Paise(l.TaxablePaise))).ToList(),
            rd.GetString(6), rd.GetString(7), rd.GetBoolean(8), rd.GetInt32(9), Utc(rd, 10), Utc(rd, 11), rd.IsDBNull(12) ? null : rd.GetInt32(12), S(rd, 13),
            rd.IsDBNull(14) ? null : rd.GetInt64(14), SqlValues.RowVersionToWire((byte[])rd[15]));
    }

    private static async Task<CreditNoteRequestRow?> OneRequestAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(rd) : null;
    }

    public async Task<CreditNoteRequestRow?> PendingRequestAsync(ICommandTransaction tx, long invoiceId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM ivc.credit_note_request WITH (UPDLOCK) WHERE invoice_id = @i AND status = 'Pending';");
        P(cmd, "@i", SqlDbType.BigInt, invoiceId);
        return await OneRequestAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<CreditNoteRequestRow> InsertRequestAsync(ICommandTransaction tx, NewCreditNoteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Sql(tx).Command($"""
            INSERT ivc.credit_note_request(property_id, invoice_id, mode, amount_paise, lines_json, reason, status, direct, requested_by, requested_utc, expires_utc)
            OUTPUT {string.Join(", ", RequestColumns.Split(',').Select(c => "INSERTED." + c.Trim()))}
            VALUES (@p, @i, @m, @a, @l, @r, 'Pending', @d, @by, @utc, @exp);
            """);
        P(cmd, "@p", SqlDbType.Int, r.PropertyId);
        P(cmd, "@i", SqlDbType.BigInt, r.InvoiceId);
        P(cmd, "@m", SqlDbType.VarChar, CreditNoteRules.Code(r.Mode), 6);
        P(cmd, "@a", SqlDbType.BigInt, r.Amount?.Value);
        P(cmd, "@l", SqlDbType.NVarChar, r.Lines.Count == 0 ? null : JsonSerializer.Serialize(r.Lines.Select(l => new LineJson(l.InvoiceLineNo, l.Taxable.Value)), Json), 2000);
        P(cmd, "@r", SqlDbType.NVarChar, r.Reason, 200);
        P(cmd, "@d", SqlDbType.Bit, r.Direct);
        P(cmd, "@by", SqlDbType.Int, r.RequestedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, r.RequestedUtc);
        P(cmd, "@exp", SqlDbType.DateTime2, r.ExpiresUtc);
        return (await OneRequestAsync(cmd, ct).ConfigureAwait(false))!;
    }

    public async Task<CreditNoteRequestRow?> PeekRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM ivc.credit_note_request WHERE request_id = @r AND property_id = @p;");
        P(cmd, "@r", SqlDbType.BigInt, requestId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await OneRequestAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<CreditNoteRequestRow?> LockRequestAsync(ICommandTransaction tx, long requestId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM ivc.credit_note_request WITH (UPDLOCK, HOLDLOCK) WHERE request_id = @r;");
        P(cmd, "@r", SqlDbType.BigInt, requestId);
        return await OneRequestAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<string> UpdateRequestAsync(ICommandTransaction tx, long requestId, string expectedVersion, string status, int? decidedBy, DateTime? decidedUtc,
        string? decisionReason, long? creditNoteId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE ivc.credit_note_request SET status = @s, decided_by = @by, decided_utc = @utc, decision_reason = @why, credit_note_id = @cn
            OUTPUT INSERTED.row_ver WHERE request_id = @r AND row_ver = @v;
            """);
        P(cmd, "@s", SqlDbType.VarChar, status, 10);
        P(cmd, "@by", SqlDbType.Int, decidedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, decidedUtc);
        P(cmd, "@why", SqlDbType.NVarChar, decisionReason, 200);
        P(cmd, "@cn", SqlDbType.BigInt, creditNoteId);
        P(cmd, "@r", SqlDbType.BigInt, requestId);
        P(cmd, "@v", SqlDbType.Binary, SqlValues.RowVersionFromWire(expectedVersion), 8);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[]
            ?? throw new HccException(HccErrorCode.STALE_VERSION, "The credit-note request was changed by someone else. Reload and try again.");
        return SqlValues.RowVersionToWire(v);
    }

    public async Task<IReadOnlyList<CreditNoteRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RequestColumns} FROM ivc.credit_note_request WHERE property_id = @p AND (@s IS NULL OR status = @s) ORDER BY request_id DESC;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@s", SqlDbType.VarChar, status, 10);
        var list = new List<CreditNoteRequestRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRequest(rd));
        return list;
    }

    // ── credit notes ──

    public async Task<long> InsertCreditNoteAsync(ICommandTransaction tx, CreditNoteRecord n, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(n);
        var s = Sql(tx);
        long id;
        await using (var cmd = s.Command("""
            INSERT ivc.credit_note(property_id, credit_note_no, fy_code, seq, note_date, invoice_id, request_id, mode, reason, taxable_paise, cgst_paise, sgst_paise,
              igst_paise, total_paise, amount_words, issued_by, approved_by, issued_utc)
            OUTPUT INSERTED.credit_note_id
            VALUES (@p, @no, @fy, @seq, @d, @i, @r, @m, @why, @tx, @cg, @sg, @ig, @tot, @w, @by, @ap, @utc);
            """))
        {
            P(cmd, "@p", SqlDbType.Int, n.PropertyId);
            P(cmd, "@no", SqlDbType.VarChar, n.CreditNoteNo, 16);
            P(cmd, "@fy", SqlDbType.Char, n.FyCode, 4);
            P(cmd, "@seq", SqlDbType.Int, n.Seq);
            P(cmd, "@d", SqlDbType.Date, Date(n.NoteDate));
            P(cmd, "@i", SqlDbType.BigInt, n.InvoiceId);
            P(cmd, "@r", SqlDbType.BigInt, n.RequestId);
            P(cmd, "@m", SqlDbType.VarChar, CreditNoteRules.Code(n.Mode), 6);
            P(cmd, "@why", SqlDbType.NVarChar, n.Reason, 200);
            P(cmd, "@tx", SqlDbType.BigInt, n.Figures.Taxable.Value);
            P(cmd, "@cg", SqlDbType.BigInt, n.Figures.Cgst.Value);
            P(cmd, "@sg", SqlDbType.BigInt, n.Figures.Sgst.Value);
            P(cmd, "@ig", SqlDbType.BigInt, n.Figures.Igst.Value);
            P(cmd, "@tot", SqlDbType.BigInt, n.Figures.Total.Value);
            P(cmd, "@w", SqlDbType.NVarChar, n.AmountWords, 300);
            P(cmd, "@by", SqlDbType.Int, n.IssuedBy);
            P(cmd, "@ap", SqlDbType.Int, n.ApprovedBy);
            P(cmd, "@utc", SqlDbType.DateTime2, n.IssuedUtc);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        for (var k = 0; k < n.Lines.Count; k++)
        {
            var l = n.Lines[k];
            await using var cmd = s.Command("""
                INSERT ivc.credit_note_line(credit_note_id, line_no, invoice_id, invoice_line_no, category, sac, taxable_paise, tax_bps, tax_paise, cgst_paise, sgst_paise, igst_paise, folio_charge_id)
                VALUES (@c, @n, @i, @il, @cat, @sac, @tx, @bps, @tax, @cg, @sg, @ig, @ch);
                """);
            P(cmd, "@c", SqlDbType.BigInt, id);
            P(cmd, "@n", SqlDbType.Int, l.LineNo);
            P(cmd, "@i", SqlDbType.BigInt, n.InvoiceId);
            P(cmd, "@il", SqlDbType.Int, l.InvoiceLineNo);
            P(cmd, "@cat", SqlDbType.VarChar, GstCategories.Code(l.Category), 8);
            P(cmd, "@sac", SqlDbType.Char, l.Sac, 6);
            P(cmd, "@tx", SqlDbType.BigInt, l.Taxable.Value);
            P(cmd, "@bps", SqlDbType.Int, l.Bps);
            P(cmd, "@tax", SqlDbType.BigInt, l.Tax.Value);
            P(cmd, "@cg", SqlDbType.BigInt, l.Cgst.Value);
            P(cmd, "@sg", SqlDbType.BigInt, l.Sgst.Value);
            P(cmd, "@ig", SqlDbType.BigInt, l.Igst.Value);
            P(cmd, "@ch", SqlDbType.BigInt, k < n.FolioChargeIds.Count ? n.FolioChargeIds[k] : null);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    private const string NoteColumns = """
        n.credit_note_id, n.property_id, n.credit_note_no, n.fy_code, n.seq, n.note_date, n.invoice_id, n.request_id, n.mode, n.reason, n.taxable_paise, n.cgst_paise,
        n.sgst_paise, n.igst_paise, n.total_paise, n.amount_words, n.issued_by, n.approved_by, n.issued_utc
        """;

    private const string NoteLineColumns = "l.credit_note_id, l.line_no, l.invoice_line_no, l.category, l.sac, l.taxable_paise, l.tax_bps, l.tax_paise, l.cgst_paise, l.sgst_paise, l.igst_paise, l.folio_charge_id";

    private static async Task<List<CreditNoteRecord>> ReadNotesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var heads = new List<CreditNoteRecord>();
        var lines = new List<(long Note, CreditLine Line, long? Charge)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add(new CreditNoteRecord(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), D(rd, 5), rd.GetInt64(6), rd.GetInt64(7),
                CreditNoteRules.ParseMode(rd.GetString(8)), rd.GetString(9), [], [],
                new InvoiceFigures(new Paise(rd.GetInt64(10)), new Paise(rd.GetInt64(11)), new Paise(rd.GetInt64(12)), new Paise(rd.GetInt64(13)), new Paise(rd.GetInt64(14))),
                rd.GetString(15), rd.GetInt32(16), rd.GetInt32(17), Utc(rd, 18)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            lines.Add((rd.GetInt64(0), new CreditLine(rd.GetInt32(1), rd.GetInt32(2), GstCategories.Parse(rd.GetString(3)), rd.GetString(4), new Paise(rd.GetInt64(5)),
                rd.GetInt32(6), new Paise(rd.GetInt64(7)), new Paise(rd.GetInt64(8)), new Paise(rd.GetInt64(9)), new Paise(rd.GetInt64(10))), rd.IsDBNull(11) ? null : rd.GetInt64(11)));
        var by = lines.GroupBy(x => x.Note).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Line.LineNo).ToList());
        return heads.Select(h => by.TryGetValue(h.CreditNoteId, out var ls)
            ? h with { Lines = ls.Select(x => x.Line).ToList(), FolioChargeIds = ls.Select(x => x.Charge).ToList() }
            : h).ToList();
    }

    public Task<CreditNoteRecord?> ReadCreditNoteAsync(ICommandTransaction? tx, int propertyId, long creditNoteId, CancellationToken ct) =>
        WithAsync(tx, $"""
            SELECT {NoteColumns} FROM ivc.credit_note n WHERE n.credit_note_id = @c AND n.property_id = @p;
            SELECT {NoteLineColumns} FROM ivc.credit_note_line l JOIN ivc.credit_note n ON n.credit_note_id = l.credit_note_id WHERE n.credit_note_id = @c AND n.property_id = @p;
            """, async cmd =>
        {
            P(cmd, "@c", SqlDbType.BigInt, creditNoteId);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            return (await ReadNotesAsync(cmd, ct).ConfigureAwait(false)).FirstOrDefault();
        }, ct);

    public Task<IReadOnlyList<CreditNoteRecord>> CreditNotesForInvoiceAsync(long invoiceId, CancellationToken ct) =>
        WithAsync<IReadOnlyList<CreditNoteRecord>>(null, $"""
            SELECT {NoteColumns} FROM ivc.credit_note n WHERE n.invoice_id = @i ORDER BY n.credit_note_id;
            SELECT {NoteLineColumns} FROM ivc.credit_note_line l JOIN ivc.credit_note n ON n.credit_note_id = l.credit_note_id WHERE n.invoice_id = @i;
            """, async cmd =>
        {
            P(cmd, "@i", SqlDbType.BigInt, invoiceId);
            return await ReadNotesAsync(cmd, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<CreditNoteRecord>> CreditNotesInRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        WithAsync<IReadOnlyList<CreditNoteRecord>>(null, $"""
            SELECT {NoteColumns} FROM ivc.credit_note n WHERE n.property_id = @p AND n.note_date BETWEEN @f AND @t ORDER BY n.note_date, n.credit_note_no;
            SELECT {NoteLineColumns} FROM ivc.credit_note_line l JOIN ivc.credit_note n ON n.credit_note_id = l.credit_note_id
             WHERE n.property_id = @p AND n.note_date BETWEEN @f AND @t;
            """, async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@f", SqlDbType.Date, Date(from));
            P(cmd, "@t", SqlDbType.Date, Date(to));
            return await ReadNotesAsync(cmd, ct).ConfigureAwait(false);
        }, ct);
}
