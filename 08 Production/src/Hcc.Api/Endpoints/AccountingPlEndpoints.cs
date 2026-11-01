using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Import;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-3 lane acc3-pl (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2, §9: PERM-ACC-003 view as the trial balance):
/// <c>GET /accounting/pl?from&amp;to</c>, <c>GET /accounting/balance-sheet?asOf</c>, <c>GET /accounting/cash-ratio?fy</c>.
/// Queries built per request over the accounting store (no service registration).
/// </summary>
public static class AccountingPlEndpoints
{
    private static PlQueries Queries(IServiceProvider sp) => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>());

    public static RouteGroupBuilder MapAccountingPlEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/accounting/pl", async (DateOnly from, DateOnly to, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).PlAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/balance-sheet", async (DateOnly asOf, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).BalanceSheetAsync(http.HccCaller(), asOf, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/cash-ratio", async (string fy, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).CashRatioAsync(http.HccCaller(), fy, ct).ConfigureAwait(false)));
        return api;
    }
}
