// FX-RPT-C · rpt.admin.config-history — changes of the configuration settings (cfg.setting_change) by the day they were made, newest first.
// Server twin of built/admin.ts `configHistory`. View PERM-CFG-001 (Owner, Tech admin). Before / after are the stored display texts (never a secret); rupee amounts in them
// become "₹ ••" for a reader without PERM-RPT-002 (DEC-061, the same mask as the audit viewer). Every stored change is a history row, including one later withdrawn.
// The Owner's GST rate changes live in tax.gst_setting_version and are listed too (source owner only, before / after text as the client's gstHistoryFromServer).
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

    public const string WithdrawnMark = " (withdrawn)";

    public sealed record ChangeFact(long ChangeId, string At, string Actor, string Key, string Before, string After, DateOnly EffectiveFrom, string? Reason);

    /// <summary>The report body (pure): newest first, money masked for a reader without the finance right.</summary>
    public static List<ReportRow> Rows(IEnumerable<ChangeFact> facts, bool seesMoney) =>
        [.. facts.OrderByDescending(f => f.At, StringComparer.Ordinal).ThenByDescending(f => f.ChangeId)
            .Select(f => FxCSupport.Row(ReportRowKind.Row, ("at", f.At), ("actor", f.Actor), ("key", f.Key), ("before", seesMoney ? f.Before : AuditViewRules.MaskMoney(f.Before)),
                ("after", seesMoney ? f.After : AuditViewRules.MaskMoney(f.After)), ("effective", f.EffectiveFrom), ("reason", string.IsNullOrEmpty(f.Reason) ? null : f.Reason)))];

    /// <summary>One row of tax.gst_setting_version as the history needs it (seed rows only serve as the "before" value).</summary>
    public sealed record GstVersion(long VersionId, string Key, string Kind, DateOnly EffectiveFrom, long Value, bool Owner, bool Withdrawn, string? Reason, string Actor, DateTime ChangedUtc);

    private static string Group(long n)
    {
        var s = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (s.Length <= 3) return s;
        var head = s[..^3];
        var parts = new List<string>();
        while (head.Length > 2) { parts.Insert(0, head[^2..]); head = head[..^2]; }
        if (head.Length > 0) parts.Insert(0, head);
        return string.Join(',', parts) + "," + s[^3..];
    }

    /// <summary>Display text of a GST master value (prototype <c>gstShow</c>): "18%", "12.5%", "₹7,500", "Available" / "Not available", opt-in "Declared (opt-in)" / "Not declared".</summary>
    public static string GstShow(string key, string kind, long value) => kind switch
    {
        "percent" => ((decimal)value / 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%",
        "money" => "₹" + Group(value / 100) + (value % 100 == 0 ? string.Empty : "." + Math.Abs(value % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture)),
        _ => key == "gst.food.optIn" ? (value == 1 ? "Declared (opt-in)" : "Not declared") : value == 1 ? "Available" : "Not available",
    };

    /// <summary>The Owner's GST versions as history lines (client <c>gstHistoryFromServer</c>): before = the value in force just before (the latest other active version of the key that is
    /// earlier, or on the same date with a lower id), "—" for the first; after gets " (withdrawn)" when withdrawn. Seed (statutory) versions are not changes and are left out.</summary>
    public static List<ChangeFact> GstFacts(IReadOnlyList<GstVersion> all, DateTime fromUtc, DateTime toUtc)
    {
        static bool Earlier(GstVersion x, GstVersion y) => x.EffectiveFrom < y.EffectiveFrom || (x.EffectiveFrom == y.EffectiveFrom && x.VersionId < y.VersionId);
        var list = new List<ChangeFact>();
        foreach (var r in all.Where(v => v.Owner && v.ChangedUtc >= fromUtc && v.ChangedUtc < toUtc))
        {
            GstVersion? hit = null;
            foreach (var x in all.Where(x => x.Key == r.Key && !x.Withdrawn && Earlier(x, r))) if (hit is null || Earlier(hit, x)) hit = x;
            var after = GstShow(r.Key, r.Kind, r.Value);
            list.Add(new ChangeFact(r.VersionId, FxCSupport.Local(r.ChangedUtc), r.Actor, r.Key, hit is null ? "—" : GstShow(r.Key, r.Kind, hit.Value), r.Withdrawn ? after + WithdrawnMark : after,
                r.EffectiveFrom, r.Reason));
        }

        return list;
    }

    private async Task<List<ChangeFact>> GstFactsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var all = new List<GstVersion>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT v.version_id, v.setting_key, g.value_kind, v.effective_from, v.value, v.source, v.status, v.reason, COALESCE(u.display_name, N'user ' + CAST(v.changed_by AS NVARCHAR(12)), N''), v.changed_utc
              FROM tax.gst_setting_version v JOIN tax.gst_setting g ON g.setting_key = v.setting_key LEFT JOIN plat.app_user u ON u.user_id = v.changed_by
             WHERE v.property_id IS NULL OR v.property_id = @p;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            all.Add(new GstVersion(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), DateOnly.FromDateTime(rd.GetDateTime(3)), rd.GetInt64(4), rd.GetString(5) == "owner", rd.GetString(6) == "withdrawn",
                rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetString(8), rd.GetDateTime(9)));
        return GstFacts(all, FxCSupport.LocalStartUtc(from), FxCSupport.LocalStartUtc(to.AddDays(1)));
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var money = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, FxCSupport.Finance, null, ct).ConfigureAwait(false);
        var facts = new List<ChangeFact>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand("""
                SELECT TOP (@take) c.change_id, c.changed_utc, COALESCE(u.display_name, N'user ' + CAST(c.changed_by AS NVARCHAR(12))), c.setting_key, c.before_text, c.after_text, c.effective_from, c.reason, c.withdrawn_utc
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
                facts.Add(new ChangeFact(rd.GetInt64(0), FxCSupport.Local(rd.GetDateTime(1)), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5) + (rd.IsDBNull(8) ? string.Empty : WithdrawnMark),
                    DateOnly.FromDateTime(rd.GetDateTime(6)), rd.IsDBNull(7) ? null : rd.GetString(7)));
        }

        facts.AddRange(await GstFactsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false));

        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(facts, money), FxCSupport.About(Definition, parameters));
    }
}
