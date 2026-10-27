using System.Globalization;
using System.Text;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Text;

namespace Hcc.Domain.Expenses;

/// <summary>
/// SCC expense features in HCC's expense module (DEC-120, 27 Oct 2026). Pure rules ported from SCC <c>expense\index.html</c> (scratchpad
/// <c>scc\expense.js</c>; line numbers below) and from the prototype twin <c>03 Prototype/src/domain/streams/expenses-scc.ts</c>. HCC's own approval
/// rules stay (an expense counts only once approved; DEC-063 G5, REQ-EXP-003), so every SCC total that reads "non-void ledger rows" reads the
/// counted records here (Posted / Reversed originals and their negative reversal records). Money is integer paise (SCC: rupee floats).
/// </summary>
public static class ExpenseScc
{
    /// <summary>SCC REASONS (line 33): the fixed void reasons.</summary>
    public static readonly IReadOnlyList<string> VoidReasons = ["Cash shortage correction", "Duplicate entry", "Missed receipt", "Wrong payment mode", "Voucher correction", "Other"];

    public const int DescriptionMax = 300;
    public const int NoteMax = 200;

    /// <summary>SCC readPhoto: a picked file above 4 MB is refused; the app sends the photo scaled to 1000 px wide at JPEG 0.7.</summary>
    public const int PhotoMaxBytes = 4 * 1024 * 1024;

    /// <summary>SCC budgets: tab "near limit" at ≥ 85 %, alert warning at ≥ 90 %, error at ≥ 100 % (1287, 1306-1308).</summary>
    public const int BudgetNear = 85;
    public const int AlertWarn = 90;
    public const int AlertError = 100;

    /// <summary>SCC petty cash: "Replenish soon" below 15 % of the float (1213).</summary>
    public const int PettyLowPercent = 15;

    /// <summary>SCC Udhaar: up to ₹0.50 more than outstanding is accepted as typed; more is capped (1587).</summary>
    public const long UdhaarTolerancePaise = 50;

    /// <summary>SCC Udhaar: due date default = credit date + 30 days (1618).</summary>
    public const int UdhaarDueDays = 30;

    public static readonly IReadOnlyList<string> MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string Ym(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // ── Text ──

    public static string? Optional(string? value, int max, string what)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        if (t.Length > max) throw HccException.Validation($"Keep the {what} to {max} characters.");
        return t;
    }

    /// <summary>SCC: "Description is required" (addEntry 519) — HCC: when the expense goes for approval; a draft may wait.</summary>
    public static string? Description(string? value, bool required)
    {
        var t = Optional(value, DescriptionMax, "description");
        if (t is null && required) throw HccException.Validation("Description is required");
        return t;
    }

    // ── Date of an entry ──

    /// <summary>
    /// Why an expense may not be dated <paramref name="date"/> (null = it may): never after the business date (SCC bug B5 closed as the owner
    /// asked), the business date while open, an earlier date only while the Owner has it reopened (DEC-077; SCC "That day's cash statement is
    /// locked. Owner must reopen it first."), never a locked cash-book day (DEC-065); drawer cash leaves today's drawer, so a drawer-cash
    /// expense is dated the business date. Prototype <c>expDateCheck</c>.
    /// </summary>
    public static HccException? DateRefusal(DateOnly businessDate, DateOnly date, bool businessDayClosed, bool reopened, bool cashBookLocked, bool drawerCash)
    {
        if (date > businessDate) return HccException.Validation($"An expense cannot be dated after the business date ({Wire(businessDate)}).");
        if (date == businessDate)
        {
            if (businessDayClosed) return new HccException(HccErrorCode.DAY_CLOSED, "This business day is closed. Open the next business day first.");
        }
        else if (!reopened)
        {
            return new HccException(HccErrorCode.DAY_CLOSED, $"That day’s cash statement is locked ({Wire(date)} is a closed business day). The Owner must reopen it first.");
        }

        if (cashBookLocked)
            return new HccException(HccErrorCode.DAY_CLOSED, $"Cash book {Wire(date)} is locked, so an expense on that day is blocked. The Owner must reopen {Wire(date)} (with a reason) first.");
        if (drawerCash && date != businessDate)
            return HccException.Validation("Drawer cash leaves today’s drawer: date a cash expense on the business date, or choose another funding source.");
        return null;
    }

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── GST on the bill (SCC P1-17) ──

