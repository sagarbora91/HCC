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
        services.AddSingleton(sp => new RevenueByCategoryQueries(sp.GetRequiredService<Hcc.Application.Closing.IReportSource>(), sp.GetRequiredService<IRevenueCategorySource>(),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), new Hcc.Application.Accounting.AccountingReportQueries(sp.GetRequiredService<Hcc.Application.Accounting.IAccountingStore>(),
                sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetService<Hcc.Application.Import.IOpeningBalanceSource>(), sp.GetService<Hcc.Application.Accounting.IReportOpeningSource>(),
                sp.GetService<Hcc.Application.Accounting.IReportPartyNames>(),
                sp.GetService<Hcc.Application.Accounting.ILedgerMonthBalanceStore>() ?? new Hcc.Infrastructure.SqlServer.Accounting.SqlLedgerMonthBalanceStore(sp.GetRequiredService<SqlPlatform>().Connections),
                sp.GetService<TimeProvider>()))); // Block C closes to the six-column trial balance
        services.AddSingleton<IDigestDraftStore>(sp => new SqlDigestDraftStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<DigestDraftQueries>();
        services.AddSingleton(sp => new DigestDraftProjector(sp.GetRequiredService<SqlPlatform>().Outbox, sp.GetRequiredService<SqlPlatform>().Connections,
            sp.GetRequiredService<Hcc.Application.Closing.IReportSource>(), sp.GetRequiredService<IRevenueCategorySource>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IMeasDigestSource>())); // F12 MEAS: the Measurement block (registered in HccComposition F12-DIGEST)
        services.AddHostedService<DigestDraftWorker>();
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

/// <summary>Polls the outbox for BusinessDayClosed events and writes the held digest drafts (never sends).</summary>
public sealed class DigestDraftWorker(DigestDraftProjector projector, ILogger<DigestDraftWorker> logger) : OutboxPollingWorker(logger, "Owner digest draft projection")
{
    protected override Task PollAsync(CancellationToken ct) => projector.RunOnceAsync(ct: ct);
}
