namespace Hcc.Domain.Reporting;

/// <summary>F12 MEAS (meas-digest): one exception line of the Owner digest block "Measurement". Key = item switch (meas.pack.item), ReportId = drill target.
/// Mirrors the prototype measure-digest.ts.</summary>
public sealed record MeasRedRow(string Key, string ReportId, string Text, string Severity = "red");

/// <summary>The block: rows of the switched-on, subscribed items, red first. Checked = items evaluated.</summary>
public sealed record MeasDigestBlock(DateOnly Date, IReadOnlyList<MeasRedRow> Rows, int Checked);

/// <summary>Pure composer. The caller evaluates each report's exception function (the Phase B report lanes' integrator wires them) and passes the rows per item key,
/// plus the measurement gate answer (<paramref name="isOn"/>, module + pack + item) and the Owner's line subscription (<paramref name="subscribed"/>, default true).
/// Off or unsubscribed items are absent; no evaluated item at all = no block (null).</summary>
public static class MeasurementDigest
{
    public static MeasDigestBlock? Compose(DateOnly date, IReadOnlyDictionary<string, IReadOnlyList<MeasRedRow>> rowsByKey, Func<string, bool> isOn, Func<string, bool>? subscribed = null)
    {
        ArgumentNullException.ThrowIfNull(rowsByKey);
        ArgumentNullException.ThrowIfNull(isOn);
        var rows = new List<MeasRedRow>();
        var checkedItems = 0;
        foreach (var (key, list) in rowsByKey.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!isOn(key) || !(subscribed?.Invoke(key) ?? true)) continue;
            checkedItems++;
            rows.AddRange(list.Where(r => r.Key == key)); // a provider reports only on its own switch
        }
        if (checkedItems == 0) return null;
        return new MeasDigestBlock(date, [.. rows.Where(r => r.Severity == "red"), .. rows.Where(r => r.Severity != "red")], checkedItems);
    }

    /// <summary>E-mail lines (money allowed; Owner and Accountant): "  [rpt.money.high-balance] text".</summary>
    public static IReadOnlyList<string> EmailLines(MeasDigestBlock? b)
        => b is null ? [] : [$"Measurement exceptions: {b.Rows.Count}", .. (b.Rows.Count == 0 ? ["  none today"] : b.Rows.Select(r => $"  [{r.ReportId}] {r.Text}"))];

    /// <summary>WhatsApp line (DEC-202 #7): a count only.</summary>
    public static IReadOnlyList<string> WhatsAppLines(MeasDigestBlock? b) => b is null ? [] : [$"Measurement exceptions {b.Rows.Count}"];
}
