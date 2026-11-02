using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Masters;
using Hcc.Domain.Invoices;
using Hcc.Domain.Masters;
using Hcc.Domain.Tax;

namespace Hcc.Domain.FrontDeskMasters;

/// <summary>One default row of a list (stored on the first change of the list).</summary>
public sealed record FrontDeskDefault(string Code, string Name, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// MA-5 front-desk list rules (plan Part 2 §8 M9, M11, M12, M14; prototype domain/streams/frontdesk-master.ts). Field values are
/// normalised to string, int, bool, DateOnly or IReadOnlyList&lt;string&gt; and keyed by the wire names (segment, commissionBps, document,
/// startNo, activeFrom, sources …) so the generic commands can diff them for mst.master_history.
/// </summary>
public static partial class FrontDeskMastersRules
{
    public static readonly IReadOnlyList<string> Segments = ["DIRECT", "OTA", "CORPORATE", "AGENT", "GROUP", "OTHER"];
    public static readonly IReadOnlyList<string> ChannelSegments = ["OTA", "CORPORATE", "AGENT"];
    public static readonly IReadOnlyList<string> Documents = ["INV", "BOS", "CRN", "RCP", "KOT", "REG", "RST"];
    /// <summary>GST documents (yearly reset, PREFIX/FY/digits within 16 characters). MA-11: RST = Wave B's restaurant tax invoices (DEC-072),
    /// read by the invoices module through <c>INumberSeriesSource</c> exactly like the tax-invoice series.</summary>
    public static readonly IReadOnlyList<string> GstDocuments = ["INV", "BOS", "CRN", "RST"];
    /// <summary>MA-11: series defined but RESERVED — no command numbers these documents yet (receipts print the receipt id, KOTs a per-outlet
    /// counter, registration cards the booking). Kept so the Owner can set them up before they are switched on.</summary>
    public static readonly IReadOnlyList<string> ReservedDocuments = ["RCP", "KOT", "REG"];
    public const int MaxCommissionBps = 5000;
    public const int MaxHours = 720;
    public const string CaNote = "GST documents: one consecutive series, unique per financial year, no gaps, at most 16 characters (CGST Rules r.46(b)) — illustrative, CA to confirm.";

    [GeneratedRegex("^[A-Z0-9-]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    public static string Label(string master) => master switch
    {
        FrontDeskMasterIds.BookingSources => "Booking sources",
        FrontDeskMasterIds.IdTypes => "Guest ID types",
        FrontDeskMasterIds.NumberSeries => "Numbering series",
        FrontDeskMasterIds.Policies => "Booking policies",
        FrontDeskMasterIds.Purposes => "Purposes of visit",
        FrontDeskMasterIds.VehicleTypes => "Vehicle types",
        _ => throw new ArgumentOutOfRangeException(nameof(master), master, null),
    };

    public static string Singular(string master) => master switch
    {
        FrontDeskMasterIds.BookingSources => "booking source",
        FrontDeskMasterIds.IdTypes => "ID type",
        FrontDeskMasterIds.NumberSeries => "numbering series",
        FrontDeskMasterIds.Purposes => "purpose of visit",
        FrontDeskMasterIds.VehicleTypes => "vehicle type",
        _ => "policy",
    };

    /// <summary>Money lists (numbering, policies): every change is Owner-only (PERM-MST-002, DEC-058).</summary>
    public static bool IsMoneyList(string master) => master is FrontDeskMasterIds.NumberSeries or FrontDeskMasterIds.Policies;

    public static string DocumentLabel(string doc) => doc switch
    {
        "INV" => "tax invoice",
        "BOS" => "bill of supply",
        "CRN" => "credit note",
        "RCP" => "receipt",
        "KOT" => "KOT",
        "REG" => "registration card",
        "RST" => "restaurant tax invoice",
        _ => doc,
    };

    public static bool IsGst(string doc) => GstDocuments.Contains(doc);

    private static Exception Bad(string message, string? field = null) => MastersRules.Fail(MasterReasons.Invalid, message, field);

    // ── M9 booking sources ──
    public static Dictionary<string, object?> BookingSource(string? segment, string? channel, int? commissionBps)
    {
        var seg = (segment ?? string.Empty).Trim().ToUpperInvariant();
        if (!Segments.Contains(seg)) throw Bad("Choose a market segment.", "segment");
        var ch = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        if (ch is { Length: > 60 }) throw Bad("OTA channel / company: at most 60 characters.", "channel");
        if (ch is not null && !ChannelSegments.Contains(seg)) throw Bad("Only OTA, corporate and agent rows name a channel or company.", "channel");
        var c = commissionBps ?? 0;
        if (c < 0 || c > MaxCommissionBps) throw Bad("Commission: a percentage from 0 to 50.", "commissionBps");
        return new() { ["segment"] = seg, ["channel"] = ch, ["commissionBps"] = c };
    }

    /// <summary>LF A3 / A4: a list of code + name only.</summary>
    public static Dictionary<string, object?> Plain() => new();

    // ── M11 ID types ──
    public static Dictionary<string, object?> IdType(bool accepted, bool needsPhoto, bool foreignerOnly) =>
        new() { ["accepted"] = accepted, ["needsPhoto"] = needsPhoto, ["foreignerOnly"] = foreignerOnly };

    // ── M12 numbering series ──
    public static Dictionary<string, object?> NumberSeries(string? document, string? prefix, int startNo, int padding, string? reset, string? activeFrom)
    {
        var doc = (document ?? string.Empty).Trim().ToUpperInvariant();
        if (!Documents.Contains(doc)) throw Bad("Choose the document.", "document");
        var p = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (!PrefixPattern().IsMatch(p)) throw Bad("Prefix: 1–8 capital letters, digits or “-”.", "prefix");
        if (startNo < 1 || startNo > 9_999_999) throw Bad("First number: a whole number from 1 to 9,999,999.", "startNo");
        if (padding < 1 || padding > 10) throw Bad("Digits: a whole number from 1 to 10.", "padding");
        var r = (reset ?? string.Empty).Trim().ToUpperInvariant();
        if (r is not ("FY" or "NEVER")) throw Bad("Choose when the numbers restart (FY or NEVER).", "reset");
        if (!DateOnly.TryParseExact(activeFrom ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            throw Bad("Active from: a date (yyyy-MM-dd).", "activeFrom");
        if (IsGst(doc))
        {
            if (r != "FY") throw Bad($"{Cap(DocumentLabel(doc))}: GST documents restart every financial year (the number carries the FY) — CA to confirm.", "reset");
            if (p.Length > InvoiceNumbering.MaxPrefix) throw Bad($"Prefix: at most {InvoiceNumbering.MaxPrefix} characters for GST documents.", "prefix");
            var len = p.Length + 6 + padding;
            if (len > InvoiceNumbering.MaxLength)
                throw Bad($"The number would be {len} characters ({p}/2627/{new string('0', padding)}); GST allows at most {InvoiceNumbering.MaxLength}. Shorten the prefix or the digits.", "padding");
        }

        return new() { ["document"] = doc, ["prefix"] = p, ["startNo"] = startNo, ["padding"] = padding, ["reset"] = r, ["activeFrom"] = from };
    }

    /// <summary>Fields that may not change once a series has issued documents (no gaps, no renumbering).</summary>
    public static readonly IReadOnlyList<string> NumberingFields = ["document", "prefix", "startNo", "padding", "reset", "activeFrom"];

    /// <summary>A series with documents keeps its numbering fields.</summary>
    public static void EnsureSeriesUnchanged(string code, int issued, IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (issued <= 0) return;
        var changed = NumberingFields.FirstOrDefault(f => !Equals(Show(f, before.GetValueOrDefault(f)), Show(f, after.GetValueOrDefault(f))));
        if (changed is not null)
            throw MastersRules.Fail(MasterReasons.Invalid,
                $"{code} has issued {issued} number{(issued == 1 ? "" : "s")} — document, prefix, start, digits, reset and active-from cannot change (sequential, no gaps). Add a new series from a date with no documents instead.", changed);
    }

    /// <summary>A new start date: today or later, after the last document of the kind, and (yearly reset) in a FY without documents of it.</summary>
    public static void EnsureStartFree(string doc, string reset, DateOnly activeFrom, DateOnly businessDate, DateOnly? lastDocument, IReadOnlyCollection<string> fyCodesWithDocuments)
    {
        ArgumentNullException.ThrowIfNull(fyCodesWithDocuments);
        if (activeFrom < businessDate) throw Bad($"Active from: {Iso(businessDate)} (today's business date) or later.", "activeFrom");
        if (lastDocument is { } last && activeFrom <= last)
            throw Bad($"Active from: after {Iso(last)}, the last {DocumentLabel(doc)} date — a series starts on a date with no documents.", "activeFrom");
        if (reset == "FY" && fyCodesWithDocuments.Contains(InvoiceNumbering.FyCode(activeFrom)))
            throw Bad($"FY {FinancialYear.Of(activeFrom).Label} already has {DocumentLabel(doc)} numbers — the new series can start from {Iso(FinancialYear.Of(activeFrom).To.AddDays(1))} (next financial year).", "activeFrom");
    }

    /// <summary>Formatted number: PREFIX/2627/0001 (yearly reset; the invoices module's form) or PREFIX/00001 (never reset).</summary>
    public static string Format(string prefix, string reset, int padding, DateOnly date, int seq) =>
        reset == "NEVER" ? prefix + "/" + seq.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0') : InvoiceNumbering.Format(prefix, InvoiceNumbering.FyCode(date), seq, padding);

    // ── M14 policies ──
    [GeneratedRegex("^[A-Z0-9-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceCode();

    public static Dictionary<string, object?> Policy(int freeCancelHours, int cancelChargeBps, int noShowChargeBps, int depositBps, IReadOnlyList<string>? sources)
    {
        if (freeCancelHours < 0 || freeCancelHours > MaxHours) throw Bad("Free cancellation: 0 to 720 hours before arrival.", "freeCancelHours");
        foreach (var (v, f, l) in new[] { (cancelChargeBps, "cancelChargeBps", "Cancellation charge"), (noShowChargeBps, "noShowChargeBps", "No-show charge"), (depositBps, "depositBps", "Advance") })
            if (v < 0 || v > 10000) throw Bad($"{l}: a percentage from 0 to 100.", f);
        var list = (sources ?? []).Select(x => (x ?? string.Empty).Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (list.Any(x => !SourceCode().IsMatch(x))) throw Bad("Sources: booking-source codes (capital letters, digits or “-”).", "sources");
        if (list.Count > 40) throw Bad("Sources: at most 40.", "sources");
        return new() { ["freeCancelHours"] = freeCancelHours, ["cancelChargeBps"] = cancelChargeBps, ["noShowChargeBps"] = noShowChargeBps, ["depositBps"] = depositBps, ["sources"] = list };
    }

    /// <summary>One default (no sources) among the active policies; a source follows at most one active policy.</summary>
    public static void EnsurePolicyFits(string code, IReadOnlyList<string> sources, IEnumerable<(string Code, string Name, bool Active, IReadOnlyList<string> Sources)> others)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var active = others.Where(o => o.Active && o.Code != code).ToList();
        if (sources.Count == 0 && active.FirstOrDefault(o => o.Sources.Count == 0) is { Name: { } n })
            throw Bad($"{n} is already the default policy (names no source) — choose the sources this one applies to.", "sources");
        foreach (var s in sources)
            if (active.FirstOrDefault(o => o.Sources.Contains(s)) is { Name: { } o })
                throw Bad($"{s} already follows {o}.", "sources");
    }

    // ── Defaults (the prototype's fixture values) ──
    public static IReadOnlyList<FrontDeskDefault> Defaults(string master, string? invoicePrefix = null) => master switch
    {
        FrontDeskMasterIds.BookingSources =>
        [
            Src("WALKIN", "Walk-in", "DIRECT"), Src("PHONE", "Phone", "DIRECT"), Src("WHATSAPP", "WhatsApp", "DIRECT"), Src("DIRECT", "Direct", "DIRECT"),
            Src("WEBSITE", "Website", "DIRECT"), Src("OTA", "OTA", "OTA"), Src("AGENT", "Agent", "AGENT"), Src("CORPORATE", "Corporate", "CORPORATE"),
        ],
        FrontDeskMasterIds.IdTypes =>
        [
            Id("AADHAAR", "Aadhaar"), Id("PASSPORT", "Passport"), Id("DL", "Driving licence"), Id("VOTER", "Voter ID"), Id("PAN", "PAN"), Id("OTHER", "Other"),
            new("VISA", "Passport + visa (foreign national)", IdType(true, true, true)),
        ],
        FrontDeskMasterIds.NumberSeries => SeriesDefaults(invoicePrefix),
        FrontDeskMasterIds.Policies => [new("DEFAULT", "Default", Policy(0, 0, 0, 0, []))],
        FrontDeskMasterIds.Purposes =>
        [
            new("BUSINESS", "Business", Plain()), new("TOURISM", "Tourism", Plain()), new("FAMILY", "Family function", Plain()), new("MEDICAL", "Medical", Plain()),
            new("EDUCATION", "Education", Plain()), new("OFFICIAL", "Official", Plain()), new("TRANSIT", "Transit", Plain()), new("OTHER", "Other", Plain()),
        ],
        FrontDeskMasterIds.VehicleTypes => [new("CAR", "Car", Plain()), new("BIKE", "Bike", Plain()), new("TAXI", "Taxi", Plain()), new("BUS", "Bus", Plain()), new("OTHER", "Other", Plain())],
        _ => throw new ArgumentOutOfRangeException(nameof(master), master, null),
    };

    /// <summary>Default series: the invoices module's numbers as they are today (tax invoice = the invoicePrefix setting or INV, full
    /// 16-character width; credit note CN; bill of supply BOS) + one series per other document.</summary>
    public static IReadOnlyList<FrontDeskDefault> SeriesDefaults(string? invoicePrefix)
    {
        var inv = invoicePrefix is { Length: > 0 } p && PrefixPattern().IsMatch(p.Trim().ToUpperInvariant()) && p.Trim().Length <= InvoiceNumbering.MaxPrefix ? p.Trim().ToUpperInvariant() : "INV";
        var from = new DateOnly(2000, 4, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FrontDeskDefault S(string code, string name, string doc, string prefix, int pad, string reset) =>
            new(code, name, NumberSeries(doc, prefix, 1, pad, reset, from));
        return
        [
            S("INV", "Tax invoice", "INV", inv, InvoiceNumbering.MaxLength - 6 - inv.Length, "FY"),
            S("BOS", "Bill of supply", "BOS", "BOS", 7, "FY"),
            S("CRN", "Credit note", "CRN", "CN", 8, "FY"),
            S("RCP", "Receipt", "RCP", "RCP", 5, "NEVER"),
            S("KOT", "Kitchen order ticket", "KOT", "KOT", 4, "NEVER"),
            S("REG", "Registration card", "REG", "REG", 5, "NEVER"),
            S("RST", "Restaurant tax invoice", "RST", "RST", InvoiceNumbering.MaxLength - 6 - 3, "FY"), // MA-11: RST/2627/0000001, as before
        ];
    }

    /// <summary>
    /// MA-11: the row a booking's source text belongs to (prototype fdmSourceOf without the company match): "OTA · &lt;channel&gt;" → that OTA
    /// channel row; "OTA · manual" → the generic OTA row; else the row named like the source (ignoring case) or with that code. Retired rows
    /// resolve (the caller refuses them for new bookings); null = not in the list.
    /// </summary>
    public static BookingSourceRow? SourceRowOf(IReadOnlyList<BookingSourceRow> rows, string? source)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var src = (source ?? string.Empty).Trim();
        if (src.Length == 0) return null;
        var lower = src.ToLowerInvariant();
        foreach (var sep in new[] { "ota · ", "ota - " })
            if (lower.StartsWith(sep, StringComparison.Ordinal) && lower[sep.Length..] is { Length: > 0 } ch && ch != "manual"
                && rows.FirstOrDefault(r => r.Segment == "OTA" && string.Equals(r.Channel, src[sep.Length..], StringComparison.OrdinalIgnoreCase)) is { } byChannel)
                return byChannel;
        if (lower is "ota · manual" or "ota - manual") return rows.FirstOrDefault(r => r.Code == "OTA");
        return rows.FirstOrDefault(r => string.Equals(r.Name, src, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => string.Equals(r.Code, src.ToUpperInvariant(), StringComparison.Ordinal));
    }

    private static FrontDeskDefault Src(string code, string name, string segment) => new(code, name, BookingSource(segment, null, 0));

    private static FrontDeskDefault Id(string code, string name) => new(code, name, IdType(true, false, false));

    // ── Display (history values as people read them) ──
    public static string? Show(string field, object? value) => value switch
    {
        null => null,
        bool b => b ? "Yes" : "No",
        DateOnly d => Iso(d),
        IReadOnlyList<string> l => l.Count == 0 ? "—" : string.Join(", ", l),
        int i when field.EndsWith("Bps", StringComparison.Ordinal) => (i / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %",
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => s.Length == 0 ? null : s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
