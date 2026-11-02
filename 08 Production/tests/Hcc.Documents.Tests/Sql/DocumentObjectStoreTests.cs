#pragma warning disable xUnit1051 // helper calls are short-lived and do not need per-call cancellation
using System.Data;
using System.Text;
using Hcc.Application.Documents;
using Hcc.Contracts;
using Hcc.Documents.Tests.Harness;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.DocumentObjects;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Documents.Tests.Sql;

/// <summary>LF-docdb (LF-CONTRACT §1): the second database, its object store, the idempotent move job, the sweep and the reference check, on a real HccTest_* pair.</summary>
[Trait("Category", "Sql")]
public class DocumentObjectStoreTests : IAsyncLifetime
{
    private DocumentsDatabases _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _db = await DocumentsDatabases.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IDocumentObjectStore Store => _db.Platform.DocumentObjects;

    private NewDocumentObject Plain(DocumentObjectKind kind = DocumentObjectKind.ExpenseBill, int size = 40, string owner = "ops.expense", long id = 1)
        => new(_db.PropertyId, kind, owner, id, "image/jpeg", Enumerable.Range(0, size).Select(i => (byte)i).ToArray(), null, _db.UserId);

    [Fact]
    public async Task Both_databases_have_their_own_journal_and_the_derived_name()
    {
        Assert.Equal(_db.DocumentsName, _db.Platform.Documents.Database);
        Assert.Equal(2, await _db.DocsLongAsync("SELECT COUNT(*) FROM dbo.schema_migration;"));
        Assert.Equal(0, await _db.MainLongAsync("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'dob';"));
        Assert.Equal(2, await _db.DocsLongAsync("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'dob';"));
    }

    [Fact]
    public void Derived_name_must_start_with_Hcc_and_differ_from_the_main_catalog()
    {
        var ok = new SqlOptions { ConnectionString = "Server=.;Database=Hcc;Integrated Security=true" };
        Assert.Contains("HccDocuments", DocumentsConnection.Resolve(ok));
        Assert.Throws<InvalidOperationException>(() => DocumentsConnection.Resolve(new SqlOptions { ConnectionString = "Server=.;Database=Hcc;Integrated Security=true", DocumentsConnectionString = "Server=.;Database=Hcc;Integrated Security=true" }));
        Assert.Throws<InvalidOperationException>(() => DocumentsConnection.Resolve(new SqlOptions { ConnectionString = "Server=.;Database=Hcc;Integrated Security=true", DocumentsConnectionString = "Server=.;Database=Other;Integrated Security=true" }));
        Assert.Contains("MyHcc", DocumentsConnection.Resolve(new SqlOptions { ConnectionString = "Server=.;Database=Hcc;Integrated Security=true", DocumentsConnectionString = "Server=.;Database=HccMyDocs;Integrated Security=true" }).Replace("HccMyDocs", "MyHcc"));
    }

