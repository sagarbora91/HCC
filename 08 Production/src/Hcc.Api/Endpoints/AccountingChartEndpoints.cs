using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-1 chart of accounts as an Owner-only master (posting contract §1, §2, §7): <c>POST /commands/{UpsertLedger|RetireLedger|UpsertLedgerGroup}</c>
/// (PERM-ACC-007, Owner) and <c>GET /accounting/chart/master</c> (groups, ledgers, problems, canEdit) + <c>/accounting/chart/history</c>
/// (PERM-ACC-001). The services are built from the composed platform parts (no extra DI registration, so the composition root keeps one
/// append-only line per ACC-1 lane).
/// </summary>
public static class AccountingChartEndpoints
{
    private static SqlChartStore Store(IServiceProvider sp) => new(sp.GetRequiredService<SqlPlatform>().Connections);

    private static ChartService Service(IServiceProvider sp) => new(sp.GetRequiredService<ICommandRunner>(), Store(sp), sp.GetRequiredService<IAccountingStore>(),
        sp.GetRequiredService<IAccessPolicy>(), sp.GetRequiredService<TimeProvider>());

    private static ChartQueries Queries(IServiceProvider sp) => new(Store(sp), sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>());

    public static RouteGroupBuilder MapAccountingChartEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<UpsertLedgerRequest, LedgerChangeResult>(g, ChartService.OpUpsertLedger, (sp, c, k, r, ct) => Service(sp).UpsertLedgerAsync(c, k, r, ct));
        BillingEndpoints.Command<RetireLedgerRequest, LedgerChangeResult>(g, ChartService.OpRetireLedger, (sp, c, k, r, ct) => Service(sp).RetireLedgerAsync(c, k, r, ct));
        BillingEndpoints.Command<UpsertLedgerGroupRequest, LedgerGroupResult>(g, ChartService.OpUpsertGroup, (sp, c, k, r, ct) => Service(sp).UpsertGroupAsync(c, k, r, ct));
        g.MapGet("/accounting/chart/master", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).MasterAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/chart/history", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).HistoryAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        return api;
    }
}
