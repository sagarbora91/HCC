using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Domain.RoomMasters;

namespace Hcc.Domain.Import;

/// <summary>How a cell of a list master is read (and written back by the export).</summary>
public enum ListKind
{
    /// <summary>Free text (at most <see cref="ListField.Max"/> characters, default 200).</summary>
    Text,

    /// <summary>Whole number between <see cref="ListField.Min"/> and <see cref="ListField.Hi"/>.</summary>
    Int,

    /// <summary>yes / no (blank = <see cref="ListField.Default"/>).</summary>
    Bool,

    /// <summary>One of <see cref="ListField.Options"/> (case-insensitive).</summary>
    Pick,

    /// <summary>A percentage such as 12.5; <see cref="ListField.Min"/> / <see cref="ListField.Hi"/> are basis points.</summary>
    Pct,

    /// <summary>Rupees with up to 2 decimals (dated amounts count from the business date).</summary>
    Rupees,

    /// <summary>24-hour time HH:mm.</summary>
    Time,

    /// <summary>A date YYYY-MM-DD.</summary>
    Date,

    /// <summary>Several values separated by ';' — from <see cref="ListField.Options"/> or the codes of <see cref="ListField.Ref"/>.</summary>
    Set,

    /// <summary>Several free-text lines separated by ';' (order kept), between <see cref="ListField.Min"/> and <see cref="ListField.Hi"/> items.</summary>
    Lines,

    /// <summary>One code of another list (<see cref="ListField.Ref"/>).</summary>
    Ref,

    /// <summary>A number of days ≥ <see cref="ListField.Min"/>, halves (up to 2 decimals) allowed — SCC leave entitlements (DEC-116).</summary>
    Days,
}

/// <summary>One column of a list master (after code and name). <see cref="Key"/> = the prototype FieldSpec key (the CSV header).</summary>
public sealed record ListField(string Key, ListKind Kind, bool Required, string Meaning, string Example)
{
    /// <summary>Plain words used in messages ("approval above pct").</summary>
    public string Label { get; init; } = ListLabels.Of(Key);

    public int Max { get; init; }

    public long Min { get; init; }

    public long Hi { get; init; } = long.MaxValue;

    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Another list whose codes this column names: a list wire (floors, bank-accounts …) or room-types, rooms, departments, pos-outlets, users.</summary>
    public string? Ref { get; init; }

    public string Default { get; init; } = "";

    /// <summary>A dated amount (price, rate): the amount in force on the business date; blank keeps what is there, a new amount starts from the business date.</summary>
    public bool Dated { get; init; }

    /// <summary>Money field of a master that is otherwise operational (DEC-058): the master's commit is Owner-only.</summary>
    public bool Money { get; init; }
}

/// <summary>A cell problem of a row check: column + plain-English message.</summary>
public sealed record ListProblem(string Column, string Message);

/// <summary>
/// One list master (prototype masters framework): code, name, <see cref="Fields"/>, sortOrder, active. The CSV headers are exactly
/// code, name, the field keys in order, sortOrder, active — the prototype FieldSpec keys — so a master exported by the prototype
/// (or by <see cref="ImportLists.ToCsv"/>) is a valid import file. A test keeps templates, specs and the prototype in step.
/// </summary>
public sealed record ListDef(ImportMaster Master, string Wire, string Proto, string Title, string Singular, bool OwnerOnlyCommit, string DependsOn, IReadOnlyList<ListField> Fields)
{
    public string CodeMeaning { get; init; } = "Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import.";

    public string CodeExample { get; init; } = "";

    public string NameExample { get; init; } = "";

    public int CodeMax { get; init; } = 20;

    /// <summary>The code is a date (holidays): any date format is accepted and stored as YYYY-MM-DD.</summary>
    public bool CodeIsDate { get; init; }

    /// <summary>Row rules beyond the cell checks (module rules, cross-field rules).</summary>
    public Action<ListRowCheck>? Check { get; init; }

    /// <summary>Rules across the final list (existing rows overlaid with the file): unique defaults, one active per key …</summary>
    public Action<ListFileCheck>? FileCheck { get; init; }

    /// <summary>Fields of an existing row that a file may not change (the row is used in documents): a difference is a row error.</summary>
    public IReadOnlyList<string> Frozen { get; init; } = [];

    public string FrozenMessage { get; init; } = "";

    public IReadOnlyList<string> Columns => ["code", "name", .. Fields.Select(f => f.Key), "sortOrder", "active"];

    public ListField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);
}

/// <summary>An existing row of a list as the owning module stores it (values in the canonical text of its field kinds).</summary>
public sealed record ListSnapRow(long Id, string Code, string Name, int SortOrder, bool Active, IReadOnlyDictionary<string, string> Values);

/// <summary>Everything a row check may look at.</summary>
public sealed class ListRowCheck(ListDef def, IReadOnlyDictionary<string, string> values, ListSnapRow? existing, string code, string name, ImportSnapshot snap, bool active)
{
    private readonly List<ListProblem> _problems = [];

    public ListDef Def => def;

    public string Code => code;

    public string Name => name;

    public bool Active => active;

    /// <summary>Canonical cell values by field key.</summary>
    public IReadOnlyDictionary<string, string> V => values;

    public string this[string key] => values.TryGetValue(key, out var v) ? v : string.Empty;

    public bool Has(string key) => this[key].Length > 0;

    public ListSnapRow? Existing => existing;

    public ImportSnapshot Snap => snap;

    public IReadOnlyList<ListProblem> Problems => _problems;

    public void Error(string column, string message) => _problems.Add(new ListProblem(column, message));

