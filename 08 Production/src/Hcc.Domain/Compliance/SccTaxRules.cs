using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Compliance;

// SCC tax port (DEC-116) — SCC's compliance rule functions, ported line by line from SCC tax/index.html (scratchpad/scc/tax.js; index.html line
// = tax.js line + 861), as the prototype's compliance-catalogue.ts. SCC's logic is kept even where it looks wrong (owner questions in
// 05 Delivery Evidence/SCC Port/SCC_Tax_Port_2026-10-27.md). HCC difference (architecture rule): "today" is the business date. Regexes use
// RegexOptions.ECMAScript so \b, \d, \s and \w match JavaScript's.

public sealed record SccDaysTag(string Text, string Cls);

public sealed record SccEscalation(string Label, string Cls, string Note);

public sealed record SccControl(string Risk, string CashFlow, string Responsible, string Reviewer, int Buffer, DateOnly? InternalDate, SccEscalation Escalation,
    IReadOnlyList<string> Checklist, IReadOnlyList<string> Workflow, IReadOnlyList<string> Evidence);

public sealed record SccAdvisory(string Tone, string Text);

public sealed record SccQuarter(string Key, string Label, IReadOnlyList<string> Months, string Period);

public static partial class SccTaxRules
{
    public const int FyBaselineStart = 2024;
    /// <summary>SCC NEW_ACT_FORMS (L165): the Income-tax Act, 2025 renames are on (SCC: a one-flag rollback).</summary>
    public static readonly bool NewActForms = true;
    public const int FyMin = 2026, FyMax = 2032;
    public const int ActionCenterDays = 30;
    public static readonly IReadOnlyList<string> BusinessLabIds = ["dc_shops", "jn_11", "fb9", "mr11"];
    public static readonly IReadOnlyList<string> Cats = ["it", "gst", "comp", "lab"];
    public static readonly IReadOnlyList<string> CatOrder = ["gst", "comp", "it", "lab"];
    public static readonly IReadOnlyList<string> GstTypes = ["regular", "composition", "both", "unregistered"];
    public static readonly IReadOnlyList<string> Entities = ["proprietorship", "partnership", "llp", "company"];
    public static readonly IReadOnlyList<string> Freqs = ["Monthly", "Quarterly", "Annual", "One-time"];

    /// <summary>SCC QUARTERS (L1136–1141), period texts verbatim.</summary>
    public static readonly IReadOnlyList<SccQuarter> Quarters =
    [
        new("Q1", "Q1 – Apr to Jun", ["April", "May", "June"], "April – June 2024"),
        new("Q2", "Q2 – Jul to Sep", ["July", "August", "September"], "July – September 2024"),
        new("Q3", "Q3 – Oct to Dec", ["October", "November", "December"], "October – December 2024"),
        new("Q4", "Q4 – Jan to Mar", ["January", "February", "March"], "January – March 2025"),
    ];

