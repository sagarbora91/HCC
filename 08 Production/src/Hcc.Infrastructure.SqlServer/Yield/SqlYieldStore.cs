using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Yield;
using Hcc.Contracts.Yield;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Yield;

/// <summary>SQL adapter for <see cref="IYieldStore"/> (schema yld). Reads inv.* only to learn rooms, tariffs and bookings; never writes it.</summary>
public sealed class SqlYieldStore(SqlConnectionFactory connections) : IYieldStore
{
    private const string SeedSql = """
        IF NOT EXISTS (SELECT 1 FROM yld.settings WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p)
        BEGIN
          INSERT yld.settings(property_id) VALUES (@p);
          INSERT yld.rate_plan(property_id, plan_code, name, adj_bps, min_nights, sell_dow, dayuse_from, dayuse_to, dayuse_close_occ, active, note) VALUES
            (@p, 'BAR', N'Best available rate', 0, 1, NULL, NULL, NULL, NULL, 1, N'Published to the tariff schedule; all other plans derive from it.'),
            (@p, 'CORP', N'Corporate', -1200, 1, NULL, NULL, NULL, NULL, 1, N'Contracted companies; GSTIN on the booking.'),
            (@p, 'WKND', N'Weekend stay', -500, 2, '560', NULL, NULL, NULL, 1, N'Fri, Sat and Sun nights only; minimum 2 nights.'),
            (@p, 'LONG7', N'Long-stay 7+ nights', -1500, 7, NULL, NULL, NULL, NULL, 1, N'Applies to every night of a 7+ night stay.'),
            (@p, 'DAYUSE', N'Day-use (10:00–17:00)', -4500, 0, NULL, '10:00', '17:00', 90, 1, N'No overnight; room must be cleaned before evening arrivals. Closed on very high demand.');
        END
        ELSE IF @lock = 1 SELECT 1 FROM yld.settings WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;
        """;

    public async Task EnsureSeededAsync(ICommandTransaction tx, int propertyId, bool lockProperty, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await SeedAsync(s, propertyId, lockProperty, ct).ConfigureAwait(false);
    }

    private static async Task SeedAsync(SqlScope s, int propertyId, bool lockProperty, CancellationToken ct)
    {
        await using var q = s.Command(SeedSql);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Add("@lock", SqlDbType.Bit).Value = lockProperty;
        try
        {
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw YieldVersion.NotFound("Property");
        }
    }

    /// <summary>Reads (no command transaction) make sure the property has its settings and default plans, in a short transaction of their own.</summary>
    private async Task<T> ReadAsync<T>(int propertyId, ICommandTransaction? tx, Func<SqlScope, Task<T>> work, CancellationToken ct)
    {
        if (tx is not null) return await work(SqlScope.Of(tx, connections)).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using (var t = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            await SeedAsync(new SqlScope(conn, t, connections), propertyId, false, ct).ConfigureAwait(false);
            await t.CommitAsync(ct).ConfigureAwait(false);
        }

        return await work(new SqlScope(conn, null, connections)).ConfigureAwait(false);
    }

    // ───────────── configuration ─────────────
    private sealed record Raw(int[] Dow, YieldGuard Guard, string SettingsVersion, List<(YieldPlan Plan, string Version)> Plans,
        List<(YieldSeason Season, string Version)> Seasons, List<(YieldOccRule Rule, string Version)> Occ,
        List<(int Id, YieldRestrictionDef Def, string Version)> Restrictions, List<(YieldOverrideDef Def, int Actor, DateTime At)> Overrides,
        List<(DateOnly Night, int Rooms)> Forecast, List<YieldHistoryView> History);

    private static async Task<Raw> ReadRawAsync(SqlScope s, int propertyId, int historyLimit, CancellationToken ct)
    {
        await using var q = s.Command("""
            SELECT dow0_bps, dow1_bps, dow2_bps, dow3_bps, dow4_bps, dow5_bps, dow6_bps, floor_bps, ceil_bps, round_to_paise, row_ver FROM yld.settings WHERE property_id = @p;
            SELECT plan_code, name, adj_bps, min_nights, sell_dow, dayuse_from, dayuse_to, dayuse_close_occ, active, note, row_ver FROM yld.rate_plan WHERE property_id = @p ORDER BY CASE plan_code WHEN 'BAR' THEN 0 ELSE 1 END, plan_code;
            SELECT season_id, name, from_date, to_date, adj_bps, priority, row_ver FROM yld.season WHERE property_id = @p ORDER BY season_id;
            SELECT st.season_id, st.room_type_id FROM yld.season_type st JOIN yld.season x ON x.season_id = st.season_id WHERE x.property_id = @p ORDER BY st.season_id, st.room_type_id;
            SELECT rule_id, op, occ_pct, adj_bps, row_ver FROM yld.occ_rule WHERE property_id = @p ORDER BY rule_id;
            SELECT restriction_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note, row_ver FROM yld.restriction WHERE property_id = @p ORDER BY restriction_id;
            SELECT room_type_id, night, rate_paise, reason, actor_id, set_utc FROM yld.rate_override WHERE property_id = @p ORDER BY night, room_type_id;
            SELECT night, rooms_sold FROM yld.forecast WHERE property_id = @p ORDER BY night;
            SELECT TOP (@h) history_id, at_utc, actor_id, area, before_text, after_text, reason FROM yld.history WHERE property_id = @p ORDER BY history_id DESC;
            """);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Int("@h", historyLimit);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw YieldVersion.NotFound("Yield settings");
        var dow = Enumerable.Range(0, 7).Select(rd.GetInt32).ToArray();
        var guard = new YieldGuard(rd.GetInt32(7), rd.GetInt32(8), rd.GetInt32(9));
        var settingsVersion = YieldVersion.ToWire((byte[])rd[10]);
        var plans = new List<(YieldPlan, string)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var days = rd.IsDBNull(4) ? null : (IReadOnlyList<int>)rd.GetString(4).Select(ch => ch - '0').ToList();
            plans.Add((new YieldPlan(rd.GetString(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), days, rd.GetStringOrNull(5), rd.GetStringOrNull(6),
                rd.IsDBNull(7) ? null : rd.GetInt32(7), rd.GetBoolean(8), rd.GetString(9)), YieldVersion.ToWire((byte[])rd[10])));
        }

