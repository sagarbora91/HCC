using System.Text.Json;
using Hcc.Configuration.Tests.Harness;

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
}
