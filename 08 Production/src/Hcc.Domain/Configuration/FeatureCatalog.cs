namespace Hcc.Domain.Configuration;

/// <summary>
/// One sub-function of a module (ADM-1, DEC-214 / DEC-215). <see cref="Id"/> is the full id (<c>frontdesk.reservations</c>); the switch
/// setting key is <c>feature.&lt;Id&gt;</c>. <see cref="Core"/> features are never switchable. <see cref="Requires"/> lists the features this
/// one needs: switching one of those off switches this one off too. <see cref="Pages"/> and <see cref="Prefixes"/> are the prototype's nav
/// pages and command prefixes (kept so the golden stays one file); the server gates its own routes with <c>RequireHccFeature(id)</c>.
/// </summary>
public sealed record FeatureDef(string Id, string Label, bool Core, IReadOnlyList<string> Pages, IReadOnlyList<string> Prefixes,
    IReadOnlyList<string> Except, IReadOnlyList<string> Requires, IReadOnlyList<string> PermPrefixes, string Note);

/// <summary>A catalogue module: a label, the DEC-057 module switch it follows (if any) and its sub-functions.</summary>
public sealed record FeatureModule(string Id, string Label, string? Switch, IReadOnlyList<string> Coarse, IReadOnlyList<FeatureDef> Features);

/// <summary>Feature ids used by server routes (full ids; the rest of the catalogue is gated in the prototype only for now).</summary>
public static class FeatureIds
{
    public const string Reservations = "frontdesk.reservations";
    public const string Alerts = "frontdesk.alerts";
    public const string Keys = "frontdesk.keys";
    public const string CashBook = "cash.cashbook";
    public const string CounterBills = "cash.counter";
    public const string Expenses = "cash.expenses";
    public const string PosMenu = "pos.menu";
    public const string Complaints = "housekeeping.complaints";
    public const string LostFound = "housekeeping.lostfound";
    public const string MaintenanceAssets = "maintenance.assets";
    public const string GuestRequests = "guestsvc.requests";
    public const string Handover = "guestsvc.handover";
    public const string MessageTemplates = "messaging.templates";
    public const string Crm = "customers.crm";
    public const string Yield = "revenue.yield";
    public const string OtaPayouts = "revenue.payouts";
    public const string Purchases = "stock.purchases";
    public const string AssetRegister = "stock.assets";
    public const string Leave = "people.leave";
    public const string Vouchers = "accounting.vouchers";
    public const string Gst = "accounting.gst";
    public const string Tds = "accounting.tds";
    public const string Tally = "accounting.tally";
    public const string PlansExtras = "reports.plans";
    public const string Compliance = "compliance.calendar";
    public const string Masters = "admin.masters";
    public const string LegacyImport = "admin.legacy";

    public static string SettingKey(string featureId) => "feature." + featureId;
}

/// <summary>
/// The feature catalogue — ported from <c>03 Prototype/src/domain/feature-catalogue.ts</c>, the single source. The golden
/// <c>08 Production/database/feature-catalogue.json</c> is generated from the prototype (<c>node tools/feature-catalogue.mjs --write</c> in
/// <c>03 Prototype</c>) and <c>FeatureCatalogTests</c> fails when this class drifts from it. Order, ids, labels, core flags and requires are
/// the contract; pages and prefixes are carried verbatim.
/// </summary>
public static class FeatureCatalog
{
    private static FeatureDef F(string id, string label, bool core, string[] pages, string[] prefixes, string note, string[]? except = null, string[]? requires = null) =>
        new(id, label, core, pages, prefixes, except ?? [], requires ?? [], PermPrefixesOf(id), note);

