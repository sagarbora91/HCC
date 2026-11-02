using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts.Stays;
using Hcc.Domain.Configuration;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>Owner settings of the LF desk fields (Master configuration › Front desk; prototype domain/streams/desk-fields.ts). The half-day percentage is a money setting (Owner only).</summary>
public static class StaysSettings
{
    public const string PurposeRequired = "frontdesk.purposeRequired";
    public const string FormCRequired = "frontdesk.formCRequired";
    public const string DefaultCheckoutBasis = "frontdesk.defaultCheckoutBasis";
    public const string LateGraceHours = "frontdesk.lateCheckoutGraceHours";
    public const string EarlyGraceHours = "frontdesk.earlyCheckinGraceHours";
    public const string HalfDayPct = "frontdesk.halfDayPct";

    public static ISettingsProvider Provider { get; } = new SettingsProvider("lf-desk", ConfigGroupIds.FrontDesk,
    [
        new(PurposeRequired, "Purpose of visit required at check-in", SettingType.Boolean) { Unit = "on / off", Default = ConfigValue.Of(true), Affects = "Check-ins from now on; stays already in house are unchanged." },
        new(FormCRequired, "Form C required for foreign guests", SettingType.Boolean) { Unit = "on / off", Default = ConfigValue.Of(true), Affects = "Check-ins from now on." },
        new(DefaultCheckoutBasis, "Default check-out basis", SettingType.Choice)
        {
            Unit = "fixed time / 24 hours", Default = ConfigValue.Of("fixed"), Options = [ConfigValue.Of("fixed"), ConfigValue.Of("24h")],
            Help = "fixed = the check-out time on the departure date; 24h = 24 hours per night from the check-in time.", Affects = "Bookings made from now on.",
        },
        new(LateGraceHours, "Late check-out grace", SettingType.Integer) { Unit = "hours after the due-out time", Suffix = "h", Min = 0, Max = 24, Default = ConfigValue.Of(1L), Affects = "Check-outs from now on." },
        new(EarlyGraceHours, "Early check-in grace", SettingType.Integer) { Unit = "hours before the check-in time", Suffix = "h", Min = 0, Max = 24, Default = ConfigValue.Of(1L), Affects = "Warnings at check-in from now on." },
        new(HalfDayPct, "Half-day late check-out charge", SettingType.Integer)
        {
            Unit = "% of the night rate", Suffix = "%", Min = 0, Max = 100, Default = ConfigValue.Of(50L), Money = true, Illustrative = true,
            Help = "Illustrative — CA to confirm for a hotel. The late check-out line is this share of the stay's room rate, at the room GST slab of that rate.",
            Affects = "Late check-out charges from now on; lines already posted are unchanged.",
        },
    ]);
}

/// <summary>What the desk fields hold for one reservation.</summary>
public sealed record DeskReservation(string? PurposeCode, string? VehicleNo, string? VehicleType, string CheckoutBasis, DateTime? CheckedInUtc, long NightRatePaise);

public sealed record DeskGuestFacts(string Nationality, string? IdType, bool HasDob, bool HasIdNumber);

/// <summary>Persistence port of the desk fields (SQL adapter: Infrastructure/Stays/SqlDeskFieldStore; migrations 4695–4699).</summary>
public interface IDeskFieldStore
{
    /// <summary>True when the purpose code is an active row of mst.purpose_of_visit (the built-in list when the property has no rows yet).</summary>
    Task<bool> PurposeUsableAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);

    /// <summary>The master name of an active vehicle type (code or name given), or null when unknown / retired. Built-ins when the property has no rows.</summary>
    Task<string?> VehicleTypeNameAsync(ICommandTransaction tx, int propertyId, string typeOrCode, CancellationToken ct = default);

    Task SetBookingFieldsAsync(ICommandTransaction tx, long reservationId, string? purposeCode, string? vehicleNo, string? vehicleType, string? checkoutBasis, CancellationToken ct = default);

    Task<DeskReservation?> GetAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    Task<DeskGuestFacts?> GuestFactsAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default);

    Task UpsertFormCAsync(ICommandTransaction tx, int propertyId, long stayId, FormCFacts form, int by, DateTime utc, CancellationToken ct = default);

    Task<string?> LateDecisionAsync(ICommandTransaction tx, long stayId, CancellationToken ct = default);

    Task SetLateDecisionAsync(ICommandTransaction tx, long stayId, string decision, CancellationToken ct = default);

    /// <summary>Foreign stays arriving or staying in the range, with their Form C (own connection).</summary>
    Task<IReadOnlyList<FormCRegisterRaw>> FormCRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record FormCRegisterRaw(long StayId, string Booking, string Room, string Guest, string Nationality, DateOnly Arrival, DateOnly Departure, string Status, DateOnly? Dob, FormCFacts? Form);

/// <summary>Posts the late check-out line on the folio (Billing implements it; the room slab of the night rate decides the GST).</summary>
public interface ILateCheckoutPoster
{
    Task PostAsync(ICommandTransaction tx, long folioId, DateOnly businessDate, long nightRatePaise, long amountPaise, string label, CancellationToken ct = default);
}
