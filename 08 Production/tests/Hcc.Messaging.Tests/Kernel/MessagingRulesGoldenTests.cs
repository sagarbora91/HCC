using System.Globalization;
using Hcc.Domain.Messaging;
using Hcc.Messaging.Tests.Harness;

namespace Hcc.Messaging.Tests.Kernel;

/// <summary>Messaging rules against the prototype's figures (no database).</summary>
public class MessagingRulesGoldenTests
{
    private static readonly MessagingGolden G = MessagingGolden.Load();

    private static long Paise(string text) => (long)(decimal.Parse(text.Replace("₹", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture) * 100);

    [Fact]
    public void Placeholder_whitelist_templates_and_rules_equal_the_prototype()
    {
        Assert.Equal(G.Placeholders, MessagingRules.Placeholders);
        Assert.Equal(G.Templates.Select(t => (t.Id, t.Name, t.Channel, t.Subject, t.Body)), MessagingRules.DefaultTemplates.Select(t => (t.Code, t.Name, t.Channel, t.Subject, t.Body)));
        Assert.Equal(G.Rules.Select(r => (r.Id, r.Event, r.Template, r.Channel, r.Enabled)), MessagingRules.DefaultRules.Select(r => (r.RuleId, r.Event, r.TemplateCode, r.Channel, r.Enabled)));
        Assert.False(MessagingRules.DefaultRules.Single(r => r.RuleId == "R6").Enabled);
    }

    [Fact]
    public void Rendered_subjects_and_bodies_match_the_prototype_for_every_booking_and_template()
    {
        Assert.Equal(36, G.Rendered.Count);
        foreach (var r in G.Rendered)
        {
            var b = G.Bookings.Single(x => x.Id == r.Booking);
            var total = Paise(r.AmountText);
            var facts = new MsgBookingFacts(G.Hotel, b.Id, b.Guest, b.Room, DateOnly.Parse(b.Arrival, CultureInfo.InvariantCulture), DateOnly.Parse(b.Departure, CultureInfo.InvariantCulture),
                G.Checkin, G.Checkout, total, Paise(r.BalanceText), null);
            var values = MessagingRules.Values(facts);
            var t = MessagingRules.DefaultTemplates.Single(x => x.Code == r.Template);
            Assert.Equal(r.Body, MessagingRules.Render(t.Body, values).Text);
            Assert.Equal(r.Subject, MessagingRules.Render(t.Subject, values).Text);
            Assert.Equal(r.AmountText, values["amount"]);
            Assert.Equal(r.BalanceText, values["balance"]);
        }
    }

    [Fact]
    public void Recipients_are_masked_and_consent_blocking_matches()
    {
        foreach (var r in G.Recipients)
        {
            var b = G.Bookings.Single(x => x.Id == r.Booking);
            // The prototype's masked phone is the last 4 of a 10-digit number: rebuild a number with the same tail.
            var phone = r.Channel == "Email" ? null : "982000" + r.Masked[^4..];
            var contact = new MsgContact(phone, b.Email);
            var rec = MessagingRules.Recipient(contact, r.Channel);
            Assert.Equal(r.Masked, rec.Masked);
            Assert.Equal(r.Valid, rec.Valid);
            Assert.Equal(r.Block, MessagingRules.BlockReason(new MsgConsent(r.Consent.Whatsapp, r.Consent.Sms, r.Consent.Email, r.Consent.OptOut), contact, r.Channel));
        }

        Assert.Equal("Guest opted out of messages", MessagingRules.BlockReason(new MsgConsent(true, true, true, true), new MsgContact("9820001111", null), "SMS"));
        Assert.Equal("No SMS consent", MessagingRules.BlockReason(new MsgConsent(true, false, true, false), new MsgContact("9820001111", null), "SMS"));
        Assert.Equal("No email address on file", MessagingRules.BlockReason(MsgConsent.Default, new MsgContact("9820001111", null), "Email"));
        Assert.Equal("No phone number on file", MessagingRules.BlockReason(MsgConsent.Default, new MsgContact("", null), "WhatsApp"));
        Assert.Equal(string.Empty, MessagingRules.BlockReason(MsgConsent.Default, new MsgContact("123", null), "WhatsApp")); // invalid number is allowed, fails delivery
        Assert.False(MessagingRules.Recipient(new MsgContact("123", null), "SMS").Valid);
    }

    [Fact]
    public void Simulated_outcomes_equal_the_prototype_hash_for_40_ids_3_channels()
    {
        Assert.Equal(240, G.Outcomes.Count);
        foreach (var o in G.Outcomes)
        {
            var id = long.Parse(o.Id[4..], CultureInfo.InvariantCulture);
            Assert.Equal(o.Outcome, MessagingRules.Outcome(MessagingRules.WireId(id), o.Channel, o.Valid));
        }

        Assert.Contains(G.Outcomes, o => o.Valid && o.Outcome == "Failed"); // the 1-in-7 hash really fires
    }

    [Fact]
    public void Delivery_paths_equal_the_prototype()
    {
        foreach (var outcome in new[] { "Read", "Delivered", "Failed" })
            Assert.Equal(G.Steps[outcome].Select(s => (s.Status, s.Offset)), MessagingRules.Steps(outcome, false).Select(s => (s.Status, s.Offset)));
        Assert.Equal(G.Steps["blocked"].Select(s => (s.Status, s.Offset)), MessagingRules.Steps("Read", true).Select(s => (s.Status, s.Offset)));
        var q = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Queued", "Sent", "Delivered", "Read"], new[] { 0, 1, 3, 8 }.Select(m => MessagingRules.StatusAt(q, q.AddMinutes(m), 0, "Read", false)));
        Assert.Equal("Sent", MessagingRules.StatusAt(q, q.AddMinutes(2), 0, "Delivered", false));
        Assert.Equal("Failed", MessagingRules.StatusAt(q, q.AddMinutes(2), 0, "Failed", false));
        Assert.Equal("Not sent", MessagingRules.StatusAt(q, q.AddMinutes(30), 0, "Read", true));
        Assert.Equal(1, MessagingRules.NextBoost(q, q, 0, "Read", false));
        Assert.Equal(2, MessagingRules.NextBoost(q, q, 1, "Read", false));
        Assert.Equal(0, MessagingRules.NextBoost(q, q, 8, "Read", false));
    }

    [Fact]
    public void Sms_segments_and_placeholder_validation_match()
    {
        foreach (var s in G.Sms) Assert.Equal(s.Segments, MessagingRules.SmsSegments(new string('x', s.Length)));
        Assert.Equal(["nope", "x"], MessagingRules.UnknownPlaceholders("Hi {guest} {nope} {x} {nope}"));
        Assert.Empty(MessagingRules.UnknownPlaceholders("{firstName} ok"));
        Assert.Equal("Hi [nope?]", MessagingRules.Render("Hi {nope}", new Dictionary<string, string>()).Text);
    }

    [Fact]
    public void Indian_money_grouping_and_id_number_guard()
    {
        Assert.Equal("₹2,200", MessagingRules.FormatMoney(220000));
        Assert.Equal("₹1,23,456.50", MessagingRules.FormatMoney(12345650));
        Assert.Equal("₹0", MessagingRules.FormatMoney(0));
        Assert.Equal("₹999", MessagingRules.FormatMoney(99900));
        Assert.True(MessagingRules.LooksLikeAadhaar("my id 1234 5678 9012 ok"));
        Assert.True(MessagingRules.LooksLikeAadhaar("123456789012"));
        Assert.False(MessagingRules.LooksLikeAadhaar("call 9820001234"));
        Assert.False(MessagingRules.LooksLikeAadhaar("booking 1234567"));
    }

    [Fact]
    public void The_prototype_base_fixture_has_exactly_the_two_pre_arrival_triggers_pending()
    {
        Assert.Equal(["R2:HCC-1049", "R2:HCC-1050"], G.Pending.Select(p => p.Key));
        Assert.Equal(22, G.AllEvents);
    }
}
