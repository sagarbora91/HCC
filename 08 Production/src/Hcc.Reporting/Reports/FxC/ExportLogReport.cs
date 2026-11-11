// FX-RPT-C · rpt.admin.export-log — every print and export (rule V.4, rpt.export_log): who, which report, which parameters, which format, how many rows; newest first.
// Server twin of built/admin.ts `exportLog`. Owner and Manager only (the prototype refuses everyone else with FORBIDDEN, although the permission PERM-ADM-004 also opens the
// catalogue entry for the Accountant). No money column. Time is the hotel clock; the report title is looked up in the registry (the id when the report is no longer registered).
using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ExportLogReport(SqlPlatform platform, IReportPrincipalRoles roles, IServiceProvider services) : IReportSelector
{
    public const string Id = "rpt.admin.export-log";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("at", "Time"), T("user", "User"), T("report", "Report"), T("params", "Parameters"), T("format", "Format"), I("rows", "Rows")];

    public ReportDefinition Definition { get; } = new(Id, "Export and print log", "admin", "P2", "built", false, null, ["PERM-ADM-004"], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business), new("report", "Report", "text"), new("user", "User", "user")], Columns,
        "Every print and export is recorded when it is made (rule V.4): who, which report, which parameters, which format and how many rows. Newest first. Owner and Manager only.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No);

    /// <summary>One log row as read from rpt.export_log (hotel-local time text, user name, stored parameter JSON).</summary>
    public sealed record LogFact(long LogId, string At, string User, string ReportId, string ParamsJson, string Format, int Rows);

    /// <summary>"k=v, k=v" from the stored parameter JSON (a flat object; the prototype <c>paramsText</c>); null when there are none.</summary>
    public static string? ParamsText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json.Length == 0 ? null : json;
            var parts = doc.RootElement.EnumerateObject().Where(p => p.Name != "legacy" && p.Value.ValueKind != JsonValueKind.Null)
                .Select(p => p.Name + "=" + (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString()));
            var s = string.Join(", ", parts);
            return s.Length == 0 ? null : s;
        }
        catch (JsonException) { return json.Length == 0 ? null : json; }
    }

    /// <summary>The report body (pure): the report filter matches the id or the title, case-insensitive.</summary>
    public static List<ReportRow> Rows(IEnumerable<LogFact> facts, Func<string, string?> titleOf, string reportFilter)
    {
        var q = (reportFilter ?? string.Empty).Trim();
        return [.. facts
            .Select(f => (Fact: f, Title: titleOf(f.ReportId) ?? f.ReportId))
            .Where(x => q.Length == 0 || x.Fact.ReportId.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(x => FxCSupport.Row(ReportRowKind.Row, ("at", x.Fact.At), ("user", x.Fact.User), ("report", x.Title), ("params", ParamsText(x.Fact.ParamsJson)), ("format", x.Fact.Format), ("rows", (long)x.Fact.Rows)))];
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!await FxCSupport.HasRoleAsync(roles, caller.PrincipalId, ct, "OWN", "MGR").ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "The export and print log is for the Owner and Manager.");
        var user = Param(parameters, "user").Trim();
        int? userId = user.Length == 0 ? null : int.TryParse(user, NumberStyles.None, CultureInfo.InvariantCulture, out var u) ? u : -1;
        var facts = new List<LogFact>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand("""
                SELECT TOP (@take) l.log_id, l.at_utc, COALESCE(u.display_name, N'System'), l.report_id, l.params, l.format, l.row_count
                  FROM rpt.export_log l LEFT JOIN plat.app_user u ON u.user_id = l.principal_id
                 WHERE l.property_id = @p AND l.at_utc >= @from AND l.at_utc < @to AND (@user IS NULL OR l.principal_id = @user)
                 ORDER BY l.at_utc DESC, l.log_id DESC;
                """, conn);
            cmd.Parameters.Add("@take", SqlDbType.Int).Value = ReportRunner.FileMax + 1;
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
            cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = FxCSupport.LocalStartUtc(parameters.From!.Value);
            cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = FxCSupport.LocalStartUtc(parameters.To!.Value.AddDays(1));
            cmd.Parameters.Add("@user", SqlDbType.Int).Value = userId is { } id ? id : DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                facts.Add(new LogFact(rd.GetInt64(0), FxCSupport.Local(rd.GetDateTime(1)), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetInt32(6)));
        }

        var catalogue = services.GetService<IReportCatalogue>();
        var rows = Rows(facts, id => catalogue?.Find(id)?.Definition.Title, Param(parameters, "report"));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, FxCSupport.About(Definition, parameters));
    }
}
