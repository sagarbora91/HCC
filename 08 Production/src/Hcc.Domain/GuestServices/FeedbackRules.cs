namespace Hcc.Domain.GuestServices;

/// <summary>F4-T3 qw-feedback: the pure rules of guest feedback at check-out (prototype <c>domain/streams/feedback.ts</c>).</summary>
public static class FeedbackRules
{
    /// <summary>Stars at or below this open a complaint on their own.</summary>
    public const int LowStars = 2;
    /// <summary>A complaint or lost-and-found item unresolved this many days gets an Owner follow-up call.</summary>
    public const int OverdueDays = 7;
    public const int MaxComment = 400;

    public static bool StarsValid(int stars) => stars is >= 1 and <= 5;

    /// <summary>A complaint opens for 2 stars or fewer, or for a comment that carries a (valid) complaint category.</summary>
    public static bool OpensComplaint(int stars, string? comment, string? category) =>
        stars <= LowStars || (!string.IsNullOrWhiteSpace(comment) && category is not null && GuestServiceVocab.ComplaintCategories.Contains(category));

    /// <summary>1 star High, 2 stars Medium, otherwise Low (same mapping as the prototype).</summary>
    public static string Severity(int stars) => stars <= 1 ? "High" : stars == 2 ? "Medium" : "Low";

    public static bool IsOverdue(DateOnly since, DateOnly today) => today.DayNumber - since.DayNumber >= OverdueDays;

    public static double? Average(IReadOnlyCollection<int> stars) => stars.Count == 0 ? null : Math.Round(stars.Average(), 1, MidpointRounding.AwayFromZero);
}
