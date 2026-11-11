using Hcc.Application.Hall;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Hall.Tests.Quote;

/// <summary>
/// F6 H2 on a disposable HccTest_* hotel (migrations 4330–4334, 4349): numbering QT/2627/0001 and the FY reset, revise keeps the number,
/// discount limits with an Owner approval (not self), the placeholder guard and the Owner override, a sent snapshot unchanged after a template
/// save, Reception / Manager refused on templates, expiry, lost / cancel, and accept (lane H1) turning the sent quotation into an event with the plan.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HallQuoteSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> SetupAsync(Hotel h, string date = "2026-12-05")
    {
        return await h.LongAsync($"""
            INSERT hall.venue(property_id, code, name, kind, cap_round, cap_theatre) VALUES ({h.PropertyId}, 'V-SAGAR', N'Sagar Hall', 'HALL', 150, 200);
            DECLARE @v INT = SCOPE_IDENTITY();
            INSERT hall.venue_slot(venue_id, slot, time_from, time_to, rate_paise) VALUES (@v, 'EVENING', '18:00', '23:00', 1500000);
            IF NOT EXISTS (SELECT 1 FROM hall.menu_package WHERE property_id = {h.PropertyId})
              INSERT hall.menu_package(property_id, code, name, per_plate_paise) VALUES ({h.PropertyId}, 'MP-SILVER-VEG', N'Silver Veg', 65000);
            INSERT hall.enquiry(property_id, name, phone, event_type, dates, slot, pax, venue_id, source, follow_up, status, created_by, created_utc)
              VALUES ({h.PropertyId}, N'Kulkarni family (fictional)', '9000000123', 'RECEPTION', '{date}', 'EVENING', 150, @v, 'PHONE', '2026-10-24', 'OPEN', {h.Reception}, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
            """, Ct);
    }

    private static async Task<long> EnquiryAsync(Hotel h, string date = "2026-12-06") => await h.LongAsync($"""
        INSERT hall.enquiry(property_id, name, phone, event_type, dates, slot, pax, venue_id, source, follow_up, status, created_by, created_utc)
          SELECT {h.PropertyId}, N'Deshmukh (fictional)', '9000000456', 'RECEPTION', '{date}', 'EVENING', 150, venue_id, 'PHONE', '2026-10-24', 'OPEN', {h.Reception}, SYSUTCDATETIME()
          FROM hall.venue WHERE property_id = {h.PropertyId} AND code = 'V-SAGAR';
        SELECT SCOPE_IDENTITY();
        """, Ct);

    private static QuoteLineIn[] Golden(int pct) =>
    [
        new("HALL", "Sagar Hall · evening", 1, 1_500_000 - 15_000 * pct, "V-SAGAR", "slot"),
        new("MENU", "Silver Veg per plate", 150, 65_000 - 650 * pct, "MP-SILVER-VEG", "plate"),
        new("EXTRA", "Stage decor", 1, 1_000_000 - 10_000 * pct, null, "lot", 1_000_000),
    ];

    private static Task RealTncAsync(Hotel h, HallQuoteCommands c, string type = "GENERAL") =>
        c.SaveTemplateAsync(h.Ctx(h.Owner, HallQuoteCommands.OpTemplate), new SaveTncTemplateRequest(type, [new("Advance and payment", "25 % on booking."), new("Cancellation", "As per slabs.")]), Ct);

    private static async Task<HccErrorCode> Code(Func<Task> f) => (await Assert.ThrowsAsync<HccException>(f)).Code;

    [Fact]
    public async Task Numbering_fy_reset_revise_keeps_number_and_cancel_keeps_it()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var e = await SetupAsync(h);
        var c = h.Get<HallQuoteCommands>();
        var q1 = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e, MenuCode: "MP-SILVER-VEG"), Ct)).Value;
        Assert.Equal("QT/2627/0001", q1.Number);
        Assert.Equal(1_500_000L + 150 * 65_000, await h.LongAsync($"SELECT SUM(qty * list_rate_paise) FROM hall.quotation_line l JOIN hall.quotation_version v ON v.version_id = l.version_id WHERE v.quotation_id = {q1.QuotationId}", Ct));
        await c.CancelAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCancel), new QuotationActionRequest(q1.QuotationId), Ct);
        var q2 = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e, MenuCode: "MP-SILVER-VEG"), Ct)).Value;
        Assert.Equal("QT/2627/0002", q2.Number);

        await RealTncAsync(h, c);
        await c.SendAsync(h.Ctx(h.Reception, HallQuoteCommands.OpSend), new QuotationActionRequest(q2.QuotationId), Ct);
        var r = (await c.ReviseAsync(h.Ctx(h.Reception, HallQuoteCommands.OpRevise), new QuotationActionRequest(q2.QuotationId), Ct)).Value;
        Assert.Equal(("QT/2627/0002", 2, "DRAFT"), (r.Number, r.Version, r.Status));
        Assert.Equal(["SUPERSEDED", "DRAFT"], await h.RowsAsync($"SELECT status FROM hall.quotation_version WHERE quotation_id = {q2.QuotationId} ORDER BY v", x => x.GetString(0), Ct));
        Assert.Equal(HccErrorCode.HCC_QUO_NOT_LATEST, await Code(() => c.SendAsync(h.Ctx(h.Reception, HallQuoteCommands.OpSend), new QuotationActionRequest(q2.QuotationId, 1), Ct)));

        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2027-04-01' WHERE property_id = {h.PropertyId};", Ct);
        var e2 = await EnquiryAsync(h, "2027-05-01");
        var q3 = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e2, MenuCode: "MP-SILVER-VEG"), Ct)).Value;
        Assert.Equal("QT/2728/0001", q3.Number);
    }

    [Fact]
    public async Task Discount_limits_approval_not_self_placeholder_guard_and_override()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var e = await SetupAsync(h);
        var c = h.Get<HallQuoteCommands>();
        var a = (await c.CreateAsync(h.Ctx(h.Manager, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e, Lines: Golden(10)), Ct)).Value;
        Assert.False(a.NeedsApproval);
        Assert.Equal(HccErrorCode.HCC_TNC_PLACEHOLDER, await Code(() => c.SendAsync(h.Ctx(h.Manager, HallQuoteCommands.OpSend), new QuotationActionRequest(a.QuotationId), Ct)));
        Assert.Equal(HccErrorCode.HCC_TNC_OWNER_ONLY, await Code(() => c.OverrideAsync(h.Ctx(h.Manager, HallQuoteCommands.OpOverride), new QuotationActionRequest(a.QuotationId, Reason: "customer in a hurry"), Ct)));
        await c.OverrideAsync(h.Ctx(h.Owner, HallQuoteCommands.OpOverride), new QuotationActionRequest(a.QuotationId, Reason: "Paper T&C handed over"), Ct);
        Assert.Equal(1L, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE property_id = {h.PropertyId} AND action = 'HALL_TNC_OVERRIDE'", Ct));
        Assert.Equal("SENT", (await c.SendAsync(h.Ctx(h.Manager, HallQuoteCommands.OpSend), new QuotationActionRequest(a.QuotationId), Ct)).Value.Status);

        await RealTncAsync(h, c);
        var e2 = await EnquiryAsync(h);
        var b = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e2, Lines: Golden(1)), Ct)).Value;
        Assert.True(b.NeedsApproval);
        Assert.Equal(HccErrorCode.HCC_DISCOUNT_LIMIT, await Code(() => c.SendAsync(h.Ctx(h.Reception, HallQuoteCommands.OpSend), new QuotationActionRequest(b.QuotationId), Ct)));
        var m = (await c.CreateAsync(h.Ctx(h.Manager, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e2, Lines: Golden(11)), Ct)).Value;
        Assert.Equal(HccErrorCode.HCC_DISCOUNT_LIMIT, await Code(() => c.SendAsync(h.Ctx(h.Manager, HallQuoteCommands.OpSend), new QuotationActionRequest(m.QuotationId), Ct)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => c.ApproveDiscountAsync(h.Ctx(h.Manager, HallQuoteCommands.OpApprove), new QuotationActionRequest(m.QuotationId), Ct)));
        await c.ApproveDiscountAsync(h.Ctx(h.Owner, HallQuoteCommands.OpApprove), new QuotationActionRequest(m.QuotationId), Ct);
        Assert.Equal("SENT", (await c.SendAsync(h.Ctx(h.Manager, HallQuoteCommands.OpSend), new QuotationActionRequest(m.QuotationId), Ct)).Value.Status);
        var o = (await c.CreateAsync(h.Ctx(h.Owner, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e2, Lines: Golden(30)), Ct)).Value;
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => c.ApproveDiscountAsync(h.Ctx(h.Owner, HallQuoteCommands.OpApprove), new QuotationActionRequest(o.QuotationId), Ct)));
        Assert.Equal("SENT", (await c.SendAsync(h.Ctx(h.Owner, HallQuoteCommands.OpSend), new QuotationActionRequest(o.QuotationId), Ct)).Value.Status);
    }

    [Fact]
    public async Task Snapshot_unchanged_after_template_save_templates_owner_only_expiry_lost_and_accept()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var e = await SetupAsync(h);
        var c = h.Get<HallQuoteCommands>();
        foreach (var who in new[] { h.Reception, h.Manager })
            Assert.Equal(HccErrorCode.HCC_TNC_OWNER_ONLY, await Code(() => c.SaveTemplateAsync(h.Ctx(who, HallQuoteCommands.OpTemplate), new SaveTncTemplateRequest("GENERAL", [new("H1", "abc")]), Ct)));
        await RealTncAsync(h, c, "RECEPTION");
        var q = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e, Lines: Golden(0)), Ct)).Value;
        var sent = (await c.SendAsync(h.Ctx(h.Reception, HallQuoteCommands.OpSend), new QuotationActionRequest(q.QuotationId), Ct)).Value;
        Assert.Equal("T-RECEPTION", sent.TemplateCode);
        var snap = $"SELECT s.body FROM hall.quotation_tnc_snapshot s JOIN hall.quotation_version v ON v.version_id = s.version_id WHERE v.quotation_id = {q.QuotationId} AND v.v = 1 ORDER BY s.n";
        var before = await h.RowsAsync(snap, x => x.GetString(0), Ct);
        var saved = (await c.SaveTemplateAsync(h.Ctx(h.Owner, HallQuoteCommands.OpTemplate), new SaveTncTemplateRequest("RECEPTION", [new("Advance", "Changed wording after sending.")]), Ct)).Value;
        Assert.Equal(2, saved.Version);
        Assert.Equal(before, await h.RowsAsync(snap, x => x.GetString(0), Ct));

        // FX-SMALL: the read model names the T&C version a sent quotation is locked to and serves the saved templates, so the app prints the Owner's terms (not the seeded placeholders).
        var qs = h.Get<HallQueries>();
        var ver = Assert.Single((await qs.QuotationsAsync(h.PropertyId, h.Owner, null, q.QuotationId, Ct)).Single().Versions);
        Assert.Equal(("T-RECEPTION", 1), (ver.TncTemplateCode, ver.TncV));
        var tpl = (await qs.TncTemplatesAsync(h.PropertyId, h.Owner, Ct)).Single(t => t.Code == "T-RECEPTION");
        Assert.Equal([1, 2], tpl.Versions.Select(x => x.V).ToArray());
        Assert.Equal("Changed wording after sending.", tpl.Versions[1].Clauses.Single().Text);
        Assert.NotEqual(tpl.Versions[1].Clauses.Single().Text, tpl.Versions[0].Clauses[0].Text);   // the sent quotation still resolves to v1, not the newer wording

        // accept (lane H1) → event + 25 / 50 / balance plan of the accepted total
        var acc = (await h.Get<HallBillingCommands>().AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q.QuotationId), Ct)).Value;
        Assert.Equal("CONFIRMED", acc.Status);
        Assert.Equal(acc.TotalPaise, await h.LongAsync($"SELECT SUM(amount_paise) FROM hall.event_advance WHERE event_id = {acc.EventId}", Ct));
        Assert.Equal(3L, await h.LongAsync($"SELECT COUNT(*) FROM hall.event_advance WHERE event_id = {acc.EventId}", Ct));

        // expiry + lost
        var e2 = await EnquiryAsync(h);
        var q2 = (await c.CreateAsync(h.Ctx(h.Reception, HallQuoteCommands.OpCreate), new CreateQuotationRequest(e2, Lines: Golden(0)), Ct)).Value;
        await c.SendAsync(h.Ctx(h.Reception, HallQuoteCommands.OpSend), new QuotationActionRequest(q2.QuotationId), Ct);
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => c.ExpireAsync(h.Ctx(h.Reception, HallQuoteCommands.OpExpire), new ExpireQuotationsRequest(), Ct)));
        await h.ScalarAsync($"UPDATE inv.property SET business_date = DATEADD(DAY, 16, business_date) WHERE property_id = {h.PropertyId};", Ct);
        Assert.Equal(1, (await c.ExpireAsync(h.Ctx(h.Manager, HallQuoteCommands.OpExpire), new ExpireQuotationsRequest(), Ct)).Value.Count);
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => c.LoseAsync(h.Ctx(h.Reception, HallQuoteCommands.OpLost), new QuotationActionRequest(q2.QuotationId, ReasonId: "LR-OTHER"), Ct)));
        await c.LoseAsync(h.Ctx(h.Reception, HallQuoteCommands.OpLost), new QuotationActionRequest(q2.QuotationId, ReasonId: "LR-PRICE"), Ct);
        Assert.Equal("LOST", await h.ScalarAsync($"SELECT status FROM hall.enquiry WHERE enquiry_id = {e2}", Ct));
    }
}
