using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Import;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-1 accounting reports (lane acc1-statements; contract §6; PERM-ACC-001): <c>GET /accounting/statement?account&amp;from&amp;to</c>
/// (ledger code, control or party ledger <c>&lt;code&gt;/&lt;party&gt;</c>), <c>GET /accounting/day-book?from&amp;to</c>, and the six-column
/// <c>GET /accounting/trial-balance?from&amp;to&amp;detail</c> through <see cref="TrialBalanceAsync"/>. That path is already mapped by
/// <see cref="AccountingEndpoints"/> (<c>?date&amp;cumulative</c>, a different query on the same route): ASP.NET Core would report two
/// endpoints for one request, so the existing handler dispatches a request carrying <c>from</c> to <see cref="TrialBalanceAsync"/>
/// (ACC1-STATEMENTS-DONE.md, Integration needed 1). The queries are built per request over the accounting store, so no service registration is needed;
/// the optional opening-entry and party-name ports are used when composed (<see cref="IReportOpeningSource"/>, <see cref="IReportPartyNames"/>).
/// </summary>
public static class AccountingReportEndpoints
{
    private static AccountingReportQueries Queries(IServiceProvider sp) => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>(),
        // ACC-2 parity (contract §8): the aggregated opening read over acc.ledger_month_balance (migration 4257); any range
        sp.GetService<ILedgerMonthBalanceStore>() ?? new Hcc.Infrastructure.SqlServer.Accounting.SqlLedgerMonthBalanceStore(sp.GetRequiredService<Hcc.Infrastructure.SqlServer.SqlPlatform>().Connections),
        sp.GetService<TimeProvider>());

    public static RouteGroupBuilder MapAccountingReportEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/accounting/statement", async (string account, DateOnly from, DateOnly to, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).StatementAsync(http.HccCaller(), account, from, to, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/day-book", async (DateOnly from, DateOnly to, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).DayBookAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false)));
        return api;
    }

    /// <summary>The six-column trial balance answer for <c>GET /accounting/trial-balance?from&amp;to&amp;detail</c> (contract §6).</summary>
    public static async Task<IResult> TrialBalanceAsync(HttpContext http, DateOnly from, DateOnly to, bool detail, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Results.Ok(await Queries(http.RequestServices).TrialBalanceAsync(http.HccCaller(), from, to, detail, ct).ConfigureAwait(false));
    }
}
