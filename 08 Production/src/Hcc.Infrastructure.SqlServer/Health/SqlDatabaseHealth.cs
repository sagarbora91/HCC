using System.Globalization;
using Hcc.Application.Documents;
using Hcc.Application.Health;
using Hcc.Contracts.Health;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Documents;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Health;

/// <summary>
/// LF-docdb (LF-CONTRACT §1.9): the "Databases" block of System health. Sizes from <c>sys.database_files</c> inside each database (readable by the
/// service login without extra rights); schema version from the journal; last backup / drill per database from <c>plat.backup_run.database_name</c>;
/// the documents row adds the object count and the daily reference-check result. An unreachable documents database is reported, never thrown.
/// </summary>
public sealed class SqlDatabaseHealth(SqlConnectionFactory main, SqlDocumentConnectionFactory documents) : IDatabaseHealth
{
    private const string FilesSql = """
        SELECT DB_NAME(),
               ISNULL((SELECT SUM(CAST(size AS BIGINT)) * 8192 FROM sys.database_files WHERE type = 0), 0),
               ISNULL((SELECT SUM(CAST(size AS BIGINT)) * 8192 FROM sys.database_files WHERE type = 1), 0),
               CASE WHEN OBJECT_ID('dbo.schema_migration') IS NULL THEN 0 ELSE (SELECT ISNULL(MAX(version), 0) FROM dbo.schema_migration) END;
        """;

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static int Pct(long data) => (int)Math.Min(100, data * 100 / SqlDocumentObjectStore.ExpressLimitBytes);

    public async Task<IReadOnlyList<DatabaseHealthView>> ReadAsync(CancellationToken ct = default)
    {
        var list = new List<DatabaseHealthView>();
        // Main database.
        await using (var conn = await main.OpenAsync(ct).ConfigureAwait(false))
        {
            string name; long data, log; int version;
            await using (var cmd = main.Command(FilesSql, conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                (name, data, log, version) = (r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt32(3));
            }

            var (backup, drill, dangling, orphans) = await BackupAndRefsAsync(conn, name, ct).ConfigureAwait(false);
            list.Add(new DatabaseHealthView(name, "main", version.ToString("0000", CultureInfo.InvariantCulture), true, data, log, SqlDocumentObjectStore.ExpressLimitBytes, Pct(data),
                null, null, null, null, backup, drill));
            // The documents row reuses the reference-check numbers recorded in the main database.
            try
            {
                await using var dconn = await documents.OpenAsync(ct).ConfigureAwait(false);
                string dname; long ddata, dlog; int dversion;
                await using (var cmd = documents.Command(FilesSql, dconn))
                await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    await r.ReadAsync(ct).ConfigureAwait(false);
                    (dname, ddata, dlog, dversion) = (r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt32(3));
                }

                int objects = 0; long content = 0;
                await using (var cmd = documents.Command("IF OBJECT_ID('dob.object') IS NOT NULL SELECT COUNT(*), ISNULL(SUM(CAST(size_bytes AS BIGINT)), 0) FROM dob.object WHERE deleted_utc IS NULL;", dconn))
                await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    if (await r.ReadAsync(ct).ConfigureAwait(false)) (objects, content) = (r.GetInt32(0), r.GetInt64(1));
                var (dbackup, ddrill, _, _) = await BackupAndRefsAsync(conn, dname, ct).ConfigureAwait(false);
                list.Add(new DatabaseHealthView(dname, "documents", dversion.ToString("0000", CultureInfo.InvariantCulture), true, ddata, dlog, SqlDocumentObjectStore.ExpressLimitBytes,
                    Pct(ddata), objects, content, dangling, orphans, dbackup, ddrill));
            }
            catch (Exception ex) when (ex is SqlException or Hcc.Domain.Errors.HccException)
            {
                list.Add(new DatabaseHealthView(new SqlConnectionStringBuilder(documents.Options.ConnectionString).InitialCatalog, "documents", "----", false, 0, 0,
                    SqlDocumentObjectStore.ExpressLimitBytes, 0, null, null, null, null, null, null));
            }
        }

        return list;
    }

    private async Task<(string? Backup, string? Drill, int? Dangling, int? Orphans)> BackupAndRefsAsync(SqlConnection conn, string database, CancellationToken ct)
    {
        await using var cmd = main.Command("""
            SELECT MAX(CASE WHEN kind IN ('Full','Differential') AND succeeded = 1 THEN finished_utc END),
                   MAX(CASE WHEN kind = 'RestoreDrill' AND succeeded = 1 THEN finished_utc END)
              FROM plat.backup_run WHERE database_name = @d;
            SELECT SUM(dangling_refs), SUM(orphan_objects) FROM plat.doc_ref_check;
            """, conn);
        cmd.Parameters.AddWithValue("@d", database);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        string? b = null, d = null;
        if (await r.ReadAsync(ct).ConfigureAwait(false)) { b = r.IsDBNull(0) ? null : Utc(r.GetDateTime(0)); d = r.IsDBNull(1) ? null : Utc(r.GetDateTime(1)); }
        await r.NextResultAsync(ct).ConfigureAwait(false);
        int? dang = null, orph = null;
        if (await r.ReadAsync(ct).ConfigureAwait(false) && !r.IsDBNull(0)) { dang = r.GetInt32(0); orph = r.GetInt32(1); }
        return (b, d, dang, orph);
    }
}
