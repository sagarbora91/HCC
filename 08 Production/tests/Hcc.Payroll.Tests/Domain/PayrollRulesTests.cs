using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Domain;

/// <summary>Statutory and attendance rules at their thresholds — the prototype's [PAY-SCC] / [PAY-FIX] figures (tests/stream-payroll-*.mjs).</summary>
public sealed class PayrollRulesTests
{
    private static readonly PayrollRates R = PayrollRates.Defaults();

    private static AttendanceCounts Full(string ym = "2026-09", int days = 30, decimal? p = null, decimal l = 0, decimal h = 0, decimal a = 0, decimal unmarked = 0,
        decimal lop = 0, int ot = 0) => new(ym, days, p ?? days, l, h, 0, a, 0, 0, 0, lop, unmarked, 0, ot);

    private static StaffProfile Mon(long monthly, PayStructure? structure = null, bool? pf = null, bool? esic = null, Gender? g = null) =>
        new(1, "Test Person", "FO", "x", PayType.Monthly, monthly, 0, ShiftCode.M, 2, new DateOnly(2025, 4, 1), null, true, g, structure, pf, esic);

    [Fact]
    public void Default_rate_version_is_SCC_dated_1_Apr_2025_and_marked_illustrative()
    {
        Assert.Equal((new DateOnly(2025, 4, 1), 5000, 8000, 1000, 1200, 1300, 1_500_000L, 75, 325, 2_100_000L), (R.From, R.BasicBps, R.HraBps, R.WashBps, R.PfEeBps, R.PfErBps, R.PfWageCap, R.EsicEeBps, R.EsicErBps, R.EsicCeiling));
        Assert.Equal((750_000L, 1_000_000L, 17_500L, 20_000L, 30_000L, 2_500_000L, 10_000, 3, StatutoryRounding.Paise, true),
            (R.PtMaleExempt, R.PtMaleMid, R.PtMaleMidAmt, R.PtStd, R.PtFeb, R.PtFemaleExempt, R.OtBps, R.LateBlock, R.Rounding, R.EsicPeriod));
        Assert.Contains("CA to confirm for a hotel", PayrollRates.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void PF_is_12_and_13_percent_of_basic_capped_at_15000()
    {
        var at = PayrollRules.ComputeLine(Mon(3_000_000), Full(), R);
        Assert.Equal((1_500_000L, 1_500_000L, 180_000L, 195_000L), (at.Basic, at.PfWages, at.Pf, at.PfEr));
        var over = PayrollRules.ComputeLine(Mon(4_000_000), Full(), R);
        Assert.Equal((2_000_000L, 1_500_000L, 180_000L, 195_000L), (over.Basic, over.PfWages, over.Pf, over.PfEr));
        var under = PayrollRules.ComputeLine(Mon(2_000_000), Full(), R);
        Assert.Equal((1_000_000L, 120_000L, 130_000L), (under.PfWages, under.Pf, under.PfEr));
        var off = PayrollRules.ComputeLine(Mon(2_000_000, pf: false), Full(), R);
        Assert.Equal(0, off.Pf + off.PfEr + off.PfWages);
        Assert.Equal(0, PayrollRules.ComputeLine(Mon(1_200_000, PayStructure.B), Full(), R).Pf);   // SCC: PF only for structured pay by default
        Assert.Equal(144_000, PayrollRules.ComputeLine(Mon(1_200_000, PayStructure.B, pf: true), Full(), R).Pf);
    }

    [Fact]
    public void ESIC_is_075_and_325_percent_of_earned_less_washing_with_half_up_paise()
    {
        var at = PayrollRules.ComputeLine(Mon(2_100_000), Full(), R);
        Assert.Equal((1_050_000L, 105_000L, 1_995_000L, 14_963L, 64_838L), (at.Basic, at.Washing, at.EsicWages, at.Esic, at.EsicEr));   // 14,962.5 → 14,963
        var above = PayrollRules.ComputeLine(Mon(2_100_001), Full(), R);
        Assert.Equal((0L, 0L, 0L), (above.Esic, above.EsicEr, above.EsicWages));
        Assert.Equal(0, PayrollRules.ComputeLine(Mon(1_500_000, esic: false), Full(), R).Esic);
        // SCC month-by-month (rule off): ₹25,000 with 6 days LOP earns ₹20,000 → covered that month.
        var lop = PayrollRules.ComputeLine(Mon(2_500_000), Full(p: 24, a: 6), R with { EsicPeriod = false });
        Assert.Equal(2_000_000, lop.Earned);
        Assert.True(lop.Esic > 0);
    }

    [Fact]
    public void F07_contribution_period_keeps_22000_with_2_LOP_days_uncovered_and_a_covered_staff_covered_to_period_end()
    {
        var lop = PayrollRules.ComputeLine(Mon(2_200_000), Full(p: 28, a: 2), R);
        Assert.Equal((2_053_333L, 0L, 0L, (bool?)false), (lop.Earned, lop.Esic, lop.EsicEr, lop.EsicCovered));
        var scc = PayrollRules.ComputeLine(Mon(2_200_000), Full(p: 28, a: 2), R with { EsicPeriod = false });
        Assert.Equal((14_630L, 63_397L), (scc.Esic, scc.EsicEr));
        // Covered at the start of Oct–Mar (October locked as covered), raised to ₹23,000 in December: covered to March, not from April.
        var raised = Mon(2_300_000);
        var prior = new[] { (new PayMonth(2026, 10), (bool?)true) };
        Assert.True(PayrollRules.EsicCovered(raised, new PayMonth(2026, 12), R, prior));
        Assert.True(PayrollRules.EsicCovered(raised, new PayMonth(2027, 3), R, prior));
        Assert.False(PayrollRules.EsicCovered(raised, new PayMonth(2027, 4), R, prior));
        Assert.True(PayrollRules.ComputeLine(raised, Full("2026-12", 31), R, null, true).Esic > 0);
        Assert.Null(PayrollRules.EsicCovered(raised, new PayMonth(2026, 12), R with { EsicPeriod = false }, prior));
        Assert.Equal(["2026-04", "2026-04", "2026-10", "2026-10"],
            new[] { new PayMonth(2026, 4), new PayMonth(2026, 9), new PayMonth(2026, 10), new PayMonth(2027, 3) }.Select(m => m.EsicPeriodStart.ToString()));
        // Daily wage contract test: daily × 26.
        var daily = new StaffProfile(2, "D", "KIT", "x", PayType.Daily, 0, 80_000, ShiftCode.M, 2, new DateOnly(2025, 4, 1), null, true);
        Assert.Equal(2_080_000, daily.EsicContractWage);
    }

    [Fact]
    public void Rupee_rounding_PF_nearest_rupee_ESIC_next_rupee()
    {
        var rr = R with { Rounding = StatutoryRounding.Rupee };
        var a = PayrollRules.ComputeLine(Mon(2_100_000), Full(), rr);
        Assert.Equal((15_000L, 64_900L, 126_000L), (a.Esic, a.EsicEr, a.Pf));
        Assert.Equal(126_054, PayrollRules.ComputeLine(Mon(2_100_900), Full(), R).Pf);
        Assert.Equal(126_100, PayrollRules.ComputeLine(Mon(2_100_900), Full(), rr).Pf);   // 1,260.54 → 1,261
        Assert.Equal(126_006, PayrollRules.ComputeLine(Mon(2_100_100), Full(), R).Pf);
        Assert.Equal(126_000, PayrollRules.ComputeLine(Mon(2_100_100), Full(), rr).Pf);   // 1,260.06 → 1,260
        Assert.Equal(14_963, PayMath.Pct(1_995_000, 75, StatutoryRounding.Paise));
        Assert.Equal(15_000, PayMath.Pct(1_995_000, 75, StatutoryRounding.Rupee, esic: true));
        Assert.Equal(100, PayMath.Pct(100, 75, StatutoryRounding.Rupee, esic: true));
    }

    [Fact]
    public void Maharashtra_PT_slabs_by_gender_with_February_top_slab()
    {
        long Pt(long b, Gender g, string ym = "2026-09") => PayrollRules.Pt(b, g, PayMonth.Parse(ym), R);
        Assert.Equal([0L, 17_500, 17_500, 20_000], new[] { Pt(750_000, Gender.M), Pt(750_001, Gender.M), Pt(1_000_000, Gender.M), Pt(1_000_001, Gender.M) });
        Assert.Equal([0L, 17_500, 30_000], new[] { Pt(750_000, Gender.M, "2027-02"), Pt(1_000_000, Gender.M, "2027-02"), Pt(1_000_001, Gender.M, "2027-02") });
        Assert.Equal([0L, 20_000, 30_000, 0], new[] { Pt(2_500_000, Gender.F), Pt(2_500_001, Gender.F), Pt(2_500_001, Gender.F, "2027-02"), Pt(1_000_001, Gender.F) });
        Assert.Equal(30_000, PayrollRules.ComputeLine(Mon(2_000_000), Full("2027-02", 28), R).Pt);
        Assert.Equal(0, PayrollRules.ComputeLine(Mon(2_200_000, g: Gender.F), Full(), R).Pt);
        var zero = PayrollRules.ComputeLine(Mon(2_000_000), Full(p: 0, a: 30), R);
        Assert.Equal((0L, 0L), (zero.Earned, zero.Pt));
    }

    [Fact]
    public void Overtime_at_the_ordinary_rate_is_outside_the_statutory_base()
    {
        var l = PayrollRules.ComputeLine(Mon(1_000_000), Full(ot: 600), R);
        Assert.Equal(41_667, l.Ot);   // 1,000,000 × 600 / (30 × 8 × 60) = 41,666.67
        Assert.Equal((17_500L, 60_000L, 950_000L, 7_125L, 30_875L), (l.Pt, l.Pf, l.EsicWages, l.Esic, l.EsicEr));
        Assert.Equal((1_041_667L, 84_625L, 957_042L), (l.Gross, l.Deductions, l.Net));
        Assert.Equal(83_333, PayrollRules.ComputeLine(Mon(1_000_000), Full(ot: 600), R with { OtBps = 20_000 }).Ot);
        var big = PayMath.MulDiv(100_000_000, 14_880, 30_000, 8L * 60 * 10000 * 31);
        Assert.Equal((long)(((System.Numerics.BigInteger)100_000_000 * 14_880 * 30_000 * 2 + 8L * 60 * 10000 * 31) / (2 * (System.Numerics.BigInteger)(8L * 60 * 10000 * 31))), big);
    }

    [Fact]
    public void Three_late_marks_are_half_a_day_and_the_structured_split_sums_to_earned()
    {
        decimal Pd(AttendanceCounts c) => PayrollRules.ComputeLine(Mon(3_000_000), c, R).PayableDays;
        Assert.Equal([30m, 29.5m, 29.5m, 29m, 29.5m, 29m, 29m, 29.5m],
            new[] { Pd(Full(l: 2)), Pd(Full(l: 3)), Pd(Full(l: 5)), Pd(Full(l: 6)), Pd(Full(h: 1)), Pd(Full(a: 1)), Pd(Full(unmarked: 1)), Pd(Full(lop: 0.5m)) });
        foreach (var m in new long[] { 1_234_567, 1_350_000, 999_999 })
        {
            var l = PayrollRules.ComputeLine(Mon(m), Full(h: 3, l: 4), R);
            Assert.Equal(l.Earned, l.Basic + l.Hra + l.Washing + l.Special);
            Assert.True(l.Special >= 0);
        }

        Assert.Equal(30m, PayrollRules.ComputeLine(Mon(3_000_000), Full(l: 3), R with { LateBlock = 0 }).PayableDays);
    }

    [Fact]
    public void F01_LOP_leave_over_a_weekly_off_deducts_only_the_stored_days()
    {
        // Aarav (₹18,000, weekly off Tuesday); LOP Mon 9 – Wed 11 Nov 2026 → 2 days, payable 28 of 30.
        var st = new StaffProfile(2, "Aarav Joshi", "FO", "x", PayType.Monthly, 1_800_000, 0, ShiftCode.M, 2, new DateOnly(2025, 4, 1), null, true);
        var leave = new LeaveSpan(1, 2, LeaveType.LOP, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 11), null, 0, LeaveStatus.Approved);
        var days = new StaffDays(leaves: [leave]);
        Assert.Equal(2m, PayrollRules.LeaveDays(st, days, leave.From, leave.To, null));
        var nov = new PayMonth(2026, 11);
        var c = PayrollRules.Counts(st, days, nov);
        Assert.Equal(2m, c.LopLeave);
        var line = PayrollRules.ComputeLine(st, c with { Unmarked = 0 }, R);
        Assert.Equal(28m, line.PayableDays);
        Assert.Equal(PayMath.MulDiv(1_800_000, 56, 1, 60), line.Earned);
        Assert.Equal(0m, PayrollRules.LeaveDays(st, days, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), LeaveHalf.AM));   // a weekly off
    }

