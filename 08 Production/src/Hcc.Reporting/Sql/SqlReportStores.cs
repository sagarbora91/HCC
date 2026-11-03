// F7-R1 · SQL adapters of the report module: property info (inv.property), favourites (rpt.favourite), schedules (rpt.schedule), runs (rpt.run)
// and the print / export log (rpt.export_log). Migrations 4400–4405.
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Sql;

public sealed class SqlReportStores(SqlConnectionFactory connections) : IReportPropertyInfo, IReportScheduleStore, IReportRunStore, IReportExportLog, IReportFavourites
{
    private static void Add(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static string Fmt(ReportFormat f) => f.ToString().ToLowerInvariant();

    private static ReportFormat ParseFmt(string s) => Enum.Parse<ReportFormat>(s, ignoreCase: true);

    public async Task<ReportProperty> GetAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT code, name, business_date FROM inv.property WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        return new ReportProperty(rd.GetString(0), rd.GetString(1), DateOnly.FromDateTime(rd.GetDateTime(2)));
    }

    /// <summary>Every property id (the scheduled-run worker visits each).</summary>
    public async Task<IReadOnlyList<int>> PropertyIdsAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT property_id FROM inv.property ORDER BY property_id;", conn);
        var list = new List<int>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetInt32(0));
        return list;
    }

    // ── favourites ──
    public async Task<bool> ToggleAsync(int propertyId, int principalId, string reportId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            IF EXISTS (SELECT 1 FROM rpt.favourite WHERE property_id = @p AND principal_id = @u AND report_id = @r)
              BEGIN DELETE rpt.favourite WHERE property_id = @p AND principal_id = @u AND report_id = @r; SELECT CAST(0 AS BIT); END
            ELSE BEGIN INSERT rpt.favourite(property_id, principal_id, report_id) VALUES (@p, @u, @r); SELECT CAST(1 AS BIT); END
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@u", SqlDbType.Int, principalId); Add(cmd, "@r", SqlDbType.VarChar, reportId);
        return (bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<string>> FavouritesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT report_id FROM rpt.favourite WHERE property_id = @p AND principal_id = @u ORDER BY report_id;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@u", SqlDbType.Int, principalId);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }

    // ── schedules ──
    public async Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT schedule_id, report_id, params, format, cadence, cadence_day, run_time, owner_principal_id, recipients, enabled, last_run_utc
            FROM rpt.schedule WHERE property_id = @p ORDER BY schedule_id;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<ReportScheduleRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ReportScheduleRecord(rd.GetInt64(0), rd.GetString(1), JsonSerializer.Deserialize<Dictionary<string, string>>(rd.GetString(2))!, ParseFmt(rd.GetString(3)),
                rd.GetString(4), rd.IsDBNull(5) ? null : rd.GetByte(5), TimeOnly.FromTimeSpan(rd.GetTimeSpan(6)), rd.GetInt32(7),
                JsonSerializer.Deserialize<List<int>>(rd.GetString(8))!, rd.GetBoolean(9), rd.IsDBNull(10) ? null : new DateTimeOffset(rd.GetDateTime(10), TimeSpan.Zero)));
        return list;
    }

    public async Task<long> SaveAsync(int propertyId, ReportScheduleRecord s, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(s.Id == 0
            ? """
              INSERT rpt.schedule(property_id, report_id, params, format, cadence, cadence_day, run_time, owner_principal_id, recipients, enabled)
              OUTPUT INSERTED.schedule_id VALUES (@p, @r, @params, @f, @c, @d, @t, @o, @rc, @e);
              """
            : """
              UPDATE rpt.schedule SET report_id = @r, params = @params, format = @f, cadence = @c, cadence_day = @d, run_time = @t, recipients = @rc, enabled = @e,
                last_run_utc = @lr
              OUTPUT INSERTED.schedule_id WHERE property_id = @p AND schedule_id = @id;
              """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@id", SqlDbType.BigInt, s.Id); Add(cmd, "@r", SqlDbType.VarChar, s.ReportId);
        Add(cmd, "@params", SqlDbType.NVarChar, JsonSerializer.Serialize(s.Params)); Add(cmd, "@f", SqlDbType.VarChar, Fmt(s.Format));
        Add(cmd, "@c", SqlDbType.VarChar, s.Cadence); Add(cmd, "@d", SqlDbType.TinyInt, s.Day is { } d ? (byte)d : null); Add(cmd, "@t", SqlDbType.Time, s.Time.ToTimeSpan());
        Add(cmd, "@o", SqlDbType.Int, s.OwnerPrincipalId); Add(cmd, "@rc", SqlDbType.NVarChar, JsonSerializer.Serialize(s.Recipients)); Add(cmd, "@e", SqlDbType.Bit, s.Enabled);
        Add(cmd, "@lr", SqlDbType.DateTime2, s.LastRun?.UtcDateTime);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : throw new HccException(HccErrorCode.NOT_FOUND, "Schedule not found.");
    }

    public async Task DeleteAsync(int propertyId, long id, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("DELETE rpt.schedule WHERE property_id = @p AND schedule_id = @id;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@id", SqlDbType.BigInt, id);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) throw new HccException(HccErrorCode.NOT_FOUND, "Schedule not found.");
    }

    // ── runs ──
    public async Task<long> StartAsync(int propertyId, ReportRunRecord run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            INSERT rpt.run(property_id, schedule_id, report_id, params, principal_id, started_utc, status) OUTPUT INSERTED.run_id
            VALUES (@p, @s, @r, @params, @u, @at, 'running');
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@s", SqlDbType.BigInt, run.ScheduleId); Add(cmd, "@r", SqlDbType.VarChar, run.ReportId);
        Add(cmd, "@params", SqlDbType.NVarChar, run.ParamsJson); Add(cmd, "@u", SqlDbType.Int, run.PrincipalId); Add(cmd, "@at", SqlDbType.DateTime2, run.StartedAt.UtcDateTime);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task FinishAsync(int propertyId, long id, string status, int? rows, string? fileRef, string? error, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            UPDATE rpt.run SET status = @st, row_count = @n, file_ref = @f, error = @e, finished_utc = SYSUTCDATETIME() WHERE property_id = @p AND run_id = @id;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@id", SqlDbType.BigInt, id); Add(cmd, "@st", SqlDbType.VarChar, status);
        Add(cmd, "@n", SqlDbType.Int, rows); Add(cmd, "@f", SqlDbType.NVarChar, fileRef); Add(cmd, "@e", SqlDbType.NVarChar, error is { Length: > 400 } ? error[..400] : error);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReportRunRecord>> ListAsync(int propertyId, int take, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) run_id, schedule_id, report_id, params, principal_id, started_utc, finished_utc, status, row_count, file_ref, error
            FROM rpt.run WHERE property_id = @p ORDER BY run_id DESC;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@n", SqlDbType.Int, Math.Clamp(take, 1, 500));
        var list = new List<ReportRunRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ReportRunRecord(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetInt64(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4),
                new DateTimeOffset(rd.GetDateTime(5), TimeSpan.Zero), rd.IsDBNull(6) ? null : new DateTimeOffset(rd.GetDateTime(6), TimeSpan.Zero), rd.GetString(7),
                rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetString(9), rd.IsDBNull(10) ? null : rd.GetString(10)));
        return list;
    }

    // ── export log ──
    public async Task<long> AppendAsync(int propertyId, ReportExportLogRecord e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            INSERT rpt.export_log(property_id, at_utc, principal_id, report_id, params_hash, params, format, row_count, file_name, provisional)
            OUTPUT INSERTED.log_id VALUES (@p, @at, @u, @r, @h, @params, @f, @n, @fn, @pv);
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@at", SqlDbType.DateTime2, e.At.UtcDateTime); Add(cmd, "@u", SqlDbType.Int, e.PrincipalId);
        Add(cmd, "@r", SqlDbType.VarChar, e.ReportId); Add(cmd, "@h", SqlDbType.Char, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.ParamsJson))));
        Add(cmd, "@params", SqlDbType.NVarChar, e.ParamsJson); Add(cmd, "@f", SqlDbType.VarChar, Fmt(e.Format)); Add(cmd, "@n", SqlDbType.Int, e.Rows);
        Add(cmd, "@fn", SqlDbType.NVarChar, e.FileName); Add(cmd, "@pv", SqlDbType.Bit, e.Provisional);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>Rows of the export log (newest first) — read by the rpt.admin.export-log report (R8) and the tests.</summary>
    public async Task<IReadOnlyList<ReportExportLogRecord>> ExportLogAsync(int propertyId, int take, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) log_id, at_utc, principal_id, report_id, params, format, row_count, file_name, provisional FROM rpt.export_log WHERE property_id = @p ORDER BY log_id DESC;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId); Add(cmd, "@n", SqlDbType.Int, Math.Clamp(take, 1, 1000));
        var list = new List<ReportExportLogRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ReportExportLogRecord(rd.GetInt64(0), new DateTimeOffset(rd.GetDateTime(1), TimeSpan.Zero), rd.GetInt32(2), rd.GetString(3), rd.GetString(4),
                ParseFmt(rd.GetString(5)), rd.GetInt32(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetBoolean(8)));
        return list;
    }

    internal static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Current role codes of a user (plat.user_role valid now) — schedule recipient check (catalogue §1.9).</summary>
public sealed class SqlReportPrincipalRoles(SqlConnectionFactory connections, TimeProvider clock) : IReportPrincipalRoles
{
    public async Task<IReadOnlyList<string>> RolesAsync(int principalId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT role_code FROM plat.user_role WHERE user_id = @u AND valid_from_utc <= @now AND (valid_until_utc IS NULL OR valid_until_utc > @now);
            """, conn);
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = clock.GetUtcNow().UtcDateTime;
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }
}
