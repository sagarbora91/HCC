using System.Globalization;
using Hcc.Domain.Maintenance;

namespace Hcc.Maintenance.Tests.Harness;

/// <summary>
/// The prototype's module-15 BASE fixture (03 Prototype/src/fixtures/ext/maintenance.ts, fictional data) as plain specs, used by the
/// kernel tests (in-memory) and the SQL seed. Prototype clock 2026-10-24T08:00, business date 2026-10-24 (treated as UTC).
/// </summary>
public static class GoldenFixture
{
    public const string BusinessDate = "2026-10-24";
    public const string Clock = "2026-10-24T08:00:00";

    public sealed record VendorSpec(string Code, string Name, string Trade, string Phone);

    public sealed record AssetSpec(string Code, string Name, string Kind, string? Room, string? Area, string Make, string Model, string Installed, string? Vendor, string Status);

    public sealed record SpareSpec(string Code, string Name, string Unit, int Stock, int ReorderAt, long Cost);

    public sealed record PlanSpec(string Code, string Asset, string Title, int Every, string NextDue, string LastDone);

    public sealed record AmcSpec(string Code, string Vendor, string Scope, string From, string To, int Visits, long Value);

    public sealed record PartSpec(string Spare, int Qty, long Cost);

    public sealed record WoSpec(string Code, string Kind, string Title, string Priority, string Status, string? Room, string? Asset, string? Area, string? Assignee, string? Vendor,
        string Created, string CreatedBy, string? Started = null, string? Done = null, string? DoneBy = null, string? Verified = null, string? VerifiedBy = null,
        string? Hold = null, string? AwaitingSpare = null, string? Note = null, string? Plan = null, string? Due = null, string? DtFrom = null, string? DtTo = null, PartSpec[]? Parts = null);

    public static readonly VendorSpec[] Vendors =
    [
        new("VEN-01", "Sheetal Cooling Services", "AC & refrigeration", "+91 90000 11201"),
        new("VEN-02", "Unnati Lifts & Elevators", "Lift", "+91 90000 11202"),
        new("VEN-03", "Urjadhara Power Systems", "Generator / electrical", "+91 90000 11203"),
        new("VEN-04", "Jalsetu Plumbing Works", "Plumbing & water", "+91 90000 11204"),
    ];

    public static readonly AmcSpec[] Amcs =
    [
        new("AMC-01", "VEN-02", "Passenger lift — monthly service, breakdown calls", "2026-04-01", "2027-03-31", 12, 4_800_000),
        new("AMC-02", "VEN-03", "62.5 kVA DG set — quarterly service", "2026-01-01", "2026-12-31", 4, 3_000_000),
        new("AMC-03", "VEN-01", "All split ACs — filter, gas check, breakdowns", "2025-11-01", "2026-10-31", 4, 9_600_000),
    ];

    private static AssetSpec Ac(string room, string inst, string status = "Working") => new("AC-" + room, $"Split AC 1.5 T · {room}", "AC", room, null, "Himsheetal", "HS-18 Inverter", inst, "VEN-01", status);

    private static AssetSpec Gy(string room, string status = "Working") => new("GY-" + room, $"Geyser 25 L · {room}", "Geyser", room, null, "Garmjal", "GJ-25 Storage", "2023-03-10", "VEN-04", status);

    private static AssetSpec Tv(string room) => new("TV-" + room, $"LED TV 43\" · {room}", "TV", room, null, "Drishya", "D43 Smart", "2024-06-01", null, "Working");

    public static readonly AssetSpec[] Assets =
    [
        Ac("101", "2022-04-12"), Ac("103", "2022-04-12"), Ac("106", "2022-04-12"), Ac("203", "2023-05-02"), Ac("205", "2023-05-02", "Faulty"), Ac("306", "2024-02-20"),
        Gy("104"), Gy("106", "Faulty"), Gy("205"), Tv("103"), Tv("201"), Tv("303"),
        new("LFT-1", "Passenger lift (6 persons)", "Lift", null, "Lobby", "Unnati", "UL-6 MRL", "2021-09-01", "VEN-02", "Faulty"),
        new("DG-1", "Diesel generator 62.5 kVA", "Generator", null, "Basement", "Urjadhara", "UD-62", "2021-08-15", "VEN-03", "Working"),
        new("FP-1", "Fire hydrant pump", "Pump", null, "Basement", "Jalshakti", "FH-10", "2021-08-15", "VEN-04", "Working"),
        new("RO-1", "RO water plant 250 LPH", "RO plant", null, "Kitchen", "Nirmalneer", "NN-250", "2022-01-20", "VEN-04", "Working"),
        new("AC-LOB", "Lobby ducted AC 5.5 T", "AC", null, "Lobby", "Himsheetal", "HS-D55", "2021-09-01", "VEN-01", "Working"),
    ];

