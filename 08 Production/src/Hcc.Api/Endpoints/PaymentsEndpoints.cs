using Hcc.Application.Billing;
using Hcc.Application.Payments;
using Hcc.Contracts.Billing;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Cashier endpoints: CreateTill, OpenShift, SubmitShiftCount, GetShift, cash desk, daily collections (DEC-052 shared till); Wave B1 (DEC-073):
/// bank deposits, till transfers, Owner drawings and their list / slip photo. All require a session.
/// </summary>
public static class PaymentsEndpoints
{
    public static RouteGroupBuilder MapPaymentsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<CreateTillRequest, TillResult>(g, "CreateTill", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().CreateTillAsync(c, k, r, ct));
        BillingEndpoints.Command<OpenShiftRequest, ShiftResult>(g, "OpenShift", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().OpenShiftAsync(c, k, r, ct));
        BillingEndpoints.Command<SubmitShiftCountRequest, ShiftResult>(g, "SubmitShiftCount", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().SubmitShiftCountAsync(c, k, r, ct));
        g.MapGet("/shifts/{shiftId:long}", async (long shiftId, HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetShiftAsync(http.HccCaller(), shiftId, ct).ConfigureAwait(false)));
        g.MapGet("/cashier/collections", async (DateOnly date, HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetCollectionsAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/cashier/desk", async (HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetCashDeskAsync(http.HccCaller(), ct).ConfigureAwait(false))); // W4-3 sync layer
        g.MapGet("/cashier/folios", async (HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetDeskFoliosAsync(http.HccCaller(), ct).ConfigureAwait(false))); // D5: Cashier's folio list (PERM-FOL-001)

        // Wave B1 (DEC-073, A03b-01): cash leaving the drawer.
        BillingEndpoints.Command<RecordCashBankingRequest, CashTransferResult>(g, "RecordCashBanking", (sp, c, k, r, ct) => sp.GetRequiredService<CashOutService>().RecordCashBankingAsync(c, k, r, ct));
        BillingEndpoints.Command<ConfirmCashBankingRequest, CashTransferResult>(g, "ConfirmCashBanking", (sp, c, k, r, ct) => sp.GetRequiredService<CashOutService>().ConfirmCashBankingAsync(c, k, r, ct));
        BillingEndpoints.Command<TransferTillCashRequest, CashTransferResult>(g, "TransferTillCash", (sp, c, k, r, ct) => sp.GetRequiredService<CashOutService>().TransferTillCashAsync(c, k, r, ct));
        BillingEndpoints.Command<AcceptTillTransferRequest, CashTransferResult>(g, "AcceptTillTransfer", (sp, c, k, r, ct) => sp.GetRequiredService<CashOutService>().AcceptTillTransferAsync(c, k, r, ct));
        BillingEndpoints.Command<RecordOwnerDrawingRequest, CashTransferResult>(g, "RecordOwnerDrawing", (sp, c, k, r, ct) => sp.GetRequiredService<CashOutService>().RecordOwnerDrawingAsync(c, k, r, ct));
        g.MapGet("/cashier/transfers", async (DateOnly from, DateOnly? to, HttpContext http, CashOutService q, CancellationToken ct)
            => Results.Ok(await q.ListAsync(http.HccCaller(), from, to ?? from, ct).ConfigureAwait(false)));
        g.MapGet("/cashier/transfers/{transferId:long}/slip", async (long transferId, HttpContext http, CashOutService q, CancellationToken ct)
            => Results.Ok(await q.SlipAsync(http.HccCaller(), transferId, ct).ConfigureAwait(false)));
        return api;
    }
}
