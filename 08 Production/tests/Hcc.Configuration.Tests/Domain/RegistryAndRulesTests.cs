using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;

namespace Hcc.Configuration.Tests.Domain;

/// <summary>
/// [CFG] golden rules ported from <c>03 Prototype/tests/stream-config.mjs</c>: fixed groups, registry invariants, DEC-058 decisions,
/// read-only reasons, plain validation messages, effective dates, dated values, summaries, search, display of secrets. No database.
/// </summary>
public class RegistryAndRulesTests
{
    private static readonly SettingsRegistry Registry = new([]);
    private static readonly DateOnly Day = new(2026, 10, 24);

    private static RegisteredSetting S(string key) => Registry.Get(key);

    private static ConfigValue T(string s) => ConfigValue.Of(s);

    private static ConfigValue N(long n) => ConfigValue.Of(n);

    [Fact]
    public void Twelve_fixed_groups_in_three_sections_with_pinned_names()
    {
        Assert.Equal(["Property and branding", "Rooms and rate plans", "Front-desk policies", "Money and tax", "Restaurant and POS", "Housekeeping and maintenance",
            "People, payroll and leave", "Compliance obligations", "Modules on/off", "Users, roles and PINs", "Messages and documents", "Data and system"],
            ConfigCatalog.Groups.Select(g => g.Label));
        Assert.Equal(["Business setup", "Operations and people", "Software and security"], ConfigCatalog.Sections);
        Assert.Equal(["rooms", "money", "pos", "people", "compliance"], ConfigCatalog.Groups.Where(g => g.Money).Select(g => g.Id));
        Assert.All(ConfigCatalog.Groups, g => Assert.Contains(g.Section, ConfigCatalog.Sections));
        Assert.Equal(["pos", "groups", "ota", "website", "messaging", "documents", "assistant", "inventory", "maintenance", "payroll"], ConfigCatalog.Modules.Select(m => m.Id));
        Assert.Equal(6, ConfigCatalog.CoreModules.Count);
    }

    [Fact]
    public void Registry_has_unique_keys_every_group_and_the_legacy_keys_and_no_synthetic_tax()
    {
        var keys = Registry.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var g in ConfigCatalog.Groups) Assert.Contains(Registry.All, s => s.Group.Id == g.Id);
        foreach (var k in new[] { "hotel", "checkin", "checkout", "discountLimit", "approvalExpiryHours", "inspection", "invoicePrefix" }) Assert.Contains(k, keys);
        Assert.DoesNotContain("taxBps", keys); // DEC-069: tax comes from the dated GST master (agent C), never a hub number
        foreach (var s in Registry.All)
        {
            Assert.False(string.IsNullOrEmpty(s.Affects), s.Key);
            if (s.Definition.Illustrative) Assert.Contains("Illustrative — CA to confirm for a hotel", s.Definition.Help, StringComparison.Ordinal);
            if (s.Type == SettingType.Choice) Assert.Null(SettingRules.Validate(s, s.Default));
        }

