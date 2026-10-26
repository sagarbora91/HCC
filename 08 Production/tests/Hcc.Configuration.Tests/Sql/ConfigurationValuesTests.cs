using System.Text.Json;
using Hcc.Configuration.Tests.Harness;
using Hcc.Contracts.Configuration;
using static Hcc.Configuration.Tests.Harness.ConfigDatabase;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>[CFG W4-5] GET /configuration/values: every signed-in user reads the values in force (and dated ones ahead); never secrets or pointer lines.</summary>
[Trait("Category", "Sql")]
public class ConfigurationValuesTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>
{
    [Fact]
    public async Task Any_user_reads_values_in_force_and_scheduled_ones_without_secrets_or_fixed_lines()
    {
        var h = await db.NewHotelAsync();
        await db.SetAsync(h, h.Owner, "hotel", "Sand Residency", null, null);
        await db.SetAsync(h, h.Owner, "checkin", "13:00", null, null);
        await db.SetAsync(h, h.Owner, "msg.gatewayKey", "secret-key-123", null, null);
        await db.SetAsync(h, h.Owner, "module.pos", false, null, null);
        await db.SetAsync(h, h.Owner, "discountLimit", 60000, "2026-10-30", null);

        foreach (var who in new[] { h.Reception, h.Housekeeper, h.Owner, h.TechAdmin })
        {
            var r = await db.Queries.ValuesAsync(h.PropertyId, who);
            Assert.Equal("2026-10-24", r.BusinessDate);
            Assert.Equal("Sand Residency", r.Values["hotel"].GetString());
            Assert.Equal("13:00", r.Values["checkin"].GetString());
            Assert.Equal(JsonValueKind.False, r.Values["module.pos"].ValueKind);
            Assert.Equal(25000, r.Values["discountLimit"].GetInt64());   // the dated change is not in force yet
            Assert.Contains(r.Scheduled, x => x.Key == "discountLimit" && x.EffectiveFrom == "2026-10-30" && x.Value.GetInt64() == 60000);
            Assert.False(r.Values.ContainsKey("msg.gatewayKey"));          // secret: never returned
            Assert.False(r.Values.ContainsKey("currency"));                // fixed pointer line
            Assert.DoesNotContain("secret-key-123", JsonSerializer.Serialize(r), StringComparison.Ordinal);
        }
    }

    /// <summary>W4 D2: the web client saves with the version from GET /configuration/values. Editing the same setting twice (and again after a
    /// fresh read) succeeds; a version older than the row is still STALE_VERSION; a key never saved has no version and needs none.</summary>
    [Fact]
    public async Task Values_carry_row_versions_so_the_same_setting_can_be_edited_twice()
    {
        var h = await db.NewHotelAsync();
        var r0 = await db.Queries.ValuesAsync(h.PropertyId, h.Owner);
        Assert.False((r0.Versions ?? new Dictionary<string, string>()).ContainsKey("brand.gstin"));
        var first = await db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.gstin", J("27AAAAA0000A1Z2"), ExpectedVersion: r0.Versions?.GetValueOrDefault("brand.gstin")));
        Assert.Equal("applied", first.Value.Status);

        var r1 = await db.Queries.ValuesAsync(h.PropertyId, h.Owner);
        Assert.Equal(first.Value.Version, r1.Versions!["brand.gstin"]);
        var second = await db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.gstin", J("27AAPFU0939F1ZV"), ExpectedVersion: r1.Versions["brand.gstin"]));
        Assert.Equal("applied", second.Value.Status);
        var r2 = await db.Queries.ValuesAsync(h.PropertyId, h.Reception);   // any user reads versions (no values leak through them)
        Assert.Equal("27AAPFU0939F1ZV", r2.Values["brand.gstin"].GetString());
        Assert.NotEqual(r1.Versions["brand.gstin"], r2.Versions!["brand.gstin"]);
        Assert.Equal("STALE_VERSION", await Expect.Code(() => db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.gstin", J(""), ExpectedVersion: r1.Versions["brand.gstin"]))));
        var third = await db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.gstin", J(""), ExpectedVersion: r2.Versions["brand.gstin"]));
        Assert.Equal("applied", third.Value.Status);
    }
}