    public static readonly SpareSpec[] Spares =
    [
        new("SP-01", "AC filter mesh", "pcs", 6, 4, 35_000), new("SP-02", "R32 gas refill", "kg", 2, 2, 180_000), new("SP-03", "AC run capacitor 35 µF", "pcs", 0, 2, 65_000),
        new("SP-04", "Geyser thermostat", "pcs", 3, 2, 65_000), new("SP-05", "Geyser heating element 2 kW", "pcs", 2, 1, 90_000), new("SP-06", "TV remote (universal)", "pcs", 3, 2, 45_000),
        new("SP-07", "Fuse 10 A", "pcs", 20, 10, 4_000), new("SP-08", "AC drain pipe", "m", 9, 5, 6_000), new("SP-09", "DG diesel filter", "pcs", 1, 1, 125_000),
    ];

    public static readonly PlanSpec[] Plans =
    [
        new("PM-01", "DG-1", "Generator weekly test run (15 min on load)", 7, "2026-10-24", "2026-10-17"),
        new("PM-02", "FP-1", "Fire pump weekly run test", 7, "2026-10-24", "2026-10-19"),
        new("PM-03", "AC-101", "AC filter cleaning", 30, "2026-10-25", "2026-09-25"),
        new("PM-04", "GY-106", "Geyser descaling & anode check", 180, "2026-10-26", "2026-04-29"),
        new("PM-05", "LFT-1", "Lift monthly AMC service visit", 30, "2026-10-28", "2026-09-28"),
        new("PM-06", "RO-1", "RO plant membrane & filter check", 90, "2026-11-15", "2026-08-17"),
        new("PM-07", "AC-LOB", "Lobby AC coil cleaning", 90, "2026-12-01", "2026-09-02"),
    ];

    public static readonly WoSpec[] Orders =
    [
        new("WO-0901", "Corrective", "AC not cooling — capacitor failed", "P2", "OnHold", "205", "AC-205", null, "maint", null, "2026-10-22T19:10:00", "supervisor",
            Started: "2026-10-23T10:00:00", Hold: "Capacitor ordered from Sheetal Cooling", AwaitingSpare: "SP-03", Parts: [new("SP-02", 1, 180_000)]),
        new("WO-0902", "Corrective", "Lift door sensor intermittent — doors reopen", "P1", "InProgress", null, "LFT-1", "Lobby", "maint", null, "2026-10-24T07:20:00", "manager", Started: "2026-10-24T07:40:00"),
        new("WO-0903", "Corrective", "Geyser not heating", "P3", "Open", "106", "GY-106", null, null, null, "2026-10-23T21:30:00", "reception"),
        new("WO-0904", "Corrective", "TV shows “No signal”", "P3", "Done", "103", "TV-103", null, "maint", null, "2026-10-23T18:00:00", "supervisor",
            Started: "2026-10-24T07:30:00", Done: "2026-10-24T07:55:00", DoneBy: "maint", Note: "HDMI cable reseated; set-top box reset; channels checked"),
        new("WO-0905", "Corrective", "Bathroom exhaust fan noisy", "P4", "Verified", "104", null, null, "maint", null, "2026-10-20T10:00:00", "supervisor",
            Started: "2026-10-20T13:00:00", Done: "2026-10-20T15:00:00", DoneBy: "maint", Verified: "2026-10-20T17:00:00", VerifiedBy: "supervisor", Note: "Fan blade cleaned and bearing oiled"),
        new("WO-0906", "Corrective", "AC dripping water on carpet", "P2", "Verified", "203", "AC-203", null, "maint", null, "2026-10-21T09:00:00", "reception",
            Started: "2026-10-21T10:00:00", Done: "2026-10-21T13:30:00", DoneBy: "maint", Verified: "2026-10-21T14:00:00", VerifiedBy: "supervisor", Note: "Drain pipe replaced and sloped",
            DtFrom: "2026-10-21T09:30:00", DtTo: "2026-10-21T14:00:00", Parts: [new("SP-08", 1, 6_000)]),
        new("WO-0907", "Preventive", "Generator weekly test run (15 min on load)", "P3", "Verified", null, "DG-1", "Basement", null, "VEN-03", "2026-10-16T09:00:00", "supervisor",
            Started: "2026-10-17T10:00:00", Done: "2026-10-17T10:30:00", DoneBy: "maint", Verified: "2026-10-17T12:00:00", VerifiedBy: "supervisor", Note: "Ran 15 min on load; voltage and frequency normal", Plan: "PM-01", Due: "2026-10-17"),
        new("WO-0908", "Preventive", "Fire pump weekly run test", "P3", "Verified", null, "FP-1", "Basement", "maint", null, "2026-10-16T09:00:00", "supervisor",
            Started: "2026-10-19T09:00:00", Done: "2026-10-19T09:20:00", DoneBy: "maint", Verified: "2026-10-19T11:00:00", VerifiedBy: "supervisor", Note: "Done 2 days late — pump room key was missing", Plan: "PM-02", Due: "2026-10-17"),
    ];

