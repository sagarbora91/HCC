using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

// ACC-4 · Bank reconciliation and UPI / card clearing (02 Planning/agents/ACC4-CONTRACT.md §2; rulebook B8, T14a / T14b; owner default A23).
// Prototype twin: 03 Prototype/src/domain/streams/accounting-bank.ts (same parsing, matching rules, settlement lines and report figures).
// Tables: migrations 4285–4289 (mst.bank_statement_layout, acc.bank_statement_import / _row, acc.bank_match, cleared_date). Money = paise;
// bank view: credit = money in.

public static class AccountingBankPermissions
{
    public const string Reconcile = "PERM-ACC-016"; // accounting.bank.import / match / settlement post
    public const string Layouts = "PERM-ACC-017";   // accounting.bank.layout master
}

public sealed record BankLayoutColumns(string Date, string Narration, string? Ref = null, string? Debit = null, string? Credit = null, string? Amount = null,
    string? DrCrFlag = null, string? Balance = null);

public sealed record BankStatementLayout(string Id, string Name, string BankLedger, char Delimiter, int HeaderRows, string DateFormat, BankLayoutColumns Columns,
    bool Active = true, string? RefPattern = null, string? SettlementPattern = null);

public sealed record BankParsedRow(int Line, DateOnly Date, string Narration, string? Ref, long Debit, long Credit, long? Balance);

public sealed record BankParse(IReadOnlyList<BankParsedRow> Rows, long OpeningBalance, long ClosingBalance);

public sealed record BankStatementRow(string Id, string ImportId, string BankLedger, DateOnly Date, string Narration, string? Ref, long Debit, long Credit, long? Balance,
    string Status = "unmatched", string? MatchId = null)
{
    public long Amount => Credit - Debit;
}

public sealed record BankLineRef(string VoucherId, int Line);

public sealed record BankBookLine(BankLineRef Ref, string No, DateOnly Date, string Narration, long Amount);

public sealed record BankMatch(string Id, IReadOnlyList<string> RowIds, IReadOnlyList<BankLineRef> Lines, string Rule, DateOnly ClearedDate, int By, DateTime At, DateTime? UndoneAt = null);

public sealed record BankProposal(IReadOnlyList<string> RowIds, IReadOnlyList<BankBookLine> Lines, string Rule, DateOnly ClearedDate);

public sealed record BankAutoResult(IReadOnlyList<BankProposal> Proposals, IReadOnlyList<string> Settlements, IReadOnlyList<string> Ambiguous);

public sealed record BankRecon(string Ledger, DateOnly AsOf, long BooksBalance, IReadOnlyList<BankBookLine> IssuedNotPresented, IReadOnlyList<BankBookLine> DepositsNotCredited,
    IReadOnlyList<BankStatementRow> BankOnly, long IssuedTotal, long DepositsTotal, long BankOnlyTotal, long ComputedStatement, long StatementBalance)
{
    public long Difference => StatementBalance - ComputedStatement;
}

public static class BankReconRules
{
    public const int RefDays = 7, AfterDays = 3;

    public static readonly BankStatementLayout Generic = new("generic", "Generic", "1030", ',', 1, "DD/MM/YYYY",
        new BankLayoutColumns("date", "narration", "ref", "debit", "credit", Balance: "balance"), SettlementPattern: "(UPI|CARD|POS|MERCHANT)[^A-Z]*(SETTL|STL)");

    public static HccException Refuse(string reason, string message, HccErrorCode code = HccErrorCode.VALIDATION) => YearLockRules.Refuse(code, reason, message);

    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    public static DateOnly? ParseDate(string v, string format)
    {
        var t = v.Trim();
        if (format == "DD-MMM-YYYY")
        {
            var p = t.Split('-');
            if (p.Length != 3) return null;
            var m = Array.IndexOf(Months, p[1].ToUpperInvariant());
            if (m < 0 || !int.TryParse(p[0], out var d) || !int.TryParse(p[2], out var y)) return null;
            try { return new DateOnly(y, m + 1, d); } catch (ArgumentOutOfRangeException) { return null; }
        }

        var f = format switch { "DD/MM/YYYY" => new[] { "d/M/yyyy", "dd/MM/yyyy" }, "DD-MM-YYYY" => ["d-M-yyyy", "dd-MM-yyyy"], "YYYY-MM-DD" => ["yyyy-MM-dd"], _ => [] };
        return DateOnly.TryParseExact(t, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var r) ? r : null;
    }

