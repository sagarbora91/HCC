using System.Globalization;
using System.Text;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Stays;

namespace Hcc.Domain.Customers;

/// <summary>A guest profile as the customer read model sees it (gst.guest; never any ID number, DEC-068).</summary>
public sealed record CrmGuest(long GuestId, string Name, string Phone, string? Email, string? Preferences, string? Notes);

/// <summary>
/// One booking of a customer (inv.reservation, plus the stay/registration when there is one). <c>TotalPaise</c> is the folio total (net + tax) in paise;
/// <c>RatePaise</c> the average nightly room rate (net) in paise; both are 0 when there is no folio yet.
/// </summary>
public sealed record CrmStay(long ReservationId, string PublicNo, long? GuestId, string GuestName, DateOnly Arrival, DateOnly Departure, string Status, string? Room, long TotalPaise, long RatePaise)
{
    public const string Held = "Held", Confirmed = "Confirmed", InHouse = "InHouse", Departed = "Departed", Cancelled = "Cancelled", NoShow = "NoShow", Expired = "Expired";

    public int Nights => Departure.DayNumber - Arrival.DayNumber;

    public bool Happened => Status is InHouse or Departed;
}

public sealed record Customer(
    string Key, string Name, string Phone, string? Email, IReadOnlyList<long> GuestIds, IReadOnlyList<CrmStay> Stays, IReadOnlyList<CrmStay> Completed,
    int Nights, long LifetimePaise, long AvgRatePaise, DateOnly? FirstStay, DateOnly? LastStay, CrmStay? Current, IReadOnlyList<CrmStay> Upcoming, int Cancelled, IReadOnlyList<string> TopRooms);

public sealed record DuplicateGroup(string Reason, IReadOnlyList<string> Keys);

/// <summary>
/// Customer 360 rules, ported from the prototype's pure selectors (<c>customerIndex</c>, <c>customerSearch</c>) plus the production additions:
/// duplicate detection (phone/e-mail/name normalisation), merge validation and the maker-checker decision rules. No I/O.
/// </summary>
public static class CustomerRules
{
    public const string ReasonEmail = "email", ReasonNameOnly = "name-without-phone";
    public const int TagMax = 30, TagsMax = 12, NoteMax = 500, PrefMax = 500, ReasonMin = 5, ReasonMax = 300;

    /// <summary>Last 10 digits of a mobile number when it has at least 10 digits, else "" (prototype <c>customerPhoneKey</c>).</summary>
    public static string PhoneKey(string? phone)
    {
        var d = Digits(phone);
        return d.Length >= 10 ? d[^10..] : string.Empty;
    }

