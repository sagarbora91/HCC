using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Ota;
using Hcc.Application.Reservations;
using Hcc.Contracts.Ota;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Ota;
using Hcc.Infrastructure.SqlServer.Reservations;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Ota.Tests.Harness;

/// <summary>
/// The prototype BASE scenario in SQL (property 1, clock 08:00 IST on 24 Oct 2026) plus the module-21 fixture (4 fictional channels, 6 pending
/// inbound messages, 1 historical rejection, 3 scripted messages, same figures as <c>03 Prototype/src/fixtures/ext/channels.ts</c>) and the REAL access policy
/// with one user per role. The P2 services run over the same database with that policy, exactly as in the composition.
/// </summary>
public sealed class OtaFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public GoldenInventory Golden { get; } = GoldenInventory.Load();

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public TestClock Clock { get; private set; } = null!;

    public SqlAccessPolicy Policy { get; private set; } = null!;

    public SqlOtaStore Store { get; private set; } = null!;

    public InventoryService Inventory { get; private set; } = null!;

    public ReservationService Booking { get; private set; } = null!;

    public OtaQueries Queries { get; private set; } = null!;

    public OtaService Service { get; private set; } = null!;

    public OtaChannelManagerProjector Projector { get; private set; } = null!;

    public Dictionary<string, int> TypeIds { get; } = [];

    public Dictionary<string, int> ChannelIds { get; } = [];

    public Dictionary<string, long> MessageIds { get; } = [];

    public int Owner, Manager, Reception, Accountant, TechAdmin, Cashier;

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        await Golden.SeedAsync(_db);
        Clock = new TestClock(new DateTimeOffset(GoldenInventory.LocalToUtc(Golden.Raw.Clock), TimeSpan.Zero));
        var conns = _db.Platform.Connections;
        Policy = new SqlAccessPolicy(conns, Clock, []);
        var inv = new SqlInventoryStore(conns);
        Inventory = new InventoryService(inv, _db.Platform.Commands, Policy, new Hcc.Application.Tax.FixedGstRateResolver(Golden.Raw.TaxBps), Clock);
        Booking = new ReservationService(new SqlReservationStore(conns), inv, Inventory, _db.Platform.Commands, Policy,
            new Hcc.Application.Billing.BillingReservationMoney(new Hcc.Infrastructure.SqlServer.Billing.SqlBillingStore(conns)), Clock);
        Store = new SqlOtaStore(conns);
        Queries = new OtaQueries(Store, inv, Inventory, Booking, Policy, Clock);
        Service = new OtaService(Store, Queries, Inventory, Booking, _db.Platform.Commands, Policy, Clock);
        Projector = new OtaChannelManagerProjector(_db.Platform.Outbox, conns, Clock);

        Owner = await SeedUserAsync("owner", "OWN");
        Manager = await SeedUserAsync("manager", "MGR");
        Reception = await SeedUserAsync("reception", "REC");
        Accountant = await SeedUserAsync("accountant", "ACC");
        TechAdmin = await SeedUserAsync("techadmin", "TAD");
        Cashier = await SeedUserAsync("cashier", "CSH");

        await using (var conn = await conns.OpenAsync())
        {
            await using var q = new Microsoft.Data.SqlClient.SqlCommand("SELECT code, room_type_id FROM inv.room_type;", conn);
            await using var rd = await q.ExecuteReaderAsync();
            while (await rd.ReadAsync()) TypeIds[rd.GetString(0)] = rd.GetInt32(1);
        }

        await SeedChannelsAsync();
    }

    private async Task SeedChannelsAsync()
    {
        const string stamp = "2026-10-24 02:15:00"; // 07:45 IST
        var sql = new System.Text.StringBuilder();
        void Channel(string code, string name, string status, int com, int mk, string? sync, string note) =>
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT ota.channel(property_id, code, name, status, commission_bps, markup_bps, last_sync_utc, status_note) VALUES (1, '{code}', N'{name}', '{status}', {com}, {mk}, {(sync is null ? "NULL" : "'" + sync + "'")}, N'{note}');");
        Channel("BOOKNEST", "BookNest", "Connected", 1800, 1000, stamp, "Simulated connection");
        Channel("STAYHUB", "StayHub", "Connected", 1500, 800, stamp, "Simulated connection");
        Channel("TRIPKART", "TripKart", "Error", 1200, 500, "2026-10-24 01:00:00", "Simulated: channel credentials expired at 06:30 — reconnect to resume");
        Channel("ROOMRAAHI", "RoomRaahi", "Disconnected", 1000, 0, null, "Not yet set up");
        void Map(string ch, string type, string code, bool stop = false) =>
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT ota.channel_room_map(channel_id, room_type_id, channel_code, stop_sell) SELECT c.channel_id, t.room_type_id, '{code}', {(stop ? 1 : 0)} FROM ota.channel c, inv.room_type t WHERE c.code = '{ch}' AND t.code = '{type}';");
        Map("BOOKNEST", "Classic", "BN-CLS");
        Map("BOOKNEST", "Deluxe", "BN-DLX");
        Map("BOOKNEST", "Suite", "BN-STE");
        Map("STAYHUB", "Classic", "SH-STD");
        Map("STAYHUB", "Deluxe", "SH-DLX", stop: true);
        Map("TRIPKART", "Classic", "TK-C1");
        Map("TRIPKART", "Deluxe", "TK-D1");
        Map("TRIPKART", "Suite", "TK-S1");

        void Msg(string key, string ch, string kind, string cref, string guest, string code, string a, string d, int adults, int children, long amount, string status, string? recv, string? reason = null) =>
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT ota.message(property_id, channel_id, external_id, kind, channel_ref, guest_name, phone, email, channel_room_code, arrival, departure, adults, children, amount_paise, received_utc, status, reason) SELECT 1, channel_id, '{key}', '{kind}', '{cref}', N'{guest}', '+91 90000 {new string(cref.Where(char.IsDigit).ToArray())[^5..]}', '{guest.Split(' ')[0].ToLowerInvariant()}@example.test', '{code}', '{a}', '{d}', {adults}, {children}, {amount}, {(recv is null ? "NULL" : "'" + recv + "'")}, '{status}', {(reason is null ? "NULL" : "N'" + reason + "'")} FROM ota.channel WHERE code = '{ch}';");
        Msg("F0", "BOOKNEST", "New", "BN-88190", "Harsh Vora", "BN-DLX", "2026-10-23", "2026-10-24", 2, 0, 354000, "Rejected", "2026-10-23 15:40:00", "Arrival date already passed when received");
        Msg("F1", "BOOKNEST", "New", "BN-88231", "Kiran Joshi", "BN-DLX", "2026-10-27", "2026-10-29", 2, 0, 708000, "Pending", "2026-10-24 01:22:00");
        Msg("F2", "STAYHUB", "New", "SH-55102", "Pooja Kulkarni", "SH-STD", "2026-10-25", "2026-10-26", 1, 0, 230000, "Pending", "2026-10-24 01:35:00");
        Msg("F3", "BOOKNEST", "New", "BN-88240", "Farah Siddiqui", "BN-STE", "2026-10-25", "2026-10-27", 2, 1, 1062000, "Pending", "2026-10-24 01:48:00");
        Msg("F4", "TRIPKART", "New", "TK-30017", "Rohan Pillai", "TK-S1", "2026-10-25", "2026-10-26", 2, 0, 504000, "Pending", "2026-10-24 00:42:00");
        Msg("F5", "BOOKNEST", "New", "BN-88262", "Sameer Qureshi", "BN-CLS", "2026-10-26", "2026-10-28", 3, 0, 472000, "Pending", "2026-10-24 02:01:00");
        Msg("F6", "STAYHUB", "Modify", "SH-55102", "Pooja Kulkarni", "SH-STD", "2026-10-25", "2026-10-27", 1, 0, 460000, "Pending", "2026-10-24 02:10:00");
        Msg("O1", "BOOKNEST", "New", "BN-88275", "Lakshmi Menon", "BN-STE", "2026-10-25", "2026-10-26", 2, 0, 531000, "Queued", null);
        Msg("O2", "BOOKNEST", "Modify", "BN-88231", "Kiran Joshi", "BN-DLX", "2026-10-27", "2026-10-30", 2, 0, 1062000, "Queued", null);
        Msg("O3", "TRIPKART", "Cancel", "TK-30017", "Rohan Pillai", "TK-S1", "2026-10-25", "2026-10-26", 2, 0, 0, "Queued", null);
        await _db!.ExecAsync(sql.ToString());

        await using var conn = await _db.Platform.Connections.OpenAsync();
        await using (var q = new Microsoft.Data.SqlClient.SqlCommand("SELECT code, channel_id FROM ota.channel;", conn))
        await using (var rd = await q.ExecuteReaderAsync())
            while (await rd.ReadAsync()) ChannelIds[rd.GetString(0)] = rd.GetInt32(1);
        await using (var q = new Microsoft.Data.SqlClient.SqlCommand("SELECT external_id, message_id FROM ota.message;", conn))
        await using (var rd = await q.ExecuteReaderAsync())
            while (await rd.ReadAsync()) MessageIds[rd.GetString(0)] = rd.GetInt64(1);
    }

    private async Task<int> SeedUserAsync(string login, string role)
    {
        var id = Convert.ToInt32(await Db.ScalarAsync(
            $"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (1, '{login}', N'User {login}', '2026-01-01');"), CultureInfo.InvariantCulture);
        await Db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
        return id;
    }

    public CommandContext Ctx(int principal, string operation, string? key = null) => new(1, principal, operation, key ?? Guid.NewGuid().ToString("N"));

    public async Task<ChannelMessageView> MessageAsync(string fixtureKey, int? principal = null)
        => (await Queries.ListMessagesAsync(1, principal ?? Reception, null, 500, TestContext.Current.CancellationToken)).Single(m => m.MessageId == MessageIds[fixtureKey]);

    public async Task<Hcc.Contracts.Ota.AcceptResult> AcceptAsync(string fixtureKey, int? principal = null, int? roomId = null, string? key = null)
    {
        var who = principal ?? Reception;
        var m = await MessageAsync(fixtureKey, who);
        return (await Service.AcceptAsync(Ctx(who, "AcceptChannelMessage", key), new AcceptChannelMessageRequest(m.MessageId, m.Version, roomId), TestContext.Current.CancellationToken)).Value;
    }

    public async Task<Hcc.Contracts.Reservations.ReservationDetails> ReservationAsync(long id)
        => await Booking.GetAsync(1, Owner, id, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}
