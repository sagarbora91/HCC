using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcc.Domain.Dates;
using Hcc.Domain.Money;

namespace Hcc.Domain.Yield;

/// <summary>
/// Port of the prototype's pure yield rules (<c>03 Prototype/src/domain/streams/yield.ts</c>): base → season → weekday → forecast
/// occupancy → guardrail → rounding → (override) → suggested, rate plans, quotes with restrictions, and the publish plan.
/// Money is <c>long</c> paise, adjustments are basis points, percentages round half away from zero (JS Math.round on positives).
/// </summary>
public sealed class YieldEngine(YieldConfig config, YieldInputs inputs)
{
    public YieldConfig Config { get; } = config;

    public YieldInputs Inputs { get; } = inputs;

    private YieldRoomType Type(int id) => Inputs.Types.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown room type.");

    public static int DayOfWeek(DateOnly d) => (int)d.DayOfWeek;

    /// <summary>Paise × (10000 + bps) / 10000, half away from zero.</summary>
    public static long Adjust(long paise, int bps) => MoneyMath.DivideRoundHalfAwayFromZero(checked(paise * (10000L + bps)), 10000L);

    /// <summary>Round to a unit (minimum one unit): max(unit, round(p / unit) × unit).</summary>
    public static long Round(long paise, long unit) => Math.Max(unit, MoneyMath.DivideRoundHalfAwayFromZero(paise, unit) * unit);

    private static TariffRow? Latest(IEnumerable<TariffRow> rows) => rows.OrderBy(r => r.EffectiveFrom).LastOrDefault();

    public bool IsOwned(TariffRow t) => Inputs.Owned.Contains(t);

    /// <summary>Base rate: latest manual (non-yield) tariff with effective date ≤ the night, else the room-type rack rate.</summary>
    public (long Rate, string Source) Base(int roomTypeId, DateOnly date)
    {
        var t = Latest(Inputs.Tariffs.Where(x => x.RoomTypeId == roomTypeId && x.EffectiveFrom <= date && !IsOwned(x)));
        return t is null ? (Type(roomTypeId).RackPaise, "rack") : (t.RatePaise, "tariff");
    }

    /// <summary>Live tariff (what P2's quote uses): latest tariff row of any origin, else rack.</summary>
    public long Live(int roomTypeId, DateOnly date) => RateIn(Inputs.Tariffs, roomTypeId, date);

    private long RateIn(IEnumerable<TariffRow> rows, int roomTypeId, DateOnly date)
        => Latest(rows.Where(x => x.RoomTypeId == roomTypeId && x.EffectiveFrom <= date))?.RatePaise ?? Type(roomTypeId).RackPaise;

    /// <summary>Forecast occupancy: max(on the books, simulated forecast) over sellable rooms, rounded to a whole percent.</summary>
    public YieldOcc Occ(DateOnly date)
    {
        var sellable = Inputs.Sellable.GetValueOrDefault(date);
        var onBooks = Inputs.OnBooks.GetValueOrDefault(date);
        var forecast = Inputs.Forecast.GetValueOrDefault(date);
        var sold = Math.Max(onBooks, forecast);
        var pct = sellable == 0 ? 0 : (int)Math.Min(100, MoneyMath.DivideRoundHalfAwayFromZero(sold * 100L, sellable));
        return new YieldOcc(onBooks, forecast, sellable, pct, forecast > onBooks);
    }

    public YieldSeason? SeasonFor(int roomTypeId, DateOnly date)
        => Config.Seasons.Where(x => x.From <= date && x.To >= date && (x.RoomTypeIds.Count == 0 || x.RoomTypeIds.Contains(roomTypeId)))
            .Aggregate((YieldSeason?)null, (best, x) => best is null || x.Priority >= best.Priority ? x : best);

    public IReadOnlyList<YieldOccRule> OccRulesFor(int pct)
    {
        var up = Config.OccRules.Where(r => r.Op == ">=" && pct >= r.Occ).OrderByDescending(r => r.Occ).FirstOrDefault();
        var down = Config.OccRules.Where(r => r.Op == "<" && pct < r.Occ).OrderBy(r => r.Occ).FirstOrDefault();
        return new[] { up, down }.Where(r => r is not null).Select(r => r!).ToList();
    }

