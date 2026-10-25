using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Messaging;

/// <summary>Channels (prototype <c>MSGChannel</c>; SQL <c>msg.*.channel</c>).</summary>
public static class MsgChannel
{
    public const string WhatsApp = "WhatsApp";
    public const string Sms = "SMS";
    public const string Email = "Email";

    public static readonly IReadOnlyList<string> All = [WhatsApp, Sms, Email];

    public static bool IsValid(string? channel) => channel is WhatsApp or Sms or Email;
}

/// <summary>Delivery statuses shown on the wire (prototype <c>MSGStatus</c>).</summary>
public static class MsgStatus
{
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Read = "Read";
    public const string Failed = "Failed";
    public const string NotSent = "Not sent";
}

/// <summary>Predetermined simulated outcomes (SQL <c>msg.message.outcome</c>).</summary>
public static class MsgOutcome
{
    public const string Read = "Read";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
}

/// <summary>Trigger events of the rules (prototype <c>MSGEvent</c>).</summary>
public static class MsgEvent
{
    public const string BookingConfirmed = "booking-confirmed";
    public const string PreArrival = "pre-arrival";
    public const string CheckedIn = "checked-in";
    public const string PaymentReceived = "payment-received";
    public const string CheckedOut = "checked-out";

    public static readonly IReadOnlyList<string> All = [BookingConfirmed, PreArrival, CheckedIn, PaymentReceived, CheckedOut];
}

public sealed record MsgTemplateDef(string Code, string Name, string Channel, string Subject, string Body);

public sealed record MsgRuleDef(string RuleId, string Event, string TemplateCode, string Channel, bool Enabled);

/// <summary>Per-guest consent; no stored record means everything allowed (transactional consent captured at booking).</summary>
public sealed record MsgConsent(bool WhatsApp, bool Sms, bool Email, bool OptOut)
{
    public static readonly MsgConsent Default = new(true, true, true, false);

    public bool Allows(string channel) => channel switch { MsgChannel.WhatsApp => WhatsApp, MsgChannel.Sms => Sms, _ => Email };
}

/// <summary>Guest contact details as stored on the guest profile (never an ID number).</summary>
public sealed record MsgContact(string? Phone, string? Email);

public sealed record MsgRecipient(bool Present, bool Valid, string Masked);

/// <summary>One step of a message's simulated delivery path, <see cref="Offset"/> minutes after queuing.</summary>
public sealed record MsgStep(string Status, int Offset);

/// <summary>Everything needed to fill the placeholders of a template.</summary>
public sealed record MsgBookingFacts(string Hotel, string PublicNo, string GuestName, string? RoomNo, DateOnly Arrival, DateOnly Departure,
    string CheckInTime, string CheckOutTime, long TotalPaise, long BalancePaise, long? ReceiptPaise);

/// <summary>
/// Messaging rules ported from the prototype (<c>03 Prototype/src/domain/streams/messaging.ts</c>): placeholders, templates and
/// default rules, consent blocking, masked recipients, deterministic simulated delivery. Pure; no I/O; nothing is ever sent.
/// </summary>
public static partial class MessagingRules
{
    public const int ManualBodyMax = 600;
    public const int TemplateBodyMin = 10;
    public const int TemplateBodyMax = 600;
    public const int SubjectMin = 3;
    public const int SubjectMax = 120;

    public static readonly IReadOnlyList<string> Placeholders =
        ["guest", "firstName", "hotel", "booking", "room", "arrival", "departure", "nights", "checkin", "checkout", "amount", "balance", "invoiceLink", "feedbackLink"];