    /// <summary>Runs a module rule that throws a validation error; its message becomes a row error on the named column (or the rule's own field).</summary>
    public void Rule(string defaultColumn, Action rule)
    {
        try
        {
            rule();
        }
        catch (Errors.HccException e)
        {
            var field = e.Details is { } d && d.TryGetValue("field", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String ? f.GetString() : null;
            Error(field is not null && def.Columns.Contains(field) ? field : defaultColumn, e.Message);
        }
    }

    public IReadOnlyList<ListSnapRow> Rows(string wire) => snap.Lists is { } l && l.TryGetValue(wire, out var rows) ? rows : [];
}

/// <summary>One row of the final list (what the list will hold after the file is committed) for <see cref="ListFileCheck"/>.</summary>
public sealed record ListFinalRow(int? FileRow, string Code, string Name, bool Active, IReadOnlyDictionary<string, string> Values)
{
    public string this[string key] => Values.TryGetValue(key, out var v) ? v : string.Empty;
}

public sealed class ListFileCheck(ListDef def, IReadOnlyList<ListFinalRow> final, ImportSnapshot snap)
{
    private readonly List<(int Row, ListProblem Problem)> _problems = [];

    public ListDef Def => def;

    public IReadOnlyList<ListFinalRow> Final => final;

    public ImportSnapshot Snap => snap;

    public IReadOnlyList<(int Row, ListProblem Problem)> Problems => _problems;

    /// <summary>A problem on a file row (rows only in HCC are never blamed).</summary>
    public void Error(ListFinalRow row, string column, string message)
    {
        if (row.FileRow is { } r) _problems.Add((r, new ListProblem(column, message)));
    }
}

internal static class ListLabels
{
    private static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        ["approvalAbovePct"] = "approval-above percentage", ["maxPct"] = "maximum discount", ["gst"] = "GST treatment", ["gstClass"] = "GST class",
        ["gstProfile"] = "GST profile", ["commissionBps"] = "commission", ["cancelChargeBps"] = "cancellation charge", ["noShowChargeBps"] = "no-show charge",
        ["depositBps"] = "advance", ["slaHours"] = "SLA hours", ["tds"] = "TDS", ["pan"] = "PAN", ["gstin"] = "GSTIN", ["ifsc"] = "IFSC", ["defaultPayRule"] = "default pay rule",
        ["accountLast4"] = "account number (last 4)", ["tableNo"] = "table number", ["kotStation"] = "KOT station", ["surchargePct"] = "surcharge", ["sortOrder"] = "sort order",
    };

    public static string Of(string key)
    {
        if (Fixed.TryGetValue(key, out var f)) return f;
        var sb = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}

/// <summary>
/// The list masters of the masters wave (MA-8): every master that is a plain list of rows with a code. Definitions mirror the prototype
/// FieldSpecs (03 Prototype/src/domain/streams/*.ts) and the owning server modules' rules, which the row checks call where they exist.
/// Canonical cell text: Bool yes/no, Pct percent without % (12.5), Rupees 0.00, Time HH:mm, Date YYYY-MM-DD, Set and Lines joined by ';'.
/// </summary>
public static partial class ImportLists
{
    public const string UserRef = "users";
    public const string RoomRef = "rooms";
    public const string RoomTypeRef = "room-types";
    public const string DepartmentRef = "departments";
    public const string OutletRef = "pos-outlets";

    private static ListField Text(string key, bool req, string meaning, string example, int max = 200) => new(key, ListKind.Text, req, meaning, example) { Max = max };

    private static ListField Int(string key, bool req, string meaning, string example, long min, long hi) => new(key, ListKind.Int, req, meaning, example) { Min = min, Hi = hi };

    private static ListField Flag(string key, string meaning, string example, string dflt = "no") => new(key, ListKind.Bool, false, meaning, example) { Default = dflt };

    private static ListField Pick(string key, bool req, string meaning, string example, params string[] options) => new(key, ListKind.Pick, req, meaning, example) { Options = options };

    private static ListField Pct(string key, bool req, string meaning, string example, long minBps, long maxBps) => new(key, ListKind.Pct, req, meaning, example) { Min = minBps, Hi = maxBps };

    private static ListField RupeesDated(string key, bool req, string meaning, string example) => new(key, ListKind.Rupees, req, meaning, example) { Dated = true };

    private static ListField Rupees(string key, bool req, string meaning, string example) => new(key, ListKind.Rupees, req, meaning, example);

    private static ListField Set(string key, bool req, string meaning, string example, params string[] options) => new(key, ListKind.Set, req, meaning, example) { Options = options };

    private static ListField RefSet(string key, bool req, string meaning, string example, string reference) => new(key, ListKind.Set, req, meaning, example) { Ref = reference };

    private static ListField RefOne(string key, bool req, string meaning, string example, string reference) => new(key, ListKind.Ref, req, meaning, example) { Ref = reference };

    private const string MoneyNote = "Amounts in rupees before GST, up to 2 decimals.";
    private const string DatedNote = " The amount in force from the business date is imported; an amount already in effect is never rewritten — a different amount starts from the business date and the old one stays in the history.";

