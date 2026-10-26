using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Domain.MoneyMasters;

/// <summary>
/// A payment method as the money-taking commands see it (MA-11): one row of <c>mst.payment_method</c> with the kind of the account it
/// settles to. <see cref="SettlesToTill"/> replaces the old literal "Cash" test: a method that settles to a till moves the shared drawer
/// (pay.cash_movement, DEC-052) and needs an open shift; every other method does not.
/// </summary>
public sealed record TenderMethod(string Code, string Name, string Kind, bool SettlesToTill, bool NeedsReference, bool FrontDesk, bool Pos, bool Expenses,
    bool Active = true, string? Ledger = null)
{
    public bool UsableAt(string place) => place switch
    {
        MoneyMasterValues.FrontDesk => FrontDesk,
        MoneyMasterValues.Pos => Pos,
        MoneyMasterValues.Expenses => Expenses,
        _ => false,
    };

    public bool IsCredit => Kind == MoneyMasterValues.Credit;
}

/// <summary>
/// Resolution of a payment method named on a receipt, an outlet bill or an expense (MA-3 / MA-11). The list is the property's
/// <c>mst.payment_method</c> rows; an empty list (a property whose money masters were never seeded) uses <see cref="Defaults"/>, which equal
/// the rows <c>mst.usp_seed_money_masters</c> stores (migration 2830) and the old fixed lists (folio Cash / UPI / Card / Bank transfer, outlet
/// Cash / UPI / Card, expenses Cash / UPI / Bank transfer / Vendor credit), so a property without a list behaves exactly as before.
/// Records keep storing the method NAME (pay.receipt.method, pos.bill.tender, ops.expense.method).
/// </summary>
public static class PaymentMethodBook
{
    public const string Cash = "Cash";

    public static readonly IReadOnlyList<TenderMethod> Defaults =
    [
        new("CASH", "Cash", MoneyMasterValues.Cash, true, false, true, true, true, true, "1000"),
        new("UPI", "UPI", MoneyMasterValues.Upi, false, true, true, true, true, true, "1010"),
        new("CARD", "Card", MoneyMasterValues.Card, false, true, true, true, false, true, "1020"),
        new("BANK", "Bank transfer", MoneyMasterValues.Bank, false, true, true, false, true, true, "1030"),
        new("VCREDIT", "Vendor credit", MoneyMasterValues.Credit, false, false, false, false, true, true, "2200"),
        new("COMPANY", "Company credit", MoneyMasterValues.Credit, false, false, false, false, false, true, null),
    ];

    /// <summary>The stored list, or the defaults when the property has none.</summary>
    public static IReadOnlyList<TenderMethod> Effective(IReadOnlyList<TenderMethod>? stored) => stored is { Count: > 0 } ? stored : Defaults;

    /// <summary>Row of a value: its name, its code, or its name ignoring case (retired rows resolve).</summary>
    public static TenderMethod? Find(IReadOnlyList<TenderMethod> methods, string? value)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return methods.FirstOrDefault(m => string.Equals(m.Name, t, StringComparison.Ordinal))
            ?? methods.FirstOrDefault(m => string.Equals(m.Code, t.ToUpperInvariant(), StringComparison.Ordinal))
            ?? methods.FirstOrDefault(m => string.Equals(m.Name, t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Active method names usable at a place, in list order.</summary>
    public static IReadOnlyList<string> NamesAt(IReadOnlyList<TenderMethod>? stored, string place) =>
        Effective(stored).Where(m => m.Active && m.UsableAt(place)).Select(m => m.Name).ToList();

    /// <summary>
    /// The method a new record may use at <paramref name="place"/>: known, usable there and active (MASTER_RETIRED otherwise; a refund back to
    /// the original receipt's method passes <paramref name="allowRetired"/>), with a reference when the method needs one and
    /// <paramref name="requireReference"/> is set (front desk and outlet; expenses keep their bill number instead).
    /// </summary>
    public static TenderMethod Pick(IReadOnlyList<TenderMethod>? stored, string? value, string place, string? reference, bool requireReference = true, bool allowRetired = false)
    {
        var list = Effective(stored);
        var m = Find(list, value);
        if (m is null || !m.UsableAt(place)) throw HccException.Validation(Choose(list, place));
        if (!m.Active && !allowRetired)
            throw MastersRules.Fail(MasterReasons.Retired, $"{m.Name} is retired — choose another payment method.", "method");
        if (requireReference && m.NeedsReference && string.IsNullOrWhiteSpace(reference))
            throw HccException.Validation("Enter the transaction reference for a non-cash payment.");
        return m;
    }

    /// <summary>
    /// Does a STORED value (an earlier record's method) settle to a till? Resolved against the list (retired rows included) and then the
    /// defaults; a value nobody knows is cash only when it is the literal "Cash" (records written before the masters).
    /// </summary>
    public static bool SettlesToTill(IReadOnlyList<TenderMethod>? stored, string? value) =>
        (Find(Effective(stored), value) ?? Find(Defaults, value))?.SettlesToTill ?? string.Equals((value ?? string.Empty).Trim(), Cash, StringComparison.Ordinal);

    /// <summary>Kind of a stored value (null when unknown).</summary>
    public static string? KindOf(IReadOnlyList<TenderMethod>? stored, string? value) => (Find(Effective(stored), value) ?? Find(Defaults, value))?.Kind;

    /// <summary>The refusal text listing what may be chosen (the same words as the old fixed lists when the list is the default one).</summary>
    public static string Choose(IReadOnlyList<TenderMethod> list, string place)
    {
        ArgumentNullException.ThrowIfNull(list);
        var names = list.Where(m => m.Active && m.UsableAt(place)).Select(m => m.Name).ToList();
        if (place == MoneyMasterValues.Expenses) return "Choose a funding source.";
        if (names.Count == 0) return "No payment method is usable here. The Owner adds one in Master configuration › Money › Payment methods.";
        var or = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        return place == MoneyMasterValues.Pos ? $"Select {or}." : $"Select a payment method ({or}).";
    }
}
