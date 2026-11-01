using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Hcc.Domain.ThirdParty.Etp;

namespace Hcc.Domain.Accounting;

/// <summary>A Tally group created by the masters file: an Owner sub-group of the chart under its Tally parent (Tally's 28 primaries exist in every company).</summary>
public sealed record TallyGroupMaster(string Name, string Parent);

/// <summary>The name (and GSTIN, companies / vendors) of a party sub-ledger <c>&lt;control&gt;/&lt;party&gt;</c> as the masters hold it.</summary>
public sealed record TallyPartyInfo(string Name, string? Gstin = null);

/// <summary>
/// One Tally ledger of the masters file. <paramref name="OpeningNet"/> = opening balance at the FY start in paise, Dr − Cr (Dr positive);
/// the file writes it with Tally's sign (Cr positive, Dr negative). <paramref name="BillWise"/> = party ledger under Sundry Debtors / Creditors
/// (bill-wise on, opening kept as one bill). <paramref name="Nature"/> = the normal side of its group ("Dr" / "Cr").
/// </summary>
public sealed record TallyLedgerMaster(string Id, string Name, string Group, bool BillWise, long OpeningNet, string? Gstin, string Nature, bool Party);

/// <summary>
/// ACC-3 acc3-tally (contract §4): the Tally ledger masters of one property — Owner sub-groups, every ledger of the effective chart and every
/// party sub-ledger (<c>&lt;control&gt;/&lt;party&gt;</c>, Tally name = the party's display name, <c>(2)</c>, <c>(3)</c> … after a clash) — with the
/// opening balances at the FY start. Profit &amp; loss ledgers carry no opening in Tally: their net opening goes to <see cref="ProfitCarryNet"/>.
/// Pure; the prototype mirrors it byte for byte (<c>accounting-tally.ts</c>).
/// </summary>
public sealed class TallyBook
{
    private readonly Dictionary<string, TallyLedgerMaster> byId;

