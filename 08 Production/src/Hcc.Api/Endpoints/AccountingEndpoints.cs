using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Accounting & Tally (W2-3): <c>POST /commands/{LockAccountingPeriod|UnlockAccountingPeriod|SetLedgerMapping|ResetLedgerMapping}</c>,
/// <c>GET /accounting/days/{date}</c>, <c>/accounting/days?from&amp;to</c> (W4-7, ≤ 62 days), <c>/accounting/trial-balance</c>, <c>/accounting/mapping</c>(+<c>/history</c>), <c>/accounting/chart</c>,
/// <c>/accounting/locks/history</c>, <c>/accounting/export/csv</c>, <c>/accounting/export/tally</c> (simulated), plus the background
/// outbox consumer turning billing/payments (and later POS, expenses, payroll) events into voucher sources.
/// </summary>
public static class AccountingEndpoints
{
    public static IServiceCollection AddAccountingModule(this IServiceCollection services)
    {
        services.AddSingleton<IAccountingStore>(sp => new SqlAccountingStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IChargeGstReader, StoredChargeGstReader>(); // agent C tax.charge_gst when present, else the stored tax split
        services.AddSingleton(sp => new AccountingVoucherProjector(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<SqlPlatform>().Outbox,
            sp.GetRequiredService<IChargeGstReader>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<AccountingService>();
        // DEC-077: the Tally export is recorded (log + audit) on the platform runner: it posts on no business date, so the late-write
        // barrier does not apply.
        services.AddSingleton(sp => new AccountingQueries(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(),
            sp.GetService<Hcc.Application.Import.IOpeningBalanceSource>(), sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IOpeningEntryStore>())); // ACC-1: the approved opening entry supersedes the imported opening in the cumulative TB
        services.AddSingleton<IReportPartyNames>(sp => new SqlReportPartyNames(sp.GetRequiredService<SqlPlatform>().Connections)); // ACC-1 party names (reports)
        services.AddSingleton<IAccountingDayHooks, AccountingDayHooks>(); // DEC-077: Closing's reopen asks for the period lock and flags Tally re-export
        services.AddHostedService<AccountingProjectorWorker>();
        return services;
    }

    public static RouteGroupBuilder MapAccountingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<LockPeriodRequest, PeriodLockResult>(g, AccountingService.OpLock, (sp, c, k, r, ct) => sp.GetRequiredService<AccountingService>().LockAsync(c, k, r, ct));
        BillingEndpoints.Command<UnlockPeriodRequest, PeriodLockResult>(g, AccountingService.OpUnlock, (sp, c, k, r, ct) => sp.GetRequiredService<AccountingService>().UnlockAsync(c, k, r, ct));
        BillingEndpoints.Command<SetLedgerMappingRequest, LedgerMappingResult>(g, AccountingService.OpMapSet, (sp, c, k, r, ct) => sp.GetRequiredService<AccountingService>().SetMappingAsync(c, k, r, ct));
        BillingEndpoints.Command<ResetLedgerMappingRequest, LedgerMappingResult>(g, AccountingService.OpMapReset, (sp, c, k, r, ct) => sp.GetRequiredService<AccountingService>().ResetMappingAsync(c, k, r, ct));
        g.MapGet("/accounting/days/{date}", async (DateOnly date, HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.DayAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/days", async (DateOnly from, DateOnly to, HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.DaysAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false)));
        // ACC-1 (statements lane): ?from&to&detail = the six-column trial balance (contract §6); ?date&cumulative unchanged.
        g.MapGet("/accounting/trial-balance", async (HttpContext http, AccountingQueries q, CancellationToken ct, DateOnly? date = null, bool cumulative = false,
            DateOnly? from = null, DateOnly? to = null, bool detail = false) =>
            from is { } f ? await AccountingReportEndpoints.TrialBalanceAsync(http, f, to ?? f, detail, ct).ConfigureAwait(false)
            : date is { } d ? Results.Ok(await q.TrialBalanceAsync(http.HccCaller(), d, cumulative, ct).ConfigureAwait(false))
            : throw Hcc.Domain.Errors.HccException.Validation("Give \"date\" (day / cumulative) or \"from\" and \"to\" (six columns)."));
        g.MapGet("/accounting/mapping", async (HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.MappingAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/mapping/history", async (HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.MappingHistoryAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/chart", async (HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.ChartAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/locks/history", async (HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.LockHistoryAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/accounting/export/csv", async (DateOnly from, DateOnly to, HttpContext http, AccountingQueries q, CancellationToken ct) =>
        {
            var (name, csv) = await q.CsvAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", name);
        });
        g.MapGet("/accounting/export/tally", async (DateOnly from, DateOnly to, HttpContext http, AccountingQueries q, CancellationToken ct)
            => Results.Ok(await q.TallyAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false)));
        return api;
    }
}

/// <summary>Polls the outbox for accounting events every 2 s (at-least-once delivery, exactly-once effect per consumer).</summary>
// ETP-REUSE: align with ETP background-job hosting (Windows service worker, retry/back-off, logging) when ETP (V: drive) is available.
public sealed class AccountingProjectorWorker(AccountingVoucherProjector projector, ILogger<AccountingProjectorWorker> logger) : OutboxPollingWorker(logger, "Accounting outbox projection")
{
    protected override Task PollAsync(CancellationToken ct) => projector.RunOnceAsync(ct: ct);
}
