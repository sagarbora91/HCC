// F12-RPT-REVENUE · read-only loaders shared by the six revenue reports (SQL over inv / ota / meas; yield figures through IYieldStore + YieldEngine).
// Nothing here writes. Booking date = reservation created (property-local date), cancellation date = status change of a Cancelled reservation.
using System.Data;
using Hcc.Application.Configuration;
using Hcc.Application.Yield;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasRevenue;

internal sealed record RType(int Id, string Name, int Rooms);

internal sealed record RRoom(int Id, int TypeId, DateOnly? RetiredOn);

internal sealed record RStay(long Id, int RoomId, int TypeId, DateOnly From, DateOnly To, string Status, string Channel, long Rate, DateOnly BookedOn, DateOnly? CancelledOn, bool Live);

internal sealed record REvent(string Name, DateOnly From, DateOnly To, int ImpactPct);

internal sealed record ROtb(DateOnly TakenOn, DateOnly Night, int Rooms, long RevenuePaise);

internal sealed record RCompetitor(string Id, string Name);

internal sealed record RShop(DateOnly ShopDate, string CompetitorId, int TypeId, DateOnly StayDate, long RatePaise, string ClientId);

internal sealed record RCounter(DateOnly Night, string Kind, int Refused, int Reshaped);

internal sealed record RWalked(DateOnly Night, long ReservationId, long CostPaise);

internal sealed record RChannel(int Id, int MarkupBps, IReadOnlySet<int> SellsTypes);

