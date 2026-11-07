// F12-MEAS2-C · M9 (yld.overbook.<room-type code> key family in the settings registry, maximum 10, read by OverbookingWashReport by code)
// and L7 (meas.target.upsellPerArrival, Owner money target, default Rs 50 = 5000 paise). Owner decisions 7 Nov 2026.
using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Reporting.Reports.MeasRevenue;

namespace Hcc.Meas.Tests;

/// <summary>Read store with stored changes only (the reader path that SettingsReader.GetAsync(propertyId, …) uses).</summary>
internal sealed class FakeReadStore : IConfigurationReadStore
{
    public List<SettingChangeRow> Rows { get; } = [];

    public void Store(string key, long value) => Rows.Add(new SettingChangeRow(Rows.Count + 1, key, new DateOnly(2026, 1, 1), value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "null", "", value.ToString(System.Globalization.CultureInfo.InvariantCulture), null, "test", 1, "Owner", null, null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null));

    public Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(new DateOnly(2026, 11, 7));

    public Task<string> PropertyNameAsync(int propertyId, CancellationToken ct = default) => Task.FromResult("Test");

    public Task<IReadOnlyList<SettingChangeRow>> ActiveChangesAsync(int propertyId, string? key, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SettingChangeRow>>(Rows.Where(r => key is null || r.Key == key).ToList());

    public Task<IReadOnlyDictionary<string, byte[]>> VersionsAsync(int propertyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, byte[]>>(new Dictionary<string, byte[]>());

    public Task<(int Total, IReadOnlyList<SettingChangeRow> Items)> HistoryAsync(int propertyId, string? key, int page, int pageSize, CancellationToken ct = default)
        => Task.FromResult<(int, IReadOnlyList<SettingChangeRow>)>((Rows.Count, Rows));

    public Task<IReadOnlyList<SettingChangeRow>> AllChangesAsync(int propertyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SettingChangeRow>>(Rows);

    public Task<IReadOnlyList<SettingRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SettingRequestRow>>([]);

    public Task<int> SchemaVersionAsync(CancellationToken ct = default) => Task.FromResult(0);
}

public sealed class MeasSettingsFamilyTests
{
    private static readonly SettingsRegistry Registry = new([MeasurementCatalogue.Provider()!]);

    [Fact]
    public void Family_resolves_room_type_codes_and_nothing_else()
    {
        var s = Registry.Find("yld.overbook.DLX");
        Assert.NotNull(s);
        Assert.Equal("yld.overbook.DLX", s!.Key);
        Assert.Equal("Overbooking allowance · DLX", s.Label);
        Assert.Equal(SettingType.Integer, s.Type);
        Assert.Equal((0L, 10L), (s.Definition.Min!.Value, s.Definition.Max!.Value));
        Assert.Equal(ConfigValue.Of(0L), s.Default);
        Assert.True(s.Money);
        Assert.Equal(SettingApprover.Owner, s.Approver);
        Assert.Equal("measure", s.Group.Id);
        Assert.NotNull(Registry.Find("yld.overbook.Classic")); // prototype fixture keys (Room.type) are mixed case
        Assert.Null(Registry.Find("yld.other.DLX"));
        Assert.Null(Registry.Find("yld.overbook."));
        Assert.Null(Registry.Find("yld.overbook.A.B"));
        Assert.Null(Registry.Find("yld.overbook.TOO-LONG-CODE-123456789"));
        Assert.Null(Registry.Find("yld.overbook.D L X"));
        Assert.Null(Registry.Find("meas.target.nope"));
        Assert.DoesNotContain(Registry.All, x => x.Key.StartsWith("yld.overbook.", StringComparison.Ordinal)); // per-property keys are not listed
        Assert.Single(Registry.Families);
        Assert.Equal("yld.overbook.*", Registry.Families[0].Key);
    }

    [Fact]
    public void Family_outside_the_prefix_list_is_refused()
    {
        var bad = new SettingsProvider("x", MeasurementCatalogue.GroupId, [new SettingDefinition("meas.any.*", "Any", SettingType.Integer)]);
        Assert.Throws<InvalidOperationException>(() => new SettingsRegistry([bad]));
    }

    [Fact]
    public async Task Overbooking_report_reads_a_stored_2_for_DLX_by_code_and_clamps_at_10()
    {
        var ct = TestContext.Current.CancellationToken;
        var reads = new FakeReadStore();
        reads.Store("yld.overbook.DLX", 2);
        reads.Store("yld.overbook.STE", 15); // above the maximum (written before the cap): read as 10
        var reader = new SettingsReader(Registry, null!, reads);
        Assert.Equal(ConfigValue.Of(2L), await reader.GetAsync(1, "yld.overbook.DLX", null, ct));
        Assert.Equal(2, await OverbookingWashReport.AllowedAsync(reader, 1, ["DLX"], ct));
        Assert.Equal(0, await OverbookingWashReport.AllowedAsync(reader, 1, ["STD"], ct)); // never set = 0 (M-6)
        Assert.Equal(12, await OverbookingWashReport.AllowedAsync(reader, 1, ["DLX", "STD", "STE"], ct));
        Assert.Equal(0, await OverbookingWashReport.AllowedAsync(reader, 1, ["", "A.B"], ct)); // no code / invalid code = 0, never an error
        Assert.Equal(0, await OverbookingWashReport.AllowedAsync(null, 1, ["DLX"], ct));
    }

    [Fact]
    public async Task Upsell_target_is_an_owner_money_setting_with_default_5000_paise()
    {
        var t = MeasurementCatalogue.Targets.Single(x => x.Key == MeasurementCatalogue.UpsellTargetKey);
        Assert.Equal(("money", 5000L), (t.Kind, t.Default));
        var s = Registry.Get("meas.target.upsellPerArrival");
        Assert.Equal(SettingType.Money, s.Type);
        Assert.Equal(SettingApprover.Owner, s.Approver);
        Assert.Equal(ConfigValue.Of(5000L), s.Default);
        var ct = TestContext.Current.CancellationToken;
        var reads = new FakeReadStore();
        var reader = new SettingsReader(Registry, null!, reads);
        Assert.Equal(5000, (await reader.GetAsync(1, MeasurementCatalogue.UpsellTargetKey, null, ct)).Number);
        reads.Store(MeasurementCatalogue.UpsellTargetKey, 6000);
        Assert.Equal(6000, (await reader.GetAsync(1, MeasurementCatalogue.UpsellTargetKey, null, ct)).Number);
    }
}
