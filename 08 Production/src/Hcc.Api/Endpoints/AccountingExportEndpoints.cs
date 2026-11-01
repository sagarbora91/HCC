using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Import;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-3 exports (lane acc3-exports; contract §5; PERM-ACC-005): <c>GET /accounting/export/statements.zip?from&amp;to&amp;parties=all|receivable|payable&amp;format=csv|xlsx</c>
/// = one statement per party (CSV, or Excel with <c>format=xlsx</c>), stored in a zip; parties with neither an opening balance nor a movement are left out.
/// The statements are the ACC-1 ones (<see cref="AccountingReportQueries.StatementAsync"/>), so the figures are the screen's. Names
/// <c>&lt;property code&gt;-statement-&lt;ledger&gt;-&lt;party&gt;-&lt;from&gt;-&lt;to&gt;.&lt;ext&gt;</c>.
/// </summary>
public static class AccountingExportEndpoints
{
    /// <summary>Receivable controls (contract §3).</summary>
    public static readonly IReadOnlyList<string> ReceivableControls = ["1100", "1110", "1115", "1120"];

    /// <summary>Payable controls (contract §3).</summary>
    public static readonly IReadOnlyList<string> PayableControls = ["2200"];

    /// <summary>The party ledger ids a bulk export covers, in a fixed order (control, id).</summary>
    public static IReadOnlyList<string> SelectParties(IEnumerable<string> partyIds, string which)
    {
        var w = (which ?? "all").Trim().ToLowerInvariant();
        if (w is not ("all" or "receivable" or "payable")) throw HccException.Validation("parties must be all, receivable or payable.");
        return [.. partyIds.Where(PartyLedgers.IsParty)
            .Where(id => w == "all" || (w == "receivable" ? ReceivableControls : PayableControls).Contains(PartyLedgers.ControlOf(id)))
            .OrderBy(id => id, StringComparer.Ordinal)];
    }

    // Same composition as AccountingReportEndpoints.Queries (that class is another lane's file; duplicated rather than edited).
    private static AccountingReportQueries Queries(IServiceProvider sp) => new(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
        sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>(),
        sp.GetService<ILedgerMonthBalanceStore>() ?? new Hcc.Infrastructure.SqlServer.Accounting.SqlLedgerMonthBalanceStore(sp.GetRequiredService<Hcc.Infrastructure.SqlServer.SqlPlatform>().Connections),
        sp.GetService<TimeProvider>());

    public static RouteGroupBuilder MapAccountingExportEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/accounting/export/statements.zip", async (DateOnly from, DateOnly to, string? parties, string? format, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            var caller = http.HccCaller();
            if (from > to) throw HccException.Validation("Choose a valid range: \"from\" on or before \"to\".");
            var fmt = (format ?? "csv").Trim().ToLowerInvariant();
            if (fmt is not ("csv" or "xlsx")) throw HccException.Validation("format must be csv or xlsx.");
            await sp.GetRequiredService<IAccessPolicy>().RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Export, ct: ct).ConfigureAwait(false);
            var names = sp.GetService<IReportPartyNames>();
            var ids = SelectParties(names is null ? [] : (await names.GetAsync(caller.PropertyId, ct).ConfigureAwait(false)).Keys, parties ?? "all");
            var queries = Queries(sp);
            var statements = new List<StatementView>();
            foreach (var id in ids) statements.Add(await queries.StatementAsync(caller, id, from, to, ct).ConfigureAwait(false));
            var prop = StatementExports.PropertyCode(await sp.GetRequiredService<IAccountingStore>().PropertyNameAsync(caller.PropertyId, ct).ConfigureAwait(false));
            var (bytes, _) = StatementExports.StatementsZip(prop, statements, fmt);
            return Results.File(bytes, "application/zip", StatementExports.FileName(prop, "statements-" + (parties ?? "all").Trim().ToLowerInvariant(), from, to, "zip"));
        });
        return api;
    }
}
