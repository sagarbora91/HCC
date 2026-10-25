using System.Text.Json;
using Hcc.Application.Payroll;
using Hcc.Domain.Payroll;

namespace Hcc.Payroll.Tests.Harness;

/// <summary>
/// The prototype BASE payroll fixture exported by <c>Golden/export-payroll-golden.mjs</c>: 12 fictional staff, Aug + Sep 2026 attendance,
/// leave, the open advance and the expected August (locked in the fixture) and September lines.
/// </summary>
public sealed class PayrollGolden
{
    public sealed record GStaff(string Id, string Name, string Dept, string Designation, string? UserId, string PayType, long Monthly, long Daily, string DefaultShift,
        int WeeklyOff, string Joined, string Phone, string Gender, string BankName, string AccountNo, string Ifsc);

    public sealed record GLeave(string Id, string StaffId, string Type, string From, string To, string? Half, decimal Days, string Status);

    public sealed record GAdvance(string Id, string StaffId, long Amount, long Installment, string Date, string? FromMonth);

    public sealed record GAtt(string Date, string StaffId, string Status, int OtMin);

    public sealed record GDept(string Id, string Name);

    public sealed record GCounts(string Ym, int Days, decimal P, decimal L, decimal H, decimal HL, decimal A, decimal OFFW, decimal Off, decimal PaidLeave, decimal LopLeave,
        decimal Unmarked, decimal Na, int OtMin);

    public sealed record GLine(string StaffId, GCounts Counts, int LatePenaltyHalf, decimal PayableDays, long Earned, long Basic, long Hra, long Washing, long Special, long Ot,
        long Gross, long PfWages, long Pf, long PfEr, long EsicWages, long Esic, long EsicEr, long Pt, long Advance, long Deductions, long Net, long Ctc, bool? EsicCovered);

    public sealed record GTotals(int Staff, long Earned, long Gross, long Pf, long PfEr, long Esic, long EsicEr, long Pt, long Advance, long Deductions, long Net, long Ctc);

    public sealed record GRun(IReadOnlyList<GLine> Lines, GTotals Totals, IReadOnlyList<string> Warnings);

    public sealed record File(string BusinessDate, IReadOnlyList<GDept> Depts, IReadOnlyList<GStaff> Staff, IReadOnlyList<GLeave> Leaves, IReadOnlyList<GAdvance> Advances,
        IReadOnlyList<GAtt> Attendance, GRun August, GRun September);

    private static readonly Lazy<File> Loaded = new(() =>
        JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "payroll-base.golden.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!);

    public static File Data => Loaded.Value;

    public static DateOnly Date(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Golden staff code (HR-S01…) → numeric staff id used in a computation (1…12, or the SQL ids).</summary>
    public static StaffProfile Profile(GStaff g, long id) => new(id, g.Name, g.Dept, g.Designation, g.PayType == "daily" ? PayType.Daily : PayType.Monthly, g.Monthly, g.Daily,
        Enum.Parse<ShiftCode>(g.DefaultShift), g.WeeklyOff, Date(g.Joined), null, true, Enum.Parse<Gender>(g.Gender));

    public static LeaveSpan Span(GLeave l, long leaveId, long staffId) => new(leaveId, staffId, Enum.Parse<LeaveType>(l.Type), Date(l.From), Date(l.To),
        l.Half is null ? null : Enum.Parse<LeaveHalf>(l.Half), l.Days, Enum.Parse<LeaveStatus>(l.Status));

    /// <summary>An in-memory month world over the golden data (staff ids 1…12 in fixture order).</summary>
    public static MonthWorld World(PayMonth month, IReadOnlyList<AdvanceRow> advances, IReadOnlyList<LockedCoverage> coverage)
    {
        var d = Data;
        var ids = d.Staff.Select((s, i) => (s.Id, Id: (long)(i + 1))).ToDictionary(x => x.Item1, x => x.Id);
        var staff = d.Staff.Select(s => new StaffRow(Profile(s, ids[s.Id]), null, s.Phone, s.BankName, s.AccountNo, s.Ifsc, "AAAAAAAAAAE=")).ToList();
        var days = d.Staff.ToDictionary(s => ids[s.Id], s => new StaffDays(null,
            d.Attendance.Where(a => a.StaffId == s.Id && Date(a.Date) >= month.First && Date(a.Date) <= month.Last)
                .ToDictionary(a => Date(a.Date), a => new AttendanceMark(Date(a.Date), Enum.Parse<AttendanceStatus>(a.Status), a.OtMin)),
            d.Leaves.Select((l, i) => (l, i)).Where(x => x.l.StaffId == s.Id).Select(x => Span(x.l, x.i + 1, ids[s.Id])).ToList()));
        return new MonthWorld(month, staff, days, advances, coverage, []);
    }

    public static long StaffNo(string code) => Data.Staff.Select((s, i) => (s.Id, i)).First(x => x.Id == code).i + 1;

    /// <summary>Asserts a computed line equals the golden line field by field.</summary>
    public static void AssertLine(GLine g, PayLine l)
    {
        var c = l.Counts;
        Assert.Equal((g.Counts.Ym, g.Counts.Days, g.Counts.P, g.Counts.L, g.Counts.H, g.Counts.HL, g.Counts.A), (c.Ym, c.Days, c.P, c.L, c.H, c.HL, c.A));
        Assert.Equal((g.Counts.OFFW, g.Counts.Off, g.Counts.PaidLeave, g.Counts.LopLeave, g.Counts.Unmarked, g.Counts.Na, g.Counts.OtMin),
            (c.OFFW, c.Off, c.PaidLeave, c.LopLeave, c.Unmarked, c.Na, c.OtMin));
        Assert.Equal((g.LatePenaltyHalf, g.PayableDays), (l.LatePenaltyHalf, l.PayableDays));
        Assert.Equal((g.Earned, g.Basic, g.Hra, g.Washing, g.Special, g.Ot, g.Gross), (l.Earned, l.Basic, l.Hra, l.Washing, l.Special, l.Ot, l.Gross));
        Assert.Equal((g.PfWages, g.Pf, g.PfEr, g.EsicWages, g.Esic, g.EsicEr, g.Pt), (l.PfWages, l.Pf, l.PfEr, l.EsicWages, l.Esic, l.EsicEr, l.Pt));
        Assert.Equal((g.Advance, g.Deductions, g.Net, g.Ctc, g.EsicCovered), (l.Advance, l.Deductions, l.Net, l.Ctc, l.EsicCovered));
    }
}
