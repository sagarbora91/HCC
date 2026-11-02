using System.Data;
using System.Globalization;
using Hcc.Application.Documents;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Documents;

public sealed record DocumentMoveResult(int Moved, int Cleared, int Remaining, IReadOnlyList<string> StuckTables);

public sealed record DocumentSweepResult(int Removed);

public sealed record DocumentKindCheck(DocumentObjectKind Kind, int Referenced, int Dangling, int Orphans);

public sealed record DocumentRefCheckResult(IReadOnlyList<DocumentKindCheck> Kinds, string? MainDatabaseNote)
{
    public int Dangling => Kinds.Sum(k => k.Dangling);

    public int Orphans => Kinds.Sum(k => k.Orphans);
}

/// <summary>Shared journal write: <c>plat.doc_move_run</c> (migration 4673).</summary>
internal static class DocumentRunLog
{
    public static async Task WriteAsync(SqlConnectionFactory main, string status, DateTime started, int moved, int cleared, int remaining, string? error, CancellationToken ct)
    {
        await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = main.Command("""
            INSERT plat.doc_move_run(started_utc, finished_utc, moved_rows, cleared_rows, remaining_rows, status, error)
            VALUES (@s, SYSUTCDATETIME(), @m, @c, @r, @st, @e);
            """, conn);
        cmd.Parameters.Add("@s", SqlDbType.DateTime2).Value = started;
        cmd.Parameters.Add("@m", SqlDbType.Int).Value = moved;
        cmd.Parameters.Add("@c", SqlDbType.Int).Value = cleared;
        cmd.Parameters.Add("@r", SqlDbType.Int).Value = remaining;
        cmd.Parameters.Add("@st", SqlDbType.VarChar, 12).Value = status;
        cmd.Parameters.Add("@e", SqlDbType.NVarChar, 400).Value = (object?)error ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<int>> PropertiesAsync(SqlConnectionFactory main, CancellationToken ct)
    {
        await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = main.Command("SELECT property_id FROM inv.property ORDER BY property_id;", conn);
        var list = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(r.GetInt32(0));
        return list;
    }

    public static async Task<HashSet<Guid>> RefsAsync(SqlConnectionFactory main, DocumentContentTable t, int propertyId, CancellationToken ct)
    {
        await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = main.Command(t.RefsSql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var set = new HashSet<Guid>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) set.Add(r.GetGuid(0));
        return set;
    }
}

/// <summary>
/// LF-docdb (contract §1.6): the idempotent move of the four content tables to <c>HccDocuments</c>. Batches of 50 rows by key, one <c>Hcc</c>
/// transaction per batch; the object is written first in its own committed transaction (§1.8), then the row gets its ref and loses its content.
/// A failing batch rolls back its <c>Hcc</c> rows; the objects already written are orphans the sweep removes after 24 hours. Re-running continues
/// with the rows whose <c>object_ref</c> is NULL.
/// </summary>
public sealed class DocumentMoveJob(SqlConnectionFactory main, SqlDocumentConnectionFactory documents, IDocumentObjectStore store)
{
    public const int BatchSize = 50;

