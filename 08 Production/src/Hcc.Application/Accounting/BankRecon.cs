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

    /// <summary>Line −1 = the bank ledger's entry of that voucher (a voucher made from a row, matched before it is projected).</summary>
    private static string Any(BankLineRef r) => r.VoucherId + "#-1";

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
        List<BankBookLine> Free() => free.Where(l => !used.Contains(Key(l.Ref)) && !used.Contains(Any(l.Ref))).ToList();
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
        var live = matches.Where(m => m.UndoneAt is null).SelectMany(m => m.Lines.Select(l => (Key(l), m.ClearedDate))).GroupBy(x => x.Item1, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().ClearedDate, StringComparer.Ordinal);
        var unmatched = BookLines(vs, ledger, asOf).Where(l => !(live.TryGetValue(Key(l.Ref), out var d) || live.TryGetValue(Any(l.Ref), out d)) || d > asOf).ToList();
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

/// <summary>Imported statement header (migration 4286).</summary>
public sealed record BankImport(string Id, string BankLedger, string LayoutId, string FileName, string FileHash, DateOnly From, DateOnly To, long OpeningBalance, long ClosingBalance,
    int Rows, int ImportedBy, DateTime ImportedUtc);

/// <summary>Persistence of the bank reconciliation (migrations 4285–4287).</summary>
public interface IBankReconStore
{
    Task<IReadOnlyList<BankStatementLayout>> LayoutsAsync(int propertyId, CancellationToken ct);
    Task UpsertLayoutAsync(BankStatementLayout layout, CancellationToken ct);
    Task<bool> HashExistsAsync(int propertyId, string hash, CancellationToken ct);
    Task<BankImport> InsertImportAsync(int propertyId, BankImport header, IReadOnlyList<BankParsedRow> rows, CancellationToken ct);
    Task<IReadOnlyList<BankImport>> ImportsAsync(int propertyId, string ledger, CancellationToken ct);
    Task<IReadOnlyList<BankStatementRow>> RowsAsync(int propertyId, string? ledger, CancellationToken ct);
    Task<IReadOnlyList<BankMatch>> MatchesAsync(int propertyId, string? ledger, CancellationToken ct);
    Task<BankMatch> InsertMatchAsync(int propertyId, string ledger, IReadOnlyList<string> rowIds, IReadOnlyList<BankLineRef> lines, string rule, DateOnly cleared, int by, DateTime now, CancellationToken ct);
    Task UndoMatchAsync(int propertyId, string matchId, int by, DateTime now, CancellationToken ct);
    Task IgnoreRowAsync(int propertyId, string rowId, string reason, CancellationToken ct);
}

public sealed record BankImportResult(string ImportId, int Rows, int Skipped, long OpeningBalance, long ClosingBalance);
public sealed record BankAutoMatchResult(int Matched, IReadOnlyList<string> Settlements, IReadOnlyList<string> Ambiguous);
public sealed record BankVoucherResult(long VoucherId, string Status, string? MatchId);

