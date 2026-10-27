using System.Globalization;
using Hcc.Domain.Payroll;

namespace Hcc.Payroll.Tests.Domain;

/// <summary>
/// SCC leave port (DEC-116): <see cref="SccLeave"/> against a line-by-line C# transcription of SCC LeaveDesk (leave\index.html — isActiveLeave,
/// isCountedLeave, leaveDaysOf, getCapacity, canAddLeave, the submitLeave loop and toast, requestedLeaveDays, fyUsage, the balance confirm and the
/// blackout gate) over seeded random grids. SCC keys records by staff NAME; HCC by staff ID (DEC-119) — the oracle names staff "S&lt;id&gt;".
/// </summary>
public sealed class SccLeaveTests
{
    /// <summary>The SCC oracle: the original functions with SCC's own data shape (data.leaves[date] = [{ name, type, status, category }]).</summary>
    private static class Scc
    {
        public sealed record L(string Name, string Type, string? Status, string Category = "Casual Leave");

        private const int MaxDay = 3, MaxShift = 4;
        private static readonly string[] Statuses = ["pending", "approved", "rejected"];

        private static string LeaveStatus(L l) => l.Status is not null && Statuses.Contains(l.Status) ? l.Status : "approved";

        private static bool IsActiveLeave(L l) => LeaveStatus(l) != "rejected";

        private static bool IsCountedLeave(L l) => LeaveStatus(l) == "approved";

        private static decimal LeaveDaysOf(L l) => l.Type == "full_day" ? 1 : 0.5m;

        private static bool IsWeekOffDate(int[] weekOff, string k) => weekOff.Contains((int)Day(k).DayOfWeek);

        public static (int AmUsed, int PmUsed, int DayUsed, bool DayFull, bool AmFull, bool PmFull, bool BothFull, bool CanAddFullDay, bool CanAddAM, bool CanAddPM)
            GetCapacity(List<L> day)
        {
            var leaves = day.Where(IsActiveLeave).ToList();
            var amUsed = leaves.Count(l => l.Type is "full_day" or "half_day_am");
            var pmUsed = leaves.Count(l => l.Type is "full_day" or "half_day_pm");
            var dayUsed = leaves.Count;
            var dayFull = dayUsed >= MaxDay;
            return (amUsed, pmUsed, dayUsed, dayFull, amUsed >= MaxShift || dayFull, pmUsed >= MaxShift || dayFull, (amUsed >= MaxShift && pmUsed >= MaxShift) || dayFull,
                amUsed < MaxShift && pmUsed < MaxShift && !dayFull, amUsed < MaxShift && !dayFull, pmUsed < MaxShift && !dayFull);
        }

        public static bool CanAddLeave(List<L> day, string leaveType)
        {
            var cap = GetCapacity(day);
            return leaveType switch
            {
                "full_day" => cap.CanAddFullDay,
                "half_day_am" => cap.CanAddAM,
                "half_day_pm" => cap.CanAddPM,
                _ => false,
            };
        }

        /// <summary>submitLeave's loop and toast (L1388-1406); returns the toast and the dates added.</summary>
        public static (string Toast, List<string> Added) SubmitLeave(Dictionary<string, List<L>> data, int[] weekOff, string name, string from, string to, string type)
        {
            var d = Day(from);
            var end = Day(to);
            int added = 0, blocked = 0, dupCount = 0, weekendSkipped = 0;
            var dates = new List<string>();
            while (d <= end)
            {
                var key = Key(d);
                if (!IsWeekOffDate(weekOff, key))
                {
                    if (!data.TryGetValue(key, out var day)) data[key] = day = [];
                    var isDup = day.Any(l => LeaveStatus(l) != "rejected" && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)
                        && (l.Type == type || l.Type == "full_day" || type == "full_day"));
                    if (isDup) dupCount++;
                    else if (!CanAddLeave(day, type)) blocked++;
                    else
                    {
                        day.Add(new L(name, type, "pending"));
                        added++;
                        dates.Add(key);
                    }
                }
                else weekendSkipped++;

                d = d.AddDays(1);
            }

