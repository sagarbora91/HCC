using System.Globalization;

namespace Hcc.Domain.Compliance;

// SCC tax port (DEC-116) — SCC's calendar over one property's compliance data (the prototype's domain/streams/compliance.ts selectors):
// expandCustom, getVisibleItems, the month page with its 7 stats, overdue dots, the quarterly dashboard, the 30-day Action Center,
// evidence completeness, the 13-column CSV register, the evidence ZIP plan and the ≈₹ payable figures. One property = one SCC firm.

/// <summary>The property's firm profile (SCC saveFirm L1187–1216).</summary>
public sealed record CmpProfile(string Name, string Pan, string Gstin, string Entity, string GstType, bool HasEmployees, bool Qrmp, string Notes);

/// <summary>One record per FY and card (SCC compliance[firm][fy][itemId]); done and NA are independent flags, NA wins on screen.</summary>
public sealed record CmpRecord(int FyStart, string ItemId, bool Done, bool Na, DateOnly? FiledOn, string? Notes, string? EvidenceRef, string? ReviewerNote,
    DateOnly? DueDate, int? DoneBy, DateTime? DoneUtc, int? NaBy, DateTime? NaUtc, string? Legacy);

/// <summary>SCC custom obligation (L1382–1404); id "cust-&lt;CustomId&gt;".</summary>
public sealed record CmpCustom(long CustomId, string Cat, string Name, string Freq, int DueDay, IReadOnlyList<string> Months, string Form, string Who, string What,
    string Why, string Penalty, string Responsible, string Reviewer, string? Risk, int? Buffer, IReadOnlyList<string> Checklist, IReadOnlyList<string> Workflow,
    IReadOnlyList<string> Evidence)
{
    public string Id => "cust-" + CustomId.ToString(CultureInfo.InvariantCulture);
}

public sealed record CmpEvidence(long EvidenceId, int FyStart, string ItemId, string FileName, string ContentType, int SizeBytes, int AddedBy, DateTime AddedUtc);

public sealed record CmpCardView(SccCard Card, string Month, int Fy, CmpRecord? Record, bool IsNa, bool IsDone, DateOnly? Due, int? Days, bool IsOverdue, SccDaysTag? Tag,
    SccControl Control, string Name, string Form, int Files);

public sealed record CmpMonthStats(int Total, int Completed, int Pending, int HighRisk, int NotApplicable, int Overdue, string Evidence);

public sealed record CmpAction(string ItemId, string Month, string Name, string Form, string Due, DateOnly? DueDate, string Cat, int Days);

public sealed record CmpQuarterRow(string Month, int Total, int Done, int Overdue, int Score);

public sealed record CmpQuarterItem(SccCard Card, string Month, SccControl Control, int? Days);

public sealed record CmpQuarterView(string Key, string Label, int Total, int Done, int Pending, int Overdue, int HighRisk, int Score, IReadOnlyList<CmpQuarterItem> Items,
    IReadOnlyList<(string Key, string Label, int Total, int Score)> Quarters, IReadOnlyList<(string Owner, int Count)> OwnerLoad, int CashHigh, int CashMedium,
    int CashLow, IReadOnlyList<CmpQuarterRow> Monthly, IReadOnlyList<CmpQuarterItem> Focus, string Period);

public sealed record CmpEvidenceStatus(int ApplicableTotal, int DoneTotal, int DoneSatisfied, int Pct, IReadOnlyList<(string Id, string Name, string Month, string Cat)> Missing, bool NoFirm);

/// <summary>One month of the statutory-payable feed in paise (HCC's equivalent of SCC's saagar_tax_payable entry).</summary>
public sealed record CmpPayableMonth(string Ym, long Pf, long Esic, long Pt, long Gst, IReadOnlyList<string> Sources);

public sealed class SccCalendar
{
    private readonly Dictionary<(int, string), CmpRecord> _records;

    public SccCalendar(CmpProfile? profile, IEnumerable<CmpRecord> records, IEnumerable<CmpCustom> custom, IEnumerable<string> archived, IEnumerable<string> archivedCats,
        IEnumerable<CmpEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(records);
        Profile = profile;
        _records = records.ToDictionary(r => (r.FyStart, r.ItemId));
        Custom = custom?.ToList() ?? [];
        Archived = new HashSet<string>(archived ?? [], StringComparer.Ordinal);
        ArchivedCats = new HashSet<string>(archivedCats ?? [], StringComparer.Ordinal);
        Evidence = evidence?.ToList() ?? [];
    }