    public static readonly IReadOnlyList<MsgTemplateDef> DefaultTemplates =
    [
        new("booking-confirmation", "Booking confirmation", MsgChannel.WhatsApp, "Your booking {booking} is confirmed",
            "Namaste {firstName}, your stay at {hotel} is confirmed. Booking {booking}, room {room}, {arrival} to {departure} ({nights} nights). Check-in from {checkin}."),
        new("pre-arrival", "Pre-arrival", MsgChannel.WhatsApp, "See you soon at {hotel}",
            "Hello {firstName}, we look forward to welcoming you on {arrival}. Check-in is from {checkin}. Reply here for airport pickup or early arrival requests."),
        new("checkin-welcome", "Check-in welcome", MsgChannel.WhatsApp, "Welcome to {hotel}",
            "Welcome to {hotel}, {firstName}! You are in room {room} until {departure}. Checkout is at {checkout}. Dial 9 for the front desk."),
        new("payment-receipt", "Payment receipt", MsgChannel.Sms, "Payment received · {booking}",
            "{hotel}: payment of {amount} received for booking {booking}. Balance due {balance}. Thank you."),
        new("checkout-invoice", "Checkout invoice link", MsgChannel.Email, "Your invoice for booking {booking}",
            "Dear {guest}, thank you for staying with us. Your sample invoice for {booking} is at {invoiceLink} (simulated link)."),
        new("feedback", "Feedback request", MsgChannel.WhatsApp, "How was your stay?",
            "Hi {firstName}, how was your stay at {hotel}? Rate us in one tap: {feedbackLink} (simulated link)."),
    ];

    public static readonly IReadOnlyList<MsgRuleDef> DefaultRules =
    [
        new("R1", MsgEvent.BookingConfirmed, "booking-confirmation", MsgChannel.WhatsApp, true),
        new("R2", MsgEvent.PreArrival, "pre-arrival", MsgChannel.WhatsApp, true),
        new("R3", MsgEvent.CheckedIn, "checkin-welcome", MsgChannel.WhatsApp, true),
        new("R4", MsgEvent.PaymentReceived, "payment-receipt", MsgChannel.Sms, true),
        new("R5", MsgEvent.CheckedOut, "checkout-invoice", MsgChannel.Email, true),
        new("R6", MsgEvent.CheckedOut, "feedback", MsgChannel.WhatsApp, false),
    ];

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex PlaceholderPattern();

    // Aadhaar-shaped numbers: 12 digits, optionally grouped 4-4-4 with spaces or hyphens (DEC-068: never in a message).
    [GeneratedRegex(@"(?<!\d)\d{4}[ -]?\d{4}[ -]?\d{4}(?!\d)")]
    private static partial Regex AadhaarShape();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailShape();

    /// <summary>Placeholders used in <paramref name="text"/> that are not on the whitelist (order of first use).</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<string>();
        foreach (Match m in PlaceholderPattern().Matches(text))
        {
            var name = m.Groups[1].Value;
            if (!Placeholders.Contains(name, StringComparer.Ordinal) && !found.Contains(name, StringComparer.Ordinal)) found.Add(name);
        }

