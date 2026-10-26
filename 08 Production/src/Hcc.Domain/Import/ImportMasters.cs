namespace Hcc.Domain.Import;

/// <summary>The masters that can be imported (wire names in <see cref="ImportMasters.Wire"/>).</summary>
public enum ImportMaster
{
    RoomTypes,
    Rooms,
    Tariffs,
    Seasons,
    Staff,
    Ledgers,
    OpeningBalances,
    TillCash,
    Guests,
    PosOutlets,
    PosMenu,
    Departments,
    Companies,
    Suppliers,
    StockItems,
    // MA-8: the list masters of the masters wave (definitions in ImportLists).
    Floors,
    Amenities,
    OccupancyCharges,
    MealPlans,
    Extras,
    DiscountReasons,
    PaymentMethods,
    BankAccounts,
    ExpenseCategories,
    ExpenseVendors,
    MenuCategories,
    PosTables,
    BookingSources,
    IdTypes,
    NumberSeries,
    Policies,
    Designations,
    ShiftPatterns,
    LeaveTypes,
    Holidays,
    HkTaskTypes,
    HkZones,
    ComplaintCategories,
    LostFoundCategories,
}

/// <summary>How a masters import treats spreadsheet-formula text in a column (A06-08, OWASP CSV injection).</summary>
public enum ImportCellText
{
    /// <summary>Checked by the column's own rule (code, number, date, phone, yes/no, a lookup): formula text never passes it, and a minus sign
    /// in a number column (<c>-10</c> percent, <c>-500</c> rupees) is read as a number, never as a formula.</summary>
    Checked,

    /// <summary>Stored as typed (names, notes, e-mail): a cell starting with = + - @ is kept with a leading apostrophe, as text, and the preview warns.</summary>
    Free,

    /// <summary>A list of free names separated by ; (POS tables): each item is treated like <see cref="Free"/>.</summary>
    FreeList,
}

/// <summary>A template column: canonical name (the template header), required flag, plain-English meaning, an example, older header names
/// still accepted (<paramref name="Aliases"/>, masters wave), and how formula text is treated (<paramref name="Text"/>, A06-08).</summary>
public sealed record ImportColumn(string Name, bool Required, string Meaning, string Example, IReadOnlyList<string>? Aliases = null,
    ImportCellText Text = ImportCellText.Checked);


public sealed record ImportMasterSpec(ImportMaster Master, string Wire, string Title, bool OwnerOnlyCommit, bool SetupStateOnly, string DependsOn,
    IReadOnlyList<ImportColumn> Columns);

