using Hcc.Application.Catalogue;
using Hcc.Application.Masters;
using Hcc.Application.Tax;
using Hcc.Contracts.Catalogue;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Catalogue;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Catalogue endpoints (MA-2): POST /api/v1/commands/{Save|Retire|Reactivate|Delete}{MealPlan|ExtraItem|DiscountReason},
/// Reorder{MealPlans|ExtraItems|DiscountReasons}, PostExtra, SetReservationMealPlan, PostMealPlanNights, RequestReasonedDiscount; GET
/// /api/v1/catalogue/{meal-plans|extras|discount-reasons} (+ /history, /{id}/history) and /api/v1/catalogue/links.
/// </summary>
public static class CatalogueEndpoints
{
    public static IServiceCollection AddCatalogueModule(this IServiceCollection services)
    {
        services.AddSingleton<ICatalogueStore, SqlCatalogueStore>();
        services.AddSingleton<ICatalogueReadStore>(sp => new SqlCatalogueReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(sp => new CatalogueCommands(sp.GetRequiredService<Application.Commands.ICommandRunner>(), sp.GetRequiredService<Application.Access.IAccessPolicy>(),
            sp.GetRequiredService<ICatalogueStore>(), sp.GetRequiredService<IMastersStore>(), sp.GetRequiredService<Application.Billing.IBillingStore>(),
            sp.GetRequiredService<IGstRateResolver>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IGstChargeSnapshots>()));
        services.AddSingleton<CatalogueQueries>();
        services.AddSingleton<Application.Closing.IMealPlanNightPoster, MealPlanNightPoster>();          // MA-11: close night step posts plan lines
        services.AddSingleton<Application.Billing.IRoomNightBundle, Infrastructure.SqlServer.Catalogue.SqlRoomNightBundle>(); // MA-11: bundled slab
        return services;
    }

    public static RouteGroupBuilder MapCatalogueEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        static string C(string op) => "/commands/" + op;
        g.MapPost(C(CatalogueCommands.OpSaveMealPlan), async (HttpContext http, SaveMealPlanRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.SaveMealPlanAsync(http.HccCommand(CatalogueCommands.OpSaveMealPlan), body, ct)));
        g.MapPost(C(CatalogueCommands.OpRetireMealPlan), async (HttpContext http, MealPlanRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.RetireMealPlanAsync(http.HccCommand(CatalogueCommands.OpRetireMealPlan), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReactivateMealPlan), async (HttpContext http, MealPlanRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReactivateMealPlanAsync(http.HccCommand(CatalogueCommands.OpReactivateMealPlan), body, ct)));
        g.MapPost(C(CatalogueCommands.OpDeleteMealPlan), async (HttpContext http, MealPlanRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.DeleteMealPlanAsync(http.HccCommand(CatalogueCommands.OpDeleteMealPlan), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReorderMealPlans), async (HttpContext http, ReorderMealPlansRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReorderMealPlansAsync(http.HccCommand(CatalogueCommands.OpReorderMealPlans), body, ct)));
        g.MapPost(C(CatalogueCommands.OpSaveExtraItem), async (HttpContext http, SaveExtraItemRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.SaveExtraItemAsync(http.HccCommand(CatalogueCommands.OpSaveExtraItem), body, ct)));
        g.MapPost(C(CatalogueCommands.OpRetireExtraItem), async (HttpContext http, ExtraItemRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.RetireExtraItemAsync(http.HccCommand(CatalogueCommands.OpRetireExtraItem), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReactivateExtraItem), async (HttpContext http, ExtraItemRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReactivateExtraItemAsync(http.HccCommand(CatalogueCommands.OpReactivateExtraItem), body, ct)));
        g.MapPost(C(CatalogueCommands.OpDeleteExtraItem), async (HttpContext http, ExtraItemRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.DeleteExtraItemAsync(http.HccCommand(CatalogueCommands.OpDeleteExtraItem), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReorderExtraItems), async (HttpContext http, ReorderExtraItemsRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReorderExtraItemsAsync(http.HccCommand(CatalogueCommands.OpReorderExtraItems), body, ct)));
        g.MapPost(C(CatalogueCommands.OpSaveDiscountReason), async (HttpContext http, SaveDiscountReasonRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.SaveDiscountReasonAsync(http.HccCommand(CatalogueCommands.OpSaveDiscountReason), body, ct)));
        g.MapPost(C(CatalogueCommands.OpRetireDiscountReason), async (HttpContext http, DiscountReasonRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.RetireDiscountReasonAsync(http.HccCommand(CatalogueCommands.OpRetireDiscountReason), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReactivateDiscountReason), async (HttpContext http, DiscountReasonRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReactivateDiscountReasonAsync(http.HccCommand(CatalogueCommands.OpReactivateDiscountReason), body, ct)));
        g.MapPost(C(CatalogueCommands.OpDeleteDiscountReason), async (HttpContext http, DiscountReasonRef body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.DeleteDiscountReasonAsync(http.HccCommand(CatalogueCommands.OpDeleteDiscountReason), body, ct)));
        g.MapPost(C(CatalogueCommands.OpReorderDiscountReasons), async (HttpContext http, ReorderDiscountReasonsRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.ReorderDiscountReasonsAsync(http.HccCommand(CatalogueCommands.OpReorderDiscountReasons), body, ct)));
        g.MapPost(C(CatalogueCommands.OpPostExtra), async (HttpContext http, PostExtraRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.PostExtraAsync(http.HccCommand(CatalogueCommands.OpPostExtra), body, ct)));
        g.MapPost(C(CatalogueCommands.OpSetReservationMealPlan), async (HttpContext http, SetReservationMealPlanRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.SetReservationMealPlanAsync(http.HccCommand(CatalogueCommands.OpSetReservationMealPlan), body, ct)));
        g.MapPost(C(CatalogueCommands.OpPostMealPlanNights), async (HttpContext http, PostMealPlanNightsRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.PostMealPlanNightsAsync(http.HccCommand(CatalogueCommands.OpPostMealPlanNights), body, ct)));
        g.MapPost(C(CatalogueCommands.OpRequestReasonedDiscount), async (HttpContext http, RequestReasonedDiscountRequest body, CatalogueCommands c, CancellationToken ct) => http.HccReply(await c.RequestReasonedDiscountAsync(http.HccCommand(CatalogueCommands.OpRequestReasonedDiscount), body, ct)));

        g.MapGet("/catalogue/meal-plans", async (HttpContext http, CatalogueQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.MealPlansAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/catalogue/extras", async (HttpContext http, CatalogueQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ExtrasAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/catalogue/discount-reasons", async (HttpContext http, CatalogueQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.DiscountReasonsAsync(property, principal, page, pageSize, status, ct));
        });
        foreach (var (path, master) in new[] { ("meal-plans", CatalogueIds.MealPlans), ("extras", CatalogueIds.Extras), ("discount-reasons", CatalogueIds.DiscountReasons) })
        {
            g.MapGet($"/catalogue/{path}/history", async (HttpContext http, CatalogueQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, null, page, pageSize, ct));
            });
            g.MapGet($"/catalogue/{path}/{{id:int}}/history", async (HttpContext http, CatalogueQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, id, page, pageSize, ct));
            });
        }

        g.MapGet("/catalogue/links", async (HttpContext http, CatalogueQueries q, CancellationToken ct, string? from = null, string? to = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.LinksAsync(property, principal, from, to, ct));
        });
        return api;
    }
}
