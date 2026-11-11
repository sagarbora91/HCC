using System.Diagnostics;
using System.Globalization;
using System.Text;
using Hcc.Customers.Tests.Harness;

namespace Hcc.Customers.Tests.Perf;

/// <summary>
/// AF-CTL02 / PER-01. Runs only with HCC_PERF=1 (otherwise returns at once). For each N (env HCC_PERF_N, default "1000,10000,30000") it builds a
/// disposable HccTest_* hotel, bulk-inserts N fictional guests (phone 9000000000 + i) each with one departed reservation, a folio and two room charges,
/// then times CustomerQueries.ListAsync (page 1 and 20; q = null / "rao" / "900012" / "0001234") and ProfileAsync (5 warm-up + 20 measured runs each):
/// p50 / p95 ms and bytes allocated per call (GC.GetTotalAllocatedBytes, precise). With HCC_PERF_OLD=1 the pre-rewrite query
/// (<see cref="Sql.PreRewriteCustomerQueries"/>) is timed too, rows marked "(before)". Results append to HCC_PERF_OUT (default %TEMP%\hcc-per01.md).
/// Each world is disposed (database dropped) before the next N.
/// PER-01 pass threshold (rewritten query only): every call's p95 must stay under <see cref="MaxP95Ms"/> and its median allocation under
/// <see cref="MaxMegabytes"/> for its N. The limits are about 1.5x (allocation, deterministic) and 2-3x (time) what this PC measured (Debug build), so they do not flake, and well under the
/// pre-rewrite cost (about 29 ms and 4.3 MB per 1,000 guests), so a return to whole-property loading fails.
/// </summary>
[Trait("Category", "Perf")]
public class CustomerQueryBenchmark
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] First = ["Asha", "Vikram", "Meera", "Rohan", "Sunita", "Kiran", "Anil", "Pooja", "Nikhil", "Divya"];
    private static readonly string[] Last = ["Rao", "Shah", "Patil", "Kulkarni", "Deshmukh", "Joshi", "Naik", "Pawar", "Jadhav", "More"];

    /// <summary>PER-01 pass limit: p95 wall time of one call at N guests (ms).</summary>
    public static double MaxP95Ms(int n) => 120 + 8.0 * n / 1000;

    /// <summary>PER-01 pass limit: median bytes allocated by one call at N guests (MB).</summary>
    public static double MaxMegabytes(int n) => 2 + 1.0 * n / 1000;

    [Fact]
    public async Task Customer_list_and_profile_timings()
    {
        if (Environment.GetEnvironmentVariable("HCC_PERF") != "1") return;
        var sizes = (Environment.GetEnvironmentVariable("HCC_PERF_N") ?? "1000,10000,30000").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var withOld = Environment.GetEnvironmentVariable("HCC_PERF_OLD") == "1";
        var outPath = Environment.GetEnvironmentVariable("HCC_PERF_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "hcc-per01.md");
        var failures = new List<string>();
        foreach (var n in sizes)
        {
            await using var w = await CrmWorld.CreateAsync(Ct);
            var sw = Stopwatch.StartNew();
            await SeedAsync(w, n);
            var seedSeconds = sw.Elapsed.TotalSeconds;
            var owner = w.Users["owner"];
            var old = new Sql.PreRewriteCustomerQueries(w.Policy, new Hcc.Infrastructure.SqlServer.Customers.SqlCustomerReadStore(w.Platform.Connections), w.Messaging);
            var key = (await w.Queries.ListAsync(w.PropertyId, owner, "900012", null, 1, 1, Ct)).Customers.FirstOrDefault()?.Key
                ?? (await w.Queries.ListAsync(w.PropertyId, owner, null, null, 1, 1, Ct)).Customers[0].Key;
            var md = new StringBuilder();
            md.AppendLine(CultureInfo.InvariantCulture, $"### N = {n:N0} guests (seeded in {seedSeconds:F1} s; limit p95 {MaxP95Ms(n):F0} ms, {MaxMegabytes(n):F1} MB)");
            md.AppendLine("| call | total rows | p50 ms | p95 ms | allocated MB / call |");
            md.AppendLine("|---|---|---|---|---|");
            async Task Row(string label, Func<Task<int>> call, bool before = false)
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
                var mb = bytes[9] / 1048576.0;
                md.AppendLine(CultureInfo.InvariantCulture, $"| {label}{(before ? " (before)" : string.Empty)} | {total} | {ms[9]:F0} | {ms[18]:F0} | {mb:F1} |");
                if (!before && (ms[18] > MaxP95Ms(n) || mb > MaxMegabytes(n)))
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"N={n} {label}: p95 {ms[18]:F0} ms (limit {MaxP95Ms(n):F0}), {mb:F1} MB (limit {MaxMegabytes(n):F1})"));
            }

            foreach (var q in new string?[] { null, "rao", "900012", "0001234" })
                foreach (var page in new[] { 1, 20 })
                {
                    var label = $"ListAsync q={(q is null ? "null" : q)} page {page}";
                    await Row(label, async () => (await w.Queries.ListAsync(w.PropertyId, owner, q, null, page, 50, Ct)).Total);
                    if (withOld) await Row(label, async () => (await old.ListAsync(w.PropertyId, owner, q, null, page, 50, Ct)).Total, before: true);
                }

            await Row("ProfileAsync one key", async () => (await w.Queries.ProfileAsync(w.PropertyId, owner, key, Ct)).Stays.Count);
            if (withOld) await Row("ProfileAsync one key", async () => (await old.ProfileAsync(w.PropertyId, owner, key, Ct)).Stays.Count, before: true);
            md.AppendLine();
            await File.AppendAllTextAsync(outPath, md.ToString(), Ct);
        }

        Assert.True(failures.Count == 0, "PER-01 threshold exceeded:\n" + string.Join('\n', failures));
    }

    /// <summary>
    /// CU-PER (HCC_PERF=1): the customer commands' key lookup and the duplicate finder, rewritten vs the pre-rewrite whole-property load.
    /// NOTE: the "(before)" rows are RECONSTRUCTIONS run on the new code (LoadAsync + Index; the key resolve goes through the new warm token cache, and
    /// the duplicates row leaves out policy, secret, legacy and summary work the "after" row includes), not measurements of the old code. The old
    /// lookup also paid one HMAC per customer key per call, so these rows UNDERSTATE the gain. Rows:
    /// <see cref="Sql.CustomerQueryEquivalenceTests"/> style: LoadAsync + Index). Rows: AddTag + RemoveTag pair (one key lookup each), DuplicatesAsync,
    /// forged ck_ token profile lookup (cache warm; refused from memory). Sizes from HCC_PERF_N (default "1000,10000,15000"); appends to HCC_PERF_OUT.
    /// </summary>
    [Fact]
    public async Task Customer_commands_and_duplicates_timings()
    {
        if (Environment.GetEnvironmentVariable("HCC_PERF") != "1") return;
        var sizes = (Environment.GetEnvironmentVariable("HCC_PERF_N") ?? "1000,10000,15000").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var outPath = Environment.GetEnvironmentVariable("HCC_PERF_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "hcc-cu-per.md");
        foreach (var n in sizes)
        {
            await using var w = await CrmWorld.CreateAsync(Ct);
            await SeedAsync(w, n);
            var reads = new Hcc.Infrastructure.SqlServer.Customers.SqlCustomerReadStore(w.Platform.Connections);
            var owner = w.Users["owner"];
            var key = (await w.Queries.ListAsync(w.PropertyId, owner, null, null, 1, 1, Ct)).Customers[0].Key;
            var md = new StringBuilder();
            md.AppendLine(CultureInfo.InvariantCulture, $"### CU-PER N = {n:N0} guests");
            md.AppendLine("| call | p50 ms | p95 ms | allocated MB / call |");
            md.AppendLine("|---|---|---|---|");
            async Task Row(string label, Func<Task> call)
            {
                for (var i = 0; i < 3; i++) await call();
                var ms = new List<double>();
                var bytes = new List<long>();
                for (var i = 0; i < 10; i++)
                {
                    var b0 = GC.GetTotalAllocatedBytes(true);
                    var t0 = Stopwatch.GetTimestamp();
                    await call();
                    ms.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                    bytes.Add(GC.GetTotalAllocatedBytes(true) - b0);
                }

                ms.Sort();
                bytes.Sort();
                md.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {ms[4]:F0} | {ms[9]:F0} | {bytes[4] / 1048576.0:F1} |");
            }

            await Row("AddTag + RemoveTag (after)", async () =>
            {
                await w.Commands.AddTagAsync(w.Ctx("owner", Guid.NewGuid().ToString("N")), new Hcc.Contracts.Customers.AddCustomerTagRequest(key, "perf"), Ct);
                await w.Commands.RemoveTagAsync(w.Ctx("owner", Guid.NewGuid().ToString("N")), new Hcc.Contracts.Customers.RemoveCustomerTagRequest(key, "perf"), Ct);
            });
            // before: the lookup both commands did (whole-property load, full index, key resolve), twice
            await Row("key lookup x2 (reconstructed before, warm cache: LoadAsync + Index)", async () =>
            {
                for (var i = 0; i < 2; i++)
                {
                    var src = await reads.LoadAsync(w.PropertyId, Ct);
                    var index = src.Index();
                    var keys = await Hcc.Application.Customers.CustomerKeySpace.ForAsync(reads, w.PropertyId, src, index, Ct);
                    var k = Hcc.Domain.Customers.CustomerRules.Resolve(keys.ResolveInbound(key, true), src.Aliases);
                    _ = index.First(c => c.Key == k);
                }
            });
            await Row("forged ck_ token ProfileAsync (cache warm, NOT_FOUND)", async () =>
            {
                try
                {
                    await w.Queries.ProfileAsync(w.PropertyId, owner, "ck_AAAAAAAAAAAAAAAAAAAAAA", Ct);
                }
                catch (Hcc.Domain.Errors.HccException)
                {
                }
            });
            await Row("DuplicatesAsync (after)", async () => await w.Queries.DuplicatesAsync(w.PropertyId, owner, Ct));
            await Row("DuplicatesAsync (reconstructed before: LoadAsync + Index + FindDuplicates only)", async () =>
            {
                var src = await reads.LoadAsync(w.PropertyId, Ct);
                var index = src.Index();
                _ = Hcc.Domain.Customers.CustomerRules.FindDuplicates(index);
            });
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
