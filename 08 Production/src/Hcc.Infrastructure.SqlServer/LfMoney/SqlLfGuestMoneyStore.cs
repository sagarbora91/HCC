using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.LfMoney;

/// <summary>SQL adapter of the LF guest money fields (migrations 4677 gst.guest_agreed_rate, 4694 gst.guest_credit_account).</summary>
public sealed class SqlLfGuestMoneyStore(SqlConnectionFactory connections) : ILfGuestMoneyStore
{
    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value)
    {
        cmd.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    private async Task<T> RunAsync<T>(ICommandTransaction? tx, string sql, Func<SqlCommand, Task<T>> work, CancellationToken ct)
    {
        if (tx is SqlCommandTransaction s)
        {
            await using var cmd = s.Command(sql);
            return await work(cmd).ConfigureAwait(false);
        }

        if (tx is not null) throw new InvalidOperationException("The LF store needs the SQL command transaction.");
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        return await work(c2).ConfigureAwait(false);
    }

    private static object? D(DateOnly? d) => d is { } x ? x.ToDateTime(TimeOnly.MinValue) : null;

    public Task<string?> GuestPhoneAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct) =>
        RunAsync(tx, "SELECT phone FROM gst.guest WHERE property_id = @p AND guest_id = @g;", async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@g", SqlDbType.BigInt, guestId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        }, ct);

    public Task<bool> RoomTypeExistsAsync(ICommandTransaction tx, int propertyId, int roomTypeId, CancellationToken ct) =>
        RunAsync(tx, "SELECT COUNT(*) FROM inv.room_type WHERE property_id = @p AND room_type_id = @t;", async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@t", SqlDbType.Int, roomTypeId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
        }, ct);

    public Task UpsertAgreedRateAsync(ICommandTransaction tx, GuestAgreedRate rate, int by, CancellationToken ct) =>
        RunAsync(tx, """
            MERGE gst.guest_agreed_rate AS t USING (SELECT @g AS guest_id, @t AS room_type_id) AS s ON t.guest_id = s.guest_id AND t.room_type_id = s.room_type_id
            WHEN MATCHED THEN UPDATE SET rate_paise = @r, plan_code = @pl, valid_to = @v, note = @n, set_by = @b, set_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (guest_id, room_type_id, rate_paise, plan_code, valid_to, note, set_by, set_utc) VALUES (@g, @t, @r, @pl, @v, @n, @b, SYSUTCDATETIME());
            """, async cmd =>
        {
            ArgumentNullException.ThrowIfNull(rate);
            P(cmd, "@g", SqlDbType.BigInt, rate.GuestId);
            P(cmd, "@t", SqlDbType.Int, rate.RoomTypeId);
            P(cmd, "@r", SqlDbType.BigInt, rate.Rate.Value);
            P(cmd, "@pl", SqlDbType.VarChar, rate.PlanCode);
            P(cmd, "@v", SqlDbType.Date, D(rate.ValidTo));
            P(cmd, "@n", SqlDbType.NVarChar, rate.Note);
            P(cmd, "@b", SqlDbType.Int, by);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task<bool> DeleteAgreedRateAsync(ICommandTransaction tx, long guestId, int roomTypeId, CancellationToken ct) =>
        RunAsync(tx, "DELETE gst.guest_agreed_rate WHERE guest_id = @g AND room_type_id = @t;", async cmd =>
        {
            P(cmd, "@g", SqlDbType.BigInt, guestId);
            P(cmd, "@t", SqlDbType.Int, roomTypeId);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        }, ct);

    public Task<IReadOnlyList<GuestAgreedRate>> AgreedRatesAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct) =>
        RunAsync<IReadOnlyList<GuestAgreedRate>>(tx, """
            SELECT a.guest_id, a.room_type_id, a.rate_paise, a.plan_code, a.valid_to, a.note FROM gst.guest_agreed_rate a
              JOIN gst.guest g ON g.guest_id = a.guest_id WHERE g.property_id = @p AND a.guest_id = @g ORDER BY a.room_type_id;
            """, async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@g", SqlDbType.BigInt, guestId);
            var list = new List<GuestAgreedRate>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new(rd.GetInt64(0), rd.GetInt32(1), new Paise(rd.GetInt64(2)), rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? null : DateOnly.FromDateTime(rd.GetDateTime(4)), rd.IsDBNull(5) ? null : rd.GetString(5)));
            return list;
        }, ct);

    public Task<(GuestCreditAccount Account, string? Note)?> CreditAccountAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct) =>
        RunAsync<(GuestCreditAccount, string?)?>(tx, "SELECT limit_paise, active, customer_key, note FROM gst.guest_credit_account WHERE property_id = @p AND guest_id = @g;", async cmd =>
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@g", SqlDbType.BigInt, guestId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            return (new GuestCreditAccount(guestId, new Paise(rd.GetInt64(0)), rd.GetBoolean(1), rd.GetString(2)), rd.IsDBNull(3) ? null : rd.GetString(3));
        }, ct);

    public Task UpsertCreditAccountAsync(ICommandTransaction tx, int propertyId, GuestCreditAccount account, string? note, int by, CancellationToken ct) =>
        RunAsync(tx, """
            MERGE gst.guest_credit_account AS t USING (SELECT @g AS guest_id) AS s ON t.guest_id = s.guest_id
            WHEN MATCHED THEN UPDATE SET limit_paise = @l, active = @a, note = @n, set_by = @b, set_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (guest_id, property_id, limit_paise, active, customer_key, note, set_by, set_utc) VALUES (@g, @p, @l, @a, @k, @n, @b, SYSUTCDATETIME());
            """, async cmd =>
        {
            ArgumentNullException.ThrowIfNull(account);
            P(cmd, "@g", SqlDbType.BigInt, account.GuestId);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@l", SqlDbType.BigInt, account.Limit.Value);
            P(cmd, "@a", SqlDbType.Bit, account.Active);
            P(cmd, "@k", SqlDbType.VarChar, account.CustomerKey);
            P(cmd, "@n", SqlDbType.NVarChar, note);
            P(cmd, "@b", SqlDbType.Int, by);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct);
}
