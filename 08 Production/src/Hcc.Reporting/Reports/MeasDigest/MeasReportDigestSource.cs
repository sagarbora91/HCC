// F12 MEAS · f12-int-an · the server's Owner-digest "Measurement" block (MEAS-DIGEST item 6, review M2). For a closed business date it runs every switched-on
// MEAS report of the daily, revenue, guest, housekeeping and people packs (each report's own selector, so its own exception rule decides) as the Owner and keeps
// the red rows: a row in the "red" group (guest, housekeeping lanes) or with a set "exception" cell (daily, people, rate parity). The study reports have no
// red-row rule and are not evaluated. MeasurementDigest.Compose applies the gate (IMeasurementGate.IsOnAsync, module + pack + item) and the Owner's line
// subscription (meas.digest.line.<key>, default on). A report that fails is left out of the block (never breaks the day-close digest).
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Domain.Money;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Reporting.Reports.MeasDigest;

public sealed class MeasReportDigestSource(IServiceProvider services, SqlPlatform platform) : IMeasDigestSource
{
    /// <summary>Lines kept per item; further red rows are summed into one "… and N more" line.</summary>
    public const int RowsPerItem = 5;

    public async Task<MeasDigestBlock?> BuildAsync(int propertyId, DateOnly date, CancellationToken ct = default)
    {
        var gate = services.GetService<IMeasurementGate>();
        if (gate is null) return null;
        var settings = services.GetService<ISettingsReader>();
        var reports = services.GetServices<IReportSelector>().Where(s => s is IMeasuredReport m && Evaluated(s.Definition, m.MeasKey)).ToList();
        var owner = await OwnerAsync(propertyId, ct).ConfigureAwait(false);
        var caller = new Caller(propertyId, owner);
        var rows = new Dictionary<string, IReadOnlyList<MeasRedRow>>(StringComparer.Ordinal);
        var on = new HashSet<string>(StringComparer.Ordinal);
        var subscribed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in reports)
        {
            var key = ((IMeasuredReport)r).MeasKey;
            if (!await IsOnAsync(gate, propertyId, key, ct).ConfigureAwait(false)) continue;
            if (!await SubscribedAsync(settings, propertyId, key, ct).ConfigureAwait(false)) continue;
            try
            {
                var p = ParamBinder.Bind(r.Definition, new Dictionary<string, string?>(StringComparer.Ordinal) { ["from"] = Iso(date), ["to"] = Iso(date) }, date);
                var result = await r.SelectAsync(caller, p, ct).ConfigureAwait(false);
                var list = rows.TryGetValue(key, out var have) ? [.. have] : new List<MeasRedRow>();
                list.AddRange(RedRows(key, result, RowsPerItem));
                rows[key] = list;
                on.Add(key); subscribed.Add(key);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // a report that cannot run for this date (no data source, permission, validation) is not part of the block
            }
        }
        return MeasurementDigest.Compose(date, rows, on.Contains, subscribed.Contains);
    }

    /// <summary>Daily, revenue, guest, housekeeping and people reports with a switch; study reports and the pack are not evaluated (no red-row rule).</summary>
    public static bool Evaluated(ReportDefinition def, string measKey)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.Area != "study" && !measKey.StartsWith("meas.study.", StringComparison.Ordinal);
    }

    /// <summary>The red rows of one report result (every section): Group "red", or an "exception" cell that is a non-empty text, true or 1.
    /// Text = the row's first three plain cells (masked columns skipped; money as rupees) plus the exception reason.</summary>
    public static IReadOnlyList<MeasRedRow> RedRows(string key, ReportResult result, int cap = RowsPerItem)
    {
        ArgumentNullException.ThrowIfNull(result);
        var tables = result.Sections is { Count: > 0 } s ? s : [new ReportTable("main", null, result.Columns, result.Rows)];
        var found = new List<string>();
        foreach (var t in tables)
            foreach (var row in t.Rows)
            {
                if (row.Kind != ReportRowKind.Row) continue;
                var exc = row.Cells.GetValueOrDefault("exception");
                if (!(row.Group == "red" || Truthy(exc))) continue;
                var parts = t.Columns.Where(c => c.Key != "exception" && c.Mask == ReportMask.None).Select(c => Cell(c, row.Cells.GetValueOrDefault(c.Key))).Where(x => x.Length > 0).Take(3).ToList();
                var text = string.Join(" · ", parts);
                if (exc is string reason && reason.Length > 0) text = text.Length > 0 ? text + " — " + reason : reason;
                found.Add(text.Length > 0 ? text : result.Title);
            }
        var outRows = found.Take(cap).Select(x => new MeasRedRow(key, result.Id, x)).ToList();
        if (found.Count > cap) outRows.Add(new MeasRedRow(key, result.Id, $"… and {(found.Count - cap).ToString(CultureInfo.InvariantCulture)} more in {result.Title}"));
        return outRows;
    }

    private static bool Truthy(object? v) => v switch
    {
        null => false, string s => s.Length > 0 && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase), bool b => b, long l => l != 0, int i => i != 0, double d => d != 0, _ => false,
    };

    private static string Cell(ReportColumn c, object? v) => v switch
    {
        null => "",
        long l when c.Type == ReportColumnType.Money => new Paise(l).ToString(),
        double d when c.Type == ReportColumnType.Percent => d.ToString("0.#", CultureInfo.InvariantCulture) + "%",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<bool> IsOnAsync(IMeasurementGate gate, int propertyId, string key, CancellationToken ct)
    {
        try { return await gate.IsOnAsync(propertyId, key, ct).ConfigureAwait(false); }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }

    private static async Task<bool> SubscribedAsync(ISettingsReader? settings, int propertyId, string key, CancellationToken ct)
    {
        if (settings is null) return true;
        try
        {
            var v = await settings.GetAsync(propertyId, "meas.digest.line." + key, null, ct).ConfigureAwait(false);
            return v.Kind != ConfigValueKind.Boolean || v.Flag;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return true; // not registered / not stored: subscribed (prototype default)
        }
    }

    /// <summary>The digest is the Owner's (PERM-DGS-001): the reports run as the property's Owner so the Owner-only rules (people pulse, scorecard) apply.</summary>
    private async Task<int> OwnerAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) u.user_id FROM plat.app_user u JOIN plat.user_role r ON r.user_id = u.user_id
             WHERE u.property_id = @p AND r.role_code = 'OWN' AND r.valid_from_utc <= SYSUTCDATETIME() ORDER BY u.user_id;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id ? id : 0;
    }
}
