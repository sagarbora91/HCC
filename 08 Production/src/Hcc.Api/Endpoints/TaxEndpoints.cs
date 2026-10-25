using Hcc.Application.Tax;
using Hcc.Contracts.Tax;
using Hcc.Domain.Dates;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Tax;

namespace Hcc.Api.Endpoints;

/// <summary>
/// GST master (DEC-069): <c>POST /api/v1/commands/{SetGstSetting|WithdrawGstSetting}</c> (Owner only) and
/// <c>GET /api/v1/tax/gst?date=YYYY-MM-DD</c> (Owner, Tech admin read-only). Every route requires a session.
/// </summary>
public static class TaxEndpoints
{
    /// <summary>DI for the GST master (idempotent: billing also calls it, since every posting needs the resolver).</summary>
    public static IServiceCollection AddTaxModule(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IGstRateResolver))) return services;
        services.AddSingleton(sp => new SqlGstMasterStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IGstMasterStore>(sp => sp.GetRequiredService<SqlGstMasterStore>());
        services.AddSingleton<IGstChargeSnapshots>(sp => sp.GetRequiredService<SqlGstMasterStore>());
        services.AddSingleton<GstRateResolver>();
        services.AddSingleton<IGstRateResolver>(sp => sp.GetRequiredService<GstRateResolver>());
        services.AddSingleton<GstMasterService>();
        return services;
    }

    public static RouteGroupBuilder MapTaxEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + GstMasterService.OpSet, async (HttpContext http, SetGstSettingRequest body, GstMasterService svc, CancellationToken ct) =>
            http.HccReply(await svc.SetAsync(http.HccCommand(GstMasterService.OpSet), body, ct)));
        g.MapPost("/commands/" + GstMasterService.OpWithdraw, async (HttpContext http, WithdrawGstSettingRequest body, GstMasterService svc, CancellationToken ct) =>
            http.HccReply(await svc.WithdrawAsync(http.HccCommand(GstMasterService.OpWithdraw), body, ct)));
        g.MapGet("/tax/gst", async (HttpContext http, string? date, GstMasterService svc, TimeProvider clock, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            var d = string.IsNullOrWhiteSpace(date) ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) : StayDates.Parse(date, "date");
            return Results.Ok(await svc.GetAsync(property, principal, d, ct));
        });
        return api;
    }
}
