using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>A3 purpose, A4 vehicle, A6 basis, A5 Form C as validated for one reservation (before they are stored).</summary>
public sealed record DeskPlan(string? PurposeCode, string? VehicleNo, string? VehicleType, string? Basis, FormCFacts? Form);

/// <summary>
/// The LF desk-field rules in the command transactions (LF-CONTRACT §8; prototype handlers/desk.ts): booking fields, the check-in block (purpose required by
/// the Owner setting when the request comes from a desk form, vehicle, Form C for foreign guests), the late check-out decision and the Form C register.
/// Everything is optional on the wire; a request without desk fields behaves as before.
/// </summary>
public sealed class DeskFieldService(IDeskFieldStore store, ISettingsReader? settings, IAccessPolicy policy, ILateCheckoutPoster? poster = null)
{
    public const string WaivePermission = "PERM-APR-001";
    public const string IdDetailsPermission = "PERM-DOC-004";

    private static readonly string[] Basis = [DeskFieldRules.BasisFixed, DeskFieldRules.Basis24h];

    private async Task<bool> FlagAsync(ICommandTransaction tx, int p, string key, bool dflt)
    {
        if (settings is null) return dflt;
        var v = await settings.GetAsync(tx, p, key, null, default).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Boolean ? v.Flag : dflt;
    }

    private async Task<long> NumberAsync(ICommandTransaction tx, int p, string key, long dflt)
    {
        if (settings is null) return dflt;
        var v = await settings.GetAsync(tx, p, key, null, default).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Number ? v.Number : dflt;
    }

