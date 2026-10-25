/*
 * HCC calendar-date conversion for an instant in the property's IANA timezone (C# port of the lab H04 adaptation).
 *
 * Derived from HAIP, file apps/api/src/common/date/property-business-date.ts (calendarDateInTimeZone)
 *   repository https://github.com/TelivityAI/haip.git, commit 3a2f1c89139e8d7d915528abac706a512af16970
 *   source SHA-256 e8b250e22136e584340bedf5a12b4223a3174595e60280ef0ba0d1f82a8499b7
 * Licensed under the Apache License, Version 2.0 — see ../ThirdParty/LICENSE-HAIP-Apache-2.0.txt (copy of the HAIP root LICENSE).
 *
 * Modified by HCC (Apache-2.0 §4(b) notice of changes):
 *  24 Oct 2026 (lab, 07 Integration Lab/qualifications/H04-property-calendar-date/src/calendar-date.ts):
 *  - an invalid timezone is a configuration error (InvalidTimeZoneError), never a silent UTC fallback;
 *  - an invalid instant is rejected;
 *  - assertTimeZone() added for validation when property configuration is saved;
 *  - scope note: returns a CALENDAR date only. The HCC business date is persisted state advanced by day close (DEC-042),
 *    never derived from this function or from midnight.
 *  25 Oct 2026 (production P0): ported to C# using TimeZoneInfo with IANA ids (ICU on Windows) instead of Intl.DateTimeFormat;
 *  instants are DateTimeOffset; result is DateOnly. Recorded in 08 Production/third-party/source-reuse.json.
 */

namespace Hcc.Domain.Dates;

public sealed class InvalidPropertyTimeZoneException : Exception
{
    public InvalidPropertyTimeZoneException(string timeZone)
        : base($"Invalid property timezone: \"{timeZone}\". Configure a valid IANA zone such as Asia/Kolkata.") => TimeZone = timeZone;

    public InvalidPropertyTimeZoneException() : this(string.Empty) { }

    public InvalidPropertyTimeZoneException(string message, Exception innerException) : base(message, innerException) => TimeZone = string.Empty;

    public string TimeZone { get; }
}

public static class CalendarDate
{
    /// <summary>Validates an IANA zone id (property configuration save). Windows ids are rejected: the stored value must be IANA.</summary>
    public static TimeZoneInfo AssertTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) throw new InvalidPropertyTimeZoneException(timeZone ?? string.Empty);
        if (timeZone is "UTC" or "Etc/UTC") return TimeZoneInfo.Utc;
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidPropertyTimeZoneException(timeZone);
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidPropertyTimeZoneException(timeZone);
        }

        // A Windows id such as "India Standard Time" resolves on Windows but is not a portable IANA id.
        if (!tz.HasIanaId) throw new InvalidPropertyTimeZoneException(timeZone);
        return tz;
    }

    /// <summary>The calendar date (YYYY-MM-DD) of <paramref name="instant"/> as seen in <paramref name="timeZone"/>.</summary>
    public static DateOnly InTimeZone(DateTimeOffset instant, string timeZone)
    {
        if (instant == default) throw new ArgumentOutOfRangeException(nameof(instant), "Invalid instant.");
        var tz = AssertTimeZone(timeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);
    }
}
