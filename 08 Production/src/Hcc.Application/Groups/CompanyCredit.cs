using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Groups;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Application.Groups;

/// <summary>A company-credit receipt attributed to the company (checkout to company credit): the stay's balance the company owes.</summary>
public sealed record CompanyCreditReceiptRow(long ReceiptId, long ReservationId, string PublicNo, DateOnly BusinessDate, Paise Amount);

/// <summary>Money received from the company against its company-credit receivable (<c>grp.company_payment</c>).</summary>
public sealed record CompanyPaymentRow(long PaymentId, DateOnly BusinessDate, string Tender, string Reference, string? InvoiceNo, Paise Amount, int PrincipalId,
    DateTime RecordedUtc);

/// <summary>
/// A live reservation billed to the company: its posted lines, what the guest paid (payments − refunds), the company credit already moved off its
/// folio (to the company's receivable, or a group master's transfer) and the agreed nights (P2 snapshot: rate + GST rate).
/// </summary>
public sealed record CompanyStayRow(long ReservationId, string PublicNo, string Guest, string Status, IReadOnlyList<PostedLine> Lines, Paise GuestPaid,
    Paise CompanyCredit, IReadOnlyList<(DateOnly Night, Paise Rate, int Bps)> Agreed);

/// <summary>Everything the exposure of one company needs, read in one batch (inside the command transaction or on the store's own connection).</summary>
public sealed record CompanyCreditFacts(DateOnly BusinessDate, IReadOnlyList<CompanyCreditReceiptRow> Credits, IReadOnlyList<CompanyPaymentRow> Payments,
    IReadOnlyList<CompanyStayRow> Stays, IReadOnlyList<long> GroupIds);

public sealed record NewCompanyPayment(long CompanyId, DateOnly BusinessDate, string Tender, string Reference, string? InvoiceNo, Paise Amount, int PrincipalId,
    DateTime RecordedUtc);

/// <summary>SQL adapter: Hcc.Infrastructure.SqlServer/Groups/SqlCompanyCreditStore (migration 3500).</summary>
public interface ICompanyCreditStore
{
    /// <summary>
    /// The company's receivable rows, its payments, its live stays (Held / Confirmed / InHouse reservations with <c>company_id</c> = the company that are
    /// not members of an open group master billed to the same company) and its open group masters.
    /// </summary>
    Task<CompanyCreditFacts> LoadAsync(ICommandTransaction? tx, int propertyId, long companyId, CancellationToken ct = default);

    Task<long> InsertPaymentAsync(ICommandTransaction tx, NewCompanyPayment payment, CancellationToken ct = default);
}

/// <summary>
/// DEC-076 port for the modules that bill to a company account (Reservations: booking guarantee; Billing: company credit at checkout; Groups:
/// charge routing to a company-billed master). Lock the company first, compute the exposure, apply <see cref="CompanyCreditRules"/>.
/// </summary>
public interface ICompanyCreditGuard
{
    /// <summary>The company account of the property under UPDLOCK + HOLDLOCK (serializes the credit decisions of one company); NOT_FOUND when absent.</summary>
    Task<CompanyRow> LockAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct = default);

    Task<CompanyExposure> ExposureAsync(ICommandTransaction? tx, int propertyId, long companyId, CancellationToken ct = default);
}

