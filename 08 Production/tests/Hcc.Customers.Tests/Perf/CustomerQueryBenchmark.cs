using System.Diagnostics;
using System.Globalization;
using System.Text;
using Hcc.Customers.Tests.Harness;

namespace Hcc.Customers.Tests.Perf;

/// <summary>
/// AF-CTL02 (PER-01): measurement only, no production code. Runs only with HCC_PERF=1 (otherwise returns at once). For each N (env HCC_PERF_N, default
/// "1000,10000,30000") it builds a disposable HccTest_* hotel, bulk-inserts N fictional guests (phone 9000000000 + i) each with one departed reservation,
/// a folio and two room charges, then times CustomerQueries.ListAsync (page 1 and 20; q = null / "rao" / "900012") and ProfileAsync (5 warm-up + 20
/// measured runs each): p50 / p95 ms and bytes allocated per call (GC.GetTotalAllocatedBytes, precise). Results append to HCC_PERF_OUT
/// (default %TEMP%\hcc-per01.md). Each world is disposed (database dropped) before the next N.
/// </summary>
[Trait("Category", "Perf")]
public class CustomerQueryBenchmark
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] First = ["Asha", "Vikram", "Meera", "Rohan", "Sunita", "Kiran", "Anil", "Pooja", "Nikhil", "Divya"];
    private static readonly string[] Last = ["Rao", "Shah", "Patil", "Kulkarni", "Deshmukh", "Joshi", "Naik", "Pawar", "Jadhav", "More"];

    [Fact]
    public async Task Customer_list_and_profile_timings()
    {
        if (Environment.GetEnvironmentVariable("HCC_PERF") != "1") return;
        var sizes = (Environment.GetEnvironmentVariable("HCC_PERF_N") ?? "1000,10000,30000").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var outPath = Environment.GetEnvironmentVariable("HCC_PERF_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "hcc-per01.md");
        foreach (var n in sizes)
        {
            await using var w = await CrmWorld.CreateAsync(Ct);
            var sw = Stopwatch.StartNew();
            await SeedAsync(w, n);
            var seedSeconds = sw.Elapsed.TotalSeconds;
            var owner = w.Users["owner"];
            var key = (await w.Queries.ListAsync(w.PropertyId, owner, "900012", null, 1, 1, Ct)).Customers.FirstOrDefault()?.Key
                ?? (await w.Queries.ListAsync(w.PropertyId, owner, null, null, 1, 1, Ct)).Customers[0].Key;
            var md = new StringBuilder();
            md.AppendLine(CultureInfo.InvariantCulture, $"### N = {n:N0} guests (seeded in {seedSeconds:F1} s)");
            md.AppendLine("| call | total rows | p50 ms | p95 ms | allocated MB / call |");
            md.AppendLine("|---|---|---|---|---|");
            async Task Row(string label, Func<Task<int>> call)
            {
                var total = 0;
                for (var i = 0; i < 5; i++) total = await call();
                var ms = new List<double>();
                var bytes = new List<long>();
                for (var i = 0; i < 20; i++)
                {
                    var b0 = GC.GetTotalAllocatedBytes(true);
                    var t0 = Stopwatch.GetTimestamp();
                    total = await call();
                    ms.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                    bytes.Add(GC.GetTotalAllocatedBytes(true) - b0);
                }

                ms.Sort();
                bytes.Sort();
                md.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {total} | {ms[9]:F0} | {ms[18]:F0} | {bytes[9] / 1048576.0:F1} |");
            }

            foreach (var q in new string?[] { null, "rao", "900012" })
                foreach (var page in new[] { 1, 20 })
                    await Row($"ListAsync q={(q is null ? "null" : q)} page {page}", async () => (await w.Queries.ListAsync(w.PropertyId, owner, q, null, page, 50, Ct)).Total);
            await Row("ProfileAsync one key", async () => (await w.Queries.ProfileAsync(w.PropertyId, owner, key, Ct)).Stays.Count);
            md.AppendLine();
            await File.AppendAllTextAsync(outPath, md.ToString(), Ct);
        }
    }

    private static async Task SeedAsync(CrmWorld w, int n)
    {
        var names = string.Join(",", First.SelectMany(f => Last.Select(l => $"(N'{f} {l}')")));
        var p = w.PropertyId;
        var reception = w.Users["reception"];
        await w.ExecAsync($"""
            SET NOCOUNT ON;
            DECLARE @names TABLE(i INT IDENTITY(0,1), nm NVARCHAR(60)); INSERT @names(nm) VALUES {names};
            DECLARE @k INT = (SELECT COUNT(*) FROM @names);
            ;WITH t(i) AS (SELECT TOP ({n}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT gst.guest(property_id, full_name, phone, email, verified, origin, created_utc)
            SELECT {p}, nm.nm + N' ' + CAST(t.i AS NVARCHAR(10)), CAST(9000000000 + t.i AS VARCHAR(20)), 'g' + CAST(t.i AS VARCHAR(10)) + '@example.test', 1, 'manual', '2026-10-01'
              FROM t JOIN @names nm ON nm.i = t.i % @k WHERE NOT EXISTS (SELECT 1 FROM gst.guest g WHERE g.phone = CAST(9000000000 + t.i AS VARCHAR(20)) AND g.property_id = {p});
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            SELECT {p}, 'BM' + g.phone, g.full_name, '2026-09-01', '2026-09-03', 'Departed' FROM gst.guest g WHERE g.phone LIKE '9%' AND g.property_id = {p} AND g.email LIKE 'g%@example.test';
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by)
            SELECT r.reservation_id, g.guest_id, '2026-09-01', {reception} FROM inv.reservation r JOIN gst.guest g ON 'BM' + g.phone = r.public_no WHERE r.public_no LIKE 'BM%' AND g.property_id = {p};
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no LIKE 'BM%' AND property_id = {p};
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise)
            SELECT f.folio_id, 'room', d.dt, N'Room night', 400000, 500, 20000 FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              CROSS JOIN (VALUES ('2026-09-01'), ('2026-09-02')) d(dt) WHERE r.public_no LIKE 'BM%' AND r.property_id = {p};
            """, Ct);
    }
}
