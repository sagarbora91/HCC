using Hcc.Api;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;

// HCC API host bootstrap (P0). Usage:
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

builder.Services.AddSingleton(platform);
builder.Services.AddSingleton(platform.Connections);
builder.Services.AddSingleton<ICommandRunner>(platform.Commands);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHccAccess(builder.Configuration, platform);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
app.UseMiddleware<HccErrorMiddleware>();

var api = app.MapGroup(ApiConventions.BasePath);
api.MapAccessEndpoints();
api.MapGet("/system/health", async (SqlConnectionFactory connections, TimeProvider clock, CancellationToken ct) =>
{
    await using var conn = await connections.OpenAsync(ct);
    var version = await MigrationRunner.CurrentVersionAsync(conn, ct);
    return Results.Ok(new HealthResult("ok", conn.Database, version, clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
});

await app.RunAsync();
