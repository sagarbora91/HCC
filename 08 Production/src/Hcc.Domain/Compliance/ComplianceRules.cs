using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Compliance;

// Compliance calendar = SCC's tax module in full (DEC-116, SCC port 27 Oct 2026). SCC's rules live in SccTaxCatalogue / SccTaxRules /
// SccCalendar; this file keeps HCC's platform pieces: limits, the Owner-reopen reason, the simulated CA pack lines and the mapping of the
// W05-C calendar (obligation × period) onto SCC's cards, used by migration 4131 and its tests. The W05-C period engine, the paid-holiday /
// Sunday due-date shift, the "1–28 of the next month" rule, dated rule versions and the 4–40 reference / 5–120 not-applicable reason are gone.

public static class ComplianceRules
{
    public const int MaxPacks = 20;
    public const int MaxReopened = 200;
    public const int DefaultDueSoonDays = 7;
    public const string DefaultCaName = "Rao & Associates (fictional)";
    public const string DefaultCaEmail = "ca.desk@example.test";
    public const string Illustrative = "SCC compliance catalogue — due dates and texts as SCC ships them (Maharashtra); CA to confirm for a hotel.";

    /// <summary>HCC safeguard (SCC has no limit): an evidence file up to 5 MB.</summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>HCC column limits for SCC's free texts (SCC has none).</summary>
    public const int MaxFirmName = 120, MaxPan = 20, MaxGstin = 20, MaxNotes = 500, MaxEvidenceRef = 200, MaxReviewer = 200, MaxName = 200, MaxText = 2000, MaxShort = 200,
        MaxListLines = 40, MaxFileName = 200;

    /// <summary>DEC-133: a licence number (free text) up to 60 characters.</summary>
    public const int MaxLicenceNo = 60;

    /// <summary>DEC-133: the licence part of the firm-profile audit text (prototype cmpLicenceAudit).</summary>
    public static string LicenceAudit(CmpProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (p.FssaiExpiry is { } f ? " · FSSAI licence expires " + D(f) : string.Empty) + (p.LodgingExpiry is { } l ? " · lodging licence expires " + D(l) : string.Empty);
    }

    public const string PayableNote = "Estimate — verify with CA before filing.";

    public const string PayableBasis = "EPF = employee + employer PF and ESIC = employee + employer ESIC of every line of the month’s LOCKED pay run; Professional Tax = PT deducted in that run; GST (estimate) = CGST + SGST + IGST on the tax invoices issued in the month (rooms and restaurant), less the month’s credit notes — the GSTR-1 working summary’s SAC total; input tax credit is not deducted.";

    /// <summary>The Owner's reopen reason (HCC safeguard; SCC's undo has none).</summary>
    public static string CleanReason(string? raw)
    {
        var reason = (raw ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 120) throw HccException.Validation("Give a reason of 5–120 characters.");
        return reason;
    }

    /// <summary>"Nd overdue" / "Due today" / "Nd left" (SCC Action Center L1954).</summary>
    public static string When(int days) => days < 0 ? $"{(-days).ToString(CultureInfo.InvariantCulture)}d overdue" : days == 0 ? "Due today" : $"{days.ToString(CultureInfo.InvariantCulture)}d left";

