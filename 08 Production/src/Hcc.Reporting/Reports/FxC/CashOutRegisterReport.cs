// FX-RPT-C · rpt.money.cash-out-register — bank deposits, till transfers and Owner drawings (pay.cash_transfer) by the business date the cash left the drawer, oldest first,
// plus "Totals per kind" that leave out rejected records. Server twin of built/money.ts `cashOutRegister`. The slip photo is never read. View and money gate PERM-RPT-002.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class CashOutRegisterReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.money.cash-out-register";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        Dt("date", "Date"), T("kind", "Kind"), M("amount", "Amount"), T("bankSlip", "Bank and slip"), T("fromTill", "From till"), T("toTill", "To till"), T("reason", "Reason"),
        T("status", "Status"), T("decidedBy", "Decided by"),
    ];

    public static readonly IReadOnlyList<ReportColumn> KindColumns = [T("kind", "Kind"), I("count", "Records"), M("amount", "Amount")];

    public static readonly IReadOnlyList<string> KindLabels = ["bank deposit", "till transfer", "owner drawing"];

    private static readonly string[] KindCodes = ["banking", "till", "drawing"];

    public ReportDefinition Definition { get; } = new(Id, "Banking, transfers and drawings", "money", "P2", "built", true, FxCSupport.Finance, [FxCSupport.Finance], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business), new("kind", "Kind", "enum", null, ["all", .. KindLabels], "all")], Columns,
        "Cash-out records by the business date the cash left the drawer. The slip photo is never in the report. Totals per kind leave out rejected records.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public sealed record Fact(DateOnly Date, string KindCode, long Amount, string? Bank, string? Slip, string FromTill, string? ToTill, string? Reason, string Status, string? DecidedBy);

    /// <summary>The two blocks (pure): the records and the totals per kind (rejected records left out).</summary>
    public static (List<ReportRow> Records, List<ReportRow> Kinds) Blocks(IEnumerable<Fact> facts, string want)
    {
        var recs = facts.Where(f => want == "all" || KindLabels[Array.IndexOf(KindCodes, f.KindCode)] == want).ToList();
        var records = recs.Select(f => FxCSupport.Row(ReportRowKind.Row, ("date", f.Date), ("kind", KindLabels[Array.IndexOf(KindCodes, f.KindCode)]), ("amount", f.Amount),
            ("bankSlip", string.Join(" · ", new[] { f.Bank, f.Slip }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } b ? b : null), ("fromTill", f.FromTill), ("toTill", f.ToTill),
            ("reason", string.IsNullOrEmpty(f.Reason) ? null : f.Reason), ("status", f.Status), ("decidedBy", f.DecidedBy))).ToList();
        var kinds = KindCodes.Select((code, i) => (Label: KindLabels[i], Live: recs.Where(r => r.KindCode == code && r.Status != "Rejected").ToList()))
            .Where(k => want == "all" || k.Label == want)
            .Select(k => FxCSupport.Row(ReportRowKind.Row, ("kind", k.Label), ("count", (long)k.Live.Count), ("amount", k.Live.Sum(r => r.Amount)))).ToList();
        return (records, kinds);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var names = await FxCSupport.NamesAsync(platform, caller.PropertyId, ct).ConfigureAwait(false);
        var want = Param(parameters, "kind");
        if (want.Length == 0) want = "all";
        if (want != "all" && !KindLabels.Contains(want)) throw HccException.Validation("Kind: choose one of all, bank deposit, till transfer, owner drawing.");
        var facts = new List<Fact>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand("""
                SELECT x.business_date, x.kind, x.amount_paise, x.bank, x.slip_no, f.name, t.name, x.reason, x.status, x.decided_by
                  FROM pay.cash_transfer x JOIN pay.till f ON f.till_id = x.from_till_id LEFT JOIN pay.till t ON t.till_id = x.to_till_id
                 WHERE x.property_id = @p AND x.business_date BETWEEN @from AND @to
                 ORDER BY x.business_date, x.recorded_utc, x.transfer_id;
                """, conn);
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
            cmd.Parameters.Add("@from", SqlDbType.Date).Value = parameters.From!.Value.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@to", SqlDbType.Date).Value = parameters.To!.Value.ToDateTime(TimeOnly.MinValue);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                facts.Add(new Fact(DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetString(1), rd.GetInt64(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.GetString(5), rd.IsDBNull(6) ? null : rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetString(8),
                    rd.IsDBNull(9) ? null : FxCSupport.NameOf(names, rd.GetInt32(9))));
        }

        var (records, kinds) = Blocks(facts, want);
        var a = new ReportTable("records", "Records", Columns, records);
        return new ReportResult(Id, Definition.Title, parameters, a.Columns, a.Rows, FxCSupport.About(Definition, parameters), Sections: [a, new("kinds", "Totals per kind", KindColumns, kinds)]);
    }
}