        // Group order: settings are listed in the 12-group order.
        var order = ConfigCatalog.Groups.Select(g => g.Id).ToList();
        Assert.Equal(Registry.All.Select(s => order.IndexOf(s.Group.Id)).Order(), Registry.All.Select(s => order.IndexOf(s.Group.Id)));
        Assert.Equal(10, Registry.InGroup("modules").Count());
        Assert.False(S("currency").Editable);
        Assert.Equal(EffectiveMode.Date, S("discountLimit").Mode);
        Assert.True(S("discountLimit").Dated);
        Assert.Equal(EffectiveMode.NextDay, S("fd.noShowTime").Mode);
    }

    [Fact]
    public void Provider_api_adds_dated_settings_with_group_defaults_and_a_later_provider_replaces_a_key()
    {
        var reg = new SettingsRegistry([
            new SettingsProvider("test-statutory", ConfigGroupIds.People, [
                new("test.pfCeiling", "PF wage ceiling", SettingType.Money) { Default = N(1_500_000), Dated = true, Illustrative = true, Help = "Illustrative — CA to confirm for a hotel." },
                new("test.weeklyOff", "Weekly off", SettingType.Choice) { Options = [T("Sunday"), T("Monday")], Default = T("Sunday"), Money = false },
            ]),
            new SettingsProvider("hk-override", ConfigGroupIds.Housekeeping, [new("hk.maxRoomsPerAttendant", "Rooms per attendant", SettingType.Integer) { Min = 2, Max = 30, Default = N(10) }]),
        ]);
        var d = reg.Get("test.pfCeiling");
        Assert.Equal(("people", EffectiveMode.Date, true, "test-statutory"), (d.Group.Id, d.Mode, d.Money, d.Source));
        Assert.Equal(ChangeMode.Deny, SettingRules.Decide(d, ConfigAuthority.Technical).Mode);
        Assert.Equal(ChangeMode.Allow, SettingRules.Decide(reg.Get("test.weeklyOff"), ConfigAuthority.Technical).Mode);
        Assert.Equal(("hk-override", 10L), (reg.Get("hk.maxRoomsPerAttendant").Source, reg.Get("hk.maxRoomsPerAttendant").Default.Number));
        Assert.Single(reg.All, s => s.Key == "hk.maxRoomsPerAttendant");
        Assert.Throws<InvalidOperationException>(() => new SettingsRegistry([new SettingsProvider("bad", "nope", [])]));
        Assert.Throws<InvalidOperationException>(() => new SettingsRegistry([new SettingsProvider("bad", "data", [new("Bad Key", "x", SettingType.Text)])]));
    }

    [Fact]
    public void DEC058_Owner_changes_everything_Tech_admin_only_non_money_others_read_only()
    {
        var editable = Registry.All.Where(s => s.Editable).ToList();
        Assert.All(editable, s => Assert.Equal(ChangeMode.Allow, SettingRules.Decide(s, ConfigAuthority.Full).Mode));
        Assert.All(editable, s => Assert.Equal(s.Money ? ChangeMode.Deny : s.Approver == SettingApprover.None ? ChangeMode.Allow : ChangeMode.Request,
            SettingRules.Decide(s, ConfigAuthority.Technical).Mode));
        Assert.All(editable, s => Assert.Equal(ChangeMode.Deny, SettingRules.Decide(s, ConfigAuthority.None).Mode));
        Assert.Contains("Owner-only (DEC-058)", SettingRules.Decide(S("discountLimit"), ConfigAuthority.Technical).Reason, StringComparison.Ordinal);
        Assert.Equal(("frontdesk", true), (S("fd.deposit").Group.Id, S("fd.deposit").Money));
        Assert.Equal(SettingApprover.Owner, SettingRules.Decide(S("sec.pinLock"), ConfigAuthority.Technical).Approver);
        Assert.Equal("Your change goes to the Manager or Owner for approval.", SettingRules.Decide(S("inspection"), ConfigAuthority.Technical).Reason);
        Assert.Equal("Owner or Tech admin (DEC-058) · Tech admin changes need Manager or Owner approval", S("inspection").Approval);
        Assert.Equal("Owner only (DEC-058)", S("discountLimit").Approval);
    }

    [Fact]
    public void Read_only_groups_show_the_reason_and_are_never_hidden()
    {
        foreach (var id in new[] { "rooms", "money", "pos", "people", "compliance" })
            Assert.Equal(new GroupAccess(true, SettingRules.OwnerOnly), SettingRules.Access(ConfigCatalog.Group(id)!, Registry.All, ConfigAuthority.Technical));
        foreach (var id in new[] { "property", "frontdesk", "hkm", "modules", "users", "messages", "data" })
            Assert.False(SettingRules.Access(ConfigCatalog.Group(id)!, Registry.All, ConfigAuthority.Technical).ReadOnly, id);
        foreach (var g in ConfigCatalog.Groups.Where(g => Registry.InGroup(g.Id).Any(s => s.Editable)))
            Assert.False(SettingRules.Access(g, Registry.All, ConfigAuthority.Full).ReadOnly, g.Id);
        Assert.Contains("Owner changes this", SettingRules.Access(ConfigCatalog.Group("property")!, Registry.All, ConfigAuthority.None).Reason, StringComparison.Ordinal);
        // Groups filled by module providers later show the pointer line read-only, with the reason.
        Assert.Equal("Nothing in this group is configurable here.", SettingRules.Access(ConfigCatalog.Group("pos")!, Registry.All, ConfigAuthority.Full).Reason);
    }

    [Fact]
    public void Validation_messages_are_plain_and_per_field()
    {
        Assert.Equal("Enter a time as HH:mm.", SettingRules.Validate(S("checkin"), T("25:00")));
        Assert.Equal("Use 2–8 capital letters, digits or hyphens.", SettingRules.Validate(S("invoicePrefix"), T("inv")));
        Assert.Equal("Use a whole number between 1 and 72 h.", SettingRules.Validate(S("fd.holdHours"), N(0)));
        Assert.Equal("Enter an amount from ₹0 to ₹1,00,000.", SettingRules.Validate(S("discountLimit"), N(-1)));
        Assert.Equal("A GSTIN has 15 capital letters and digits.", SettingRules.Validate(S("brand.gstin"), T("12345")));
        Assert.Null(SettingRules.Validate(S("brand.gstin"), T("")));
        Assert.Equal("Choose one of the listed options.", SettingRules.Validate(S("brand.printFormat"), T("Letter")));
        Assert.Equal("Choose on or off.", SettingRules.Validate(S("inspection"), N(1)));
        Assert.Equal("Enter text.", SettingRules.Validate(S("hotel"), N(1)));
        Assert.Equal("Property display name is required.", SettingRules.Validate(S("hotel"), T("  ")));
        Assert.Equal("Use at most 60 characters.", SettingRules.Validate(S("hotel"), T(new string('x', 61))));
        Assert.Equal("Enter an amount from ₹0 to ₹20,000.", SettingRules.Validate(S("fd.deposit"), ConfigValue.Parse("1500.5")));
        Assert.Equal(SettingRules.NotConfigurable, SettingRules.Validate(S("currency"), T("USD")));
        var pct = RegisteredSetting.From("t", ConfigCatalog.Group("rooms")!, new("yld.ceilBps", "Ceiling", SettingType.Percent) { Min = 0, Max = 10_000 });
        Assert.Equal("Use a percentage between 0 and 100%.", SettingRules.Validate(pct, N(20_000)));
        Assert.Equal("12.5%", SettingRules.Show(pct, N(1250)));
        Assert.Equal(T("Grand"), SettingRules.Clean(S("hotel"), T("  Grand ")));
    }

    [Fact]
    public void Effective_dates_immediate_from_a_date_or_next_business_day()
    {
        Assert.Equal("Property display name takes effect immediately; it cannot be dated.", SettingRules.EffectiveFrom(S("hotel"), Day, "2026-10-30").Error);
        Assert.Equal((Day, (string?)null), SettingRules.EffectiveFrom(S("hotel"), Day, "2026-10-24"));
        Assert.Equal("Effective date cannot be before the current business date.", SettingRules.EffectiveFrom(S("discountLimit"), Day, "2026-10-20").Error);
        Assert.Equal("Effective date can be at most one year ahead.", SettingRules.EffectiveFrom(S("discountLimit"), Day, "2028-01-01").Error);
        Assert.Equal("Choose an effective date.", SettingRules.EffectiveFrom(S("discountLimit"), Day, "27/10/2026").Error);
        Assert.Equal(new DateOnly(2026, 10, 27), SettingRules.EffectiveFrom(S("discountLimit"), Day, "2026-10-27").Date);
        Assert.Equal("No-show cut-off takes effect from the next business day (2026-10-25).", SettingRules.EffectiveFrom(S("fd.noShowTime"), Day, "2026-10-30").Error);
        Assert.Equal(new DateOnly(2026, 10, 25), SettingRules.EffectiveFrom(S("fd.noShowTime"), Day, null).Date);
    }

    [Fact]
    public void Value_in_force_on_a_date_is_the_latest_dated_version_and_newest_wins_on_the_same_date()
    {
        var versions = new[] { new DatedValue(1, Day, N(60_000)), new DatedValue(3, new(2026, 10, 27), N(70_000)), new DatedValue(2, Day, N(65_000)) };
        Assert.Equal(N(25_000), SettingRules.ValueOn(versions, Day.AddDays(-1), N(25_000)));
        Assert.Equal(N(65_000), SettingRules.ValueOn(versions, Day, N(25_000)));
        Assert.Equal(N(65_000), SettingRules.ValueOn(versions, new(2026, 10, 26), N(25_000)));
        Assert.Equal(N(70_000), SettingRules.ValueOn(versions, new(2026, 10, 30), N(25_000)));
        Assert.Equal([3L], SettingRules.Scheduled(versions, Day).Select(v => v.ChangeId));
    }

    [Fact]
    public void Summaries_search_display_and_module_messages_match_the_prototype()
    {
        var modules = ConfigCatalog.Group("modules")!;
        Assert.Equal("9 of 10 on · 6 core always on", SettingRules.Summary(modules, Registry.All, 0, 0, 9));
        Assert.Equal("10 of 10 on · 6 core always on", SettingRules.Summary(modules, Registry.All, 0, 0, 10));
        Assert.Matches(@"^\d+ settings$", SettingRules.Summary(ConfigCatalog.Group("property")!, Registry.All, 0, 0, 10));
        Assert.Equal("6 settings · 1 change pending", SettingRules.Summary(ConfigCatalog.Group("frontdesk")!, Registry.All, 1, 1, 10));
        Assert.Equal("3 settings · 2 dated rates ahead · 2 changes pending", SettingRules.Summary(ConfigCatalog.Group("money")!, Registry.All, 2, 2, 10));

        var g = SettingRules.Search(Registry.All, "gstin");
        Assert.Equal(("property", "brand.gstin"), (Assert.Single(g).Group.Id, Assert.Single(g[0].Settings).Key));
        Assert.Contains(SettingRules.Search(Registry.All, "pf"), h => h.Group.Id == "people");
        Assert.Contains(SettingRules.Search(Registry.All, "no-show"), h => h.Settings.Any(s => s.Key == "fd.noShowTime"));
        Assert.Empty(SettingRules.Search(Registry.All, "zzzz"));
        Assert.Equal(12, SettingRules.Search(Registry.All, "").Count);

        var key = S("msg.gatewayKey");
        Assert.Equal("Set (hidden)", SettingRules.Display(key, T("sk-demo-123"), ConfigAuthority.Full));
        Assert.Equal("Set (hidden)", SettingRules.Display(key, T("sk-demo-123"), ConfigAuthority.Technical));
        Assert.Equal("Not set", SettingRules.Show(key, T("")));
        Assert.Equal("₹250", SettingRules.Show(S("discountLimit"), N(25_000)));
        Assert.Equal("On", SettingRules.Show(S("inspection"), ConfigValue.Of(true)));
        Assert.Equal("24 h", SettingRules.Show(S("approvalExpiryHours"), N(24)));
        Assert.Equal("6 h", SettingRules.Show(S("fd.holdHours"), N(6)));
        Assert.Equal("—", SettingRules.Show(S("hotel"), T("")));
        Assert.Equal("Restaurant & POS is switched off in Master configuration › Modules on/off. Its data is kept; the Owner or Tech admin can switch it back on.",
            ConfigCatalog.ModuleOffMessage(ConfigCatalog.Module("pos")!));
        Assert.Contains("is hidden from menus, tiles, search, reminders and phone tabs, and its commands are refused. Its data and history are kept.",
            SettingRules.Impact(S("module.pos"), ConfigValue.Of(false), Day, Day, ConfigValue.Of(true))[^1], StringComparison.Ordinal);
        Assert.Contains("Takes effect on 2026-10-27; until then ₹250 stays in force.", SettingRules.Impact(S("discountLimit"), N(70_000), Day, new(2026, 10, 27), N(25_000)));
    }

    [Fact]
    public void Decide_block_never_self_approve_and_approver_rules()
    {
        Assert.Equal("You raised this request — another authorised user must decide it.", SettingRules.DecideBlock(7, SettingApprover.Owner, 7, ConfigAuthority.Full, true));
        Assert.Null(SettingRules.DecideBlock(7, SettingApprover.Owner, 1, ConfigAuthority.Full, true));
        Assert.Equal("Only the Owner can decide this request.", SettingRules.DecideBlock(7, SettingApprover.Owner, 2, ConfigAuthority.None, true));
        Assert.Null(SettingRules.DecideBlock(7, SettingApprover.Manager, 2, ConfigAuthority.None, true));
        Assert.Equal("Only a Manager or the Owner can decide this request.", SettingRules.DecideBlock(7, SettingApprover.Manager, 3, ConfigAuthority.None, false));
    }

    [Fact]
    public void Config_values_round_trip_as_json()
    {
        Assert.Equal("\"A5\"", T("A5").ToJson());
        Assert.Equal(T("A5"), ConfigValue.Parse("\"A5\""));
        Assert.Equal(N(-1500), ConfigValue.Parse("-1500"));
        Assert.Equal(ConfigValue.Of(true), ConfigValue.Parse("true"));
        Assert.Equal(ConfigValueKind.Other, ConfigValue.Parse("1.5").Kind);
        Assert.Equal(ConfigValueKind.Other, ConfigValue.Parse("{\"a\":1}").Kind);
        Assert.True(ConfigValue.Parse("null").IsNull);
    }
}
