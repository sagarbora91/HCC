using Hcc.Domain.Yield;

namespace Hcc.Yield.Tests.Domain;

/// <summary>F12 MEAS meas-forecast: the C# port gives the prototype's figures (tests/stream-meas-forecast.mjs, same worked examples).</summary>
public sealed class MeasForecastTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    private static MeasFcStay St(string room, string from, string to, string booked, string? cancelled = null, bool noShow = false) =>
        new(room, D(from), D(to), D(booked), cancelled is null ? null : D(cancelled), noShow);

    [Fact]
    public void Night_counts_and_on_the_books_follow_booking_and_cancellation_dates()
    {
        var xs = new[]
        {
            St("101", "2026-01-01", "2026-01-03", "2025-12-20"), St("101", "2026-01-02", "2026-01-03", "2025-12-21"), St("102", "2026-01-02", "2026-01-03", "2026-01-01"),
            St("103", "2026-01-02", "2026-01-04", "2025-12-01", "2025-12-25"), St("104", "2026-01-02", "2026-01-03", "2025-12-01", noShow: true),
        };
        var c = MeasForecast.NightCounts(xs);
        Assert.Equal(1, c[D("2026-01-01")]);
        Assert.Equal(2, c[D("2026-01-02")]);
        Assert.Equal(4, MeasForecast.OnBooks(xs, D("2026-01-02"), D("2025-12-24")));
        Assert.Equal(2, MeasForecast.OnBooks(xs, D("2026-01-02"), D("2025-12-31")));
        Assert.Equal(3, MeasForecast.OnBooks(xs, D("2026-01-02"), D("2026-01-01")));
    }

    [Fact]
    public void Baseline_last_two_years_pickup_and_event_uplift_match_the_prototype()
    {
        var xs = new List<MeasFcStay>();
        for (var d = D("2024-01-01"); d < D("2026-01-01"); d = d.AddDays(1))
        {
            var y = d < D("2025-01-01") ? 3 : 6;
            for (var r = 0; r < y; r++) xs.Add(new MeasFcStay((100 + r).ToString(System.Globalization.CultureInfo.InvariantCulture), d, d.AddDays(1), d.AddDays(-30)));
        }
        var g = MeasForecast.ForecastNight(xs, 10, D("2026-01-01"), D("2026-06-15"));
        Assert.Equal("last-years", g.BaselineSource);
        Assert.Equal(50, g.BaselinePct);
        Assert.Equal(6, g.Pickup);
        Assert.Equal(6, g.Rooms);
        var up = MeasForecast.ForecastNight(xs, 10, D("2026-01-01"), D("2026-06-15"), [new MeasFcEvent(D("2026-06-15"), D("2026-06-15"), 50)]);
        Assert.Equal(8, up.Rooms);
        var tr = MeasForecast.ForecastNight(xs.Where(x => x.From >= D("2025-11-01")).ToList(), 10, D("2026-01-01"), D("2026-01-05"), historyFrom: D("2025-11-01"));
        Assert.Equal("trailing", tr.BaselineSource);
        Assert.Equal(60, tr.BaselinePct);
        Assert.Equal("none", MeasForecast.ForecastNight([], 10, D("2026-01-01"), D("2026-01-05")).BaselineSource);
        Assert.Equal(100, MeasForecast.EventPct([new MeasFcEvent(D("2026-01-01"), D("2026-01-02"), 90), new MeasFcEvent(D("2026-01-01"), D("2026-01-01"), 40)], D("2026-01-01")));
        Assert.Equal(-50, MeasForecast.EventPct([new MeasFcEvent(D("2026-01-01"), D("2026-01-02"), -90)], D("2026-01-02")));
    }

    [Fact]
    public void Pickup_uses_last_years_pace_and_never_drops_below_the_books()
    {
        var xs = new List<MeasFcStay>();
        for (var r = 0; r < 6; r++) xs.Add(St((200 + r).ToString(System.Globalization.CultureInfo.InvariantCulture), "2025-03-10", "2025-03-11", r < 2 ? "2025-02-01" : "2025-03-08"));
        xs.Add(St("300", "2026-03-09", "2026-03-10", "2026-02-15"));
        xs.Add(St("301", "2026-03-09", "2026-03-10", "2026-02-15"));
        xs.Add(St("302", "2026-03-09", "2026-03-10", "2026-02-15"));
        var f = MeasForecast.ForecastNight(xs, 10, D("2026-03-02"), D("2026-03-09"));
        Assert.Equal(3, f.OnBooks);
        Assert.Equal(4, f.Pickup);
        Assert.Equal(5, f.Rooms); // (3 + 4 + 2) ÷ 2 = 4.5 → 5
        Assert.True(f.PaceFactorBps > 10000);
    }

    [Fact]
    public void Displacement_accept_counter_and_break_even_rounded_up_to_fifty_rupees()
    {
        var nights = new[] { new MeasDispNight(D("2026-12-04"), 30, 25, 200000), new MeasDispNight(D("2026-12-05"), 30, 28, 200000) };
        var a = MeasForecast.Displacement(nights, 10, 150000);
        Assert.Equal([5, 8], a.Rows.Select(r => r.Displaced));
        Assert.Equal(2600000, a.DisplacedPaise);
        Assert.Equal(3000000, a.GroupPaise);
        Assert.Equal(400000, a.NetPaise);
        Assert.Equal("accept", a.Decision);
        var c = MeasForecast.Displacement(nights, 10, 100000);
        Assert.Equal(-600000, c.NetPaise);
        Assert.Equal("counter", c.Decision);
        Assert.Equal(130000, c.BreakEvenRatePaise);
        var d = MeasForecast.Displacement(nights, 10, 100000, 20000);
        Assert.Equal(2400000, d.GroupPaise);
        Assert.Equal(110000, d.BreakEvenRatePaise);
        Assert.Equal(125000, MeasForecast.Displacement([new MeasDispNight(D("2026-12-04"), 10, 10, 123457)], 3, 0).BreakEvenRatePaise);
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasForecast.Displacement(nights, -1, 100000));
    }

    [Fact]
    public void Backtest_on_a_flat_history_is_exact()
    {
        var xs = new List<MeasFcStay>();
        for (var d = D("2024-04-01"); d <= D("2026-03-31"); d = d.AddDays(1))
            for (var r = 0; r < 4; r++) xs.Add(new MeasFcStay((100 + r).ToString(System.Globalization.CultureInfo.InvariantCulture), d, d.AddDays(1), d.AddDays(-10)));
        var bt = MeasForecast.Backtest(xs, 10, D("2025-04-01"), D("2026-03-31"), 7, D("2024-04-01"));
        Assert.Equal(365, bt.Nights);
        Assert.Equal(40m, bt.ActualPct);
        Assert.True(Math.Abs(bt.DiffPts) <= 2m, bt.ToString());
    }
}
