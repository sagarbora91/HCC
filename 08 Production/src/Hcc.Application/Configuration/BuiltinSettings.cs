using System.Reflection;
using System.Text.RegularExpressions;
using Hcc.Domain.Configuration;

namespace Hcc.Application.Configuration;

/// <summary>
/// The hub's own settings, ported from the prototype's built-in providers and legacy BL-027 keys
/// (<c>03 Prototype/src/domain/streams/config.ts</c> <c>builtins()</c>, <c>admin.ts</c> <c>ADMIN_SETTINGS</c>). Module-owned settings
/// (yield guardrails, guest-service SLAs, POS menu, payroll statutory rates, compliance, assistant) are registered by those modules
/// through <see cref="ISettingsProvider"/>; until then their groups show a read-only pointer line. Registered at the Wave 2 merge:
/// the GST master group (Tax: <c>GstSettings.Provider</c>, Money and tax) and Housekeeping's <c>inspection</c> (<c>HousekeepingSettings.Provider</c>).
/// Defaults are neutral production values (the prototype's fictional property texts are not carried over).
/// </summary>
public static partial class BuiltinSettings
{
    [GeneratedRegex(@"^[0-9A-Z]{15}$")]
    private static partial Regex Gstin();

    [GeneratedRegex(@"^[A-Z0-9-]{2,8}$")]
    private static partial Regex InvoicePrefix();

    private static ConfigValue T(string s) => ConfigValue.Of(s);

    private static ConfigValue N(long n) => ConfigValue.Of(n);

    private static ConfigValue B(bool b) => ConfigValue.Of(b);

    private static SettingDefinition Pointer(string key, string label, string text, string reason) =>
        new(key, label, SettingType.Fixed) { FixedText = text, FixedReason = reason, Editable = false };

