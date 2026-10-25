using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Payroll;

/// <summary>Bank details as frozen at lock (review F-02: the bank file pays where the checker approved, not the live staff master).</summary>
public sealed record BankDetails(string BankName, string AccountNo, string Ifsc)
{
    /// <summary>Masked account number for audit text and screens: last 4 digits only.</summary>
    public static string Mask(string? account) => string.IsNullOrEmpty(account) ? "(none)" : "••••" + account[^Math.Min(4, account.Length)..];
}

/// <summary>
/// Payroll register, bank transfer and statutory files (SCC reportBankCsv / reportStatutoryCsv via the prototype). Rupees with 2 decimals;
/// review F-17: text cells starting with = + - @ are neutralised and the statutory file says DRAFT unless the run is locked.
/// ETP-REUSE: align with ETP's exporters (CSV/Excel) when V:\ETP is available.
/// </summary>
public static partial class PayrollFiles
{
    [GeneratedRegex(@"^[=+\-@\t\r]")]
    private static partial Regex Risky();

    [GeneratedRegex(@"^-?\d+(\.\d+)?$")]
    private static partial Regex Numeric();

    public static string Rupees(long paise) => (paise / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Cell(string text, bool isText = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text;
        if (isText && Risky().IsMatch(t) && !Numeric().IsMatch(t)) t = "'" + t;
        return t.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : t;
    }

    private static string Row(params object[] cells) => string.Join(',', cells.Select(c => c switch
    {
        string s => Cell(s),
        decimal d => Cell(PayrollRules.Num(d), false),
        IFormattable f => Cell(f.ToString(null, CultureInfo.InvariantCulture), false),
        _ => Cell(c?.ToString() ?? string.Empty),
    }));

    private static string Money(long p) => Rupees(p);

    public static string Register(PayMonth month, bool locked, IReadOnlyList<PayLine> lines, Func<string, string> deptName)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(deptName);
        var sb = new StringBuilder();
        sb.Append(Row("Month", "Staff ID", "Name", "Department", "Designation", "Pay type", "Payable days", "Earned", "Basic", "HRA", "Washing", "Special", "Overtime",
            "Gross", "PF wages", "PF (employee)", "ESIC (employee)", "PT", "Advance recovery", "Total deductions", "PF (employer)", "ESIC (employer)", "Net pay")).Append('\n');
        foreach (var l in lines)
            sb.Append(Row(month.ToString(), l.StaffId.ToString(CultureInfo.InvariantCulture), l.Name, deptName(l.Dept), l.Designation, l.PayType.ToString().ToLowerInvariant(),
                l.PayableDays, Money(l.Earned), Money(l.Basic), Money(l.Hra), Money(l.Washing), Money(l.Special), Money(l.Ot), Money(l.Gross), Money(l.PfWages),
                Money(l.Pf), Money(l.Esic), Money(l.Pt), Money(l.Advance), Money(l.Deductions), Money(l.PfEr), Money(l.EsicEr), Money(l.Net))).Append('\n');
        var t = PayTotals.Of(lines);
        sb.Append(Row(month.ToString(), "", "TOTAL", "", "", "", "", Money(t.Earned), Money(t.Basic), Money(t.Hra), Money(t.Washing), Money(t.Special), Money(t.Ot),
            Money(t.Gross), Money(t.PfWages), Money(t.Pf), Money(t.Esic), Money(t.Pt), Money(t.Advance), Money(t.Deductions), Money(t.PfEr), Money(t.EsicEr), Money(t.Net))).Append('\n');
        sb.Append(Cell($"{(locked ? "Locked" : "Draft")} run · fictional data · {PayrollRates.Disclaimer}")).Append('\n');
        return sb.ToString();
    }