    private async Task<TimeOnly> TimeAsync(ICommandTransaction tx, int p, string key, TimeOnly dflt)
    {
        if (settings is null) return dflt;
        var v = await settings.GetAsync(tx, p, key, null, default).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Text && TimeOnly.TryParseExact(v.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : dflt;
    }

    public async Task<string> DefaultBasisAsync(ICommandTransaction tx, int propertyId)
    {
        if (settings is null) return DeskFieldRules.BasisFixed;
        var v = await settings.GetAsync(tx, propertyId, StaysSettings.DefaultCheckoutBasis, null, default).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Text && v.Text == DeskFieldRules.Basis24h ? DeskFieldRules.Basis24h : DeskFieldRules.BasisFixed;
    }

    /// <summary>Validated purpose / vehicle / basis of a booking or check-in request; each part may be absent.</summary>
    public async Task<DeskPlan> ValidateAsync(ICommandTransaction tx, int propertyId, string? purposeCode, string? vehicleNo, string? vehicleType, string? basis, CancellationToken ct)
    {
        string? purpose = null;
        if (!string.IsNullOrWhiteSpace(purposeCode))
        {
            purpose = purposeCode.Trim().ToUpperInvariant();
            if (!await store.PurposeUsableAsync(tx, propertyId, purpose, ct).ConfigureAwait(false))
                throw HccException.Validation($"“{purpose}” is not an active purpose of visit (Master configuration › Purposes of visit).");
        }

        string? no = null, type = null;
        if (!string.IsNullOrWhiteSpace(vehicleNo))
        {
            no = DeskFieldRules.NormaliseVehicle(vehicleNo);
            if (!string.IsNullOrWhiteSpace(vehicleType))
                type = await store.VehicleTypeNameAsync(tx, propertyId, vehicleType.Trim(), ct).ConfigureAwait(false)
                    ?? throw HccException.Validation($"“{vehicleType.Trim()}” is not an active vehicle type (Master configuration › Vehicle types).");
        }
        else if (!string.IsNullOrWhiteSpace(vehicleType)) throw HccException.Validation("Enter the vehicle number.");

        string? b = null;
        if (!string.IsNullOrWhiteSpace(basis))
        {
            b = basis.Trim();
            if (!Basis.Contains(b)) throw HccException.Validation("Check-out basis: fixed time or 24 hours.");
        }

        return new DeskPlan(purpose, no, type, b, null);
    }

    /// <summary>Booking: validate and store purpose / vehicle / basis on the new reservation (the 24h default setting applies when no basis is given).</summary>
    public async Task ApplyBookingAsync(ICommandTransaction tx, int propertyId, long reservationId, string? purposeCode, string? vehicleNo, string? vehicleType, string? basis, CancellationToken ct)
    {
        var plan = await ValidateAsync(tx, propertyId, purposeCode, vehicleNo, vehicleType, basis, ct).ConfigureAwait(false);
        var b = plan.Basis ?? (await DefaultBasisAsync(tx, propertyId).ConfigureAwait(false) == DeskFieldRules.Basis24h ? DeskFieldRules.Basis24h : null);
        if (plan.PurposeCode is null && plan.VehicleNo is null && b is null) return;
        await store.SetBookingFieldsAsync(tx, reservationId, plan.PurposeCode, plan.VehicleNo, plan.VehicleType, b, ct).ConfigureAwait(false);
    }

    /// <summary>Check-in block: purpose (required when the request is a desk form and the setting is on), vehicle, basis, Form C for a foreign guest. Throws before anything is stored.</summary>
    public async Task<DeskPlan> PlanCheckInAsync(ICommandTransaction tx, int propertyId, long reservationId, long guestId, DateOnly arrival, CheckInRequest req, CancellationToken ct)
    {
        var existing = await store.GetAsync(tx, reservationId, ct).ConfigureAwait(false);
        var plan = await ValidateAsync(tx, propertyId, req.PurposeCode ?? existing?.PurposeCode, req.VehicleNo, req.VehicleType, req.CheckoutBasis, ct).ConfigureAwait(false);
        if (plan.PurposeCode is null && req.Desk && await FlagAsync(tx, propertyId, StaysSettings.PurposeRequired, true).ConfigureAwait(false))
            throw HccException.Validation("Choose the purpose of visit.");
        var guest = await store.GuestFactsAsync(tx, guestId, ct).ConfigureAwait(false);
        FormCFacts? form = null;
        if (guest is not null && DeskFieldRules.IsForeign(guest.Nationality))
        {
            var needed = await FlagAsync(tx, propertyId, StaysSettings.FormCRequired, true).ConfigureAwait(false);
            if (req.FormC is not null || needed)
            {
                form = ToFacts(req.FormC ?? throw HccException.Validation("Enter the Form C details (foreign guest)."));
                var passport = (guest.IdType ?? string.Empty).StartsWith("passport", StringComparison.OrdinalIgnoreCase);
                DeskFieldRules.ValidateFormC(form, arrival, passport && guest.HasIdNumber, guest.HasDob);
            }
        }

        return plan with { Form = form };
    }

    /// <summary>After the stay row exists: store the booking fields, the check-in basis and the Form C.</summary>
    public async Task SaveCheckInAsync(ICommandTransaction tx, int propertyId, long reservationId, long stayId, DeskPlan plan, int by, DateTime utc, CancellationToken ct)
    {
        if (plan.PurposeCode is not null || plan.VehicleNo is not null || plan.Basis is not null)
            await store.SetBookingFieldsAsync(tx, reservationId, plan.PurposeCode, plan.VehicleNo, plan.VehicleType, plan.Basis, ct).ConfigureAwait(false);
        if (plan.Form is not null) await store.UpsertFormCAsync(tx, propertyId, stayId, plan.Form, by, utc, ct).ConfigureAwait(false);
    }

    /// <summary>Save (or replace) the Form C of a stay that is already in house.</summary>
    public async Task SaveFormCAsync(ICommandTransaction tx, int propertyId, long stayId, long reservationId, long guestId, DateOnly arrival, FormCDto dto, int by, DateTime utc, CancellationToken ct)
    {
        var guest = await store.GuestFactsAsync(tx, guestId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest not found.");
        if (!DeskFieldRules.IsForeign(guest.Nationality)) throw HccException.Validation("This guest is registered as an Indian national: Form C is for foreign guests.");
        var form = ToFacts(dto);
        var passport = (guest.IdType ?? string.Empty).StartsWith("passport", StringComparison.OrdinalIgnoreCase);
        DeskFieldRules.ValidateFormC(form, arrival, passport && guest.HasIdNumber, guest.HasDob);
        await store.UpsertFormCAsync(tx, propertyId, stayId, form, by, utc, ct).ConfigureAwait(false);
        _ = reservationId;
    }

    // ── A6 late check-out ──
    public async Task<LateCheckoutResult> LateAsync(ICommandTransaction tx, int propertyId, long reservationId, DateOnly arrival, DateOnly departure, DateTime utcNow, CancellationToken ct)
    {
        var d = await store.GetAsync(tx, reservationId, ct).ConfigureAwait(false);
        var basis = d?.CheckoutBasis ?? DeskFieldRules.BasisFixed;
        var co = await TimeAsync(tx, propertyId, "checkout", new TimeOnly(11, 0)).ConfigureAwait(false);
        var ci = await TimeAsync(tx, propertyId, "checkin", new TimeOnly(14, 0)).ConfigureAwait(false);
        var due = DeskFieldRules.DueOut(basis, arrival, departure, co, ci, d?.CheckedInUtc?.AddHours(5.5));
        var grace = (int)Math.Clamp(await NumberAsync(tx, propertyId, StaysSettings.LateGraceHours, 1).ConfigureAwait(false), 0, 24);
        var pct = (int)Math.Clamp(await NumberAsync(tx, propertyId, StaysSettings.HalfDayPct, 50).ConfigureAwait(false), 0, 100);
        return DeskFieldRules.LateCheckout(due, utcNow.AddHours(5.5), grace, d?.NightRatePaise ?? 0, pct);
    }

    /// <summary>At CheckoutStay: past due-out + grace needs a decision (posted earlier, or LateCharge on this request).</summary>
    public async Task EnsureLateDecisionAsync(ICommandTransaction tx, CommandContext ctx, StayRecord stay, string? lateCharge, string? reason, DateTime utcNow, DateOnly businessDate, CancellationToken ct)
    {
        var late = await LateAsync(tx, ctx.PropertyId, stay.ReservationId, stay.Arrival, stay.Departure, utcNow, ct).ConfigureAwait(false);
        if (!late.Late) return;
        if (await store.LateDecisionAsync(tx, stay.StayId, ct).ConfigureAwait(false) is not null) return;
        if (string.IsNullOrWhiteSpace(lateCharge)) throw new HccException(HccErrorCode.INVALID_TRANSITION, late.Message);
        await DecideAsync(tx, ctx, stay, late, lateCharge.Trim(), reason, businessDate, ct).ConfigureAwait(false);
    }

    /// <summary>The PostLateCheckoutCharge command body: half-day posts the charge, waived needs PERM-APR-001 and a reason.</summary>
    public async Task<(string Decision, long Amount)> PostLateAsync(ICommandTransaction tx, CommandContext ctx, StayRecord stay, string charge, string? reason, DateTime utcNow, DateOnly businessDate, CancellationToken ct)
    {
        var late = await LateAsync(tx, ctx.PropertyId, stay.ReservationId, stay.Arrival, stay.Departure, utcNow, ct).ConfigureAwait(false);
        if (!late.Late) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay is not past its due-out time.");
        if (await store.LateDecisionAsync(tx, stay.StayId, ct).ConfigureAwait(false) is not null)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "The late check-out charge is already decided for this stay.");
        return await DecideAsync(tx, ctx, stay, late, charge, reason, businessDate, ct).ConfigureAwait(false);
    }

    private async Task<(string, long)> DecideAsync(ICommandTransaction tx, CommandContext ctx, StayRecord stay, LateCheckoutResult late, string charge, string? reason, DateOnly bd, CancellationToken ct)
    {
        switch (charge)
        {
            case "half-day":
                if (late.HalfDayPaise <= 0) throw HccException.Validation("The half-day charge is ₹0 (check “Half-day late check-out charge” in Master configuration).");
                if (poster is null) throw new InvalidOperationException("No folio poster is wired.");
                var d = await store.GetAsync(tx, stay.ReservationId, ct).ConfigureAwait(false);
                await poster.PostAsync(tx, stay.FolioId, bd, d?.NightRatePaise ?? 0, late.HalfDayPaise, "Late check-out · half day", ct).ConfigureAwait(false);
                await store.SetLateDecisionAsync(tx, stay.StayId, "half-day", ct).ConfigureAwait(false);
                await tx.AuditAsync("LATE_CHECKOUT", "stay-" + stay.StayId.ToString(CultureInfo.InvariantCulture), $"half day {late.HalfDayPaise} paise", ct).ConfigureAwait(false);
                return ("half-day", late.HalfDayPaise);
            case "waived":
                await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, WaivePermission, null, null, ct).ConfigureAwait(false);
                var r = (reason ?? string.Empty).Trim();
                if (r.Length < 3) throw HccException.Validation("Give the reason for waiving the late check-out charge.");
                await store.SetLateDecisionAsync(tx, stay.StayId, "waived", ct).ConfigureAwait(false);
                await tx.AuditAsync("LATE_CHECKOUT", "stay-" + stay.StayId.ToString(CultureInfo.InvariantCulture), $"charge waived · {(r.Length > 80 ? r[..80] : r)}", ct).ConfigureAwait(false);
                return ("waived", 0);
            case "full-night":
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A full night is an extension: extend the stay by one night, then check out.");
            default:
                throw HccException.Validation("Late check-out: choose half-day or waived.");
        }
    }

