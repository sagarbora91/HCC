using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Payments;

/// <summary>
/// SQL Server implementation of <see cref="ICashOutStore"/> (migration 3100: <c>pay.cash_transfer</c>, <c>pay.banking_slip</c>). The slip photo
/// lives in its own table so listings never read it; the nightly retention purge deletes it after 600 days (<c>SqlRetentionPurge</c>).
/// </summary>
public sealed class SqlCashOutStore(SqlConnectionFactory connections, Hcc.Application.Documents.IDocumentObjectStore? objects = null) : ICashOutStore
{
    private const string Columns = """
        t.transfer_id, t.property_id, t.kind, t.status, t.business_date, t.amount_paise, t.from_till_id, t.from_shift_id, t.to_till_id, t.to_shift_id,
        t.bank, t.slip_no, t.reason, t.approval_id, t.recorded_by, t.recorded_utc, t.decided_by, t.decided_utc, t.decided_date, t.decision_reason,
        t.slip_purged_utc, CASE WHEN EXISTS (SELECT 1 FROM pay.banking_slip b WHERE b.transfer_id = t.transfer_id) THEN 1 ELSE 0 END, t.row_ver
        """;

    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The cash-out store needs a SQL command transaction.")).Command(sql);

    public async Task<(long TransferId, string Version)> InsertAsync(ICommandTransaction tx, NewCashTransfer t, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        await using var cmd = Cmd(tx, """
            INSERT pay.cash_transfer(property_id, kind, status, business_date, amount_paise, from_till_id, from_shift_id, to_till_id, bank, slip_no, reason,
                                     recorded_by, recorded_utc)
            OUTPUT INSERTED.transfer_id, INSERTED.row_ver
            VALUES (@p, @k, @s, @d, @a, @ft, @fs, @tt, @b, @sn, @r, @u, @ru);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = t.PropertyId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CashOutRules.Code(t.Kind);
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = t.Status;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = t.BusinessDate;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = t.Amount.Value;
        cmd.Parameters.Add("@ft", SqlDbType.Int).Value = t.FromTillId;
        cmd.Parameters.Add("@fs", SqlDbType.BigInt).Value = t.FromShiftId;
        cmd.Parameters.Add("@tt", SqlDbType.Int).Value = (object?)t.ToTillId ?? DBNull.Value;
        cmd.Parameters.Add("@b", SqlDbType.NVarChar, 100).Value = (object?)t.Bank ?? DBNull.Value;
        cmd.Parameters.Add("@sn", SqlDbType.NVarChar, 40).Value = (object?)t.SlipNo ?? DBNull.Value;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object?)t.Reason ?? DBNull.Value;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = t.RecordedBy;
        Utc(cmd, "@ru", t.RecordedUtc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), SqlValues.RowVersionToWire((byte[])rd[1]));
    }

    public async Task SetApprovalAsync(ICommandTransaction tx, long transferId, long approvalId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pay.cash_transfer SET approval_id = @a WHERE transfer_id = @t;");
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = approvalId;
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertSlipAsync(ICommandTransaction tx, long transferId, string contentType, byte[] image, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        Guid? objectRef = null;
        if (objects is not null)
        {
            // LF-docdb (contract §1.8): object first, in its own committed transaction; this Hcc transaction stores the ref.
            int propertyId, by;
            await using (var q = Cmd(tx, "SELECT property_id, recorded_by FROM pay.cash_transfer WHERE transfer_id = @t;"))
            {
                q.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException("The deposit to attach a slip photo to was not found.");
                (propertyId, by) = (r.GetInt32(0), r.GetInt32(1));
            }

            objectRef = await objects.PutAsync(new Hcc.Application.Documents.NewDocumentObject(propertyId, Hcc.Application.Documents.DocumentObjectKind.BankingSlip, "pay.banking_transfer",
                transferId, contentType, image, null, by), ct).ConfigureAwait(false);
        }

        await using var cmd = Cmd(tx, "INSERT pay.banking_slip(transfer_id, content_type, size_bytes, image, object_ref) VALUES (@t, @c, @n, @i, @r);");
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 10).Value = contentType;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = image.Length;
        cmd.Parameters.Add("@i", SqlDbType.VarBinary, -1).Value = objectRef is null ? image : DBNull.Value;
        cmd.Parameters.Add("@r", SqlDbType.UniqueIdentifier).Value = objectRef is null ? DBNull.Value : objectRef.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<CashTransferRow?> PeekAsync(ICommandTransaction tx, long transferId, CancellationToken ct) => RowAsync(tx, transferId, "", ct);

    public Task<CashTransferRow?> LockAsync(ICommandTransaction tx, long transferId, CancellationToken ct) => RowAsync(tx, transferId, "WITH (UPDLOCK)", ct);

    private static async Task<CashTransferRow?> RowAsync(ICommandTransaction tx, long transferId, string hint, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {Columns} FROM pay.cash_transfer t {hint} WHERE t.transfer_id = @t;");
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Read(rd) : null;
    }

    public async Task DecideAsync(ICommandTransaction tx, long transferId, CashTransferDecision d, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(d);
        await using var cmd = Cmd(tx, """
            UPDATE pay.cash_transfer SET status = @s, decided_by = @u, decided_utc = @du, decided_date = @dd, decision_reason = @r, to_shift_id = COALESCE(@ts, to_shift_id)
            WHERE transfer_id = @t AND status = 'Pending';
            """);
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = d.Status;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = d.DecidedBy;
        Utc(cmd, "@du", d.DecidedUtc);
        cmd.Parameters.Add("@dd", SqlDbType.Date).Value = d.DecidedDate;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object?)d.Reason ?? DBNull.Value;
        cmd.Parameters.Add("@ts", SqlDbType.BigInt).Value = (object?)d.ToShiftId ?? DBNull.Value;
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("The cash-out record is no longer pending.");
    }

    public async Task<IReadOnlyList<CashTransferListRow>> ListAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {Columns}, ft.code, tt.code, a.row_ver
              FROM pay.cash_transfer t JOIN pay.till ft ON ft.till_id = t.from_till_id
              LEFT JOIN pay.till tt ON tt.till_id = t.to_till_id
              LEFT JOIN bill.approval a ON a.approval_id = t.approval_id
             WHERE t.property_id = @p AND (t.business_date BETWEEN @from AND @to OR t.decided_date BETWEEN @from AND @to OR t.status = 'Pending')
             ORDER BY t.transfer_id DESC;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from;
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to;
        var list = new List<CashTransferListRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CashTransferListRow(Read(rd), rd.GetString(23), rd.IsDBNull(24) ? null : rd.GetString(24),
                rd.IsDBNull(25) ? null : SqlValues.RowVersionToWire((byte[])rd[25])));
        return list;
    }

    public async Task<(string ContentType, byte[] Image)?> SlipAsync(int propertyId, long transferId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT b.content_type, b.image, b.object_ref FROM pay.banking_slip b JOIN pay.cash_transfer t ON t.transfer_id = b.transfer_id
            WHERE b.transfer_id = @t AND t.property_id = @p AND t.kind = 'banking';
            """, conn);
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = transferId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        string type; byte[]? inline; Guid? objectRef;
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (type, inline, objectRef) = (rd.GetString(0), rd.IsDBNull(1) ? null : (byte[])rd[1], rd.IsDBNull(2) ? null : rd.GetGuid(2));
        }