    [Fact]
    public async Task Put_get_info_for_plain_and_sealed_objects()
    {
        var r = await Store.PutAsync(Plain(), Ct);
        var got = (await Store.GetAsync(r, Ct))!.Value;
        Assert.Equal(40, got.Content!.Length);
        Assert.Null(got.SealedText);
        Assert.Equal((DocumentObjectKind.ExpenseBill, "image/jpeg", 40, false), (got.Info.Kind, got.Info.ContentType, got.Info.SizeBytes, got.Info.Sealed));
        var s = await Store.PutAsync(new NewDocumentObject(_db.PropertyId, DocumentObjectKind.IdImage, "doc.id_capture", 7, "image/png", null, "hcc1:k1:SEALED", _db.UserId), Ct);
        var gs = (await Store.GetAsync(s, Ct))!.Value;
        Assert.Equal("hcc1:k1:SEALED", gs.SealedText);
        Assert.Null(gs.Content);
        Assert.True(gs.Info.Sealed);
        var hash = (byte[])(await _db.DocsScalarAsync($"SELECT sha256 FROM dob.object WHERE object_ref = '{s}'", Ct))!;
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("hcc1:k1:SEALED")), hash);
        Assert.Null(await Store.GetAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Put_needs_exactly_one_content_form_and_reseal_needs_a_sealed_object()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.PutAsync(new NewDocumentObject(_db.PropertyId, DocumentObjectKind.IdImage, "x", 1, "image/png", null, null, 1), Ct));
        var plain = await Store.PutAsync(Plain(), Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store.ResealAsync(plain, "new", Ct));
        var sealedRef = await Store.PutAsync(new NewDocumentObject(_db.PropertyId, DocumentObjectKind.IdImage, "doc.id_capture", 1, "image/png", null, "old", 1), Ct);
        await Store.ResealAsync(sealedRef, "new-sealed", Ct);
        Assert.Equal("new-sealed", (await Store.GetAsync(sealedRef, Ct))!.Value.SealedText);
    }

    [Fact]
    public async Task Release_is_idempotent_and_hides_the_object()
    {
        var r = await Store.PutAsync(Plain(), Ct);
        await Store.ReleaseAsync(r, Ct);
        await Store.ReleaseAsync(r, Ct);
        Assert.Null(await Store.GetAsync(r, Ct));
        Assert.NotNull((await Store.InfoAsync(r, Ct))!.DeletedUtc);
        Assert.Empty(await Store.ListAsync(_db.PropertyId, DocumentObjectKind.ExpenseBill, null, 10, Ct));
    }

    [Fact]
    public async Task Sweep_removes_unreferenced_old_objects_and_deleted_ones_but_keeps_live_and_young()
    {
        var keep = await Store.PutAsync(Plain(id: 1), Ct);
        var orphan = await Store.PutAsync(Plain(id: 2), Ct);
        var released = await Store.PutAsync(Plain(id: 3), Ct);
        await Store.ReleaseAsync(released, Ct);
        // Young objects (inside the 24 h margin) are never swept.
        Assert.Equal(0, await Store.SweepAsync(_db.PropertyId, DocumentObjectKind.ExpenseBill, DateTime.UtcNow.AddHours(-24), new HashSet<Guid> { keep }, Ct));
        var removed = await Store.SweepAsync(_db.PropertyId, DocumentObjectKind.ExpenseBill, DateTime.UtcNow.AddMinutes(1), new HashSet<Guid> { keep }, Ct);
        Assert.Equal(2, removed);
        Assert.NotNull(await Store.GetAsync(keep, Ct));
        Assert.Null(await Store.InfoAsync(orphan, Ct));
        Assert.Null(await Store.InfoAsync(released, Ct));
        Assert.Equal(1, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object_content;", Ct));
    }

    [Fact]
    public async Task Size_reports_database_files_and_the_object_counts()
    {
        await Store.PutAsync(Plain(size: 100), Ct);
        await Store.PutAsync(Plain(size: 50), Ct);
        var s = await Store.SizeAsync(Ct);
        Assert.Equal(_db.DocumentsName, s.Database);
        Assert.Equal((2, 150L, 50L * 1024 * 1024 * 1024), (s.ObjectCount, s.ContentBytes, s.LimitBytes));
        Assert.True(s.DataBytes > 0 && s.LogBytes > 0);
    }

    [Fact]
    public async Task An_unreachable_documents_database_fails_with_the_documents_message()
    {
        await using var gone = new SqlPlatformGone(_db);
        var ex = await Assert.ThrowsAsync<HccException>(() => gone.Store.PutAsync(Plain(), Ct));
        Assert.Equal(HccErrorCode.UNAVAILABLE, ex.Code);
        Assert.Equal(SqlDocumentConnectionFactory.UnavailableText, ex.Message);
    }

    private sealed class SqlPlatformGone : IAsyncDisposable
    {
        public SqlPlatformGone(DocumentsDatabases db)
        {
            var p = new Hcc.Infrastructure.SqlServer.SqlPlatform(new SqlOptions
            {
                ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") ?? @".\SQLEXPRESS", "HccTest_00000000000000_deadbeef"),
                CommandTimeoutSeconds = 5,
            });
            Store = p.DocumentObjects;
            _ = db;
        }

        public IDocumentObjectStore Store { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── the four stores: ref path and NULL-ref fallback ──

    private async Task<long> SeedExpenseAsync() => await _db.MainLongAsync($"INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc) OUTPUT INSERTED.expense_id VALUES ({_db.PropertyId}, N'V', N'C', 1000, 'Cash', '2026-10-24', 'Draft', {_db.UserId}, '2026-10-24');", Ct);

    [Fact]
    public async Task Expense_bill_photo_goes_to_the_documents_database_and_old_rows_still_read_inline()
    {
        var id = await SeedExpenseAsync();
        var inlineStore = new Hcc.Infrastructure.SqlServer.Expenses.SqlExpenseStore(_db.Platform.Connections);                       // pre-LF wiring: inline column
        var refStore = new Hcc.Infrastructure.SqlServer.Expenses.SqlExpenseStore(_db.Platform.Connections, _db.Platform.DocumentObjects);
        var bytes = new byte[] { 1, 2, 3, 4 };
        // An old (inline, NULL-ref) row reads through the new store.
        await _db.MainScalarAsync($"INSERT ops.expense_bill_photo(expense_id, content_type, size_bytes, image, updated_by, updated_utc) VALUES ({id}, 'image/jpeg', 4, 0x01020304, {_db.UserId}, '2026-10-24');", Ct);
        Assert.Equal(bytes, (await refStore.BillPhotoAsync(_db.PropertyId, id, Ct))!.Value.Image);
        Assert.Equal(bytes, (await inlineStore.BillPhotoAsync(_db.PropertyId, id, Ct))!.Value.Image);
        // A second expense through the ref path leaves no content in Hcc.
        var id2 = await SeedExpenseAsync();
        await _db.Platform.Commands.RunAsync(new Hcc.Application.Commands.CommandContext(_db.PropertyId, _db.UserId, "T", Guid.NewGuid().ToString("N")), new { id2 },
            async (tx, c) => { await refStore.PutBillPhotoAsync(tx, id2, "image/png", [9, 8, 7], _db.UserId, DateTime.UtcNow, c); await tx.AuditAsync("TEST_PUT", "expense:" + id2, "first", c); return new { ok = true }; }, Ct);
        Assert.Equal(1, await _db.MainLongAsync($"SELECT COUNT(*) FROM ops.expense_bill_photo WHERE expense_id = {id2} AND image IS NULL AND object_ref IS NOT NULL;", Ct));
        Assert.Equal(new byte[] { 9, 8, 7 }, (await refStore.BillPhotoAsync(_db.PropertyId, id2, Ct))!.Value.Image);
        Assert.Equal(1, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object WHERE kind = 'expense-bill';", Ct));
        // Replacing the photo publishes a release for the old object.
        await _db.Platform.Commands.RunAsync(new Hcc.Application.Commands.CommandContext(_db.PropertyId, _db.UserId, "T", Guid.NewGuid().ToString("N")), new { id2, again = true },
            async (tx, c) => { await refStore.PutBillPhotoAsync(tx, id2, "image/png", [5, 5], _db.UserId, DateTime.UtcNow, c); await tx.AuditAsync("TEST_PUT", "expense:" + id2, "again", c); return new { ok = true }; }, Ct);
        Assert.Equal(1, await _db.MainLongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'DocumentObjectReleased';", Ct));
        var projector = new DocumentReleaseProjector(_db.Platform.Outbox, _db.Platform.DocumentObjects);
        Assert.Equal(1, await projector.RunOnceAsync(100, Ct));
        Assert.Equal(1, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object WHERE deleted_utc IS NOT NULL;", Ct));
    }

    [Fact]
    public async Task A_dangling_ref_reads_as_DOC_MISSING()
    {
        var id = await SeedExpenseAsync();
        await _db.MainScalarAsync($"INSERT ops.expense_bill_photo(expense_id, content_type, size_bytes, image, object_ref, updated_by, updated_utc) VALUES ({id}, 'image/jpeg', 4, NULL, NEWID(), {_db.UserId}, '2026-10-24');", Ct);
        var store = new Hcc.Infrastructure.SqlServer.Expenses.SqlExpenseStore(_db.Platform.Connections, _db.Platform.DocumentObjects);
        var ex = await Assert.ThrowsAsync<HccException>(() => store.BillPhotoAsync(_db.PropertyId, id, Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, ex.Code);
        Assert.Equal("DOC_MISSING", ex.Details!["reason"].GetString());
    }

    // ── the move job ──

    private async Task SeedFourTablesAsync(int each)
    {
        await _db.MainScalarAsync("ALTER TABLE pay.cash_transfer NOCHECK CONSTRAINT ALL;", Ct);
        for (var i = 1; i <= each; i++)
        {
            var cap = await _db.MainLongAsync($"INSERT doc.id_capture(capture_ref, property_id, guest_id, doc_kind, content_type, size_bytes, status, captured_date, captured_utc, captured_by) OUTPUT INSERTED.capture_id VALUES (NEWID(), {_db.PropertyId}, {_db.GuestId}, 'aadhaar', 'image/png', 10, 'Captured', '2026-10-24', '2026-10-24', {_db.UserId});", Ct);
            await _db.MainScalarAsync($"INSERT doc.id_capture_image(capture_id, sealed) VALUES ({cap}, 'hcc1:k1:IMG{i}');", Ct);
            var exp = await SeedExpenseAsync();
            await _db.MainScalarAsync($"INSERT ops.expense_bill_photo(expense_id, content_type, size_bytes, image, updated_by, updated_utc) VALUES ({exp}, 'image/jpeg', 3, 0x0A0B0C, {_db.UserId}, '2026-10-24');", Ct);
            var tr = await _db.MainLongAsync($"INSERT pay.cash_transfer(property_id, kind, status, business_date, amount_paise, from_till_id, from_shift_id, bank, slip_no, recorded_by, recorded_utc) OUTPUT INSERTED.transfer_id VALUES ({_db.PropertyId}, 'banking', 'Pending', '2026-10-24', 100, 1, 1, N'B', N'S{i}', {_db.UserId}, '2026-10-24');", Ct);
            await _db.MainScalarAsync($"INSERT pay.banking_slip(transfer_id, content_type, size_bytes, image) VALUES ({tr}, 'image/png', 2, 0xAABB);", Ct);
            var ev = await _db.MainLongAsync($"INSERT cmp.evidence(property_id, fy_start, item_id, file_name, content_type, size_bytes, added_by, added_utc) OUTPUT INSERTED.evidence_id VALUES ({_db.PropertyId}, 2026, 'gstr1-{i}', N'f.pdf', 'application/pdf', 4, {_db.UserId}, '2026-10-24');", Ct);
            await _db.MainScalarAsync($"INSERT cmp.evidence_content(evidence_id, content) VALUES ({ev}, 0x25504446);", Ct);
        }
    }

    [Fact]
    public async Task Move_job_moves_all_four_tables_leaves_no_content_in_Hcc_and_a_second_run_moves_nothing()
    {
        await SeedFourTablesAsync(3);
        var first = await _db.Platform.DocumentMove.RunAsync(null, Ct);
        Assert.Equal((12, 0), (first.Moved, first.Remaining));
        Assert.Equal(12, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object;", Ct));
        Assert.Equal(0, await _db.MainLongAsync("SELECT COUNT(*) FROM doc.id_capture_image WHERE sealed IS NOT NULL;", Ct)
            + await _db.MainLongAsync("SELECT COUNT(*) FROM ops.expense_bill_photo WHERE image IS NOT NULL;", Ct)
            + await _db.MainLongAsync("SELECT COUNT(*) FROM pay.banking_slip WHERE image IS NOT NULL;", Ct)
            + await _db.MainLongAsync("SELECT COUNT(*) FROM cmp.evidence_content WHERE content IS NOT NULL;", Ct));
        // Every moved ref resolves, and the content is what was there.
        var refs = new List<Guid>();
        await using (var conn = await _db.Platform.Connections.OpenAsync(Ct))
        await using (var cmd = new SqlCommand("SELECT object_ref FROM doc.id_capture_image UNION ALL SELECT object_ref FROM ops.expense_bill_photo UNION ALL SELECT object_ref FROM pay.banking_slip UNION ALL SELECT object_ref FROM cmp.evidence_content;", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(Ct))
            while (await rd.ReadAsync(Ct)) refs.Add(rd.GetGuid(0));
        Assert.Equal(12, refs.Count);
        foreach (var r in refs) Assert.NotNull(await Store.GetAsync(r, Ct));
        Assert.Equal("hcc1:k1:IMG1", (await Store.GetAsync((Guid)(await _db.MainScalarAsync("SELECT TOP 1 object_ref FROM doc.id_capture_image ORDER BY capture_id;", Ct))!, Ct))!.Value.SealedText);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await Store.GetAsync((Guid)(await _db.MainScalarAsync("SELECT TOP 1 object_ref FROM cmp.evidence_content;", Ct))!, Ct))!.Value.Content);
        Assert.Equal("main_database:" + _db.MainName, "main_database:" + await _db.DocsScalarAsync("SELECT meta_value FROM plat.doc_meta WHERE meta_key = 'main_database';", Ct));
        var second = await _db.Platform.DocumentMove.RunAsync(null, Ct);
        Assert.Equal((0, 0), (second.Moved, second.Remaining));
        Assert.Equal(12, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object;", Ct));
        Assert.Equal(2, await _db.MainLongAsync("SELECT COUNT(*) FROM plat.doc_move_run WHERE status = 'Move';", Ct));
    }

    [Fact]
    public async Task Sweep_removes_an_orphan_whose_Hcc_commit_never_happened()
    {
        await SeedFourTablesAsync(2);
        var run = await _db.Platform.DocumentMove.RunAsync(null, Ct);
        Assert.Equal(0, run.Remaining);
        // An orphan (object written, Hcc commit never happened) is removed by the sweep once past the margin; the live ones are kept.
        var orphan = await Store.PutAsync(Plain(DocumentObjectKind.ExpenseBill, 9, id: 999), Ct);
        Assert.NotNull(await Store.InfoAsync(orphan, Ct));
        var young = await _db.Platform.DocumentSweep.RunAsync(null, Ct);
        Assert.Equal(0, young.Removed);
        var swept = await _db.Platform.DocumentSweep.RunAsync(DateTime.UtcNow.AddHours(25), Ct);
        Assert.Equal(1, swept.Removed);
        Assert.Equal(8, await _db.DocsLongAsync("SELECT COUNT(*) FROM dob.object;", Ct));
        Assert.Equal(1, await _db.MainLongAsync("SELECT COUNT(*) FROM plat.doc_move_run WHERE status = 'Sweep' AND cleared_rows = 1;", Ct));
    }

    [Fact]
    public async Task Ref_check_counts_dangling_refs_and_orphans_and_keeps_the_daily_result()
    {
        await SeedFourTablesAsync(1);
        await _db.Platform.DocumentMove.RunAsync(null, Ct);
        var clean = await _db.Platform.DocumentRefs.RunAsync(true, Ct);
        Assert.Equal((0, 0), (clean.Dangling, clean.Orphans));
        Assert.Equal(_db.MainName, clean.MainDatabaseNote);
        // Delete one object behind Hcc's back: the Hcc row now dangles.
        await _db.DocsScalarAsync("DELETE dob.object WHERE kind = 'banking-slip';", Ct);
        var broken = await _db.Platform.DocumentRefs.RunAsync(true, Ct);
        Assert.Equal(1, broken.Dangling);
        Assert.Equal(1, await _db.MainLongAsync("SELECT SUM(dangling_refs) FROM plat.doc_ref_check;", Ct));
        var health = await new Hcc.Infrastructure.SqlServer.Health.SqlDatabaseHealth(_db.Platform.Connections, _db.Platform.Documents).ReadAsync(Ct);
        Assert.Equal(["main", "documents"], health.Select(h => h.Kind));
        Assert.All(health, h => Assert.True(h.Reachable));
        Assert.Equal(1, health[1].DanglingRefs);
        Assert.Equal(3, health[1].ObjectCount);
        Assert.Equal("0002", health[1].SchemaVersion);
    }

    [Fact]
    public async Task Schema_guard_status_sees_a_documents_database_that_is_behind()
    {
        var scripts = new EmbeddedMigrationSource(EmbeddedMigrationSource.DocsPrefix).Load();
        Assert.Equal([1, 2], scripts.Select(s => s.Version));
        await using var conn = await _db.Platform.Documents.OpenAsync(Ct);
        Assert.False((await MigrationRunner.StatusAsync(conn, scripts, Ct)).IsBehind);
        await _db.DocsScalarAsync("DELETE dbo.schema_migration WHERE version = 2;", Ct);
        var behind = await MigrationRunner.StatusAsync(conn, scripts, Ct);
        Assert.True(behind.IsBehind);
        Assert.Equal([2], behind.Missing);
    }
}
