using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-1 lane acc1-parties (02 Planning/agents/ACC1-POSTING-CONTRACT.md §3, §4): party sub-ledgers <c>&lt;control&gt;/&lt;party&gt;</c>, the party on
/// every rule, the no-party warning, the summary / detail trial balance and the Udhaar credit raised (rulebook A17, DEC-144). Mirrors the
/// prototype's tests/stream-accounting-parties.mjs.
/// </summary>
public sealed class PartyLedgerTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    private static List<(string, long, long)> E(Voucher v) => v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToList();

    [Fact]
    public void Party_ids_compose_only_on_control_ledgers_and_split_back()
    {
        Assert.Equal(["1000", "1030", "1110", "1115", "1120", "1200", "1210", "2200", "2400"], PartyLedgers.Controls); // 1210: ACC-2 ruling 7
        Assert.Equal("1110/CMP-7", PartyLedgers.Compose("1110", "CMP-7"));
        Assert.Equal("1010", PartyLedgers.Compose("1010", "BANK-1"));   // UPI clearing is not a control ledger
        Assert.Equal("2200", PartyLedgers.Compose("2200", null));
        Assert.True(PartyLedgers.IsParty("2200/V-MORNING"));
        Assert.False(PartyLedgers.IsParty("2200"));
        Assert.Equal(("2200", "V-MORNING"), (PartyLedgers.ControlOf("2200/V-MORNING"), PartyLedgers.PartyOf("2200/V-MORNING")));
        Assert.Null(PartyLedgers.PartyOf("4000"));
        Assert.Equal("CMP-7", PartyLedgers.Company(7));
        Assert.Equal("STF-12", PartyLedgers.Staff(12));
        Assert.Equal("Company receivables (city ledger) · CMP-7", PartyLedgers.DisplayName("1110/CMP-7"));
        Assert.Equal("Room revenue", PartyLedgers.DisplayName("4000"));
    }

    [Theory]
    [InlineData("Ramesh", "+91 98765 43210", "UD-9876543210")]
    [InlineData("  Ramesh (fictional) ", null, "UD-RAMESH-FICTIONAL")]
    [InlineData("Ramesh", "12345", "UD-RAMESH")]
    [InlineData("—", "", "UD-X")]
    public void Udhaar_customer_party_is_the_mobile_else_the_slug_of_the_name_as_the_prototype(string name, string? mobile, string expected)
        => Assert.Equal(expected, PartyLedgers.Udhaar(name, mobile));

    [Fact]
    public void Party_context_names_each_key_its_party()
    {
        var p = new PartyContext(Tender: "TILL", Company: "CMP-1", Udhaar: "UD-1", Staff: "STF-1", Vendor: "V-1", Bank: "BANK-1", Channel: "GOIBIBO");
        Assert.Equal("TILL", p.For("method:Cash"));
        Assert.Equal("TILL", p.For("funding:Cash"));
        Assert.Equal("V-1", (p with { Tender = null }).For("funding:Vendor credit"));
        Assert.Equal("CMP-1", p.For("receipt:company-credit"));
        Assert.Equal("UD-1", p.For("receipt:udhaar"));
        Assert.Equal("GOIBIBO", p.For("receipt:ota"));
        Assert.Equal("STF-1", p.For("payroll:advances"));
        Assert.Equal("STF-1", p.For("payroll:fnf-recoverable"));
        Assert.Equal("BANK-1", p.For("closing:bank-deposit"));
        Assert.Null(p.For("folio:guest"));
        Assert.Null(p.For("receipt:group-deposit"));
    }

    [Fact]
    public void Rules_post_to_party_ledgers_and_the_mapping_still_resolves_the_code_first()
    {
        var vs = VoucherBuilder.Build(D,
        [
            AccountingEventMapper.Receipt(1, 11, "payment", "Cash", D, 10_000, new PartyContext(Tender: "TILL")),
            AccountingEventMapper.Receipt(1, 12, "payment", "Bank transfer", D, 20_000, new PartyContext(Tender: "BANK-1")),
            AccountingEventMapper.Receipt(1, 13, "payment", "UPI", D, 30_000, new PartyContext(Tender: "BANK-1")),
            AccountingEventMapper.Receipt(1, 14, "company-credit", "Company credit", D, 630_000, new PartyContext(Company: "CMP-7")),
            AccountingEventMapper.CompanyPayment(new CompanyPaymentRecordedEvent(7, 81, D, "Bank transfer", 400_000, "NEFT-1", "Deccan"), new PartyContext(Tender: "BANK-1")),
            AccountingEventMapper.Expense(new ExpensePostedEvent(91, D, "Supplies", "Vendor credit", 59_000, "Morning Supplies", null), new PartyContext(Vendor: "V-MORNING")),
            AccountingEventMapper.Advance(new AdvanceGivenEvent(5, 7, 500_000, 100_000, D, "Cash"), new PartyContext(Tender: "TILL")),
            AccountingEventMapper.Banking(new CashBankingEvent(3, 1, 1, D, 100_000, "Hotel current account", "S-1"), false, new PartyContext(Tender: "TILL", Bank: "BANK-1")),
        ], Map);
        Assert.Equal([("1000/TILL", 10_000L, 0L), ("1100", 0L, 10_000L)], E(vs.Single(v => v.SourceKey == "receipt:11")));
        Assert.Equal([("1030/BANK-1", 20_000L, 0L), ("1100", 0L, 20_000L)], E(vs.Single(v => v.SourceKey == "receipt:12")));
        Assert.Equal([("1010", 30_000L, 0L), ("1100", 0L, 30_000L)], E(vs.Single(v => v.SourceKey == "receipt:13")));
        Assert.Equal([("1110/CMP-7", 630_000L, 0L), ("1100", 0L, 630_000L)], E(vs.Single(v => v.SourceKey == "receipt:14")));
        Assert.Equal([("1030/BANK-1", 400_000L, 0L), ("1110/CMP-7", 0L, 400_000L)], E(vs.Single(v => v.SourceKey == "company-payment:81")));
        Assert.Equal([("5000", 59_000L, 0L), ("2200/V-MORNING", 0L, 59_000L)], E(vs.Single(v => v.SourceKey == "expense:91")));
        Assert.Equal([("1200/STF-7", 500_000L, 0L), ("1000/TILL", 0L, 500_000L)], E(vs.Single(v => v.SourceKey == "advance:5")));
        Assert.Equal([("1030/BANK-1", 100_000L, 0L), ("1000/TILL", 0L, 100_000L)], E(vs.Single(v => v.SourceKey == "banking:3")));
        Assert.All(vs, v => Assert.True(v.Balanced, v.Number));
        // An Owner override re-points the code; the party follows the control ledger it lands on.
        var moved = VoucherBuilder.Build(D, [AccountingEventMapper.Receipt(1, 11, "payment", "Cash", D, 10_000, new PartyContext(Tender: "TILL"))],
            ChartOfAccounts.Effective(new Dictionary<string, string> { ["method:Cash"] = "1010" })).Single();
        Assert.Equal("1010", moved.Entries[0].Account);
        Assert.DoesNotContain(VoucherBuilder.Exceptions(vs, vs, false, DayStatus.Closed), x => x.Kind == "no-party");
    }

    [Fact]
    public void Full_and_final_names_the_leaver_and_a_pay_run_without_a_staff_split_stays_on_the_control_with_a_warning()
    {
        var fnf = AccountingEventMapper.Fnf(new FnfSettledEvent(9, 12, "2026-10", 1_000_000, 300_000, 700_000, 100_000), D, new PartyContext(Tender: "BANK-1"));
        var vs = VoucherBuilder.Build(D, fnf, Map);
        Assert.Contains(vs.SelectMany(v => v.Entries), e => e.Account == "1200/STF-12" && e.CreditPaise == 100_000);
        Assert.Contains(vs.SelectMany(v => v.Entries), e => e.Account == "1030/BANK-1" && e.CreditPaise == 700_000);
        var run = VoucherBuilder.Build(D, [AccountingEventMapper.PayRun(new PayrollPostedEvent(7, "2026-10", D, 3_000_000, 180_000, 97_500, 180_000, 22_500, 20_000, 100_000, 0, 2_677_500))], Map);
        Assert.Contains(run.Single().Entries, e => e.Account == "1200" && e.CreditPaise == 100_000);
        var w = VoucherBuilder.Exceptions(run, run, false, DayStatus.Closed).Single(x => x.Kind == "no-party");
        Assert.Equal(("warning", "noparty:" + run[0].Number + ":1200"), (w.Severity, w.Id));
        Assert.DoesNotContain(VoucherBuilder.Exceptions(run, run, true, DayStatus.Locked), x => x.Kind == "no-party");   // not on a locked snapshot
    }

    [Theory]
    [InlineData(105_000, 500, 100_000, 2_500, 2_500)]
    [InlineData(100, 500, 95, 3, 2)]
    [InlineData(21, 500, 20, 1, 0)]
    [InlineData(118, 1800, 100, 9, 9)]
    [InlineData(5_000, 0, 5_000, 0, 0)]
    [InlineData(-105_000, 500, -100_000, -2_500, -2_500)]
    public void Udhaar_gst_is_inside_the_gross_rounded_half_away_from_zero_as_the_prototype(long gross, int bps, long taxable, long cgst, long sgst)
    {
        Assert.Equal((taxable, cgst, sgst), SourceRules.UdhaarGst(gross, bps));
        Assert.Equal(gross, taxable + cgst + sgst);
    }

    [Fact]
    public void Udhaar_credit_raised_is_a_sales_voucher_on_the_customer_ledger_by_kind()
    {
        Assert.Contains(AccountingEvents.UdhaarCreditRaised, AccountingEvents.Consumed);
        var vs = VoucherBuilder.Build(D,
        [
            AccountingEventMapper.UdhaarCredit(new UdhaarCreditRaisedEvent(31, D, "Suresh Patil (fictional)", "9876543210", 105_000, "restaurant", 500)),
            AccountingEventMapper.UdhaarCredit(new UdhaarCreditRaisedEvent(32, D, "Room guest (fictional)", null, 210_000, "room", 500)),
            AccountingEventMapper.UdhaarCredit(new UdhaarCreditRaisedEvent(33, D, "Old style (fictional)", null, 12_345)),
        ], Map);
        Assert.All(vs, v => Assert.Equal((VoucherType.Sales, "receipt", "udhaar-credit", true), (v.Type, v.SourceKind, v.Sub, v.Balanced)));
        Assert.Equal([("1120/UD-9876543210", 105_000L, 0L), (Map["pos:sales"], 0L, 100_000L), ("2110", 0L, 2_500L), ("2120", 0L, 2_500L)], E(vs.Single(v => v.SourceKey == "udhaar:31")));
        Assert.Equal([("1120/UD-ROOM-GUEST-FICTIONAL", 210_000L, 0L), ("4000", 0L, 200_000L), ("2110", 0L, 5_000L), ("2120", 0L, 5_000L)], E(vs.Single(v => v.SourceKey == "udhaar:32")));
        Assert.Equal([("1120/UD-OLD-STYLE-FICTIONAL", 12_345L, 0L), ("4100", 0L, 12_345L)], E(vs.Single(v => v.SourceKey == "udhaar:33")));
        Assert.Equal("Udhaar credit · Suresh Patil (fictional) · restaurant · GST 5 % · credit UD-31", vs.Single(v => v.SourceKey == "udhaar:31").Narration);
        // The payment credits the same party ledger: raised and paid in full nets the customer to zero.
        var pay = AccountingEventMapper.UdhaarPayment(new UdhaarPaymentRecordedEvent(31, 41, D, "UPI", 105_000, "Suresh Patil (fictional)", "UPI-1"),
            new PartyContext(Tender: "BANK-1", Udhaar: PartyLedgers.Udhaar("Suresh Patil (fictional)", "9876543210")));
        var all = VoucherBuilder.Build(D, [AccountingEventMapper.UdhaarCredit(new UdhaarCreditRaisedEvent(31, D, "Suresh Patil (fictional)", "9876543210", 105_000, "restaurant", 500)), pay], Map);
        Assert.Equal(0, VoucherBuilder.TrialBalance(all, detail: true).Single(r => r.Account == "1120/UD-9876543210").BalancePaise);
    }

    [Fact]
    public void Trial_balance_summary_sums_parties_into_controls_and_detail_lists_them_under_the_control()
    {
        var vs = VoucherBuilder.Build(D,
        [
            AccountingEventMapper.Receipt(1, 14, "company-credit", "Company credit", D, 630_000, new PartyContext(Company: "CMP-7")),
            AccountingEventMapper.Receipt(2, 15, "company-credit", "Company credit", D, 100_000, new PartyContext(Company: "CMP-8")),
            AccountingEventMapper.Receipt(3, 16, "company-credit", "Company credit", D, 50_000),   // published before ACC-1: no party
        ], Map);
        var sum = VoucherBuilder.TrialBalance(vs);
        Assert.Equal(780_000, sum.Single(r => r.Account == "1110").DebitPaise);
        Assert.DoesNotContain(sum, r => PartyLedgers.IsParty(r.Account) || r.Control is not null);
        var det = VoucherBuilder.TrialBalance(vs, detail: true);
        Assert.Equal(["1100", "1110", "1110/CMP-7", "1110/CMP-8"], det.Select(r => r.Account).Order(StringComparer.Ordinal));
        var order = det.Select(r => r.Account).ToList();
        Assert.True(order.IndexOf("1100") < order.IndexOf("1110") && order.IndexOf("1110") < order.IndexOf("1110/CMP-7") && order.IndexOf("1110/CMP-7") < order.IndexOf("1110/CMP-8"));
        var p = det.Single(r => r.Account == "1110/CMP-7");
        Assert.Equal(("1110", "Company receivables (city ledger) · CMP-7", AccountGroup.Asset, 630_000L), (p.Control, p.Name, p.Group, p.DebitPaise));
        Assert.Equal(sum.Sum(r => r.BalancePaise), det.Sum(r => r.BalancePaise));
        Assert.Single(VoucherBuilder.Exceptions(vs, vs, false, DayStatus.Closed), x => x.Kind == "no-party");
        // Tally carries the control name; the CSV names the party ledger.
        var xml = VoucherExport.TallyXml(vs, "HCC Demo Residency", "DEMO");
        Assert.DoesNotContain("CMP-7", xml, StringComparison.Ordinal);
        Assert.Contains("Company receivables (city ledger) · CMP-7", VoucherExport.Csv(vs), StringComparison.Ordinal);
    }
}
