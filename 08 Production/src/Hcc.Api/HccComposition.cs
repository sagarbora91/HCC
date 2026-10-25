using Hcc.Api.Endpoints;
using Hcc.Api.Hosting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Api;

/// <summary>
/// The whole server composition (Wave 1 integration): platform services, every module's DI and every module's routes, used by
/// Program.cs and by the end-to-end tests so both run the same wiring. One line per module.
/// </summary>
public static class HccComposition
{
    public static IServiceCollection AddHcc(this IServiceCollection services, IConfiguration configuration, SqlPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        services.AddSingleton(platform);
        services.AddSingleton(platform.Connections);
        services.AddSingleton<ICommandRunner>(platform.Commands);
        services.AddSingleton(TimeProvider.System);
        services.AddHccAccess(configuration, platform);  // P1: sessions, THE IAccessPolicy (scope resolvers from the modules below)
        services.AddInventoryModule();                   // P2
        services.AddReservationsModule();                // P2
        services.AddHccStays();                          // P3
        services.AddHccHousekeeping();                   // P3
        services.AddBillingModule();                     // P4 billing + payments
        services.AddTaxModule();                         // Wave 1 C: GST master (DEC-069), THE IGstRateResolver
        services.AddGuestIdModule(configuration);        // Wave 1 C: encrypted full guest ID numbers (DEC-068)
        services.AddClosingModule();                     // P6 closing + reports; installs the late-write barrier runner for P2-P4
        services.AddHccConfiguration();                  // W2-1 Master configuration (settings registry, reader, module gate)
        services.AddExpensesModule();                   // W2-4 expenses and petty cash
        services.AddAccountingModule();                  // W2-3 accounting & Tally (outbox consumer)
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        return services;
    }

    public static RouteGroupBuilder MapHcc(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<HccErrorMiddleware>();
        var api = app.MapGroup(ApiConventions.BasePath);
        api.MapGet("/system/health", async (SqlConnectionFactory connections, TimeProvider clock, CancellationToken ct) =>
        {
            await using var conn = await connections.OpenAsync(ct);
            var version = await MigrationRunner.CurrentVersionAsync(conn, ct);
            return Results.Ok(new HealthResult("ok", conn.Database, version, clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        });
        api.MapHccDiscovery();           // P7: GET /system/discovery (no auth, no secrets)
        api.MapAccessEndpoints();        // P1 (sign-in/out, /me/capabilities, admin)
        api.MapInventoryEndpoints();     // P2
        api.MapReservationsEndpoints();  // P2
        api.MapStaysEndpoints();         // P3
        api.MapHousekeepingEndpoints();  // P3
        api.MapBillingEndpoints();       // P4
        api.MapPaymentsEndpoints();      // P4
        api.MapTaxEndpoints();           // Wave 1 C (GST master)
        api.MapGuestIdEndpoints();       // Wave 1 C (guest ID reveal / rekey)
        api.MapClosingEndpoints();       // P6
        api.MapConfigurationEndpoints(); // W2-1
        api.MapExpensesEndpoints();       // W2-4
        api.MapAccountingEndpoints();    // W2-3
        return api;
    }
}
