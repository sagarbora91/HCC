using Hcc.Application.Tariff;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Tariff;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Tariff;
using Hcc.Infrastructure.SqlServer.Tax;
using Hcc.Masters.Tests.Harness;

namespace Hcc.Masters.Tests.Sql;

/// <summary>F4-T1 tariff card on SQL Server: publish in one transaction (schedule rows with day_set + card version, publish log, one audit row per
/// master row + TRC_PUBLISH), day-set resolution through inv.fn_tariff_on, permissions PERM-TRF-001…003, published rows read-only.</summary>
public class TariffCardSqlTests : IAsyncLifetime
{
    private MastersWorld _w = null!;
    private TariffCardCommands _c = null!;
    private TariffCardQueries _q = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MastersWorld.CreateAsync(Ct);
        var store = new SqlTariffCardStore(_w.Platform.Connections);
        var gst = new SqlGstMasterStore(_w.Platform.Connections);
        _c = new TariffCardCommands(_w.Platform.Commands, _w.Policy, store, gst, new GstRateResolver(gst), _w.Clock);
        _q = new TariffCardQueries(_w.Policy, store);
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static SaveTariffCardDraftRequest Pair(string from = "2026-11-02") => new(null, "Winter", from,
        [new TariffCardLineDto(1, "room", "Classic", "EP", DaySet: "weekday", RateExcl: 300_000), new TariffCardLineDto(2, "room", "Classic", "EP", DaySet: "weekend", RateExcl: 360_000),
         new TariffCardLineDto(3, "occupancy", "Classic", OccKind: "child")]);

    [Fact]
    public async Task Manager_proposes_owner_publishes_in_one_transaction_with_audit_rows_and_day_set_resolution()
    {
        var draft = (await _c.SaveDraftAsync(_w.Ctx("manager", "trc-1"), Pair(), Ct)).Value;
        Assert.Equal("draft", draft.Status);
        Assert.Equal(500, draft.Lines[0].TaxBps);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _c.PublishAsync(_w.Ctx("manager", "trc-2"), new(draft.VersionId), Ct))).Code);
        Assert.Equal("proposed", (await _c.ProposeAsync(_w.Ctx("manager", "trc-3"), new(draft.VersionId), Ct)).Value.Status);
        var pub = (await _c.PublishAsync(_w.Ctx("owner", "trc-4"), new(draft.VersionId), Ct)).Value;
        Assert.Equal("published", pub.Status);
        Assert.Equal(2, pub.PublishLog.Count);
        Assert.Equal(2, await _w.CountAsync($"SELECT COUNT(*) FROM inv.room_type_tariff WHERE card_version_id = {draft.VersionId};", Ct));
        Assert.Equal(2, await _w.CountAsync($"SELECT COUNT(*) FROM mst.tariff_card_publish_log WHERE version_id = {draft.VersionId};", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'TRC_PUBLISH_ROW';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'TRC_PUBLISH';", Ct));
        const string T = "(SELECT room_type_id FROM inv.room_type WHERE code = 'Classic')";
        Assert.Equal(300_000L, Convert.ToInt64(await _w.ScalarAsync($"SELECT inv.fn_tariff_on({T}, '2026-11-05');", Ct), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(360_000L, Convert.ToInt64(await _w.ScalarAsync($"SELECT inv.fn_tariff_on({T}, '2026-11-06');", Ct), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(360_000L, Convert.ToInt64(await _w.ScalarAsync($"SELECT inv.fn_tariff_on({T}, '2026-11-08');", Ct), System.Globalization.CultureInfo.InvariantCulture));
        // F4-int: the quote / availability / web-booking / OTA lookups read the same day-set aware rate (weekday Thu, weekend Fri).
        var inv = new Hcc.Infrastructure.SqlServer.Inventory.SqlInventoryStore(_w.Platform.Connections);
        var typeId = Convert.ToInt32(await _w.ScalarAsync($"SELECT {T};", Ct), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(300_000L, await inv.GetTariffOnAsync(typeId, new DateOnly(2026, 11, 5), null, Ct));
        Assert.Equal(360_000L, await inv.GetTariffOnAsync(typeId, new DateOnly(2026, 11, 6), null, Ct));
        // published = read-only (application refusal and the table trigger)
        var e = await Assert.ThrowsAsync<HccException>(() => _c.SaveDraftAsync(_w.Ctx("owner", "trc-5"), Pair() with { VersionId = draft.VersionId }, Ct));
        Assert.Equal("TRC_PUBLISHED_READONLY", e.Details!["reason"].GetString());
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"UPDATE mst.tariff_card_version SET label = 'x' WHERE version_id = {draft.VersionId};", Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"DELETE mst.tariff_card_line WHERE version_id = {draft.VersionId};", Ct));
    }

    [Fact]
    public async Task Housekeeping_cannot_view_past_dates_and_hand_entered_rows_are_refused()
    {
        await Assert.ThrowsAsync<HccException>(() => _q.ListAsync(_w.PropertyId, _w.Users["housekeeper"], Ct));
        Assert.Empty((await _q.ListAsync(_w.PropertyId, _w.Users["reception"], Ct)).Versions);
        var past = await Assert.ThrowsAsync<HccException>(() => _c.SaveDraftAsync(_w.Ctx("owner", "trc-p"), Pair("2026-10-23"), Ct));
        Assert.Equal("TRC_PAST_EFFECTIVE", past.Details!["reason"].GetString());
        await _w.ExecAsync("INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) SELECT room_type_id, '2026-11-01', 220000 FROM inv.room_type WHERE code = 'Classic';", Ct);
        var d = (await _c.SaveDraftAsync(_w.Ctx("owner", "trc-h1"), new(null, "C", "2026-11-01", [new TariffCardLineDto(1, "room", "Classic", "EP", RateExcl: 230_000)]), Ct)).Value;
        var c = await Assert.ThrowsAsync<HccException>(() => _c.PublishAsync(_w.Ctx("owner", "trc-h2"), new(d.VersionId), Ct));
        Assert.Equal("TRC_CONFLICT", c.Details!["reason"].GetString());
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action LIKE 'TRC_PUBLISH%';", Ct));
    }
}