    public static string Digits(string? s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? string.Empty) if (char.IsAsciiDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Lower case, anything but a-z/0-9 becomes a space, spaces collapsed; the prototype's "(fictional)" marker is dropped.</summary>
    public static string NormName(string? name)
    {
        var s = (name ?? string.Empty).Replace("(fictional)", " ", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        var space = true;
        foreach (var c in s)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Lower-cased e-mail; for gmail.com / googlemail.com the dots and a +tag of the local part are ignored. "" when not an address.</summary>
    public static string NormEmail(string? email)
    {
        var e = (email ?? string.Empty).Trim().ToLowerInvariant();
        var at = e.LastIndexOf('@');
        if (at <= 0 || at == e.Length - 1) return string.Empty;
        var local = e[..at];
        var domain = e[(at + 1)..];
        if (domain is "gmail.com" or "googlemail.com")
        {
            var plus = local.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", string.Empty, StringComparison.Ordinal);
            domain = "gmail.com";
        }

        return local.Length == 0 ? string.Empty : local + "@" + domain;
    }

    /// <summary>The customer key of a guest/booking: mobile key, else <c>n:</c> + normalised name (prototype rule).</summary>
    public static string KeyOf(string? phone, string? name)
    {
        var p = PhoneKey(phone);
        return p.Length > 0 ? p : "n:" + NormName(name);
    }

    /// <summary>Follows merge aliases to the surviving key (cycle-safe).</summary>
    public static string Resolve(string key, IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var seen = new HashSet<string>(StringComparer.Ordinal) { key };
        var cur = key;
        while (aliases.TryGetValue(cur, out var next) && seen.Add(next)) cur = next;
        return cur;
    }

    private static string Clean(string? s) => (s ?? string.Empty).Replace("(fictional)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    private static bool IsNameKey(string key) => key.StartsWith("n:", StringComparison.Ordinal);

    private static List<string> Top(IEnumerable<string?> xs, int n) =>
        xs.Where(x => !string.IsNullOrEmpty(x)).GroupBy(x => x!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(n).Select(g => g.Key).ToList();

    /// <summary>
    /// All customers, newest stay first (prototype <c>customerIndex</c>). Expired holds are not bookings and are left out.
    /// <paramref name="aliases"/> = approved merges (source key to target key): the duplicate's stays are listed under the survivor.
    /// </summary>
    public static IReadOnlyList<Customer> Index(IReadOnlyList<CrmGuest> guests, IReadOnlyList<CrmStay> stays, IReadOnlyDictionary<string, string> aliases, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(guests);
        ArgumentNullException.ThrowIfNull(stays);
        ArgumentNullException.ThrowIfNull(aliases);
        var byGuest = guests.ToDictionary(g => g.GuestId);
        var byPhone = new Dictionary<string, CrmGuest>(StringComparer.Ordinal);
        foreach (var g in guests.OrderBy(g => g.GuestId))
        {
            var k = PhoneKey(g.Phone);
            if (k.Length > 0) byPhone.TryAdd(k, g);
        }

        var groups = new Dictionary<string, List<CrmStay>>(StringComparer.Ordinal);
        foreach (var s in stays.Where(s => s.Status != CrmStay.Expired))
        {
            var g = s.GuestId is { } id && byGuest.TryGetValue(id, out var gg) ? gg : null;
            var key = Resolve(KeyOf(g?.Phone, g?.Name ?? s.GuestName), aliases);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(s);
        }

        var all = new List<Customer>();
        foreach (var (key, bs) in groups)
        {
            var ordered = bs.OrderByDescending(b => b.Arrival).ThenByDescending(b => b.PublicNo, StringComparer.Ordinal).ToList();
            var profiles = ordered.Select(b => b.GuestId is { } id && byGuest.TryGetValue(id, out var g) ? g : null).OfType<CrmGuest>().ToList();
            var guest = profiles.FirstOrDefault() ?? (IsNameKey(key) ? null : byPhone.GetValueOrDefault(key));
            var completed = ordered.Where(b => b.Happened).ToList();
            var latest = ordered[0];
            var avg = completed.Count == 0 ? 0 : (long)Math.Round((decimal)completed.Sum(b => b.RatePaise) / completed.Count, MidpointRounding.AwayFromZero);
            all.Add(new Customer(key, Clean(guest?.Name ?? latest.GuestName), guest?.Phone ?? string.Empty,
                guest?.Email ?? profiles.Select(p => p.Email).FirstOrDefault(e => !string.IsNullOrEmpty(e)),
                profiles.Select(p => p.GuestId).Distinct().OrderBy(x => x).ToList(), ordered, completed,
                completed.Sum(b => b.Nights), completed.Sum(b => b.TotalPaise), avg,
                completed.Count > 0 ? completed[^1].Arrival : null, completed.Count > 0 ? completed[0].Arrival : null,
                ordered.FirstOrDefault(b => b.Status == CrmStay.InHouse),
                ordered.Where(b => (b.Status is CrmStay.Confirmed or CrmStay.Held) && b.Departure >= today).Reverse().ToList(),
                ordered.Count(b => b.Status is CrmStay.Cancelled or CrmStay.NoShow),
                Top(completed.Select(b => b.Room), 3)));
        }

        return all.OrderByDescending(c => c.Stays[0].Arrival).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Search by name (every typed word) or mobile digits (3+; never for roles that only see masked numbers). No booking-ID matching.</summary>
    public static IReadOnlyList<Customer> Search(IReadOnlyList<Customer> list, string? q, bool phoneOk = true)
    {
        ArgumentNullException.ThrowIfNull(list);
        var text = (q ?? string.Empty).Trim();
        if (text.Length == 0) return list;
        if (text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
        {
            var words = NormName(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return list.Where(c => words.All(w => NormName(c.Name).Contains(w, StringComparison.Ordinal))).ToList();
        }

        var digits = Digits(text);
        if (!phoneOk || digits.Length < 3) return [];
        return list.Where(c => Digits(c.Phone).Contains(digits, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Duplicate candidates: customers sharing a normalised e-mail address, and a customer without a mobile number whose normalised name equals
    /// that of exactly one customer who has one. Groups are deterministic (keys sorted, groups by first key).
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> FindDuplicates(IReadOnlyList<Customer> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var groups = new List<DuplicateGroup>();
        foreach (var g in list.Where(c => NormEmail(c.Email).Length > 0).GroupBy(c => NormEmail(c.Email), StringComparer.Ordinal).Where(g => g.Count() > 1))
            groups.Add(new DuplicateGroup(ReasonEmail, g.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList()));
        var phoned = list.Where(c => !IsNameKey(c.Key)).GroupBy(c => NormName(c.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var c in list.Where(c => IsNameKey(c.Key)))
            if (phoned.TryGetValue(NormName(c.Name), out var match) && match.Count == 1)
                groups.Add(new DuplicateGroup(ReasonNameOnly, new[] { c.Key, match[0].Key }.OrderBy(k => k, StringComparer.Ordinal).ToList()));
        return groups.OrderBy(g => g.Keys[0], StringComparer.Ordinal).ThenBy(g => g.Reason, StringComparer.Ordinal).ToList();
    }

    /// <summary>Which of two customers survives by default: the one with a mobile number, then more stays, then the earlier key.</summary>
    public static string SuggestSurvivor(Customer a, Customer b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var pa = !IsNameKey(a.Key);
        var pb = !IsNameKey(b.Key);
        if (pa != pb) return pa ? a.Key : b.Key;
        if (a.Stays.Count != b.Stays.Count) return a.Stays.Count > b.Stays.Count ? a.Key : b.Key;
        return string.CompareOrdinal(a.Key, b.Key) <= 0 ? a.Key : b.Key;
    }

    public static string CleanReason(string? reason)
    {
        var r = (reason ?? string.Empty).Trim();
        if (r.Length < ReasonMin || r.Length > ReasonMax) throw HccException.Validation($"Give a reason of {ReasonMin}-{ReasonMax} characters.");
        return r;
    }

    public static string CleanTag(string? tag)
    {
        var t = string.Join(' ', (tag ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (t.Length == 0 || t.Length > TagMax) throw HccException.Validation($"A tag is 1-{TagMax} characters.");
        if (t.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'))) throw HccException.Validation("A tag may contain letters, digits, spaces, - and _ only.");
        return t;
    }

    public static string CleanNote(string? body)
    {
        var b = (body ?? string.Empty).Trim();
        if (b.Length == 0 || b.Length > NoteMax) throw HccException.Validation($"A note is 1-{NoteMax} characters.");
        return b;
    }

    public static string CleanPreferences(string? text)
    {
        var p = (text ?? string.Empty).Trim();
        if (p.Length > PrefMax) throw HccException.Validation($"Preferences are limited to {PrefMax} characters.");
        return p;
    }

    /// <summary>Maker is not checker for a merge decision.</summary>
    public static void EnsureChecker(int proposedBy, int decidedBy)
    {
        if (proposedBy == decidedBy) throw new HccException(HccErrorCode.FORBIDDEN, "A different person must decide this merge (maker-checker).");
    }

    public static string MaskPhone(string? phone) => GuestRules.MaskPhone(phone);

    public static string MaskEmail(string? email) => GuestRules.MaskEmail(email);

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
