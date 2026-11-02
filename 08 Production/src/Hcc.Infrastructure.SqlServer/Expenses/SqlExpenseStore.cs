using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Expenses;

/// <summary>SQL adapter of <see cref="IExpenseStore"/> (ops.expense, migrations 0800 + 4150 + 4190 ITC flag). Row lock: UPDLOCK; versioned updates (STALE_VERSION).</summary>
public sealed class SqlExpenseStore(SqlConnectionFactory connections, Hcc.Application.Documents.IDocumentObjectStore? objects = null) : IExpenseStore
{
    private const string Columns = """
        e.expense_id, e.property_id, e.vendor, e.category, e.amount_paise, e.method, e.business_date, e.status, e.submitted_by, e.notes, e.receipt, e.reason,
        e.decided_by, e.decided_utc, e.till_id, e.reversal_of, e.reversed_by, e.row_ver, e.description, e.gst_rate_bps, e.gst_paise, e.source, e.source_ref,
        e.void_reason, e.voided_by, e.voided_utc, e.created_utc,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM ops.expense_bill_photo bp WHERE bp.expense_id = e.expense_id) THEN 1 ELSE 0 END AS BIT), e.itc_allowed,
        e.gst_kind, e.rcm, e.rcm_bps, e.capital, e.asset_no, e.tds_paise, e.tds_section, e.return_of, e.cash_deduction_lost
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The expense store needs the SQL command transaction.");

    internal static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateTime? Utc(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static ExpenseRecord Read(SqlDataReader rd) => new(
        rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), SqlValues.ReadPaise(rd, 4), rd.GetString(5), DateOnly.FromDateTime(rd.GetDateTime(6)),
        rd.GetString(7), rd.GetInt32(8), Str(rd, 9), Str(rd, 10), Str(rd, 11), rd.IsDBNull(12) ? null : rd.GetInt32(12),
        Utc(rd, 13), rd.IsDBNull(14) ? null : rd.GetInt32(14),
        rd.IsDBNull(15) ? null : rd.GetInt64(15), rd.IsDBNull(16) ? null : rd.GetInt64(16), (byte[])rd[17],
        Str(rd, 18), rd.IsDBNull(19) ? null : rd.GetInt32(19), rd.IsDBNull(20) ? null : rd.GetInt64(20), Str(rd, 21), Str(rd, 22),
        Str(rd, 23), rd.IsDBNull(24) ? null : rd.GetInt32(24), Utc(rd, 25), Utc(rd, 26), rd.GetBoolean(27), rd.GetBoolean(28),
        Str(rd, 29), rd.GetBoolean(30), rd.IsDBNull(31) ? null : rd.GetInt32(31), rd.GetBoolean(32), Str(rd, 33), rd.IsDBNull(34) ? null : rd.GetInt64(34), Str(rd, 35),
        rd.IsDBNull(36) ? null : rd.GetInt64(36), // ACC-2 acc2-purchase (migration 4230)
        rd.GetBoolean(37)); // ACC-2 integration: cash_deduction_lost (B10)

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<ExpenseRecord> InsertAsync(ICommandTransaction tx, NewExpense e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Sql(tx).Command("""
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, notes, receipt, reason,
              decided_by, decided_utc, till_id, reversal_of, description, gst_rate_bps, gst_paise, source, source_ref, itc_allowed,
              gst_kind, rcm, rcm_bps, capital, tds_paise, tds_section, return_of)
            OUTPUT INSERTED.expense_id
            VALUES (@p, @v, @c, @a, @m, @d, @s, @u, @now, @n, @r, @why, @by, CASE WHEN @by IS NULL THEN NULL ELSE @now END, @till, @rev, @desc, @gr, @g, @src, @sref, @itc,
              @gk, @rcm, @rbps, @cap, @tds, @tsec, @ret);
            """);
        Add(cmd, "@p", SqlDbType.Int, e.PropertyId);
        Add(cmd, "@v", SqlDbType.NVarChar, e.Vendor, ExpenseRules.VendorMax);
        Add(cmd, "@c", SqlDbType.NVarChar, e.Category, ExpenseRules.CategoryMax);
        Add(cmd, "@a", SqlDbType.BigInt, e.Amount.Value);
        Add(cmd, "@m", SqlDbType.NVarChar, e.Method, 60);
        Add(cmd, "@d", SqlDbType.Date, e.BusinessDate.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@s", SqlDbType.VarChar, e.Status, 10);
        Add(cmd, "@u", SqlDbType.Int, e.SubmittedBy);
        Add(cmd, "@now", SqlDbType.DateTime2, e.NowUtc);
        Add(cmd, "@n", SqlDbType.NVarChar, e.Notes, ExpenseRules.NotesMax);
        Add(cmd, "@r", SqlDbType.NVarChar, e.Receipt, ExpenseRules.ReceiptMax);
        Add(cmd, "@why", SqlDbType.NVarChar, e.Reason, ExpenseRules.ReasonMax);
        Add(cmd, "@by", SqlDbType.Int, e.DecidedBy);
        Add(cmd, "@till", SqlDbType.Int, e.TillId);
        Add(cmd, "@rev", SqlDbType.BigInt, e.ReversalOf);
        Add(cmd, "@desc", SqlDbType.NVarChar, e.Description, ExpenseScc.DescriptionMax);
        Add(cmd, "@gr", SqlDbType.Int, e.GstRateBps);
        Add(cmd, "@g", SqlDbType.BigInt, e.GstPaise);
        Add(cmd, "@src", SqlDbType.VarChar, e.Source, 10);
        Add(cmd, "@sref", SqlDbType.VarChar, e.SourceRef, 60);
        Add(cmd, "@itc", SqlDbType.Bit, e.ItcAllowed && (e.GstPaise is not null || e.Rcm));
        Add(cmd, "@gk", SqlDbType.VarChar, e.GstKind, 5);
        Add(cmd, "@rcm", SqlDbType.Bit, e.Rcm);
        Add(cmd, "@rbps", SqlDbType.Int, e.Rcm ? e.RcmBps ?? 0 : null);
        Add(cmd, "@cap", SqlDbType.Bit, e.Capital);
        Add(cmd, "@tds", SqlDbType.BigInt, e.TdsPaise is 0 ? null : e.TdsPaise);
        Add(cmd, "@tsec", SqlDbType.VarChar, e.TdsSection, 20);
        Add(cmd, "@ret", SqlDbType.BigInt, e.ReturnOf);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        return (await LockAsync(tx, e.PropertyId, id, ct).ConfigureAwait(false))!;
    }

    public async Task<ExpenseRecord?> LockAsync(ICommandTransaction tx, int propertyId, long expenseId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {Columns} FROM ops.expense e WITH (UPDLOCK, ROWLOCK) WHERE e.expense_id = @id AND e.property_id = @p;");
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Read(rd) : null;
    }

    public async Task<ExpenseRecord> UpdateAsync(ICommandTransaction tx, ExpenseRecord updated, byte[] expectedVersion, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.expense SET status = @s, business_date = @d, reason = @why, decided_by = @by, decided_utc = @at, till_id = @till, reversed_by = @rb,
              amount_paise = @a, category = @c, method = @m, receipt = @r, description = @desc, gst_rate_bps = @gr, gst_paise = @g,
              void_reason = @vr, voided_by = @vb, voided_utc = @vu, itc_allowed = @itc,
              gst_kind = @gk, rcm = @rcm, rcm_bps = @rbps, capital = @cap, asset_no = @asset, tds_paise = @tds, tds_section = @tsec
            WHERE expense_id = @id AND row_ver = @v;
            """);
        Add(cmd, "@s", SqlDbType.VarChar, updated.Status, 10);
        Add(cmd, "@d", SqlDbType.Date, updated.BusinessDate.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@why", SqlDbType.NVarChar, updated.Reason, ExpenseRules.ReasonMax);
        Add(cmd, "@by", SqlDbType.Int, updated.DecidedBy);
        Add(cmd, "@at", SqlDbType.DateTime2, updated.DecidedBy is null ? null : updated.DecidedUtc ?? nowUtc);
        Add(cmd, "@till", SqlDbType.Int, updated.TillId);
        Add(cmd, "@rb", SqlDbType.BigInt, updated.ReversedBy);
        Add(cmd, "@a", SqlDbType.BigInt, updated.Amount.Value);
        Add(cmd, "@c", SqlDbType.NVarChar, updated.Category, ExpenseRules.CategoryMax);
        Add(cmd, "@m", SqlDbType.NVarChar, updated.Method, 60);
        Add(cmd, "@r", SqlDbType.NVarChar, updated.Receipt, ExpenseRules.ReceiptMax);
        Add(cmd, "@desc", SqlDbType.NVarChar, updated.Description, ExpenseScc.DescriptionMax);
        Add(cmd, "@gr", SqlDbType.Int, updated.GstRateBps);
        Add(cmd, "@g", SqlDbType.BigInt, updated.GstPaise);
        Add(cmd, "@vr", SqlDbType.NVarChar, updated.VoidReason, ExpenseRules.ReasonMax);
        Add(cmd, "@vb", SqlDbType.Int, updated.VoidedBy);
        Add(cmd, "@vu", SqlDbType.DateTime2, updated.VoidedBy is null ? null : updated.VoidedUtc ?? nowUtc);
        Add(cmd, "@itc", SqlDbType.Bit, updated.ItcAllowed && (updated.GstPaise is not null || updated.Rcm));
        Add(cmd, "@gk", SqlDbType.VarChar, updated.GstKind, 5);
        Add(cmd, "@rcm", SqlDbType.Bit, updated.Rcm);
        Add(cmd, "@rbps", SqlDbType.Int, updated.Rcm ? updated.RcmBps ?? 0 : null);
        Add(cmd, "@cap", SqlDbType.Bit, updated.Capital);
        Add(cmd, "@asset", SqlDbType.VarChar, updated.AssetNo, 20);
        Add(cmd, "@tds", SqlDbType.BigInt, updated.TdsPaise is 0 ? null : updated.TdsPaise);
        Add(cmd, "@tsec", SqlDbType.VarChar, updated.TdsSection, 20);
        Add(cmd, "@id", SqlDbType.BigInt, updated.ExpenseId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This expense");
        return (await LockAsync(tx, updated.PropertyId, updated.ExpenseId, ct).ConfigureAwait(false))!;
    }

    private const string FactSql = "SELECT expense_id, vendor, amount_paise, business_date, method, status, reversal_of, description FROM ops.expense WHERE property_id = @p AND business_date = @d;";

    private static async Task<IReadOnlyList<ExpenseFact>> ReadFactsAsync(SqlCommand cmd, int propertyId, DateOnly date, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        var list = new List<ExpenseFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ExpenseFact(rd.GetInt64(0), rd.GetString(1), SqlValues.ReadPaise(rd, 2), DateOnly.FromDateTime(rd.GetDateTime(3)), rd.GetString(4), rd.GetString(5),
                !rd.IsDBNull(6), Str(rd, 7)));
        return list;
    }

    public async Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(FactSql);
        return await ReadFactsAsync(cmd, propertyId, date, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(FactSql, conn);
        return await ReadFactsAsync(cmd, propertyId, date, ct).ConfigureAwait(false);
    }

    public async Task PutBillPhotoAsync(ICommandTransaction tx, long expenseId, string contentType, byte[] image, int by, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        Guid? newRef = null, oldRef = null;
        if (objects is not null)
        {
            // LF-docdb (contract §1.8): the object first, in its own committed transaction; this Hcc transaction stores the ref.
            int propertyId;
            await using (var q = Sql(tx).Command("SELECT e.property_id, (SELECT object_ref FROM ops.expense_bill_photo WHERE expense_id = @id) FROM ops.expense e WHERE e.expense_id = @id;"))
            {
                Add(q, "@id", SqlDbType.BigInt, expenseId);
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException("The expense to attach a bill photo to was not found.");
                propertyId = r.GetInt32(0);
                oldRef = r.IsDBNull(1) ? null : r.GetGuid(1);
            }

            newRef = await objects.PutAsync(new Hcc.Application.Documents.NewDocumentObject(propertyId, Hcc.Application.Documents.DocumentObjectKind.ExpenseBill, "ops.expense", expenseId,
                contentType, image, null, by), ct).ConfigureAwait(false);
        }

        await using var cmd = Sql(tx).Command("""
            UPDATE ops.expense_bill_photo SET content_type = @t, size_bytes = @n, image = @i, object_ref = @ref, updated_by = @by, updated_utc = @now WHERE expense_id = @id;
            IF @@ROWCOUNT = 0 INSERT ops.expense_bill_photo(expense_id, content_type, size_bytes, image, object_ref, updated_by, updated_utc) VALUES (@id, @t, @n, @i, @ref, @by, @now);
            """);
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@t", SqlDbType.VarChar, contentType, 10);
        Add(cmd, "@n", SqlDbType.Int, image.Length);
        cmd.Parameters.Add("@i", SqlDbType.VarBinary, -1).Value = newRef is null ? image : DBNull.Value;
        cmd.Parameters.Add("@ref", SqlDbType.UniqueIdentifier).Value = newRef is null ? DBNull.Value : newRef.Value;
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@now", SqlDbType.DateTime2, nowUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (oldRef is { } old) await Documents.DocumentObjectAccess.PublishReleaseAsync(tx, old, "expense-bill", expenseId, ct).ConfigureAwait(false);
    }

    public async Task<(string ContentType, byte[] Image)?> BillPhotoAsync(int propertyId, long expenseId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT bp.content_type, bp.image, bp.object_ref FROM ops.expense_bill_photo bp JOIN ops.expense e ON e.expense_id = bp.expense_id
            WHERE bp.expense_id = @id AND e.property_id = @p;
            """, conn);
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        string type; byte[]? inline; Guid? objectRef;
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (type, inline, objectRef) = (rd.GetString(0), rd.IsDBNull(1) ? null : (byte[])rd[1], rd.IsDBNull(2) ? null : rd.GetGuid(2));
        }

        // LF-docdb: a row with a ref reads from HccDocuments; a row without one still reads its inline column (until migration 4719).
        return (type, objectRef is { } r ? await Documents.DocumentObjectAccess.ReadAsync(objects, r, ct).ConfigureAwait(false) : inline ?? throw Documents.DocumentObjectAccess.Missing());
    }

    public async Task InsertEditAsync(ICommandTransaction tx, long expenseId, int by, DateTime nowUtc, string reason, string oldJson, string newJson, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("INSERT ops.expense_edit(expense_id, edited_by, edited_utc, reason, old_json, new_json) VALUES (@id, @by, @now, @r, @o, @n);");
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@now", SqlDbType.DateTime2, nowUtc);
        Add(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        Add(cmd, "@o", SqlDbType.NVarChar, oldJson, 2000);
        Add(cmd, "@n", SqlDbType.NVarChar, newJson, 2000);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> EditorsAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT DISTINCT edited_by FROM ops.expense_edit WHERE expense_id = @id;");
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        var list = new List<int>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetInt32(0));
        return list;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<ExpenseEditRecord>>> EditsAsync(int propertyId, IReadOnlyCollection<long> expenseIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expenseIds);
        var map = new Dictionary<long, List<ExpenseEditRecord>>();
        if (expenseIds.Count == 0) return new Dictionary<long, IReadOnlyList<ExpenseEditRecord>>();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT x.edit_id, x.expense_id, x.edited_by, x.edited_utc, x.reason, x.old_json, x.new_json
            FROM ops.expense_edit x JOIN ops.expense e ON e.expense_id = x.expense_id
            WHERE e.property_id = @p AND x.expense_id IN (SELECT CAST(value AS BIGINT) FROM STRING_SPLIT(@ids, ','))
            ORDER BY x.expense_id, x.edit_id;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@ids", SqlDbType.VarChar, string.Join(',', expenseIds.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))), -1);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var r = new ExpenseEditRecord(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc), rd.GetString(4), rd.GetString(5), rd.GetString(6));
            if (!map.TryGetValue(r.ExpenseId, out var l)) map[r.ExpenseId] = l = [];
            l.Add(r);
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ExpenseEditRecord>)kv.Value);
    }

    private const string LedgerSql = """
        SELECT e.expense_id, e.business_date, e.category, e.method, e.vendor, e.description, e.amount_paise, e.source, e.source_ref, e.status, e.void_reason,
               COALESCE(u.display_name, CONCAT('user ', e.submitted_by)), e.gst_paise, e.gst_rate_bps, CAST(CASE WHEN e.reversal_of IS NULL THEN 0 ELSE 1 END AS BIT),
               e.created_utc, e.decided_utc, e.itc_allowed,
               CAST(CASE WHEN e.capital = 1 OR EXISTS (SELECT 1 FROM ops.expense o WHERE o.expense_id = e.reversal_of AND o.capital = 1) THEN 1 ELSE 0 END AS BIT)
        FROM ops.expense e LEFT JOIN plat.app_user u ON u.user_id = e.submitted_by
        WHERE e.property_id = @p AND (@from IS NULL OR e.business_date >= @from) AND (@to IS NULL OR e.business_date <= @to)
        ORDER BY e.business_date, e.expense_id;
        """;

    public async Task<IReadOnlyList<LedgerRow>> LedgerAsync(ICommandTransaction? tx, int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (tx is not null)
        {
            await using var c1 = Sql(tx).Command(LedgerSql);
            return await ReadLedgerAsync(c1, propertyId, from, to, ct).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(LedgerSql, conn);
        return await ReadLedgerAsync(c2, propertyId, from, to, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<LedgerRow>> ReadLedgerAsync(SqlCommand cmd, int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@from", SqlDbType.Date, from?.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@to", SqlDbType.Date, to?.ToDateTime(TimeOnly.MinValue));
        var list = new List<LedgerRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new LedgerRow(rd.GetInt64(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetString(2), rd.GetString(3), rd.GetString(4), Str(rd, 5), rd.GetInt64(6),
                Str(rd, 7), Str(rd, 8), rd.GetString(9), Str(rd, 10), rd.GetString(11), rd.IsDBNull(12) ? null : rd.GetInt64(12), rd.IsDBNull(13) ? null : rd.GetInt32(13),
                rd.GetBoolean(14), DateTime.SpecifyKind(rd.GetDateTime(15), DateTimeKind.Utc), Utc(rd, 16), rd.GetBoolean(17), rd.GetBoolean(18)));
        return list;
    }

    public async Task<(int Count, long Sum)> SourceRefAsync(ICommandTransaction tx, int propertyId, string sourceRef, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT COUNT(*), COALESCE(SUM(amount_paise), 0) FROM ops.expense
            WHERE property_id = @p AND source_ref = @r AND reversal_of IS NULL AND status NOT IN ('Void','Rejected','Reversed');
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@r", SqlDbType.VarChar, sourceRef, 60);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), rd.GetInt64(1));
    }

    public async Task<string> HotelNameAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT name FROM inv.property WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string ?? string.Empty;
    }

    public async Task<DateOnly> ReadBusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT business_date FROM inv.property WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<ExpenseRecord?> GetAsync(int propertyId, long expenseId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {Columns} FROM ops.expense e WHERE e.expense_id = @id AND e.property_id = @p;", conn);
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Read(rd) : null;
    }

    public async Task<(IReadOnlyList<ExpenseRecord> Items, int Total)> ListAsync(int propertyId, ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        const string where = """
            WHERE e.property_id = @p AND (@st IS NULL OR e.status = @st) AND (@from IS NULL OR e.business_date >= @from)
              AND (@to IS NULL OR e.business_date <= @to) AND (@by IS NULL OR e.submitted_by = @by)
            """;
        await using var cmd = connections.Command($"""
            SELECT COUNT(*) FROM ops.expense e {where};
            SELECT {Columns} FROM ops.expense e {where} ORDER BY e.business_date DESC, e.expense_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@st", SqlDbType.VarChar, filter.Status, 10);
        Add(cmd, "@from", SqlDbType.Date, filter.From?.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@to", SqlDbType.Date, filter.To?.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@by", SqlDbType.Int, filter.SubmittedBy);
        Add(cmd, "@skip", SqlDbType.Int, (page - 1) * pageSize);
        Add(cmd, "@take", SqlDbType.Int, pageSize);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var total = rd.GetInt32(0);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var items = new List<ExpenseRecord>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) items.Add(Read(rd));
        return (items, total);
    }
}

/// <summary>
/// Own-submission ("O") resolver for P1's policy, scope kind <c>submission</c>: the expense is at the principal's property and the
/// principal submitted it (Reception, Cashier and Accountant hold EXP-001 as own only).
/// </summary>
public sealed class SqlExpenseScopeResolver(SqlConnectionFactory connections) : IAccessScopeResolver
{
    public string Kind => AccessScopeKinds.Submission;

    public async Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind != Kind) return false;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT COUNT(*) FROM ops.expense WHERE expense_id = @id AND property_id = @p AND submitted_by = @u;", conn);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = scope.TargetId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }
}
