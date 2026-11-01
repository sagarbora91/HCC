using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Import;
using Hcc.Application.Invoices;
using Hcc.Contracts.Invoices;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-3 acc3-ageing (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §3): <c>GET /accounting/ageing?asOf&amp;side=receivable|payable</c> and
/// <c>GET /accounting/gst/summary?month=YYYY-MM</c>, permission as the trial balance. Queries are built per request (no registration needed);
/// the GSTR-1 port runs <see cref="InvoiceQueries.Gstr1Async"/> as the caller.
/// </summary>
public static class AccountingAgeingEndpoints
{
    private sealed class Gstr1Port(InvoiceQueries q) : IGstr1Source
    {
        public Task<Gstr1View> GetAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default) => q.Gstr1Async(caller.PropertyId, caller.PrincipalId, from, to, ct);
    }

    private static AgeingQueries Ageing(IServiceProvider sp) => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>(),
        sp.GetService<IAgeingAllocationSource>() ?? new SqlAgeingAllocationSource(sp.GetRequiredService<SqlPlatform>().Connections));

    private static GstSummaryQueries Gst(IServiceProvider sp) => new(sp.GetRequiredService<IAccountingStore>(),
        sp.GetService<IGstMonthStore>() ?? new SqlGstMonthStore(sp.GetRequiredService<SqlPlatform>().Connections), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IGstr1Source>() ?? new Gstr1Port(sp.GetRequiredService<InvoiceQueries>()));

    public static RouteGroupBuilder MapAccountingAgeingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/accounting/ageing", async (DateOnly asOf, string? side, HttpContext http, CancellationToken ct)
            => Results.Ok(await Ageing(http.RequestServices).AgeingAsync(http.HccCaller(), asOf, side, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/gst/summary", async (string? month, HttpContext http, CancellationToken ct)
            => Results.Ok(await Gst(http.RequestServices).SummaryAsync(http.HccCaller(), month, ct).ConfigureAwait(false)));
        return api;
    }
}
