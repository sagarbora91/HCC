using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Tax;
using Hcc.GstIds.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Tax;
using Microsoft.Data.SqlClient;

namespace Hcc.GstIds.Tests.Gst;

/// <summary>
/// Wave C7 (found by the Wave C room-board probe): the "specified premises" derivation (<see cref="SqlGstMasterStore.MaxUnitTariffSql"/>)
/// read every room night of the previous FY with its folio and reservation (the A03a-02 reversed-night exclusion added that branch):
/// about 15,000 page reads on average and up to 119,000 / 1.34 s on a 240-day seed, and it runs for every food charge. It now walks the
/// room nights from the highest amount down and stops at the first one of the year that belongs to the property and was not reversed
/// (migration 3951). Same result as the 70c2713 statement for every year and property, reversed nights included; far fewer reads.
/// Own database: about 18,800 room nights over FY 2024-25 … FY 2026-27 plus a second property.
/// </summary>
[Trait("Category", "Sql")]
public sealed partial class GstSpecifiedPremisesPerfTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    /// <summary>
    /// Page reads the new statement may take on this fixture (tolerant: it reads about 200, most of them skipping the second hotel's 20
    /// dearer nights; a one-property hotel reads fewer).
    /// </summary>
    private const long ReadsCeiling = 1_000;

    /// <summary>The statement as shipped at 70c2713 (fix wave A, A03a-02), for the equivalence and the before/after reads.</summary>
    private const string LegacySql = """
        SELECT MAX(v) FROM (
          SELECT COALESCE((SELECT TOP (1) t.rate_paise FROM inv.room_type_tariff t
                           WHERE t.room_type_id = rt.room_type_id AND t.effective_from <= @from ORDER BY t.effective_from DESC), rt.rack_rate_paise) AS v
          FROM inv.room_type rt WHERE rt.property_id = @p AND EXISTS (SELECT 1 FROM inv.room r WHERE r.room_type_id = rt.room_type_id)
          UNION ALL
          SELECT t.rate_paise FROM inv.room_type_tariff t JOIN inv.room_type rt ON rt.room_type_id = t.room_type_id
          WHERE rt.property_id = @p AND t.effective_from > @from AND t.effective_from <= @to
            AND EXISTS (SELECT 1 FROM inv.room r WHERE r.room_type_id = rt.room_type_id)
          UNION ALL
          SELECT c.amount_paise FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation res ON res.reservation_id = f.reservation_id
          WHERE res.property_id = @p AND c.kind = 'room' AND c.reverses_charge_id IS NULL AND c.service_date BETWEEN @from AND @to
            AND NOT EXISTS (SELECT 1 FROM bill.charge r WHERE r.reverses_charge_id = c.charge_id)
        ) x;
        """;

    private const string SeedSql = """
        DECLARE @start DATE = '2024-04-01';
        -- 936 business days × 20 one-night stays (1 Apr 2024 … 23 Oct 2026); ₹3,000 … ₹7,000 a night, ₹12,000 for every 7th stay this FY.
        WITH n AS (SELECT TOP (18720) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
        INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
        SELECT @p, CONCAT('PF', i), N'Fictional perf guest', DATEADD(day, i / 20, @start), DATEADD(day, i / 20 + 1, @start), 'Departed',
               CASE WHEN DATEADD(day, i / 20, @start) >= '2026-04-01' AND i % 7 = 0 THEN 1200000 ELSE 300000 + (i % 9) * 50000 END
        FROM n;
        -- Specials: last FY a ₹9,500 and a ₹9,000 night that were reversed (one by a correction line, one by a room-kind line) and a
        -- standing ₹7,300 night; FY 2024-25 a standing ₹8,000 night. Second property: 20 nights at ₹15,000 last FY.
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('PERF2', N'Second hotel (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p2 INT = SCOPE_IDENTITY();
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p2, 'Std', 2, N'Standard', 300000, 1);
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p2, '201', SCOPE_IDENTITY());
        DECLARE @x TABLE(no VARCHAR(20), p INT, d DATE, amt BIGINT, rev VARCHAR(12) NULL);
        INSERT @x VALUES ('PX1', @p, '2025-12-01', 950000, 'correction'), ('PX2', @p, '2025-12-02', 900000, 'room'), ('PX3', @p, '2026-01-15', 730000, NULL),
                         ('PX4', @p, '2024-06-01', 800000, NULL);
        INSERT @x SELECT TOP (20) CONCAT('PY', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), @p2, DATEADD(day, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '2025-05-01'), 1500000, NULL
                  FROM sys.all_objects;
        INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
        SELECT x.p, x.no, N'Fictional perf guest', x.d, DATEADD(day, 1, x.d), 'Departed', x.amt FROM @x x;
        INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no LIKE 'P[FXY]%';
        INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise)
        SELECT f.folio_id, 'room', r.arrival, N'Room night (fictional)', r.nightly_rate_paise, 1800, r.nightly_rate_paise * 18 / 100
        FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id WHERE r.public_no LIKE 'P[FXY]%';
        -- Two service lines per stay (restaurant, laundry): the table holds more than room nights, as a real one does.
        INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise)
        SELECT f.folio_id, 'service', r.arrival, s.label, s.amt, 500, s.amt / 20
        FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id
        CROSS JOIN (VALUES (N'Dinner (fictional)', 90000), (N'Laundry (fictional)', 30000)) s(label, amt) WHERE r.public_no LIKE 'PF%';
        INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id)
        SELECT c.folio_id, x.rev, c.service_date, N'Reversal (fictional)', -c.amount_paise, c.tax_bps, -c.tax_paise, c.charge_id
        FROM @x x JOIN inv.reservation r ON r.public_no = x.no AND r.property_id = x.p JOIN bill.folio f ON f.reservation_id = r.reservation_id
        JOIN bill.charge c ON c.folio_id = f.folio_id WHERE x.rev IS NOT NULL;
        -- A dated Suite tariff last FY (the tariff branches still count).
        INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) SELECT room_type_id, '2025-08-01', 500000 FROM inv.room_type WHERE property_id = @p AND code = 'Suite';
        UPDATE STATISTICS bill.charge; UPDATE STATISTICS bill.folio; UPDATE STATISTICS inv.reservation;
        SELECT @p2;
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"logical reads (\d+)")]
    private static partial Regex LogicalReads();

    [GeneratedRegex(@"elapsed time = (\d+) ms")]
    private static partial Regex Elapsed();

    private async Task<long> RunAsync(string sql, int propertyId, DateOnly from, DateOnly to, Action<string>? message = null)
    {
        await using var conn = await new SqlConnectionFactory(db.Options).OpenAsync(Ct);
        if (message is not null)
        {
            conn.InfoMessage += (_, e) => { foreach (SqlError x in e.Errors) message(x.Message); };
            await using var on = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", conn);
            await on.ExecuteNonQueryAsync(Ct);
        }

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        return await cmd.ExecuteScalarAsync(Ct) is long v ? v : 0L;
    }

    /// <summary>Median page reads and elapsed ms of 5 warm runs (1 warm-up) of one statement for last FY of the first property.</summary>
    private async Task<(long Reads, long Ms)> MeasureAsync(string sql)
    {
        var prev = FinancialYear.Of(GstIdsDb.Day).Previous;
        await RunAsync(sql, db.PropertyId, prev.From, prev.To);
        var runs = new List<(long Reads, long Ms)>();
        for (var i = 0; i < 5; i++)
        {
            var messages = new List<string>();
            await RunAsync(sql, db.PropertyId, prev.From, prev.To, messages.Add);
            var reads = messages.SelectMany(m => LogicalReads().Matches(m)).Sum(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            var ms = messages.SelectMany(m => Elapsed().Matches(m)).Sum(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            runs.Add((reads, ms));
        }

        return (runs.Select(r => r.Reads).Order().ElementAt(2), runs.Select(r => r.Ms).Order().ElementAt(2));
    }

    [Fact]
    public async Task Specified_premises_tariff_is_the_same_as_before_for_every_year_and_property_reversed_nights_included_and_reads_a_fraction()
    {
        var p2 = Convert.ToInt32(await db.ScalarAsync(SeedSql, ("@p", SqlDbType.Int, db.PropertyId)), CultureInfo.InvariantCulture);
        Assert.True(await db.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND reverses_charge_id IS NULL;") >= 18_744);

        // Same result as the 70c2713 statement: every FY of both properties, through the store and the raw statement.
        var expected = new Dictionary<(int, string), long>
        {
            [(db.PropertyId, "2024-25")] = 800_000,   // the standing ₹8,000 night
            [(db.PropertyId, "2025-26")] = 730_000,   // the ₹9,500 and ₹9,000 nights were reversed: the standing ₹7,300 night
            [(db.PropertyId, "2026-27")] = 1_200_000, // this FY's ₹12,000 nights
            [(db.PropertyId, "2020-21")] = 450_000,   // no nights: the Suite rack rate
            [(p2, "2025-26")] = 1_500_000,             // the second hotel's own nights, never the first one's
            [(p2, "2024-25")] = 300_000,
        };
        foreach (var ((p, fy), want) in expected)
        {
            var year = FinancialYear.Of(new DateOnly(int.Parse(fy[..4], CultureInfo.InvariantCulture), 4, 1));
            var legacy = await RunAsync(LegacySql, p, year.From, year.To);
            Assert.Equal(want, legacy);
            Assert.Equal(legacy, await RunAsync(SqlGstMasterStore.MaxUnitTariffSql, p, year.From, year.To));
            Assert.Equal(legacy, await db.GstStore.MaxUnitTariffAsync(null, p, year.From, year.To, Ct));
        }

        // So the reversed nights still never make last FY specified premises (restaurant stays at 5 %); FY 2024-25's ₹8,000 night does.
        var food = await db.Gst.ChargeAsync(null, db.PropertyId, GstIdsDb.Day, GstCategory.Food, Ct);
        Assert.Equal((500, false), (food.Bps, food.Itc));
        Assert.Equal(1800, (await db.Gst.ChargeAsync(null, db.PropertyId, new DateOnly(2026, 3, 31), GstCategory.Food, Ct)).Bps);

        // After: the new statement with migration 3951. Before: the 70c2713 statement without the 3951 indexes, once with the plan the
        // optimizer picks on fresh statistics (scans) and once in the nested-loop shape the room-board probe's Query Store recorded on
        // the 240-day seed (each night of last FY looked up in bill.folio and inv.reservation, OPTION (LOOP JOIN)).
        var after = await MeasureAsync(SqlGstMasterStore.MaxUnitTariffSql);
        var legacyWithIndexes = await MeasureAsync(LegacySql);
        await db.ScalarAsync("DROP INDEX IX_charge_room_night_amount ON bill.charge; DROP INDEX IX_charge_reverses ON bill.charge;");
        var before = await MeasureAsync(LegacySql);
        var beforeLoops = await MeasureAsync(LegacySql.TrimEnd().TrimEnd(';') + " OPTION (LOOP JOIN);");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Specified-premises tariff, last FY, {await db.LongAsync("SELECT COUNT(*) FROM bill.charge;")} charges: before (70c2713 statement, schema 3701) "
            + $"{before.Reads} reads / {before.Ms} ms, in the probe's loop shape {beforeLoops.Reads} reads / {beforeLoops.Ms} ms; after (new statement + 3951) "
            + $"{after.Reads} reads / {after.Ms} ms; old statement with the 3951 indexes {legacyWithIndexes.Reads} reads / {legacyWithIndexes.Ms} ms (ceiling {ReadsCeiling}).");
        Assert.True(after.Reads < ReadsCeiling, $"The new statement read {after.Reads} pages (ceiling {ReadsCeiling}); before it read {before.Reads}.");
        Assert.True(before.Reads > after.Reads * 3, $"The old statement read only {before.Reads} pages against {after.Reads}; the fixture no longer shows the cost.");
        Assert.True(beforeLoops.Reads > ReadsCeiling * 10, $"The old statement's loop plan read only {beforeLoops.Reads} pages; the fixture no longer shows the cost.");
    }
}