/// <summary>
/// Company credit (DEC-076, audit A03a-05): the exposure of a company account (<see cref="CompanyExposure"/> defines it), the guard the billing
/// modules call before billing more to the company, and <c>RecordCompanyPayment</c> — money received from the company against its company-credit
/// receivable (PERM-GRP-005, non-cash like the group master folio, never more than the receivable), which frees credit. Every command goes through
/// <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox: <c>CompanyPaymentRecorded</c>, booked by accounting as Dr tender · Cr company receivables).
/// </summary>
public sealed class CompanyCredit(ICommandRunner runner, ICompanyStore companies, ICompanyCreditStore store, IGroupStore groups, IAccessPolicy policy,
    TimeProvider clock, IBusinessDayGuard? days = null) : ICompanyCreditGuard
{
    public const string OpPayment = "RecordCompanyPayment";

    public async Task<CompanyRow> LockAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct = default)
        => await companies.LockAsync(tx, propertyId, companyId, ct).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Company account not found.");

    public async Task<CompanyExposure> ExposureAsync(ICommandTransaction? tx, int propertyId, long companyId, CancellationToken ct = default)
        => await ComputeAsync(tx, propertyId, await store.LoadAsync(tx, propertyId, companyId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    private async Task<CompanyExposure> ComputeAsync(ICommandTransaction? tx, int propertyId, CompanyCreditFacts f, CancellationToken ct)
    {
        var items = new List<CompanyExposureItem>();
        var receivable = Paise.Zero;
        foreach (var r in f.Credits)
        {
            items.Add(new CompanyExposureItem("receivable", r.ReceiptId, r.PublicNo, r.Amount));
            receivable += r.Amount;
        }

        foreach (var p in f.Payments)
        {
            items.Add(new CompanyExposureItem("payment", p.PaymentId, p.InvoiceNo ?? p.Reference, -p.Amount));
            receivable -= p.Amount;
        }

        var stays = Paise.Zero;
        foreach (var s in f.Stays)
        {
            var e = GroupEstimate.Exposure(s.Status, s.Lines, s.Agreed, s.GuestPaid + s.CompanyCredit);
            items.Add(new CompanyExposureItem("stay", s.ReservationId, s.PublicNo, e));
            stays += e;
        }

        var masters = Paise.Zero;
        foreach (var id in f.GroupIds)
        {
            var snapshot = await groups.LoadAsync(tx, propertyId, id, ct).ConfigureAwait(false);
            if (snapshot is null) continue;
            var e = MasterExposure(snapshot);
            items.Add(new CompanyExposureItem("group", id, snapshot.Group.Code, e));
            masters += e;
        }

        return new CompanyExposure(receivable < Paise.Zero ? Paise.Zero : receivable, stays, masters, items);
    }

    /// <summary>A group master's exposure: member lines already routed to the master + attrition with GST − master receipts; never below 0.</summary>
    public static Paise MasterExposure(GroupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ledger = GroupLedger.Build(snapshot);
        var routed = ledger.Members.SelectMany(m => m.Lines).Where(l => l.Routed).Aggregate(Paise.Zero, (a, l) => a + l.Shares.Master);
        var attrition = snapshot.Entries.Where(e => e.Kind == "attrition").Aggregate(Paise.Zero, (a, e) => a + e.Amount + (e.Tax ?? Paise.Zero));
        var owed = routed + attrition - ledger.Master.Received;
        return owed < Paise.Zero ? Paise.Zero : owed;
    }

    public async Task<CommandResult<CompanyPaymentResult>> RecordPaymentAsync(CommandContext ctx, RecordCompanyPaymentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        var reference = GroupText.Optional(req.Reference, 120) ?? string.Empty;
        var tender = Tenders.Validate(req.Tender, reference);
        if (tender == Tenders.Cash)
            throw HccException.Validation("A company pays by UPI, card or bank transfer (a cheque is recorded as a bank transfer). Take cash on a guest folio in a cashier shift.");
        var invoiceNo = GroupText.Optional(req.InvoiceNo, 16)?.ToUpperInvariant();
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Payment, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // DAY_CLOSED (P6)
            var co = await LockAsync(tx, ctx.PropertyId, req.CompanyId, c).ConfigureAwait(false);
            var facts = await store.LoadAsync(tx, ctx.PropertyId, co.CompanyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, facts.BusinessDate, "a company payment", c).ConfigureAwait(false); // DEC-065
            var before = await ComputeAsync(tx, ctx.PropertyId, facts, c).ConfigureAwait(false);
            if (before.Receivable <= Paise.Zero) throw GroupCommands.Invalid($"{co.Name} owes nothing on company credit.");
            if (amount > before.Receivable)
                throw HccException.Validation($"Payment cannot exceed what {co.Name} owes on company credit ({before.Receivable}).");
            var id = await store.InsertPaymentAsync(tx, new NewCompanyPayment(co.CompanyId, facts.BusinessDate, tender, reference, invoiceNo, amount, ctx.PrincipalId,
                clock.GetUtcNow().UtcDateTime), c).ConfigureAwait(false);
            var receivable = before.Receivable - amount;
            var used = before.Total - amount;
            var limit = new Paise(co.CreditLimitPaise);
            await tx.AuditAsync("COMPANY_PAYMENT_RECORDED", Target(co.CompanyId), $"{co.Name} · {tender} · {amount} · ref {reference}"
                + (invoiceNo is null ? "" : " · invoice " + invoiceNo) + $" · receivable {before.Receivable} → {receivable} · manual entry, not verified", c).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.CompanyPaymentRecorded, co.CompanyId,
                new CompanyPaymentRecordedEvent(co.CompanyId, id, facts.BusinessDate, tender, amount.Value, reference, co.Name), c).ConfigureAwait(false);
            return new CompanyPaymentResult(co.CompanyId, id, amount.Value, facts.BusinessDate, receivable.Value, used.Value, CompanyCreditRules.Remaining(limit, used).Value);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The credit figures of one company (GET /groups/companies carries them per company for PERM-GRP-004 holders).</summary>
    public static CompanyCreditView View(long companyId, long limitPaise, CompanyExposure e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var limit = new Paise(limitPaise);
        return new CompanyCreditView(companyId, limitPaise, e.Total.Value, e.Receivable.Value, e.OpenStays.Value, e.GroupMasters.Value,
            CompanyCreditRules.Remaining(limit, e.Total).Value, e.Items.Select(i => new CompanyCreditItemView(i.Kind, i.Id, i.Reference, i.Amount.Value)).ToList());
    }

    /// <summary>One company's credit picture and its payments (newest first), from one read.</summary>
    public async Task<(CompanyCreditView Credit, IReadOnlyList<CompanyPaymentView> Payments)> PictureAsync(int propertyId, CompanyRow company, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        var facts = await store.LoadAsync(null, propertyId, company.CompanyId, ct).ConfigureAwait(false);
        var e = await ComputeAsync(null, propertyId, facts, ct).ConfigureAwait(false);
        return (View(company.CompanyId, company.CreditLimitPaise, e), facts.Payments.OrderByDescending(p => p.PaymentId)
            .Select(p => new CompanyPaymentView(p.PaymentId, company.CompanyId, p.BusinessDate, p.Tender, p.Reference, p.InvoiceNo, p.Amount.Value, p.PrincipalId, p.RecordedUtc))
            .ToList());
    }

    private static string Target(long id) => "company:" + id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The DEC-076 checks the billing modules run, all on one company row lock: <see cref="NewAsync"/> (a booking or group line billed to the
/// company for the first time), <see cref="ChangeAsync"/> (something already billed to it changed) and <see cref="CreditAsync"/> (a stay's balance
/// put on company credit). Refusals are INVALID_TRANSITION with details.reason = CREDIT_LIMIT and the limit, used, remaining and needed figures.
/// </summary>
public static class CompanyCreditCheck
{
    /// <summary>After the write: the subject (stay or group) must fit beside everything else billed to the company.</summary>
    public static async Task NewAsync(ICompanyCreditGuard guard, ICommandTransaction tx, CompanyRow company, string kind, long subjectId, string what, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(company);
        var after = await guard.ExposureAsync(tx, company.PropertyId, company.CompanyId, ct).ConfigureAwait(false);
        var subject = after.Of(kind, subjectId);
        CompanyCreditRules.Ensure(company.CompanyId, company.Name, new Paise(company.CreditLimitPaise), after.Total - subject, subject, what);
    }

    /// <summary>After the write, with the exposure read before it: refused only when the change raises the exposure beyond the limit.</summary>
    public static async Task ChangeAsync(ICompanyCreditGuard guard, ICommandTransaction tx, CompanyRow company, CompanyExposure before, string kind, long subjectId,
        string what, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(before);
        var after = await guard.ExposureAsync(tx, company.PropertyId, company.CompanyId, ct).ConfigureAwait(false);
        CompanyCreditRules.EnsureChange(company.CompanyId, company.Name, new Paise(company.CreditLimitPaise), before.Total, after.Total, after.Of(kind, subjectId), what);
    }

    /// <summary>
    /// Company credit for a stay billed to the company: <paramref name="amount"/> (the folio balance) must fit beside the rest of the company's
    /// exposure (the stay itself excluded, since its balance is what moves to the receivable).
    /// </summary>
    public static async Task CreditAsync(ICompanyCreditGuard guard, ICommandTransaction tx, int propertyId, long companyId, long reservationId, Paise amount,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(guard);
        var co = await guard.LockAsync(tx, propertyId, companyId, ct).ConfigureAwait(false);
        var e = await guard.ExposureAsync(tx, propertyId, companyId, ct).ConfigureAwait(false);
        CompanyCreditRules.Ensure(co.CompanyId, co.Name, new Paise(co.CreditLimitPaise), e.Total - e.Of("stay", reservationId), amount, "stay on company credit");
    }
}
