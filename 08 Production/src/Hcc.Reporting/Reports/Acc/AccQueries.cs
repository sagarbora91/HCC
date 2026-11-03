// F7-R6 · the ACC-1 / ACC-3 queries the accounting reports wrap, built the way the accounting endpoints build them per request (no new figures, same stores and ports),
// plus the year-lock PROVISIONAL test (ACC4-CONTRACT §1.2) the exports use. The Tally queries are built WITHOUT the file log and command runner: a report run records no export.
using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Import;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Reporting.Reports.Acc;

public sealed class AccQueries(IServiceProvider sp)
{
    private SqlPlatform Platform => sp.GetRequiredService<SqlPlatform>();

    public AccountingReportQueries Reports() => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>(),
        sp.GetService<ILedgerMonthBalanceStore>() ?? new SqlLedgerMonthBalanceStore(Platform.Connections), sp.GetService<TimeProvider>());

    public PlQueries Pl() => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>());

    public AgeingQueries Ageing() => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>(),
        sp.GetService<IAgeingAllocationSource>() ?? new SqlAgeingAllocationSource(Platform.Connections));

    public TallyQueries Tally() => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(), sp.GetService<IReportPartyNames>(),
        sp.GetService<ITallyPartyGstin>() ?? new SqlTallyStore(Platform.Connections), sp.GetService<IReportOpeningSource>(), sp.GetService<IOpeningBalanceSource>(),
        log: null, runner: null, clock: sp.GetService<TimeProvider>(), settings: sp.GetService<Hcc.Application.Configuration.ISettingsReader>(),
        allocations: sp.GetService<IAgeingAllocationSource>() ?? new SqlAgeingAllocationSource(Platform.Connections));

    /// <summary>True when the range touches a reopened year (or the year after it): the export is stamped "PROVISIONAL — year reopened".</summary>
    public async Task<bool> YearProvisionalAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var store = sp.GetService<IYearLockStore>();
        if (store is null) return false;
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        for (var d = from; d <= to; d = d.AddYears(1)) if (YearLockRules.Provisional(years, d)) return true;
        return YearLockRules.Provisional(years, to);
    }
}
