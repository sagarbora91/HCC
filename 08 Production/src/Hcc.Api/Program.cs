using Hcc.Api;
using Hcc.Api.Hosting;
using Hcc.Operations;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;

// HCC API host bootstrap. Usage:
//   dotnet run --project src/Hcc.Api                -> serve /api/v1 (Kestrel; LAN TLS + Windows service hosting are P7)
//   dotnet run --project src/Hcc.Api -- migrate     -> apply pending migrations (backup gate first on a non-empty database) and exit
// ETP-REUSE: replace/align with ETP migration CLI / operator entry point (and its logging) when V:\ETP is available.
var builder = WebApplication.CreateBuilder(args);
builder.UseHccHosting(); // P7: Windows service, production config validation, LAN HTTPS (Kestrel + machine-store certificate)
var sqlOptions = builder.Configuration.GetSection("Hcc:Sql").Get<SqlOptions>() ?? new SqlOptions();
var platform = new SqlPlatform(sqlOptions);

if (args.Length > 0 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
{
    // P7: when Hcc:Operations:BackupScript is set, the pre-migration backup is taken by deployment\windows\Backup-HccDatabase.ps1.
    var ops = (builder.Configuration.GetSection(OperationsOptions.Section).Get<OperationsOptions>() ?? new OperationsOptions()).WithBackupDirectory(sqlOptions.BackupDirectory);
    ScriptBackupGate? scriptGate = string.IsNullOrWhiteSpace(ops.BackupScript) ? null : new ScriptBackupGate(ops, new ProcessScriptRunner());
    var runner = scriptGate is null ? platform.Migrations : new MigrationRunner(platform.Connections, new EmbeddedMigrationSource(), scriptGate);
    var result = await runner.MigrateAsync();
    Console.WriteLine($"Schema version {result.FromVersion} -> {result.ToVersion}; applied [{string.Join(", ", result.Applied)}]; backup gate: {(result.BackupTaken ? (scriptGate?.LastBackupTarget ?? platform.BackupGate.LastBackupTarget) : "not needed")}.");
    return;
}

builder.Services.AddHcc(builder.Configuration, platform); // platform + every module (HccComposition.cs)

var app = builder.Build();
app.MapHcc();
await app.RunAsync();