    /// <summary>Bank transfer file: one row per staff member with net pay; account numbers keep a leading apostrophe for Excel. Simulated — no money moves.</summary>
    public static string Bank(PayMonth month, IReadOnlyList<PayLine> lines, IReadOnlyDictionary<long, BankDetails> frozen, int issueNo)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(frozen);
        var sb = new StringBuilder();
        sb.Append(Row("Sr", "Employee Name", "Employee ID", "Bank Name", "Account No", "IFSC", "Net Amount")).Append('\n');
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var b = frozen.TryGetValue(l.StaffId, out var x) ? x : new BankDetails("", "", "");
            sb.Append(Row((i + 1).ToString(CultureInfo.InvariantCulture), l.Name, l.StaffId.ToString(CultureInfo.InvariantCulture), b.BankName, "'" + b.AccountNo, b.Ifsc, Money(l.Net))).Append('\n');
        }

        sb.Append(Row("", "TOTAL", "", "", "", "", Money(lines.Sum(l => l.Net)))).Append('\n');
        sb.Append(Cell($"{month.Label} · Locked · bank details as locked · file {issueNo}{(issueNo > 1 ? " (REISSUE — do not pay twice)" : "")} · fictional bank details · simulated transfer — no money moves")).Append('\n');
        return sb.ToString();
    }

    /// <summary>Statutory summary + member-wise register (SCC reportStatutoryCsv + statutoryMemberRows). Nothing is filed (CA item P17).</summary>
    public static string Statutory(PayMonth month, bool locked, DateOnly ratesFrom, IReadOnlyList<PayLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var t = PayTotals.Of(lines);
        var sb = new StringBuilder();
        sb.Append(Row("Component", "Employee", "Employer", "Total")).Append('\n');
        sb.Append(Row("Provident Fund", Money(t.Pf), Money(t.PfEr), Money(t.Pf + t.PfEr))).Append('\n');
        sb.Append(Row("ESIC", Money(t.Esic), Money(t.EsicEr), Money(t.Esic + t.EsicEr))).Append('\n');
        sb.Append(Row("Professional Tax (Maharashtra)", Money(t.Pt), "0.00", Money(t.Pt))).Append('\n');
        sb.Append('\n');
        sb.Append(Row("Sr", "Staff ID", "Name", "Earned", "PF wages", "PF EE", "PF ER", "ESIC wages", "ESIC EE", "ESIC ER", "PT")).Append('\n');
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            sb.Append(Row((i + 1).ToString(CultureInfo.InvariantCulture), l.StaffId.ToString(CultureInfo.InvariantCulture), l.Name, Money(l.Earned), Money(l.PfWages),
                Money(l.Pf), Money(l.PfEr), Money(l.EsicWages), Money(l.Esic), Money(l.EsicEr), Money(l.Pt))).Append('\n');
        }

        sb.Append(Cell($"{month.Label} · {(locked ? "Locked run" : "DRAFT — NOT FINAL, do not file")} · rates from {ratesFrom:yyyy-MM-dd} · {PayrollRates.Disclaimer}")).Append('\n');
        return sb.ToString();
    }

    /// <summary>WhatsApp number: digits only, 10 digits → 91…; empty when unusable (SCC waEmpMessage).</summary>
    public static string WhatsAppNumber(string? phone)
    {
        var d = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (d.Length == 10) d = "91" + d;
        return d.Length == 12 && d.StartsWith("91", StringComparison.Ordinal) ? d : string.Empty;
    }

    private static readonly string[] Ones = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
        "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    private static string T2(long n) => n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");

    private static string T3(long n)
    {
        var h = n / 100;
        var r = n % 100;
        return (h > 0 ? Ones[h] + " Hundred" + (r > 0 ? " " : "") : "") + (r > 0 ? T2(r) : "");
    }

    /// <summary>Amount in words, Indian system (SCC slipWords), with paise.</summary>
    public static string AmountInWords(long paise)
    {
        var neg = paise < 0;
        var v = Math.Abs(paise);
        var num = v / 100;
        var ps = v % 100;
        string words;
        if (num == 0) words = "Zero";
        else
        {
            var parts = new List<string>();
            var cr = num / 10_000_000; num %= 10_000_000;
            var lk = num / 100_000; num %= 100_000;
            var th = num / 1000; num %= 1000;
            if (cr > 0) parts.Add(T3(cr) + " Crore");
            if (lk > 0) parts.Add(T3(lk) + " Lakh");
            if (th > 0) parts.Add(T3(th) + " Thousand");
            if (num > 0) parts.Add(T3(num));
            words = string.Join(' ', parts);
        }

        return (neg ? "Minus " : "") + "Rupees " + words + (ps > 0 ? " and " + T2(ps) + " Paise" : "") + " Only";
    }
}
