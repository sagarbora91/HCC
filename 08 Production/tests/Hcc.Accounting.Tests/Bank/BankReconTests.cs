// ACC-4 · F5-A1 · bank reconciliation on the server (ACC4-CONTRACT §2, §6). The fictional statement 03 Prototype/tests/fixtures/bank-april-2026.csv
// (linked into the output as Bank/bank-april-2026.csv) against the production engine's April 2026 month: after auto-match, one manual match (three
// UPI rows ↔ one contra) and two created vouchers the difference is zero and 1010 / 1020 are zero — the same figures as stream-acc4-bank.mjs.
using System.Text.RegularExpressions;
using Hcc.Accounting.Tests.Golden;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.Bank;

public sealed class BankReconTests
{
    private const string Hdfc = "1030/BA-HDFC";
    private static readonly string Csv = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bank", "bank-april-2026.csv"));
    private static readonly DateOnly End = new(2026, 4, 30);

    private static List<BankStatementRow> Rows(BankParse p) =>
        p.Rows.Select((r, i) => new BankStatementRow($"R{i + 1:00}", "I1", Hdfc, r.Date, r.Narration, r.Ref, r.Debit, r.Credit, r.Balance)).ToList();

    [Fact]
    public void Golden_reconciles_to_zero()
    {
        var p = BankReconRules.Parse(Csv, BankReconRules.Generic);
        Assert.Equal(30_000_000, p.OpeningBalance);
        Assert.Equal(54_656_100, p.ClosingBalance);
        var rows = Rows(p);
        var month = RulebookMonthTests.FullMonthVouchers().ToList();
        var pattern = new Regex(BankReconRules.Generic.SettlementPattern!, RegexOptions.IgnoreCase);
        var auto = BankReconRules.AutoMatch(rows, BankReconRules.BookLines(month, Hdfc, End), [], _ => false, _ => pattern);
        Assert.Equal(10, auto.Proposals.Count);
        Assert.Equal(3, auto.Settlements.Count);
        var matches = auto.Proposals.Select((x, i) => new BankMatch($"M{i}", x.RowIds, x.Lines.Select(l => l.Ref).ToList(), x.Rule, x.ClearedDate, 1, DateTime.UtcNow)).ToList();
        rows = rows.Select(r => matches.Any(m => m.RowIds.Contains(r.Id)) ? r with { Status = "matched" } : r).ToList();
        // manual: three UPI settlement rows ↔ the 2,13,000 contra
        var upi = rows.Where(r => r.Narration.StartsWith("UPI SETTL", StringComparison.Ordinal)).ToList();
        var contra = BankReconRules.BookLines(month, Hdfc, End).Single(l => l.Amount == 21_300_000);
        BankReconRules.CheckManual(upi, [contra]);
        matches.Add(new BankMatch("MM", upi.Select(r => r.Id).ToList(), [contra.Ref], "manual", new DateOnly(2026, 4, 27), 1, DateTime.UtcNow));
        rows = rows.Select(r => upi.Any(u => u.Id == r.Id) ? r with { Status = "matched" } : r).ToList();
        // created: interest ₹120 (Cr 4960) and SMS charges ₹59 (Dr 5080)
        Voucher Made(string key, BankStatementRow r, string ledger) => new($"X/{key}", r.Date, r.Debit > 0 ? VoucherType.Payment : VoucherType.Receipt, r.Narration, key, "manual", "bank",
            r.Debit > 0 ? [new(Hdfc, 0, r.Debit), new(ledger, r.Debit, 0)] : [new(Hdfc, r.Credit, 0), new(ledger, 0, r.Credit)], []);
        foreach (var (row, ledger, key) in new[] { (rows.Single(r => r.Narration.StartsWith("INTEREST", StringComparison.Ordinal)), "4960", "manual:B1"), (rows.Single(r => r.Narration.StartsWith("SMS", StringComparison.Ordinal)), "5080", "manual:B2") })
        {
            month.Add(Made(key, row, ledger));
            matches.Add(new BankMatch("C" + key, [row.Id], [new BankLineRef(key, 0)], "created", row.Date, 1, DateTime.UtcNow));
            rows = rows.Select(r => r.Id == row.Id ? r with { Status = "matched" } : r).ToList();
        }

        var rec = BankReconRules.Recon(Hdfc, End, month, rows, matches, p.OpeningBalance);
        Assert.Equal(50_156_100, rec.BooksBalance);
        Assert.Equal(4_500_000, rec.IssuedTotal);
        Assert.Empty(rec.BankOnly);
        Assert.Equal(0, rec.Difference);
        Assert.Contains("Difference", BankReconRules.ReconCsv(rec), StringComparison.Ordinal);
        Assert.Equal(0, BankReconRules.Clearing(month, "1010", End).Balance);
        Assert.Equal(0, BankReconRules.Clearing(month, "1020", End).Balance);
    }

