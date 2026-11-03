// F7-R1 · engine demo report rpt.money.revenue-by-category-tender (F4-T2) — adapter selector over RevenueByCategoryQueries, no new figures
// (RPT-ARCHITECTURE §7). Same column keys as the prototype (03 Prototype/src/domain/reports/money/revenue-by-category-tender.ts). Occ % is the
// OP-13 computed-column demo: pct(nightsSold,rooms), filled by the engine.
using Hcc.Application.Billing;
using Hcc.Application.Reporting;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Reporting;

namespace Hcc.Reporting.Reports.Money;

public sealed class RevenueByCategoryTenderReport(RevenueByCategoryQueries queries) : IReportSelector
{
    public const string Id = "rpt.money.revenue-by-category-tender";

    private static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    private static ReportColumn T(string key, string label) => new(key, label, ReportColumnType.Text, false);

    public static readonly IReadOnlyList<ReportColumn> ColumnsA =
    [
        T("category", "Category"), new("rooms", "Rooms (room-nights sellable)", ReportColumnType.Int, false), new("nightsSold", "Room-nights sold", ReportColumnType.Int, false),
        new("occPct", "Occ %", ReportColumnType.Percent, false, ReportMask.None, new ReportComputed("pct(nightsSold,rooms)", ["nightsSold", "rooms"])),
        M("roomTariff", "Room tariff"), M("extraPerson", "Extra person"), M("mealPlans", "Meal plans"), M("otherRoomCharges", "Other charges"), M("discounts", "Discounts"),
        M("corrections", "Corrections"), M("net", "Net (excl. GST)"), M("cgst", "CGST"), M("sgst", "SGST"), M("gross", "Gross (incl. GST)"), M("adr", "ADR"),
    ];

    public static readonly IReadOnlyList<ReportColumn> ColumnsB =
    [
        T("category", "Category"), M("cash", "Cash"), M("upi", "UPI"), M("card", "Card"), M("bank", "Bank transfer"), M("companyCredit", "Company credit"), M("udhaar", "Udhaar"),
        M("ota", "OTA"), M("otherTenders", "Other tenders"), M("advancesReceived", "Advances received"), M("advancesAdjusted", "Advances adjusted"),
        M("advancesRefunded", "Advances refunded"), M("refunds", "Refunds (in tenders)"), M("netReceived", "Net received"),
    ];

    public static readonly IReadOnlyList<ReportColumn> ColumnsC = [T("item", "Item"), M("amount", "Amount")];

    public ReportDefinition Definition { get; } = new(Id, "Revenue by room category and tender", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003",
        ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business),
         new("basis", "Basis", "enum", null, [RevenueByCategoryCalculator.BusinessDate, RevenueByCategoryCalculator.Stay], RevenueByCategoryCalculator.BusinessDate)],
        ColumnsA, "Charges by service (business) date, receipts by receipt date; stay basis = Block A by arrival date. Bridge always on business dates.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Summary);

    private static ReportRow A(RevenueRowA x, ReportRowKind k) => new(k, new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["category"] = x.Label, ["rooms"] = (long)x.RoomNightsSellable, ["nightsSold"] = (long)x.NightsSold, ["roomTariff"] = x.RoomTariff, ["extraPerson"] = x.ExtraPerson,
        ["mealPlans"] = x.MealPlans, ["otherRoomCharges"] = x.OtherCharges, ["discounts"] = x.Discounts, ["corrections"] = x.Corrections, ["net"] = x.Net,
        ["cgst"] = x.Cgst, ["sgst"] = x.Sgst, ["gross"] = x.Gross, ["adr"] = x.Adr,
    });

    private static ReportRow B(RevenueRowB x, ReportRowKind k) => new(k, new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["category"] = x.Label, ["cash"] = x.Cash, ["upi"] = x.Upi, ["card"] = x.Card, ["bank"] = x.Bank, ["companyCredit"] = x.CompanyCredit, ["udhaar"] = x.Udhaar,
        ["ota"] = x.Ota, ["otherTenders"] = x.OtherTenders.Values.Sum(), ["advancesReceived"] = x.AdvancesReceived, ["advancesAdjusted"] = x.AdvancesAdjusted,
        ["advancesRefunded"] = x.AdvancesRefunded, ["refunds"] = x.Refunds, ["netReceived"] = x.NetReceived,
    });

    private static ReportRow C(string item, long? amount, ReportRowKind k = ReportRowKind.Row) =>
        new(k, new Dictionary<string, object?>(StringComparer.Ordinal) { ["item"] = item, ["amount"] = amount });

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var r = await queries.GetAsync(caller, parameters.From!.Value, parameters.To!.Value, parameters.Values.GetValueOrDefault("basis"), ct).ConfigureAwait(false);
        var c = r.C;
        var a = new ReportTable("a", "A Revenue", ColumnsA, [.. r.A.Select(x => A(x, ReportRowKind.Row)), A(r.ATotal, ReportRowKind.Total)]);
        var b = new ReportTable("b", "B Collections", ColumnsB, [.. r.B.Select(x => B(x, ReportRowKind.Row)), B(r.BTotal, ReportRowKind.Total)]);
        var cc = new ReportTable("c", "C Bridge", ColumnsC,
        [
            C("Opening due", c.OpeningDue), C("Gross charges", c.Gross), C("Net received", c.NetReceived), C("Advance movement", c.AdvanceMovement),
            C("Closing due", c.ClosingDue), C("Opening advances held", c.OpeningAdvances), C("Closing advances held", c.ClosingAdvances),
            C("Difference (must be 0)", c.Difference, ReportRowKind.Total),
        ]);
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, r.Legacy, parameters.From, parameters.To);
        return new ReportResult(Id, Definition.Title, parameters, a.Columns, a.Rows, about, Sections: [a, b, cc]);
    }
}
