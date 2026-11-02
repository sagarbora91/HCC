using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Documents;

/// <summary>SQL adapter of <see cref="IDocumentStore"/> and <see cref="IDocumentImageStore"/> (schema <c>doc</c>, migration 2100). Only sealed text reaches SQL.
/// LF-docdb: with an <see cref="IDocumentObjectStore"/> the sealed image lives in HccDocuments and <c>doc.id_capture_image</c> keeps only the ref (contract §1.5); a row with no ref still reads its inline column.</summary>
public sealed class SqlDocumentStore(SqlConnectionFactory connections, IDocumentObjectStore? objects = null) : IDocumentStore, IDocumentImageStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The document store needs the SQL command transaction.");

    private const string Cols = """
        c.capture_id, c.capture_ref, c.property_id, c.guest_id, c.stay_id, c.doc_kind, c.content_type, c.size_bytes, c.status, c.ocr_doc_kind, c.ocr_last4,
        c.ocr_confidence, c.ocr_sealed, c.captured_date, c.captured_utc, c.captured_by, c.confirmed_utc, c.confirmed_by, c.reject_reason, c.purged_utc,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM doc.id_capture_image i WHERE i.capture_id = c.capture_id) THEN 1 ELSE 0 END AS BIT), c.row_ver
        """;

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static DateTime? U(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);

    private static IdCaptureRow Read(SqlDataReader r) => new(r.GetInt64(0), r.GetGuid(1), r.GetInt32(2), r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5), r.GetString(6),
        r.GetInt32(7), r.GetString(8), S(r, 9), S(r, 10), r.IsDBNull(11) ? null : r.GetByte(11), S(r, 12), DateOnly.FromDateTime(r.GetDateTime(13)),
        DateTime.SpecifyKind(r.GetDateTime(14), DateTimeKind.Utc), r.GetInt32(15), U(r, 16), r.IsDBNull(17) ? null : r.GetInt32(17), S(r, 18), U(r, 19), r.GetBoolean(20), (byte[])r[21]);

    private async Task<IReadOnlyList<IdCaptureRow>> QueryAsync(ICommandTransaction? tx, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        SqlConnection? own = null;
        try
        {
            SqlCommand cmd;
            if (tx is not null) cmd = Sql(tx).Command(sql);
            else
            {
                own = await connections.OpenAsync(ct).ConfigureAwait(false);
                cmd = connections.Command(sql, own);
            }

            await using (cmd)
            {
                bind(cmd);
                var list = new List<IdCaptureRow>();
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(rd));
                return list;
            }
        }
        finally
        {
            if (own is not null) await own.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<bool> GuestExistsAsync(ICommandTransaction tx, int propertyId, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM gst.guest WITH (UPDLOCK, ROWLOCK) WHERE guest_id = @g AND property_id = @p;");
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<bool> StayBelongsToGuestAsync(ICommandTransaction tx, int propertyId, long stayId, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM gst.stay WHERE stay_id = @s AND guest_id = @g AND property_id = @p;");
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = stayId;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<IdCaptureRow> InsertAsync(ICommandTransaction tx, NewIdCapture c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Sql(tx).Command("""
            INSERT doc.id_capture(capture_ref, property_id, guest_id, stay_id, doc_kind, content_type, size_bytes, status, ocr_doc_kind, ocr_last4, ocr_confidence, ocr_sealed,
                                  captured_date, captured_utc, captured_by)
            OUTPUT INSERTED.capture_id
            VALUES (@ref, @p, @g, @s, @k, @t, @sz, 'Captured', @ok, @l4, @cf, @os, @d, @utc, @by);
            """);
        cmd.Parameters.Add("@ref", SqlDbType.UniqueIdentifier).Value = c.CaptureRef;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = c.PropertyId;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = c.GuestId;
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = (object?)c.StayId ?? DBNull.Value;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 10).Value = c.DocKind;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 20).Value = c.ContentType;
        cmd.Parameters.Add("@sz", SqlDbType.Int).Value = c.SizeBytes;
        cmd.Parameters.Add("@ok", SqlDbType.VarChar, 10).Value = c.OcrDocKind;
        cmd.Parameters.Add("@l4", SqlDbType.VarChar, 4).Value = c.OcrLast4;
        cmd.Parameters.Add("@cf", SqlDbType.TinyInt).Value = (byte)c.OcrConfidence;
        cmd.Parameters.Add("@os", SqlDbType.VarChar, 4000).Value = c.OcrSealed;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = c.CapturedDate.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@utc", SqlDbType.DateTime2).Value = c.CapturedUtc;
        cmd.Parameters.Add("@by", SqlDbType.Int).Value = c.CapturedBy;
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return await FindAsync(tx, c.PropertyId, id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Inserted capture not found.");
    }

    public async Task<IdCaptureRow?> LockAsync(ICommandTransaction tx, int propertyId, long captureId, CancellationToken ct = default)
        => (await QueryAsync(tx, $"SELECT {Cols} FROM doc.id_capture c WITH (UPDLOCK, ROWLOCK) WHERE c.capture_id = @c AND c.property_id = @p;",
            cmd => { cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId; cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId; }, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IdCaptureRow?> FindAsync(ICommandTransaction? tx, int propertyId, long captureId, CancellationToken ct = default)
        => (await QueryAsync(tx, $"SELECT {Cols} FROM doc.id_capture c WHERE c.capture_id = @c AND c.property_id = @p;",
            cmd => { cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId; cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId; }, ct).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<IdCaptureRow>> ListAsync(int propertyId, long? guestId, long? stayId, string? status, CancellationToken ct = default)
        => QueryAsync(null, $"""
            SELECT TOP (200) {Cols} FROM doc.id_capture c
            WHERE c.property_id = @p AND (@g IS NULL OR c.guest_id = @g) AND (@s IS NULL OR c.stay_id = @s) AND (@st IS NULL OR c.status = @st)
            ORDER BY c.capture_id DESC;
            """, cmd =>
        {
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = (object?)guestId ?? DBNull.Value;
            cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = (object?)stayId ?? DBNull.Value;
            cmd.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = (object?)status ?? DBNull.Value;
        }, ct);

    public async Task SetStatusAsync(ICommandTransaction tx, long captureId, string status, int principalId, DateTime utc, string? rejectReason, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE doc.id_capture SET status = @st, reject_reason = @r,
              confirmed_utc = CASE WHEN @st = 'Confirmed' THEN @utc ELSE confirmed_utc END,
              confirmed_by = CASE WHEN @st = 'Confirmed' THEN @u ELSE confirmed_by END
            WHERE capture_id = @c;
            """);
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        cmd.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = status;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object?)rejectReason ?? DBNull.Value;
        cmd.Parameters.Add("@utc", SqlDbType.DateTime2).Value = utc;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetGuestIdProfileAsync(ICommandTransaction tx, long guestId, string idType, string last4, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE gst.guest SET id_type = @t, id_last4 = @l WHERE guest_id = @g;");
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 40).Value = idType;
        cmd.Parameters.Add("@l", SqlDbType.VarChar, 4).Value = last4;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<IdCaptureRow>> LockPurgeCandidatesAsync(ICommandTransaction tx, int propertyId, DateOnly cutoff, CancellationToken ct = default)
        => QueryAsync(tx, $"SELECT {Cols} FROM doc.id_capture c WITH (UPDLOCK, ROWLOCK) WHERE c.property_id = @p AND c.status <> 'Purged' AND c.captured_date <= @d ORDER BY c.capture_id;",
            cmd => { cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId; cmd.Parameters.Add("@d", SqlDbType.Date).Value = cutoff.ToDateTime(TimeOnly.MinValue); }, ct);

    public async Task MarkPurgedAsync(ICommandTransaction tx, long captureId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE doc.id_capture SET status = 'Purged', ocr_sealed = NULL, purged_utc = @utc WHERE capture_id = @c;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        cmd.Parameters.Add("@utc", SqlDbType.DateTime2).Value = utc;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A07-07: the prefix test is a binary (case-sensitive) LEFT(...) comparison, not LIKE, so a key id is never read as a pattern; capture rows are locked before image rows
    /// (the order the purge uses).
    /// </summary>
    public Task<IReadOnlyList<IdCaptureRow>> LockSealedUnderOtherKeysAsync(ICommandTransaction tx, int propertyId, string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyId);
        return QueryAsync(tx, $"""
            SELECT {Cols} FROM doc.id_capture c WITH (UPDLOCK, ROWLOCK)
            WHERE c.property_id = @p
              AND ((c.ocr_sealed IS NOT NULL AND LEFT(c.ocr_sealed, LEN(@prefix)) COLLATE Latin1_General_BIN2 <> @prefix)
                   OR EXISTS (SELECT 1 FROM doc.id_capture_image i WITH (UPDLOCK, ROWLOCK) WHERE i.capture_id = c.capture_id
                                AND (i.object_ref IS NOT NULL OR LEFT(i.sealed, LEN(@prefix)) COLLATE Latin1_General_BIN2 <> @prefix)))
            ORDER BY c.capture_id;
            """, cmd =>
        {
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            cmd.Parameters.Add("@prefix", SqlDbType.VarChar, 200).Value = Hcc.Application.GuestIds.GuestIdProtector.SecretPrefix + keyId + ":";
        }, ct);
    }

    public async Task ResealOcrAsync(ICommandTransaction tx, long captureId, string ocrSealed, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE doc.id_capture SET ocr_sealed = @os WHERE capture_id = @c AND ocr_sealed IS NOT NULL;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        cmd.Parameters.Add("@os", SqlDbType.VarChar, 4000).Value = ocrSealed;
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("The capture's OCR text to re-seal was not found.");
    }

    public async Task ReplaceAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default)
    {
        if (objects is not null && await RefOfAsync(tx, captureId, ct).ConfigureAwait(false) is { } objectRef)
        {
            await objects.ResealAsync(objectRef, sealedImage, ct).ConfigureAwait(false);
            return;
        }

        await using var cmd = Sql(tx).Command("UPDATE doc.id_capture_image SET sealed = @s WHERE capture_id = @c;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, -1).Value = sealedImage;
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("The capture image to re-seal was not found.");
    }

    private async Task<Guid?> RefOfAsync(ICommandTransaction tx, long captureId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("SELECT object_ref FROM doc.id_capture_image WHERE capture_id = @c;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as Guid?;
    }

    public async Task PutAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default)
    {
        if (objects is not null)
        {
            // Contract §1.8: the object is written first, in its own committed transaction on HccDocuments; this command's Hcc transaction then stores the ref.
            int propertyId, by; string docKind;
            await using (var q = Sql(tx).Command("SELECT property_id, captured_by, doc_kind FROM doc.id_capture WHERE capture_id = @c;"))
            {
                q.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException("The capture to attach an image to was not found.");
                (propertyId, by, docKind) = (r.GetInt32(0), r.GetInt32(1), r.GetString(2));
            }

            var kind = docKind switch { "legacy" => DocumentObjectKind.LegacyImage, "photo" => DocumentObjectKind.GuestPhoto, _ => DocumentObjectKind.IdImage };
            var objectRef = await objects.PutAsync(new NewDocumentObject(propertyId, kind, "doc.id_capture", captureId, "image/jpeg", null, sealedImage, by), ct).ConfigureAwait(false);
            await using var put = Sql(tx).Command("INSERT doc.id_capture_image(capture_id, sealed, object_ref) VALUES (@c, NULL, @r);");
            put.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
            put.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef;
            await put.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        await using var cmd = Sql(tx).Command("INSERT doc.id_capture_image(capture_id, sealed) VALUES (@c, @s);");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, -1).Value = sealedImage;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(ICommandTransaction? tx, long captureId, CancellationToken ct = default)
    {
        SqlConnection? own = null;
        try
        {
            SqlCommand cmd;
            const string sql = "SELECT sealed, object_ref FROM doc.id_capture_image WHERE capture_id = @c;";
            if (tx is not null) cmd = Sql(tx).Command(sql);
            else
            {
                own = await connections.OpenAsync(ct).ConfigureAwait(false);
                cmd = connections.Command(sql, own);
            }

            string? inline; Guid? objectRef; bool found;
            await using (cmd)
            {
                cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                found = await r.ReadAsync(ct).ConfigureAwait(false);
                inline = found && !r.IsDBNull(0) ? r.GetString(0) : null;
                objectRef = found && !r.IsDBNull(1) ? r.GetGuid(1) : null;
            }

            if (!found) return null;
            if (objectRef is null) return inline;
            if (objects is null) throw new InvalidOperationException("This image is in the documents database, which is not configured.");
            var got = await objects.GetAsync(objectRef.Value, ct).ConfigureAwait(false);
            return got?.SealedText ?? throw DocumentObjectAccess.Missing();
        }
        finally
        {
            if (own is not null) await own.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(ICommandTransaction tx, long captureId, CancellationToken ct = default)
    {
        var objectRef = await RefOfAsync(tx, captureId, ct).ConfigureAwait(false);
        await using var cmd = Sql(tx).Command("DELETE doc.id_capture_image WHERE capture_id = @c;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (objectRef is { } r) await DocumentObjectAccess.PublishReleaseAsync(tx, r, "id-image", captureId, ct).ConfigureAwait(false);
    }
}
