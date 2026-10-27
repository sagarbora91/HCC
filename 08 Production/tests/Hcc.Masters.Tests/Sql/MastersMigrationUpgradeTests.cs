using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Masters.Tests.Sql;

/// <summary>
/// MA-11 review (merge report §6.5): the masters migrations (2810–2891 and 3800) apply cleanly to a database upgraded from main BEFORE the
/// masters wave — main after Wave B (2801/2802 and 2900–3701 already applied, so the runner fills 2810…2891 in below them) and the older main
/// (up to 2702) — and the upgraded schema is identical to a fresh database's; existing rows survive (Wave B holidays become paid master rows,
/// expenses keep their methods, the money masters are seeded). Every foreign key is trusted and points from the module tables to the masters.
/// </summary>
[Trait("Category", "Sql")]
public sealed partial class MastersMigrationUpgradeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    /// <summary>The masters-wave migrations (and MA-11's) that a database from main before the wave does not have.</summary>
    private static readonly int[] MastersWave = [2810, 2820, 2830, 2840, 2850, 2860, 2870, 2871, 2890, 2891, 3800];

    /// <summary>The highest migration main had after Wave B. Later migrations (3910 …) came after the masters wave and may build on its tables
    /// (4110, SCC leave port: hr.leave_type, hr.shift_pattern, hr.holiday kinds) — they run in the upgrade, in order, not in the old database.</summary>
    private const int WaveBLast = 3701;

    private sealed class FilteredSource(Func<int, bool> keep) : IMigrationSource
    {
        public IReadOnlyList<MigrationScript> Load() => new EmbeddedMigrationSource().Load().Where(s => keep(s.Version)).ToList();
    }

    private sealed class Db : IAsyncDisposable
    {
        private readonly string _server;

        public string Name { get; }

        public SqlPlatform Platform { get; }

        private Db(string server, string name)
        {
            (_server, Name) = (server, name);
            Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        }

        public static async Task<Db> CreateAsync()
        {
            var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
            var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
            if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
            await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master"));
            await conn.OpenAsync(Ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(Ct);
            return new Db(server, name);
        }

        public MigrationRunner Runner(Func<int, bool> keep) => new(Platform.Connections, new FilteredSource(keep), Platform.BackupGate);

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var conn = await Platform.Connections.OpenAsync(Ct);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            return await cmd.ExecuteScalarAsync(Ct);
        }

        public async Task<int> CountAsync(string sql) => Convert.ToInt32(await ScalarAsync(sql), CultureInfo.InvariantCulture);

        /// <summary>Tables, columns (type, length, nullability), check / default / foreign-key / index definitions — the schema as text lines.</summary>
        public async Task<List<string>> SchemaAsync()
        {
            // One query per object kind, joined in C# (sys names and definitions have different collations).
            string[] queries =
            [
                """
                SELECT 'C', s.name + '.' + t.name, c.name, ty.name + '(' + CAST(c.max_length AS varchar(10)) + ')' + IIF(c.is_nullable = 1, ' null', '')
                  FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                """,
                "SELECT 'K', OBJECT_SCHEMA_NAME(parent_object_id) + '.' + OBJECT_NAME(parent_object_id), name, definition FROM sys.check_constraints",
                "SELECT 'D', OBJECT_SCHEMA_NAME(parent_object_id) + '.' + OBJECT_NAME(parent_object_id), name, definition FROM sys.default_constraints",
                "SELECT 'F', OBJECT_SCHEMA_NAME(parent_object_id) + '.' + OBJECT_NAME(parent_object_id), name, OBJECT_SCHEMA_NAME(referenced_object_id) + '.' + OBJECT_NAME(referenced_object_id) FROM sys.foreign_keys",
                """
                SELECT 'I', OBJECT_SCHEMA_NAME(i.object_id) + '.' + OBJECT_NAME(i.object_id), i.name,
                       (SELECT STRING_AGG(c.name + IIF(ic.is_included_column = 1, '+', ''), ',') WITHIN GROUP (ORDER BY ic.is_included_column, ic.key_ordinal, c.name)
                          FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                         WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) + ' ' + IIF(i.is_unique = 1, 'unique', '')
                  FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.name IS NOT NULL
                """,
                "SELECT 'I', OBJECT_SCHEMA_NAME(i.object_id) + '.' + OBJECT_NAME(i.object_id), i.name, i.filter_definition FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.has_filter = 1",
                "SELECT 'P', SCHEMA_NAME(schema_id), name, type FROM sys.objects WHERE type IN ('P', 'TR', 'V', 'FN', 'IF', 'TF')",
            ];
            var list = new List<string>();
            await using var conn = await Platform.Connections.OpenAsync(Ct);
            foreach (var q in queries)
            {
                await using var cmd = new SqlCommand(q, conn) { CommandTimeout = 120 };
                await using var rd = await cmd.ExecuteReaderAsync(Ct);
                while (await rd.ReadAsync(Ct))
                    list.Add(string.Join(" ", Enumerable.Range(0, rd.FieldCount).Select(i => rd.IsDBNull(i) ? "" : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture))));
            }

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public async ValueTask DisposeAsync()
        {
            if (!NamePattern().IsMatch(Name)) return;
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", conn)
                { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Rows a hotel had before the masters wave: a property, expenses with the old methods, and (Wave B) a paid holiday.</summary>
    private static async Task<int> SeedOldHotelAsync(Db db)
    {
        var p = await db.CountAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('OLD', N'Old Residency (fictional)', 'Asia/Kolkata', '2026-10-24');");
        await db.ScalarAsync($"""
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc) VALUES
              ({p}, N'Morning Supplies (fictional)', N'Supplies', 50000, 'Cash', '2026-10-24', 'Pending', 1, '2026-10-24'),
              ({p}, N'Fresh Linen (fictional)', N'Laundry', 80000, 'Vendor credit', '2026-10-24', 'Pending', 1, '2026-10-24');
            IF COL_LENGTH('hr.holiday', 'holiday_date') IS NOT NULL
              INSERT hr.holiday(property_id, holiday_date, name, set_by, set_utc) VALUES ({p}, '2026-08-15', N'Independence Day', 1, '2026-08-01');
            """);
        return p;
    }

    private static async Task UpgradeAndCompareAsync(Func<int, bool> before, bool waveB)
    {
        await using var old = await Db.CreateAsync();
        var first = await old.Runner(before).MigrateAsync(Ct);
        Assert.DoesNotContain(first.Applied, v => MastersWave.Contains(v));
        var p = await SeedOldHotelAsync(old);

        var up = await old.Platform.Migrations.MigrateAsync(Ct);                       // the full embedded set: what `Hcc.Api migrate` runs
        Assert.True(up.BackupTaken);                                                  // DEC-054 gate before touching a database with data
        Assert.Equal(up.Applied.Order(), up.Applied);                                 // ascending, the masters wave fills in below 2900
        Assert.Contains(2890, up.Applied);
        Assert.Contains(2891, up.Applied);
        Assert.Contains(3800, up.Applied);
        Assert.Empty((await old.Platform.Migrations.MigrateAsync(Ct)).Applied);       // re-run is a no-op

        // Old rows survive and the masters are seeded for the existing property.
        Assert.Equal(6, await old.CountAsync($"SELECT COUNT(*) FROM mst.payment_method WHERE property_id = {p}"));
        Assert.Equal(2, await old.CountAsync($"SELECT COUNT(*) FROM ops.expense_vendor WHERE property_id = {p}"));
        Assert.Equal(new[] { "Cash", "Vendor credit" }, (await old.ScalarAsync($"SELECT STRING_AGG(method, ',') WITHIN GROUP (ORDER BY method) FROM ops.expense WHERE property_id = {p}") as string)!.Split(','));
        if (waveB) Assert.Equal(1, await old.CountAsync($"SELECT COUNT(*) FROM hr.holiday WHERE property_id = {p} AND code = '2026-08-15' AND kind = 'paid' AND active = 1"));
        // 2890: an Owner-added method is accepted by the database.
        await old.ScalarAsync($"INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc) VALUES ({p}, N'Gas (fictional)', N'Supplies', 1000, N'Paytm wallet', '2026-10-24', 'Pending', 1, '2026-10-24');");
        Assert.Equal(0, await old.CountAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1"));
        Assert.Equal(0, await old.CountAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1"));

        // The upgraded schema equals a fresh database's, line for line.
        await using var fresh = await Db.CreateAsync();
        await fresh.Platform.Migrations.MigrateAsync(Ct);
        var a = await fresh.SchemaAsync();
        var b = await old.SchemaAsync();
        Assert.Equal(string.Join("\n", a.Except(b)), string.Join("\n", b.Except(a)));
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Upgrade_from_main_after_wave_B_applies_the_masters_wave_below_2900_and_matches_a_fresh_database() =>
        await UpgradeAndCompareAsync(v => !MastersWave.Contains(v) && v <= WaveBLast, waveB: true);

    [Fact]
    public async Task Upgrade_from_the_older_main_up_to_2702_matches_a_fresh_database() =>
        await UpgradeAndCompareAsync(v => v <= 2702, waveB: false);

    [Fact]
    public async Task Fresh_database_masters_foreign_keys_point_from_the_modules_to_the_masters_and_every_migration_is_listed_in_the_solution()
    {
        await using var fresh = await Db.CreateAsync();
        var r = await fresh.Platform.Migrations.MigrateAsync(Ct);
        Assert.Equal(new EmbeddedMigrationSource().Load().Select(s => s.Version), r.Applied);
        // Masters only reference the property, other masters, or the master history; module tables reference the masters (never the other way).
        Assert.Equal(0, await fresh.CountAsync("""
            SELECT COUNT(*) FROM sys.foreign_keys
             WHERE OBJECT_SCHEMA_NAME(parent_object_id) = 'mst'
               AND NOT (OBJECT_SCHEMA_NAME(referenced_object_id) = 'mst' OR OBJECT_NAME(referenced_object_id) IN ('property', 'room_type', 'app_user'))
            """));
        foreach (var (parent, target) in new[] { ("inv.room", "mst.floor"), ("mst.payment_method", "mst.bank_account") })
            Assert.True(await fresh.CountAsync($"SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('{parent}') AND referenced_object_id = OBJECT_ID('{target}')") >= 1, $"{parent} -> {target}");

        // Hcc.slnx lists every migration file (the shared-file rule): find the solution above the test binaries.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hcc.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var slnx = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "Hcc.slnx"), Ct);
        foreach (var f in Directory.GetFiles(Path.Combine(dir.FullName, "database", "migrations"), "*.sql"))
            Assert.Contains($"database/migrations/{Path.GetFileName(f)}", slnx, StringComparison.Ordinal);
    }
}