        return found;
    }

    /// <summary>True when the text holds a 12-digit (Aadhaar-shaped) number, which may never travel in a message (DEC-068).</summary>
    public static bool LooksLikeAadhaar(string? text) => AadhaarShape().IsMatch(text ?? string.Empty);

    /// <summary>Fills <c>{placeholders}</c>; unknown ones stay visible as <c>[name?]</c> and are reported.</summary>
    public static (string Text, IReadOnlyList<string> Missing) Render(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        var missing = new List<string>();
        var rendered = PlaceholderPattern().Replace(text, m =>
        {
            var key = m.Groups[1].Value;
            if (values.TryGetValue(key, out var v)) return v;
            missing.Add(key);
            return $"[{key}?]";
        });
        return (rendered, missing);
    }

    /// <summary>Placeholder values for a booking (prototype <c>msgValues</c>). Links are simulated and never resolve.</summary>
    public static IReadOnlyDictionary<string, string> Values(MsgBookingFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var firstName = f.GuestName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? f.GuestName;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["guest"] = f.GuestName,
            ["firstName"] = firstName,
            ["hotel"] = f.Hotel,
            ["booking"] = f.PublicNo,
            ["room"] = f.RoomNo ?? "to be assigned",
            ["arrival"] = FormatDate(f.Arrival),
            ["departure"] = FormatDate(f.Departure),
            ["nights"] = (f.Departure.DayNumber - f.Arrival.DayNumber).ToString(CultureInfo.InvariantCulture),
            ["checkin"] = f.CheckInTime,
            ["checkout"] = f.CheckOutTime,
            ["amount"] = FormatMoney(f.ReceiptPaise ?? f.TotalPaise),
            ["balance"] = FormatMoney(Math.Max(0, f.BalancePaise)),
            ["invoiceLink"] = "https://demo.invalid/inv/" + f.PublicNo,
            ["feedbackLink"] = "https://demo.invalid/fb/" + f.PublicNo,
        };
    }

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"23 Oct 2026".</summary>
    public static string FormatDate(DateOnly d) => $"{d.Day.ToString(CultureInfo.InvariantCulture)} {Months[d.Month - 1]} {d.Year.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Rupee text with Indian digit grouping: "₹2,200", "₹1,23,456.50" (decimals only when the amount has paise).</summary>
    public static string FormatMoney(long paise)
    {
        var negative = paise < 0;
        var abs = Math.Abs(paise);
        var rupees = (abs / 100).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (rupees.Length <= 3) sb.Append(rupees);
        else
        {
            var head = rupees[..^3];
            for (var i = 0; i < head.Length; i++)
            {
                if (i > 0 && (head.Length - i) % 2 == 0) sb.Append(',');
                sb.Append(head[i]);
            }

            sb.Append(',').Append(rupees[^3..]);
        }

        var text = "₹" + sb;
        if (abs % 100 != 0) text += "." + (abs % 100).ToString("00", CultureInfo.InvariantCulture);
        return negative ? "-" + text : text;
    }

    /// <summary>SMS segments: 160 GSM characters each, 153 when concatenated.</summary>
    public static int SmsSegments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= 160 ? 1 : (int)Math.Ceiling(text.Length / 153.0);
    }

    // ── Recipients and consent ──
    private static string DigitsOf(string? phone)
    {
        var d = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length > 10 ? d[^10..] : d;
    }

    /// <summary>The recipient for a channel: present?, valid?, masked text (••••••1234 / a•••@example.test). Never the full contact.</summary>
    public static MsgRecipient Recipient(MsgContact contact, string channel)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (channel == MsgChannel.Email)
        {
            var e = contact.Email ?? string.Empty;
            if (e.Length == 0) return new MsgRecipient(false, false, string.Empty);
            var parts = e.Split('@');
            var user = parts[0];
            var domain = parts.Length > 1 ? parts[1] : string.Empty;
            return new MsgRecipient(true, EmailShape().IsMatch(e), (user.Length > 0 ? user[..1] : string.Empty) + "•••@" + domain);
        }

        var p = contact.Phone ?? string.Empty;
        if (p.Length == 0) return new MsgRecipient(false, false, string.Empty);
        var d = DigitsOf(p);
        return new MsgRecipient(true, d.Length == 10, "••••••" + (d.Length >= 4 ? d[^4..] : d));
    }

    /// <summary>Why a message on this channel cannot be sent ("" = allowed). An invalid number is allowed but fails delivery.</summary>
    public static string BlockReason(MsgConsent consent, MsgContact contact, string channel)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (consent.OptOut) return "Guest opted out of messages";
        if (!consent.Allows(channel)) return $"No {channel} consent";
        if (!Recipient(contact, channel).Present) return channel == MsgChannel.Email ? "No email address on file" : "No phone number on file";
        return string.Empty;
    }

    // ── Simulated delivery (deterministic; prototype msgOutcome / msgSteps) ──
    internal static uint Hash(string text)
    {
        uint h = 7;
        foreach (var c in text) h = unchecked((h * 31) + c);
        return h;
    }

    /// <summary>The wire id the simulation hashes: <c>MSG-&lt;messageId&gt;</c>.</summary>
    public static string WireId(long messageId) => "MSG-" + messageId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Predetermined outcome at queue time: invalid recipient or a deterministic 1-in-7 hash → Failed; WhatsApp mostly Read.</summary>
    public static string Outcome(string wireId, string channel, bool validRecipient)
    {
        ArgumentNullException.ThrowIfNull(wireId);
        if (!validRecipient || Hash(wireId + channel) % 7 == 3) return MsgOutcome.Failed;
        return channel == MsgChannel.WhatsApp && Hash(wireId) % 3 != 0 ? MsgOutcome.Read : MsgOutcome.Delivered;
    }

    /// <summary>The outcome a retried message gets: simulated success (WhatsApp Read, otherwise Delivered).</summary>
    public static string RetryOutcome(string channel) => channel == MsgChannel.WhatsApp ? MsgOutcome.Read : MsgOutcome.Delivered;

    /// <summary>Steps (minutes after queuing) of a message's path; a blocked message has the single step "Not sent".</summary>
    public static IReadOnlyList<MsgStep> Steps(string outcome, bool blocked)
    {
        if (blocked) return [new MsgStep(MsgStatus.NotSent, 0)];
        var steps = new List<MsgStep> { new(MsgStatus.Queued, 0), new(MsgStatus.Sent, 1) };
        if (outcome == MsgOutcome.Failed) steps.Add(new MsgStep(MsgStatus.Failed, 2));
        else
        {
            steps.Add(new MsgStep(MsgStatus.Delivered, 3));
            if (outcome == MsgOutcome.Read) steps.Add(new MsgStep(MsgStatus.Read, 8));
        }

        return steps;
    }

    private static int RealMinutes(DateTime queuedUtc, DateTime nowUtc) => Math.Max(0, (int)Math.Floor((nowUtc - queuedUtc).TotalMinutes));

    /// <summary>Whole simulated minutes since queuing plus "Simulate delivery" boosts.</summary>
    public static int Elapsed(DateTime queuedUtc, DateTime nowUtc, int boostMinutes) => RealMinutes(queuedUtc, nowUtc) + boostMinutes;

    /// <summary>The timeline reached so far: each step with its (real or boosted) time.</summary>
    public static IReadOnlyList<(string Status, DateTime AtUtc)> Timeline(DateTime queuedUtc, DateTime nowUtc, int boostMinutes, string outcome, bool blocked)
    {
        var elapsed = Elapsed(queuedUtc, nowUtc, boostMinutes);
        var real = RealMinutes(queuedUtc, nowUtc);
        return Steps(outcome, blocked).Where(s => s.Offset <= elapsed).Select(s => (s.Status, queuedUtc.AddMinutes(Math.Min(s.Offset, real)))).ToList();
    }

    public static string StatusAt(DateTime queuedUtc, DateTime nowUtc, int boostMinutes, string outcome, bool blocked) =>
        Timeline(queuedUtc, nowUtc, boostMinutes, outcome, blocked)[^1].Status;

    /// <summary>Minutes to add so the message reaches its next step (0 when it is at the end of its path).</summary>
    public static int NextBoost(DateTime queuedUtc, DateTime nowUtc, int boostMinutes, string outcome, bool blocked)
    {
        var elapsed = Elapsed(queuedUtc, nowUtc, boostMinutes);
        var next = Steps(outcome, blocked).FirstOrDefault(s => s.Offset > elapsed);
        return next is null ? 0 : next.Offset - elapsed;
    }

    // ── Validation shared by commands ──
    public static void ValidateTemplateText(string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length < TemplateBodyMin || body.Length > TemplateBodyMax) throw HccException.Validation($"Template text must be {TemplateBodyMin}–{TemplateBodyMax} characters.");
        if (subject.Length < SubjectMin || subject.Length > SubjectMax) throw HccException.Validation($"Subject must be {SubjectMin}–{SubjectMax} characters.");
        var unknown = UnknownPlaceholders(body).Concat(UnknownPlaceholders(subject)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0) throw HccException.Validation("Unknown placeholder: {" + string.Join("}, {", unknown) + "}. Use only the listed placeholders.");
        if (LooksLikeAadhaar(body) || LooksLikeAadhaar(subject)) throw HccException.Validation("A message may not contain an ID number.");
    }
}