    public CmpProfile? Profile { get; }

    public IReadOnlyList<CmpCustom> Custom { get; }

    public IReadOnlySet<string> Archived { get; }

    public IReadOnlySet<string> ArchivedCats { get; }

    public IReadOnlyList<CmpEvidence> Evidence { get; }

    /// <summary>JavaScript Math.round (halves up), on SCC's own operation order (a / b * 100).</summary>
    private static int JsRound(double v) => (int)Math.Floor(v + 0.5);

    // ── Cards ──

    /// <summary>SCC expandCustom (L1658–1704): due day 0 = "Last day of &lt;Mon&gt;", else "&lt;d&gt; &lt;Mon&gt;" in that month (no clamping).</summary>
    public IReadOnlyList<SccCard> ExpandCustom(string month)
    {
        var abbr = month.Length > 3 ? month[..3] : month;
        var list = new List<SccCard>();
        foreach (var c in Custom)
        {
            if (!c.Months.Contains(month)) continue;
            var due = c.DueDay == 0 ? "Last day of " + abbr : c.DueDay.ToString(CultureInfo.InvariantCulture) + " " + abbr;
            list.Add(new SccCard(c.Id + "-" + abbr, string.IsNullOrEmpty(c.Cat) ? "it" : c.Cat, string.IsNullOrEmpty(c.Name) ? "Custom Obligation" : c.Name, due, false,
                string.IsNullOrEmpty(c.Freq) ? "Monthly" : c.Freq, c.Form, c.Who, c.What, c.Why, c.Penalty)
            {
                IsCustom = true, Gid = c.Id, Responsible = c.Responsible, Reviewer = c.Reviewer, Risk = c.Risk ?? string.Empty, Buffer = c.Buffer,
                Checklist = c.Checklist, Workflow = c.Workflow, Evidence = c.Evidence,
            });
        }

        return list;
    }

    /// <summary>SCC getMonthItems (L1710).</summary>
    public IReadOnlyList<SccCard> MonthItems(string month) => [.. SccTaxCatalogue.BuiltIn(month), .. ExpandCustom(month)];

    /// <summary>SCC getVisibleItems (L1613–1628); no profile = SCC "no firm selected" = type 'both'.</summary>
    public IReadOnlyList<SccCard> Visible(IEnumerable<SccCard> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var type = Profile?.GstType ?? "both";
        var noEmployees = Profile is { HasEmployees: false };
        return items.Where(item =>
        {
            if (ArchivedCats.Contains(item.Cat)) return false;
            if (Archived.Contains(SccTaxRules.ObligationKey(item))) return false;
            if (noEmployees && SccTaxRules.IsPayrollItem(item)) return false;
            return item.Cat switch
            {
                "it" or "lab" => true,
                "gst" => type is "regular" or "both",
                "comp" => type is "composition" or "both",
                _ => true,
            };
        }).ToList();
    }

    public IReadOnlyList<SccCard> MonthCards(string month) => Visible(MonthItems(month));

    /// <summary>SCC findItemMonth (L269–274).</summary>
    public string? ItemMonth(string itemId) => SccTaxCatalogue.BuiltInMonth(itemId) ?? SccTaxCatalogue.Months.FirstOrDefault(m => ExpandCustom(m).Any(c => c.Id == itemId));

    public SccCard? FindCard(string itemId) => ItemMonth(itemId) is { } m ? MonthItems(m).FirstOrDefault(c => c.Id == itemId) : null;

    // ── Records ──

    public CmpRecord? Record(int fy, string itemId) => _records.TryGetValue((fy, itemId), out var r) ? r : null;

    public bool IsNa(int fy, string itemId) => Record(fy, itemId)?.Na == true;

    /// <summary>SCC isItemDone (L248): the done flag (callers check NA first).</summary>
    public bool IsDone(int fy, string itemId) => Record(fy, itemId)?.Done == true;

    public int FileCount(int fy, string itemId) => Evidence.Count(e => e.FyStart == fy && e.ItemId == itemId);

