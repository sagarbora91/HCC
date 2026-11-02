using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Keys;
using Hcc.Contracts.Keys;
using Hcc.Domain.Keys;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Keys;
using Microsoft.Data.SqlClient;

namespace Hcc.Keys.Tests.Harness;

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// In-test encoder with the MockEncoder semantics of the keyagent prototype (online / card present / fail next write, serial counter, 106 and unknown rooms refused,
/// extend NotSupported like the real adapter until its BIS screen is mapped). Used by <see cref="FakeAgent"/>.
/// </summary>
public sealed class FakeEncoder
{
    public bool CardPresent { get; set; } = true;

    public bool FailNextWrite { get; set; }

    public bool SupportsExtend { get; set; }

    public long Serial { get; set; } = 846_716_912;

    public List<string> Written { get; } = [];

    public (string Code, string? Card) Run(AgentJob j)
    {
        switch (j.Kind)
        {
            case KeyCodesKind.Issue:
                if (!KeyRules.DefaultHasLock(j.Room)) return (KeyCodes.NoLockRoom, null);
                if (!CardPresent) return (KeyCodes.NoCard, null);
                if (FailNextWrite) { FailNextWrite = false; return (KeyCodes.WriteFailed, null); }
                var sn = (++Serial).ToString(CultureInfo.InvariantCulture);
                Written.Add(j.Room + ":" + sn);
                return (KeyCodes.Ok, sn);
            case KeyCodesKind.Extend:
                return SupportsExtend ? (KeyCodes.Ok, (++Serial).ToString(CultureInfo.InvariantCulture)) : (KeyCodes.NotSupported, null);
            case KeyCodesKind.Cancel:
                return Written.Any(w => w.StartsWith(j.Room + ":", StringComparison.Ordinal)) ? (KeyCodes.Ok, null) : (KeyCodes.NotFound, null);
            default:
                return CardPresent ? (KeyCodes.Ok, Serial.ToString(CultureInfo.InvariantCulture)) : (KeyCodes.NoCard, null);
        }
    }
}

public static class KeyCodesKind
{
    public const string Issue = "issue_guest";
    public const string Extend = "extend";
    public const string Cancel = "cancel";
}

/// <summary>The front-desk agent as a test double: heartbeat, poll one job, report picked / waiting_card, run the encoder, post the result (what the real poller does over HTTP).</summary>
public sealed class FakeAgent(KeyAgentService service, string encoderId, string token, FakeEncoder encoder)
{
    public string EncoderId => encoderId;

    public string Token => token;

    public FakeEncoder Encoder => encoder;

    public Task HeartbeatAsync(bool online = true, CancellationToken ct = default) => service.HeartbeatAsync(encoderId, token, new AgentHeartbeatRequest("mock", online, "fake agent", true), ct);

    /// <summary>Handles at most one job; returns it (or null when the queue was empty).</summary>
    public async Task<(AgentJob Job, KeyJobView Result)?> StepAsync(CancellationToken ct = default)
    {
        var job = await service.NextJobAsync(encoderId, token, ct);
        if (job is null) return null;
        await service.SetStateAsync(encoderId, token, job.JobId, new AgentStateRequest("waiting_card"), ct);
        var (code, card) = encoder.Run(job);
        var view = await service.ResultAsync(encoderId, token, job.JobId, new AgentResultRequest(code, code == KeyCodes.Ok ? "ok" : "mock " + code, card, card, "test"), ct);
        return (job, view);
    }
}

/// <summary>Disposable HccTest_* database with a small hotel (rooms 101, 106, 201, 301 and X99 without a BIS door), five users, in-house bookings, the real access policy and the real key services.</summary>
public sealed partial class KeysWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private KeysWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Store = new SqlKeyStore(Platform.Connections);
        Commands = new KeyCommands(Platform.Commands, Policy, Store, Clock);
        Queries = new KeyQueries(Policy, Store, Clock);
        Agent = new KeyAgentService(Store, Clock);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public SqlKeyStore Store { get; }

    public KeyCommands Commands { get; }

    public KeyQueries Queries { get; }

    public KeyAgentService Agent { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, int> Rooms { get; } = [];

    /// <summary>Booking label (BK-1 ...) to reservation id.</summary>
    public Dictionary<string, long> Bookings { get; } = [];

    private int _key;

    public CommandContext Ctx(string user, string? key = null) => new(PropertyId, Users[user], "K", key ?? "k-" + (++_key).ToString(CultureInfo.InvariantCulture));

    public static async Task<KeysWorld> CreateAsync(CancellationToken ct)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var w = new KeysWorld(server, name);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            await w.SeedAsync(ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql, CancellationToken ct = default) => ScalarAsync(sql, ct);

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-11-02');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("housekeeper", "HKP"), ("accountant", "ACC") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }

        var type = Convert.ToInt32(await ScalarAsync($"INSERT inv.room_type(property_id, code, max_occupancy) OUTPUT INSERTED.room_type_id VALUES ({PropertyId}, 'STD', 3);", ct), CultureInfo.InvariantCulture);
        foreach (var no in new[] { "101", "106", "201", "301", "X99" })
            Rooms[no] = Convert.ToInt32(await ScalarAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({PropertyId}, '{no}', {type});", ct), CultureInfo.InvariantCulture);
        var n = 0;
        foreach (var (label, room) in new[] { ("BK-1", "201"), ("BK-2", "106"), ("BK-3", "X99"), ("BK-4", "301") })
        {
            n++;
            Bookings[label] = Convert.ToInt64(await ScalarAsync($"""
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id)
                OUTPUT INSERTED.reservation_id VALUES ({PropertyId}, '{label}', N'Guest {n}', '2026-11-02', '2026-11-04', 'InHouse', {Rooms[room]});
                """, ct), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Registers an encoder through the command, sends a heartbeat and returns the fake agent that drives it.</summary>
    public async Task<FakeAgent> OnlineAgentAsync(string id = "FRONTDESK-1", CancellationToken ct = default)
    {
        var res = (await Commands.RegisterEncoderAsync(Ctx("manager"), new RegisterKeyEncoderRequest(id, "FRONT-PC"), ct)).Value;
        var agent = new FakeAgent(Agent, res.EncoderId, res.Token, new FakeEncoder());
        await agent.HeartbeatAsync(true, ct);
        return agent;
    }

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(_name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"""
            IF DB_ID(N'{_name}') IS NOT NULL
            BEGIN
              ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{_name}];
            END
            """, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
