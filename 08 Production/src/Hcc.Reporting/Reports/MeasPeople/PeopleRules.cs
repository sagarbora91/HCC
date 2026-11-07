// F12-RPT-PEOPLE · pure rules and shared SQL helpers of the five people measurement reports (server twin of 03 Prototype/src/domain/reports/meas-people/*).
// Integer arithmetic only; hours and days, never a pay amount. Every method is the same arithmetic as its TypeScript twin so the boundary tests agree.
using System.Data;
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Contracts;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasPeople;

public readonly record struct ProdFigures(long RosterMin, long OtMin, long ScheduledDays, long AbsentHalves, long OccRoomNights, long LowRosterMin, long LowRooms);

public readonly record struct ProdRule(bool Ot, bool Absent, bool OverRostered);

public sealed record TrainingRec(string Id, string Staff, string Course, DateOnly Date, double Hours, string? CertificateNo, DateOnly? ValidTo, bool Mandatory);

public static class PeopleRules
{
    public const int PulseMinAnswers = 5;

    public static readonly IReadOnlyList<(string Match, string Label, string? Dept)> MandatoryCourses =
        [("fire", "Fire safety", null), ("first aid", "First aid", null), ("posh", "POSH", null), ("fssai", "FSSAI handler", "KIT")];

    public static (string Match, string Label, string? Dept)? MandatoryKey(string course) =>
        MandatoryCourses.Where(m => course.Contains(m.Match, StringComparison.OrdinalIgnoreCase)).Select(m => ((string, string, string?)?)m).FirstOrDefault();

    /// <summary>One decimal of num/den as a percentage (half away from zero); 0 when den is not positive.</summary>
    public static double Pct1(long num, long den) => den > 0 ? Math.Round(num * 1000.0 / den, MidpointRounding.AwayFromZero) / 10 : 0;

    /// <summary>Shift length in minutes from "HH:MM" start / end (an end at or before the start crosses midnight).</summary>
    public static int ShiftMinutes(string start, string end)
    {
        static int Min(string t) { var p = t.Split(':'); return int.Parse(p[0], CultureInfo.InvariantCulture) * 60 + int.Parse(p[1], CultureInfo.InvariantCulture); }
        int a = Min(start), b = Min(end);
        return b > a ? b - a : b + 1440 - a;
    }

    /// <summary>The seeded shift patterns (07-15, 15-23, 23-07, 09-18); the prototype reads the Shift patterns master, the server the same four.</summary>
    public static int ShiftMinutesOf(string code) => code switch { "M" => ShiftMinutes("07:00", "15:00"), "E" => ShiftMinutes("15:00", "23:00"), "N" => ShiftMinutes("23:00", "07:00"), "G" => ShiftMinutes("09:00", "18:00"), _ => 0 };

    public static ProdRule ProductivityRule(ProdFigures f, long otTarget, long absentTarget)
    {
        var ot = f.RosterMin > 0 && f.OtMin * 100 > otTarget * f.RosterMin;
        var absent = f.ScheduledDays > 0 && f.AbsentHalves * 100 > absentTarget * 2 * f.ScheduledDays;
        var over = f.LowRosterMin > 0 && (f.OccRoomNights == 0 || f.LowRosterMin * f.OccRoomNights * 100 > 120 * f.LowRooms * f.RosterMin);
        return new ProdRule(ot, absent, over);
    }

    public static bool AttritionRed(long leavers12, long headOpen, long headClose, long limitPct) => leavers12 > 0 && leavers12 * 100 * 2 > limitPct * (headOpen + headClose);

    /// <summary>eNPS = promoters (9-10) minus detractors (0-6) as whole percent of the answers, half away from zero.</summary>
    public static int Enps(IReadOnlyList<int> recommend)
    {
        var n = recommend.Count;
        if (n == 0) return 0;
        var d = (recommend.Count(v => v >= 9) - recommend.Count(v => v <= 6)) * 100;
        return d < 0 ? -(int)Math.Round(-d / (double)n, MidpointRounding.AwayFromZero) : (int)Math.Round(d / (double)n, MidpointRounding.AwayFromZero);
    }

    public static long NearestRank(IReadOnlyList<long> values, double p)
    {
        if (values.Count == 0) return 0;
        var v = values.Order().ToList();
        return v[Math.Max(0, (int)Math.Ceiling(p * v.Count) - 1)];
    }

    /// <summary>Scorecard outlier: a team of at least 4, a value above zero, and above the team's upper quartile (Q3).</summary>
    public static bool AboveUpperQuartile(IReadOnlyList<long> team, long value) => team.Count >= 4 && value > 0 && value > NearestRank(team, 0.75);

    /// <summary>A mandatory certificate is expired on asOf: valid-to before asOf and no later valid record of the same course for the same person.</summary>
    public static bool CertificateExpired(IReadOnlyList<TrainingRec> list, TrainingRec t, DateOnly asOf)
    {
        if (t.ValidTo is not { } vt || vt >= asOf) return false;
        var key = MandatoryKey(t.Course)?.Match;
        return !list.Any(o => o.Staff == t.Staff && o.Id != t.Id && MandatoryKey(o.Course)?.Match == key && (o.ValidTo is null || o.ValidTo >= asOf) && o.Date >= t.Date);
    }

    public static string HoursText(long minutes) => (Math.Round(minutes * 10.0 / 60, MidpointRounding.AwayFromZero) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    public static bool DeptMatches(string code, string name, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || string.Equals(code, filter.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(name, filter.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Shared server helpers: settings targets, the Owner / Manager check, row and column builders.</summary>
internal static class PeopleSql
{
    public static ReportColumn T(string key, string label) => new(key, label, ReportColumnType.Text, false);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static readonly ReportColumn ExceptionCol = new("exception", "Exception", ReportColumnType.Bool, false);

    public static ReportRow Row(Dictionary<string, object?> cells, ReportRowKind kind = ReportRowKind.Row) => new(kind, cells);

    public static void Add(SqlCommand c, string name, SqlDbType type, object? value) => c.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    public static object D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public static async Task<long> TargetAsync(ISettingsReader? settings, int propertyId, string key, CancellationToken ct)
    {
        var def = MeasurementCatalogue.Targets.First(t => t.Key == key).Default;
        if (settings is null) return def;
        try
        {
            var v = await settings.GetAsync(propertyId, key, null, ct).ConfigureAwait(false);
            return v.Kind is ConfigValueKind.Null ? def : v.Number;
        }
        catch (HccException)
        {
            return def;
        }
    }

    /// <summary>Owner and Manager only (PERM-HR-002: the pulse and the scorecard): refuses any other caller whatever wider permission got them here.</summary>
    public static async Task OwnerManagerOnlyAsync(IAccessPolicy policy, Caller caller, string what, CancellationToken ct)
    {
        if (!await policy.HasAsync(caller.PropertyId, caller.PrincipalId, "PERM-HR-002", new AccessScope(AccessScopeKinds.Property, 0), ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, $"{what} is for the Owner and Manager only.");
    }

    public static ReportAbout About(ReportDefinition def, BoundParams p, string basis) => new(DateTimeOffset.MinValue, "", basis, false, p.Legacy, p.From, p.To);

    public static string FyOf(DateOnly d) { var y = d.Month >= 4 ? d.Year : d.Year - 1; return $"{y}-{(y + 1) % 100:00}"; }

    public static string QuarterKey(DateOnly d)
    {
        var y = d.Month >= 4 ? d.Year : d.Year - 1;
        var q = d.Month >= 4 ? (d.Month - 4) / 3 + 1 : 4;
        return $"{y}-{(y + 1) % 100:00}-Q{q}";
    }
}
