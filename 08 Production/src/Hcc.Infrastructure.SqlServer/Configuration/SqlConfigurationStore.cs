using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Configuration;

/// <summary>SQL adapter of the Master configuration ports (0700: cfg.setting, cfg.setting_change, cfg.change_request). Locks: UPDLOCK, HOLDLOCK.</summary>
public sealed class SqlConfigurationStore(SqlConnectionFactory connections) : IConfigurationStore, IConfigurationReadStore
{
    private const string ChangeColumns = """
        c.change_id, c.setting_key, c.effective_from, c.value_json, c.before_json, c.before_text, c.after_text, c.reason, c.source,
        c.changed_by, cu.display_name, c.approved_by, au.display_name, c.request_id, c.revert_of, c.changed_utc, c.withdrawn_utc
        FROM cfg.setting_change c
        JOIN plat.app_user cu ON cu.user_id = c.changed_by
        LEFT JOIN plat.app_user au ON au.user_id = c.approved_by
        """;

    private const string RequestColumns = """
        r.request_id, r.setting_key, r.value_json, r.before_json, r.effective_from, r.approver, r.summary, r.reason, r.revert_of,
        r.requested_by, ru.display_name, r.requested_utc, r.expires_utc, r.status, r.decided_by, du.display_name, r.decision_reason, r.change_id, r.row_ver
        FROM cfg.change_request r
        JOIN plat.app_user ru ON ru.user_id = r.requested_by
        LEFT JOIN plat.app_user du ON du.user_id = r.decided_by
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The configuration store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static int? Int(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    private static long? Long(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    private static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private static SettingChangeRow ReadChange(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetString(1), Date(rd, 2), rd.GetString(3), rd.GetString(4), rd.GetString(5),
        rd.GetString(6), Str(rd, 7), rd.GetString(8), rd.GetInt32(9), rd.GetString(10), Int(rd, 11), Str(rd, 12), Long(rd, 13), Long(rd, 14), rd.GetDateTime(15),
        rd.IsDBNull(16) ? null : rd.GetDateTime(16));

    private static SettingRequestRow ReadRequest(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), Date(rd, 4), rd.GetString(5),
        rd.GetString(6), Str(rd, 7), Long(rd, 8), rd.GetInt32(9), rd.GetString(10), rd.GetDateTime(11), rd.GetDateTime(12), rd.GetString(13), Int(rd, 14), Str(rd, 15),
        Str(rd, 16), Long(rd, 17), (byte[])rd[18]);

    private static async Task<List<T>> ListAsync<T>(SqlCommand cmd, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(rd));
        return list;
    }

    private const string BusinessDateSql = "SELECT business_date FROM inv.property WHERE property_id = @p;";

    private static async Task<DateOnly> BusinessDateAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    // ───────────── command side ─────────────
    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(BusinessDateSql);
        return await BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<byte[]?> LockSettingAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT row_ver FROM cfg.setting WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND setting_key = @k;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
    }

    public async Task<byte[]> TouchSettingAsync(ICommandTransaction tx, int propertyId, string key, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE cfg.setting WITH (UPDLOCK, HOLDLOCK) SET changed_utc = @utc OUTPUT INSERTED.row_ver WHERE property_id = @p AND setting_key = @k;
            IF @@ROWCOUNT = 0
              INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) OUTPUT INSERTED.row_ver VALUES (@p, @k, @utc, @utc);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        do
        {
            if (await rd.ReadAsync(ct).ConfigureAwait(false)) return (byte[])rd[0];
        }
        while (await rd.NextResultAsync(ct).ConfigureAwait(false));
        throw new InvalidOperationException("Setting row was not written.");
    }

    public async Task<IReadOnlyList<SettingChangeRow>> ChangesAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {ChangeColumns} WHERE c.property_id = @p AND c.setting_key = @k ORDER BY c.change_id;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        return await ListAsync(cmd, ReadChange, ct).ConfigureAwait(false);
    }

    public async Task<SettingChangeRow?> ChangeAsync(ICommandTransaction tx, int propertyId, long changeId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {ChangeColumns} WHERE c.property_id = @p AND c.change_id = @c;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@c", SqlDbType.BigInt, changeId);
        return (await ListAsync(cmd, ReadChange, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<long> InsertChangeAsync(ICommandTransaction tx, NewSettingChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var cmd = Sql(tx).Command("""
            INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, reason, source,
              changed_by, approved_by, request_id, revert_of, changed_utc)
            OUTPUT INSERTED.change_id
            VALUES (@p, @k, @e, @v, @b, @bt, @at, @r, @s, @by, @ap, @rq, @rv, @utc);
            """);
        Add(cmd, "@p", SqlDbType.Int, change.PropertyId);
        Add(cmd, "@k", SqlDbType.VarChar, change.Key, 80);
        Add(cmd, "@e", SqlDbType.Date, change.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@v", SqlDbType.NVarChar, change.ValueJson, 4000);
        Add(cmd, "@b", SqlDbType.NVarChar, change.BeforeJson, 4000);
        Add(cmd, "@bt", SqlDbType.NVarChar, Clip(change.BeforeText, 300), 300);
        Add(cmd, "@at", SqlDbType.NVarChar, Clip(change.AfterText, 300), 300);
        Add(cmd, "@r", SqlDbType.NVarChar, change.Reason, 200);
        Add(cmd, "@s", SqlDbType.VarChar, change.Source, 8);
        Add(cmd, "@by", SqlDbType.Int, change.ChangedBy);
        Add(cmd, "@ap", SqlDbType.Int, change.ApprovedBy);
        Add(cmd, "@rq", SqlDbType.BigInt, change.RequestId);
        Add(cmd, "@rv", SqlDbType.BigInt, change.RevertOf);
        Add(cmd, "@utc", SqlDbType.DateTime2, change.Utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public async Task WithdrawChangeAsync(ICommandTransaction tx, long changeId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE cfg.setting_change SET withdrawn_utc = @utc, withdrawn_by = @u WHERE change_id = @c AND withdrawn_utc IS NULL;");
        Add(cmd, "@c", SqlDbType.BigInt, changeId);
        Add(cmd, "@u", SqlDbType.Int, principalId);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new HccException(HccErrorCode.NOT_FOUND, "Scheduled change not found (it may already be in effect).");
    }

    public async Task<SettingRequestRow?> PendingRequestAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default)
    {
        // Callers hold the setting row lock (LockSettingAsync); UX_cfg_request_pending is the backstop.
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} WHERE r.property_id = @p AND r.setting_key = @k AND r.status = 'Pending';");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        return (await ListAsync(cmd, ReadRequest, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<SettingRequestRow?> LockRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default)
    {
        await using (var lk = Sql(tx).Command("SELECT 1 FROM cfg.change_request WITH (UPDLOCK, ROWLOCK) WHERE request_id = @r AND property_id = @p;"))
        {
            Add(lk, "@r", SqlDbType.BigInt, requestId);
            Add(lk, "@p", SqlDbType.Int, propertyId);
            if (await lk.ExecuteScalarAsync(ct).ConfigureAwait(false) is null) return null;
        }

        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} WHERE r.request_id = @r;");
        Add(cmd, "@r", SqlDbType.BigInt, requestId);
        return (await ListAsync(cmd, ReadRequest, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<long> InsertRequestAsync(ICommandTransaction tx, NewSettingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var cmd = Sql(tx).Command("""
            INSERT cfg.change_request(property_id, setting_key, value_json, before_json, effective_from, approver, summary, reason, revert_of,
              requested_by, requested_utc, expires_utc, status)
            OUTPUT INSERTED.request_id
            VALUES (@p, @k, @v, @b, @e, @ap, @sum, @r, @rv, @by, @utc, @exp, 'Pending');
            """);
        Add(cmd, "@p", SqlDbType.Int, request.PropertyId);
        Add(cmd, "@k", SqlDbType.VarChar, request.Key, 80);
        Add(cmd, "@v", SqlDbType.NVarChar, request.ValueJson, 4000);
        Add(cmd, "@b", SqlDbType.NVarChar, request.BeforeJson, 4000);
        Add(cmd, "@e", SqlDbType.Date, request.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@ap", SqlDbType.VarChar, request.Approver, 7);
        Add(cmd, "@sum", SqlDbType.NVarChar, Clip(request.Summary, 400), 400);
        Add(cmd, "@r", SqlDbType.NVarChar, request.Reason, 200);
        Add(cmd, "@rv", SqlDbType.BigInt, request.RevertOf);
        Add(cmd, "@by", SqlDbType.Int, request.RequestedBy);
        Add(cmd, "@utc", SqlDbType.DateTime2, request.RequestedUtc);
        Add(cmd, "@exp", SqlDbType.DateTime2, request.ExpiresUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task DecideRequestAsync(ICommandTransaction tx, long requestId, byte[] expectedVersion, string status, int decidedBy, DateTime utc, string? reason, long? changeId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE cfg.change_request SET status = @s, decided_by = @u, decided_utc = @utc, decision_reason = @r, change_id = @c
            WHERE request_id = @id AND row_ver = @v AND status = 'Pending';
            """);
        Add(cmd, "@s", SqlDbType.VarChar, status, 10);
        Add(cmd, "@u", SqlDbType.Int, decidedBy);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        Add(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        Add(cmd, "@c", SqlDbType.BigInt, changeId);
        Add(cmd, "@id", SqlDbType.BigInt, requestId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new HccException(HccErrorCode.STALE_VERSION, "This request was changed by someone else. Reload and try again.");
    }

    public async Task<string> DisplayNameAsync(ICommandTransaction tx, int userId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT display_name FROM plat.app_user WHERE user_id = @u;");
        Add(cmd, "@u", SqlDbType.Int, userId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string ?? "user " + userId;
    }

    // ───────────── read side ─────────────
    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(BusinessDateSql, conn);
        return await BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<string> PropertyNameAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT name FROM inv.property WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<IReadOnlyList<SettingChangeRow>> ActiveChangesAsync(int propertyId, string? key, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {ChangeColumns} WHERE c.property_id = @p AND c.withdrawn_utc IS NULL AND (@k IS NULL OR c.setting_key = @k) ORDER BY c.change_id;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        return await ListAsync(cmd, ReadChange, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SettingChangeRow>> AllChangesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {ChangeColumns} WHERE c.property_id = @p ORDER BY c.change_id;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return await ListAsync(cmd, ReadChange, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, byte[]>> VersionsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT setting_key, row_ver FROM cfg.setting WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = await ListAsync(cmd, rd => (rd.GetString(0), (byte[])rd[1]), ct).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Item1, r => r.Item2, StringComparer.Ordinal);
    }

    public async Task<(int Total, IReadOnlyList<SettingChangeRow> Items)> HistoryAsync(int propertyId, string? key, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        int total;
        await using (var count = connections.Command("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND (@k IS NULL OR setting_key = @k);", conn))
        {
            Add(count, "@p", SqlDbType.Int, propertyId);
            Add(count, "@k", SqlDbType.VarChar, key, 80);
            total = (int)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await using var cmd = connections.Command($"""
            SELECT {ChangeColumns} WHERE c.property_id = @p AND (@k IS NULL OR c.setting_key = @k)
            ORDER BY c.change_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", SqlDbType.VarChar, key, 80);
        Add(cmd, "@skip", SqlDbType.Int, (page - 1) * pageSize);
        Add(cmd, "@take", SqlDbType.Int, pageSize);
        return (total, await ListAsync(cmd, ReadChange, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<SettingRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RequestColumns} WHERE r.property_id = @p AND (@s IS NULL OR r.status = @s) ORDER BY r.request_id DESC;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@s", SqlDbType.VarChar, status, 10);
        return await ListAsync(cmd, ReadRequest, ct).ConfigureAwait(false);
    }

    public async Task<int> SchemaVersionAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await MigrationRunner.CurrentVersionAsync(conn, ct).ConfigureAwait(false);
    }
}