/// <summary>
/// Column specifications of every master (the CSV templates under deployment/import-templates use exactly these headers; a test
/// keeps them in step). Owner-only commit = money, tax, rates, roles and accounts (DEC-058), plus department leave limits (leave
/// policy), company credit limits and stock unit costs; rooms, guest profiles, POS outlets and suppliers are the non-money masters.
/// Opening balances and till opening cash are accepted only while the property is in setup (no business day closed yet).
/// </summary>
public static class ImportMasters
{
    private static readonly IReadOnlyList<ImportMasterSpec> Legacy =
    [
        new(ImportMaster.RoomTypes, "room-types", "Room types", true, false, "—",
        [
            new("code", true, "Short code of the room type, letters/digits/dash, up to 20 characters. Used by the rooms and tariffs files.", "DLX"),
            new("name", true, "Name shown on screens and bills, up to 60 characters.", "Deluxe Room", Text: ImportCellText.Free),
            new("maxOccupancy", true, "Most guests allowed in one room of this type (1 to 10).", "3"),
            new("rackRate", true, "Standard price per night in rupees before GST (fallback when no tariff is dated). Up to 2 decimals.", "4200.00"),
            new("sortOrder", false, "Order on screens (1 first). Leave blank for file order.", "1"),
        ]),
        new(ImportMaster.Rooms, "rooms", "Rooms", false, false, "room-types",
        [
            new("roomNo", true, "Room number as written on the door, up to 10 characters.", "101"),
            new("roomType", true, "Code of the room type (from the room types file or already in HCC).", "DLX"),
        ]),
        new(ImportMaster.Tariffs, "tariffs", "Tariffs (BAR)", true, false, "room-types",
        [
            new("roomType", true, "Code of the room type.", "DLX"),
            new("effectiveFrom", true, "First night this rate applies (YYYY-MM-DD or DD-MM-YYYY); it applies until the next dated rate. Not before the business date.", "2026-11-01"),
            new("rate", true, "Best available rate per night in rupees before GST. Up to 2 decimals.", "4500.00"),
        ]),
        new(ImportMaster.Seasons, "seasons", "Seasons (yield)", true, false, "room-types",
        [
            new("name", true, "Season name, at least 2 characters.", "Diwali peak", Text: ImportCellText.Free),
            new("from", true, "First night of the season.", "2026-11-05"),
            new("to", true, "Last night of the season (at most one year after the first).", "2026-11-15"),
            new("adjustmentPercent", true, "Change to the BAR in percent: +15 raises, -10 lowers (-50 to +100, not 0).", "+15"),
            new("roomTypes", false, "Room type codes separated by ; (blank = all room types).", "DLX;STE"),
            new("priority", false, "When seasons overlap the higher number wins (default 1).", "1"),
        ]),
        new(ImportMaster.Staff, "staff", "Staff and users", true, false, "—",
        [
            new("loginName", true, "Sign-in name: 3 to 40 lower-case letters, digits, dot, dash or underscore.", "priya.reception"),
            new("displayName", true, "Name shown on screens and in the audit trail.", "Priya Kulkarni", Text: ImportCellText.Free),
            new("roles", true, "Role codes or names separated by ; (REC Reception, CSH Cashier, MGR Manager, ACC Accountant, SUP Housekeeping supervisor, HKP Housekeeper, MNT Maintenance, TAD Technical administrator). The Owner role is not imported.", "REC;CSH"),
        ]),
        new(ImportMaster.Ledgers, "ledgers", "Ledgers (chart of accounts)", true, false, "—",
        [
            new("code", true, "Your ledger code (for example the Tally code), up to 20 characters. Must not be one of HCC's own 4-digit codes.", "CAP01"),
            new("name", true, "Ledger name, up to 100 characters.", "Owner's capital", Text: ImportCellText.Free),
            new("group", true, "Asset, Liability, Equity, Income or Expense.", "Equity"),
            new("hccAccount", false, "HCC account it rolls up into (4-digit code from the README list), same group; blank for Equity and ledgers HCC does not post to.", ""),
        ]),
        new(ImportMaster.OpeningBalances, "opening-balances", "Opening balances (trial balance)", true, true, "ledgers",
        [
            new("ledgerCode", true, "Ledger code: one of HCC's 4-digit accounts or a code from the ledgers file.", "1000"),
            new("debit", false, "Debit balance in rupees (assets, expenses). Leave blank or 0 when the balance is a credit.", "25000.00"),
            new("credit", false, "Credit balance in rupees (liabilities, capital, income). Leave blank or 0 when the balance is a debit.", ""),
        ]),
        new(ImportMaster.TillCash, "till-cash", "Till opening cash", true, true, "—",
        [
            new("tillCode", true, "Till code, letters/digits/dash, up to 20 characters (created when new).", "FD-1"),
            new("tillName", true, "Till name, up to 100 characters.", "Front desk drawer", Text: ImportCellText.Free),
            new("openingCash", true, "Cash physically in the drawer when HCC starts, in rupees. The first shift opens with this float.", "5000.00"),
        ]),
        new(ImportMaster.Guests, "guests", "Guest profiles (optional)", false, false, "—",
        [
            new("name", true, "Guest's full name.", "Rahul Deshmukh", Text: ImportCellText.Free),
            new("phone", true, "10-digit mobile number (+91 allowed).", "9822001122"),
            new("email", false, "E-mail address.", "rahul@example.com", Text: ImportCellText.Free),
            new("address", false, "Postal address.", "12 MG Road, Pune", Text: ImportCellText.Free),
            new("nationality", false, "Nationality (default Indian).", "Indian", Text: ImportCellText.Free),
            new("company", false, "Company name (required when a GSTIN is given).", "", Text: ImportCellText.Free),
            new("gstin", false, "Company GSTIN, 15 characters.", ""),
            new("notes", false, "Notes for the front desk.", "Prefers a ground-floor room", Text: ImportCellText.Free),
        ]),
        new(ImportMaster.PosOutlets, "pos-outlets", "Restaurant & room-service outlets (POS)", true, false, "—",
        [
            new("code", true, "Outlet code, letters/digits/dash, up to 20 characters (stored in capitals).", "REST", ["outletCode"]),
            new("name", true, "Outlet name shown on the POS screen and bills, up to 100 characters.", "Restaurant", ["outletName"], Text: ImportCellText.Free),
            new("type", true, "restaurant, room-service, bar or banquet (the older words table = restaurant and room = room-service still work). Room-service outlets have no tables.", "restaurant", ["kind"]),
            new("opens", false, "Opening time HH:mm (blank = not set).", "07:00"),
            new("closes", false, "Closing time HH:mm (blank = not set).", "23:00"),
            new("gstProfile", false, "hotel (hotel status, default), standard or specified premises — decides the restaurant GST rate (illustrative, CA to confirm). Owner-only.", "hotel"),
            new("kotStation", false, "Kitchen station a ticket prints at, up to 20 characters (blank = one ticket per send).", "Kitchen"),
            new("sortOrder", false, "Order on screens (10, 20 …). Blank keeps the outlet's order, or puts a new outlet last.", "10"),
            new("active", false, "yes or no (blank keeps the state, new = yes). A file cannot retire an outlet — retire it on its list, where open checks are looked at.", "yes"),
            new("tables", false, "Table names separated by ; (up to 10 characters each). Creates the tables of a new dining outlet (required then); an existing outlet keeps its tables, which are added or retired in the pos-tables list. Blank for room-service.", "T1;T2;T3;T4", Text: ImportCellText.FreeList),
        ]),
        new(ImportMaster.PosMenu, "pos-menu", "Restaurant & room-service menu (POS)", true, false, "pos-outlets",
        [
            new("code", true, "Menu item code, letters/digits/dash/underscore, up to 20 characters (stored in capitals).", "DAL", ["itemCode"]),
            new("name", true, "Item name shown on the POS screen and bills, up to 100 characters.", "Dal Tadka", ["itemName"], Text: ImportCellText.Free),
            new("category", true, "Menu category: the code or the name of a category (a new name creates the category), up to 60 characters.", "Main course", Text: ImportCellText.Free),
            new("shortCode", false, "Short code the waiter types, up to 10 characters.", "DT"),
            new("price", true, "Selling price in rupees before GST (0 allowed). A different price starts from the business date; earlier prices stay in the history.", "180.00"),
            new("gstClass", false, "food (default), laundry, banquet or other — the GST master holds the rates. Owner-only.", "food"),
            new("veg", true, "yes for vegetarian, no for non-vegetarian.", "yes"),
            new("outlets", false, "Outlet codes that sell the item, separated by ; (blank = every outlet already in HCC).", "REST;RS"),
            new("station", false, "Kitchen station, up to 20 characters (blank = the outlet's station).", "Tandoor"),
            new("sortOrder", false, "Order on screens (10, 20 …). Blank keeps the item's order, or puts a new item last.", "10"),
            new("active", false, "yes or no (blank keeps the state, new = yes). A file cannot retire an item — retire it on the menu list.", "yes"),
        ]),
        new(ImportMaster.Departments, "departments", "Departments (staff and payroll)", true, false, "—",
        [
            new("code", true, "Department code, 2 to 10 capital letters or digits (stored in capitals).", "FO"),
            new("name", true, "Department name, 2 to 60 characters.", "Front office", Text: ImportCellText.Free),
            new("leaveCap", false, "Most staff of this department on leave on one day, 1 to 20 (blank = the leave policy default).", "2"),
        ]),
        new(ImportMaster.Companies, "companies", "Company accounts (corporate clients)", true, false, "room-types",
        [
            new("name", true, "Company name, up to 200 characters. Used to find the account again on a re-import.", "Mehta Traders (fictional)", Text: ImportCellText.Free),
            new("gstin", false, "Company GSTIN, 15 characters.", "27ABCDE1234F1Z5"),
            new("contact", false, "Contact person, up to 120 characters.", "Arjun Mehta", Text: ImportCellText.Free),
            new("phone", false, "Phone number, 7 to 16 digits (+91 and spaces allowed).", "9890011223"),
            new("email", false, "E-mail address.", "accounts@example.com", Text: ImportCellText.Free),
            new("city", false, "City, up to 80 characters.", "Mumbai", Text: ImportCellText.Free),
            new("creditLimit", false, "Credit limit in rupees (blank or 0 = no credit). Enforced (DEC-076): bookings, company credit and group routing billed to the company stop at it.", "50000"),
            new("negotiatedRates", false, "Negotiated nightly rates before GST: room type code=rupees, separated by ; (blank = none).", "DLX=3800;STD=2600"),
        ]),
        new(ImportMaster.Suppliers, "suppliers", "Suppliers and service vendors", false, false, "—",
        [
            new("name", true, "Supplier or vendor name, 3 to 120 characters. Used to find it again on a re-import.", "Shree Laundry Services (fictional)", Text: ImportCellText.Free),
            new("phone", true, "Phone number (digits, spaces, dash; + allowed).", "9822100200"),
            new("trade", false, "What they supply or repair, up to 60 characters (blank = General).", "Laundry", Text: ImportCellText.Free),
        ]),
        new(ImportMaster.StockItems, "stock-items", "Stock items (linen, amenities, cleaning)", true, false, "—",
        [
            new("code", true, "Item code like LIN-BS: 2 to 6 letters/digits, a dash, 1 to 10 letters/digits (stored in capitals).", "LIN-BS"),
            new("name", true, "Item name, 2 to 120 characters.", "Bed sheet (double)", Text: ImportCellText.Free),
            new("kind", true, "linen, amenity or cleaning.", "linen"),
            new("unit", true, "Unit of counting, up to 20 characters.", "pcs", Text: ImportCellText.Free),
            new("unitCost", false, "Cost per unit in rupees (blank = not costed yet). Up to 2 decimals, at most ₹1,00,000.", "450.00"),
            new("minQty", false, "Minimum stock before a reorder is suggested, 0 to 10,000 (blank = 0).", "40"),
            new("reorderQty", false, "Quantity to reorder, 0 to 10,000 (blank = 0).", "60"),
            new("usePerRoomNight", false, "Standard use per occupied room-night, 0 to 100, up to 2 decimals (blank = none).", "1.5"),
            new("laundryRate", false, "Laundry charge per piece in rupees (linen only; blank = none).", "12.00"),
            new("supplier", false, "Usual supplier, up to 120 characters.", "Shree Linen House (fictional)", Text: ImportCellText.Free),
        ]),
    ];

