// A04 kernel probe: the server's own payroll rules (Hcc.Domain.Payroll + Hcc.Application.Payroll.PayrollCalculator) on
//   (1) the golden BASE August / September runs (HR-S05 monthly, HR-S10 daily),
//   (2) Ravi Shinde's full and final (exit 24 Oct 2026, whole outstanding advance, two adjustments),
//   (3) the edge scenarios of A04_recompute.mjs (counts given, money arithmetic only).
// Usage: dotnet <out>/A04_kernel.dll <repo root>   -> prints JSON (saved by the caller as evidence/A04/kernel.json)
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcc.Application.Payroll;
using Hcc.Domain.Payroll;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "08 Production/tests/Hcc.Payroll.Tests/Golden/payroll-base.golden.json")))!;
var scen = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "05 Delivery Evidence/Audit/evidence/A04/scenarios.json")))!;
var R = PayrollRates.Defaults();
DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
var staffJ = golden["staff"]!.AsArray();
var codes = staffJ.Select((s, i) => (Code: (string)s!["id"]!, Id: (long)(i + 1))).ToDictionary(x => x.Code, x => x.Id);
StaffProfile Prof(JsonNode s) => new(codes[(string)s["id"]!], (string)s["name"]!, (string)s["dept"]!, (string)s["designation"]!,
    (string)s["payType"]! == "daily" ? PayType.Daily : PayType.Monthly, (long)s["monthly"]!, (long)s["daily"]!, Enum.Parse<ShiftCode>((string)s["defaultShift"]!),
    (int)s["weeklyOff"]!, D((string)s["joined"]!), null, true, Enum.Parse<Gender>((string)s["gender"]!));
var leaves = golden["leaves"]!.AsArray().Select((l, i) => new LeaveSpan(i + 1, codes[(string)l!["staffId"]!], Enum.Parse<LeaveType>((string)l["type"]!), D((string)l["from"]!),
    D((string)l["to"]!), l["half"] is null ? null : Enum.Parse<LeaveHalf>((string)l["half"]!), (decimal)l["days"]!, Enum.Parse<LeaveStatus>((string)l["status"]!))).ToList();
var att = golden["attendance"]!.AsArray().Select(a => (Code: (string)a!["staffId"]!, M: new AttendanceMark(D((string)a["date"]!), Enum.Parse<AttendanceStatus>((string)a["status"]!), (int)a["otMin"]!))).ToList();
StaffDays Days(string code, PayMonth m, IEnumerable<AttendanceMark>? extra = null) => new(null,
    att.Where(a => a.Code == code && a.M.Date >= m.First && a.M.Date <= m.Last).Select(a => a.M).Concat(extra ?? []).ToDictionary(a => a.Date),
    leaves.Where(l => l.StaffId == codes[code]).ToList());
MonthWorld World(PayMonth m, IReadOnlyList<AdvanceRow> adv, IReadOnlyList<LockedCoverage> cov, Func<JsonNode, StaffProfile>? prof = null, IEnumerable<AttendanceMark>? extraRavi = null)
{
    var rows = staffJ.Select(s => new StaffRow((prof ?? Prof)(s!), null, (string)s!["phone"]!, null, null, null, "AAAAAAAAAAE=")).ToList();
    var days = staffJ.ToDictionary(s => codes[(string)s!["id"]!], s => Days((string)s!["id"]!, m, (string)s["id"]! == "HR-S05" ? extraRavi : null));
    return new MonthWorld(m, rows, days, adv, cov, []);
}
object L(PayLine l) => new { l.PayableDays, l.Earned, l.Basic, l.Hra, l.Washing, l.Special, l.Ot, l.Gross, l.PfWages, l.Pf, l.PfEr, l.EsicWages, l.Esic, l.EsicEr, l.Pt, l.Advance, l.Deductions, l.Net, l.Ctc, l.EsicCovered,
    counts = new { l.Counts.P, l.Counts.L, l.Counts.H, l.Counts.HL, l.Counts.A, l.Counts.OFFW, l.Counts.Off, l.Counts.PaidLeave, l.Counts.LopLeave, l.Counts.Unmarked, l.Counts.Na, l.Counts.OtMin } };

