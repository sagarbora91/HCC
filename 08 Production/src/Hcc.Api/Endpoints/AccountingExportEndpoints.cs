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

    private const string CsvGap = "\r\n\r\n";

    /// <summary>F5-int (ACC4-CONTRACT §1.2): true when the export range touches a reopened year (or the year after it) — the file is stamped
    /// "PROVISIONAL — year reopened" (<see cref="YearLockRules.Provisional"/>).</summary>
    public static async Task<bool> YearProvisionalAsync(IServiceProvider sp, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var store = sp.GetService<IYearLockStore>();
        if (store is null) return false;
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        for (var d = from; d <= to; d = d.AddYears(1)) if (YearLockRules.Provisional(years, d)) return true;
        return YearLockRules.Provisional(years, to);
    }

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
            var (bytes, _) = StatementExports.StatementsZip(prop, statements, fmt, await YearProvisionalAsync(sp, caller.PropertyId, from, to, ct).ConfigureAwait(false));
            return Results.File(bytes, "application/zip", StatementExports.FileName(prop, "statements-" + (parties ?? "all").Trim().ToLowerInvariant(), from, to, "zip"));
        });
        // ACC-3 integration: the §2–§3 report views through OpenXmlExporter / CSV — PERM-ACC-005 (export) plus the report's own PERM-ACC-001 (view).
        g.MapGet("/accounting/export/report", async (string kind, DateOnly? from, DateOnly? to, DateOnly? asOf, string? month, string? fy, string? format, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            var caller = http.HccCaller();
            var fmt = (format ?? "xlsx").Trim().ToLowerInvariant();
            if (fmt is not ("csv" or "xlsx")) throw HccException.Validation("format must be csv or xlsx.");
            await sp.GetRequiredService<IAccessPolicy>().RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Export, ct: ct).ConfigureAwait(false);
            var store = sp.GetRequiredService<IAccountingStore>();
            var prop = StatementExports.PropertyCode(await store.PropertyNameAsync(caller.PropertyId, ct).ConfigureAwait(false));
            DateOnly Need(DateOnly? d, string n) => d ?? throw HccException.Validation(n + " is required for this report.");
            var pl = new PlQueries(store, sp.GetRequiredService<IAccessPolicy>(), sp.GetService<IOpeningBalanceSource>(), sp.GetService<IReportOpeningSource>(), sp.GetService<IReportPartyNames>());
            IReadOnlyList<StatementExports.Table> tables;
            string report;
            DateOnly a, b;
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "pl":
                    (a, b) = (Need(from, "from"), Need(to, "to"));
                    tables = ReportExports.Pl(prop, await pl.PlAsync(caller, a, b, ct).ConfigureAwait(false)); report = "pl"; break;
                case "balance-sheet":
                    a = b = Need(asOf, "asOf");
                    tables = [ReportExports.BalanceSheet(prop, await pl.BalanceSheetAsync(caller, a, ct).ConfigureAwait(false))]; report = "balance-sheet"; break;
                case "cash-ratio":
                    var c = await pl.CashRatioAsync(caller, fy ?? "", ct).ConfigureAwait(false);
                    (a, b) = PlReports.FyRange(fy ?? "");
                    tables = [ReportExports.CashRatio(prop, c)]; report = "cash-ratio"; break;
                case "ageing-receivable" or "ageing-payable":
                    a = b = Need(asOf, "asOf");
                    var side = kind!.Trim().ToLowerInvariant()["ageing-".Length..];
                    tables = ReportExports.Ageing(prop, await AccountingAgeingEndpoints.AgeingQueriesOf(sp).AgeingAsync(caller, a, side, ct).ConfigureAwait(false)); report = "ageing-" + side; break;
                case "gst-summary":
                    var g = await AccountingAgeingEndpoints.GstQueriesOf(sp).SummaryAsync(caller, month, ct).ConfigureAwait(false);
                    a = b = new DateOnly(int.Parse(g.Month[..4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(g.Month[5..7], System.Globalization.CultureInfo.InvariantCulture), 1);
                    tables = [ReportExports.GstSummary(prop, g)]; report = "gst-summary"; break;
                default:
                    throw HccException.Validation("kind must be pl, balance-sheet, cash-ratio, ageing-receivable, ageing-payable or gst-summary.");
            }

            if (await YearProvisionalAsync(sp, caller.PropertyId, a, b, ct).ConfigureAwait(false)) tables = [.. tables.Select(StatementExports.Stamp)]; // F5-int
            var name = StatementExports.FileName(prop, report, a, b, fmt);
            return fmt == "xlsx"
                ? Results.File(StatementExports.Xlsx(tables), OpenXmlExporter.Mime, name)
                : Results.File(System.Text.Encoding.UTF8.GetBytes(string.Join(CsvGap, tables.Select(StatementExports.Csv))), "text/csv", name);
        });
        return api;
    }
}
