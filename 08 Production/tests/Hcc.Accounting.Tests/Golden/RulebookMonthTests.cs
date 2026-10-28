using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Golden;

/// <summary>
/// ACC-1 (lane acc1-tests) + ACC-2 (lane acc2-tests): the rulebook golden month, the WHOLE of §8.2 — 02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional.
/// <c>rulebook-month.json</c> (written by <c>build-rulebook-month.mjs</c> from 03 Prototype/tests/_rulebook-month.mjs, the same data the prototype's
/// tests/stream-accounting-rulebook.mjs uses) holds the opening balances (§8.1), the 26 entries of §8.2, the server events for the 19 steps that have events
/// (T1 T2 T3 T4 T5a T5b T5c T6 T7a T7b T8 T9 T10 T12 T13a T13b T13c T15a T15b — ACC-2 adds receipt kinds <c>ota-credit</c> / <c>deposit-apply</c> / <c>deposit</c>, a TDS
/// amount on a company payment, charge kind <c>food</c> and per-staff advance recovery), and the expectation worked out from the rulebook: the closing trial balance
/// by control ledger and by party. Events go through the production <see cref="AccountingEventMapper"/> and <see cref="VoucherBuilder"/>.
/// The 7 steps with no server command / event yet — T11 vendor payment (acc2-purchase), T14a T14b T16 T17 T18 manual vouchers (acc2-manual), T19 OTA payout (acc2-flows) —
/// have their intended payloads in the json (<c>waiting.events</c>) and a skipped test each with the reason. Tests fail today where an ACC-2 lane has not landed
/// (marked <c>Lane</c>); they pass once <c>claude/acc2</c> is merged. Money = integer paise.
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
        Assert.Equal("JRN/20260405/001", by[Date("2026-04-05")].Single(v => v.SourceKey == "expense:501").Number);
        Assert.Equal(VoucherType.Receipt, by[Date("2026-04-10")].Single(v => v.Sub == "company-payment").Type);
        Assert.Equal(VoucherType.Journal, by[Date("2026-04-12")].Single(v => v.SourceKey == "expense:502").Type);
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
                    new PartyContext(Tender: Tender(S(p, "tender")), Company: S(p, "kind") == "company-credit" ? PartyLedgers.Company(7) : null)),
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
        // 1115/Goibibo needs the OTA party on the receipt (acc2-flows adds it to PartyContext): its own skipped test below.
        foreach (var (ledger, want) in Expected("closing", "party").Where(x => !x.Key.StartsWith("1115/", StringComparison.Ordinal)))
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

    [Fact(Skip = "waits for lane acc2-flows: the OTA party on a receipt (PartyContext gets the channel) so T4's ota-credit posts 1115/<channel> — intended: 1115/CHN-GOIBIBO 21,000 Dr after T4 (2,100,000 paise), nil after T19")]
    [Trait("Lane", "acc2-flows")]
    public void Ota_receivable_is_a_party_ledger_per_channel()
    {
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

    [Fact(Skip = "waits for lane acc2-purchase: VendorPaymentRecorded → SourceRules.VendorPayment (exp.vendor_payment + _alloc, migrations 4230–4237), source kind 'vendor'. Intended (rulebook T11, 18 Apr): Payment Dr 2200/EV-MORNING 60,00,000 paise · Cr 1030/BA-HDFC; allocation oldest first = the 25,000 opening then 35,000 of bill 501; 2200/EV-MORNING closes 2,400,000 Cr; ageing 0–30: Morning 24,000, Kaveri 42,000")]
    [Trait("Lane", "acc2-purchase")]
    public void T11_vendor_payment_allocated_oldest_first()
    {
    }

    [Fact(Skip = "waits for lane acc2-manual: ManualVoucherApproved → SourceRules.Manual (acc.manual_voucher / acc.manual_line, migrations 4238–4241). Intended: T14a Contra (CTR/20260427/001, sub contra) Dr 1030/BA-HDFC 21,300,000 · Cr 1010; T14b Journal Dr 1030/BA-HDFC 14,823,000 · Dr 5080 177,000 · Cr 1020 15,000,000 (the MDR line is not a cash / bank ledger, so not a Contra); T16 Payment (owner-drawing-bank) Dr 3000 5,000,000 · Cr 1030/BA-HDFC; T17 Payment (loan-emi) Dr 2400/LN-HDFC 4,000,000 · Dr 5090 1,500,000 · Cr 1030/BA-HDFC 5,500,000; T18 Payment (bank-charges) Dr 5080 59,000 · Cr 1030/BA-HDFC; a pending Accountant voucher posts nothing until the Owner approves; a locked day takes a reversing voucher")]
    [Trait("Lane", "acc2-manual")]
    public void T14_T16_T17_T18_manual_vouchers_post_their_template_entries()
    {
    }

    [Fact(Skip = "waits for lane acc2-flows: OtaPayoutRecorded → SourceRules.OtaPayout (ota.payout, migrations 4250–4254), source kind 'ota'. Intended (rulebook T19, 29 Apr): Receipt Dr 1030/BA-HDFC 1,736,000 · Dr 5070 354,000 · Dr 1340 10,000 · Cr 1115/CHN-GOIBIBO 2,100,000 (gross = net + commission + GST + TCS); refused when it does not add up or exceeds the open receivable")]
    [Trait("Lane", "acc2-flows")]
    public void T19_ota_payout_from_the_statement()
    {
    }

    [Fact(Skip = "waits for lanes acc2-gst + acc2-manual: GstSetOff (six hand-worked cases incl. the §8.6 month), acc.gst_setoff / acc.month_lock (migrations 4246–4249), template gst-payment. Intended (rulebook §8.6, in May): set-off Journal Dr 2110 1,450,000 · Dr 2120 1,450,000 · Cr 1340 2,900,000, then Dr 1340 2,890,000 · Cr 1030/BA-HDFC 2,890,000 (challan ₹28,900); month lock refused until the set-off is posted and no manual voucher is pending")]
    [Trait("Lane", "acc2-gst")]
    public void GST_set_off_and_the_month_lock_for_April()
    {
    }

    [Fact(Skip = "waits for the lanes above: the full month from the engine only — every one of the 26 steps through events / commands, cumulative trial balance at 30 April = expected.full (₹84,27,500 both sides, 1360 TDS receivable 6,000, 1340 GST cash ledger 100, 2400/LN-HDFC 19,60,000 Cr, 1030/BA-HDFC 5,01,500 Dr, Morning 24,000 Cr, Kaveri 42,000 Cr), profit ₹1,40,400, six-column TB balanced. Replaces the stand-in test above once T11 T14 T16-T19 exist")]
    [Trait("Lane", "acc2")]
    public void The_full_month_from_the_engine_only()
    {
    }
}
