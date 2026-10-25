using System.Text.Json.Nodes;
using Hcc.Api;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Sql;

namespace Hcc.Platform.Tests.Kernel;

/// <summary>H04 port (lab hcc.test.ts cases, minus the donor-parity check which needs the donor source), stay dates, business day, errors.</summary>
public class DatesAndErrorsTests
{
    private static DateOnly D(string s) => StayDates.Parse(s);

    [Fact]
    public void H04_IST_boundary_and_year_boundaries()
    {
        Assert.Equal(D("2026-10-23"), CalendarDate.InTimeZone(DateTimeOffset.Parse("2026-10-23T18:29:59Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        Assert.Equal(D("2026-10-24"), CalendarDate.InTimeZone(DateTimeOffset.Parse("2026-10-23T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        Assert.Equal(D("2027-04-01"), CalendarDate.InTimeZone(DateTimeOffset.Parse("2027-03-31T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        Assert.Equal(D("2027-01-01"), CalendarDate.InTimeZone(DateTimeOffset.Parse("2026-12-31T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        var t = DateTimeOffset.Parse("2024-06-02T00:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(D("2024-06-01"), CalendarDate.InTimeZone(t, "America/New_York"));
        Assert.Equal(D("2024-06-02"), CalendarDate.InTimeZone(t, "UTC"));
    }

    [Fact]
    public void H04_invalid_timezone_is_a_configuration_error_not_a_UTC_fallback()
    {
        var t = DateTimeOffset.Parse("2024-06-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<InvalidPropertyTimeZoneException>(() => CalendarDate.InTimeZone(t, "Not/A_Timezone"));
        Assert.Throws<InvalidPropertyTimeZoneException>(() => CalendarDate.AssertTimeZone(""));
        Assert.Throws<InvalidPropertyTimeZoneException>(() => CalendarDate.AssertTimeZone("India Standard Time")); // Windows id, not IANA
        Assert.Equal(TimeSpan.FromMinutes(330), CalendarDate.AssertTimeZone("Asia/Kolkata").BaseUtcOffset);
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarDate.InTimeZone(default, "Asia/Kolkata"));
    }

    [Fact]
    public void Calendar_date_is_not_the_business_date_DEC042()
    {
        // 19:00Z on 24 Oct is 25 Oct in India, but the business date stays 24 Oct until day close.
        var day = new BusinessDay(D("2026-10-24"), new HashSet<DateOnly> { D("2026-10-23") });
        Assert.Equal(D("2026-10-25"), CalendarDate.InTimeZone(DateTimeOffset.Parse("2026-10-24T19:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        Assert.Equal(D("2026-10-24"), day.Date);
        Assert.Equal(HccErrorCode.DAY_CLOSED, Assert.Throws<HccException>(() => day.EnsureOpen(D("2026-10-23"))).Code);
        day.EnsureOpen(D("2026-10-24"));
        var next = day.AdvanceAfterClose();
        Assert.Equal(D("2026-10-25"), next.Date);
        Assert.True(next.IsClosed(D("2026-10-24")));
    }

    [Fact]
    public void Stay_dates_are_half_open_and_validated()
    {
        Assert.Equal(2, StayDates.Nights(D("2026-10-24"), D("2026-10-26")));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => StayDates.Nights(D("2026-10-24"), D("2026-10-24"))).Code);
        Assert.Equal([D("2026-10-24"), D("2026-10-25")], StayDates.EachNight(D("2026-10-24"), D("2026-10-26")).ToArray());
        Assert.False(StayDates.Overlaps(D("2026-10-24"), D("2026-10-26"), D("2026-10-26"), D("2026-10-28"))); // adjacent
        Assert.True(StayDates.Overlaps(D("2026-10-24"), D("2026-10-26"), D("2026-10-25"), D("2026-10-27")));
        Assert.Throws<HccException>(() => StayDates.Parse("24/10/2026"));
        Assert.Throws<HccException>(() => StayDates.Parse("2026-02-30"));
        Assert.Equal("2026-10-24", StayDates.ToWire(D("2026-10-24")));
    }

    [Fact]
    public void Error_codes_cover_the_prototype_codes_and_the_contract_table()
    {
        // Prototype ErrorCode (03 Prototype/src/domain/errors.ts) minus fault-injection-only codes.
        string[] prototype = ["FORBIDDEN", "NOT_FOUND", "NOT_AVAILABLE", "INVALID_TRANSITION", "VALIDATION", "STALE_VERSION", "DAY_CLOSED", "RESTRICTED"];
        var names = Enum.GetNames<HccErrorCode>();
        Assert.All(prototype, p => Assert.Contains(p, names));
        Assert.DoesNotContain("SIMULATED_REJECTION", names);
        var expected = new Dictionary<HccErrorCode, int>
        {
            [HccErrorCode.VALIDATION] = 422, [HccErrorCode.FORBIDDEN] = 403, [HccErrorCode.NOT_FOUND] = 404,
            [HccErrorCode.NOT_AVAILABLE] = 409, [HccErrorCode.RESTRICTED] = 409, [HccErrorCode.INVALID_TRANSITION] = 409,
            [HccErrorCode.STALE_VERSION] = 409, [HccErrorCode.DAY_CLOSED] = 409, [HccErrorCode.APPROVAL_REQUIRED] = 403,
            [HccErrorCode.IDEMPOTENCY_MISMATCH] = 409, [HccErrorCode.UNAVAILABLE] = 503,
            [HccErrorCode.UNAUTHENTICATED] = 401,
        };
        Assert.Equal(expected.Count, names.Length);
        foreach (var (code, status) in expected)
        {
            Assert.Equal(status, HccErrorCatalog.HttpStatus(code));
            Assert.False(string.IsNullOrWhiteSpace(HccErrorCatalog.Title(code)));
        }
    }

    [Fact]
    public void Api_maps_errors_to_contract_body_without_leaking_internals()
    {
        var (status, body) = HccErrorMiddleware.Map(new HccException(HccErrorCode.STALE_VERSION, "Record changed"));
        Assert.Equal(409, status);
        Assert.Equal("STALE_VERSION", body.Code);
        (status, body) = HccErrorMiddleware.Map(new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again."));
        Assert.Equal(401, status);
        Assert.Equal("UNAUTHENTICATED", body.Code);
        (status, body) = HccErrorMiddleware.Map(new InvalidOperationException("secret connection detail"));
        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_json_hash_ignores_key_order_and_detects_changes()
    {
        var a = JsonNode.Parse("""{"amount":100000,"folio":{"id":7,"kind":"payment"}}""");
        var b = JsonNode.Parse("""{"folio":{"kind":"payment","id":7},"amount":100000}""");
        var c = JsonNode.Parse("""{"amount":999,"folio":{"id":7,"kind":"payment"}}""");
        Assert.Equal(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
        Assert.Equal(CanonicalJson.Sha256(a), CanonicalJson.Sha256(b));
        Assert.NotEqual(CanonicalJson.Sha256(a), CanonicalJson.Sha256(c));
        Assert.Equal("""{"amount":100000,"folio":{"id":7,"kind":"payment"}}""", CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Command_context_validates_key_length_and_rowversion_tokens()
    {
        Assert.Throws<HccException>(() => new CommandContext(1, 1, "RecordReceipt", new string('k', 81)).Validate());
        Assert.Throws<HccException>(() => new CommandContext(1, 1, "RecordReceipt", " ").Validate());
        new CommandContext(1, 1, "RecordReceipt", new string('k', 80)).Validate();
        Assert.Throws<HccException>(() => SqlValues.RowVersionFromWire("not base64!"));
        Assert.Throws<HccException>(() => SqlValues.RowVersionFromWire(Convert.ToBase64String(new byte[4])));
        Assert.Equal(HccErrorCode.STALE_VERSION, Assert.Throws<HccException>(() => SqlValues.EnsureVersionMatched(0, "Reservation")).Code);
    }

    [Fact]
    public void Sql_batches_split_on_GO_lines_only()
    {
        var batches = SqlBatches.Split("CREATE SCHEMA a;\r\nGO\r\nSELECT 'GO here';\ngo -- comment\n\nGO\n");
        Assert.Equal(["CREATE SCHEMA a;", "SELECT 'GO here';"], batches);
    }
}
