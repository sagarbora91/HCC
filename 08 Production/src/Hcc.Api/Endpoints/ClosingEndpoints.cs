using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Reporting;
using Hcc.Contracts.Closing;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Reporting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Closing + reporting (P6): POST /commands/{RunBusinessDayClose|LockCashBookDay|ReopenCashBookDay}; GET /closing/preflight?date=,
/// /closing/runs/{date}, /closing/snapshots/{date}, /cashbook/{date}, /cashbook?from=&amp;to= (range), /cashbook/{date}/book (day with entries), /reports/daily?from=&amp;to=. All require a session.
/// </summary>
public static class ClosingEndpoints
{
    /// <summary>
    /// DI for closing and reporting. Also installs the late-write barrier for every module: the <see cref="ICommandRunner"/> that
    /// P2–P4 services resolve becomes <see cref="BusinessDayGuardedCommandRunner"/> over the platform runner (P1 and the close itself
    /// use <see cref="SqlPlatform.Commands"/> directly).
    /// </summary>
    public static IServiceCollection AddClosingModule(this IServiceCollection services)
    {
        services.AddSingleton<IBusinessDayGuard, SqlBusinessDayGuard>();
        services.Replace(ServiceDescriptor.Singleton<ICommandRunner>(sp =>
            new BusinessDayGuardedCommandRunner(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IBusinessDayGuard>())));
        services.AddSingleton<IClosingStore>(sp => new SqlClosingStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IReportSource>(sp => new SqlReportSource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.TryAddSingleton<ICloseStepObserver, NoCloseStepObserver>();
        services.AddSingleton(sp => new ClosingService(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IClosingStore>(),
            sp.GetRequiredService<IReportSource>(), sp.GetRequiredService<IAccessPolicy>(), sp.GetRequiredService<BillingService>(),
            sp.GetRequiredService<ICloseStepObserver>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<Hcc.Application.Pos.IPosCloseGate>())); // POS open checks / pending voids block the close
        services.AddSingleton<ReportingQueries>();
        services.AddSingleton<ICashBookSource>(sp => new SqlCashBookSource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<CashBookQueries>();
        return services;
    }

    public static RouteGroupBuilder MapClosingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<RunBusinessDayCloseRequest, CloseRunResult>(g, ClosingService.OpRun, (sp, c, k, r, ct) => sp.GetRequiredService<ClosingService>().RunBusinessDayCloseAsync(c, k, r, ct));
        BillingEndpoints.Command<LockCashBookDayRequest, CashBookDayResult>(g, ClosingService.OpLockCashBook, (sp, c, k, r, ct) => sp.GetRequiredService<ClosingService>().LockCashBookDayAsync(c, k, r, ct));
        BillingEndpoints.Command<ReopenCashBookDayRequest, CashBookDayResult>(g, ClosingService.OpReopenCashBook, (sp, c, k, r, ct) => sp.GetRequiredService<ClosingService>().ReopenCashBookDayAsync(c, k, r, ct));
        g.MapGet("/closing/preflight", async (HttpContext http, ClosingService s, DateOnly? date, CancellationToken ct) =>
            Results.Ok(await s.GetPreflightAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/closing/runs/{date}", async (HttpContext http, ClosingService s, DateOnly date, CancellationToken ct) =>
            Results.Ok(await s.GetRunAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/closing/snapshots/{date}", async (HttpContext http, ClosingService s, DateOnly date, CancellationToken ct) =>
            Results.Ok(await s.GetSnapshotAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/cashbook/{date}", async (HttpContext http, ClosingService s, DateOnly date, CancellationToken ct) =>
            Results.Ok(await s.GetCashBookDayAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/cashbook", async (HttpContext http, CashBookQueries q, DateOnly from, DateOnly? to, CancellationToken ct) =>
            Results.Ok(await q.GetRangeAsync(http.HccCaller(), from, to ?? from, ct).ConfigureAwait(false)));
        g.MapGet("/cashbook/{date}/book", async (HttpContext http, CashBookQueries q, DateOnly date, CancellationToken ct) =>
            Results.Ok(await q.GetDayAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/reports/daily", async (HttpContext http, ReportingQueries q, DateOnly from, DateOnly? to, CancellationToken ct) =>
            Results.Ok(await q.GetDailyReportAsync(http.HccCaller(), from, to ?? from, ct).ConfigureAwait(false)));
        return api;
    }
}
