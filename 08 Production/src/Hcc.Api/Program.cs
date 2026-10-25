using Hcc.Api;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;

// HCC API host bootstrap. Usage:
//   dotnet run --project src/Hcc.Api                -> serve /api/v1 (Kestrel; LAN TLS + Windows service hosting are P7)
//   dotnet run --project src/Hcc.Api -- migrate     -> apply pending migrations (backup gate first on a non-empty database) and exit
// ETP-REUSE: replace/align with ETP migration CLI / operator entry point (and its logging) when V:\ETP is available.
var builder = WebApplication.CreateBuilder(args);
var sqlOptions = builder.Configuration.GetSection("Hcc:Sql").Get<SqlOptions>() ?? new SqlOptions();
var platform = new SqlPlatform(sqlOptions);

if (args.Length > 0 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
{
    var result = await platform.Migrations.MigrateAsync();
    Console.WriteLine($"Schema version {result.FromVersion} -> {result.ToVersion}; applied [{string.Join(", ", result.Applied)}]; backup gate: {(result.BackupTaken ? platform.BackupGate.LastBackupTarget : "not needed")}.");
    return;
}

builder.Services.AddHcc(builder.Configuration, platform); // platform + every module (HccComposition.cs)

var app = builder.Build();
app.MapHcc();
await app.RunAsync();
