using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Expenses;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Expenses;

/// <summary>DEC-120 SCC registers (migration 4150): ops.expense_budget, ops.expense_recurring, ops.petty_float_entry, ops.udhaar_*, ops.expense_month_lock;
/// migration 4190 (SCC decisions wave): Udhaar desk receipts (DEC-130), month unlocks + the ITC part of the feed (DEC-131, DEC-135).</summary>
public sealed class SqlExpenseRegisterStore(SqlConnectionFactory connections) : IExpenseRegisterStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The expense register store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0) => SqlExpenseStore.Add(cmd, name, type, value, size);

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private static object D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    /// <summary>Runs <paramref name="sql"/> inside the transaction when there is one, else on its own connection.</summary>
    private async Task<List<T>> QueryAsync<T>(ICommandTransaction? tx, string sql, Action<SqlCommand> bind, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        if (tx is not null)
        {
            await using var c1 = Sql(tx).Command(sql);
            bind(c1);
            await using var r1 = await c1.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r1.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(r1));
            return list;
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        bind(c2);
        await using var r2 = await c2.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r2.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(r2));
        return list;
    }

    private static async Task ExecAsync(ICommandTransaction tx, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command(sql);
        bind(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> InsertIdAsync(ICommandTransaction tx, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command(sql);
        bind(cmd);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // ── Budgets ──

    public async Task<IReadOnlyDictionary<string, long>> BudgetsAsync(ICommandTransaction? tx, int propertyId, string month, CancellationToken ct = default)
    {
        var rows = await QueryAsync(tx, "SELECT category, amount_paise FROM ops.expense_budget WHERE property_id = @p AND budget_month = @m;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, month, 7); }, r => (r.GetString(0), r.GetInt64(1)), ct).ConfigureAwait(false);
        return rows.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<(string Month, string Category, long Amount)>> AllBudgetsAsync(int propertyId, CancellationToken ct = default) =>
        await QueryAsync(null, "SELECT budget_month, category, amount_paise FROM ops.expense_budget WHERE property_id = @p ORDER BY budget_month, category;",
            c => Add(c, "@p", SqlDbType.Int, propertyId), r => (r.GetString(0), r.GetString(1), r.GetInt64(2)), ct).ConfigureAwait(false);

    public Task SetBudgetAsync(ICommandTransaction tx, int propertyId, string month, string category, long amount, int by, DateTime nowUtc, CancellationToken ct = default) =>
        ExecAsync(tx, """
            UPDATE ops.expense_budget WITH (UPDLOCK, HOLDLOCK) SET amount_paise = @a, updated_by = @by, updated_utc = @now WHERE property_id = @p AND budget_month = @m AND category = @c;
            IF @@ROWCOUNT = 0 INSERT ops.expense_budget(property_id, budget_month, category, amount_paise, updated_by, updated_utc) VALUES (@p, @m, @c, @a, @by, @now);
            """, c =>
        {
            Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, month, 7); Add(c, "@c", SqlDbType.NVarChar, category, 60);
            Add(c, "@a", SqlDbType.BigInt, amount); Add(c, "@by", SqlDbType.Int, by); Add(c, "@now", SqlDbType.DateTime2, nowUtc);
        }, ct);

    // ── Recurring templates ──

    private const string RecCols = "template_id, property_id, name, amount_paise, due_day, category, method, vendor, active, created_by, created_utc";

    private static RecurringRecord ReadRec(SqlDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt64(3), r.GetByte(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.GetBoolean(8), r.GetInt32(9), Utc(r, 10));

    public async Task<IReadOnlyList<RecurringRecord>> RecurringAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
        await QueryAsync(tx, $"SELECT {RecCols} FROM ops.expense_recurring WHERE property_id = @p AND deleted_utc IS NULL ORDER BY template_id;",
            c => Add(c, "@p", SqlDbType.Int, propertyId), ReadRec, ct).ConfigureAwait(false);

    public async Task<RecurringRecord?> LockRecurringAsync(ICommandTransaction tx, int propertyId, int templateId, CancellationToken ct = default) =>
        (await QueryAsync(tx, $"SELECT {RecCols} FROM ops.expense_recurring WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND template_id = @id AND deleted_utc IS NULL;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@id", SqlDbType.Int, templateId); }, ReadRec, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<int> InsertRecurringAsync(ICommandTransaction tx, RecurringRecord t, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        return (int)await InsertIdAsync(tx, """
            INSERT ops.expense_recurring(property_id, name, amount_paise, due_day, category, method, vendor, active, created_by, created_utc)
            OUTPUT INSERTED.template_id VALUES (@p, @n, @a, @d, @c, @m, @v, @act, @by, @now);
            """, c => BindRec(c, t), ct).ConfigureAwait(false);
    }

    public Task UpdateRecurringAsync(ICommandTransaction tx, RecurringRecord t, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        return ExecAsync(tx, """
            UPDATE ops.expense_recurring SET name = @n, amount_paise = @a, due_day = @d, category = @c, method = @m, vendor = @v, active = @act
            WHERE template_id = @id AND property_id = @p;
            """, c => { BindRec(c, t); Add(c, "@id", SqlDbType.Int, t.TemplateId); }, ct);
    }

    private static void BindRec(SqlCommand c, RecurringRecord t)
    {
        Add(c, "@p", SqlDbType.Int, t.PropertyId); Add(c, "@n", SqlDbType.NVarChar, t.Name, 60); Add(c, "@a", SqlDbType.BigInt, t.Amount);
        Add(c, "@d", SqlDbType.TinyInt, (byte)t.DueDay); Add(c, "@c", SqlDbType.NVarChar, t.Category, 60); Add(c, "@m", SqlDbType.NVarChar, t.Method, 60);
        Add(c, "@v", SqlDbType.NVarChar, t.Vendor, 120); Add(c, "@act", SqlDbType.Bit, t.Active); Add(c, "@by", SqlDbType.Int, t.CreatedBy);
        Add(c, "@now", SqlDbType.DateTime2, t.CreatedUtc);
    }

    public Task DeleteRecurringAsync(ICommandTransaction tx, int templateId, int by, DateTime nowUtc, CancellationToken ct = default) =>
        ExecAsync(tx, "UPDATE ops.expense_recurring SET deleted_by = @by, deleted_utc = @now WHERE template_id = @id;",
            c => { Add(c, "@id", SqlDbType.Int, templateId); Add(c, "@by", SqlDbType.Int, by); Add(c, "@now", SqlDbType.DateTime2, nowUtc); }, ct);

    // ── Petty-cash float ──

    private const string PettyCols = "entry_id, property_id, amount_paise, business_date, status, note, recorded_by, recorded_utc, decided_by, decided_utc, reason";

    private static PettyFloatRecord ReadPetty(SqlDataReader r) => new(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), Date(r, 3), r.GetString(4), Str(r, 5), r.GetInt32(6),
        Utc(r, 7), r.IsDBNull(8) ? null : r.GetInt32(8), r.IsDBNull(9) ? null : Utc(r, 9), Str(r, 10));

    public async Task<IReadOnlyList<PettyFloatRecord>> PettyAsync(int propertyId, CancellationToken ct = default) =>
        await QueryAsync(null, $"SELECT {PettyCols} FROM ops.petty_float_entry WHERE property_id = @p ORDER BY entry_id;", c => Add(c, "@p", SqlDbType.Int, propertyId), ReadPetty, ct)
            .ConfigureAwait(false);

    public Task<long> InsertPettyAsync(ICommandTransaction tx, PettyFloatRecord e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        return InsertIdAsync(tx, """
            INSERT ops.petty_float_entry(property_id, amount_paise, business_date, status, note, recorded_by, recorded_utc)
            OUTPUT INSERTED.entry_id VALUES (@p, @a, @d, 'Pending', @n, @by, @now);
            """, c =>
        {
            Add(c, "@p", SqlDbType.Int, e.PropertyId); Add(c, "@a", SqlDbType.BigInt, e.Amount); Add(c, "@d", SqlDbType.Date, D(e.BusinessDate));
            Add(c, "@n", SqlDbType.NVarChar, e.Note, 200); Add(c, "@by", SqlDbType.Int, e.RecordedBy); Add(c, "@now", SqlDbType.DateTime2, e.RecordedUtc);
        }, ct);
    }

    public async Task<PettyFloatRecord?> LockPettyAsync(ICommandTransaction tx, int propertyId, long entryId, CancellationToken ct = default) =>
        (await QueryAsync(tx, $"SELECT {PettyCols} FROM ops.petty_float_entry WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND entry_id = @id;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@id", SqlDbType.BigInt, entryId); }, ReadPetty, ct).ConfigureAwait(false)).FirstOrDefault();

    public Task DecidePettyAsync(ICommandTransaction tx, long entryId, string status, int by, DateTime nowUtc, string? reason, CancellationToken ct = default) =>
        ExecAsync(tx, "UPDATE ops.petty_float_entry SET status = @s, decided_by = @by, decided_utc = @now, reason = @r WHERE entry_id = @id AND status = 'Pending';", c =>
        {
            Add(c, "@id", SqlDbType.BigInt, entryId); Add(c, "@s", SqlDbType.VarChar, status, 10); Add(c, "@by", SqlDbType.Int, by);
            Add(c, "@now", SqlDbType.DateTime2, nowUtc); Add(c, "@r", SqlDbType.NVarChar, reason, 200);
        }, ct);

    // ── Udhaar ──

    private const string UdCols = "receivable_id, property_id, customer_name, mobile, amount_paise, credit_date, due_date, note, created_by, created_utc, is_void, void_reason, voided_by, voided_date, kind, gst_bps";

    private static UdhaarRecord ReadUd(SqlDataReader r) => new(r.GetInt64(0), r.GetInt32(1), r.GetString(2), Str(r, 3), r.GetInt64(4), Date(r, 5),
        r.IsDBNull(6) ? null : Date(r, 6), Str(r, 7), r.GetInt32(8), Utc(r, 9), r.GetBoolean(10), Str(r, 11), r.IsDBNull(12) ? null : r.GetInt32(12),
        r.IsDBNull(13) ? null : Date(r, 13), [], Str(r, 14), r.GetInt32(15)); // ACC-1: kind, gst_bps (migration 4215)

    private static UdhaarSettlementRecord ReadSettle(SqlDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), Date(r, 3), r.GetString(4), r.GetInt32(5), Utc(r, 6),
        r.GetBoolean(7), Str(r, 8), r.IsDBNull(9) ? null : r.GetInt32(9));

    private const string SettleCols = "s.settlement_id, s.receivable_id, s.amount_paise, s.settle_date, s.mode, s.recorded_by, s.recorded_utc, s.desk, s.reference, s.till_id";

    public async Task<IReadOnlyList<UdhaarRecord>> UdhaarAsync(int propertyId, CancellationToken ct = default)
    {
        var list = await QueryAsync(null, $"SELECT {UdCols} FROM ops.udhaar_receivable WHERE property_id = @p ORDER BY credit_date, receivable_id;",
            c => Add(c, "@p", SqlDbType.Int, propertyId), ReadUd, ct).ConfigureAwait(false);
        var settles = await QueryAsync(null, $"""
            SELECT {SettleCols} FROM ops.udhaar_settlement s JOIN ops.udhaar_receivable r ON r.receivable_id = s.receivable_id
            WHERE r.property_id = @p ORDER BY s.settlement_id;
            """, c => Add(c, "@p", SqlDbType.Int, propertyId), ReadSettle, ct).ConfigureAwait(false);
        var by = settles.GroupBy(s => s.ReceivableId).ToDictionary(g => g.Key, g => (IReadOnlyList<UdhaarSettlementRecord>)g.ToList());
        return list.Select(r => by.TryGetValue(r.ReceivableId, out var s) ? r with { Settlements = s } : r).ToList();
    }

    public Task<long> InsertUdhaarAsync(ICommandTransaction tx, UdhaarRecord r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        return InsertIdAsync(tx, """
            INSERT ops.udhaar_receivable(property_id, customer_name, mobile, amount_paise, credit_date, due_date, note, created_by, created_utc, kind, gst_bps)
            OUTPUT INSERTED.receivable_id VALUES (@p, @n, @mob, @a, @d, @due, @note, @by, @now, @kind, @gst);
            """, c =>
        {
            Add(c, "@p", SqlDbType.Int, r.PropertyId); Add(c, "@n", SqlDbType.NVarChar, r.Name, 80); Add(c, "@mob", SqlDbType.Char, r.Mobile, 10);
            Add(c, "@a", SqlDbType.BigInt, r.Amount); Add(c, "@d", SqlDbType.Date, D(r.CreditDate)); Add(c, "@due", SqlDbType.Date, r.DueDate is { } d ? D(d) : null);
            Add(c, "@note", SqlDbType.NVarChar, r.Note, 200); Add(c, "@by", SqlDbType.Int, r.CreatedBy); Add(c, "@now", SqlDbType.DateTime2, r.CreatedUtc);
            Add(c, "@kind", SqlDbType.VarChar, r.Kind, 10); Add(c, "@gst", SqlDbType.Int, r.GstBps); // ACC-1 (migration 4215)
        }, ct);
    }

    public async Task<UdhaarRecord?> LockUdhaarAsync(ICommandTransaction tx, int propertyId, long receivableId, CancellationToken ct = default)
    {
        var r = (await QueryAsync(tx, $"SELECT {UdCols} FROM ops.udhaar_receivable WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND receivable_id = @id;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@id", SqlDbType.BigInt, receivableId); }, ReadUd, ct).ConfigureAwait(false)).FirstOrDefault();
        if (r is null) return null;
        var s = await QueryAsync(tx, $"SELECT {SettleCols} FROM ops.udhaar_settlement s WHERE s.receivable_id = @id ORDER BY s.settlement_id;",
            c => Add(c, "@id", SqlDbType.BigInt, receivableId), ReadSettle, ct).ConfigureAwait(false);
        return r with { Settlements = s };
    }

    public Task<long> InsertSettlementAsync(ICommandTransaction tx, UdhaarSettlementRecord s, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        return InsertIdAsync(tx, """
            INSERT ops.udhaar_settlement(receivable_id, amount_paise, settle_date, mode, recorded_by, recorded_utc, desk, reference, till_id)
            OUTPUT INSERTED.settlement_id VALUES (@id, @a, @d, @m, @by, @now, @desk, @ref, @till);
            """, c =>
        {
            Add(c, "@id", SqlDbType.BigInt, s.ReceivableId); Add(c, "@a", SqlDbType.BigInt, s.Amount); Add(c, "@d", SqlDbType.Date, D(s.Date));
            Add(c, "@m", SqlDbType.NVarChar, s.Mode, 60); Add(c, "@by", SqlDbType.Int, s.RecordedBy); Add(c, "@now", SqlDbType.DateTime2, s.RecordedUtc);
            Add(c, "@desk", SqlDbType.Bit, s.Desk); Add(c, "@ref", SqlDbType.NVarChar, s.Reference, 120); Add(c, "@till", SqlDbType.Int, s.TillId);
        }, ct);
    }

    public Task VoidUdhaarAsync(ICommandTransaction tx, long receivableId, string? reason, int by, DateOnly date, CancellationToken ct = default) =>
        ExecAsync(tx, "UPDATE ops.udhaar_receivable SET is_void = 1, void_reason = @r, voided_by = @by, voided_date = @d WHERE receivable_id = @id AND is_void = 0;", c =>
        {
            Add(c, "@id", SqlDbType.BigInt, receivableId); Add(c, "@r", SqlDbType.NVarChar, reason, 200); Add(c, "@by", SqlDbType.Int, by); Add(c, "@d", SqlDbType.Date, D(date));
        }, ct);

    // ── Month locks ──

    private const string LockCols =
        "lock_month, expense_paise, entry_count, gst_paid_paise, by_category, generated_by, generated_utc, stale, stale_utc, gst_itc_paise, unlocked_by, unlocked_utc, unlock_reason";

    private static MonthLockRecord ReadLock(SqlDataReader r) => new(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetInt64(3), r.GetString(4), r.GetInt32(5), Utc(r, 6),
        r.GetBoolean(7), r.IsDBNull(8) ? null : Utc(r, 8), r.IsDBNull(9) ? null : r.GetInt64(9), r.IsDBNull(10) ? null : r.GetInt32(10), r.IsDBNull(11) ? null : Utc(r, 11), Str(r, 12));

    public async Task<IReadOnlyList<MonthLockRecord>> MonthLocksAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
        await QueryAsync(tx, $"SELECT {LockCols} FROM ops.expense_month_lock WHERE property_id = @p ORDER BY lock_month;", c => Add(c, "@p", SqlDbType.Int, propertyId), ReadLock, ct)
            .ConfigureAwait(false);

    public async Task<MonthLockRecord?> MonthLockAsync(ICommandTransaction tx, int propertyId, string month, CancellationToken ct = default) =>
        (await QueryAsync(tx, $"SELECT {LockCols} FROM ops.expense_month_lock WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND lock_month = @m;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, month, 7); }, ReadLock, ct).ConfigureAwait(false)).FirstOrDefault();

    public Task SaveMonthLockAsync(ICommandTransaction tx, int propertyId, MonthLockRecord m, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(m);
        return ExecAsync(tx, """
            UPDATE ops.expense_month_lock WITH (UPDLOCK, HOLDLOCK) SET expense_paise = @e, entry_count = @n, gst_paid_paise = @g, by_category = @c, generated_by = @by,
              generated_utc = @now, stale = 0, stale_utc = NULL, gst_itc_paise = @gi, unlocked_by = NULL, unlocked_utc = NULL, unlock_reason = NULL
            WHERE property_id = @p AND lock_month = @m;
            IF @@ROWCOUNT = 0 INSERT ops.expense_month_lock(property_id, lock_month, expense_paise, entry_count, gst_paid_paise, by_category, generated_by, generated_utc, gst_itc_paise)
              VALUES (@p, @m, @e, @n, @g, @c, @by, @now, @gi);
            """, c =>
        {
            Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, m.Month, 7); Add(c, "@e", SqlDbType.BigInt, m.Expense); Add(c, "@n", SqlDbType.Int, m.Count);
            Add(c, "@g", SqlDbType.BigInt, m.GstPaid); Add(c, "@c", SqlDbType.NVarChar, m.ByCategoryJson, -1); Add(c, "@by", SqlDbType.Int, m.GeneratedBy);
            Add(c, "@now", SqlDbType.DateTime2, m.GeneratedUtc); Add(c, "@gi", SqlDbType.BigInt, m.GstItc);
        }, ct);
    }

    public Task MarkStaleAsync(ICommandTransaction tx, int propertyId, string month, DateTime nowUtc, CancellationToken ct = default) =>
        ExecAsync(tx, "UPDATE ops.expense_month_lock SET stale = 1, stale_utc = @now WHERE property_id = @p AND lock_month = @m;",
            c => { Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, month, 7); Add(c, "@now", SqlDbType.DateTime2, nowUtc); }, ct);

    public Task UnlockMonthAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime nowUtc, string reason, CancellationToken ct = default) =>
        ExecAsync(tx, """
            UPDATE ops.expense_month_lock SET unlocked_by = @by, unlocked_utc = @now, unlock_reason = @r, stale = 1, stale_utc = @now
            WHERE property_id = @p AND lock_month = @m AND unlocked_utc IS NULL;
            IF @@ROWCOUNT <> 1 THROW 54190, 'The month is not locked.', 1;
            INSERT ops.expense_month_unlock(property_id, lock_month, unlocked_by, unlocked_utc, reason) VALUES (@p, @m, @by, @now, @r);
            """, c =>
        {
            Add(c, "@p", SqlDbType.Int, propertyId); Add(c, "@m", SqlDbType.Char, month, 7); Add(c, "@by", SqlDbType.Int, by);
            Add(c, "@now", SqlDbType.DateTime2, nowUtc); Add(c, "@r", SqlDbType.NVarChar, reason, 200);
        }, ct);

    public async Task<IReadOnlyList<MonthUnlockRecord>> MonthUnlocksAsync(int propertyId, CancellationToken ct = default) =>
        await QueryAsync(null, "SELECT lock_month, unlocked_by, unlocked_utc, reason FROM ops.expense_month_unlock WHERE property_id = @p ORDER BY unlock_id;",
            c => Add(c, "@p", SqlDbType.Int, propertyId), r => new MonthUnlockRecord(r.GetString(0), r.GetInt32(1), Utc(r, 2), r.GetString(3)), ct).ConfigureAwait(false);
}

