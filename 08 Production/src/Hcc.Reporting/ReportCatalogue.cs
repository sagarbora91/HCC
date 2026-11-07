// F7-R1 · report definition registry (RPT-ARCHITECTURE §2). Same ids, column keys and flags as the prototype registry; the cross-check test reads
// 03 Prototype/src/domain/reports/catalogue.json (copied into Hcc.Reporting.Tests by tools/rpt-catalogue.mjs).
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting;

public sealed partial class ReportCatalogue : IReportCatalogue
{
    private readonly Dictionary<string, IReportSelector> _byId = new(StringComparer.Ordinal);

    public ReportCatalogue(IEnumerable<IReportSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        foreach (var s in selectors)
        {
            Validate(s.Definition);
            if (!_byId.TryAdd(s.Definition.Id, s)) throw new InvalidOperationException($"Report {s.Definition.Id} is registered twice.");
        }

        All = _byId.Values.Select(s => s.Definition).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<ReportDefinition> All { get; }

    public IReportSelector? Find(string id) => _byId.GetValueOrDefault(id);

    [GeneratedRegex("^rpt\\.(guests|rooms|money|tax|acc|people|stock|rest|admin|revenue|study)\\.[a-z0-9-]+$")]
    private static partial Regex IdPattern();

    public static void Validate(ReportDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!IdPattern().IsMatch(d.Id)) throw new InvalidOperationException($"Report id {d.Id} must be rpt.<area>.<slug>.");
        if (d.Id.Split('.')[1] != d.Area) throw new InvalidOperationException($"Report {d.Id}: area {d.Area} does not match the id.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in d.Columns) if (!keys.Add(c.Key)) throw new InvalidOperationException($"Report {d.Id}: duplicate column {c.Key}.");
        foreach (var c in d.Columns)
            foreach (var dep in c.Computed?.Deps ?? [])
                if (!keys.Contains(dep)) throw new InvalidOperationException($"Report {d.Id}: computed column {c.Key} needs unknown column {dep}.");
        if (d.MoneyFlag && string.IsNullOrEmpty(d.MoneyGate)) throw new InvalidOperationException($"Report {d.Id}: a money report names its money gate.");
        if (d.ViewPerms.Count == 0) throw new InvalidOperationException($"Report {d.Id}: at least one view permission.");
    }
}
