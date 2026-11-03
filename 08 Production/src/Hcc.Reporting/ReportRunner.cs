// F7-R1 · the engine (RPT-ARCHITECTURE §1): bind → permission (rule V) → select once → computed → money removal (rule M) → masks → page.
// Every renderer takes the result this returns; figures are identical by construction.
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Reporting;

/// <summary>Code, name and business date of the property (SQL: inv.property).</summary>
public sealed record ReportProperty(string Code, string Name, DateOnly BusinessDate);

public interface IReportPropertyInfo
{
    Task<ReportProperty> GetAsync(int propertyId, CancellationToken ct = default);
}

public sealed record RunOptions(int? Page = null, int PageSize = ReportRunner.PageDesktop, bool All = false, bool Export = false, string? ExportFormat = null);

public sealed class ReportRunner(IReportCatalogue catalogue, IAccessPolicy policy, IReportPropertyInfo property, IEnumerable<IComputedColumnEngine> engines, TimeProvider clock)
{
    public const int PageDesktop = 100;
    public const int PagePhone = 50;
    public const int FileMax = 50_000;
    public const string DefaultExportPerm = "PERM-RPT-003";
    private readonly IReadOnlyList<IComputedColumnEngine> _engines = engines.Any() ? engines.ToList() : [new FixedCodeEngine()];

    private Task<bool> HoldsAsync(Caller c, string perm, bool ownShift, CancellationToken ct) =>
        policy.HasAsync(c.PropertyId, c.PrincipalId, perm, ownShift ? null : new AccessScope(AccessScopeKinds.Property, 0), ct);

    public async Task<bool> CanViewAsync(Caller c, ReportDefinition d, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(d);
        foreach (var p in d.ViewPerms.Concat(d.DeskPerms)) if (await HoldsAsync(c, p, d.OwnShift, ct).ConfigureAwait(false)) return true;
        return false;
    }

    public async Task<bool> CanExportAsync(Caller c, ReportDefinition d, CancellationToken ct = default) =>
        await CanViewAsync(c, d, ct).ConfigureAwait(false) && await HoldsAsync(c, string.IsNullOrEmpty(d.ExportPerm) ? DefaultExportPerm : d.ExportPerm, false, ct).ConfigureAwait(false);

    public async Task<bool> HoldsMoneyGateAsync(Caller c, ReportDefinition d, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(d);
        return string.IsNullOrEmpty(d.MoneyGate) || await HoldsAsync(c, d.MoneyGate, d.OwnShift, ct).ConfigureAwait(false);
    }

    /// <summary>Definitions the caller may view, with export / money flags.</summary>
    public async Task<IReadOnlyList<ReportCatalogueEntry>> CatalogueAsync(Caller c, CancellationToken ct = default)
    {
        var list = new List<ReportCatalogueEntry>();
        foreach (var d in catalogue.All)
            if (await CanViewAsync(c, d, ct).ConfigureAwait(false))
                list.Add(new ReportCatalogueEntry(d, await CanExportAsync(c, d, ct).ConfigureAwait(false), await HoldsMoneyGateAsync(c, d, ct).ConfigureAwait(false)));
        return list;
    }

    public async Task<ReportResult> RunAsync(Caller caller, string id, IReadOnlyDictionary<string, string?> raw, RunOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var o = options ?? new RunOptions();
        var sel = catalogue.Find(id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"No report {id}.");
        var def = sel.Definition;
        if (!await CanViewAsync(caller, def, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, $"You cannot open {def.Title}.");
        if (o.Export && !await CanExportAsync(caller, def, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Exporting {def.Title} needs {(string.IsNullOrEmpty(def.ExportPerm) ? DefaultExportPerm : def.ExportPerm)}.");
        if (o.ExportFormat is { } f && !def.Formats.Any(x => string.Equals(x.ToString(), f, StringComparison.OrdinalIgnoreCase)))
            throw HccException.Validation($"{def.Title} is not offered as {f}.");
        var prop = await property.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var bound = ParamBinder.Bind(def, raw, prop.BusinessDate);
        var r = await sel.SelectAsync(caller, bound, ct).ConfigureAwait(false);
        var money = await HoldsMoneyGateAsync(caller, def, ct).ConfigureAwait(false);
        var names = await HoldsAsync(caller, "PERM-RES-001", false, ct).ConfigureAwait(false) || await HoldsAsync(caller, "PERM-FOL-001", false, ct).ConfigureAwait(false);
        var removed = new List<string>();
        var tables = (r.Sections is { Count: > 0 } s ? s : [new ReportTable("main", null, r.Columns, r.Rows)])
            .Select(t => MoneyMasker.Compute(t, _engines))
            .Select(t => money ? t : MoneyMasker.RemoveMoney(t, removed))
            .Select(t => MoneyMasker.ApplyMasks(t, names))
            .Where(t => t.Columns.Count > 0).ToList();
        if (tables.Count == 0) throw new HccException(HccErrorCode.FORBIDDEN, $"{def.Title} shows only money, and you do not hold {def.MoneyGate}.");
        var total = tables.Sum(t => t.Rows.Count);
        if (o.All && total > FileMax) throw new HccException(HccErrorCode.VALIDATION, $"RPT_TOO_LARGE: {total} rows is more than {FileMax}. Narrow the range.");
        int pageIndex = 0, pageSize = 0;
        if (!o.All && o.Page is { } pg && tables.Count == 1)
        {
            pageIndex = Math.Max(0, pg); pageSize = o.PageSize;
            tables[0] = tables[0] with { Rows = tables[0].Rows.Skip(pageIndex * pageSize).Take(pageSize).ToList() };
        }

        var provisional = r.About.Provisional || (bound.To is { } to && to >= prop.BusinessDate);
        var about = new ReportAbout(clock.GetUtcNow(), string.IsNullOrEmpty(r.About.User) ? string.Create(CultureInfo.InvariantCulture, $"user #{caller.PrincipalId}") : r.About.User, def.DateBasis, provisional, bound.Legacy || r.About.Legacy, bound.From, bound.To);
        var multi = tables.Count > 1 || r.Sections is { Count: > 0 };
        return new ReportResult(def.Id, def.Title, bound, tables[0].Columns, tables[0].Rows, about,
            removed.Count > 0 ? removed.Distinct().ToList() : null, pageIndex, pageSize, total, multi ? tables : null);
    }

    /// <summary>The tables of a result: its sections, else its one table.</summary>
    public static IReadOnlyList<ReportTable> Tables(ReportResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Sections is { Count: > 0 } s ? s : [new ReportTable("main", null, r.Columns, r.Rows)];
    }
}

public sealed record ReportCatalogueEntry(ReportDefinition Definition, bool CanExport, bool Money);
