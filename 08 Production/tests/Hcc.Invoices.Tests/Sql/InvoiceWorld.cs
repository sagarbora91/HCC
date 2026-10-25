using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;
using Hcc.Invoices.Tests.Domain;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>
/// The P6 fictional hotel (real composition, real access policy, business-day guarded runner) with the hotel GSTIN recorded, and helpers that
/// take a stay through the real Billing/Stays commands to a settled checkout. Room nights are posted on their own business dates by moving
/// <c>inv.property.business_date</c> (test-only stand-in for the night close).
/// </summary>
public sealed class InvoiceWorld : IAsyncDisposable
{
    private InvoiceWorld(Hotel h) => H = h;

    public Hotel H { get; }

    public InvoiceService Invoices => H.Get<InvoiceService>();

    public InvoiceQueries Queries => H.Get<InvoiceQueries>();

    public static async Task<InvoiceWorld> CreateAsync(bool withGstin = true, CancellationToken ct = default)
    {
        var h = await Hotel.CreateAsync(ct);
        if (withGstin) await h.ScalarAsync($"UPDATE inv.property SET gstin = '{InvoiceRulesTests.HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", ct);
        return new InvoiceWorld(h);
    }

    public Task SetBusinessDateAsync(string date, CancellationToken ct = default) =>
        H.ScalarAsync($"UPDATE inv.property SET business_date = '{date}' WHERE property_id = {H.PropertyId};", ct);

    public sealed record Stay(long StayId, long FolioId, long ReceiptId);

    /// <summary>
    /// Room <paramref name="room"/>, 22 → 24 Oct 2026 at ₹2,000 (5 % GST from the master). <paramref name="ac006"/> adds the prototype AC-006
    /// laundry ₹500 (18 %) and ₹500 discount (room rate) → folio ₹4,265; otherwise ₹4,200. Paid by UPI, checked out on 24 Oct.
    /// </summary>
    public async Task<Stay> DepartedStayAsync(string room, bool ac006, bool checkout = true, CancellationToken ct = default)
    {
        await SetBusinessDateAsync("2026-10-22", ct);
        var (_, stayId, folioId) = await H.SeedInHouseAsync(room, "2026-10-22", "2026-10-24", ct: ct);
        await H.Billing.PostRoomNightAsync(H.As(H.Owner), Hotel.Key(), new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 22)), ct);
        await SetBusinessDateAsync("2026-10-23", ct);
        await H.Billing.PostRoomNightAsync(H.As(H.Owner), Hotel.Key(), new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 23)), ct);
        if (ac006)
        {
            await H.Billing.PostChargeAsync(H.As(H.Reception), Hotel.Key(), new PostChargeRequest(folioId, "Laundry service", 50000), ct);
            var v = (await H.BillingQueries.GetFolioAsync(H.As(H.Owner), folioId, ct)).Version;
            var d = (await H.Billing.RequestAdjustmentAsync(H.As(H.Owner), Hotel.Key(), new RequestAdjustmentRequest("discount", folioId, null, 50000, "Service delay goodwill", v), ct)).Value;
            Assert.Equal("Posted", d.Outcome);
        }

        await SetBusinessDateAsync("2026-10-24", ct);
        var balance = (await H.BillingQueries.GetFolioAsync(H.As(H.Owner), folioId, ct)).Totals.BalancePaise;
        Assert.Equal(ac006 ? 426500 : 420000, balance);
        var receipt = (await H.PayAsync(folioId, balance, "UPI", ct: ct)).Value;
        if (checkout)
        {
            var version = await H.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stayId}", ct);
            await H.Stays.CheckoutStayAsync(H.Ctx(H.Reception, "CheckoutStay"), new CheckoutStayRequest(stayId, version), ct);
        }

        return new Stay(stayId, folioId, receipt.ReceiptId);
    }

    public async Task<IssueInvoiceResult> IssueAsync(long folioId, int? who = null, string? supply = null, string? name = null, string? gstin = null, string? pos = null, string? key = null,
        CancellationToken ct = default) =>
        (await Invoices.IssueInvoiceAsync(H.Ctx(who ?? H.Reception, InvoiceService.OpIssue, key), new IssueInvoiceRequest("folio", folioId, supply, name, gstin, null, pos), ct)).Value;

    public async Task<CreditNoteRequestResult> RequestCreditAsync(int who, long invoiceId, string mode, long? amount = null, IReadOnlyList<CreditLineRequest>? lines = null,
        string reason = "Noisy room goodwill", CancellationToken ct = default) =>
        (await Invoices.RequestCreditNoteAsync(H.Ctx(who, InvoiceService.OpRequestCreditNote), new RequestCreditNoteRequest(invoiceId, mode, reason, amount, lines), ct)).Value;

    public async Task<CreditNoteRequestResult> DecideAsync(int who, CreditNoteRequestResult r, bool approve, CancellationToken ct = default) =>
        (await Invoices.DecideCreditNoteAsync(H.Ctx(who, InvoiceService.OpDecideCreditNote), new DecideCreditNoteRequest(r.RequestId, approve, approve ? null : "Not justified", r.RequestVersion), ct)).Value;

    public ValueTask DisposeAsync() => H.DisposeAsync();
}
