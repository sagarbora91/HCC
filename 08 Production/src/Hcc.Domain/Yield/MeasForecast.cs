namespace Hcc.Domain.Yield;

/// <summary>One stay as the forecast sees it (reservations, legacy <c>lgc</c> stays). <c>BookedOn</c> null = arrival − 14 days.</summary>
public sealed record MeasFcStay(string Room, DateOnly From, DateOnly To, DateOnly? BookedOn = null, DateOnly? CancelledOn = null, bool NoShow = false);

/// <summary>A local event (meas.event) that lifts or lowers demand on the nights it covers.</summary>
public sealed record MeasFcEvent(DateOnly From, DateOnly To, int ImpactPct);

public sealed record MeasFcNight(DateOnly Date, int Rooms, int Pct, int OnBooks, int BaselinePct, string BaselineSource, int? Pickup, int? PaceFactorBps, int EventPct, int Sellable);

public sealed record MeasFcBacktest(int Nights, decimal ForecastPct, decimal ActualPct, decimal DiffPts);

public sealed record MeasDispNight(DateOnly Date, int Sellable, int ForecastRooms, long AdrPaise);

public sealed record MeasDispRow(DateOnly Date, int Free, int Displaced, long DisplacedPaise, long GroupPaise);

public sealed record MeasDisplacement(IReadOnlyList<MeasDispRow> Rows, int GroupRoomNights, long GroupPaise, long DisplacedPaise, long NetPaise, string Decision, long BreakEvenRatePaise);

/// <summary>
/// F12 MEAS meas-forecast: port of <c>03 Prototype/src/domain/forecast.ts</c> (same rules, same arithmetic; JS <c>Math.round</c> on
/// non-negative values = round half up, which <see cref="RoundHalfUp"/> reproduces). Real forecast curve (plan 2.2): same weekday one and
/// two years back (±1 and ±2 weeks, weights 2 : 1), else the trailing 8-week weekday mean; pickup from last year's pace blended 50 / 50;
/// event uplift; never below the rooms on the books. Displacement (2.4) in integer paise with the break-even rate rounded up to ₹50.
/// </summary>
public static class MeasForecast
{
    public const int DefaultLeadDays = 14;

    public static int RoundHalfUp(double v) => (int)Math.Floor(v + 0.5);

    public static Dictionary<DateOnly, int> NightCounts(IEnumerable<MeasFcStay> stays)
    {
        var per = new Dictionary<DateOnly, HashSet<string>>();
        foreach (var x in stays)
        {
            if (x.CancelledOn is not null || x.NoShow) continue;
            for (var d = x.From; d < x.To; d = d.AddDays(1))
            {
                if (!per.TryGetValue(d, out var set)) per[d] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(x.Room);
            }
        }
        return per.ToDictionary(p => p.Key, p => p.Value.Count);
    }

