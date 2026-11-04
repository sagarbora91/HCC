using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Egress;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.Egress;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace Hcc.Egress.Tests.Sql;

/// <summary>
/// AF-NETSET (audit item 23): <c>SetNetSetting</c> on SQL with the real P1 policy. It writes through Master configuration (same table, change
/// history with reason, audit SETTING_SET, outbox SettingChanged) and never writes <c>ops.egress_firewall_state</c>.
/// </summary>
[Trait("Category", "Sql")]
public sealed class NetSettingTests(EgressDatabase db) : IClassFixture<EgressDatabase>
{
    private sealed record Ctx(NetSettingCommands Commands, SettingsReader Reader, int Property, int Owner, int Manager);

    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero));
    private static int _n;

    private async Task<int> ScalarIntAsync(string sql, params (string, object)[] args)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(CancellationToken.None);
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<Ctx> NewAsync()
    {
        var n = Interlocked.Increment(ref _n).ToString("000", CultureInfo.InvariantCulture);
        var p = await ScalarIntAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES (@c, @n, 'Asia/Kolkata', '2026-10-24');", ("@c", "N" + n), ("@n", "HCC Demo Residency " + n));
        async Task<int> User(string login, string role)
        {
            var id = await ScalarIntAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @l, '2026-01-01');", ("@p", p), ("@l", login + n));
            await ScalarIntAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) OUTPUT INSERTED.user_id VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", id), ("@r", role));
            return id;
        }

        var policy = new AccessModule(db.Platform, new AccessOptions { PasswordIterations = 1000, PinIterations = 1000 }, Clock).Policy;
        var registry = new SettingsRegistry([Hcc.Api.Endpoints.EgressEndpoints.Provider]);
        var store = new SqlConfigurationStore(db.Platform.Connections);
        var config = new ConfigurationService(db.Platform.Commands, policy, registry, store, Clock);
        return new Ctx(new NetSettingCommands(config, policy), new SettingsReader(registry, store, store), p, await User("owner", RoleCodes.Owner), await User("manager", RoleCodes.Manager));
    }

    private static CommandContext C(Ctx x, int who, string? key = null) => new(x.Property, who, "op", key ?? Guid.NewGuid().ToString("N"));

    private static SetNetSettingRequest R(string setting, object? value, string? reason) =>
        new(setting, JsonSerializer.SerializeToElement(value), reason);

    private static async Task<string> CodeAsync(Func<Task> act)
    {
        try
        {
            await act();
            return "OK";
        }
        catch (HccException ex)
        {
            return ex.Code.ToString();
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_sets_net_state_on_with_reason_audit_and_change_row()
    {
        var x = await NewAsync();
        const string firewall = "SELECT CONCAT(wanted_state, '|', CONVERT(varchar(33), wanted_utc, 126)) FROM ops.egress_firewall_state;";
        var firewallBefore = await db.ScalarAsync(firewall);
        var r = await x.Commands.SetAsync(C(x, x.Owner), R("net.messaging", true, "Test the gateway"), Ct);
        Assert.Equal("applied", r.Value.Status);
        Assert.Equal(ConfigValue.Of(true), await x.Reader.GetAsync(x.Property, "net.messaging", null, Ct));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND setting_key = 'net.messaging' AND reason = 'Test the gateway';", ("@p", x.Property)));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'SETTING_SET' AND detail LIKE 'Setting net.messaging%';", ("@p", x.Property)));
        // The wanted firewall state is the outbox worker's (EGRESS-CONTRACT §4); this command never writes it.
        Assert.Equal(firewallBefore, await db.ScalarAsync(firewall));
        // The window travels as its JSON text.
        var w = await x.Commands.SetAsync(C(x, x.Owner), R("net.window", new { from = "08:00", to = "20:00", days = new[] { 1, 2, 3 } }, "Office hours only"), Ct);
        Assert.Equal("applied", w.Value.Status);
    }

    [Fact]
    public async Task Manager_is_FORBIDDEN()
    {
        var x = await NewAsync();
        Assert.Equal("FORBIDDEN", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Manager), R("net.internetEnabled", true, "Try it"), Ct)));
        Assert.Equal(ConfigValue.Of(false), await x.Reader.GetAsync(x.Property, "net.internetEnabled", null, Ct));
    }

    [Fact]
    public async Task Unknown_key_is_VALIDATION()
    {
        var x = await NewAsync();
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.nothing", true, "Because"), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("hotel", true, "Because"), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", "yes", "Because"), Ct)));
    }

    [Fact]
    public async Task Missing_reason_is_VALIDATION()
    {
        var x = await NewAsync();
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", true, null), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", true, "ab"), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", true, new string('x', 121)), Ct)));
        Assert.Equal(ConfigValue.Of(false), await x.Reader.GetAsync(x.Property, "net.ota", null, Ct));
    }

    [Fact]
    public async Task Same_key_replay_is_idempotent()
    {
        var x = await NewAsync();
        var key = Guid.NewGuid().ToString("N");
        var req = R("net.email", true, "Send the daily report");
        var first = await x.Commands.SetAsync(C(x, x.Owner, key), req, Ct);
        var again = await x.Commands.SetAsync(C(x, x.Owner, key), req, Ct);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND setting_key = 'net.email';", ("@p", x.Property)));
    }

    [Fact]
    public void Key_list_is_the_contract_list()
    {
        var expected = EgressKeys.All.Select(EgressKeys.Of).Append(EgressKeys.Master).Append(EgressKeys.Window).Order(StringComparer.Ordinal);
        Assert.Equal(expected, NetSettingCommands.Keys.Order(StringComparer.Ordinal));
    }
}
