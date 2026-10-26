using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;
using Hcc.Domain.Stock;

namespace Hcc.Domain.Import;

public sealed record SnapRoomType(int Id, string Code, string? Name, int MaxOccupancy, long RackPaise, int SortOrder);

public sealed record SnapRoom(string RoomNo, int TypeId);

public sealed record SnapTariff(int TypeId, DateOnly From, long RatePaise);

public sealed record SnapSeason(int Id, string Name, DateOnly From, DateOnly To, int AdjBps, IReadOnlyList<int> TypeIds, int Priority);

/// <summary>A role of the access seed; <see cref="CredentialKind"/> PASSWORD or PIN (DEC-062).</summary>
public sealed record SnapRole(string Code, string Name, string CredentialKind);

public sealed record SnapUser(string Login, string DisplayName, IReadOnlyList<string> Roles);

public sealed record SnapLedger(string Code, string Name, string Group, string? HccAccount);

public sealed record SnapOpening(string LedgerCode, long DebitPaise, long CreditPaise);

/// <summary>A till; <see cref="ImportedOpening"/> = the float of its only shift when that shift is an untouched opening count (opening = counted, no movements).</summary>
public sealed record SnapTill(int TillId, string Code, string Name, int Shifts, long? ImportedOpening);

public sealed record SnapOutlet(int Id, string Code, string Name, string Kind, IReadOnlyList<string> Tables);

public sealed record SnapMenuItem(int Id, string Code, string Name, string Category, long PricePaise, bool Veg, IReadOnlyList<int> OutletIds);

public sealed record SnapDepartment(string Code, string Name, int? LeaveCap);

public sealed record SnapCompany(long Id, string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City, IReadOnlyDictionary<string, long> Rates,
    long CreditLimitPaise);

public sealed record SnapSupplier(int Id, string Name, string Trade, string Phone);

public sealed record SnapStockItem(int Id, string Code, string Name, string Kind, string Unit, long CostPaise, int MinQty, int ReorderQty, int? StdX100, long? LaundryPaise,
    string? Supplier);

/// <summary>What the property holds today, read from the owning modules (planning input; loaded again inside the commit transaction).</summary>
public sealed record ImportSnapshot(
    DateOnly BusinessDate,
    bool AnyDayClosed,
    IReadOnlyList<SnapRoomType> RoomTypes,
    IReadOnlyList<SnapRoom> Rooms,
    IReadOnlyList<SnapTariff> Tariffs,
    IReadOnlyList<SnapSeason> Seasons,
    IReadOnlyList<SnapRole> Roles,
    IReadOnlyList<SnapUser> Users,
    IReadOnlyList<SnapLedger> Ledgers,
    DateOnly? OpeningAsOf,
    IReadOnlyList<SnapOpening> Opening,
    IReadOnlyList<SnapTill> Tills,
    IReadOnlySet<string> GuestKeys,
    IReadOnlyList<SnapOutlet>? PosOutlets = null,
    IReadOnlyList<SnapMenuItem>? PosMenu = null,
    IReadOnlyList<SnapDepartment>? Departments = null,
    IReadOnlyList<SnapCompany>? Companies = null,
    IReadOnlyList<SnapSupplier>? Suppliers = null,
    IReadOnlyList<SnapStockItem>? StockItems = null);

public enum ImportAction
{
    Create,
    Update,
    Unchanged,
    Remove,
}

/// <summary>Typed payload of a planned change (what the commit writes).</summary>
public abstract record ImportData;

public sealed record RoomTypeData(string Code, string Name, int MaxOccupancy, long RackPaise, int SortOrder, int? ExistingId) : ImportData;

public sealed record RoomData(string RoomNo, int TypeId, string TypeCode) : ImportData;

public sealed record TariffData(int TypeId, string TypeCode, DateOnly From, long RatePaise) : ImportData;

public sealed record SeasonData(string Name, DateOnly From, DateOnly To, int AdjBps, IReadOnlyList<int> TypeIds, int Priority) : ImportData;

public sealed record StaffData(string Login, string DisplayName, IReadOnlyList<string> Roles, string CredentialKind) : ImportData;

public sealed record LedgerData(string Code, string Name, string Group, string? HccAccount) : ImportData;

public sealed record OpeningData(string LedgerCode, long DebitPaise, long CreditPaise) : ImportData;

public sealed record TillData(string Code, string Name, long OpeningPaise, int? TillId) : ImportData;

public sealed record GuestData(GuestDraft Draft, string Key) : ImportData;

public sealed record OutletData(string Code, string Name, string Kind, IReadOnlyList<string> Tables) : ImportData;

public sealed record MenuItemData(string Code, string Name, string Category, long PricePaise, bool Veg, IReadOnlyList<int> OutletIds, int? ExistingId) : ImportData;

public sealed record DepartmentData(string Code, string Name, int? LeaveCap, bool Exists) : ImportData;

/// <summary>A company account; <see cref="Rates"/> = room type code → nightly paise (sorted); <see cref="LimitChanged"/> = the stored credit limit changes.</summary>
public sealed record CompanyData(string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City, IReadOnlyDictionary<string, long> Rates,
    long CreditLimitPaise, long? ExistingId, bool LimitChanged) : ImportData;

public sealed record SupplierData(string Name, string Trade, string Phone, int? ExistingId) : ImportData;

/// <summary>A stock item; an existing item (<see cref="ExistingId"/>) only has its levels (minimum, reorder, use per room-night) updated.</summary>
public sealed record StockItemData(string Code, string Name, string Kind, string Unit, long CostPaise, int MinQty, int ReorderQty, int? StdX100, long? LaundryPaise,
    string? Supplier, int? ExistingId) : ImportData;

public sealed record ImportPlanItem(int Row, ImportAction Action, string Key, string Detail, ImportData? Data);

public sealed record ImportPlan(
    ImportMaster Master,
    int Rows,
    IReadOnlyList<ImportRowError> Errors,
    IReadOnlyList<ImportRowError> Warnings,
    IReadOnlyList<ImportPlanItem> Items,
    long? DebitPaise,
    long? CreditPaise)
{
    public bool Valid => Errors.Count == 0;

    public ImportCounts Counts => new(
        Items.Count(i => i.Action == ImportAction.Create), Items.Count(i => i.Action == ImportAction.Update),
        Items.Count(i => i.Action == ImportAction.Unchanged), Items.Count(i => i.Action == ImportAction.Remove));

    public IEnumerable<ImportPlanItem> Changes => Items.Where(i => i.Action is ImportAction.Create or ImportAction.Update or ImportAction.Remove);
}