    [Fact]
    public void Half_day_leave_with_a_half_day_worked_is_a_full_paid_day()
    {
        var st = new StaffProfile(9, "Suresh", "KIT", "Cook", PayType.Monthly, 1_600_000, 0, ShiftCode.M, 3, new DateOnly(2025, 4, 1), null, true);
        var d = new DateOnly(2026, 9, 10);
        var att = new Dictionary<DateOnly, AttendanceMark> { [d] = new(d, AttendanceStatus.H, 0) };
        var cl = new LeaveSpan(1, 9, LeaveType.CL, d, d, LeaveHalf.AM, 0.5m, LeaveStatus.Approved);
        var c = PayrollRules.Counts(st, new StaffDays(null, att, [cl]), new PayMonth(2026, 9), d);
        Assert.Equal((1m, 0.5m, 0m), (c.HL, c.PaidLeave, c.H));
        var lop = PayrollRules.Counts(st, new StaffDays(null, att, [cl with { Type = LeaveType.LOP }]), new PayMonth(2026, 9), d);
        Assert.Equal(0.5m, lop.LopLeave);
        var none = PayrollRules.Counts(st, new StaffDays(null, null, [cl]), new PayMonth(2026, 9), d);
        var baseline = PayrollRules.Counts(st, new StaffDays(), new PayMonth(2026, 9), d);
        Assert.Equal(baseline.Unmarked - 0.5m, none.Unmarked);   // the leave half is paid, the other half unmarked
    }

