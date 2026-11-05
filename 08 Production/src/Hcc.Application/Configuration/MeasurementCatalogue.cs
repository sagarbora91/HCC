// F12 MEAS · meas-settings (MEAS-CONTRACT §1.2, §1.5). Static key table mirrored from the prototype's MEAS_ITEMS
// (03 Prototype/src/domain/streams/measure.ts); tests/stream-meas-settings.mjs and Hcc.Meas.Tests pin the two tables equal (parity).
using Hcc.Domain.Configuration;

namespace Hcc.Application.Configuration;

/// <summary>One switchable measurement item: key <c>meas.&lt;pack&gt;.&lt;item&gt;</c>; <see cref="Core"/> items are always on;
/// <see cref="Module"/> = optional module the item needs (off module = item off).</summary>
public sealed record MeasurementItem(string Key, string Pack, string Label, string? ReportId = null, bool Core = false, string? Module = null);

public sealed record MeasurementTarget(string Key, string Label, string Kind, long Default);

public static class MeasurementCatalogue
{
    /// <summary>Configuration group id (prototype CfgGroupId 'measure'). The server ConfigCatalog does not list it yet (integration item).</summary>
    public const string GroupId = "measure";
    public const string HighBalanceKey = "meas.daily.high-balance";
    /// <summary>Company credit = Groups &amp; companies module: when it is on, High-balance is core (contract §1.3).</summary>
    public const string CreditModule = "groups";
    public const string OffReason = "MEAS_OFF";
    public const string PackOffReason = "MEAS_PACK_OFF";

    public static IReadOnlyList<string> Packs { get; } = ["daily", "revenue", "guest", "hk", "people", "study"];

    public static string PackKey(string pack) => "meas.pack." + pack;

    private static MeasurementItem I(string pack, string key, string label, string? report = null, bool core = false, string? module = null) => new(key, pack, label, report, core, module);

