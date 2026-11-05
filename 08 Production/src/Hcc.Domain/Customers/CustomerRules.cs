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

/// <summary>PER-01: the narrow guest row the customer key pass needs (no e-mail, preferences or notes).</summary>
public sealed record CrmGuestHead(long GuestId, string Name, string Phone);

/// <summary>
/// PER-01: the narrow booking row the customer key pass needs (no departure, room or folio). Expired holds are not passed in (they are not bookings).
/// <c>GuestName</c> may be null when <c>GuestId</c> is a profile of the property: the booking name is then never used (the profile's name wins).
/// </summary>
public sealed record CrmStayHead(long ReservationId, string PublicNo, long? GuestId, string? GuestName, DateOnly Arrival);

/// <summary>
/// PER-01: one customer as the key pass sees it - the same key, display name, mobile and newest arrival as <see cref="CustomerRules.Index"/> gives,
/// <c>Slot</c> identifies it in its <see cref="CustomerHeadIndex"/> (to load the full customer for a page or a profile only).
/// </summary>
public sealed record CustomerHead(string Key, string Name, string Phone, DateOnly LatestArrival, int Slot);

/// <summary>PER-01: the heads of <see cref="CustomerRules.Heads"/> in Index order, and the rows that make up any chosen heads.</summary>
public sealed class CustomerHeadIndex
{
    private readonly IReadOnlyList<CrmStayHead> _stays;
    private readonly int[] _staySlot;
    private readonly long[] _stayGuest;
    private readonly long[] _fallback;

    internal CustomerHeadIndex(IReadOnlyList<CustomerHead> heads, IReadOnlyList<CrmStayHead> stays, int[] staySlot, long[] stayGuest, long[] fallback)
    {
        Heads = heads;
        _stays = stays;
        _staySlot = staySlot;
        _stayGuest = stayGuest;
        _fallback = fallback;
    }

    public IReadOnlyList<CustomerHead> Heads { get; }

