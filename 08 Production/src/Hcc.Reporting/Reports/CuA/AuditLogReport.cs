// CU-RPT-A · rpt.admin.audit-log — the audit trail (plat.audit_event) for a range, newest first. Server twin of built/admin.ts `auditLog`. No money column.
// Permission PERM-ADM-004. The detail shows only to Owner / Manager / Accountant (prototype adminAuditDetailVisible); rupee amounts in it are masked for a reader without
// PERM-RPT-002 (DEC-061, the same "₹ ••" as the audit viewer). A reader without the detail right also cannot search inside the detail text (default: no side channel).
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Audit;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.CuA;

public sealed class AuditLogReport(SqlPlatform platform, IAccessPolicy policy, IReportPrincipalRoles roles) : IReportSelector
{
    public const string Id = "rpt.admin.audit-log";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("at", "Time"), T("actor", "Actor"), T("action", "Action"), T("detail", "Detail"), T("id", "Id")];

    public ReportDefinition Definition { get; } = new(Id, "Audit log", "admin", "P1", "built", false, null, [CuASql.AuditView], [], "PERM-RPT-003", ReportDateClass.Business,
        [CuASql.From(ReportDateClass.Business), CuASql.To(ReportDateClass.Business), new("actor", "Actor", "text"), new("action", "Action", "text"), new("text", "Contains", "text")], Columns,
        "Audit events by the date they were recorded, newest first. Detail is shown only to roles with audit detail; money in a detail is masked for roles that never see hotel money (DEC-061).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    /// <summary>The report body from the audit facts (pure; the tests feed it facts).</summary>
    public static List<ReportRow> Rows(IEnumerable<AuditFact> facts, bool detailVisible, bool seesMoney) =>
        [.. facts.Select(f => CuASql.Row(ReportRowKind.Row, ("at", f.AtLocal), ("actor", f.Actor), ("action", f.Action),
            ("detail", detailVisible ? (seesMoney ? f.Detail : AuditViewRules.MaskMoney(f.Detail)) : null), ("id", CuASql.AuditId(f))))];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var detail = (await roles.RolesAsync(caller.PrincipalId, ct).ConfigureAwait(false)).Any(CuASql.AuditDetailRoles.Contains);
        var money = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, CuASql.Finance, null, ct).ConfigureAwait(false);
        var facts = await CuASql.AuditAsync(platform.Connections, caller.PropertyId, parameters.From!.Value, parameters.To!.Value, Param(parameters, "actor"), Param(parameters, "action"),
            Param(parameters, "text"), detail, ReportRunner.FileMax + 1, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(facts, detail, money), About(Definition, parameters));
    }
}
