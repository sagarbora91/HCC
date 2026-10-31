using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 · lane acc2-tds (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §5, §9; DEC-146):
/// <c>POST /commands/{SetTdsSettings|SetTdsRate}</c> (Owner, PERM-CFG-003), <c>POST /commands/RecordTdsRemittance</c> (PERM-ACC-013 Owner /
/// Accountant), <c>GET /accounting/tds</c>, <c>GET /accounting/tds/deductees?period=</c>, <c>GET /accounting/tds/receivable?fy=</c> (PERM-ACC-013).
/// The service is built from the platform per request (no composition line besides the map call); an <see cref="ITdsRemittancePoster"/>
/// registered by the manual-voucher lane is picked up when present (ACC2-TDS-DONE.md "Integration needed").
/// </summary>
public static class AccountingTdsEndpoints
{
    /// <summary>ACC-2 integration: TdsService as a singleton — ExpenseCommands holds TDS back at approval (TdsCalculator.ForBill), the remittance posts
    /// its tds-remittance voucher through the manual lane (ITdsRemittancePoster) and marks the month's TDS compliance card (ruling 9).</summary>
    public static IServiceCollection AddAccountingTdsModule(this IServiceCollection services)
    {
        services.AddSingleton<ITdsStore>(sp => new SqlTdsStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(Build);
        return services;
    }

    private static TdsService Build(IServiceProvider sp)
    {
        var platform = sp.GetRequiredService<SqlPlatform>();
        return new TdsService(platform.Commands, sp.GetService<ITdsStore>() ?? new SqlTdsStore(platform.Connections),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<ITdsRemittancePoster>(),
            new ComplianceCardHook(new Hcc.Infrastructure.SqlServer.Compliance.SqlComplianceStore(platform.Connections)));
    }

    private static TdsService Service(IServiceProvider sp) => sp.GetService<TdsService>() ?? Build(sp);

    public static RouteGroupBuilder MapAccountingTdsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<SetTdsSettingsRequest, TdsSettingsResult>(g, TdsService.OpSettings, (sp, c, k, r, ct) => Service(sp).SetSettingsAsync(c, k, r, ct));
        BillingEndpoints.Command<SetTdsRateRequest, TdsRateChangeResult>(g, TdsService.OpRate, (sp, c, k, r, ct) => Service(sp).SetRateAsync(c, k, r, ct));
        BillingEndpoints.Command<RecordTdsRemittanceRequest, TdsRemittanceResult>(g, TdsService.OpRemit, (sp, c, k, r, ct) => Service(sp).RecordRemittanceAsync(c, k, r, ct));
        g.MapGet("/accounting/tds", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Service(http.RequestServices).GetAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/tds/deductees", async (HttpContext http, string? period, CancellationToken ct)
            => Results.Ok(await Service(http.RequestServices).DeducteesAsync(http.HccCaller(), period, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/tds/receivable", async (HttpContext http, string? fy, CancellationToken ct)
            => Results.Ok(await Service(http.RequestServices).ReceivableAsync(http.HccCaller(), fy, ct).ConfigureAwait(false)));
        return api;
    }
}
