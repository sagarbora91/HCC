using Hcc.Application.Compliance;
using Hcc.Contracts.Compliance;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Compliance;

using Hcc.Domain.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Compliance calendar = SCC's tax module in full (DEC-116): <c>POST /api/v1/commands/{MarkComplianceDone|MarkComplianceNotApplicable|
/// ReopenComplianceItem|SetComplianceFy|SaveComplianceProfile|ToggleComplianceArchive|ToggleComplianceCategoryArchive|SaveCustomObligation|
/// DeleteCustomObligation|AddComplianceEvidence|DeleteComplianceEvidence|SaveComplianceSettings|GenerateCaPack}</c> with an <c>Idempotency-Key</c>
/// header and <c>GET /api/v1/compliance/{state|month|dashboard|action-center|due-this-week|badge|register|print|payable|evidence/{id}|evidence-zip|
/// ca-packs}</c>. Owner + Accountant (DEC-055); the Owner manages.
/// </summary>
public static class ComplianceEndpoints
{
    public static IServiceCollection AddHccCompliance(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlComplianceStore(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<SqlPlatform>().DocumentObjects));
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
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Compliance); // ADM-1 (DEC-214): refused while the sub-function is off; reads stay on g
        cmd.MapPost("/commands/" + ComplianceCommands.OpMarkDone, async (HttpContext http, MarkDoneRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.MarkDoneAsync(http.HccCommand(ComplianceCommands.OpMarkDone), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpMarkNotApplicable, async (HttpContext http, MarkNotApplicableRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.MarkNotApplicableAsync(http.HccCommand(ComplianceCommands.OpMarkNotApplicable), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpReopen, async (HttpContext http, ReopenItemRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReopenAsync(http.HccCommand(ComplianceCommands.OpReopen), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpSetFy, async (HttpContext http, SetFyRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetFyAsync(http.HccCommand(ComplianceCommands.OpSetFy), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpSaveProfile, async (HttpContext http, SaveProfileRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveProfileAsync(http.HccCommand(ComplianceCommands.OpSaveProfile), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpToggleArchive, async (HttpContext http, ToggleArchiveRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ToggleArchiveAsync(http.HccCommand(ComplianceCommands.OpToggleArchive), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpToggleCategory, async (HttpContext http, ToggleCategoryArchiveRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ToggleCategoryAsync(http.HccCommand(ComplianceCommands.OpToggleCategory), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpSaveCustom, async (HttpContext http, SaveCustomObligationRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveCustomAsync(http.HccCommand(ComplianceCommands.OpSaveCustom), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpDeleteCustom, async (HttpContext http, DeleteCustomObligationRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteCustomAsync(http.HccCommand(ComplianceCommands.OpDeleteCustom), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpAddEvidence, async (HttpContext http, AddEvidenceRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddEvidenceAsync(http.HccCommand(ComplianceCommands.OpAddEvidence), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpDeleteEvidence, async (HttpContext http, DeleteEvidenceRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteEvidenceAsync(http.HccCommand(ComplianceCommands.OpDeleteEvidence), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpSaveSettings, async (HttpContext http, SaveSettingsRequest body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveSettingsAsync(http.HccCommand(ComplianceCommands.OpSaveSettings), body, ct)));
        cmd.MapPost("/commands/" + ComplianceCommands.OpGenerateCaPack, async (HttpContext http, GenerateCaPackRequest? body, ComplianceCommands c, CancellationToken ct) =>
            http.HccReply(await c.GenerateCaPackAsync(http.HccCommand(ComplianceCommands.OpGenerateCaPack), body ?? new GenerateCaPackRequest(), ct)));

        g.MapGet("/compliance/state", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetStateAsync(property, principal, ct));
        });
        g.MapGet("/compliance/month", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null, string? month = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetMonthAsync(property, principal, fy, month, ct));
        });
        g.MapGet("/compliance/dashboard", async (HttpContext http, ComplianceQueries queries, CancellationToken ct, int? fy = null, string? q = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await queries.GetDashboardAsync(property, principal, fy, q, ct));
        });
        g.MapGet("/compliance/action-center", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetActionCenterAsync(property, principal, fy, ct));
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
        g.MapGet("/compliance/register", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetRegisterCsvAsync(property, principal, fy, ct));
        });
        g.MapGet("/compliance/print", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null, string? month = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetPrintAsync(property, principal, fy, month ?? string.Empty, ct));
        });
        g.MapGet("/compliance/payable", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetPayableAsync(property, principal, fy, ct));
        });
        g.MapGet("/compliance/evidence/{id:long}", async (HttpContext http, ComplianceQueries q, long id, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetEvidenceFileAsync(property, principal, id, ct));
        });
        g.MapGet("/compliance/evidence-zip", async (HttpContext http, ComplianceQueries q, CancellationToken ct, int? fy = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetEvidenceZipAsync(property, principal, fy, ct));
        });
        g.MapGet("/compliance/ca-packs", async (HttpContext http, ComplianceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetCaPacksAsync(property, principal, ct));
        });
        return api;
    }
}