    /// <summary>ADM-2: server permission-id prefixes per sub-function (golden <c>permPrefixes</c>); the role grid grants / removes ids by these.</summary>
    private static readonly Dictionary<string, string[]> PermPrefixes = new(StringComparer.Ordinal)
    {
        ["frontdesk.roomboard"] = ["PERM-ROOM-"],
        ["frontdesk.reservations"] = ["PERM-RES-"],
        ["frontdesk.checkin"] = ["PERM-STY-", "PERM-RATE-"],
        ["frontdesk.guests"] = ["PERM-GST-"],
        ["frontdesk.alerts"] = ["PERM-RMD-"],
        ["frontdesk.keys"] = ["PERM-KEY-"],
        ["cash.folios"] = ["PERM-FOL-"],
        ["cash.cashier"] = ["PERM-PAY-", "PERM-SHF-", "PERM-DRW-"],
        ["cash.cashbook"] = ["PERM-CLS-"],
        ["cash.counter"] = ["PERM-CTR-"],
        ["cash.expenses"] = ["PERM-EXP-"],
        ["cash.invoices"] = ["PERM-INV-"],
        ["pos.checks"] = ["PERM-POS-"],
        ["pos.menu"] = ["PERM-POS-"],
        ["housekeeping.board"] = ["PERM-HK-"],
        ["housekeeping.complaints"] = ["PERM-GSV-"],
        ["housekeeping.lostfound"] = ["PERM-GSV-"],
        ["maintenance.workorders"] = ["PERM-MNT-"],
        ["maintenance.assets"] = ["PERM-MNT-"],
        ["guestsvc.requests"] = ["PERM-GSV-"],
        ["guestsvc.inhouse"] = ["PERM-STY-"],
        ["guestsvc.handover"] = ["PERM-HND-"],
        ["messaging.messages"] = ["PERM-MSG-"],
        ["messaging.templates"] = ["PERM-MSG-"],
        ["documents.capture"] = ["PERM-DOC-"],
        ["customers.c360"] = ["PERM-CRM-"],
        ["customers.crm"] = ["PERM-CRM-"],
        ["revenue.yield"] = ["PERM-YLD-"],
        ["revenue.groups"] = ["PERM-GRP-"],
        ["revenue.ota"] = ["PERM-OTA-"],
        ["revenue.payouts"] = ["PERM-OTA-"],
        ["revenue.website"] = ["PERM-WEB-"],
        ["revenue.tariffcard"] = ["PERM-TRF-"],
        ["revenue.feedback"] = ["PERM-FBK-"],
        ["revenue.hall"] = ["PERM-HAL-"],
        ["revenue.quotations"] = ["PERM-HAL-"],
        ["stock.linen"] = ["PERM-STK-"],
        ["stock.purchases"] = ["PERM-EXP-"],
        ["stock.assets"] = ["PERM-ACC-"],
        ["people.staff"] = ["PERM-HR-"],
        ["people.leave"] = ["PERM-HR-"],
        ["people.payroll"] = ["PERM-HR-"],
        ["people.myhr"] = ["PERM-HR-"],
        ["closing.dayclose"] = ["PERM-CLS-"],
        ["accounting.ledgers"] = ["PERM-ACC-"],
        ["accounting.vouchers"] = ["PERM-ACC-"],
        ["accounting.gst"] = ["PERM-ACC-"],
        ["accounting.tds"] = ["PERM-ACC-"],
        ["accounting.tally"] = ["PERM-ACC-"],
        ["accounting.yearend"] = ["PERM-ACC-"],
        ["accounting.bank"] = ["PERM-ACC-"],
        ["reports.reports"] = ["PERM-RPT-"],
        ["reports.hub"] = ["PERM-RPT-"],
        ["reports.digest"] = ["PERM-DGS-"],
        ["reports.plans"] = ["PERM-MST-"],
        ["compliance.calendar"] = ["PERM-CMP-"],
        ["assistant.assistant"] = [],
        ["admin.access"] = ["PERM-ADM-"],
        ["admin.config"] = ["PERM-CFG-"],
        ["admin.masters"] = ["PERM-MST-"],
        ["admin.approvals"] = ["PERM-APR-"],
        ["admin.backup"] = ["PERM-SYS-"],
        ["admin.health"] = ["PERM-SYS-"],
        ["admin.prefs"] = [],
        ["admin.legacy"] = ["PERM-LGC-", "PERM-IMP-"],
        ["admin.internet"] = ["PERM-NET-"],
        ["admin.roadmap"] = [],
    };

    private static string[] PermPrefixesOf(string id) => PermPrefixes.TryGetValue(id, out var p) ? p : [];

