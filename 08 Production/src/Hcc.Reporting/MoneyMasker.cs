// F7-R1 · rule M (money columns removed, not blanked, with computed columns over them) and catalogue §1.6 masks, applied to the selector output
// before any renderer. Same behaviour as the prototype mask.ts.
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting;

public static class MoneyMasker
{
    /// <summary>Role codes that never pass a money gate (DEC-061): Housekeeping supervisor, Housekeeper, Maintenance (and a future stock-only role).</summary>
    public static readonly IReadOnlySet<string> NoMoneyRoles = new HashSet<string>(StringComparer.Ordinal) { "SUP", "HKP", "MNT", "STK" };

    /// <summary>Money columns plus computed columns that depend on one (transitively).</summary>
    public static HashSet<string> MoneyColumns(IReadOnlyList<ReportColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var m = columns.Where(c => c.Money).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        bool grew;
        do
        {
            grew = false;
            foreach (var c in columns)
                if (!m.Contains(c.Key) && c.Computed is { } k && k.Deps.Any(m.Contains)) { m.Add(c.Key); grew = true; }
        }
        while (grew);
        return m;
    }

    public static ReportTable RemoveMoney(ReportTable t, List<string> removed)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(removed);
        var m = MoneyColumns(t.Columns);
        if (m.Count == 0) return t;
        removed.AddRange(m);
        return t with
        {
            Columns = t.Columns.Where(c => !m.Contains(c.Key)).ToList(),
            Rows = t.Rows.Select(r => r with { Cells = r.Cells.Where(kv => !m.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) }).ToList(),
        };
    }

    /// <summary><c>J••• T•••</c> / <c>XXXX-XXXX-1234</c> / <c>XXXXXX1234</c>.</summary>
    public static string Mask(ReportMask kind, string v)
    {
        if (string.IsNullOrEmpty(v) || kind == ReportMask.None) return v ?? "";
        if (kind == ReportMask.GuestName)
            return string.Join(' ', v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + "•••"));
        var alnum = new string(v.Where(char.IsLetterOrDigit).ToArray());
        var last = alnum.Length <= 4 ? alnum : alnum[^4..];
        return kind == ReportMask.IdNumber ? "XXXX-XXXX-" + last : "XXXXXX" + last;
    }

    /// <summary>Guest names stay visible when <paramref name="namesVisible"/>; ID numbers are always masked; bank accounts masked here.</summary>
    public static ReportTable ApplyMasks(ReportTable t, bool namesVisible)
    {
        ArgumentNullException.ThrowIfNull(t);
        var mc = t.Columns.Where(c => c.Mask != ReportMask.None && !(c.Mask == ReportMask.GuestName && namesVisible)).ToList();
        if (mc.Count == 0) return t;
        return t with
        {
            Rows = t.Rows.Select(r =>
            {
                var cells = new Dictionary<string, object?>(r.Cells, StringComparer.Ordinal);
                foreach (var c in mc) if (cells.TryGetValue(c.Key, out var v) && v is string s) cells[c.Key] = Mask(c.Mask, s);
                return r with { Cells = cells };
            }).ToList(),
        };
    }

    /// <summary>Fills computed columns the selector left absent (rows, subtotals and totals alike).</summary>
    public static ReportTable Compute(ReportTable t, IReadOnlyList<IComputedColumnEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(engines);
        var comp = t.Columns.Where(c => c.Computed is not null).ToList();
        if (comp.Count == 0) return t;
        return t with
        {
            Rows = t.Rows.Select(r =>
            {
                var cells = new Dictionary<string, object?>(r.Cells, StringComparer.Ordinal);
                foreach (var c in comp)
                {
                    if (cells.ContainsKey(c.Key)) continue;
                    var e = engines.FirstOrDefault(x => x.CanEvaluate(c.Computed!)) ?? throw new InvalidOperationException($"No engine evaluates {c.Computed!.Expr}.");
                    cells[c.Key] = e.Evaluate(c.Computed!, cells);
                }

                return r with { Cells = cells };
            }).ToList(),
        };
    }
}
