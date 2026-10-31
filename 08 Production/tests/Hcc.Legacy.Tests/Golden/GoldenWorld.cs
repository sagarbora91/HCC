using Hcc.Contracts.Legacy;
using Hcc.Legacy.Tests.Import;

namespace Hcc.Legacy.Tests.Golden;

/// <summary>Runs the importer on a copy of Fixtures/run-golden over lg-import's disposable database (LegacyImportWorld).</summary>
public sealed class GoldenWorld : IAsyncDisposable
{
    private readonly LegacyImportWorld _w;

    private GoldenWorld(LegacyImportWorld w) => _w = w;

    public static async Task<GoldenWorld> CreateAsync() => new(await LegacyImportWorld.CreateAsync(TestContext.Current.CancellationToken));

    /// <summary>Copies the golden folder to a temp folder (the importer may write into it) and returns the path.</summary>
    public static string CopyRunFolder()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Golden", "Fixtures", "run-golden");
        var dst = Path.Combine(Path.GetTempPath(), "lg-golden-" + Guid.NewGuid().ToString("N")[..8], "run-golden");
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*.csv")) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        return dst;
    }

    public async Task<LegacyImportResult> RunAsync(string folder, string[] parts, bool dryRun, string totalsJson)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "totals.json"), totalsJson, TestContext.Current.CancellationToken);
        return (await _w.Service.ImportAsync(_w.Ctx("owner"), new LegacyImportRequest(folder, parts, dryRun, totalsJson), TestContext.Current.CancellationToken)).Value;
    }

    public async Task<(int Stays, int Bills, int Receipts)> CountsAsync(string fy) => (
        (int)await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {_w.PropertyId} AND legacy_fy = '{fy}'"),
        (int)await _w.CountAsync($"SELECT COUNT(*) FROM lgc.bill WHERE property_id = {_w.PropertyId} AND legacy_fy = '{fy}'"),
        (int)await _w.CountAsync($"SELECT COUNT(*) FROM lgc.receipt WHERE property_id = {_w.PropertyId} AND legacy_fy = '{fy}'"));

    public ValueTask DisposeAsync() => _w.DisposeAsync();
}
