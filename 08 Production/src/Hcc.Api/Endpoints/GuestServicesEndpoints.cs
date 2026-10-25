using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.GuestServices;
using Hcc.Contracts.GuestServices;
using Hcc.Infrastructure.SqlServer.GuestServices;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Guest-services endpoints (module 18): <c>POST /api/v1/commands/{LogServiceRequest|AssignServiceRequest|StartServiceRequest|CompleteServiceRequest|
/// CancelServiceRequest|LogComplaint|UpdateComplaint|ResolveComplaint|RecordComplaintFollowUp|OfferRecoveryDiscount|LogLostItem|ClaimLostItem|
/// HandOverLostItem|DisposeLostItem|SaveGuestServiceSettings}</c> and the <c>GET /api/v1/guest-services/*</c> queries. All require a session.
/// </summary>
public static class GuestServicesEndpoints
{
    public static IServiceCollection AddGuestServicesModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlGuestServicesStore(sp.GetRequiredService<Hcc.Infrastructure.SqlServer.SqlPlatform>().Connections));
        services.AddSingleton<IGuestServicesStore>(sp => sp.GetRequiredService<SqlGuestServicesStore>());
        services.AddSingleton<IGuestServicesReadStore>(sp => sp.GetRequiredService<SqlGuestServicesStore>());
        services.AddSingleton<GuestServicesCommands>(sp => new GuestServicesCommands(sp.GetRequiredService<ICommandRunner>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(),
            sp.GetRequiredService<IGuestServicesStore>(), sp.GetRequiredService<BillingService>(), sp.GetRequiredService<IBillingStore>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<GuestServicesQueries>();
        return services;
    }

    private static void Cmd<TRequest, TResult>(RouteGroupBuilder g, string op, Func<GuestServicesCommands, CommandContext, TRequest, CancellationToken, Task<CommandResult<TResult>>> run) =>
        g.MapPost("/commands/" + op, async (HttpContext http, TRequest body, GuestServicesCommands cmds, CancellationToken ct) =>
            http.HccReply(await run(cmds, http.HccCommand(op), body, ct).ConfigureAwait(false)));

    public static RouteGroupBuilder MapGuestServicesEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        Cmd<LogServiceRequestRequest, ServiceRequestResult>(g, GuestServicesCommands.OpLogRequest, (c, x, r, ct) => c.LogRequestAsync(x, r, ct));
        Cmd<AssignServiceRequestRequest, ServiceRequestResult>(g, GuestServicesCommands.OpAssignRequest, (c, x, r, ct) => c.AssignRequestAsync(x, r, ct));
        Cmd<StartServiceRequestRequest, ServiceRequestResult>(g, GuestServicesCommands.OpStartRequest, (c, x, r, ct) => c.StartRequestAsync(x, r, ct));
        Cmd<CompleteServiceRequestRequest, ServiceRequestResult>(g, GuestServicesCommands.OpCompleteRequest, (c, x, r, ct) => c.CompleteRequestAsync(x, r, ct));
        Cmd<CancelServiceRequestRequest, ServiceRequestResult>(g, GuestServicesCommands.OpCancelRequest, (c, x, r, ct) => c.CancelRequestAsync(x, r, ct));
        Cmd<LogComplaintRequest, ComplaintResult>(g, GuestServicesCommands.OpLogComplaint, (c, x, r, ct) => c.LogComplaintAsync(x, r, ct));
        Cmd<UpdateComplaintRequest, ComplaintResult>(g, GuestServicesCommands.OpUpdateComplaint, (c, x, r, ct) => c.UpdateComplaintAsync(x, r, ct));
        Cmd<ResolveComplaintRequest, ComplaintResult>(g, GuestServicesCommands.OpResolveComplaint, (c, x, r, ct) => c.ResolveComplaintAsync(x, r, ct));
        Cmd<RecordComplaintFollowUpRequest, ComplaintResult>(g, GuestServicesCommands.OpFollowUp, (c, x, r, ct) => c.RecordFollowUpAsync(x, r, ct));
        Cmd<OfferRecoveryDiscountRequest, RecoveryResult>(g, GuestServicesCommands.OpOfferRecovery, (c, x, r, ct) => c.OfferRecoveryDiscountAsync(x, r, ct));
        Cmd<LogLostItemRequest, LostItemResult>(g, GuestServicesCommands.OpLogItem, (c, x, r, ct) => c.LogItemAsync(x, r, ct));
        Cmd<ClaimLostItemRequest, LostItemResult>(g, GuestServicesCommands.OpClaimItem, (c, x, r, ct) => c.ClaimItemAsync(x, r, ct));
        Cmd<HandOverLostItemRequest, LostItemResult>(g, GuestServicesCommands.OpHandOverItem, (c, x, r, ct) => c.HandOverItemAsync(x, r, ct));
        Cmd<DisposeLostItemRequest, LostItemResult>(g, GuestServicesCommands.OpDisposeItem, (c, x, r, ct) => c.DisposeItemAsync(x, r, ct));
        Cmd<SaveGuestServiceSettingsRequest, GuestServiceSettingsView>(g, GuestServicesCommands.OpSaveSettings, (c, x, r, ct) => c.SaveSettingsAsync(x, r, ct));

        var q = g.MapGroup("/guest-services");
        q.MapGet("/requests", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct, int page = 1, int pageSize = 50, bool openOnly = false) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.RequestsAsync(p, u, page, pageSize, openOnly, ct).ConfigureAwait(false));
        });
        q.MapGet("/requests/{id:long}", async (long id, HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.RequestAsync(p, u, id, ct).ConfigureAwait(false));
        });
        q.MapGet("/complaints", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct, int page = 1, int pageSize = 50, bool openOnly = false) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.ComplaintsAsync(p, u, page, pageSize, openOnly, ct).ConfigureAwait(false));
        });
        q.MapGet("/complaints/{id:long}", async (long id, HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.ComplaintAsync(p, u, id, ct).ConfigureAwait(false));
        });
        q.MapGet("/lost-items", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.ItemsAsync(p, u, page, pageSize, ct).ConfigureAwait(false));
        });
        q.MapGet("/settings", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.SettingsAsync(p, u, ct).ConfigureAwait(false));
        });
        q.MapGet("/history/{kind}/{id:long}", async (string kind, long id, HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.HistoryAsync(p, u, kind, id, ct).ConfigureAwait(false));
        });
        q.MapGet("/reports/response", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct, string by = "type") =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.ResponseReportAsync(p, u, by == "dept", ct).ConfigureAwait(false));
        });
        q.MapGet("/reports/complaints", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.ComplaintReportAsync(p, u, ct).ConfigureAwait(false));
        });
        q.MapGet("/reports/lost-found", async (HttpContext http, GuestServicesQueries qs, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await qs.LostFoundSummaryAsync(p, u, ct).ConfigureAwait(false));
        });
        return api;
    }
}
