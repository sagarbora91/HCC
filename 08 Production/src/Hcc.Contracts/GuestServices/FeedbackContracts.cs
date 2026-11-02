namespace Hcc.Contracts.GuestServices;

/// <summary>F4-T3 qw-feedback: guest feedback at check-out (1-5 stars, optional comment, optional complaint category).</summary>
public sealed record RecordGuestFeedbackRequest(long StayId, int Stars, string? Comment, string? Category);

public sealed record FeedbackResult(long FeedbackId, long? ComplaintId);

public sealed record FeedbackView(long FeedbackId, long StayId, string RoomNo, string GuestName, int Stars, string? Comment, long? ComplaintId, DateTime RecordedUtc, int RecordedBy);

public sealed record FeedbackPage(int Total, double? Average, IReadOnlyList<FeedbackView> Items);

/// <summary>Owner follow-up call for a complaint or lost-and-found item unresolved 7+ days. Source: complaint | lostfound.</summary>
public sealed record FeedbackFollowUpView(long FollowUpId, string Source, long RefId, DateOnly Since, string Status, DateTime CreatedUtc, DateTime? DoneUtc);

public sealed record ResolveFeedbackFollowUpRequest(long FollowUpId);
