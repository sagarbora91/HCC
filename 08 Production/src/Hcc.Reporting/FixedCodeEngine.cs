// F7-R1 · OP-13 computed-column hook (RPT-ARCHITECTURE §6): fixed-code functions over other columns of the row — the same results as the prototype
// (computed.ts): integer paise, half away from zero; pct = percent with one decimal.
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting;

public sealed partial class FixedCodeEngine : IComputedColumnEngine
{
    private static readonly Dictionary<string, Func<long[], object>> Fns = new(StringComparer.Ordinal)
    {
        ["add"] = a => a.Sum(),
        ["sub"] = a => a.Length == 0 ? 0L : a.Skip(1).Aggregate(a[0], (x, y) => x - y),
        ["div"] = a => DivRound(a.ElementAtOrDefault(0), a.ElementAtOrDefault(1)),
        ["pct"] = a => DivRound(a.ElementAtOrDefault(0) * 1000, a.ElementAtOrDefault(1)) / 10.0,
        ["neg"] = a => -a.ElementAtOrDefault(0),
    };

    [GeneratedRegex("^([a-z]+)\\(([a-zA-Z0-9_,\\s]*)\\)$")]
    private static partial Regex Parse();

    /// <summary>n / d rounded half away from zero (0 when d = 0).</summary>
    public static long DivRound(long n, long d)
    {
        if (d == 0) return 0;
        var r = (Math.Abs(n) * 2 + Math.Abs(d)) / (2 * Math.Abs(d));
        return (n < 0) != (d < 0) ? -r : r;
    }

    public bool CanEvaluate(ReportComputed computed)
    {
        ArgumentNullException.ThrowIfNull(computed);
        var m = Parse().Match(computed.Expr);
        return m.Success && Fns.ContainsKey(m.Groups[1].Value);
    }

    public object? Evaluate(ReportComputed computed, IReadOnlyDictionary<string, object?> cells)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(cells);
        var m = Parse().Match(computed.Expr);
        if (!m.Success || !Fns.TryGetValue(m.Groups[1].Value, out var fn)) throw new InvalidOperationException($"No engine evaluates {computed.Expr}.");
        var args = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vals = new long[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            if (!cells.TryGetValue(args[i], out var v) || v is null) return null;
            vals[i] = v switch { long l => l, int n => n, double x => (long)x, decimal x => (long)x, _ => Convert.ToInt64(v, CultureInfo.InvariantCulture) };
        }

        return fn(vals);
    }
}
