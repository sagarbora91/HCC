using System.Globalization;

namespace Hcc.Domain.Hall;

/// <summary>A numbered T&amp;C clause (contract §2 TncClause).</summary>
public sealed record HallTncClause(int N, string Heading, string Text);

/// <summary>One version of a T&amp;C template, in force from <see cref="EffectiveFrom"/>.</summary>
public sealed record HallTncVersion(int V, DateOnly EffectiveFrom, IReadOnlyList<HallTncClause> Clauses);

/// <summary>A T&amp;C template: one per event type, or GENERAL (used where an event type has none).</summary>
public sealed record HallTncTemplate(string Code, string EventType, IReadOnlyList<HallTncVersion> Versions);

/// <summary>The copy of the clauses a quotation version keeps (immutable once sent).</summary>
public sealed record HallTncSnapshot(string TemplateCode, int V, IReadOnlyList<HallTncClause> Clauses, bool HasPlaceholder);

public sealed record HallLostReason(string Id, string Label, bool NeedsNote);

/// <summary>
/// F6 lane H2 quotation + T&amp;C rules (F6-CONTRACT §2–§3; spec §6–§8) — the server twin of the prototype's
/// <c>src/domain/streams/hall-quote.ts</c> and <c>handlers/hall-quote.ts</c>: numbering QT/&lt;fy&gt;/0001 restarting every 1 April (versions keep
/// the number), the version status machine (Draft → Sent → Superseded / Expired / Accepted / Lost; Draft → Cancelled), the T&amp;C template in
/// force and its snapshot, the "OWNER TO REPLACE" guard (Owner override with a reason, once per quotation), discount limits through
/// <see cref="HallBilling.DiscountWithin"/> (above the sender's limit → Owner approval, never the author), validity 15 days.
/// </summary>
public static class HallQuoteRules
{
    public const string DefaultPrefix = "QT";
    public const int Padding = 4;

    public static readonly IReadOnlyList<string> Setups = ["THEATRE", "ROUND", "USHAPE", "FLOATING", "CLASSROOM"];

    public static readonly IReadOnlyList<string> LineKinds = ["HALL", "MENU", "EXTRA", "ROOM", "OTHER"];

    public static readonly IReadOnlyList<string> GstClasses = [HallBilling.ClassHallRent, HallBilling.ClassBanquetFood, HallBilling.ClassService, HallBilling.ClassRoom];

    /// <summary>The seeded clause headings (spec §7), in print order.</summary>
    public static readonly IReadOnlyList<string> Headings = ["Advance and payment", "Cancellation slabs", "Guaranteed pax and extra plates", "Timings and overtime", "Damages",
        "Outside caterers and decorators", "Music and noise limits", "Liquor and licences (customer’s responsibility)", "GST note", "Force majeure"];

    public static readonly DateOnly SeedEffective = new(2026, 4, 1);

    /// <summary>Used while the property has no lost-reason rows (seed 4349 writes the same list).</summary>
    public static readonly IReadOnlyList<HallLostReason> DefaultLostReasons =
    [
        new("LR-PRICE", "Price too high", false), new("LR-DATE", "Date not available", false), new("LR-OTHERVENUE", "Chose another venue", false),
        new("LR-CANCELLED", "Event cancelled", false), new("LR-NORESPONSE", "No response", false), new("LR-OTHER", "Other", true),
    ];

    /// <summary>The placeholder General template, used while the property has no template rows.</summary>
    public static HallTncTemplate DefaultTemplate() =>
        new("T-GEN", "GENERAL", [new HallTncVersion(1, SeedEffective, Headings.Select((h, i) => new HallTncClause(i + 1, h, HallRules.Placeholder)).ToList())]);

    // ── numbering ──