        return (type, objectRef is { } r ? await Documents.DocumentObjectAccess.ReadAsync(objects, r, ct).ConfigureAwait(false) : inline ?? throw Documents.DocumentObjectAccess.Missing());
    }

    private static CashTransferRow Read(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), CashOutRules.ParseKind(rd.GetString(2)), rd.GetString(3),
        rd.GetFieldValue<DateOnly>(4), SqlValues.ReadPaise(rd, 5), rd.GetInt32(6), rd.GetInt64(7), rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetInt64(9),
        Str(rd, 10), Str(rd, 11), Str(rd, 12), rd.IsDBNull(13) ? null : rd.GetInt64(13), rd.GetInt32(14), DateTime.SpecifyKind(rd.GetDateTime(15), DateTimeKind.Utc),
        rd.IsDBNull(16) ? null : rd.GetInt32(16), rd.IsDBNull(17) ? null : DateTime.SpecifyKind(rd.GetDateTime(17), DateTimeKind.Utc),
        rd.IsDBNull(18) ? null : rd.GetFieldValue<DateOnly>(18), Str(rd, 19), rd.IsDBNull(20) ? null : DateTime.SpecifyKind(rd.GetDateTime(20), DateTimeKind.Utc),
        rd.GetInt32(21) == 1, SqlValues.RowVersionToWire((byte[])rd[22]));

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static void Utc(SqlCommand cmd, string name, DateTime value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.DateTime2);
        p.Scale = 3;
        p.Value = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
    }
}
