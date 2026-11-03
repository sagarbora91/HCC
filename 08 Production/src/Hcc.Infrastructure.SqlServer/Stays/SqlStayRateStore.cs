using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Stays;

namespace Hcc.Infrastructure.SqlServer.Stays;

/// <summary>SQL adapter of <see cref="IStayRateStore"/> (LF A1: 4675 gst.stay_rate_change, 4690 gst.stay_rate_request; 0101 PERM-RATE-001 grants).</summary>
public sealed class SqlStayRateStore : IStayRateStore
{
    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public async Task<RateAuthority> AuthorityAsync(ICommandTransaction tx, int principalId, CancellationToken ct)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT g.mode, g.limit_value FROM plat.user_role ur JOIN plat.role_grant g ON g.role_code = ur.role_code AND g.permission_id = 'PERM-RATE-001'
            WHERE ur.user_id = @u AND ur.valid_from_utc <= SYSUTCDATETIME() AND (ur.valid_until_utc IS NULL OR ur.valid_until_utc > SYSUTCDATETIME());
            """);
        cmd.Add("@u", SqlDbType.Int, principalId);
        bool owner = false, manager = false;
        var limit = 0;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var mode = rd.GetString(0);
            if (mode == "A") owner = true;
            else if (mode == "L" && !rd.IsDBNull(1))
            {
                manager = true;
                limit = Math.Max(limit, (int)Math.Min(10_000, rd.GetInt64(1)));
            }
        }

        return new RateAuthority(owner, manager, limit);
    }

    public async Task<long> TariffAsync(ICommandTransaction tx, int roomTypeId, DateOnly date, CancellationToken ct)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT COALESCE(inv.fn_tariff_on(@t, @d), -- F4-T1: day-set aware resolver (migration 4381)
                            (SELECT rack_rate_paise FROM inv.room_type WHERE room_type_id = @t), 0);
            """);
        cmd.Add("@t", SqlDbType.Int, roomTypeId);
        cmd.Add("@d", SqlDbType.Date, D(date));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<(int Capacity, bool ExtraPersonCharge)> RoomTypeAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT t.max_occupancy, CASE WHEN EXISTS (SELECT 1 FROM inv.occupancy_charge o WHERE o.room_type_id = t.room_type_id AND o.active = 1) THEN 1 ELSE 0 END
            FROM inv.room_type t WHERE t.room_type_id = @t;
            """);
        cmd.Add("@t", SqlDbType.Int, roomTypeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetByte(0), rd.GetInt32(1) == 1) : (4, false);
    }

    public async Task InsertRequestAsync(ICommandTransaction tx, StayRateRequestMeta meta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(meta);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT gst.stay_rate_request(approval_id, stay_id, scope, new_rate_paise, new_people, new_children, old_rate_paise, old_people, old_children)
            VALUES (@a, @s, @sc, @nr, @np, @nc, @or, @op, @oc);
            """);
        cmd.Add("@a", SqlDbType.BigInt, meta.ApprovalId);
        cmd.Add("@s", SqlDbType.BigInt, meta.StayId);
        cmd.Add("@sc", SqlDbType.VarChar, meta.Scope, 10);
        cmd.Add("@nr", SqlDbType.BigInt, meta.NewRatePaise);
        cmd.Add("@np", SqlDbType.TinyInt, (byte)meta.NewPeople);
        cmd.Add("@nc", SqlDbType.TinyInt, (byte)meta.NewChildren);
        cmd.Add("@or", SqlDbType.BigInt, meta.OldRatePaise);
        cmd.Add("@op", SqlDbType.TinyInt, (byte)meta.OldPeople);
        cmd.Add("@oc", SqlDbType.TinyInt, (byte)meta.OldChildren);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<StayRateRequestMeta?> RequestAsync(ICommandTransaction tx, long approvalId, CancellationToken ct)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT approval_id, stay_id, scope, new_rate_paise, new_people, new_children, old_rate_paise, old_people, old_children
            FROM gst.stay_rate_request WHERE approval_id = @a;
            """);
        cmd.Add("@a", SqlDbType.BigInt, approvalId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new StayRateRequestMeta(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.GetInt64(3), rd.GetByte(4), rd.GetByte(5), rd.GetInt64(6), rd.GetByte(7), rd.GetByte(8));
    }

    public async Task SetReservationAsync(ICommandTransaction tx, long reservationId, long ratePaise, int adults, int children, CancellationToken ct)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE inv.reservation SET nightly_rate_paise = @r, adults = @a, children = @c WHERE reservation_id = @id;");
        cmd.Add("@r", SqlDbType.BigInt, ratePaise);
        cmd.Add("@a", SqlDbType.TinyInt, (byte)adults);
        cmd.Add("@c", SqlDbType.TinyInt, (byte)children);
        cmd.Add("@id", SqlDbType.BigInt, reservationId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertChangeAsync(ICommandTransaction tx, StayRateChangeRow r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT gst.stay_rate_change(property_id, stay_id, reservation_id, room_id, business_date, changed_utc, changed_by, approval_id, approved_by, scope, reason,
              old_rate_paise, new_rate_paise, old_people, new_people, old_children, new_children, effective_from, reposted_json)
            OUTPUT INSERTED.change_id
            VALUES (@p, @s, @r, @room, @bd, @u, @by, @a, @ab, @sc, @why, @or, @nr, @op, @np, @oc, @nc, @from, @json);
            """);
        cmd.Add("@p", SqlDbType.Int, r.PropertyId);
        cmd.Add("@s", SqlDbType.BigInt, r.StayId);
        cmd.Add("@r", SqlDbType.BigInt, r.ReservationId);
        cmd.Add("@room", SqlDbType.Int, r.RoomId);
        cmd.Add("@bd", SqlDbType.Date, D(r.BusinessDate));
        cmd.Add("@u", SqlDbType.DateTime2, r.Utc);
        cmd.Add("@by", SqlDbType.Int, r.ChangedBy);
        cmd.Add("@a", SqlDbType.BigInt, r.ApprovalId);
        cmd.Add("@ab", SqlDbType.Int, r.ApprovedBy);
        cmd.Add("@sc", SqlDbType.VarChar, r.Scope, 10);
        cmd.Add("@why", SqlDbType.NVarChar, r.Reason, 200);
        cmd.Add("@or", SqlDbType.BigInt, r.OldRate);
        cmd.Add("@nr", SqlDbType.BigInt, r.NewRate);
        cmd.Add("@op", SqlDbType.TinyInt, (byte)r.OldPeople);
        cmd.Add("@np", SqlDbType.TinyInt, (byte)r.NewPeople);
        cmd.Add("@oc", SqlDbType.TinyInt, (byte)r.OldChildren);
        cmd.Add("@nc", SqlDbType.TinyInt, (byte)r.NewChildren);
        cmd.Add("@from", SqlDbType.Date, D(r.EffectiveFrom));
        cmd.Add("@json", SqlDbType.NVarChar, r.RepostedJson, 2000);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
