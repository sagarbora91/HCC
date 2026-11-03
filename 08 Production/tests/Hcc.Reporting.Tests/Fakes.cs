using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Reporting.Tests;

/// <summary>Role grants as in 0101 + 4405: principal id → role code; scoped grants (Cashier V·S on RPT-002) pass only without a property scope.</summary>
public sealed class FakePolicy : IAccessPolicy
{
    public static readonly Dictionary<int, string> Roles = new() { [1] = "OWN", [2] = "MGR", [3] = "REC", [4] = "CSH", [5] = "ACC", [6] = "SUP", [7] = "HKP", [8] = "MNT" };

    public readonly Dictionary<string, HashSet<string>> Grants = new(StringComparer.Ordinal)
    {
        ["PERM-RPT-001"] = ["OWN", "MGR", "ACC"], ["PERM-RPT-002"] = ["OWN", "MGR", "ACC"], ["PERM-RPT-003"] = ["OWN", "MGR", "ACC"], ["PERM-RPT-004"] = ["OWN", "MGR", "ACC"],
        ["PERM-RES-001"] = ["OWN", "MGR", "REC"], ["PERM-HK-001"] = ["OWN", "MGR", "SUP", "HKP", "MNT"],
    };

    public readonly HashSet<(int, string)> Denied = [];

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
    {
        if (Denied.Contains((principalId, permissionId)) || !Roles.TryGetValue(principalId, out var role)) return Task.FromResult(false);
        if (permissionId == "PERM-RPT-002" && role == "CSH") return Task.FromResult(scope is null); // V·S own shift
        return Task.FromResult(Grants.TryGetValue(permissionId, out var g) && g.Contains(role));
    }

    public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        if (!await HasAsync(propertyId, principalId, permissionId, scope, ct)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: " + permissionId);
    }
}

public sealed class FakeProperty(DateOnly date) : IReportPropertyInfo
{
    public DateOnly Date { get; set; } = date;

    public Task<ReportProperty> GetAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(new ReportProperty("HPE", "Test Hotel", Date));
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A throw-away report: masked guest, ID, nights, money, computed per-night money, formula-looking note.</summary>
public sealed class DemoSelector(int rows = 2, ReportDefinition? def = null) : IReportSelector
{
    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        new("guest", "Guest", ReportColumnType.Text, false, ReportMask.GuestName), new("id", "ID", ReportColumnType.Text, false, ReportMask.IdNumber),
        new("n", "Nights", ReportColumnType.Int, false), new("amt", "Amount", ReportColumnType.Money, true),
        new("avg", "Per night", ReportColumnType.Money, false, ReportMask.None, new ReportComputed("div(amt,n)", ["amt", "n"])), new("note", "Note", ReportColumnType.Text, false),
    ];

    public ReportDefinition Definition { get; } = def ?? new("rpt.admin.engine-test", "Engine test", "admin", "P3", "planned", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003",
        ReportDateClass.Business, [new("tag", "Tag", "enum", null, ["a", "b"], "a")], Cols, "Test basis",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        var list = new List<ReportRow>();
        if (rows == 2)
        {
            list.Add(new(ReportRowKind.Row, new Dictionary<string, object?> { ["guest"] = "Journey Test", ["id"] = "1234 5678 9012", ["n"] = 3L, ["amt"] = 100001L, ["note"] = "=HYPERLINK(\"x\")" }));
            list.Add(new(ReportRowKind.Total, new Dictionary<string, object?> { ["guest"] = "Total", ["id"] = null, ["n"] = 3L, ["amt"] = 100001L, ["note"] = "" }));
        }
        else
            for (var i = 0; i < rows; i++) list.Add(new(ReportRowKind.Row, new Dictionary<string, object?> { ["guest"] = "G" + i, ["n"] = 1L, ["amt"] = (long)i, ["note"] = "" }));
        return Task.FromResult(new ReportResult(Definition.Id, Definition.Title, parameters, Definition.Columns, list,
            new ReportAbout(DateTimeOffset.MinValue, "Tester", Definition.DateBasis, false, false)));
    }
}

public static class Rig
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 6, 30, 0, TimeSpan.Zero);

    public static ReportRunner Runner(FakePolicy? p, params IReportSelector[] selectors) =>
        new(new ReportCatalogue(selectors), p ?? new FakePolicy(), new FakeProperty(new DateOnly(2026, 10, 24)), [new FixedCodeEngine()], new FixedClock(Now));

    public static Caller C(int principal) => new(1, principal);

    public static readonly IReadOnlyDictionary<string, string?> NoParams = new Dictionary<string, string?>();
}
