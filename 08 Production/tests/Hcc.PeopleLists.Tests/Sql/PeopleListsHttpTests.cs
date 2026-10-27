using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.PeopleLists.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.PeopleLists.Tests.Sql;

/// <summary>The People lists endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI lines and Map call as HccComposition, the real access
/// policy and a test session service: the wire shapes the production sync layer (sync/masters.ts MST_SERVER) reads.</summary>
[Trait("Category", "Sql")]
public sealed class PeopleListsHttpTests(PeopleDb db) : IClassFixture<PeopleDb>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private Hotel _h = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _h = await db.NewHotelAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(db.Platform);
        builder.Services.AddSingleton(db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(db.Clock);
        builder.Services.AddMastersModule();
        builder.Services.AddPeopleListsModule();
        builder.Services.AddSingleton<IAccessPolicy>(db.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_h.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        var api = _app.MapGroup(ApiConventions.BasePath);
        api.MapMastersEndpoints();
        api.MapPeopleListsEndpoints();
        await _app.StartAsync(Ct);
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    private HttpRequestMessage Req(HttpMethod m, string path, string? user, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (user is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(_h.Users[user]));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    [Fact]
    public async Task Every_list_reads_for_any_signed_in_user_with_the_wire_shapes_the_sync_layer_expects()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/hr/holidays", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        foreach (var (route, id) in new[] { ("designations", "designationId"), ("shift-patterns", "shiftPatternId"), ("leave-types", "leaveTypeId") })
        {
            using var r = await _http.SendAsync(Req(HttpMethod.Get, $"/hr/{route}?page=1&pageSize=200", "housekeeper"), Ct);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await r.Content.ReadAsStringAsync(Ct);
            if (route == "designations") Assert.Contains("\"total\":0", json, StringComparison.Ordinal);   // no staff in this hotel: nothing to seed from
            else
            {
                Assert.Contains($"\"{id}\":", json, StringComparison.Ordinal);
                Assert.Contains("\"version\":", json, StringComparison.Ordinal);
                Assert.Contains("\"used\":", json, StringComparison.Ordinal);
            }
        }

        using (var shifts = await _http.SendAsync(Req(HttpMethod.Get, "/hr/shift-patterns", "reception"), Ct))
        {
            var page = (await shifts.Content.ReadFromJsonAsync<PeopleListPage<ShiftPatternView>>(Web, Ct))!;
            Assert.Equal((4, "07:00", "fixed"), (page.Total, page.Items[0].Start, page.Items[0].WeeklyOffRule));
        }

        using var hol = await _http.SendAsync(Req(HttpMethod.Get, "/hr/holidays", "maint"), Ct);
        Assert.Equal(0, (await hol.Content.ReadFromJsonAsync<PeopleListPage<HolidayView>>(Web, Ct))!.Total);
    }

    [Fact]
    public async Task Commands_enforce_permissions_idempotency_versions_and_the_master_error_contract()
    {
        var body = new SaveShiftPatternRequest(null, "S", "Split", "10:00", "20:00", null, 60);
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "owner", body), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "manager", body, "w0"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        ShiftPatternView added;
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "techadmin", body, "w1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            added = (await ok.Content.ReadFromJsonAsync<ShiftPatternView>(Web, Ct))!;
            Assert.Equal(("S", 50, 60), (added.Code, added.SortOrder, added.BreakMinutes));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "techadmin", body, "w1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var taken = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "owner", body with { Name = "Other", Start = "11:00", End = "19:00" }, "w2"), Ct))
        {
            var err = (await taken.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_CODE_TAKEN", "code"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var bad = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveShiftPattern", "owner", new SaveShiftPatternRequest(null, "S5", "Five", "25:00", "20:00"), "w3"), Ct))
        {
            var err = (await bad.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("MASTER_INVALID", "start"), (err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireShiftPattern", "owner", new RetireShiftPatternRequest(added.ShiftPatternId, "AAAAAAAAAAA="), "w4"), Ct))
            Assert.Equal("STALE_VERSION", (await stale.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var hol = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHoliday", "techadmin", new SaveHolidayRequest(null, "2026-11-08", "Diwali", "paid"), "w5"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, hol.StatusCode);
        using (var holOwner = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHoliday", "owner", new SaveHolidayRequest(null, "2026-11-08", "Diwali", "paid"), "w6"), Ct))
        {
            var h = (await holOwner.Content.ReadFromJsonAsync<HolidayView>(Web, Ct))!;
            Assert.Equal(("2026-11-08", "FY 2026–27", "paid"), (h.Code, h.Fy, h.Kind));
        }

        using (var order = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReorderShiftPatterns", "owner", new ReorderShiftPatternsRequest(
            (await _h.Queries.ShiftPatternsAsync(_h.PropertyId, _h.U("owner"), ct: Ct)).Items.Select(x => x.ShiftPatternId).Reverse().ToList()), "w7"), Ct))
            Assert.Equal("S", (await order.Content.ReadFromJsonAsync<PeopleListPage<ShiftPatternView>>(Web, Ct))!.Items[0].Code);
        using (var del = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DeleteShiftPattern", "owner", new DeleteShiftPatternRequest(added.ShiftPatternId, await _h.VersionAsync("hr.shift_pattern", "shift_pattern_id", added.ShiftPatternId)), "w8"), Ct))
            Assert.True((await del.Content.ReadFromJsonAsync<DeletePeopleListResult>(Web, Ct))!.Deleted);
    }

    [Fact]
    public async Task History_routes_need_the_masters_permission_and_are_paged_per_list_and_per_row()
    {
        foreach (var route in new[] { "designations", "shift-patterns", "leave-types", "holidays" })
        {
            using var mgr = await _http.SendAsync(Req(HttpMethod.Get, $"/hr/{route}/history", "manager"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
            using var tech = await _http.SendAsync(Req(HttpMethod.Get, $"/hr/{route}/history?page=1&pageSize=200", "techadmin"), Ct);
            Assert.Equal(HttpStatusCode.OK, tech.StatusCode);
        }

        // SCC leave port (DEC-116): the leave list is fixed, so the history is shown on an edit of CL (name, sort order and a half-day quota).
        var cl = (await _h.Queries.LeaveTypesAsync(_h.PropertyId, _h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "CL");
        using var made = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveLeaveType", "owner",
            new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual leave (CL)", true, 3.5m, 15, cl.Version), "x1"), Ct);
        var saved = (await made.Content.ReadFromJsonAsync<LeaveTypeView>(Web, Ct))!;
        Assert.Equal(3.5m, saved.YearlyQuota);
        using var hist = await _http.SendAsync(Req(HttpMethod.Get, $"/hr/leave-types/{saved.LeaveTypeId}/history?page=1&pageSize=2", "owner"), Ct);
        var h = (await hist.Content.ReadFromJsonAsync<MasterHistoryList>(Web, Ct))!;
        Assert.Equal((3, 2), (h.Total, h.Items.Count));   // name, sortOrder, yearlyQuota: one row per changed field
        Assert.Equal(_h.U("owner"), h.Items[0].ChangedBy);
    }
}
