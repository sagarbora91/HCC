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

/// <summary>SQL adapter of <see cref="IExpenseStore"/> (ops.expense, migration 0800). Row lock: UPDLOCK; versioned updates (STALE_VERSION).</summary>
public sealed class SqlExpenseStore(SqlConnectionFactory connections) : IExpenseStore
{
    private const string Columns = """
        expense_id, property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, notes, receipt, reason,
        decided_by, decided_utc, till_id, reversal_of, reversed_by, row_ver
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The expense store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static ExpenseRecord Read(SqlDataReader rd) => new(
        rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), SqlValues.ReadPaise(rd, 4), rd.GetString(5), DateOnly.FromDateTime(rd.GetDateTime(6)),
        rd.GetString(7), rd.GetInt32(8), Str(rd, 9), Str(rd, 10), Str(rd, 11), rd.IsDBNull(12) ? null : rd.GetInt32(12),
        rd.IsDBNull(13) ? null : DateTime.SpecifyKind(rd.GetDateTime(13), DateTimeKind.Utc), rd.IsDBNull(14) ? null : rd.GetInt32(14),
        rd.IsDBNull(15) ? null : rd.GetInt64(15), rd.IsDBNull(16) ? null : rd.GetInt64(16), (byte[])rd[17]);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<ExpenseRecord> InsertAsync(ICommandTransaction tx, NewExpense e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Sql(tx).Command($"""
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, notes, receipt, reason,
              decided_by, decided_utc, till_id, reversal_of)
            OUTPUT {string.Join(", ", Columns.Split(',').Select(c => "INSERTED." + c.Trim()))}
            VALUES (@p, @v, @c, @a, @m, @d, @s, @u, @now, @n, @r, @why, @by, CASE WHEN @by IS NULL THEN NULL ELSE @now END, @till, @rev);
            """);
        Add(cmd, "@p", SqlDbType.Int, e.PropertyId);
        Add(cmd, "@v", SqlDbType.NVarChar, e.Vendor, ExpenseRules.VendorMax);
        Add(cmd, "@c", SqlDbType.NVarChar, e.Category, ExpenseRules.CategoryMax);
        Add(cmd, "@a", SqlDbType.BigInt, e.Amount.Value);
        Add(cmd, "@m", SqlDbType.VarChar, e.Method, 14);
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
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return Read(rd);
    }

    public async Task<ExpenseRecord?> LockAsync(ICommandTransaction tx, int propertyId, long expenseId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {Columns} FROM ops.expense WITH (UPDLOCK, ROWLOCK) WHERE expense_id = @id AND property_id = @p;");
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Read(rd) : null;
    }

    public async Task<ExpenseRecord> UpdateAsync(ICommandTransaction tx, ExpenseRecord updated, byte[] expectedVersion, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.expense SET status = @s, business_date = @d, reason = @why, decided_by = @by, decided_utc = @at, till_id = @till, reversed_by = @rb
            WHERE expense_id = @id AND row_ver = @v;
            """);
        Add(cmd, "@s", SqlDbType.VarChar, updated.Status, 10);
        Add(cmd, "@d", SqlDbType.Date, updated.BusinessDate.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@why", SqlDbType.NVarChar, updated.Reason, ExpenseRules.ReasonMax);
        Add(cmd, "@by", SqlDbType.Int, updated.DecidedBy);
        Add(cmd, "@at", SqlDbType.DateTime2, updated.DecidedBy is null ? null : updated.DecidedUtc ?? nowUtc);
        Add(cmd, "@till", SqlDbType.Int, updated.TillId);
        Add(cmd, "@rb", SqlDbType.BigInt, updated.ReversedBy);
        Add(cmd, "@id", SqlDbType.BigInt, updated.ExpenseId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This expense");
        return (await LockAsync(tx, updated.PropertyId, updated.ExpenseId, ct).ConfigureAwait(false))!;
    }

    private const string FactSql = "SELECT expense_id, vendor, amount_paise, business_date, method, status, reversal_of FROM ops.expense WHERE property_id = @p AND business_date = @d;";

    private static async Task<IReadOnlyList<ExpenseFact>> ReadFactsAsync(SqlCommand cmd, int propertyId, DateOnly date, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        var list = new List<ExpenseFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ExpenseFact(rd.GetInt64(0), rd.GetString(1), SqlValues.ReadPaise(rd, 2), DateOnly.FromDateTime(rd.GetDateTime(3)), rd.GetString(4), rd.GetString(5), !rd.IsDBNull(6)));
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

    public async Task<ExpenseRecord?> GetAsync(int propertyId, long expenseId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {Columns} FROM ops.expense WHERE expense_id = @id AND property_id = @p;", conn);
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
            WHERE property_id = @p AND (@st IS NULL OR status = @st) AND (@from IS NULL OR business_date >= @from)
              AND (@to IS NULL OR business_date <= @to) AND (@by IS NULL OR submitted_by = @by)
            """;
        await using var cmd = connections.Command($"""
            SELECT COUNT(*) FROM ops.expense {where};
            SELECT {Columns} FROM ops.expense {where} ORDER BY business_date DESC, expense_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
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
