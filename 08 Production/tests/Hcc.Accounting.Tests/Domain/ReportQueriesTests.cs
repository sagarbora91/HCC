using Hcc.Application.Accounting;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-1 acc1-statements (contract §6; rulebook §7.1–7.3, §G2): the pure report builders behind <see cref="AccountingReportQueries"/> —
/// statement running balance, opening before <c>from</c> (an opening voucher dated on <c>from</c> included), six-column trial balance
/// summary vs party detail, day book order, "opening balances pending". Same fixtures as tests/stream-accounting-reports.mjs.
/// </summary>
public sealed class ReportQueriesTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);
    private static readonly DateOnly Oct24 = new(2026, 10, 24);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);

    private static Voucher V(string no, DateOnly date, VoucherType type, string sub, params (string Account, long D, long C)[] entries)
        => new(no, date, type, "Fictional " + no, "T/" + no, sub == "opening-entry" ? "opening" : "test", sub, entries.Select(e => new VoucherEntry(e.Account, e.D, e.C)).ToList(), []);

    /// <summary>Two companies under control 1110, a no-party entry on 1110 itself (1 Oct), then a receipt from company A (24 Oct).</summary>
    private static List<Voucher> Parties() =>
    [
        V("SAL/20261001/001", Oct1, VoucherType.Sales, "test", ("1110/CMP-A", 1_000_000, 0), ("4000", 0, 1_000_000)),
        V("RCT/20261001/001", Oct1, VoucherType.Receipt, "test", ("1000", 400_000, 0), ("1110/CMP-B", 0, 400_000)),
        V("JRN/20261001/001", Oct1, VoucherType.Journal, "test", ("1110", 250_000, 0), ("4100", 0, 250_000)),
        V("RCT/20261024/001", Oct24, VoucherType.Receipt, "test", ("1030", 300_000, 0), ("1110/CMP-A", 0, 300_000)),
    ];

    [Fact]
    public void Statement_opening_is_every_voucher_before_from_rows_carry_a_running_balance_and_the_counter_ledger()
    {
        var (opening, period) = AccountingReports.Split(Parties(), Oct2, Oct24);
        Assert.Equal(3, opening.Count);
        var a = AccountingReports.Statement("1110/CMP-A", Oct2, Oct24, opening, period);
        Assert.Equal(1_000_000, a.OpeningPaise);
        var row = Assert.Single(a.Rows);
        Assert.Equal(("RCT/20261024/001", ChartOfAccounts.NameOf("1030"), 0L, 300_000L, 700_000L), (row.No, row.Counter, row.DebitPaise, row.CreditPaise, row.RunningPaise));
        Assert.Equal(700_000, a.ClosingPaise);
        Assert.Equal($"{ChartOfAccounts.NameOf("1110")} · CMP-A", a.Name);

        // a control takes its parties and its own postings; running balance in date, number order; Cr shows negative (signed)
        var (o2, p2) = AccountingReports.Split(Parties(), Oct1, Oct24);
        var c = AccountingReports.Statement("1110", Oct1, Oct24, o2, p2);
        Assert.Equal(0, c.OpeningPaise);
        Assert.Equal(["JRN/20261001/001", "RCT/20261001/001", "SAL/20261001/001", "RCT/20261024/001"], c.Rows.Select(r => r.No));
        Assert.Equal([250_000L, -150_000L, 850_000L, 550_000L], c.Rows.Select(r => r.RunningPaise));
        Assert.Equal((1_250_000L, 700_000L, 550_000L), (c.DebitPaise, c.CreditPaise, c.ClosingPaise));
        Assert.Equal(-400_000, AccountingReports.Statement("1110/CMP-B", Oct1, Oct24, o2, p2).ClosingPaise);
        Assert.All(c.Rows, r => Assert.True(r.DebitPaise >= 0 && r.CreditPaise >= 0));
    }

    [Fact]
    public void Six_column_trial_balance_summary_sums_parties_into_the_control_detail_lists_them_both_balanced()
    {
        var (o, p) = AccountingReports.Split(Parties(), Oct1, Oct1);
        var sum = AccountingReports.TrialBalance6(Oct1, Oct1, false, o, p);
        var det = AccountingReports.TrialBalance6(Oct1, Oct1, true, o, p);
        Assert.True(sum.Balanced);
        Assert.True(det.Balanced);
        var ctl = Assert.Single(sum.Rows, r => r.Account == "1110");
        Assert.Equal((1_250_000L, 400_000L, 850_000L, 0L, (string?)null), (ctl.DebitPaise, ctl.CreditPaise, ctl.ClosingDebitPaise, ctl.ClosingCreditPaise, ctl.Control));
        Assert.DoesNotContain(sum.Rows, r => r.Account.Contains('/', StringComparison.Ordinal));
        var rows = det.Rows.Where(r => r.Account == "1110" || r.Control == "1110").ToList();
        Assert.Equal(["1110", "1110/CMP-A", "1110/CMP-B"], rows.Select(r => r.Account));
        Assert.Equal([(250_000L, 0L), (1_000_000L, 0L), (0L, 400_000L)], rows.Select(r => (r.ClosingDebitPaise, r.ClosingCreditPaise)));
        Assert.All(rows, r => Assert.Equal(ChartOfAccounts.Find("1110")!.Group.ToString(), r.Group));
        Assert.Equal(850_000, rows.Sum(r => r.ClosingDebitPaise - r.ClosingCreditPaise));
        Assert.Equal((sum.Totals.DebitPaise, sum.Totals.CreditPaise), (det.Totals.DebitPaise, det.Totals.CreditPaise));

        // later window: the month before is the opening column, closing = opening + period, never Dr and Cr on one row
        var (o2, p2) = AccountingReports.Split(Parties(), Oct2, Oct24);
        var tb = AccountingReports.TrialBalance6(Oct2, Oct24, false, o2, p2);
        Assert.True(tb.Balanced);
        var r1110 = Assert.Single(tb.Rows, r => r.Account == "1110");
        Assert.Equal((850_000L, 0L, 300_000L, 550_000L), (r1110.OpeningDebitPaise, r1110.DebitPaise, r1110.CreditPaise, r1110.ClosingDebitPaise));
        Assert.All(tb.Rows, r =>
        {
            Assert.False(r.OpeningDebitPaise > 0 && r.OpeningCreditPaise > 0);
            Assert.False(r.ClosingDebitPaise > 0 && r.ClosingCreditPaise > 0);
            Assert.Equal(r.OpeningDebitPaise - r.OpeningCreditPaise + r.DebitPaise - r.CreditPaise, r.ClosingDebitPaise - r.ClosingCreditPaise);
        });
        Assert.Equal(["1000", "1030", "1110", "4000", "4100"], tb.Rows.Select(r => r.Account)); // chart order
        Assert.Equal(tb.Totals.OpeningDebitPaise, tb.Totals.OpeningCreditPaise);
    }

    [Fact]
    public void An_opening_voucher_dated_on_from_is_the_opening_not_a_movement_and_the_imported_opening_keeps_unmapped_ledgers()
    {
        var entry = V("JRN/20260401/000", Apr1, VoucherType.Journal, "opening-entry", ("1000", 500_000, 0), ("1030/BA-HDFC", 2_000_000, 0), ("3100", 0, 2_500_000));
        var (o, p) = AccountingReports.Split([entry, .. Parties()], Apr1, Oct24);
        Assert.Contains(entry, o);
        var tb = AccountingReports.TrialBalance6(Apr1, Oct24, false, o, p);
        Assert.Equal(2_500_000, tb.Totals.OpeningDebitPaise);
        Assert.Equal(2_000_000, Assert.Single(tb.Rows, r => r.Account == "1030").OpeningDebitPaise);
        Assert.DoesNotContain(AccountingReports.DayBook(Apr1, Oct24, p).Rows, r => r.No == "JRN/20260401/000");
        var (_, earlier) = AccountingReports.Split([entry], Apr1.AddDays(-1), Oct24);
        Assert.Contains(AccountingReports.DayBook(Apr1.AddDays(-1), Oct24, earlier).Rows, r => r.No == "JRN/20260401/000");

        var extra = new Dictionary<string, (string Name, string Group)>(StringComparer.Ordinal);
        var imported = AccountingReports.ImportedVoucher(new OpeningBalancesView(Apr1, 1, 900_000, 900_000, true,
        [
            new OpeningBalanceRowView("CASH", "Cash", "Asset", "1000", 900_000, 0),
            new OpeningBalanceRowView("CAP", "Proprietor's capital", "Equity", null, 0, 900_000),
        ]), extra)!;
        Assert.Equal(("opening-import", Apr1), (imported.Sub, imported.Date));
        Assert.Equal(["1000", "CAP"], imported.Entries.Select(e => e.Account));
        var names = new LedgerNames(null, extra);
        var (io, ip) = AccountingReports.Split([imported], Apr1, Oct24);
        var itb = AccountingReports.TrialBalance6(Apr1, Oct24, false, io, ip, names);
        var cap = Assert.Single(itb.Rows, r => r.Account == "CAP");
        Assert.Equal(("Proprietor's capital", "Equity", 900_000L), (cap.Name, cap.Group, cap.OpeningCreditPaise));
        Assert.Null(AccountingReports.ImportedVoucher(new OpeningBalancesView(null, null, 0, 0, true, []), extra));
    }

    [Fact]
    public void Opening_balances_are_pending_until_an_approved_entry_with_nothing_on_1400()
    {
        var balanced = V("JRN/20260401/000", Apr1, VoucherType.Journal, "opening-entry", ("1000", 500_000, 0), ("3100", 0, 500_000));
        var gap = V("JRN/20260401/000", Apr1, VoucherType.Journal, "opening-entry", ("1000", 501_000, 0), ("3100", 0, 500_000), ("1400", 0, 1_000));
        Assert.True(AccountingReports.OpeningPending(null));
        Assert.True(AccountingReports.OpeningPending(new ReportOpening(null, true)));
        Assert.True(AccountingReports.OpeningPending(new ReportOpening(balanced, false)));
        Assert.False(AccountingReports.OpeningPending(new ReportOpening(balanced, true)));
        Assert.True(AccountingReports.OpeningPending(new ReportOpening(gap, true)));
        Assert.True(AccountingReports.TrialBalance6(Apr1, Oct24, false, [], []).OpeningPending);
    }

    [Fact]
    public void Day_book_lists_every_entry_in_date_and_number_order_with_ledger_names_and_balances()
    {
        var shuffled = Parties().AsEnumerable().Reverse().ToList();
        var (_, p) = AccountingReports.Split(shuffled, Oct1, Oct24);
        var db = AccountingReports.DayBook(Oct1, Oct24, p);
        Assert.Equal(8, db.Rows.Count);
        Assert.Equal(4, db.Vouchers);
        Assert.True(db.Balanced);
        Assert.Equal(1_950_000, db.DebitPaise);
        Assert.Equal(["JRN/20261001/001", "RCT/20261001/001", "SAL/20261001/001", "RCT/20261024/001"], db.Rows.Select(r => r.No).Distinct());
        Assert.Equal(("1110/CMP-A", $"{ChartOfAccounts.NameOf("1110")} · CMP-A", "T/SAL/20261001/001"),
            (db.Rows[4].Account, db.Rows[4].Ledger, db.Rows[4].Source));
        var named = AccountingReports.DayBook(Oct1, Oct24, p, new LedgerNames(new Dictionary<string, string> { ["1110/CMP-A"] = "Deccan Agro Ltd" }));
        Assert.Equal($"{ChartOfAccounts.NameOf("1110")} · Deccan Agro Ltd", named.Rows[4].Ledger);
        Assert.Empty(AccountingReports.DayBook(Oct2, Oct2, AccountingReports.Split(Parties(), Oct2, Oct2).Period).Rows);
    }

    [Fact]
    public void Party_ids_split_on_the_first_slash_and_a_party_never_matches_its_control_statement_the_other_way()
    {
        Assert.Equal(("1110", "CMP-0007"), (AccountingReports.ControlOf("1110/CMP-0007"), AccountingReports.PartyOf("1110/CMP-0007")));
        Assert.Equal(("1100", (string?)null), (AccountingReports.ControlOf("1100"), AccountingReports.PartyOf("1100")));
        Assert.True(AccountingReports.Matches("1110", "1110/CMP-A"));
        Assert.True(AccountingReports.Matches("1110", "1110"));
        Assert.False(AccountingReports.Matches("1110/CMP-A", "1110"));
        Assert.False(AccountingReports.Matches("1110/CMP-A", "1110/CMP-B"));
        Assert.False(AccountingReports.Matches("111", "1110"));
    }
}