    // ── A5 register ──
    public async Task<FormCRegister> RegisterAsync(int propertyId, int principalId, string? from, string? to, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, IdDetailsPermission, null, null, ct).ConfigureAwait(false);
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) throw HccException.Validation("from: a date (yyyy-MM-dd).");
        if (!DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) throw HccException.Validation("to: a date (yyyy-MM-dd).");
        if (t < f || t.DayNumber - f.DayNumber > 400) throw HccException.Validation("Choose a range of at most 400 days.");
        var can = await policy.HasAsync(propertyId, principalId, IdDetailsPermission, null, ct).ConfigureAwait(false);
        var rows = await store.FormCRangeAsync(propertyId, f, t, ct).ConfigureAwait(false);
        return new FormCRegister(from!, to!, rows.Select(r => new FormCRegisterRow(r.StayId, r.Booking, r.Room, r.Guest, r.Nationality, Day(r.Arrival), Day(r.Departure), r.Status,
            r.Dob is { } dob ? Day(dob) : null, r.Form is null ? null : ToDto(r.Form, can), r.Form is null)).ToList());
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static FormCDto ToDto(FormCFacts f, bool canSeeVisa) => new(f.PassportPlace, Day(f.PassportIssued), Day(f.PassportExpiry), DeskFieldRules.VisaForViewer(f.VisaNo, canSeeVisa), f.VisaType, f.VisaPlace,
        Day(f.VisaIssued), Day(f.VisaExpiry), f.ArrivedFrom, Day(f.ArrivalInIndia), f.IntendedStayDays, f.NextDestination, f.EmployedInIndia, f.EmployerOrBusiness, f.AddressAbroad, f.ContactAbroad, f.Sex.ToString());

    internal static FormCFacts ToFacts(FormCDto d)
    {
        DateOnly Date(string v, string label) => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? x : throw HccException.Validation($"Form C: {label} must be a date (yyyy-mm-dd).");
        var sex = (d.Sex ?? string.Empty).Trim().ToUpperInvariant();
        return new FormCFacts((d.PassportPlace ?? string.Empty).Trim(), Date(d.PassportIssued, "the passport issue date"), Date(d.PassportExpiry, "the passport expiry date"), (d.VisaNo ?? string.Empty).Trim().ToUpperInvariant(),
            (d.VisaType ?? string.Empty).Trim(), (d.VisaPlace ?? string.Empty).Trim(), Date(d.VisaIssued, "the visa issue date"), Date(d.VisaExpiry, "the visa expiry date"), (d.ArrivedFrom ?? string.Empty).Trim(),
            Date(d.ArrivalInIndia, "the date of arrival in India"), d.IntendedStayDays, (d.NextDestination ?? string.Empty).Trim(), d.EmployedInIndia,
            string.IsNullOrWhiteSpace(d.EmployerOrBusiness) ? null : d.EmployerOrBusiness.Trim(), (d.AddressAbroad ?? string.Empty).Trim(), string.IsNullOrWhiteSpace(d.ContactAbroad) ? null : d.ContactAbroad.Trim(),
            sex.Length == 1 ? sex[0] : '?');
    }
}
