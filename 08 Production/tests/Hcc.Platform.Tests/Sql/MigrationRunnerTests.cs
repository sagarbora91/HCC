using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Platform.Tests.Harness;

namespace Hcc.Platform.Tests.Sql;

/// <summary>Migration runner: journal, checksum drift, version guard, backup gate (CHECKDB + backup), rollback, concurrency.</summary>
[Trait("Category", "Sql")]
public class MigrationRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ExpectedTables =
        ["plat.idempotency", "plat.outbox", "plat.consumer_dedup", "plat.audit_event", "inv.property", "inv.room_type", "inv.room",
         "inv.reservation", "inv.room_night", "bill.folio", "bill.charge", "pay.receipt"];

    [Fact]
    public async Task Fresh_database_gets_all_migrations_once_without_backup_and_rerun_is_a_no_op()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: false, Ct);
        var runner = db.Platform().Migrations;
        var first = await runner.MigrateAsync(Ct);
        Assert.Equal(0, first.FromVersion);
        var embedded = new EmbeddedMigrationSource().Load().Select(s => s.Version).ToArray();
        Assert.Equal(embedded, first.Applied);
        Assert.False(first.BackupTaken);
        foreach (var t in ExpectedTables) Assert.Equal(1, await db.CountAsync($"SELECT CASE WHEN OBJECT_ID('{t}') IS NULL THEN 0 ELSE 1 END", Ct));
        foreach (var s in new[] { "inv", "gst", "bill", "pay", "ops", "cls", "plat" }) Assert.Equal(1, await db.CountAsync($"SELECT COUNT(*) FROM sys.schemas WHERE name = '{s}'", Ct));

        var second = await runner.MigrateAsync(Ct);
        Assert.Empty(second.Applied);
        Assert.Equal(embedded[^1], second.ToVersion);
        var scripts = new EmbeddedMigrationSource().Load();
        foreach (var s in scripts)
            Assert.Equal(s.Checksum, (string?)await db.ScalarAsync($"SELECT checksum_sha256 FROM dbo.schema_migration WHERE version = {s.Version}", Ct));
    }

    [Fact]
    public async Task Fresh_database_applies_every_module_block_in_order_and_cross_module_foreign_keys_resolve()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: false, Ct);
        var result = await db.Platform().Migrations.MigrateAsync(Ct);
        Assert.Equal([1, 2, 100, 101, 200, 300, 400], result.Applied);
        Assert.Equal("1,2,100,101,200,300,400", await db.ScalarAsync("SELECT STRING_AGG(CAST(version AS varchar(10)), ',') WITHIN GROUP (ORDER BY applied_utc, version) FROM dbo.schema_migration", Ct));

        // Module tables exist (P1 access, P2 inventory, P3 stays/housekeeping, P4 billing/cashier).
        foreach (var t in new[] { "plat.app_user", "plat.session", "plat.role_grant", "inv.type_night_count", "inv.room_block", "inv.reservation_night", "bill.quote",
                     "gst.guest", "gst.stay", "ops.hk_task", "ops.room_block", "pay.till", "pay.shift", "pay.cash_movement", "bill.approval" })
            Assert.Equal(1, await db.CountAsync($"SELECT CASE WHEN OBJECT_ID('{t}') IS NULL THEN 0 ELSE 1 END", Ct));

        // Every foreign key is enabled and trusted, and the cross-module references point where the modules expect.
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1", Ct));
        (string Fk, string Parent, string Referenced)[] crossModule =
        [
            ("FK_stay_reservation", "gst.stay", "inv.reservation"), ("FK_stay_folio", "gst.stay", "bill.folio"),
            ("FK_registration_reservation", "gst.registration", "inv.reservation"), ("FK_stay_segment_room", "gst.stay_segment", "inv.room"),
            ("FK_hk_task_property", "ops.hk_task", "inv.property"), ("FK_reservation_quote", "inv.reservation", "bill.quote"),
            ("FK_reservation_night_res", "inv.reservation_night", "inv.reservation"), ("FK_approval_folio", "bill.approval", "bill.folio"),
            ("FK_receipt_shift", "pay.receipt", "pay.shift"), ("FK_receipt_approval", "pay.receipt", "bill.approval"),
            ("FK_charge_approval", "bill.charge", "bill.approval"), ("FK_till_property", "pay.till", "inv.property"),
        ];
        foreach (var (fk, parent, referenced) in crossModule)
            Assert.Equal(1, await db.CountAsync(
                $"SELECT COUNT(*) FROM sys.foreign_keys WHERE name = '{fk}' AND parent_object_id = OBJECT_ID('{parent}') AND referenced_object_id = OBJECT_ID('{referenced}')", Ct));
    }

    [Fact]
    public async Task Changed_applied_script_and_newer_database_are_refused()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: true, Ct);
        await db.ExecAsync("UPDATE dbo.schema_migration SET checksum_sha256 = REPLICATE('0', 64) WHERE version = 2", Ct);
        var drift = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Platform().Migrations.MigrateAsync(Ct));
        Assert.Contains("checksum drift", drift.Message, StringComparison.Ordinal);

        var real = new EmbeddedMigrationSource().Load().Single(s => s.Version == 2).Checksum;
        await db.ExecAsync($"UPDATE dbo.schema_migration SET checksum_sha256 = '{real}' WHERE version = 2; INSERT dbo.schema_migration(version, name, checksum_sha256) VALUES (99, 'future', REPLICATE('A', 64));", Ct);
        var newer = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Platform().Migrations.MigrateAsync(Ct));
        Assert.Contains("does not know", newer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pending_migration_on_a_database_with_data_runs_the_backup_gate_first()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: true, Ct);
        var options = db.Options(backupDirectory: "NUL"); // real DBCC CHECKDB + BACKUP DATABASE, stream discarded
        var gate = new SqlBackupGate(options);
        var extra = new MigrationScript(9001, "test_extra", "CREATE TABLE ops.test_extra(id INT NOT NULL PRIMARY KEY);");
        var runner = new MigrationRunner(new SqlConnectionFactory(options), new ExtraMigrationSource(extra), gate);
        var result = await runner.MigrateAsync(Ct);
        Assert.True(result.BackupTaken);
        Assert.Equal("NUL", gate.LastBackupTarget);
        Assert.Equal([9001], result.Applied);
        Assert.Contains(gate.LastMessages, m => m.StartsWith("BACKUP DATABASE successfully processed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failing_migration_rolls_back_completely_and_stops_the_run()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: true, Ct);
        var options = db.Options();
        var bad = new MigrationScript(9001, "bad", "CREATE TABLE ops.half_done(id INT);\nGO\nSELECT 1/0 AS boom;");
        var later = new MigrationScript(9002, "later", "CREATE TABLE ops.never(id INT);");
        var runner = new MigrationRunner(new SqlConnectionFactory(options), new ExtraMigrationSource(bad, later), new SqlBackupGate(options));
        await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync(Ct));
        Assert.Equal(0, await db.CountAsync("SELECT CASE WHEN OBJECT_ID('ops.half_done') IS NULL THEN 0 ELSE 1 END", Ct));
        Assert.Equal(0, await db.CountAsync("SELECT CASE WHEN OBJECT_ID('ops.never') IS NULL THEN 0 ELSE 1 END", Ct));
        Assert.Equal(LatestEmbedded, await db.CountAsync("SELECT MAX(version) FROM dbo.schema_migration", Ct));
    }

    [Fact]
    public async Task Two_runners_at_once_apply_each_migration_once()
    {
        await using var db = await DisposableDatabase.CreateAsync(migrate: false, Ct);
        var results = await Task.WhenAll(db.Platform().Migrations.MigrateAsync(Ct), db.Platform().Migrations.MigrateAsync(Ct));
        var embedded = new EmbeddedMigrationSource().Load().Select(s => s.Version).ToArray();
        Assert.Equal(embedded, results.SelectMany(r => r.Applied).Order().ToArray());
        Assert.Equal(embedded.Length, await db.CountAsync("SELECT COUNT(*) FROM dbo.schema_migration", Ct));
    }

    [Fact]
    public void Migration_versions_are_unique_and_ascending_with_gaps_between_blocks()
    {
        var versions = new EmbeddedMigrationSource().Load().Select(s => s.Version).ToArray();
        Assert.Equal([1, 2], versions[..2]);
        Assert.Equal(versions.Order().Distinct().ToArray(), versions);
        var withGap = new ExtraMigrationSource(new MigrationScript(9001, "gap", "SELECT 1;")).Load();
        Assert.Equal(9001, withGap[^1].Version);
        Assert.Throws<InvalidOperationException>(() => new ExtraMigrationSource(new MigrationScript(2, "duplicate", "SELECT 1;")).Load());
        Assert.Throws<InvalidOperationException>(() => EmbeddedMigrationSource.Validate([new MigrationScript(0, "zero", "SELECT 1;")]));
    }

    private static int LatestEmbedded => new EmbeddedMigrationSource().Load()[^1].Version;
}