    public static long? ParseAmount(string? v)
    {
        var t = new string((v ?? "").Where(c => c is not (',' or ' ' or '₹')).ToArray());
        if (t.StartsWith('(') && t.EndsWith(')')) t = "-" + t[1..^1];
        if (t.Length == 0) return 0;
        if (!Regex.IsMatch(t, @"^-?\d+(\.\d{1,2})?$")) return null;
        var neg = t.StartsWith('-');
        var parts = t.TrimStart('-').Split('.');
        var paise = checked(long.Parse(parts[0], CultureInfo.InvariantCulture) * 100 + (parts.Length > 1 ? int.Parse((parts[1] + "00")[..2], CultureInfo.InvariantCulture) : 0));
        return neg ? -paise : paise;
    }

    public static List<List<string>> Split(string text, char delim)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var q = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (q)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; }
                else cell.Append(ch);
                continue;
            }

            if (ch == '"') q = true;
            else if (ch == delim) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(c => c.Trim().Length > 0)) rows.Add(row);
                row = [];
            }
            else cell.Append(ch);
        }

        row.Add(cell.ToString());
        if (row.Any(c => c.Trim().Length > 0)) rows.Add(row);
        return rows;
    }

    /// <summary>Wave D CSV formula guard: a cell Excel would run as a formula is kept as text.</summary>
    public static string Guard(string v) => v.Length > 0 && "=+-@\t\r".Contains(v[0], StringComparison.Ordinal) && !Regex.IsMatch(v, @"^-?\d") ? "'" + v : v;

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static BankParse Parse(string text, BankStatementLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var all = Split((text ?? "").TrimStart('﻿'), layout.Delimiter);
        if (all.Count < layout.HeaderRows) throw Refuse("BANK_LAYOUT", "The file has no header row.");
        var head = all[layout.HeaderRows - 1].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string? n) => n is null ? -1 : head.IndexOf(n.Trim().ToLowerInvariant());
        var c = layout.Columns;
        int date = Col(c.Date), narr = Col(c.Narration), rf = Col(c.Ref), dr = Col(c.Debit), cr = Col(c.Credit), amt = Col(c.Amount), flag = Col(c.DrCrFlag), bal = Col(c.Balance);
        if (date < 0 || narr < 0) throw Refuse("BANK_LAYOUT", $"Columns \"{c.Date}\" and \"{c.Narration}\" must be in the header row.");
        if (!((dr >= 0 && cr >= 0) || amt >= 0)) throw Refuse("BANK_LAYOUT", "Map either debit and credit columns or one amount column.");
        var rows = new List<BankParsedRow>();
        for (var i = layout.HeaderRows; i < all.Count; i++)
        {
            var r = all[i];
            var line = i + 1;
            string Get(int k) => k >= 0 && k < r.Count ? r[k].Trim() : "";
            var d = ParseDate(Get(date), layout.DateFormat) ?? throw Refuse("BANK_PARSE", $"Row {line}: \"{Get(date)}\" is not a {layout.DateFormat} date.");
            long debit = 0, credit = 0;
            if (amt >= 0)
            {
                var a = ParseAmount(Get(amt)) ?? throw Refuse("BANK_PARSE", $"Row {line}: amount \"{Get(amt)}\" is not a number.");
                var f = Get(flag).ToUpperInvariant();
                if (f.Length > 0) { if (f.StartsWith('D')) debit = Math.Abs(a); else credit = Math.Abs(a); }
                else if (a < 0) debit = -a;
                else credit = a;
            }
            else
            {
                debit = Math.Abs(ParseAmount(Get(dr)) ?? throw Refuse("BANK_PARSE", $"Row {line}: debit is not a number."));
                credit = Math.Abs(ParseAmount(Get(cr)) ?? throw Refuse("BANK_PARSE", $"Row {line}: credit is not a number."));
            }

            if (debit != 0 && credit != 0) throw Refuse("BANK_PARSE", $"Row {line}: a row carries either a debit or a credit.");
            long? balance = bal >= 0 && Get(bal).Length > 0 ? ParseAmount(Get(bal)) ?? throw Refuse("BANK_PARSE", $"Row {line}: balance is not a number.") : null;
            var narration = Guard(Get(narr));
            var reference = Get(rf);
            if (reference.Length == 0 && layout.RefPattern is { } rp && Regex.Match(narration, rp) is { Success: true } m) reference = m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
            rows.Add(new BankParsedRow(line, d, narration, reference.Length > 0 ? Guard(reference) : null, debit, credit, balance));
        }

        if (rows.Count == 0) throw Refuse("BANK_PARSE", "The file has no statement rows.");
        if (rows.All(r => r.Balance is not null))
        {
            var opening = rows[0].Balance!.Value - rows[0].Credit + rows[0].Debit;
            var run = opening;
            foreach (var r in rows)
            {
                run += r.Credit - r.Debit;
                if (run != r.Balance) throw Refuse("BANK_BALANCE_BREAK", $"BANK_BALANCE_BREAK: the running balance breaks at row {r.Line}.");
            }

            return new BankParse(rows, opening, run);
        }

        return new BankParse(rows, 0, rows.Sum(r => r.Credit - r.Debit));
    }

    public static string VoucherId(Voucher v) => v.SourceKey;

    public static List<BankBookLine> BookLines(IEnumerable<Voucher> vouchers, string ledger, DateOnly to) =>
        vouchers.Where(v => v.Date <= to && v.SourceKind != "opening" && v.Sub is not ("opening-entry" or "opening-import" or "opening"))
            .SelectMany(v => v.Entries.Select((e, i) => (v, e, i)))
            .Where(x => x.e.Account == ledger && (x.e.DebitPaise != 0 || x.e.CreditPaise != 0))
            .Select(x => new BankBookLine(new BankLineRef(VoucherId(x.v), x.i), x.v.Number, x.v.Date, x.v.Narration, x.e.DebitPaise - x.e.CreditPaise)).ToList();

    private static string Key(BankLineRef r) => r.VoucherId + "#" + r.Line.ToString(CultureInfo.InvariantCulture);

    /// <summary>Rules 1–3 (§2.3) over the unmatched rows of a ledger, in date order; never across a locked year.</summary>
    public static BankAutoResult AutoMatch(IReadOnlyList<BankStatementRow> rows, IReadOnlyList<BankBookLine> book, IEnumerable<BankMatch> matches, Func<DateOnly, bool> yearLocked,
        Func<BankStatementRow, Regex?> settlementPattern)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(yearLocked);
        ArgumentNullException.ThrowIfNull(settlementPattern);
        var used = matches.Where(m => m.UndoneAt is null).SelectMany(m => m.Lines).Select(Key).ToHashSet(StringComparer.Ordinal);
        var free = book.Where(l => !yearLocked(l.Date)).ToList();
        List<BankBookLine> Free() => free.Where(l => !used.Contains(Key(l.Ref))).ToList();
        var proposals = new List<BankProposal>();
        var settle = new List<string>();
        var amb = new List<string>();
        foreach (var r in rows.Where(r => r.Status == "unmatched" && !yearLocked(r.Date)).OrderBy(r => r.Date))
        {
            if (r.Ref is { } rf)
            {
                var c1 = Free().Where(l => l.Amount == r.Amount && Math.Abs(r.Date.DayNumber - l.Date.DayNumber) <= RefDays && l.Narration.Contains(rf, StringComparison.Ordinal)).ToList();
                if (c1.Count == 1) { used.Add(Key(c1[0].Ref)); proposals.Add(new([r.Id], [c1[0]], "auto-ref", r.Date)); continue; }
            }

            var c = Free().Where(l => l.Amount == r.Amount && r.Date.DayNumber - l.Date.DayNumber is >= 0 and <= AfterDays).ToList();
            if (c.Count == 1) { used.Add(Key(c[0].Ref)); proposals.Add(new([r.Id], [c[0]], "auto-amount-date", r.Date)); continue; }
            if (c.Count > 1) { amb.Add(r.Id); continue; }
            if (r.Credit > 0 && settlementPattern(r) is { } p && p.IsMatch(r.Narration)) settle.Add(r.Id);
        }

        return new BankAutoResult(proposals, settle, amb);
    }

    /// <summary>Rule 4: sums must agree (BANK_MATCH_SUM).</summary>
    public static void CheckManual(IReadOnlyList<BankStatementRow> rows, IReadOnlyList<BankBookLine> lines)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(lines);
        if (rows.Count == 0 || lines.Count == 0) throw HccException.Validation("Choose at least one statement row and one book line.");
        if (rows.Any(r => r.Status != "unmatched")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A statement row is already matched or ignored.");
        if (rows.Sum(r => r.Amount) != lines.Sum(l => l.Amount))
            throw Refuse("BANK_MATCH_SUM", "BANK_MATCH_SUM: the rows and the book lines must total the same.", HccErrorCode.INVALID_TRANSITION);
    }

    /// <summary>B8 (§2.5): Dr bank net · Dr 5080 MDR (+ GST unless ITC, then 1310 / 1320 halves) · Cr 1010 / 1020 gross.</summary>
    public static IReadOnlyList<VoucherEntry> SettlementLines(string bankLedger, long net, string clearing, long gross, long mdr, long mdrGst, bool itc)
    {
        if (clearing is not ("1010" or "1020")) throw HccException.Validation("Clearing ledger: 1010 UPI or 1020 card.");
        if (gross < 0 || mdr < 0 || mdrGst < 0 || net < 0) throw HccException.Validation("Amounts are zero or more.");
        if (gross - mdr - mdrGst != net) throw Refuse("BANK_SETTLEMENT_SUM", "BANK_SETTLEMENT_SUM: gross − MDR − GST must equal the bank credit.");
        var lines = new List<VoucherEntry> { new(bankLedger, net, 0) };
        var charge = mdr + (itc ? 0 : mdrGst);
        if (charge != 0) lines.Add(new("5080", charge, 0));
        if (itc && mdrGst != 0) { var c = mdrGst / 2; lines.Add(new("1310", mdrGst - c, 0)); lines.Add(new("1320", c, 0)); }
        lines.Add(new(clearing, 0, gross));
        return lines;
    }

    /// <summary>§2.6 reconciliation as of a date: books ± unmatched items = statement; difference must be 0.</summary>
    public static BankRecon Recon(string ledger, DateOnly asOf, IEnumerable<Voucher> vouchers, IReadOnlyList<BankStatementRow> rows, IEnumerable<BankMatch> matches, long importOpening)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(rows);
        var vs = vouchers.Where(v => v.Date <= asOf).ToList();
        var books = vs.SelectMany(v => v.Entries).Where(e => e.Account == ledger).Sum(e => e.DebitPaise - e.CreditPaise);
        var live = matches.Where(m => m.UndoneAt is null).SelectMany(m => m.Lines.Select(l => (Key(l), m.ClearedDate))).ToDictionary(x => x.Item1, x => x.ClearedDate, StringComparer.Ordinal);
        var unmatched = BookLines(vs, ledger, asOf).Where(l => !live.TryGetValue(Key(l.Ref), out var d) || d > asOf).ToList();
        var issued = unmatched.Where(l => l.Amount < 0).ToList();
        var deposits = unmatched.Where(l => l.Amount > 0).ToList();
        var mine = rows.Where(r => r.BankLedger == ledger && r.Date <= asOf).OrderBy(r => r.Date).ToList();
        var bankOnly = mine.Where(r => r.Status == "unmatched").ToList();
        long it = -issued.Sum(l => l.Amount), dt = deposits.Sum(l => l.Amount), bt = bankOnly.Sum(r => r.Amount);
        var stmt = mine.Count > 0 && mine.All(r => r.Balance is not null) ? mine[^1].Balance!.Value : importOpening + mine.Sum(r => r.Amount);
        return new BankRecon(ledger, asOf, books, issued, deposits, bankOnly, it, dt, bt, books + it - dt + bt, stmt);
    }

    public static string ReconCsv(BankRecon r)
    {
        ArgumentNullException.ThrowIfNull(r);
        static string Q(string v) { var t = Guard(v); return t.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : t; }
        static string Rs(long p) => (p / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var sb = new StringBuilder("Section,Date,Voucher / row,Narration,Amount\r\n");
        void L(params string[] c) => sb.Append(string.Join(',', c.Select(Q))).Append("\r\n");
        L("Balance per books", r.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "", "", Rs(r.BooksBalance));
        foreach (var l in r.IssuedNotPresented) L("Add: issued, not presented", l.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.No, l.Narration, Rs(-l.Amount));
        foreach (var l in r.DepositsNotCredited) L("Less: deposited, not credited", l.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.No, l.Narration, Rs(-l.Amount));
        foreach (var b in r.BankOnly) L("Bank only (unmatched)", b.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), b.Id, b.Narration, Rs(b.Amount));
        L("Balance per statement", r.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "", "", Rs(r.StatementBalance));
        L("Difference", "", "", "", Rs(r.Difference));
        return sb.ToString();
    }

    /// <summary>§2.6 clearing view: 1010 / 1020 balance and the unsettled tender days (FIFO), &gt; 3 days amber, &gt; 7 red.</summary>
    public static (long Balance, IReadOnlyList<(DateOnly Date, long Open, int Age, string Band)> Open) Clearing(IEnumerable<Voucher> vouchers, string code, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var days = vouchers.Where(v => v.Date <= asOf).SelectMany(v => v.Entries.Where(e => e.Account == code).Select(e => (v.Date, e.DebitPaise, e.CreditPaise)))
            .GroupBy(x => x.Date).OrderBy(g => g.Key).Select(g => (Date: g.Key, T: g.Sum(x => x.DebitPaise), S: g.Sum(x => x.CreditPaise))).ToList();
        var pool = days.Sum(d => d.S);
        var open = new List<(DateOnly, long, int, string)>();
        foreach (var d in days)
        {
            var take = Math.Min(pool, d.T);
            pool -= take;
            var left = d.T - take;
            if (left <= 0) continue;
            var age = asOf.DayNumber - d.Date.DayNumber;
            open.Add((d.Date, left, age, age > 7 ? "red" : age > 3 ? "amber" : "ok"));
        }

        return (days.Sum(d => d.T - d.S), open);
    }
}
