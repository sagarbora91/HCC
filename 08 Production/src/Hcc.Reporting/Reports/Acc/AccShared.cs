// F7-R6 · helpers shared by the accounting reports; server twin of 03 Prototype/src/domain/reports/acc/shared.ts. Column builders are the tax lane's (TaxSql).
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Reports.Acc;

public static class AccShared
{
    public const string View = "PERM-ACC-001";
    public const string OwnerAccountant = "PERM-ACC-013";
    public const string Tally = "PERM-ACC-006";

    /// <summary>ACC-4 §1.2: a table of a reopened year (or the year after) carries the provisional text as its first row, in every format.</summary>
    public static async Task<IReadOnlyList<ReportRow>> StampAsync(AccQueries q, Caller caller, DateOnly from, DateOnly to, string textKey, CancellationToken ct) =>
        await q.YearProvisionalAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false)
            ? [Hcc.Reporting.Reports.Tax.TaxSql.Row(ReportRowKind.Subtotal, (textKey, YearLockRules.ProvisionalText))]
            : [];

    public static ReportAbout About(ReportDefinition d, BoundParams p, bool provisional) => new(DateTimeOffset.MinValue, "", d.DateBasis, provisional, p.Legacy, p.From, p.To);

    public static ReportParam Range(string label = "From", ReportDateClass dc = ReportDateClass.Financial) => new("from", label, "range", dc);

    /// <summary>Basis points → percent with one decimal, in integer arithmetic (the prototype's bpsToPct).</summary>
    public static double BpsToPct(int bps) => (bps + 5) / 10 / 10.0;

    public static DateOnly Fy(DateOnly d) => PlReports.FyStart(d);
}
