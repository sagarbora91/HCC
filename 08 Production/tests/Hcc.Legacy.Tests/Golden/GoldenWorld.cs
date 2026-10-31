using Hcc.Contracts.Legacy;

namespace Hcc.Legacy.Tests.Golden;

/// <summary>
/// The single place that runs the importer for the golden tests. NOT COMPILED YET: lg-import's LegacyImportService / contract records had not
/// landed when this lane ran. Wire after merging claude/lg-import and claude/lg-schema: build a disposable HccTest_* database (copy the
/// pattern of Hcc.Import.Tests ImportWorld), seed the 12 guests (Fixtures/run-golden/guests.csv) and call the service as the Owner.
/// </summary>
public sealed class GoldenWorld : IAsyncDisposable
{
    public static Task<GoldenWorld> CreateAsync() => throw new NotImplementedException("WIRE after lg-import lands (see class summary).");

    /// <summary>parts e.g. ["history"]; totalsJson is the text of totals.json (the contract's expectedTotals).</summary>
    public Task<LegacyImportResult> RunAsync(string folder, string[] parts, bool dryRun, string totalsJson) => throw new NotImplementedException();

    /// <summary>Row counts in lgc.stay / lgc.bill / lgc.receipt for a legacy_fy (to prove a rollback or a commit).</summary>
    public Task<(int Stays, int Bills, int Receipts)> CountsAsync(string fy) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