    public static IReadOnlyList<MeasurementItem> Items { get; } =
    [
        I("daily", "meas.daily.flash", "Manager's flash", "rpt.guests.manager-flash", core: true),
        I("daily", "meas.daily.regrets", "Regrets and denials", "rpt.guests.regrets-denials"),
        I("daily", "meas.daily.rate-variance", "Rate variance / rate check", "rpt.guests.rate-variance"),
        I("daily", "meas.daily.discrepancy", "Room discrepancy", "rpt.rooms.room-discrepancy"),
        I("daily", "meas.daily.high-balance", "High-balance and credit-limit", "rpt.money.high-balance"),
        I("daily", "meas.daily.adjustments", "Allowances, rebates, voids and corrections", "rpt.money.allowances-voids"),
        I("daily", "meas.daily.no-show", "No-show, cancellation and early departure", "rpt.guests.no-show-cancel"),
        I("daily", "meas.daily.inventory", "Complimentary, house-use and out-of-order", "rpt.rooms.inventory-exceptions", core: true),
        I("daily", "meas.daily.cashier", "Cashier and shift variance", "rpt.money.cashier-variance"),
        I("revenue", "meas.rev.demand-calendar", "Demand calendar", "rpt.revenue.demand-calendar"),
        I("revenue", "meas.rev.forecast", "Occupancy forecast curve"),
        I("revenue", "meas.rev.pickup", "Pickup and pace", "rpt.revenue.pickup-pace"),
        I("revenue", "meas.rev.displacement", "Displacement calculator", "rpt.revenue.displacement"),
        I("revenue", "meas.rev.overbooking", "Overbooking and wash", "rpt.revenue.overbooking-wash"),
        I("revenue", "meas.rev.restrictions", "Length-of-stay and minimum-stay controls", "rpt.revenue.restriction-effect"),
        I("revenue", "meas.rev.rate-shop", "Rate shop and parity", "rpt.revenue.rate-parity"),
        I("guest", "meas.guest.survey", "Guest satisfaction and NPS", "rpt.guests.satisfaction"),
        I("guest", "meas.guest.reputation", "Online reputation", "rpt.guests.reputation-index"),
        I("guest", "meas.guest.recovery", "Service recovery", "rpt.guests.service-recovery"),
        I("guest", "meas.guest.preferences", "Guest preference profile"),
        I("guest", "meas.guest.desk-timing", "Arrival and departure experience", "rpt.guests.desk-timing"),
        I("guest", "meas.guest.upsell", "Upsell and upgrade", "rpt.guests.upsell"),
        I("guest", "meas.guest.repeat", "Repeat guests and loyalty base", "rpt.guests.repeat-loyalty"),
        I("hk", "meas.hk.productivity", "Attendant productivity and turnaround", "rpt.rooms.hk-productivity"),
        I("hk", "meas.hk.ooo", "Out-of-order and lost inventory", "rpt.rooms.ooo-days"),
        I("hk", "meas.hk.ppm", "Preventive maintenance compliance", "rpt.rooms.ppm-compliance", module: "maintenance"),
        I("hk", "meas.hk.utilities", "Energy, water and LPG per occupied room-night", "rpt.rooms.utilities-per-room", module: "maintenance"),
        I("hk", "meas.hk.linen", "Linen, laundry and guest-supply cost per occupied room", "rpt.stock.linen-cost", module: "inventory"),
        I("hk", "meas.hk.assets", "Asset register, FF&E reserve and renovation cycle", "rpt.acc.asset-life"),
        I("people", "meas.people.productivity", "Staff per occupied room, overtime, absenteeism", "rpt.people.staff-productivity", module: "payroll"),
        I("people", "meas.people.attrition", "Attrition and tenure", "rpt.people.attrition", module: "payroll"),
        I("people", "meas.people.scorecard", "Performance by desk user", "rpt.people.agent-scorecard", module: "payroll"),
        I("people", "meas.people.training", "Training hours and certifications", "rpt.people.training", module: "payroll"),
        I("people", "meas.people.pulse", "Staff pulse (optional)", "rpt.people.staff-pulse", module: "payroll"),
        I("study", "meas.study.revenue-monthly", "Monthly revenue and cash (FY history)", "rpt.study.revenue-monthly"),
        I("study", "meas.study.year-comparison", "Year comparison", "rpt.study.year-comparison"),
        I("study", "meas.study.covid-index", "Index vs a base year", "rpt.study.index-vs-base"),
        I("study", "meas.study.deep-dive", "FY deep dive", "rpt.study.fy-deep-dive"),
        I("study", "meas.study.occupancy", "Occupancy history", "rpt.study.occupancy-history"),
        I("study", "meas.study.patterns", "Operations patterns", "rpt.study.operations-patterns"),
        I("study", "meas.study.rooms-rates", "Rooms, rates and discounts", "rpt.study.rooms-and-rates"),
        I("study", "meas.study.zero-rate", "Zero-rate and complimentary stays", "rpt.study.zero-rate-complimentary"),
        I("study", "meas.study.receivables", "Receivables and bad debt", "rpt.study.receivables-bad-debt"),
        I("study", "meas.study.rm-kpi", "Occupancy, ADR, RevPAR by month, FY, type, channel", "rpt.study.revenue-kpi"),
        I("study", "meas.study.rm-channel", "Channel economics and net ADR", "rpt.study.channel-economics"),
        I("study", "meas.study.rm-pricing", "Pricing shape", "rpt.study.pricing-shape"),
        I("study", "meas.study.guest-repeat", "Repeat guests and lifetime value", "rpt.study.guest-repeat-value"),
        I("study", "meas.study.guest-origin", "Guest origin, nationality, meal plan", "rpt.study.guest-origin-plan"),
        I("study", "meas.study.hk-ops", "Housekeeping operations", "rpt.study.housekeeping-operations"),
        I("study", "meas.study.fnb-halls", "F&B, halls and ancillary", "rpt.study.fnb-halls-ancillary"),
        I("study", "meas.study.finance", "Finance and cost", "rpt.study.finance-cost"),
        I("study", "meas.study.sales", "Corporate accounts and win-back", "rpt.study.corporate-accounts"),
        I("study", "meas.study.compliance", "Compliance and risk", "rpt.study.compliance-risk"),
        I("study", "meas.study.forecast", "Seasonality, forecast and scenarios", "rpt.study.forecast-scenarios"),
        I("study", "meas.study.dashboard", "Owner 12-KPI monthly dashboard", "rpt.study.owner-dashboard"),
        I("study", "meas.study.management-pack", "Management study pack (PDF + Excel)", "rpt.study.management-pack"),
    ];

