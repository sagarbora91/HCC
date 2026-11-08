using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

/// <summary>A reservation as the split-folio commands see it, read with the folio rows locked (the store creates the primary folio, seq 1,
/// when the reservation has none yet — a Confirmed booking before its first advance).</summary>
public sealed record SplitFolioStay(long ReservationId, string Status, bool GroupMember, long? BookingCompanyId, FolioRoutingSnapshot Snapshot);

/// <summary>A company account as OpenFolio copies it onto the folio (name + GSTIN at open).</summary>
public sealed record SplitFolioCompany(long CompanyId, string Name, string? Gstin, bool Active);

/// <summary>D7 figures of a company folio after the command (SF-CONTRACT §7.3): limit, the company's exposure without this folio, the folio's estimate.</summary>
public sealed record SplitFolioCredit(string Company, Paise Limit, Paise UsedWithoutFolio, Paise FolioEstimate);

/// <summary>Persistence of the split-folio commands (migrations 4350 / 4351). Every call runs inside the command transaction.
/// SQL adapter: sf-server (Hcc.Infrastructure.SqlServer, over bill.folio / bill.folio_route).</summary>
public interface ISplitFolioStore
{
    Task<SplitFolioStay?> LockStayAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct);

    Task<SplitFolioCompany?> CompanyAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct);

    /// <summary>True when the customer key names a guest with an active LF B2 credit account.</summary>
    Task<bool> CreditGuestActiveAsync(ICommandTransaction tx, int propertyId, string customerKey, CancellationToken ct);

    Task<long> InsertFolioAsync(ICommandTransaction tx, long reservationId, int seq, string payerKind, long? companyId, string? customerKey, string? payerName,
        string? payerGstin, int by, DateTime utc, CancellationToken ct);

    /// <summary>Appends a bill.folio_route row (seq = the next for that reservation and category).</summary>
    Task InsertRouteAsync(ICommandTransaction tx, long reservationId, FolioRouteCategory category, long folioId, string? reason, int by, DateTime utc, CancellationToken ct);

    Task<(int Charges, int Receipts, bool Invoiced)> FolioUseAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    Task MarkRemovedAsync(ICommandTransaction tx, long folioId, DateTime utc, CancellationToken ct);

    /// <summary>D7 figures of the reservation's company folio (exposure query of SqlCompanyCreditStore, F8 block).</summary>
    Task<SplitFolioCredit> CompanyFolioCreditAsync(ICommandTransaction tx, int propertyId, long companyId, long reservationId, long folioId, CancellationToken ct);
}

/// <summary>
/// F8 split folio commands (DEC-152, SF-CONTRACT §4): OpenFolio, SetFolioRouting, RemoveFolio. Billing permission (PERM-FOL-002; SFQ-4 default:
/// no new permission). They write configuration only (no charge, no receipt: R7) and are audited. Rules in <see cref="SplitFolioRules"/>.
/// </summary>
public sealed class SplitFolioService(ICommandRunner runner, IAccessPolicy policy, ISplitFolioStore store, TimeProvider clock)
{
    public const string OpOpen = "OpenFolio", OpRoute = "SetFolioRouting", OpRemove = "RemoveFolio";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<FolioRouteCategory> ParseRoutes(IReadOnlyList<string>? routes)
    {
        var list = new List<FolioRouteCategory>();
        foreach (var r in routes ?? [])
        {
            FolioRouteCategory c;
            try { c = FolioRouting.Parse(r); }
            catch (ArgumentOutOfRangeException) { throw HccException.Validation("Route a category: room or extras."); }
            if (!list.Contains(c)) list.Add(c);
        }
        return list;
    }

