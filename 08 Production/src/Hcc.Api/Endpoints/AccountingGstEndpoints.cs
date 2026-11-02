using Hcc.Application.Accounting;
using Hcc.Application.Compliance;
using Hcc.Application.Expenses;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Compliance;
using Hcc.Infrastructure.SqlServer.Expenses;

using Hcc.Domain.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 acc2-gst (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 switch, §6, §9): <c>POST /commands/{PostGstSetoff | RecordGstPayment |
/// RecordStatutoryRemittance | RequestAccountingMonthLock}</c> (PERM-ACC-013, Owner + Accountant), <c>{LockAccountingMonth |
/// UnlockAccountingMonth}</c> (PERM-ACC-012, Owner), <c>SetGstOnAdvances</c> (PERM-CFG-003, Owner) and <c>GET /accounting/gst/month?month=</c>,
/// <c>GET /accounting/gst/locks</c> (PERM-ACC-001). Commands run on the platform runner. The payments go through the manual-voucher API of
/// acc2-manual (<see cref="IGstManualVouchers"/>): until the integrator registers it, <see cref="UnwiredGstManualVouchers"/> answers "not
/// configured". <see cref="AddAccountingGstModule"/> registers the service; the endpoints build it from the platform when it is not registered.
/// </summary>
public static class AccountingGstEndpoints
{
    public static IServiceCollection AddAccountingGstModule(this IServiceCollection services)
    {
        services.AddSingleton<IGstMonthStore>(sp => new SqlGstMonthStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(Build);
        return services;
    }

    private static GstMonthService Build(IServiceProvider sp)
    {
        var platform = sp.GetRequiredService<SqlPlatform>();
        var expenses = new ExpenseMonthLockAdapter(new SqlExpenseStore(platform.Connections, platform.DocumentObjects), new SqlExpenseRegisterStore(platform.Connections));
        return new GstMonthService(platform.Commands, sp.GetRequiredService<IAccountingStore>(), sp.GetService<IGstMonthStore>() ?? new SqlGstMonthStore(platform.Connections),
            sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IGstManualVouchers>(), expenses,
            new ComplianceCardHook(new SqlComplianceStore(platform.Connections, platform.DocumentObjects)), sp.GetService<IOpeningEntryStore>(),
            sp.GetService<Hcc.Application.Payroll.IPayrollStore>() is { } payroll ? new PayRunMonthCheck(payroll) : null); // ACC-2 integration (ruling 6)
    }

    private static GstMonthService Service(IServiceProvider sp) => sp.GetService<GstMonthService>() ?? Build(sp);

    public static RouteGroupBuilder MapAccountingGstEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Gst); // ADM-1 (DEC-214): refused while the sub-function is off; reads stay on g
        BillingEndpoints.Command<PostGstSetoffRequest, GstSetoffResult>(cmd, GstMonthService.OpSetoff, (sp, c, k, r, ct) => Service(sp).SetoffAsync(c, k, r, ct));
        BillingEndpoints.Command<RecordGstPaymentRequest, GstRemittanceResult>(cmd, GstMonthService.OpPay, (sp, c, k, r, ct) => Service(sp).PayAsync(c, k, r, ct));
        BillingEndpoints.Command<RecordStatutoryRemittanceRequest, GstRemittanceResult>(cmd, GstMonthService.OpRemit, (sp, c, k, r, ct) => Service(sp).RemitAsync(c, k, r, ct));
        BillingEndpoints.Command<LockAccountingMonthRequest, AccountingMonthLockResult>(cmd, GstMonthService.OpLock, (sp, c, k, r, ct) => Service(sp).LockMonthAsync(c, k, r, ct));
        BillingEndpoints.Command<LockAccountingMonthRequest, AccountingMonthLockResult>(cmd, GstMonthService.OpRequest, (sp, c, k, r, ct) => Service(sp).RequestMonthLockAsync(c, k, r, ct));
        BillingEndpoints.Command<UnlockAccountingMonthRequest, AccountingMonthLockResult>(cmd, GstMonthService.OpUnlock, (sp, c, k, r, ct) => Service(sp).UnlockMonthAsync(c, k, r, ct));
        BillingEndpoints.Command<SetGstOnAdvancesRequest, GstOnAdvancesResult>(cmd, GstMonthService.OpAdvances, (sp, c, k, r, ct) => Service(sp).SetGstOnAdvancesAsync(c, k, r, ct));
        g.MapGet("/accounting/gst/month", async (HttpContext http, string? month, CancellationToken ct)
            => Results.Ok(await Service(http.RequestServices).MonthAsync(http.HccCaller(), month, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/gst/locks", async (HttpContext http, CancellationToken ct)
            => Results.Ok(await Service(http.RequestServices).LocksAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        return api;
    }
}
