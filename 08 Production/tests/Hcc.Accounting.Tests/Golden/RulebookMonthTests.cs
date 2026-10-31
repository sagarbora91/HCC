using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Expenses;

namespace Hcc.Accounting.Tests.Golden;

/// <summary>
/// ACC-1 (lane acc1-tests) + ACC-2 (lane acc2-tests): the rulebook golden month, the WHOLE of §8.2 — 02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional.
/// <c>rulebook-month.json</c> (written by <c>build-rulebook-month.mjs</c> from 03 Prototype/tests/_rulebook-month.mjs, the same data the prototype's
/// tests/stream-accounting-rulebook.mjs uses) holds the opening balances (§8.1), the 26 entries of §8.2, the server events for the 19 steps that have events
/// (T1 T2 T3 T4 T5a T5b T5c T6 T7a T7b T8 T9 T10 T12 T13a T13b T13c T15a T15b — ACC-2 adds receipt kinds <c>ota-credit</c> / <c>deposit-apply</c> / <c>deposit</c>, a TDS
/// amount on a company payment, charge kind <c>food</c> and per-staff advance recovery), and the expectation worked out from the rulebook: the closing trial balance
/// by control ledger and by party. Events go through the production <see cref="AccountingEventMapper"/> and <see cref="VoucherBuilder"/>.
/// The 7 steps with no server command / event yet — T11 vendor payment (acc2-purchase), T14a T14b T16 T17 T18 manual vouchers (acc2-manual), T19 OTA payout (acc2-flows) —
/// have their payloads in the json (<c>waiting.events</c>); since the ACC-2 integration each goes through its lane's production mapper (T11 VendorPayment, T14–T18
/// ManualVoucher, T19 OtaPayout) and the full month closes to <c>expected.full</c> from the engine only (no stand-in left). Money = integer paise.
/// </summary>
public sealed class RulebookMonthTests
{
    private static readonly JsonDocument Month = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-month.json")));

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonElement Root => Month.RootElement;

    private static string S(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();

    private static DateOnly Date(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Control code of a ledger id: <c>1110/CMP-0007</c> → <c>1110</c> (a party sub-ledger sums into its control).</summary>
    internal static string Control(string account) => account.Contains('/', StringComparison.Ordinal) ? account[..account.IndexOf('/', StringComparison.Ordinal)] : account;

    private static Dictionary<string, string> MappingOverrides() => Root.GetProperty("mappingOverrides").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

    /// <summary>The month's server events → sources (production mapper), exactly as the outbox consumer maps them.</summary>
    internal static IReadOnlyList<(string Step, AccountingSource Source)> Sources()
    {
        var list = new List<(string, AccountingSource)>();
        foreach (var ev in Root.GetProperty("events").EnumerateArray())
        {
            var (step, type, p) = (S(ev, "step"), S(ev, "type"), ev.GetProperty("payload"));
            AccountingSource source = type switch
            {
                AccountingEvents.ChargePosted => ChargeOf(p),
                AccountingEvents.ReceiptRecorded => AccountingEventMapper.Receipt(L(p, "folioId"), L(p, "receiptId"), S(p, "kind"), S(p, "tender"), Date(S(p, "businessDate")), L(p, "amountPaise")),
                AccountingEvents.PosBillSettled => AccountingEventMapper.PosBill(p.Deserialize<PosBillSettledEvent>(Web)!),
                AccountingEvents.CompanyPaymentRecorded => AccountingEventMapper.CompanyPayment(p.Deserialize<CompanyPaymentRecordedEvent>(Web)!),
                AccountingEvents.ExpensePosted => AccountingEventMapper.Expense(p.Deserialize<ExpensePostedEvent>(Web)!),
                AccountingEvents.AdvanceGiven => AccountingEventMapper.Advance(p.Deserialize<AdvanceGivenEvent>(Web)!),
                AccountingEvents.PayrollPosted => AccountingEventMapper.PayRun(p.Deserialize<PayrollPostedEvent>(Web)!),
                AccountingEvents.PayrollPaid => AccountingEventMapper.PayRunPaid(p.Deserialize<PayrollPaidEvent>(Web)!),
                AccountingEvents.CashBanked => AccountingEventMapper.Banking(p.Deserialize<CashBankingEvent>(Web)!, rejected: false),
                AccountingEvents.ShiftDifferenceDecided => CashDifferenceOf(p),
                _ => throw new InvalidOperationException("Unknown event type " + type),
            };
            list.Add((step, source));
        }

        return list;
    }

    private static AccountingSource ChargeOf(JsonElement p)
    {
        var (cgst, sgst) = GstSplit.Of(L(p, "taxPaise"));   // the stored CGST / SGST split of the charge (agent C's tax.charge_gst), never recomputed
        return AccountingEventMapper.Charge(L(p, "folioId"), L(p, "chargeId"), S(p, "kind"), Date(S(p, "serviceDate")), L(p, "amountPaise"), cgst, sgst);
    }

    private static AccountingSource CashDifferenceOf(JsonElement p)
        => AccountingEventMapper.CashDifference(L(p, "shiftId"), 1, Date(S(p, "businessDate")), L(p, "expectedPaise"), L(p, "countedPaise"));

    /// <summary>The opening journal of §8.1 (dated asOf) as a voucher over the control ledgers.</summary>
    internal static Voucher OpeningVoucher()
    {
        var asOf = Date(S(Root, "asOf"));
        var entries = Root.GetProperty("opening").EnumerateArray().Select(x => new VoucherEntry(S(x, "account"), L(x, "debitPaise"), L(x, "creditPaise"))).ToList();
        return new Voucher("JRN/" + asOf.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "/000", asOf, VoucherType.Journal, "Opening balances", "opening", "opening", "opening-entry",
            entries, []);
    }

    /// <summary>All vouchers of the month (opening journal first), built day by day with the Owner's mapping of the month.</summary>
    internal static IReadOnlyList<Voucher> Vouchers(bool withOpening = true)
    {
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        var sources = Sources().Select(x => x.Source).ToList();
        var vouchers = new List<Voucher>();
        if (withOpening) vouchers.Add(OpeningVoucher());
        foreach (var d in sources.Select(s => s.BusinessDate).Distinct().Order())
            vouchers.AddRange(VoucherBuilder.Build(d, sources, mapping));
        return vouchers;
    }

    /// <summary>Signed balance (debit − credit) by control ledger.</summary>
    internal static SortedDictionary<string, long> Balances(IEnumerable<Voucher> vouchers)
    {
        var m = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in vouchers.SelectMany(v => v.Entries)) m[Control(e.Account)] = m.GetValueOrDefault(Control(e.Account)) + e.DebitPaise - e.CreditPaise;
        foreach (var k in m.Where(x => x.Value == 0).Select(x => x.Key).ToList()) m.Remove(k);
        return m;
    }

    internal static SortedDictionary<string, long> Expected(string table, string kind)
    {
        var m = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in Root.GetProperty("expected").GetProperty(table).GetProperty(kind).EnumerateObject()) m[p.Name] = p.Value.GetInt64();
        return m;
    }

