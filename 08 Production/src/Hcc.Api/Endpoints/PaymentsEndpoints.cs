using Hcc.Application.Billing;
using Hcc.Application.Payments;
using Hcc.Contracts.Billing;

namespace Hcc.Api.Endpoints;

/// <summary>Cashier endpoints: CreateTill, OpenShift, SubmitShiftCount, GetShift, daily collections (DEC-052 shared till).</summary>
public static class PaymentsEndpoints
{
    public static RouteGroupBuilder MapPaymentsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        BillingEndpoints.Command<CreateTillRequest, TillResult>(api, "CreateTill", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().CreateTillAsync(c, k, r, ct));
        BillingEndpoints.Command<OpenShiftRequest, ShiftResult>(api, "OpenShift", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().OpenShiftAsync(c, k, r, ct));
        BillingEndpoints.Command<SubmitShiftCountRequest, ShiftResult>(api, "SubmitShiftCount", (sp, c, k, r, ct) => sp.GetRequiredService<CashierService>().SubmitShiftCountAsync(c, k, r, ct));
        api.MapGet("/shifts/{shiftId:long}", async (long shiftId, HttpContext http, ISessionCallerResolver session, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetShiftAsync(session.Resolve(http), shiftId, ct).ConfigureAwait(false)));
        api.MapGet("/cashier/collections", async (DateOnly date, HttpContext http, ISessionCallerResolver session, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetCollectionsAsync(session.Resolve(http), date, ct).ConfigureAwait(false)));
        return api;
    }
}
