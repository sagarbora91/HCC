using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Payments;

/// <summary>Cash movement kinds (prototype <c>shiftMovements()</c> kinds; till decision pack "movement identity").</summary>
public enum CashMovementKind
{
    Payment,
    Deposit,
    Refund,
    DepositReturn,
    CompanyCredit,
    PosSale,
    PosVoid,
    Expense,
    ExpenseReversal,
    TransferIn,
    TransferOut,
    Banking,
}

public sealed record CashMovement(CashMovementKind Kind, Paise Amount);

/// <summary>The expected-cash formula with its parts (prototype <c>shiftExpected()</c>; Financial Rulebook §4; DEC-052).</summary>
public sealed record ExpectedCash(Paise Opening, Paise Payments, Paise Deposits, Paise Refunds, Paise Returns, Paise Expenses,
    Paise Other, Paise Restaurant, Paise Transfers, Paise Expected, bool Consistent);

/// <summary>
/// Shift drawer arithmetic. Ports <c>shiftMovements()</c> / <c>shiftExpected()</c> from
/// <c>03 Prototype/src/domain/streams/closing.ts</c>: every movement is signed (+ into the drawer, − out) and
/// expected = opening float + Σ movements. Refunds, returns and expenses are reported as positive parts.
/// </summary>
public static class CashDrawer
{
    public static string Code(CashMovementKind k) => k switch
    {
        CashMovementKind.Payment => "payment",
        CashMovementKind.Deposit => "deposit",
        CashMovementKind.Refund => "refund",
        CashMovementKind.DepositReturn => "deposit-return",
        CashMovementKind.CompanyCredit => "company-credit",
        CashMovementKind.PosSale => "pos-sale",
        CashMovementKind.PosVoid => "pos-void",
        CashMovementKind.Expense => "expense",
        CashMovementKind.ExpenseReversal => "expense-reversal",
        CashMovementKind.TransferIn => "transfer-in",
        CashMovementKind.TransferOut => "transfer-out",
        CashMovementKind.Banking => "banking",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "Unknown movement kind."),
    };

    public static CashMovementKind Parse(string code)
    {
        foreach (var k in Enum.GetValues<CashMovementKind>())
            if (Code(k) == code) return k;
        throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown movement kind.");
    }

    /// <summary>The movement a cash folio receipt makes: payments and deposits come in; every other kind goes out (prototype rule).</summary>
    public static CashMovement ForReceipt(ReceiptKind kind, Paise amount) => kind switch
    {
        ReceiptKind.Payment => new(CashMovementKind.Payment, amount),
        ReceiptKind.Deposit => new(CashMovementKind.Deposit, amount),
        ReceiptKind.Refund => new(CashMovementKind.Refund, -amount),
        ReceiptKind.DepositReturn => new(CashMovementKind.DepositReturn, -amount),
        ReceiptKind.CompanyCredit => new(CashMovementKind.CompanyCredit, -amount),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown receipt kind."),
    };

    public static ExpectedCash Expected(Paise openingFloat, IReadOnlyCollection<CashMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);
        Paise Sum(params CashMovementKind[] kinds) => movements.Where(m => kinds.Contains(m.Kind)).Aggregate(Paise.Zero, (a, m) => a + m.Amount);
        var payments = Sum(CashMovementKind.Payment);
        var deposits = Sum(CashMovementKind.Deposit);
        var refunds = -Sum(CashMovementKind.Refund);
        var returns = -Sum(CashMovementKind.DepositReturn);
        var expenses = -Sum(CashMovementKind.Expense, CashMovementKind.ExpenseReversal);
        var other = Sum(CashMovementKind.CompanyCredit);
        var restaurant = Sum(CashMovementKind.PosSale, CashMovementKind.PosVoid);
        var transfers = Sum(CashMovementKind.TransferIn, CashMovementKind.TransferOut, CashMovementKind.Banking);
        var expected = openingFloat + payments + deposits + restaurant + other + transfers - refunds - returns - expenses;
        var direct = movements.Aggregate(openingFloat, (a, m) => a + m.Amount);
        return new ExpectedCash(openingFloat, payments, deposits, refunds, returns, expenses, other, restaurant, transfers, expected, expected == direct);
    }

    /// <summary>Note denominations for the count grid in rupees (prototype SHIFT_DENOMINATIONS); coins are entered in paise.</summary>
    public static readonly IReadOnlyList<int> Denominations = [500, 200, 100, 50, 20, 10];

    /// <summary>Count grid → paise (prototype <c>shiftDenominationTotal</c>; coins in paise, so no rounding is needed).</summary>
    public static Paise DenominationTotal(IReadOnlyDictionary<string, int> notes, long coinsPaise)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (coinsPaise < 0) throw HccException.Validation("Denomination counts cannot be negative.");
        var total = new Paise(coinsPaise);
        foreach (var (note, count) in notes)
        {
            if (!int.TryParse(note, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rupees) || !Denominations.Contains(rupees))
                throw HccException.Validation($"Unknown note denomination '{note}'.");
            if (count < 0) throw HccException.Validation("Denomination counts cannot be negative.");
            total += new Paise(checked((long)count * rupees * 100));
        }

        return total;
    }
}