    /// <summary>ADM-2: prototype coarse permissions per module (golden <c>coarse</c>); carried for the golden only.</summary>
    private static readonly Dictionary<string, string[]> Coarse = new(StringComparer.Ordinal)
    {
        ["frontdesk"] = ["reservations", "checkin"],
        ["cash"] = ["billing", "payments", "checkout"],
        ["pos"] = ["billing"],
        ["housekeeping"] = ["housekeeping", "inspect"],
        ["maintenance"] = ["housekeeping"],
        ["guestsvc"] = ["checkin"],
        ["messaging"] = ["reservations"],
        ["documents"] = ["checkin"],
        ["customers"] = ["reservations"],
        ["revenue"] = ["reservations"],
        ["stock"] = ["housekeeping"],
        ["people"] = ["housekeeping"],
        ["closing"] = ["close"],
        ["accounting"] = ["reports"],
        ["reports"] = ["reports"],
        ["compliance"] = ["reports"],
        ["assistant"] = ["reports"],
        ["admin"] = ["admin"],
    };

    public static IReadOnlyList<FeatureModule> Modules { get; } =
    [
        new("frontdesk", "Front desk", null, Coarse["frontdesk"],
        [
            F("frontdesk.roomboard", "Room board", true, ["rooms"], [], "Live room status, blocks and quick actions"),
            F("frontdesk.reservations", "Reservations", false, ["reservations", "calendar"], ["BOOK", "CONFIRM", "CHANGE_DATES", "CANCEL", "BOOKING_COMPANY"],
                "Advance bookings, holds, calendar and amendments. Walk-in check-in for today stays available while off."),
            F("frontdesk.checkin", "Arrivals & check-in", true, ["arrivals"], ["REGISTER", "CHECKIN", "MOVE", "STAY_"], "Registration, ID, check-in, room move, stay changes"),
            F("frontdesk.guests", "Guest register", true, ["guests"], ["GUEST_SAVE", "GUEST_ID_VIEW", "GUEST_PHOTO"], "Guest profiles, IDs and photos"),
            F("frontdesk.alerts", "Reminders & wake-ups", false, ["desk-alerts"], ["RMD_", "GSV_WAKE_"], "Desk reminders and wake-up calls (LF-ALERTS)"),
            F("frontdesk.keys", "Room keys", false, ["keys"], ["KEY_"], "Key cards through the Key Agent (KEYS)"),
        ]),
        new("cash", "Cash & billing", null, Coarse["cash"],
        [
            F("cash.folios", "Guests & folios", true, ["folios"], ["CHARGE", "DISCOUNT", "REFUND_", "COMPANY_CREDIT_", "CORRECTION_", "CREDIT_NOTE", "BILL_CORRECTION_", "APPROVAL", "CAT_", "GUEST_RATE_", "GUEST_CREDIT_"],
                "Folio charges, discounts, refunds, credit notes and corrections"),
            F("cash.cashier", "Cashier & payments", true, ["cashier"], ["PAYMENT", "SHIFT_", "TILL_", "CASH_BANK", "OWNER_DRAWING", "DEPOSIT_"], "Payments, shifts, till and bank drops"),
            F("cash.cashbook", "Cash book", false, ["cashbook"], ["CB_"], "Day-wise cash book with lock and reopen"),
            F("cash.counter", "Counter bills", false, ["counter-bills"], ["CTR_"], "Walk-in counter bills (LF-UI)", requires: ["cash.folios"]),
            F("cash.expenses", "Expenses", false, ["expenses"], ["EXPENSE", "EXP_"], "Expense entry, approval, petty cash, budgets, Udhaar", except: ["EXP_VENDOR_"]),
            F("cash.invoices", "Invoices & credit notes", true, [], [], "GST invoices and credit notes (statutory)"),
        ]),
        new("pos", "Restaurant & POS", ConfigModuleIds.Pos, Coarse["pos"],
        [
            F("pos.checks", "Outlets & checks", false, ["pos", "kds"] /* F8-QW-KDS */, ["POS_ORDER_", "POS_ITEM_", "POS_KOT_", "POS_SETTLE", "POS_VOID_", "POS_INVOICE_"], "Checks, KOT, settlement and voids"),
            F("pos.menu", "Menu & prices", false, [], ["POS_MENU_"], "Menu availability and price changes", requires: ["pos.checks"]),
        ]),
        new("housekeeping", "Housekeeping", null, Coarse["housekeeping"],
        [
            F("housekeeping.board", "Board & tasks", true, ["housekeeping"], ["TASK", "BLOCK", "UNBLOCK", "STAYOVER_", "REPAIR_REQUEST"], "Cleaning tasks, inspections, room blocks"),
            F("housekeeping.complaints", "Complaints", false, [], ["GSV_COMPLAINT_"], "Guest complaints and service recovery"),
            F("housekeeping.lostfound", "Lost & found", false, [], ["GSV_LF_"], "Lost and found register"),
        ]),
        new("maintenance", "Maintenance", ConfigModuleIds.Maintenance, Coarse["maintenance"],
        [
            F("maintenance.workorders", "Work orders", false, ["maintenance"], ["MNT_WO_", "MNT_SPARE_", "MNT_VENDOR_"], "Work orders, parts and vendors"),
            F("maintenance.assets", "Assets, AMC & preventive plans", false, [], ["MNT_ASSET_", "MNT_AMC_", "MNT_PLAN_", "MNT_PM_"], "Asset register, AMCs and preventive maintenance", requires: ["maintenance.workorders"]),
        ]),
        new("guestsvc", "Guest services", null, Coarse["guestsvc"],
        [
            F("guestsvc.requests", "Guest requests", false, ["guest-services", "gsv-my-requests"], ["GSV_REQUEST_", "GSV_SETTINGS"], "Service requests with SLAs"),
            F("guestsvc.inhouse", "In-house list", true, ["inhouse"], [], "Who is in the hotel right now"),
            F("guestsvc.handover", "Checklists & handover", false, ["handover"], ["HOV_"], "Shift checklists and handover notes"),
        ]),
        new("messaging", "Guest messaging", ConfigModuleIds.Messaging, Coarse["messaging"],
        [
            F("messaging.messages", "Messages", false, ["messages"], ["MSG_SEND", "MSG_RETRY", "MSG_SIMULATE", "MSG_CONSENT", "MSG_RUN_RULES"], "WhatsApp / SMS / e-mail to guests"),
            F("messaging.templates", "Templates & rules", false, [], ["MSG_TEMPLATE", "MSG_RULE"], "Message templates and trigger rules", requires: ["messaging.messages"]),
        ]),
        new("documents", "Document capture", ConfigModuleIds.Documents, Coarse["documents"],
        [
            F("documents.capture", "ID capture", false, ["documents"], ["MSG_DOC_"], "Scan and file IDs and bills"),
        ]),
        new("customers", "Customers", null, Coarse["customers"],
        [
            F("customers.c360", "Customer 360", true, ["customers"], [], "Customer profiles and full stay history"),
            F("customers.crm", "CRM notes, tags & merge", false, [], [], "Tags, notes, preferences, consent, merge"),
        ]),
        new("revenue", "Revenue & sales", null, Coarse["revenue"],
        [
            F("revenue.yield", "Tariff & yield", false, ["yield", "meas-events", "meas-rateshop", "meas-displacement"], ["YLD_", "TARIFF_SCHEDULE"], "Rate plans, seasons, guardrails, overrides"),
            F("revenue.groups", "Groups & companies", false, ["groups"], ["GRP_"], "Group blocks, companies, credit limits (follows the Groups module switch)"),
            F("revenue.ota", "OTA channels", false, ["channels"], ["CHN_"], "OTA inbound bookings and rate push (follows the OTA module switch)", except: ["CHN_WEB_", "CHN_PAYOUT"]),
            F("revenue.payouts", "OTA payouts", false, [], ["CHN_PAYOUT"], "OTA payout receipts and commission", requires: ["revenue.ota"]),
            F("revenue.website", "Direct booking site", false, ["booking-site"], ["CHN_WEB_"], "Booking-engine requests (follows the Direct booking site module switch)"),
            F("revenue.tariffcard", "Tariff card", false, ["tariff-card"], ["TRC_"], "Published room tariff card: draft, propose, publish (F4-T1)"),
            F("revenue.feedback", "Guest feedback & QR", false, [], ["FBK_", "QW_"], "Feedback capture, follow-ups, QR printer (F4-T3)"),
            F("revenue.hall", "Hall booking", false, ["hall-booking"], ["HALL_"], "Venues, menus, enquiries, events, advances (F6)"),
            F("revenue.quotations", "Hall quotations", false, [], ["QUO_"], "Quotations, revisions, discounts (F6-H2)", requires: ["revenue.hall"]),
        ]),
        new("stock", "Stock & purchasing", null, Coarse["stock"],
        [
            F("stock.linen", "Linen & stock", false, ["inventory"], ["INV_"], "Linen, amenities, laundry and counts (follows the Linen & stock module switch)"),
            F("stock.purchases", "Purchases & vendor payments", false, [], ["EXP_VENDOR_"], "Vendor bills, payments, returns (ACC-2)", requires: ["cash.expenses"]),
            F("stock.assets", "Asset register", false, [], ["ACC_ASSET_"], "Fixed assets, put-to-use, disposal (ACC-2)"),
        ]),
        new("people", "People & payroll", null, Coarse["people"],
        [
            F("people.staff", "Staff, shifts & attendance", true, ["staff"], ["HR_STAFF_", "HR_ROSTER_", "HR_ATT_", "HR_HOLIDAY_", "HR_AGENDA_", "HR_REQUEST_"], "Staff register, roster, attendance"),
            F("people.leave", "Leave", false, [], ["HR_LEAVE_"], "Leave requests, approvals, entitlements"),
            F("people.payroll", "Payroll", false, [], ["HR_PAYROLL_", "HR_RATES_", "HR_ADVANCE_", "HR_PAYSLIP_", "HR_FNF_", "HR_STAT_"], "Pay runs, advances, payslips, F&F (follows the Payroll module switch)"),
            F("people.myhr", "My shifts & pay", true, ["my-hr"], [], "Own shifts, leave and payslips (DEC-061)"),
        ]),
        new("closing", "Day closing", null, Coarse["closing"],
        [
            F("closing.dayclose", "Day close", true, ["closing"], ["DAY_", "NIGHT_POST", "NEXT_DAY", "CLOSE_RUN"], "Night posting, day close and reopen"),
        ]),
        new("accounting", "Accounting", null, Coarse["accounting"],
        [
            F("accounting.ledgers", "Chart, ledgers & opening balances", true, ["accounting"], ["ACC_GROUP_", "ACC_LEDGER_", "ACC_OPENING_", "ACC_MAP_", "ACC_LOCK", "ACC_UNLOCK"], "Chart of accounts, party ledgers, period lock (ACC-1)"),
            F("accounting.vouchers", "Manual vouchers & loans", false, [], ["ACC_MANUAL_", "ACC_LOAN_"], "Journal, contra, receipt and payment vouchers; loans"),
            F("accounting.gst", "GST set-off & month lock", false, [], ["ACC_GST_", "ACC_MONTH_", "ACC_STAT_"], "GST set-off, payment, month lock"),
            F("accounting.tds", "TDS", false, [], ["ACC_TDS_"], "TDS by payee type, remittance"),
            F("accounting.tally", "Tally export", false, [], ["ACC_TALLY_"], "Tally masters and vouchers (DEC-140)"),
            F("accounting.yearend", "Year end & year lock", false, [], ["ACC_YEAR_"], "Carry forward, year lock and reopen (F5 ACC-4)"),
            F("accounting.bank", "Bank reconciliation", false, [], ["BANK_"], "Statement import, matching, settlements (F5 ACC-4)"),
        ]),
        new("reports", "Reports", null, Coarse["reports"],
        [
            F("reports.reports", "Reports", true, ["reports"], [], "Operations and finance reports"),
            F("reports.hub", "Reports hub", false, ["report-hub"], [], "Report catalogue on the reporting engine (F7)"),
            F("reports.digest", "Revenue & owner digest", false, ["owner-digest", "revenue-category"], ["DIGEST_"], "Revenue by category and the owner digest (F4-T2)"),
            F("reports.plans", "Meal plans & extras", false, ["plans-extras"], ["CAT_PLAN_"], "Plan and extras catalogue (MA-2)"),
        ]),
        new("compliance", "Compliance", null, Coarse["compliance"],
        [
            F("compliance.calendar", "Compliance calendar", false, ["compliance"], ["CMP_"], "Obligations, firm profile, evidence (DEC-119)"),
        ]),
        new("assistant", "Management assistant", ConfigModuleIds.Assistant, Coarse["assistant"],
        [
            F("assistant.assistant", "Assistant", false, ["assistant"], ["AI_"], "Questions about hotel data, anomaly alerts"),
        ]),
        new("admin", "Admin & setup", null, Coarse["admin"],
        [
            F("admin.access", "Administration", true, ["access"], ["USER", "GRANT", "POLICY_"], "Users, roles, modules & features, audit (ADM)"),
            F("admin.config", "Master configuration", true, ["settings"], ["SETTING", "CFG_", "TAX_SCHEDULE"], "Settings hub and module switches"),
            F("admin.masters", "Masters", false, [], ["MST_", "RMS_"], "Rooms, floors, lists and catalogues (MA waves)"),
            F("admin.approvals", "Approvals & audit", true, ["approvals"], [], "Pending requests and audit trail"),
            F("admin.backup", "Backup & recovery", true, ["backup"], ["BACKUP_"], "Backups and restore drill"),
            F("admin.health", "System health", true, ["syshealth"], ["SYS_"], "Parked events, retry, last backup"),
            F("admin.prefs", "Appearance & display", true, ["prefs"], [], "Per-device preferences"),
            F("admin.legacy", "Legacy import", false, ["legacy-import"], [], "Old Lodge software import (LG)"),
            F("admin.internet", "Internet access", false, [], ["NET_"], "Guest internet / egress control (F5)"),
            F("admin.roadmap", "Roadmap", false, ["roadmap"], [], "What is built and what is next"),
        ]),
    ];