    /// <summary>The simulated CA pack (prototype <c>cmpPackLines</c>): position line, the Action Center rows, filings of the last 35 days; at most 60 lines.</summary>
    public static (IReadOnlyList<string> Lines, int Overdue, int DueSoon, int Done, int NotApplicable, int Open) PackLines(SccCalendar cal, int fy, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(cal);
        int done = 0, na = 0, open = 0, overdue = 0;
        var filed = new List<string>();
        foreach (var m in SccTaxCatalogue.Months)
            foreach (var c in cal.MonthCards(m, fy))
            {
                if (cal.IsNa(fy, c.Id)) { na++; continue; }
                if (cal.IsDone(fy, c.Id)) { done++; continue; }
                open++;
                if (SccTaxRules.DaysLeft(c.Due, fy, today) is < 0) overdue++;
            }

        var ac = cal.ActionCenter(fy, today);
        var lines = new List<string>
        {
            $"Position as on {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({SccTaxRules.FyLabel(fy)}): {done.ToString(CultureInfo.InvariantCulture)} done, {open.ToString(CultureInfo.InvariantCulture)} pending ({overdue.ToString(CultureInfo.InvariantCulture)} overdue), {na.ToString(CultureInfo.InvariantCulture)} not applicable.",
        };
        foreach (var r in ac)
            lines.Add($"OPEN · {SccTaxRules.StripHtml(SccTaxRules.RenderFyText(r.Name, fy))} · {r.Month} · due {r.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? r.Due} · {When(r.Days)}");
        foreach (var m in SccTaxCatalogue.Months)
            foreach (var c in cal.MonthCards(m, fy))
                if (cal.Record(fy, c.Id) is { Done: true, Na: false, FiledOn: { } on } rec && today.DayNumber - on.DayNumber is >= 0 and <= 35)
                    filed.Add($"FILED · {SccTaxRules.StripHtml(SccTaxRules.RenderFyText(c.Name, fy))} · filed {on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(string.IsNullOrEmpty(rec.EvidenceRef) ? string.Empty : " · ref " + rec.EvidenceRef)}");
        lines.AddRange(filed);
        return (lines.Take(60).ToList(), overdue, ac.Count(r => r.Days >= 0), done, na, open);
    }

    // ── The W05-C calendar mapped onto SCC's cards (gap report §7 step 2; migration 4131) ──

    /// <summary>HCC obligation kind → its SCC card per page April…March. GSTR-9 for FY y → dc5 in FY y + 1.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> LegacyCards = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["TDS"] = ["ap1", "my1", "jn1", "jl1", "ag1", "sp1", "oc1", "nv1", "dc1", "jn_1", "fb1", "mr1"],
        ["GSTR-1"] = ["ap2", "my3", "jn5", "jl4", "ag3", "sp4", "oc4", "nv3", "dc3", "jn_3", "fb4", "mr5"],
        ["GSTR-3B"] = ["ap3", "my4", "jn6", "jl6", "ag4", "sp5", "oc5", "nv4", "dc4", "jn_4", "fb5", "mr6"],
        ["EPF"] = ["ap5", "my5", "jn8", "jl9", "ag5", "sp6", "oc9", "nv5", "dc7", "jn_8", "fb6", "mr8"],
        ["ESIC"] = ["ap6", "my6", "jn9", "jl10", "ag6", "sp7", "oc10", "nv6", "dc8", "jn_9", "fb7", "mr9"],
        ["PT"] = ["ap7", "my8", "jn10", "jl12", "ag7", "sp8", "oc11", "nv8", "dc9", "jn_12", "fb8", "mr10"],
    };

    /// <summary>The SCC archive keys of a switched-off HCC obligation kind.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> LegacyKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["TDS"] = ["TDS Deposit"], ["GSTR-1"] = ["GSTR-1"], ["GSTR-3B"] = ["GSTR-3B"], ["GSTR-9"] = ["GSTR-9"],
        ["EPF"] = ["EPF Contribution Deposit", "EPF Contribution"], ["ESIC"] = ["ESIC Contribution Deposit", "ESIC Contribution"], ["PT"] = ["Professional Tax"],
    };

    /// <summary>Where an HCC filing (kind, period) lands: the card on page month(P + 1) of the FY that holds that page.</summary>
    public static (int FyStart, string ItemId)? LegacyCard(string kind, string period)
    {
        if (period is not { Length: 7 } || !int.TryParse(period.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return null;
        if (kind == "GSTR-9") return (y + 1, "dc5");
        if (!LegacyCards.TryGetValue(kind, out var cards) || !int.TryParse(period.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 12) return null;
        var page = new DateOnly(y, m, 1).AddMonths(1);
        var idx = (page.Month + 8) % 12;   // April = 0 … March = 11
        return (SccTaxRules.FyOfDate(page), cards[idx]);
    }
}
