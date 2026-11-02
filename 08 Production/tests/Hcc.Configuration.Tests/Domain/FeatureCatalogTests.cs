using System.Text.Json;
using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;

namespace Hcc.Configuration.Tests.Domain;

/// <summary>
/// ADM-1 [ADM1] feature catalogue (DEC-214 / DEC-215): the C# catalogue equals the golden <c>08 Production/database/feature-catalogue.json</c>
/// generated from the prototype (<c>node tools/feature-catalogue.mjs --write</c> in <c>03 Prototype</c>), core rows have no switch, every
/// switchable feature has one <c>feature.&lt;id&gt;</c> boolean in the Modules on/off group, requires resolve, closure and impact lines.
/// No database.
/// </summary>
public class FeatureCatalogTests
{
    private static readonly SettingsRegistry Registry = new([]);

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Not found above the test output folder: " + Path.Combine(parts));
    }

    [Fact]
    public async Task Catalogue_equals_the_golden_generated_from_the_prototype()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(RepoFile("08 Production", "database", "feature-catalogue.json"), TestContext.Current.CancellationToken));
        Assert.Equal("hcc-feature-catalogue", json.RootElement.GetProperty("format").GetString());
        var modules = json.RootElement.GetProperty("modules").EnumerateArray().ToList();
        Assert.Equal(modules.Count, FeatureCatalog.Modules.Count);
        for (var i = 0; i < modules.Count; i++)
        {
            var gm = modules[i];
            var m = FeatureCatalog.Modules[i];
            Assert.Equal(gm.GetProperty("id").GetString(), m.Id);
            Assert.Equal(gm.GetProperty("label").GetString(), m.Label);
            Assert.Equal(gm.TryGetProperty("switch", out var sw) ? sw.GetString() : null, m.Switch);
            Assert.Equal(Strings(gm, "coarse"), m.Coarse);
            var gf = gm.GetProperty("features").EnumerateArray().ToList();
            Assert.Equal(gf.Count, m.Features.Count);
            for (var j = 0; j < gf.Count; j++)
            {
                var g = gf[j];
                var f = m.Features[j];
                Assert.Equal(g.GetProperty("id").GetString(), f.Id);
                Assert.Equal(g.GetProperty("label").GetString(), f.Label);
                Assert.Equal(g.GetProperty("core").GetBoolean(), f.Core);
                Assert.Equal(g.GetProperty("note").GetString(), f.Note);
                Assert.Equal(Strings(g, "pages"), f.Pages);
                Assert.Equal(Strings(g, "prefixes"), f.Prefixes);
                Assert.Equal(Strings(g, "except"), f.Except);
                Assert.Equal(Strings(g, "requires"), f.Requires);
                Assert.Equal(Strings(g, "permPrefixes"), f.PermPrefixes);
            }
        }

        static List<string> Strings(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    [Fact]
    public void Ids_unique_requires_resolve_module_switches_exist_and_the_always_on_list_is_pinned()
    {
        var ids = FeatureCatalog.All.Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(18, FeatureCatalog.Modules.Count);
        foreach (var f in FeatureCatalog.All)
        {
            Assert.StartsWith(FeatureCatalog.ModuleOf(f.Id)!.Id + ".", f.Id, StringComparison.Ordinal);
            foreach (var q in f.Requires)
            {
                Assert.NotNull(FeatureCatalog.Feature(q));
                Assert.NotEqual(f.Id, q);
            }
        }
        foreach (var m in FeatureCatalog.Modules.Where(m => m.Switch is not null)) Assert.NotNull(ConfigCatalog.Module(m.Switch!));
        foreach (var id in new[] { "frontdesk.checkin", "cash.folios", "cash.cashier", "cash.invoices", "closing.dayclose", "admin.access", "admin.config", "admin.backup", "admin.health", "people.myhr" })
            Assert.True(FeatureCatalog.Feature(id)!.Core, id + " must be core (DEC-215)");
        Assert.False(FeatureCatalog.Feature(FeatureIds.Reservations)!.Core);
    }

    [Fact]
    public void Every_switchable_feature_has_one_boolean_switch_in_the_modules_group_and_core_features_have_none()
    {
        var all = Registry.All.ToList();
        foreach (var f in FeatureCatalog.Switchable)
        {
            var s = Assert.Single(all, x => x.Key == FeatureIds.SettingKey(f.Id));
            Assert.Equal((ConfigGroupIds.Modules, SettingType.Boolean, true, false), (s.Group.Id, s.Type, s.Editable, s.Money));
            Assert.Equal(ConfigValue.Of(true), s.Default);
            Assert.Equal($"{FeatureCatalog.ModuleOf(f.Id)!.Label} › {f.Label}", s.Label);
        }
        foreach (var f in FeatureCatalog.All.Where(f => f.Core))
            Assert.DoesNotContain(all, x => x.Key == FeatureIds.SettingKey(f.Id));
        // The ten DEC-057 module switches are untouched.
        foreach (var m in ConfigCatalog.Modules) Assert.Contains(all, x => x.Key == ConfigModuleIds.SettingKey(m.Id));
    }

    [Fact]
    public void Closure_lists_the_feature_and_its_transitive_dependants_and_impact_names_them()
    {
        Assert.Equal(["cash.expenses", "stock.purchases"], FeatureCatalog.Closure(FeatureIds.Expenses));
        Assert.Equal(["revenue.ota", "revenue.payouts"], FeatureCatalog.Closure("revenue.ota"));
        Assert.Equal(["stock.purchases"], FeatureCatalog.Closure(FeatureIds.Purchases));
        var s = Registry.Get(FeatureIds.SettingKey(FeatureIds.Expenses));
        var day = new DateOnly(2026, 10, 24);
        var off = SettingRules.Impact(s, ConfigValue.Of(false), day, day, ConfigValue.Of(true));
        Assert.Contains(off, l => l.Contains("Expenses is hidden and its commands are refused", StringComparison.Ordinal));
        Assert.Contains(off, l => l == "Also switched off because they need it: Purchases & vendor payments.");
        var on = SettingRules.Impact(s, ConfigValue.Of(true), day, day, ConfigValue.Of(false));
        Assert.DoesNotContain(on, l => l.StartsWith("Also switched off", StringComparison.Ordinal));
    }

    [Fact]
    public void Feature_refusal_carries_the_prototype_sentence_MODULE_OFF_and_the_feature_id()
    {
        var e = ModuleGate.FeatureRefusal(FeatureIds.Reservations);
        Assert.Equal(Hcc.Domain.Errors.HccErrorCode.FORBIDDEN, e.Code);
        Assert.Equal("Reservations (Front desk) is switched off in Master configuration › Modules & features. Its data is kept; the Owner or Tech admin can switch it back on.", e.Message);
        Assert.Equal(ConfigCatalog.ModuleOffReason, e.Details!["reason"].GetString());
        Assert.Equal(FeatureIds.Reservations, e.Details["featureId"].GetString());
        Assert.Equal("frontdesk", e.Details["moduleId"].GetString());
        Assert.Equal(ConfigModuleIds.Pos, ModuleGate.FeatureRefusal(FeatureIds.PosMenu).Details!["moduleId"].GetString()); // a module with a DEC-057 switch names it
    }
}