/// <summary>
/// Pure validation + preview of one import file against the property's current data. Every problem is a row-level error
/// (spreadsheet row, column, plain-English message); the commit refuses a plan with any error. Re-importing the same file plans
/// only "Unchanged" rows, so a second commit writes nothing new.
/// </summary>
public static partial class ImportPlanner
{
    public const int MaxListed = 500;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,19}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9/-]{0,9}$")]
    private static partial Regex RoomNoPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$")]
    private static partial Regex LedgerCodePattern();

    [GeneratedRegex(@"^[a-z0-9._-]{3,40}$")]
    private static partial Regex LoginPattern();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9-]{0,19}$")]
    private static partial Regex TillCodePattern();

    public static readonly IReadOnlyList<string> LedgerGroups = ["Asset", "Liability", "Equity", "Income", "Expense"];

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static string Rs(long paise) => new Paise(paise).ToString();

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Guest duplicate key: last 10 phone digits + lower-case name.</summary>
    public static string GuestKey(string? phone, string? name) => GuestRules.NormalisePhone(phone) + "|" + (name ?? string.Empty).Trim().ToLowerInvariant();

    private sealed class Sheet(CsvTable table, ImportMasterSpec spec)
    {
        public readonly List<ImportRowError> Errors = [];
        public readonly List<ImportRowError> Warnings = [];
        public readonly List<ImportPlanItem> Items = [];
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public CsvTable Table => table;

        public bool CheckHeaders()
        {
            var known = spec.Columns.ToDictionary(c => CsvParser.Key(c.Name), c => c.Name, StringComparer.Ordinal);
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var h = table.Headers[i];
                var k = CsvParser.Key(h);
                if (k.Length == 0) continue;
                if (ImportMasters.IsForbiddenHeader(k))
                {
                    Errors.Add(new(1, h, $"Column '{h}' is not allowed: files never carry passwords, PINs, setup codes or full ID numbers. Delete the column."));
                    continue;
                }

                if (!known.ContainsKey(k))
                {
                    Warnings.Add(new(1, h, $"Column '{h}' is not part of the {spec.Title.ToLowerInvariant()} template and was ignored."));
                    continue;
                }

                if (!_index.TryAdd(k, i)) Errors.Add(new(1, h, $"Column '{h}' appears twice."));
            }

            foreach (var c in spec.Columns.Where(c => c.Required && !_index.ContainsKey(CsvParser.Key(c.Name))))
                Errors.Add(new(1, c.Name, $"Required column '{c.Name}' is missing. Use the template headers."));
            if (spec.Master == ImportMaster.OpeningBalances && !_index.ContainsKey("debit") && !_index.ContainsKey("credit"))
                Errors.Add(new(1, "debit", "Add the 'debit' and 'credit' columns."));
            return Errors.Count == 0;
        }

        public string Cell(CsvRow r, string column) =>
            _index.TryGetValue(CsvParser.Key(column), out var i) && i < r.Cells.Count ? r.Cells[i].Trim() : string.Empty;

        public void Error(CsvRow r, string column, string message) => Errors.Add(new(r.Row, column, message));

        public void Warn(CsvRow r, string column, string message) => Warnings.Add(new(r.Row, column, message));

        public void Item(CsvRow r, ImportAction a, string key, string detail, ImportData? data) => Items.Add(new(r.Row, a, key, detail, data));

        public string? Required(CsvRow r, string column, int max, string what)
        {
            var v = Cell(r, column);
            if (v.Length == 0)
            {
                Error(r, column, $"Enter the {what}.");
                return null;
            }

            if (v.Length > max)
            {
                Error(r, column, $"The {what} is at most {max} characters.");
                return null;
            }

            return v;
        }

        public long? Money(CsvRow r, string column, bool required, bool positive)
        {
            var v = Cell(r, column);
            if (v.Length == 0)
            {
                if (required) Error(r, column, "Enter an amount in rupees, for example 4200 or 4200.50.");
                return required ? null : 0;
            }

            if (!ImportValues.TryRupees(v, out var p, out var rounded, out var err))
            {
                Error(r, column, err!);
                return null;
            }

            if (positive && p <= 0)
            {
                Error(r, column, "The amount must be more than ₹0.");
                return null;
            }

            if (rounded) Warn(r, column, $"'{v}' has more than 2 decimals; rounded to {Rs(p)}.");
            return p;
        }

        public DateOnly? Date(CsvRow r, string column, string what)
        {
            var v = Cell(r, column);
            if (ImportValues.TryDate(v, out var d)) return d;
            Error(r, column, v.Length == 0 ? $"Enter the {what} (YYYY-MM-DD)." : $"'{v}' is not a date. Use YYYY-MM-DD, for example 2026-11-01.");
            return null;
        }

        public ImportPlan Result(long? debit = null, long? credit = null) =>
            new(spec.Master, table.Rows.Count, Errors, Warnings, Items, debit, credit);
    }

    /// <summary>Plans a parsed file. <paramref name="asOf"/> is the opening-balance date (opening-balances only).</summary>
    public static ImportPlan Plan(ImportMaster master, CsvTable table, ImportSnapshot snap, DateOnly? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(snap);
        var spec = ImportMasters.Spec(master);
        var sheet = new Sheet(table, spec);
        if (spec.SetupStateOnly && snap.AnyDayClosed)
            sheet.Errors.Add(new(0, "", $"{spec.Title} can be imported only while HCC is being set up. The first business day is already closed, so post a journal or a cash entry instead."));
        if (!sheet.CheckHeaders()) return sheet.Result();
        if (table.Rows.Count == 0)
        {
            sheet.Errors.Add(new(0, "", "The file has a header row but no data rows."));
            return sheet.Result();
        }

        switch (master)
        {
            case ImportMaster.RoomTypes: RoomTypes(sheet, snap); break;
            case ImportMaster.Rooms: Rooms(sheet, snap); break;
            case ImportMaster.Tariffs: Tariffs(sheet, snap); break;
            case ImportMaster.Seasons: Seasons(sheet, snap); break;
            case ImportMaster.Staff: Staff(sheet, snap); break;
            case ImportMaster.Ledgers: Ledgers(sheet, snap); break;
            case ImportMaster.OpeningBalances: return Opening(sheet, snap, asOf);
            case ImportMaster.TillCash: Tills(sheet, snap); break;
            case ImportMaster.Guests: Guests(sheet, snap); break;
            case ImportMaster.PosOutlets: PosOutlets(sheet, snap); break;
            case ImportMaster.PosMenu: PosMenu(sheet, snap); break;
            case ImportMaster.Departments: Departments(sheet, snap); break;
            case ImportMaster.Companies: Companies(sheet, snap); break;
            case ImportMaster.Suppliers: Suppliers(sheet, snap); break;
            case ImportMaster.StockItems: StockItems(sheet, snap); break;
            default: throw new ArgumentOutOfRangeException(nameof(master));
        }

        return sheet.Result();
    }

    private static bool Duplicate(Sheet s, Dictionary<string, int> seen, CsvRow r, string key, string column)
    {
        if (seen.TryGetValue(key, out var first))
        {
            s.Error(r, column, $"Duplicate of row {I(first)}.");
            return true;
        }

        seen[key] = r.Row;
        return false;
    }

    private static SnapRoomType? Type(ImportSnapshot snap, string code) => snap.RoomTypes.FirstOrDefault(t => Ci.Equals(t.Code, code));

    private static void RoomTypes(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        var position = 0;
        foreach (var r in s.Table.Rows)
        {
            position++;
            var errs = s.Errors.Count;
            var code = s.Required(r, "code", 20, "room type code");
            if (code is not null && !CodePattern().IsMatch(code)) s.Error(r, "code", "A room type code uses letters, digits, '-' or '_' (up to 20).");
            var name = s.Required(r, "name", 60, "room type name");
            var occText = s.Cell(r, "maxOccupancy");
            if (!ImportValues.TryInt(occText, out var occ) || occ is < 1 or > 10) s.Error(r, "maxOccupancy", "Enter the maximum guests as a whole number from 1 to 10.");
            var rack = s.Money(r, "rackRate", true, true);
            var sort = position;
            var sortText = s.Cell(r, "sortOrder");
            if (sortText.Length > 0 && (!ImportValues.TryInt(sortText, out sort) || sort is < 0 or > 9999)) s.Error(r, "sortOrder", "The sort order is a whole number from 0 to 9999.");
            if (code is null || Duplicate(s, seen, r, code, "code") || s.Errors.Count > errs) continue;
            var existing = Type(snap, code);
            var data = new RoomTypeData(existing?.Code ?? code, name!, occ, rack!.Value, sort, existing?.Id);
            if (existing is null)
            {
                s.Item(r, ImportAction.Create, code, $"New room type {code} · {name} · up to {I(occ)} guests · rack {Rs(rack.Value)}", data);
                continue;
            }

            var changes = new List<string>();
            if (!string.Equals(existing.Name, name, StringComparison.Ordinal)) changes.Add($"name {existing.Name ?? "—"} → {name}");
            if (existing.MaxOccupancy != occ) changes.Add($"guests {I(existing.MaxOccupancy)} → {I(occ)}");
            if (existing.RackPaise != rack.Value) changes.Add($"rack {Rs(existing.RackPaise)} → {Rs(rack.Value)}");
            if (existing.SortOrder != sort) changes.Add($"order {I(existing.SortOrder)} → {I(sort)}");
            if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, existing.Code, "Already in HCC with these details", data);
            else s.Item(r, ImportAction.Update, existing.Code, string.Join(" · ", changes), data);
        }
    }

    private static void Rooms(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var no = s.Required(r, "roomNo", 10, "room number");
            if (no is not null && !RoomNoPattern().IsMatch(no)) s.Error(r, "roomNo", "A room number uses letters, digits, '-' or '/' (up to 10).");
            var typeCode = s.Required(r, "roomType", 20, "room type code");
            SnapRoomType? type = null;
            if (typeCode is not null && (type = Type(snap, typeCode)) is null)
                s.Error(r, "roomType", $"Room type {typeCode} is not in HCC yet. Import the room types file first.");
            if (no is null || Duplicate(s, seen, r, no, "roomNo") || s.Errors.Count > errs) continue;
            var existing = snap.Rooms.FirstOrDefault(x => Ci.Equals(x.RoomNo, no));
            var data = new RoomData(existing?.RoomNo ?? no, type!.Id, type.Code);
            if (existing is null) s.Item(r, ImportAction.Create, no, $"New room {no} · {type.Code}", data);
            else if (existing.TypeId == type.Id) s.Item(r, ImportAction.Unchanged, existing.RoomNo, $"Already in HCC as a {type.Code} room", data);
            else
            {
                var current = snap.RoomTypes.FirstOrDefault(t => t.Id == existing.TypeId)?.Code ?? "?";
                s.Error(r, "roomType", $"Room {existing.RoomNo} is already a {current} room. Change a room's type on the Rooms screen, not by import.");
            }
        }
    }

    private static void Tariffs(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var typeCode = s.Required(r, "roomType", 20, "room type code");
            SnapRoomType? type = null;
            if (typeCode is not null && (type = Type(snap, typeCode)) is null)
                s.Error(r, "roomType", $"Room type {typeCode} is not in HCC yet. Import the room types file first.");
            var from = s.Date(r, "effectiveFrom", "first night of the rate");
            if (from is { } f && f < snap.BusinessDate)
                s.Error(r, "effectiveFrom", $"A tariff starts on or after the business date ({ImportValues.Wire(snap.BusinessDate)}).");
            var rate = s.Money(r, "rate", true, true);
            if (s.Errors.Count > errs || type is null || from is null) continue;
            if (Duplicate(s, seen, r, type.Code + "|" + ImportValues.Wire(from.Value), "effectiveFrom")) continue;
            var key = $"{type.Code} from {ImportValues.Wire(from.Value)}";
            var data = new TariffData(type.Id, type.Code, from.Value, rate!.Value);
            var existing = snap.Tariffs.FirstOrDefault(t => t.TypeId == type.Id && t.From == from.Value);
            if (existing is null) s.Item(r, ImportAction.Create, key, $"BAR {Rs(rate.Value)} per night", data);
            else if (existing.RatePaise == rate.Value) s.Item(r, ImportAction.Unchanged, key, $"Already {Rs(rate.Value)}", data);
            else s.Item(r, ImportAction.Update, key, $"BAR {Rs(existing.RatePaise)} → {Rs(rate.Value)}", data);
        }
    }

    private static void Seasons(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var name = s.Required(r, "name", 80, "season name");
            if (name is { Length: < 2 }) s.Error(r, "name", "The season name needs at least 2 characters.");
            var from = s.Date(r, "from", "first night");
            var to = s.Date(r, "to", "last night");
            if (from is { } f && to is { } t)
            {
                if (t < f) s.Error(r, "to", "The season must end on or after its first night.");
                else if (t.DayNumber - f.DayNumber > 365) s.Error(r, "to", "A season can cover at most one year.");
            }

            var adjText = s.Cell(r, "adjustmentPercent");
            if (!ImportValues.TryPercentBps(adjText, out var adj) || adj is < -5000 or > 10000 || adj == 0)
                s.Error(r, "adjustmentPercent", "Enter the change in percent from -50 to +100 (not 0), for example +15 or -10.");
            var typeIds = new List<int>();
            var typeCodes = new List<string>();
            foreach (var code in ImportValues.List(s.Cell(r, "roomTypes")))
            {
                var type = Type(snap, code);
                if (type is null) s.Error(r, "roomTypes", $"Room type {code} is not in HCC yet.");
                else if (!typeIds.Contains(type.Id))
                {
                    typeIds.Add(type.Id);
                    typeCodes.Add(type.Code);
                }
            }

            var priority = 1;
            var pText = s.Cell(r, "priority");
            if (pText.Length > 0 && (!ImportValues.TryInt(pText, out priority) || priority is < 1 or > 99)) s.Error(r, "priority", "The priority is a whole number from 1 to 99.");
            if (name is null || Duplicate(s, seen, r, name, "name") || s.Errors.Count > errs) continue;
            typeIds.Sort();
            var data = new SeasonData(name, from!.Value, to!.Value, adj, typeIds, priority);
            var show = $"{ImportValues.Wire(data.From)} to {ImportValues.Wire(data.To)} · {(adj > 0 ? "+" : "")}{(adj / 100m).ToString("0.##", CultureInfo.InvariantCulture)}% · {(typeCodes.Count == 0 ? "all room types" : string.Join(", ", typeCodes))}";
            var existing = snap.Seasons.FirstOrDefault(x => Ci.Equals(x.Name, name));
            if (existing is null) s.Item(r, ImportAction.Create, name, "New season " + show, data);
            else if (existing.From == data.From && existing.To == data.To && existing.AdjBps == adj && existing.Priority == priority
                && existing.TypeIds.Order().SequenceEqual(typeIds)) s.Item(r, ImportAction.Unchanged, existing.Name, "Already in HCC: " + show, data);
            else s.Error(r, "name", $"A season named {existing.Name} already exists with different dates or adjustment. Change it on the Yield screen, or give this one another name.");
        }
    }

    private static void Staff(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var raw = s.Required(r, "loginName", 40, "sign-in name");
            var login = raw?.ToLowerInvariant();
            if (login is not null && !LoginPattern().IsMatch(login)) s.Error(r, "loginName", "The sign-in name must be 3–40 characters: letters, digits, dot, dash or underscore.");
            var display = s.Required(r, "displayName", 100, "display name");
            var roles = new List<SnapRole>();
            var list = ImportValues.List(s.Cell(r, "roles"));
            if (list.Count == 0) s.Error(r, "roles", "Give the user at least one role, for example REC or Reception.");
            foreach (var v in list)
            {
                var role = snap.Roles.FirstOrDefault(x => Ci.Equals(x.Code, v) || Ci.Equals(x.Name, v));
                if (role is null) s.Error(r, "roles", $"'{v}' is not a role. Use a code such as REC, CSH, MGR, ACC, SUP, HKP, MNT or TAD.");
                else if (role.Code == "OWN") s.Error(r, "roles", "The Owner role is not imported. Add another Owner in Access control.");
                else if (roles.Contains(role)) s.Error(r, "roles", $"Role {role.Code} is listed twice.");
                else roles.Add(role);
            }

            if (login is null || Duplicate(s, seen, r, login, "loginName") || s.Errors.Count > errs) continue;
            var codes = roles.Select(x => x.Code).Order(StringComparer.Ordinal).ToList();
            var kind = roles.Any(x => x.CredentialKind == "PASSWORD") ? "PASSWORD" : "PIN";
            var data = new StaffData(login, display!, codes, kind);
            var existing = snap.Users.FirstOrDefault(u => u.Login == login);
            if (existing is null)
                s.Item(r, ImportAction.Create, login, $"New user {display} · {string.Join(", ", codes)} · first {(kind == "PASSWORD" ? "password" : "PIN")} to be set by the Owner in Access control", data);
            else if (existing.DisplayName == display && existing.Roles.Order(StringComparer.Ordinal).SequenceEqual(codes))
                s.Item(r, ImportAction.Unchanged, login, "Already in HCC with these roles", data);
            else s.Error(r, "loginName", $"User {login} already exists with a different name or roles. Change users in Access control, not by import.");
        }
    }

    private static string? Group(string v) =>
        LedgerGroups.FirstOrDefault(g => Ci.Equals(g, v)) ?? (Ci.Equals(v, "Capital") ? "Equity" : Ci.Equals(v, "Liabilities") ? "Liability" : Ci.Equals(v, "Assets") ? "Asset" : null);

    private static void Ledgers(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "code", 20, "ledger code");
            if (code is not null && !LedgerCodePattern().IsMatch(code)) s.Error(r, "code", "A ledger code uses letters, digits, '.', '-' or '_' (up to 20).");
            else if (code is not null && ChartOfAccounts.Find(code) is { } own)
                s.Error(r, "code", $"{code} is HCC's own account '{own.Name}'. Use your own code, or leave the row out and use {code} directly in the opening balances.");
            var name = s.Required(r, "name", 100, "ledger name");
            var groupText = s.Cell(r, "group");
            var group = Group(groupText);
            if (group is null) s.Error(r, "group", "The group is Asset, Liability, Equity, Income or Expense.");
            var hcc = s.Cell(r, "hccAccount");
            string? account = null;
            if (hcc.Length > 0)
            {
                var acc = ChartOfAccounts.Find(hcc);
                if (acc is null || acc.Group == AccountGroup.Suspense) s.Error(r, "hccAccount", $"{hcc} is not an HCC account. Use a 4-digit code from the README list or leave it blank.");
                else if (group == "Equity") s.Error(r, "hccAccount", "Equity ledgers have no HCC account; leave it blank.");
                else if (group is not null && acc.Group.ToString() != group) s.Error(r, "hccAccount", $"{hcc} {acc.Name} is an {acc.Group} account, but this ledger is in group {group}.");
                else account = acc.Code;
            }

            if (code is null || Duplicate(s, seen, r, code, "code") || s.Errors.Count > errs) continue;
            var existing = snap.Ledgers.FirstOrDefault(l => Ci.Equals(l.Code, code));
            var data = new LedgerData(existing?.Code ?? code, name!, group!, account);
            var show = $"{name} · {group}{(account is null ? "" : " → " + account + " " + ChartOfAccounts.NameOf(account))}";
            if (existing is null) s.Item(r, ImportAction.Create, code, "New ledger " + show, data);
            else if (existing.Name == name && existing.Group == group && existing.HccAccount == account) s.Item(r, ImportAction.Unchanged, existing.Code, "Already in HCC: " + show, data);
            else s.Item(r, ImportAction.Update, existing.Code, $"{existing.Name} · {existing.Group}{(existing.HccAccount is null ? "" : " → " + existing.HccAccount)} → {show}", data);
        }
    }

    /// <summary>The name and group of a ledger code: an HCC account (not suspense) or an imported ledger; null when unknown.</summary>
    public static (string Code, string Name, string Group, string? HccAccount)? Ledger(ImportSnapshot snap, string code)
    {
        ArgumentNullException.ThrowIfNull(snap);
        if (ChartOfAccounts.Find(code) is { } a && a.Group != AccountGroup.Suspense) return (a.Code, a.Name, a.Group.ToString(), a.Code);
        return snap.Ledgers.FirstOrDefault(l => Ci.Equals(l.Code, code)) is { } l ? (l.Code, l.Name, l.Group, l.HccAccount) : null;
    }

    private static ImportPlan Opening(Sheet s, ImportSnapshot snap, DateOnly? asOf)
    {
        if (asOf is null) s.Errors.Add(new(0, "asOfDate", "Choose the date of the opening balances (the last day before HCC starts)."));
        else if (asOf.Value > snap.BusinessDate) s.Errors.Add(new(0, "asOfDate", $"The opening balances date cannot be after the business date ({ImportValues.Wire(snap.BusinessDate)})."));
        var seen = new Dictionary<string, int>(Ci);
        long debit = 0, credit = 0;
        var inFile = new HashSet<string>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "ledgerCode", 20, "ledger code");
            var ledger = code is null ? null : Ledger(snap, code);
            if (code is not null && ledger is null) s.Error(r, "ledgerCode", $"Ledger {code} is not an HCC account or an imported ledger. Import the ledgers file first.");
            var d = s.Money(r, "debit", false, false);
            var c = s.Money(r, "credit", false, false);
            if (d is { } dv && c is { } cv)
            {
                if (dv == 0 && cv == 0) s.Error(r, "debit", "Enter the balance as a debit or a credit (a zero balance can be left out).");
                else if (dv != 0 && cv != 0) s.Error(r, "credit", "Enter either a debit or a credit, not both.");
            }

            if (code is null || ledger is null || Duplicate(s, seen, r, ledger.Value.Code, "ledgerCode") || s.Errors.Count > errs) continue;
            var lc = ledger.Value.Code;
            inFile.Add(lc);
            debit += d!.Value;
            credit += c!.Value;
            var data = new OpeningData(lc, d.Value, c.Value);
            var show = d.Value != 0 ? "Dr " + Rs(d.Value) : "Cr " + Rs(c.Value);
            var existing = snap.Opening.FirstOrDefault(o => Ci.Equals(o.LedgerCode, lc));
            var key = $"{lc} {ledger.Value.Name}";
            if (existing is null) s.Item(r, ImportAction.Create, key, "Opening " + show, data);
            else if (existing.DebitPaise == d.Value && existing.CreditPaise == c.Value) s.Item(r, ImportAction.Unchanged, key, "Already " + show, data);
            else s.Item(r, ImportAction.Update, key, $"{(existing.DebitPaise != 0 ? "Dr " + Rs(existing.DebitPaise) : "Cr " + Rs(existing.CreditPaise))} → {show}", data);
        }

        foreach (var o in snap.Opening.Where(o => !inFile.Contains(o.LedgerCode)))
            s.Items.Add(new(0, ImportAction.Remove, o.LedgerCode, "Not in this file: its opening balance is removed (the file replaces all opening balances)", null));
        if (snap.OpeningAsOf is { } prev && asOf is { } now && prev != now)
            s.Items.Add(new(0, ImportAction.Update, "asOfDate", $"Opening balances date {ImportValues.Wire(prev)} → {ImportValues.Wire(now)}", null));
        if (s.Errors.Count == 0 && debit != credit)
            s.Errors.Add(new(0, debit > credit ? "credit" : "debit",
                $"The trial balance does not balance: total debits {Rs(debit)}, total credits {Rs(credit)}, difference {Rs(Math.Abs(debit - credit))}. Nothing can be imported until debits equal credits."));
        return s.Result(debit, credit);
    }

    private static void Tills(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "tillCode", 20, "till code")?.ToUpperInvariant();
            if (code is not null && !TillCodePattern().IsMatch(code)) s.Error(r, "tillCode", "A till code uses letters, digits and '-' only (up to 20).");
            var name = s.Required(r, "tillName", 100, "till name");
            var cash = s.Money(r, "openingCash", true, false);
            if (code is null || Duplicate(s, seen, r, code, "tillCode") || s.Errors.Count > errs) continue;
            var existing = snap.Tills.FirstOrDefault(t => Ci.Equals(t.Code, code));
            var data = new TillData(code, name!, cash!.Value, existing?.TillId);
            if (existing is null) s.Item(r, ImportAction.Create, code, $"New till {name} · opening cash {Rs(cash.Value)} (counted; the first shift opens with it)", data);
            else if (existing.Shifts == 0)
            {
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal)) s.Warn(r, "tillName", $"Till {code} is called {existing.Name} in HCC; the name is not changed.");
                s.Item(r, ImportAction.Update, code, $"Opening cash {Rs(cash.Value)} for the existing till {existing.Name}", data);
            }
            else if (existing.ImportedOpening == cash.Value) s.Item(r, ImportAction.Unchanged, code, $"Opening cash {Rs(cash.Value)} already recorded", data);
            else s.Error(r, "openingCash", $"Till {code} already has cash history in HCC. Correct its cash through a shift count, not by import.");
        }
    }

    [GeneratedRegex(@"^[A-Z0-9-]{1,20}$")]
    private static partial Regex OutletCodePattern();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{0,19}$")]
    private static partial Regex MenuCodePattern();

    private static void PosOutlets(Sheet s, ImportSnapshot snap)
    {
        var existingOutlets = snap.PosOutlets ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "outletCode", 20, "outlet code")?.ToUpperInvariant();
            if (code is not null && !OutletCodePattern().IsMatch(code)) s.Error(r, "outletCode", "An outlet code uses letters, digits and '-' only (up to 20).");
            var name = s.Required(r, "outletName", 100, "outlet name");
            var kindText = s.Cell(r, "kind").ToLowerInvariant();
            if (kindText is not ("table" or "room")) s.Error(r, "kind", "The kind is table (dining outlet) or room (room service).");
            var tables = ImportValues.List(s.Cell(r, "tables")).ToList();
            foreach (var t in tables.Where(t => t.Length > 10)) s.Error(r, "tables", $"Table name '{t}' is longer than 10 characters.");
            if (tables.Distinct(Ci).Count() != tables.Count) s.Error(r, "tables", "Table names must be unique within the outlet.");
            if (kindText == "table" && tables.Count == 0) s.Error(r, "tables", "A dining outlet needs at least one table (separate them with ;).");
            if (kindText == "room" && tables.Count > 0) s.Error(r, "tables", "A room-service outlet has no tables: leave the column blank.");
            if (code is null || Duplicate(s, seen, r, code, "outletCode") || s.Errors.Count > errs) continue;
            var data = new OutletData(code, name!, kindText, tables);
            var show = kindText == "room" ? "room service" : $"{I(tables.Count)} table(s): {string.Join(", ", tables)}";
            var existing = existingOutlets.FirstOrDefault(o => Ci.Equals(o.Code, code));
            if (existing is null) s.Item(r, ImportAction.Create, code, $"New outlet {name} · {show}", data);
            else if (string.Equals(existing.Name, name, StringComparison.Ordinal) && existing.Kind == kindText && existing.Tables.Order(Ci).SequenceEqual(tables.Order(Ci), Ci))
                s.Item(r, ImportAction.Unchanged, existing.Code, "Already in HCC: " + show, data);
            else s.Error(r, "outletCode", $"Outlet {existing.Code} already exists with a different name, kind or tables. Change it on the POS setup screen, or use another code.");
        }
    }

    private static bool TryVeg(string text, out bool veg)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "yes" or "y" or "true" or "1" or "veg" or "vegetarian": veg = true; return true;
            case "no" or "n" or "false" or "0" or "non-veg" or "nonveg" or "non veg" or "non-vegetarian": veg = false; return true;
            default: veg = false; return false;
        }
    }

    private static void PosMenu(Sheet s, ImportSnapshot snap)
    {
        var outlets = snap.PosOutlets ?? [];
        var items = snap.PosMenu ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "itemCode", 20, "item code")?.ToUpperInvariant();
            if (code is not null && !MenuCodePattern().IsMatch(code)) s.Error(r, "itemCode", "An item code uses letters, digits, '-' or '_' (up to 20).");
            var name = s.Required(r, "itemName", 100, "item name");
            var category = s.Required(r, "category", 60, "category");
            var price = s.Money(r, "price", true, false);
            var vegText = s.Cell(r, "veg");
            var veg = false;
            if (!TryVeg(vegText, out veg)) s.Error(r, "veg", "Enter yes (vegetarian) or no (non-vegetarian).");
            var ids = new List<int>();
            var codes = new List<string>();
            var listed = ImportValues.List(s.Cell(r, "outlets"));
            if (listed.Count == 0)
            {
                if (outlets.Count == 0) s.Error(r, "outlets", "No outlet is in HCC yet. Import the POS outlets file first.");
                foreach (var o in outlets)
                {
                    ids.Add(o.Id);
                    codes.Add(o.Code);
                }
            }

            foreach (var oc in listed)
            {
                var o = outlets.FirstOrDefault(x => Ci.Equals(x.Code, oc));
                if (o is null) s.Error(r, "outlets", $"Outlet {oc} is not in HCC yet. Import the POS outlets file first.");
                else if (!ids.Contains(o.Id))
                {
                    ids.Add(o.Id);
                    codes.Add(o.Code);
                }
            }

            if (code is null || Duplicate(s, seen, r, code, "itemCode") || s.Errors.Count > errs) continue;
            ids.Sort();
            var existing = items.FirstOrDefault(x => Ci.Equals(x.Code, code));
            var data = new MenuItemData(code, name!, category!, price!.Value, veg, ids, existing?.Id);
            var show = $"{name} · {category} · {Rs(price.Value)} · {(veg ? "veg" : "non-veg")} · {string.Join(", ", codes)}";
            if (existing is null) s.Item(r, ImportAction.Create, code, "New item " + show, data);
            else
            {
                var changes = new List<string>();
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal)) changes.Add($"name {existing.Name} → {name}");
                if (!string.Equals(existing.Category, category, StringComparison.Ordinal)) changes.Add($"category {existing.Category} → {category}");
                if (existing.PricePaise != price.Value) changes.Add($"price {Rs(existing.PricePaise)} → {Rs(price.Value)}");
                if (existing.Veg != veg) changes.Add(veg ? "now vegetarian" : "now non-vegetarian");
                if (!existing.OutletIds.Order().SequenceEqual(ids)) changes.Add("outlets " + string.Join(", ", codes));
                if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, existing.Code, "Already in HCC: " + show, data);
                else s.Item(r, ImportAction.Update, existing.Code, string.Join(" · ", changes), data);
            }
        }
    }

    // ── departments, companies, suppliers, stock items (A09-09) ──

    [GeneratedRegex(@"^[A-Z0-9]{2,10}$")]
    private static partial Regex DeptCodePattern();

    [GeneratedRegex(@"^[+\d][\d\s-]{6,}$")]
    private static partial Regex VendorPhonePattern();

    [GeneratedRegex(@"^[A-Z0-9]{2,6}-[A-Z0-9]{1,10}$")]
    private static partial Regex StockCodePattern();

    /// <summary>A blank optional cell is null; longer than <paramref name="max"/> is a row error.</summary>
    private static string? Optional(Sheet s, CsvRow r, string column, int max, string what)
    {
        var v = s.Cell(r, column);
        if (v.Length == 0) return null;
        if (v.Length <= max) return v;
        s.Error(r, column, $"The {what} is at most {I(max)} characters.");
        return null;
    }

    /// <summary>Runs a module's own field rule (which throws a validation error) and turns its message into a row error.</summary>
    private static string? Rule(Sheet s, CsvRow r, string column, Func<string?, string?> rule)
    {
        try
        {
            return rule(s.Cell(r, column));
        }
        catch (HccException e)
        {
            s.Error(r, column, e.Message);
            return null;
        }
    }

    private static int Quantity(Sheet s, CsvRow r, string column, string what)
    {
        var v = s.Cell(r, column);
        if (v.Length == 0) return 0;
        if (ImportValues.TryInt(v, out var q) && q is >= 0 and <= StockRules.MaxQty) return q;
        s.Error(r, column, $"The {what} is a whole number from 0 to {StockRules.MaxQty.ToString("N0", CultureInfo.InvariantCulture)}.");
        return 0;
    }

    private static void Departments(Sheet s, ImportSnapshot snap)
    {
        var existing = snap.Departments ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "code", 10, "department code")?.ToUpperInvariant();
            if (code is not null && !DeptCodePattern().IsMatch(code)) s.Error(r, "code", "A department code is 2 to 10 capital letters or digits, for example FO.");
            var name = s.Required(r, "name", 60, "department name");
            if (name is { Length: < 2 }) s.Error(r, "name", "The department name needs at least 2 characters.");
            int? cap = null;
            var capText = s.Cell(r, "leaveCap");
            if (capText.Length > 0)
            {
                if (ImportValues.TryInt(capText, out var c) && c is >= 1 and <= 20) cap = c;
                else s.Error(r, "leaveCap", "The leave limit is a whole number from 1 to 20 (leave it blank for the leave policy default).");
            }

            if (code is null || Duplicate(s, seen, r, code, "code") || s.Errors.Count > errs) continue;
            var have = existing.FirstOrDefault(d => Ci.Equals(d.Code, code));
            var data = new DepartmentData(code, name!, cap, have is not null);
            var show = name + (cap is { } x ? $" · at most {I(x)} on leave per day" : " · leave policy default");
            if (have is null)
            {
                s.Item(r, ImportAction.Create, code, "New department " + show, data);
                continue;
            }

            var changes = new List<string>();
            if (!string.Equals(have.Name, name, StringComparison.Ordinal)) changes.Add($"name {have.Name} → {name}");
            if (have.LeaveCap != cap) changes.Add($"leave limit {(have.LeaveCap is { } a ? I(a) : "default")} → {(cap is { } b ? I(b) : "default")}");
            if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, have.Code, "Already in HCC: " + show, data);
            else s.Item(r, ImportAction.Update, have.Code, string.Join(" · ", changes), data);
        }
    }

    /// <summary>"DLX=3800;STD=2600" → room type code (capitals) → nightly paise, sorted; every code must be a room type in HCC.</summary>
    private static SortedDictionary<string, long> Rates(Sheet s, CsvRow r, ImportSnapshot snap)
    {
        var rates = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var part in ImportValues.List(s.Cell(r, "negotiatedRates")))
        {
            var bits = part.Split(['=', ':'], 2, StringSplitOptions.TrimEntries);
            if (bits.Length != 2 || bits[0].Length == 0)
            {
                s.Error(r, "negotiatedRates", $"'{part}' is not a rate. Write room type=rupees, for example DLX=3800, separated by ;.");
                continue;
            }

            var type = Type(snap, bits[0]);
            if (type is null)
            {
                s.Error(r, "negotiatedRates", $"Room type {bits[0]} is not in HCC yet. Import the room types file first.");
                continue;
            }

            var code = type.Code.ToUpperInvariant();
            if (!ImportValues.TryRupees(bits[1], out var paise, out var rounded, out var err)) s.Error(r, "negotiatedRates", $"{code}: {err}");
            else if (paise is <= 0 or > 10_000_000) s.Error(r, "negotiatedRates", $"{code}: a negotiated nightly rate is more than ₹0 and at most ₹1,00,000.");
            else if (!rates.TryAdd(code, paise)) s.Error(r, "negotiatedRates", $"Room type {code} is listed twice.");
            else if (rounded) s.Warn(r, "negotiatedRates", $"{code}: '{bits[1]}' has more than 2 decimals; rounded to {Rs(paise)}.");
        }

        return rates;
    }

    private static string RatesText(IReadOnlyDictionary<string, long> rates) =>
        rates.Count == 0 ? "no negotiated rates" : string.Join(", ", rates.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} {Rs(x.Value)}"));

    private static void Companies(Sheet s, ImportSnapshot snap)
    {
        var existing = snap.Companies ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var name = s.Required(r, "name", 200, "company name");
            var gstin = Rule(s, r, "gstin", GroupText.Gstin);
            var contact = Optional(s, r, "contact", 120, "contact name");
            var phone = Rule(s, r, "phone", GroupText.Phone);
            var email = Rule(s, r, "email", GroupText.Email);
            var city = Optional(s, r, "city", 80, "city");
            var limit = s.Money(r, "creditLimit", false, false);
            var rates = Rates(s, r, snap);
            if (name is null || Duplicate(s, seen, r, name, "name") || s.Errors.Count > errs) continue;
            var have = existing.FirstOrDefault(c => Ci.Equals(c.Name, name));
            var show = $"{name}{(gstin is null ? "" : " · GSTIN " + gstin)}{(city is null ? "" : " · " + city)} · credit limit {Rs(limit!.Value)} · {RatesText(rates)}";
            var data = new CompanyData(name, gstin, contact, phone, email, city, rates, limit.Value, have?.Id, have is null ? limit.Value > 0 : have.CreditLimitPaise != limit.Value);
            if (have is null)
            {
                s.Item(r, ImportAction.Create, name, "New company account " + show, data);
                continue;
            }

            var changes = new List<string>();
            void Diff(string what, string? from, string? to)
            {
                if (!string.Equals(from, to, StringComparison.Ordinal)) changes.Add($"{what} {from ?? "—"} → {to ?? "—"}");
            }

            Diff("name", have.Name, name);
            Diff("GSTIN", have.Gstin, gstin);
            Diff("contact", have.Contact, contact);
            Diff("phone", have.Phone, phone);
            Diff("e-mail", have.Email, email);
            Diff("city", have.City, city);
            if (have.CreditLimitPaise != limit.Value) changes.Add($"credit limit {Rs(have.CreditLimitPaise)} → {Rs(limit.Value)}");
            var haveRates = new SortedDictionary<string, long>(have.Rates.ToDictionary(x => x.Key.ToUpperInvariant(), x => x.Value), StringComparer.Ordinal);
            if (!haveRates.SequenceEqual(rates)) changes.Add($"rates {RatesText(haveRates)} → {RatesText(rates)}");
            if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, have.Name, "Already in HCC: " + show, data);
            else s.Item(r, ImportAction.Update, have.Name, string.Join(" · ", changes), data);
        }
    }

    private static void Suppliers(Sheet s, ImportSnapshot snap)
    {
        var existing = snap.Suppliers ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var name = s.Required(r, "name", 120, "supplier name");
            if (name is { Length: < 3 }) s.Error(r, "name", "The supplier name needs at least 3 characters.");
            var phone = s.Required(r, "phone", 30, "phone number");
            if (phone is not null && !VendorPhonePattern().IsMatch(phone)) s.Error(r, "phone", "Enter a phone number: digits, spaces or '-', at least 7 (a leading + is allowed).");
            var trade = Optional(s, r, "trade", 60, "trade") ?? "General";
            if (name is null || Duplicate(s, seen, r, name, "name") || s.Errors.Count > errs) continue;
            var same = existing.Where(v => Ci.Equals(v.Name, name)).ToList();
            if (same.Count > 1)
            {
                s.Error(r, "name", $"HCC already has {I(same.Count)} suppliers named {name}. Change them on the Maintenance › Vendors & AMC screen, not by import.");
                continue;
            }

            var have = same.FirstOrDefault();
            var data = new SupplierData(name, trade, phone!, have?.Id);
            var show = $"{name} · {trade} · {phone}";
            if (have is null)
            {
                s.Item(r, ImportAction.Create, name, "New supplier " + show, data);
                continue;
            }

            var changes = new List<string>();
            if (!string.Equals(have.Name, name, StringComparison.Ordinal)) changes.Add($"name {have.Name} → {name}");
            if (!string.Equals(have.Trade, trade, StringComparison.Ordinal)) changes.Add($"trade {have.Trade} → {trade}");
            if (!string.Equals(have.Phone, phone, StringComparison.Ordinal)) changes.Add($"phone {have.Phone} → {phone}");
            if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, have.Name, "Already in HCC: " + show, data);
            else s.Item(r, ImportAction.Update, have.Name, string.Join(" · ", changes), data);
        }
    }

    private static string Std(int? x100) => x100 is { } v ? (v / 100m).ToString("0.##", CultureInfo.InvariantCulture) : "none";

    private static void StockItems(Sheet s, ImportSnapshot snap)
    {
        var existing = snap.StockItems ?? [];
        var seen = new Dictionary<string, int>(Ci);
        foreach (var r in s.Table.Rows)
        {
            var errs = s.Errors.Count;
            var code = s.Required(r, "code", 20, "item code")?.ToUpperInvariant();
            if (code is not null && !StockCodePattern().IsMatch(code)) s.Error(r, "code", "An item code looks like LIN-BS: 2 to 6 letters or digits, a dash, then 1 to 10 letters or digits.");
            var name = s.Required(r, "name", 120, "item name");
            if (name is { Length: < 2 }) s.Error(r, "name", "The item name needs at least 2 characters.");
            var kind = s.Cell(r, "kind").ToLowerInvariant();
            if (!StockRules.Kinds.Contains(kind)) s.Error(r, "kind", "The kind is linen, amenity or cleaning.");
            var unit = s.Required(r, "unit", 20, "unit");
            var costGiven = s.Cell(r, "unitCost").Length > 0;
            var cost = s.Money(r, "unitCost", false, false);
            if (cost > StockRules.MaxUnitCostPaise) s.Error(r, "unitCost", "The unit cost is at most ₹1,00,000.");
            var min = Quantity(s, r, "minQty", "minimum quantity");
            var reorder = Quantity(s, r, "reorderQty", "reorder quantity");
            int? std = null;
            var stdText = s.Cell(r, "usePerRoomNight");
            if (stdText.Length > 0)
            {
                if (decimal.TryParse(stdText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var u) && u is >= 0 and <= 100 && decimal.Round(u * 100m) == u * 100m)
                    std = (int)(u * 100m);
                else s.Error(r, "usePerRoomNight", "The use per occupied room-night is a number from 0 to 100 with up to 2 decimals, for example 1.5.");
            }

            long? laundry = null;
            if (s.Cell(r, "laundryRate").Length > 0)
            {
                laundry = s.Money(r, "laundryRate", false, false);
                if (laundry > StockRules.MaxUnitCostPaise) s.Error(r, "laundryRate", "The laundry rate is at most ₹1,00,000.");
                else if (kind != "linen") s.Error(r, "laundryRate", "A laundry rate applies to linen only; leave it blank for amenities and cleaning items.");
            }

            var supplier = Optional(s, r, "supplier", 120, "supplier name");
            if (code is null || Duplicate(s, seen, r, code, "code") || s.Errors.Count > errs) continue;
            var have = existing.FirstOrDefault(i => Ci.Equals(i.Code, code));
            var data = new StockItemData(code, name!, kind, unit!, cost!.Value, min, reorder, std, laundry, supplier, have?.Id);
            var levels = $"minimum {I(min)}, reorder {I(reorder)}, use per room-night {Std(std)}";
            if (have is null)
            {
                s.Item(r, ImportAction.Create, code, $"New item {name} · {kind} · {unit}{(cost.Value > 0 ? $" · {Rs(cost.Value)} per {unit}" : "")} · {levels}", data);
                continue;
            }

            if (!string.Equals(have.Name, name, StringComparison.Ordinal) || have.Kind != kind || !string.Equals(have.Unit, unit, StringComparison.Ordinal))
            {
                s.Error(r, "code", $"Item {have.Code} is already in HCC as {have.Name} ({have.Kind}, {have.Unit}). Change it on the Stock screen, or use another code.");
                continue;
            }

            if (costGiven && have.CostPaise != cost.Value)
                s.Warn(r, "unitCost", $"Item {have.Code} keeps its unit cost {Rs(have.CostPaise)} (goods receipts keep the average cost); {Rs(cost.Value)} from the file is not used.");
            if (laundry is not null && have.LaundryPaise != laundry)
                s.Warn(r, "laundryRate", $"Item {have.Code} keeps its laundry rate {(have.LaundryPaise is { } lp ? Rs(lp) : "none")}; change it on the Stock screen.");
            if (supplier is not null && !string.Equals(have.Supplier, supplier, StringComparison.Ordinal))
                s.Warn(r, "supplier", $"Item {have.Code} keeps its supplier {have.Supplier ?? "—"}; change it on the Stock screen.");
            var changes = new List<string>();
            if (have.MinQty != min) changes.Add($"minimum {I(have.MinQty)} → {I(min)}");
            if (have.ReorderQty != reorder) changes.Add($"reorder {I(have.ReorderQty)} → {I(reorder)}");
            if (have.StdX100 != std) changes.Add($"use per room-night {Std(have.StdX100)} → {Std(std)}");
            if (changes.Count == 0) s.Item(r, ImportAction.Unchanged, have.Code, "Already in HCC: " + levels, data);
            else s.Item(r, ImportAction.Update, have.Code, string.Join(" · ", changes), data);
        }
    }

    private static readonly Dictionary<string, string> GuestColumns = new(StringComparer.Ordinal)
    {
        ["name"] = "name", ["phone"] = "phone", ["email"] = "email", ["address"] = "address", ["nationality"] = "nationality", ["company"] = "company", ["gstin"] = "gstin",
    };

    private static void Guests(Sheet s, ImportSnapshot snap)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in s.Table.Rows)
        {
            var draft = new GuestDraft(s.Cell(r, "name"), s.Cell(r, "phone"), s.Cell(r, "email"), s.Cell(r, "address"), s.Cell(r, "nationality"),
                Company: s.Cell(r, "company"), Gstin: s.Cell(r, "gstin"), Notes: s.Cell(r, "notes"));
            var errors = GuestRules.Validate(draft, false);
            foreach (var (field, message) in errors) s.Error(r, GuestColumns.TryGetValue(field, out var col) ? col : field, message);
            if ((draft.Notes ?? string.Empty).Length > 1000) s.Error(r, "notes", "Notes are at most 1000 characters.");
            if (errors.Count > 0 || (draft.Notes ?? string.Empty).Length > 1000) continue;
            var clean = GuestRules.Clean(draft);
            var key = GuestKey(clean.Phone, clean.Name);
            if (Duplicate(s, seen, r, key, "phone")) continue;
            var label = $"{clean.Name} · {GuestRules.MaskPhone(clean.Phone)}";
            if (snap.GuestKeys.Contains(key)) s.Item(r, ImportAction.Unchanged, label, "A profile with this name and phone is already in HCC; it is not changed", new GuestData(clean, key));
            else s.Item(r, ImportAction.Create, label, "New guest profile", new GuestData(clean, key));
        }
    }
}