    /// <summary>
    /// SCC prefillGst (445-448): round(amount × r ÷ (100 + r), 2 dp) of a GST-inclusive bill, computed with SCC's own double arithmetic (rupees and
    /// percent) so an exact half paisa follows SCC; returned in paise. 0 when the amount or the rate is not positive. Prototype <c>expGstPrefill</c>.
    /// </summary>
    public static long GstPrefill(long amountPaise, int rateBps)
    {
        if (amountPaise <= 0 || rateBps <= 0) return 0;
        double a = amountPaise / 100d, r = rateBps / 100d;
        return (long)JsRound(a * r / (100 + r) * 100);
    }

    /// <summary>JavaScript <c>Math.round</c>: the nearest integer, a half going up (towards +∞).</summary>
    public static double JsRound(double x)
    {
        var f = Math.Floor(x);
        return x - f >= 0.5 ? f + 1 : f;
    }

    /// <summary>Rate 0–100 % in basis points and amount ≥ 0; both kept only when &gt; 0 (SCC normEntry). GST above the amount needs the confirm.</summary>
    public static (int? RateBps, long? Paise) Gst(int? rateBps, long? gstPaise, long amountPaise, bool confirmed)
    {
        if (rateBps is { } r && (r < 0 || r > 10_000)) throw HccException.Validation("Enter a GST rate between 0 and 100 %.");
        if (gstPaise is { } g && (g < 0 || g > ApiConventions.MaxPaisePerField)) throw HccException.Validation("Enter the GST on the bill in rupees (0 or more).");
        var paise = gstPaise ?? 0;
        if (paise > amountPaise && !confirmed) throw HccException.Validation(GstOverMessage(paise, amountPaise));
        return (rateBps is > 0 ? rateBps : null, paise > 0 ? paise : null);
    }

    // ── DEC-135 · input tax credit on the bill's GST (owner, 27 Oct 2026) ──

    public const string ItcAllowed = "allowed";
    public const string ItcDisallowed = "disallowed";

