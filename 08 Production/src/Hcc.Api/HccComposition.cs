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
        services.AddSingleton(sp => new SchemaGuard(platform.Connections, new EmbeddedMigrationSource(), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<SchemaGuard>>()));   // A09-04: refuse to serve while the schema is behind this build
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
        services.AddHccCompliance();                     // W2-5 compliance calendar
        services.AddPosModule();                         // W2-6 restaurant POS
        services.AddHccMaintenance();                    // W2-7
        services.AddHccStock();                          // W2-8 linen & stock
        services.AddHccMessaging();                      // W2-10 messaging (simulated sending)
        services.AddHandoverModule();                    // W2-11 shift handover
        services.AddYieldModule();                       // W2-12 yield & tariffs
        services.AddGuestServicesModule();               // W2-9 guest services
        services.AddPayrollModule();                     // W2-2 payroll and leave
        services.AddSetupModule();                       // W3-b first-time hotel setup (loopback, empty install only)
        services.AddDocumentsModule();                   // W3-3 ID document capture / OCR (needs the guest ID key)
        services.AddCustomersModule();                   // W3-5 customers / CRM (customer 360, merge duplicates)
        services.AddWebBookingModule(configuration);     // W3-2 booking website (public quote/request, staff queue, deposit link)
        services.AddOtaModule();                         // W3-1 OTA channels (simulated channel manager)
        services.AddGroupsModule();                      // W3-4 groups billing (master folio, routing, pickup billing, attrition)
        services.AddInvoicesModule();                    // W3-6 tax invoices & credit notes (numbering per FY, documents)
        services.AddImportModule();                      // W4-13 masters & opening balances import (CSV validate/preview/commit)
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        return services;
    }

    private static async Task<bool> HealthDetailsAllowedAsync(HttpContext http, CancellationToken ct)
    {
        if (AccessEndpoints.IsServerPc(http)) return true;
        var auth = http.Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim()
            : http.Request.Cookies.TryGetValue(Contracts.Access.AccessConventions.SessionCookie, out var cookie) ? cookie : null;
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            var device = http.Request.Headers[Contracts.Access.AccessConventions.DeviceTokenHeader].ToString();
            var session = await http.RequestServices.GetRequiredService<Application.Access.ISessionService>()
                .ValidateAsync(token, device.Length > 0 ? device : null, ct);
            var policy = http.RequestServices.GetRequiredService<Application.Access.IAccessPolicy>();
            return await policy.HasAsync(session.PropertyId, session.UserId, "PERM-ADM-005", null, ct)
                || await policy.HasAsync(session.PropertyId, session.UserId, "PERM-ADM-003", null, ct);
        }
        catch (Domain.Errors.HccException)
        {
            return false;   // no or ended session: the anonymous answer
        }
    }

    public static RouteGroupBuilder MapHcc(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseHccHostingGuards();       // F-H04/F-L11/F-L02: listener rules, forwarded headers (public listener only), security headers
        app.UseMiddleware<HccErrorMiddleware>();
        app.UseHccSchemaGuard();         // A09-04: 503 on /api/v1 while the database schema is behind this build (health says why)
        var api = app.MapGroup(ApiConventions.BasePath);
        // F-L01: anonymous callers get only "ok" and the server time (the clients' clock) — no database name, no schema version and
        // no SQL connection. The server PC itself (loopback, no proxy header) and signed-in administrators (configuration or
        // backup permission) get the database check and details. A09-04: 503 with the reason while the schema is behind this build.
        api.MapGet("/system/health", async (HttpContext http, SqlConnectionFactory connections, SchemaGuard schema, TimeProvider clock, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (await schema.BehindAsync(ct) is { } behind) return SchemaGuard.Unavailable(http, behind);
            var time = clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (!await HealthDetailsAllowedAsync(http, ct)) return Results.Ok(new HealthResult("ok", null, null, time));
            await using var conn = await connections.OpenAsync(ct);
            var version = await MigrationRunner.CurrentVersionAsync(conn, ct);
            return Results.Ok(new HealthResult("ok", conn.Database, version, time));
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
        api.MapComplianceEndpoints();    // W2-5
        api.MapPosEndpoints();           // W2-6
        api.MapMaintenanceEndpoints();   // W2-7
        api.MapStockEndpoints();         // W2-8
        api.MapMessagingEndpoints();     // W2-10
        api.MapHandoverEndpoints();      // W2-11
        api.MapYieldEndpoints();         // W2-12
        api.MapGuestServicesEndpoints(); // W2-9
        api.MapPayrollEndpoints();       // W2-2
        api.MapSetupEndpoints();         // W3-b first-time setup
        api.MapDocumentsEndpoints();     // W3-3 ID document capture
        api.MapCustomersEndpoints();     // W3-5
        api.MapWebBookingEndpoints();    // W3-2 booking website
        api.MapOtaEndpoints();           // W3-1 OTA channels
        api.MapGroupsEndpoints();        // W3-4 groups billing
        api.MapInvoicesEndpoints();      // W3-6 invoices & credit notes
        api.MapImportEndpoints();        // W4-13 masters & opening balances import
        app.MapHccWebApp(app.Configuration); // W4 F3 (H1): the production web build (index.html) on the same origin
        return api;
    }
}