    public IReadOnlyList<YieldRestrictionDef> RestrictionsFor(int roomTypeId, DateOnly date)
        => Config.Restrictions.Where(r => (r.RoomTypeId is null || r.RoomTypeId == roomTypeId) && r.From <= date && r.To >= date).ToList();

    public YieldNight Night(int roomTypeId, DateOnly date)
    {
        var type = Type(roomTypeId);
        var b = Base(roomTypeId, date);
        var occ = Occ(date);
        var steps = new List<YieldStep>();
        var season = SeasonFor(roomTypeId, date);
        if (season is not null && season.AdjBps != 0)
            steps.Add(new YieldStep("Season · " + season.Name, season.AdjBps,
                $"{StayDates.ToWire(season.From)} to {StayDates.ToWire(season.To)}" + (season.RoomTypeIds.Count > 0 ? " · " + string.Join(", ", season.RoomTypeIds.Select(i => Type(i).Code)) : "")));
        var dw = DayOfWeek(date);
        var dowBps = dw < Config.Dow.Count ? Config.Dow[dw] : 0;
        if (dowBps != 0) steps.Add(new YieldStep("Day of week · " + YieldFormat.DowNames[dw], dowBps, "Weekday rule"));
        foreach (var r in OccRulesFor(occ.Pct).Where(r => r.AdjBps != 0))
            steps.Add(new YieldStep($"Forecast occupancy {occ.Pct}% {(r.Op == ">=" ? "≥" : "<")} {r.Occ}%", r.AdjBps,
                $"{Math.Max(occ.OnBooks, occ.Forecast)} of {occ.Sellable} sellable rooms{(occ.Simulated ? " (simulated forecast)" : " on the books")}"));
        var raw = steps.Sum(s => s.Bps);
        var applied = Math.Max(Config.Guard.FloorBps, Math.Min(Config.Guard.CeilBps, raw));
        var clamped = applied != raw ? (raw < applied ? "floor" : "ceiling") : null;
        var computed = Round(Adjust(b.Rate, applied), Config.Guard.RoundToPaise);
        var over = Config.Overrides.FirstOrDefault(o => o.RoomTypeId == roomTypeId && o.Night == date);
        var suggested = over?.RatePaise ?? computed;
        var parts = new List<string> { "Base " + YieldFormat.Money(b.Rate) };
        parts.AddRange(steps.Select(s => $"{s.Label} {YieldFormat.Pct(s.Bps)}"));
        if (clamped is not null) parts.Add($"capped at {clamped} {YieldFormat.Pct(applied)}");
        parts.Add("rounded → " + YieldFormat.Money(computed));
        if (over is not null) parts.Add($"manual override {YieldFormat.Money(over.RatePaise)} ({over.Reason})");
        return new YieldNight(roomTypeId, type.Code, date, b.Rate, b.Source, steps, raw, applied, clamped, computed, over?.RatePaise, suggested, Live(roomTypeId, date), occ,
            RestrictionsFor(roomTypeId, date), string.Join(" · ", parts));
    }

    public IReadOnlyList<YieldNight> Calendar(DateOnly from, int nights)
        => Inputs.Types.SelectMany(t => Enumerable.Range(0, nights).Select(i => Night(t.Id, from.AddDays(i)))).ToList();

    // ───────────── rate plans and quotes ─────────────
    public (bool Ok, string? Reason) PlanAvailable(YieldPlan p, DateOnly date, int occPct)
    {
        if (!p.Active) return (false, "Plan inactive");
        if (p.SellOnDays is not null && !p.SellOnDays.Contains(DayOfWeek(date))) return (false, "Not sold on " + YieldFormat.DowNames[DayOfWeek(date)]);
        if (p.CloseAtOcc is { } close && occPct >= close) return (false, $"Closed: forecast {occPct}% ≥ {close}%");
        return (true, null);
    }

    public long PlanRate(YieldPlan p, long suggested) => Round(Adjust(suggested, p.AdjBps), Config.Guard.RoundToPaise);