    public static readonly IReadOnlyList<ImportMasterSpec> All = [.. Legacy, .. ImportLists.All.Select(ListSpec)];

    private static ImportMasterSpec ListSpec(ListDef d) => new(d.Master, d.Wire, d.Title, d.OwnerOnlyCommit, false, d.DependsOn, ImportLists.ColumnsOf(d));

    public static ImportMasterSpec Spec(ImportMaster m) => All.First(s => s.Master == m);

    public static string Wire(ImportMaster m) => Spec(m).Wire;

    public static ImportMasterSpec? Find(string? wire) =>
        All.FirstOrDefault(s => string.Equals(s.Wire, (wire ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Header keys that are never accepted in any file: secrets (passwords, PINs, setup codes — users get their first credential through
    /// Access admin) and full identity numbers (DEC-068). Compared on <see cref="CsvParser.Key"/>.
    /// </summary>
    public static bool IsForbiddenHeader(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string[] contains = ["password", "passwd", "passcode", "secret", "aadhaar", "aadhar", "passport", "idnumber", "setupcode", "drivinglicen", "voterid"];
        string[] exact = ["pin", "pinno", "loginpin", "otp", "pan", "panno", "pannumber", "pancard", "idno", "fullid", "idproof", "idlast4", "uid"];
        return contains.Any(c => key.Contains(c, StringComparison.Ordinal)) || exact.Contains(key, StringComparer.Ordinal);
    }
}
