using System.Globalization;
using Hcc.Contracts.Accounting;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>
/// ACC-3 integration (acc3-exports' <see cref="OpenXmlExporter"/> fed with the acc3-pl / acc3-ageing views): the same tables as the prototype
/// <c>accounting-exports.ts</c> (<c>accPlTables</c>, <c>accBalanceSheetTable</c>, <c>accCashRatioTable</c>, <c>accAgeingTables</c>, <c>accGstSummaryTable</c>):
/// title block, bold header, body; money as <see cref="XlsxMoney"/>, dates as dates. Rendered with <see cref="StatementExports.Xlsx"/> / <see cref="StatementExports.Csv"/>.
/// </summary>
public static class ReportExports
{
    private static XlsxMoney M(long p) => new(p);

    private static XlsxBold B(string t) => new(t);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<IReadOnlyList<object?>> Block(string prop, string title, string span, IReadOnlyList<object?[]> range, bool provisional, IEnumerable<string>? notes = null)
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { B($"{title} · {span}{(provisional ? " · PROVISIONAL" : "")}") }, new object?[] { "Property", prop } };
        rows.AddRange(range);
        if (provisional) rows.Add(new object?[] { "Status", "Provisional — the range includes days that are not locked, or opening balances are pending." });
        foreach (var n in notes ?? []) rows.Add(new object?[] { "Note", n });
        rows.Add(Array.Empty<object?>());
        return rows;
    }

    private static StatementExports.Table Table(string name, List<IReadOnlyList<object?>> top, string[] head, IEnumerable<IReadOnlyList<object?>> body, double[] widths)
    {
        var freeze = top.Count + 1;
        top.Add(head.Select(h => (object?)B(h)).ToArray());
        top.AddRange(body);
        return new StatementExports.Table(name, top, freeze, widths);
    }

    private static IReadOnlyList<object?> R(params object?[] cells) => cells;

    public static IReadOnlyList<StatementExports.Table> Pl(string prop, PlView pl)
    {
        ArgumentNullException.ThrowIfNull(pl);
        var body = new List<IReadOnlyList<object?>>();
        void Section(string title, IEnumerable<PlLineView> lines, long total, string label)
        {
            body.Add(R(B(title), "", ""));
            foreach (var l in lines) body.Add(R(l.Code, l.Name, M(l.Paise)));
            body.Add(R("", B(label), M(total)));
            body.Add(R());
        }

        Section("Revenue", pl.Revenue.Lines, pl.Revenue.Total, "Total revenue");
        Section("Other income", pl.OtherIncome.Lines, pl.OtherIncome.Total, "Total other income");
        var ex = pl.Expenses;
        foreach (var g in new[] { ex.Consumption, ex.Employee, ex.Finance, ex.Depreciation, ex.Other })
            Section("Expenses · " + g.Name, g.Lines, g.Total, "Total " + g.Name.ToLowerInvariant());
        body.Add(R("", B("Total expenses"), M(ex.Total)));
        body.Add(R("", B("Profit before tax"), M(pl.ProfitBeforeTax)));
        var span = $"{Iso(pl.From)} to {Iso(pl.To)}";
        object?[][] range = [["From", pl.From], ["To", pl.To]];
        var notes = new List<string> { "No tax line (proprietor)." };
        notes.AddRange(pl.Warnings ?? []);
        return
        [
            Table("Profit and loss", Block(prop, "Profit & loss", span, range, pl.Provisional, notes), ["Code", "Ledger", "Amount"], body, [10, 44, 18]),
            Table("By Tally group", Block(prop, "Profit & loss by Tally group", span, range, pl.Provisional), ["Group", "Name", "Amount"],
                pl.ByTallyGroup.Select(g => R(g.Group, g.Name, M(g.Paise))), [24, 44, 18]),
        ];
    }

    public static StatementExports.Table BalanceSheet(string prop, BalanceSheetView bs)
    {
        ArgumentNullException.ThrowIfNull(bs);
        var body = new List<IReadOnlyList<object?>>();
        void Side(string title, IEnumerable<BsBlockView> blocks, long total)
        {
            body.Add(R(B(title), "", "", ""));
            foreach (var b in blocks)
            {
                body.Add(R("", B(b.Name), "", ""));
                foreach (var l in b.Lines) body.Add(R("", l.Code, l.Name, M(l.Paise)));
                body.Add(R("", "", "Total " + b.Name, M(b.Total)));
            }

            body.Add(R(B("Total " + title.ToLowerInvariant()), "", "", M(total)));
            body.Add(R());
        }

        Side("Liabilities", [bs.Liabilities.OwnersFunds, bs.Liabilities.NonCurrent, bs.Liabilities.Current], bs.Liabilities.Total);
        Side("Assets", [bs.Assets.NonCurrent, bs.Assets.Current], bs.Assets.Total);
        body.Add(R("Balanced", bs.Balanced ? "Yes" : "No", "", ""));
        body.Add(R("Suspense (9999)", "", "", M(bs.Suspense)));
        body.Add(R("Opening difference (1400)", "", "", M(bs.OpeningDifference)));
        return Table("Balance sheet", Block(prop, "Balance sheet", "as of " + Iso(bs.AsOf), [["As of", bs.AsOf]], bs.Provisional, ["HCC view — not the statutory statement."]),
            ["Side", "Block / code", "Ledger", "Amount"], body, [24, 22, 40, 18]);
    }

    public static StatementExports.Table CashRatio(string prop, CashRatioView c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return Table("Cash ratio", Block(prop, "Cash ratio", "FY " + c.Fy, [["Period", "FY " + c.Fy]], false, c.Flagged ? ["FLAGGED: a ratio is above 5 %."] : null), ["Measure", "Amount", "Ratio"],
        [
            R("Cash receipts", M(c.CashReceipts), new XlsxPercent(c.ReceiptsPct / 10000.0)), R("Total receipts", M(c.TotalReceipts), ""),
            R("Cash payments", M(c.CashPayments), new XlsxPercent(c.PaymentsPct / 10000.0)), R("Total payments", M(c.TotalPayments), ""), R("Flagged", c.Flagged ? "Yes" : "No", ""),
        ], [24, 18, 12]);
    }

    public static IReadOnlyList<StatementExports.Table> Ageing(string prop, AgeingView a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var t = a.Totals;
        var side = a.Side == "receivable" ? "Receivables" : "Payables";
        object?[][] range = [["As of", a.AsOf]];
        var rows = a.Rows.Select(r => R(r.Party, r.Name, r.Control, M(r.Open), M(r.B0_30), M(r.B31_60), M(r.B61_90), M(r.B90))).ToList();
        rows.Add(R(B("Total"), "", "", M(t.Open), M(t.B0_30), M(t.B31_60), M(t.B61_90), M(t.B90)));
        return
        [
            Table("Outstanding", Block(prop, side + " outstanding with ageing", "as of " + Iso(a.AsOf), range, false),
                ["Party", "Name", "Control", "Open", "0-30 days", "31-60 days", "61-90 days", "90+ days"], rows, [18, 36, 10, 16, 16, 16, 16, 16]),
            Table("Bills", Block(prop, side + " open bills", "as of " + Iso(a.AsOf), range, false), ["Party", "Name", "Bill ref", "Bill date", "Due", "Amount", "Paid", "Open", "Days"],
                a.Rows.SelectMany(r => r.Bills.Select(b => R(r.Party, r.Name, b.Ref, b.Date, b.Due, M(b.Amount), M(b.Paid), M(b.Open), b.Days))),
                [18, 36, 24, 12, 12, 16, 16, 16, 8]),
        ];
    }

    private static long Sum(GstHeadsView h) => h.IgstPaise + h.CgstPaise + h.SgstPaise;

    public static StatementExports.Table GstSummary(string prop, GstSummaryView g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var o = g.Output;
        var s = g.Setoff;
        return Table("GST summary", Block(prop, "GST summary", g.Month, [["Period", g.Month]], false, g.Ties.Differences), ["Item", "Amount"],
        [
            R(B("Output tax"), ""), R("5 % slab", M(o.Rate5)), R("18 % slab", M(o.Rate18)), R("B2B", M(o.B2b)), R("B2C small", M(o.B2cs)),
            R("Advances received (11A)", M(o.Advances11A)), R("Advances adjusted (11B)", M(o.Advances11B)), R("Credit notes", M(o.CreditNotes)),
            R(B("Input tax credit"), ""), R("CGST", M(g.Itc.CgstPaise)), R("SGST", M(g.Itc.SgstPaise)), R("IGST", M(g.Itc.IgstPaise)),
            R(B("Other"), ""), R("Reverse charge", M(g.Rcm)), R("Set-off · output tax", M(Sum(s.Output))), R("Set-off · credit used", M(Sum(s.Credit) - Sum(s.CreditLeft))),
            R("Set-off · payable in cash", M(s.Payable)), R("Set-off · challan", M(s.Challan)), R("Credit carried forward", M(Sum(s.CreditLeft))),
            R("Paid in cash (challan)", M(g.CashPaid)), R("TCS credit", M(g.TcsCredit)), R("Cash ledger balance (1340)", M(g.CashLedgerBalance)),
            R(B("Ties"), ""), R("Agrees with GSTR-1 summary", g.Ties.Gstr1 ? "Yes" : "No"), R("Agrees with GST month (GSTR-3B)", g.Ties.Gstr3b ? "Yes" : "No"),
        ], [34, 18]);
    }
}