/// <summary>
/// Application service of §2 (PERM-ACC-016 Owner + Accountant; layouts PERM-ACC-017 Owner). Vouchers made from a row (charges, interest, B8
/// settlement) go through the manual-voucher service (<paramref name="postVoucher"/>): the Owner's post at once and are matched to the row; the
/// Accountant's wait for the Owner (status pending, no match yet — match by hand once approved).
/// </summary>
public sealed class BankReconService(IBankReconStore store, IYearLockStore books, Access.IAccessPolicy policy, Func<DateTime> clock,
    Func<Billing.Caller, Contracts.Accounting.SaveManualVoucherRequest, CancellationToken, Task<Contracts.Accounting.ManualVoucherResult>>? postVoucher = null)
{
    private Task Can(Billing.Caller c, string perm, CancellationToken ct) => policy.RequireAsync(c.PropertyId, c.PrincipalId, perm, ct: ct);

    private async Task<bool> YearLocked(int pid, DateOnly d, CancellationToken ct) => YearLockRules.LockedOn(await books.YearsAsync(pid, ct).ConfigureAwait(false), d);

    public async Task UpsertLayoutAsync(Billing.Caller c, BankStatementLayout l, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(l);
        await Can(c, AccountingBankPermissions.Layouts, ct).ConfigureAwait(false);
        if (!Regex.IsMatch(l.Id ?? "", "^[a-z0-9][a-z0-9-]{0,39}$")) throw HccException.Validation("Layout id: lower-case letters, digits and dashes.");
        if (string.IsNullOrWhiteSpace(l.Name)) throw HccException.Validation("Name the layout.");
        if (l.Delimiter is not (',' or ';' or '\t')) throw HccException.Validation("Delimiter: comma, semicolon or tab.");
        if (l.HeaderRows is < 1 or > 10) throw HccException.Validation("Header rows: 1 to 10.");
        if (l.DateFormat is not ("DD/MM/YYYY" or "DD-MM-YYYY" or "YYYY-MM-DD" or "DD-MMM-YYYY")) throw HccException.Validation("Choose a date format.");
        if (string.IsNullOrWhiteSpace(l.Columns.Date) || string.IsNullOrWhiteSpace(l.Columns.Narration) || !((l.Columns.Debit is not null && l.Columns.Credit is not null) || l.Columns.Amount is not null))
            throw HccException.Validation("Map date, narration and debit + credit (or one amount column).");
        foreach (var p in new[] { l.RefPattern, l.SettlementPattern }.Where(p => p is not null))
            try { _ = new Regex(p!); } catch (ArgumentException) { throw HccException.Validation("A pattern is not a valid regular expression."); }
        await store.UpsertLayoutAsync(l, ct).ConfigureAwait(false);
    }

    public async Task<BankImportResult> ImportAsync(Billing.Caller c, string? ledger, string? layoutId, string? fileName, string? text, bool keepDuplicates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        if (ledger is null || !Regex.IsMatch(ledger, @"^1030/[A-Za-z0-9][A-Za-z0-9._-]{0,35}$")) throw HccException.Validation("Choose the bank account (1030/<bank>).");
        if (string.IsNullOrEmpty(text) || text.Length > 2_000_000) throw HccException.Validation("Choose a statement file (up to 2 MB).");
        var lay = (await store.LayoutsAsync(c.PropertyId, ct).ConfigureAwait(false)).FirstOrDefault(l => l.Id == (layoutId ?? "generic") && l.Active)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Choose an active statement layout.");
        var p = BankReconRules.Parse(text, lay);
        var hash = BankReconRules.Hash(text);
        if (await store.HashExistsAsync(c.PropertyId, hash, ct).ConfigureAwait(false)) throw BankReconRules.Refuse("BANK_DUP_FILE", "BANK_DUP_FILE: this statement file was already imported.");
        var years = await books.YearsAsync(c.PropertyId, ct).ConfigureAwait(false);
        if (p.Rows.FirstOrDefault(r => YearLockRules.LockedOn(years, r.Date)) is { } lr) throw YearLockRules.Locked(YearLockRules.FyOf(lr.Date));
        static string K(DateOnly d, long amt, string? rf, string n) => $"{d:yyyy-MM-dd}|{amt}|{rf}|{n}";
        var prev = (await store.RowsAsync(c.PropertyId, ledger, ct).ConfigureAwait(false)).Select(r => K(r.Date, r.Amount, r.Ref, r.Narration)).ToHashSet(StringComparer.Ordinal);
        var take = p.Rows.Where(r => keepDuplicates || !prev.Contains(K(r.Date, r.Credit - r.Debit, r.Ref, r.Narration))).ToList();
        var h = await store.InsertImportAsync(c.PropertyId, new BankImport("", ledger, lay.Id, (fileName ?? "statement.csv")[..Math.Min(200, (fileName ?? "statement.csv").Length)], hash,
            p.Rows[0].Date, p.Rows[^1].Date, p.OpeningBalance, p.ClosingBalance, take.Count, c.PrincipalId, clock()), take, ct).ConfigureAwait(false);
        return new BankImportResult(h.Id, take.Count, p.Rows.Count - take.Count, p.OpeningBalance, p.ClosingBalance);
    }

    private async Task<(IReadOnlyList<BankStatementRow> Rows, IReadOnlyList<BankBookLine> Book, IReadOnlyList<BankMatch> Matches)> LoadAsync(int pid, string ledger, DateOnly to, CancellationToken ct)
    {
        var rows = await store.RowsAsync(pid, ledger, ct).ConfigureAwait(false);
        var end = rows.Count > 0 && rows.Max(r => r.Date) > to ? rows.Max(r => r.Date) : to;
        var book = BankReconRules.BookLines(await books.VouchersUpToAsync(pid, end, ct).ConfigureAwait(false), ledger, end);
        return (rows, book, await store.MatchesAsync(pid, ledger, ct).ConfigureAwait(false));
    }

    public async Task<BankAutoMatchResult> AutoMatchAsync(Billing.Caller c, string ledger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        var (rows, book, matches) = await LoadAsync(c.PropertyId, ledger, DateOnly.MinValue, ct).ConfigureAwait(false);
        var years = await books.YearsAsync(c.PropertyId, ct).ConfigureAwait(false);
        var layouts = await store.LayoutsAsync(c.PropertyId, ct).ConfigureAwait(false);
        var imports = (await store.ImportsAsync(c.PropertyId, ledger, ct).ConfigureAwait(false)).ToDictionary(i => i.Id, i => i.LayoutId, StringComparer.Ordinal);
        Regex? Pattern(BankStatementRow r) => imports.TryGetValue(r.ImportId, out var lid) && layouts.FirstOrDefault(l => l.Id == lid)?.SettlementPattern is { } sp ? new Regex(sp, RegexOptions.IgnoreCase) : null;
        var a = BankReconRules.AutoMatch(rows, book, matches, d => YearLockRules.LockedOn(years, d), Pattern);
        var now = clock();
        foreach (var x in a.Proposals)
            await store.InsertMatchAsync(c.PropertyId, ledger, x.RowIds, x.Lines.Select(l => l.Ref).ToList(), x.Rule, x.ClearedDate, c.PrincipalId, now, ct).ConfigureAwait(false);
        return new BankAutoMatchResult(a.Proposals.Count, a.Settlements, a.Ambiguous);
    }

    public async Task<BankMatch> MatchAsync(Billing.Caller c, IReadOnlyList<string> rowIds, IReadOnlyList<BankLineRef> refs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(refs);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        var all = await store.RowsAsync(c.PropertyId, null, ct).ConfigureAwait(false);
        var rows = rowIds.Select(id => all.FirstOrDefault(r => r.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "A statement row was not found.")).ToList();
        if (rows.Select(r => r.BankLedger).Distinct(StringComparer.Ordinal).Count() != 1) throw HccException.Validation("All rows must be on one bank ledger.");
        var ledger = rows[0].BankLedger;
        foreach (var r in rows) if (await YearLocked(c.PropertyId, r.Date, ct).ConfigureAwait(false)) throw YearLockRules.Locked(YearLockRules.FyOf(r.Date));
        var (_, book, matches) = await LoadAsync(c.PropertyId, ledger, rows.Max(r => r.Date), ct).ConfigureAwait(false);
        var used = matches.Where(m => m.UndoneAt is null).SelectMany(m => m.Lines).ToHashSet();
        var lines = refs.Select(rf => book.FirstOrDefault(b => b.Ref == rf) ?? throw HccException.Validation($"Book line {rf.VoucherId} is not on {ledger}.")).ToList();
        if (lines.Any(l => used.Contains(l.Ref))) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A book line is already matched.");
        BankReconRules.CheckManual(rows, lines);
        return await store.InsertMatchAsync(c.PropertyId, ledger, rowIds, refs, "manual", rows.Max(r => r.Date), c.PrincipalId, clock(), ct).ConfigureAwait(false);
    }

    public async Task UnmatchAsync(Billing.Caller c, string matchId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        var m = (await store.MatchesAsync(c.PropertyId, null, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == matchId && x.UndoneAt is null)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "This match was not found.");
        if ((await books.LockedMonthsAsync(c.PropertyId, ct).ConfigureAwait(false)).Contains(m.ClearedDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Month {m.ClearedDate:yyyy-MM} is locked — the match stays.");
        if (await YearLocked(c.PropertyId, m.ClearedDate, ct).ConfigureAwait(false)) throw YearLockRules.Locked(YearLockRules.FyOf(m.ClearedDate));
        await store.UndoMatchAsync(c.PropertyId, matchId, c.PrincipalId, clock(), ct).ConfigureAwait(false);
    }

    public async Task IgnoreAsync(Billing.Caller c, string rowId, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        if ((reason ?? "").Trim().Length < 3) throw HccException.Validation("A reason is required to ignore a statement row.");
        await store.IgnoreRowAsync(c.PropertyId, rowId, reason!.Trim(), ct).ConfigureAwait(false);
    }

    private async Task<BankStatementRow> RowAsync(Billing.Caller c, string rowId, CancellationToken ct)
    {
        var r = (await store.RowsAsync(c.PropertyId, null, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == rowId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This statement row was not found.");
        if (r.Status != "unmatched") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This row is already matched or ignored.");
        if (await YearLocked(c.PropertyId, r.Date, ct).ConfigureAwait(false)) throw YearLockRules.Locked(YearLockRules.FyOf(r.Date));
        return r;
    }

    private async Task<BankVoucherResult> PostAsync(Billing.Caller c, BankStatementRow r, string type, string template, string narration, IReadOnlyList<VoucherEntry> lines, CancellationToken ct)
    {
        if (postVoucher is null) throw new HccException(HccErrorCode.UNAVAILABLE, "Manual vouchers are not available on this server.");
        var req = new Contracts.Accounting.SaveManualVoucherRequest(null, r.Date, type, narration.Length > 400 ? narration[..400] : narration, (r.Ref ?? "BANK-" + r.Id) is var rf && rf.Length > 60 ? rf[..60] : rf, template,
            lines.Select(l => new Contracts.Accounting.ManualLineRequest(l.Account, l.DebitPaise, l.CreditPaise, null)).ToList());
        var v = await postVoucher(c, req, ct).ConfigureAwait(false);
        if (v.Status != "approved") return new BankVoucherResult(v.VoucherId, v.Status, null);
        var m = await store.InsertMatchAsync(c.PropertyId, r.BankLedger, [r.Id], [new BankLineRef("manual:" + v.VoucherId.ToString(CultureInfo.InvariantCulture), -1)], "created", r.Date, c.PrincipalId, clock(), ct).ConfigureAwait(false);
        return new BankVoucherResult(v.VoucherId, v.Status, m.Id);
    }

    /// <summary>Rule 5: charges (Dr ledger · Cr bank) or interest / other credits (Dr bank · Cr ledger); 9999 refused.</summary>
    public async Task<BankVoucherResult> CreateVoucherAsync(Billing.Caller c, string rowId, string? ledger, string? narration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        var r = await RowAsync(c, rowId, ct).ConfigureAwait(false);
        var led = ledger ?? (r.Debit > 0 ? "5080" : "4960");
        if (led == ChartOfAccounts.Suspense) throw HccException.Validation("An unknown credit cannot go to 9999 Suspense — the row stays unmatched until its ledger is known.");
        var amt = r.Debit > 0 ? r.Debit : r.Credit;
        var lines = r.Debit > 0 ? new VoucherEntry[] { new(r.BankLedger, 0, amt), new(led, amt, 0) } : [new(r.BankLedger, amt, 0), new(led, 0, amt)];
        return await PostAsync(c, r, r.Debit > 0 ? "Payment" : "Receipt", r.Debit > 0 ? "bank-charges" : "interest-income",
            string.IsNullOrWhiteSpace(narration) ? $"{(r.Debit > 0 ? "Bank charge" : "Bank credit")} · {r.Narration}" : narration.Trim(), lines, ct).ConfigureAwait(false);
    }

    /// <summary>B8 settlement journal matched to its row.</summary>
    public async Task<BankVoucherResult> SettlementAsync(Billing.Caller c, string rowId, string clearing, long? gross, long mdr, long mdrGst, bool itc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingBankPermissions.Reconcile, ct).ConfigureAwait(false);
        var r = await RowAsync(c, rowId, ct).ConfigureAwait(false);
        if (r.Credit <= 0) throw HccException.Validation("Choose a credit row.");
        var lines = BankReconRules.SettlementLines(r.BankLedger, r.Credit, clearing, gross ?? r.Credit, mdr, mdrGst, itc);
        return await PostAsync(c, r, "Journal", "journal", $"{(clearing == "1010" ? "UPI" : "Card")} settlement · {r.Narration}", lines, ct).ConfigureAwait(false);
    }

    public async Task<BankRecon> ReconAsync(Billing.Caller c, string ledger, DateOnly asOf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingPermissions.View, ct).ConfigureAwait(false);
        var rows = await store.RowsAsync(c.PropertyId, ledger, ct).ConfigureAwait(false);
        var opening = (await store.ImportsAsync(c.PropertyId, ledger, ct).ConfigureAwait(false)).OrderBy(i => i.From).FirstOrDefault()?.OpeningBalance ?? 0;
        return BankReconRules.Recon(ledger, asOf, await books.VouchersUpToAsync(c.PropertyId, asOf, ct).ConfigureAwait(false), rows,
            await store.MatchesAsync(c.PropertyId, ledger, ct).ConfigureAwait(false), opening);
    }

    public async Task<IReadOnlyList<BankStatementRow>> RowsAsync(Billing.Caller c, string ledger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingPermissions.View, ct).ConfigureAwait(false);
        return await store.RowsAsync(c.PropertyId, ledger, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BankStatementLayout>> LayoutsAsync(Billing.Caller c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await Can(c, AccountingPermissions.View, ct).ConfigureAwait(false);
        return await store.LayoutsAsync(c.PropertyId, ct).ConfigureAwait(false);
    }
}
