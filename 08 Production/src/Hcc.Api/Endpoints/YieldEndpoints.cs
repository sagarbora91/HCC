using Hcc.Application.Billing;
using Hcc.Application.Yield;
using Hcc.Contracts.Yield;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Yield;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Yield &amp; tariff endpoints (W2-12): GET /yield/calendar|config|quote|proposals|publishes and POST /commands/{SaveYieldPlan, SaveYieldSeason,
/// SetYieldDayOfWeek, SaveYieldOccupancyRule, SetYieldGuardrails, SaveYieldRestriction, SetYieldOverride, SetYieldForecast, PublishYieldTariffs,
/// ProposeYieldPublish, DecideYieldProposal}. Every route needs a session; the actor comes from the session only.
/// </summary>
public static class YieldEndpoints
{
    public static IServiceCollection AddYieldModule(this IServiceCollection services)
    {
        services.AddSingleton<IYieldStore>(sp => new SqlYieldStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IRatePublicationPort>(sp => new SqlRatePublicationPort(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<YieldService>();
        services.AddSingleton<YieldQueries>();
        services.AddSingleton<IYieldRateQuery>(sp => sp.GetRequiredService<YieldQueries>()); // for P2's quote/availability (see README)
        return services;
    }

    public static RouteGroupBuilder MapYieldEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<SaveYieldPlanRequest, YieldChangeResult>(g, "SaveYieldPlan", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SavePlanAsync(Ctx(c, "SaveYieldPlan", k), r, ct));
        BillingEndpoints.Command<SaveYieldSeasonRequest, YieldChangeResult>(g, "SaveYieldSeason", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SaveSeasonAsync(Ctx(c, "SaveYieldSeason", k), r, ct));
        BillingEndpoints.Command<SetYieldDayOfWeekRequest, YieldChangeResult>(g, "SetYieldDayOfWeek", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SetDayOfWeekAsync(Ctx(c, "SetYieldDayOfWeek", k), r, ct));
        BillingEndpoints.Command<SaveYieldOccupancyRuleRequest, YieldChangeResult>(g, "SaveYieldOccupancyRule", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SaveOccupancyRuleAsync(Ctx(c, "SaveYieldOccupancyRule", k), r, ct));
        BillingEndpoints.Command<SetYieldGuardrailsRequest, YieldChangeResult>(g, "SetYieldGuardrails", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SetGuardrailsAsync(Ctx(c, "SetYieldGuardrails", k), r, ct));
        BillingEndpoints.Command<SaveYieldRestrictionRequest, YieldChangeResult>(g, "SaveYieldRestriction", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SaveRestrictionAsync(Ctx(c, "SaveYieldRestriction", k), r, ct));
        BillingEndpoints.Command<SetYieldOverrideRequest, YieldChangeResult>(g, "SetYieldOverride", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SetOverrideAsync(Ctx(c, "SetYieldOverride", k), r, ct));
        BillingEndpoints.Command<SetYieldForecastRequest, YieldChangeResult>(g, "SetYieldForecast", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().SetForecastAsync(Ctx(c, "SetYieldForecast", k), r, ct));
        BillingEndpoints.Command<PublishYieldRequest, YieldPublishResult>(g, "PublishYieldTariffs", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().PublishAsync(Ctx(c, "PublishYieldTariffs", k), r, ct));
        BillingEndpoints.Command<ProposeYieldPublishRequest, YieldProposalResult>(g, "ProposeYieldPublish", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().ProposeAsync(Ctx(c, "ProposeYieldPublish", k), r, ct));
        BillingEndpoints.Command<DecideYieldProposalRequest, YieldDecisionResult>(g, "DecideYieldProposal", (sp, c, k, r, ct) => sp.GetRequiredService<YieldService>().DecideAsync(Ctx(c, "DecideYieldProposal", k), r, ct));

        g.MapGet("/yield/calendar", async (HttpContext http, YieldQueries q, string? from, int? nights, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetCalendarAsync(property, principal, from, nights ?? YieldService.WindowNights, ct).ConfigureAwait(false));
        });
        g.MapGet("/yield/config", async (HttpContext http, YieldQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetConfigAsync(property, principal, ct).ConfigureAwait(false));
        });
        g.MapGet("/yield/quote", async (HttpContext http, YieldQueries q, string planCode, int roomTypeId, string arrival, int nights, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.QuoteAsync(property, principal, planCode, roomTypeId, arrival, nights, ct).ConfigureAwait(false));
        });
        g.MapGet("/yield/proposals", async (HttpContext http, YieldQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListProposalsAsync(property, principal, ct).ConfigureAwait(false));
        });
        g.MapGet("/yield/publishes", async (HttpContext http, YieldQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListPublishesAsync(property, principal, ct).ConfigureAwait(false));
        });
        return api;
    }

    private static Hcc.Application.Commands.CommandContext Ctx(Caller caller, string operation, string key) => new(caller.PropertyId, caller.PrincipalId, operation, key);
}
