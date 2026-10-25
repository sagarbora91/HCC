using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Stays;

/// <summary>An accompanying guest (prototype <c>Guest.party[]</c>).</summary>
public sealed record GuestPartyMember(string Name, string AgeBand, string? Relation);

/// <summary>
/// Editable guest fields (prototype <c>GuestDraft</c>, <c>03 Prototype/src/domain/streams/guests.ts</c>). The ID document is
/// stored as type + last 4 characters only — never the full number (prototype rule; see decision note in the P3 report).
/// </summary>
public sealed record GuestDraft(
    string? Name,
    string? Phone,
    string? Email = null,
    string? Address = null,
    string? Nationality = null,
    string? IdType = null,
    string? IdLast4 = null,
    string? Company = null,
    string? Gstin = null,
    string? Preferences = null,
    string? Notes = null,
    IReadOnlyList<GuestPartyMember>? Party = null);

/// <summary>Validation, normalisation and field masks ported from the prototype's guest stream (REQ-GUEST-004/006).</summary>
public static partial class GuestRules
{
    public const string Restricted = "Restricted";
    public const int MaxParty = 6;

    public static readonly IReadOnlyList<string> AgeBands = ["Adult", "Child", "Infant", "Senior"];

    public static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>
    {
        ["name"] = "Name", ["phone"] = "Phone", ["email"] = "Email", ["address"] = "Address", ["nationality"] = "Nationality",
        ["idType"] = "ID type", ["idLast4"] = "ID last 4", ["gstin"] = "GSTIN", ["company"] = "Company", ["party"] = "Party",
    };

    [GeneratedRegex(@"^[+\d\s()-]+$")]
    private static partial Regex PhoneChars();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex Email();

    [GeneratedRegex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z\d]Z[A-Z\d]$")]
    private static partial Regex Gstin();

    [GeneratedRegex(@"^[A-Za-z0-9]{4}$")]
    private static partial Regex Last4();

    private static string T(string? v) => (v ?? string.Empty).Trim();

    /// <summary>Digits only, last 10 (drops +91 / leading 0).</summary>
    public static string NormalisePhone(string? phone)
    {
        var sb = new StringBuilder();
        foreach (var c in phone ?? string.Empty) if (char.IsAsciiDigit(c)) sb.Append(c);
        var d = sb.ToString();
        return d.Length > 10 ? d[^10..] : d;
    }

    /// <summary>Field → message (first error per field, prototype wording). <paramref name="registration"/> adds address + ID.</summary>
    public static IReadOnlyDictionary<string, string> Validate(GuestDraft d, bool registration)
    {
        ArgumentNullException.ThrowIfNull(d);
        var e = new Dictionary<string, string>();
        if (T(d.Name).Length < 2) e["name"] = "Enter the guest’s name (at least 2 characters).";
        var ph = NormalisePhone(d.Phone);
        if (T(d.Phone).Length == 0) e["phone"] = "Enter a phone number.";
        else if (ph.Length < 10 || !PhoneChars().IsMatch(T(d.Phone))) e["phone"] = "Enter a 10-digit phone number (fictional).";
        if (T(d.Email).Length > 0 && !Email().IsMatch(T(d.Email))) e["email"] = "Enter a valid email or leave it blank.";
        if (T(d.Gstin).Length > 0 && !Gstin().IsMatch(T(d.Gstin).ToUpperInvariant())) e["gstin"] = "GSTIN must be 15 characters in the format 27ABCDE1234F1Z5 (fictional).";
        if (T(d.Gstin).Length > 0 && T(d.Company).Length == 0) e["company"] = "Enter the company name for this GSTIN.";
        if (T(d.IdLast4).Length > 0 && !Last4().IsMatch(T(d.IdLast4))) e["idLast4"] = "Enter exactly the last 4 letters or digits of the ID — never the full number.";
        if (T(d.IdLast4).Length > 0 && T(d.IdType).Length == 0) e["idType"] = "Choose the ID type.";
        var party = d.Party ?? [];
        for (var i = 0; i < party.Count; i++)
        {
            if (T(party[i].Name).Length < 2) e["party"] = $"Party member {i + 1}: enter a name.";
            else if (!AgeBands.Contains(party[i].AgeBand)) e["party"] = $"Party member {i + 1}: choose an age band.";
        }

        if (party.Count > MaxParty) e["party"] = "At most 6 accompanying guests in this demo.";
        if (registration)
        {
            if (T(d.Address).Length == 0) e["address"] = "Enter the address.";
            if (T(d.IdType).Length == 0) e["idType"] = "Choose the ID type.";
            if (T(d.IdLast4).Length == 0) e["idLast4"] = "Enter the last 4 characters of the ID.";
        }

        // Production length limits (SQL columns); the prototype has none.
        if (T(d.Name).Length > 200) e["name"] = "Name is at most 200 characters.";
        if (T(d.Address).Length > 400) e["address"] = "Address is at most 400 characters.";
        if (T(d.Email).Length > 200) e["email"] = "Email is at most 200 characters.";
        if (T(d.Phone).Length > 20) e["phone"] = "Enter a 10-digit phone number (fictional).";
        return e;
    }

    /// <summary>The prototype's single VALIDATION message: first error, plus "(n more: labels)".</summary>
    public static string Summary(IReadOnlyDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var keys = errors.Keys.ToList();
        if (keys.Count == 1) return errors[keys[0]];
        var rest = string.Join(", ", keys.Skip(1).Select(k => FieldLabels.TryGetValue(k, out var l) ? l : k));
        return string.Create(CultureInfo.InvariantCulture, $"{errors[keys[0]]} ({keys.Count - 1} more: {rest})");
    }

    /// <summary>Trimmed copy (upper-case ID last 4 / GSTIN; nationality defaults to Indian; blanks become null).</summary>
    public static GuestDraft Clean(GuestDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        static string? N(string? v) => T(v).Length == 0 ? null : T(v);
        return new GuestDraft(T(d.Name), T(d.Phone), N(d.Email), N(d.Address), N(d.Nationality) ?? "Indian", N(d.IdType),
            N(d.IdLast4)?.ToUpperInvariant(), N(d.Company), N(d.Gstin)?.ToUpperInvariant(), N(d.Preferences), N(d.Notes),
            (d.Party ?? []).Select(p => new GuestPartyMember(T(p.Name), p.AgeBand, N(p.Relation))).ToList());
    }

    /// <summary>"••••••1041" (last 4 digits) or "—".</summary>
    public static string MaskPhone(string? phone)
    {
        var sb = new StringBuilder();
        foreach (var c in phone ?? string.Empty) if (char.IsAsciiDigit(c)) sb.Append(c);
        var d = sb.ToString();
        return d.Length == 0 ? "—" : "••••••" + (d.Length > 4 ? d[^4..] : d);
    }

    /// <summary>"g•••@example.test" or "—".</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "—";
        var at = email.IndexOf('@', StringComparison.Ordinal);
        var user = at < 0 ? email : email[..at];
        var dom = at < 0 ? string.Empty : email[(at + 1)..];
        return (user.Length > 0 ? user[..1] : string.Empty) + "•••@" + dom;
    }
}
