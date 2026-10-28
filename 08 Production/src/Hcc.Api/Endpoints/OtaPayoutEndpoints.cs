using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Configuration;
using Hcc.Application.Expenses;
using Hcc.Application.Groups;
using Hcc.Application.Ota;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 lane acc2-flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §7, §8). Commands <c>POST /commands/{ApplyDeposit (PERM-PAY-001, open shift),
/// SettleFolioToOta (PERM-STY-003), RecordOtaPayout (PERM-OTA-004), SetChannelItc (Owner), ReverseUdhaarPayment (Owner), SetCompanyActive (PERM-GRP-002)}</c>
/// and <c>GET /ota/payouts</c> (PERM-OTA-004). RecordOtaPayout / SetChannelItc follow the OTA module switch (DEC-057). <see cref="AddAccountingFlowsModule"/>
/// registers the services and the cash-guard settings (one <c>services.AddAccountingFlowsModule()</c> line at integration — see ACC2-FLOWS-DONE.md);
/// until then the endpoints build what they need from the platform.
/// </summary>
public static class OtaPayoutEndpoints
{
    public static IServiceCollection AddAccountingFlowsModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlFlowsStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<ICashFlowReader>(sp => sp.GetRequiredService<SqlFlowsStore>());
        services.AddSingleton<IFolioChannelSource>(sp => sp.GetRequiredService<SqlFlowsStore>());
        services.AddSingleton<IOtaPayoutStore>(sp => sp.GetRequiredService<SqlFlowsStore>());
        services.AddSingleton<IUdhaarReversalStore>(sp => sp.GetRequiredService<SqlFlowsStore>());
        services.AddSingleton<ICompanyRetireStore>(sp => sp.GetRequiredService<SqlFlowsStore>());
        services.AddSingleton<ISettingsProvider>(CashGuardSettings.Provider); // cash.receiptLimit / paymentLimit / transportLimit in Master configuration › Money and tax
        services.AddSingleton(sp => new CashGuardService(sp.GetRequiredService<ICashFlowReader>(), sp.GetService<ISettingsReader>()));
        services.AddSingleton(Deposits);
        services.AddSingleton(Payouts);
        services.AddSingleton(Reversals);
        services.AddSingleton(Retire);
        return services;
    }

    private static SqlFlowsStore Store(IServiceProvider sp) => sp.GetService<SqlFlowsStore>() ?? new SqlFlowsStore(sp.GetRequiredService<SqlPlatform>().Connections);

    private static DepositFlowsService Deposits(IServiceProvider sp) => new(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IBillingStore>(),
        sp.GetRequiredService<IAccessPolicy>(), Store(sp), sp.GetRequiredService<TimeProvider>(), sp.GetService<IAccountingStore>());

    private static OtaPayoutService Payouts(IServiceProvider sp) => new(sp.GetRequiredService<SqlPlatform>().Commands, Store(sp), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetRequiredService<TimeProvider>());

    private static UdhaarReversalService Reversals(IServiceProvider sp) => new(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IAccessPolicy>(),
        sp.GetRequiredService<IExpenseStore>(), sp.GetRequiredService<IExpenseRegisterStore>(), Store(sp), sp.GetRequiredService<TimeProvider>(), sp.GetService<IBusinessDayGuard>(),
        sp.GetService<ICashDrawerPort>());

    private static CompanyRetireService Retire(IServiceProvider sp) => new(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<ICompanyStore>(),
        sp.GetRequiredService<ICompanyCreditStore>(), Store(sp), sp.GetRequiredService<IAccessPolicy>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IAccountingStore>());

    private static T Get<T>(IServiceProvider sp, Func<IServiceProvider, T> build) where T : class => sp.GetService<T>() ?? build(sp);

    public static RouteGroupBuilder MapOtaPayoutEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var ota = g.MapGroup(string.Empty).RequireHccModule(ConfigModuleIds.Ota); // DEC-057: refused while OTA channels is off; the read stays
        BillingEndpoints.Command<ApplyDepositRequest, FolioFlowResult>(g, DepositFlowsService.OpApplyDeposit, (sp, c, k, r, ct) => Get(sp, Deposits).ApplyDepositAsync(c, k, r, ct));
        BillingEndpoints.Command<SettleFolioToOtaRequest, FolioFlowResult>(g, DepositFlowsService.OpSettleToOta, (sp, c, k, r, ct) => Get(sp, Deposits).SettleToOtaAsync(c, k, r, ct));
        BillingEndpoints.Command<RecordOtaPayoutRequest, OtaPayoutResult>(ota, OtaPayoutService.OpRecord,
            (sp, c, k, r, ct) => Get(sp, Payouts).RecordAsync(new(c.PropertyId, c.PrincipalId, OtaPayoutService.OpRecord, k), r, ct));
        BillingEndpoints.Command<SetChannelItcRequest, OtaPayoutResult>(ota, OtaPayoutService.OpSetItc,
            (sp, c, k, r, ct) => Get(sp, Payouts).SetItcAsync(new(c.PropertyId, c.PrincipalId, OtaPayoutService.OpSetItc, k), r, ct));
        BillingEndpoints.Command<ReverseUdhaarPaymentRequest, UdhaarReversalResult>(g, UdhaarReversalService.Op,
            (sp, c, k, r, ct) => Get(sp, Reversals).ReverseAsync(new(c.PropertyId, c.PrincipalId, UdhaarReversalService.Op, k), r, ct));
        BillingEndpoints.Command<SetCompanyActiveRequest, CompanyActiveResult>(g, CompanyRetireService.Op,
            (sp, c, k, r, ct) => Get(sp, Retire).SetActiveAsync(new(c.PropertyId, c.PrincipalId, CompanyRetireService.Op, k), r, ct));
        g.MapGet("/ota/payouts", async (HttpContext http, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await Get(http.RequestServices, Payouts).ListAsync(property, principal, ct).ConfigureAwait(false));
        });
        return api;
    }
}
