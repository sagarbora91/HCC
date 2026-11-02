using System.Data;
using System.Security.Cryptography;
using System.Text;
using Hcc.Application.Documents;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Documents;

/// <summary>
/// LF-docdb (LF-CONTRACT §1.5): <see cref="IDocumentObjectStore"/> on the separate documents database (<c>HccDocuments</c>, schema <c>dob</c>).
/// Every call is its own local transaction; nothing here joins an <c>Hcc</c> transaction (no MSDTC, §1.8).
/// </summary>
public sealed class SqlDocumentObjectStore(SqlDocumentConnectionFactory documents) : IDocumentObjectStore
{
    public const long ExpressLimitBytes = 50L * 1024 * 1024 * 1024;

    public static string KindText(DocumentObjectKind k) => k switch
    {
        DocumentObjectKind.IdImage => "id-image",
        DocumentObjectKind.GuestPhoto => "guest-photo",
        DocumentObjectKind.ExpenseBill => "expense-bill",
        DocumentObjectKind.BankingSlip => "banking-slip",
        DocumentObjectKind.ComplianceEvidence => "compliance-evidence",
        DocumentObjectKind.CounterAttachment => "counter-attachment",
        DocumentObjectKind.LegacyImage => "legacy-image",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    public static DocumentObjectKind ParseKind(string s) => s switch
    {
        "id-image" => DocumentObjectKind.IdImage,
        "guest-photo" => DocumentObjectKind.GuestPhoto,
        "expense-bill" => DocumentObjectKind.ExpenseBill,
        "banking-slip" => DocumentObjectKind.BankingSlip,
        "compliance-evidence" => DocumentObjectKind.ComplianceEvidence,
        "counter-attachment" => DocumentObjectKind.CounterAttachment,
        "legacy-image" => DocumentObjectKind.LegacyImage,
        _ => throw new InvalidOperationException($"Unknown document object kind '{s}'."),
    };

    private const string InfoCols = "object_ref, kind, content_type, size_bytes, sealed, created_utc, deleted_utc";

    private static DocumentObjectInfo ReadInfo(SqlDataReader r) => new(r.GetGuid(0), ParseKind(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetBoolean(4),
        DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc), r.IsDBNull(6) ? null : DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc));

