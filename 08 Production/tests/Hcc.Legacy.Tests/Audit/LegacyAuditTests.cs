using System.Globalization;
using System.Text;
using Hcc.Contracts.Legacy;
using Hcc.Legacy.Tests.Import;

namespace Hcc.Legacy.Tests.Audit;

/// <summary>
/// [LG-AUDIT] independent checks of the legacy importer: the reconciliation it reports is recomputed here from the run folder's CSVs with
/// this file's own reader (no importer code), and the lgc.* invariants are asserted on the database after a committed load.
/// </summary>
public sealed class LegacyAuditTests : IAsyncLifetime
{
    private LegacyImportWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await LegacyImportWorld.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private long P => _w.PropertyId;

    internal static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        string[]? header = null;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0) continue;
            var cells = Split(line);
            if (header is null)
            {
                header = cells;
                continue;
            }

            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Length; i++) d[header[i]] = i < cells.Length ? cells[i] : "";
            rows.Add(d);
        }

        return rows;
    }

    private static string[] Split(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"') q = false;
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',')
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }

        cells.Add(sb.ToString());
        return [.. cells];
    }

    private static long L(string s) => s.Length == 0 ? 0 : long.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The eight reconciliation figures per FY, computed from the CSVs alone.</summary>
    internal static Dictionary<string, long[]> Independent(string folder)
    {
        var bills = ReadCsv(Path.Combine(folder, "lgc_bill.csv"));
        var rec = ReadCsv(Path.Combine(folder, "lgc_receipt.csv"));
        var fys = bills.Select(b => b["legacy_fy"]).Concat(rec.Select(r => r["legacy_fy"])).Distinct(StringComparer.Ordinal);
        var res = new Dictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var fy in fys)
        {
            var b = bills.Where(x => x["legacy_fy"] == fy).ToList();
            var money = rec.Where(x => x["legacy_fy"] == fy && (x["kind"] == "advance" || x["kind"] == "payment")).ToList();
            var refunds = rec.Where(x => x["legacy_fy"] == fy && x["kind"] == "refund").ToList();
            res[fy] =
            [
                b.Count, b.Sum(x => L(x["net_paise"])), b.Sum(x => L(x["room_gst_paise"])), b.Sum(x => L(x["fb_paise"])),
                money.Count, money.Sum(x => L(x["amount_paise"])), refunds.Count, refunds.Sum(x => L(x["amount_paise"])),
            ];
        }

        return res;
    }

    private static long[] Of(LegacyReconciliation r) =>
        [r.BillsCount, r.NetPaise, r.RoomGstPaise, r.FbPaise, r.ReceiptsCount, r.ReceiptsPaise, r.RefundsCount, r.RefundsPaise];

    [Fact]
    public async Task Dry_run_reconciliation_equals_an_independent_computation()
    {
        var expected = Independent(_w.Folder);
        var r = (await _w.RunAsync(_w.Request(true, null, "history"))).Value;
        Assert.False(r.Committed);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), r.History!.PerFy.Select(y => y.Fy).Order(StringComparer.Ordinal));
        var stays = ReadCsv(Path.Combine(_w.Folder, "lgc_stay.csv"));
        foreach (var y in r.History.PerFy)
        {
            Assert.Equal(expected[y.Fy], Of(y.Reconciliation));
            Assert.True(y.Reconciliation.Match, y.Fy);
            Assert.Equal(stays.Count(s => s["legacy_fy"] == y.Fy), y.Stays);
            Assert.Equal(stays.Count(s => s["legacy_fy"] == y.Fy && s["status"] == "Duplicate"), y.DuplicatesDropped);
        }
    }

    [Fact]
    public async Task Committed_load_keeps_the_lgc_invariants()
    {
        var r = (await _w.RunAsync(_w.Request(false))).Value;
        Assert.True(r.Committed);

        // unique keys
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM (SELECT 1 x FROM lgc.stay WHERE property_id = {P} GROUP BY legacy_fy, legacy_booking_id HAVING COUNT(*) > 1) d"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM (SELECT 1 x FROM lgc.bill WHERE property_id = {P} GROUP BY legacy_fy, legacy_bill_no HAVING COUNT(*) > 1) d"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM (SELECT 1 x FROM lgc.receipt WHERE property_id = {P} GROUP BY legacy_fy, legacy_source, legacy_row_id HAVING COUNT(*) > 1) d"));

        // every guest_id resolves to gst.guest of the same property
        Assert.Equal(0, await _w.CountAsync($"""
            SELECT COUNT(*) FROM lgc.guest_map m LEFT JOIN gst.guest g ON g.guest_id = m.guest_id AND g.property_id = m.property_id
            WHERE m.property_id = {P} AND m.guest_id IS NOT NULL AND g.guest_id IS NULL
            """));
        Assert.True(await _w.CountAsync($"SELECT COUNT(*) FROM lgc.guest_map WHERE property_id = {P} AND guest_id IS NOT NULL") > 0);

        // no customer_key on a stay / bill / receipt that differs from the guest map for the same legacy guest
        foreach (var t in new[] { "stay", "bill", "receipt" })
        {
            Assert.Equal(0, await _w.CountAsync($"""
                SELECT COUNT(*) FROM lgc.{t} x JOIN lgc.guest_map m ON m.property_id = x.property_id AND m.legacy_guest_id = x.legacy_guest_id
                WHERE x.property_id = {P} AND x.customer_key <> m.customer_key
                """));
        }

        // the guest map holds exactly the CSV's legacy guests with the CSV's customer_key
        var map = ReadCsv(Path.Combine(_w.Folder, "lgc_guest_map.csv"));
        Assert.Equal(map.Count, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.guest_map WHERE property_id = {P}"));
        foreach (var m in map)
        {
            Assert.Equal(m["customer_key"], (string?)await _w.ScalarAsync(
                $"SELECT customer_key FROM lgc.guest_map WHERE property_id = {P} AND legacy_guest_id = {L(m["legacy_guest_id"])}"));
        }

        // v_customer_stay hides Duplicate and only Duplicate
        var stays = ReadCsv(Path.Combine(_w.Folder, "lgc_stay.csv"));
        Assert.Contains(stays, s => s["status"] == "Duplicate");
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.v_customer_stay WHERE property_id = {P} AND status = 'Duplicate'"));
        Assert.Equal(stays.Count(s => s["status"] != "Duplicate"), await _w.CountAsync($"SELECT COUNT(*) FROM lgc.v_customer_stay WHERE property_id = {P}"));
        Assert.Equal(stays.Count, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P}"));

        // database totals equal the independent CSV totals
        foreach (var (fy, e) in Independent(_w.Folder))
        {
            Assert.Equal(e[0], await _w.CountAsync($"SELECT COUNT(*) FROM lgc.bill WHERE property_id = {P} AND legacy_fy = '{fy}'"));
            Assert.Equal(e[1], await _w.CountAsync($"SELECT ISNULL(SUM(net_paise), 0) FROM lgc.bill WHERE property_id = {P} AND legacy_fy = '{fy}'"));
            Assert.Equal(e[5], await _w.CountAsync(
                $"SELECT ISNULL(SUM(amount_paise), 0) FROM lgc.receipt WHERE property_id = {P} AND legacy_fy = '{fy}' AND kind IN ('advance', 'payment')"));
        }
    }
}
