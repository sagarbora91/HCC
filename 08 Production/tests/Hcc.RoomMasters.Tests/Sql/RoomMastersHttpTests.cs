using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.RoomMasters;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.RoomMasters.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.RoomMasters.Tests.Sql;

/// <summary>The rooms-masters endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI / Map lines as HccComposition (+ MA-0's masters
/// module and the inventory block port they use), the real access policy and a test session service: the wire shapes sync/roommasters.ts and
/// sync/rooms.ts read (roomTypeId, rackRates[{amountPaise, effectiveFrom}], floorCode, …) and the contract errors.</summary>
[Trait("Category", "Sql")]
public class RoomMastersHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private RoomMastersWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await RoomMastersWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IRoomBlockPort>(new SqlRoomBlockPort(_w.Platform.Connections, _w.Clock));
        builder.Services.AddMastersModule();
        builder.Services.AddRoomMastersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapMastersEndpoints();
        _app.MapGroup(ApiConventions.BasePath).MapRoomMastersEndpoints();
        await _app.StartAsync(Ct);
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _w.DisposeAsync();
    }

    private HttpRequestMessage Req(HttpMethod m, string path, string? user, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (user is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(_w.Users[user]));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    private async Task<string> GetAsync(string path, string user)
    {
        using var r = await _http.SendAsync(Req(HttpMethod.Get, path, user), Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Lists_read_for_every_role_with_the_sync_wire_shapes_and_money_only_for_money_readers()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/masters/rooms", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        var rooms = await GetAsync("/masters/rooms", "housekeeper");
        foreach (var f in new[] { "\"roomId\":", "\"roomNo\":\"101\"", "\"floorCode\":\"F1\"", "\"roomTypeCode\":\"Classic\"", "\"features\":[]", "\"active\":true", "\"version\":" })
            Assert.Contains(f, rooms, StringComparison.Ordinal);
        var hk = await GetAsync("/masters/room-types?page=1&pageSize=200", "housekeeper");
        Assert.Contains("\"roomTypeId\":", hk, StringComparison.Ordinal);
        Assert.Contains("\"rackRates\":[]", hk, StringComparison.Ordinal);
        var rec = await GetAsync("/masters/room-types?page=1&pageSize=200", "reception");
        Assert.Contains("\"rackRates\":[{\"amountPaise\":200000,\"effectiveFrom\":\"2000-01-01\"}]", rec, StringComparison.Ordinal);
        Assert.Contains("\"total\":0", await GetAsync("/masters/amenities?page=1&pageSize=200", "maint"), StringComparison.Ordinal);
        Assert.Contains("\"items\":[]", await GetAsync("/masters/occupancy-charges?page=1&pageSize=200", "cashier"), StringComparison.Ordinal);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Get, "/masters/room-types/history", "manager"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
        using (var tad = await _http.SendAsync(Req(HttpMethod.Get, "/masters/occupancy-charges/history", "techadmin"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, tad.StatusCode);
        Assert.Contains("\"items\":", await GetAsync("/masters/amenities/history?page=1&pageSize=200", "techadmin"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commands_over_HTTP_add_rooms_save_a_room_type_and_return_master_errors_with_reason()
    {
        var body = new AddRoomsRequest(["110", "111"], _w.FloorIds["F1"], _w.Types["Deluxe"]);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/AddRooms", "manager", body, "h0"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/AddRooms", "techadmin", body, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(["110", "111"], (await ok.Content.ReadFromJsonAsync<AddRoomsResult>(Web, Ct))!.Rooms.Select(r => r.RoomNo));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/AddRooms", "techadmin", body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var taken = await _http.SendAsync(Req(HttpMethod.Post, "/commands/AddRooms", "owner", body with { RoomNos = ["111"] }, "h2"), Ct))
        {
            var err = (await taken.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_CODE_TAKEN", "roomNos"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        var types = JsonSerializer.Deserialize<RoomTypeList>(await GetAsync("/masters/room-types", "owner"), Web)!;
        var deluxe = types.Items.First(t => t.Code == "Deluxe");
        var save = new SaveRoomTypeRequest(deluxe.RoomTypeId, "Deluxe", "Deluxe King", 2, 1, true, "King", 240, [], "Spacious", 20,
            [new DatedAmount(300000, "2000-01-01"), new DatedAmount(320000, "2026-11-01")], deluxe.Version);
        using (var t = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveRoomType", "owner", save, "h3"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, t.StatusCode);
            var v = (await t.Content.ReadFromJsonAsync<RoomTypeView>(Web, Ct))!;
            Assert.Equal(("Deluxe King", 2, 11), (v.Name, v.RackRates.Count, v.RoomCount));
        }

        var room = JsonSerializer.Deserialize<RoomMasterList>(await GetAsync("/masters/rooms", "owner"), Web)!.Items.First(r => r.RoomNo == "103");
        using (var locked = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RenumberRoom", "owner", new RenumberRoomRequest(room.RoomId, "113", room.Version), "h4"), Ct))
        {
            var err = (await locked.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("INVALID_TRANSITION", "MASTER_LOCKED_BY_BOOKINGS"), (err.Code, err.Details!["reason"].GetString()));
        }

        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireRoom", "owner", new RoomIdRequest(room.RoomId, "AAAAAAAAAAA="), "h5"), Ct))
            Assert.Equal("STALE_VERSION", (await stale.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var order = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReorderAmenities", "owner", new ReorderAmenitiesRequest([]), "h6"), Ct))
            Assert.Equal("VALIDATION", (await order.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);   // nothing to reorder
    }
}
