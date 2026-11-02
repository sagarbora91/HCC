using Hcc.Application.Keys;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Keys;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Room keys (HCC_KeyAgent_Spec_v1). Staff (session): <c>POST /api/v1/commands/{RequestKey|CancelKey|ReadKey|RecordManualKey|SaveKeyLock|RegisterKeyEncoder}</c>,
/// <c>GET /api/v1/keys/{panel/{reservationId}|job/{id}|log|locks|encoder}</c>. Agent (bearer token of one encoder, no session; the PC polls, no inbound port):
/// <c>GET /keys/jobs?encoder=&amp;wait=</c> (long poll, 200 job or 204), <c>POST /keys/jobs/{id}/state</c>, <c>POST /keys/jobs/{id}/result</c>, <c>POST /keys/encoders/{id}/heartbeat</c>.
/// </summary>
public static class KeysEndpoints
{
    public static IServiceCollection AddKeysModule(this IServiceCollection services)
    {
        services.AddSingleton<IKeyStore>(sp => new SqlKeyStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<KeyCommands>();
        services.AddSingleton<KeyQueries>();
        services.AddSingleton<KeyAgentService>();
        services.AddSingleton(sp => new KeyDepartureProjector(sp.GetRequiredService<SqlPlatform>().Outbox, sp.GetRequiredService<KeyCommands>()));
        services.AddHostedService<KeyDepartureWorker>();
        return services;
    }

    private static string? Bearer(HttpContext http)
    {
        var auth = http.Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : null;
    }

    public static RouteGroupBuilder MapKeysEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + KeyCommands.OpRequestKey, async (HttpContext http, RequestKeyRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.RequestKeyAsync(http.HccCommand(KeyCommands.OpRequestKey), body, ct)));
        g.MapPost("/commands/" + KeyCommands.OpCancelKey, async (HttpContext http, CancelKeyRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.CancelKeyAsync(http.HccCommand(KeyCommands.OpCancelKey), body, ct)));
        g.MapPost("/commands/" + KeyCommands.OpReadKey, async (HttpContext http, ReadKeyRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.ReadKeyAsync(http.HccCommand(KeyCommands.OpReadKey), body, ct)));
        g.MapPost("/commands/" + KeyCommands.OpManualKey, async (HttpContext http, RecordManualKeyRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.RecordManualKeyAsync(http.HccCommand(KeyCommands.OpManualKey), body, ct)));
        g.MapPost("/commands/" + KeyCommands.OpSaveLock, async (HttpContext http, SaveKeyLockRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.SaveLockAsync(http.HccCommand(KeyCommands.OpSaveLock), body, ct)));
        g.MapPost("/commands/" + KeyCommands.OpRegisterEncoder, async (HttpContext http, RegisterKeyEncoderRequest body, KeyCommands c, CancellationToken ct) => http.HccReply(await c.RegisterEncoderAsync(http.HccCommand(KeyCommands.OpRegisterEncoder), body, ct)));

        g.MapGet("/keys/panel/{reservationId:long}", async (HttpContext http, long reservationId, KeyQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.PanelAsync(p, u, reservationId, ct));
        });
        g.MapGet("/keys/job/{id}", async (HttpContext http, string id, KeyQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.JobAsync(p, u, id, ct));
        });
        g.MapGet("/keys/log", async (HttpContext http, KeyQueries q, CancellationToken ct, string? from = null, string? to = null, string? room = null, string? booking = null, int? user = null, string? ev = null) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.LogAsync(p, u, from, to, room, booking, user, ev, ct));
        });
        g.MapGet("/keys/locks", async (HttpContext http, KeyQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.LocksAsync(p, u, ct));
        });
        g.MapGet("/keys/encoder", async (HttpContext http, KeyQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.EncoderAsync(p, u, ct));
        });

        // Agent side: bearer token per encoder, no session.
        api.MapGet("/keys/jobs", async (HttpContext http, KeyAgentService a, CancellationToken ct, string encoder, int wait = 0) =>
        {
            var token = Bearer(http);
            var until = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(wait, 0, 25));
            while (true)
            {
                var job = await a.NextJobAsync(encoder, token, ct);
                if (job is not null) return Results.Ok(job);
                if (DateTimeOffset.UtcNow >= until) return Results.NoContent();
                await Task.Delay(500, ct);
            }
        });
        api.MapPost("/keys/jobs/{id}/state", async (HttpContext http, string id, string encoder, AgentStateRequest body, KeyAgentService a, CancellationToken ct) =>
        {
            await a.SetStateAsync(encoder, Bearer(http), id, body, ct);
            return Results.Ok(new { ok = true });
        });
        api.MapPost("/keys/jobs/{id}/result", async (HttpContext http, string id, string encoder, AgentResultRequest body, KeyAgentService a, CancellationToken ct) =>
            Results.Ok(await a.ResultAsync(encoder, Bearer(http), id, body, ct)));
        api.MapPost("/keys/encoders/{id}/heartbeat", async (HttpContext http, string id, AgentHeartbeatRequest body, KeyAgentService a, CancellationToken ct) =>
        {
            await a.HeartbeatAsync(id, Bearer(http), body, ct);
            return Results.Ok(new { ok = true });
        });
        return api;
    }
}

/// <summary>Polls the outbox for <c>StayDeparted</c> every 2 s and queues the software key cancel.</summary>
public sealed class KeyDepartureWorker(KeyDepartureProjector projector, ILogger<KeyDepartureWorker> logger) : OutboxPollingWorker(logger, "Room key check-out cancel")
{
    protected override Task PollAsync(CancellationToken ct) => projector.RunOnceAsync(ct: ct);
}