    private static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] Short = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static int? ShortIndex(string s)
    {
        var i = Array.IndexOf(Short, s);
        return i < 0 ? null : i;
    }

    public static string CatLabel(string c) => c switch { "it" => "Income Tax", "gst" => "GST", "comp" => "Composition GST", _ => "Labour Law" };

    public static string TypeLabel(string t) => t switch
    {
        "regular" => "Regular Dealer",
        "composition" => "Composition Dealer",
        "both" => "Both / All",
        "unregistered" => "Unregistered",
        _ => t ?? string.Empty,
    };

    // ── FY ──

    public static string FyLabel(int fy) => $"FY {fy.ToString(CultureInfo.InvariantCulture)}–{((fy + 1) % 100).ToString("00", CultureInfo.InvariantCulture)}";

    public static string FyShortLabel(int fy) => $"{fy.ToString(CultureInfo.InvariantCulture)}–{((fy + 1) % 100).ToString("00", CultureInfo.InvariantCulture)}";

    public static int CalendarYearForMonth(string month, int fy) => month is "January" or "February" or "March" ? fy + 1 : fy;

    /// <summary>The FY whose pages hold this date (Apr–Dec: that year; Jan–Mar: the year before).</summary>
    public static int FyOfDate(DateOnly d) => d.Month >= 4 ? d.Year : d.Year - 1;

    public static int ClampFy(int fy) => Math.Max(FyMin, fy);

    /// <summary>DEC-125: an FY inside SCC's list (2026–27 … 2032–33): before it → 2026–27, after it → the last FY.</summary>
    public static int FyInList(int fy) => Math.Clamp(fy, FyMin, FyMax);

    /// <summary>DEC-125: the FY of the business date, kept inside SCC's list.</summary>
    public static int BusinessFy(DateOnly businessDate) => FyInList(FyOfDate(businessDate));

    /// <summary>
    /// DEC-125 (owner, 27 Oct 2026): the compliance FY follows the business date on 1 April. A pick holds while the business date is still in the
    /// FY it was picked in (<paramref name="chosenIn"/> = <see cref="FyOfDate"/> of the business date at the pick); after the next 1 April — and
    /// without a pick (null) — the business date's FY. Never the device clock. Prototype twin: cmpEffectiveFy.
    /// </summary>
    public static int EffectiveFy(int selected, int? chosenIn, DateOnly businessDate) =>
        chosenIn is { } c && c == FyOfDate(businessDate) ? FyInList(selected) : BusinessFy(businessDate);

    /// <summary>DEC-125: the pick in force (the business date's FY it was made in), or null once it lapsed / when there is none.</summary>
    public static int? PickInForce(int? chosenIn, DateOnly businessDate) => chosenIn is { } c && c == FyOfDate(businessDate) ? c : null;

    /// <summary>JavaScript <c>new Date(y, mi, day)</c>: day 0 = last day of the month before, 31 Apr = 1 May.</summary>
    private static DateOnly JsDate(int y, int mi, int day) => new DateOnly(y, 1, 1).AddMonths(mi).AddDays(day - 1);

    [GeneratedRegex(@"^(?:last day of|by end of)\s+(.+)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase)]
    private static partial Regex LastEndRx();

    [GeneratedRegex(@"^(\d{1,2})\s+([A-Za-z]+)$", RegexOptions.ECMAScript)]
    private static partial Regex DayMonRx();

    /// <summary>SCC parseDueDate (L345–369). The due string decides the date, not the page month. Null where SCC returns null.</summary>
    public static DateOnly? ParseDue(string? due, int fy)
    {
        if (string.IsNullOrEmpty(due)) return null;
        var le = LastEndRx().Match(due);
        if (le.Success)
        {
            var key = le.Groups[1].Value.Trim();
            var mi = ShortIndex(key.Length > 3 ? key[..3] : key);
            if (mi is null) return null;
            var y = CalendarYearForMonth(MonthNames[mi.Value], fy);
            return JsDate(y, mi.Value + 1, 0);
        }

        var m = DayMonRx().Match(due);
        if (m.Success)
        {
            var day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var mon = m.Groups[2].Value;
            var mi = ShortIndex(mon.Length > 3 ? mon[..3] : mon);
            if (mi is null) return null;
            var y = mi <= 2 ? fy + 1 : fy;
            return JsDate(y, mi.Value, day);
        }

        return null;
    }

    /// <summary>SCC getDaysLeft (L371–376), today = the business date.</summary>
    public static int? DaysLeft(string? due, int fy, DateOnly today) => ParseDue(due, fy) is { } d ? d.DayNumber - today.DayNumber : null;

    /// <summary>SCC daysLeftTag (L378–386).</summary>
    public static SccDaysTag? DaysTag(int? days, bool isDone)
    {
        if (isDone) return new("✓ Filed", "done-days");
        if (days is not { } d) return null;
        if (d < 0) return new($"⚠ {Math.Abs(d).ToString(CultureInfo.InvariantCulture)}d overdue", "overdue");
        if (d == 0) return new("🔴 Due today", "urgent-days");
        if (d <= 7) return new($"⚡ {d.ToString(CultureInfo.InvariantCulture)}d left", "urgent-days");
        if (d <= 15) return new($"⏰ {d.ToString(CultureInfo.InvariantCulture)}d left", "soon");
        return new($"{d.ToString(CultureInfo.InvariantCulture)}d left", "ok");
    }

    // ── Texts: Income-tax Act 2025 renames + FY rebasing (SCC L165–219) ──

    private const RegexOptions Js = RegexOptions.ECMAScript;

    [GeneratedRegex("Income[‑\\- ]?tax Act,?\\s*1961", RegexOptions.ECMAScript | RegexOptions.IgnoreCase)]
    private static partial Regex ActRx();

    private static readonly (Regex Rx, string To)[] Renames =
    [
        (new Regex(@"\bForm 16A\b", Js), "Form 131"),
        (new Regex(@"\bForm 16\b", Js), "Form 130"),
        (new Regex(@"\bForm 24Q\b", Js), "Form 138"),
        (new Regex(@"\bForm 26Q\b", Js), "Form 140"),
        (new Regex(@"\bForm 26AS\b", Js), "Form 168"),
        (new Regex(@"\bSection 200\b", Js), "Section 392"),
        (new Regex(@"(\bu\/s |\bSec\.? |\bSection )192\b", Js), "${1}392"),
        (new Regex(@"\b194I\b", Js), "393"),
        (new Regex(@"\b194J\b", Js), "393"),
        (new Regex(@"(\bu\/s )234B\b", Js), "${1}424"),
        (new Regex(@"(\bu\/s )234C\b", Js), "${1}425"),
        (new Regex(@"(\bu\/s )234E\b", Js), "${1}427"),
        (new Regex(@"\b80C\b", Js), "123"),
        (new Regex(@"\b80D\b", Js), "126"),
        (new Regex(@"\bu\/s 201\(1A\)", Js), "under the Income-tax Act, 2025"),
        (new Regex(@"\bu\/s 271H", Js), "under the Income-tax Act, 2025"),
        (new Regex(@"\bu\/s 40\(a\)\(ia\)", Js), "under the Income-tax Act, 2025"),
        (new Regex(@"\bAssessment Year\b", Js), "Tax Year"),
        (new Regex(@"\bPrevious Year\b", Js), "Tax Year"),
    ];

    /// <summary>SCC renderNewActForms (L166–189).</summary>
    public static string RenderNewActForms(string text)
    {
        if (!NewActForms) return text ?? string.Empty;
        var s = ActRx().Replace(text ?? string.Empty, "Income-tax Act, 2025");
        foreach (var (rx, to) in Renames) s = rx.Replace(s, to);
        return s;
    }

    [GeneratedRegex(@"\b(FY |AY )?(\d{4})([–-])(\d{2})\b", RegexOptions.ECMAScript)]
    private static partial Regex FyRangeRx();

    [GeneratedRegex(@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{4})\b", RegexOptions.ECMAScript)]
    private static partial Regex MonthYearRx();

    /// <summary>SCC renderFYText (L190–219): rebases baked FY / AY ranges and the authored "&lt;Month&gt; YYYY" mentions onto the FY, then renames.</summary>
    public static string RenderFyText(string? text, int fy)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var shift = fy - FyBaselineStart;
        var rolled = FyRangeRx().Replace(text, m =>
        {
            var start = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if ((start + 1) % 100 != int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)) return m.Value;
            var ns = start + shift;
            return m.Groups[1].Value + ns.ToString(CultureInfo.InvariantCulture) + m.Groups[3].Value + ((ns + 1) % 100).ToString("00", CultureInfo.InvariantCulture);
        });
        rolled = MonthYearRx().Replace(rolled, m =>
        {
            var cal = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var idx = Array.IndexOf(MonthNames, m.Groups[1].Value);
            var fyOfDate = idx <= 2 ? cal - 1 : cal;
            if (fyOfDate < FyBaselineStart - 1 || fyOfDate > FyBaselineStart) return m.Value;
            return m.Groups[1].Value + " " + (cal + shift).ToString(CultureInfo.InvariantCulture);
        });
        return RenderNewActForms(rolled);
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRx();

    /// <summary>SCC stripHtml (L1970).</summary>
    public static string StripHtml(string? s) => TagRx().Replace(s ?? string.Empty, string.Empty)
        .Replace("&amp;", "&", StringComparison.Ordinal).Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal)
        .Replace("&#39;", "'", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal);

    // ── Keys and classes ──

    /// <summary>SCC obligationKey (L1643–1647): custom = its id; built-in = the name up to the first en-dash or pipe.</summary>
    public static string ObligationKey(SccCard? item)
    {
        if (item is null) return string.Empty;
        if (item.IsCustom && !string.IsNullOrEmpty(item.Gid)) return item.Gid;
        var name = item.Name ?? string.Empty;
        var cut = name.IndexOfAny(['–', '|']);
        return (cut < 0 ? name : name[..cut]).Trim();
    }

    /// <summary>SCC isPayrollItem (L1609–1611). DEC-133: the licence cards are business-level too (like SCC's Shops &amp; Establishment renewal).</summary>
    public static bool IsPayrollItem(SccCard? item) => item is not null && item.Cat == "lab" && !item.IsCustom && item.Licence is null && !BusinessLabIds.Contains(item.Id);

    /// <summary>SCC gstFilingMode (L1804–1810).</summary>
    public static string? GstFilingMode(SccCard? item)
    {
        if (item is null || item.Cat != "gst") return null;
        var n = (item.Name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("qrmp", StringComparison.Ordinal)) return "qrmp";
        if (n.Contains("monthly filers", StringComparison.Ordinal)) return "monthly";
        return null;
    }

    /// <summary>SCC taxPayableField (L1768–1775).</summary>
    public static string? TaxPayableField(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("gstr-3b", StringComparison.Ordinal) && n.Contains("monthly filers", StringComparison.Ordinal)) return "gstEstimate";
        if (n.Contains("epf", StringComparison.Ordinal) && n.Contains("contribution", StringComparison.Ordinal)) return "pf";
        if (n.Contains("esic", StringComparison.Ordinal) && n.Contains("contribution", StringComparison.Ordinal)) return "esic";
        if (n.Contains("professional tax", StringComparison.Ordinal) && n.Contains("deduction", StringComparison.Ordinal)) return "pt";
        return null;
    }

    /// <summary>SCC taxPayableAmount key (L1776–1781): the calendar month before the page, "YYYY-MM".</summary>
    public static string PayableYm(string month, int fy)
    {
        var mi = Array.IndexOf(MonthNames, month);
        var d = JsDate(CalendarYearForMonth(month, fy), mi - 1, 1);
        return d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    /// <summary>SCC fmtYMLabel (L1841).</summary>
    public static string YmLabel(string ym) =>
        ym is { Length: 7 } && int.TryParse(ym.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12 ? Short[m - 1] + " " + ym[..4] : ym;

    /// <summary>SCC getControlProfile (L2244–2276) with today = the business date.</summary>
    public static SccControl ControlProfile(SccCard item, int fy, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = (item.Name ?? string.Empty).ToLowerInvariant();
        bool Has(string s) => name.Contains(s, StringComparison.Ordinal);
        var isGstPayment = Has("gstr-3b") || Has("cmp-08");
        var isTds = Has("tds");
        var isPayroll = Has("epf") || Has("esic") || Has("professional tax");
        var isAdvanceTax = Has("advance tax");
        var risk = isGstPayment || isTds || isPayroll || isAdvanceTax ? "High" : item.Cat is "lab" or "gst" ? "Medium" : "Low";
        if (item.Risk is "High" or "Medium" or "Low") risk = item.Risk;
        var cashFlow = isGstPayment || isAdvanceTax || isTds ? "High" : isPayroll || item.Cat == "gst" ? "Medium" : "Low";
        var responsible = !string.IsNullOrEmpty(item.Responsible) ? item.Responsible : isPayroll ? "Payroll / HR Executive" : item.Cat is "gst" or "comp" ? "GST Executive" : "Tax Executive";
        var reviewer = !string.IsNullOrEmpty(item.Reviewer) ? item.Reviewer : isAdvanceTax ? "Partner / CFO" : isPayroll ? "HR & Finance Manager" : "Compliance Manager";
        var buffer = item.Buffer is >= 0 ? item.Buffer.Value : risk == "High" ? 5 : 3;
        var due = ParseDue(item.Due, fy);
        DateOnly? internalDate = due?.AddDays(-buffer);
        int? left = internalDate is { } i ? i.DayNumber - today.DayNumber : null;
        var escalation = new SccEscalation("Monitor", "status-watch", "Track preparation and keep the working papers current.");
        if (left is <= 1) escalation = new("Critical", "status-critical", "Escalate immediately to leadership and secure filing evidence today.");
        else if (left is <= 3) escalation = new("Escalate", "status-escalate", "Reviewer intervention required. Resolve blockers before portal risk increases.");
        else if (left is <= 7) escalation = new("Reminder", "status-reminder", "Executor reminder due. Validate data pack and challan readiness.");
        IReadOnlyList<string> checklist = isGstPayment
            ? ["Sales register and purchase register closed", "ITC matched with GSTR-2B", "Net GST payable and challan amount approved"]
            : isTds
                ? ["Deduction working with section-wise mapping", "PAN and deductee details validated", "Challan or return control total checked"]
                : isPayroll
                    ? ["Payroll register frozen", "Employee contribution workings validated", "Portal upload file and payment reference prepared"]
                    : ["Books closed for the period", "Working papers reviewed", "Final computation approved for filing"];
        if (item.Checklist.Count > 0) checklist = item.Checklist;
        IReadOnlyList<string> workflow = ["Prepare data pack", "Reviewer check and approval", "File and store evidence"];
        if (item.Workflow.Count > 0) workflow = item.Workflow;
        IReadOnlyList<string> evidence = isGstPayment ? ["GST challan", "Filed return PDF", "Working reconciliation"]
            : isTds ? ["OLTAS challan", "Filing acknowledgement", "Deduction working"]
            : ["Portal challan", "Return / ECR copy", "Payroll or support working"];
        if (item.Evidence.Count > 0) evidence = item.Evidence;
        return new SccControl(risk, cashFlow, responsible, reviewer, buffer, internalDate, escalation, checklist, workflow, evidence);
    }

    /// <summary>SCC qrmpAdvisory (L1816–1833): advisory only. Needs a firm (the property's profile).</summary>
    public static SccAdvisory? QrmpAdvisory(SccCard item, string? gstType, bool qrmp, bool hasProfile)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!hasProfile || item.Cat != "gst") return null;
        var mode = GstFilingMode(item);
        if (mode is null) return null;
        if (qrmp)
        {
            if (mode == "monthly")
                return gstType == "both" ? null : new("amber", "QRMP firm: you file this quarterly, not monthly. File the quarterly GSTR-1 / GSTR-3B and pay monthly via PMT-06 challan (GSTR-1 IFF upload is optional).");
            return mode == "qrmp" ? new("green", "Applies to your firm (QRMP quarterly filer).") : null;
        }

        return mode == "qrmp" ? new("grey", "Only for QRMP-scheme firms — your firm files monthly (see the GSTR-1 / GSTR-3B Monthly card).") : null;
    }

    [GeneratedRegex(@"^[\t\r\n ]*[=+\-@]")]
    private static partial Regex FormulaRx();

    /// <summary>SCC exportComplianceCSV cell (L1975): formula guard, quotes.</summary>
    public static string CsvCell(string? v)
    {
        var s = v ?? string.Empty;
        if (FormulaRx().IsMatch(s)) s = "'" + s;
        s = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        return s.IndexOfAny(['"', ',', '\r', '\n']) >= 0 ? "\"" + s + "\"" : s;
    }

    /// <summary>SCC exportEvidenceZip index.csv cell (L2092): quotes only.</summary>
    public static string ZipCell(string? v)
    {
        var s = (v ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return s.IndexOfAny(['"', ',', '\r', '\n']) >= 0 ? "\"" + s + "\"" : s;
    }

    [GeneratedRegex(@"[^\w.-]+", RegexOptions.ECMAScript)]
    private static partial Regex UnsafeRx();

    [GeneratedRegex("^_+|_+$")]
    private static partial Regex EdgeRx();

    /// <summary>SCC file-name sanitiser (L1989, L2063).</summary>
    public static string Sanitize(string? s) => EdgeRx().Replace(UnsafeRx().Replace(s ?? string.Empty, "_"), string.Empty);

    /// <summary>The text hash the golden test pins: page by page the note, then name / form / who / what / why / penalty of each card, rendered.</summary>
    public static string RenderedTextHash(int fy)
    {
        var parts = new List<string>();
        foreach (var m in SccTaxCatalogue.Months)
        {
            parts.Add(RenderFyText(SccTaxCatalogue.Note(m), fy));
            foreach (var i in SccTaxCatalogue.BuiltIn(m))
                parts.AddRange([RenderFyText(i.Name, fy), RenderFyText(i.Form, fy), RenderFyText(i.Who, fy), RenderFyText(i.What, fy), RenderFyText(i.Why, fy), RenderFyText(i.Penalty, fy)]);
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u0001', parts))));
    }
}
