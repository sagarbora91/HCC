using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.Import;

namespace Hcc.Domain.Import;

/// <summary>
/// Plans a file of a list master (<see cref="ImportLists"/>) against the property's current rows: cell parsing into canonical text,
/// the owning module's rules (row checks), cross-row rules (file checks), then Create / Update / Unchanged per row. A file never retires an
/// active row or deletes anything; an absent optional column leaves existing rows as they are; a blank cell of a present column clears it
/// (the file is the whole row), except sort order, active and dated amounts (blank keeps).
/// </summary>
internal static partial class ImportListPlanner
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    [GeneratedRegex(@"^([01]?\d|2[0-3])[:.]([0-5]\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static void Plan(ImportPlanner.Sheet s, ImportSnapshot snap, ListDef d)
    {
        var existing = snap.Lists is { } l && l.TryGetValue(d.Wire, out var rows) ? rows : [];
        var byCode = new Dictionary<string, ListSnapRow>(Ci);
        foreach (var e in existing) byCode.TryAdd(e.Code, e);
        var seen = new Dictionary<string, int>(Ci);
        var nextSort = existing.Select(e => e.SortOrder).DefaultIfEmpty(0).Max();
        var inFile = new List<(CsvRow Row, ListRowData Data, ListSnapRow? Have, bool Active)>();
        var codePattern = new Regex($"^[A-Z0-9-]{{1,{d.CodeMax}}}$", RegexOptions.CultureInvariant);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = ReadCode(s, r, d, codePattern);
            var name = s.Required(r, "name", 60, "name");
            ListSnapRow? have = code is not null && byCode.TryGetValue(code, out var h) ? h : null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in d.Fields) values[f.Key] = ReadField(s, r, f, snap, have) ?? string.Empty;
            var sort = ReadSort(s, r);
            var wanted = ReadActive(s, r);
            if (code is null || Duplicate(s, seen, r, code) || s.Errors.Count > errs) continue;
            if (have is { Active: true } && wanted == false)
            {
                s.Error(r, "active", $"{Cap(d.Singular)} {code} is active in HCC. A file does not retire rows: retire it on its list, where its use is checked.");
                continue;
            }

            var active = wanted ?? have?.Active ?? true;
            if (have is not null)
            {
                var drift = d.Frozen.FirstOrDefault(k => !string.Equals(Canon(have.Values.GetValueOrDefault(k)), Canon(values.GetValueOrDefault(k)), StringComparison.Ordinal));
                if (drift is not null)
                {
                    s.Error(r, drift, string.Format(CultureInfo.InvariantCulture, d.FrozenMessage, code));
                    continue;
                }
            }

            var check = new ListRowCheck(d, values, have, code, name!, snap, active);
            d.Check?.Invoke(check);
            if (check.Problems.Count > 0)
            {
                foreach (var p in check.Problems) s.Error(r, p.Column, p.Message);
                continue;
            }

            var order = sort ?? have?.SortOrder ?? Math.Min(MaxSort, (nextSort / 10 + 1) * 10);
            if (have is null && sort is null) nextSort = order;
            else if (sort is { } so && so > nextSort) nextSort = so;
            var changes = Diff(d, have, name!, order, active, values);
            var data = new ListRowData(d.Wire, code, name!, order, wanted, values, have?.Id, changes);
            inFile.Add((r, data, have, active));
            if (have is null) s.Item(r, ImportAction.Create, code, $"New {d.Singular} {name}{Summary(d, values)}", data);
            else if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, have.Code, $"Already in HCC: {name}", data);
            else s.Item(r, ImportAction.Update, have.Code, string.Join(" · ", changes.Select(c => $"{ShowField(d, c.Field)} {c.Old ?? "—"} → {c.New ?? "—"}")), data);
        }

        if (s.Errors.Count > 0) return;
        var fileCodes = inFile.ToDictionary(x => x.Data.Code, Ci);
        var final = new List<ListFinalRow>();
        foreach (var e in existing.Where(e => !fileCodes.ContainsKey(e.Code))) final.Add(new ListFinalRow(null, e.Code, e.Name, e.Active, e.Values));
        foreach (var x in inFile) final.Add(new ListFinalRow(x.Row.Row, x.Data.Code, x.Data.Name, x.Active, x.Data.Values));
        foreach (var g in final.Where(f => f.Active).GroupBy(f => f.Name, Ci).Where(g => g.Count() > 1))
            foreach (var f in g.Where(x => x.FileRow is not null))
                s.Errors.Add(new ImportRowError(f.FileRow!.Value, "name", $"Another active {d.Singular} is already called “{f.Name}”."));
        var fc = new ListFileCheck(d, final, snap);
        d.FileCheck?.Invoke(fc);
        foreach (var (row, p) in fc.Problems) s.Errors.Add(new ImportRowError(row, p.Column, p.Message));
    }

    private const int MaxSort = 1_000_000;

    private static string Cap(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];

    private static string Canon(string? v) => v ?? string.Empty;

    private static bool Duplicate(ImportPlanner.Sheet s, Dictionary<string, int> seen, CsvRow r, string key)
    {
        if (seen.TryGetValue(key, out var first))
        {
            s.Error(r, "code", $"Duplicate of row {I(first)}.");
            return true;
        }

        seen[key] = r.Row;
        return false;
    }

    private static string? ReadCode(ImportPlanner.Sheet s, CsvRow r, ListDef d, Regex pattern)
    {
        var raw = s.Cell(r, "code");
        if (raw.Length == 0)
        {
            s.Error(r, "code", "Enter the code.");
            return null;
        }

        if (d.CodeIsDate)
        {
            if (ImportValues.TryDate(raw, out var date)) return ImportValues.Wire(date);
            s.Error(r, "code", $"'{raw}' is not a date. Use YYYY-MM-DD, for example 2026-11-08.");
            return null;
        }

        var code = raw.ToUpperInvariant();
        if (pattern.IsMatch(code)) return code;
        s.Error(r, "code", d.CodeMax < 20 ? $"A code is 1 to {I(d.CodeMax)} capital letters, digits or '-'." : "A code is 1 to 20 capital letters, digits or '-'.");
        return null;
    }

    private static int? ReadSort(ImportPlanner.Sheet s, CsvRow r)
    {
        var t = s.Cell(r, "sortOrder");
        if (t.Length == 0) return null;
        if (ImportValues.TryInt(t, out var v) && v is >= 0 and <= MaxSort) return v;
        s.Error(r, "sortOrder", "Sort order is a whole number from 0 to 1,000,000.");
        return null;
    }

    private static bool? ReadActive(ImportPlanner.Sheet s, CsvRow r)
    {
        var t = s.Cell(r, "active");
        if (t.Length == 0) return null;
        if (TryBool(t, out var b)) return b;
        s.Error(r, "active", "Enter yes or no.");
        return null;
    }

    public static bool TryBool(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "yes" or "y" or "true" or "1" or "on": value = true; return true;
            case "no" or "n" or "false" or "0" or "off": value = false; return true;
            default: value = false; return false;
        }
    }

    private static string? ReadField(ImportPlanner.Sheet s, CsvRow r, ListField f, ImportSnapshot snap, ListSnapRow? have)
    {
        var kept = have?.Values.GetValueOrDefault(f.Key);
        if (!s.HasColumn(f.Key)) return kept ?? f.Default; // absent optional column: leave as it is
        var cell = s.Cell(r, f.Key);
        if (cell.Length == 0)
        {
            if (f.Required)
            {
                s.Error(r, f.Key, $"Enter the {f.Label}.");
                return null;
            }

            if (f.Dated && have is not null) return kept ?? string.Empty;
            return f.Default;
        }

        switch (f.Kind)
        {
            case ListKind.Text:
            {
                var max = f.Max > 0 ? f.Max : 200;
                if (cell.Length <= max) return cell;
                s.Error(r, f.Key, $"The {f.Label} is at most {I(max)} characters.");
                return null;
            }

            case ListKind.Int:
                if (ImportValues.TryInt(cell, out var n) && n >= f.Min && n <= f.Hi) return I(n);
                s.Error(r, f.Key, f.Hi == long.MaxValue ? $"The {f.Label} is a whole number, {I(f.Min)} or more." : $"The {f.Label} is a whole number from {I(f.Min)} to {f.Hi.ToString("N0", CultureInfo.InvariantCulture)}.");
                return null;
            case ListKind.Bool:
                if (TryBool(cell, out var b)) return b ? "yes" : "no";
                s.Error(r, f.Key, $"The {f.Label} is yes or no.");
                return null;
            case ListKind.Pick:
            {
                var o = f.Options!.FirstOrDefault(x => Ci.Equals(x, cell));
                if (o is not null) return o;
                s.Error(r, f.Key, $"The {f.Label} is one of: {string.Join(", ", f.Options!)}.");
                return null;
            }

            case ListKind.Days:
                if (decimal.TryParse(cell, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var days) && days >= f.Min && days <= f.Hi && decimal.Round(days, 2) == days)
                    return ImportLists.DaysText(days);
                s.Error(r, f.Key, $"The {f.Label} is a number of days, {I(f.Min)} or more (up to 2 decimals).");
                return null;
            case ListKind.Pct:
                if (ImportValues.TryPercentBps(cell, out var bps) && bps >= f.Min && bps <= f.Hi) return ImportLists.PctText(bps);
                s.Error(r, f.Key, $"The {f.Label} is a percentage from {ImportLists.PctText((int)f.Min)} to {ImportLists.PctText((int)f.Hi)}.");
                return null;
            case ListKind.Rupees:
            {
                if (!ImportValues.TryRupees(cell, out var paise, out var rounded, out var err))
                {
                    s.Error(r, f.Key, err!);
                    return null;
                }

                if (rounded) s.Warn(r, f.Key, $"'{cell}' has more than 2 decimals; rounded to {ImportLists.RupeesText(paise)}.");
                return ImportLists.RupeesText(paise);
            }

            case ListKind.Time:
            {
                var m = TimePattern().Match(cell);
                if (m.Success) return $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):00}:{m.Groups[2].Value}";
                s.Error(r, f.Key, $"The {f.Label} is a 24-hour time such as 07:00.");
                return null;
            }

            case ListKind.Date:
                if (ImportValues.TryDate(cell, out var date)) return ImportValues.Wire(date);
                s.Error(r, f.Key, $"'{cell}' is not a date. Use YYYY-MM-DD, for example 2026-11-01.");
                return null;
            case ListKind.Set:
                return ReadSet(s, r, f, cell, snap, have);
            case ListKind.Lines:
            {
                var items = ImportLists.Items(cell);
                if (items.Count < f.Min || items.Count > f.Hi)
                {
                    s.Error(r, f.Key, $"The {f.Label} has {I(f.Min)} to {I(f.Hi)} items separated by ;.");
                    return null;
                }

                if (f.Max > 0 && items.FirstOrDefault(x => x.Length > f.Max) is { } longest)
                {
                    s.Error(r, f.Key, $"The item '{longest[..Math.Min(20, longest.Length)]}…' is longer than {I(f.Max)} characters.");
                    return null;
                }

                return string.Join(';', items);
            }

            case ListKind.Ref:
                return ResolveRef(s, r, f, cell, snap, have);
            default:
                return cell;
        }
    }

    private static string? ReadSet(ImportPlanner.Sheet s, CsvRow r, ListField f, string cell, ImportSnapshot snap, ListSnapRow? have)
    {
        var items = ImportLists.Items(cell);
        var picked = new List<string>();
        foreach (var item in items)
        {
            string? canonical;
            if (f.Options is { } options)
            {
                canonical = options.FirstOrDefault(o => Ci.Equals(o, item));
                if (canonical is null)
                {
                    s.Error(r, f.Key, $"'{item}' is not one of: {string.Join(", ", options)}.");
                    return null;
                }
            }
            else
            {
                canonical = ResolveRef(s, r, f, item, snap, have, set: true);
                if (canonical is null) return null;
            }

            if (!picked.Contains(canonical, Ci)) picked.Add(canonical);
        }

        return string.Join(';', f.Options is { } fixedOptions ? fixedOptions.Where(o => picked.Contains(o, Ci)) : picked.Order(StringComparer.Ordinal));
    }

    private static string? ResolveRef(ImportPlanner.Sheet s, CsvRow r, ListField f, string text, ImportSnapshot snap, ListSnapRow? have, bool set = false)
    {
        var rows = RefRows(snap, f.Ref!);
        var hit = rows.FirstOrDefault(x => Ci.Equals(x.Code, text)) is { Code: not null } a ? a : rows.FirstOrDefault(x => x.Alt is not null && Ci.Equals(x.Alt, text));
        if (hit.Code is null)
        {
            var title = f.Ref == ImportLists.UserRef ? "the users" : ImportMasters.Find(f.Ref)?.Title ?? f.Ref!;
            s.Error(r, f.Key, $"'{text}' is not in HCC yet ({title}). Import or add it first.");
            return null;
        }

        if (!hit.Active)
        {
            var before = have?.Values.GetValueOrDefault(f.Key) ?? string.Empty;
            if (!(set ? ImportLists.Items(before) : [before]).Contains(hit.Code, Ci))
            {
                s.Error(r, f.Key, $"'{hit.Code}' is retired — choose an active one.");
                return null;
            }
        }

        return hit.Code;
    }

    private static IReadOnlyList<(string Code, bool Active, string? Alt)> RefRows(ImportSnapshot snap, string reference) => reference switch
    {
        ImportLists.UserRef => snap.Users.Select(u => (u.Login, true, (string?)u.DisplayName)).ToList(),
        ImportLists.RoomRef => snap.Rooms.Select(x => (x.RoomNo, true, (string?)null)).ToList(),
        ImportLists.RoomTypeRef => snap.RoomTypes.Select(x => (x.Code, true, (string?)x.Name)).ToList(),
        ImportLists.DepartmentRef => (snap.Departments ?? []).Select(x => (x.Code, true, (string?)x.Name)).ToList(),
        ImportLists.OutletRef => (snap.PosOutlets ?? []).Select(x => (x.Code, x.Active, (string?)x.Name)).ToList(),
        _ => (snap.Lists is { } l && l.TryGetValue(reference, out var rows) ? rows : []).Select(x => (x.Code, x.Active, (string?)x.Name)).ToList(),
    };

    // ── changes and display ──

    private static IReadOnlyList<(string Field, string? Old, string? New)> Diff(ListDef d, ListSnapRow? have, string name, int order, bool active, IReadOnlyDictionary<string, string> values)
    {
        var list = new List<(string, string?, string?)>();
        if (have is null) return list;
        if (!string.Equals(have.Name, name, StringComparison.Ordinal)) list.Add(("name", have.Name, name));
        foreach (var f in d.Fields)
        {
            var o = Canon(have.Values.GetValueOrDefault(f.Key));
            var n = Canon(values.GetValueOrDefault(f.Key));
            if (!string.Equals(o, n, StringComparison.Ordinal)) list.Add((f.Key, Show(f, o), Show(f, n)));
        }

        if (have.SortOrder != order) list.Add(("sortOrder", I(have.SortOrder), I(order)));
        if (have.Active != active) list.Add(("active", have.Active ? "yes" : "no", active ? "yes" : "no"));
        return list;
    }

    private static string ShowField(ListDef d, string key) => key switch { "name" => "name", "sortOrder" => "sort order", "active" => "active", _ => d.Field(key)?.Label ?? key };

    private static string? Show(ListField f, string v)
    {
        if (v.Length == 0) return null;
        return f.Kind switch
        {
            ListKind.Rupees => "₹" + decimal.Parse(v, CultureInfo.InvariantCulture).ToString("#,##0.##", CultureInfo.InvariantCulture),
            ListKind.Pct => v + " %",
            ListKind.Set or ListKind.Lines => string.Join(", ", v.Split(';')),
            _ => v,
        };
    }

    private static string Summary(ListDef d, IReadOnlyDictionary<string, string> values)
    {
        var parts = d.Fields.Where(f => values.GetValueOrDefault(f.Key, string.Empty).Length > 0).Select(f => $"{f.Label} {Show(f, values[f.Key])}").Take(6).ToList();
        return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
    }
}
