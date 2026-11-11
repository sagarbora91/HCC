// FX-RPT-C · rpt.admin.config-history — changes of the configuration settings (cfg.setting_change) by the day they were made, newest first.
// Server twin of built/admin.ts `configHistory`. View PERM-CFG-001 (Owner, Tech admin). Before / after are the stored display texts (never a secret); rupee amounts in them
// become "₹ ••" for a reader without PERM-RPT-002 (DEC-061, the same mask as the audit viewer). Every stored change is a history row, including one later withdrawn.
// Masters are not in cfg.setting_change on the server (they keep their own audit trail, see the audit log report), so this report lists configuration settings only.
using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Audit;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ConfigHistoryReport(SqlPlatform platform, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.admin.config-history";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("at", "Changed"), T("actor", "By"), T("key", "Setting"), T("before", "Before"), T("after", "After"), Dt("effective", "Effective from"), T("reason", "Reason")];

    public ReportDefinition Definition { get; } = new(Id, "Configuration and masters history", "admin", "P2", "built", false, null, ["PERM-CFG-001"], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business)], Columns,
        "Changes of the configuration settings by the date they were made, newest first. Money values are masked for roles that never see hotel money (DEC-061).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public sealed record ChangeFact(long ChangeId, string At, string Actor, string Key, string Before, string After, DateOnly EffectiveFrom, string? Reason);

    /// <summary>The report body (pure): newest first, money masked for a reader without the finance right.</summary>
    public static List<ReportRow> Rows(IEnumerable<ChangeFact> facts, bool seesMoney) =>
        [.. facts.OrderByDescending(f => f.At, StringComparer.Ordinal).ThenByDescending(f => f.ChangeId)
            .Select(f => FxCSupport.Row(ReportRowKind.Row, ("at", f.At), ("actor", f.Actor), ("key", f.Key), ("before", seesMoney ? f.Before : AuditViewRules.MaskMoney(f.Before)),
                ("after", seesMoney ? f.After : AuditViewRules.MaskMoney(f.After)), ("effective", f.EffectiveFrom), ("reason", string.IsNullOrEmpty(f.Reason) ? null : f.Reason)))];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var money = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, FxCSupport.Finance, null, ct).ConfigureAwait(false);
        var facts = new List<ChangeFact>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand("""
                SELECT TOP (@take) c.change_id, c.changed_utc, COALESCE(u.display_name, N'user ' + CAST(c.changed_by AS NVARCHAR(12))), c.setting_key, c.before_text, c.after_text, c.effective_from, c.reason
                  FROM cfg.setting_change c LEFT JOIN plat.app_user u ON u.user_id = c.changed_by
                 WHERE c.property_id = @p AND c.changed_utc >= @from AND c.changed_utc < @to
                 ORDER BY c.changed_utc DESC, c.change_id DESC;
                """, conn);
            cmd.Parameters.Add("@take", SqlDbType.Int).Value = ReportRunner.FileMax + 1;
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
            cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = FxCSupport.LocalStartUtc(parameters.From!.Value);
            cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = FxCSupport.LocalStartUtc(parameters.To!.Value.AddDays(1));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                facts.Add(new ChangeFact(rd.GetInt64(0), FxCSupport.Local(rd.GetDateTime(1)), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5),
                    DateOnly.FromDateTime(rd.GetDateTime(6)), rd.IsDBNull(7) ? null : rd.GetString(7)));
        }

        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(facts, money), FxCSupport.About(Definition, parameters));
    }
}
