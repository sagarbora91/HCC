using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-2 · lane acc2-tds: SQL Server implementation of <see cref="ITdsStore"/> (migrations 4242 grp.company_payment.tds_paise, 4243 acc.tds_rate /
/// acc.tds_setting, 4244 acc.tds_remit). Commands serialize on <c>acc:tds:&lt;property&gt;</c>. The bill reads use the purchase lane's columns
/// (ops.expense tds_paise / tds_section, ops.expense_vendor tds_section — migrations 4230–4237) only when they exist (<c>COL_LENGTH</c>), so this
/// store works before and after that merge: without them no bill carries TDS and a vendor has no section.
/// </summary>
public sealed class SqlTdsStore(SqlConnectionFactory connections) : ITdsStore
{
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The TDS store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    public static string Resource(int propertyId) => "acc:tds:" + propertyId.ToString(CultureInfo.InvariantCulture);

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public async Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", c, t);
            cmd.Parameters.Set("@res", SqlDbType.NVarChar, Resource(propertyId), 255);
            var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another TDS change is in progress. Try again.");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT business_date FROM inv.property WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return v is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        }, ct);

    public Task<TdsSettings> SettingsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT hotel_deducts_tds, deductor_type FROM acc.tds_setting WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return TdsSettings.Default;
            return new TdsSettings(rd.GetBoolean(0), rd.GetString(1) == "individual" ? TdsColumn.Individual : TdsColumn.Other);
        }, ct);

    public Task SaveSettingsAsync(ICommandTransaction tx, int propertyId, TdsSettings settings, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(settings);
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                MERGE acc.tds_setting AS s
                USING (SELECT @p AS property_id) AS x ON s.property_id = x.property_id
                WHEN MATCHED THEN UPDATE SET hotel_deducts_tds = @h, deductor_type = @d, updated_by = @u, updated_utc = @now
                WHEN NOT MATCHED THEN INSERT (property_id, hotel_deducts_tds, deductor_type, updated_by, updated_utc) VALUES (@p, @h, @d, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@h", SqlDbType.Bit, settings.HotelDeductsTds);
            cmd.Parameters.Set("@d", SqlDbType.VarChar, settings.Column == TdsColumn.Individual ? "individual" : "other", 10);
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);
    }

    public Task<IReadOnlyList<TdsRateRecord>> RatesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT rate_id, rate_key, effective_from, value, reason FROM acc.tds_rate
                 WHERE property_id = @p AND status = 'active' ORDER BY effective_from, rate_id;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<TdsRateRecord>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new TdsRateRecord(rd.GetInt64(0), rd.GetString(1), AccSql.Date(rd, 2), rd.GetInt64(3), rd.GetString(4)));
            return (IReadOnlyList<TdsRateRecord>)list;
        }, ct);

    public Task<long> InsertRateAsync(ICommandTransaction tx, int propertyId, string key, DateOnly effectiveFrom, long value, string reason, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                INSERT acc.tds_rate(property_id, rate_key, effective_from, value, reason, set_by, set_utc) OUTPUT INSERTED.rate_id
                VALUES (@p, @k, @d, @v, @r, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, key, 40);
            cmd.Parameters.Set("@d", SqlDbType.Date, D(effectiveFrom));
            cmd.Parameters.Set("@v", SqlDbType.BigInt, value);
            cmd.Parameters.Set("@r", SqlDbType.NVarChar, reason, 200);
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }, ct);
    }

    public Task<IReadOnlyList<TdsRemitRecord>> RemitsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT remit_id, month, section, amount_paise, tender, challan, business_date, manual_voucher_id FROM acc.tds_remit
                 WHERE property_id = @p ORDER BY business_date, remit_id;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<TdsRemitRecord>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new TdsRemitRecord(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt64(3), rd.GetString(4), rd.GetString(5), AccSql.Date(rd, 6),
                    rd.IsDBNull(7) ? null : rd.GetInt64(7).ToString(CultureInfo.InvariantCulture)));
            return (IReadOnlyList<TdsRemitRecord>)list;
        }, ct);

    public Task<long> InsertRemitAsync(ICommandTransaction tx, int propertyId, TdsRemitRecord remit, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(remit);
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                INSERT acc.tds_remit(property_id, month, section, amount_paise, tender, challan, business_date, recorded_by, recorded_utc) OUTPUT INSERTED.remit_id
                VALUES (@p, @m, @s, @a, @t, @c, @d, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@m", SqlDbType.Char, remit.Month, 7);
            cmd.Parameters.Set("@s", SqlDbType.VarChar, remit.Section, 20);
            cmd.Parameters.Set("@a", SqlDbType.BigInt, remit.AmountPaise);
            cmd.Parameters.Set("@t", SqlDbType.VarChar, remit.Tender, 16);
            cmd.Parameters.Set("@c", SqlDbType.NVarChar, remit.Challan, 40);
            cmd.Parameters.Set("@d", SqlDbType.Date, D(remit.BusinessDate));
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }, ct);
    }

    public Task LinkVoucherAsync(ICommandTransaction tx, long remitId, string manualVoucherId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (!long.TryParse(manualVoucherId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("A manual voucher id is a number.", nameof(manualVoucherId));
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("UPDATE acc.tds_remit SET manual_voucher_id = @v WHERE remit_id = @r AND manual_voucher_id IS NULL;", c, t);
            cmd.Parameters.Set("@v", SqlDbType.BigInt, id);
            cmd.Parameters.Set("@r", SqlDbType.BigInt, remitId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);
    }

    // The purchase lane's columns, read only when present (dynamic SQL so this compiles to a valid batch either way).
    private const string BillsSql = """
        IF COL_LENGTH('ops.expense', 'tds_paise') IS NOT NULL AND COL_LENGTH('ops.expense', 'tds_section') IS NOT NULL
          EXEC sp_executesql N'
            SELECT e.expense_id, e.business_date, COALESCE(v.name, e.vendor), v.pan, e.tds_section, e.amount_paise - COALESCE(e.gst_paise, 0), e.tds_paise
              FROM ops.expense e
              LEFT JOIN ops.expense_vendor v ON v.property_id = e.property_id AND (v.name = e.vendor OR v.code = e.vendor)
             WHERE e.property_id = @p AND e.status IN (''Posted'', ''Reversed'') AND e.tds_paise IS NOT NULL AND e.tds_paise <> 0 AND e.tds_section IS NOT NULL
             ORDER BY e.business_date, e.expense_id;', N'@p INT', @p = @p;
        ELSE
          SELECT CAST(0 AS BIGINT), CAST(NULL AS DATE), N'', CAST(NULL AS VARCHAR(10)), '', CAST(0 AS BIGINT), CAST(0 AS BIGINT) WHERE 1 = 0;
        """;

    public Task<IReadOnlyList<TdsBillRow>> BillsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd(BillsSql, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<TdsBillRow>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new TdsBillRow(rd.GetInt64(0), AccSql.Date(rd, 1), rd.GetString(2), AccSql.StrOrNull(rd, 3), rd.GetString(4), rd.GetInt64(5), rd.GetInt64(6)));
            return (IReadOnlyList<TdsBillRow>)list;
        }, ct);

    private const string VendorSql = """
        IF COL_LENGTH('ops.expense_vendor', 'tds_section') IS NOT NULL
          EXEC sp_executesql N'SELECT TOP (1) name, tds, tds_section, payee_type FROM ops.expense_vendor WHERE property_id = @p AND (name = @n OR code = @n) ORDER BY active DESC, expense_vendor_id;',
            N'@p INT, @n NVARCHAR(120)', @p = @p, @n = @n;
        ELSE
          SELECT TOP (1) name, tds, CAST(NULL AS VARCHAR(30)), CAST(NULL AS VARCHAR(10)) FROM ops.expense_vendor WHERE property_id = @p AND (name = @n OR code = @n) ORDER BY active DESC, expense_vendor_id;
        """;

    public Task<TdsVendor?> VendorAsync(ICommandTransaction? tx, int propertyId, string vendor, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd(VendorSql, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@n", SqlDbType.NVarChar, (vendor ?? "").Trim(), 120);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return (TdsVendor?)null;
            return new TdsVendor(rd.GetString(0), rd.GetBoolean(1), rd.IsDBNull(2) ? null : Convert.ToString(rd.GetValue(2), CultureInfo.InvariantCulture),
                !rd.IsDBNull(3) && rd.GetString(3) == "individual" ? TdsColumn.Individual : TdsColumn.Other); // ACC-2 integration (ruling 9): the payee's column
        }, ct);

    private const string VendorBillsSql = """
        IF COL_LENGTH('ops.expense', 'tds_paise') IS NOT NULL
          EXEC sp_executesql N'
            SELECT e.business_date, e.amount_paise, COALESCE(e.gst_paise, 0), COALESCE(e.tds_paise, 0) FROM ops.expense e
             WHERE e.property_id = @p AND e.status IN (''Posted'', ''Reversed'') AND e.business_date BETWEEN @f AND @t
               AND (@x IS NULL OR e.expense_id <> @x)
               AND (e.vendor = @n OR e.vendor IN (SELECT v.name FROM ops.expense_vendor v WHERE v.property_id = @p AND v.code = @n)
                    OR e.vendor IN (SELECT v.code FROM ops.expense_vendor v WHERE v.property_id = @p AND v.name = @n))
             ORDER BY e.business_date, e.expense_id;', N'@p INT, @n NVARCHAR(120), @f DATE, @t DATE, @x BIGINT', @p = @p, @n = @n, @f = @f, @t = @t, @x = @x;
        ELSE
          SELECT e.business_date, e.amount_paise, COALESCE(e.gst_paise, 0), CAST(0 AS BIGINT) FROM ops.expense e
           WHERE e.property_id = @p AND e.status IN ('Posted', 'Reversed') AND e.business_date BETWEEN @f AND @t AND (@x IS NULL OR e.expense_id <> @x)
             AND (e.vendor = @n OR e.vendor IN (SELECT v.name FROM ops.expense_vendor v WHERE v.property_id = @p AND v.code = @n)
                  OR e.vendor IN (SELECT v.code FROM ops.expense_vendor v WHERE v.property_id = @p AND v.name = @n))
           ORDER BY e.business_date, e.expense_id;
        """;

    public Task<IReadOnlyList<TdsPriorBill>> VendorBillsAsync(ICommandTransaction? tx, int propertyId, string vendor, DateOnly from, DateOnly to, long? excludeExpenseId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd(VendorBillsSql, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@n", SqlDbType.NVarChar, (vendor ?? "").Trim(), 120);
            cmd.Parameters.Set("@f", SqlDbType.Date, D(from));
            cmd.Parameters.Set("@t", SqlDbType.Date, D(to));
            cmd.Parameters.Set("@x", SqlDbType.BigInt, excludeExpenseId);
            var list = new List<TdsPriorBill>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new TdsPriorBill(AccSql.Date(rd, 0), rd.GetInt64(1), rd.GetInt64(2), rd.GetInt64(3)));
            return (IReadOnlyList<TdsPriorBill>)list;
        }, ct);

    public Task<IReadOnlyList<TdsReceivablePayment>> ReceivablePaymentsAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT cp.company_id, a.name, cp.business_date, cp.amount_paise, cp.tds_paise
                  FROM grp.company_payment cp JOIN grp.company_account a ON a.company_id = cp.company_id
                 WHERE a.property_id = @p AND cp.tds_paise <> 0 AND cp.business_date BETWEEN @f AND @t
                 ORDER BY cp.business_date, cp.payment_id;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@f", SqlDbType.Date, D(from));
            cmd.Parameters.Set("@t", SqlDbType.Date, D(to));
            var list = new List<TdsReceivablePayment>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new TdsReceivablePayment(rd.GetInt64(0), rd.GetString(1), AccSql.Date(rd, 2), rd.GetInt64(3), rd.GetInt64(4)));
            return (IReadOnlyList<TdsReceivablePayment>)list;
        }, ct);
}
