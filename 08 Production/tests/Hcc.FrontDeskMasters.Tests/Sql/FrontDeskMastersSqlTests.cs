using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Domain.Errors;
using Hcc.FrontDeskMasters.Tests.Harness;

namespace Hcc.FrontDeskMasters.Tests.Sql;

/// <summary>MA-5 commands and reads on SQL Server: defaults until the first change, permissions (DEC-MA-1 / DEC-058), versions,
/// idempotency, in-use rules, numbering rules, policy rules, history.</summary>
[Trait("Category", "Sql")]
public class FrontDeskMastersSqlTests : IAsyncLifetime
{
    private FdmWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await FdmWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private FrontDeskMastersCommandsShim C => new(_w);

    [Fact]
    public async Task Lists_read_their_defaults_with_negative_ids_until_the_first_change_and_mask_commission_without_money_visibility()
    {
        var hk = await _w.Queries.BookingSourcesAsync(_w.PropertyId, _w.Users["housekeeper"], ct: Ct);
        Assert.Equal(8, hk.Total);
        Assert.Equal((-1, "WALKIN", "DIRECT", string.Empty), (hk.Items[0].RowId, hk.Items[0].Code, hk.Items[0].Segment, hk.Items[0].Version));
        Assert.All(hk.Items, x => Assert.Null(x.CommissionBps));
        Assert.All((await _w.Queries.BookingSourcesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items, x => Assert.Equal(0, x.CommissionBps));
        var ids = await _w.Queries.IdTypesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct);
        Assert.Equal(("VISA", true, true), (ids.Items[^1].Code, ids.Items[^1].NeedsPhoto, ids.Items[^1].ForeignerOnly));
        var series = await _w.Queries.NumberSeriesAsync(_w.PropertyId, _w.Users["cashier"], ct: Ct);
        Assert.Equal(("INV", "INV/2627/0000001", 0), (series.Items[0].Code, series.Items[0].NextNumber, series.Items[0].Issued));
        Assert.Equal("CN/2627/00000001", series.Items[2].NextNumber);
        Assert.Equal("RCP/00001", series.Items[3].NextNumber);
        var pol = Assert.Single((await _w.Queries.PoliciesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items);
        Assert.Equal(("DEFAULT", 1), (pol.Code, pol.Usage));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM mst.booking_source", Ct));
    }

    [Fact]
    public async Task First_change_stores_the_defaults_then_adds_the_row_with_audit_and_history()
    {
        var r = await C.SaveSource("techadmin", new SaveBookingSourceRequest(null, "agent-tg", "Travel Guru (agent)", "AGENT"));
        Assert.Equal("AGENT-TG", r.Code);
        Assert.Equal(9, await _w.CountAsync($"SELECT COUNT(*) FROM mst.booking_source WHERE property_id = {_w.PropertyId}", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = N'Master Booking sources: 8 default rows stored'", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = N'Master Booking sources: added AGENT-TG · Travel Guru (agent)'", Ct));
        Assert.Equal(5, await _w.CountAsync($"SELECT COUNT(*) FROM mst.master_history WHERE master = 'bookingSources' AND row_id = {r.RowId}", Ct)); // code, name, sortOrder, segment, commissionBps
        var list = await _w.Queries.BookingSourcesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct);
        Assert.True(list.Items.All(x => x.RowId > 0 && x.Version.Length > 0));
        var hist = await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["techadmin"], FrontDeskMasterIds.BookingSources, r.RowId, ct: Ct);
        Assert.Equal(5, hist.Total);
        await Assert.ThrowsAsync<HccException>(() => _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["manager"], FrontDeskMasterIds.BookingSources, ct: Ct));
    }

    [Fact]
    public async Task Sources_and_id_types_are_for_Owner_and_Tech_admin_commission_for_the_Owner_only()
    {
        foreach (var u in new[] { "manager", "reception", "housekeeper" })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => C.SaveSource(u, new SaveBookingSourceRequest(null, "X", "X", "DIRECT")))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => C.SaveSource("techadmin", new SaveBookingSourceRequest(null, "AG2", "Agent two", "AGENT", null, 1000)))).Code);
        var a = await C.SaveSource("owner", new SaveBookingSourceRequest(null, "AG2", "Agent two", "AGENT", null, 1000));
        var (id, ver) = await _w.RowAsync("mst.booking_source", "AG2", Ct);
        var renamed = await C.SaveSource("techadmin", new SaveBookingSourceRequest(id, "AG2", "Agent 2", "AGENT", null, null, null, ver)); // commission omitted = kept
        Assert.Equal(1000, (await _w.Queries.BookingSourcesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(x => x.Code == "AG2").CommissionBps);
        Assert.NotEqual(a.Version, renamed.Version);
        var t = await C.SaveIdType("techadmin", new SaveIdTypeRequest(-5, "PAN", "PAN", Accepted: false));
        Assert.Equal("PAN", t.Code);
        Assert.False((await _w.Queries.IdTypesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items.Single(x => x.Code == "PAN").Accepted);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => C.SaveIdType("reception", new SaveIdTypeRequest(null, "RATION", "Ration card")))).Code);
    }

    [Fact]
    public async Task Versions_defaults_after_storage_idempotency_and_typed_master_errors()
    {
        await C.SaveSource("owner", new SaveBookingSourceRequest(null, "AG2", "Agent two", "AGENT"));
        var stale = await Assert.ThrowsAsync<HccException>(() => C.SaveSource("owner", new SaveBookingSourceRequest(-1, "WALKIN", "Walk-in guest", "DIRECT")));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        var (id, ver) = await _w.RowAsync("mst.booking_source", "WALKIN", Ct);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(() => C.SaveSource("owner", new SaveBookingSourceRequest(id, "WALKIN", "Walk-in guest", "DIRECT", null, null, null, "AAAAAAAAAAA=")))).Code);
        var key = FdmWorld.Key();
        var first = await _w.Commands.SaveBookingSourceAsync(_w.Ctx("owner", key), new SaveBookingSourceRequest(id, "WALKIN", "Walk-in guest", "DIRECT", null, null, null, ver), Ct);
        var again = await _w.Commands.SaveBookingSourceAsync(_w.Ctx("owner", key), new SaveBookingSourceRequest(id, "WALKIN", "Walk-in guest", "DIRECT", null, null, null, ver), Ct);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value, again.Value);
        var taken = await Assert.ThrowsAsync<HccException>(() => C.SaveSource("owner", new SaveBookingSourceRequest(null, "phone", "Phone 2", "DIRECT")));
        Assert.Equal((HccErrorCode.VALIDATION, "MASTER_CODE_TAKEN"), (taken.Code, Reason(taken)));
        var dupChannel = await C.SaveSource("owner", new SaveBookingSourceRequest(null, "OTA-BN", "OTA · BookNest", "OTA", "BookNest"));
        Assert.Equal("MASTER_INVALID", Reason(await Assert.ThrowsAsync<HccException>(() => C.SaveSource("owner", new SaveBookingSourceRequest(null, "OTA-BN2", "BookNest again", "OTA", "booknest")))));
        Assert.True(dupChannel.RowId > 0);
    }

    [Fact]
    public async Task A_source_used_by_reservations_keeps_its_code_retires_and_is_never_deleted()
    {
        await _w.ExecAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, source) VALUES ({_w.PropertyId}, 'HCC-9001', N'Test (fictional)', '2026-10-24', '2026-10-25', 'Confirmed', 'Walk-in');", Ct);
        await C.SaveSource("owner", new SaveBookingSourceRequest(null, "AG2", "Agent two", "AGENT"));
        var (id, ver) = await _w.RowAsync("mst.booking_source", "WALKIN", Ct);
        var code = await Assert.ThrowsAsync<HccException>(() => C.SaveSource("owner", new SaveBookingSourceRequest(id, "WALK", "Walk-in", "DIRECT", null, null, null, ver)));
        Assert.Equal((HccErrorCode.INVALID_TRANSITION, "MASTER_IN_USE"), (code.Code, Reason(code)));
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => C.Delete("owner", FrontDeskMasterIds.BookingSources, id, ver))));
        var retired = await C.Retire("techadmin", FrontDeskMasterIds.BookingSources, id, ver);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM mst.booking_source WHERE row_id = {id} AND active = 0 AND retired_on = '2026-10-24'", Ct));
        Assert.Contains("still used: 1", await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'MST_FDM' ORDER BY audit_id DESC", Ct) as string, StringComparison.Ordinal);
        await C.Reactivate("techadmin", FrontDeskMasterIds.BookingSources, id, retired.Version);
        var (w, wv) = await _w.RowAsync("mst.booking_source", "WHATSAPP", Ct);
        Assert.True((await C.Delete("techadmin", FrontDeskMasterIds.BookingSources, w, wv)).Deleted);
        await _w.ExecAsync($"INSERT gst.guest(property_id, full_name, phone, id_type, origin, created_utc) VALUES ({_w.PropertyId}, N'Guest (fictional)', '9000000000', N'Aadhaar', 'manual', '2026-10-24');", Ct);
        Assert.Equal(1, (await _w.Queries.IdTypesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(x => x.Code == "AADHAAR").Usage);
    }

    [Fact]
    public async Task Numbering_is_Owner_only_new_series_start_on_a_free_date_and_the_series_in_force_stays()
    {
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => C.SaveSeries("techadmin", new SaveNumberSeriesRequest(-1, "INV", "Tax invoice", "INV", "HCC", 1, 5, "FY", "2000-04-01")))).Code);
        var inv = await C.SaveSeries("owner", new SaveNumberSeriesRequest(-1, "INV", "Tax invoice", "INV", "HCC", 101, 5, "FY", "2000-04-01"));
        Assert.Equal("HCC/2627/00101", (await _w.Queries.NumberSeriesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(x => x.Code == "INV").NextNumber);
        Assert.Contains("today's business date", (await Assert.ThrowsAsync<HccException>(() =>
            C.SaveSeries("owner", new SaveNumberSeriesRequest(null, "INV-OLD", "Old", "INV", "OLD", 1, 5, "FY", "2026-01-01")))).Message, StringComparison.Ordinal);
        var next = await C.SaveSeries("owner", new SaveNumberSeriesRequest(null, "INV-2728", "Tax invoice FY 2027-28", "INV", "H27", 1, 6, "FY", "2027-04-01"));
        Assert.Contains("already starts", (await Assert.ThrowsAsync<HccException>(() =>
            C.SaveSeries("owner", new SaveNumberSeriesRequest(null, "INV-X", "Another", "INV", "HX", 1, 6, "FY", "2027-04-01")))).Message, StringComparison.Ordinal);
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => C.Retire("owner", FrontDeskMasterIds.NumberSeries, inv.RowId, inv.Version))));
        var retired = await C.Retire("owner", FrontDeskMasterIds.NumberSeries, next.RowId, next.Version);
        Assert.True((await C.Delete("owner", FrontDeskMasterIds.NumberSeries, next.RowId, retired.Version)).Deleted);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["techadmin"], FrontDeskMasterIds.NumberSeries, ct: Ct))).Code);
        Assert.True((await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], FrontDeskMasterIds.NumberSeries, inv.RowId, ct: Ct)).Total >= 3); // prefix, startNo, padding
    }

    [Fact]
    public async Task Policies_are_Owner_only_with_one_default_one_policy_per_source_and_the_default_kept()
    {
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => C.SavePolicy("techadmin", new SaveBookingPolicyRequest(null, "OTA-STD", "OTA standard", 48)))).Code);
        Assert.Contains("already the default policy", (await Assert.ThrowsAsync<HccException>(() => C.SavePolicy("owner", new SaveBookingPolicyRequest(null, "P2", "Second", 0)))).Message, StringComparison.Ordinal);
        var p = await C.SavePolicy("owner", new SaveBookingPolicyRequest(null, "OTA-STD", "OTA standard", 48, 10000, 10000, 2500, ["OTA", "ota-booknest"]));
        Assert.Equal(new[] { "OTA", "OTA-BOOKNEST" }, (await _w.Queries.PoliciesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items.Single(x => x.RowId == p.RowId).Sources);
        Assert.Contains("already follows OTA standard", (await Assert.ThrowsAsync<HccException>(() => C.SavePolicy("owner", new SaveBookingPolicyRequest(null, "P3", "Third", 1, 0, 0, 0, ["OTA"])))).Message, StringComparison.Ordinal);
        var (d, dv) = await _w.RowAsync("inv.policy", "DEFAULT", Ct);
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => C.Retire("owner", FrontDeskMasterIds.Policies, d, dv))));
        var hist = await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], FrontDeskMasterIds.Policies, p.RowId, ct: Ct);
        Assert.Contains(hist.Items, h => h.Field == "cancelChargeBps" && h.NewValue == "100 %");
    }

    [Fact]
    public async Task Reorder_takes_default_ids_on_a_fresh_list_and_every_row_once()
    {
        var r = await C.Reorder("techadmin", FrontDeskMasterIds.IdTypes, [-7, -1, -2, -3, -4, -5, -6]);
        Assert.Equal(7, r.Rows);
        Assert.Equal("VISA", (await _w.Queries.IdTypesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items[0].Code);
        await Assert.ThrowsAsync<HccException>(() => C.Reorder("techadmin", FrontDeskMasterIds.IdTypes, [-1]));
    }

    /// <summary>Short calls with a fresh Idempotency-Key.</summary>
    private sealed class FrontDeskMastersCommandsShim(FdmWorld w)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public async Task<FrontDeskRowResult> SaveSource(string u, SaveBookingSourceRequest r) => (await w.Commands.SaveBookingSourceAsync(w.Ctx(u, FdmWorld.Key()), r, Ct)).Value;

        public async Task<FrontDeskRowResult> SaveIdType(string u, SaveIdTypeRequest r) => (await w.Commands.SaveIdTypeAsync(w.Ctx(u, FdmWorld.Key()), r, Ct)).Value;

        public async Task<FrontDeskRowResult> SaveSeries(string u, SaveNumberSeriesRequest r) => (await w.Commands.SaveNumberSeriesAsync(w.Ctx(u, FdmWorld.Key()), r, Ct)).Value;

        public async Task<FrontDeskRowResult> SavePolicy(string u, SaveBookingPolicyRequest r) => (await w.Commands.SaveBookingPolicyAsync(w.Ctx(u, FdmWorld.Key()), r, Ct)).Value;

        public async Task<FrontDeskRowResult> Retire(string u, string m, int id, string v) => (await w.Commands.RetireAsync(w.Ctx(u, FdmWorld.Key()), m, new FrontDeskRowRequest(id, v), Ct)).Value;

        public async Task<FrontDeskRowResult> Reactivate(string u, string m, int id, string v) => (await w.Commands.ReactivateAsync(w.Ctx(u, FdmWorld.Key()), m, new FrontDeskRowRequest(id, v), Ct)).Value;

        public async Task<FrontDeskDeleteResult> Delete(string u, string m, int id, string v) => (await w.Commands.DeleteAsync(w.Ctx(u, FdmWorld.Key()), m, new FrontDeskRowRequest(id, v), Ct)).Value;

        public async Task<FrontDeskReorderResult> Reorder(string u, string m, IReadOnlyList<int> ids) => (await w.Commands.ReorderAsync(w.Ctx(u, FdmWorld.Key()), m, new FrontDeskReorderRequest(ids), Ct)).Value;
    }
}