    public async Task<DocumentMoveResult> RunAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var moved = 0;
        var stuck = new List<string>();
        try
        {
            await WriteMainNoteAsync(ct).ConfigureAwait(false);
            foreach (var t in DocumentContentTable.All)
            {
                var tableMoved = 0;
                long after = 0;
                while (true)
                {
                    var batch = await ReadBatchAsync(t, after, ct).ConfigureAwait(false);
                    if (batch.Count == 0) break;
                    await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
                    await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                    try
                    {
                        foreach (var row in batch)
                        {
                            var o = new NewDocumentObject(row.PropertyId, t.Kind, t.OwnerTable, row.Key, row.ContentType,
                                t.Sealed ? null : row.Bytes, t.Sealed ? row.Text : null, 0);
                            var objectRef = await store.PutAsync(o, ct).ConfigureAwait(false);
                            await using var upd = main.Command(t.SetRefSql, conn, tx);
                            upd.Parameters.Add("@ref", SqlDbType.UniqueIdentifier).Value = objectRef;
                            upd.Parameters.Add("@k", SqlDbType.BigInt).Value = row.Key;
                            await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        tableMoved += batch.Count;
                    }
                    catch
                    {
                        if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }

                    after = batch[^1].Key;
                }

                moved += tableMoved;
                log?.Invoke(string.Create(CultureInfo.InvariantCulture, $"documents: {t.Table} moved {tableMoved} row(s)."));
            }

            var remaining = 0;
            foreach (var t in DocumentContentTable.All)
            {
                await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = main.Command(t.RemainingSql, conn);
                var n = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0);
                remaining += n;
                if (n > 0) stuck.Add($"{t.Table} ({n})");
            }

            await DocumentRunLog.WriteAsync(main, "Move", started, moved, moved, remaining, null, ct).ConfigureAwait(false);
            return new DocumentMoveResult(moved, moved, remaining, stuck);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DocumentRunLog.WriteAsync(main, "Failed", started, moved, moved, -1, "Move failed: " + ex.GetType().Name, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private sealed record Row(long Key, int PropertyId, string ContentType, byte[]? Bytes, string? Text);

    private async Task<List<Row>> ReadBatchAsync(DocumentContentTable t, long after, CancellationToken ct)
    {
        await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = main.Command(t.SelectToMoveSql, conn);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = BatchSize;
        cmd.Parameters.Add("@after", SqlDbType.BigInt).Value = after;
        var rows = new List<Row>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new Row(r.GetInt64(0), r.GetInt32(1), r.GetString(2), t.Sealed ? null : (byte[])r[3], t.Sealed ? r.GetString(3) : null));
        return rows;
    }

    private async Task WriteMainNoteAsync(CancellationToken ct)
    {
        string mainDb;
        await using (var c = await main.OpenAsync(ct).ConfigureAwait(false)) mainDb = c.Database;
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command("""
            MERGE plat.doc_meta AS d USING (SELECT 'main_database' AS k) s ON d.meta_key = s.k
            WHEN MATCHED THEN UPDATE SET meta_value = @v, updated_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT(meta_key, meta_value) VALUES (s.k, @v);
            """, conn);
        cmd.Parameters.Add("@v", SqlDbType.NVarChar, 200).Value = mainDb;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// LF-docdb (contract §1.7): the orphan sweep. Per property and kind it reads the refs <c>Hcc</c> holds and removes the objects older than 24 hours
/// that are not among them, and everything marked deleted more than 24 hours ago. The margin covers a write whose <c>Hcc</c> transaction is open.
/// </summary>
public sealed class DocumentSweepJob(SqlConnectionFactory main, IDocumentObjectStore store)
{
    public static readonly TimeSpan Margin = TimeSpan.FromHours(24);

    public async Task<DocumentSweepResult> RunAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var before = (nowUtc ?? DateTime.UtcNow) - Margin;
        var removed = 0;
        try
        {
            foreach (var p in await DocumentRunLog.PropertiesAsync(main, ct).ConfigureAwait(false))
                foreach (var t in DocumentContentTable.All)
                {
                    var live = await DocumentRunLog.RefsAsync(main, t, p, ct).ConfigureAwait(false);
                    removed += await store.SweepAsync(p, t.Kind, before, live, ct).ConfigureAwait(false);
                }

            await DocumentRunLog.WriteAsync(main, "Sweep", started, 0, removed, 0, null, ct).ConfigureAwait(false);
            return new DocumentSweepResult(removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DocumentRunLog.WriteAsync(main, "Failed", started, 0, removed, 0, "Sweep failed: " + ex.GetType().Name, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>
/// LF-docdb (contract §1.7, §1.9): compares every <c>object_ref</c> in <c>Hcc</c> with <c>HccDocuments</c> (dangling = referenced but missing or
/// deleted; orphans = live and older than 24 hours but unreferenced) and keeps the daily result in <c>plat.doc_ref_check</c> for System health.
/// </summary>
public sealed class DocumentRefCheck(SqlConnectionFactory main, SqlDocumentConnectionFactory documents, IDocumentObjectStore store)
{
    public async Task<DocumentRefCheckResult> RunAsync(bool record = true, CancellationToken ct = default)
    {
        var kinds = new List<DocumentKindCheck>();
        var cutoff = DateTime.UtcNow - DocumentSweepJob.Margin;
        var props = await DocumentRunLog.PropertiesAsync(main, ct).ConfigureAwait(false);
        foreach (var t in DocumentContentTable.All)
        {
            int referenced = 0, dangling = 0, orphans = 0;
            foreach (var p in props)
            {
                var refs = await DocumentRunLog.RefsAsync(main, t, p, ct).ConfigureAwait(false);
                var live = (await store.ListAsync(p, t.Kind, null, 1_000_000, ct).ConfigureAwait(false)).ToDictionary(x => x.Ref, x => x.CreatedUtc);
                referenced += refs.Count;
                dangling += refs.Count(r => !live.ContainsKey(r));
                orphans += live.Count(kv => kv.Value < cutoff && !refs.Contains(kv.Key));
            }

            kinds.Add(new DocumentKindCheck(t.Kind, referenced, dangling, orphans));
        }

        string? note = null;
        try
        {
            await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = documents.Command("SELECT meta_value FROM plat.doc_meta WHERE meta_key = 'main_database';", conn);
            note = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        }
        catch (SqlException)
        {
            note = null;
        }

        if (record)
        {
            await using var conn = await main.OpenAsync(ct).ConfigureAwait(false);
            foreach (var k in kinds)
            {
                await using var cmd = main.Command("""
                    MERGE plat.doc_ref_check AS d USING (SELECT @k AS kind) s ON d.kind = s.kind
                    WHEN MATCHED THEN UPDATE SET checked_utc = SYSUTCDATETIME(), dangling_refs = @d, orphan_objects = @o
                    WHEN NOT MATCHED THEN INSERT(kind, checked_utc, dangling_refs, orphan_objects) VALUES (s.kind, SYSUTCDATETIME(), @d, @o);
                    """, conn);
                cmd.Parameters.Add("@k", SqlDbType.VarChar, 20).Value = SqlDocumentObjectStore.KindText(k.Kind);
                cmd.Parameters.Add("@d", SqlDbType.Int).Value = k.Dangling;
                cmd.Parameters.Add("@o", SqlDbType.Int).Value = k.Orphans;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        return new DocumentRefCheckResult(kinds, note);
    }
}