    public static readonly IReadOnlyList<ListDef> All =
    [
        new(ImportMaster.Floors, "floors", "floors", "Floors", "floor", false, "—", [])
        {
            CodeMeaning = "Floor code F1, F2 … (the production room board maps room 101 to F1). Capital letters, digits or dash, up to 20 characters.", CodeExample = "F1", NameExample = "Ground floor",
        },
        new(ImportMaster.Amenities, "amenities", "amenities", "Room amenities", "amenity", false, "—",
        [
            Pick("icon", false, "Icon shown next to the amenity (blank = none): " + string.Join(", ", RoomMastersRules.Icons) + ".", "wifi", [.. RoomMastersRules.Icons]),
            Flag("showOnWebsite", "yes to list it on the booking website (blank = yes).", "yes", "yes"),
        ])
        {
            CodeExample = "WIFI", NameExample = "Free Wi-Fi",
            Check = c => c.Rule("icon", () => RoomMastersRules.ValidateIcon(c["icon"].Length == 0 ? null : c["icon"])),
        },
        new(ImportMaster.OccupancyCharges, "occupancy-charges", "occupancyCharges", "Extra-person charges (occupancy)", "occupancy charge", true, "room-types",
        [
            RefOne("roomType", true, "Code of the room type these charges belong to (one active row per room type).", "DLX", RoomTypeRef),
            RupeesDated("extraAdult", false, "Extra adult per night. " + MoneyNote + DatedNote, "800.00"),
            RupeesDated("extraChild", false, "Extra child per night. Same rules as extraAdult.", "400.00"),
            RupeesDated("extraBed", false, "Extra bed per night. Same rules as extraAdult.", "600.00"),
            Int("childAgeFrom", false, "Youngest age (years, 0 to 17) charged as a child; blank = no lower limit.", "6", 0, 17),
            Int("childAgeTo", false, "Oldest age (years, 0 to 17) charged as a child; blank = no upper limit.", "12", 0, 17),
        ])
        {
            CodeExample = "OC-DLX", NameExample = "Deluxe extra person",
            Check = c => c.Rule("childAgeFrom", () => RoomMastersRules.ValidateAges(IntOrNull(c["childAgeFrom"]), IntOrNull(c["childAgeTo"]))),
            FileCheck = f => OnePerKey(f, r => r["roomType"], "roomType", "room type", "Another active extra-person charge row already covers room type {0}."),
        },
        new(ImportMaster.MealPlans, "meal-plans", "mealPlans", "Meal plans", "meal plan", true, "room-types",
        [
            Set("meals", false, "Meals included, B (breakfast), L (lunch), D (dinner) separated by ; — blank = room only.", "B;D", "B", "L", "D"),
            RupeesDated("addOn", false, "Add-on per guest per night. " + MoneyNote + DatedNote, "750.00"),
            Pick("gst", true, "GST treatment: bundled (with the room) or separate (restaurant line). Illustrative — CA to confirm.", "separate", "bundled", "separate"),
            RefSet("roomTypes", false, "Room type codes the plan is offered for, separated by ; (blank = every room type).", "DLX;STE", RoomTypeRef),
        ])
        {
            CodeExample = "MAP", NameExample = "Breakfast and dinner (MAP)",
        },
        new(ImportMaster.Extras, "extras", "extras", "Extras catalogue (charges)", "extra", true, "—",
        [
            Pick("category", true, "transport, laundry, fnb, room or misc.", "laundry", "transport", "laundry", "fnb", "room", "misc"),
            RupeesDated("price", false, "Default price. " + MoneyNote + DatedNote, "500.00"),
            Pick("gst", true, "GST class: food, laundry, banquet or other (the GST master holds the rates).", "laundry", "food", "laundry", "banquet", "other"),
            Pick("unit", true, "each, piece, bag, night, trip, hour or person.", "bag", "each", "piece", "bag", "night", "trip", "hour", "person"),
            Pick("department", false, "frontdesk, housekeeping, laundry, restaurant, transport, banquet or maintenance (blank = none).", "laundry",
                "frontdesk", "housekeeping", "laundry", "restaurant", "transport", "banquet", "maintenance"),
        ])
        {
            CodeExample = "LAUNDRY", NameExample = "Laundry",
        },
        new(ImportMaster.DiscountReasons, "discount-reasons", "discountReasons", "Discount reasons", "discount reason", true, "—",
        [
            Pct("maxPct", true, "Largest discount allowed with this reason, percent of the folio's net charges (0.01 to 100).", "25", 1, 10000),
            Pct("approvalAbovePct", false, "Above this percent a second person must approve (blank = never; must be below the maximum).", "10", 0, 10000),
            Set("roles", false, "Roles that may use the reason, Owner, Manager, Reception separated by ; (blank = every role that may discount).", "Owner;Manager", "Owner", "Manager", "Reception"),
        ])
        {
            CodeExample = "SERVICE", NameExample = "Service recovery",
            Check = c => c.Rule("approvalAbovePct", () => Catalogue.CatalogueRules.DiscountLimits(BpsOf(c["maxPct"]) ?? 0, BpsOf(c["approvalAbovePct"]))),
        },
        new(ImportMaster.PaymentMethods, "payment-methods", "paymentMethods", "Payment methods", "payment method", true, "bank-accounts",
        [
            Pick("kind", true, "cash, upi, card, bank, wallet or credit.", "upi", "cash", "upi", "card", "bank", "wallet", "credit"),
            RefOne("settlesTo", false, "Code of the bank account or till the money lands in (cash → a till, credit → blank, the rest → a bank account).", "HDFC", "bank-accounts"),
            Flag("needsReference", "yes when a reference number (UPI id, card slip) must be typed.", "yes"),
            Set("usableAt", false, "Where it may be taken: frontdesk, pos, expenses separated by ;.", "frontdesk;pos", "frontdesk", "pos", "expenses"),
            Pct("surchargePct", false, "Surcharge percent added to the bill (0 to 10; blank = none).", "0", 0, 1000) with { Default = "0" },
            Text("ledger", false, "4-digit ledger of the chart of accounts the method posts to (an asset or liability account; blank = by kind).", "1010", 4),
        ])
        {
            CodeExample = "UPI", NameExample = "UPI",
            Check = PaymentMethodCheck,
        },
        new(ImportMaster.BankAccounts, "bank-accounts", "bankAccounts", "Bank accounts and tills", "account", true, "—",
        [
            Pick("kind", true, "till (a cash drawer) or bank.", "bank", "till", "bank"),
            Text("bank", false, "Bank name (required for kind bank; blank for a till).", "HDFC Bank", 60),
            Text("accountLast4", false, "Last 4 digits of the account number only — HCC never stores more.", "4821", 4),
            Text("ifsc", false, "IFSC code, for example HDFC0001234.", "HDFC0001234", 11),
            Rupees("openingBalance", false, "Opening balance. Setup-only: it is not changed once records settle through the account (blank = 0).", "250000.00") with { Default = "0.00" },
            Set("defaultFor", false, "Method kinds this account is the default for: upi, card, bank, wallet separated by ; (one default per kind).", "upi;card", "upi", "card", "bank", "wallet"),
        ])
        {
            CodeExample = "HDFC", NameExample = "HDFC current account",
            Check = BankAccountCheck,
            FileCheck = BankDefaultsCheck,
        },
        new(ImportMaster.ExpenseCategories, "expense-categories", "expenseCategories", "Expense categories", "expense category", true, "—",
        [
            Text("ledger", true, "4-digit expense ledger of the chart of accounts the category posts to (5000 to 5900).", "5000", 4),
            Flag("needsBill", "yes when a bill / receipt photo must be attached.", "yes"),
            Rupees("approvalLimit", false, "Manager approval limit for this category in rupees (blank = the general limit applies).", "5000.00"),
            // ACC-2 acc2-purchase (contract §3; DEC-143, DEC-145; rulebook C10).
            Flag("itcBlocked", "yes when the GST on these bills can never be claimed (CGST s.17(5): food, catering, health, club, vehicles ≤ 13 seats, construction…); the ITC switch then stays off (DEC-143).", "no"),
            Flag("rcm", "yes for a reverse-charge category (security agency, goods transport, advocate, hired vehicle — DEC-145): the bill carries no GST; HCC posts the GST to 2140.", "no"),
            Pct("rcmBps", false, "Reverse-charge GST percent without % (e.g. 18; required when rcm is yes).", "18", 1, 2800),
            Flag("capital", "yes for a fixed-asset category: the ledger is then a fixed-asset block 1500 to 1540 and each bill enters the asset register.", "no"),
            // ACC-2 integration (ruling 10): goods transport — the cash-payment limit is ₹35,000 a day instead of ₹10,000.
            Flag("transport", "yes for hire of goods carriages: cash paid to one transporter in a day may reach ₹35,000 (not ₹10,000) before the income-tax deduction is lost.", "no"),
        ])
        {
            CodeExample = "SUPPLIES", NameExample = "Supplies",
            Check = ExpenseCategoryCheck,
        },
        new(ImportMaster.ExpenseVendors, "expense-vendors", "expenseVendors", "Expense vendors", "expense vendor", true, "—",
        [
            Pick("type", true, "supplier, utility, laundry or other.", "supplier", "supplier", "utility", "laundry", "other"),
            Text("gstin", false, "GSTIN, 15 characters (blank = unregistered).", "27ABCDE1234F1Z5", 15),
            Flag("tds", "yes when TDS is deducted from the vendor's bills. A PAN is an ID number and is never put in a file: type it on the vendor's row in HCC first (Master configuration › Expense vendors); a vendor without a PAN there cannot be set to yes.", "no"),
            Text("state", false, "Two-letter GST state code (MH = the hotel's state, CGST + SGST bills; any other state = IGST bills). Blank = MH.", "MH", 2), // ACC-2
            Pick("tdsSection", false, "rent, contractor, professional or commission — the TDS rate-table row (required when tds is yes).", "contractor", "rent", "contractor", "professional", "commission"),
            // ACC-2 integration (ruling 9): the TDS rate column follows the payee.
            Pick("payeeType", false, "individual (individual / HUF) or other — the TDS rate column (blank = from the PAN in HCC, else other).", "other", "individual", "other"),
            Text("contact", false, "Contact person and phone, up to 100 characters.", "Mr Patil 9822000000", 100),
            Text("terms", false, "Payment terms, up to 60 characters.", "30 days", 60),
        ])
        {
            CodeExample = "VND-LAUNDRY", NameExample = "Shree Laundry Services (fictional)",
            Check = ExpenseVendorCheck,
        },
        new(ImportMaster.MenuCategories, "menu-categories", "menuCategories", "Menu categories (POS)", "menu category", false, "—", [])
        {
            CodeExample = "MAIN", NameExample = "Main course",
        },
        new(ImportMaster.PosTables, "pos-tables", "posTables", "Restaurant tables and counters (POS)", "table", false, "pos-outlets",
        [
            RefOne("outlet", true, "Code of the dining outlet the table belongs to.", "REST", OutletRef),
            Text("tableNo", true, "Table or counter number as the waiter types it, up to 10 characters (one active row per outlet and number).", "T1", 10),
            Int("seats", false, "Seats, 1 to 60 (blank = not recorded).", "4", 1, 60),
            Text("section", false, "Section or area, up to 40 characters.", "Terrace", 40),
            Flag("counter", "yes for a counter / bar seat rather than a table.", "no"),
        ])
        {
            CodeExample = "REST-T1", NameExample = "Restaurant T1",
            FileCheck = f => OnePerKey(f, r => r["outlet"] + "/" + r["tableNo"].ToUpperInvariant(), "tableNo", "table", "Table {0} is already an active row (a table number is used once per outlet)."),
        },
        new(ImportMaster.BookingSources, "booking-sources", "bookingSources", "Booking sources and segments", "booking source", true, "—",
        [
            Pick("segment", true, "DIRECT, OTA, CORPORATE, AGENT, GROUP or OTHER.", "OTA", "DIRECT", "OTA", "CORPORATE", "AGENT", "GROUP", "OTHER"),
            Text("channel", false, "OTA channel or company / agent name (only for segments OTA, CORPORATE, AGENT).", "Booking.com", 60),
            new ListField("commissionBps", ListKind.Pct, false, "Commission percent paid on bookings (0 to 50; blank = 0). Owner-only (money).", "15") { Min = 0, Hi = 5000, Money = true, Default = "0" },
        ])
        {
            CodeExample = "OTA-BKG", NameExample = "Booking.com",
            Check = c => c.Rule("segment", () => FrontDeskMasters.FrontDeskMastersRules.BookingSource(c["segment"], c["channel"].Length == 0 ? null : c["channel"], BpsOf(c["commissionBps"]) ?? 0)),
            FileCheck = f => OnePerKey(f, r => r["channel"].Length == 0 ? "" : r["segment"] + "/" + r["channel"].ToUpperInvariant(), "channel", "booking source", "{0} already stands for another active booking source."),
        },
        new(ImportMaster.IdTypes, "id-types", "idTypes", "Guest ID types", "ID type", false, "—",
        [
            Flag("accepted", "yes when the hotel accepts this ID at check-in (blank = yes).", "yes", "yes"),
            Flag("needsPhoto", "yes when a photo of the ID is suggested (optional; DEC-193: a photo is never required).", "no"),
            Flag("foreignerOnly", "yes when only foreign nationals use it (passport + visa).", "no"),
        ])
        {
            CodeExample = "AADHAAR", NameExample = "Aadhaar",
        },
        new(ImportMaster.NumberSeries, "number-series", "numberSeries", "Document numbering series", "numbering series", true, "—",
        [
            Pick("document", true, "INV tax invoice, BOS bill of supply, CRN credit note, RCP receipt, KOT kitchen ticket, REG registration card, RST restaurant tax invoice.", "INV", "INV", "BOS", "CRN", "RCP", "KOT", "REG", "RST"), // RST: MA-11 (DEC-MA-11d) made the restaurant series a row of this master
            Text("prefix", true, "Printed prefix, 1 to 8 capital letters, digits or dash (GST documents: at most 6).", "HCC", 8),
            Int("start", true, "First number of the series, 1 to 9,999,999.", "1", 1, 9_999_999),
            Int("padding", true, "Digits the number is padded to, 1 to 10 (GST documents: prefix + 6 + digits is at most 16 characters).", "4", 1, 10),
            Pick("reset", true, "FY = numbers restart every 1 April (required for GST documents), NEVER = one run.", "FY", "FY", "NEVER"),
            new ListField("activeFrom", ListKind.Date, true, "First date the series is used. A series that has issued documents never changes; a new one starts from a date with no documents.", "2026-11-01"),
        ])
        {
            CodeExample = "INV-2", NameExample = "Tax invoice",
            Check = NumberSeriesCheck,
            Frozen = ["document", "prefix", "start", "padding", "reset", "activeFrom"],
            FrozenMessage = "Series {0} already exists with other numbering settings (sequential, no gaps — GST). Change it on the Numbering series screen, or add a new series from a date with no documents.",
            FileCheck = f => OnePerKey(f, r => r["document"] + "/" + r["activeFrom"], "activeFrom", "series", "Another active series of the same document already starts on that date ({0})."),
        },
        new(ImportMaster.Policies, "policies", "policies", "Booking policies (cancellation, no-show, deposit)", "policy", true, "booking-sources",
        [
            Int("freeCancelHours", true, "Hours before arrival up to which cancelling is free, 0 to 720.", "48", 0, 720),
            Pct("cancelChargeBps", false, "Cancellation charge percent of the first night(s), 0 to 100 (blank = 0).", "100", 0, 10000).WithMoney() with { Default = "0" },
            Pct("noShowChargeBps", false, "No-show charge percent, 0 to 100 (blank = 0).", "100", 0, 10000).WithMoney() with { Default = "0" },
            Pct("depositBps", false, "Advance deposit percent asked at booking, 0 to 100 (blank = 0).", "25", 0, 10000).WithMoney() with { Default = "0" },
            RefSet("sources", false, "Booking-source codes the policy applies to, separated by ; (blank = the default policy for every other source).", "OTA-BKG", "booking-sources"),
        ])
        {
            CodeExample = "NONREF", NameExample = "Non-refundable",
            Check = c => c.Rule("freeCancelHours", () => FrontDeskMasters.FrontDeskMastersRules.Policy(IntOrNull(c["freeCancelHours"]) ?? 0, BpsOf(c["cancelChargeBps"]) ?? 0,
                BpsOf(c["noShowChargeBps"]) ?? 0, BpsOf(c["depositBps"]) ?? 0, Items(c["sources"]))),
            FileCheck = PolicyFileCheck,
        },
        new(ImportMaster.Designations, "designations", "designations", "Staff designations", "designation", true, "departments",
        [
            RefOne("dept", false, "Department code (from the departments file); blank = none.", "FO", DepartmentRef),
            Text("grade", false, "Grade, letters, digits, space, '.', '/' or '-', up to 20 characters.", "G2", 20),
            new ListField("defaultPayRule", ListKind.Pick, false, "monthly-structured, monthly-basic or daily (blank = none). Owner-only (pay).", "monthly-structured")
                { Options = ["monthly-structured", "monthly-basic", "daily"], Money = true },
        ])
        {
            CodeExample = "RECEPTIONIST", NameExample = "Receptionist",
            Check = c => c.Rule("grade", () => PeopleLists.PeopleListsRules.Designation(c.Code, c.Name, null, c["dept"].Length == 0 ? null : c["dept"], c["grade"].Length == 0 ? null : c["grade"],
                c["defaultPayRule"].Length == 0 ? null : c["defaultPayRule"])),
        },
        new(ImportMaster.ShiftPatterns, "shift-patterns", "shiftPatterns", "Shift patterns", "shift pattern", false, "—",
        [
            new ListField("start", ListKind.Time, true, "Duty start, 24-hour HH:mm.", "07:00"),
            new ListField("end", ListKind.Time, true, "Duty end, 24-hour HH:mm (a shift past midnight must be marked night).", "15:00"),
            Int("breakMinutes", false, "Unpaid break in minutes, 0 to 240 (blank = 0).", "30", 0, 240) with { Default = "0" },
            Flag("night", "yes for a night shift (ends after midnight).", "no"),
            Pick("weeklyOffRule", false, "fixed (a set weekly-off day) or roster (set in the roster); blank = fixed.", "fixed", "fixed", "roster"),
        ])
        {
            CodeMeaning = "Shift code, 1 to 3 capital letters or digits (OFF is reserved). Staff and the roster use it.", CodeExample = "M", NameExample = "Morning",
            CodeMax = 3,
            Check = c => c.Rule("start", () => PeopleLists.PeopleListsRules.ShiftPattern(c.Code, c.Name, null, c["start"], c["end"], IntOrNull(c["breakMinutes"]) ?? 0, c["night"] == "yes",
                c["weeklyOffRule"].Length == 0 ? null : c["weeklyOffRule"])),
            FileCheck = ShiftTimesCheck,
        },
        new(ImportMaster.LeaveTypes, "leave-types", "leaveTypes", "Leave types", "leave type", true, "—",
        [
            new ListField("paid", ListKind.Bool, false, "yes for paid leave, no for unpaid (blank = no). Fixed: only LOP (leave without pay) is unpaid. Owner-only (pay).", "yes") { Default = "no", Money = true },
            new ListField("yearlyQuota", ListKind.Days, false, "Days per financial year for CL, SL and EL only, 0 or more, halves allowed (blank = not tracked).", "12") { Min = 0, Money = true },
        ])
        {
            CodeMeaning = "SCC’s fixed leave categories: CL, SL, EL, ML, CO, OTH and LOP (leave without pay). No other code.", CodeExample = "CL", NameExample = "Casual leave",
            CodeMax = 6,
            Check = c => c.Rule("paid", () => PeopleLists.PeopleListsRules.LeaveType(c.Code, c.Name, null, c["paid"] == "yes", DaysOrNull(c["yearlyQuota"]))),
        },
        new(ImportMaster.Holidays, "holidays", "holidays", "Holiday calendar", "holiday", true, "—",
        [
            Pick("kind", true, "paid (counts like a weekly off in payroll) or restricted.", "paid", "paid", "restricted"),
        ])
        {
            CodeMeaning = "The holiday date, YYYY-MM-DD (any common date format is accepted). One holiday per date.", CodeExample = "2026-11-08", NameExample = "Diwali",
            CodeMax = 10, CodeIsDate = true,
        },
        new(ImportMaster.HkTaskTypes, "hk-task-types", "hkTaskTypes", "Housekeeping task types", "task type", false, "—",
        [
            Set("appliesTo", true, "What it serves: checkout, move, stayover, inspection, deep separated by ; (each kind belongs to one active type).", "checkout;move", "checkout", "move", "stayover", "inspection", "deep"),
            Int("standardMinutes", true, "Standard minutes for one task, 5 to 240 (feeds attendant load and board timing).", "40", 5, 240),
            Flag("needsInspection", "yes when a finished clean waits for the supervisor (never for a stayover service).", "yes"),
            new ListField("checklist", ListKind.Lines, true, "Checklist items separated by ; (1 to 8 items, each up to 60 characters).", "Linen & bathroom;Surfaces & amenities;Final room check") { Min = 1, Hi = 8, Max = 60 },
        ])
        {
            CodeExample = "DEP", NameExample = "Departure clean",
            Check = c => c.Rule("appliesTo", () => OpsMasters.OpsMastersRules.ValidateTaskType(Items(c["appliesTo"]), IntOrNull(c["standardMinutes"]), c["needsInspection"] == "yes", Items(c["checklist"]))),
            FileCheck = KindsFreeCheck,
        },
        new(ImportMaster.HkZones, "hk-zones", "hkZones", "Housekeeping zones", "zone", false, "floors",
        [
            RefSet("floors", false, "Floor codes in the zone separated by ; (a floor belongs to one active zone).", "F1;F2", "floors"),
            RefSet("rooms", false, "Extra room numbers in the zone separated by ; (a room belongs to one active zone).", "301;302", RoomRef),
            RefOne("defaultAttendant", false, "Sign-in name of the default attendant (the display name works too); blank = none.", "meena.hk", UserRef),
        ])
        {
            CodeExample = "ZONE-A", NameExample = "Zone A (floors 1-2)",
            Check = c => { if (!c.Has("floors") && !c.Has("rooms")) c.Error("floors", "Choose at least one floor or room for the zone."); },
            FileCheck = ZoneFreeCheck,
        },
        new(ImportMaster.ComplaintCategories, "complaint-categories", "complaintCategories", "Complaint categories", "complaint category", false, "—",
        [
            Pick("department", true, "Department that normally handles it: Housekeeping, Maintenance or Front desk.", "Housekeeping", [.. OpsMasters.OpsMastersRules.Departments]),
            Int("slaHours", true, "Answer or resolve within this many hours, 1 to 720.", "4", 1, 720),
        ])
        {
            CodeExample = "CLEAN", NameExample = "Room cleanliness",
        },
        new(ImportMaster.LostFoundCategories, "lost-found-categories", "lostFoundCategories", "Lost-and-found categories", "lost-and-found category", false, "—",
        [
            Pick("department", true, "Department that handles it: Housekeeping, Maintenance or Front desk.", "Front desk", [.. OpsMasters.OpsMastersRules.Departments]),
            Int("slaHours", true, "Return or follow up within this many hours, 1 to 720.", "48", 1, 720),
            Flag("valuable", "yes for a valuable (held in the front-office safe; disposal needs a manager).", "yes"),
        ])
        {
            CodeExample = "JEWEL", NameExample = "Jewellery",
        },
    ];