    /// <summary>Quote a stay (or a day-use visit) and list every restriction/plan rule it breaks (prototype yldQuote).</summary>
    public YieldQuote Quote(string planCode, int roomTypeId, DateOnly arrival, int nights)
    {
        var plan = Config.Plans.FirstOrDefault(p => p.Code == planCode) ?? Config.Plans[0];
        var issues = new List<string>();
        var count = plan.IsDayUse ? 1 : Math.Max(1, nights);
        if (arrival < Inputs.BusinessDate) issues.Add("Arrival is before the business date.");
        var rows = new List<YieldQuoteLine>();
        for (var i = 0; i < count; i++)
        {
            var date = arrival.AddDays(i);
            var nt = Night(roomTypeId, date);
            var a = PlanAvailable(plan, date, nt.Occ.Pct);
            if (!a.Ok) issues.Add($"{StayDates.ToWire(date)}: {a.Reason}");
            rows.Add(new YieldQuoteLine(date, PlanRate(plan, nt.Suggested), nt.Suggested));
        }

        if (!plan.IsDayUse)
        {
            if (count < plan.MinNights) issues.Add($"{plan.Name} needs at least {plan.MinNights} nights.");
            foreach (var r in RestrictionsFor(roomTypeId, arrival))
            {
                if (r.ClosedToArrival) issues.Add($"Closed to arrival on {StayDates.ToWire(arrival)}{(r.Note.Length > 0 ? " (" + r.Note + ")" : "")}.");
                if (count < r.MinStay) issues.Add($"Minimum stay {r.MinStay} nights for arrivals {StayDates.ToWire(r.From)} to {StayDates.ToWire(r.To)}.");
            }
        }

        return new YieldQuote(plan, roomTypeId, arrival, plan.IsDayUse ? 0 : count, rows, rows.Sum(r => r.Rate), issues);
    }

    // ───────────── publishing ─────────────
    /// <summary>
    /// Plans a publish without changing anything: a tariff row on each night whose live rate differs, plus a closing row on
    /// <paramref name="to"/>+1 that keeps the rate after the window unchanged. <paramref name="target"/> defaults to the suggested rate.
    /// </summary>
    public YieldPublishPlan PlanPublish(DateOnly from, DateOnly to, IEnumerable<int> roomTypeIds, Func<int, DateOnly, long>? target = null)
    {
        var list = Inputs.Tariffs.ToList();
        var orig = Inputs.Tariffs;
        var rows = new List<YieldPublishRow>();
        void Put(int type, DateOnly d, long rate)
        {
            list.RemoveAll(t => t.RoomTypeId == type && t.EffectiveFrom == d);
            list.Add(new TariffRow(type, d, rate));
        }

        foreach (var type in roomTypeIds)
        {
            var after = to.AddDays(1);
            var keep = RateIn(orig, type, after);
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var t = target is null ? Night(type, d).Suggested : target(type, d);
                if (RateIn(list, type, d) != t)
                {
                    rows.Add(new YieldPublishRow(type, d, RateIn(orig, type, d), t, false));
                    Put(type, d, t);
                }
            }

            if (RateIn(list, type, after) != keep)
            {
                rows.Add(new YieldPublishRow(type, after, keep, keep, true));
                Put(type, after, keep);
            }
        }

        return new YieldPublishPlan(rows, list);
    }

    /// <summary>Nights in the window whose suggested rate differs from the live tariff.</summary>
    public int PendingNights(DateOnly from, int nights) => Calendar(from, nights).Count(n => n.Suggested != n.Live);

    /// <summary>Signature of the tariff rows of the given types: a proposal is stale once it changes.</summary>
    public static string TariffSignature(IEnumerable<TariffRow> tariffs, IEnumerable<int> roomTypeIds)
    {
        var types = roomTypeIds.ToHashSet();
        var text = string.Join("|", tariffs.Where(t => types.Contains(t.RoomTypeId)).OrderBy(t => t.RoomTypeId).ThenBy(t => t.EffectiveFrom)
            .Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.RoomTypeId},{StayDates.ToWire(t.EffectiveFrom)},{t.RatePaise}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public YieldSummaryFigures Summary(DateOnly from, int windowNights)
    {
        var week = Calendar(from, 7);
        var avg = week.Count == 0 ? 0 : MoneyMath.DivideRoundHalfAwayFromZero(week.Sum(n => n.Suggested), week.Count * 100L) * 100L;
        var high = Enumerable.Range(0, windowNights).Count(i => Occ(from.AddDays(i)).Pct >= 80);
        return new YieldSummaryFigures(avg, high, PendingNights(from, windowNights));
    }
}

public sealed record YieldSummaryFigures(long AverageBar7Paise, int HighDemandNights, int PendingNights);
