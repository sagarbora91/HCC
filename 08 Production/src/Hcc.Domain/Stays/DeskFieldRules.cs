using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Stays;

/// <summary>Form C of a foreign guest's stay (LF-CONTRACT §8.3). The passport NUMBER is the guest's ID number and is not part of it.</summary>
public sealed record FormCFacts(
    string PassportPlace, DateOnly PassportIssued, DateOnly PassportExpiry,
    string VisaNo, string VisaType, string VisaPlace, DateOnly VisaIssued, DateOnly VisaExpiry,
    string ArrivedFrom, DateOnly ArrivalInIndia, int IntendedStayDays, string NextDestination,
    bool EmployedInIndia, string? EmployerOrBusiness, string AddressAbroad, string? ContactAbroad, char Sex);

/// <summary>What a late check-out costs, computed from the stay (A6).</summary>
public sealed record LateCheckoutResult(bool Late, DateTime DueOut, int MinutesPast, string Message, long HalfDayPaise);

/// <summary>
/// LF lf-desk rules (LF-CONTRACT §8; prototype domain/streams/desk-fields.ts): A4 vehicle number, A5 Form C validation and visa masking,
/// A6 due-out time and late check-out. Pure; the stores and the check-in / check-out commands call these.
/// </summary>
public static partial class DeskFieldRules
{
    public const string BasisFixed = "fixed";
    public const string Basis24h = "24h";

    [GeneratedRegex("^[A-Z0-9][A-Z0-9 -]{2,11}[A-Z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex VehiclePattern();

    // ── A4 vehicle ──
    /// <summary>Upper-case, spaces collapsed; 4–13 characters of A–Z 0–9 - space. Throws VALIDATION with the prototype's message.</summary>
    public static string NormaliseVehicle(string? raw)
    {
        var no = Regex.Replace((raw ?? string.Empty).Trim().ToUpperInvariant(), "\\s+", " ");
        if (no.Length == 0) throw HccException.Validation("Enter the vehicle number.");
        if (no.Length is < 4 or > 13 || !VehiclePattern().IsMatch(no))
            throw HccException.Validation("Vehicle number: 4–13 characters, letters, digits, hyphen or space (for example MH24 AB 1234).");
        return no;
    }

    /// <summary>Search key of a vehicle number (spaces and hyphens ignored).</summary>
    public static string VehicleKey(string? v) => Regex.Replace((v ?? string.Empty).ToUpperInvariant(), "[\\s-]", string.Empty);

    // ── A3 ──
    /// <summary>True when the nationality is filled in and is not Indian (Form C is needed).</summary>
    public static bool IsForeign(string? nationality)
    {
        var n = (nationality ?? string.Empty).Trim().ToLowerInvariant();
        return n.Length > 0 && n is not ("indian" or "india");
    }

    // ── A5 Form C ──
    /// <summary>Throws VALIDATION with the first problem of a Form C (null = valid). The guest must hold a passport ID and a date of birth.</summary>
    public static void ValidateFormC(FormCFacts f, DateOnly arrival, bool guestHasPassportId, bool guestHasDob)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!guestHasPassportId) throw HccException.Validation("Form C needs the guest’s ID type to be Passport — update the guest profile.");
        if (!guestHasDob) throw HccException.Validation("Form C needs the guest’s date of birth — update the guest profile.");
        void Text(string v, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) throw HccException.Validation($"Form C: enter {label}.");
            if (v.Trim().Length > max) throw HccException.Validation($"Form C: {label} is too long (maximum {max} characters).");
        }

        Text(f.PassportPlace, "the place the passport was issued", 60);
        Text(f.VisaNo, "the visa number", 30);
        Text(f.VisaType, "the visa type", 30);
        Text(f.VisaPlace, "the place the visa was issued", 60);
        Text(f.ArrivedFrom, "where the guest arrived from", 60);
        Text(f.NextDestination, "the next destination", 60);
        Text(f.AddressAbroad, "the permanent address abroad", 160);
        if (f.PassportIssued >= f.PassportExpiry) throw HccException.Validation("Form C: the passport expiry must be after its issue date.");
        if (f.PassportExpiry <= arrival) throw HccException.Validation("Form C: the passport expires on or before the arrival date.");
        if (f.VisaIssued >= f.VisaExpiry) throw HccException.Validation("Form C: the visa expiry must be after its issue date.");
        if (f.VisaExpiry <= arrival) throw HccException.Validation("Form C: the visa expires on or before the arrival date.");
        if (f.ArrivalInIndia > arrival) throw HccException.Validation("Form C: the arrival in India cannot be after this stay’s arrival.");
        if (f.IntendedStayDays is < 1 or > 3650) throw HccException.Validation("Form C: intended stay in India must be at least 1 day.");
        if (f.Sex is not ('M' or 'F' or 'X')) throw HccException.Validation("Form C: choose the sex of the guest.");
        if (f.EmployedInIndia && string.IsNullOrWhiteSpace(f.EmployerOrBusiness)) throw HccException.Validation("Form C: name the employer or business in India.");
        if ((f.EmployerOrBusiness?.Length ?? 0) > 80 || (f.ContactAbroad?.Length ?? 0) > 80) throw HccException.Validation("Form C: employer / contact text is too long (maximum 80 characters).");
    }

    /// <summary>The visa number as a viewer may see it: in full with PERM-DOC-004, else the last four characters (never the whole number).</summary>
    public static string VisaForViewer(string visaNo, bool canSeeIdDetails)
    {
        if (canSeeIdDetails) return visaNo;
        return visaNo.Length <= 4 ? "••••" : "••••" + visaNo[^4..];
    }

    // ── A6 due-out and late check-out ──
    /// <summary>Due-out moment: <c>fixed</c> → the departure date at the check-out time; <c>24h</c> → the check-in time + nights × 24 h
    /// (an old stay without a recorded check-in time counts from arrival at the check-in time).</summary>
    public static DateTime DueOut(string basis, DateOnly arrival, DateOnly departure, TimeOnly checkoutTime, TimeOnly checkinTime, DateTime? checkedInLocal)
    {
        if (basis == Basis24h)
        {
            var from = checkedInLocal ?? arrival.ToDateTime(checkinTime);
            return from.AddHours((departure.DayNumber - arrival.DayNumber) * 24);
        }

        return departure.ToDateTime(checkoutTime);
    }

    /// <summary>Is <paramref name="nowLocal"/> past the due-out time plus the grace? Half day = round(night rate × pct / 100), half away from zero (paise).</summary>
    public static LateCheckoutResult LateCheckout(DateTime dueOut, DateTime nowLocal, int graceHours, long nightRatePaise, int halfDayPct)
    {
        var past = (int)Math.Max(0, Math.Round((nowLocal - dueOut).TotalMinutes));
        var late = nowLocal > dueOut.AddHours(graceHours);
        var hhmm = string.Create(CultureInfo.InvariantCulture, $"{past / 60}:{past % 60:00}");
        var half = (long)Math.Round(nightRatePaise * (decimal)halfDayPct / 100m, MidpointRounding.AwayFromZero);
        return new LateCheckoutResult(late, dueOut, past, $"Guest is past the due-out time by {hhmm} — post the late check-out charge (half day) or extend the stay first.", half);
    }

    /// <summary>Early check-in warning (no charge): a fixed stay checked in before the check-in time minus the grace.</summary>
    public static bool IsEarlyCheckin(string basis, DateTime nowLocal, TimeOnly checkinTime, int graceHours) =>
        basis == BasisFixed && nowLocal < DateOnly.FromDateTime(nowLocal).ToDateTime(checkinTime).AddHours(-graceHours);
}