var ravi = codes["HR-S05"];
var advance = new AdvanceRow(1, ravi, 300_000, 100_000, 0, D("2026-08-10"), new PayMonth(2026, 8));
var aug = PayrollCalculator.ComputeRun(World(new PayMonth(2026, 8), [advance], []), R).Lines;
var cov = aug.Select(l => new LockedCoverage(l.StaffId, new PayMonth(2026, 8), l.EsicCovered)).ToList();
var augRec = aug.Single(l => l.StaffId == ravi).Advance;
var sep = PayrollCalculator.ComputeRun(World(new PayMonth(2026, 9), [advance with { RecoveredPaise = augRec }], cov), R).Lines;
var cases = new Dictionary<string, object>();
foreach (var (code, id) in new[] { ("HR-S05", ravi), ("HR-S10", codes["HR-S10"]) })
{
    cases[$"{code} 2026-08"] = L(aug.Single(l => l.StaffId == id));
    cases[$"{code} 2026-09"] = L(sep.Single(l => l.StaffId == id));
}

// F&F: exit 24 Oct 2026; August locked (Rs 1,000 recovered); September not locked.
var f = scen["fnf"]!;
var exit = D((string)f["exit"]!);
var oct = new PayMonth(2026, 10);
var octMarks = f["octAttendance"]!.AsArray().Select(a => new AttendanceMark(D((string)a!["date"]!), Enum.Parse<AttendanceStatus>((string)a["status"]!), (int)a["otMin"]!)).ToList();
StaffProfile ExitProf(JsonNode s) { var p = Prof(s); return p.StaffId == ravi ? p with { Active = false, ExitedOn = exit } : p; }
var octWorld = World(oct, [advance with { RecoveredPaise = augRec }], [], ExitProf, octMarks);   // golden has no October rows: marks from scenarios.json
var adjustments = f["adjustments"]!.AsArray().Select(a => new FnfAdjustment((string)a!["label"]!, (string)a["kind"]! == "add", (long)a["amount"]!)).ToList();
var ravRow = octWorld.Staff.Single(s => s.StaffId == ravi);
var fnf = PayrollCalculator.ComputeFnf(octWorld, ravRow, R, exitMonthPaid: false, adjustments);
var el = FnfRules.ElEncashment(ravRow.Profile, 15m, exit);

// Edge scenarios.
var edges = new List<object>();
foreach (var e in scen["edges"]!.AsArray())
{
    var c = e!["counts"]!;
    var ym = PayMonth.Parse((string)e["ym"]!);
    var daily = (string)e["payType"]! == "daily";
    var st = new StaffProfile(99, "Edge", "FO", "x", daily ? PayType.Daily : PayType.Monthly, (long)e["monthly"]!, (long)e["daily"]!, ShiftCode.G, 0, D((string)e["joined"]!), null, true,
        Enum.Parse<Gender>((string)e["gender"]!));
    var counts = new AttendanceCounts(ym.ToString(), (int)c["days"]!, (decimal)c["present"]!, (decimal)c["late"]!, (decimal)c["half"]!, 0, (decimal)c["absent"]!, (decimal)c["workedOff"]!,
        (decimal)c["weeklyOff"]!, (decimal)c["paidLeave"]!, (decimal)c["lopLeave"]!, (decimal)c["unmarked"]!, (decimal)c["notInService"]!, (int)c["otMin"]!);
    (long, long)? adv = e["advance"] is { } a ? ((long)a["installment"]!, (long)a["balance"]!) : null;
    bool? covered = e["esicCovered"] is { } cv ? (bool)cv : null;
    edges.Add(new { label = (string)e["label"]!, line = L(PayrollRules.ComputeLine(st, counts, R, adv, covered)) });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "A04_kernel (Hcc.Domain.Payroll + PayrollCalculator, prebuilt DLLs)",
    dlls = new[] { typeof(PayrollRules).Assembly.Location, typeof(PayrollCalculator).Assembly.Location },
    rates = R,
    cases,
    fnf = new { fnf.SalaryIncluded, line = fnf.Line is null ? null : L(fnf.Line), fnf.AdvanceOutstanding, fnf.Additions, fnf.Earnings, fnf.Recoveries, fnf.Net, elEncashment = el },
    edges,
}, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