    public static string ServerVersion { get; } =
        typeof(BuiltinSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

    public static IReadOnlyList<ISettingsProvider> Providers() =>
    [
        new SettingsProvider("property", ConfigGroupIds.Property,
        [
            new("hotel", "Property display name", SettingType.Text)
            {
                Unit = "text, ≤ 60 chars", Help = "Shown in the header, sidebar and documents.", MaxLength = 60, Default = T(string.Empty),
                Affects = "Header, sidebar, documents and the export file name.",
            },
            new("brand.address", "Address on documents", SettingType.Text)
            {
                MaxLength = 120, Default = T(string.Empty), Unit = "text, ≤ 120 chars", Help = "Printed under the property name.",
                Affects = "Invoices, folios and confirmations generated after the change.",
            },
            new("brand.gstin", "GSTIN on documents", SettingType.Text)
            {
                MaxLength = 15, AllowEmpty = true, Default = T(string.Empty), Pattern = Gstin(),
                PatternMessage = "A GSTIN has 15 capital letters and digits.", Unit = "15 characters, optional",
                Help = "Printed on invoices; the GST portal is not checked.", Affects = "Document headers generated after the change.",
            },
            new("brand.invoiceHeader", "Invoice header line", SettingType.Text) { MaxLength = 80, AllowEmpty = true, Default = T(string.Empty), Unit = "text, ≤ 80 chars", Help = "Second line of the invoice header." },
            new("brand.invoiceFooter", "Invoice footer", SettingType.Text) { MaxLength = 120, Default = T("Thank you for staying with us."), Unit = "text, ≤ 120 chars", Help = "Last line of invoices and receipts." },
            new("brand.printFormat", "Print format", SettingType.Choice) { Options = [T("A4"), T("A5"), T("80 mm thermal")], Default = T("A4"), Unit = "choice", Help = "Default paper for invoices and receipts." },
            new("brand.language", "Document language", SettingType.Choice) { Options = [T("English"), T("English + Hindi labels")], Default = T("English"), Unit = "choice", Help = "Labels on printed documents." },
        ]),
        new SettingsProvider("rooms", ConfigGroupIds.Rooms,
        [
            Pointer("rooms.master", "Room types, rooms and tariffs", "Maintained in Inventory (room types, rooms, dated tariffs)", "Rooms are masters in Inventory; tariffs are dated (Owner only)"),
        ]),
        new SettingsProvider("frontdesk", ConfigGroupIds.FrontDesk,
        [
            new("checkin", "Standard check-in time", SettingType.Time) { Unit = "HH:mm (IST)", Default = T("14:00"), Help = "Affects arrivals expected today.", Affects = "Arrivals expected today and printed confirmations. Booking dates and charges never change." },
            new("checkout", "Standard checkout time", SettingType.Time) { Unit = "HH:mm (IST)", Default = T("11:00"), Help = "Affects in-house stays departing today.", Affects = "In-house stays departing today and printed folios. Booking dates and charges never change." },
            new("fd.holdHours", "Unconfirmed hold expiry", SettingType.Integer) { Min = 1, Max = 72, Default = N(6), Suffix = "h", Unit = "hours", Effective = EffectiveMode.NextDay, Help = "Holds without an advance are released after this long.", Affects = "Holds created from the next business day." },
            new("fd.deposit", "Refundable deposit at check-in", SettingType.Money) { Money = true, Min = 0, Max = 2_000_000, Default = N(100_000), Unit = "₹ per stay", Effective = EffectiveMode.NextDay, Dated = true, Help = "Suggested deposit shown at check-in.", Affects = "Check-ins from the next business day; deposits already held are unchanged." },
            new("fd.idRequired", "ID documents required", SettingType.Choice) { Options = [T("Lead guest only"), T("Every adult guest")], Default = T("Every adult guest"), Unit = "choice", Help = "Who must show an ID document at registration.", Affects = "Registrations from now on." },
            new("fd.noShowTime", "No-show cut-off", SettingType.Time) { Default = T("23:00"), Unit = "HH:mm (IST)", Effective = EffectiveMode.NextDay, Help = "Confirmed arrivals not checked in by this time may be marked no-show.", Affects = "Arrivals from the next business day." },
        ]),
        new SettingsProvider("money", ConfigGroupIds.Money,
        [
            new("discountLimit", "Discount threshold without approval (Reception)", SettingType.Money)
            {
                Unit = "₹ per request", Max = 10_000_000, Default = N(25_000), Effective = EffectiveMode.Date, Dated = true,
                Help = "Above this, another authorised user must approve (PERM-FOL-003).",
                Affects = "Discounts above this need another authorised user’s approval (PERM-FOL-003). Pending requests are not re-evaluated.",
            },
            new("approvalExpiryHours", "Approval request expiry", SettingType.Integer)
            {
                Unit = "hours", Min = 1, Max = 168, Default = N(24), Suffix = "h", Show = v => v + " h",
                Help = "Requests expire after this long (RBAC §7 default 24 h).", Affects = "New approval requests expire after this many hours; existing requests keep their expiry.",
            },
            new("invoicePrefix", "Invoice number prefix", SettingType.Text)
            {
                Default = T("INV"), Pattern = InvoicePrefix(), PatternMessage = "Use 2–8 capital letters, digits or hyphens.", MaxLength = 8,
                Unit = "2–8 capitals, digits or hyphen", Help = "Printed before the invoice number.", Affects = "Documents generated after the change.",
            },
            Pointer("currency", "Currency and money unit", "INR · paise", "Not configurable"),
            Pointer("managerLimits", "Role limits (discount, rate override, cash difference, credit, expenses)", "Set per role in Access control", "Edited in Access control → Role templates (PERM-ADM-002)"),
        ]),
        new SettingsProvider("pos", ConfigGroupIds.Pos,
        [
            Pointer("pos.outlets", "Outlets and menu", "Set up in Restaurant & POS", "Outlets and menu are masters in Restaurant & POS"),
        ]),
        new SettingsProvider("hkm", ConfigGroupIds.Housekeeping,
        [
            // "inspection" is registered by the Housekeeping module (HousekeepingSettings.Provider) and read by its commands.
            new("hk.maxRoomsPerAttendant", "Rooms per attendant (guide)", SettingType.Integer) { Min = 4, Max = 20, Default = N(12), Suffix = "rooms", Unit = "rooms per shift", Help = "Planning guide shown on the housekeeping board.", Affects = "Planning hints only." },
            new("mnt.pmLeadDays", "Preventive maintenance lead time", SettingType.Integer) { Min = 1, Max = 30, Default = N(3), Suffix = "days", Unit = "days", Effective = EffectiveMode.Date, Dated = true, Help = "Preventive work orders are listed this many days ahead.", Affects = "Preventive plans generated after the change." },
        ]),
        new SettingsProvider("people", ConfigGroupIds.People,
        [
            Pointer("hr.statutory", "Statutory rates and leave policy", "Dated PF / ESIC / PT rates and leave policy come from Payroll", "Registered by the Payroll module (dated rate versions)"),
        ]),
        new SettingsProvider("compliance", ConfigGroupIds.Compliance,
        [
            Pointer("cmp.obligations", "Compliance obligations", "GST, TDS, PF, ESIC and PT due rules come from the Compliance calendar", "Dated rules — edited in the Compliance calendar"),
        ]),
        new SettingsProvider("modules", ConfigGroupIds.Modules,
            ConfigCatalog.Modules.Select(m => new SettingDefinition(ConfigModuleIds.SettingKey(m.Id), m.Label, SettingType.Boolean)
            {
                Unit = "on / off", Help = m.Note, Default = B(true),
                Affects = "Menus, tiles, search, reminders and phone tabs; commands are refused while off. Data is kept.",
            }).ToList()),
        new SettingsProvider("users", ConfigGroupIds.Users,
        [
            Pointer("users.access", "Users and roles", "Create, disable and assign roles in Access control", "Users and roles are changed in Access control"),
            new("sec.pinLock", "PIN lock on shared devices", SettingType.Boolean) { Default = B(false), Approver = SettingApprover.Owner, Unit = "on / off", Help = "Off by default; switch on deliberately (SCC rule).", Affects = "Next sign-in on every device." },
            new("sec.pinIdleMinutes", "Lock after idle", SettingType.Integer) { Min = 1, Max = 60, Default = N(5), Suffix = "min", Approver = SettingApprover.Owner, Unit = "minutes", Help = "Used when the PIN lock is on.", Affects = "Next sign-in on every device." },
        ]),
        new SettingsProvider("messages", ConfigGroupIds.Messages,
        [
            new("msg.senderName", "Sender name", SettingType.Text) { MaxLength = 40, Default = T(string.Empty), Unit = "text, ≤ 40 chars", Help = "Shown on WhatsApp, SMS and e-mail messages." },
            new("msg.marketing", "Marketing messages", SettingType.Choice) { Options = [T("Only with guest consent"), T("Never")], Default = T("Only with guest consent"), Unit = "choice", Help = "Service messages are not affected." },
            new("msg.gatewayKey", "Messaging gateway key", SettingType.Text) { MaxLength = 64, AllowEmpty = true, Default = T(string.Empty), Secret = true, Approver = SettingApprover.Owner, Unit = "secret", Help = "Never shown in full and never exported.", Affects = "Messages sent after the change." },
            new("doc.retentionDays", "Captured document retention", SettingType.Integer) { Min = 30, Max = 3650, Default = N(365), Suffix = "days", Unit = "days", Help = "Captured images are discarded after this (metadata stays)." },
        ]),
        new SettingsProvider("data", ConfigGroupIds.Data,
        [
            new("data.backupTime", "Daily full backup time", SettingType.Time) { Default = T("02:30"), Unit = "HH:mm (IST)", Help = "Task Scheduler full backup (deployment default 02:30)." },
            new("data.backupKeep", "Backups kept", SettingType.Integer) { Min = 1, Max = 90, Default = N(30), Suffix = "days", Unit = "days", Help = "Deployment default: 30 days, at least 3 full backups." },
            new("data.restoreDrillDays", "Restore drill every", SettingType.Integer) { Min = 7, Max = 180, Default = N(7), Suffix = "days", Unit = "days", Help = "Reminder to test a restore (deployment default: weekly)." },
            Pointer("about.version", "Version", "Server " + ServerVersion, "Set by the build"),
        ]),
    ];
}
