using System.Text.Json;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Golden;

/// <summary>
/// Rebuilds the prototype's vouchers from the raw source facts (accounting-golden.json, exported by export-accounting-golden.mjs
/// from the MAIN prototype) with Hcc.Domain.Accounting and asserts the prototype's own numbers, entries, trial balance, CSV and
/// Tally XML exactly. Tax per line is the prototype's stored lineTax, split with the shared CGST/SGST rule.
/// </summary>
public sealed class GoldenAccountingTests
{
    private static readonly JsonDocument Golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "accounting-golden.json")));

    private static IEnumerable<JsonElement> Days => Golden.RootElement.GetProperty("days").EnumerateArray();

    public static TheoryData<string> DayNames()
    {
        var d = new TheoryData<string>();
        foreach (var x in Days) d.Add(x.GetProperty("name").GetString()!);
        return d;
    }

    private static JsonElement Day(string name) => Days.First(d => d.GetProperty("name").GetString() == name);

    private static DateOnly Date(JsonElement e, string p = "date") => DateOnly.Parse(e.GetProperty(p).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static string S(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();

    /// <summary>Raw prototype facts → sources through the production rules.</summary>
    private static List<AccountingSource> Sources(JsonElement day)
    {
        var date = Date(day);
        var list = new List<AccountingSource>();
        foreach (var s in day.GetProperty("sources").EnumerateArray())
        {
            var (id, n) = (S(s, "id"), S(s, "narration"));
            list.Add(S(s, "kind") switch
            {
                "line" => Line(s, id, date, n),
                "receipt" => SourceRules.FolioReceipt(id, S(s, "sub"), S(s, "tender"), date, L(s, "amountPaise"), n),
                "pos" => Pos(s, id, date, n),
                "expense" => SourceRules.Expense(id, S(s, "sub") == "reversal", S(s, "category"), S(s, "funding"), date, L(s, "amountPaise"), n),
                "closing" => SourceRules.CashDifference(id, date, L(s, "amountPaise"), n),
                var k => throw new InvalidOperationException("Unknown source kind " + k),
            });
        }

        return list;
    }

    private static AccountingSource Line(JsonElement s, string id, DateOnly date, string n)
    {
        var (cgst, sgst) = GstSplit.Of(L(s, "taxPaise"));
        return SourceRules.FolioLine(id, S(s, "sub"), date, L(s, "amountPaise"), cgst, sgst, n);
    }

    private static AccountingSource Pos(JsonElement s, string id, DateOnly date, string n)
    {
        var (cgst, sgst) = GstSplit.Of(L(s, "taxPaise"));
        return SourceRules.PosBill(id, S(s, "sub"), S(s, "tender"), date, L(s, "amountPaise"), L(s, "netPaise"), cgst, sgst, n);
    }

    private static Dictionary<string, string> Mapping(JsonElement day)
        => ChartOfAccounts.Effective(day.GetProperty("mappingOverrides").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));

    private static IReadOnlyList<Voucher> Build(JsonElement day) => VoucherBuilder.Build(Date(day), Sources(day), Mapping(day));

    [Fact]
    public void Default_mapping_contains_every_prototype_default()
    {
        foreach (var p in Golden.RootElement.GetProperty("defaultMapping").EnumerateObject())
            Assert.True(ChartOfAccounts.DefaultMapping.TryGetValue(p.Name, out var a) && a == p.Value.GetString(), p.Name);
    }

    [Theory]
    [MemberData(nameof(DayNames))]
    public void Vouchers_match_the_prototype(string name)
    {
        var day = Day(name);
        var expected = day.GetProperty("vouchers").EnumerateArray().ToList();
        var actual = Build(day);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var (e, a) = (expected[i], actual[i]);
            Assert.Equal(S(e, "no"), a.Number);
            Assert.Equal(S(e, "type"), a.Type.ToString());
            Assert.Equal(S(e, "narration"), a.Narration);
            Assert.Equal(S(e, "sourceId"), a.SourceKey);
            Assert.Equal(S(e, "sourceKind"), a.SourceKind);
            Assert.Equal(S(e, "sub"), a.Sub);
            Assert.Equal(e.GetProperty("entries").EnumerateArray().Select(x => (S(x, "account"), L(x, "debitPaise"), L(x, "creditPaise"))).ToList(),
                a.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToList());
            Assert.Equal(e.GetProperty("unmapped").EnumerateArray().Select(x => x.GetString()!).ToList(), a.Unmapped);
            Assert.True(a.Balanced, a.Number);
        }
    }

    [Theory]
    [MemberData(nameof(DayNames))]
    public void Trial_balance_csv_and_exceptions_match_the_prototype(string name)
    {
        var day = Day(name);
        var vs = Build(day);
        Assert.Equal(day.GetProperty("trialBalance").EnumerateArray().Select(x => (S(x, "account"), L(x, "debitPaise"), L(x, "creditPaise"), L(x, "balancePaise"))).ToList(),
            VoucherBuilder.TrialBalance(vs).Select(r => (r.Account, r.DebitPaise, r.CreditPaise, r.BalancePaise)).ToList());
        Assert.Equal(S(day, "csv"), VoucherExport.Csv(vs));
        var issues = VoucherBuilder.Exceptions(vs, vs, false, DayStatus.Closed);
        Assert.Equal(day.GetProperty("exceptions").EnumerateArray().Select(x => (S(x, "kind"), S(x, "severity"), x.GetProperty("key").ValueKind == JsonValueKind.Null ? null : x.GetProperty("key").GetString())).ToList(),
            issues.Select(x => (x.Kind, x.Severity, x.Key)).ToList());
    }

    [Fact]
    public void Base_fixture_figures_24_oct_collections_and_23_oct_gst_halves()
    {
        var d24 = Build(Day("base-24oct"));
        Assert.Equal(10, d24.Count(v => v.Type == VoucherType.Receipt));
        // Net receipts ₹21,500 = Overview "collections" (spec §7): tender debits of the payment receipts.
        Assert.Equal(2_150_000, d24.Where(v => v.SourceKind == "receipt").Sum(v => v.Entries.Where(e => e.Account is "1000" or "1010" or "1020" or "1030").Sum(e => e.DebitPaise)));
        var d23 = Build(Day("base-23oct"));
        Assert.Equal(8, d23.Count);
        var tb = VoucherBuilder.TrialBalance(d23).ToDictionary(r => r.Account);
        var room = tb["4000"].CreditPaise;
        Assert.Equal(room / 40, tb["2110"].CreditPaise);  // 5 % room GST, each half 2.5 % (DEC-069)
        Assert.Equal(room / 40, tb["2120"].CreditPaise);
        Assert.Equal(room + room / 20, tb["1100"].DebitPaise);
        Assert.False(tb.ContainsKey("2100"));
        Assert.Equal(0, VoucherBuilder.TrialBalance(d23).Sum(r => r.BalancePaise));
    }

    [Fact]
    public void Full_day_flow_discount_refund_cash_short_and_reversal_entries()
    {
        var flow = Build(Day("full-day-flow"));
        Assert.Equal([("1100", 0L, 21000L), ("4900", 20000L, 0L), ("2110", 500L, 0L), ("2120", 500L, 0L)],
            flow.Single(v => v.Sub == "discount").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        var refund = flow.Single(v => v.Sub == "refund");
        Assert.Equal(VoucherType.Payment, refund.Type);
        Assert.Equal([("1100", 5000L, 0L), ("1000", 0L, 5000L)], refund.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal([("5900", 5000L, 0L), ("1000", 0L, 5000L)],
            Build(Day("cash-short")).Single(v => v.SourceKind == "closing").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal([("5010", 0L, 180000L), ("1000", 180000L, 0L)],
            Build(Day("expense-reversal")).Single(v => v.Sub == "reversal").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        var gas = Build(Day("acc-unmapped")).Single(v => v.SourceKey == "EXP-ACC-1");
        Assert.Equal("9999", gas.Entries[0].Account);
        Assert.True(gas.Balanced);
    }

    [Fact]
    public void Tally_xml_matches_the_prototype_except_the_production_comment()
    {
        var t = Golden.RootElement.GetProperty("tally");
        var vouchers = Build(Day("base-23oct")).Concat(Build(Day("base-24oct"))).ToList();
        Assert.Equal(t.GetProperty("count").GetInt32(), vouchers.Count);
        var (d, c) = VoucherBuilder.Totals(vouchers);
        Assert.Equal(L(t, "debitPaise"), d);
        Assert.Equal(L(t, "creditPaise"), c);
        var expected = S(t, "xml").Split('\n').ToList();
        var actual = VoucherExport.TallyXml(vouchers, S(t, "company")).Split('\n').ToList();
        Assert.StartsWith("<!-- SIMULATED PREVIEW", expected[1], StringComparison.Ordinal);
        Assert.Equal(VoucherExport.TallyComment, actual[1]);
        expected.RemoveAt(1);
        actual.RemoveAt(1);
        Assert.Equal(expected, actual);
    }
}
