using Hcc.Application.Reporting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Api.Endpoints;

/// <summary>F4-T2: GET /api/v1/reports/revenue-by-category-tender?from&amp;to&amp;basis (Owner, Manager, Accountant) and GET /api/v1/digest/drafts?date (Owner; held drafts, never sent).</summary>
public static class RevenueDigestEndpoints
{
    public static IServiceCollection AddRevenueDigestModule(this IServiceCollection services)
    {
        services.AddSingleton<IRevenueCategorySource>(sp => new SqlRevenueCategorySource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<RevenueByCategoryQueries>();
        services.AddSingleton<IDigestDraftStore>(sp => new SqlDigestDraftStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<DigestDraftQueries>();
        return services;
    }

    public static RouteGroupBuilder MapRevenueDigestEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/reports/revenue-by-category-tender", async (HttpContext http, RevenueByCategoryQueries q, DateOnly from, DateOnly? to, string? basis, CancellationToken ct) =>
            Results.Ok(await q.GetAsync(http.HccCaller(), from, to ?? from, basis, ct).ConfigureAwait(false)));
        g.MapGet("/digest/drafts", async (HttpContext http, DigestDraftQueries q, DateOnly date, CancellationToken ct) =>
            Results.Ok(await q.ListAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        return api;
    }
}
