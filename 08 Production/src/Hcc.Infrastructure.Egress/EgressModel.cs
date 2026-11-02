using System.Globalization;
using System.Text.Json;

namespace Hcc.Infrastructure.Egress;

/// <summary>Everything that may ever talk to the internet (EGRESS-CONTRACT §1). Setting key of each switch = <see cref="EgressKeys.Of"/>.</summary>
public enum EgressIntegration
{
    Messaging,
    Ota,
    Ocr,
    Payments,
    Email,
    UpdateCheck,
}

/// <summary>Result of one evaluation. Reason: <c>master-off</c>, <c>integration-off</c>, <c>outside-window</c>, <c>settings-unreadable</c>, <c>integration-missing</c> or <c>on</c>.</summary>
public sealed record EgressDecision(bool Allowed, string Reason)
{
    public static EgressDecision On { get; } = new(true, EgressReasons.On);

    public static EgressDecision Off(string reason) => new(false, reason);
}

/// <summary>An allowed window in hotel local time: <see cref="From"/> inclusive, <see cref="To"/> exclusive; From &gt; To spans midnight (the day is the day it started).</summary>
public sealed record EgressWindow(TimeOnly From, TimeOnly To, IReadOnlyList<DayOfWeek> Days);

/// <summary>The state for System health and clients (user ids only for the Owner; see <c>EgressHealthView</c>).</summary>
public sealed record EgressState(bool MasterOn, IReadOnlyDictionary<EgressIntegration, bool> Integrations, EgressWindow? Window, bool EffectiveNow,
    DateTimeOffset? ChangedAt, string? ChangedBy, string? Reason);

public static class EgressReasons
{
    public const string On = "on";
    public const string MasterOff = "master-off";
    public const string IntegrationOff = "integration-off";
    public const string OutsideWindow = "outside-window";
    public const string SettingsUnreadable = "settings-unreadable";
    public const string IntegrationMissing = "integration-missing";
}

/// <summary>Setting keys (group Internet, Master configuration) and the request option key.</summary>
public static class EgressKeys
{
    public const string Master = "net.internetEnabled";
    public const string Window = "net.window";
    public const string ClientName = "egress";

    /// <summary><see cref="HttpRequestOptions"/> key every egress request must carry.</summary>
    public const string OptionName = "hcc.egress.integration";

    public static readonly HttpRequestOptionsKey<EgressIntegration> Option = new(OptionName);

    public static string Of(EgressIntegration i) => i switch
    {
        EgressIntegration.Messaging => "net.messaging",
        EgressIntegration.Ota => "net.ota",
        EgressIntegration.Ocr => "net.ocr",
        EgressIntegration.Payments => "net.payments",
        EgressIntegration.Email => "net.email",
        EgressIntegration.UpdateCheck => "net.updateCheck",
        _ => throw new ArgumentOutOfRangeException(nameof(i), i, "Unknown integration."),
    };

    /// <summary>Wire / audit name ("messaging", "updateCheck").</summary>
    public static string Name(EgressIntegration i) => Of(i)[4..];

    public static IReadOnlyList<EgressIntegration> All { get; } = Enum.GetValues<EgressIntegration>();

    /// <summary>Marks a request with its integration (a request without it is refused: <c>integration-missing</c>).</summary>
    public static HttpRequestMessage For(this HttpRequestMessage request, EgressIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(Option, integration);
        return request;
    }
}

/// <summary>
/// The raw settings as read from the store (null = missing or unreadable → OFF). <see cref="WindowJson"/> = the <c>net.window</c> text
/// (<c>{"from":"HH:mm","to":"HH:mm","days":[0..6]}</c>, empty / null = no window).
/// </summary>
public sealed record EgressSettings(bool? Master, IReadOnlyDictionary<EgressIntegration, bool?> Switches, string? WindowJson,
    DateTimeOffset? ChangedAt = null, string? ChangedBy = null, string? Reason = null);

/// <summary>Pure rules (no I/O): the effective rule and the window parser.</summary>
public static class EgressRules
{
    /// <summary>EffectiveNow(i) = master &amp;&amp; net.&lt;i&gt; &amp;&amp; (window == null || local ∈ window). Never throws: anything unreadable is OFF.</summary>
    public static EgressDecision Evaluate(EgressSettings? s, EgressIntegration integration, DateTimeOffset localNow)
    {
        if (s is null) return EgressDecision.Off(EgressReasons.SettingsUnreadable);
        if (s.Master is null) return EgressDecision.Off(EgressReasons.SettingsUnreadable);
        if (s.Master == false) return EgressDecision.Off(EgressReasons.MasterOff);
        if (!s.Switches.TryGetValue(integration, out var sw) || sw is null) return EgressDecision.Off(EgressReasons.SettingsUnreadable);
        if (sw == false) return EgressDecision.Off(EgressReasons.IntegrationOff);
        if (!TryParseWindow(s.WindowJson, out var w)) return EgressDecision.Off(EgressReasons.SettingsUnreadable);
        if (w is not null && !InWindow(w, localNow)) return EgressDecision.Off(EgressReasons.OutsideWindow);
        return EgressDecision.On;
    }

    /// <summary>From inclusive, To exclusive; across midnight when From &gt; To (then the late part counts for the start day). From == To = never.</summary>
    public static bool InWindow(EgressWindow w, DateTimeOffset localNow)
    {
        ArgumentNullException.ThrowIfNull(w);
        var t = TimeOnly.FromDateTime(localNow.DateTime);
        var day = localNow.DayOfWeek;
        if (w.From < w.To) return w.Days.Contains(day) && t >= w.From && t < w.To;
        if (w.From == w.To) return false;
        if (t >= w.From) return w.Days.Contains(day);
        if (t < w.To) return w.Days.Contains((DayOfWeek)(((int)day + 6) % 7));
        return false;
    }

    /// <summary>False = present but invalid (→ settings-unreadable). Null / empty / "null" = no window.</summary>
    public static bool TryParseWindow(string? json, out EgressWindow? window)
    {
        window = null;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            if (!TimeOnly.TryParseExact(r.GetProperty("from").GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)) return false;
            if (!TimeOnly.TryParseExact(r.GetProperty("to").GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)) return false;
            var days = new List<DayOfWeek>();
            foreach (var d in r.GetProperty("days").EnumerateArray())
            {
                var n = d.GetInt32();
                if (n is < 0 or > 6) return false;
                if (!days.Contains((DayOfWeek)n)) days.Add((DayOfWeek)n);
            }

            if (days.Count == 0) return false;
            window = new EgressWindow(from, to, days);
            return true;
        }
#pragma warning disable CA1031 // Any malformed value is "unreadable" (fail closed), never an exception.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>Validation text for the <c>net.window</c> setting (null = valid).</summary>
    public static string? WindowProblem(string? json) =>
        TryParseWindow(json, out _) ? null : "Write the window as {\"from\":\"HH:mm\",\"to\":\"HH:mm\",\"days\":[0-6]} (0 = Sunday), or leave it empty for no window.";
}
