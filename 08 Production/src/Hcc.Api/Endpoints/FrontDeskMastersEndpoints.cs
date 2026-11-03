using Hcc.Application.FrontDeskMasters;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.FrontDeskMasters;

namespace Hcc.Api.Endpoints;

/// <summary>
/// MA-5 front-desk lists: <c>POST /api/v1/commands/{Save|Retire|Reactivate|Delete}{BookingSource|IdType|NumberSeries|BookingPolicy}</c>,
/// <c>Reorder{BookingSources|IdTypes|NumberSeries|BookingPolicies}</c>; <c>GET /api/v1/masters/{booking-sources|id-types|number-series|policies}</c>
/// and their <c>/history</c> (+ <c>/{id}/history</c>). Needs the Masters module (AddMastersModule: history store, business date).
/// </summary>
public static class FrontDeskMastersEndpoints
{
    public static IServiceCollection AddFrontDeskMastersModule(this IServiceCollection services)
    {
        services.AddSingleton<IFrontDeskMastersStore, SqlFrontDeskMastersStore>();
        services.AddSingleton<IFrontDeskMastersReadStore>(sp => new SqlFrontDeskMastersReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<INumberSeriesSource, NumberSeriesSource>();   // read by InvoiceService (tax invoice, bill of supply, credit note, RST)
        services.AddSingleton<Application.Reservations.IBookingSourceCheck, Infrastructure.SqlServer.FrontDeskMasters.SqlBookingSourceCheck>(); // MA-11: CreateReservation
        services.AddSingleton<FrontDeskMastersCommands>();
        services.AddSingleton<FrontDeskMastersQueries>();
        return services;
    }

    private static readonly (string Path, string Master)[] Lists =
    [
        ("booking-sources", FrontDeskMasterIds.BookingSources), ("id-types", FrontDeskMasterIds.IdTypes),
        ("number-series", FrontDeskMasterIds.NumberSeries), ("policies", FrontDeskMasterIds.Policies),
        ("purposes", FrontDeskMasterIds.Purposes), ("vehicle-types", FrontDeskMasterIds.VehicleTypes), // LF A3, A4
    ];

    public static RouteGroupBuilder MapFrontDeskMastersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var mst = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Masters); // ADM follow-up (DEC-214): master-list commands only; reads stay on g
        static string Op(string master, string action) => FrontDeskMastersCommands.Op(master, action);

        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.BookingSources, "save"), async (HttpContext http, SaveBookingSourceRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveBookingSourceAsync(http.HccCommand(Op(FrontDeskMasterIds.BookingSources, "save")), body, ct)));
        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.IdTypes, "save"), async (HttpContext http, SaveIdTypeRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveIdTypeAsync(http.HccCommand(Op(FrontDeskMasterIds.IdTypes, "save")), body, ct)));
        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.Purposes, "save"), async (HttpContext http, SavePlainListRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SavePurposeAsync(http.HccCommand(Op(FrontDeskMasterIds.Purposes, "save")), body, ct)));
        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.VehicleTypes, "save"), async (HttpContext http, SavePlainListRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveVehicleTypeAsync(http.HccCommand(Op(FrontDeskMasterIds.VehicleTypes, "save")), body, ct)));
        foreach (var (path, master) in new[] { ("purposes", FrontDeskMasterIds.Purposes), ("vehicle-types", FrontDeskMasterIds.VehicleTypes) })
            g.MapGet("/masters/" + path, async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
            {
                var (property, _) = http.HccActor();
                return Results.Ok(await q.PlainListAsync(property, master, page, pageSize, status, ct));
            });
        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.NumberSeries, "save"), async (HttpContext http, SaveNumberSeriesRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveNumberSeriesAsync(http.HccCommand(Op(FrontDeskMasterIds.NumberSeries, "save")), body, ct)));
        mst.MapPost("/commands/" + Op(FrontDeskMasterIds.Policies, "save"), async (HttpContext http, SaveBookingPolicyRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveBookingPolicyAsync(http.HccCommand(Op(FrontDeskMasterIds.Policies, "save")), body, ct)));

        foreach (var (path, master) in Lists)
        {
            var retire = Op(master, "retire");
            var reactivate = Op(master, "reactivate");
            var delete = Op(master, "delete");
            var reorder = Op(master, "reorder");
            mst.MapPost("/commands/" + retire, async (HttpContext http, FrontDeskRowRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.RetireAsync(http.HccCommand(retire), master, body, ct)));
            mst.MapPost("/commands/" + reactivate, async (HttpContext http, FrontDeskRowRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.ReactivateAsync(http.HccCommand(reactivate), master, body, ct)));
            mst.MapPost("/commands/" + delete, async (HttpContext http, FrontDeskRowRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.DeleteAsync(http.HccCommand(delete), master, body, ct)));
            mst.MapPost("/commands/" + reorder, async (HttpContext http, FrontDeskReorderRequest body, FrontDeskMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.ReorderAsync(http.HccCommand(reorder), master, body, ct)));
            g.MapGet("/masters/" + path + "/history", async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, null, page, pageSize, ct));
            });
            g.MapGet("/masters/" + path + "/{id:int}/history", async (HttpContext http, FrontDeskMastersQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, id, page, pageSize, ct));
            });
        }

        g.MapGet("/masters/booking-sources", async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BookingSourcesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/id-types", async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.IdTypesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/number-series", async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.NumberSeriesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/policies", async (HttpContext http, FrontDeskMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.PoliciesAsync(property, principal, page, pageSize, status, ct));
        });
        return api;
    }
}