        var seasonRows = new List<(int Id, string Name, DateOnly From, DateOnly To, int Adj, int Prio, string Ver)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            seasonRows.Add((rd.GetInt32(0), rd.GetString(1), rd.GetDate(2), rd.GetDate(3), rd.GetInt32(4), rd.GetInt32(5), YieldVersion.ToWire((byte[])rd[6])));
        var seasonTypes = new List<(int Season, int Type)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) seasonTypes.Add((rd.GetInt32(0), rd.GetInt32(1)));
        var seasons = seasonRows.Select(x => (new YieldSeason(x.Id, x.Name, x.From, x.To, x.Adj, seasonTypes.Where(t => t.Season == x.Id).Select(t => t.Type).ToList(), x.Prio), x.Ver)).ToList();
        var occ = new List<(YieldOccRule, string)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            occ.Add((new YieldOccRule(rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3)), YieldVersion.ToWire((byte[])rd[4])));
        var restrictions = new List<(int, YieldRestrictionDef, string)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            restrictions.Add((rd.GetInt32(0), new YieldRestrictionDef(rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetInt32(2), rd.GetDate(3), rd.GetDate(4), rd.GetByte(5), rd.GetBoolean(6), rd.GetString(7)),
                YieldVersion.ToWire((byte[])rd[8])));
        var overrides = new List<(YieldOverrideDef, int, DateTime)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            overrides.Add((new YieldOverrideDef(rd.GetInt32(0), rd.GetDate(1), rd.GetInt64(2), rd.GetString(3)), rd.GetInt32(4), DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc)));
        var forecast = new List<(DateOnly, int)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) forecast.Add((rd.GetDate(0), rd.GetInt32(1)));
        var history = new List<YieldHistoryView>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            history.Add(new YieldHistoryView(rd.GetInt64(0), YieldService.Iso(rd.GetDateTime(1)), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetString(6)));
        return new Raw(dow, guard, settingsVersion, plans, seasons, occ, restrictions, overrides, forecast, history);
    }

    public async Task<YieldConfig> LoadConfigAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default)
    {
        var raw = await ReadAsync(propertyId, tx, s => ReadRawAsync(s, propertyId, 0, ct), ct).ConfigureAwait(false);
        return new YieldConfig(raw.Plans.Select(x => x.Plan).ToList(), raw.Seasons.Select(x => x.Season).ToList(), raw.Dow, raw.Occ.Select(x => x.Rule).ToList(), raw.Guard,
            raw.Restrictions.Select(x => x.Def).ToList(), raw.Overrides.Select(x => x.Def).ToList());
    }

    public async Task<YieldConfigResult> GetConfigViewAsync(int propertyId, int historyLimit, CancellationToken ct = default)
    {
        var raw = await ReadAsync(propertyId, null, s => ReadRawAsync(s, propertyId, historyLimit, ct), ct).ConfigureAwait(false);
        return new YieldConfigResult(
            raw.Plans.Select(x => new YieldPlanView(x.Plan.Code, x.Plan.Name, x.Plan.AdjBps, x.Plan.MinNights, x.Plan.SellOnDays, x.Plan.DayUseFrom, x.Plan.DayUseTo, x.Plan.CloseAtOcc, x.Plan.Active, x.Plan.Note, x.Version)).ToList(),
            raw.Seasons.Select(x => new YieldSeasonView(x.Season.Id, x.Season.Name, StayDates.ToWire(x.Season.From), StayDates.ToWire(x.Season.To), x.Season.AdjBps, x.Season.RoomTypeIds, x.Season.Priority, x.Version)).ToList(),
            raw.Dow, raw.Guard.FloorBps, raw.Guard.CeilBps, raw.Guard.RoundToPaise, raw.SettingsVersion,
            raw.Occ.Select(x => new YieldOccRuleView(x.Rule.Id, x.Rule.Op, x.Rule.Occ, x.Rule.AdjBps, x.Version)).ToList(),
            raw.Restrictions.Select(x => new YieldRestrictionView(x.Id, x.Def.Code, x.Def.RoomTypeId, StayDates.ToWire(x.Def.From), StayDates.ToWire(x.Def.To), x.Def.MinStay, x.Def.ClosedToArrival, x.Def.Note, x.Version)).ToList(),
            raw.Overrides.Select(x => new YieldOverrideView(x.Def.RoomTypeId, StayDates.ToWire(x.Def.Night), x.Def.RatePaise, x.Def.Reason, x.Actor, YieldService.Iso(x.At))).ToList(),
            raw.Forecast.Select(x => new YieldForecastNight(StayDates.ToWire(x.Night), x.Rooms)).ToList(),
            raw.History);
    }

    // ───────────── inventory facts ─────────────
    public async Task<YieldInputs> LoadInputsAsync(int propertyId, DateOnly? from, int nights, DateTime nowUtc, ICommandTransaction? tx = null, CancellationToken ct = default)
    {
        if (nights < 1 || nights > 366) throw HccException.Validation("Choose 1–366 nights.");
        return await ReadAsync(propertyId, tx, async s =>
        {
            await using var q = s.Command("""
                DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
                IF @bd IS NULL THROW 52010, 'Property not found.', 1;
                DECLARE @start DATE = COALESCE(@from, @bd);
                SELECT @bd;
                SELECT room_type_id, code, rack_rate_paise FROM inv.room_type WHERE property_id = @p ORDER BY sort_order, room_type_id;
                SELECT t.room_type_id, t.effective_from, t.rate_paise FROM inv.room_type_tariff t JOIN inv.room_type ty ON ty.room_type_id = t.room_type_id WHERE ty.property_id = @p;
                SELECT o.room_type_id, o.effective_from, o.rate_paise FROM yld.owned_tariff o JOIN inv.room_type ty ON ty.room_type_id = o.room_type_id WHERE ty.property_id = @p;
                ;WITH n AS (SELECT @start AS night, 1 AS i UNION ALL SELECT DATEADD(DAY, 1, night), i + 1 FROM n WHERE i < @n)
                SELECT n.night,
                  (SELECT COUNT(*) FROM inv.room r WHERE r.property_id = @p AND NOT EXISTS (
                      SELECT 1 FROM inv.room_block b WHERE b.room_id = r.room_id AND b.active = 1
                        AND (b.from_night IS NULL OR b.from_night < DATEADD(DAY, 1, n.night)) AND (b.to_night IS NULL OR b.to_night > n.night))),
                  (SELECT COUNT(*) FROM inv.reservation x WHERE x.property_id = @p AND x.arrival <= n.night AND x.departure > n.night
                      AND (x.status IN ('Confirmed', 'InHouse') OR (x.status = 'Held' AND (x.hold_until_utc IS NULL OR @now < x.hold_until_utc)))),
                  COALESCE((SELECT f.rooms_sold FROM yld.forecast f WHERE f.property_id = @p AND f.night = n.night), 0)
                FROM n OPTION (MAXRECURSION 400);
                """);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.NullableDate("@from", from);
            q.Parameters.Int("@n", nights);
            q.Parameters.Utc("@now", nowUtc);
            try
            {
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await rd.ReadAsync(ct).ConfigureAwait(false);
                var bd = rd.GetDate(0);
                var types = new List<YieldRoomType>();
                await rd.NextResultAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) types.Add(new YieldRoomType(rd.GetInt32(0), rd.GetString(1), rd.GetInt64(2)));
                var tariffs = new List<TariffRow>();
                await rd.NextResultAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) tariffs.Add(new TariffRow(rd.GetInt32(0), rd.GetDate(1), rd.GetInt64(2)));
                var owned = new HashSet<TariffRow>();
                await rd.NextResultAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) owned.Add(new TariffRow(rd.GetInt32(0), rd.GetDate(1), rd.GetInt64(2)));
                var sellable = new Dictionary<DateOnly, int>();
                var onBooks = new Dictionary<DateOnly, int>();
                var forecast = new Dictionary<DateOnly, int>();
                await rd.NextResultAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    var d = rd.GetDate(0);
                    sellable[d] = rd.GetInt32(1);
                    onBooks[d] = rd.GetInt32(2);
                    forecast[d] = rd.GetInt32(3);
                }

                // A tariff row counts as yield-owned only while it still holds the rate yield wrote (a manual schedule overwrote it otherwise).
                return new YieldInputs(bd, types, tariffs, owned.Where(o => tariffs.Contains(o)).ToHashSet(), sellable, onBooks, forecast);
            }
            catch (SqlException ex) when (ex.Number == 52010)
            {
                throw YieldVersion.NotFound("Property");
            }
        }, ct).ConfigureAwait(false);
    }

    // ───────────── rule writes ─────────────
    private static async Task<string> VersionedAsync(SqlScope s, string updateSql, Action<SqlParameterCollection> bind, string existsSql, Action<SqlParameterCollection> bindExists,
        string what, CancellationToken ct)
    {
        await using (var u = s.Command(updateSql))
        {
            bind(u.Parameters);
            var v = await u.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (v is byte[] bytes) return YieldVersion.ToWire(bytes);
        }

        await using var e = s.Command(existsSql);
        bindExists(e.Parameters);
        throw await e.ExecuteScalarAsync(ct).ConfigureAwait(false) is null ? YieldVersion.NotFound(what) : YieldVersion.Stale(what);
    }

    public async Task<string> UpdatePlanAsync(ICommandTransaction tx, int propertyId, YieldPlan plan, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        return await VersionedAsync(s,
            "UPDATE yld.rate_plan SET adj_bps = @a, min_nights = @m, active = @act, dayuse_close_occ = @c OUTPUT INSERTED.row_ver WHERE property_id = @p AND plan_code = @code AND row_ver = @v;",
            ps =>
            {
                ps.Int("@a", plan.AdjBps);
                ps.Int("@m", plan.MinNights);
                ps.Add("@act", SqlDbType.Bit).Value = plan.Active;
                ps.Int("@c", plan.CloseAtOcc);
                ps.Int("@p", propertyId);
                ps.Add("@code", SqlDbType.VarChar, 12).Value = plan.Code;
                ps.Version("@v", expected);
            }, "SELECT 1 FROM yld.rate_plan WHERE property_id = @p AND plan_code = @code;",
            ps =>
            {
                ps.Int("@p", propertyId);
                ps.Add("@code", SqlDbType.VarChar, 12).Value = plan.Code;
            }, "The rate plan", ct).ConfigureAwait(false);
    }

    public async Task<(int Id, string Version)> SaveSeasonAsync(ICommandTransaction tx, int propertyId, int? id, byte[]? expected, string name, DateOnly from, DateOnly to,
        int adjBps, IReadOnlyList<int> roomTypeIds, int priority, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        int seasonId;
        string version;
        if (id is null)
        {
            await using var ins = s.Command("INSERT yld.season(property_id, name, from_date, to_date, adj_bps, priority) OUTPUT INSERTED.season_id, INSERTED.row_ver VALUES (@p, @n, @f, @t, @a, @pr);");
            Bind(ins.Parameters);
            await using var rd = await ins.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            seasonId = rd.GetInt32(0);
            version = YieldVersion.ToWire((byte[])rd[1]);
        }
        else
        {
            seasonId = id.Value;
            version = await VersionedAsync(s,
                "UPDATE yld.season SET name = @n, from_date = @f, to_date = @t, adj_bps = @a, priority = @pr OUTPUT INSERTED.row_ver WHERE season_id = @id AND property_id = @p AND row_ver = @v;",
                ps =>
                {
                    Bind(ps);
                    ps.Int("@id", seasonId);
                    ps.Version("@v", expected ?? throw HccException.Validation("expectedVersion is required."));
                }, "SELECT 1 FROM yld.season WHERE season_id = @id AND property_id = @p;",
                ps =>
                {
                    ps.Int("@id", seasonId);
                    ps.Int("@p", propertyId);
                }, "The season", ct).ConfigureAwait(false);
            await using var del = s.Command("DELETE FROM yld.season_type WHERE season_id = @id;");
            del.Parameters.Int("@id", seasonId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var t in roomTypeIds)
        {
            await using var st = s.Command("INSERT yld.season_type(season_id, room_type_id) VALUES (@id, @t);");
            st.Parameters.Int("@id", seasonId);
            st.Parameters.Int("@t", t);
            await st.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return (seasonId, version);

        void Bind(SqlParameterCollection ps)
        {
            ps.Int("@p", propertyId);
            ps.Text("@n", name, 80);
            ps.Date("@f", from);
            ps.Date("@t", to);
            ps.Int("@a", adjBps);
            ps.Int("@pr", priority);
        }
    }

    public async Task DeleteSeasonAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await CheckVersionAsync(s, "SELECT row_ver FROM yld.season WITH (UPDLOCK) WHERE season_id = @id AND property_id = @p;", id, propertyId, expected, "The season", ct).ConfigureAwait(false);
        await using var d1 = s.Command("DELETE FROM yld.season_type WHERE season_id = @id; DELETE FROM yld.season WHERE season_id = @id;");
        d1.Parameters.Int("@id", id);
        await d1.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task CheckVersionAsync(SqlScope s, string sql, int id, int propertyId, byte[] expected, string what, CancellationToken ct)
    {
        await using var q = s.Command(sql);
        q.Parameters.Int("@id", id);
        q.Parameters.Int("@p", propertyId);
        var v = await q.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (v is not byte[] cur) throw YieldVersion.NotFound(what);
        if (!cur.AsSpan().SequenceEqual(expected)) throw YieldVersion.Stale(what);
    }

    public async Task<string> SetDowAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> dow, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        return await VersionedAsync(s,
            "UPDATE yld.settings SET dow0_bps = @d0, dow1_bps = @d1, dow2_bps = @d2, dow3_bps = @d3, dow4_bps = @d4, dow5_bps = @d5, dow6_bps = @d6 OUTPUT INSERTED.row_ver WHERE property_id = @p AND row_ver = @v;",
            ps =>
            {
                for (var i = 0; i < 7; i++) ps.Int("@d" + i.ToString(CultureInfo.InvariantCulture), dow[i]);
                ps.Int("@p", propertyId);
                ps.Version("@v", expected);
            }, "SELECT 1 FROM yld.settings WHERE property_id = @p;", ps => ps.Int("@p", propertyId), "The day-of-week rules", ct).ConfigureAwait(false);
    }

    public async Task<string> SetGuardAsync(ICommandTransaction tx, int propertyId, YieldGuard guard, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        return await VersionedAsync(s,
            "UPDATE yld.settings SET floor_bps = @f, ceil_bps = @c, round_to_paise = @r OUTPUT INSERTED.row_ver WHERE property_id = @p AND row_ver = @v;",
            ps =>
            {
                ps.Int("@f", guard.FloorBps);
                ps.Int("@c", guard.CeilBps);
                ps.Int("@r", guard.RoundToPaise);
                ps.Int("@p", propertyId);
                ps.Version("@v", expected);
            }, "SELECT 1 FROM yld.settings WHERE property_id = @p;", ps => ps.Int("@p", propertyId), "The guardrails", ct).ConfigureAwait(false);
    }

    public async Task<(int Id, string Version)> SaveOccRuleAsync(ICommandTransaction tx, int propertyId, int? id, byte[]? expected, string op, int occ, int adjBps, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        if (id is null)
        {
            await using var ins = s.Command("INSERT yld.occ_rule(property_id, op, occ_pct, adj_bps) OUTPUT INSERTED.rule_id, INSERTED.row_ver VALUES (@p, @op, @o, @a);");
            ins.Parameters.Int("@p", propertyId);
            ins.Parameters.Add("@op", SqlDbType.VarChar, 2).Value = op;
            ins.Parameters.Int("@o", occ);
            ins.Parameters.Int("@a", adjBps);
            await using var rd = await ins.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            return (rd.GetInt32(0), YieldVersion.ToWire((byte[])rd[1]));
        }

        var ver = await VersionedAsync(s,
            "UPDATE yld.occ_rule SET op = @op, occ_pct = @o, adj_bps = @a OUTPUT INSERTED.row_ver WHERE rule_id = @id AND property_id = @p AND row_ver = @v;",
            ps =>
            {
                ps.Add("@op", SqlDbType.VarChar, 2).Value = op;
                ps.Int("@o", occ);
                ps.Int("@a", adjBps);
                ps.Int("@id", id.Value);
                ps.Int("@p", propertyId);
                ps.Version("@v", expected ?? throw HccException.Validation("expectedVersion is required."));
            }, "SELECT 1 FROM yld.occ_rule WHERE rule_id = @id AND property_id = @p;", ps => { ps.Int("@id", id.Value); ps.Int("@p", propertyId); }, "The rule", ct).ConfigureAwait(false);
        return (id.Value, ver);
    }

    public async Task DeleteOccRuleAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await CheckVersionAsync(s, "SELECT row_ver FROM yld.occ_rule WITH (UPDLOCK) WHERE rule_id = @id AND property_id = @p;", id, propertyId, expected, "The rule", ct).ConfigureAwait(false);
        await using var d = s.Command("DELETE FROM yld.occ_rule WHERE rule_id = @id;");
        d.Parameters.Int("@id", id);
        await d.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static YieldRestrictionDef ReadRestriction(SqlDataReader rd, int o = 0)
        => new(rd.GetString(o), rd.IsDBNull(o + 1) ? null : rd.GetInt32(o + 1), rd.GetDate(o + 2), rd.GetDate(o + 3), rd.GetByte(o + 4), rd.GetBoolean(o + 5), rd.GetString(o + 6));

    public async Task<(YieldRestrictionDef? Before, YieldRestrictionDef After, int Id, string Version)> SaveRestrictionAsync(ICommandTransaction tx, int propertyId, int? id,
        byte[]? expected, int? roomTypeId, DateOnly from, DateOnly to, int minStay, bool cta, string note, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        if (id is null)
        {
            await using var ins = s.Command("""
                DECLARE @code VARCHAR(20) = 'YLDR-' + CAST(NEXT VALUE FOR yld.seq_restriction AS VARCHAR(10));
                INSERT yld.restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note)
                OUTPUT INSERTED.code, INSERTED.room_type_id, INSERTED.arrival_from, INSERTED.arrival_to, INSERTED.min_stay, INSERTED.closed_to_arrival, INSERTED.note, INSERTED.restriction_id, INSERTED.row_ver
                VALUES (@p, @code, @t, @f, @to, @m, @c, @n);
                """);
            Bind(ins.Parameters);
            await using var rd = await ins.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            return (null, ReadRestriction(rd), rd.GetInt32(7), YieldVersion.ToWire((byte[])rd[8]));
        }

        YieldRestrictionDef before;
        await using (var cur = s.Command("SELECT code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note, row_ver FROM yld.restriction WITH (UPDLOCK) WHERE restriction_id = @id AND property_id = @p;"))
        {
            cur.Parameters.Int("@id", id.Value);
            cur.Parameters.Int("@p", propertyId);
            await using var rd = await cur.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw YieldVersion.NotFound("The restriction");
            if (!((byte[])rd[7]).AsSpan().SequenceEqual(expected ?? throw HccException.Validation("expectedVersion is required."))) throw YieldVersion.Stale("The restriction");
            before = ReadRestriction(rd);
        }

        await using var up = s.Command("""
            UPDATE yld.restriction SET room_type_id = @t, arrival_from = @f, arrival_to = @to, min_stay = @m, closed_to_arrival = @c, note = @n
            OUTPUT INSERTED.code, INSERTED.room_type_id, INSERTED.arrival_from, INSERTED.arrival_to, INSERTED.min_stay, INSERTED.closed_to_arrival, INSERTED.note, INSERTED.row_ver
            WHERE restriction_id = @id AND property_id = @p;
            """);
        Bind(up.Parameters);
        up.Parameters.Int("@id", id.Value);
        await using var r2 = await up.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await r2.ReadAsync(ct).ConfigureAwait(false);
        return (before, ReadRestriction(r2), id.Value, YieldVersion.ToWire((byte[])r2[7]));

        void Bind(SqlParameterCollection ps)
        {
            ps.Int("@p", propertyId);
            ps.Int("@t", roomTypeId);
            ps.Date("@f", from);
            ps.Date("@to", to);
            ps.Add("@m", SqlDbType.TinyInt).Value = (byte)minStay;
            ps.Add("@c", SqlDbType.Bit).Value = cta;
            ps.Text("@n", note, 200);
        }
    }

    public async Task<YieldRestrictionDef> DeleteRestrictionAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        YieldRestrictionDef old;
        await using (var cur = s.Command("SELECT code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note, row_ver FROM yld.restriction WITH (UPDLOCK) WHERE restriction_id = @id AND property_id = @p;"))
        {
            cur.Parameters.Int("@id", id);
            cur.Parameters.Int("@p", propertyId);
            await using var rd = await cur.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw YieldVersion.NotFound("The restriction");
            if (!((byte[])rd[7]).AsSpan().SequenceEqual(expected)) throw YieldVersion.Stale("The restriction");
            old = ReadRestriction(rd);
        }

        await using var d = s.Command("DELETE FROM yld.restriction WHERE restriction_id = @id;");
        d.Parameters.Int("@id", id);
        await d.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return old;
    }

    public async Task<bool> SetOverrideAsync(ICommandTransaction tx, int propertyId, int roomTypeId, DateOnly night, long? ratePaise, string reason, int actor, DateTime nowUtc, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("""
            DECLARE @existed BIT = CASE WHEN EXISTS (SELECT 1 FROM yld.rate_override WITH (UPDLOCK) WHERE property_id = @p AND room_type_id = @t AND night = @n) THEN 1 ELSE 0 END;
            IF @rate IS NULL DELETE FROM yld.rate_override WHERE property_id = @p AND room_type_id = @t AND night = @n;
            ELSE IF @existed = 1 UPDATE yld.rate_override SET rate_paise = @rate, reason = @reason, actor_id = @actor, set_utc = @now WHERE property_id = @p AND room_type_id = @t AND night = @n;
            ELSE INSERT yld.rate_override(property_id, room_type_id, night, rate_paise, reason, actor_id, set_utc) VALUES (@p, @t, @n, @rate, @reason, @actor, @now);
            SELECT @existed;
            """);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Int("@t", roomTypeId);
        q.Parameters.Date("@n", night);
        q.Parameters.Big("@rate", ratePaise);
        q.Parameters.Text("@reason", reason, 200);
        q.Parameters.Int("@actor", actor);
        q.Parameters.Utc("@now", nowUtc);
        return (bool)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task SetForecastAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<(DateOnly Night, int RoomsSold)> nights, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        foreach (var (night, rooms) in nights.OrderBy(x => x.Night))
        {
            await using var q = s.Command("""
                UPDATE yld.forecast SET rooms_sold = @r WHERE property_id = @p AND night = @n;
                IF @@ROWCOUNT = 0 INSERT yld.forecast(property_id, night, rooms_sold) VALUES (@p, @n, @r);
                """);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Date("@n", night);
            q.Parameters.Int("@r", rooms);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task AddHistoryAsync(ICommandTransaction tx, int propertyId, DateTime nowUtc, int actor, string area, string before, string after, string reason, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("INSERT yld.history(property_id, at_utc, actor_id, area, before_text, after_text, reason) VALUES (@p, @now, @a, @area, @b, @af, @r);");
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Utc("@now", nowUtc);
        q.Parameters.Int("@a", actor);
        q.Parameters.Text("@area", Cut(area, 120), 120);
        q.Parameters.Text("@b", Cut(before, 400), 400);
        q.Parameters.Text("@af", Cut(after, 400), 400);
        q.Parameters.Text("@r", Cut(reason, 200), 200);
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    // ───────────── publish and proposals ─────────────
    public async Task MarkOwnedAsync(ICommandTransaction tx, IReadOnlyList<TariffRow> rows, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        foreach (var r in rows)
        {
            await using var q = s.Command("""
                UPDATE yld.owned_tariff SET rate_paise = @r WHERE room_type_id = @t AND effective_from = @d;
                IF @@ROWCOUNT = 0 INSERT yld.owned_tariff(room_type_id, effective_from, rate_paise) VALUES (@t, @d, @r);
                """);
            q.Parameters.Int("@t", r.RoomTypeId);
            q.Parameters.Date("@d", r.EffectiveFrom);
            q.Parameters.Big("@r", r.RatePaise);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<long> InsertPublishAsync(ICommandTransaction tx, int propertyId, DateOnly from, DateOnly to, int actor, DateTime nowUtc, string reason, long? proposalId,
        IReadOnlyList<YieldPublishRow> rows, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        long id;
        await using (var q = s.Command("INSERT yld.publish(property_id, from_night, to_night, actor_id, published_utc, reason, proposal_id) OUTPUT INSERTED.publish_id VALUES (@p, @f, @t, @a, @now, @r, @pr);"))
        {
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Date("@f", from);
            q.Parameters.Date("@t", to);
            q.Parameters.Int("@a", actor);
            q.Parameters.Utc("@now", nowUtc);
            q.Parameters.Text("@r", reason, 200);
            q.Parameters.Big("@pr", proposalId);
            id = (long)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var r in rows)
        {
            await using var q = s.Command("INSERT yld.publish_row(publish_id, room_type_id, night, before_paise, after_paise, closing) VALUES (@id, @t, @n, @b, @a, @c);");
            q.Parameters.Big("@id", id);
            q.Parameters.Int("@t", r.RoomTypeId);
            q.Parameters.Date("@n", r.Date);
            q.Parameters.Big("@b", r.Before);
            q.Parameters.Big("@a", r.After);
            q.Parameters.Add("@c", SqlDbType.Bit).Value = r.Closing;
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task<long> InsertProposalAsync(ICommandTransaction tx, int propertyId, YieldProposalDraft d, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        long id;
        await using (var q = s.Command("""
            INSERT yld.proposal(property_id, from_night, to_night, status, proposed_by, proposed_utc, expires_utc, reason, sig_sha256, changed_nights)
            OUTPUT INSERTED.proposal_id VALUES (@p, @f, @t, 'Pending', @by, @now, @exp, @r, @sig, @ch);
            """))
        {
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Date("@f", d.From);
            q.Parameters.Date("@t", d.To);
            q.Parameters.Int("@by", d.ProposedBy);
            q.Parameters.Utc("@now", d.ProposedUtc);
            q.Parameters.Utc("@exp", d.ExpiresUtc);
            q.Parameters.Text("@r", Cut(d.Reason, 200), 200);
            q.Parameters.Add("@sig", SqlDbType.Char, 64).Value = d.Signature;
            q.Parameters.Int("@ch", d.ChangedNights);
            id = (long)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var t in d.RoomTypeIds)
        {
            await using var q = s.Command("INSERT yld.proposal_type(proposal_id, room_type_id) VALUES (@id, @t);");
            q.Parameters.Big("@id", id);
            q.Parameters.Int("@t", t);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (type, night, rate) in d.Rates)
        {
            await using var q = s.Command("INSERT yld.proposal_rate(proposal_id, room_type_id, night, rate_paise) VALUES (@id, @t, @n, @r);");
            q.Parameters.Big("@id", id);
            q.Parameters.Int("@t", type);
            q.Parameters.Date("@n", night);
            q.Parameters.Big("@r", rate);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task<bool> HasLivePendingProposalAsync(ICommandTransaction tx, int propertyId, DateTime nowUtc, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("SELECT COUNT(*) FROM yld.proposal WHERE property_id = @p AND status = 'Pending' AND expires_utc > @now;");
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Utc("@now", nowUtc);
        return (int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    private const string ProposalSelect = """
        SELECT proposal_id, from_night, to_night, status, proposed_by, proposed_utc, expires_utc, reason, sig_sha256, changed_nights, decided_by, decided_utc, decision_note, publish_id, row_ver
        FROM yld.proposal
        """;

    private static async Task<List<YieldProposalRecord>> ReadProposalsAsync(SqlScope s, string where, Action<SqlParameterCollection> bind, string top, CancellationToken ct)
    {
        var heads = new List<(long Id, DateOnly From, DateOnly To, string Status, int By, DateTime At, DateTime Exp, string Reason, string Sig, int Ch, int? DecBy, DateTime? DecAt, string? Note, long? Pub, byte[] Ver)>();
        await using (var q = s.Command(ProposalSelect.Replace("SELECT", "SELECT " + top, StringComparison.Ordinal) + (where.Length > 0 ? " WHERE " + where : "") + " ORDER BY proposal_id DESC;"))
        {
            bind(q.Parameters);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                heads.Add((rd.GetInt64(0), rd.GetDate(1), rd.GetDate(2), rd.GetString(3), rd.GetInt32(4), DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc), DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc),
                    rd.GetString(7), rd.GetString(8).Trim(), rd.GetInt32(9), rd.IsDBNull(10) ? null : rd.GetInt32(10), rd.GetUtcOrNull(11), rd.GetStringOrNull(12), rd.GetInt64OrNull(13), (byte[])rd[14]));
        }

        var list = new List<YieldProposalRecord>();
        foreach (var h in heads)
        {
            var types = new List<int>();
            var rates = new List<(int, DateOnly, long)>();
            await using var q = s.Command("SELECT room_type_id FROM yld.proposal_type WHERE proposal_id = @id ORDER BY room_type_id; SELECT room_type_id, night, rate_paise FROM yld.proposal_rate WHERE proposal_id = @id ORDER BY room_type_id, night;");
            q.Parameters.Big("@id", h.Id);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) types.Add(rd.GetInt32(0));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rates.Add((rd.GetInt32(0), rd.GetDate(1), rd.GetInt64(2)));
            list.Add(new YieldProposalRecord(h.Id, h.From, h.To, h.Status, h.By, h.At, h.Exp, h.Reason, h.Sig, h.Ch, h.DecBy, h.DecAt, h.Note, h.Pub, types, rates, h.Ver));
        }

        return list;
    }

    public async Task<YieldProposalRecord?> GetProposalAsync(int propertyId, long proposalId, ICommandTransaction? tx = null, CancellationToken ct = default)
    {
        var list = await ReadAsync(propertyId, tx, s => ReadProposalsAsync(s, "property_id = @p AND proposal_id = @id" + (tx is null ? "" : ""), ps =>
        {
            ps.Int("@p", propertyId);
            ps.Big("@id", proposalId);
        }, "TOP (1)", ct), ct).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public async Task<string> CompleteProposalAsync(ICommandTransaction tx, long proposalId, string status, int decidedBy, DateTime nowUtc, string? note, long? publishId, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("UPDATE yld.proposal SET status = @s, decided_by = @by, decided_utc = @now, decision_note = @n, publish_id = @pub OUTPUT INSERTED.row_ver WHERE proposal_id = @id AND status = 'Pending';");
        q.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = status;
        q.Parameters.Int("@by", decidedBy);
        q.Parameters.Utc("@now", nowUtc);
        q.Parameters.Text("@n", note is null ? null : Cut(note, 200), 200);
        q.Parameters.Big("@pub", publishId);
        q.Parameters.Big("@id", proposalId);
        return await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is byte[] v ? YieldVersion.ToWire(v) : throw YieldVersion.Stale("The request");
    }

    public async Task<IReadOnlyList<YieldProposalRecord>> ListProposalsAsync(int propertyId, int limit, CancellationToken ct = default)
        => await ReadAsync(propertyId, null, s => ReadProposalsAsync(s, "property_id = @p", ps => ps.Int("@p", propertyId), $"TOP ({Math.Clamp(limit, 1, 200).ToString(CultureInfo.InvariantCulture)})", ct), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<YieldPublishView>> ListPublishesAsync(int propertyId, int limit, CancellationToken ct = default)
        => await ReadAsync(propertyId, null, async s =>
        {
            var heads = new List<(long Id, DateOnly From, DateOnly To, int Actor, DateTime At, string Reason, long? Proposal)>();
            await using (var q = s.Command($"SELECT TOP ({Math.Clamp(limit, 1, 200).ToString(CultureInfo.InvariantCulture)}) publish_id, from_night, to_night, actor_id, published_utc, reason, proposal_id FROM yld.publish WHERE property_id = @p ORDER BY publish_id DESC;"))
            {
                q.Parameters.Int("@p", propertyId);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    heads.Add((rd.GetInt64(0), rd.GetDate(1), rd.GetDate(2), rd.GetInt32(3), DateTime.SpecifyKind(rd.GetDateTime(4), DateTimeKind.Utc), rd.GetString(5), rd.GetInt64OrNull(6)));
            }

            var views = new List<YieldPublishView>();
            foreach (var h in heads)
            {
                var rows = new List<YieldPublishRowView>();
                await using var q = s.Command("SELECT room_type_id, night, before_paise, after_paise, closing FROM yld.publish_row WHERE publish_id = @id ORDER BY room_type_id, night;");
                q.Parameters.Big("@id", h.Id);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    rows.Add(new YieldPublishRowView(rd.GetInt32(0), StayDates.ToWire(rd.GetDate(1)), rd.GetInt64(2), rd.GetInt64(3), rd.GetBoolean(4)));
                views.Add(new YieldPublishView(h.Id, StayDates.ToWire(h.From), StayDates.ToWire(h.To), h.Actor, YieldService.Iso(h.At), h.Reason, h.Proposal, rows));
            }

            return (IReadOnlyList<YieldPublishView>)views;
        }, ct).ConfigureAwait(false);
}
