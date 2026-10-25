namespace Hcc.Domain.Configuration;

/// <summary>One of the 12 fixed groups of Master configuration (DEC-057; names pinned by tests, as in the prototype <c>CFG_GROUPS</c>).</summary>
public sealed record ConfigGroup(string Id, string Label, string Section, bool Money, string Blurb);

/// <summary>A switchable module (DEC-057). Core modules are not listed and can never be switched off.</summary>
public sealed record ConfigModule(string Id, string Label, string Note);

/// <summary>Group ids (prototype <c>CfgGroupId</c>).</summary>
public static class ConfigGroupIds
{
    public const string Property = "property";
    public const string Rooms = "rooms";
    public const string FrontDesk = "frontdesk";
    public const string Money = "money";
    public const string Pos = "pos";
    public const string Housekeeping = "hkm";
    public const string People = "people";
    public const string Compliance = "compliance";
    public const string Modules = "modules";
    public const string Users = "users";
    public const string Messages = "messages";
    public const string Data = "data";
}

/// <summary>Switchable module ids (prototype <c>CfgModuleId</c>). Setting key of each switch = <c>module.&lt;id&gt;</c>.</summary>
public static class ConfigModuleIds
{
    public const string Pos = "pos";
    public const string Groups = "groups";
    public const string Ota = "ota";
    public const string Website = "website";
    public const string Messaging = "messaging";
    public const string Documents = "documents";
    public const string Assistant = "assistant";
    public const string Inventory = "inventory";
    public const string Maintenance = "maintenance";
    public const string Payroll = "payroll";

    public static string SettingKey(string moduleId) => "module." + moduleId;
}

/// <summary>Groups, sections and switchable modules of Master configuration — ported from <c>03 Prototype/src/domain/streams/config.ts</c>.</summary>
public static class ConfigCatalog
{
    public static IReadOnlyList<string> Sections { get; } = ["Business setup", "Operations and people", "Software and security"];

    public static IReadOnlyList<ConfigGroup> Groups { get; } =
    [
        new(ConfigGroupIds.Property, "Property and branding", "Business setup", false, "Name, address, GSTIN, invoice header and footer, print format, language"),
        new(ConfigGroupIds.Rooms, "Rooms and rate plans", "Business setup", true, "Room types, dated tariffs, rate guardrails and restrictions"),
        new(ConfigGroupIds.FrontDesk, "Front-desk policies", "Business setup", false, "Check-in and checkout times, hold expiry, deposit, ID rules, no-show, guest-service SLAs"),
        new(ConfigGroupIds.Money, "Money and tax", "Business setup", true, "Dated GST master (rooms, restaurant, services), discount and approval limits, payment methods, document numbering, till model"),
        new(ConfigGroupIds.Pos, "Restaurant and POS", "Business setup", true, "Outlets, menu availability and KOT"),
        new(ConfigGroupIds.Housekeeping, "Housekeeping and maintenance", "Operations and people", false, "Inspection, dirty-room alert, attendant load, preventive maintenance"),
        new(ConfigGroupIds.People, "People, payroll and leave", "Operations and people", true, "Departments, dated PF / ESIC / PT rates, leave policy, weekly offs"),
        new(ConfigGroupIds.Compliance, "Compliance obligations", "Operations and people", true, "GST, TDS, PF, ESIC and PT list, due rules, CA contact"),
        new(ConfigGroupIds.Modules, "Modules on/off", "Software and security", false, "Switch optional modules off; their data is kept"),
        new(ConfigGroupIds.Users, "Users, roles and PINs", "Software and security", false, "Users and roles live in Access control; PIN lock options"),
        new(ConfigGroupIds.Messages, "Messages and documents", "Software and security", false, "Sender name, consent, gateway key, document retention"),
        new(ConfigGroupIds.Data, "Data and system", "Software and security", false, "Backup schedule, restore drill, storage, diagnostics, about"),
    ];

    public static IReadOnlyList<ConfigModule> Modules { get; } =
    [
        new(ConfigModuleIds.Pos, "Restaurant & POS", "Restaurant and room-service checks, KOT, menu"),
        new(ConfigModuleIds.Groups, "Groups & companies", "Group blocks, companies and credit limits"),
        new(ConfigModuleIds.Ota, "OTA channels", "Simulated OTA inbound bookings and rate push"),
        new(ConfigModuleIds.Website, "Direct booking site", "Simulated booking-engine requests"),
        new(ConfigModuleIds.Messaging, "Guest messaging", "Simulated WhatsApp / SMS / e-mail and templates"),
        new(ConfigModuleIds.Documents, "Document capture", "Scan and file IDs and bills (simulated OCR)"),
        new(ConfigModuleIds.Assistant, "Management assistant", "Questions about hotel data, anomaly alerts"),
        new(ConfigModuleIds.Inventory, "Linen & stock", "Linen, amenities, laundry and counts"),
        new(ConfigModuleIds.Maintenance, "Maintenance", "Work orders, assets, AMC and preventive plans"),
        new(ConfigModuleIds.Payroll, "Payroll", "Pay runs, advances, payslips (attendance and leave stay on)"),
    ];

    /// <summary>Always on (DEC-057).</summary>
    public static IReadOnlyList<string> CoreModules { get; } = ["Front desk", "Guests & folios", "Cashier", "Day closing", "Access control", "Master configuration"];

    public static ConfigGroup? Group(string id) => Groups.FirstOrDefault(g => g.Id == id);

    public static ConfigModule? Module(string id) => Modules.FirstOrDefault(m => m.Id == id);

    /// <summary>Refusal text for a command of a switched-off module (prototype <c>cfgModuleOffMessage</c>).</summary>
    public static string ModuleOffMessage(ConfigModule m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return $"{m.Label} is switched off in Master configuration › Modules on/off. Its data is kept; the Owner or Tech admin can switch it back on.";
    }
}
