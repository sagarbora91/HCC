using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Tax;

/// <summary>SQL adapter of the GST master (migration 0600, schema <c>tax</c>) and the per-charge GST snapshot.</summary>
public sealed class SqlGstMasterStore(SqlConnectionFactory connections) : IGstMasterStore, IGstChargeSnapshots
{
    private const string ActiveSql = """
        SELECT setting_key, effective_from, value,
               -- same effective date: property change over seed, then the later change
               CAST(CASE WHEN property_id IS NULL THEN 0 ELSE 1 END AS BIGINT) * CAST(1000000000000 AS BIGINT) + version_id
        FROM tax.gst_setting_version {0}
        WHERE status = 'active' AND (property_id IS NULL OR property_id = @p);
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The GST store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private async Task<T> WithCommandAsync<T>(ICommandTransaction? tx, string sql, Func<SqlCommand, Task<T>> run, CancellationToken ct)
    {
        if (tx is not null)
        {
            await using var c = Sql(tx).Command(sql);
            return await run(c).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(sql, conn);
        return await run(cmd).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GstSettingVersion>> ReadVersionsAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<GstSettingVersion>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new GstSettingVersion(rd.GetString(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetInt64(2), rd.GetInt64(3)));
        return list;
    }

    public Task<IReadOnlyList<GstSettingVersion>> ActiveVersionsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default)
        => WithCommandAsync(tx, string.Format(System.Globalization.CultureInfo.InvariantCulture, ActiveSql, ""), c => ReadVersionsAsync(c, propertyId, ct), ct);

    public async Task<IReadOnlyList<GstSettingVersion>> LockActiveVersionsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(string.Format(System.Globalization.CultureInfo.InvariantCulture, ActiveSql, "WITH (UPDLOCK, HOLDLOCK)"));
        return await ReadVersionsAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public Task<long> MaxUnitTariffAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => WithCommandAsync(tx, """
            -- Per room type that has at least one unit: the tariff in force on the first day (latest dated tariff on or before it, else
            -- the rack rate), any dated tariff that starts inside the year, and any room night actually charged in the year.
            SELECT MAX(v) FROM (
              SELECT COALESCE((SELECT TOP (1) t.rate_paise FROM inv.room_type_tariff t
                               WHERE t.room_type_id = rt.room_type_id AND t.effective_from <= @from ORDER BY t.effective_from DESC), rt.rack_rate_paise) AS v
              FROM inv.room_type rt WHERE rt.property_id = @p AND EXISTS (SELECT 1 FROM inv.room r WHERE r.room_type_id = rt.room_type_id)
              UNION ALL
              SELECT t.rate_paise FROM inv.room_type_tariff t JOIN inv.room_type rt ON rt.room_type_id = t.room_type_id
              WHERE rt.property_id = @p AND t.effective_from > @from AND t.effective_from <= @to
                AND EXISTS (SELECT 1 FROM inv.room r WHERE r.room_type_id = rt.room_type_id)
              UNION ALL
              SELECT c.amount_paise FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation res ON res.reservation_id = f.reservation_id
              WHERE res.property_id = @p AND c.kind = 'room' AND c.reverses_charge_id IS NULL AND c.service_date BETWEEN @from AND @to
            ) x;
            """, async c =>
        {
            Add(c, "@p", SqlDbType.Int, propertyId);
            Add(c, "@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
            Add(c, "@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
            return await c.ExecuteScalarAsync(ct).ConfigureAwait(false) is long v ? v : 0L;
        }, ct);

    private const string RowColumns = "version_id, property_id, setting_key, effective_from, value, source, status, reason, changed_by, changed_utc, withdrawn_by, withdrawn_utc";

    private static GstVersionRow ReadRow(SqlDataReader rd) => new(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetInt32(1), rd.GetString(2),
        DateOnly.FromDateTime(rd.GetDateTime(3)), rd.GetInt64(4), rd.GetString(5), rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7),
        rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.GetDateTime(9), rd.IsDBNull(10) ? null : rd.GetInt32(10), rd.IsDBNull(11) ? null : rd.GetDateTime(11));

    public async Task<IReadOnlyList<GstVersionRow>> HistoryAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RowColumns} FROM tax.gst_setting_version WHERE property_id IS NULL OR property_id = @p ORDER BY version_id DESC;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<GstVersionRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRow(rd));
        return list;
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d
            ? DateOnly.FromDateTime(d)
            : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<long> InsertVersionAsync(ICommandTransaction tx, int propertyId, string key, DateOnly effectiveFrom, long value, string reason, int principalId,
        DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT tax.gst_setting_version(property_id, setting_key, effective_from, value, source, reason, changed_by, changed_utc)
            OUTPUT INSERTED.version_id VALUES (@p, @k, @from, @v, 'owner', @r, @u, @utc);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 40);
        Add(cmd, "@from", SqlDbType.Date, effectiveFrom.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@v", SqlDbType.BigInt, value);
        Add(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        Add(cmd, "@u", SqlDbType.Int, principalId);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<GstVersionRow?> LockVersionAsync(ICommandTransaction tx, long versionId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RowColumns} FROM tax.gst_setting_version WITH (UPDLOCK, ROWLOCK) WHERE version_id = @id;");
        Add(cmd, "@id", SqlDbType.BigInt, versionId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRow(rd) : null;
    }

    public async Task WithdrawVersionAsync(ICommandTransaction tx, long versionId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE tax.gst_setting_version SET status = 'withdrawn', withdrawn_by = @u, withdrawn_utc = @utc
            WHERE version_id = @id AND status = 'active' AND source = 'owner';
            """);
        Add(cmd, "@id", SqlDbType.BigInt, versionId);
        Add(cmd, "@u", SqlDbType.Int, principalId);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "This GST change can no longer be withdrawn.");
    }

    // ── Per-charge snapshot ──

    public async Task RecordAsync(ICommandTransaction tx, GstChargeSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var cmd = Sql(tx).Command("""
            INSERT tax.charge_gst(charge_id, category, tax_bps, itc, tax_paise, cgst_paise, sgst_paise, rule_text)
            VALUES (@c, @cat, @bps, @itc, @tax, @cgst, @sgst, @rule);
            """);
        Add(cmd, "@c", SqlDbType.BigInt, snapshot.ChargeId);
        Add(cmd, "@cat", SqlDbType.VarChar, GstCategories.Code(snapshot.Category), 8);
        Add(cmd, "@bps", SqlDbType.Int, snapshot.Bps);
        Add(cmd, "@itc", SqlDbType.Bit, snapshot.Itc);
        Add(cmd, "@tax", SqlDbType.BigInt, snapshot.Tax.Value);
        Add(cmd, "@cgst", SqlDbType.BigInt, snapshot.Cgst.Value);
        Add(cmd, "@sgst", SqlDbType.BigInt, snapshot.Sgst.Value);
        Add(cmd, "@rule", SqlDbType.NVarChar, snapshot.Rule.Length <= 80 ? snapshot.Rule : snapshot.Rule[..80], 80);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<GstChargeSnapshot?> FindAsync(ICommandTransaction tx, long chargeId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT category, tax_bps, itc, tax_paise, cgst_paise, sgst_paise, rule_text FROM tax.charge_gst WHERE charge_id = @c;");
        Add(cmd, "@c", SqlDbType.BigInt, chargeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new GstChargeSnapshot(chargeId, GstCategories.Parse(rd.GetString(0)), rd.GetInt32(1), rd.GetBoolean(2), new Paise(rd.GetInt64(3)),
            new Paise(rd.GetInt64(4)), new Paise(rd.GetInt64(5)), rd.GetString(6));
    }
}