    public CmpCardView View(SccCard card, string month, int fy, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(card);
        var rec = Record(fy, card.Id);
        var na = rec?.Na == true;
        var done = !na && rec?.Done == true;
        var days = SccTaxRules.DaysLeft(card.Due, fy, today);
        return new CmpCardView(card, month, fy, rec, na, done, SccTaxRules.ParseDue(card.Due, fy), days, !na && !done && days < 0, SccTaxRules.DaysTag(days, done),
            SccTaxRules.ControlProfile(card, fy, today), SccTaxRules.RenderFyText(card.Name, fy), SccTaxRules.RenderFyText(card.Form, fy), FileCount(fy, card.Id));
    }

    /// <summary>SCC renderMonth (L2427–2451): the visible cards and the 7 stats over the non-NA ones.</summary>
    public (IReadOnlyList<CmpCardView> Cards, CmpMonthStats Stats) MonthPage(string month, int fy, DateOnly today)
    {
        var all = MonthCards(month).Select(c => View(c, month, fy, today)).ToList();
        var active = all.Where(v => !v.IsNa).ToList();
        var ev = EvidenceStatus(month, fy);
        return (all, new CmpMonthStats(active.Count, active.Count(v => IsDone(fy, v.Card.Id)), active.Count(v => !IsDone(fy, v.Card.Id)),
            active.Count(v => v.Control.Risk == "High"), all.Count - active.Count, active.Count(v => !IsDone(fy, v.Card.Id) && (v.Days ?? 999) < 0),
            ev.DoneTotal > 0 ? $"{ev.DoneSatisfied.ToString(CultureInfo.InvariantCulture)}/{ev.DoneTotal.ToString(CultureInfo.InvariantCulture)}" : "0/0"));
    }

    /// <summary>SCC hasOverdueInMonth (L2120–2128).</summary>
    public bool HasOverdueInMonth(string month, int fy, DateOnly today) =>
        MonthCards(month).Any(i => !IsNa(fy, i.Id) && !IsDone(fy, i.Id) && SccTaxRules.DaysLeft(i.Due, fy, today) is < 0);

    // ── Dashboard (SCC renderDashboard L2478–2511) ──

    public CmpQuarterView Quarter(string key, int fy, DateOnly today)
    {
        var data = SccTaxRules.Quarters.Select(q =>
        {
            var items = q.Months.SelectMany(m => MonthCards(m).Where(c => !IsNa(fy, c.Id))
                .Select(c => new CmpQuarterItem(c, m, SccTaxRules.ControlProfile(c, fy, today), SccTaxRules.DaysLeft(c.Due, fy, today)))).ToList();
            var done = items.Count(i => IsDone(fy, i.Card.Id));
            var overdue = items.Count(i => !IsDone(fy, i.Card.Id) && (i.Days ?? 999) < 0);
            var high = items.Count(i => i.Control.Risk == "High");
            var score = items.Count > 0 ? JsRound((double)done / items.Count * 100) : 0;
            return (Q: q, Items: items, Done: done, Overdue: overdue, High: high, Score: score);
        }).ToList();
        var cq = data.FirstOrDefault(d => d.Q.Key == key);
        if (cq.Items is null) cq = data[0];
        var load = new List<(string, int)>();
        int ch = 0, cm = 0, cl = 0;
        foreach (var i in cq.Items)
        {
            var at = load.FindIndex(o => o.Item1 == i.Control.Responsible);
            if (at >= 0) load[at] = (load[at].Item1, load[at].Item2 + 1);
            else load.Add((i.Control.Responsible, 1));
            if (i.Control.CashFlow == "High") ch++;
            else if (i.Control.CashFlow == "Medium") cm++;
            else cl++;
        }

        var monthly = cq.Q.Months.Select(m =>
        {
            var items = MonthCards(m).Where(c => !IsNa(fy, c.Id)).ToList();
            var done = items.Count(c => IsDone(fy, c.Id));
            var overdue = items.Count(c => !IsDone(fy, c.Id) && (SccTaxRules.DaysLeft(c.Due, fy, today) ?? 999) < 0);
            return new CmpQuarterRow(m, items.Count, done, overdue, items.Count > 0 ? JsRound((double)done / items.Count * 100) : 0);
        }).ToList();
        static int Rank(string r) => r switch { "High" => 0, "Medium" => 1, _ => 2 };
        var focus = cq.Items.Where(i => !IsDone(fy, i.Card.Id)).Select((i, n) => (i, n))
            .OrderBy(x => Rank(x.i.Control.Risk)).ThenBy(x => x.i.Days ?? 999).ThenBy(x => x.n).Select(x => x.i).Take(8).ToList();
        return new CmpQuarterView(cq.Q.Key, cq.Q.Label, cq.Items.Count, cq.Done, cq.Items.Count - cq.Done, cq.Overdue, cq.High, cq.Score, cq.Items,
            data.Select(d => (d.Q.Key, d.Q.Label, d.Items.Count, d.Score)).ToList(), load, ch, cm, cl, monthly, focus, SccTaxRules.RenderFyText(cq.Q.Period, fy));
    }

