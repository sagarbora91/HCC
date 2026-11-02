using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.LfMoney;

/// <summary>SQL adapter of <see cref="IGuestCreditStore"/> (LF B2: ops.udhaar_receivable + 4691 links, gst.stay / gst.guest).</summary>
public sealed class SqlGuestCreditStore : IGuestCreditStore
{
    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The guest-credit store needs the SQL command transaction.")).Command(sql);

    public async Task<FolioGuest?> GuestOfFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT TOP (1) g.guest_id, g.full_name, g.phone FROM bill.folio f JOIN gst.stay s ON s.reservation_id = f.reservation_id JOIN gst.guest g ON g.guest_id = s.guest_id
            WHERE f.folio_id = @f;
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new FolioGuest(rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2)) : null;
    }

    public async Task<Paise> OpenReceivablesAsync(ICommandTransaction tx, int propertyId, long guestId, string customerKey, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT COALESCE(SUM(r.amount_paise - COALESCE(st.paid, 0)), 0)
            FROM ops.udhaar_receivable r WITH (UPDLOCK, HOLDLOCK)
            OUTER APPLY (SELECT SUM(x.amount_paise) AS paid FROM ops.udhaar_settlement x WHERE x.receivable_id = r.receivable_id) st
            WHERE r.property_id = @p AND r.is_void = 0 AND (r.guest_id = @g OR (@k IS NOT NULL AND r.mobile = @k));
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@k", SqlDbType.Char, 10).Value = customerKey is { Length: 10 } k && k.All(char.IsAsciiDigit) ? k : DBNull.Value;
        return new Paise(Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture));
    }

    public async Task<long> InsertReceivableAsync(ICommandTransaction tx, int propertyId, string name, string? mobile, Paise amount, DateOnly date, long guestId, long folioId,
        long receiptId, string note, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT ops.udhaar_receivable(property_id, customer_name, mobile, amount_paise, credit_date, note, created_by, created_utc, kind, gst_bps, guest_id, folio_id, receipt_id)
            OUTPUT INSERTED.receivable_id VALUES (@p, @n, @m, @a, @d, @note, @by, @u, 'room', 0, @g, @f, @r);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 80).Value = name;
        cmd.Parameters.Add("@m", SqlDbType.Char, 10).Value = (object?)mobile ?? DBNull.Value;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = amount.Value;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@note", SqlDbType.NVarChar, 200).Value = note.Length <= 200 ? note : note[..200];
        cmd.Parameters.Add("@by", SqlDbType.Int).Value = by;
        cmd.Parameters.Add("@u", SqlDbType.DateTime2).Value = utc;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = receiptId;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