    public async Task<Guid> PutAsync(NewDocumentObject o, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        var sealedObj = o.SealedText is not null;
        if (sealedObj == o.Content.HasValue) throw new ArgumentException("Exactly one of Content and SealedText is required.", nameof(o));
        byte[] bytes = sealedObj ? Encoding.UTF8.GetBytes(o.SealedText!) : o.Content!.Value.ToArray();
        var size = sealedObj ? o.SealedText!.Length : bytes.Length;
        if (size < 1) throw new ArgumentException("The object is empty.", nameof(o));
        var objectRef = Guid.NewGuid();
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            long id;
            await using (var ins = documents.Command("""
                INSERT dob.object(object_ref, property_id, kind, owner_table, owner_id, content_type, size_bytes, sealed, sha256, created_utc, created_by)
                OUTPUT INSERTED.object_id
                VALUES (@r, @p, @k, @ot, @oi, @ct, @n, @s, @h, SYSUTCDATETIME(), @by);
                """, conn, tx))
            {
                ins.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
                ins.Parameters.Add("@p", SqlDbType.Int).Value = o.PropertyId;
                ins.Parameters.Add("@k", SqlDbType.VarChar, 20).Value = KindText(o.Kind);
                ins.Parameters.Add("@ot", SqlDbType.VarChar, 40).Value = o.OwnerTable;
                ins.Parameters.Add("@oi", SqlDbType.BigInt).Value = o.OwnerId;
                ins.Parameters.Add("@ct", SqlDbType.VarChar, 30).Value = o.ContentType;
                ins.Parameters.Add("@n", SqlDbType.Int).Value = size;
                ins.Parameters.Add("@s", SqlDbType.Bit).Value = sealedObj;
                ins.Parameters.Add("@h", SqlDbType.Binary, 32).Value = SHA256.HashData(bytes);
                ins.Parameters.Add("@by", SqlDbType.Int).Value = o.CreatedBy;
                id = (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            await using (var c = documents.Command("INSERT dob.object_content(object_id, content, sealed_text) VALUES (@i, @c, @t);", conn, tx))
            {
                c.Parameters.Add("@i", SqlDbType.BigInt).Value = id;
                c.Parameters.Add("@c", SqlDbType.VarBinary, -1).Value = sealedObj ? DBNull.Value : bytes;
                c.Parameters.Add("@t", SqlDbType.VarChar, -1).Value = sealedObj ? o.SealedText! : DBNull.Value;
                await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return objectRef;
        }
        catch (SqlException ex)
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new HccException(HccErrorCode.UNAVAILABLE, SqlDocumentConnectionFactory.UnavailableText, null, ex);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DocumentObjectInfo?> InfoAsync(Guid objectRef, CancellationToken ct = default)
    {
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command($"SELECT {InfoCols} FROM dob.object WHERE object_ref = @r;", conn);
        cmd.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? ReadInfo(r) : null;
    }

    public async Task<(DocumentObjectInfo Info, byte[]? Content, string? SealedText)?> GetAsync(Guid objectRef, CancellationToken ct = default)
    {
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command("""
            SELECT o.object_ref, o.kind, o.content_type, o.size_bytes, o.sealed, o.created_utc, o.deleted_utc, c.content, c.sealed_text
            FROM dob.object o JOIN dob.object_content c ON c.object_id = o.object_id WHERE o.object_ref = @r AND o.deleted_utc IS NULL;
            """, conn);
        cmd.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (ReadInfo(r), r.IsDBNull(7) ? null : (byte[])r[7], r.IsDBNull(8) ? null : r.GetString(8));
    }

    public async Task ResealAsync(Guid objectRef, string sealedText, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sealedText);
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            long id;
            await using (var q = documents.Command("SELECT object_id FROM dob.object WITH (UPDLOCK) WHERE object_ref = @r AND sealed = 1 AND deleted_utc IS NULL;", conn, tx))
            {
                q.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
                id = await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is long v ? v : throw new InvalidOperationException("The object to re-seal was not found or is not sealed.");
            }

            await using (var u = documents.Command("UPDATE dob.object_content SET sealed_text = @t WHERE object_id = @i;", conn, tx))
            {
                u.Parameters.Add("@i", SqlDbType.BigInt).Value = id;
                u.Parameters.Add("@t", SqlDbType.VarChar, -1).Value = sealedText;
                await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var h = documents.Command("UPDATE dob.object SET sha256 = @h, size_bytes = @n WHERE object_id = @i;", conn, tx))
            {
                h.Parameters.Add("@i", SqlDbType.BigInt).Value = id;
                h.Parameters.Add("@h", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(sealedText));
                h.Parameters.Add("@n", SqlDbType.Int).Value = Math.Min(sealedText.Length, 8388608);
                await h.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ReleaseAsync(Guid objectRef, CancellationToken ct = default)
    {
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command("UPDATE dob.object SET deleted_utc = SYSUTCDATETIME() WHERE object_ref = @r AND deleted_utc IS NULL;", conn);
        cmd.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(Guid Ref, DateTime CreatedUtc)>> ListAsync(int propertyId, DocumentObjectKind kind, DateTime? createdBefore, int take, CancellationToken ct = default)
    {
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command("""
            SELECT TOP (@take) object_ref, created_utc FROM dob.object
            WHERE property_id = @p AND kind = @k AND deleted_utc IS NULL AND (@before IS NULL OR created_utc < @before)
            ORDER BY created_utc, object_id;
            """, conn);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Max(1, take);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 20).Value = KindText(kind);
        cmd.Parameters.Add("@before", SqlDbType.DateTime2).Value = createdBefore.HasValue ? createdBefore.Value : DBNull.Value;
        var list = new List<(Guid, DateTime)>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add((r.GetGuid(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)));
        return list;
    }

    public async Task<int> SweepAsync(int propertyId, DocumentObjectKind kind, DateTime before, IReadOnlySet<Guid> live, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(live);
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // The candidates are read first and compared with the live set here: a set of refs can be far larger than a parameter list.
            var doomed = new List<Guid>();
            await using (var q = documents.Command("""
                SELECT object_ref, deleted_utc FROM dob.object WITH (UPDLOCK)
                WHERE property_id = @p AND kind = @k AND ((deleted_utc IS NOT NULL AND deleted_utc < @b) OR (deleted_utc IS NULL AND created_utc < @b));
                """, conn, tx))
            {
                q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
                q.Parameters.Add("@k", SqlDbType.VarChar, 20).Value = KindText(kind);
                q.Parameters.Add("@b", SqlDbType.DateTime2).Value = before;
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                    if (!r.IsDBNull(1) || !live.Contains(r.GetGuid(0))) doomed.Add(r.GetGuid(0));
            }

            var removed = 0;
            foreach (var chunk in doomed.Chunk(200))
            {
                await using var d = documents.Command("DELETE o FROM dob.object o JOIN (SELECT TRY_CAST(value AS UNIQUEIDENTIFIER) AS r FROM STRING_SPLIT(@refs, ',')) x ON x.r = o.object_ref;", conn, tx);
                d.Parameters.Add("@refs", SqlDbType.VarChar, -1).Value = string.Join(',', chunk);
                removed += await d.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return removed;
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DocumentDatabaseSize> SizeAsync(CancellationToken ct = default)
    {
        await using var conn = await documents.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = documents.Command("""
            SELECT DB_NAME(),
                   ISNULL((SELECT SUM(CAST(size AS BIGINT)) * 8192 FROM sys.database_files WHERE type = 0), 0),
                   ISNULL((SELECT SUM(CAST(size AS BIGINT)) * 8192 FROM sys.database_files WHERE type = 1), 0),
                   (SELECT COUNT(*) FROM dob.object WHERE deleted_utc IS NULL),
                   ISNULL((SELECT SUM(CAST(size_bytes AS BIGINT)) FROM dob.object WHERE deleted_utc IS NULL), 0);
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await r.ReadAsync(ct).ConfigureAwait(false);
        return new DocumentDatabaseSize(r.GetString(0), r.GetInt64(1), r.GetInt64(2), ExpressLimitBytes, r.GetInt32(3), r.GetInt64(4));
    }
}
