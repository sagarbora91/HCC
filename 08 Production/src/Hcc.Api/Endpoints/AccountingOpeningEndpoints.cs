using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-1 opening balances entered by hand (02 Planning/agents/ACC1-POSTING-CONTRACT.md §5, §7; DEC-139 / DEC-148):
/// <c>POST /commands/{SaveOpeningBalances|ApproveOpeningBalances|ReopenOpeningBalances}</c> (PERM-ACC-008 enter: Owner, Manager,
/// Accountant; PERM-ACC-009 approve / reopen: Owner) and <c>GET /accounting/opening</c> (PERM-ACC-001).
/// The commands run on the platform runner: the opening entry posts on no business date (no late-write barrier).
/// <see cref="AddAccountingOpeningModule"/> registers the store so that <see cref="AccountingService"/> refuses a period lock while the
/// opening balances are pending and <see cref="AccountingQueries"/> adds the approved entry to the cumulative trial balance; until the
/// composition calls it, the endpoints build what they need from the platform (see ACC1-OPENING-DONE.md "Integration needed").
/// </summary>
public static class AccountingOpeningEndpoints
{
    public static IServiceCollection AddAccountingOpeningModule(this IServiceCollection services)
    {
        services.AddSingleton<IOpeningEntryStore>(sp => new SqlOpeningStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(sp => new OpeningBalanceService(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IOpeningEntryStore>(),
            sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new OpeningBalanceQueries(sp.GetRequiredService<IOpeningEntryStore>(), sp.GetRequiredService<IAccountingStore>(),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>()));
        services.AddSingleton<IReportOpeningSource>(sp => new OpeningReportSource(sp.GetRequiredService<IOpeningEntryStore>())); // ACC-1 integration: report opening
        return services;
    }

    private static IOpeningEntryStore Store(IServiceProvider sp)
        => sp.GetService<IOpeningEntryStore>() ?? new SqlOpeningStore(sp.GetRequiredService<SqlPlatform>().Connections);

    private static OpeningBalanceService Service(IServiceProvider sp)
        => sp.GetService<OpeningBalanceService>() ?? new OpeningBalanceService(sp.GetRequiredService<SqlPlatform>().Commands, Store(sp),
            sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>());

    private static OpeningBalanceQueries Queries(IServiceProvider sp)
        => sp.GetService<OpeningBalanceQueries>() ?? new OpeningBalanceQueries(Store(sp), sp.GetRequiredService<IAccountingStore>(),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>());

    public static RouteGroupBuilder MapAccountingOpeningEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<SaveOpeningBalancesRequest, OpeningBalancesResult>(g, OpeningBalanceService.OpSave, (sp, c, k, r, ct) => Service(sp).SaveAsync(c, k, r, ct));
        BillingEndpoints.Command<ApproveOpeningBalancesRequest, OpeningBalancesResult>(g, OpeningBalanceService.OpApprove, (sp, c, k, r, ct) => Service(sp).ApproveAsync(c, k, r, ct));
        BillingEndpoints.Command<ReopenOpeningBalancesRequest, OpeningBalancesResult>(g, OpeningBalanceService.OpReopen, (sp, c, k, r, ct) => Service(sp).ReopenAsync(c, k, r, ct));
        g.MapGet("/accounting/opening", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).GetAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        return api;
    }
}
