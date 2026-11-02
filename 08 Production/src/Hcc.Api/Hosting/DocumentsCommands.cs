using Hcc.Application.Documents;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Operations;

namespace Hcc.Api.Hosting;

/// <summary>
/// LF-docdb (LF-CONTRACT §1.6, §1.7, §1.9): the operator verbs of the documents database.
/// <c>Hcc.Api.exe migrate</c> = (1) the main database (the caller), (2) <see cref="MigrateDocumentsAsync"/> (own backup gate), (3) the idempotent move job.
/// <c>sweep-documents</c> = the orphan sweep on demand; <c>check-documents</c> = dangling / orphan counts and the main-database note (restore step).
/// </summary>
public static class DocumentsCommands
{
    public static bool IsVerb(string[] args) => args.Length > 0 &&
        (string.Equals(args[0], "sweep-documents", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "check-documents", StringComparison.OrdinalIgnoreCase));

    /// <summary>Steps 2 and 3 of <c>migrate</c>; returns the process exit code.</summary>
    public static async Task<int> MigrateDocumentsAsync(SqlPlatform platform, IBackupGate? gate, TextWriter output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            var runner = new MigrationRunner(platform.Documents.Inner, new EmbeddedMigrationSource(EmbeddedMigrationSource.DocsPrefix), gate ?? new SqlBackupGate(platform.Documents.Options), "HCC.DocMigrationRunner");
            var result = await runner.MigrateAsync(ct);
            output.WriteLine($"Documents database {platform.Documents.Database}: schema version {result.FromVersion} -> {result.ToVersion}; applied [{string.Join(", ", result.Applied)}]; backup gate: {(result.BackupTaken ? "taken" : "not needed")}.");
        }
        catch (Hcc.Domain.Errors.HccException)
        {
            output.WriteLine($"The documents database {platform.Documents.Database} is not reachable. Create it with Initialize-HccDatabase.ps1 -DocumentsDatabase {platform.Documents.Database}, then run migrate again.");
            return 1;
        }

        var check = await platform.DocumentRefs.RunAsync(false, ct);
        if (check.Dangling > 0)
        {
            output.WriteLine($"Documents: {check.Dangling} reference(s) in Hcc point at objects missing in {platform.Documents.Database}; the move job was not run. Run check-documents and restore the matching documents backup.");
            return 2;
        }

        var move = await platform.DocumentMove.RunAsync(m => output.WriteLine(m), ct);
        output.WriteLine($"Documents move: moved {move.Moved} row(s); remaining in Hcc {move.Remaining}{(move.StuckTables.Count > 0 ? " (" + string.Join(", ", move.StuckTables) + ")" : string.Empty)}.");
        return move.Remaining > 0 && move.Moved == 0 ? 3 : 0;
    }

    public static async Task<int> RunVerbAsync(string[] args, SqlPlatform platform, TextWriter output, CancellationToken ct = default)
    {
        if (string.Equals(args[0], "sweep-documents", StringComparison.OrdinalIgnoreCase))
        {
            var r = await platform.DocumentSweep.RunAsync(null, ct);
            output.WriteLine($"Documents sweep: removed {r.Removed} orphan / released object(s).");
            return 0;
        }

        var check = await platform.DocumentRefs.RunAsync(true, ct);
        foreach (var k in check.Kinds) output.WriteLine($"{k.Kind}: referenced {k.Referenced}, dangling {k.Dangling}, orphans {k.Orphans}");
        output.WriteLine($"Dangling references: {check.Dangling}; orphan objects: {check.Orphans}; documents note main_database = {check.MainDatabaseNote ?? "(none)"}.");
        return check.Dangling > 0 ? 1 : 0;
    }
}
