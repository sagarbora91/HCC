using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Hcc.Accounting.Tests.Golden;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.ThirdParty.Etp;

namespace Hcc.Accounting.Tests.Tally;

/// <summary>
/// ACC-3 acc3-tally (contract §4): the Tally masters and vouchers files. The golden files are built by the prototype from the ACC-RULEBOOK scenario
/// (Golden/build-tally-golden.mjs → tally-april-2026.input.json / .masters.xml / .vouchers.xml, fictional); here the SERVER builders must reproduce
/// the same bytes from the same input. Also: well-formed, counts, Σ Dr = Σ Cr per voucher, REMOTEID stability, voucher types, bill allocations,
/// names, escaping, and the real engine vouchers of the rulebook month.
/// </summary>
public sealed class TallyExportTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name);

    private static JsonElement Input => JsonDocument.Parse(File.ReadAllText(GoldenPath("tally-april-2026.input.json"))).RootElement;

    private static DateOnly Date(string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture);

    private static Voucher VoucherOf(JsonElement v) => new(v.GetProperty("no").GetString()!, Date(v.GetProperty("date").GetString()!),
        Enum.Parse<VoucherType>(v.GetProperty("type").GetString()!), v.GetProperty("narration").GetString()!, v.GetProperty("sourceId").GetString()!, "", v.GetProperty("sub").GetString()!,
        [.. v.GetProperty("entries").EnumerateArray().Select(e => new VoucherEntry(e.GetProperty("account").GetString()!, e.GetProperty("debit").GetInt64(), e.GetProperty("credit").GetInt64()))], []);

    private static (TallyBook Book, List<Voucher> Stream, HashSet<string> Emit, string Company, string Identity, DateOnly FyStart) FromInput()
    {
        var i = Input;
        var opening = i.GetProperty("opening").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64());
        var parties = i.GetProperty("parties").EnumerateObject().ToDictionary(p => p.Name,
            p => new TallyPartyInfo(p.Value.GetProperty("name").GetString()!, p.Value.TryGetProperty("gstin", out var g) ? g.GetString() : null));
        var seen = i.GetProperty("seen").EnumerateArray().Select(x => x.GetString()!).ToList();
        var book = TallyBook.Build(ChartOfAccounts.Builtin, opening, parties, seen);
        return (book, [.. i.GetProperty("stream").EnumerateArray().Select(VoucherOf)], [.. i.GetProperty("emit").EnumerateArray().Select(x => x.GetString()!)],
            i.GetProperty("company").GetString()!, i.GetProperty("identity").GetString()!, Date(i.GetProperty("fyStart").GetString()!));
    }

    [Fact]
    public void Golden_masters_are_byte_identical_to_the_prototype_build()
    {
        var (book, _, _, company, _, fy) = FromInput();
        var xml = VoucherExport.TallyMastersXml(book, company, fy);
        Assert.Equal(File.ReadAllBytes(GoldenPath("tally-april-2026.masters.xml")), Utf8.GetBytes(xml));
    }

    [Fact]
    public void Golden_vouchers_are_byte_identical_to_the_prototype_build()
    {
        var (book, stream, emit, company, identity, _) = FromInput();
        var xml = VoucherExport.TallyVouchersXml(stream, emit, book, ChartOfAccounts.Builtin, company, identity);
        Assert.Equal(File.ReadAllBytes(GoldenPath("tally-april-2026.vouchers.xml")), Utf8.GetBytes(xml));
    }

    [Fact]
    public void The_input_chart_is_the_server_builtin_chart()
    {
        // the prototype built the golden from its own chart; the server's built-in chart must agree name for name and group for group
        var chart = Input.GetProperty("chart");
        var ledgers = chart.GetProperty("ledgers").EnumerateArray().Where(l => !l.GetProperty("code").GetString()!.Contains('/', StringComparison.Ordinal)).ToDictionary(l => l.GetProperty("code").GetString()!, l => l);
        Assert.Equal(ChartOfAccounts.All.Select(l => l.Code).Order(StringComparer.Ordinal), ledgers.Keys.Order(StringComparer.Ordinal));
        foreach (var l in ChartOfAccounts.All)
        {
            Assert.Equal(l.Name, ledgers[l.Code].GetProperty("name").GetString());
            Assert.Equal(l.GroupId, ledgers[l.Code].GetProperty("group").GetString());
        }

        Assert.Equal(ChartOfAccounts.Groups.Select(g => (g.Id, g.Name, g.Parent)), chart.GetProperty("groups").EnumerateArray()
            .Select(g => (g.GetProperty("id").GetString()!, g.GetProperty("name").GetString()!, g.TryGetProperty("parent", out var p) ? p.GetString() : null)));
    }

    [Fact]
    public void Both_files_are_well_formed_with_the_right_counts_and_every_voucher_balances()
    {
        var (book, stream, emit, company, identity, fy) = FromInput();
        var masters = XDocument.Parse(VoucherExport.TallyMastersXml(book, company, fy));
        var vouchers = XDocument.Parse(VoucherExport.TallyVouchersXml(stream, emit, book, ChartOfAccounts.Builtin, company, identity));
        Assert.Equal(book.Groups.Count + book.Ledgers.Count + (book.ProfitCarryNet != 0 ? 1 : 0), masters.Descendants("TALLYMESSAGE").Count());
        Assert.Equal(emit.Count, vouchers.Descendants("VOUCHER").Count());
        Assert.True(emit.Count > 0);
        foreach (var v in vouchers.Descendants("VOUCHER"))
        {
            var sum = v.Elements("ALLLEDGERENTRIES.LIST").Sum(e => decimal.Parse(e.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture));
            Assert.Equal(0m, sum);
            Assert.Equal(v.Attribute("REMOTEID")!.Value, v.Element("GUID")!.Value);
            foreach (var e in v.Elements("ALLLEDGERENTRIES.LIST"))
            {
                var amount = decimal.Parse(e.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture);
                Assert.Equal(amount < 0 ? "Yes" : "No", e.Element("ISDEEMEDPOSITIVE")!.Value);
                var bills = e.Elements("BILLALLOCATIONS.LIST").ToList();
                if (bills.Count > 0) Assert.Equal(amount, bills.Sum(b => decimal.Parse(b.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture)));
            }
        }

        // masters: Tally's sign, Σ opening of the ledgers = 0 (the opening journal balances)
        var open = masters.Descendants("LEDGER").Where(l => l.Element("OPENINGBALANCE") is not null && l.Attribute("ACTION")!.Value == "Create")
            .Sum(l => decimal.Parse(l.Element("OPENINGBALANCE")!.Value, CultureInfo.InvariantCulture));
        Assert.Equal(0m, open);
        Assert.DoesNotContain(masters.Descendants("GROUP"), _ => true); // no Owner sub-group in the rulebook month
    }

    [Fact]
    public void REMOTEID_is_stable_per_voucher_and_follows_the_identity()
    {
        var (book, stream, emit, company, _, _) = FromInput();
        string Ids(string identity) => string.Join("|", XDocument.Parse(VoucherExport.TallyVouchersXml(stream, emit, book, ChartOfAccounts.Builtin, company, identity))
            .Descendants("VOUCHER").Select(v => v.Attribute("REMOTEID")!.Value));
        Assert.Equal(Ids("ABC"), Ids("ABC"));
        var ids = Ids("ABC").Split('|');
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, x => Assert.StartsWith("HCC-ABC-", x, StringComparison.Ordinal));
        var first = stream.First(v => emit.Contains(v.SourceKey));
        Assert.Equal(VoucherExport.RemoteId("ABC", first.SourceKey), ids[0]);
        Assert.NotEqual(Ids("ABC"), Ids("DEF"));
        // a range that carries fewer vouchers keeps the others' ids (same voucher, same id in every export)
        var half = emit.Take(emit.Count / 2).ToHashSet();
        var halfIds = XDocument.Parse(VoucherExport.TallyVouchersXml(stream, half, book, ChartOfAccounts.Builtin, company, "ABC")).Descendants("VOUCHER").Select(v => v.Attribute("REMOTEID")!.Value).ToList();
        Assert.All(halfIds, x => Assert.Contains(x, ids));
    }

    [Fact]
    public void Two_vouchers_with_one_source_key_are_refused()
    {
        var (book, stream, _, company, identity, _) = FromInput();
        var dup = stream.Take(2).Select(v => v with { SourceKey = "same" }).ToList();
        Assert.Throws<InvalidOperationException>(() => VoucherExport.TallyVouchersXml(dup, new HashSet<string> { "same" }, book, ChartOfAccounts.Builtin, company, identity));
    }

    [Fact]
    public void Receipts_are_allocated_oldest_bill_first_and_the_rest_stays_on_account()
    {
        var chart = ChartOfAccounts.Builtin;
        var parties = new Dictionary<string, TallyPartyInfo> { ["1110/CMP-1"] = new("Alpha Co", "27AAAAA0000A1Z5") };
        var book = TallyBook.Build(chart, new Dictionary<string, long> { ["1110/CMP-1"] = 10_000 }, parties, []);
        Assert.True(book.Find("1110/CMP-1")!.BillWise);
        VoucherEntry D(string a, long p) => new(a, p, 0);
        VoucherEntry C(string a, long p) => new(a, 0, p);
        Voucher V(string no, int day, VoucherType t, params VoucherEntry[] e) => new(no, new DateOnly(2026, 4, day), t, "n", "k" + no, "x", "", e, []);
        var bills = new VoucherExport.TallyBills(book);
        var inv1 = bills.Allocate(V("S1", 1, VoucherType.Sales, D("1110/CMP-1", 5_000), C("4000", 5_000)));
        var inv2 = bills.Allocate(V("S2", 2, VoucherType.Sales, D("1110/CMP-1", 7_000), C("4000", 7_000)));
        Assert.Equal(("S1", "New Ref", 5_000L), (inv1[0][0].Name, inv1[0][0].Type, inv1[0][0].Net));
        Assert.Equal("S2", inv2[0][0].Name);
        // 10,000 opening + 5,000 + 7,000 = 22,000 open; a receipt of 25,000 clears the three bills and leaves 3,000 on account
        var rec = bills.Allocate(V("R1", 3, VoucherType.Receipt, D("1030", 25_000), C("1110/CMP-1", 25_000)))[1];
        Assert.Equal(["Opening balance", "S1", "S2", "R1"], rec.Select(a => a.Name));
        Assert.Equal(["Agst Ref", "Agst Ref", "Agst Ref", "On Account"], rec.Select(a => a.Type));
        Assert.Equal(-25_000, rec.Sum(a => a.Net));
        Assert.Empty(bills.Allocate(V("R1", 3, VoucherType.Receipt, D("1030", 1), C("4000", 1)))[0]);
        // a vendor-style credit ledger works the other way round
        var cbook = TallyBook.Build(chart, new Dictionary<string, long> { ["2200/V-1"] = -8_000 }, new Dictionary<string, TallyPartyInfo> { ["2200/V-1"] = new("Vendor") }, []);
        var cb = new VoucherExport.TallyBills(cbook);
        var pay = cb.Allocate(V("P1", 4, VoucherType.Payment, D("2200/V-1", 3_000), C("1030", 3_000)))[0];
        Assert.Equal(("Opening balance", "Agst Ref", 3_000L), (pay[0].Name, pay[0].Type, pay[0].Net));
        Assert.Equal("New Ref", cb.Allocate(V("PU1", 5, VoucherType.Purchase, D("5000", 100), C("2200/V-1", 100)))[1][0].Type);
    }

    [Theory]
    [InlineData(VoucherType.Receipt, "", "Receipt")]
    [InlineData(VoucherType.Payment, "", "Payment")]
    [InlineData(VoucherType.Sales, "", "Sales")]
    [InlineData(VoucherType.Purchase, "expense", "Purchase")]
    [InlineData(VoucherType.Purchase, "return", "Debit Note")]
    [InlineData(VoucherType.Contra, "", "Contra")]
    [InlineData(VoucherType.Sales, "credit-note", "Credit Note")]
    [InlineData(VoucherType.Journal, "", "Journal")]
    public void Voucher_types_map_as_the_contract_says(VoucherType type, string sub, string want)
    {
        var v = new Voucher("N", new DateOnly(2026, 4, 1), type, "n", "k", "x", sub, [new("5000", 100, 0), new("1030/BA-1", 0, 100)], []);
        Assert.Equal(want, VoucherExport.TallyVoucherType(v));
    }

    [Fact]
    public void A_journal_between_cash_and_bank_is_a_contra()
    {
        var v = new Voucher("N", new DateOnly(2026, 4, 1), VoucherType.Journal, "n", "k", "x", "bank-deposit", [new("1030/BA-1", 100, 0), new("1000", 0, 100)], []);
        Assert.Equal("Contra", VoucherExport.TallyVoucherType(v));
    }

    [Fact]
    public void Names_are_unique_xml_is_escaped_and_owner_groups_and_the_profit_carry_are_written()
    {
        var chart = new EffectiveChart(
            [.. ChartOfAccounts.Groups, new LedgerGroup("g-banquet", "Banquet & Events", "direct-income", "Cr", "PL", true)],
            [.. ChartOfAccounts.All, new LedgerAccount("4700", "Banquet <income>", AccountGroup.Income) { GroupId = "g-banquet", System = false }]);
        var parties = new Dictionary<string, TallyPartyInfo>
        {
            ["1110/A"] = new("Same Name"), ["1110/B"] = new("same name"), ["2200/V"] = new("Room revenue"), ["1110/C"] = new("Line\r\nBreak \"Q\" & Co"),
        };
        var opening = new Dictionary<string, long> { ["4000"] = -500_000, ["5000"] = 120_000, ["1000"] = 380_000, ["1110/A"] = 1_000 };
        var book = TallyBook.Build(chart, opening, parties, []);
        var names = book.Ledgers.Select(l => l.Name).ToList();
        Assert.Equal(names.Count, names.Select(n => n.ToLowerInvariant()).Distinct().Count());
        Assert.Contains("Same Name", names);
        Assert.Contains("same name (2)", names);
        Assert.Contains("Room revenue (2)", names);
        Assert.Equal(-380_000, book.ProfitCarryNet);   // earlier years' profit 3,80,000 (credit) → Dr − Cr = −380,000
        Assert.Equal(0, book.Find("4000")!.OpeningNet);
        var text = VoucherExport.TallyMastersXml(book, "R & D <Hotel>", new DateOnly(2026, 4, 1));
        var doc = XDocument.Parse(text);
        Assert.Equal("Banquet & Events", doc.Descendants("GROUP").Single().Attribute("NAME")!.Value);
        Assert.Equal("Direct Incomes", doc.Descendants("GROUP").Single().Element("PARENT")!.Value);
        Assert.Contains("Banquet &lt;income&gt;", text, StringComparison.Ordinal);
        Assert.Contains("Line  Break &quot;Q&quot; &amp; Co", text, StringComparison.Ordinal);
        Assert.Equal("R & D <Hotel>", doc.Descendants("SVCURRENTCOMPANY").Single().Value);
        var pl = doc.Descendants("LEDGER").Single(l => l.Attribute("ACTION")!.Value == "Alter");
        Assert.Equal("3800.00", pl.Element("OPENINGBALANCE")!.Value);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SVCURRENTCOMPANY", VoucherExport.TallyMastersXml(book, null, new DateOnly(2026, 4, 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void Notes_follow_the_fixed_comments_and_the_range_rule_is_kept()
    {
        var (book, stream, emit, company, identity, _) = FromInput();
        var text = VoucherExport.TallyVouchersXml(stream, emit, book, ChartOfAccounts.Builtin, company, identity,
            [VoucherExport.PendingNote(new DateOnly(2026, 4, 30), 2), "odd -- text"]);
        var lines = text.Split("\r\n");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("<!-- SIMULATED EXPORT", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("<!-- VOUCHER IDS", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("<!-- PROVISIONAL · 2026-04-30", lines[3], StringComparison.Ordinal);
        Assert.Equal("<!-- odd - - text -->", lines[4]);
        Assert.NotNull(VoucherExport.TallyRangeError(new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 2)));
        Assert.Null(VoucherExport.TallyRangeError(new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1)));
        Assert.Equal("-1234.50", EtpTallyXml.Amount(123_450));
        Assert.Equal("1234.50", EtpTallyXml.Amount(-123_450));
        Assert.Equal("0.00", EtpTallyXml.Amount(0));
        Assert.Equal(64, EtpTallyXml.Sha256Hex(Utf8.GetBytes(text)).Length);
    }

    [Fact]
    public void The_engine_vouchers_of_the_rulebook_month_export_cleanly()
    {
        // the real server pipeline (production mappers + VoucherBuilder) over the whole rulebook month, April 2026
        var all = RulebookMonthTests.FullMonthVouchers().OrderBy(v => v.Date).ThenBy(v => v.Number, StringComparer.Ordinal).ToList();
        var fy = new DateOnly(2026, 4, 1);
        var (opening, period) = AccountingReports.Split(all, fy, new DateOnly(2026, 4, 30));
        var open = new Dictionary<string, long>();
        foreach (var e in opening.SelectMany(v => v.Entries)) open[e.Account] = open.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        var parties = new Dictionary<string, TallyPartyInfo>
        {
            ["1030/BA-HDFC"] = new("HDFC"), ["2400/LN-HDFC"] = new("HDFC term loan"), ["2200/EV-MORNING"] = new("Morning Supplies", "27ABCDE1234F1Z5"),
            ["2200/EV-KAVERI"] = new("Kaveri Traders"), ["1110/CMP-7"] = new("Deccan Agro Ltd", "27AAACD1111A1Z1"), ["1115/CHN-GOIBIBO"] = new("Goibibo"), ["1200/STF-7"] = new("Staff 07"),
        };
        var book = TallyBook.Build(ChartOfAccounts.Builtin, open, parties, period.SelectMany(v => v.Entries.Select(e => e.Account)));
        var masters = XDocument.Parse(VoucherExport.TallyMastersXml(book, "Rulebook Hotels", fy));
        Assert.Contains(masters.Descendants("LEDGER"), l => l.Element("PARTYGSTIN")?.Value == "27AAACD1111A1Z1" && l.Element("ISBILLWISEON")!.Value == "Yes");
        var deccan = masters.Descendants("LEDGER").Single(l => l.Attribute("NAME")!.Value == "Deccan Agro Ltd");
        Assert.Equal("-40000.00", deccan.Element("OPENINGBALANCE")!.Value);   // receivable 40,000 Dr opening (rulebook §8.1)
        var stream = all.Where(v => !AccountingReports.IsOpening(v)).ToList();
        var emit = period.Where(v => !AccountingReports.IsOpening(v)).Select(v => v.SourceKey).ToHashSet();
        var doc = XDocument.Parse(VoucherExport.TallyVouchersXml(stream, emit, book, ChartOfAccounts.Builtin, "Rulebook Hotels", "T"));
        Assert.Equal(emit.Count, doc.Descendants("VOUCHER").Count());
        foreach (var v in doc.Descendants("VOUCHER"))
            Assert.Equal(0m, v.Elements("ALLLEDGERENTRIES.LIST").Sum(e => decimal.Parse(e.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture)));
        // T7a: HDFC receives 40,000 from Deccan against the opening bill; T7b the rest against the sales invoice (63,000 total with TDS)
        var refs = doc.Descendants("BILLALLOCATIONS.LIST").Where(b => b.Element("BILLTYPE")!.Value == "Agst Ref" && b.Element("NAME")!.Value == "Opening balance").ToList();
        Assert.NotEmpty(refs);
        Assert.DoesNotContain(doc.Descendants("VOUCHER"), v => v.Element("VOUCHERNUMBER")!.Value.EndsWith("/000", StringComparison.Ordinal) && v.Element("NARRATION")!.Value == "Opening balances");
    }
}