    /// <summary>
    /// The ITC flag an expense keeps (prototype <c>expItcOf</c> + <c>expItcRefusal</c>): only with GST on the bill; anything but "allowed" is
    /// disallowed (nothing is claimed unless marked); an unknown value is refused; ITC is never allowed on more GST than the bill amount.
    /// </summary>
    public static bool Itc(string? value, long? gstPaise, long amountPaise)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length > 0 && v is not (ItcAllowed or ItcDisallowed)) throw HccException.Validation("Choose ITC allowed or ITC disallowed.");
        if (gstPaise is not { } g || g == 0) return false;
        var allowed = v == ItcAllowed;
        if (allowed && Math.Abs(g) > Math.Abs(amountPaise))
            throw HccException.Validation("ITC cannot be allowed on more GST than the bill amount — correct the GST or mark ITC disallowed.");
        return allowed;
    }

    /// <summary>The wire / CSV text of the flag: allowed / disallowed with GST on the bill, null without.</summary>
    public static string? ItcText(long? gstPaise, bool allowed) => gstPaise is null or 0 ? null : allowed ? ItcAllowed : ItcDisallowed;

    /// <summary>SCC confirm (528, 573).</summary>
    public static string GstOverMessage(long gstPaise, long amountPaise) =>
        $"GST amount ({new Paise(gstPaise)}) is more than the entry amount ({new Paise(amountPaise)}). Save anyway?";

    // ── Duplicates (SCC addEntry 526) ──

    /// <summary>SCC key: same date + same amount + same description (case-insensitive); not void / rejected; not a reversal record.</summary>
    public static IReadOnlyList<long> Duplicates(IEnumerable<ExpenseFact> all, string? description, Paise amount, DateOnly date, long exceptId = 0)
    {
        ArgumentNullException.ThrowIfNull(all);
        var d = (description ?? string.Empty).Trim();
        if (d.Length == 0 || amount == Paise.Zero) return [];
        return all.Where(e => e.ExpenseId != exceptId && !e.IsReversal && e.Status is not (ExpenseState.Rejected or ExpenseState.Void) && e.Amount == amount && e.Date == date
                && string.Equals((e.Description ?? string.Empty).Trim(), d, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ExpenseId).ToList();
    }

    public static string DuplicateMessage(DateOnly date, Paise amount, string description) =>
        $"A similar entry already exists for {Wire(date)} ({amount} — {description}). Save anyway?";

    // ── Month lock (SCC P1-18) ──

    public static string MonthLockedMessage(string month) => $"Month {month} is tax-locked — its tax feed was already generated. Owner override required.";

    /// <summary>DEC-131: the Owner's unlock reason, 3–200 characters (as Accounting's period unlock asks for one).</summary>
    public static string UnlockReason(string? reason)
    {
        var r = (reason ?? string.Empty).Trim();
        if (r.Length < 3) throw HccException.Validation("A reason is required to unlock a month.");
        if (r.Length > NoteMax) throw HccException.Validation($"Keep the reason to {NoteMax} characters.");
        return r;
    }

    /// <summary>SCC genTaxFeed (1491): a future month cannot be locked; HCC's "today" is the business date.</summary>
    public static string LockableMonth(string? month, DateOnly businessDate)
    {
        var m = (month ?? string.Empty).Trim();
        if (m.Length != 7 || !DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || string.CompareOrdinal(m, Ym(businessDate)) > 0)
            throw HccException.Validation("Pick the current or a past month — a future month cannot be locked");
        return m;
    }

    /// <summary>The month's counted figures (SCC genTaxFeed 1492-1504, the expense side) and "GST paid on purchases" (1430).</summary>
    public static MonthSummary Summary(IEnumerable<LedgerRow> rows, string month)
    {
        ArgumentNullException.ThrowIfNull(rows);
        long expense = 0, gst = 0, itc = 0;
        var count = 0;
        var byCat = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => r.Counts && Ym(r.Date) == month))
        {
            expense += r.Amount;
            gst += r.GstPaise ?? 0;
            if (r.ItcAllowed) itc += r.GstPaise ?? 0;
            count++;
            byCat[r.Category] = (byCat.TryGetValue(r.Category, out var v) ? v : 0) + r.Amount;
        }

        return new MonthSummary(expense, count, gst, byCat, itc);
    }

    /// <summary>SCC stale banner (1459-1470): the stored flag, or a counted record of the month posted (or created) after the feed.</summary>
    public static bool Stale(bool storedStale, DateTime generatedUtc, IEnumerable<LedgerRow> rows, string month)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return storedStale || rows.Any(r => r.Counts && Ym(r.Date) == month && (r.PostedUtc ?? r.CreatedUtc) > generatedUtc);
    }

    // ── Budgets (SCC renderBudget 1272-1290, budgetAlertFor 1297-1309, copyBudgets 1327-1340) ──

    /// <summary>SCC tab: pct = round(actual ÷ budget × 100) (JS Math.round); "over" above 100, "near" from 85, "ok"; no budget = "none".</summary>
    public static (int Pct, string Status) BudgetRow(long budget, long actual)
    {
        var pct = budget != 0 ? JsRoundPct(actual, budget) : 0;
        return (pct, budget == 0 ? "none" : pct > 100 ? "over" : pct >= BudgetNear ? "near" : "ok");
    }

    /// <summary>SCC Math.round(ac / bg × 100) on the same values (paise ratios equal rupee ratios).</summary>
    public static int JsRoundPct(long actual, long budget) => (int)JsRound((double)actual / budget * 100);

    /// <summary>SCC budgetAlertFor: business month only, budget &gt; 0, below 90 silent, error at ≥ 100. <paramref name="extra"/> = not yet counted.</summary>
    public static BudgetAlert? Alert(string category, DateOnly date, DateOnly businessDate, long budget, long spend, long extra = 0)
    {
        if (Ym(date) != Ym(businessDate) || budget <= 0) return null;
        var pct = JsRoundPct(spend + extra, budget);
        if (pct < AlertWarn) return null;
        var mon = MonthNames[date.Month - 1];
        return new BudgetAlert(extra != 0 ? $"{category} would be {pct}% of {mon} budget once approved" : $"{category} now {pct}% of {mon} budget",
            pct >= AlertError ? "err" : "warn", pct);
    }

    public static string PrevMonth(string month)
    {
        var y = int.Parse(month[..4], CultureInfo.InvariantCulture);
        var m = int.Parse(month[5..7], CultureInfo.InvariantCulture) - 1;
        if (m < 1) { y--; m = 12; }
        return y.ToString("0000", CultureInfo.InvariantCulture) + "-" + m.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>SCC copyBudgets: heads with a budget &gt; 0 last month and none &gt; 0 this month; SCC's messages when nothing to copy.</summary>
    public static IReadOnlyList<string> CopyHeads(string month, IReadOnlyDictionary<string, long> previous, IReadOnlyDictionary<string, long> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var prev = PrevMonth(month);
        if (!previous.Values.Any(v => v > 0)) throw HccException.Validation($"No budgets found for {prev}");
        var heads = previous.Where(kv => kv.Value > 0 && !(current.TryGetValue(kv.Key, out var c) && c > 0)).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (heads.Count == 0) throw HccException.Validation($"Every head already has a budget for {month}");
        return heads;
    }

    // ── Recurring templates (SCC P1-16, 680-884) ──

    public static string RecRef(long templateId, string month) => $"rec:{templateId.ToString(CultureInfo.InvariantCulture)}:{month}";

    /// <summary>SCC recurringDue: active, not posted this month, and the day of the month ≥ min(due day, days in the month).</summary>
    public static bool RecurringDue(bool active, int dueDay, int postedThisMonth, DateOnly businessDate) =>
        active && postedThisMonth == 0 && businessDate.Day >= Math.Min(dueDay, DateTime.DaysInMonth(businessDate.Year, businessDate.Month));

    /// <summary>SCC addRecTpl / editRecTpl: name, typical amount &gt; 0, due day 1–31.</summary>
    public static (string Name, int DueDay) Template(string? name, long amountPaise, int dueDay)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw HccException.Validation("Template name is required");
        if (n.Length > 60) throw HccException.Validation("Keep the name to 60 characters.");
        if (amountPaise <= 0 || amountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter a typical amount greater than 0");
        if (dueDay is < 1 or > 31) throw HccException.Validation("Due day must be between 1 and 31");
        return (n, dueDay);
    }

    public static string RecurringDescription(string name, string month) => $"{name} — {month}";

    public static string AlreadyPostedMessage(string month, int count, Paise sum) =>
        $"Already posted for {month} ({count} entr{(count == 1 ? "y" : "ies")} · {sum}). Confirming will post ANOTHER entry for this month.";

    // ── Petty cash (SCC renderPetty 1203-1224, pettyOut 1226-1243) ──

    /// <summary>SCC: "Replenish soon" when balance &lt; 15 % of the float and the float &gt; 0.</summary>
    public static string PettyStatus(long floatPaise, long disbursedPaise) =>
        floatPaise != 0 && (floatPaise - disbursedPaise) * 100 < PettyLowPercent * floatPaise ? "Replenish soon" : "Healthy";

    /// <summary>SCC pettyOut description: "Petty: &lt;for, or sundry&gt;".</summary>
    public static string PettyDescription(string? what) => "Petty: " + ((what ?? string.Empty).Trim() is { Length: > 0 } w ? w : "sundry");

    // ── Udhaar (SCC Wave 3, 1547-1651) ──

    /// <summary>SCC udOut: amount − Σ settlements, floored at 0.</summary>
    public static long UdhaarOut(long amount, long paid) => Math.Max(0, amount - paid);

    /// <summary>SCC payReceivable: more than outstanding + ₹0.50 is capped to the outstanding; up to ₹0.50 over is taken as typed.</summary>
    public static (long Amount, bool Capped) UdhaarPayment(long outstanding, long typed) =>
        typed > outstanding + UdhaarTolerancePaise ? (outstanding, true) : (typed, false);

    /// <summary>SCC addReceivable: digits only, kept only when ≥ 10 digits (the last 10).</summary>
    public static string? UdhaarMobile(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : null;
    }

    /// <summary>SCC udBucket: days since the credit date ≤ 30 "0–30", ≤ 60 "31–60", else "60+".</summary>
    public static string UdhaarBucket(DateOnly creditDate, DateOnly today)
    {
        var d = today.DayNumber - creditDate.DayNumber;
        return d <= 30 ? "0–30" : d <= 60 ? "31–60" : "60+";
    }

    /// <summary>SCC remindReceivable text with the hotel's name instead of SCC's firm and town.</summary>
    public static string UdhaarReminder(string hotel, string name, long outstanding, DateOnly? due) =>
        "Namaste " + name + ",\nA gentle reminder from " + hotel + " regarding your pending balance of " + new Paise(outstanding)
        + (due is { } d ? " (due " + Wire(d) + ")" : string.Empty) + ".\nKindly clear it at your convenience. Thank you.";

    // ── CSV (SCC exportLedgerCSV 657-663, exportMonthCSV 1517-1522, dlCSV 664-665) ──

    /// <summary>SCC's columns, then HCC's Ref and Status and (DEC-135) ITC — allowed / disallowed for a bill with GST, empty without.</summary>
    public static readonly IReadOnlyList<string> LedgerColumns =
        ["Date", "Firm", "Type", "Category", "Mode", "Vendor", "Description", "Amount", "Source", "Void", "VoidReason", "CreatedBy", "Store", "GSTAmt", "GSTRate%", "Ref", "Status", "ITC"];

    public static readonly IReadOnlyList<string> MonthColumns =
        ["Date", "Firm", "Type", "Category", "Mode", "Vendor", "Description", "Amount", "Source", "GSTAmt", "GSTRate%", "Ref", "Status", "ITC"];

    /// <summary>SCC writes the JS number of rupees (1180, 1180.5, -850); the same text from paise.</summary>
    public static string Rupees(long paise) => (paise / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    public static string Percent(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>SCC ledger CSV: every record (void ones too), date ascending, ledger_&lt;business date&gt;.csv. Firm = the hotel; Store = Unassigned.</summary>
    public static (string FileName, string Csv, int Rows) LedgerCsv(string hotel, DateOnly businessDate, IEnumerable<LedgerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = rows.OrderBy(r => r.Date).ThenBy(r => r.ExpenseId).ToList();
        var sb = new StringBuilder(Line(LedgerColumns));
        foreach (var r in list)
            sb.Append('\n').Append(Line([Wire(r.Date), Firm(hotel), "expense", r.Category, r.Method, r.Vendor, r.DescriptionOrVendor, Rupees(r.Amount), r.Source ?? "manual",
                r.Status == ExpenseState.Void ? "Y" : string.Empty, r.VoidReason ?? string.Empty, r.CreatedBy, "Unassigned",
                r.GstPaise is { } g ? Rupees(g) : string.Empty, r.GstRateBps is { } b ? Percent(b) : string.Empty, Ref(r.ExpenseId), r.Status, ItcText(r.GstPaise, r.ItcAllowed) ?? string.Empty]));
        return ("ledger_" + Wire(businessDate) + ".csv", sb.ToString(), list.Count);
    }

    /// <summary>SCC month CSV: the month's counted records, date ascending, expense_month_&lt;YYYY-MM&gt;.csv.</summary>
    public static (string FileName, string Csv, int Rows) MonthCsv(string hotel, string month, IEnumerable<LedgerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = rows.Where(r => r.Counts && Ym(r.Date) == month).OrderBy(r => r.Date).ThenBy(r => r.ExpenseId).ToList();
        var sb = new StringBuilder(Line(MonthColumns));
        foreach (var r in list)
            sb.Append('\n').Append(Line([Wire(r.Date), Firm(hotel), "expense", r.Category, r.Method, r.Vendor, r.DescriptionOrVendor, Rupees(r.Amount), r.Source ?? "manual",
                r.GstPaise is { } g ? Rupees(g) : string.Empty, r.GstRateBps is { } b ? Percent(b) : string.Empty, Ref(r.ExpenseId), r.Status, ItcText(r.GstPaise, r.ItcAllowed) ?? string.Empty]));
        return ("expense_month_" + month + ".csv", sb.ToString(), list.Count);
    }

    private static string Firm(string hotel) => string.IsNullOrWhiteSpace(hotel) ? "Unassigned" : hotel;

    private static string Ref(long id) => "EXP-" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>SCC dlCSV quoting (quote only when the cell has a quote, comma or line break) + HCC's formula guard (A06-08).</summary>
    private static string Line(IEnumerable<string> cells) => string.Join(',', cells.Select(Cell));

    private static string Cell(string value)
    {
        var t = CsvFormula.Neutralise(value ?? string.Empty);
        return t.IndexOfAny(['"', ',', '\r', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : t;
    }
}

/// <summary>One expense record as the SCC registers read it (ledger CSV, month summary, budgets, stale rule).</summary>
public sealed record LedgerRow(long ExpenseId, DateOnly Date, string Category, string Method, string Vendor, string? Description, long Amount, string? Source,
    string? SourceRef, string Status, string? VoidReason, string CreatedBy, long? GstPaise, int? GstRateBps, bool IsReversal, DateTime CreatedUtc, DateTime? PostedUtc,
    bool ItcAllowed = false)
{
    /// <summary>Posted / Reversed originals and reversal records (prototype <c>expenseCounts</c>).</summary>
    public bool Counts => ExpenseRules.Counts(Status);

    public string DescriptionOrVendor => Description ?? Vendor;
}

/// <summary>The month's counted figures; <c>GstItcPaise</c> (DEC-135) = the part of the GST paid marked ITC allowed.</summary>
public sealed record MonthSummary(long ExpensePaise, int Count, long GstPaidPaise, IReadOnlyDictionary<string, long> ByCategory, long GstItcPaise = 0);

public sealed record BudgetAlert(string Message, string Style, int Pct);