    private static readonly Dictionary<string, (FeatureModule Module, FeatureDef Feature)> ById =
        Modules.SelectMany(m => m.Features.Select(f => (m, f))).ToDictionary(x => x.f.Id, x => (x.m, x.f), StringComparer.Ordinal);

    /// <summary>Every feature in module order.</summary>
    public static IEnumerable<FeatureDef> All => Modules.SelectMany(m => m.Features);

    /// <summary>Switchable (non-core) features in module order.</summary>
    public static IEnumerable<FeatureDef> Switchable => All.Where(f => !f.Core);

    public static FeatureDef? Feature(string id) => ById.TryGetValue(id, out var x) ? x.Feature : null;

    public static FeatureModule? ModuleOf(string featureId) => ById.TryGetValue(featureId, out var x) ? x.Module : null;

    /// <summary>Features that list <paramref name="id"/> in Requires (direct dependants).</summary>
    public static IEnumerable<FeatureDef> Dependants(string id) => All.Where(f => f.Requires.Contains(id, StringComparer.Ordinal));

    /// <summary><paramref name="id"/> plus every feature that transitively requires it (prototype <c>featureClosure</c>).</summary>
    public static IReadOnlyList<string> Closure(string id)
    {
        var outList = new List<string>();
        void Walk(string x)
        {
            if (outList.Contains(x, StringComparer.Ordinal)) return;
            outList.Add(x);
            foreach (var d in Dependants(x)) Walk(d.Id);
        }
        Walk(id);
        return outList;
    }

    /// <summary>Always-on reason shown on core rows (DEC-215).</summary>
    public const string CoreReason = "Always on: money, statutory or sign-in path (DEC-215).";

    /// <summary>Refusal text for a command of a switched-off sub-function (prototype <c>cfgFeatureOffMessage</c>).</summary>
    public static string FeatureOffMessage(FeatureDef f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var m = ModuleOf(f.Id)!;
        return $"{f.Label} ({m.Label}) is switched off in Master configuration › Modules & features. Its data is kept; the Owner or Tech admin can switch it back on.";
    }
}
