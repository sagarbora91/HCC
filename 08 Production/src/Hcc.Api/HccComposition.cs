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
        services.AddHostedService<OutboxFailureLogger>();  // A08-02: failed and parked outbox deliveries as log warnings
        services.AddSingleton(sp => new SchemaGuard(platform.Connections, new EmbeddedMigrationSource(), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<SchemaGuard>>(), platform.Documents, new EmbeddedMigrationSource(EmbeddedMigrationSource.DocsPrefix)));   // A09-04: refuse to serve while the schema is behind this build
        services.AddHccAccess(configuration, platform);  // P1: sessions, THE IAccessPolicy (scope resolvers from the modules below)
        services.AddInventoryModule(configuration);      // P2 (+ Hcc:GroupBlocks:Enabled, DEC-080)
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
        services.AddAccountingOpeningModule();           // ACC-1 opening balances (IOpeningEntryStore: lock guard, cumulative TB, report opening)
        // F5-A1
        services.AddAccountingYearBankModule();          // ACC-4 year lock / carry-forward and bank reconciliation stores
        // F5-A1 end
        // ACC-2 integration: the lanes' services (endpoint lines in the ACC-2 block below).
        services.AddAccountingManualModule();            // acc2-manual: manual vouchers + the IGstManualVouchers / ITdsRemittancePoster ports over them
        services.AddAccountingTdsModule();               // acc2-tds: TdsService (TDS held back at expense approval, remittance → TDS compliance card)
        services.AddAccountingGstModule();               // acc2-gst: GST month, set-off, month locks (pending manual vouchers through the manual port)
        services.AddAccountingFlowsModule();             // acc2-flows: SqlFlowsStore ports, CashGuardService (+ settings provider) → BillingService / PosService / ExpenseCommands / ExpenseRegisterCommands / vendor payments / manual vouchers / advances
        services.AddSingleton<Hcc.Application.Accounting.ILedgerMonthBalanceStore>(sp =>  // acc2-parity: acc.ledger_month_balance (report opening read)
            new Hcc.Infrastructure.SqlServer.Accounting.SqlLedgerMonthBalanceStore(sp.GetRequiredService<SqlPlatform>().Connections));
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
        services.AddMastersModule();                     // MA-0 masters framework + Floors (DEC-MA-1)
        services.AddRoomMastersModule();                 // MA-1 room types, rooms, amenities, occupancy charges
        services.AddMoneyMastersModule();                // MA-3 payment methods, bank accounts, expense categories and vendors (DEC-058)
        services.AddCatalogueModule();                   // MA-2 meal plans, extras catalogue, discount reasons
        services.AddFrontDeskMastersModule();            // MA-5 booking sources, ID types, numbering series, policies
        services.AddPeopleListsModule();                 // MA-6 designations, shift patterns, leave types, holidays
        services.AddOpsMastersModule();                  // MA-7 housekeeping task types / zones, complaint and lost-and-found categories
        services.AddSystemHealthModule();                // Wave C3 (A09-05): parked outbox events, retry, last backup (PERM-SYS-001)
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        // LG
        services.AddLegacyModule();                      // W1a LG legacy Lodge importer (contract §5)
        // LG end
        services.AddKeysModule();                        // KEYS room keys (HCC_KeyAgent_Spec_v1)
        // LF lanes: ONE services.Add<Lane>Module(); line inside your own block (append only).
        // LF-DOCDB
        services.AddDocumentObjectsModule();             // LF-docdb: HccDocuments object store, release consumer, daily sweep (contract §1)
        // LF-DOCDB end
        // LF-RATE
        services.AddLfMoneyModule();                     // LF-money L1: agreed rate, guest credit account (§3, §7)
        // LF-RATE end
        // LF-BILL
        // LF-BILL end
        // LF-COUNTER
        // LF-COUNTER end
        // LF-DESK
        // LF-DESK end
        // LF-ALERTS
        services.AddRemindersModule();                   // LF B4 desk reminders (PERM-RMD-001)
        // LF-ALERTS end
        // LF-IMAGES
        // LF-IMAGES end
        // F7-R1
        Hcc.Reporting.ReportingModule.AddReportingModule(services); // F7-R1 report engine: catalogue, runner, renderers, schedules / runs / export log
        services.AddHostedService<Hcc.Api.Endpoints.ReportScheduleWorker>();
        // F7-R1 end
        // F7-RPT (report lanes: one services.AddReport<...>() line each)
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Guests.MovementRegisterReport>(services); // F7-R3
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Guests.GuestRegisterReport>(services); // F7-R3
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Guests.FormCRegisterReport>(services); // F7-R3
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Guests.RateChangesReport>(services); // F7-R3
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Rooms.RoomStatusReport>(services); // F7-R3
        // F7-R4 money-a reports (batch 1)
        services.AddSingleton<Hcc.Reporting.Sql.IMoneyReportSource>(sp => new Hcc.Reporting.Sql.SqlMoneyReportSource(sp.GetRequiredService<SqlPlatform>().Connections));
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Money.ShiftReport>(services);
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Money.CollectionDetailReport>(services);
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Money.CreditBillsReport>(services);
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Money.ExpenseRegisterReport>(services);
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Money.ExpensesByHeadReport>(services);
        // F7-R4 end
        // F7-R5 tax reports (GST summary reads the ACC-3 queries, which the accounting endpoints otherwise build per request)
        services.AddSingleton(sp => Hcc.Api.Endpoints.AccountingAgeingEndpoints.GstQueriesOf(sp)); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.SacSummaryReport>(services); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.DocumentsIssuedReport>(services); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.GstByBillReport>(services); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.InvoiceRegisterReport>(services); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.BillCorrectionsReport>(services); // F7-R5
        Hcc.Reporting.ReportingModule.AddReport<Hcc.Reporting.Reports.Tax.GstSummaryReport>(services); // F7-R5
        services.AddSingleton<Hcc.Reporting.Documents.DocumentService>(); // F7-R7 document layouts
        services.AddScoped<Hcc.Reporting.Documents.DocumentSql>(); // F7-R7
        // F7-RPT end
        // F5-E1
        services.AddEgressModule();                      // F5-E1 internet egress gate: net.* settings, "egress" client, hold / replay, System health Egress
        // F5-E1 end
        // F4 lanes: ONE services.Add<Lane>Module(); line inside your own block (append only).
        // F4-T1
        services.AddTariffCardModule();                 // F4-T1 tariff card (PERM-TRF-001…003)
        // F4-T1 end
        // F4-T2
        services.AddRevenueDigestModule();               // F4-T2 revenue by category and tender, owner digest drafts
        // F4-T2 end
        // F4-T3
        services.AddFeedbackModule();
        // F4-T3 end
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
            if (session.MustChangeCredential) return false;   // A06-01: nothing but the credential change before it is changed
            var policy = http.RequestServices.GetRequiredService<Application.Access.IAccessPolicy>();
            return await policy.HasAsync(session.PropertyId, session.UserId, "PERM-ADM-005", null, ct)
                || await policy.HasAsync(session.PropertyId, session.UserId, "PERM-CFG-001", null, ct);
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
            var docs = http.RequestServices.GetService<Hcc.Infrastructure.SqlServer.SqlPlatform>()?.Documents;
            string? docDb = null; int? docVersion = null;
            if (docs is not null)
            {
                try
                {
                    await using var dconn = await docs.OpenAsync(ct);
                    docDb = dconn.Database;
                    docVersion = await MigrationRunner.CurrentVersionAsync(dconn, ct);
                }
                catch (Hcc.Domain.Errors.HccException) { docDb = docs.Database; }   // unreachable: the main health still answers
            }

            return Results.Ok(new HealthResult("ok", conn.Database, version, time, docDb, docVersion));
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
        // ACC-1 lanes: add ONE api.MapAccounting<Lane>Endpoints(); line below (append only).
        api.MapAccountingChartEndpoints(); // ACC-1 chart lane
        api.MapAccountingOpeningEndpoints(); // ACC-1 opening balances (contract §5)
        // F5-A1
        api.MapAccountingYearBankEndpoints(); // ACC-4 (contract §1, §2)
        // F5-A1 end
        api.MapAccountingReportEndpoints(); // ACC-1 acc1-statements
        // ACC-1 end
        // ACC-2 lanes: add ONE api.MapAccounting<Lane>Endpoints(); (or services.Add…Module()) line below (append only).
        api.MapExpenseVendorEndpoints(); // acc2-purchase (vendor payments, returns, ageing, asset register)
        api.MapAccountingManualEndpoints(); // ACC-2 acc2-manual (contract §2: manual vouchers, loans)
        api.MapAccountingTdsEndpoints(); // ACC-2 tds lane (contract §4 A9, §5)
        api.MapAccountingGstEndpoints(); // ACC-2 gst lane (contract §4 switch, §6)
        api.MapOtaPayoutEndpoints(); // acc2-flows: ApplyDeposit, SettleFolioToOta, RecordOtaPayout, SetChannelItc, ReverseUdhaarPayment, SetCompanyActive, GET /ota/payouts
        api.MapAccountingParityEndpoints(); // acc2-parity: BackfillUdhaarCredits (contract §8)
        // ACC-2 end
        // ACC-3 lanes: add ONE api.MapAccounting<Lane>Endpoints(); line below (append only).
        api.MapAccountingPlEndpoints(); // acc3-pl (contract §2)
        api.MapAccountingAgeingEndpoints(); // ACC-3 acc3-ageing (contract §3: ageing, GST summary)
        api.MapAccountingTallyEndpoints(); // ACC-3 acc3-tally (contract §4: Tally masters per FY, vouchers per range)
        api.MapAccountingExportEndpoints(); // ACC-3 acc3-exports (statements.zip, report exports)
        // ACC-3 end
        api.MapComplianceEndpoints();    // W2-5
        api.MapPosEndpoints();           // W2-6
        api.MapMaintenanceEndpoints();   // W2-7
        api.MapStockEndpoints();         // W2-8
        api.MapMessagingEndpoints();     // W2-10
        api.MapHandoverEndpoints();      // W2-11
        api.MapKeysEndpoints();          // KEYS room keys
        api.MapYieldEndpoints();         // W2-12
        api.MapGuestServicesEndpoints(); // W2-9
        api.MapFeedbackEndpoints();      // F4-T3
        api.MapPayrollEndpoints();       // W2-2
        api.MapSetupEndpoints();         // W3-b first-time setup
        api.MapDocumentsEndpoints();     // W3-3 ID document capture
        api.MapCustomersEndpoints();     // W3-5
        api.MapWebBookingEndpoints();    // W3-2 booking website
        api.MapOtaEndpoints();           // W3-1 OTA channels
        api.MapGroupsEndpoints();        // W3-4 groups billing
        api.MapInvoicesEndpoints();      // W3-6 invoices & credit notes
        api.MapImportEndpoints();        // W4-13 masters & opening balances import
        api.MapMastersEndpoints();       // MA-0 masters framework + Floors
        api.MapRoomMastersEndpoints();   // MA-1 rooms and room types masters
        api.MapMoneyMastersEndpoints();  // MA-3 money masters
        api.MapCatalogueEndpoints();     // MA-2 meal plans, extras catalogue, discount reasons
        api.MapFrontDeskMastersEndpoints(); // MA-5 front-desk lists
        api.MapPeopleListsEndpoints();   // MA-6 people lists
        api.MapOpsMastersEndpoints();    // MA-7 housekeeping task types / zones, complaint and lost-and-found categories
        api.MapSystemHealthEndpoints();  // Wave C3 (A09-05): GET /admin/system-health, RetryParkedEvent
        // LG
        api.MapLegacyEndpoints();        // W1a LG legacy importer + runs
        // LG end
        // F4-T2
        api.MapRevenueDigestEndpoints(); // F4-T2 GET /reports/revenue-by-category-tender, GET /digest/drafts
        // F4-T2 end
        // F7-R1
        api.MapReportEndpoints();        // F7-R1 /reports/catalogue, /reports/{id}, export, print-log, schedules, runs, favourites
        // F7-R1 end
        // F7-R7
        api.MapDocumentEndpoints();      // F7-R7 /documents/invoice, credit-note, payslip, bill, kot (Lodge look, html or pdf)
        // F7-R7 end
        // LF lanes: ONE api.Map<Lane>Endpoints(); line inside your own block (append only).
        // LF-DOCDB
        api.MapDocumentObjectEndpoints(); // LF-docdb: POST /admin/documents/sweep (PERM-DOC-006)
        // LF-DOCDB end
        // LF-RATE
        api.MapLfMoneyEndpoints();       // LF-money L1
        // LF-RATE end
        // LF-BILL
        // LF-BILL end
        // LF-COUNTER
        // LF-COUNTER end
        // LF-DESK
        // LF-DESK end
        // LF-ALERTS
        api.MapRemindersEndpoints();     // LF B4 desk reminders
        // LF-ALERTS end
        // LF-IMAGES
        // LF-IMAGES end
        // F5-E1
        api.MapEgressEndpoints();        // F5-E1: GET /admin/system-health/egress (PERM-NET-002)
        // F5-E1 end
        // F4-T1
        api.MapTariffCardEndpoints();    // F4-T1 tariff card
        // F4-T1 end
        app.MapHccWebApp(app.Configuration); // W4 F3 (H1): the production web build (index.html) on the same origin
        return api;
    }
}
