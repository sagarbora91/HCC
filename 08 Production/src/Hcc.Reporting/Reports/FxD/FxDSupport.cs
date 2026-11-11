// FX-RPT-D · shared helpers of the seven stock reports moved from the prototype to the server (reorder, laundry, counts, losses, consumption, par, movements).
// Labels equal the prototype INV_LOCS / INV_KINDS; money = long paise; no wall clock; every query sits behind the stock read store (parameterised, property scoped).
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.FxD;

internal static class FxDSupport
{
    /// <summary>The stock module view permission: quantities. Unit cost, value, laundry charge and loss value need PERM-STK-004 (<see cref="Gate"/>) and are removed without it (rule M).</summary>
    public const string View = "PERM-STK-001";

    public const string Gate = "PERM-STK-004";

    /// <summary>Long location labels (prototype INV_LOCS label).</summary>
    private static readonly IReadOnlyDictionary<string, string> LocLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["store"] = "Main store", ["floor1"] = "Floor 1 pantry", ["floor2"] = "Floor 2 pantry", ["floor3"] = "Floor 3 pantry",
        ["rooms"] = "In rooms", ["soiled"] = "Soiled (to send)", ["laundry"] = "At laundry",
    };

    private static readonly IReadOnlyDictionary<string, string> KindLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["linen"] = "Linen", ["amenity"] = "Amenities", ["cleaning"] = "Cleaning supplies",
    };

    /// <summary>Unknown ids come back unchanged, as <c>invLocLabel</c> (a goods receipt's "from" is the supplier name, a use move goes to "used").</summary>
    public static string Loc(string id) => LocLabels.GetValueOrDefault(id, id);

    public static string Kind(string id) => KindLabels.GetValueOrDefault(id, id);

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) => MoneyRules.Row(kind, null, cells);

    public static ReportParam From() => MoneyRules.Range(ReportDateClass.Business);

    public static ReportParam To() => MoneyRules.RangeTo(ReportDateClass.Business);

    /// <summary>Prototype <c>userName</c>: the display name; empty for no actor; "user N" when the user is unknown.</summary>
    public static string Who(IReadOnlyDictionary<int, string> names, int id) => MoneyRules.NameOf(names, id);

    /// <summary>Prototype <c>invFmt</c>: a whole number as is, else two decimals.</summary>
    public static string Fmt(double n) => n == Math.Floor(n) ? ((long)n).ToString(System.Globalization.CultureInfo.InvariantCulture) : n.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