    // ── Action Center (SCC collectOutstanding L1897–1917) ──

    public IReadOnlyList<CmpAction> ActionCenter(int fy, DateOnly today)
    {
        if (Profile is null) return [];
        var rows = new List<(CmpAction A, int N)>();
        foreach (var m in SccTaxCatalogue.Months)
            foreach (var it in MonthCards(m))
            {
                if (IsNa(fy, it.Id) || IsDone(fy, it.Id)) continue;
                var days = SccTaxRules.DaysLeft(it.Due, fy, today);
                if (days is null || days > SccTaxRules.ActionCenterDays) continue;
                rows.Add((new CmpAction(it.Id, m, it.Name, it.Form, it.Due, SccTaxRules.ParseDue(it.Due, fy), it.Cat, days.Value), rows.Count));
            }

        return rows.OrderBy(r => r.A.Days).ThenBy(r => r.N).Select(r => r.A).ToList();
    }

    // ── Evidence completeness (SCC computeEvidenceCompleteness L2326–2363) ──

    /// <summary><paramref name="scope"/> = "fy" or a month name.</summary>
    public CmpEvidenceStatus EvidenceStatus(string scope, int fy)
    {
        if (Profile is null) return new(0, 0, 0, 0, [], true);
        var months = scope == "fy" ? SccTaxCatalogue.Months : [scope];
        int applicable = 0, done = 0, ok = 0;
        var missing = new List<(string, string, string, string)>();
        foreach (var m in months)
            foreach (var i in MonthCards(m))
            {
                if (IsNa(fy, i.Id)) continue;
                applicable++;
                if (!IsDone(fy, i.Id)) continue;
                done++;
                if (!string.IsNullOrWhiteSpace(Record(fy, i.Id)?.EvidenceRef) || FileCount(fy, i.Id) > 0) ok++;
                else missing.Add((i.Id, i.Name, m, i.Cat));
            }

        return new(applicable, done, ok, done > 0 ? JsRound((double)ok / done * 100) : 0, missing, false);
    }

    // ── CSV register (SCC exportComplianceCSV L1971–1999) ──

    public static readonly IReadOnlyList<string> CsvHeader = ["Firm", "FY", "Month", "Due", "Obligation", "Form/Portal", "Category", "Frequency", "Responsible", "Risk", "Status", "Filed On", "Days Left"];

    public IReadOnlyList<IReadOnlyList<string>> CsvRows(int fy, DateOnly today)
    {
        var rows = new List<IReadOnlyList<string>> { CsvHeader };
        var label = SccTaxRules.FyLabel(fy);
        foreach (var m in SccTaxCatalogue.Months)
            foreach (var it in MonthCards(m))
            {
                var na = IsNa(fy, it.Id);
                var done = !na && IsDone(fy, it.Id);
                var rec = Record(fy, it.Id);
                var prof = SccTaxRules.ControlProfile(it, fy, today);
                var days = SccTaxRules.DaysLeft(it.Due, fy, today);
                rows.Add([Profile?.Name ?? string.Empty, label, m, it.Due, SccTaxRules.StripHtml(SccTaxRules.RenderFyText(it.Name, fy)),
                    SccTaxRules.StripHtml(SccTaxRules.RenderFyText(it.Form, fy)), SccTaxRules.CatLabel(it.Cat), it.Freq, prof.Responsible, prof.Risk,
                    na ? "N/A" : done ? "Done" : "Pending", rec?.FiledOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    days?.ToString(CultureInfo.InvariantCulture) ?? string.Empty]);
            }

        return rows;
    }

    /// <summary>The register text (CRLF; a byte-order mark first, as SCC) and SCC's file name; null without a profile (SCC: firm first).</summary>
    public (string FileName, string Csv, int Rows)? Csv(int fy, DateOnly today)
    {
        if (Profile is null) return null;
        var rows = CsvRows(fy, today);
        var fileBase = SccTaxRules.Sanitize("Compliance_" + (string.IsNullOrEmpty(Profile.Name) ? "firm" : Profile.Name) + "_" + SccTaxRules.FyLabel(fy));
        return ((fileBase.Length > 0 ? fileBase : "Compliance") + ".csv", "﻿" + string.Join("\r\n", rows.Select(r => string.Join(',', r.Select(SccTaxRules.CsvCell)))), rows.Count - 1);
    }

