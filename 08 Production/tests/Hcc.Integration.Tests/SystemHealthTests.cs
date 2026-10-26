using System.Data;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Health;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Integration.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Integration.Tests;

/// <summary>
/// Wave C3 (audit A09-05 + fix wave A "Left open") over HTTP with the real composition: an outbox event parked by its consumer appears in
/// GET /admin/system-health (consumer, event type, attempts, failure times and the error TYPE — no payload, no error text); the last
/// backup / restore-drill results the scripts record in plat.backup_run appear next to it; RetryParkedEvent (Tech admin) releases the event,
/// is audited, and the consumer's next poll delivers it. The Manager and the Accountant are refused both the read and the command.
/// </summary>
[Trait("Category", "Sql")]
public sealed class SystemHealthTests
{
    private const string OwnerLogin = "owner.health";
    private const string Password = "Maroon&Sand#Ledger26";
    private const string Consumer = "health-probe";
    private const string EventType = "HealthProbe";
    private const string Secret = "duplicate key value is (98765 43210, Ravi Kumar)";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        SELECT SCOPE_IDENTITY();
        """;

    private static async Task<HttpClient> PasswordUserAsync(HccServer s, HttpClient owner, string login, string role)
    {
        var u = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest(login, "Health " + role, [new RoleAssignmentInput(role)]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(u.UserId, Password, null, u.Version), Ct);
        var client = s.CookieClient();
        using var r = await Wire.PostAsync(client, "/auth/sign-in", new SignInRequest(login, Password: Password), null, Ct);
        await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct);
        return client;
    }

    [Fact]
    public async Task Parked_event_is_listed_without_payload_retried_by_the_Tech_admin_and_delivered_by_the_next_poll_others_are_refused()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Health Owner", Password), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: Password), null, Ct))
            await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
        using var tad = await PasswordUserAsync(s, owner, "health.tad", "TAD");
        using var acc = await PasswordUserAsync(s, owner, "health.acc", "ACC");
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Health tablet"), Ct);
        var mgrUser = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest("health.mgr", "Health MGR", [new RoleAssignmentInput("MGR")]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(mgrUser.UserId, null, "618204", mgrUser.Version), Ct);
        using var mgr = s.DeviceClient(device.DeviceToken!);
        using (var r = await Wire.PostAsync(mgr, "/auth/sign-in", new SignInRequest("health.mgr", Pin: "618204", ReturnToken: true), null, Ct))
            HccServer.UseBearer(mgr, (await Wire.OkAsync<SignInResult>(r, "manager sign-in", Ct)).SessionToken!);

        // Nothing parked, nothing recorded yet.
        var empty = await Wire.GetAsync<SystemHealthView>(owner, "/admin/system-health", Ct);
        Assert.Empty(empty.Parked);
        Assert.Null(empty.LastBackup);
        Assert.Null(empty.LastSuccessfulBackupUtc);
        Assert.Null(empty.LastRestoreDrill);
        Assert.Contains(empty.EventLog, new[] { "on", "off", "unavailable" });

        // A consumer fails one event five times (its effect throws an error whose text carries personal data): parked.
        var id = Convert.ToInt64(await s.ScalarAsync($$"""
            INSERT plat.outbox(event_type, aggregate_id, payload, created_utc, property_id) OUTPUT INSERTED.outbox_id
            VALUES ('{{EventType}}', 1, N'{"guest":"Ravi Kumar","phone":"9876543210"}', DATEADD(MINUTE, -10, SYSUTCDATETIME()), {{s.PropertyId}});
            """, Ct), CultureInfo.InvariantCulture);
        var failing = true;
        var delivered = 0;
        Task Effect(OutboxEvent e, SqlConnection c, SqlTransaction t, CancellationToken ct)
        {
            if (failing) throw new InvalidOperationException(Secret);
            delivered++;
            return Task.CompletedTask;
        }

        Task<int> Poll() => s.Platform.Outbox.ConsumeAsync(Consumer, [EventType], Effect, ct: Ct);
        for (var i = 1; i <= OutboxConsumer.ParkAfterFailures; i++) Assert.Equal(0, await Poll());

        using (var raw = await owner.GetAsync(ApiConventions.BasePath + "/admin/system-health", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
            Assert.Equal("no-store", raw.Headers.CacheControl?.ToString());
            var text = await raw.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("Ravi", text, StringComparison.Ordinal);            // neither the payload nor the error text (F-L07)
            Assert.DoesNotContain("9876543210", text, StringComparison.Ordinal);
            Assert.DoesNotContain("duplicate key", text, StringComparison.Ordinal);
            var view = JsonSerializer.Deserialize<SystemHealthView>(text, HccServer.Json)!;
            var p = Assert.Single(view.Parked);
            Assert.Equal((Consumer, Consumer, id, EventType, OutboxConsumer.ParkAfterFailures, "InvalidOperationException"),
                (p.Consumer, p.ConsumerLabel, p.OutboxId, p.EventType, p.Attempts, p.ErrorType));
            Assert.True(DateTime.TryParse(p.LastFailedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var last) && last > DateTime.UtcNow.AddMinutes(-5));
            Assert.EndsWith("Z", p.ParkedUtc, StringComparison.Ordinal);
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(["attempts", "consumer", "consumerLabel", "errorType", "eventType", "firstFailedUtc", "lastFailedUtc", "outboxId", "parkedUtc"],
                doc.RootElement.GetProperty("parked")[0].EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        }

        // The backup scripts' results (Backup-HccDatabase.ps1 / Restore-HccDrill.ps1 insert one row per run): the last backup failed,
        // the last good one was the night before, and the weekly drill passed. The backup role may write that table and nothing else.
        await s.ScalarAsync("""
            INSERT plat.backup_run(kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason, host_name) VALUES
              ('Full', 1, '2026-10-25T21:00:00', '2026-10-25T21:01:10', N'Hcc_FULL_20261025T210000Z.bak.hccenc', 52428800, NULL, N'HOTEL-PC'),
              ('RestoreDrill', 1, '2026-10-25T22:00:00', '2026-10-25T22:04:00', N'Hcc_FULL_20261025T210000Z.bak.hccenc', NULL, NULL, N'HOTEL-PC'),
              ('Differential', 0, '2026-10-26T03:00:00', '2026-10-26T03:00:05', NULL, NULL, N'sqlcmd failed: Msg 3201, Msg 3013', N'HOTEL-PC');
            """, Ct);
        Assert.Equal(1, await s.CountAsync("""
            SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals r ON r.principal_id = p.grantee_principal_id
             WHERE r.name = 'hcc_backup_reporter' AND p.permission_name = 'INSERT' AND p.major_id = OBJECT_ID('plat.backup_run') AND p.state = 'G';
            """, Ct));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals r ON r.principal_id = p.grantee_principal_id WHERE r.name = 'hcc_backup_reporter';", Ct));
        var health = await Wire.GetAsync<SystemHealthView>(tad, "/admin/system-health", Ct);
        Assert.Equal(("Differential", false, "sqlcmd failed: Msg 3201, Msg 3013"), (health.LastBackup!.Kind, health.LastBackup.Succeeded, health.LastBackup.Reason));
        Assert.Equal("2026-10-25T21:01:10.000Z", health.LastSuccessfulBackupUtc);
        Assert.Equal(("RestoreDrill", true, "2026-10-25T22:04:00.000Z"), (health.LastRestoreDrill!.Kind, health.LastRestoreDrill.Succeeded, health.LastRestoreDrill.FinishedUtc));
        Assert.Single(health.Parked);

        // Manager and Accountant: no read, no retry (PERM-SYS-001 is Owner + Tech admin).
        foreach (var other in new[] { mgr, acc })
        {
            using var r = await other.GetAsync(ApiConventions.BasePath + "/admin/system-health", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            await Wire.CommandFailsAsync(other, SystemHealthServiceOp, new RetryParkedEventRequest(Consumer, id), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        }

        await Wire.CommandFailsAsync(tad, SystemHealthServiceOp, new RetryParkedEventRequest(" ", id), HttpStatusCode.UnprocessableEntity, "VALIDATION", Ct);
        await Wire.CommandFailsAsync(tad, SystemHealthServiceOp, new RetryParkedEventRequest("messaging-triggers", id), HttpStatusCode.NotFound, "NOT_FOUND", Ct);
        Assert.Equal(OutboxConsumer.ParkAfterFailures, Assert.Single(await s.Platform.Outbox.ParkedAsync(Consumer, Ct)).Attempts);

        // The Tech admin fixes the cause and retries: released (audited), gone from the list, delivered by the consumer's next poll.
        failing = false;
        var key = Wire.NewKey("retry");
        var res = await Wire.CommandAsync<RetryParkedEventResult>(tad, SystemHealthServiceOp, new RetryParkedEventRequest(Consumer, id), Ct, key);
        Assert.Equal((Consumer, id, EventType, OutboxConsumer.ParkAfterFailures, true), (res.Consumer, res.OutboxId, res.EventType, res.Attempts, res.Released));
        var replay = await Wire.CommandAsync<RetryParkedEventResult>(tad, SystemHealthServiceOp, new RetryParkedEventRequest(Consumer, id), Ct, key);
        Assert.Equal(res, replay);   // same Idempotency-Key: the stored result, nothing released twice
        var audit = (string)(await s.ScalarAsync($"SELECT detail FROM plat.audit_event WHERE action = 'OUTBOX_PARKED_RETRY' AND target = 'outbox:{id}';", Ct))!;
        Assert.Equal($"Parked event {id} ({EventType}) of {Consumer} [{Consumer}] released for one more delivery attempt after 5 failed attempt(s) (InvalidOperationException)", audit);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'OUTBOX_PARKED_RETRY' AND target = 'outbox:{id}';", Ct));
        Assert.Empty((await Wire.GetAsync<SystemHealthView>(owner, "/admin/system-health", Ct)).Parked);
        Assert.Equal(1, await Poll());
        Assert.Equal(1, delivered);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM plat.consumer_dedup WHERE consumer = '{Consumer}' AND outbox_id = {id};", Ct));
        Assert.Equal(OutboxConsumer.ParkAfterFailures, await s.CountAsync($"SELECT attempts FROM plat.consumer_failure WHERE consumer = '{Consumer}' AND outbox_id = {id};", Ct));

        // Delivered: nothing left to retry.
        await Wire.CommandFailsAsync(owner, SystemHealthServiceOp, new RetryParkedEventRequest(Consumer, id), HttpStatusCode.NotFound, "NOT_FOUND", Ct);
        Assert.Empty((await Wire.GetAsync<SystemHealthView>(owner, "/admin/system-health", Ct)).Parked);
    }

    private const string SystemHealthServiceOp = Hcc.Application.Health.SystemHealthService.OpRetryParked;
}
