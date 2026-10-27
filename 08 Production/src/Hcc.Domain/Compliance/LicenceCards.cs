using System.Globalization;
using System.Text.Json;

namespace Hcc.Domain.Compliance;

// DEC-133 (owner, 27 Oct 2026; APR-015): the hotel in Latur, Maharashtra also needs its FSSAI licence and its lodging house licence renewed.
// Two HCC licence-renewal cards beside SCC's catalogue (SCC's Maharashtra texts stay). Texts in LicenceCards.json (embedded resource; a prototype
// test checks it equals 03 Prototype/src/domain/streams/compliance-licences.ts). A card exists only while the firm profile carries the licence's
// expiry date; it sits on the FY page of the expiry month, due on the expiry date ("D Mon", so SCC's due-date engine gives the date back),
// risk High, and SCC's control layer gives the internal deadline and escalation. Prototype twin: cmpLicenceCard / cmpLicenceCards.

/// <summary>One licence-renewal card definition (kind "fssai" / "lodging").</summary>
public sealed record LicenceDef(string Kind, string Id, string Cat, string Name, string Freq, string Form, string Who, string What, string Why, string Penalty, string Risk,
    string Responsible, string Reviewer, IReadOnlyList<string> Checklist, IReadOnlyList<string> Workflow, IReadOnlyList<string> Evidence);

public static class LicenceCards
{
    private static readonly string[] ShortMonths = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly Lazy<(string Json, IReadOnlyList<LicenceDef> Defs)> Data = new(Load);

    /// <summary>The embedded definitions text as stored.</summary>
    public static string Json => Data.Value.Json.TrimEnd('\n', '\r');

    public static IReadOnlyList<LicenceDef> Defs => Data.Value.Defs;

    private static (string, IReadOnlyList<LicenceDef>) Load()
    {
        using var stream = typeof(LicenceCards).Assembly.GetManifestResourceStream("Hcc.Domain.Compliance.LicenceCards.json")
            ?? throw new InvalidOperationException("The licence card resource is missing.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        var list = new List<LicenceDef>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string S(string k) => e.GetProperty(k).GetString() ?? string.Empty;
            IReadOnlyList<string> L(string k) => e.GetProperty(k).EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList();
            list.Add(new LicenceDef(S("kind"), S("id"), S("cat"), S("name"), S("freq"), S("form"), S("who"), S("what"), S("why"), S("penalty"), S("risk"), S("responsible"),
                S("reviewer"), L("checklist"), L("workflow"), L("evidence")));
        }

        return (json, list);
    }

    /// <summary>The profile's licence number and expiry date of a kind.</summary>
    public static (string? No, DateOnly? Expiry) Of(CmpProfile? profile, string kind) => profile is null ? (null, null) : kind switch
    {
        "fssai" => (profile.FssaiNo, profile.FssaiExpiry),
        "lodging" => (profile.LodgingNo, profile.LodgingExpiry),
        _ => (null, null),
    };

    /// <summary>The licence obligations (archive keys = the name up to the en dash, as SCC's keys) in the Labour Law category.</summary>
    public static IReadOnlyList<(string Key, string Cat, string Name)> Obligations() =>
        Defs.Select(d => (SccTaxRules.ObligationKey(new SccCard(d.Id, d.Cat, d.Name, string.Empty, false, d.Freq, d.Form, d.Who, d.What, d.Why, d.Penalty)), d.Cat, d.Name)).ToList();

    /// <summary>The card of a kind while its expiry date is set: the card, its page month and its FY (the FY of the expiry date).</summary>
    public static (SccCard Card, string Month, int Fy, DateOnly Expiry)? Card(CmpProfile? profile, string kind)
    {
        var d = Defs.FirstOrDefault(x => x.Kind == kind);
        var (no, expiry) = Of(profile, kind);
        if (d is null || expiry is not { } exp) return null;
        var n = (no ?? string.Empty).Trim();
        var card = new SccCard(d.Id, d.Cat, d.Name, exp.Day.ToString(CultureInfo.InvariantCulture) + " " + ShortMonths[exp.Month - 1], false, d.Freq,
            d.Form + (n.Length > 0 ? " · Licence no. " + n : string.Empty), d.Who, d.What, d.Why, d.Penalty)
        {
            Risk = d.Risk, Responsible = d.Responsible, Reviewer = d.Reviewer, Buffer = null, Checklist = d.Checklist, Workflow = d.Workflow, Evidence = d.Evidence, Licence = d.Kind,
        };
        return (card, CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(exp.Month), SccTaxRules.FyOfDate(exp), exp);
    }

    /// <summary>The card of an item id ("lic_fssai" / "lic_lodging") while its expiry date is set.</summary>
    public static (SccCard Card, string Month, int Fy, DateOnly Expiry)? ById(CmpProfile? profile, string itemId) =>
        Defs.FirstOrDefault(x => x.Id == itemId) is { } d ? Card(profile, d.Kind) : null;

    /// <summary>The licence cards on one page of FY <paramref name="fy"/>.</summary>
    public static IReadOnlyList<SccCard> Cards(CmpProfile? profile, string month, int fy) =>
        Defs.Select(d => Card(profile, d.Kind)).Where(c => c is { } x && x.Fy == fy && x.Month == month).Select(c => c!.Value.Card).ToList();
}