    private TallyBook(IReadOnlyList<TallyGroupMaster> groups, IReadOnlyList<TallyLedgerMaster> ledgers, long profitCarry)
    {
        Groups = groups;
        Ledgers = ledgers;
        ProfitCarryNet = profitCarry;
        byId = ledgers.ToDictionary(l => l.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<TallyGroupMaster> Groups { get; }

    public IReadOnlyList<TallyLedgerMaster> Ledgers { get; }

    /// <summary>Σ opening (Dr − Cr, paise) of the profit &amp; loss ledgers: earlier years' result, written to Tally's "Profit &amp; Loss A/c" opening.</summary>
    public long ProfitCarryNet { get; }

    public TallyLedgerMaster? Find(string ledgerId) => byId.TryGetValue(ledgerId, out var l) ? l : null;

    /// <summary>The Tally ledger name of a ledger id (a party ledger id → the party's name).</summary>
    public string NameOf(string ledgerId) => Find(ledgerId)?.Name ?? ledgerId;

    private static int Depth(EffectiveChart chart, LedgerGroup g)
    {
        var d = 0;
        for (var p = g; p.Parent is not null && d < 32; d++)
        {
            if (chart.GroupOf(p.Parent) is not { } next) break;
            p = next;
        }

        return d;
    }

    /// <summary>
    /// Builds the masters. <paramref name="opening"/> = Dr − Cr per ledger id before the FY start (the approved opening entry included);
    /// <paramref name="parties"/> = party ledger id → name / GSTIN; <paramref name="seen"/> = ledger ids the vouchers post to (a party that has
    /// no master row any more still gets a ledger, named by its party id).
    /// </summary>
    public static TallyBook Build(EffectiveChart chart, IReadOnlyDictionary<string, long> opening, IReadOnlyDictionary<string, TallyPartyInfo> parties, IEnumerable<string> seen)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(seen);
        var groups = chart.Groups.Where(g => g.Custom)
            .OrderBy(g => Depth(chart, g)).ThenBy(g => g.Id, StringComparer.Ordinal)
            .Select(g => new TallyGroupMaster(VoucherExport.XmlClean(g.Name), g.Parent is null ? "" : VoucherExport.XmlClean(chart.GroupOf(g.Parent)?.Name ?? g.Parent))).ToList();

        var ids = new HashSet<string>(chart.Ledgers.Select(l => l.Code), StringComparer.Ordinal);
        foreach (var id in parties.Keys.Concat(opening.Keys).Concat(seen))
            if (id.Length > 0) ids.Add(id);
        var ordered = ids.OrderBy(PartyLedgers.ControlOf, StringComparer.Ordinal).ThenBy(i => PartyLedgers.IsParty(i) ? 1 : 0)
            .ThenBy(i => PartyLedgers.PartyOf(i) ?? "", StringComparer.Ordinal).ToList();

        // Names: chart ledgers first, then parties; Tally names are case-insensitive and unique across all ledgers.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Unique(string name)
        {
            var n = name;
            for (var k = 2; !used.Add(n); k++) n = name + " (" + k.ToString(CultureInfo.InvariantCulture) + ")";
            return n;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ordered.Where(i => !PartyLedgers.IsParty(i)))
            names[id] = Unique(VoucherExport.XmlClean(chart.Find(id) is { } l ? l.EffectiveTallyName : id));
        foreach (var id in ordered.Where(PartyLedgers.IsParty))
            names[id] = Unique(VoucherExport.XmlClean(parties.TryGetValue(id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name : PartyLedgers.PartyOf(id)!));

        var carry = 0L;
        var ledgers = new List<TallyLedgerMaster>();
        foreach (var id in ordered)
        {
            var control = chart.Find(PartyLedgers.ControlOf(id));
            var groupId = control?.GroupId;
            var root = chart.RootOf(groupId);
            var group = chart.GroupOf(groupId)?.Name ?? "Suspense A/c";
            var nature = root?.Nature ?? "Dr";
            var party = PartyLedgers.IsParty(id);
            var net = opening.TryGetValue(id, out var o) ? o : 0L;
            if (root is { Side: "PL" })
            {
                carry += net;
                net = 0;
            }

            var billWise = party && groupId is not null && (chart.IsUnder(groupId, "debtors") || chart.IsUnder(groupId, "creditors"));
            var gstin = party && parties.TryGetValue(id, out var pi) && !string.IsNullOrWhiteSpace(pi.Gstin) ? pi.Gstin!.Trim() : null;
            ledgers.Add(new TallyLedgerMaster(id, names[id], VoucherExport.XmlClean(group), billWise, net, gstin, nature, party));
        }

        return new TallyBook(groups, ledgers, carry);
    }
}

public static partial class VoucherExport
{
    /// <summary>Bill name of an opening balance kept as a bill (masters) that later receipts / payments are allocated against.</summary>
    public const string TallyOpeningBill = "Opening balance";

    public const string TallyMastersNote = "MASTERS · import this file into the Tally company first (Import › Masters), then the vouchers files. Tally's own 28 groups are not created;"
        + " ledger opening balances use Tally's sign (credit positive, debit negative) as at the start of the financial year; party ledgers under Sundry Debtors / Creditors are bill-wise,"
        + " their opening kept as one bill named \"Opening balance\". The opening journal of HCC is not in the vouchers files (it would count twice).";

    [GeneratedRegex(@"[\u0000-\u001F￾￿]")]
    private static partial Regex XmlBad();

    /// <summary>Text for an element, attribute or comment of a Tally file: control characters (and so line breaks and tabs) become a space.</summary>
    public static string XmlClean(string? t) => XmlBad().Replace(t ?? "", " ");

    /// <summary>Body text of a comment line: "--" cannot close the comment early.</summary>
    private static string CommentText(string text) => " " + XmlClean(text).Replace("--", "- -", StringComparison.Ordinal).Trim() + " ";

    /// <summary>The text inside a comment line built by <see cref="TallyNote"/> / the fixed comment constants (<c>&lt;!-- x --&gt;</c> → <c>x</c>).</summary>
    private static string Inner(string comment) => comment[4..^3].Trim();

    private static void El(XmlWriter w, string name, string text)
    {
        w.WriteStartElement(name);
        w.WriteString(XmlClean(text));
        w.WriteFullEndElement();
    }

    private static string Render(IEnumerable<string> comments, Action<XmlWriter> envelope)
    {
        var settings = EtpTallyXml.Settings();
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartDocument();
            foreach (var c in comments) w.WriteComment(CommentText(c));
            envelope(w);
            w.WriteEndDocument();
        }

        return settings.Encoding.GetString(ms.ToArray()) + "\r\n";
    }

    private static void Envelope(XmlWriter w, string report, string? company, Action<XmlWriter> messages)
    {
        w.WriteStartElement("ENVELOPE");
        w.WriteStartElement("HEADER");
        El(w, "TALLYREQUEST", "Import Data");
        w.WriteEndElement();
        w.WriteStartElement("BODY");
        w.WriteStartElement("IMPORTDATA");
        w.WriteStartElement("REQUESTDESC");
        El(w, "REPORTNAME", report);
        w.WriteStartElement("STATICVARIABLES");
        if (!string.IsNullOrWhiteSpace(company)) El(w, "SVCURRENTCOMPANY", company);
        w.WriteFullEndElement();
        w.WriteEndElement();
        w.WriteStartElement("REQUESTDATA");
        messages(w);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Message(XmlWriter w, Action<XmlWriter> body)
    {
        w.WriteStartElement("TALLYMESSAGE");
        w.WriteAttributeString("xmlns", "UDF", null, "TallyUDF");
        body(w);
        w.WriteEndElement();
    }

    private static void NameList(XmlWriter w, string name)
    {
        w.WriteStartElement("NAME.LIST");
        El(w, "NAME", name);
        w.WriteEndElement();
    }

    private static string Td(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The masters file of a financial year (contract §4; "All Masters" Import Data envelope): Owner sub-groups, every ledger of the chart and every party
    /// sub-ledger with its opening balance at <paramref name="fyStart"/> (Tally sign), bill-wise on the party ledgers, GSTIN on companies / vendors.
    /// <paramref name="notes"/> (provisional opening, repeated export) follow the fixed comments. Well-formed by <see cref="XmlWriter"/>; CRLF; UTF-8 without BOM.
    /// </summary>
    public static string TallyMastersXml(TallyBook book, string? company, DateOnly fyStart, IReadOnlyList<string>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        var comments = new List<string> { Inner(TallyComment), TallyMastersNote };
        comments.AddRange(notes ?? []);
        return Render(comments, w => Envelope(w, "All Masters", company, m =>
        {
            foreach (var g in book.Groups)
                Message(m, x =>
                {
                    x.WriteStartElement("GROUP");
                    x.WriteAttributeString("NAME", XmlClean(g.Name));
                    x.WriteAttributeString("ACTION", "Create");
                    NameList(x, g.Name);
                    El(x, "PARENT", g.Parent);
                    El(x, "ISSUBLEDGER", "No");
                    x.WriteEndElement();
                });
            foreach (var l in book.Ledgers)
                Message(m, x =>
                {
                    x.WriteStartElement("LEDGER");
                    x.WriteAttributeString("NAME", XmlClean(l.Name));
                    x.WriteAttributeString("ACTION", "Create");
                    NameList(x, l.Name);
                    El(x, "PARENT", l.Group);
                    El(x, "ISBILLWISEON", l.BillWise ? "Yes" : "No");
                    if (l.OpeningNet != 0) El(x, "OPENINGBALANCE", EtpTallyXml.Amount(l.OpeningNet));
                    if (l.Gstin is not null)
                    {
                        El(x, "GSTREGISTRATIONTYPE", "Regular");
                        El(x, "PARTYGSTIN", l.Gstin);
                    }

                    if (l.BillWise && l.OpeningNet != 0)
                    {
                        x.WriteStartElement("BILLALLOCATIONS.LIST");
                        El(x, "NAME", TallyOpeningBill);
                        El(x, "BILLDATE", Td(fyStart.AddDays(-1)));
                        El(x, "OPENINGBALANCE", EtpTallyXml.Amount(l.OpeningNet));
                        x.WriteEndElement();
                    }

                    x.WriteEndElement();
                });
            if (book.ProfitCarryNet != 0)
                Message(m, x =>
                {
                    x.WriteStartElement("LEDGER");
                    x.WriteAttributeString("NAME", "Profit & Loss A/c");
                    x.WriteAttributeString("ACTION", "Alter");
                    NameList(x, "Profit & Loss A/c");
                    El(x, "OPENINGBALANCE", EtpTallyXml.Amount(book.ProfitCarryNet));
                    x.WriteEndElement();
                });
        }));
    }

    // ── Voucher type mapping (contract §4) ──

    /// <summary>
    /// Tally voucher type of an HCC voucher: receipt kinds → Receipt, payments / vendor payments / remittances → Payment, cash ↔ bank → Contra
    /// (a Contra voucher, or a Journal whose every entry is a cash / bank ledger), invoices → Sales, purchases → Purchase, credit notes → Credit Note
    /// (sub <c>credit-note</c>), purchase returns → Debit Note (a Purchase with sub <c>return</c>), everything else → Journal.
    /// </summary>
    public static string TallyVoucherType(Voucher v, EffectiveChart? chart = null)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (string.Equals(v.Sub, "credit-note", StringComparison.Ordinal)) return "Credit Note";
        var c = chart ?? ChartOfAccounts.Builtin;
        switch (v.Type)
        {
            case VoucherType.Receipt: return "Receipt";
            case VoucherType.Payment: return "Payment";
            case VoucherType.Contra: return "Contra";
            case VoucherType.Sales: return "Sales";
            case VoucherType.Purchase: return string.Equals(v.Sub, "return", StringComparison.Ordinal) ? "Debit Note" : "Purchase";
            default:
                return v.Entries.Count >= 2 && v.Entries.All(e => c.Find(PartyLedgers.ControlOf(e.Account))?.GroupId is { } g && (c.IsUnder(g, "cash") || c.IsUnder(g, "bank")))
                    ? "Contra" : "Journal";
        }
    }

    // ── Bill allocations (contract §4: New Ref / Agst Ref / On Account) ──

    /// <summary>One bill allocation on a party ledger entry; <paramref name="Net"/> has the entry's side (Dr positive).</summary>
    public sealed record TallyBillAllocation(string Name, string Type, long Net);

    /// <summary>
    /// Bill-wise state of the party ledgers under Sundry Debtors / Creditors: the opening balance is one open bill ("Opening balance");
    /// an entry on the ledger's natural side is a new bill named by the voucher number; an entry on the other side is allocated oldest bill
    /// first (Agst Ref) and what no open bill takes stays On Account. Vouchers must be fed in date, number order.
    /// </summary>
    public sealed class TallyBills
    {
        private readonly TallyBook book;
        private readonly Dictionary<string, List<(string Name, long Left, string Key)>> open = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>? explicitAllocations;

        /// <summary>Bill-wise allocations. <paramref name="explicitAllocations"/> (ACC-3 integration, acc3-ageing's ACC-2 vendor allocations) = per payment
        /// voucher source key, the bills it pays (ref = the bill voucher's source key, or <c>opening</c>); they apply first, the rest oldest-first.</summary>
        public TallyBills(TallyBook book, IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>? explicitAllocations = null)
        {
            this.book = book ?? throw new ArgumentNullException(nameof(book));
            this.explicitAllocations = explicitAllocations;
            foreach (var l in book.Ledgers.Where(l => l.BillWise && l.OpeningNet != 0))
            {
                var natural = l.Nature == "Dr" ? 1 : -1;
                if (l.OpeningNet * natural > 0) open[l.Id] = [(TallyOpeningBill, Math.Abs(l.OpeningNet), "opening")];
            }
        }

        /// <summary>Allocations of every entry of the voucher (same index as <see cref="Voucher.Entries"/>; empty list = not bill-wise).</summary>
        public IReadOnlyList<IReadOnlyList<TallyBillAllocation>> Allocate(Voucher v)
        {
            ArgumentNullException.ThrowIfNull(v);
            var result = new List<IReadOnlyList<TallyBillAllocation>>();
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in v.Entries)
            {
                var net = e.DebitPaise - e.CreditPaise;
                if (net == 0 || book.Find(e.Account) is not { BillWise: true } l)
                {
                    result.Add([]);
                    continue;
                }

                var side = net > 0 ? 1 : -1;
                var natural = l.Nature == "Dr" ? 1 : -1;
                var list = new List<TallyBillAllocation>();
                if (side == natural)
                {
                    names[l.Id] = names.TryGetValue(l.Id, out var k) ? k + 1 : 1;
                    var name = names[l.Id] == 1 ? v.Number : v.Number + "/" + names[l.Id].ToString(CultureInfo.InvariantCulture);
                    list.Add(new TallyBillAllocation(name, "New Ref", net));
                    if (!open.TryGetValue(l.Id, out var bills)) open[l.Id] = bills = [];
                    bills.Add((name, Math.Abs(net), v.SourceKey));
                }
                else
                {
                    var need = Math.Abs(net);
                    if (open.TryGetValue(l.Id, out var bills))
                    {
                        if (explicitAllocations is not null && explicitAllocations.TryGetValue(v.SourceKey, out var targets))
                            foreach (var (reference, paise) in targets)
                            {
                                var i = bills.FindIndex(b => b.Key == reference && b.Left > 0);
                                if (i < 0 || need <= 0) continue;
                                var take = Math.Min(Math.Min(need, paise), bills[i].Left);
                                if (take <= 0) continue;
                                list.Add(new TallyBillAllocation(bills[i].Name, "Agst Ref", side * take));
                                bills[i] = (bills[i].Name, bills[i].Left - take, bills[i].Key);
                                need -= take;
                            }

                        for (var i = 0; i < bills.Count && need > 0; i++)
                        {
                            var take = Math.Min(need, bills[i].Left);
                            list.Add(new TallyBillAllocation(bills[i].Name, "Agst Ref", side * take));
                            bills[i] = (bills[i].Name, bills[i].Left - take, bills[i].Key);
                            need -= take;
                        }

                        bills.RemoveAll(b => b.Left == 0);
                    }

                    if (need > 0) list.Add(new TallyBillAllocation(v.Number, "On Account", side * need));
                }

                result.Add(list);
            }

            return result;
        }
    }

    /// <summary>
    /// The vouchers file of a range (<= 31 days, <see cref="TallyRangeError"/>). <paramref name="stream"/> = every voucher of the financial year up to the
    /// end of the range, in date, number order (the opening journal never: the masters carry the opening); every one feeds the bill allocations, only
    /// those whose source key is in <paramref name="emit"/> are written (the range, plus re-export dates before it). REMOTEID / GUID =
    /// <see cref="RemoteId"/>; no VCHKEY (Tally's own key is never invented here).
    /// </summary>
    public static string TallyVouchersXml(IReadOnlyList<Voucher> stream, IReadOnlySet<string> emit, TallyBook book, EffectiveChart? chart, string? company, string identity,
        IReadOnlyList<string>? notes = null, IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>? explicitAllocations = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(book);
        var written = stream.Where(v => emit.Contains(v.SourceKey)).ToList();
        var ids = written.Select(v => RemoteId(identity, v.SourceKey)).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("Two vouchers in one Tally export share a source key; every REMOTEID must be unique.");
        var bills = new TallyBills(book, explicitAllocations);
        var comments = new List<string> { Inner(TallyComment), Inner(TallyIdNote) };
        comments.AddRange(notes ?? []);
        return Render(comments, w => Envelope(w, "Vouchers", company, m =>
        {
            var n = 0;
            foreach (var v in stream)
            {
                var alloc = bills.Allocate(v);
                if (!emit.Contains(v.SourceKey)) continue;
                var id = ids[n++];
                var type = TallyVoucherType(v, chart);
                Message(m, x =>
                {
                    x.WriteStartElement("VOUCHER");
                    x.WriteAttributeString("REMOTEID", XmlClean(id));
                    x.WriteAttributeString("VCHTYPE", type);
                    x.WriteAttributeString("ACTION", "Create");
                    x.WriteAttributeString("OBJVIEW", "Accounting Voucher View");
                    El(x, "DATE", Td(v.Date));
                    El(x, "GUID", id);
                    El(x, "VOUCHERTYPENAME", type);
                    El(x, "VOUCHERNUMBER", v.Number);
                    El(x, "NARRATION", v.Narration);
                    El(x, "PERSISTEDVIEW", "Accounting Voucher View");
                    El(x, "EFFECTIVEDATE", Td(v.Date));
                    for (var k = 0; k < v.Entries.Count; k++)
                    {
                        var e = v.Entries[k];
                        var net = e.DebitPaise - e.CreditPaise;
                        x.WriteStartElement("ALLLEDGERENTRIES.LIST");
                        El(x, "LEDGERNAME", book.Find(e.Account)?.Name ?? (chart ?? ChartOfAccounts.Builtin).TallyNameOf(PartyLedgers.ControlOf(e.Account)));
                        El(x, "ISDEEMEDPOSITIVE", net > 0 ? "Yes" : "No");
                        El(x, "AMOUNT", EtpTallyXml.Amount(net));
                        foreach (var a in alloc[k])
                        {
                            x.WriteStartElement("BILLALLOCATIONS.LIST");
                            El(x, "NAME", a.Name);
                            El(x, "BILLTYPE", a.Type);
                            El(x, "AMOUNT", EtpTallyXml.Amount(a.Net));
                            x.WriteEndElement();
                        }

                        x.WriteEndElement();
                    }

                    x.WriteEndElement();
                });
            }
        }));
    }
}
