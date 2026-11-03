using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

using Hcc.Domain.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 manual vouchers and loan party ledgers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2, §9):
/// <c>POST /commands/{SaveManualVoucher|DecideManualVoucher|VoidManualVoucher}</c> (PERM-ACC-010 enter: Owner approved at once, Accountant
/// pending; PERM-ACC-011 decide / void: Owner), <c>POST /commands/AddLoan</c> (PERM-ACC-007, Owner) and <c>GET /accounting/manual</c>
/// (PERM-ACC-001). An approved voucher publishes ManualVoucherApproved, booked by the accounting consumer as <c>manual:&lt;id&gt;</c>.
/// The services are built on demand from the platform (one composition line: <see cref="MapAccountingManualEndpoints"/>);
/// <see cref="AddAccountingManualModule"/> registers them as singletons when the composition wants it.
/// </summary>
public static class AccountingManualEndpoints
{
    public static IServiceCollection AddAccountingManualModule(this IServiceCollection services)
    {
        services.AddSingleton<IManualVoucherStore>(sp => new SqlManualVoucherStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(sp => new ManualVoucherService(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IManualVoucherStore>(),
            sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<CashGuardService>()));
        // ACC-2 integration: the GST / statutory payments (acc2-gst) and the TDS remittance (acc2-tds) save their template vouchers here.
        services.AddSingleton(sp => new ManualVoucherPorts(sp.GetRequiredService<ManualVoucherService>(), sp.GetRequiredService<IManualVoucherStore>()));
        services.AddSingleton<IGstManualVouchers>(sp => sp.GetRequiredService<ManualVoucherPorts>());
        services.AddSingleton<ITdsRemittancePoster>(sp => sp.GetRequiredService<ManualVoucherPorts>());
        services.AddSingleton(sp => new ManualVoucherQueries(sp.GetRequiredService<IManualVoucherStore>(), sp.GetRequiredService<IAccountingStore>(),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>()));
        return services;
    }

    private static IManualVoucherStore Store(IServiceProvider sp)
        => sp.GetService<IManualVoucherStore>() ?? new SqlManualVoucherStore(sp.GetRequiredService<SqlPlatform>().Connections);

    private static ManualVoucherService Service(IServiceProvider sp)
        => sp.GetService<ManualVoucherService>() ?? new ManualVoucherService(sp.GetRequiredService<SqlPlatform>().Commands, Store(sp),
            sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<CashGuardService>());

    private static ManualVoucherQueries Queries(IServiceProvider sp)
        => sp.GetService<ManualVoucherQueries>() ?? new ManualVoucherQueries(Store(sp), sp.GetRequiredService<IAccountingStore>(),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>());

    public static RouteGroupBuilder MapAccountingManualEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Vouchers); // ADM-1 (DEC-214): refused while the sub-function is off; reads stay on g
        BillingEndpoints.Command<SaveManualVoucherRequest, ManualVoucherResult>(cmd, ManualVoucherService.OpSave, (sp, c, k, r, ct) => Service(sp).SaveAsync(c, k, r, ct));
        BillingEndpoints.Command<DecideManualVoucherRequest, ManualVoucherResult>(cmd, ManualVoucherService.OpDecide, (sp, c, k, r, ct) => Service(sp).DecideAsync(c, k, r, ct));
        BillingEndpoints.Command<VoidManualVoucherRequest, ManualVoucherResult>(cmd, ManualVoucherService.OpVoid, (sp, c, k, r, ct) => Service(sp).VoidAsync(c, k, r, ct));
        BillingEndpoints.Command<AddLoanRequest, LoanResult>(cmd, ManualVoucherService.OpAddLoan, (sp, c, k, r, ct) => Service(sp).AddLoanAsync(c, k, r, ct));
        g.MapGet("/accounting/manual", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).GetAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        return api;
    }
}
