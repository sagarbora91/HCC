using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Platform.Tests.Migrations;

/// <summary>
/// LF-docdb (LF-CONTRACT 1.3): the HccDocuments migration stream has its own lock file (<c>database/migrations-docs.lock.json</c>, written by
/// <c>tools/migrations-lock.mjs --docs --add</c>) under the same rules as the main one: a locked script never changes, nothing is numbered below
/// the highest locked version, every script is locked. The two streams never share a file.
/// </summary>
public sealed class DocumentsMigrationLockTests
{
    private static IReadOnlyList<MigrationScript> Docs() => new EmbeddedMigrationSource(EmbeddedMigrationSource.DocsPrefix).Load();

    private static MigrationLock.LockFile LoadDocsLock() => MigrationLock.Parse(File.ReadAllText(Path.Combine(MigrationLock.ProductionRoot().FullName, "database", "migrations-docs.lock.json")));

    [Fact]
    public void The_documents_stream_is_embedded_separately_and_locked_in_its_own_file()
    {
        var scripts = Docs();
        var main = new EmbeddedMigrationSource().Load();
        Assert.Equal([1, 2], scripts.Select(s => s.Version));
        Assert.Empty(MigrationLock.Problems(LoadDocsLock(), scripts));
        Assert.Equal(scripts.Count, LoadDocsLock().Migrations.Count);
        var onDisk = Directory.EnumerateFiles(Path.Combine(MigrationLock.ProductionRoot().FullName, "database", "migrations-docs"), "*.sql").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(onDisk, LoadDocsLock().Migrations.Select(e => e.File).Order(StringComparer.Ordinal).ToArray());
        // The main lock knows none of them, and no main script is in the documents stream.
        Assert.DoesNotContain(MigrationLock.Load().Migrations, e => e.File is "0001_objects.sql" or "0002_doc_meta.sql");
        Assert.DoesNotContain(main, s => s.Name is "objects" or "doc_meta");
    }

    [Fact]
    public void A_changed_or_unlocked_documents_script_fails_its_check()
    {
        var scripts = Docs();
        var lockFile = LoadDocsLock();
        var one = scripts.Single(s => s.Version == 1);
        var edited = scripts.Select(s => s.Version == 1 ? new MigrationScript(1, one.Name, one.Sql + "\n-- edited") : s).ToList();
        Assert.StartsWith("(a) 0001_objects.sql changed after it was locked", Assert.Single(MigrationLock.Problems(lockFile, edited)), StringComparison.Ordinal);
        var next = scripts.Append(new MigrationScript(3, "later", "SELECT 1;")).ToList();
        Assert.StartsWith("(c) 0003_later.sql is not in migrations.lock.json", Assert.Single(MigrationLock.Problems(lockFile, next)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_documents_database_holds_only_content_and_its_journal_and_the_main_database_gains_the_refs()
    {
        var all = string.Join("\n", Docs().Select(s => s.Sql));
        Assert.Contains("CREATE SCHEMA dob;", all, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dob.object(", all, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dob.object_content(", all, StringComparison.Ordinal);
        Assert.Contains("kind IN ('id-image','guest-photo','expense-bill','banking-slip','compliance-evidence','counter-attachment','legacy-image')", all, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCES gst.", all, StringComparison.Ordinal);    // no cross-database foreign key (contract 1.7)
        var main = new EmbeddedMigrationSource().Load();
        var refs = main.Single(s => s.Version == 4670).Sql;
        foreach (var t in new[] { "doc.id_capture_image", "ops.expense_bill_photo", "pay.banking_slip", "cmp.evidence_content" })
            Assert.Contains($"ALTER TABLE {t} ADD object_ref UNIQUEIDENTIFIER NULL;", refs, StringComparison.Ordinal);
        Assert.Equal([4670, 4671, 4672, 4673, 4674], main.Where(s => s.Version is >= 4670 and <= 4674).Select(s => s.Version));
        Assert.Contains("database_name", main.Single(s => s.Version == 4671).Sql, StringComparison.Ordinal);
        Assert.Contains("PERM-DOC-006", main.Single(s => s.Version == 4672).Sql, StringComparison.Ordinal);
    }
}
