using System.Globalization;
using Hcc.Application.Messaging;
using Hcc.Contracts.Messaging;
using Hcc.Domain.Messaging;
using Hcc.Messaging.Tests.Harness;

namespace Hcc.Messaging.Tests.Sql;

/// <summary>Messaging use cases and the trigger consumer on a real database. Everything is simulated; nothing is sent.</summary>
[Trait("Category", "Sql")]
public class MessagingSqlTests : IAsyncLifetime
{
    private MsgWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MsgWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<MessageView> View(long id) => _w.Queries.GetMessageAsync(_w.PropertyId, Users.Reception, id, Ct);

    private async Task<(long ResId, long GuestId, string No)> InHouseAsync(string phone = "+91 98200 01041", string? email = "guest1@example.test", string room = "101")
        => await _w.BookingAsync(room, "2026-10-23", "2026-10-26", "Ananya Rao", phone, email, inHouse: true, ct: Ct);

    [Fact]
    public async Task Send_to_an_in_house_guest_is_simulated_masked_and_audited_without_the_text()
    {
        var (res, _, no) = await InHouseAsync();
        var r = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "s1"), new SendMessageRequest(res, "WhatsApp", "Hello {firstName}, room {room} at {hotel}."), Ct);
        Assert.Equal("••••••1041", r.Value.To);
        var v = await View(r.Value.MessageId);
        Assert.Equal("Hello Ananya, room 101 at HCC Demo Residency.", v.Body);
        Assert.Equal("Queued", v.Status);
        Assert.True(v.Simulated);
        Assert.Equal("MSG-" + r.Value.MessageId, v.Id);
        Assert.Equal(no, v.BookingNo);
        Assert.Equal("••••••1041", v.To);
        var audit = await _w.StringAsync("SELECT detail FROM plat.audit_event WHERE operation = 'SendMessage';", Ct);
        Assert.DoesNotContain("Hello", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("9820001041", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("98200 01041", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_is_idempotent_and_refused_for_the_wrong_guest_text_consent_and_role()
    {
        var (res, guest, _) = await InHouseAsync();
        var req = new SendMessageRequest(res, "SMS", "Your car is ready.");
        var first = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k1"), req, Ct);
        var again = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k1"), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.MessageId, again.Value.MessageId);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM msg.message;", Ct));
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k1"), req with { Body = "Different" }, Ct)));

        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Cashier, "k2"), req, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k3"), req with { Body = "Hi {nope}" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k4"), req with { Body = "" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k5"), req with { Body = new string('a', 601) }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k6"), req with { Channel = "Fax" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k7"), req with { Body = "ID 1234 5678 9012 is mine" }, Ct)));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k8"), req with { ReservationId = 9999 }, Ct)));

        var (conf, _, _) = await _w.BookingAsync("102", "2026-10-25", "2026-10-26", "Vikram Shah", "9820001042", null, ct: Ct);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k9"), req with { ReservationId = conf }, Ct)));

        await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "c1"), new SetMessagingConsentRequest(guest, Sms: false), Ct);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k10"), req, Ct)));
        await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "c2"), new SetMessagingConsentRequest(guest, Sms: true, OptOut: true), Ct);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "k11"), req with { Channel = "WhatsApp" }, Ct)));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM msg.message;", Ct));
    }

    [Fact]
    public async Task Delivery_status_follows_the_clock_and_the_predetermined_outcome()
    {
        var (res, _, _) = await InHouseAsync();
        var sent = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "d1"), new SendMessageRequest(res, "WhatsApp", "Timeline check"), Ct);
        var outcome = MessagingRules.Outcome(MessagingRules.WireId(sent.Value.MessageId), "WhatsApp", true);
        var expected = MessagingRules.Steps(outcome, false);
        var elapsed = 0;
        foreach (var step in expected)
        {
            _w.Clock.Advance(step.Offset - elapsed);
            elapsed = step.Offset;
            Assert.Equal(step.Status, (await View(sent.Value.MessageId)).Status);
        }

        var v = await View(sent.Value.MessageId);
        Assert.Equal(expected.Count, v.Timeline.Count);
    }

    [Fact]
    public async Task Simulate_delivery_advances_one_step_and_a_failed_message_is_retried_once()
    {
        var (res, _, _) = await InHouseAsync(phone: "12345"); // invalid number -> Failed at minute 2
        var m = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "r1"), new SendMessageRequest(res, "SMS", "Retry me"), Ct);
        Assert.Equal("Queued", (await View(m.Value.MessageId)).Status);
        await _w.Commands.SimulateAsync(_w.Ctx(Users.Reception, "r2"), new SimulateMessageDeliveryRequest(m.Value.MessageId), Ct);
        Assert.Equal("Sent", (await View(m.Value.MessageId)).Status);
        await _w.Commands.SimulateAsync(_w.Ctx(Users.Reception, "r3"), new SimulateMessageDeliveryRequest(m.Value.MessageId), Ct);
        Assert.Equal("Failed", (await View(m.Value.MessageId)).Status);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.SimulateAsync(_w.Ctx(Users.Reception, "r4"), new SimulateMessageDeliveryRequest(m.Value.MessageId), Ct)));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.SimulateAsync(_w.Ctx(Users.Reception, "r5"), new SimulateMessageDeliveryRequest(424242), Ct)));

        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.RetryAsync(_w.Ctx(Users.Cashier, "x"), new RetryMessageRequest(m.Value.MessageId), Ct)));
        var retry = await _w.Commands.RetryAsync(_w.Ctx(Users.Reception, "r6"), new RetryMessageRequest(m.Value.MessageId), Ct);
        var rv = await View(retry.Value.NewMessageId);
        Assert.Equal(m.Value.MessageId, rv.RetryOf);
        Assert.Equal(retry.Value.NewMessageId, (await View(m.Value.MessageId)).RetriedBy);
        _w.Clock.Advance(3);
        Assert.Equal("Delivered", (await View(retry.Value.NewMessageId)).Status);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.RetryAsync(_w.Ctx(Users.Reception, "r7"), new RetryMessageRequest(m.Value.MessageId), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.RetryAsync(_w.Ctx(Users.Reception, "r8"), new RetryMessageRequest(retry.Value.NewMessageId), Ct)));
    }

    [Fact]
    public async Task Opt_out_stops_messages_still_queued_but_not_those_already_sent()
    {
        var (res, guest, _) = await InHouseAsync();
        var a = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "o1"), new SendMessageRequest(res, "WhatsApp", "First message"), Ct);
        _w.Clock.Advance(1); // a is now Sent
        var b = await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "o2"), new SendMessageRequest(res, "SMS", "Second message"), Ct);
        var r = await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "o3"), new SetMessagingConsentRequest(guest, OptOut: true), Ct);
        Assert.Equal(1, r.Value.QueuedStopped);
        Assert.Equal("Not sent", (await View(b.Value.MessageId)).Status);
        Assert.Equal("Guest opted out before sending", (await View(b.Value.MessageId)).BlockedReason);
        Assert.Equal("Sent", (await View(a.Value.MessageId)).Status);
        Assert.True((await _w.Queries.ConsentAsync(_w.PropertyId, Users.Reception, guest, Ct)).OptOut);
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "o4"), new SetMessagingConsentRequest(777, OptOut: true), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SetConsentAsync(_w.Ctx(Users.Cashier, "o5"), new SetMessagingConsentRequest(guest, OptOut: false), Ct)));
    }

    private async Task PublishAsync(string type, long aggregate, string payloadJson) =>
        await _w.ExecAsync($"INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id) VALUES ('{type}', {aggregate}, N'{payloadJson}', {_w.PropertyId}, {Users.Reception});", Ct);

    [Fact]
    public async Task Outbox_events_queue_R1_R4_R5_once_and_never_the_disabled_R6()
    {
        var (res, _, no) = await _w.BookingAsync("104", "2026-10-24", "2026-10-26", "Riya Mehta", "9820007777", "guest3@example.test", inHouse: true, ct: Ct);
        var folio = Convert.ToInt64(await _w.ScalarAsync($"SELECT folio_id FROM bill.folio WHERE reservation_id = {res};", Ct), CultureInfo.InvariantCulture);
        await _w.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({folio}, 'room', '2026-10-24', N'room', 200000, 1000, 20000);", Ct);
        await _w.ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES ({folio}, 'payment', 'cash', 150000, '2026-10-24');", Ct);
        var receipt = Convert.ToInt64(await _w.ScalarAsync($"SELECT receipt_id FROM pay.receipt WHERE folio_id = {folio};", Ct), CultureInfo.InvariantCulture);

        await PublishAsync("ReservationCreated", res, $"{{\"reservationId\":{res},\"status\":\"Confirmed\"}}");
        await PublishAsync("ReservationConfirmed", res, $"{{\"reservationId\":{res}}}");
        await PublishAsync("ReservationCreated", res + 1000, "{\"status\":\"Held\"}");
        await PublishAsync("ReceiptRecorded", folio, $"{{\"folioId\":{folio},\"receiptId\":{receipt},\"kind\":\"payment\",\"amountPaise\":150000}}");
        await PublishAsync("ReceiptRecorded", folio, $"{{\"folioId\":{folio},\"receiptId\":{receipt + 50},\"kind\":\"deposit\",\"amountPaise\":1000}}");
        await PublishAsync("StayDeparted", 1, $"{{\"stayId\":1,\"reservationId\":{res},\"propertyId\":{_w.PropertyId},\"roomId\":1,\"businessDate\":\"2026-10-26\"}}");

        var applied = await _w.Projector.RunOnceAsync(ct: Ct);
        Assert.Equal(6, applied);
        Assert.Equal(0, await _w.Projector.RunOnceAsync(ct: Ct)); // exactly-once per consumer
        var rows = new List<(string Rule, string Channel, string Body, string To, string? Subject)>();
        await using (var conn = await _w.Platform.Connections.OpenAsync(Ct))
        await using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT rule_id, channel, body, recipient_masked, subject FROM msg.message ORDER BY rule_id;", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(Ct))
            while (await rd.ReadAsync(Ct)) rows.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4)));
        Assert.Equal(["R1", "R4", "R5"], rows.Select(r => r.Rule));
        Assert.Contains("Booking " + no + ", room 104, 24 Oct 2026 to 26 Oct 2026 (2 nights)", rows[0].Body, StringComparison.Ordinal);
        Assert.Equal("••••••7777", rows[0].To);
        Assert.Equal("HCC Demo Residency: payment of ₹1,500 received for booking " + no + ". Balance due ₹700. Thank you.", rows[1].Body);
        Assert.Equal("Dear Riya Mehta, thank you for staying with us. Your sample invoice for " + no + " is at https://demo.invalid/inv/" + no + " (simulated link).", rows[2].Body);
        Assert.Equal("Your invoice for booking " + no, rows[2].Subject);
        Assert.Equal("g•••@example.test", rows[2].To);

        // direct redelivery of the same event cannot double-queue either (unique trigger key)
        var evt = new Hcc.Application.Commands.OutboxEvent(0, "ReservationConfirmed", res, $"{{\"reservationId\":{res}}}", DateTime.UtcNow);
        await using (var conn = await _w.Platform.Connections.OpenAsync(Ct))
        await using (var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(Ct))
        {
            await _w.Projector.ApplyAsync(evt, conn, tx, Ct);
            await tx.CommitAsync(Ct);
        }

        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM msg.message;", Ct));
    }

    [Fact]
    public async Task A_trigger_the_guest_forbids_is_recorded_as_not_sent_with_the_reason()
    {
        var (res, guest, _) = await _w.BookingAsync("105", "2026-10-24", "2026-10-26", "Kabir Sen", "9820001046", null, ct: Ct);
        await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "n1"), new SetMessagingConsentRequest(guest, WhatsApp: false), Ct);
        await PublishAsync("ReservationConfirmed", res, $"{{\"reservationId\":{res}}}");
        await _w.Projector.RunOnceAsync(ct: Ct);
        var list = await _w.Queries.MessagesAsync(_w.PropertyId, Users.Reception, "Not sent", 1, 50, Ct);
        var m = Assert.Single(list.Items);
        Assert.Equal("No WhatsApp consent", m.BlockedReason);
        Assert.Equal("R1", m.RuleId);
        _w.Clock.Advance(60);
        Assert.Equal("Not sent", (await View(m.MessageId)).Status);
    }

    [Fact]
    public async Task Run_triggers_queues_pre_arrival_and_check_in_welcome_once()
    {
        await _w.BookingAsync("101", "2026-10-24", "2026-10-26", "Today Arrival", "9820000001", null, ct: Ct);        // R2 (arrival today)
        await _w.BookingAsync("102", "2026-10-25", "2026-10-27", "Tomorrow Arrival", "9820000002", null, ct: Ct);     // R2 (arrival tomorrow)
        await _w.BookingAsync("104", "2026-10-26", "2026-10-27", "Later Arrival", "9820000003", null, ct: Ct);         // none
        await _w.BookingAsync("105", "2026-10-23", "2026-10-26", "In House Guest", "9820000004", null, inHouse: true, ct: Ct); // R3
        var before = await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct);
        Assert.Equal(3, before.PendingTriggers);

        var r = await _w.Commands.RunTriggersAsync(_w.Ctx(Users.Reception, "t1"), new RunMessagingTriggersRequest(), Ct);
        Assert.Equal((3, 0), (r.Value.Queued, r.Value.NotSent));
        Assert.Equal(0, (await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct)).PendingTriggers);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.RunTriggersAsync(_w.Ctx(Users.Reception, "t2"), new RunMessagingTriggersRequest(), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.RunTriggersAsync(_w.Ctx(Users.Cashier, "t3"), new RunMessagingTriggersRequest(), Ct)));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM msg.message WHERE rule_id = 'R2';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM msg.message WHERE rule_id = 'R3';", Ct));

        // disabling a rule removes its trigger; a new in-house stay is then not pending for R3
        var rules = await _w.Queries.RulesAsync(_w.PropertyId, Users.Manager, Ct);
        var r3 = rules.Single(x => x.RuleId == "R3");
        await _w.Commands.UpdateRuleAsync(_w.Ctx(Users.Manager, "t4"), new UpdateMessageRuleRequest("R3", r3.Version, Enabled: false), Ct);
        await _w.BookingAsync("101", "2026-10-20", "2026-10-26", "Second In House", "9820000005", null, inHouse: true, ct: Ct);
        Assert.Equal(0, (await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct)).PendingTriggers);
    }

    [Fact]
    public async Task Templates_and_rules_are_edited_by_owner_and_manager_only_with_whitelisted_placeholders_and_versions()
    {
        var t = (await _w.Queries.TemplatesAsync(_w.PropertyId, Users.Reception, Ct));
        Assert.Equal(6, t.Templates.Count);
        Assert.Equal(MessagingRules.Placeholders, t.Placeholders);
        var pre = t.Templates.Single(x => x.Code == "pre-arrival");
        var req = new UpdateMessageTemplateRequest("pre-arrival", "Hello {firstName}, see you on {arrival}.", pre.Version);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Reception, "e1"), req, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Manager, "e2"), req with { Body = "Hello {firstName} {passport}" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Manager, "e3"), req with { Body = "short" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Manager, "e4"), req with { Subject = "x" }, Ct)));
        var ok = await _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Manager, "e5"), req with { Channel = "SMS" }, Ct);
        Assert.Equal("SMS", ok.Value.Template.Channel);
        Assert.NotEqual(pre.Version, ok.Value.Template.Version);
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Owner, "e6"), req, Ct)));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Owner, "e7"), req with { Code = "nope", ExpectedVersion = ok.Value.Template.Version }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.UpdateTemplateAsync(_w.Ctx(Users.Owner, "e8"), req with { ExpectedVersion = "not-base64!" }, Ct)));

        var rules = await _w.Queries.RulesAsync(_w.PropertyId, Users.Owner, Ct);
        Assert.Equal(["R1", "R2", "R3", "R4", "R5", "R6"], rules.Select(x => x.RuleId));
        var r6 = rules.Single(x => x.RuleId == "R6");
        Assert.False(r6.Enabled);
        var on = await _w.Commands.UpdateRuleAsync(_w.Ctx(Users.Owner, "e9"), new UpdateMessageRuleRequest("R6", r6.Version, Enabled: true), Ct);
        Assert.True(on.Value.Rule.Enabled);
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.UpdateRuleAsync(_w.Ctx(Users.Owner, "e10"), new UpdateMessageRuleRequest("R6", r6.Version, Enabled: false), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.UpdateRuleAsync(_w.Ctx(Users.Reception, "e11"), new UpdateMessageRuleRequest("R6", on.Value.Rule.Version, Enabled: false), Ct)));
    }

    [Fact]
    public async Task No_id_number_reaches_a_message_a_recipient_or_the_audit_trail()
    {
        var (res, guest, no) = await InHouseAsync();
        await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "i1"), new SendMessageRequest(res, "Email", "Hello {guest}", Subject: "About {booking}"), Ct);
        await _w.Commands.RunTriggersAsync(_w.Ctx(Users.Reception, "i2"), new RunMessagingTriggersRequest(), Ct);
        await PublishAsync("ReservationConfirmed", res, $"{{\"reservationId\":{res}}}");
        await _w.Projector.RunOnceAsync(ct: Ct);
        await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "i3"), new SetMessagingConsentRequest(guest, Email: false), Ct);
        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM msg.message;", Ct) >= 3);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM msg.message WHERE body LIKE '%Z987%' OR ISNULL(subject, '') LIKE '%Z987%' OR recipient_masked LIKE '%Z987%' OR body LIKE '%Aadhaar%';", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%Z987%' OR detail LIKE '%Aadhaar%' OR detail LIKE '%Hello%';", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'msg' AND (COLUMN_NAME LIKE '%id\\_number%' ESCAPE '\\' OR COLUMN_NAME LIKE '%id\\_last4%' ESCAPE '\\' OR COLUMN_NAME LIKE '%aadhaar%');", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE payload LIKE '%Z987%';", Ct));
        _ = no;
    }

    [Fact]
    public async Task List_filters_by_status_pages_and_the_summary_counts_each_status()
    {
        var (res, _, _) = await InHouseAsync();
        for (var i = 0; i < 3; i++) await _w.Commands.SendAsync(_w.Ctx(Users.Reception, "l" + i.ToString(CultureInfo.InvariantCulture)), new SendMessageRequest(res, "SMS", "Message " + i.ToString(CultureInfo.InvariantCulture)), Ct);
        var s = await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct);
        Assert.Equal(3, s.Queued);
        Assert.True(s.Simulated);
        var all = await _w.Queries.MessagesAsync(_w.PropertyId, Users.Reception, null, 1, 2, Ct);
        Assert.Equal((3, 2), (all.Total, all.Items.Count));
        Assert.True(all.Items[0].MessageId > all.Items[1].MessageId);
        _w.Clock.Advance(20);
        var done = await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct);
        Assert.Equal(0, done.Queued);
        Assert.Equal(3, done.Delivered + done.Failed);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.SummaryAsync(_w.PropertyId, Users.Cashier, Ct)));
    }
}
