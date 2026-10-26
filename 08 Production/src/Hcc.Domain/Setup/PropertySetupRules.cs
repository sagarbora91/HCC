using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Setup;

/// <summary>The cleaned first-time setup values (property row + the Master configuration identity seeded from them).</summary>
public sealed record PropertySetupPlan(string Code, string Name, string Address, string State, string? Gstin, DateOnly BusinessDate,
    string TimeZone, string CheckInTime, string CheckOutTime);

/// <summary>
/// First-time hotel setup rules (W3-b SetupProperty): defaults (code MAIN, Maharashtra, Asia/Kolkata, 14:00 / 11:00, business date =
/// today in the hotel's time zone), lengths matching the Master configuration settings they seed, IANA time zone only, and a GSTIN
/// whose first two digits are the GST state code of the chosen state (intra-state CGST/SGST depends on it) and whose check character
/// is right. The GSTIN stays optional at setup, but no tax invoice is issued until a valid one is recorded (IssueInvoice names the setting).
/// </summary>
public static partial class PropertySetupRules
{
    public const string DefaultCode = "MAIN";
    public const string DefaultState = "Maharashtra";
    public const string DefaultTimeZone = "Asia/Kolkata";
    public const string DefaultCheckIn = "14:00";
    public const string DefaultCheckOut = "11:00";
    public const int NameMax = 60;      // = setting "hotel"
    public const int AddressMax = 120;  // = setting "brand.address"

    [GeneratedRegex(@"^[A-Z0-9-]{2,20}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[0-9]{2}[0-9A-Z]{13}$")]
    private static partial Regex GstinPattern();

    /// <summary>GST state / union territory codes (GSTIN digits 1–2).</summary>
    public static IReadOnlyDictionary<string, string> StateCodes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Jammu and Kashmir"] = "01", ["Himachal Pradesh"] = "02", ["Punjab"] = "03", ["Chandigarh"] = "04", ["Uttarakhand"] = "05",
        ["Haryana"] = "06", ["Delhi"] = "07", ["Rajasthan"] = "08", ["Uttar Pradesh"] = "09", ["Bihar"] = "10", ["Sikkim"] = "11",
        ["Arunachal Pradesh"] = "12", ["Nagaland"] = "13", ["Manipur"] = "14", ["Mizoram"] = "15", ["Tripura"] = "16", ["Meghalaya"] = "17",
        ["Assam"] = "18", ["West Bengal"] = "19", ["Jharkhand"] = "20", ["Odisha"] = "21", ["Chhattisgarh"] = "22", ["Madhya Pradesh"] = "23",
        ["Gujarat"] = "24", ["Dadra and Nagar Haveli and Daman and Diu"] = "26", ["Maharashtra"] = "27", ["Karnataka"] = "29", ["Goa"] = "30",
        ["Lakshadweep"] = "31", ["Kerala"] = "32", ["Tamil Nadu"] = "33", ["Puducherry"] = "34", ["Andaman and Nicobar Islands"] = "35",
        ["Telangana"] = "36", ["Andhra Pradesh"] = "37", ["Ladakh"] = "38",
    };

    public static PropertySetupPlan Plan(string? name, string? address, string? code, string? gstin, string? state, DateOnly? businessDate,
        string? timeZone, string? checkIn, string? checkOut, DateTimeOffset now)
    {
        var n = Text(name, "Hotel name", NameMax);
        var a = Text(address, "Address", AddressMax);
        var c = string.IsNullOrWhiteSpace(code) ? DefaultCode : code.Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(c)) throw HccException.Validation("The property code is 2–20 capital letters, digits or hyphens.");
        var stateName = string.IsNullOrWhiteSpace(state) ? DefaultState : state.Trim();
        if (!StateCodes.TryGetValue(stateName, out var stateCode)) throw HccException.Validation($"Unknown state or union territory '{stateName}'.");
        stateName = StateCodes.Keys.First(k => string.Equals(k, stateName, StringComparison.OrdinalIgnoreCase)); // canonical spelling
        string? g = null;
        if (!string.IsNullOrWhiteSpace(gstin))
        {
            g = gstin.Trim().ToUpperInvariant();
            if (!GstinPattern().IsMatch(g)) throw HccException.Validation("A GSTIN has 15 capital letters and digits.");
            if (!g.StartsWith(stateCode, StringComparison.Ordinal))
                throw HccException.Validation($"This GSTIN is not registered in {stateName}: it must start with {stateCode}.");
            // Same rule as invoicing and Master configuration (brand.gstin): a GSTIN with a wrong check character would block every invoice.
            if (Hcc.Domain.Invoices.Gstin.Problem(g, "hotel") is { } problem) throw HccException.Validation(problem);
        }

        var tzId = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
        TimeZoneInfo tz;
        try
        {
            tz = CalendarDate.AssertTimeZone(tzId);
        }
        catch (InvalidPropertyTimeZoneException)
        {
            throw HccException.Validation($"'{tzId}' is not an IANA time zone (for India use Asia/Kolkata).");
        }

        var bd = businessDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        return new PropertySetupPlan(c, n, a, stateName, g, bd, tzId, Time(checkIn, DefaultCheckIn, "Check-in time"), Time(checkOut, DefaultCheckOut, "Checkout time"));
    }

    private static string Text(string? value, string label, int max)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) throw HccException.Validation($"{label} is required.");
        if (t.Length > max) throw HccException.Validation($"{label} must be at most {max} characters.");
        return t;
    }

    private static string Time(string? value, string fallback, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw HccException.Validation($"{label} must be HH:mm (24-hour), for example 14:00.");
        return t.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
