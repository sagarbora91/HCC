using Hcc.Application.Compliance;
using Hcc.Contracts.Compliance;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Compliance;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Compliance calendar endpoints (W2-5): <c>POST /api/v1/commands/{MarkComplianceFiled|MarkComplianceNotApplicable|ReopenComplianceFiling|
/// SaveComplianceObligation|ToggleComplianceObligation|SaveComplianceSettings|GenerateCaPack|SeedComplianceDefaults}</c> with an
/// <c>Idempotency-Key</c> header and <c>GET /api/v1/compliance/{calendar|due-this-week|badge|obligations|ca-packs}</c>. Owner + Accountant (DEC-055).
/// </summary>
public static class ComplianceEndpoints
{
    public static IServiceCollection AddHccCompliance(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlComplianceStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IComplianceStore>(sp => sp.GetRequiredService<SqlComplianceStore>());
        services.AddSingleton<IComplianceReadStore>(sp => sp.GetRequiredService<SqlComplianceStore>());
        services.AddSingleton<ComplianceCommands>();
        services.AddSingleton<ComplianceQueries>();
        return services;
    }

    public static RouteGroupBuilder MapComplianceEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + ComplianceCommands.OpMarkFiled, async (HttpContext http, MarkFiledRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.MarkFiledAsync(http.HccCommand(ComplianceCommands.OpMarkFiled), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpMarkNotApplicable, async (HttpContext http, MarkNotApplicableRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.MarkNotApplicableAsync(http.HccCommand(ComplianceCommands.OpMarkNotApplicable), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpReopen, async (HttpContext http, ReopenFilingRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReopenAsync(http.HccCommand(ComplianceCommands.OpReopen), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpSaveObligation, async (HttpContext http, SaveObligationRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveObligationAsync(http.HccCommand(ComplianceCommands.OpSaveObligation), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpToggleObligation, async (HttpContext http, ToggleObligationRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ToggleObligationAsync(http.HccCommand(ComplianceCommands.OpToggleObligation), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpSaveSettings, async (HttpContext http, SaveSettingsRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveSettingsAsync(http.HccCommand(ComplianceCommands.OpSaveSettings), body, ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpGenerateCaPack, async (HttpContext http, GenerateCaPackRequest? body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.GenerateCaPackAsync(http.HccCommand(ComplianceCommands.OpGenerateCaPack), body ?? new GenerateCaPackRequest(), ct)));
        g.MapPost("/commands/" + ComplianceCommands.OpSeedDefaults, async (HttpContext http, SeedDefaultsRequest? body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SeedDefaultsAsync(http.HccCommand(ComplianceCommands.OpSeedDefaults), body ?? new SeedDefaultsRequest(), ct)));

        g.MapGet("/compliance/calendar", async (HttpContext http, ComplianceQueries q, CancellationToken ct, bool includeInactive = false) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetCalendarAsync(property, principal, includeInactive, ct));
        });
        g.MapGet("/compliance/due-this-week", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetDueThisWeekAsync(property, principal, ct));
        });
        g.MapGet("/compliance/badge", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetBadgeAsync(property, principal, ct));
        });
        g.MapGet("/compliance/obligations", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetObligationsAsync(property, principal, ct));
        });
        g.MapGet("/compliance/ca-packs", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetCaPacksAsync(property, principal, ct));
        });
        return api;
    }
}