internal static class MeasRevenueData
{
    private static SqlCommand Cmd(SqlConnection c, string sql, int propertyId)
    {
        var cmd = new SqlCommand(sql, c);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, propertyId);
        return cmd;
    }

    public static async Task<IReadOnlyList<RType>> TypesAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var list = new List<RType>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, """
            SELECT t.room_type_id, COALESCE(t.name, t.code), (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = t.room_type_id AND r.active = 1)
              FROM inv.room_type t WHERE t.property_id = @p AND t.active = 1 ORDER BY t.room_type_id;
            """, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RType(rd.GetInt32(0), GuestsSql.Str(rd, 1), rd.GetInt32(2)));
        return list;
    }

    public static async Task<IReadOnlyList<RRoom>> RoomsAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var list = new List<RRoom>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT room_id, room_type_id, retired_on FROM inv.room WHERE property_id = @p;", propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RRoom(rd.GetInt32(0), rd.GetInt32(1), GuestsSql.DateN(rd, 2)));
        return list;
    }

    /// <summary>Rooms in service on a night (optionally one type): not retired on or before it.</summary>
    public static int Sellable(IReadOnlyList<RRoom> rooms, DateOnly date, int? typeId = null) =>
        rooms.Count(r => (typeId is null || r.TypeId == typeId) && (r.RetiredOn is null || r.RetiredOn > date));

    public static async Task<IReadOnlyList<RStay>> StaysAsync(SqlPlatform platform, int propertyId, GuestsSql.Clock clock, DateTime nowUtc, CancellationToken ct)
    {
        var list = new List<RStay>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, """
            SELECT reservation_id, ISNULL(room_id, 0), ISNULL(room_type_id, 0), arrival, departure, status, source, ISNULL(nightly_rate_paise, 0), created_utc, status_utc, hold_until_utc
              FROM inv.reservation WHERE property_id = @p AND status <> 'Expired' AND departure > arrival;
            """, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var status = rd.GetString(5);
            var created = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(rd.GetDateTime(8), DateTimeKind.Utc), clock.Zone));
            DateOnly? cancelled = status == "Cancelled" && !rd.IsDBNull(9)
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(rd.GetDateTime(9), DateTimeKind.Utc), clock.Zone)) : status == "Cancelled" ? GuestsSql.Date(rd, 3).AddDays(-1) : null;
            if (status == "Held" && (rd.IsDBNull(10) || rd.GetDateTime(10) <= nowUtc)) continue; // a lapsed hold holds nothing
            var live = status is "Confirmed" or "InHouse" or "Departed" or "Held";
            list.Add(new RStay(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt32(2), GuestsSql.Date(rd, 3), GuestsSql.Date(rd, 4), status, GuestsSql.Str(rd, 6), rd.GetInt64(7), created, cancelled, live));
        }
        return list;
    }

    public static async Task<IReadOnlyList<REvent>> EventsAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var list = new List<REvent>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT name, from_date, to_date, impact_pct FROM meas.event WHERE property_id = @p AND deleted = 0 ORDER BY from_date, event_id;", propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new REvent(GuestsSql.Str(rd, 0), GuestsSql.Date(rd, 1), GuestsSql.Date(rd, 2), rd.GetInt32(3)));
        return list;
    }

    public static async Task<IReadOnlyList<ROtb>> OtbAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var list = new List<ROtb>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT taken_on, night, rooms, revenue_paise FROM meas.otb_snapshot WHERE property_id = @p;", propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new ROtb(GuestsSql.Date(rd, 0), GuestsSql.Date(rd, 1), rd.GetInt32(2), rd.GetInt64(3)));
        return list;
    }

    public static async Task<IReadOnlyList<RCompetitor>> CompetitorsAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var list = new List<RCompetitor>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT TOP (6) competitor_id, name FROM meas.competitor WHERE property_id = @p AND active = 1 ORDER BY name;", propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RCompetitor(rd.GetString(0), GuestsSql.Str(rd, 1)));
        return list;
    }

    public static async Task<IReadOnlyList<RShop>> ShopsAsync(SqlPlatform platform, int propertyId, DateOnly from, DateOnly to, int typeId, CancellationToken ct)
    {
        var list = new List<RShop>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT shop_date, competitor_id, room_type_id, stay_date, rate_paise, client_id FROM meas.rate_shop WHERE property_id = @p AND stay_date BETWEEN @from AND @to AND room_type_id = @t;", propertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
        GuestsSql.Add(cmd, "@t", SqlDbType.Int, typeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RShop(GuestsSql.Date(rd, 0), rd.GetString(1), rd.GetInt32(2), GuestsSql.Date(rd, 3), rd.GetInt64(4), rd.GetString(5)));
        return list;
    }

    public static async Task<bool> AnyShopAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT CASE WHEN EXISTS (SELECT 1 FROM meas.rate_shop WHERE property_id = @p) OR EXISTS (SELECT 1 FROM meas.competitor WHERE property_id = @p) THEN 1 ELSE 0 END;", propertyId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
    }

    public static async Task<IReadOnlyList<RCounter>> CountersAsync(SqlPlatform platform, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var list = new List<RCounter>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT night, kind, refused, reshaped FROM meas.restriction_counter WHERE property_id = @p AND night BETWEEN @from AND @to;", propertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RCounter(GuestsSql.Date(rd, 0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3)));
        return list;
    }

    public static async Task<IReadOnlyList<RWalked>> WalkedAsync(SqlPlatform platform, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var list = new List<RWalked>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, "SELECT night, reservation_id, ISNULL(cost_paise, 0) FROM meas.walked WHERE property_id = @p AND night BETWEEN @from AND @to;", propertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RWalked(GuestsSql.Date(rd, 0), rd.GetInt64(1), rd.GetInt64(2)));
        return list;
    }

    /// <summary>Connected channels with the room types they sell (mapped and not stop-sold) and their markup.</summary>
    public static async Task<IReadOnlyList<RChannel>> ChannelsAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var map = new Dictionary<int, (int Markup, HashSet<int> Types)>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(conn, """
            SELECT c.channel_id, c.markup_bps, m.room_type_id FROM ota.channel c
              LEFT JOIN ota.channel_room_map m ON m.channel_id = c.channel_id AND m.stop_sell = 0
             WHERE c.property_id = @p AND c.status = 'Connected';
            """, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt32(0);
            if (!map.TryGetValue(id, out var e)) map[id] = e = (rd.GetInt32(1), []);
            if (!rd.IsDBNull(2)) e.Types.Add(rd.GetInt32(2));
        }
        return map.Select(kv => new RChannel(kv.Key, kv.Value.Markup, kv.Value.Types)).ToList();
    }

    /// <summary>yld.overbook.&lt;roomTypeId&gt; (Owner setting, default 0, integer 0 to 10).</summary>
    public static async Task<int> OverbookAllowedAsync(ISettingsReader? reader, int propertyId, int typeId, CancellationToken ct)
    {
        if (reader is null) return 0;
        try
        {
            var v = await reader.GetAsync(propertyId, "yld.overbook." + typeId, null, ct).ConfigureAwait(false);
            return v.Kind == Hcc.Domain.Configuration.ConfigValueKind.Number ? (int)Math.Clamp(v.Number, 0, 10) : 0;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return 0; }
    }

    public static async Task<bool> RestrictionsEnforcedAsync(ISettingsReader? reader, int propertyId, CancellationToken ct)
    {
        if (reader is null) return false;
        try { return (await reader.GetAsync(propertyId, "yld.restrictionsEnforce", null, ct).ConfigureAwait(false)).Flag; }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }

    public static async Task<bool> RealForecastOnAsync(IMeasurementGate? gate, int propertyId, CancellationToken ct) =>
        gate is null || await gate.IsOnAsync(propertyId, "meas.rev.forecast", ct).ConfigureAwait(false);

    /// <summary>Yield engine for a window (BAR, base, suggested rate, offers, restrictions).</summary>
    public static async Task<YieldEngine> EngineAsync(IYieldStore store, int propertyId, DateOnly from, int nights, DateTime nowUtc, CancellationToken ct)
    {
        var inputs = await store.LoadInputsAsync(propertyId, from, Math.Max(1, nights), nowUtc, null, ct).ConfigureAwait(false);
        var cfg = await store.LoadConfigAsync(propertyId, null, ct).ConfigureAwait(false);
        return new YieldEngine(cfg, inputs);
    }

    /// <summary>Reference room type for single-rate columns: the type with the most rooms (ties: first).</summary>
    public static RType? RefType(IReadOnlyList<RType> types) => types.Count == 0 ? null : types.Aggregate((best, t) => t.Rooms > best.Rooms ? t : best);

    public static bool Live(RStay x) => x.Live;

    public static IEnumerable<DateOnly> Nights(RStay x)
    {
        for (var d = x.From; d < x.To; d = d.AddDays(1)) yield return d;
    }

    /// <summary>Rooms with a live stay on the night (optionally one type).</summary>
    public static int RoomsOn(IReadOnlyList<RStay> stays, DateOnly night, int? typeId = null) =>
        stays.Where(x => x.Live && x.From <= night && x.To > night && (typeId is null || x.TypeId == typeId)).Select(x => x.RoomId).Distinct().Count();

    public static MeasFcStay ToFc(RStay x) => new(x.RoomId.ToString(), x.From, x.To, x.BookedOn, x.CancelledOn, x.Status == "NoShow");
}
