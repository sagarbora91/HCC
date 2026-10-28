using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 · lane acc2-parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8): <c>POST /commands/BackfillUdhaarCredits</c> (Owner,
/// PERM-ACC-003; idempotent) publishes UdhaarCreditRaised for the Udhaar credits raised before migration 4215. The month rows of the
/// aggregated opening read (<see cref="SqlLedgerMonthBalanceStore"/>) are used by <see cref="AccountingReportEndpoints"/>.
/// Nothing to register: the endpoint builds the service from the platform (an <see cref="UdhaarCreditBackfill"/> or
/// <see cref="IUdhaarBackfillStore"/> registered by the composition wins).
/// </summary>
public static class AccountingParityEndpoints
{
    private static UdhaarCreditBackfill Backfill(IServiceProvider sp)
        => sp.GetService<UdhaarCreditBackfill>() ?? new UdhaarCreditBackfill(sp.GetRequiredService<SqlPlatform>().Commands,
            sp.GetService<IUdhaarBackfillStore>() ?? new SqlUdhaarBackfillStore(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>());

    public static RouteGroupBuilder MapAccountingParityEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<BackfillUdhaarCreditsRequest, BackfillUdhaarCreditsResult>(g, UdhaarCreditBackfill.Op, (sp, c, k, r, ct) => Backfill(sp).RunAsync(c, k, r, ct));
        return api;
    }
}
