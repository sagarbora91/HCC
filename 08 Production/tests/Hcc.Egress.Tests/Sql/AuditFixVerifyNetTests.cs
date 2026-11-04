using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Egress;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace Hcc.Egress.Tests.Sql;

/// <summary>AF-VERIFY (wave 4): hostile attempts against SetNetSetting (item 23). Prefix V-NET-nn = attempt id in af-verify-DONE.md.</summary>
[Trait("Category", "Sql")]
public sealed class AuditFixVerifyNetTests(EgressDatabase db) : IClassFixture<EgressDatabase>
{
    private sealed record Ctx(NetSettingCommands Commands, ConfigurationService Config, SettingsReader Reader, int Property, int Owner, int Manager, int Reception, int Accountant, int Housekeeper);

    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero));
    private static int _n = 500;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<int> ScalarIntAsync(string sql, params (string, object)[] args)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(CancellationToken.None);
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    private async Task<Ctx> NewAsync()
    {
        var n = Interlocked.Increment(ref _n).ToString("000", CultureInfo.InvariantCulture);
        var p = await ScalarIntAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES (@c, @n, 'Asia/Kolkata', '2026-10-24');", ("@c", "V" + n), ("@n", "HCC Verify Residency " + n));
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
        return new Ctx(new NetSettingCommands(config, policy), config, new SettingsReader(registry, store, store), p, await User("owner", RoleCodes.Owner), await User("manager", RoleCodes.Manager),
            await User("rec", RoleCodes.Reception), await User("acc", RoleCodes.Accountant), await User("hk", RoleCodes.Housekeeper));
    }

    private static CommandContext C(Ctx x, int who, string? key = null) => new(x.Property, who, "op", key ?? Guid.NewGuid().ToString("N"));

    private static SetNetSettingRequest R(string setting, object? value, string? reason, string? version = null) => new(setting, JsonSerializer.SerializeToElement(value), reason, version);

    private static async Task<string> CodeAsync(Func<Task> act)
    {
        try { await act(); return "OK"; }
        catch (HccException ex) { return ex.Code.ToString(); }
    }

    private const string Firewall = "SELECT CONCAT(COALESCE(wanted_state, ''), '|', COALESCE(CONVERT(varchar(33), wanted_utc, 126), '')) FROM ops.egress_firewall_state;";

    private async Task<string?> VersionOf(Ctx x, string key)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new SqlCommand("SELECT TOP 1 row_ver FROM cfg.setting WHERE property_id = @p AND setting_key = @k;", conn);
        cmd.Parameters.AddWithValue("@p", x.Property);
        cmd.Parameters.AddWithValue("@k", key);
        var v = await cmd.ExecuteScalarAsync(Ct);
        return v is byte[] b ? Convert.ToBase64String(b) : null;
    }

    // V-NET-01 only the Owner
    [Fact]
    public async Task V_NET_01_every_role_but_the_owner_is_FORBIDDEN_for_every_key_and_nothing_changes()
    {
        var x = await NewAsync();
        var fw = await db.ScalarAsync(Firewall);
        foreach (var who in new[] { x.Manager, x.Reception, x.Accountant, x.Housekeeper })
            foreach (var key in NetSettingCommands.Keys.Where(k => k != "net.window"))
                Assert.Equal("FORBIDDEN", await CodeAsync(() => x.Commands.SetAsync(C(x, who), R(key, true, "Please enable"), Ct)));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND setting_key LIKE 'net.%';", ("@p", x.Property)));
        Assert.Equal(fw, await db.ScalarAsync(Firewall));
    }

    // V-NET-02 keys: unknown, odd spellings, generic keys that are not net.*
    [Theory]
    [InlineData("net.nothing")]
    [InlineData("NET.OTA")]
    [InlineData("net.ota.")]
    [InlineData("net.")]
    [InlineData("")]
    [InlineData("hotel.name")]
    [InlineData("net.ota;DROP TABLE cfg.setting_change")]
    public async Task V_NET_02_unknown_or_oddly_spelt_keys_are_VALIDATION(string key)
    {
        var x = await NewAsync();
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R(key, true, "Because it is needed"), Ct)));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p;", ("@p", x.Property)));
    }

    // V-NET-03 value shapes of the switches
    [Fact]
    public async Task V_NET_03_non_boolean_values_are_VALIDATION_for_every_switch()
    {
        var x = await NewAsync();
        object?[] bad = ["true", "yes", 1, 0, null, new object[] { true }, new { a = 1 }, ""];
        foreach (var key in NetSettingCommands.Keys.Where(k => k != "net.window"))
            foreach (var v in bad)
                Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R(key, v, "Because it is needed"), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), new SetNetSettingRequest("net.ota", null, "Because it is needed"), Ct))); // Value omitted entirely
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p;", ("@p", x.Property)));
    }

    // V-NET-04 the window: garbage must never be stored
    [Theory]
    [InlineData("garbage")]
    [InlineData("{\"from\":\"25:00\",\"to\":\"26:99\",\"days\":[1]}")]
    [InlineData("{\"from\":\"08:00\",\"to\":\"20:00\",\"days\":[9]}")]
    [InlineData("{\"from\":\"08:00\"}")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("{\"from\":\"08:00\",\"to\":\"20:00\",\"days\":\"all\"}")]
    public async Task V_NET_04_garbage_window_is_VALIDATION_and_not_stored(string window)
    {
        var x = await NewAsync();
        var asText = await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.window", window, "Office hours only"), Ct));
        Assert.True(asText == "VALIDATION", $"window text '{window}' -> {asText}");
        if (window.StartsWith('{') || window.StartsWith('['))
        {
            var asObject = await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), new SetNetSettingRequest("net.window", JsonDocument.Parse(window).RootElement.Clone(), "Office hours only"), Ct));
            Assert.True(asObject == "VALIDATION", $"window object {window} -> {asObject}");
        }

        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND setting_key = 'net.window';", ("@p", x.Property)));
    }

    [Fact]
    public async Task V_NET_04b_numbers_booleans_and_oversized_window_values_are_VALIDATION_and_clearing_works()
    {
        var x = await NewAsync();
        foreach (var v in new object?[] { 5, true, new string('x', 200_000) })
            Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.window", v, "Office hours only"), Ct)));
        Assert.Equal("OK", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.window", new { from = "08:00", to = "20:00", days = new[] { 1, 2 } }, "Office hours only"), Ct)));
        var ver = await VersionOf(x, "net.window");
        string clear;
        try { await x.Commands.SetAsync(C(x, x.Owner), R("net.window", "", "Clear the window", ver), Ct); clear = "OK"; }
        catch (HccException e) { clear = e.Code + ": " + e.Message; }
        Assert.Equal("OK", clear);
    }

    // V-NET-05 reasons
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    public async Task V_NET_05_blank_or_short_reasons_are_VALIDATION(string? reason)
    {
        var x = await NewAsync();
        Assert.Equal("VALIDATION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", true, reason), Ct)));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p;", ("@p", x.Property)));
    }

    // V-NET-06 wanted_state never written; one change, one audit, one outbox; replay and mismatch
    [Fact]
    public async Task V_NET_06_wanted_state_untouched_one_change_one_audit_replay_and_mismatch()
    {
        var x = await NewAsync();
        var fw = await db.ScalarAsync(Firewall);
        var key = Guid.NewGuid().ToString("N");
        var first = await x.Commands.SetAsync(C(x, x.Owner, key), R("net.internetEnabled", true, "Switch the internet on"), Ct);
        Assert.False(first.Replayed);
        var again = await x.Commands.SetAsync(C(x, x.Owner, key), R("net.internetEnabled", true, "Switch the internet on"), Ct);
        Assert.True(again.Replayed);
        Assert.Equal("IDEMPOTENCY_MISMATCH", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner, key), R("net.internetEnabled", false, "Switch the internet on"), Ct)));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p AND setting_key = 'net.internetEnabled';", ("@p", x.Property)));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'SETTING_SET' AND detail LIKE 'Setting net.internetEnabled%';", ("@p", x.Property)));
        Assert.Equal(fw, await db.ScalarAsync(Firewall));
        Assert.Equal(ConfigValue.Of(true), await x.Reader.GetAsync(x.Property, "net.internetEnabled", null, Ct));
    }

    // V-NET-07 stale version
    [Fact]
    public async Task V_NET_07_stale_expected_version_is_refused_and_the_value_is_unchanged()
    {
        var x = await NewAsync();
        await x.Commands.SetAsync(C(x, x.Owner), R("net.ota", true, "Enable OTA sync"), Ct);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", false, "Disable OTA sync", null), Ct)));
        Assert.Equal(ConfigValue.Of(true), await x.Reader.GetAsync(x.Property, "net.ota", null, Ct));
        var ver = await VersionOf(x, "net.ota");
        Assert.Equal("OK", await CodeAsync(() => x.Commands.SetAsync(C(x, x.Owner), R("net.ota", false, "Disable OTA sync", ver), Ct)));
    }

    // V-NET-08 the generic SetSetting path by a manager must not apply a net switch
    [Fact]
    public async Task V_NET_08_generic_SetSetting_by_a_manager_does_not_apply_a_net_switch()
    {
        var x = await NewAsync();
        string outcome;
        try
        {
            var r = await x.Config.SetSettingAsync(C(x, x.Manager), new SetSettingRequest("net.messaging", JsonSerializer.SerializeToElement(true), null, "Turn it on please", null), Ct);
            outcome = "RESULT:" + r.Value.Status;
        }
        catch (HccException e) { outcome = e.Code.ToString(); }

        Assert.DoesNotContain("applied", outcome, StringComparison.Ordinal);
        Assert.NotEqual(ConfigValue.Of(true), await x.Reader.GetAsync(x.Property, "net.messaging", null, Ct));
    }
}