/// <summary>The expense categories / vendors masters (migration 2830) as the expense commands read them; SCC vendor auto-add writes mst.master_history.</summary>
public sealed class SqlExpenseMasterSource(SqlConnectionFactory connections) : IExpenseMasterSource
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The expense master source needs the SQL command transaction.");

    private async Task<List<T>> QueryAsync<T>(ICommandTransaction? tx, string sql, int propertyId, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        if (tx is not null)
        {
            await using var c1 = Sql(tx).Command(sql);
            c1.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            await using var r1 = await c1.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r1.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(r1));
            return list;
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        c2.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var r2 = await c2.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r2.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(r2));
        return list;
    }

    public async Task<IReadOnlyList<ExpenseCategoryRow>> CategoriesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
        await QueryAsync(tx, "SELECT code, name, active, needs_bill, approval_limit_paise, rcm, rcm_bps, capital, itc_blocked, ledger FROM ops.expense_category WHERE property_id = @p ORDER BY sort_order, code;", propertyId,
            r => new ExpenseCategoryRow(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.GetBoolean(7), r.GetBoolean(8), r.GetString(9).Trim()), ct).ConfigureAwait(false); // ACC-2 (4231)

    public async Task<IReadOnlyList<ExpenseVendorRow>> VendorsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
        await QueryAsync(tx, "SELECT expense_vendor_id, code, name, active, gst_state, tds, tds_section FROM ops.expense_vendor WHERE property_id = @p ORDER BY sort_order, code;", propertyId,
            r => new ExpenseVendorRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5),
                r.IsDBNull(6) ? null : r.GetString(6)), ct).ConfigureAwait(false); // ACC-2 (4231)

    public async Task AddVendorAsync(ICommandTransaction tx, int propertyId, string name, int by, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var s = Sql(tx);
        await using (var seed = s.Command("EXEC mst.usp_seed_money_masters @p;"))
        {
            seed.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            await seed.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var rows = new List<(string Code, string Name, int Sort)>();
        await using (var read = s.Command("SELECT code, name, sort_order FROM ops.expense_vendor WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;"))
        {
            read.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            await using var rd = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((rd.GetString(0).TrimEnd(), rd.GetString(1), rd.GetInt32(2)));
        }

        if (rows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        var code = ExpenseMasters.VendorCode(name, new HashSet<string>(rows.Select(r => r.Code), StringComparer.Ordinal));
        var sort = (rows.Count == 0 ? 0 : rows.Max(r => r.Sort)) + 10;
        await using var ins = s.Command("""
            INSERT ops.expense_vendor(property_id, code, name, sort_order, vendor_type, tds, updated_by, updated_utc)
            OUTPUT INSERTED.expense_vendor_id VALUES (@p, @c, @n, @s, @t, 0, @by, @now);
            """);
        ins.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        ins.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = code;
        ins.Parameters.Add("@n", SqlDbType.NVarChar, 60).Value = name;
        ins.Parameters.Add("@s", SqlDbType.Int).Value = sort;
        ins.Parameters.Add("@t", SqlDbType.VarChar, 8).Value = ExpenseMasters.VendorType(name);
        ins.Parameters.Add("@by", SqlDbType.Int).Value = by;
        ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        var id = Convert.ToInt64(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await using var hist = s.Command("""
            INSERT mst.master_history(property_id, master, row_id, code, field, old_value, new_value, changed_by, changed_utc)
            VALUES (@p, @m, @r, @c, 'name', NULL, @n, @by, @now);
            """);
        hist.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        hist.Parameters.Add("@m", SqlDbType.VarChar, 30).Value = MoneyMasterIds.ExpenseVendors;
        hist.Parameters.Add("@r", SqlDbType.BigInt).Value = id;
        hist.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = code;
        hist.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = name + " (added from an expense)";
        hist.Parameters.Add("@by", SqlDbType.Int).Value = by;
        hist.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        await hist.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
