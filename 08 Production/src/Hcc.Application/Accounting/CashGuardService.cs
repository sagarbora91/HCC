using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Domain.Accounting;
using Hcc.Domain.Configuration;

namespace Hcc.Application.Accounting;

// ACC-2 flows · cash guards (02 Planning/agents/ACC2-POSTING-CONTRACT.md §7; rulebook B9, B10). Prototype twin:
// 03 Prototype/src/domain/streams/accounting-guards.ts (accCashGuard, accCashEnforce). Pure rules: Hcc.Domain.Accounting.CashGuard.

/// <summary>
/// The person a cash movement is from / to, as the guard totals it: <c>folio</c> (a guest's booking; Id = folio id — receipts of that booking on the
/// day and over the stay), <c>udhaar</c> (Id = the credit — its cash settlements), <c>vendor</c> (Text = the expense's vendor — Posted drawer-cash
/// expenses of the day), <c>bill</c> (an outlet bill on its own: nothing earlier counts).
/// </summary>
public sealed record CashParty(string Kind, long Id, string? Text = null)
{
    public const string Folio = "folio";
    public const string Udhaar = "udhaar";
    public const string Vendor = "vendor";
    public const string Bill = "bill";
}

/// <summary>Cash already recorded for a party (paise, signed; the record being checked is not in it). SQL adapter: SqlFlowsStore.</summary>
public interface ICashFlowReader
{
    /// <summary>Drawer-cash receipts from the party: on <paramref name="date"/> and over the whole scope (booking / credit).</summary>
    Task<(long Day, long Scope)> ReceiptTotalsAsync(ICommandTransaction tx, int propertyId, CashParty party, DateOnly date, CancellationToken ct = default);

    /// <summary>Drawer-cash payments to the party on <paramref name="date"/> (posted expenses).</summary>
    Task<long> PaymentDayTotalAsync(ICommandTransaction tx, int propertyId, CashParty party, DateOnly date, CancellationToken ct = default);
}

/// <summary>Marks a posted expense "deduction lost" (B10; ops.expense.cash_deduction_lost, migration 4254). SQL adapter: SqlFlowsStore.</summary>
public interface ICashDeductionMarker
{
    Task MarkDeductionLostAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default);
}

/// <summary>The three dated limits in Master configuration › Money and tax (Owner-only money settings, DEC-058; "VERIFY section numbers").</summary>
public static class CashGuardSettings
{
    private static SettingDefinition Limit(string key, string label, long seed, string unit, string help) => new(key, label, SettingType.Money)
    {
        Money = true, Min = 100, Max = 100_000_000, Default = ConfigValue.Of(seed), Unit = unit, Effective = EffectiveMode.Date, Dated = true,
        Help = help + " " + CashGuard.Verify + ". Illustrative — CA to confirm for a hotel.",
        Affects = "Cash receipts (refused at the limit) and cash payments (warned above it) dated on or after the effective date. Recorded entries are not changed.",
    };

    public static ISettingsProvider Provider { get; } = new SettingsProvider("acc-cash-guards", ConfigGroupIds.Money,
    [
        Limit(CashGuard.ReceiptLimitKey, "Cash receipt limit (refused at or above)", CashGuard.SeedReceiptLimitPaise, "₹ from one person in a day, or for one booking / transaction / event",
            "A cash receipt that takes one party's cash to this amount or more is refused (Income-tax Act 2025 s.186, old s.269ST)."),
        Limit(CashGuard.PaymentLimitKey, "Cash payment limit (warned above)", CashGuard.SeedPaymentLimitPaise, "₹ to one person in a day",
            "A cash payment above this to one party in a day is allowed with a warning and marked “deduction lost” (old s.40A(3))."),
        Limit(CashGuard.TransportLimitKey, "Cash payment limit for goods transport (warned above)", CashGuard.SeedTransportLimitPaise, "₹ to one transporter in a day",
            "As the cash payment limit, for expense categories of goods transport (old s.40A(3A) / Rule 6DD)."),
    ]);
}

/// <summary>
/// The commands' one guarded call (folio receipts, POS bills, Udhaar settlements; expenses): limits from Master configuration (seed without it),
/// totals from <see cref="ICashFlowReader"/>, decision by <see cref="CashGuard"/>. A refused receipt throws VALIDATION with
/// <c>details.reason</c> CASH_LIMIT; a payment above the limit returns <see cref="CashGuardResult.DeductionLost"/> and the warning.
/// </summary>
public sealed class CashGuardService(ICashFlowReader reader, ISettingsReader? settings = null, ICashDeductionMarker? marker = null)
{
    /// <summary>B10: flag the expense "deduction lost" (no-op without the marker).</summary>
    public Task MarkDeductionLostAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default)
        => marker is null ? Task.CompletedTask : marker.MarkDeductionLostAsync(tx, expenseId, ct);

    public async Task<CashGuardLimits> LimitsAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        if (settings is null) return CashGuardLimits.Seed;
        async Task<long> One(string key, long seed)
        {
            var v = await settings.GetAsync(tx, propertyId, key, date, ct).ConfigureAwait(false);
            return v.Kind == ConfigValueKind.Number && v.Number > 0 ? v.Number : seed;
        }

        return new(await One(CashGuard.ReceiptLimitKey, CashGuard.SeedReceiptLimitPaise).ConfigureAwait(false),
            await One(CashGuard.PaymentLimitKey, CashGuard.SeedPaymentLimitPaise).ConfigureAwait(false),
            await One(CashGuard.TransportLimitKey, CashGuard.SeedTransportLimitPaise).ConfigureAwait(false));
    }

    /// <summary>B9: throws CASH_LIMIT when this cash receipt takes the party to the limit (day or scope); otherwise the totals.</summary>
    public async Task<CashGuardResult> EnsureReceiptAsync(ICommandTransaction tx, int propertyId, CashParty party, long amountPaise, DateOnly date, string who,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(party);
        var limits = await LimitsAsync(tx, propertyId, date, ct).ConfigureAwait(false);
        var (day, scope) = party.Kind == CashParty.Bill ? (0L, 0L) : await reader.ReceiptTotalsAsync(tx, propertyId, party, date, ct).ConfigureAwait(false);
        var r = CashGuard.Receipt(day, scope, scoped: true, amountPaise, limits.ReceiptLimitPaise, who, date);
        if (r.Refused) throw CashGuard.Refusal(r);
        return r;
    }

    /// <summary>B10: the warning and the "deduction lost" flag when this cash payment takes the party's day above the limit.</summary>
    public async Task<CashGuardResult> PaymentAsync(ICommandTransaction tx, int propertyId, CashParty party, long amountPaise, DateOnly date, bool transport, string who,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(party);
        var limits = await LimitsAsync(tx, propertyId, date, ct).ConfigureAwait(false);
        var day = party.Kind == CashParty.Bill ? 0 : await reader.PaymentDayTotalAsync(tx, propertyId, party, date, ct).ConfigureAwait(false);
        return CashGuard.Payment(day, amountPaise, transport ? limits.TransportLimitPaise : limits.PaymentLimitPaise, who, date);
    }
}
