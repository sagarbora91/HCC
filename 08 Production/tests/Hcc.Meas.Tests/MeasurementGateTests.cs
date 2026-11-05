// F12-SETTINGS · MeasurementGate (MEAS-CONTRACT §1.3–§1.5): module AND pack AND item, core always on, MEAS_OFF refusal, parity with the
// prototype key table (03 Prototype/src/domain/streams/measure.ts).
using System.Text.RegularExpressions;
using Hcc.Api.Endpoints;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Hcc.Meas.Tests;

internal sealed class FakeSettings : ISettingsReader
{
    public Dictionary<string, bool> Values { get; } = new(StringComparer.Ordinal);

    private ConfigValue Get(string key) => Values.TryGetValue(key, out var v) ? ConfigValue.Of(v) : ConfigValue.Of(true);

    public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(Get(key));

    public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(Get(key));
}

internal sealed class FakeModules : IModuleGate
{
    public HashSet<string> Off { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsOnAsync(int propertyId, string moduleId, CancellationToken ct = default) => Task.FromResult(!Off.Contains(moduleId));

    public Task EnsureOnAsync(int propertyId, string moduleId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default) => Task.FromResult(true);

    public Task EnsureFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class MeasurementGateTests
{
    private readonly FakeSettings _settings = new();
    private readonly FakeModules _modules = new();

    private MeasurementGate Gate() => new(_settings, _modules);

    private bool On(string key) => Gate().IsOnAsync(1, key, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    [Fact]
    public void Everything_is_on_by_default()
    {
        foreach (var i in MeasurementCatalogue.Items) Assert.True(On(i.Key), i.Key);
    }

    [Fact]
    public void Item_off_is_off_and_pack_off_turns_every_non_core_item_off()
    {
        _settings.Values["meas.daily.regrets"] = false;
        Assert.False(On("meas.daily.regrets"));
        Assert.True(On("meas.daily.cashier"));
        _settings.Values["meas.pack.daily"] = false;
        Assert.False(On("meas.daily.cashier"));
        Assert.True(On("meas.daily.flash"));      // core
        Assert.True(On("meas.daily.inventory"));  // core
        Assert.True(On("meas.daily.high-balance")); // core while company credit (groups) is on
        _modules.Off.Add("groups");
        Assert.False(On("meas.daily.high-balance"));
    }

    [Fact]
    public void Core_items_ignore_their_own_switch()
    {
        _settings.Values["meas.daily.flash"] = false;
        Assert.True(On("meas.daily.flash"));
    }

    [Fact]
    public void Module_off_turns_its_items_off()
    {
        _modules.Off.Add("payroll");
        foreach (var i in MeasurementCatalogue.Items.Where(i => i.Pack == "people")) Assert.False(On(i.Key), i.Key);
        _modules.Off.Add("maintenance");
        Assert.False(On("meas.hk.ppm"));
        Assert.False(On("meas.hk.utilities"));
        Assert.True(On("meas.hk.ooo"));
        _modules.Off.Add("inventory");
        Assert.False(On("meas.hk.linen"));
    }

    [Fact]
    public async Task EnsureOn_refuses_with_MEAS_OFF_details()
    {
        _settings.Values["meas.study.dashboard"] = false;
        var ex = await Assert.ThrowsAsync<HccException>(() => Gate().EnsureOnAsync(1, "meas.study.dashboard", TestContext.Current.CancellationToken));
        Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        Assert.StartsWith("MEAS_OFF", ex.Message, StringComparison.Ordinal);
        Assert.Equal("MEAS_OFF", ex.Details!["reason"].GetString());
        Assert.Equal("meas.study.dashboard", ex.Details!["key"].GetString());
        await Gate().EnsureOnAsync(1, "meas.study.management-pack", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unknown_key_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Gate().IsOnAsync(1, "meas.daily.nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Catalogue_counts_and_unique_keys()
    {
        Assert.Equal(56, MeasurementCatalogue.Items.Count);
        Assert.Equal(34, MeasurementCatalogue.Items.Count(i => i.Pack != "study"));
        Assert.Equal(22, MeasurementCatalogue.Items.Count(i => i.Pack == "study"));
        Assert.Equal(MeasurementCatalogue.Items.Count, MeasurementCatalogue.Items.Select(i => i.Key).Distinct().Count());
        Assert.Equal(54, MeasurementCatalogue.Items.Count(i => i.ReportId is not null));
        Assert.Equal(13, MeasurementCatalogue.Targets.Count);
        Assert.Equal(205000, MeasurementCatalogue.Targets.Single(t => t.Key == "meas.target.adr").Default);
        Assert.Equal(35, MeasurementCatalogue.Targets.Single(t => t.Key == "meas.target.occupancy").Default);
    }

    [Fact]
    public void Definitions_are_owner_only_and_valid_keys()
    {
        var defs = MeasurementCatalogue.Definitions();
        Assert.All(defs, d => Assert.True(d.Money == true && d.Approver == SettingApprover.Owner, d.Key));
        Assert.All(defs, d => Assert.Matches("^[a-z][a-zA-Z0-9._-]{1,79}$", d.Key));
        Assert.Equal(6 + 56 + 13 + 5, defs.Count);
    }

    [Fact]
    public void Route_helper_carries_metadata_and_rejects_unknown_keys()
    {
        var app = WebApplication.CreateBuilder().Build();
        var route = app.MapPost("/meas/test", () => Results.Ok()).RequireHccMeasurement("meas.guest.survey");
        Assert.NotNull(route);
        Assert.Throws<ArgumentException>(() => app.MapPost("/meas/bad", () => Results.Ok()).RequireHccMeasurement("meas.guest.nope"));
    }

    private static string PrototypeMeasureTs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "03 Prototype", "src", "domain", "streams", "measure.ts"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "03 Prototype", "src", "domain", "streams", "measure.ts"));
    }

    [Fact]
    public void Parity_with_the_prototype_key_table()
    {
        var ts = PrototypeMeasureTs();
        // I('<pack>','<key>','<label>','<report>'|undefined,{core / module})
        var rows = Regex.Matches(ts, @"^\s*I\('(?<pack>[a-z]+)','(?<key>meas\.[a-z]+\.[a-z-]+)',(?:'(?:[^'\\]|\\.)*'|""[^""]*""),(?:'(?<rpt>rpt\.[a-z]+\.[a-z0-9-]+)'|undefined)?(?<rest>.*)$", RegexOptions.Multiline)
            .Select(m => (Pack: m.Groups["pack"].Value, Key: m.Groups["key"].Value, Report: m.Groups["rpt"].Success ? m.Groups["rpt"].Value : null,
                Core: m.Groups["rest"].Value.Contains("core:true", StringComparison.Ordinal),
                Module: Regex.Match(m.Groups["rest"].Value, "module:'([a-z]+)'") is { Success: true } mm ? mm.Groups[1].Value : null)).ToList();
        Assert.Equal(MeasurementCatalogue.Items.Count, rows.Count);
        for (var n = 0; n < rows.Count; n++)
        {
            var c = MeasurementCatalogue.Items[n];
            Assert.Equal((c.Pack, c.Key, c.ReportId, c.Core, c.Module), rows[n]);
        }

        var targets = Regex.Matches(ts, @"\{key:'(meas\.target\.[A-Za-z]+)',label:'[^']*',kind:'([a-z]+)',default:(\d+)").Select(m => (m.Groups[1].Value, m.Groups[2].Value, long.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(MeasurementCatalogue.Targets.Select(t => (t.Key, t.Kind, t.Default)).ToList(), targets);
    }
}