    [Fact]
    public void Refusals_balance_break_match_sum_settlement_sum()
    {
        Assert.Equal("BANK_BALANCE_BREAK", Assert.Throws<HccException>(() => BankReconRules.Parse(Csv.Replace("245000.00", "245001.00", StringComparison.Ordinal), BankReconRules.Generic)).Details!["reason"].GetString());
        var rows = Rows(BankReconRules.Parse(Csv, BankReconRules.Generic));
        var line = new BankBookLine(new BankLineRef("x", 0), "X", new DateOnly(2026, 4, 5), "x", -1);
        Assert.Equal("BANK_MATCH_SUM", Assert.Throws<HccException>(() => BankReconRules.CheckManual([rows[0]], [line])).Details!["reason"].GetString());
        Assert.Equal("BANK_SETTLEMENT_SUM", Assert.Throws<HccException>(() => BankReconRules.SettlementLines(Hdfc, 14_800_000, "1020", 15_000_000, 150_000, 27_000, false)).Details!["reason"].GetString());
        Assert.NotEqual(BankReconRules.Hash(Csv), BankReconRules.Hash(Csv + " "));
    }

    [Fact]
    public void Settlement_T14b_and_ambiguous_amounts()
    {
        var l = BankReconRules.SettlementLines(Hdfc, 14_823_000, "1020", 15_000_000, 150_000, 27_000, false);
        Assert.Equal(new VoucherEntry[] { new(Hdfc, 14_823_000, 0), new("5080", 177_000, 0), new("1020", 0, 15_000_000) }, l);
        Assert.Equal(0, BankReconRules.SettlementLines(Hdfc, 14_823_000, "1020", 15_000_000, 150_000, 27_000, true).Sum(e => e.DebitPaise - e.CreditPaise));
        var row = new BankStatementRow("R1", "I", "1030/T", new DateOnly(2026, 4, 11), "UPI CR", null, 0, 3_150_000, null);
        var book = new[] { 1, 2 }.Select(i => new BankBookLine(new BankLineRef("v" + i, 0), "V" + i, new DateOnly(2026, 4, 10), "t", 3_150_000)).ToList();
        var r = BankReconRules.AutoMatch([row], book, [], _ => false, _ => null);
        Assert.Empty(r.Proposals);
        Assert.Single(r.Ambiguous);
    }

    [Fact]
    public void Layout_single_amount_with_dr_cr_column()
    {
        var lay = new BankStatementLayout("amt", "Amount + Dr/Cr", Hdfc, ';', 1, "DD-MMM-YYYY", new BankLayoutColumns("Txn Date", "Details", Amount: "Amount", DrCrFlag: "Type"));
        var p = BankReconRules.Parse("Txn Date;Details;Amount;Type\n05-Apr-2026;EMI;\"55,000.00\";Dr\n10-Apr-2026;=HYPERLINK(x);40000;Cr\n", lay);
        Assert.Equal((5_500_000L, 0L), (p.Rows[0].Debit, p.Rows[0].Credit));
        Assert.Equal((0L, 4_000_000L), (p.Rows[1].Debit, p.Rows[1].Credit));
        Assert.StartsWith("'=", p.Rows[1].Narration, StringComparison.Ordinal);
    }
}