            var parts = new List<string>();
            if (added > 0) parts.Add($"{added} day(s) submitted — pending approval");
            if (blocked > 0) parts.Add($"{blocked} skipped — day/shift full (max {MaxDay}/day)");
            if (dupCount > 0) parts.Add($"{dupCount} skipped — duplicate");
            if (weekendSkipped > 0 && added == 0 && blocked == 0 && dupCount == 0) parts.Add($"{weekendSkipped} day(s) skipped — weekend/weekly-off");
            return (parts.Count > 0 ? string.Join(", ", parts) : "Nothing added", dates);
        }

        public static decimal RequestedLeaveDays(int[] weekOff, string from, string to, string type)
        {
            if (string.CompareOrdinal(from, to) > 0) return 0;
            DateOnly d = Day(from), end = Day(to);
            int n = 0, guard = 0;
            while (d <= end && guard++ < 400)
            {
                if (!IsWeekOffDate(weekOff, Key(d))) n++;
                d = d.AddDays(1);
            }

            return n * (type == "full_day" ? 1 : 0.5m);
        }

        public static Dictionary<string, decimal> FyUsage(Dictionary<string, List<L>> data, string name, (string From, string To) fy)
        {
            var used = new Dictionary<string, decimal>();
            foreach (var (k, list) in data)
            {
                if (string.CompareOrdinal(k, fy.From) < 0 || string.CompareOrdinal(k, fy.To) > 0) continue;
                foreach (var l in list)
                {
                    if (!IsCountedLeave(l)) continue;
                    if (!string.Equals(l.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    used[l.Category] = used.GetValueOrDefault(l.Category) + LeaveDaysOf(l);
                }
            }

            return used;
        }

        public static (string From, string To, string Label) FyRangeOf(int year, int month0)
        {
            var s = month0 >= 3 ? year : year - 1;
            return ($"{s}-04-01", $"{s + 1}-03-31", $"FY {s}–{(s + 1).ToString(CultureInfo.InvariantCulture)[^2..]}");
        }

        /// <summary>JS n % 1 === 0 ? n : n.toFixed(1).</summary>
        public static string Fmt(decimal n) => n % 1 == 0 ? Js(n) : Math.Round(n, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>A JavaScript number in a template literal.</summary>
        public static string Js(decimal n) => n.ToString("0.############", CultureInfo.InvariantCulture);

        /// <summary>The balance confirm (L1345-1351); null when nothing is confirmed.</summary>
        public static string? BalanceConfirm(Dictionary<string, List<L>> data, decimal? quotaCat, string name, string category, int[] weekOff, string from, string to, string type)
        {
            if (quotaCat is null) return null;
            var p = Day(from);
            var fy = FyRangeOf(p.Year, p.Month - 1);
            var used = FyUsage(data, name, (fy.From, fy.To)).GetValueOrDefault(category);
            var req = RequestedLeaveDays(weekOff, from, to, type);
            if (used + req <= quotaCat) return null;
            return $"{name} has used {Fmt(used)} of {Js(quotaCat.Value)} {category} day(s) this {fy.Label}.\nThis request adds {Fmt(req)} more and EXCEEDS the balance.\n\nRecord it anyway (it will still need approval)?";
        }

        public sealed record B(string From, string To, string Store, string Kind, bool Hard, string Label);

        /// <summary>The blackout gate (L1357-1373): (hard toast | soft confirm | null).</summary>
        public static (bool Hard, string? Text) BlackoutGate(IReadOnlyList<B> entries, int[] weekOff, string storeCode, string from, string to, string category)
        {
            var hits = new List<B>();
            DateOnly bd = Day(from), bend = Day(to);
            var bguard = 0;
            while (bd <= bend && bguard++ < 400)
            {
                var k = Key(bd);
                if (!IsWeekOffDate(weekOff, k))
                    hits.AddRange(entries.Where(e => string.CompareOrdinal(k, e.From) >= 0 && string.CompareOrdinal(k, e.To) <= 0 && (e.Store.Length == 0 || e.Store == storeCode)));
                bd = bd.AddDays(1);
            }

            if (hits.Count == 0) return (false, null);
            var labels = string.Join(", ", hits.Select(b => b.Label.Length > 0 ? b.Label : b.Kind == "holiday" ? "Store holiday" : "Blackout").Distinct());
            if (hits.Any(b => b.Hard)) return (true, "Leave falls in a hard blackout window (" + labels + ") — blocked");
            return (false, "These dates fall in a blackout window (" + labels + "): " + category + " leave is discouraged.\nRecord it anyway?");
        }
    }

    private static DateOnly Day(string k) => DateOnly.ParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly string[] Types = ["full_day", "half_day_am", "half_day_pm"];

    private static LeaveHalf? Half(string sccType) => sccType switch { "half_day_am" => LeaveHalf.AM, "half_day_pm" => LeaveHalf.PM, _ => null };

    /// <summary>SCC status → HCC (no status = legacy approved).</summary>
    private static LeaveStatus Status(string? s) => s switch { "pending" => LeaveStatus.Pending, "rejected" => LeaveStatus.Rejected, _ => LeaveStatus.Approved };

    private static readonly (LeaveType Type, string Category)[] Cats =
        [(LeaveType.CL, "Casual Leave"), (LeaveType.SL, "Sick Leave"), (LeaveType.EL, "Earned Leave"), (LeaveType.ML, "Maternity / Paternity"), (LeaveType.CO, "Compensatory Off"),
         (LeaveType.OTH, "Other")];

    private static int[] WeekOff(Random r) => Enumerable.Range(0, 7).Where(_ => r.Next(5) == 0).ToArray();

    /// <summary>A random SCC data.leaves around 9 Nov 2026 and the same records as HCC rows (one per date).</summary>
    private static (Dictionary<string, List<Scc.L>> Scc, List<LeaveSpan> Hcc) World(Random r, DateOnly start, int days, int maxPerDay, string? cancelledInto = null)
    {
        var scc = new Dictionary<string, List<Scc.L>>();
        var hcc = new List<LeaveSpan>();
        long id = 1;
        for (var i = 0; i < days; i++)
        {
            var d = start.AddDays(i);
            var list = new List<Scc.L>();
            for (var n = r.Next(maxPerDay + 1); n > 0; n--)
            {
                var staff = r.Next(1, 7);
                var type = Types[r.Next(3)];
                var status = new string?[] { "pending", "approved", "rejected", null }[r.Next(4)];
                var cat = Cats[r.Next(Cats.Length)];
                list.Add(new Scc.L("S" + staff.ToString(CultureInfo.InvariantCulture), type, status, cat.Category));
                hcc.Add(new LeaveSpan(id++, staff, cat.Type, d, d, Half(type), type == "full_day" ? 1 : 0.5m, Status(status)));
            }

            // HCC: a removed record is CANCELLED and kept (DEC-119); SCC deletes it — a cancelled row must behave as if it were not there.
            if (cancelledInto is not null && r.Next(3) == 0)
                hcc.Add(new LeaveSpan(id++, r.Next(1, 7), LeaveType.CL, d, d, Half(Types[r.Next(3)]), 1, LeaveStatus.Cancelled));
            if (list.Count > 0) scc[Key(d)] = list;
        }

        return (scc, hcc);
    }

    [Fact]
    public void Capacity_is_SCC_getCapacity_and_canAddLeave_for_every_mix_of_up_to_six_records()
    {
        var r = new Random(4110);
        for (var i = 0; i < 5000; i++)
        {
            var (scc, hcc) = World(r, new DateOnly(2026, 11, 9), 1, 6, cancelledInto: "x");
            var day = scc.GetValueOrDefault("2026-11-09") ?? [];
            var o = Scc.GetCapacity(day);
            var c = SccLeave.Capacity(hcc);
            Assert.Equal((o.AmUsed, o.PmUsed, o.DayUsed, o.DayFull, o.AmFull, o.PmFull, o.BothFull, o.CanAddFullDay, o.CanAddAM, o.CanAddPM),
                (c.AmUsed, c.PmUsed, c.DayUsed, c.DayFull, c.AmFull, c.PmFull, c.BothFull, c.CanAddFullDay, c.CanAddAm, c.CanAddPm));
            foreach (var t in Types) Assert.Equal(Scc.CanAddLeave(day, t), c.CanAdd(Half(t)));
        }
    }

    [Fact]
    public void Request_plan_is_SCC_submitLeave_dates_counts_and_toast_over_a_random_grid()
    {
        var r = new Random(4111);
        var start = new DateOnly(2026, 11, 2);
        for (var i = 0; i < 3000; i++)
        {
            var (scc, hcc) = World(r, start, 12, 4, cancelledInto: "x");
            var weekOff = WeekOff(r);
            var staff = r.Next(1, 7);
            var from = start.AddDays(r.Next(12));
            var to = from.AddDays(r.Next(6));
            var type = Types[r.Next(3)];
            var o = Scc.SubmitLeave(scc, weekOff, "S" + staff.ToString(CultureInfo.InvariantCulture), Key(from), Key(to), type);
            var p = SccLeave.Plan(SccLeave.Mask(weekOff), hcc, staff, from, to, Half(type));
            Assert.Equal(o.Toast, p.Toast);
            Assert.Equal(o.Added, p.Dates.Where(x => x.Result == LeaveDateResult.Added).Select(x => Key(x.Date)).ToList());
        }
    }

    [Fact]
    public void Requested_days_are_SCC_requestedLeaveDays_with_the_400_day_guard()
    {
        var r = new Random(4112);
        for (var i = 0; i < 3000; i++)
        {
            var weekOff = WeekOff(r);
            var from = new DateOnly(2026, 4, 1).AddDays(r.Next(365));
            var to = from.AddDays(r.Next(-3, i % 10 == 0 ? 500 : 40));
            var type = Types[r.Next(3)];
            Assert.Equal(Scc.RequestedLeaveDays(weekOff, Key(from), Key(to), type), SccLeave.RequestedDays(SccLeave.Mask(weekOff), from, to, Half(type)));
        }

        Assert.Equal(400m, SccLeave.RequestedDays(0, new DateOnly(2026, 4, 1), new DateOnly(2028, 4, 1), null));   // SCC looks at 400 dates at most
    }

    [Fact]
    public void FY_usage_counts_approved_and_legacy_records_by_date_only_and_matches_the_balance_confirm_text()
    {
        var r = new Random(4113);
        for (var i = 0; i < 1500; i++)
        {
            var (scc, hcc) = World(r, new DateOnly(2027, 3, 20), 25, 3, cancelledInto: "x");   // across 31 March 2027
            var staff = r.Next(1, 7);
            var name = "S" + staff.ToString(CultureInfo.InvariantCulture);
            var on = new DateOnly(2027, 3, 20).AddDays(r.Next(25));
            var fy = FinancialYear.Containing(on);
            var range = Scc.FyRangeOf(on.Year, on.Month - 1);
            var o = Scc.FyUsage(scc, name, (range.From, range.To));
            var u = SccLeave.FyUsage(hcc, staff, fy);
            foreach (var (t, cat) in Cats) Assert.Equal(o.GetValueOrDefault(cat), u.GetValueOrDefault(t));

            // The confirm: a random quota (halves allowed), override or default, tracked categories only.
            var (type, category) = Cats[r.Next(Cats.Length)];
            var quota = r.Next(0, 9) / 2m;
            var policy = LeavePolicy.Defaults() with { Overrides = new Dictionary<long, LeaveEntOverride> { [staff] = new(quota, quota, quota) }, WeekOffMask = SccLeave.Mask(WeekOff(r)) };
            var to = on.AddDays(r.Next(5));
            var kind = Types[r.Next(3)];
            var tracked = type == LeaveType.CL || type == LeaveType.SL || type == LeaveType.EL;
            Assert.Equal(Scc.BalanceConfirm(scc, tracked ? quota : null, name, category, SccLeave.Days(policy.WeekOffMask).ToArray(), Key(on), Key(to), kind),
                SccLeave.OverBalanceText(policy, hcc, staff, name, type, on, to, Half(kind)));
        }
    }

    [Fact]
    public void Blackout_gate_is_SCC_hard_toast_then_soft_confirm_with_labels_in_first_seen_order()
    {
        var r = new Random(4114);
        string[] stores = ["", "HK", "FO"];
        var start = new DateOnly(2026, 12, 20);
        for (var i = 0; i < 2000; i++)
        {
            var entries = new List<Scc.B>();
            var rows = new List<LeaveBlackout>();
            for (var n = r.Next(4); n > 0; n--)
            {
                var f = start.AddDays(r.Next(15));
                var t = f.AddDays(r.Next(4));
                var kind = r.Next(3) == 0 ? "holiday" : "blackout";
                var hard = kind == "blackout" && r.Next(3) == 0;
                var label = new[] { "", "Diwali rush", "Year end", "Audit" }[r.Next(4)];
                var store = stores[r.Next(3)];
                entries.Add(new Scc.B(Key(f), Key(t), store, kind, hard, label));
                rows.Add(new LeaveBlackout(n, f, t, store, kind, hard, label));
            }

            var weekOff = WeekOff(r);
            var policy = LeavePolicy.Defaults() with { Blackouts = rows, WeekOffMask = SccLeave.Mask(weekOff) };
            var from = start.AddDays(r.Next(15));
            var to = from.AddDays(r.Next(5));
            var dept = stores[1 + r.Next(2)];
            var (type, category) = Cats[r.Next(Cats.Length)];
            var o = Scc.BlackoutGate(entries, weekOff, dept, Key(from), Key(to), category);
            var b = SccLeave.BlackoutHits(policy, from, to, dept);
            Assert.Equal(o.Hard, b.Hard);
            Assert.Equal(o.Text, b.Hits.Count == 0 ? null : b.Hard ? SccLeave.HardBlackoutText(b.Labels) : SccLeave.SoftBlackoutText(b.Labels, type));
        }
    }

    [Fact]
    public void Week_off_mask_maps_SCC_day_numbers_and_the_SCC_default_is_Saturday_and_Sunday()
    {
        Assert.Equal(SccLeave.SccWeekOffDefault, SccLeave.Mask([0, 6]));
        Assert.Equal([0, 6], SccLeave.Days(SccLeave.SccWeekOffDefault));
        Assert.Equal(0, LeavePolicy.Defaults().WeekOffMask);   // HCC default: no day ticked (owner question Q1)
        Assert.True(SccLeave.IsWeekOff(SccLeave.SccWeekOffDefault, new DateOnly(2026, 11, 8)));    // Sunday
        Assert.False(SccLeave.IsWeekOff(SccLeave.SccWeekOffDefault, new DateOnly(2026, 11, 9)));   // Monday
        Assert.Equal(("Full Day", "Half Day AM", "Half Day PM"), (SccLeave.KindLabel(null), SccLeave.KindLabel(LeaveHalf.AM), SccLeave.KindLabel(LeaveHalf.PM)));
        Assert.Equal(("12", "7.5", "0.3"), (SccLeave.Num(12m), SccLeave.Fmt(7.5m), SccLeave.Fmt(0.25m)));
    }
}
