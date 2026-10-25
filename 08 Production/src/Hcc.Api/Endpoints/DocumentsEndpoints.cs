using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Contracts.Documents;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Documents;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ID document capture (W3-3, DEC-068): <c>POST /api/v1/commands/{CaptureIdDocument|ConfirmIdCapture|RejectIdCapture|ViewIdCaptureImage|RevealIdCaptureNumber|
/// PurgeIdCaptures}</c> and masked <c>GET /api/v1/documents/id-captures[/{id}]</c>. Needs the guest-ID key (<c>Hcc:GuestIdKeys</c>): without it the module is not
/// registered and the routes answer UNAVAILABLE. Image and OCR text are sealed with the guest-ID service; the simulated OCR engine is the default.
/// </summary>
public static class DocumentsEndpoints
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddSingleton<Hcc.Application.Configuration.ISettingsProvider>(DocumentSettings.Provider); // documents.retentionDays in Master configuration
        if (!services.Any(d => d.ServiceType == typeof(GuestIdProtector))) return services;
        services.AddSingleton(sp => new SqlDocumentStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IDocumentStore>(sp => sp.GetRequiredService<SqlDocumentStore>());
        services.AddSingleton<IDocumentImageStore>(sp => sp.GetRequiredService<SqlDocumentStore>());
        services.AddSingleton<IIdOcrEngine, SimulatedIdOcrEngine>();
        services.AddSingleton<DocumentService>();
        return services;
    }

    private static DocumentService Svc(IServiceProvider sp) => sp.GetService<DocumentService>()
        ?? throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.UNAVAILABLE, "ID document capture needs guest ID encryption, which is not configured on this server.");

    public static RouteGroupBuilder MapDocumentsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + DocumentService.OpCapture, async (HttpContext http, CaptureIdDocumentRequest body, IServiceProvider sp, CancellationToken ct) =>
            http.HccReply(await Svc(sp).CaptureAsync(http.HccCommand(DocumentService.OpCapture), body, ct)));
        g.MapPost("/commands/" + DocumentService.OpConfirm, async (HttpContext http, ConfirmIdCaptureRequest body, IServiceProvider sp, CancellationToken ct) =>
            http.HccReply(await Svc(sp).ConfirmAsync(http.HccCommand(DocumentService.OpConfirm), body, ct)));
        g.MapPost("/commands/" + DocumentService.OpReject, async (HttpContext http, RejectIdCaptureRequest body, IServiceProvider sp, CancellationToken ct) =>
            http.HccReply(await Svc(sp).RejectAsync(http.HccCommand(DocumentService.OpReject), body, ct)));
        g.MapPost("/commands/" + DocumentService.OpViewImage, async (HttpContext http, ViewIdCaptureImageRequest body, IServiceProvider sp, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return http.HccReply(await Svc(sp).ViewImageAsync(http.HccCommand(DocumentService.OpViewImage), body, ct));
        });
        g.MapPost("/commands/" + DocumentService.OpReveal, async (HttpContext http, RevealIdCaptureRequest body, IServiceProvider sp, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return http.HccReply(await Svc(sp).RevealAsync(http.HccCommand(DocumentService.OpReveal), body, ct));
        });
        g.MapPost("/commands/" + DocumentService.OpPurge, async (HttpContext http, PurgeIdCapturesRequest body, IServiceProvider sp, CancellationToken ct) =>
            http.HccReply(await Svc(sp).PurgeAsync(http.HccCommand(DocumentService.OpPurge), body, ct)));
        g.MapGet("/documents/id-captures", async (HttpContext http, IServiceProvider sp, CancellationToken ct, long? guestId = null, long? stayId = null, string? status = null) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await Svc(sp).ListAsync(p, u, guestId, stayId, status, ct));
        });
        g.MapGet("/documents/id-captures/{id:long}", async (HttpContext http, long id, IServiceProvider sp, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await Svc(sp).GetAsync(p, u, id, ct));
        });
        return api;
    }
}
