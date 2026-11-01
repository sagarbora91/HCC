namespace Hcc.Domain.Expenses;

/// <summary>
/// ACC-3 acc3-carry C4 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7; DEC-136): the first two digits of a vendor's GSTIN are its GST state
/// code; they should agree with the state the vendor master keeps (empty = the hotel's own state, MH). HCC bills CGST + SGST either way, so a
/// mismatch is a warning, never a refusal. Prototype twin: <c>expVendorGstinWarning</c> (domain/streams/expenses-vendor.ts), same text.
/// </summary>
public static class VendorGstin
{
    public const string HomeState = "MH";

    /// <summary>GST state codes by the two-letter state code of the vendor master (common variants included).</summary>
    public static readonly IReadOnlyDictionary<string, string> StateCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["JK"] = "01", ["HP"] = "02", ["PB"] = "03", ["CH"] = "04", ["UK"] = "05", ["UT"] = "05", ["HR"] = "06", ["DL"] = "07", ["RJ"] = "08", ["UP"] = "09",
        ["BR"] = "10", ["SK"] = "11", ["AR"] = "12", ["NL"] = "13", ["MN"] = "14", ["MZ"] = "15", ["TR"] = "16", ["ML"] = "17", ["AS"] = "18", ["WB"] = "19",
        ["JH"] = "20", ["OD"] = "21", ["OR"] = "21", ["CG"] = "22", ["CT"] = "22", ["MP"] = "23", ["GJ"] = "24", ["DD"] = "25", ["DN"] = "26", ["MH"] = "27",
        ["KA"] = "29", ["GA"] = "30", ["LD"] = "31", ["KL"] = "32", ["TN"] = "33", ["PY"] = "34", ["AN"] = "35", ["TS"] = "36", ["TG"] = "36", ["AP"] = "37",
        ["LA"] = "38",
    };

    /// <summary>The warning text, or null when consistent / nothing to compare.</summary>
    public static string? Warning(string? name, string? gstin, string? state)
    {
        var g = (gstin ?? "").Trim().ToUpperInvariant();
        if (g.Length < 2 || !char.IsAsciiDigit(g[0]) || !char.IsAsciiDigit(g[1])) return null;
        var st = (state ?? "").Trim().ToUpperInvariant();
        if (st.Length == 0) st = HomeState;
        if (!StateCodes.TryGetValue(st, out var code)) return null;
        return g[..2] == code ? null
            : $"GSTIN {g} of {name ?? "the vendor"} starts {g[..2]}, but its GST state is {st} ({code}) — check the vendor master (DEC-136: CGST + SGST are billed either way).";
    }
}
