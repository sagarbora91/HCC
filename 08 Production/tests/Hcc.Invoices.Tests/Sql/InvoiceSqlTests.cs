using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Invoices;
using Hcc.Infrastructure.SqlServer.Accounting;
using Microsoft.Data.SqlClient;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>Tax invoices on SQL with the real composition: golden AC-006 figures, numbering, immutability, permissions, day-closed refusal.</summary>
[Trait("Category", "Sql")]
public class InvoiceSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Golden_AC006_folio_becomes_tax_invoice_INV_2627_0000001_for_4265_rupees()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: true, ct: Ct);
        var key = Hotel.Key();
        var issued = await w.IssueAsync(stay.FolioId, key: key, ct: Ct);
        Assert.Equal(("INV/2627/0000001", "INV", new DateOnly(2026, 10, 24), 426500L, true), (issued.InvoiceNo, issued.DocType, issued.InvoiceDate, issued.TotalPaise, issued.Created));

        // Same key + payload → stored result (replay); another key → the one invoice of this folio, never a second number.
        var replay = await w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue, key), new IssueInvoiceRequest("folio", stay.FolioId), Ct);
        Assert.True(replay.Replayed);
        var again = await w.IssueAsync(stay.FolioId, who: h.Cashier, ct: Ct);
        Assert.Equal((issued.InvoiceId, false), (again.InvoiceId, again.Created));
        Assert.Equal(1, await h.LongAsync("SELECT last_seq FROM ivc.series WHERE doc_type = 'INV'", Ct));

        var v = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Accountant, issued.InvoiceId, Ct);
        Assert.Equal((400000L, 13250L, 13250L, 0L, 426500L), (v.TaxablePaise, v.CgstPaise, v.SgstPaise, v.IgstPaise, v.TotalPaise));
        Assert.Equal("Rupees Four Thousand Two Hundred Sixty Five Only", v.AmountInWords);
        Assert.Equal(("B2C", "27", false, InvoiceRulesTestsGstin), (v.SupplyType, v.PlaceOfSupply, v.InterState, v.Supplier.Gstin));
        Assert.Equal([(200000L, 500, 10000L), (200000L, 500, 10000L), (50000L, 1800, 9000L), (-50000L, 500, -2500L)], v.Lines.Select(l => (l.TaxablePaise, l.TaxBps, l.TaxPaise)));
        Assert.Equal(["room", "room", "laundry", "room"], v.Lines.Select(l => l.Category));
        Assert.StartsWith("Room night · 2026-10-22 · Room 105", v.Lines[0].Description, StringComparison.Ordinal);
        Assert.Equal((400000L, 426500L), (v.CreditableTaxablePaise, v.CreditableValuePaise));
        Assert.StartsWith("Seed guest", v.Recipient.Name, StringComparison.Ordinal);

        // Lines equal the folio's stored snapshot (charge tax and tax.charge_gst split), never recomputed.
        Assert.Equal(await h.LongAsync($"SELECT SUM(tax_paise) FROM bill.charge WHERE folio_id = {stay.FolioId}", Ct), v.CgstPaise + v.SgstPaise);
        Assert.Equal(await h.LongAsync($"SELECT SUM(g.cgst_paise) FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.folio_id = {stay.FolioId}", Ct), v.CgstPaise);

        var register = await w.Queries.RegisterAsync(h.PropertyId, h.Manager, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);
        Assert.Equal((1, 426500L, 0L, 426500L), (register.Rows.Count, register.InvoicedTotalPaise, register.CreditedTotalPaise, register.NetTotalPaise));
        var gstr1 = await w.Queries.Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);
        Assert.Equal([("B2CS", 500, 350000L, 8750L), ("B2CS", 1800, 50000L, 4500L)], gstr1.Rates.Select(r => (r.Section, r.TaxBps, r.TaxablePaise, r.CgstPaise)));
        Assert.Equal(("INV/2627/0000001", "INV/2627/0000001", 1), (gstr1.Documents.Single().First, gstr1.Documents.Single().Last, gstr1.Documents.Single().Count));

        var text = await w.Queries.RenderInvoiceAsync(h.PropertyId, h.Cashier, issued.InvoiceId, "text", Ct);
        Assert.Contains("INV/2627/0000001", text.Content, StringComparison.Ordinal);
        Assert.Contains("₹4,265.00", text.Content, StringComparison.Ordinal);
        Assert.Contains("SAC 999712", text.Content, StringComparison.Ordinal);
        var html = await w.Queries.RenderInvoiceAsync(h.PropertyId, h.Owner, issued.InvoiceId, null, Ct);
        Assert.StartsWith("text/html", html.ContentType, StringComparison.Ordinal);
        Assert.Contains("GSTIN " + InvoiceRulesTestsGstin, html.Content, StringComparison.Ordinal);

        // Audit and outbox in the same transaction.
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'INVOICE_ISSUED'", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'InvoiceIssued' AND aggregate_id = {issued.InvoiceId}", Ct));
    }

    private const string InvoiceRulesTestsGstin = Domain.InvoiceRulesTests.HotelGstin;

    /// <summary>W4 D1: a GSTIN with a wrong check character is refused naming the setting; the Master configuration GSTIN (brand.gstin) wins
    /// over the setup copy on the property row, so correcting it there unblocks invoicing for a stay that already checked out.</summary>
    [Fact]
    public async Task A_bad_hotel_GSTIN_names_the_setting_and_the_corrected_Master_configuration_GSTIN_is_used()
    {
        await using var w = await InvoiceWorld.CreateAsync(withGstin: false, ct: Ct);
        var h = w.H;
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '27AAAAA0000A1Z5' WHERE property_id = {h.PropertyId};", Ct); // wrong check character
        var departed = await w.DepartedStayAsync("102", ac006: false, ct: Ct);
        var e = await Expect.Fails(HccErrorCode.VALIDATION, () => w.IssueAsync(departed.FolioId, ct: Ct));
        Assert.Contains("check character is wrong", e.Message, StringComparison.Ordinal);
        Assert.Contains("(brand.gstin)", e.Message, StringComparison.Ordinal);
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM ivc.series", Ct));

        var cfg = h.Get<Hcc.Application.Configuration.ConfigurationService>();
        var bad = await Expect.Fails(HccErrorCode.VALIDATION, () => cfg.SetSettingAsync(h.Ctx(h.Owner, "SetSetting"),
            new Hcc.Contracts.Configuration.SetSettingRequest("brand.gstin", System.Text.Json.JsonSerializer.SerializeToElement("27AAAAA0000A1Z5"))));
        Assert.Contains("check character is wrong", bad.Message, StringComparison.Ordinal);
        await cfg.SetSettingAsync(h.Ctx(h.Owner, "SetSetting"), new Hcc.Contracts.Configuration.SetSettingRequest("brand.gstin", System.Text.Json.JsonSerializer.SerializeToElement(InvoiceRulesTestsGstin)), Ct);

        var issued = await w.IssueAsync(departed.FolioId, ct: Ct);
        Assert.Equal("INV/2627/0000001", issued.InvoiceNo);
        Assert.Equal(InvoiceRulesTestsGstin, (await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, issued.InvoiceId, Ct)).Supplier.Gstin);
    }

    [Fact]
    public async Task Issue_refuses_open_or_unsettled_folios_missing_GSTIN_and_bad_B2B_and_numbers_the_next_folio()
    {
        await using var w = await InvoiceWorld.CreateAsync(withGstin: false, ct: Ct);
        var h = w.H;
        var inHouse = await w.DepartedStayAsync("101", ac006: false, checkout: false, ct: Ct);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.IssueAsync(inHouse.FolioId, ct: Ct));
        var departed = await w.DepartedStayAsync("102", ac006: false, ct: Ct);
        var e = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.IssueAsync(departed.FolioId, ct: Ct));
        Assert.Contains("GSTIN", e.Message, StringComparison.Ordinal);
        Assert.Contains("Master configuration › Property and branding › GSTIN on documents (brand.gstin)", e.Message, StringComparison.Ordinal); // W4 D1: names the setting
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM ivc.series", Ct));

        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{InvoiceRulesTestsGstin}' WHERE property_id = {h.PropertyId};", Ct);
        await Expect.Fails(HccErrorCode.VALIDATION, () => w.IssueAsync(departed.FolioId, supply: "B2B", name: "Demo Industries Pvt Ltd (fictional)", gstin: "27AAAAA0000A1Z5", ct: Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => w.IssueAsync(departed.FolioId, supply: "B2B", gstin: "29AAGCB7383J1Z4", ct: Ct));
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => w.IssueAsync(999999, ct: Ct));
        var b2b = await w.IssueAsync(departed.FolioId, supply: "B2B", name: "Demo Industries Pvt Ltd (fictional)", gstin: "29AAGCB7383J1Z4", ct: Ct);
        Assert.Equal("INV/2627/0000001", b2b.InvoiceNo);
        var view = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, b2b.InvoiceId, Ct);
        Assert.Equal(("B2B", "29AAGCB7383J1Z4", "29", "27", false), (view.SupplyType, view.Recipient.Gstin, view.Recipient.StateCode, view.PlaceOfSupply, view.InterState));

        // Inter-state place of supply (CA question G9): whole tax as IGST, same total.
        var other = await w.DepartedStayAsync("103", ac006: false, ct: Ct);
        var igst = await w.IssueAsync(other.FolioId, supply: "B2B", name: "Karnataka Co (fictional)", gstin: "29AAGCB7383J1Z4", pos: "29", ct: Ct);
        Assert.Equal("INV/2627/0000002", igst.InvoiceNo);
        var iv = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, igst.InvoiceId, Ct);
        Assert.Equal((true, 0L, 0L, 20000L, 420000L), (iv.InterState, iv.CgstPaise, iv.SgstPaise, iv.IgstPaise, iv.TotalPaise));
    }

    [Fact]
    public async Task Numbering_is_gap_free_under_concurrency_and_a_rolled_back_issue_releases_its_number()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        // A command that takes a number and then fails: the number is not consumed (same transaction).
        var store = h.Get<IInvoiceStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get<ICommandRunner>().RunAsync(h.Ctx(h.Owner, "TestTakeNumber"), new { x = 1 }, async (tx, c) =>
        {
            await store.NextNumberAsync(tx, h.PropertyId, "2627", InvoiceDocType.TaxInvoice, "INV", DateTime.UtcNow, c);
            throw new InvalidOperationException("crash after numbering");
#pragma warning disable CS0162 // Unreachable code detected
            return new { ok = true };
#pragma warning restore CS0162
        }, Ct));

        var rooms = new[] { "101", "102", "103", "104", "105", "106", "107", "108" };
        var folios = new List<long>();
        foreach (var r in rooms) folios.Add((await w.DepartedStayAsync(r, ac006: r == "105", ct: Ct)).FolioId);
        var results = await Task.WhenAll(folios.Select(f => Task.Run(() => w.IssueAsync(f, ct: Ct), Ct)));
        var numbers = results.Select(r => r.InvoiceNo).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(Enumerable.Range(1, rooms.Length).Select(i => InvoiceNumbering.Format("INV", "2627", i)), numbers);
        Assert.All(numbers, n => Assert.Equal(16, n.Length));
        Assert.Equal(rooms.Length, await h.LongAsync("SELECT last_seq FROM ivc.series WHERE fy_code = '2627' AND doc_type = 'INV'", Ct));

        // Two racers on one folio: one number, both see the same invoice.
        var extra = await w.DepartedStayAsync("109", ac006: false, ct: Ct);
        var pair = await Task.WhenAll(Task.Run(() => w.IssueAsync(extra.FolioId, ct: Ct), Ct), Task.Run(() => w.IssueAsync(extra.FolioId, who: h.Cashier, ct: Ct), Ct));
        Assert.Equal(pair[0].InvoiceId, pair[1].InvoiceId);
        Assert.Equal(1, pair.Count(p => p.Created));
        Assert.Equal("INV/2627/0000009", pair[0].InvoiceNo);
    }

    [Fact]
    public async Task Issued_documents_and_series_are_immutable_in_the_database()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: true, ct: Ct);
        var inv = await w.IssueAsync(stay.FolioId, ct: Ct);
        await w.RequestCreditAsync(h.Owner, inv.InvoiceId, "amount", 10000, ct: Ct);
        async Task Refused(string sql, int number)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => h.ScalarAsync(sql, Ct));
            Assert.Equal(number, ex.Number);
        }

        await Refused($"UPDATE ivc.invoice SET total_paise = total_paise WHERE invoice_id = {inv.InvoiceId}", 52400);
        await Refused($"DELETE ivc.invoice WHERE invoice_id = {inv.InvoiceId}", 52400);
        await Refused($"UPDATE ivc.invoice_line SET description = N'edited' WHERE invoice_id = {inv.InvoiceId}", 52401);
        await Refused("UPDATE ivc.credit_note SET reason = N'edited'", 52402);
        await Refused("DELETE ivc.credit_note_line", 52403);
        await Refused("UPDATE ivc.series SET last_seq = last_seq + 2 WHERE doc_type = 'INV'", 52405);
        await Refused("DELETE ivc.series", 52404);
        Assert.Equal(426500, await h.LongAsync($"SELECT total_paise FROM ivc.invoice WHERE invoice_id = {inv.InvoiceId}", Ct));
    }

    [Fact]
    public async Task Permissions_follow_the_matrix_v2_invoice_cells()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var housekeeper = Convert.ToInt32(await h.ScalarAsync($"""
            INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES ({h.PropertyId}, 'hkp.sunil', N'Sunil (Housekeeper)', SYSUTCDATETIME());
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, 'HKP', DATEADD(day, -30, SYSUTCDATETIME()), SYSUTCDATETIME());
            SELECT @u;
            """, Ct), System.Globalization.CultureInfo.InvariantCulture);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.IssueAsync(stay.FolioId, who: h.Accountant, ct: Ct));  // ACC: view only
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.IssueAsync(stay.FolioId, who: housekeeper, ct: Ct));
        var inv = await w.IssueAsync(stay.FolioId, who: h.Cashier, ct: Ct);
        await w.Queries.GetInvoiceAsync(h.PropertyId, h.Accountant, inv.InvoiceId, Ct);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.Queries.GetInvoiceAsync(h.PropertyId, housekeeper, inv.InvoiceId, Ct));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.Queries.RenderInvoiceAsync(h.PropertyId, h.Accountant, inv.InvoiceId, "text", Ct)); // print = INV-002
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.RequestCreditAsync(housekeeper, inv.InvoiceId, "full", ct: Ct));
        var acc = await w.RequestCreditAsync(h.Accountant, inv.InvoiceId, "amount", 1000, ct: Ct); // ACC: request (R)
        Assert.Equal("PendingApproval", acc.Outcome);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.DecideAsync(h.Manager, acc, true, Ct));    // DEC-063 G3: Owner only
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.DecideAsync(h.Accountant, acc, true, Ct));
        var rejected = await w.DecideAsync(h.Owner, acc, false, Ct);
        Assert.Equal("Rejected", rejected.Outcome);
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM ivc.credit_note", Ct));
        var list = await w.Queries.RequestsAsync(h.PropertyId, h.Owner, "Rejected", Ct);
        Assert.Equal(("Not justified", h.Owner), (list.Single().DecisionReason, list.Single().DecidedBy));
    }

    [Fact]
    public async Task Closed_business_day_refuses_issue_and_credit_notes()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var other = await w.DepartedStayAsync("106", ac006: false, ct: Ct);
        var inv = await w.IssueAsync(other.FolioId, ct: Ct);
        var run = await h.LongAsync($"""
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            OUTPUT INSERTED.run_id VALUES ({h.PropertyId}, '2026-10-24', 'r', 'r', 'Closed', 'close', 1, {h.Owner}, '2026-10-24T20:00:00', {h.Owner}, '2026-10-24T20:00:00');
            """, Ct);
        await h.ScalarAsync($"INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES ({h.PropertyId}, '2026-10-24', {run}, {h.Owner}, '2026-10-24T20:00:00');", Ct);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => w.IssueAsync(stay.FolioId, ct: Ct));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => w.RequestCreditAsync(h.Owner, inv.InvoiceId, "full", ct: Ct));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM ivc.invoice", Ct));
    }
}