    public static DateTime T(string iso) => DateTime.SpecifyKind(DateTime.Parse(iso, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    public static DateOnly D(string iso) => DateOnly.Parse(iso, CultureInfo.InvariantCulture);

    /// <summary>In-memory domain objects for the kernel tests (ids = position + 1; rooms by number; users by a small map).</summary>
    public static (List<MntOrder> Orders, List<MntAsset> Assets, List<MntPlan> Plans, List<MntSpare> Spares) Build()
    {
        var assets = Assets.Select((a, i) => new MntAsset(i + 1, a.Name, a.Kind, a.Room is null ? null : int.Parse(a.Room, CultureInfo.InvariantCulture), a.Room, a.Area, a.Make, a.Model, D(a.Installed), null, a.Status, [])).ToList();
        int AssetId(string? code) => code is null ? 0 : Array.FindIndex(Assets, a => a.Code == code) + 1;
        var plans = Plans.Select((p, i) => new MntPlan(i + 1, AssetId(p.Asset), p.Title, p.Every, D(p.NextDue), true, D(p.LastDone), [])).ToList();
        var spares = Spares.Select((s, i) => new MntSpare(i + 1, s.Name, s.Unit, s.Stock, s.ReorderAt, s.Cost, [])).ToList();
        static int User(string? n) => n switch { "maint" => 9, "supervisor" => 5, "manager" => 2, "reception" => 3, _ => 0 };
        var orders = Orders.Select((o, i) => new MntOrder(i + 1, o.Kind, o.Title, o.Priority, o.Status, o.Room is null ? null : int.Parse(o.Room, CultureInfo.InvariantCulture), o.Room,
            o.Asset is null ? null : AssetId(o.Asset), o.Area, o.Assignee is null ? null : User(o.Assignee), null, T(o.Created), User(o.CreatedBy),
            o.Started is null ? null : T(o.Started), o.Done is null ? null : T(o.Done), o.DoneBy is null ? null : User(o.DoneBy), o.Verified is null ? null : T(o.Verified),
            o.VerifiedBy is null ? null : User(o.VerifiedBy), o.Hold, null, o.Note, null, null, null, o.Plan is null ? null : Array.FindIndex(Plans, p => p.Code == o.Plan) + 1,
            o.Due is null ? null : D(o.Due), o.DtFrom is null ? null : T(o.DtFrom), o.DtTo is null ? null : T(o.DtTo), null,
            (o.Parts ?? []).Select(p => new MntPart(Array.FindIndex(Spares, s => s.Code == p.Spare) + 1, p.Qty, p.Cost)).ToList(), [])).ToList();
        return (orders, assets, plans, spares);
    }
}