    /// <summary>FY code of a date: 2026-11-03 → 2627, 2027-03-31 → 2627, 2027-04-01 → 2728.</summary>
    public static string FyCode(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return (start % 100).ToString("00", CultureInfo.InvariantCulture) + ((start + 1) % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    public static string Format(string prefix, string fy, int seq) => $"{prefix}/{fy}/{seq.ToString(new string('0', Padding), CultureInfo.InvariantCulture)}";

    // ── roles (PERM-HAL-002 / 005) ──

    public static bool MayQuote(string role) => role is "Owner" or "Manager" or "Reception";

    public static bool MayEditAdvancePlan(string role) => role is "Owner" or "Manager";

    public static bool MayEditTemplates(string role) => role == "Owner";

    public static bool MayOverridePlaceholder(string role) => role == "Owner";

    public static bool MayApproveDiscount(string role) => role == "Owner";

    public static bool MayRunExpiry(string role) => role is "Owner" or "Manager";

    // ── status machine ──

    public static bool CanEdit(string status) => status == "DRAFT";

    public static bool CanRevise(string status) => status is "SENT" or "EXPIRED";

    public static bool CanSend(string status) => status == "DRAFT";

    public static bool CanLose(string status) => status is "DRAFT" or "SENT" or "EXPIRED";

    public static bool CanCancel(string status) => status == "DRAFT";

    /// <summary>The status the replaced version takes on a revise: SENT → SUPERSEDED; EXPIRED stays EXPIRED.</summary>
    public static string StatusAfterRevise(string status) => status == "SENT" ? "SUPERSEDED" : status;

    /// <summary>A SENT version expires the day after its validity date (the night run).</summary>
    public static bool Expired(string status, DateOnly validUntil, DateOnly today) => status == "SENT" && validUntil < today;

    /// <summary>The reminder window: the validity ends within <see cref="HallRules.ExpiryReminderDays"/> days.</summary>
    public static bool ExpiryReminder(string status, DateOnly validUntil, DateOnly today) =>
        status == "SENT" && validUntil >= today && validUntil.AddDays(-HallRules.ExpiryReminderDays) <= today;

    public static DateOnly ValidUntil(DateOnly date) => date.AddDays(HallRules.ValidityDays);

    // ── T&C ──

    public static HallTncTemplate? TemplateFor(IReadOnlyList<HallTncTemplate> templates, string eventType)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var list = templates.Count > 0 ? templates : [DefaultTemplate()];
        return list.FirstOrDefault(t => t.EventType == eventType) ?? list.FirstOrDefault(t => t.EventType == "GENERAL");
    }

    public static HallTncVersion VersionInForce(HallTncTemplate t, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(t);
        var sorted = t.Versions.OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.V).ToList();
        return sorted.FirstOrDefault(v => v.EffectiveFrom <= date) ?? sorted[^1];
    }

    public static bool HasPlaceholder(IEnumerable<HallTncClause> clauses) =>
        clauses.Any(c => c.Text.Contains(HallRules.Placeholder, StringComparison.Ordinal) || c.Heading.Contains(HallRules.Placeholder, StringComparison.Ordinal));

    public static HallTncSnapshot Snapshot(IReadOnlyList<HallTncTemplate> templates, string eventType, DateOnly date)
    {
        var t = TemplateFor(templates, eventType);
        if (t is null) return new HallTncSnapshot("", 0, [], true);
        var v = VersionInForce(t, date);
        var clauses = v.Clauses.Select(c => new HallTncClause(c.N, c.Heading, c.Text)).ToList();
        return new HallTncSnapshot(t.Code, v.V, clauses, clauses.Count == 0 || HasPlaceholder(clauses));
    }

    /// <summary>Final = no placeholder, or the Owner overrode it.</summary>
    public static bool Final(bool hasPlaceholder, bool overridden) => !hasPlaceholder || overridden;

    /// <summary>Null when the clauses are acceptable: 1–40, heading 2–80, text 3–2000 characters.</summary>
    public static string? ClausesProblem(IReadOnlyList<(string Heading, string Text)>? clauses)
    {
        if (clauses is null || clauses.Count == 0) return "Give at least one clause.";
        if (clauses.Count > 40) return "At most 40 clauses.";
        for (var i = 0; i < clauses.Count; i++)
        {
            var (h, t) = (clauses[i].Heading?.Trim() ?? "", clauses[i].Text?.Trim() ?? "");
            if (h.Length is < 2 or > 80) return $"Clause {i + 1}: heading 2–80 characters.";
            if (t.Length is < 3 or > 2000) return $"Clause {i + 1}: text 3–2000 characters.";
        }

        return null;
    }

    // ── discounts ──

    /// <summary>True when the lines' discount is above the role's limit and no Owner approval is on the version.</summary>
    public static bool NeedsApproval(IReadOnlyList<HallLine> lines, string role) => !HallBilling.DiscountWithin(lines, HallBilling.DiscountLimitBps(role));

    /// <summary>"10 %" / "12.5 %".</summary>
    public static string Pct(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %";
}