    // ── Evidence ZIP (SCC exportEvidenceZip L2045–2098) ──

    public (string FileName, IReadOnlyList<(long EvidenceId, string Path)> Entries, string Index) ZipPlan(int fy)
    {
        var meta = new Dictionary<string, (string Name, string Cat, string Month)>(StringComparer.Ordinal);
        foreach (var m in SccTaxCatalogue.Months)
            foreach (var it in MonthItems(m))
                meta.TryAdd(it.Id, (SccTaxRules.StripHtml(SccTaxRules.RenderFyText(it.Name, fy)), SccTaxRules.CatLabel(it.Cat), m));
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var entries = new List<(long, string)>();
        var manifest = new List<string[]> { new[] { "Obligation", "Original filename", "Stored path", "Added-on" } };
        foreach (var e in Evidence.Where(x => x.FyStart == fy).OrderBy(x => x.EvidenceId))
        {
            var has = meta.TryGetValue(e.ItemId, out var mm);
            var folder = SccTaxRules.Sanitize(has ? mm.Month + "_" + mm.Cat + "_" + mm.Name : e.ItemId);
            if (folder.Length > 60) folder = folder[..60];
            if (folder.Length == 0) folder = "evidence";
            var tail = SccTaxRules.Sanitize(e.ItemId);
            folder = folder + "__" + (tail.Length == 0 ? "x" : tail.Length > 24 ? tail[..24] : tail);
            if (!seen.TryGetValue(folder, out var names)) seen[folder] = names = new HashSet<string>(StringComparer.Ordinal);
            var fname = SccTaxRules.Sanitize(string.IsNullOrEmpty(e.FileName) ? "evidence" : e.FileName);
            if (fname.Length == 0) fname = "evidence";
            if (names.Contains(fname))
            {
                var dot = fname.LastIndexOf('.');
                var stem = dot > 0 ? fname[..dot] : fname;
                var ext = dot > 0 ? fname[dot..] : string.Empty;
                var n = 2;
                while (names.Contains(stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + ext)) n++;
                fname = stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + ext;
            }

            names.Add(fname);
            entries.Add((e.EvidenceId, folder + "/" + fname));
            manifest.Add([has ? mm.Name : e.ItemId, e.FileName, folder + "/" + fname, e.AddedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]);
        }

        var fileBase = SccTaxRules.Sanitize("Evidence_" + (string.IsNullOrEmpty(Profile?.Name) ? "firm" : Profile!.Name) + "_" + SccTaxRules.FyShortLabel(fy));
        return ((fileBase.Length > 0 ? fileBase : "Evidence") + ".zip", entries, "﻿" + string.Join("\r\n", manifest.Select(r => string.Join(',', r.Select(SccTaxRules.ZipCell)))));
    }

    // ── ≈₹ payable (SCC L1751–1895), the feed computed from HCC's own data ──

    /// <summary>SCC taxPayableAmount: whole rupees (rounded) of the month before the page; null when none or ≤ 0.</summary>
    public static long? PayableAmount(IReadOnlyDictionary<string, CmpPayableMonth> feed, string name, string month, int fy)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var field = SccTaxRules.TaxPayableField(name);
        if (field is null || !feed.TryGetValue(SccTaxRules.PayableYm(month, fy), out var rec)) return null;
        var paise = field switch { "pf" => rec.Pf, "esic" => rec.Esic, "pt" => rec.Pt, _ => rec.Gst };
        var rupees = (long)Math.Round(paise / 100.0, MidpointRounding.AwayFromZero);
        return rupees > 0 ? rupees : null;
    }

    /// <summary>SCC getStatutoryPayable (L1852–1861): the business month's entry, else the newest available.</summary>
    public static (CmpPayableMonth Rec, bool IsCurrent)? StatutoryPayable(IReadOnlyDictionary<string, CmpPayableMonth> feed, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (feed.Count == 0) return null;
        var cy = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (feed.TryGetValue(cy, out var cur)) return (cur, true);
        return (feed[feed.Keys.OrderBy(k => k, StringComparer.Ordinal).Last()], false);
    }
}
