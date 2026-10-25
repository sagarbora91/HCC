using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Migrations;

/// <summary>One migration script: <c>NNNN_name.sql</c>. The checksum covers the script text with line endings normalized to LF.</summary>
public sealed record MigrationScript(int Version, string Name, string Sql)
{
    public string Checksum { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.Replace("\r\n", "\n", StringComparison.Ordinal))));
}

public interface IMigrationSource
{
    IReadOnlyList<MigrationScript> Load();
}

/// <summary>Migrations embedded in this assembly from <c>08 Production/database/migrations/*.sql</c>.</summary>
public sealed partial class EmbeddedMigrationSource : IMigrationSource
{
    private const string Prefix = "Hcc.Migrations.";

    [GeneratedRegex(@"^(\d{4})_([a-z0-9_]+)\.sql$")]
    private static partial Regex FileName();

    public IReadOnlyList<MigrationScript> Load()
    {
        var asm = typeof(EmbeddedMigrationSource).Assembly;
        var list = new List<MigrationScript>();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var file = res[Prefix.Length..];
            var m = FileName().Match(file);
            if (!m.Success) throw new InvalidOperationException($"Migration file name '{file}' must look like 0001_name.sql.");
            using var stream = asm.GetManifestResourceStream(res) ?? throw new InvalidOperationException($"Missing resource {res}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            list.Add(new MigrationScript(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, reader.ReadToEnd()));
        }

        return Validate(list);
    }

    public static IReadOnlyList<MigrationScript> Validate(IEnumerable<MigrationScript> scripts)
    {
        var ordered = scripts.OrderBy(s => s.Version).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Version != i + 1)
                throw new InvalidOperationException($"Migration versions must be contiguous from 0001; found {ordered[i].Version:0000} at position {i + 1}.");
        }

        return ordered;
    }
}

/// <summary>
/// Gate run before pending migrations are applied to a database that already holds data (DEC-054: backup gate + DBCC CHECKDB).
/// ETP-REUSE: replace/align with ETP migration backup gate when V:\ETP is available.
/// </summary>
public interface IBackupGate
{
    Task BeforeMigrateAsync(SqlConnection connection, string database, int fromVersion, int toVersion, CancellationToken ct);
}

/// <summary>
/// Minimal backup gate: DBCC CHECKDB (must be clean) then a COPY_ONLY, CHECKSUM full backup with verification.
/// <see cref="SqlOptions.BackupDirectory"/> = "NUL" discards the backup stream (tests only).
/// ETP-REUSE: replace/align with ETP backup gate (backup naming, retention, restore drill) when V:\ETP is available.
/// </summary>
public sealed class SqlBackupGate(SqlOptions options) : IBackupGate
{
    public string? LastBackupTarget { get; private set; }

    /// <summary>Server messages from the last gate run (CHECKDB, BACKUP, VERIFYONLY), for the operations log.</summary>
    public IReadOnlyList<string> LastMessages { get; private set; } = [];

    public async Task BeforeMigrateAsync(SqlConnection connection, string database, int fromVersion, int toVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        var quoted = QuoteName(database);
        var messages = new List<string>();
        void OnInfo(object? sender, SqlInfoMessageEventArgs e) => messages.AddRange(e.Errors.Cast<SqlError>().Select(x => x.Message));
        connection.InfoMessage += OnInfo;
        try
        {
            await using (var check = new SqlCommand($"DBCC CHECKDB ({quoted}) WITH NO_INFOMSGS, ALL_ERRORMSGS;", connection) { CommandTimeout = 0 })
                await check.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            string target;
            if (string.Equals(options.BackupDirectory, "NUL", StringComparison.OrdinalIgnoreCase))
            {
                target = "NUL";
            }
            else
            {
                var dir = options.BackupDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    await using var q = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400));", connection);
                    dir = (string?)await q.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("No backup directory configured.");
                }

                var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                target = Path.Combine(dir, $"{database}_premigrate_{fromVersion:0000}_to_{toVersion:0000}_{stamp}.bak");
            }

            await using (var backup = new SqlCommand($"BACKUP DATABASE {quoted} TO DISK = @target WITH COPY_ONLY, CHECKSUM, INIT;", connection) { CommandTimeout = 0 })
            {
                backup.Parameters.AddWithValue("@target", target);
                await backup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (target != "NUL")
            {
                await using var verify = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @target WITH CHECKSUM;", connection) { CommandTimeout = 0 };
                verify.Parameters.AddWithValue("@target", target);
                await verify.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            LastBackupTarget = target;
        }
        finally
        {
            connection.InfoMessage -= OnInfo;
            LastMessages = messages;
        }
    }

    internal static string QuoteName(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}