    public static int OnBooks(IEnumerable<MeasFcStay> stays, DateOnly night, DateOnly asOf)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in stays)
        {
            if (x.From > night || x.To <= night) continue;
            if ((x.BookedOn ?? x.From.AddDays(-DefaultLeadDays)) > asOf) continue;
            if (x.CancelledOn is { } c && c <= asOf) continue;
            if (x.NoShow && night < asOf) continue;
            set.Add(x.Room);
        }
        return set.Count;
    }

    public static int EventPct(IEnumerable<MeasFcEvent>? events, DateOnly date) =>
        Math.Clamp((events ?? []).Where(e => e.From <= date && e.To >= date).Sum(e => e.ImpactPct), -50, 100);

    public static MeasFcNight ForecastNight(IReadOnlyList<MeasFcStay> stays, int sellable, DateOnly asOf, DateOnly date,
        IReadOnlyList<MeasFcEvent>? events = null, DateOnly? historyFrom = null, Dictionary<DateOnly, int>? counts = null)
    {
        counts ??= NightCounts(stays);
        var rooms = Math.Max(0, sellable);
        var from = historyFrom ?? stays.Select(s => s.From).Append(asOf).Min();
        bool Known(DateOnly d) => d >= from && d < asOf;
        double PctOf(DateOnly d) => rooms == 0 ? 0 : (counts.TryGetValue(d, out var n) ? n : 0) * 100.0 / rooms;

        var onBooks = Math.Min(rooms, OnBooks(stays, date, asOf));
        double wsum = 0, acc = 0;
        foreach (var (k, w) in new[] { (1, 2), (2, 1) })
        {
            var xs = new[] { -14, -7, 0, 7, 14 }.Select(o => date.AddDays(-364 * k + o)).Where(Known).ToList();
            if (xs.Count > 0) { acc += w * xs.Average(PctOf); wsum += w; }
        }
        double basePct; string source;
        if (wsum > 0) { basePct = acc / wsum; source = "last-years"; }
        else
        {
            var tr = Enumerable.Range(1, 56).Select(i => asOf.AddDays(-i)).Where(d => d.DayOfWeek == date.DayOfWeek && Known(d)).ToList();
            if (tr.Count > 0) { basePct = tr.Average(PctOf); source = "trailing"; } else { basePct = 0; source = "none"; }
        }
        var baseRooms = basePct * rooms / 100;
        var lead = date.DayNumber - asOf.DayNumber;
        int? pickup = null, pace = null;
        var blend = baseRooms;
        var ly = date.AddDays(-364);
        if (lead > 0 && Known(ly))
        {
            var lyFinal = counts.TryGetValue(ly, out var f) ? f : 0;
            var lyOn = OnBooks(stays, ly, asOf.AddDays(-364));
            pickup = Math.Max(0, lyFinal - lyOn);
            pace = RoundHalfUp((onBooks + 1) * 10000.0 / (lyOn + 1));
            blend = (onBooks + pickup.Value + baseRooms) / 2;
        }
        else if (lead <= 0) blend = Math.Max(baseRooms, onBooks);
        var ev = EventPct(events, date);
        var sold = Math.Clamp(Math.Max(onBooks, RoundHalfUp(blend * (100 + ev) / 100)), 0, rooms);
        var pct = rooms == 0 ? 0 : RoundHalfUp(sold * 100.0 / rooms);
        return new MeasFcNight(date, sold, pct, onBooks, RoundHalfUp(basePct), source, pickup, pace, ev, rooms);
    }

    public static MeasFcBacktest Backtest(IReadOnlyList<MeasFcStay> stays, int rooms, DateOnly from, DateOnly to, int leadDays = 7, DateOnly? historyFrom = null)
    {
        var counts = NightCounts(stays);
        long f = 0, a = 0; var n = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            f += ForecastNight(stays, rooms, d.AddDays(-leadDays), d, null, historyFrom, counts).Rooms;
            a += counts.TryGetValue(d, out var c) ? c : 0;
            n++;
        }
        decimal R1(decimal v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
        var fp = n == 0 || rooms == 0 ? 0m : f * 100m / (n * rooms);
        var ap = n == 0 || rooms == 0 ? 0m : a * 100m / (n * rooms);
        return new MeasFcBacktest(n, R1(fp), R1(ap), R1(fp - ap));
    }

    public static MeasDisplacement Displacement(IReadOnlyList<MeasDispNight> nights, int rooms, long groupRatePaise, long ancillaryPerRoomNightPaise = 0)
    {
        if (rooms < 0 || groupRatePaise < 0) throw new ArgumentOutOfRangeException(nameof(rooms), "Displacement inputs must be whole numbers (paise).");
        var rows = nights.Select(n =>
        {
            var free = Math.Max(0, n.Sellable - n.ForecastRooms);
            var displaced = Math.Max(0, rooms - free);
            return new MeasDispRow(n.Date, free, displaced, displaced * n.AdrPaise, rooms * (groupRatePaise + ancillaryPerRoomNightPaise));
        }).ToList();
        var roomNights = rooms * nights.Count;
        var group = rows.Sum(r => r.GroupPaise);
        var displacedPaise = rows.Sum(r => r.DisplacedPaise);
        var net = group - displacedPaise;
        var need = Math.Max(0, displacedPaise - ancillaryPerRoomNightPaise * roomNights);
        long breakEven = 0;
        if (roomNights > 0)
        {
            var perNight = (need + roomNights - 1) / roomNights;
            breakEven = (perNight + 4999) / 5000 * 5000;
        }
        return new MeasDisplacement(rows, roomNights, group, displacedPaise, net, net >= 0 ? "accept" : "counter", breakEven);
    }
}
