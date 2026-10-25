using System.Text.Json;

namespace Hcc.Messaging.Tests.Harness;

/// <summary>Prototype figures exported by <c>Golden/export-messaging-golden.mjs</c> (regenerate after a prototype change).</summary>
public sealed record MessagingGolden(string BusinessDate, List<string> Placeholders, List<GoldenTemplate> Templates, List<GoldenRule> Rules, string Hotel, string Checkin, string Checkout,
    List<GoldenBooking> Bookings, List<GoldenRendered> Rendered, List<GoldenRecipient> Recipients, List<GoldenOutcome> Outcomes,
    Dictionary<string, List<GoldenStep>> Steps, List<GoldenSms> Sms, List<JsonElement> Unknown, List<GoldenPending> Pending, int AllEvents)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<MessagingGolden> Instance = new(() =>
        JsonSerializer.Deserialize<MessagingGolden>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "messaging-golden.json")), Options)
        ?? throw new InvalidOperationException("Empty golden file."));

    public static MessagingGolden Load() => Instance.Value;
}

public sealed record GoldenTemplate(string Id, string Name, string Channel, string Subject, string Body);

public sealed record GoldenRule(string Id, string Event, string Template, string Channel, bool Enabled);

public sealed record GoldenBooking(string Id, string Room, string Status, string Arrival, string Departure, string Guest, string? Phone, string? Email);

public sealed record GoldenRendered(string Booking, string Template, string Body, string Subject, string AmountText, string BalanceText);

public sealed record GoldenConsent(bool Whatsapp, bool Sms, bool Email, bool OptOut);

public sealed record GoldenRecipient(string Booking, string Channel, string Masked, bool Valid, string Block, GoldenConsent Consent);

public sealed record GoldenOutcome(string Id, string Channel, bool Valid, string Outcome);

public sealed record GoldenStep(string Status, int Offset);

public sealed record GoldenSms(int Length, int Segments);

public sealed record GoldenPending(string Key, string Rule, string Booking);
