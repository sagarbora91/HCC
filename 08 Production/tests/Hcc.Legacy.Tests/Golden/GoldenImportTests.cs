using System.Text.Json;
using Hcc.Contracts.Legacy;

namespace Hcc.Legacy.Tests.Golden;

/// <summary>
/// Golden run folder (Fixtures/run-golden, built by Fixtures/make_golden.py; fictional) through the importer. The expected figures below are
/// worked out by hand from the bill / receipt tables in make_golden.py (rupees x 100), NOT read from any extractor output.
/// Reconciliation definition used here: bills = all loaded bills (out_of_fy included, dropped duplicate excluded); receipts = every receipt row
/// except kind 'refund'; refunds = kind 'refund'. Names/shape of totals.json are assumed (see the DONE file) until lg-extract's version is merged.
/// </summary>
public sealed class GoldenImportTests
{
    // 2025-26: net by hand 4980+2240+11440+7760+20250+5600+15540+2555+4280+5348 = 79,993 rupees; room GST 480+240+1080+720+1800+600+1440+240+480+528 = 7,608;
    // F&B 500+0+1200+800+3000+0+2000+300+0+400 = 8,200; receipts 12 = 61,870 rupees; 1 refund = 500.
    // 2026-27: net 51,565; room GST 2,280; F&B 3,700; receipts 10 = 39,145; 1 refund = 300.
    private static readonly Dictionary<string, Dictionary<string, long>> Expected = new()
    {
        ["2025-26"] = new() { ["stays"] = 11, ["segments"] = 13, ["bills"] = 10, ["receipts"] = 13, ["duplicatesDropped"] = 1, ["outOfFy"] = 1,
            ["billsCount"] = 10, ["netPaise"] = 7_999_300, ["roomGstPaise"] = 760_800, ["fbPaise"] = 820_000, ["receiptsCount"] = 12,
            ["receiptsPaise"] = 6_187_000, ["refundsCount"] = 1, ["refundsPaise"] = 50_000 },
        ["2026-27"] = new() { ["stays"] = 9, ["segments"] = 9, ["bills"] = 10, ["receipts"] = 11, ["duplicatesDropped"] = 0, ["outOfFy"] = 0,
            ["billsCount"] = 10, ["netPaise"] = 5_156_500, ["roomGstPaise"] = 228_000, ["fbPaise"] = 370_000, ["receiptsCount"] = 10,
            ["receiptsPaise"] = 3_914_500, ["refundsCount"] = 1, ["refundsPaise"] = 30_000 },
    };

    private static string RunFolder(out string temp)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Golden", "Fixtures", "run-golden");
        temp = Path.Combine(Path.GetTempPath(), "lg-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        foreach (var f in Directory.GetFiles(src, "*.csv")) File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        return temp;
    }

    private static string TotalsJson(bool wrong)
    {
        var perFy = Expected.ToDictionary(kv => kv.Key, kv => kv.Value.Where(x => x.Key is not ("stays" or "segments" or "bills" or "receipts" or "duplicatesDropped" or "outOfFy"))
            .ToDictionary(x => x.Key, x => x.Value));
        if (wrong) perFy["2026-27"]["netPaise"] += 100; // one rupee out
        return JsonSerializer.Serialize(new
        {
            extractorVersion = "golden-1",
            perFy,
            dedup = new { billsDropped = 1, bookingsDuplicate = 1, receiptsDropped = 1 },
        });
    }

    [Fact]
    public async Task Golden_dry_run_matches_every_reconciliation_figure()
    {
        await using var world = await GoldenWorld.CreateAsync();
        var folder = RunFolder(out var temp);
        try
        {
            var r = await world.RunAsync(folder, ["history"], dryRun: true, TotalsJson(wrong: false));
            Assert.True(r.DryRun);
            Assert.Equal(2, r.History.PerFy.Count);
            foreach (var fy in r.History.PerFy)
            {
                var e = Expected[fy.Fy];
                Assert.Equal(e["stays"], fy.Stays); Assert.Equal(e["segments"], fy.Segments);
                Assert.Equal(e["bills"], fy.Bills); Assert.Equal(e["receipts"], fy.Receipts);
                Assert.Equal(e["duplicatesDropped"], fy.DuplicatesDropped); Assert.Equal(e["outOfFy"], fy.OutOfFy);
                var c = fy.Reconciliation;
                Assert.True(c.Match, string.Join("; ", c.Differences));
                Assert.Empty(c.Differences);
                Assert.Equal(e["billsCount"], c.BillsCount); Assert.Equal(e["netPaise"], c.NetPaise);
                Assert.Equal(e["roomGstPaise"], c.RoomGstPaise); Assert.Equal(e["fbPaise"], c.FbPaise);
                Assert.Equal(e["receiptsCount"], c.ReceiptsCount); Assert.Equal(e["receiptsPaise"], c.ReceiptsPaise);
                Assert.Equal(e["refundsCount"], c.RefundsCount); Assert.Equal(e["refundsPaise"], c.RefundsPaise);
            }
            Assert.Equal((0, 0, 0), await world.CountsAsync("2025-26")); // dry run leaves nothing
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Wrong_totals_roll_back_that_FY_only_and_commit_the_other()
    {
        await using var world = await GoldenWorld.CreateAsync();
        var folder = RunFolder(out var temp);
        try
        {
            var r = await world.RunAsync(folder, ["history"], dryRun: false, TotalsJson(wrong: true));
            var good = r.History.PerFy.Single(x => x.Fy == "2025-26");
            var bad = r.History.PerFy.Single(x => x.Fy == "2026-27");
            Assert.True(good.Reconciliation.Match);
            Assert.False(bad.Reconciliation.Match);
            var d = Assert.Single(bad.Reconciliation.Differences);
            Assert.Contains("netPaise", d.ToString());
            Assert.Equal((11, 10, 13), await world.CountsAsync("2025-26"));
            Assert.Equal((0, 0, 0), await world.CountsAsync("2026-27"));
        }
        finally { Directory.Delete(temp, true); }
    }
}