    [Fact]
    public void Self_checkin_is_late_after_shift_start_plus_15_minutes()
    {
        Assert.Equal(AttendanceStatus.P, Shifts.CheckinStatus(new TimeOnly(7, 15), ShiftCode.M));
        Assert.Equal(AttendanceStatus.L, Shifts.CheckinStatus(new TimeOnly(7, 16), ShiftCode.M));
        Assert.Equal(AttendanceStatus.P, Shifts.CheckinStatus(new TimeOnly(23, 59), ShiftCode.N));
    }

    [Fact]
    public void F05_a_version_starts_from_the_version_in_force_on_its_date_and_carries_forward()
    {
        // Jan PF 10%, then Apr changes only ESIC → April keeps PF 10%.
        var v = RateVersions.Save([R], new DateOnly(2027, 1, 1), new RateChange(PfEeBps: 1000), null).Versions;
        v = RateVersions.Save(v, new DateOnly(2027, 4, 1), new RateChange(EsicEeBps: 100), null).Versions;
        Assert.Equal((1000, 100), (RateVersions.For(v, new PayMonth(2027, 4)).PfEeBps, RateVersions.For(v, new PayMonth(2027, 4)).EsicEeBps));
        Assert.Equal(1200, RateVersions.For(v, new PayMonth(2026, 12)).PfEeBps);
        // Reverse order: the earlier change is carried into the later version that had inherited the old value.
        var r = RateVersions.Save([R], new DateOnly(2027, 4, 1), new RateChange(EsicEeBps: 100), null).Versions;
        var o = RateVersions.Save(r, new DateOnly(2027, 1, 1), new RateChange(PfEeBps: 1000), null);
        Assert.Equal(1000, RateVersions.For(o.Versions, new PayMonth(2027, 4)).PfEeBps);
        Assert.Equal(75, RateVersions.For(o.Versions, new PayMonth(2027, 1)).EsicEeBps);
        Assert.Equal(["pfEeBps → 2027-04-01"], o.Carried);
        // A later version that set its own value keeps it.
        var k = RateVersions.Save([R], new DateOnly(2027, 4, 1), new RateChange(PfEeBps: 1100), null).Versions;
        var ko = RateVersions.Save(k, new DateOnly(2027, 1, 1), new RateChange(PfEeBps: 1000), null);
        Assert.Equal((1100, 1000), (RateVersions.For(ko.Versions, new PayMonth(2027, 4)).PfEeBps, RateVersions.For(ko.Versions, new PayMonth(2027, 2)).PfEeBps));
        Assert.Equal(["2027-04-01 keeps its own pfEeBps"], ko.Kept);
        // Locked months are frozen; validation runs on the new version.
        Assert.Equal(HccErrorCode.INVALID_TRANSITION,
            Assert.Throws<HccException>(() => RateVersions.Save([R], new DateOnly(2026, 8, 1), new RateChange(PfEeBps: 1000), new PayMonth(2026, 8))).Code);
        Assert.Throws<HccException>(() => RateVersions.Save([R], new DateOnly(2027, 1, 1), new RateChange(OtBps: 5000), null));
        Assert.Throws<HccException>(() => RateVersions.Save([R], new DateOnly(2027, 1, 2), new RateChange(OtBps: 20000), null));
    }

