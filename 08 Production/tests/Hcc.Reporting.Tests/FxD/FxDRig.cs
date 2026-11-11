// FX-RPT-D · shared rig of the seven stock report tests: the runner over the real access policy (CuBRig), a supervisor user and the common item seed.
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Infrastructure.SqlServer.Stock;
using Hcc.Reporting.Reports.FxD;
using Hcc.Reporting.Sql;
using Hcc.Reporting.Tests.CuB;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.FxD;

internal static class FxDRig
{
    public static IStockReadStore Store(ReportDatabase db) => new SqlStockReadStore(db.Platform.Connections);

    public static IMoneyReportSource Names(ReportDatabase db) => new SqlMoneyReportSource(db.Platform.Connections);

    /// <summary>
    /// Five items (code order): A-SOAP Soap bar amenity 12.50 min 100 reorder 200 std 1.5 (store 60, floor 2 10); A-ZETA Zeta wipes amenity 5.00 min 10 reorder 5 (no stock);
    /// C-FLOOR Floor cleaner cleaning 80.00 min 5 reorder 10 std 1.0 (store 10); L-BED Bedsheet linen 450.00 min 20 reorder 40 (store 5, floor 1 4, rooms 20, laundry 5);
    /// L-TOW Towel linen 200.00 min 30 reorder 25 (store 19, soiled 3).
    /// </summary>
    public static string ItemsSql(ReportDatabase db) => $"""
        DECLARE @p INT = {db.PropertyId};
        INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, std_x100, created_utc) VALUES
          (@p, 'A-SOAP', N'Soap bar', 'amenity', 'pcs', 1250, 100, 200, 150, '2026-01-01'), (@p, 'A-ZETA', N'Zeta wipes', 'amenity', 'pcs', 500, 10, 5, NULL, '2026-01-01'),
          (@p, 'C-FLOOR', N'Floor cleaner', 'cleaning', 'litre', 8000, 5, 10, 100, '2026-01-01'), (@p, 'L-BED', N'Bedsheet', 'linen', 'pcs', 45000, 20, 40, NULL, '2026-01-01'),
          (@p, 'L-TOW', N'Towel', 'linen', 'pcs', 20000, 30, 25, NULL, '2026-01-01');
        INSERT stk.stock(item_id, loc, qty) SELECT item_id, x.loc, x.qty FROM stk.item i JOIN (VALUES
          ('A-SOAP', 'store', 60), ('A-SOAP', 'floor2', 10), ('C-FLOOR', 'store', 10), ('L-BED', 'store', 5), ('L-BED', 'floor1', 4), ('L-BED', 'rooms', 20), ('L-BED', 'laundry', 5),
          ('L-TOW', 'store', 19), ('L-TOW', 'soiled', 3)) x(code, loc, qty) ON x.code = i.code;
        """;

    public static List<ReportRow> Data(ReportResult r) => r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    public static string Item(string code) => $"(SELECT item_id FROM stk.item WHERE code = '{code}')";

    /// <summary>Supervisor (view only, no stock money gate) next to the five seeded roles.</summary>
    public static Task AddSupervisor(ReportDatabase db) => CuBRig.AddUser(db, "SUP");
}
