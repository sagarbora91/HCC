// F7-R1 · parameter binding by date class (RPT-ARCHITECTURE §3.1, catalogue §1.4) — the same rules and messages as the prototype params.ts.
// Defaults come from the property's business date, never the wall clock.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;

namespace Hcc.Reporting;

public static class ParamBinder
{
    /// <summary>First business date of HCC's own data; earlier ranges read the legacy Lodge history.</summary>
    public static readonly DateOnly LegacyBefore = new(2026, 4, 1);

    public static int? MaxDays(ReportDateClass dc) => dc switch
    {
        ReportDateClass.Day => 1, ReportDateClass.Short => 62, ReportDateClass.Business => 366, ReportDateClass.Financial => 366,
        ReportDateClass.Month => 31, ReportDateClass.FinancialYear => 366, ReportDateClass.Forward => 366, _ => null,
    };

    public static string FyOf(DateOnly d)
    {
        var s = d.Month >= 4 ? d.Year : d.Year - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{s}-{(s + 1) % 100:00}");
    }

    private static HccException Bad(string m) => HccException.Validation(m);

    private static DateOnly Date(string v) =>
        DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw Bad("Dates are YYYY-MM-DD.");

    public static BoundParams Bind(ReportDefinition def, IReadOnlyDictionary<string, string?> raw, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(raw);
        string? Str(string k) => raw.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v : null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        DateOnly? from = null, to = null;
        var dc = def.DateClass;
        switch (dc)
        {
            case ReportDateClass.Month:
            {
                var m = Str("month") ?? today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                if (!DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) || m.Length != 7) throw Bad("Choose a month (YYYY-MM).");
                from = f; to = f.AddMonths(1).AddDays(-1);
                if (to > today && from <= today) to = today;
                values["month"] = m;
                break;
            }
            case ReportDateClass.FinancialYear:
            {
                var fy = Str("fy") ?? FyOf(today);
                if (fy.Length != 7 || fy[4] != '-' || !int.TryParse(fy[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                    || !int.TryParse(fy[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var y2) || (y + 1) % 100 != y2)
                    throw Bad("Choose a financial year (for example 2026-27).");
                from = new DateOnly(y, 4, 1); to = new DateOnly(y + 1, 3, 31);
                if (to > today && from <= today) to = today;
                values["fy"] = fy;
                break;
            }
            case ReportDateClass.AsOf:
                to = Date(Str("asOf") ?? Str("to") ?? today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                values["asOf"] = to.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case null or ReportDateClass.Now:
                break;
            default:
                from = Str("from") is { } fs ? Date(fs) : today;
                to = Str("to") is { } ts ? Date(ts) : dc == ReportDateClass.Forward ? today.AddDays(6) : from;
                break;
        }

        if (from is { } a && to is { } b && a > b) throw Bad("The From date is after the To date.");
        if (dc != ReportDateClass.Forward && to > today) throw Bad($"The report cannot run for a date after the business date ({today:yyyy-MM-dd}).");
        if (dc == ReportDateClass.Day && from != to) throw Bad("This report runs for one business date.");
        if (dc is { } c && MaxDays(c) is { } max && from is { } f2 && to is { } t2 && t2.DayNumber - f2.DayNumber + 1 > max) throw Bad($"The range is longer than {max} days. Narrow the range.");
        if (from is { } ff) values["from"] = ff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (to is { } tt) values["to"] = tt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var p in def.Params)
        {
            if (p.Key is "from" or "to" or "month" or "fy" or "asOf") continue;
            var v = Str(p.Key) ?? p.Default;
            if (v is null) { if (p.Required) throw Bad($"{p.Label} is required."); continue; }
            if (p.Type == "bool") { values[p.Key] = v is "true" or "1" or "yes" ? "true" : "false"; continue; }
            if (p.Type == "enum" && p.Options is { } o && !o.Contains(v)) throw Bad($"{p.Label}: choose one of {string.Join(", ", o)}.");
            if (p.Type == "date") _ = Date(v);
            if (v.Length > 120) throw Bad($"{p.Label} is too long.");
            values[p.Key] = v;
        }

        var legacy = from is { } lf ? lf < LegacyBefore : to is { } lt && lt < LegacyBefore;
        return new BoundParams(values, from, to, legacy);
    }
}
