using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Golden;

/// <summary>
/// ACC-1 (lane acc1-tests): the rulebook golden month, ACC-1 subset — 02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional.
/// <c>rulebook-month.json</c> (written by <c>build-rulebook-month.mjs</c> from 03 Prototype/tests/_rulebook-month.mjs, the same data the prototype's
/// tests/stream-accounting-rulebook.mjs uses) holds the opening balances (§8.1), the 26 entries of §8.2, the server events for the steps ACC-1
/// books (T1 T2 T3 T5a T5b T5c T7a T8 T9 T10 T12 T13a T13b T13c T15a T15b), and the expectation worked out from the rulebook: the closing trial
/// balance by control ledger and by party. Events go through the production <see cref="AccountingEventMapper"/> and <see cref="VoucherBuilder"/>.
/// Not driven: T16 (an Owner drawing from the BANK is rulebook B6, a manual voucher — ACC-2) and T4 T6 T7b T11 T14 T17 T18 T19 (ACC-2).
/// Tests marked <c>Lane</c> need that lane merged into claude/acc1 (the chart's 4200 F&amp;B revenue ledger, party sub-ledgers, the opening endpoint,
/// the statements); they fail with a plain reason until then. Money = integer paise.
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
        // the brief's subset (driven steps + T16) and the whole month are in the file for the later waves
        Assert.Equal(all.Keys, Root.GetProperty("expected").GetProperty("fullMonthControl").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(16, Root.GetProperty("driven").GetArrayLength());
    }

    [Fact]
    public void Every_driven_step_has_events_and_the_events_cover_the_driven_steps_exactly()
    {
        var driven = Root.GetProperty("driven").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(driven, Sources().Select(x => x.Step).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain("T16", driven);
        Assert.All(Sources(), x => Assert.True(x.Source.Debit == x.Source.Credit && x.Source.Debit > 0, x.Source.SourceKey));
        Assert.Equal(Sources().Count, Sources().Select(x => x.Source.SourceKey).Distinct().Count());   // one stable key per operational record
    }

    // ───────── the events, through the production mapper and VoucherBuilder ─────────

    [Fact]
    [Trait("Lane", "acc1-chart")]
    public void Each_driven_step_posts_the_rulebook_entries_on_the_control_ledgers()
    {
        var mapping = ChartOfAccounts.Effective(MappingOverrides());
        var problems = new List<string>();
        foreach (var step in Root.GetProperty("driven").EnumerateArray().Select(x => x.GetString()!))
        {
            var mine = Sources().Where(x => x.Step == step).Select(x => x.Source).ToList();
            var got = Balances(mine.SelectMany(s => VoucherBuilder.Build(s.BusinessDate, [s], mapping)));
            var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var x in Root.GetProperty("steps").GetProperty(step).EnumerateArray())
                want[S(x, "account")] = want.GetValueOrDefault(S(x, "account")) + L(x, "debitPaise") - L(x, "creditPaise");
            foreach (var k in want.Where(x => x.Value == 0).Select(x => x.Key).ToList()) want.Remove(k);
            if (!got.SequenceEqual(want)) problems.Add($"{step}: engine {string.Join(" ", got.Select(x => x.Key + "=" + x.Value))} ≠ rulebook {string.Join(" ", want.Select(x => x.Key + "=" + x.Value))}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + (problems.Any(p => p.Contains("9999", StringComparison.Ordinal)) ? Environment.NewLine + "(9999: F&B revenue 4200 is not in the server chart yet — waits for lane acc1-chart)" : ""));
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

        Assert.Equal([4_000_000L, 10_300_000L, 6_300_000L], trace);   // 40,000 → 1,03,000 (T5b) → 63,000 (T7a)
    }

    // ───────── lanes that are not merged yet: written down, skipped with the reason ─────────

    [Fact(Skip = "Waits for lane acc1-parties (contract §3): SourceEntry.Party / VoucherEntry.Account = <control>/<party>. Expected party balances are in rulebook-month.json " +
                 "(expected.closing.party): 1110/<Deccan> 63,000 Dr, 2200/EV-MORNING 84,000 Cr, 2200/EV-KAVERI 42,000 Cr, 1030/BA-HDFC 4,45,000 Dr (before T13c) — fill in when the party resolver is merged.")]
    [Trait("Lane", "acc1-parties")]
    public void Party_ledgers_hold_the_party_balances()
    {
        // Intended: build the vouchers with the party-aware VoucherBuilder (parties resolved from the month's masters), sum entries by full ledger id,
        // and compare with Expected("closing", "party"); a control ledger keeps no balance of its own (no 'no-party' warning on any day).
        Assert.NotEmpty(Expected("closing", "party"));
    }

    [Fact(Skip = "Waits for lane acc1-opening (contract §5): the opening endpoint (SaveOpeningBalances / ApproveOpeningBalances). Intended: save the 11 opening lines of rulebook-month.json " +
                 "as the Manager, approve as the Owner, then the cumulative trial balance at 30 April equals expected.closing (opening included); pending/draft opening refuses the first lock.")]
    [Trait("Lane", "acc1-opening")]
    public void Opening_entry_through_the_endpoint_is_in_the_cumulative_trial_balance_and_gates_the_first_lock()
    {
        Assert.Equal(11, Root.GetProperty("opening").GetArrayLength());
    }

    [Fact(Skip = "Waits for lane acc1-statements (contract §6): GET /accounting/trial-balance (six columns, detail), /statement, /day-book. Intended: six-column TB 1–30 April balanced in " +
                 "all three column pairs; statement of 1110/<Deccan> from 2 April: opening 40,000 Dr, T5b +63,000 → 1,03,000, T7a −40,000 → 63,000 Dr.")]
    [Trait("Lane", "acc1-statements")]
    public void Six_column_trial_balance_and_the_party_statement_follow_the_month()
    {
        Assert.NotEmpty(Expected("closing", "control"));
    }
}