    public static ListDef? Find(ImportMaster m) => All.FirstOrDefault(d => d.Master == m);

    public static ListDef? Find(string? wire) => All.FirstOrDefault(d => string.Equals(d.Wire, (wire ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    private static ListField WithMoney(this ListField f) => f with { Money = true };

    /// <summary>The template columns of a list master, with the generic meaning of code, name, sortOrder and active.</summary>
    public static IReadOnlyList<ImportColumn> ColumnsOf(ListDef d)
    {
        var cols = new List<ImportColumn>
        {
            new("code", true, d.CodeMeaning, d.CodeExample),
            new("name", true, "Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name.", d.NameExample, Text: ImportCellText.Free),
        };
        // A06-08 (Wave D1 x MA-8): free text keeps formula text with a leading apostrophe; numbers, yes/no, picks, dates, times and codes of other
        // lists (refused when not in HCC) refuse it by their own rule.
        cols.AddRange(d.Fields.Select(f => new ImportColumn(f.Key, f.Required, f.Meaning, f.Example,
            Text: f.Kind switch { ListKind.Text => ImportCellText.Free, ListKind.Lines => ImportCellText.FreeList, _ => ImportCellText.Checked })));
        cols.Add(new ImportColumn("sortOrder", false, "Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last.", "10"));
        cols.Add(new ImportColumn("active", false, "yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked.", "yes"));
        return cols;
    }

    /// <summary>Canonical text of a Set / Lines value: fixed options in option order, other codes sorted, lines in the given order.</summary>
    public static string CanonItems(ListField f, IEnumerable<string> items)
    {
        var list = items.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (f.Kind == ListKind.Lines) return string.Join(';', list);
        if (f.Options is { } options) return string.Join(';', options.Where(o => list.Contains(o, StringComparer.OrdinalIgnoreCase)));
        return string.Join(';', list.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
    }

    // ── helpers shared by the row checks ──

    public static int? IntOrNull(string? s) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static decimal? DaysOrNull(string? s) => decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Canonical text of a number of days (12, 12.5, 10.25).</summary>
    public static string DaysText(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Canonical percent text ("12.5") → basis points.</summary>
    public static int? BpsOf(string? percent) => string.IsNullOrEmpty(percent) ? null : (int)decimal.Round(decimal.Parse(percent, CultureInfo.InvariantCulture) * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Canonical rupees ("300.00") → paise.</summary>
    public static long? PaiseOf(string? rupees) => string.IsNullOrEmpty(rupees) ? null : (long)decimal.Round(decimal.Parse(rupees, CultureInfo.InvariantCulture) * 100m, 0, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<string> Items(string? text) => string.IsNullOrEmpty(text) ? [] : text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string PctText(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    public static string RupeesText(long paise) => (paise / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static void OnePerKey(ListFileCheck f, Func<ListFinalRow, string> key, string column, string what, string message)
    {
        _ = what;
        foreach (var g in f.Final.Where(r => r.Active).GroupBy(key, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Key != "/" && g.Count() > 1))
            foreach (var r in g.Where(x => x.FileRow is not null))
                f.Error(r, column, string.Format(CultureInfo.InvariantCulture, message, g.Key));
    }

    private static string? LedgerOf(string code, bool required, IReadOnlyCollection<Accounting.AccountGroup> groups)
    {
        var c = (code ?? string.Empty).Trim();
        if (c.Length == 0)
        {
            if (required) throw Masters.MastersRules.Fail(Contracts.Masters.MasterReasons.Invalid, "Choose the ledger.", "ledger");
            return null;
        }

        var a = Accounting.ChartOfAccounts.Find(c);
        if (a is null || a.Code == Accounting.ChartOfAccounts.Suspense || !groups.Contains(a.Group))
            throw Masters.MastersRules.Fail(Contracts.Masters.MasterReasons.Invalid,
                $"Ledger {c} is not a {string.Join(" / ", groups.Select(g => g.ToString().ToLowerInvariant()))} ledger of the chart of accounts.", "ledger");
        return a.Code;
    }

    private static void PaymentMethodCheck(ListRowCheck c)
    {
        var kind = c["kind"];
        c.Rule("ledger", () => LedgerOf(c["ledger"], false, [Accounting.AccountGroup.Asset, Accounting.AccountGroup.Liability]));
        var settles = c["settlesTo"];
        var acc = settles.Length == 0 ? null : c.Rows("bank-accounts").FirstOrDefault(a => string.Equals(a.Code, settles, StringComparison.OrdinalIgnoreCase));
        var accKind = acc is null ? null : acc.Values.GetValueOrDefault("kind");
        if (kind == "cash" && accKind != "till") c.Error("settlesTo", "A cash method settles to a till (the front-desk drawer).");
        else if (kind == "credit" && settles.Length > 0) c.Error("settlesTo", "A credit method settles to no account — leave settlesTo blank.");
        else if (kind is not ("cash" or "credit" or "") && accKind != "bank") c.Error("settlesTo", "Choose the bank account this method settles to.");
        var at = Items(c["usableAt"]);
        if (kind == "credit" && (at.Contains("frontdesk") || at.Contains("pos")))
            c.Error("usableAt", "Credit is not a guest payment: company credit goes through the approval on the folio. Use it for expenses only.");
    }

    private static void BankAccountCheck(ListRowCheck c)
    {
        var kind = c["kind"];
        if (c.Has("accountLast4") && !Last4().IsMatch(c["accountLast4"])) c.Error("accountLast4", "Enter only the last 4 digits of the account number.");
        if (c.Has("ifsc") && !Ifsc().IsMatch(c["ifsc"].ToUpperInvariant())) c.Error("ifsc", "IFSC: 4 letters, 0, then 6 letters or digits (e.g. ABCD0123456).");
        if (kind == "till" && (c.Has("bank") || c.Has("accountLast4") || c.Has("ifsc"))) c.Error("bank", "A till has no bank, account number or IFSC.");
        if (kind == "till" && c.Has("defaultFor")) c.Error("defaultFor", "A till is not a default for UPI, card or bank methods.");
        if (kind == "bank" && !c.Has("bank")) c.Error("bank", "Enter the bank name.");
        if (c.Existing is { } e && (PaiseOf(e.Values.GetValueOrDefault("openingBalance")) ?? 0) != (PaiseOf(c["openingBalance"]) ?? 0))
            c.Error("openingBalance", $"The opening balance of {e.Name} is set once (setup-only); it is not changed by an import. Leave it as it is in HCC ({e.Values.GetValueOrDefault("openingBalance")}).");
    }

    private static void BankDefaultsCheck(ListFileCheck f)
    {
        foreach (var k in new[] { "upi", "card", "bank", "wallet" })
        {
            var holders = f.Final.Where(r => r.Active && Items(r["defaultFor"]).Contains(k)).ToList();
            if (holders.Count > 1)
                foreach (var r in holders.Where(h => h.FileRow is not null))
                    f.Error(r, "defaultFor", $"{string.Join(" and ", holders.Select(h => h.Name))} are both the default for {k} — only one account can be.");
        }
    }

    /// <summary>ACC-2 (contract §3.4): a capital category's ledger is a fixed-asset block (15x0); otherwise an expense ledger; RCM needs its rate.</summary>
    private static void ExpenseCategoryCheck(ListRowCheck c)
    {
        var capital = c["capital"] == "yes";
        var l = c["ledger"].Trim();
        if (capital && !(Expenses.ExpensePurchase.IsAssetLedger(l) && Accounting.ChartOfAccounts.Find(l) is not null))
            c.Error("ledger", "A capital category posts to a fixed-asset block (1500 to 1540).");
        else if (!capital && Expenses.ExpensePurchase.IsAssetLedger(l)) c.Error("ledger", "A fixed-asset block is the ledger of a capital category only — set capital to yes.");
        else if (!capital) c.Rule("ledger", () => LedgerOf(c["ledger"], true, [Accounting.AccountGroup.Expense]));
        if (c["rcm"] == "yes" && c["rcmBps"].Length == 0) c.Error("rcmBps", "Enter the reverse-charge GST percent (rcm is yes).");
    }

    private static void ExpenseVendorCheck(ListRowCheck c)
    {
        var g = c["gstin"].ToUpperInvariant();
        var p = c.Existing?.Values.GetValueOrDefault("pan") ?? string.Empty; // held in HCC only: a file never carries it (DEC-068)
        if (g.Length > 0 && !Gstin().IsMatch(g)) c.Error("gstin", "GSTIN: 15 characters, e.g. 27ABCDE1234F1Z5.");
        else if (g.Length > 0 && p.Length > 0 && g.Substring(2, 10) != p) c.Error("gstin", "The PAN inside the GSTIN (characters 3 to 12) does not match the vendor's PAN in HCC.");
        if (c["tds"] == "yes" && p.Length == 0) c.Error("tds", "TDS needs the vendor's PAN, which a file never carries: enter it on the vendor's row in Master configuration › Expense vendors first.");
        var st = c["state"].Trim().ToUpperInvariant(); // ACC-2
        if (st.Length > 0 && (st.Length != 2 || !st.All(char.IsAsciiLetterUpper))) c.Error("state", "GST state: two letters (e.g. MH, KA, GJ).");
        if (c["tdsSection"].Length > 0 && c["tds"] != "yes") c.Error("tdsSection", "Set tds to yes to choose a TDS section.");
    }

    private static void NumberSeriesCheck(ListRowCheck c)
    {
        c.Rule("prefix", () => FrontDeskMasters.FrontDeskMastersRules.NumberSeries(c["document"], c["prefix"], IntOrNull(c["start"]) ?? 0, IntOrNull(c["padding"]) ?? 0, c["reset"], c["activeFrom"]));
        if (c.Existing is null && DateOnly.TryParseExact(c["activeFrom"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) && from < c.Snap.BusinessDate)
            c.Error("activeFrom", $"A new series starts today ({c.Snap.BusinessDate:yyyy-MM-dd}) or later — a series starts on a date with no documents.");
    }

    private static void PolicyFileCheck(ListFileCheck f)
    {
        var active = f.Final.Where(r => r.Active).ToList();
        var defaults = active.Where(r => Items(r["sources"]).Count == 0).ToList();
        if (defaults.Count > 1)
            foreach (var r in defaults.Where(x => x.FileRow is not null))
                f.Error(r, "sources", $"{string.Join(" and ", defaults.Select(d => d.Name))} are both default policies (name no source) — only one can be.");
        foreach (var g in active.SelectMany(r => Items(r["sources"]).Select(s => (Source: s, Row: r))).GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var x in g.Where(y => y.Row.FileRow is not null))
                f.Error(x.Row, "sources", $"{g.Key} follows more than one active policy ({string.Join(", ", g.Select(y => y.Row.Name))}).");
    }

    private static void ShiftTimesCheck(ListFileCheck f)
    {
        foreach (var g in f.Final.Where(r => r.Active).GroupBy(r => r["start"] + "-" + r["end"]).Where(g => g.Count() > 1))
            foreach (var r in g.Where(x => x.FileRow is not null))
                f.Error(r, "start", $"{string.Join(" and ", g.Select(x => x.Name))} have the same times — keep one active.");
    }

    private static void KindsFreeCheck(ListFileCheck f)
    {
        foreach (var g in f.Final.Where(r => r.Active).SelectMany(r => Items(r["appliesTo"]).Select(k => (Kind: k, Row: r))).GroupBy(x => x.Kind).Where(g => g.Count() > 1))
            foreach (var x in g.Where(y => y.Row.FileRow is not null))
                f.Error(x.Row, "appliesTo", $"{g.Key} is handled by more than one active type ({string.Join(", ", g.Select(y => y.Row.Name))}) — each kind belongs to one active type.");
    }

    private static void ZoneFreeCheck(ListFileCheck f)
    {
        foreach (var (col, label) in new[] { ("floors", "Floor"), ("rooms", "Room") })
            foreach (var g in f.Final.Where(r => r.Active).SelectMany(r => Items(r[col]).Select(k => (Key: k, Row: r))).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var x in g.Where(y => y.Row.FileRow is not null))
                    f.Error(x.Row, col, $"{label} {g.Key} is in more than one active zone ({string.Join(", ", g.Select(y => y.Row.Name))}).");
    }

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Last4();

    [GeneratedRegex("^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ifsc();

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex Gstin();

    // ── CSV export (the same file the import reads) ──

    /// <summary>One CSV cell, quoted when it holds a comma, quote or line break.</summary>
    public static string CsvCell(string? v)
    {
        var t = v ?? string.Empty;
        return t.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : t;
    }

    /// <summary>A CSV file from header names and rows of cells.</summary>
    public static string ToCsv(IEnumerable<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers)).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join(',', r.Select(CsvCell))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>The rows as a CSV file with the template headers: re-importing it plans every row as Unchanged.</summary>
    public static string ToCsv(ListDef d, IEnumerable<ListSnapRow> rows)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        sb.Append(string.Join(',', d.Columns)).Append("\r\n");
        foreach (var r in rows.OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            var cells = new List<string> { r.Code, r.Name };
            cells.AddRange(d.Fields.Select(f => r.Values.GetValueOrDefault(f.Key) ?? string.Empty));
            cells.Add(r.SortOrder.ToString(CultureInfo.InvariantCulture));
            cells.Add(r.Active ? "yes" : "no");
            sb.Append(string.Join(',', cells.Select(CsvCell))).Append("\r\n");
        }

        return sb.ToString();
    }
}
