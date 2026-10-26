using System.Text.Json;
using Hcc.Application.Configuration;
using Hcc.Contracts.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Master configuration (W2-1): <c>POST /api/v1/commands/{SetSetting|DecideSettingRequest|WithdrawScheduledSetting|RevertSetting|ImportConfiguration}</c>,
/// <c>GET /api/v1/configuration/{groups|groups/{id}|search|history|requests|modules|export}</c>, <c>POST /api/v1/configuration/import-plan</c> (dry run).
/// Other modules: register settings with <see cref="ISettingsProvider"/>, read them with <see cref="ISettingsReader"/>, and gate a
/// switchable module's commands with <see cref="IModuleGate"/> or <see cref="RequireHccModule{TBuilder}"/>.
/// </summary>
public static class ConfigurationEndpoints
{
    public static IServiceCollection AddHccConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlConfigurationStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IConfigurationStore>(sp => sp.GetRequiredService<SqlConfigurationStore>());
        services.AddSingleton<IConfigurationReadStore>(sp => sp.GetRequiredService<SqlConfigurationStore>());
        services.AddSingleton(sp => new SettingsRegistry(sp.GetServices<ISettingsProvider>())); // built-ins + every module's providers
        services.AddSingleton<ISettingsReader, SettingsReader>();
        services.AddSingleton<IModuleGate, ModuleGate>();
        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<ConfigurationQueries>();
        return services;
    }

    /// <summary>Refuses the endpoint (FORBIDDEN, prototype message) while the module is switched off (DEC-057). Use on a switchable module's commands.</summary>
    public static TBuilder RequireHccModule<TBuilder>(this TBuilder builder, string moduleId) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var (property, _) = http.HccActor();
            await http.RequestServices.GetRequiredService<IModuleGate>().EnsureOnAsync(property, moduleId, http.RequestAborted).ConfigureAwait(false);
            return await next(ctx).ConfigureAwait(false);
        });
        return builder;
    }

    public static RouteGroupBuilder MapConfigurationEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + ConfigurationService.OpSet, async (HttpContext http, SetSettingRequest body, ConfigurationService s, CancellationToken ct) =>
            http.HccReply(await s.SetSettingAsync(http.HccCommand(ConfigurationService.OpSet), body, ct)));
        g.MapPost("/commands/" + ConfigurationService.OpDecide, async (HttpContext http, DecideSettingRequestRequest body, ConfigurationService s, CancellationToken ct) =>
            http.HccReply(await s.DecideAsync(http.HccCommand(ConfigurationService.OpDecide), body, ct)));
        g.MapPost("/commands/" + ConfigurationService.OpWithdraw, async (HttpContext http, WithdrawScheduledSettingRequest body, ConfigurationService s, CancellationToken ct) =>
            http.HccReply(await s.WithdrawAsync(http.HccCommand(ConfigurationService.OpWithdraw), body, ct)));
        g.MapPost("/commands/" + ConfigurationService.OpRevert, async (HttpContext http, RevertSettingRequest body, ConfigurationService s, CancellationToken ct) =>
            http.HccReply(await s.RevertAsync(http.HccCommand(ConfigurationService.OpRevert), body, ct)));
        g.MapPost("/commands/" + ConfigurationService.OpImport, async (HttpContext http, ImportConfigurationRequest body, ConfigurationService s, CancellationToken ct) =>
            http.HccReply(await s.ImportAsync(http.HccCommand(ConfigurationService.OpImport), body, ct)));

        g.MapGet("/configuration/groups", async (HttpContext http, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GroupsAsync(p, u, ct));
        });
        g.MapGet("/configuration/groups/{groupId}", async (string groupId, HttpContext http, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GroupAsync(p, u, groupId, ct));
        });
        g.MapGet("/configuration/search", async (HttpContext http, ConfigurationQueries queries, CancellationToken ct, string? q = null) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await queries.SearchAsync(p, u, q, ct));
        });
        g.MapGet("/configuration/history", async (HttpContext http, ConfigurationQueries q, CancellationToken ct, string? key = null, int page = 1, int pageSize = 50) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.HistoryAsync(p, u, key, page, pageSize, ct));
        });
        g.MapGet("/configuration/requests", async (HttpContext http, ConfigurationQueries q, CancellationToken ct, string? status = "Pending") =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.RequestsAsync(p, u, status, ct));
        });
        g.MapGet("/configuration/modules", async (HttpContext http, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, _) = http.HccActor();
            return Results.Ok(await q.ModulesAsync(p, ct));
        });
        g.MapGet("/configuration/values", async (HttpContext http, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.ValuesAsync(p, u, ct));
        });
        g.MapGet("/configuration/export",async (HttpContext http, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.ExportAsync(p, u, ct));
        });
        g.MapPost("/configuration/import-plan", async (HttpContext http, JsonElement body, ConfigurationQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.ImportPlanAsync(p, u, body, ct));
        });
        return api;
    }
}
