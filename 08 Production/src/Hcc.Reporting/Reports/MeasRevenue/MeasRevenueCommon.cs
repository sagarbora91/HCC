// F12-RPT-REVENUE · small helpers shared by the six report classes (column makers, parameter parsing, result building).
using System.Globalization;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Guests;

namespace Hcc.Reporting.Reports.MeasRevenue;

internal static class MeasRevenueCommon
{
    public static ReportColumn T(string key, string label) => new(key, label, ReportColumnType.Text, false);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportColumn B(string key, string label) => new(key, label, ReportColumnType.Bool, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public const string MoneyGate = "PERM-RPT-002";

    public const string Note = " Capture needed: ";

    public static string CaptureNote(string basis, string what) => $"{basis} Capture needed: {what} (nothing captured yet, so no rows).";

    public static int IntParam(BoundParams p, string key, string label, int def, int min, int max)
    {
        var v = p.Values.GetValueOrDefault(key);
        if (string.IsNullOrWhiteSpace(v)) return def;
        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw HccException.Validation($"{label}: a whole number from {min} to {max}.");
        return n;
    }

    public static string Text(BoundParams p, string key) => p.Values.GetValueOrDefault(key) ?? "";

    /// <summary>A red-row-carrying computed row (the digest lane and the tests read Red).</summary>
    public sealed record Computed(Dictionary<string, object?> Cells, bool Red);

    public static ReportRow Row(Computed c) => new(ReportRowKind.Row, c.Cells);

    public static ReportResult Result(string id, ReportDefinition def, BoundParams p, IReadOnlyList<ReportColumn> columns, IReadOnlyList<ReportRow> rows, string? basis = null, DateOnly? from = null, DateOnly? to = null)
        => new(id, def.Title, p, columns, rows, new ReportAbout(DateTimeOffset.MinValue, "", basis ?? def.DateBasis, false, p.Legacy, from ?? p.From, to ?? p.To));

    public static RType? FindType(IReadOnlyList<RType> types, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var t = types.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || x.Id.ToString(CultureInfo.InvariantCulture) == name);
        return t ?? throw HccException.Validation($"Room type: choose one of {string.Join(", ", types.Select(x => x.Name))}.");
    }

    public static Task<GuestsSql.Clock> ClockAsync(Hcc.Infrastructure.SqlServer.SqlPlatform platform, int propertyId, CancellationToken ct) =>
        GuestsSql.ClockAsync(platform.Connections, propertyId, ct);

    public static DateTime NowUtc(TimeProvider? clock) => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
}
