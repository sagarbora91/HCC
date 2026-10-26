#pragma warning disable xUnit1051 // helper calls are short-lived and do not need per-call cancellation
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.Documents;
using Hcc.Contracts.GuestIds;
using Hcc.Documents.Tests.Unit;
using Hcc.Domain.Configuration;
using Hcc.Domain.Documents;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.GuestIds;
using Microsoft.Data.SqlClient;

namespace Hcc.Documents.Tests.Sql;

public sealed class FixedRetention(long days) : ISettingsReader
{
    public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(ConfigValue.Of(days));

    public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(ConfigValue.Of(days));
}

/// <summary>Disposable HccTest_* database: one property, users per role, one guest; real access policy, real stores, sealing with a test key.</summary>
public sealed partial class DocWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private DocWorld(string server, string name, long? retention)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        var policy = new SqlAccessPolicy(Platform.Connections, TimeProvider.System, []);
        Protector = Imgs.Protector();
        var docs = new SqlDocumentStore(Platform.Connections);
        Svc = new DocumentService(Platform.Commands, policy, docs, docs, new SimulatedIdOcrEngine(), new SqlGuestIdStore(Platform.Connections), Protector, TimeProvider.System,
            settings: retention is { } r ? new FixedRetention(r) : null);
    }

    public SqlPlatform Platform { get; }

    public GuestIdProtector Protector { get; }

    public DocumentService Svc { get; }

    public int PropertyId { get; private set; }

    public long GuestId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public CommandContext Ctx(string role, string? key = null) => new(PropertyId, Users[role], "D", key ?? Guid.NewGuid().ToString("N"));

    public static async Task<DocWorld> CreateAsync(CancellationToken ct, long? retention = null)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var w = new DocWorld(server, name, retention);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            w.PropertyId = Convert.ToInt32(await w.ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
            foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("housekeeper", "HKP"), ("accountant", "ACC") })
            {
                var id = Convert.ToInt32(await w.ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({w.PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
                await w.ScalarAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
                w.Users[role] = id;
            }

            w.GuestId = Convert.ToInt64(await w.ScalarAsync($"INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({w.PropertyId}, N'Test Guest', '9000000000', 'manual', '2026-10-24');", ct), CultureInfo.InvariantCulture);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<long> LongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    /// <summary>Every row and column of the given queries as one text blob (for plaintext scans).</summary>
    public async Task<string> DumpAsync(params string[] queries)
    {
        var sb = new StringBuilder();
        await using var conn = await Platform.Connections.OpenAsync();
        foreach (var q in queries)
        {
            await using var cmd = new SqlCommand(q, conn) { CommandTimeout = 120 };
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                for (var i = 0; i < rd.FieldCount; i++) sb.Append(rd.IsDBNull(i) ? "NULL" : rd[i] is byte[] b ? Convert.ToHexString(b) + Encoding.Latin1.GetString(b) : Convert.ToString(rd[i], CultureInfo.InvariantCulture)).Append('|');
        }

        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(_name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"IF DB_ID(N'{_name}') IS NOT NULL BEGIN ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]; END", conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>ID capture on SQL Server with the real access policy: sealing, masking, review and confirm, audited views, retention purge, no plaintext in SQL.</summary>
[Trait("Category", "Sql")]
public class DocumentsSqlTests : IAsyncLifetime
{
    private DocWorld _w = null!;
    private static readonly string Number = Imgs.AadhaarNumber("2345678901");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await DocWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<CommandResult<IdCaptureView>> Capture(string role = "REC", string? number = null, string kind = "aadhaar", string? key = null)
        => _w.Svc.CaptureAsync(_w.Ctx(role, key), new CaptureIdDocumentRequest(_w.GuestId, null, kind, "image/png", Imgs.B64(Imgs.Scan(kind, number ?? Number))), Ct);

    private static async Task Fails(HccErrorCode code, Func<Task> act) => Assert.Equal(code, (await Assert.ThrowsAsync<HccException>(act)).Code);

    [Fact]
    public async Task Capture_stores_sealed_image_and_ocr_and_shows_only_the_masked_number()
    {
        var v = (await Capture()).Value;
        Assert.Equal(("Captured", "aadhaar", "XXXX-XXXX-" + Number[^4..], true), (v.Status, v.OcrDocKind, v.OcrIdMasked, v.ImageAvailable));
        Assert.Equal(97, v.OcrConfidence);
        // Nothing sensitive in any SQL table: image bytes, number, name, date of birth (also not as hex of the blob).
        var all = await _w.DumpAsync("SELECT * FROM doc.id_capture;", "SELECT * FROM doc.id_capture_image;", "SELECT * FROM plat.audit_event;", "SELECT * FROM plat.outbox;", "SELECT * FROM plat.idempotency;");
        foreach (var secret in new[] { Number, "Test Guest", "1990-01-31", "HCCFAKEID", Convert.ToBase64String(Imgs.Scan("aadhaar", Number))[..40] })
            Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
        Assert.Contains("enc:v1:k1:", await _w.DumpAsync("SELECT sealed FROM doc.id_capture_image;"), StringComparison.Ordinal);
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ID_CAPTURE';"));
    }

    [Fact]
    public async Task Capture_limits_and_permissions()
    {
        await Fails(HccErrorCode.FORBIDDEN, () => Capture("HKP"));
        await Fails(HccErrorCode.FORBIDDEN, () => Capture("ACC"));
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.CaptureAsync(_w.Ctx("REC"), new CaptureIdDocumentRequest(_w.GuestId, null, "aadhaar", "application/pdf", Imgs.B64(Imgs.Scan("aadhaar", Number))), Ct));
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.CaptureAsync(_w.Ctx("REC"), new CaptureIdDocumentRequest(_w.GuestId, null, "library-card", "image/png", Imgs.B64(Imgs.Scan("aadhaar", Number))), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Svc.CaptureAsync(_w.Ctx("REC"), new CaptureIdDocumentRequest(999999, null, "aadhaar", "image/png", Imgs.B64(Imgs.Scan("aadhaar", Number))), Ct));
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.CaptureAsync(_w.Ctx("REC"), new CaptureIdDocumentRequest(_w.GuestId, 999999, "aadhaar", "image/png", Imgs.B64(Imgs.Scan("aadhaar", Number))), Ct));
        Assert.Equal(0, await _w.LongAsync("SELECT COUNT(*) FROM doc.id_capture;"));
    }

    [Fact]
    public async Task Capture_is_idempotent()
    {
        var a = await Capture(key: "same-key");
        var b = await Capture(key: "same-key");
        Assert.True(b.Replayed);
        Assert.Equal(a.Value.CaptureId, b.Value.CaptureId);
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM doc.id_capture;"));
    }

    [Fact]
    public async Task Confirm_writes_the_number_into_the_sealed_guest_id_storage()
    {
        var v = (await Capture()).Value;
        var done = (await _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, true, null), Ct)).Value;
        Assert.Equal("Confirmed", done.Status);
        Assert.Equal("Aadhaar", (string?)await _w.ScalarAsync($"SELECT id_type FROM gst.guest WHERE guest_id = {_w.GuestId}"));
        Assert.Equal(Number[^4..], (string?)await _w.ScalarAsync($"SELECT id_last4 FROM gst.guest WHERE guest_id = {_w.GuestId}"));
        // The existing reveal path opens exactly this number; the stored blob is sealed under the guest-ID key.
        var guestIds = new GuestIdService(_w.Platform.Commands, new SqlAccessPolicy(_w.Platform.Connections, TimeProvider.System, []), new SqlGuestIdStore(_w.Platform.Connections), _w.Protector, TimeProvider.System);
        var revealed = (await guestIds.RevealAsync(_w.Ctx("REC"), new RevealGuestIdRequest(_w.GuestId), Ct)).Value;
        Assert.Equal(Number, revealed.IdNumber);
        Assert.Equal("XXXX-XXXX-" + Number[^4..], revealed.IdMasked);
        var all = await _w.DumpAsync("SELECT * FROM gst.guest_id_secret;", "SELECT * FROM plat.audit_event WHERE action LIKE 'ID_CAPTURE%';", "SELECT * FROM plat.idempotency;", "SELECT * FROM plat.outbox;");
        Assert.DoesNotContain(Number, all, StringComparison.Ordinal);
        // A second confirm is an invalid transition; a stale token is STALE_VERSION.
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, true, null), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, done.Version, true, null), Ct));
    }

    [Fact]
    public async Task Staff_can_correct_the_ocr_number_and_bad_numbers_are_refused_without_echo()
    {
        var v = (await Capture(number: "100000000000")).Value; // OCR read a number with a bad checksum
        var typedWrong = "12341234";
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, false, typedWrong), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.DoesNotContain(typedWrong, ex.Message, StringComparison.Ordinal);
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, true, null), Ct)); // OCR number fails the checksum
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, false, null), Ct));
        var done = (await _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, v.Version, false, Imgs.AadhaarNumber("5555555555")), Ct)).Value;
        Assert.Equal("Confirmed", done.Status);
        Assert.Contains("corrected by staff", await _w.DumpAsync("SELECT detail FROM plat.audit_event WHERE action = 'ID_CAPTURE_CONFIRM';"), StringComparison.Ordinal);
        // Roles without the ID-view permission cannot confirm.
        var v2 = (await Capture()).Value;
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Svc.ConfirmAsync(_w.Ctx("HKP"), new ConfirmIdCaptureRequest(v2.CaptureId, v2.Version, true, null), Ct));
    }

    [Fact]
    public async Task Reject_keeps_the_capture_but_needs_a_reason()
    {
        var v = (await Capture()).Value;
        await Fails(HccErrorCode.VALIDATION, () => _w.Svc.RejectAsync(_w.Ctx("REC"), new RejectIdCaptureRequest(v.CaptureId, v.Version, "x"), Ct));
        var r = (await _w.Svc.RejectAsync(_w.Ctx("REC"), new RejectIdCaptureRequest(v.CaptureId, v.Version, "Blurred, retake"), Ct)).Value;
        Assert.Equal(("Rejected", "Blurred, retake"), (r.Status, r.RejectReason));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Svc.ConfirmAsync(_w.Ctx("REC"), new ConfirmIdCaptureRequest(v.CaptureId, r.Version, true, null), Ct));
    }

    [Fact]
    public async Task Reveal_and_image_view_need_the_id_view_permission_and_are_audited_without_the_secret()
    {
        var v = (await Capture()).Value;
        foreach (var role in new[] { "HKP", "CSH", "ACC" })
        {
            await Fails(HccErrorCode.FORBIDDEN, () => _w.Svc.RevealAsync(_w.Ctx(role), new RevealIdCaptureRequest(v.CaptureId), Ct));
            await Fails(HccErrorCode.FORBIDDEN, () => _w.Svc.ViewImageAsync(_w.Ctx(role), new ViewIdCaptureImageRequest(v.CaptureId), Ct));
        }

        Assert.Equal(0, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action IN ('ID_CAPTURE_REVEAL','ID_CAPTURE_IMAGE_VIEW');"));
        var key = "reveal-1";
        var r = (await _w.Svc.RevealAsync(_w.Ctx("MGR", key), new RevealIdCaptureRequest(v.CaptureId), Ct)).Value;
        Assert.Equal((Number, "Test Guest", new DateOnly(1990, 1, 31)), (r.IdNumber, r.Name, r.DateOfBirth));
        Assert.Equal(Number[..4] + " " + Number[4..8] + " " + Number[8..], r.Formatted);
        var img = (await _w.Svc.ViewImageAsync(_w.Ctx("REC"), new ViewIdCaptureImageRequest(v.CaptureId), Ct)).Value;
        Assert.Equal(Imgs.Scan("aadhaar", Number), Convert.FromBase64String(img.ImageBase64));
        Assert.Equal("image/png", img.ContentType);
        Assert.Equal(2, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action IN ('ID_CAPTURE_REVEAL','ID_CAPTURE_IMAGE_VIEW');"));
        Assert.Equal(_w.Users["MGR"], await _w.LongAsync("SELECT principal_id FROM plat.audit_event WHERE action = 'ID_CAPTURE_REVEAL';"));
        // The stored command results hold no number, name or image; a replay shows the number again and is audited again (F-L04).
        var stored = await _w.DumpAsync("SELECT result_json FROM plat.idempotency;", "SELECT detail, target FROM plat.audit_event;");
        foreach (var secret in new[] { Number, "Test Guest", "1990-01-31", "iVBOR" }) Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);
        var again = await _w.Svc.RevealAsync(_w.Ctx("MGR", key), new RevealIdCaptureRequest(v.CaptureId), Ct);
        Assert.True(again.Replayed);
        Assert.Equal(Number, again.Value.IdNumber);
        Assert.Equal(3, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action IN ('ID_CAPTURE_REVEAL','ID_CAPTURE_IMAGE_VIEW');"));
        Assert.Equal(2, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ID_CAPTURE_REVEAL' AND principal_id = " + _w.Users["MGR"] + ";"));
        var stillClean = await _w.DumpAsync("SELECT result_json FROM plat.idempotency;", "SELECT detail, target FROM plat.audit_event;");
        foreach (var secret in new[] { Number, "Test Guest", "1990-01-31", "iVBOR" }) Assert.DoesNotContain(secret, stillClean, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_and_detail_are_masked_and_need_the_capture_permission()
    {
        var v = (await Capture()).Value;
        var list = await _w.Svc.ListAsync(_w.PropertyId, _w.Users["REC"], _w.GuestId, null, null, Ct);
        Assert.Equal("XXXX-XXXX-" + Number[^4..], Assert.Single(list).OcrIdMasked);
        Assert.Equal(v.CaptureId, (await _w.Svc.GetAsync(_w.PropertyId, _w.Users["REC"], v.CaptureId, Ct)).CaptureId);
        Assert.DoesNotContain(Number, System.Text.Json.JsonSerializer.Serialize(list), StringComparison.Ordinal);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Svc.ListAsync(_w.PropertyId, _w.Users["HKP"], null, null, null, Ct));
    }

    [Fact]
    public async Task Purge_deletes_images_and_ocr_text_after_the_retention_period_only()
    {
        var old = (await Capture()).Value;
        // Business date 24 Oct 2026; the default retention is 90 days.
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Svc.PurgeAsync(_w.Ctx("REC"), new PurgeIdCapturesRequest("retention run"), Ct));
        Assert.Equal(0, (await _w.Svc.PurgeAsync(_w.Ctx("OWN"), new PurgeIdCapturesRequest("retention run"), Ct)).Value.Purged);
        await _w.ScalarAsync("UPDATE inv.property SET business_date = '2027-01-21' WHERE property_id = " + _w.PropertyId); // day 89
        Assert.Equal(0, (await _w.Svc.PurgeAsync(_w.Ctx("OWN"), new PurgeIdCapturesRequest("retention run"), Ct)).Value.Purged);
        await _w.ScalarAsync("UPDATE inv.property SET business_date = '2027-01-22' WHERE property_id = " + _w.PropertyId); // day 90
        var p = (await _w.Svc.PurgeAsync(_w.Ctx("MGR"), new PurgeIdCapturesRequest("retention run"), Ct)).Value;
        Assert.Equal((1, 90, new DateOnly(2026, 10, 24)), (p.Purged, p.RetentionDays, p.CutoffDate));
        Assert.Equal(0, await _w.LongAsync("SELECT COUNT(*) FROM doc.id_capture_image;"));
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM doc.id_capture WHERE status = 'Purged' AND ocr_sealed IS NULL AND purged_utc IS NOT NULL;"));
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Svc.RevealAsync(_w.Ctx("REC"), new RevealIdCaptureRequest(old.CaptureId), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Svc.ViewImageAsync(_w.Ctx("REC"), new ViewIdCaptureImageRequest(old.CaptureId), Ct));
        Assert.Equal("Purged", (await _w.Svc.GetAsync(_w.PropertyId, _w.Users["REC"], old.CaptureId, Ct)).Status);
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ID_CAPTURE_PURGE' AND detail LIKE '1 ID captures purged%';"));
    }

    [Fact]
    public async Task Purge_uses_the_configured_retention_days()
    {
        await using var w = await DocWorld.CreateAsync(Ct, retention: 30);
        await w.Svc.CaptureAsync(w.Ctx("REC"), new CaptureIdDocumentRequest(w.GuestId, null, "passport", "image/png", Imgs.B64(Imgs.Scan("passport", "Z1234567"))), Ct);
        await w.ScalarAsync("UPDATE inv.property SET business_date = '2026-11-23' WHERE property_id = " + w.PropertyId);
        var p = (await w.Svc.PurgeAsync(w.Ctx("OWN"), new PurgeIdCapturesRequest("30 day rule"), Ct)).Value;
        Assert.Equal((1, 30), (p.Purged, p.RetentionDays));
    }
}