public sealed record MigrationResult(int FromVersion, int ToVersion, IReadOnlyList<int> Applied, bool BackupTaken);

/// <summary>
/// Applies pending migrations in version order, one transaction per migration (XACT_ABORT ON), under an exclusive
/// application lock so two runners cannot interleave. The journal <c>dbo.schema_migration</c> stores each script's checksum;
/// an applied script whose text changed stops the run (drift), and a database newer than this build is refused.
/// ETP-REUSE: replace/align with ETP migration runner (journal format, CLI, logging) when V:\ETP is available.
/// </summary>
public sealed class MigrationRunner(SqlConnectionFactory connections, IMigrationSource source, IBackupGate backupGate)
{
    private const string LockName = "HCC.MigrationRunner";

    public async Task<MigrationResult> MigrateAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var scripts = source.Load();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await ExecAsync(conn, null, """
            IF OBJECT_ID('dbo.schema_migration') IS NULL
              CREATE TABLE dbo.schema_migration(
                version INT NOT NULL CONSTRAINT PK_schema_migration PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                checksum_sha256 CHAR(64) NOT NULL,
                applied_utc DATETIME2(3) NOT NULL CONSTRAINT DF_schema_migration_applied DEFAULT SYSUTCDATETIME(),
                applied_by NVARCHAR(128) NOT NULL CONSTRAINT DF_schema_migration_by DEFAULT SUSER_SNAME());
            """, ct).ConfigureAwait(false);

        await using (var lockCmd = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000; SELECT @r;", conn))
        {
            lockCmd.Parameters.AddWithValue("@res", LockName);
            var r = (int)(await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new InvalidOperationException($"Could not acquire the migration lock (sp_getapplock returned {r}).");
        }

        try
        {
            var applied = new Dictionary<int, string>();
            await using (var q = new SqlCommand("SELECT version, checksum_sha256 FROM dbo.schema_migration ORDER BY version;", conn))
            await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) applied[rd.GetInt32(0)] = rd.GetString(1);
            }

            var known = scripts.ToDictionary(s => s.Version);
            foreach (var (version, checksum) in applied)
            {
                if (!known.TryGetValue(version, out var s))
                    throw new InvalidOperationException($"Database has migration {version:0000}, which this build does not know. Refusing to run an older build against a newer database.");
                if (!string.Equals(s.Checksum, checksum.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Migration {version:0000}_{s.Name} was changed after it was applied (checksum drift). Add a new migration instead.");
            }

            var from = applied.Count == 0 ? 0 : applied.Keys.Max();
            var pending = scripts.Where(s => !applied.ContainsKey(s.Version)).ToList();
            if (pending.Count == 0) return new MigrationResult(from, from, [], false);

            var backupTaken = false;
            if (from > 0)
            {
                await backupGate.BeforeMigrateAsync(conn, conn.Database, from, pending[^1].Version, ct).ConfigureAwait(false);
                backupTaken = true;
            }

            var done = new List<int>();
            foreach (var s in pending)
            {
                await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    foreach (var batch in SqlBatches.Split(s.Sql)) await ExecAsync(conn, tx, batch, ct).ConfigureAwait(false);
                    await using var j = new SqlCommand("INSERT dbo.schema_migration(version, name, checksum_sha256) VALUES (@v, @n, @c);", conn, tx);
                    j.Parameters.AddWithValue("@v", s.Version);
                    j.Parameters.AddWithValue("@n", s.Name);
                    j.Parameters.AddWithValue("@c", s.Checksum);
                    await j.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    done.Add(s.Version);
                }
                catch
                {
                    if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            return new MigrationResult(from, done[^1], done, backupTaken);
        }
        finally
        {
            await using var unlock = new SqlCommand("EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session';", conn);
            unlock.Parameters.AddWithValue("@res", LockName);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Highest applied migration version, or 0 for an empty database.</summary>
    public static async Task<int> CurrentVersionAsync(SqlConnection connection, CancellationToken ct = default)
    {
        await using var q = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.schema_migration') IS NULL THEN 0 ELSE (SELECT ISNULL(MAX(version), 0) FROM dbo.schema_migration) END;", connection);
        return (int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0);
    }

    private async Task ExecAsync(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = Math.Max(connections.Options.CommandTimeoutSeconds, 120) };
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