    public static TheoryData<string> ExpectedLedgers()
    {
        var d = new TheoryData<string>();
        foreach (var k in Expected("closing", "control").Keys) d.Add(k);
        return d;
    }

    // ───────── self-checks: the data encodes the rulebook (no engine involved) ─────────

    [Fact]
    public void The_json_encodes_the_rulebook_opening_77_40_000_every_entry_balances_and_the_month_closes_to_the_83_trial_balance()
    {
        var opening = Root.GetProperty("opening").EnumerateArray().ToList();
        Assert.Equal(774_000_000, opening.Sum(x => L(x, "debitPaise")));
        Assert.Equal(774_000_000, opening.Sum(x => L(x, "creditPaise")));
        var steps = Root.GetProperty("steps").EnumerateObject().ToList();
        Assert.Equal(26, steps.Count);
        foreach (var s in steps)
            Assert.Equal(s.Value.EnumerateArray().Sum(x => L(x, "debitPaise")), s.Value.EnumerateArray().Sum(x => L(x, "creditPaise")));
        // opening + all 26 steps = §8.3, ledger by ledger (control level) and in total ₹84,27,500
        var all = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var x in opening.Concat(steps.SelectMany(s => s.Value.EnumerateArray())))
            all[S(x, "account")] = all.GetValueOrDefault(S(x, "account")) + L(x, "debitPaise") - L(x, "creditPaise");
        foreach (var k in all.Where(x => x.Value == 0).Select(x => x.Key).ToList()) all.Remove(k);
        Assert.Equal(Root.GetProperty("expected").GetProperty("rulebookTrialBalance83").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64()).OrderBy(x => x.Key, StringComparer.Ordinal), all);
        Assert.Equal(842_750_000, all.Values.Where(v => v > 0).Sum());
        Assert.Equal(-842_750_000, all.Values.Where(v => v < 0).Sum());
        // the whole month is in the file
        Assert.Equal(all.Keys, Root.GetProperty("expected").GetProperty("fullMonthControl").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(19, Root.GetProperty("driven").GetArrayLength());
        Assert.Equal(7, Root.GetProperty("waiting").GetProperty("steps").GetArrayLength());
        Assert.Equal(["T11", "T14a", "T14b", "T16", "T17", "T18", "T19"], Root.GetProperty("waiting").GetProperty("steps").EnumerateArray().Select(x => x.GetString()!));
        // §8.4 profit ₹1,40,400 from the P&L ledgers only (4xxx / 5xxx), §8.5 totals ₹79,27,900, §8.6 challan ₹28,900 — worked out from the 26 entries
        var pl = all.Where(x => x.Key[0] is '4' or '5').Sum(x => -x.Value);
        Assert.Equal(14_040_000, pl);
        Assert.Equal(14_040_000, Root.GetProperty("expected").GetProperty("profitAndLoss84").GetProperty("profit").GetInt64());
        var assets = all.Where(x => x.Key[0] == '1').Sum(x => x.Value);
        Assert.Equal(792_790_000, assets);
        Assert.Equal(792_790_000, -all["3100"] + pl - all["3000"] - all["2400"] - all["2200"] - all["2110"] - all["2120"] - all["2310"] - all["2320"] - all["2330"] - all["2000"]);
        Assert.Equal(2_890_000, -all["2110"] - all["2120"] - all["1340"]);   // output 29,000 less the TCS credit 100
        Assert.Equal(2_890_000, Root.GetProperty("expected").GetProperty("gst86").GetProperty("challan").GetInt64());
    }

    [Fact]
    public void Every_driven_step_has_events_and_the_events_cover_the_driven_steps_exactly()
    {
        var driven = Root.GetProperty("driven").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal).ToList();
        var waiting = Root.GetProperty("waiting").GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(driven, Sources().Select(x => x.Step).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(26, driven.Count + waiting.Count);
        Assert.Empty(driven.Intersect(waiting));
        Assert.Equal(waiting, Root.GetProperty("waiting").GetProperty("events").EnumerateArray().Select(x => S(x, "step")).Order(StringComparer.Ordinal));   // intended payloads for every waiting step
        Assert.All(Sources(), x => Assert.True(x.Source.Debit == x.Source.Credit && x.Source.Debit > 0, x.Source.SourceKey));
        Assert.Equal(Sources().Count, Sources().Select(x => x.Source.SourceKey).Distinct().Count());   // one stable key per operational record
    }

    // ───────── the events, through the production mapper and VoucherBuilder ─────────

    public static TheoryData<string> DrivenSteps()
    {
        var d = new TheoryData<string>();
        foreach (var k in Root.GetProperty("driven").EnumerateArray().Select(x => x.GetString()!)) d.Add(k);
        return d;
    }

    /// <summary>
    /// One step's event(s) through the production mapper + builder = the rulebook's entries on the control ledgers. Passing today: T1 T3 T5b T5c T6 T7a T8 T9 T10 T12 T13a-c
    /// T15a-b. Waiting for a lane (the failure says which): T2 (<c>line:food</c> → 4200: acc2-parity), T4 (receipt kind <c>ota-credit</c>: acc2-flows), T5a (kind
    /// <c>deposit-apply</c>: acc2-flows), T7b (TDS on a company payment: acc2-tds).
    /// </summary>
    [Theory]
    [MemberData(nameof(DrivenSteps))]
    public void Each_driven_step_posts_the_rulebook_entries_on_the_control_ledgers(string step)
    {
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        var mine = Sources().Where(x => x.Step == step).Select(x => x.Source).ToList();
        var got = Balances(mine.SelectMany(s => VoucherBuilder.Build(s.BusinessDate, [s], mapping)));
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var x in Root.GetProperty("steps").GetProperty(step).EnumerateArray())
            want[S(x, "account")] = want.GetValueOrDefault(S(x, "account")) + L(x, "debitPaise") - L(x, "creditPaise");
        foreach (var k in want.Where(x => x.Value == 0).Select(x => x.Key).ToList()) want.Remove(k);
        var lane = step switch { "T2" => "acc2-parity (line:food → 4200)", "T4" or "T5a" => "acc2-flows (receipt kind ota-credit / deposit-apply)", "T7b" => "acc2-tds (TDS line on a company payment)", _ => "" };
        Assert.True(got.SequenceEqual(want), $"{step}: engine {string.Join(" ", got.Select(x => x.Key + "=" + x.Value))} ≠ rulebook {string.Join(" ", want.Select(x => x.Key + "=" + x.Value))}"
            + (lane.Length > 0 ? Environment.NewLine + "(waits for lane " + lane + ")" : ""));
    }

    [Theory]
    [MemberData(nameof(ExpectedLedgers))]
    [Trait("Lane", "acc1-chart")]
    public void Closing_balance_of_each_ledger_equals_the_rulebook_expectation(string account)
    {
        // opening + the driven steps; 4200 (F&B revenue, the contract's pos:sales re-point) needs lane acc1-chart, everything else is in the server chart today
        var got = Balances(Vouchers());
        Assert.Equal(Expected("closing", "control")[account], got.GetValueOrDefault(account));
    }

    [Fact]
    [Trait("Lane", "acc1-chart")]
    public void Trial_balance_agrees_nothing_in_suspense_and_no_ledger_outside_the_expectation()
    {
        var vouchers = Vouchers();
        Assert.All(vouchers, v => Assert.True(v.Balanced, v.Number));
        var (d, c) = VoucherBuilder.Totals(vouchers);
        Assert.Equal(d, c);
        var got = Balances(vouchers);
        Assert.False(got.ContainsKey(ChartOfAccounts.Suspense), "something posted to 9999 Suspense (an unmapped key)");
        Assert.Equal(Expected("closing", "control").Keys, got.Keys);
        Assert.Empty(vouchers.SelectMany(v => v.Unmapped));
    }

    [Fact]
    public void Voucher_dates_and_numbers_follow_the_rulebook_calendar()
    {
        var vs = Vouchers(withOpening: false);
        var by = vs.GroupBy(v => v.Date).ToDictionary(g => g.Key, g => g.ToList());
        // T8 5 Apr (vendor credit = Journal), T7a 10 Apr (Receipt), T9 12 Apr, T10 15 Apr (Payment), T15a 25 Apr (contra Journal), T15b 26 Apr (Journal)
        Assert.Equal("PUR/20260405/001", by[Date("2026-04-05")].Single(v => v.SourceKey == "expense:501").Number); // ACC-2 contract §3.1: Purchase (was Journal)
        Assert.Equal(VoucherType.Receipt, by[Date("2026-04-10")].Single(v => v.Sub == "company-payment").Type);
        Assert.Equal(VoucherType.Purchase, by[Date("2026-04-12")].Single(v => v.SourceKey == "expense:502").Type); // ACC-2 contract §3.1 (was Journal)
        Assert.Equal(VoucherType.Payment, by[Date("2026-04-15")].Single(v => v.SourceKey == "expense:503").Type);
        Assert.Equal("bank-deposit", by[Date("2026-04-25")].Single().Sub);
        Assert.Equal("cash-difference", by[Date("2026-04-26")].Single().Sub);
        // company credit (T5b) before the company pays (T7a): the Deccan Agro receivable reads the opening 40,000, then 1,03,000, then 63,000
        var running = Root.GetProperty("opening").EnumerateArray().Where(x => S(x, "account") == "1110").Sum(x => L(x, "debitPaise") - L(x, "creditPaise"));
        var trace = new List<long> { running };
        foreach (var v in vs.OrderBy(v => v.Date).ThenBy(v => v.Number, StringComparer.Ordinal))
        {
            var delta = v.Entries.Where(e => Control(e.Account) == "1110").Sum(e => e.DebitPaise - e.CreditPaise);
            if (delta != 0) trace.Add(trace[^1] + delta);
        }

        Assert.Equal([4_000_000L, 10_300_000L, 6_300_000L, 0L], trace);   // 40,000 → 1,03,000 (T5b) → 63,000 (T7a) → nil (T7b: 57,000 + TDS 6,000, waits for acc2-tds)
    }

    // ───────── ACC-1 integration: the lanes' tests, un-skipped once chart, parties, opening and statements were merged ─────────

    /// <summary>The month's parties as the projector resolves them on the server (contract §3): the json's party ids → server party ids. The
    /// company's id is the event's <c>companyId</c> (7 → <c>CMP-7</c>; the json writes it <c>CMP-0007</c>), staff 7 → <c>STF-7</c>.</summary>
    internal static string ServerLedger(string ledger) => ledger
        .Replace("CMP-0007", PartyLedgers.Company(7), StringComparison.Ordinal)
        .Replace("STF-007", PartyLedgers.Staff(7), StringComparison.Ordinal);

    /// <summary>The month's OTA channel party (server: the channel code, <c>ota.channel.code</c>).</summary>
    internal const string Goibibo = "CHN-GOIBIBO";

    private static string? Tender(string tender) => tender switch { "Cash" => "TILL", "Bank transfer" => "BA-HDFC", _ => null };

    private static string? Vendor(string vendor) => vendor switch { "Morning Supplies" => "EV-MORNING", "Kaveri Textiles" => "EV-KAVERI", _ => null };

    /// <summary>The month's sources with the parties the projector reads from the masters (tender settles-to account, vendor code, company of a
    /// company-credit receipt, bank of a deposit).</summary>
    internal static IReadOnlyList<AccountingSource> PartySources()
    {
        var list = new List<AccountingSource>();
        foreach (var ev in Root.GetProperty("events").EnumerateArray())
        {
            var (type, p) = (S(ev, "type"), ev.GetProperty("payload"));
            list.Add(type switch
            {
                AccountingEvents.ChargePosted => ChargeOf(p),
                AccountingEvents.ReceiptRecorded => AccountingEventMapper.Receipt(L(p, "folioId"), L(p, "receiptId"), S(p, "kind"), S(p, "tender"), Date(S(p, "businessDate")), L(p, "amountPaise"),
                    new PartyContext(Tender: Tender(S(p, "tender")), Company: S(p, "kind") == "company-credit" ? PartyLedgers.Company(7) : null,
                        Channel: S(p, "kind") == "ota-credit" ? Goibibo : null)), // ACC-2 flows: the OTA channel (projector: the booking's channel code)
                AccountingEvents.PosBillSettled => AccountingEventMapper.PosBill(p.Deserialize<PosBillSettledEvent>(Web)!, new PartyContext(Tender: Tender(S(p, "tender")))),
                AccountingEvents.CompanyPaymentRecorded => AccountingEventMapper.CompanyPayment(p.Deserialize<CompanyPaymentRecordedEvent>(Web)!, new PartyContext(Tender: Tender(S(p, "tender")))),
                AccountingEvents.ExpensePosted => AccountingEventMapper.Expense(p.Deserialize<ExpensePostedEvent>(Web)!, new PartyContext(Tender: Tender(S(p, "funding")), Vendor: Vendor(S(p, "vendor")))),
                AccountingEvents.AdvanceGiven => AccountingEventMapper.Advance(p.Deserialize<AdvanceGivenEvent>(Web)!, new PartyContext(Tender: Tender(S(p, "funding")))),
                AccountingEvents.PayrollPosted => AccountingEventMapper.PayRun(p.Deserialize<PayrollPostedEvent>(Web)!),
                AccountingEvents.PayrollPaid => AccountingEventMapper.PayRunPaid(p.Deserialize<PayrollPaidEvent>(Web)!, new PartyContext(Tender: Tender(S(p, "funding")))),
                AccountingEvents.CashBanked => AccountingEventMapper.Banking(p.Deserialize<CashBankingEvent>(Web)!, rejected: false, new PartyContext(Tender: "TILL", Bank: "BA-HDFC")),
                AccountingEvents.ShiftDifferenceDecided => AccountingEventMapper.CashDifference(L(p, "shiftId"), 1, Date(S(p, "businessDate")), L(p, "expectedPaise"), L(p, "countedPaise"),
                    new PartyContext(Tender: "TILL")),
                _ => throw new InvalidOperationException("Unknown event type " + type),
            });
        }

        return list;
    }

    /// <summary>The opening journal of §8.1 on the party ledgers (<c>ledger</c>: <c>1030/BA-HDFC</c>, <c>1110/CMP-…</c>, <c>2200/EV-MORNING</c>).</summary>
    internal static Voucher PartyOpeningVoucher()
    {
        var entries = Root.GetProperty("opening").EnumerateArray().Select(x => new VoucherEntry(ServerLedger(S(x, "ledger")), L(x, "debitPaise"), L(x, "creditPaise"))).ToList();
        return OpeningVoucher() with { Entries = entries };
    }

    internal static IReadOnlyList<Voucher> PartyVouchers()
    {
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        var sources = PartySources();
        var vouchers = new List<Voucher> { PartyOpeningVoucher() };
        foreach (var d in sources.Select(s => s.BusinessDate).Distinct().Order())
            vouchers.AddRange(VoucherBuilder.Build(d, sources, mapping));
        return vouchers;
    }

    [Fact]
    [Trait("Lane", "acc2")]
    public void Party_ledgers_hold_the_party_balances()
    {
        var vouchers = PartyVouchers();
        var by = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in vouchers.SelectMany(v => v.Entries)) by[e.Account] = by.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        // ACC-2 integration: 1115/CHN-GOIBIBO carries the OTA party of the T4 receipt (acc2-flows PartyContext.Channel).
        foreach (var (ledger, want) in Expected("closing", "party"))
            Assert.True(want == by.GetValueOrDefault(ServerLedger(ledger)), $"{ServerLedger(ledger)}: {by.GetValueOrDefault(ServerLedger(ledger))} ≠ {want}");
        // Deccan Agro: 40,000 opening + 63,000 (T5b) − 40,000 (T7a) − 63,000 (T7b: 57,000 received + 6,000 TDS, waits for acc2-tds) = nil.
        Assert.Equal(0, by.GetValueOrDefault(PartyLedgers.Compose("1110", PartyLedgers.Company(7))));
        // Morning 84,000 Cr (T11 waits); HDFC 2,88,500 Dr (3,00,000 + 40,000 + 57,000 − 45,000 + 1,50,000 − 2,13,500; T11 T14 T16-T19 wait); the loan party 20,00,000 Cr from the opening.
        Assert.Equal(-2_000_000_00L, by[PartyLedgers.Compose("2400", "LN-HDFC")]);
        // ACC-2 (acc2-parity): the pay run recovers the advance PER STAFF — 1200/STF-7 nets to nil and nothing is left on the 1200 control (ACC-1 left 5,000 on the party and −5,000 on the control).
        Assert.Equal(0, by[PartyLedgers.Compose("1200", PartyLedgers.Staff(7))]);
        Assert.Equal(0, by.GetValueOrDefault("1200"));
        // The control-level totals are unchanged by the parties (same figures as the control test).
        Assert.Equal(Expected("closing", "control"), Balances(vouchers));
    }

    [Fact]
    [Trait("Lane", "acc2-flows")]
    public void Ota_receivable_is_a_party_ledger_per_channel()
    {
        // T4: the folio balance moves to the channel at check-out (receipt kind ota-credit) → 1115/CHN-GOIBIBO 21,000 Dr; T19's payout clears it.
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        var t4 = Root.GetProperty("events").EnumerateArray().Single(e => S(e, "step") == "T4" && S(e, "type") == AccountingEvents.ReceiptRecorded).GetProperty("payload");
        var credit = AccountingEventMapper.Receipt(L(t4, "folioId"), L(t4, "receiptId"), S(t4, "kind"), S(t4, "tender"), Date(S(t4, "businessDate")), L(t4, "amountPaise"),
            new PartyContext(Channel: Goibibo));
        var cv = Assert.Single(VoucherBuilder.Build(credit.BusinessDate, [credit], mapping));
        Assert.Equal(VoucherType.Journal, cv.Type);
        Assert.Equal([("1115/CHN-GOIBIBO", 2_100_000L, 0L), ("1100", 0L, 2_100_000L)], cv.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        var payout = Waiting("T19").Single();
        var pv = Assert.Single(VoucherBuilder.Build(payout.BusinessDate, [payout], mapping));
        var net = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in cv.Entries.Concat(pv.Entries)) net[e.Account] = net.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        Assert.Equal(0, net["1115/CHN-GOIBIBO"]);
        // without a channel the receivable stays on the control (a no-party warning, never a block)
        var bare = AccountingEventMapper.Receipt(L(t4, "folioId"), L(t4, "receiptId"), S(t4, "kind"), S(t4, "tender"), Date(S(t4, "businessDate")), L(t4, "amountPaise"));
        Assert.Contains(VoucherBuilder.Build(bare.BusinessDate, [bare], mapping).Single().Entries, e => e.Account == "1115");
    }

    [Fact]
    [Trait("Lane", "acc1-opening")]
    public void Opening_entry_through_the_rules_is_in_the_cumulative_trial_balance_and_gates_the_first_lock()
    {
        // The 11 lines of §8.1 as the Manager saves them (party ids as entered); balanced, so no 1400 line is added.
        var lines = Root.GetProperty("opening").EnumerateArray().Select(x => new OpeningLineRequest(ServerLedger(S(x, "ledger")), L(x, "debitPaise"), L(x, "creditPaise"))).ToList();
        Assert.Equal(11, lines.Count);
        Assert.Empty(OpeningBalanceRules.Errors(lines));
        var withBalance = OpeningBalanceRules.WithBalance(lines);
        Assert.DoesNotContain(withBalance, l => l.Account == OpeningBalanceRules.DifferenceAccount);
        var asOf = Date(S(Root, "asOf"));
        var draft = new OpeningEntry(1, asOf, OpeningBalanceRules.Draft, withBalance, 2, new DateTime(2026, 10, 24, 5, 0, 0, DateTimeKind.Utc), null, null, null, "AAAAAAAAB9E=");
        var approved = draft with { Status = OpeningBalanceRules.Approved, ApprovedBy = 1, ApprovedUtc = new DateTime(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc) };
        // Coordinator ruling 5: with no entry at all, locks are not gated (DEC-148: the trial period still locks days); a draft gates every date ≥ asOf.
        var end = new DateOnly(2026, 4, 30);
        Assert.Null(OpeningBalanceRules.LockBlocker(null, end));
        Assert.True(OpeningBalanceRules.Pending(null));
        Assert.StartsWith("Opening balances pending", OpeningBalanceRules.LockBlocker(draft, end), StringComparison.Ordinal);
        Assert.Null(OpeningBalanceRules.LockBlocker(draft, asOf.AddDays(-1)));
        Assert.Null(OpeningBalanceRules.LockBlocker(approved, end));
        Assert.False(OpeningBalanceRules.Pending(approved));
        // The approved journal JRN/20260401/000 + the month = the rulebook closing (control ledgers).
        var journal = OpeningBalanceRules.Voucher(approved);
        Assert.Equal(("JRN/20260401/000", asOf, 774_000_000L, 774_000_000L), (journal.Number, journal.Date, journal.Entries.Sum(e => e.DebitPaise), journal.Entries.Sum(e => e.CreditPaise)));
        Assert.Equal(Expected("closing", "control"), Balances(PartyVouchers().Skip(1).Prepend(journal)));
    }

    [Fact]
    [Trait("Lane", "acc1-statements")]
    public void Six_column_trial_balance_and_the_party_statement_follow_the_month()
    {
        var all = PartyVouchers();
        var (from, to) = (new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var (opening, period) = AccountingReports.Split(all, from, to);
        Assert.Single(opening);                                   // the opening journal dated ON 1 April counts as opening (ruling 6)
        foreach (var detail in new[] { false, true })
        {
            var tb = AccountingReports.TrialBalance6(from, to, detail, opening, period, openingPending: false);
            Assert.True(tb.Balanced, "detail " + detail);
            Assert.Equal((774_000_000L, 774_000_000L), (tb.Totals.OpeningDebitPaise, tb.Totals.OpeningCreditPaise));
            Assert.Equal(tb.Totals.DebitPaise, tb.Totals.CreditPaise);
            Assert.Equal(tb.Totals.ClosingDebitPaise, tb.Totals.ClosingCreditPaise);
            Assert.DoesNotContain(tb.Rows, r => r.Account == ChartOfAccounts.Suspense);
        }

        var summary = AccountingReports.TrialBalance6(from, to, false, opening, period, openingPending: false);
        foreach (var (code, want) in Expected("closing", "control"))
        {
            var r = summary.Rows.Single(x => x.Account == code);
            Assert.Equal(want, r.ClosingDebitPaise - r.ClosingCreditPaise);
        }

        // Statement of the company party from 2 April: opening 40,000 Dr, T5b +63,000 → 1,03,000 Dr, T7a −40,000 → 63,000 Dr, T7b −63,000 (57,000 received + 6,000 TDS: acc2-tds) → nil.
        var deccan = PartyLedgers.Compose("1110", PartyLedgers.Company(7));
        var (o2, p2) = AccountingReports.Split(all, new DateOnly(2026, 4, 2), to);
        var st = AccountingReports.Statement(deccan, new DateOnly(2026, 4, 2), to, o2, p2);
        Assert.Equal(4_000_000, st.OpeningPaise);
        Assert.Equal([10_300_000L, 6_300_000L, 0L], st.Rows.Select(r => r.RunningPaise));
        Assert.Equal((6_300_000L, 10_300_000L, 0L), (st.DebitPaise, st.CreditPaise, st.ClosingPaise));
        // Day book: every period voucher, date then number order, balanced.
        var book = AccountingReports.DayBook(from, to, period);
        Assert.True(book.Balanced);
        Assert.Equal(period.Count, book.Vouchers);
    }

    // ───────── ACC-2: the whole month — the steps that wait for a server command, and the full closing trial balance ─────────

    /// <summary>The 19 event-driven steps through the engine plus the rulebook's own entries for the 7 waiting steps, over the opening journal: ₹84,27,500 exactly, ledger by ledger.
    /// It proves the pieces compose to §8.3; each waiting step is replaced by its real command as the lane lands (see the skipped tests).</summary>
    [Fact]
    [Trait("Lane", "acc2")]
    public void The_engine_vouchers_plus_the_rulebook_entries_of_the_waiting_steps_close_to_84_27_500()
    {
        var vouchers = Vouchers().ToList();
        foreach (var step in Root.GetProperty("waiting").GetProperty("steps").EnumerateArray().Select(x => x.GetString()!))
        {
            var entries = Root.GetProperty("steps").GetProperty(step).EnumerateArray().Select(x => new VoucherEntry(S(x, "account"), L(x, "debitPaise"), L(x, "creditPaise"))).ToList();
            vouchers.Add(new Voucher("", Date("2026-04-30"), VoucherType.Journal, "Rulebook entry " + step + " (stand-in)", "standin:" + step, "manual", step, entries, []));
        }

        var got = Balances(vouchers);
        Assert.Equal(Root.GetProperty("expected").GetProperty("rulebookTrialBalance83").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => (p.Name, p.Value.GetInt64())),
            got.Select(x => (x.Key, x.Value)));
        Assert.Equal(842_750_000, got.Values.Where(v => v > 0).Sum());
        Assert.Equal(-842_750_000, got.Values.Where(v => v < 0).Sum());
        Assert.False(got.ContainsKey(ChartOfAccounts.Suspense));
    }

    // ───────── ACC-2 integration: the 7 waiting steps through the lanes' production mappers (payloads = rulebook-month.json waiting.events) ─────────

    private static JsonElement WaitingPayload(string step) => Root.GetProperty("waiting").GetProperty("events").EnumerateArray().Single(e => S(e, "step") == step).GetProperty("payload");

    /// <summary>One waiting step as the server books it: T11 VendorPaymentRecorded, T14a T14b T16 T17 T18 ManualVoucherApproved, T19 OtaPayoutRecorded, with the
    /// parties the projector reads (tender → BA-HDFC, vendor → EV-MORNING; a manual line names its party ledger; the payout names bank and channel).</summary>
    internal static IReadOnlyList<AccountingSource> Waiting(string step)
    {
        var p = WaitingPayload(step);
        return step switch
        {
            "T11" => [AccountingEventMapper.VendorPayment(new VendorPaymentEvent(L(p, "paymentId"), Date(S(p, "businessDate")), S(p, "vendorCode"), S(p, "vendor"), S(p, "tender"),
                L(p, "amountPaise"), p.GetProperty("allocations").EnumerateArray().Sum(a => L(a, "amountPaise")), S(p, "reference")),
                new PartyContext(Tender: Tender(S(p, "tender")), Vendor: S(p, "vendorCode")))],
            "T19" => [AccountingEventMapper.OtaPayout(new OtaPayoutRecordedEvent(L(p, "payoutId"), 3, Goibibo, "Goibibo", Date(S(p, "businessDate")), S(p, "statementRef"),
                L(p, "grossPaise"), L(p, "commissionPaise"), L(p, "commissionGstPaise"), L(p, "tcsPaise"), L(p, "netPaise"), "BA-HDFC", ItcAllowed: false, Bookings: 1))],
            _ => [AccountingEventMapper.ManualVoucher(new ManualVoucherApprovedEvent(L(p, "voucherId"), Date(S(p, "businessDate")), S(p, "voucherType"), S(p, "narration"), null,
                S(p, "template"), p.GetProperty("lines").EnumerateArray().Select(l => new ManualLinePayload(S(l, "ledger"), L(l, "debitPaise"), L(l, "creditPaise"))).ToList()))],
        };
    }

    /// <summary>A waiting step's vouchers = the rulebook entries on the party ledgers (the step's own entries, <c>ledger</c> column).</summary>
    private static void AssertStepEntries(string step, IReadOnlyList<Voucher> vouchers)
    {
        var got = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in vouchers.SelectMany(v => v.Entries)) got[e.Account] = got.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var x in Root.GetProperty("steps").GetProperty(step).EnumerateArray()) want[S(x, "ledger")] = want.GetValueOrDefault(S(x, "ledger")) + L(x, "debitPaise") - L(x, "creditPaise");
        Assert.True(got.SequenceEqual(want), $"{step}: engine {string.Join(" ", got.Select(x => x.Key + "=" + x.Value))} ≠ rulebook {string.Join(" ", want.Select(x => x.Key + "=" + x.Value))}");
        Assert.All(vouchers, v => Assert.True(v.Balanced && v.Unmapped.Count == 0, v.Number));
    }

    private static IReadOnlyList<Voucher> Build(IReadOnlyList<AccountingSource> sources)
    {
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        return sources.Select(s => s.BusinessDate).Distinct().Order().SelectMany(d => VoucherBuilder.Build(d, sources, mapping)).ToList();
    }

    [Fact]
    [Trait("Lane", "acc2-purchase")]
    public void T11_vendor_payment_allocated_oldest_first()
    {
        // Rulebook T11, 18 Apr: Payment Dr 2200/EV-MORNING 60,000 · Cr 1030/BA-HDFC; allocation oldest first = the 25,000 opening then 35,000 of bill 501.
        var v = Assert.Single(Build(Waiting("T11")));
        Assert.Equal((VoucherType.Payment, "vendor-payment", "vendor-payment:901"), (v.Type, v.Sub, v.SourceKey));
        AssertStepEntries("T11", [v]);
        var alloc = ExpensePurchase.Allocate([new ExpensePurchase.OpenBill(0, Date("2026-03-31"), 2_500_000, 0, 0), new ExpensePurchase.OpenBill(501, Date("2026-04-05"), 5_900_000, 0, 0)],
            L(WaitingPayload("T11"), "amountPaise"));
        Assert.Equal(WaitingPayload("T11").GetProperty("allocations").EnumerateArray()
            .Select(a => (a.GetProperty("expenseId").ValueKind == JsonValueKind.Null ? 0L : L(a, "expenseId"), L(a, "amountPaise"))), alloc);
        // 2200/EV-MORNING: 84,000 Cr after the driven steps (opening 25,000 + bill 501 59,000) → 24,000 Cr after the payment.
        var morning = PartyVouchers().Concat([v]).SelectMany(x => x.Entries).Where(e => e.Account == "2200/EV-MORNING").Sum(e => e.DebitPaise - e.CreditPaise);
        Assert.Equal(-2_400_000, morning);
    }

    [Fact]
    [Trait("Lane", "acc2-manual")]
    public void T14_T16_T17_T18_manual_vouchers_post_their_template_entries()
    {
        // T14a Contra (CTR/20260427/001, sub contra); T14b Journal (the MDR line is not a cash / bank ledger: coordinator ruling 1, rulebook B8 amended);
        // T16 owner drawing, T17 loan EMI (2400/LN-HDFC), T18 bank charges = Payments. (Pending / approval / void on a locked day: ManualVoucherSqlTests.)
        var both = Build([.. Waiting("T14a"), .. Waiting("T14b")]);
        var contra = both.Single(v => v.SourceKey == "manual:1");
        var mdr = both.Single(v => v.SourceKey == "manual:2");
        Assert.Equal(("CTR/20260427/001", VoucherType.Contra, "contra"), (contra.Number, contra.Type, contra.Sub));
        Assert.Equal((VoucherType.Journal, "journal"), (mdr.Type, mdr.Sub));
        AssertStepEntries("T14a", [contra]);
        AssertStepEntries("T14b", [mdr]);
        foreach (var (step, template) in new[] { ("T16", "owner-drawing-bank"), ("T17", "loan-emi"), ("T18", "bank-charges") })
        {
            var v = Assert.Single(Build(Waiting(step)));
            Assert.Equal((VoucherType.Payment, template), (v.Type, v.Sub));
            AssertStepEntries(step, [v]);
        }

        // the HDFC loan after the EMI principal: 20,00,000 − 40,000 = 19,60,000 Cr
        Assert.Equal(-196_000_000L, PartyVouchers().Concat(Build(Waiting("T17"))).SelectMany(v => v.Entries).Where(e => e.Account == "2400/LN-HDFC").Sum(e => e.DebitPaise - e.CreditPaise));
    }

    [Fact]
    [Trait("Lane", "acc2-flows")]
    public void T19_ota_payout_from_the_statement()
    {
        // Rulebook T19, 29 Apr: Receipt Dr 1030/BA-HDFC 17,360 · Dr 5070 3,540 · Dr 1340 100 · Cr 1115/CHN-GOIBIBO 21,000.
        var v = Assert.Single(Build(Waiting("T19")));
        Assert.Equal((VoucherType.Receipt, "ota-payout", "ota-payout:1"), (v.Type, v.Sub, v.SourceKey));
        AssertStepEntries("T19", [v]);
        var p = WaitingPayload("T19");
        Assert.Null(Hcc.Domain.Ota.OtaPayoutRules.Problem(S(p, "statementRef"), L(p, "grossPaise"), L(p, "commissionPaise"), L(p, "commissionGstPaise"), L(p, "tcsPaise"), L(p, "netPaise")));
        Assert.StartsWith("Gross ", Hcc.Domain.Ota.OtaPayoutRules.Problem(S(p, "statementRef"), L(p, "grossPaise"), L(p, "commissionPaise"), L(p, "commissionGstPaise"),
            L(p, "tcsPaise"), L(p, "netPaise") - 100), StringComparison.Ordinal);   // does not add up
        Assert.Contains("open receivable", Hcc.Domain.Ota.OtaPayoutRules.OverReceivable("Goibibo", 2_100_000, 1_000_000), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Lane", "acc2-gst")]
    public void GST_set_off_and_the_month_lock_for_April()
    {
        // Rulebook §8.6 (in May): April's output 14,500 + 14,500, the TCS 100 in 1340 → challan ₹28,900; after the challan (Dr 1340 28,900) the set-off
        // Journal Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000. (Month lock refused until the set-off is posted / no pending voucher: GstMonthSqlTests.)
        var full = FullMonthVouchers();
        var april = GstMonthRules.Compute("2026-04", full, []);
        var gst = Root.GetProperty("expected").GetProperty("gst86");
        Assert.Equal((L(gst, "cgst"), L(gst, "sgst"), L(gst, "igst")), (april.Output.Cgst, april.Output.Sgst, april.Output.Igst));
        Assert.Equal((L(gst, "tcs"), L(gst, "challan")), (april.Tcs, april.Setoff.Challan));
        // the challan paid in May (template gst-payment: Dr 1340 · Cr 1030/BA-HDFC), then the set-off uses the whole 1340 balance
        var challan = SourceRules.Manual(SourceRules.ManualKey(90), Date("2026-05-10"), VoucherType.Payment, "gst-payment", "GST paid for 2026-04 · challan CIN-DEMO",
            [("1340", april.Setoff.Challan, 0L), ("1030/BA-HDFC", 0L, april.Setoff.Challan)]);
        var withChallan = full.Concat(Build([challan])).ToList();
        var after = GstMonthRules.Compute("2026-04", withChallan, []);
        Assert.Equal(0, after.Setoff.Challan);
        var journal = SourceRules.GstSetoff("gst-setoff:2026-04@2026-05-10", Date("2026-05-10"), after.Setoff, "GST set-off 2026-04");
        var jv = Assert.Single(Build([journal]));
        Assert.Equal(VoucherType.Journal, jv.Type);
        Assert.Equal([("1340", 0L, L(gst, "cashLedgerAfterSetoff")), ("2110", L(gst, "cgst"), 0L), ("2120", L(gst, "sgst"), 0L)],
            jv.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).OrderBy(x => x.Account, StringComparer.Ordinal));
        // after the set-off 2110 / 2120 / 1340 are nil
        var bal = Balances(withChallan.Concat([jv]));
        Assert.False(bal.ContainsKey("2110") || bal.ContainsKey("2120") || bal.ContainsKey("1340"));
    }

    /// <summary>The whole month through the engine: the §8.1 opening, the 19 driven steps and the 7 waiting steps, all on party ledgers.</summary>
    internal static IReadOnlyList<Voucher> FullMonthVouchers()
    {
        var sources = PartySources().Concat(Root.GetProperty("waiting").GetProperty("steps").EnumerateArray().SelectMany(s => Waiting(s.GetString()!))).ToList();
        return [PartyOpeningVoucher(), .. Build(sources)];
    }

    [Fact]
    [Trait("Lane", "acc2")]
    public void The_full_month_from_the_engine_only()
    {
        // Every one of the 26 steps through the production mappers: the trial balance at 30 April = expected.full (₹84,27,500 both sides, 1360 TDS receivable 6,000,
        // 1340 GST cash ledger 100, 2400/LN-HDFC 19,60,000 Cr, 1030/BA-HDFC 5,01,500 Dr, Morning 24,000 Cr, Kaveri 42,000 Cr), profit ₹1,40,400, six-column TB balanced.
        var vouchers = FullMonthVouchers();
        Assert.All(vouchers, v => Assert.True(v.Balanced, v.Number));
        Assert.Empty(vouchers.SelectMany(v => v.Unmapped));
        var got = Balances(vouchers);
        Assert.Equal(Expected("full", "control"), got);
        Assert.Equal(842_750_000, got.Values.Where(v => v > 0).Sum());
        Assert.False(got.ContainsKey(ChartOfAccounts.Suspense));
        var by = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in vouchers.SelectMany(v => v.Entries)) by[e.Account] = by.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        foreach (var (ledger, want) in Expected("full", "party")) Assert.True(want == by.GetValueOrDefault(ServerLedger(ledger)), $"{ledger}: {by.GetValueOrDefault(ServerLedger(ledger))} ≠ {want}");
        Assert.Equal(14_040_000, -got.Where(x => x.Key[0] is '4' or '5').Sum(x => x.Value));   // §8.4 profit ₹1,40,400
        var (from, to) = (new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var (opening, period) = AccountingReports.Split(vouchers, from, to);
        foreach (var detail in new[] { false, true })
        {
            var tb = AccountingReports.TrialBalance6(from, to, detail, opening, period, openingPending: false);
            Assert.True(tb.Balanced, "detail " + detail);
            Assert.Equal(tb.Totals.ClosingDebitPaise, tb.Totals.ClosingCreditPaise);
        }
    }
}
