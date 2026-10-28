using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Expenses;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Expenses;

/// <summary>ACC-2 acc2-purchase: SQL adapter of <see cref="IVendorPaymentStore"/> (ops.vendor_payment + ops.vendor_payment_alloc, migration 4232).</summary>
public sealed class SqlVendorPaymentStore(SqlConnectionFactory connections) : IVendorPaymentStore
{
    private const string Columns = """
        p.vendor_payment_id, p.property_id, p.vendor_code, p.vendor_name, p.business_date, p.amount_paise, p.method, p.reference, p.till_id, p.cash_movement_id,
        p.recorded_by, p.recorded_utc, p.voided, p.void_reason, p.voided_by, p.voided_date
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The vendor payment store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0) => SqlExpenseStore.Add(cmd, name, type, value, size);

    private static VendorPaymentRecord Read(SqlDataReader rd, IReadOnlyList<VendorAllocationDto> allocations) => new(
        rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2).TrimEnd(), rd.GetString(3), DateOnly.FromDateTime(rd.GetDateTime(4)), rd.GetInt64(5), rd.GetString(6),
        rd.IsDBNull(7) ? null : rd.GetString(7), rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetInt64(9), rd.GetInt32(10),
        DateTime.SpecifyKind(rd.GetDateTime(11), DateTimeKind.Utc), rd.GetBoolean(12), rd.IsDBNull(13) ? null : rd.GetString(13), rd.IsDBNull(14) ? null : rd.GetInt32(14),
        rd.IsDBNull(15) ? null : DateOnly.FromDateTime(rd.GetDateTime(15)), allocations);

    public async Task<long> InsertAsync(ICommandTransaction tx, NewVendorPayment p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        long id;
        await using (var cmd = Sql(tx).Command("""
            INSERT ops.vendor_payment(property_id, vendor_code, vendor_name, business_date, amount_paise, method, reference, till_id, recorded_by, recorded_utc)
            OUTPUT INSERTED.vendor_payment_id VALUES (@p, @vc, @vn, @d, @a, @m, @r, @till, @by, @now);
            """))
        {
            Add(cmd, "@p", SqlDbType.Int, p.PropertyId);
            Add(cmd, "@vc", SqlDbType.VarChar, p.VendorCode, 20);
            Add(cmd, "@vn", SqlDbType.NVarChar, p.VendorName, 60);
            Add(cmd, "@d", SqlDbType.Date, p.BusinessDate.ToDateTime(TimeOnly.MinValue));
            Add(cmd, "@a", SqlDbType.BigInt, p.AmountPaise);
            Add(cmd, "@m", SqlDbType.NVarChar, p.Method, 60);
            Add(cmd, "@r", SqlDbType.NVarChar, p.Reference, 120);
            Add(cmd, "@till", SqlDbType.Int, p.TillId);
            Add(cmd, "@by", SqlDbType.Int, p.RecordedBy);
            Add(cmd, "@now", SqlDbType.DateTime2, p.NowUtc);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        foreach (var a in p.Allocations)
        {
            await using var cmd = Sql(tx).Command("INSERT ops.vendor_payment_alloc(vendor_payment_id, expense_id, amount_paise) VALUES (@id, @e, @a);");
            Add(cmd, "@id", SqlDbType.BigInt, id);
            Add(cmd, "@e", SqlDbType.BigInt, a.ExpenseId);
            Add(cmd, "@a", SqlDbType.BigInt, a.AmountPaise);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task SetCashMovementAsync(ICommandTransaction tx, long paymentId, long movementId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE ops.vendor_payment SET cash_movement_id = @m WHERE vendor_payment_id = @id;");
        Add(cmd, "@m", SqlDbType.BigInt, movementId);
        Add(cmd, "@id", SqlDbType.BigInt, paymentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<Dictionary<long, List<VendorAllocationDto>>> AllocationsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var m = new Dictionary<long, List<VendorAllocationDto>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!m.TryGetValue(rd.GetInt64(0), out var l)) m[rd.GetInt64(0)] = l = [];
            l.Add(new VendorAllocationDto(rd.GetInt64(1), rd.GetInt64(2)));
        }

        return m;
    }

    private static async Task<List<VendorPaymentRecord>> ReadAllAsync(SqlCommand rows, SqlCommand allocs, CancellationToken ct)
    {
        var a = await AllocationsAsync(allocs, ct).ConfigureAwait(false);
        var list = new List<VendorPaymentRecord>();
        await using var rd = await rows.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(Read(rd, a.TryGetValue(rd.GetInt64(0), out var l) ? l : []));
        return list;
    }

    public async Task<VendorPaymentRecord?> LockAsync(ICommandTransaction tx, int propertyId, long paymentId, CancellationToken ct = default)
    {
        await using var allocs = Sql(tx).Command("SELECT vendor_payment_id, expense_id, amount_paise FROM ops.vendor_payment_alloc WHERE vendor_payment_id = @id ORDER BY expense_id;");
        Add(allocs, "@id", SqlDbType.BigInt, paymentId);
        await using var rows = Sql(tx).Command($"SELECT {Columns} FROM ops.vendor_payment p WITH (UPDLOCK, ROWLOCK) WHERE p.vendor_payment_id = @id AND p.property_id = @p;");
        Add(rows, "@id", SqlDbType.BigInt, paymentId);
        Add(rows, "@p", SqlDbType.Int, propertyId);
        return (await ReadAllAsync(rows, allocs, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task VoidAsync(ICommandTransaction tx, long paymentId, string reason, int by, DateOnly date, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.vendor_payment SET voided = 1, void_reason = @r, voided_by = @by, voided_date = @d, voided_utc = @now WHERE vendor_payment_id = @id AND voided = 0;
            """);
        Add(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@now", SqlDbType.DateTime2, nowUtc);
        Add(cmd, "@id", SqlDbType.BigInt, paymentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const string ListWhere = "p.property_id = @p AND (@v IS NULL OR p.vendor_code = @v)";

    public async Task<IReadOnlyList<VendorPaymentRecord>> ListAsync(ICommandTransaction? tx, int propertyId, string? vendorCode, CancellationToken ct = default)
    {
        var allocSql = $"SELECT a.vendor_payment_id, a.expense_id, a.amount_paise FROM ops.vendor_payment_alloc a JOIN ops.vendor_payment p ON p.vendor_payment_id = a.vendor_payment_id WHERE {ListWhere};";
        var rowSql = $"SELECT {Columns} FROM ops.vendor_payment p WHERE {ListWhere} ORDER BY p.business_date, p.vendor_payment_id;";
        void Ps(SqlCommand c)
        {
            Add(c, "@p", SqlDbType.Int, propertyId);
            Add(c, "@v", SqlDbType.VarChar, vendorCode, 20);
        }

        if (tx is not null)
        {
            await using var a1 = Sql(tx).Command(allocSql);
            Ps(a1);
            await using var r1 = Sql(tx).Command(rowSql);
            Ps(r1);
            return (await ReadAllAsync(r1, a1, ct).ConfigureAwait(false));
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var a2 = connections.Command(allocSql, conn);
        Ps(a2);
        await using var r2 = connections.Command(rowSql, conn);
        Ps(r2);
        return (await ReadAllAsync(r2, a2, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<VendorBillRow>> BillsAsync(ICommandTransaction? tx, int propertyId, string vendorName, string vendorCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.expense_id, e.business_date, e.category, e.description, e.method, e.status, e.amount_paise, e.tds_paise, e.gst_paise, e.reversal_of, e.return_of
            FROM ops.expense e
            WHERE e.property_id = @p AND e.status IN ('Posted','Reversed') AND (LTRIM(RTRIM(e.vendor)) = @n OR LTRIM(RTRIM(e.vendor)) = @c)
            ORDER BY e.business_date, e.expense_id;
            """;
        var list = new List<VendorBillRow>();
        async Task ReadAsync(SqlCommand cmd)
        {
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            Add(cmd, "@n", SqlDbType.NVarChar, vendorName, 120);
            Add(cmd, "@c", SqlDbType.NVarChar, vendorCode, 120);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new VendorBillRow(rd.GetInt64(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetString(4),
                    rd.GetString(5), rd.GetInt64(6), rd.IsDBNull(7) ? null : rd.GetInt64(7), rd.IsDBNull(8) ? null : rd.GetInt64(8), rd.IsDBNull(9) ? null : rd.GetInt64(9),
                    rd.IsDBNull(10) ? null : rd.GetInt64(10)));
        }

        if (tx is not null)
        {
            await using var c1 = Sql(tx).Command(sql);
            await ReadAsync(c1).ConfigureAwait(false);
            return list;
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        await ReadAsync(c2).ConfigureAwait(false);
        return list;
    }

    public async Task<(long Amount, long Gst)> ReturnedAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT COALESCE(SUM(-amount_paise), 0), COALESCE(SUM(-COALESCE(gst_paise, 0)), 0) FROM ops.expense
            WHERE return_of = @id AND status NOT IN ('Void','Rejected','Reversed') AND reversal_of IS NULL;
            """);
        Add(cmd, "@id", SqlDbType.BigInt, expenseId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), rd.GetInt64(1));
    }
}

/// <summary>ACC-2 acc2-purchase: SQL adapter of <see cref="IFixedAssetStore"/> (acc.fixed_asset, migration 4233).</summary>
public sealed class SqlFixedAssetStore(SqlConnectionFactory connections) : IFixedAssetStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The fixed-asset store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0) => SqlExpenseStore.Add(cmd, name, type, value, size);

    public async Task<IReadOnlyList<string>> NumbersAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT asset_no FROM acc.fixed_asset WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, NewFixedAsset a, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        await using var cmd = Sql(tx).Command("""
            INSERT acc.fixed_asset(property_id, asset_no, ledger, name, cost_paise, itc_paise, purchase_date, vendor, expense_id, created_by, created_utc)
            OUTPUT INSERTED.fixed_asset_id VALUES (@p, @no, @l, @n, @c, @i, @d, @v, @e, @by, @now);
            """);
        Add(cmd, "@p", SqlDbType.Int, a.PropertyId);
        Add(cmd, "@no", SqlDbType.VarChar, a.AssetNo, 20);
        Add(cmd, "@l", SqlDbType.VarChar, a.Ledger, 4);
        Add(cmd, "@n", SqlDbType.NVarChar, a.Name.Length > 300 ? a.Name[..300] : a.Name, 300);
        Add(cmd, "@c", SqlDbType.BigInt, a.CostPaise);
        Add(cmd, "@i", SqlDbType.BigInt, a.ItcPaise);
        Add(cmd, "@d", SqlDbType.Date, a.PurchaseDate.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@v", SqlDbType.NVarChar, a.Vendor, 120);
        Add(cmd, "@e", SqlDbType.BigInt, a.ExpenseId);
        Add(cmd, "@by", SqlDbType.Int, a.CreatedBy);
        Add(cmd, "@now", SqlDbType.DateTime2, a.NowUtc);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task DisposeByExpenseAsync(ICommandTransaction tx, long expenseId, DateOnly date, long amountPaise, string voucher, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE acc.fixed_asset SET disposed_date = @d, disposed_amount_paise = @a, disposed_voucher = @v WHERE expense_id = @e AND disposed_date IS NULL;
            """);
        Add(cmd, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@a", SqlDbType.BigInt, amountPaise);
        Add(cmd, "@v", SqlDbType.VarChar, voucher, 60);
        Add(cmd, "@e", SqlDbType.BigInt, expenseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FixedAssetView>> ListAsync(int propertyId, CancellationToken ct = default)
    {
        static string W(DateTime d) => DateOnly.FromDateTime(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT fixed_asset_id, asset_no, ledger, name, cost_paise, itc_paise, purchase_date, vendor, expense_id, put_to_use, disposed_date, disposed_amount_paise, disposed_voucher
            FROM acc.fixed_asset WHERE property_id = @p ORDER BY purchase_date DESC, asset_no DESC;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<FixedAssetView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new FixedAssetView(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt64(4), rd.GetInt64(5), W(rd.GetDateTime(6)), rd.GetString(7),
                rd.GetInt64(8), rd.IsDBNull(9) ? null : W(rd.GetDateTime(9)), rd.IsDBNull(10) ? null : W(rd.GetDateTime(10)), rd.IsDBNull(11) ? null : rd.GetInt64(11),
                rd.IsDBNull(12) ? null : rd.GetString(12)));
        return list;
    }
}
