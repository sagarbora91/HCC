using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Inventory;
using Hcc.Application.WebBooking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.WebBooking.Tests.Kernel;

/// <summary>
/// DEC-085: the booking website is OFF at go-live and is switched on only behind a reverse proxy with a CAPTCHA (security review F-H04).
/// No database: the code default, the shipped production settings sample and the install guide are checked as shipped (both files are copied
/// next to the test assembly by the project file).
/// </summary>
public sealed class BookingWebsiteOffAtGoLiveTests
{
    private static string Shipped(string name) => Path.Combine(AppContext.BaseDirectory, "Deployment", name);

    private static WebBookingOptions OptionsFrom(IConfiguration configuration)
    {
        using var provider = new ServiceCollection().AddWebBookingModule(configuration).BuildServiceProvider();
        return provider.GetRequiredService<WebBookingOptions>();
    }

    [Fact]
    public void The_booking_website_is_off_by_default_in_code_and_without_any_configuration()
    {
        Assert.False(new WebBookingOptions().Enabled);
        Assert.False(OptionsFrom(new ConfigurationBuilder().Build()).Enabled);
        Assert.False(OptionsFrom(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Hcc:WebBooking:QuotesPerMinute"] = "10" }).Build()).Enabled);
    }

    [Fact]
    public void The_production_settings_sample_keeps_the_booking_website_off_and_only_an_explicit_true_switches_it_on()
    {
        var sample = new ConfigurationBuilder().AddJsonFile(Shipped("appsettings.Production.sample.json"), optional: false).Build();
        Assert.Equal("False", sample["Hcc:WebBooking:Enabled"], ignoreCase: true);   // the key is present and says false (not silently absent)
        Assert.False(OptionsFrom(sample).Enabled);
        Assert.Equal(0, sample.GetValue<int>("Hcc:Hosting:PublicPort"));             // and no public listener either

        // DEC-080 in the same file: group blocks stay refused (Hcc:GroupBlocks:Enabled false) until pickup / release / cancel / re-allot exist.
        Assert.Equal("False", sample["Hcc:GroupBlocks:Enabled"], ignoreCase: true);
        using var inventory = new ServiceCollection().AddInventoryModule(sample).BuildServiceProvider();
        Assert.False(inventory.GetRequiredService<GroupBlockOptions>().Enabled);
        using var blocksOn = new ServiceCollection().AddInventoryModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Hcc:GroupBlocks:Enabled"] = "true" }).Build()).BuildServiceProvider();
        Assert.True(blocksOn.GetRequiredService<GroupBlockOptions>().Enabled);

        var on = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Hcc:WebBooking:Enabled"] = "true" }).Build();
        Assert.True(OptionsFrom(on).Enabled);
    }

    [Fact]
    public void The_install_guide_says_the_booking_website_stays_off_until_a_reverse_proxy_with_a_captcha_is_in_place()
    {
        var guide = File.ReadAllText(Shipped("INSTALL.md"));
        var start = guide.IndexOf("## 10. Booking website", StringComparison.Ordinal);
        Assert.True(start >= 0, "section 10 exists");
        var end = guide.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = guide[start..(end < 0 ? guide.Length : end)];
        Assert.Contains("Hcc:WebBooking:Enabled", section, StringComparison.Ordinal);
        Assert.Contains("stays off", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEC-085", section, StringComparison.Ordinal);
        Assert.Contains("reverse proxy", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CAPTCHA", section, StringComparison.Ordinal);
    }
}
