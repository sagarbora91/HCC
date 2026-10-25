using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Documents;

/// <summary>SQL adapter of <see cref="IDocumentStore"/> and <see cref="IDocumentImageStore"/> (schema <c>doc</c>, migration 2100). Only sealed text reaches SQL.</summary>
public sealed class SqlDocumentStore(SqlConnectionFactory connections) : IDocumentStore, IDocumentImageStore
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

    public async Task PutAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default)
    {
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
            const string sql = "SELECT sealed FROM doc.id_capture_image WHERE capture_id = @c;";
            if (tx is not null) cmd = Sql(tx).Command(sql);
            else
            {
                own = await connections.OpenAsync(ct).ConfigureAwait(false);
                cmd = connections.Command(sql, own);
            }

            await using (cmd)
            {
                cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
                return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            }
        }
        finally
        {
            if (own is not null) await own.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(ICommandTransaction tx, long captureId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("DELETE doc.id_capture_image WHERE capture_id = @c;");
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = captureId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