    /// <summary>The booking ids (in row order) and guest profile ids (ascending, distinct) that make up <paramref name="heads"/>.</summary>
    public (IReadOnlyList<long> ReservationIds, IReadOnlyList<long> GuestIds) RowsOf(IEnumerable<CustomerHead> heads)
    {
        ArgumentNullException.ThrowIfNull(heads);
        var want = new HashSet<int>();
        var guests = new SortedSet<long>();
        foreach (var h in heads)
        {
            want.Add(h.Slot);
            if (_fallback[h.Slot] != 0) guests.Add(_fallback[h.Slot]);
        }

        var res = new List<long>();
        if (want.Count == 0) return (res, []);
        for (var i = 0; i < _stays.Count; i++)
        {
            if (!want.Contains(_staySlot[i])) continue;
            res.Add(_stays[i].ReservationId);
            if (_stayGuest[i] != 0) guests.Add(_stayGuest[i]);
        }

        return (res, guests.ToList());
    }
}

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
        // PER-01: no StringBuilder per call (the key pass runs this for every guest); same result.
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var n = 0;
        foreach (var c in s) if (char.IsAsciiDigit(c)) n++;
        if (n == s.Length) return s;
        if (n == 0) return string.Empty;
        return string.Create(n, s, static (span, src) =>
        {
            var i = 0;
            foreach (var c in src) if (char.IsAsciiDigit(c)) span[i++] = c;
        });
    }

    /// <summary>
    /// AF-DEC-4 (ID-01): NFKC, lower case, letters / digits of any script and combining marks (Devanagari matras) are kept, anything else becomes one space,
    /// spaces collapsed; the prototype's "(fictional)" marker is dropped. ASCII input gives exactly what the old a-z/0-9 rule gave.
    /// </summary>
    public static string NormName(string? name)
    {
        var raw = name ?? string.Empty;
        // PER-01: plain ASCII without the marker (most names) gives the same result in one pass and one allocation.
        if (System.Text.Ascii.IsValid(raw) && raw.IndexOf("(fictional)", StringComparison.OrdinalIgnoreCase) < 0) return NormAscii(raw);
        var s = raw.Replace("(fictional)", " ", StringComparison.OrdinalIgnoreCase).Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder();
        var space = true;
        foreach (var r in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (Rune.IsLetterOrDigit(r) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) { sb.Append(r.ToString()); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }

        return sb.ToString().Trim();
    }

    // ASCII form of NormName: NFKC and lower-casing are plain a-z there, letters / digits are [a-z0-9], everything else is one separating space.
    private static string NormAscii(string s)
    {
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        var n = 0;
        var space = true;
        foreach (var ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buf[n++] = char.ToLowerInvariant(ch);
                space = false;
            }
            else if (!space)
            {
                buf[n++] = ' ';
                space = true;
            }
        }

        if (n > 0 && buf[n - 1] == ' ') n--;
        return buf[..n].SequenceEqual(s) ? s : new string(buf[..n]);
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
        if (p.Length > 0) return p;
        var n = NormName(name);
        // AF-DEC-4: a symbol-only name has no usable text; key it by its raw bytes so two different unusable names never share one customer.
        return n.Length > 0 ? "n:" + n : "n:#" + Convert.ToHexString(Encoding.UTF8.GetBytes((name ?? string.Empty).Trim()));
    }

    /// <summary>Follows merge aliases to the surviving key (cycle-safe).</summary>
    public static string Resolve(string key, IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (aliases.Count == 0 || !aliases.ContainsKey(key)) return key; // PER-01: the common case needs no cycle guard
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

    private sealed class HeadAcc(string key)
    {
        public string Key { get; } = key;
        public CrmStayHead? Latest { get; set; }
        public CrmStayHead? ProfileStay { get; set; }
        public CrmGuestHead? Profile { get; set; }
    }

    // Index orders a customer's bookings by arrival, then booking number (ordinal), both descending; the first of that order wins.
    private static bool Newer(CrmStayHead a, CrmStayHead b) => a.Arrival > b.Arrival || (a.Arrival == b.Arrival && string.CompareOrdinal(a.PublicNo, b.PublicNo) > 0);

    /// <summary>
    /// PER-01: the key pass of <see cref="Index"/> on narrow rows (no folio, room or contact columns). Same keys (KeyOf + merge aliases), display names, mobiles
    /// and order as Index. <see cref="CustomerHeadIndex.RowsOf"/> gives the bookings and guest profiles Index uses for chosen heads (the profiles of their
    /// bookings and, for a mobile key without one, the first profile with that mobile), so <c>Index(those guests, those bookings)</c> rebuilds exactly the
    /// customers Index gives on the whole property. Per customer it keeps only the head (no id lists), so a page costs one pass over the rows.
    /// </summary>
    public static CustomerHeadIndex Heads(IReadOnlyList<CrmGuestHead> guests, IReadOnlyList<CrmStayHead> stays, IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(guests);
        ArgumentNullException.ThrowIfNull(stays);
        ArgumentNullException.ThrowIfNull(aliases);
        var byGuest = new Dictionary<long, int>(guests.Count);
        for (var i = 0; i < guests.Count; i++) byGuest.Add(guests[i].GuestId, i);
        var guestKey = new string?[guests.Count];
        var nameKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var acc = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<HeadAcc>();
        var staySlot = new int[stays.Count];
        var stayGuest = new long[stays.Count]; // the property profile of the booking, 0 = none
        for (var si = 0; si < stays.Count; si++)
        {
            var s = stays[si];
            CrmGuestHead? g = null;
            string? key;
            if (s.GuestId is { } id && byGuest.TryGetValue(id, out var gi))
            {
                g = guests[gi];
                key = guestKey[gi] ??= Resolve(KeyOf(g.Phone, g.Name), aliases);
                stayGuest[si] = g.GuestId;
            }
            else
            {
                var bookingName = s.GuestName ?? string.Empty;
                if (!nameKey.TryGetValue(bookingName, out key)) nameKey[bookingName] = key = Resolve(KeyOf(null, bookingName), aliases);
            }

            if (!acc.TryGetValue(key, out var slot))
            {
                acc[key] = slot = order.Count;
                order.Add(new HeadAcc(key));
            }

            staySlot[si] = slot;
            var a = order[slot];
            if (a.Latest is null || Newer(s, a.Latest)) a.Latest = s;
            if (g is not null && (a.ProfileStay is null || Newer(s, a.ProfileStay)))
            {
                a.ProfileStay = s;
                a.Profile = g;
            }
        }

        // The first profile (lowest id) per mobile key, only needed for a mobile key none of whose bookings has a profile (a merge target).
        Dictionary<string, CrmGuestHead>? byPhone = null;
        var fallback = new long[order.Count];
        var all = new List<CustomerHead>(order.Count);
        for (var slot = 0; slot < order.Count; slot++)
        {
            var a = order[slot];
            var guest = a.Profile;
            if (guest is null && !IsNameKey(a.Key))
            {
                if (byPhone is null)
                {
                    byPhone = new Dictionary<string, CrmGuestHead>(StringComparer.Ordinal);
                    foreach (var pg in guests.OrderBy(x => x.GuestId))
                    {
                        var k = PhoneKey(pg.Phone);
                        if (k.Length > 0) byPhone.TryAdd(k, pg);
                    }
                }

                guest = byPhone.GetValueOrDefault(a.Key);
                if (guest is not null) fallback[slot] = guest.GuestId;
            }

            all.Add(new CustomerHead(a.Key, Clean(guest?.Name ?? a.Latest!.GuestName), guest?.Phone ?? string.Empty, a.Latest!.Arrival, slot));
        }

        return new CustomerHeadIndex(all.OrderByDescending(c => c.LatestArrival).ThenBy(c => c.Name, StringComparer.Ordinal).ToList(), stays, staySlot, stayGuest, fallback);
    }
    /// <summary>Search by name (every typed word) or mobile digits (3+; never for roles that only see masked numbers). No booking-ID matching.</summary>
    public static IReadOnlyList<Customer> Search(IReadOnlyList<Customer> list, string? q, bool phoneOk = true) => Match(list, q, phoneOk, c => c.Name, c => c.Phone);

    /// <summary>PER-01: <see cref="Search(IReadOnlyList{Customer}, string?, bool)"/> on the key-pass heads (same rule, same order).</summary>
    public static IReadOnlyList<CustomerHead> SearchHeads(IReadOnlyList<CustomerHead> list, string? q, bool phoneOk = true) => Match(list, q, phoneOk, c => c.Name, c => c.Phone);

    private static IReadOnlyList<T> Match<T>(IReadOnlyList<T> list, string? q, bool phoneOk, Func<T, string> name, Func<T, string> phone)
    {
        ArgumentNullException.ThrowIfNull(list);
        var text = (q ?? string.Empty).Trim();
        if (text.Length == 0) return list;
        if (text.Any(char.IsLetter))
        {
            var words = NormName(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return list.Where(c =>
            {
                var n = NormName(name(c));
                return words.All(w => n.Contains(w, StringComparison.Ordinal));
            }).ToList();
        }

        var digits = Digits(text);
        if (!phoneOk || digits.Length < 3) return [];
        return list.Where(c => Digits(phone(c)).Contains(digits, StringComparison.Ordinal)).ToList();
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

/// <summary>LF B2 (LF-CONTRACT §7): ONE rule for a guest's credit-account key = the Udhaar party id the cut-over opening entry uses (prototype
/// customers.ts customerKeyForCredit): the phone's last 10 digits, or the guest id when the phone has fewer than 10 digits.</summary>
public static class CustomerCreditKey
{
    public static string Of(long guestId, string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length >= 10 ? d[^10..] : "G-" + guestId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
