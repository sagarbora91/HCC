using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Yield;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Yield;
using Hcc.Inventory.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Yield.Tests.Harness;

/// <summary>
/// The prototype BASE scenario in SQL (property 1, clock 08:00 IST on 24 Oct 2026) plus the module-13 fixture rules and the REAL access policy
/// (matrix v1 + DEC-063 grants) with one user per role template. P2's services run over the same database to prove published rates reach quotes.
/// </summary>
public sealed class YieldFixture : IAsyncLifetime
{
    public const string Expo = "Latur MIDC trade expo (fictional)";

    private InventoryDatabase? _db;

    public GoldenInventory Golden { get; } = GoldenInventory.Load();

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public TestClock Clock { get; private set; } = null!;

    public P2Services P2 { get; private set; } = null!;

    public SqlYieldStore Store { get; private set; } = null!;

    public SqlAccessPolicy Policy { get; private set; } = null!;

    public YieldService Service { get; private set; } = null!;

    public YieldQueries Queries { get; private set; } = null!;

    public Dictionary<string, int> TypeIds { get; } = [];

    public Dictionary<string, int> RoomIds { get; } = [];

    public int Owner, Manager, Reception, Accountant, TechAdmin, Cashier;

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        await Golden.SeedAsync(_db);
        Clock = new TestClock(new DateTimeOffset(GoldenInventory.LocalToUtc(Golden.Raw.Clock), TimeSpan.Zero));
        P2 = new P2Services(_db, Clock, Golden.Raw.TaxBps);
        var conns = _db.Platform.Connections;
        Policy = new SqlAccessPolicy(conns, Clock, []);
        Store = new SqlYieldStore(conns);
        Service = new YieldService(Store, new SqlRatePublicationPort(conns), _db.Platform.Commands, Policy, Clock);
        Queries = new YieldQueries(Store, Policy, Clock);

        Owner = await SeedUserAsync("owner", "OWN");
        Manager = await SeedUserAsync("manager", "MGR");
        Reception = await SeedUserAsync("reception", "REC");
        Accountant = await SeedUserAsync("accountant", "ACC");
        TechAdmin = await SeedUserAsync("techadmin", "TAD");
        Cashier = await SeedUserAsync("cashier", "CSH");

        await using (var conn = await conns.OpenAsync())
        {
            await using (var q = new SqlCommand("SELECT code, room_type_id FROM inv.room_type;", conn))
            await using (var rd = await q.ExecuteReaderAsync())
                while (await rd.ReadAsync()) TypeIds[rd.GetString(0)] = rd.GetInt32(1);
            await using (var q = new SqlCommand("SELECT room_no, room_id FROM inv.room;", conn))
            await using (var rd = await q.ExecuteReaderAsync())
                while (await rd.ReadAsync()) RoomIds[rd.GetString(0)] = rd.GetInt32(1);
        }

        await Store.GetConfigViewAsync(1, 0); // seeds the settings row and the five default plans
        var forecast = new[] { 14, 16, 9, 10, 12, 14, 18, 20, 13, 8, 9, 12, 16, 21, 22, 22, 20, 15, 10, 9, 10, 11, 12, 15, 17, 12, 10, 10, 11, 13 };
        var sql = new System.Text.StringBuilder();
        sql.AppendLine("UPDATE yld.settings SET dow5_bps = 500, dow6_bps = 1000 WHERE property_id = 1;");
        sql.AppendLine($"INSERT yld.season(property_id, name, from_date, to_date, adj_bps, priority) VALUES (1, N'Diwali festive', '2026-11-06', '2026-11-10', 2000, 2), (1, N'{Expo}', '2026-10-28', '2026-10-30', 800, 1), (1, N'Wedding season', '2026-11-20', '2026-12-14', 1000, 1);");
        sql.AppendLine($"INSERT yld.season_type(season_id, room_type_id) SELECT s.season_id, t.room_type_id FROM yld.season s CROSS JOIN inv.room_type t WHERE s.name IN (N'{Expo}', N'Wedding season') AND t.code IN ('Deluxe', 'Suite');");
        sql.AppendLine("INSERT yld.occ_rule(property_id, op, occ_pct, adj_bps) VALUES (1, '>=', 80, 1000), (1, '>=', 90, 1500), (1, '<', 40, -1000);");
        sql.AppendLine("INSERT yld.restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note) VALUES (1, 'YLDR-1', NULL, '2026-11-07', '2026-11-09', 2, 0, N'Diwali weekend');");
        sql.AppendLine("INSERT yld.restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note) SELECT 1, 'YLDR-2', room_type_id, '2026-11-08', '2026-11-08', 1, 1, N'Lakshmi Puja day' FROM inv.room_type WHERE code = 'Suite';");
        sql.AppendLine("ALTER SEQUENCE yld.seq_restriction RESTART WITH 3;");
        for (var i = 0; i < forecast.Length; i++)
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT yld.forecast(property_id, night, rooms_sold) VALUES (1, DATEADD(DAY, {i}, '2026-10-24'), {forecast[i]});");
        await _db.ExecAsync(sql.ToString());
    }

    private async Task<int> SeedUserAsync(string login, string role)
    {
        var id = Convert.ToInt32(await Db.ScalarAsync(
            $"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (1, '{login}', N'User {login}', '2026-01-01');"), CultureInfo.InvariantCulture);
        await Db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
        return id;
    }

    public CommandContext Ctx(int principal, string operation, string? key = null) => new(1, principal, operation, key ?? Guid.NewGuid().ToString("N"));

    public Task<string> ConfigVersionAsync(Func<Hcc.Contracts.Yield.YieldConfigResult, string> pick) => ConfigAsync(pick);

    private async Task<string> ConfigAsync(Func<Hcc.Contracts.Yield.YieldConfigResult, string> pick) => pick(await Store.GetConfigViewAsync(1, 20));

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}