    /// <summary>M-9 owner defaults (contract §1.2): percent kinds are whole percent, money in paise.</summary>
    public static IReadOnlyList<MeasurementTarget> Targets { get; } =
    [
        new("meas.target.occupancy", "Occupancy target", "percent", 35),
        new("meas.target.adr", "ADR target (before GST)", "money", 205000),
        new("meas.target.nps", "NPS target", "integer", 30),
        new("meas.target.repeatShare", "Repeat-guest share target", "percent", 25),
        new("meas.target.lateDeparture", "Uncharged late departures (limit)", "percent", 10),
        new("meas.target.ot", "Overtime share (limit)", "percent", 10),
        new("meas.target.highBalance", "High balance (walk-in without a company)", "money", 500000),
        new("meas.target.cashVariance", "Cash variance per shift (limit)", "money", 10000),
        new("meas.target.checkinMinutes", "Check-in time standard", "minutes", 8),
        new("meas.target.checkoutMinutes", "Check-out time standard", "minutes", 6),
        new("meas.target.absenteeism", "Absenteeism (limit)", "percent", 5),
        new("meas.target.attrition", "Annual attrition (limit)", "percent", 30),
        new("meas.target.lossPct", "Linen / supply loss (limit)", "percent", 2),
    ];

    private static readonly Dictionary<string, MeasurementItem> ByKey = Items.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public static MeasurementItem? Item(string key) => key is not null && ByKey.TryGetValue(key, out var i) ? i : null;

    /// <summary>Core in this state: always for a core item; High-balance once company credit is on.</summary>
    public static bool IsCore(MeasurementItem item, bool creditOn)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Core || (item.Key == HighBalanceKey && creditOn);
    }

    /// <summary>The settings of the group (Owner only, immediate). Registered only once ConfigCatalog lists the group (integration item).</summary>
    public static IReadOnlyList<SettingDefinition> Definitions()
    {
        static SettingDefinition Flag(string key, string label, bool def) => new(key, label, SettingType.Boolean)
        {
            Unit = "on / off", Default = ConfigValue.Of(def), Approver = SettingApprover.Owner, Money = true, Effective = EffectiveMode.Immediate,
            Affects = "Measurement reports, their capture forms and the Owner digest; data already captured is kept.",
        };
        var list = new List<SettingDefinition>();
        list.AddRange(Packs.Select(p => Flag(PackKey(p), "Pack: " + p, true)));
        list.AddRange(Items.Select(i => Flag(i.Key, i.Label, true)));
        list.AddRange(Targets.Select(t => new SettingDefinition(t.Key, t.Label, t.Kind == "money" ? SettingType.Money : SettingType.Integer)
        {
            Default = ConfigValue.Of(t.Default), Approver = SettingApprover.Owner, Money = true, Effective = EffectiveMode.Immediate,
            Min = t.Kind == "integer" ? -100 : 0, Max = t.Kind == "money" ? 10_000_000 : t.Kind == "minutes" ? 1000 : 100,
        }));
        list.Add(new SettingDefinition("meas.study.packSchedule", "Management study pack schedule", SettingType.Choice)
        {
            Default = ConfigValue.Of("monthly"), Options = [ConfigValue.Of("off"), ConfigValue.Of("monthly"), ConfigValue.Of("quarterly")], Approver = SettingApprover.Owner, Money = true,
        });
        list.Add(Flag("meas.daily.blockClose", "Day close waits for open measurement exceptions", false));
        list.Add(Flag("meas.regrets.mandatory", "Turn-away reason is mandatory", false));
        list.Add(Flag("yld.restrictionsEnforce", "Enforce yield restrictions (min stay / CTA)", false));
        list.Add(new SettingDefinition("ffe.reservePct", "FF&E reserve", SettingType.Integer) { Default = ConfigValue.Of(3L), Min = 0, Max = 20, Suffix = "%", Approver = SettingApprover.Owner, Money = true });
        return list;
    }

    /// <summary>The provider for <see cref="ISettingsProvider"/> DI registration, or null while the server catalogue has no 'measure' group.</summary>
    public static ISettingsProvider? Provider() => ConfigCatalog.Group(GroupId) is null ? null : new SettingsProvider("measure", GroupId, Definitions());
}