    public async Task<CommandResult<FolioCommandResult>> OpenAsync(CommandContext ctx, OpenFolioRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var routes = ParseRoutes(req.Routes);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BillingPermissions.ChargePost, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpOpen }, req, async (tx, c) =>
        {
            var stay = await store.LockStayAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            var kind = (req.PayerKind ?? "").Trim().ToLowerInvariant();
            var seq = SplitFolioRules.EnsureCanOpen(stay.Snapshot, stay.Status, stay.GroupMember, kind, req.CompanyId, stay.BookingCompanyId, req.CustomerKey, routes);
            string? name = req.PayerName?.Trim(), gstin = req.PayerGstin?.Trim().ToUpperInvariant();
            if (kind == "company")
            {
                var co = await store.CompanyAsync(tx, ctx.PropertyId, req.CompanyId!.Value, c).ConfigureAwait(false)
                    ?? throw new HccException(HccErrorCode.NOT_FOUND, "Company account not found.");
                if (!co.Active) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{co.Name} is retired: choose an active company account.");
                (name, gstin) = (co.Name, co.Gstin);
            }
            else if (kind == "udhaar")
            {
                if (!await store.CreditGuestActiveAsync(tx, ctx.PropertyId, req.CustomerKey!.Trim(), c).ConfigureAwait(false))
                    throw new HccException(HccErrorCode.NOT_FOUND, "Choose a guest with an active credit account to pay this folio on Udhaar.");
            }
            else if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw HccException.Validation("Enter the name of the person who pays this folio.");
            if (gstin is { Length: > 0 } && gstin.Length != 15) throw HccException.Validation("Enter a 15-character GSTIN or leave it blank.");
            if (gstin is { Length: 0 }) gstin = null;
            var now = Now;
            var folioId = await store.InsertFolioAsync(tx, req.ReservationId, seq, kind, kind == "company" ? req.CompanyId : null, kind == "udhaar" ? req.CustomerKey!.Trim() : null,
                name, gstin, ctx.PrincipalId, now, c).ConfigureAwait(false);
            foreach (var r in routes) await store.InsertRouteAsync(tx, req.ReservationId, r, folioId, reason.Length > 0 ? reason : null, ctx.PrincipalId, now, c).ConfigureAwait(false);
            if (kind == "company") await EnsureCreditAsync(tx, ctx.PropertyId, req.CompanyId!.Value, req.ReservationId, folioId, c).ConfigureAwait(false);
            var letter = FolioRouting.Letter(seq);
            await tx.AuditAsync("FOLIO_OPENED", "folio-" + N(folioId), $"reservation {N(req.ReservationId)} · folio {letter} · {kind} {name} · routes {string.Join("+", routes.Select(FolioRouting.Code))}"
                + (reason.Length > 0 ? " · " + reason : ""), c).ConfigureAwait(false);
            return new FolioCommandResult(req.ReservationId, folioId, seq, letter, kind, routes.Select(FolioRouting.Code).ToList(), false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FolioCommandResult>> SetRoutingAsync(CommandContext ctx, SetFolioRoutingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var routes = ParseRoutes(req.Routes);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give the reason for changing the routing (3–200 characters).");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BillingPermissions.ChargePost, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRoute }, req, async (tx, c) =>
        {
            var stay = await store.LockStayAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            if (stay.Status is not ("Confirmed" or "InHouse")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This booking is no longer open.");
            var f = SplitFolioRules.EnsureCanRoute(stay.Snapshot, req.FolioSeq, routes);
            var now = Now;
            var primary = stay.Snapshot.Primary.FolioId;
            foreach (var cat in new[] { FolioRouteCategory.Room, FolioRouteCategory.Extras })
            {
                var current = stay.Snapshot.Routes.TryGetValue(cat, out var fid) ? fid : primary;
                long target = routes.Contains(cat) ? f.FolioId : current == f.FolioId ? primary : current;
                if (target != current) await store.InsertRouteAsync(tx, req.ReservationId, cat, target, reason, ctx.PrincipalId, now, c).ConfigureAwait(false);
            }
            if (f.PayerKind == "company" && f.CompanyId is { } co) await EnsureCreditAsync(tx, ctx.PropertyId, co, req.ReservationId, f.FolioId, c).ConfigureAwait(false);
            await tx.AuditAsync("FOLIO_ROUTED", "folio-" + N(f.FolioId), $"reservation {N(req.ReservationId)} · folio {f.Letter} · routes {string.Join("+", routes.Select(FolioRouting.Code))} · {reason}", c).ConfigureAwait(false);
            return new FolioCommandResult(req.ReservationId, f.FolioId, f.Seq, f.Letter, f.PayerKind, routes.Select(FolioRouting.Code).ToList(), false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FolioCommandResult>> RemoveAsync(CommandContext ctx, RemoveFolioRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BillingPermissions.ChargePost, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRemove }, req, async (tx, c) =>
        {
            var stay = await store.LockStayAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            var target = stay.Snapshot.Folios.FirstOrDefault(x => x.Seq == req.FolioSeq && !x.Removed);
            var use = target is null ? (0, 0, false) : await store.FolioUseAsync(tx, target.FolioId, c).ConfigureAwait(false);
            var f = SplitFolioRules.EnsureCanRemove(stay.Snapshot, req.FolioSeq, use.Item1, use.Item2, use.Item3);
            var now = Now;
            foreach (var (cat, fid) in stay.Snapshot.Routes)
                if (fid == f.FolioId) await store.InsertRouteAsync(tx, req.ReservationId, cat, stay.Snapshot.Primary.FolioId, "folio removed", ctx.PrincipalId, now, c).ConfigureAwait(false);
            await store.MarkRemovedAsync(tx, f.FolioId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("FOLIO_REMOVED", "folio-" + N(f.FolioId), $"reservation {N(req.ReservationId)} · folio {f.Letter}", c).ConfigureAwait(false);
            return new FolioCommandResult(req.ReservationId, f.FolioId, f.Seq, f.Letter, f.PayerKind, [], true);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>D7 estimate check (§7.3): refused when the company's exposure without this folio + the folio's estimate exceeds the limit.</summary>
    private async Task EnsureCreditAsync(ICommandTransaction tx, int propertyId, long companyId, long reservationId, long folioId, CancellationToken ct)
    {
        var cr = await store.CompanyFolioCreditAsync(tx, propertyId, companyId, reservationId, folioId, ct).ConfigureAwait(false);
        SplitFolioRules.EnsureCompanyCredit(companyId, cr.Company, cr.Limit, cr.UsedWithoutFolio, cr.FolioEstimate);
    }
}
