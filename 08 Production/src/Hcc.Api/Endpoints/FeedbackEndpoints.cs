using Hcc.Application.GuestServices;
using Hcc.Contracts.GuestServices;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.GuestServices;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F4-T3 guest feedback: <c>POST /api/v1/commands/RecordGuestFeedback</c> (record), <c>GET /api/v1/guest-services/feedback?from&amp;to</c>,
/// <c>GET /api/v1/guest-services/feedback/follow-ups?openOnly</c>, <c>POST /api/v1/commands/ResolveFeedbackFollowUp</c>.
/// A background worker runs the day-close sweep (consumer <c>feedback-followups</c> on <c>BusinessDayClosed</c>).
/// </summary>
public static class FeedbackEndpoints
{
    public static IServiceCollection AddFeedbackModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeedbackStore>(sp => new SqlFeedbackStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(sp => new FeedbackService(sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<IFeedbackStore>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new FeedbackFollowUpProjector(sp.GetRequiredService<SqlPlatform>().Outbox));
        services.AddHostedService<FeedbackFollowUpWorker>();
        return services;
    }

    public static RouteGroupBuilder MapFeedbackEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var s = api.MapHccSecured();
        var g = s.MapGroup("/guest-services/feedback");
        s.MapPost("/commands/RecordGuestFeedback", async (HttpContext http, RecordGuestFeedbackRequest body, FeedbackService svc, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await svc.RecordAsync(p, u, body, ct).ConfigureAwait(false));
        });
        g.MapGet("", async (HttpContext http, FeedbackService svc, CancellationToken ct, DateOnly? from = null, DateOnly? to = null) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await svc.ListAsync(p, u, from, to, ct).ConfigureAwait(false));
        });
        g.MapGet("/follow-ups", async (HttpContext http, FeedbackService svc, CancellationToken ct, bool openOnly = true) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await svc.FollowUpsAsync(p, u, openOnly, ct).ConfigureAwait(false));
        });
        s.MapPost("/commands/ResolveFeedbackFollowUp", async (HttpContext http, ResolveFeedbackFollowUpRequest body, FeedbackService svc, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            await svc.ResolveFollowUpAsync(p, u, body, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return api;
    }
}

/// <summary>Polls the outbox for <c>BusinessDayClosed</c> (the follow-up sweep).</summary>
public sealed class FeedbackFollowUpWorker(FeedbackFollowUpProjector projector, ILogger<FeedbackFollowUpWorker> logger) : OutboxPollingWorker(logger, "Feedback follow-up sweep")
{
    protected override Task PollAsync(CancellationToken ct) => projector.RunOnceAsync(ct: ct);
}
