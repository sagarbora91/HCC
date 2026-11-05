using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Operations.Meas;

/// <summary>
/// F12 MEAS guest captures (MEAS-CONTRACT §2): the rules of the survey, the monthly review figures and the root-cause list. Mirrors
/// <c>03 Prototype/src/domain/streams/services.ts</c> (block F12-CAP-GUEST: gsvSurveyProblem, gsvReviewProblem, gsvRootCauseListProblem).
/// A survey never stores ID data or guest identity: <see cref="SurveyInput"/> has no such field, and the comment is refused when it looks
/// like an ID number. Every refusal carries the MEAS reason in <c>details.reason</c> next to the shared VALIDATION code.
/// </summary>
public static class GuestRules
{
    public const string ItemSurvey = "meas.guest.survey";
    public const string ItemReputation = "meas.guest.reputation";
    public const string ItemRecovery = "meas.guest.recovery";
    public const string ItemRepeat = "meas.guest.repeat";
    public const int CommentMax = 500;
    public const int QrWindowMinutes = 10;
    public const int QrMax = 10;
    public const int LoyaltyTagMax = 20;
    public static readonly string[] Channels = ["qr", "desk", "link"];
    public static readonly string[] Platforms = ["google", "agoda", "makemytrip", "booking", "other"];

    /// <summary>The only fields a survey carries. No name, phone, ID number or photo exists in this type (and no column in meas.survey).</summary>
    public sealed record SurveyInput(string Via, int Overall, int Cleanliness, int Staff, int Value, int? Breakfast, int Recommend, string? Comment, bool Consent);

    public sealed record ReviewInput(string Month, string Platform, int Count, decimal AvgScore, decimal? Score12m, int Responded, string? Themes);

    public sealed record RootCauseRow(string Code, string Label, bool Active);

    /// <summary>First problem of a survey as <c>(reason, message)</c>; null = valid.</summary>
    public static (string Reason, string Message)? SurveyProblem(SurveyInput s)
    {
        if (Array.IndexOf(Channels, s.Via) < 0) return ("MEAS_VALIDATION", "Channel must be qr, desk or link.");
        if (!s.Consent) return ("MEAS_NO_CONSENT", "The guest must agree to the answers being kept (consent) before they are saved.");
        foreach (var (name, v) in new[] { ("overall", s.Overall), ("cleanliness", s.Cleanliness), ("staff", s.Staff), ("value", s.Value) })
            if (v is < 1 or > 5) return ("MEAS_VALIDATION", $"{name}: a whole score from 1 to 5.");
        if (s.Breakfast is < 1 or > 5) return ("MEAS_VALIDATION", "breakfast: a whole score from 1 to 5.");
        if (s.Recommend is < 0 or > 10) return ("MEAS_VALIDATION", "recommend: a whole score from 0 to 10.");
        if (s.Comment is { } c)
        {
            if (c.Length > CommentMax) return ("MEAS_VALIDATION", $"The comment is at most {CommentMax} characters.");
            if (GuestIdRules.LooksLikeIdNumber(c)) return ("MEAS_ID_DATA", "The comment looks like an ID number. A survey never stores ID data.");
        }
        return null;
    }

    public static string? ReviewProblem(ReviewInput r)
    {
        if (r.Month.Length != 7 || r.Month[4] != '-' || !int.TryParse(r.Month.AsSpan(0, 4), out _) || !int.TryParse(r.Month.AsSpan(5, 2), out var mm) || mm is < 1 or > 12)
            return "Month must be yyyy-MM.";
        if (Array.IndexOf(Platforms, r.Platform) < 0) return "Choose a platform: Google, Agoda, MakeMyTrip, Booking.com or other.";
        if (r.Count is < 0 or > 100000) return "Review count is a whole number.";
        if (r.AvgScore is < 0 or > 5) return "Average score is 0 to 5.";
        if (r.Score12m is < 0 or > 5) return "12-month score is 0 to 5.";
        if (r.Responded < 0 || r.Responded > r.Count) return "Responded is a whole number, not above the review count.";
        if (r.Themes is { } t && (t.Length > 200 || GuestIdRules.LooksLikeIdNumber(t))) return "Themes: short text (200 characters), no ID numbers.";
        return null;
    }

    public static string? RootCauseListProblem(IReadOnlyList<RootCauseRow>? rows)
    {
        if (rows is null || rows.Count is 0 or > 30) return "Give 1 to 30 root causes.";
        var seen = new HashSet<string>();
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.Code) || r.Code.Length > 12 || !r.Code.All(ch => ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-'))
                return "A code is 1 to 12 capital letters or digits.";
            if (!seen.Add(r.Code)) return $"Code {r.Code} is repeated.";
            if (string.IsNullOrWhiteSpace(r.Label) || r.Label.Length > 40) return $"{r.Code}: a label of up to 40 characters.";
        }
        return null;
    }

    /// <summary>True when the public QR page has already taken <see cref="QrMax"/> answers inside the last <see cref="QrWindowMinutes"/> minutes.</summary>
    public static bool RateLimited(IEnumerable<DateTime> qrAnswerTimesUtc, DateTime nowUtc) =>
        qrAnswerTimesUtc.Count(t => t > nowUtc.AddMinutes(-QrWindowMinutes)) >= QrMax;

    public static int? Nps(IReadOnlyCollection<int> recommend) =>
        recommend.Count == 0 ? null : (int)Math.Round((recommend.Count(x => x >= 9) - recommend.Count(x => x <= 6)) * 100.0 / recommend.Count, MidpointRounding.AwayFromZero);

    /// <summary>Throws the platform's VALIDATION error with <c>details.reason</c> = the MEAS reason.</summary>
    public static void EnsureSurvey(SurveyInput s)
    {
        if (SurveyProblem(s) is not { } p) return;
        throw new HccException(Hcc.Contracts.HccErrorCode.VALIDATION, p.Message, new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement(p.Reason),
        });
    }
}