    [Fact]
    public void F06_default_effective_date_is_never_before_the_month_after_the_last_locked_run()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), RateVersions.DefaultFrom(new DateOnly(2026, 10, 24), new PayMonth(2026, 8)));
        Assert.Equal(new DateOnly(2026, 10, 1), RateVersions.DefaultFrom(new DateOnly(2026, 10, 24), null));
        Assert.Equal(new DateOnly(2026, 11, 1), RateVersions.DefaultFrom(new DateOnly(2026, 10, 24), new PayMonth(2026, 10)));
    }

    [Fact]
    public void Leave_balance_is_per_FY_and_never_negative_F15()
    {
        var leaves = new[]
        {
            new LeaveSpan(1, 1, LeaveType.CL, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), null, 2, LeaveStatus.Approved),
            new LeaveSpan(2, 1, LeaveType.CL, new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 29), null, 2, LeaveStatus.Pending),
            new LeaveSpan(3, 1, LeaveType.CL, new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30), null, 1, LeaveStatus.Rejected),
            new LeaveSpan(4, 1, LeaveType.CL, new DateOnly(2026, 3, 30), new DateOnly(2026, 3, 30), null, 1, LeaveStatus.Approved),   // previous FY
        };
        var b = LeaveRules.Balance(LeavePolicy.Defaults(), leaves, new DateOnly(2026, 10, 24)).Single(x => x.Type == LeaveType.CL);
        Assert.Equal((12, 4m, 8m, 0m), (b.Entitled, b.Used, b.Left, b.Over));
        var cut = LeaveRules.Balance(LeavePolicy.Defaults() with { Cl = 2 }, leaves, new DateOnly(2026, 10, 24)).Single(x => x.Type == LeaveType.CL);
        Assert.Equal((0m, 2m), (cut.Left, cut.Over));
        Assert.Equal("FY 2026–27", FinancialYear.Containing(new DateOnly(2027, 3, 31)).Label);
    }

    [Fact]
    public void Fnf_is_exit_pay_less_statutory_and_the_full_advance_plus_adjustments()
    {
        var st = Mon(1_350_000) with { Active = false, ExitedOn = new DateOnly(2026, 10, 24) };
        var line = PayrollRules.ComputeLine(st, Full("2026-10", 31, p: 24, a: 0) with { Na = 7 }, R);
        var f = FnfRules.Compute(st, st.ExitedOn!.Value, line, R, 200_000, [new FnfAdjustment("EL encashment", true, 100_000), new FnfAdjustment("Uniform", false, 50_000)]);
        Assert.Equal(line.Gross + 100_000, f.Earnings);
        Assert.Equal(line.Pf + line.Esic + line.Pt + 200_000 + 50_000, f.Recoveries);
        Assert.Equal(f.Earnings - f.Recoveries, f.Net);
        var paid = FnfRules.Compute(st, st.ExitedOn!.Value, null, R, 500_000, []);
        Assert.Equal((false, -500_000L), (paid.SalaryIncluded, paid.Net));
        Assert.Equal((20_000_000L, 10_000_000L), (FnfRules.AdjustmentMax, FnfRules.OwnerAbove));   // DEC-066
        Assert.Equal(PayMath.MulDiv(1_350_000, 7, 1, 62), FnfRules.ElEncashment(st, 3.5m, st.ExitedOn!.Value));
    }

    [Fact]
    public void Files_amount_in_words_whatsapp_numbers_and_csv_formula_guard_F17()
    {
        Assert.Equal("Rupees Twelve Thousand Three Hundred Forty Five and Fifty Paise Only", PayrollFiles.AmountInWords(1_234_550));
        Assert.Equal("Rupees One Crore Only", PayrollFiles.AmountInWords(1_000_000_000));
        Assert.Equal("Rupees Zero Only", PayrollFiles.AmountInWords(0));
        Assert.Equal(["919000010029", "919000010029", ""], new[] { PayrollFiles.WhatsAppNumber("90000 10029"), PayrollFiles.WhatsAppNumber("+91 90000 10029"), PayrollFiles.WhatsAppNumber("12345") });
        Assert.Equal("'=HYPERLINK(1)", PayrollFiles.Cell("=HYPERLINK(1)"));
        Assert.Equal("'@x", PayrollFiles.Cell("@x"));
        Assert.Equal("-12.50", PayrollFiles.Cell("-12.50"));
        Assert.Equal("\"a,b\"", PayrollFiles.Cell("a,b"));
        var line = PayrollRules.ComputeLine(Mon(2_000_000), Full(), R);
        var draft = PayrollFiles.Statutory(new PayMonth(2026, 9), false, R.From, [line]);
        Assert.Contains("DRAFT — NOT FINAL, do not file", draft, StringComparison.Ordinal);
        Assert.Contains("Locked run", PayrollFiles.Statutory(new PayMonth(2026, 9), true, R.From, [line]), StringComparison.Ordinal);
        var bank = PayrollFiles.Bank(new PayMonth(2026, 9), [line], new Dictionary<long, BankDetails> { [1] = new("Demo Bank", "000012340001", "DEMO0000101") }, 2);
        Assert.Contains("'000012340001", bank, StringComparison.Ordinal);
        Assert.Contains("REISSUE", bank, StringComparison.Ordinal);
        Assert.Equal("••••0001", BankDetails.Mask("000012340001"));
    }

    [Fact]
    public void A04_01_statutory_statement_adds_full_and_final_rows_to_members_and_totals_and_is_unchanged_without_them()
    {
        var sep = new PayMonth(2026, 9);
        var stay = PayrollRules.ComputeLine(Mon(2_000_000), Full(), R);
        // Leaver ₹13,500/month, exit 24 Oct 2026: 24 of 31 days payable (7 days after the exit) → earned 13,500 × 48/62 = 10,451.61;
        // basic 50 % 5,225.81 → PF 12 % 627.10, employer 13 % 679.36; washing 10 % of basic 522.58 → ESIC wages 9,929.03 → 0.75 % 74.47,
        // 3.25 % 322.69; PT man, earned above ₹10,000, not February → ₹200.
        var leaver = Mon(1_350_000) with { StaffId = 7, Name = "Leaver Person", Active = false, ExitedOn = new DateOnly(2026, 10, 24) };
        var fnfLine = PayrollRules.ComputeLine(leaver, Full("2026-10", 31, p: 24) with { Na = 7 }, R);
        Assert.Equal((1_045_161L, 522_581L, 62_710L, 67_936L, 992_903L, 7_447L, 32_269L, 20_000L),
            (fnfLine.Earned, fnfLine.PfWages, fnfLine.Pf, fnfLine.PfEr, fnfLine.EsicWages, fnfLine.Esic, fnfLine.EsicEr, fnfLine.Pt));

        var plain = PayrollFiles.Statutory(sep, true, R.From, [stay]);
        Assert.Equal(plain, PayrollFiles.Statutory(sep, true, R.From, [stay], []));   // no leaver: byte-identical
        Assert.DoesNotContain("F&F", plain, StringComparison.Ordinal);
        var rows = PayrollFiles.Statutory(sep, true, R.From, [stay], [new FnfStatutoryLine(fnfLine, leaver.ExitedOn!.Value)]).Split('\n');
        Assert.Equal($"Provident Fund,{PayrollFiles.Rupees(stay.Pf + 62_710)},{PayrollFiles.Rupees(stay.PfEr + 67_936)},{PayrollFiles.Rupees(stay.Pf + stay.PfEr + 62_710 + 67_936)}", rows[1]);
        Assert.Equal($"Professional Tax (Maharashtra),{PayrollFiles.Rupees(stay.Pt + 20_000)},0.00,{PayrollFiles.Rupees(stay.Pt + 20_000)}", rows[3]);
        Assert.Equal("2,7,Leaver Person,10451.61,5225.81,627.10,679.36,9929.03,74.47,322.69,200.00", rows[7]);
        Assert.Equal("F&F — exit month's salary settled in full and final (not in the pay run): Sr 2 Leaver Person (last day 2026-10-24)", rows[8]);
        Assert.Equal("Not calculated: gratuity, notice pay, bonus, TDS on salary.", PayrollRates.NotCalculated);   // A04-11
    }

    [Fact]
    public void Golden_BASE_august_and_september_payroll_match_the_prototype()
    {
        var g = PayrollGolden.Data;
        var adv = g.Advances.Single();
        var advance = new AdvanceRow(1, PayrollGolden.StaffNo(adv.StaffId), adv.Amount, adv.Installment, 0, PayrollGolden.Date(adv.Date), PayMonth.Of(PayrollGolden.Date(adv.Date)));
        var aug = PayrollCalculator.ComputeRun(PayrollGolden.World(new PayMonth(2026, 8), [advance], []), R);
        Assert.Equal(g.August.Lines.Count, aug.Lines.Count);
        foreach (var (gl, l) in g.August.Lines.Zip(aug.Lines)) PayrollGolden.AssertLine(gl, l);
        Assert.Equal(g.August.Warnings, aug.Warnings);
        Assert.Equal(21_210_166, PayTotals.Of(aug.Lines).Net);   // ₹2,12,101.66

        var recovered = aug.Lines.Single(l => l.StaffId == advance.StaffId).Advance;
        var coverage = aug.Lines.Select(l => new LockedCoverage(l.StaffId, new PayMonth(2026, 8), l.EsicCovered)).ToList();
        var sep = PayrollCalculator.ComputeRun(PayrollGolden.World(new PayMonth(2026, 9), [advance with { RecoveredPaise = recovered }], coverage), R);
        foreach (var (gl, l) in g.September.Lines.Zip(sep.Lines)) PayrollGolden.AssertLine(gl, l);
        Assert.Equal(g.September.Warnings, sep.Warnings);
        var t = PayTotals.Of(sep.Lines);
        Assert.Equal(20_888_900, t.Net);   // ₹2,08,889.00 (review F-07: was ₹2,08,745.31 before the contribution-period rule)
        Assert.Equal((g.September.Totals.Gross, g.September.Totals.Pf, g.September.Totals.PfEr, g.September.Totals.Esic, g.September.Totals.EsicEr, g.September.Totals.Pt, g.September.Totals.Ctc),
            (t.Gross, t.Pf, t.PfEr, t.Esic, t.EsicEr, t.Pt, t.Ctc));
    }
}
