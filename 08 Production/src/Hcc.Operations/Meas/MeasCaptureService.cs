// F12 MEAS · f12-int-cap: the server capture module (MEAS-CONTRACT §2). One service for every MEAS capture command. Each command:
//   1. refuses first with FORBIDDEN + details.reason MEAS_OFF while its item is off (IMeasurementGate; the route filter checks the same key),
//   2. checks the caller's roles exactly like the prototype handler (03 Prototype/src/services/mock/handlers/meas-*.ts),
//   3. validates through the pure rules (DeskRules, GuestRules, HkRules, PeopleRules),
//   4. runs in ICommandRunner (one transaction, Idempotency-Key replay, plat.audit_event row) and inserts with the tables' client id unique keys.
// AddMeasPulse is the exception: no runner (its idempotency row would carry the principal) and an audit row with principal 0 and no name.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Meas;
using Hcc.Contracts;
using Hcc.Contracts.Meas;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Operations.Meas;

public sealed class MeasCaptureService(ICommandRunner runner, IAccessPolicy policy, IMeasurementGate gate, IMeasCaptureCommon common, IMeasDeskStore desk, IMeasGuestStore guest,
    IMeasHkStore hk, IMeasPeopleStore people, IMeasRevenueStore revenue, IMeasCaptureReads reads, TimeProvider clock)
{
    // Command names (POST /api/v1/commands/<name>) = the names the prototype sync mappers send.
    public const string OpTurnaway = "LogTurnaway", OpWalked = "LogWalkedGuest", OpAllowance = "SaveAllowanceReason", OpBookingExtra = "SetBookingExtra", OpPreference = "SetCustomerPreference";
    public const string OpSurvey = "MeasSurveyAdd", OpReview = "MeasReviewSave", OpRootCauseList = "MeasRootCauseListSave", OpRootCauseSet = "MeasRootCauseSet", OpLoyalty = "MeasLoyaltySet";
    public const string OpTraining = "SaveMeasTraining", OpExit = "SetMeasExit", OpPulse = "AddMeasPulse";
    public const string OpOffline = "MeasOfflineSet", OpMeter = "MeasMeterSave", OpReading = "MeasReadingAdd", OpAssetLife = "MeasAssetLifeSet", OpDiscard = "MeasDiscardAdd";
    public const string OpEvent = "MeasEventSave", OpEventDelete = "MeasEventDelete", OpOtb = "MeasOtbSnapshot", OpCompetitor = "MeasCompetitorSave", OpRateShop = "MeasRateShopAdd";

    // Gate keys (checked against each prototype handler's own guard key; walked = meas.rev.overbooking as in turnaway.ts MD_KEY).
    public const string KeyTurnaway = DeskRules.KeyTurnaway, KeyWalked = DeskRules.KeyWalked, KeyAllowance = DeskRules.KeyAllowance, KeyExtra = DeskRules.KeyExtra, KeyPreference = DeskRules.KeyPreference;
    public const string KeySurvey = GuestRules.ItemSurvey, KeyReview = GuestRules.ItemReputation, KeyRecovery = GuestRules.ItemRecovery, KeyRepeat = GuestRules.ItemRepeat;
    public const string KeyTraining = PeopleRules.TrainingKey, KeyExit = PeopleRules.ExitKey, KeyPulse = PeopleRules.PulseKey;
    public const string KeyOoo = HkRules.KeyOoo, KeyUtilities = HkRules.KeyUtilities, KeyAssets = HkRules.KeyAssets, KeyLinen = HkRules.KeyLinen;
    public const string KeyEvents = "meas.rev.demand-calendar", KeyPickup = "meas.rev.pickup", KeyRateShop = "meas.rev.rate-shop";

    public const string BehalfPermission = "PERM-MEAS-002";
    public const string NotMonotonicReason = HkRules.ErrNotMonotonic;
    public const int OtbNights = 120, OtbKeep = 60, MaxActiveCompetitors = 8;

    private static readonly string[] EventKinds = ["wedding-season", "exam", "fair", "corporate-visit", "holiday", "other"];
    private static readonly Dictionary<string, string> MeterUnits = new() { ["electricity"] = "kWh", ["water"] = "kL", ["lpg"] = "cylinders", ["diesel"] = "litres", ["dg-hours"] = "hours" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    // ── helpers ──
    private static string Text(string? v) => (v ?? string.Empty).Trim();

    private static string? Opt(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static DateOnly Date(string? v, string message) =>
        DateOnly.TryParseExact(Text(v), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : throw HccException.Validation(message);

    private static DateOnly? OptDate(string? v, string message) => string.IsNullOrWhiteSpace(v) ? null : Date(v, message);

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private static string Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    /// <summary>The table client id: the Idempotency-Key itself when it fits, otherwise a stable hash of it.</summary>
    public static string ClientId(string key, int max) => key.Length <= max ? key : "h-" + Hex(key)[..(max - 2)];

    /// <summary>A stable GUID from the Idempotency-Key (uniqueidentifier client ids: training, pulse). One-way: it carries nothing about the caller.</summary>
    public static Guid KeyGuid(string scope, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":" + key)).AsSpan(0, 16));

    /// <summary>A stable record id from the Idempotency-Key when the client did not choose one (event, competitor, meter).</summary>
    public static string HashId(string prefix, string key) => prefix + "-" + Hex(key)[..12].ToUpperInvariant();

    private static string Target(string kind, string id) { var t = kind + ":" + id; return t.Length <= 60 ? t : t[..60]; }

    private static HccException Forbidden(string message) => new(HccErrorCode.FORBIDDEN, message);

    private static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);

    private static HccException Reasoned(HccErrorCode code, string reason, string message) =>
        new(code, message, new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(reason) });

    /// <summary>HkRules speaks role names; the session holds role codes.</summary>
    private static string RoleName(string code) => code switch
    {
        "OWN" => "Owner", "MGR" => "Manager", "SUP" => "Supervisor", "MNT" => "Maintenance", "ACC" => "Accountant", "REC" => "Reception", "CSH" => "Cashier",
        "HKP" => "Housekeeping", "TAD" => "Technical admin", _ => code,
    };

    private static bool Any(IReadOnlyCollection<string> roles, params string[] allowed) => roles.Any(allowed.Contains);

    private static bool Hk(IReadOnlyCollection<string> roles, Func<string, bool> rule) => roles.Any(r => rule(RoleName(r)));

    private static void Check(HkRuleResult r)
    {
        if (r.Ok) return;
        throw r.Code == HkRules.ErrNotMonotonic ? Reasoned(HccErrorCode.VALIDATION, NotMonotonicReason, r.Message!) : HccException.Validation(r.Message!);
    }

    /// <summary>Gate first (MEAS_OFF), then the caller's roles.</summary>
    private async Task<IReadOnlyList<string>> OpenAsync(CommandContext ctx, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await gate.EnsureOnAsync(ctx.PropertyId, key, ct).ConfigureAwait(false);
        return await common.RoleCodesAsync(ctx.PropertyId, ctx.PrincipalId, UtcNow, ct).ConfigureAwait(false);
    }

    private Task<CommandResult<MeasCaptureResult>> RunAsync<T>(CommandContext ctx, string op, T payload, Func<ICommandTransaction, CancellationToken, Task<MeasCaptureResult>> handler, CancellationToken ct) =>
        runner.RunAsync(ctx with { Operation = op }, payload, handler, ct);

    private static MeasCaptureResult R(string id, string message) => new(id, message);

    private static string Id(long id) => id.ToString(Inv);

    // ═════ desk (cap-desk) ═════

    public async Task<CommandResult<MeasCaptureResult>> LogTurnawayAsync(CommandContext ctx, LogTurnawayRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyTurnaway, ct).ConfigureAwait(false);
        if (!DeskRules.MayLogTurnaway(roles)) throw Forbidden("Access denied: turn-aways are logged by Reception, a Manager or the Owner.");
        var reason = Text(r.Reason);
        if (!DeskRules.TurnawayReasons.Contains(reason)) throw HccException.Validation("Choose why the guest could not book.");
        var arrival = Date(r.Arrival, "Choose the arrival date.");
        var input = new DeskRules.TurnawayInput(reason, arrival, r.Nights ?? 0, Text(r.RoomType), r.Rooms ?? 1, r.QuotedRatePaise, Text(r.Channel), Opt(r.Note));
        if (DeskRules.ValidateTurnaway(input) is { } bad) throw HccException.Validation(bad);
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        return await RunAsync(ctx, OpTurnaway, r, async (tx, c) =>
        {
            var line = DeskRules.TurnawayAudit(input.RoomType, input.Rooms, input.Nights, arrival, reason, input.QuotedRatePaise);
            var (id, existed) = await desk.InsertTurnawayAsync(tx, ClientId(ctx.IdempotencyKey, 64),
                new MeasTurnawayRow(reason, arrival, input.Nights, input.RoomType, input.Rooms, input.QuotedRatePaise, input.Channel, DeskRules.LeadDays(today, arrival), input.Note, today), c).ConfigureAwait(false);
            if (!existed) await tx.AuditAsync("MEAS_TURNAWAY_LOG", Target("turnaway", Id(id)), line, c).ConfigureAwait(false);
            return R(Id(id), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> LogWalkedGuestAsync(CommandContext ctx, LogWalkedGuestRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyWalked, ct).ConfigureAwait(false);
        if (!DeskRules.MayLogWalked(roles)) throw Forbidden("Access denied: a walked guest is logged by a Manager or the Owner.");
        var night = Date(r.Night, "Choose the night.");
        if (DeskRules.ValidateWalked(r.Reason, r.CostPaise) is { } bad) throw HccException.Validation(bad);
        var resId = r.ReservationId ?? throw NotFound("Booking not found.");
        return await RunAsync(ctx, OpWalked, r, async (tx, c) =>
        {
            if (!await desk.ReservationExistsAsync(tx, resId, c).ConfigureAwait(false)) throw NotFound("Booking not found.");
            var (id, existed) = await desk.InsertWalkedAsync(tx, ClientId(ctx.IdempotencyKey, 64), resId, night, Text(r.Reason), r.CostPaise, ctx.PrincipalId, c).ConfigureAwait(false);
            var line = $"Booking {resId} walked on {Wire(night)} · {Text(r.Reason)}";
            if (!existed) await tx.AuditAsync("MEAS_WALKED_LOG", Target("walked", Id(id)), line, c).ConfigureAwait(false);
            return R(Id(id), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> SaveAllowanceReasonAsync(CommandContext ctx, SaveAllowanceReasonRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyAllowance, ct).ConfigureAwait(false);
        if (!DeskRules.MaySaveAllowance(roles)) throw Forbidden("Access denied: allowance reasons are an Owner setting.");
        var code = DeskRules.NormaliseCode(r.Code) ?? throw HccException.Validation("Give the reason a short code (letters, digits, dash).");
        var label = Text(r.Label);
        if (label.Length is < 2 or > 60) throw HccException.Validation("Name the reason (2–60 letters).");
        var active = r.Active != false;
        return await RunAsync(ctx, OpAllowance, r, async (tx, c) =>
        {
            var cur = await desk.AllowanceReasonAsync(tx, code, c).ConfigureAwait(false);
            if (cur is not null && cur.Label == label && cur.Active == active) throw HccException.Validation("No change.");
            await desk.UpsertAllowanceReasonAsync(tx, code, label, active, c).ConfigureAwait(false);
            var line = cur is null ? $"Allowance reason {code} added · {label}" : $"Allowance reason {code} updated · {label}{(active ? string.Empty : " · retired")}";
            await tx.AuditAsync("MEAS_ALLOWANCE_REASON_SAVE", Target("allowance", code), line, c).ConfigureAwait(false);
            return R(code, line);
        }, ct).ConfigureAwait(false);
    }

    private static int? UserId(string? v) => int.TryParse(Text(v), NumberStyles.None, Inv, out var id) && id > 0 ? id : null;

    public async Task<CommandResult<MeasCaptureResult>> SetBookingExtraAsync(CommandContext ctx, SetBookingExtraRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyExtra, ct).ConfigureAwait(false);
        if (!DeskRules.MaySetExtra(roles)) throw Forbidden("Access denied: upgrades and late check-out are recorded by Reception, a Manager or the Owner.");
        var resId = r.ReservationId ?? throw NotFound("Booking not found.");
        var item = Opt(r.Item);
        if (item is not null && item is not (DeskRules.ItemUpgrade or DeskRules.ItemLate1 or DeskRules.ItemLate2)) throw HccException.Validation("Unknown upsell item.");
        return await RunAsync(ctx, OpBookingExtra, r, async (tx, c) =>
        {
            if (!await desk.ReservationExistsAsync(tx, resId, c).ConfigureAwait(false)) throw NotFound("Booking not found.");
            var cur = await desk.BookingExtraAsync(tx, resId, c).ConfigureAwait(false);
            string? booked = cur?.BookedType, upgrade = cur?.Upgrade;
            int? approver = UserId(cur?.UpgradeApprover);
            var notes = new List<string>();
            var assigned = await desk.ReservationRoomTypeAsync(tx, resId, c).ConfigureAwait(false);
            if (Opt(r.BookedType) is { } bt)
            {
                if (!await desk.RoomTypeExistsAsync(tx, bt, c).ConfigureAwait(false)) throw HccException.Validation("Unknown room type.");
                if (booked is not null && booked != bt) throw HccException.Validation("The booked room type is already recorded and is kept.");
                booked = bt;
                notes.Add("booked type " + bt);
            }
            else booked ??= assigned; // booked type kept even when the assigned room later differs

            bool waived = false;
            string? waiveReason = null;
            if (item == DeskRules.ItemUpgrade)
            {
                var kind = Opt(r.Upgrade);
                if (kind is not ("paid" or "free")) throw HccException.Validation("Say whether the upgrade is paid or free.");
                if (booked is null || assigned is null || booked == assigned) throw HccException.Validation("The assigned room type is the same as the booked type: nothing to upgrade.");
                if (kind == "paid")
                    throw Reasoned(HccErrorCode.VALIDATION, "MEAS_CHARGE_NOT_WIRED", "A paid upgrade is a folio charge: post item UPG on the folio (Add line). This form records free upgrades only on the server.");
                var ap = UserId(r.Approver);
                var apRoles = ap is { } a ? await desk.ApproverRolesAsync(tx, a, UtcNow, c).ConfigureAwait(false) : null;
                if (!DeskRules.FreeUpgradeApproved(roles, ctx.PrincipalId, ap, apRoles)) throw HccException.Validation("A free upgrade needs a Manager or the Owner as approver.");
                approver = DeskRules.MayBlacklist(roles) ? ctx.PrincipalId : ap;
                upgrade = "free";
                notes.Add($"free upgrade approved by {approver}");
            }
            else if (item is DeskRules.ItemLate1 or DeskRules.ItemLate2)
            {
                var at = TimeOnly.TryParseExact(Text(r.Time), "HH:mm", Inv, DateTimeStyles.None, out var t) ? t : TimeOnly.FromDateTime(UtcNow.AddMinutes(330)); // hotel clock (IST)
                if (DeskRules.LateCheckoutItem(at) != item)
                    throw HccException.Validation(item == DeskRules.ItemLate1 ? "Late check-out item LCO1 applies from 14:00 up to 18:00." : "Late check-out item LCO2 applies from 18:00.");
                if (r.Waive != true)
                    throw Reasoned(HccErrorCode.VALIDATION, "MEAS_CHARGE_NOT_WIRED", $"A late check-out charge is a folio charge: post item {item} on the folio (Add line). Only a waiver is recorded here on the server.");
                if (DeskRules.ValidateWaiver(roles, r.Reason) is { } bad) throw HccException.Validation(bad);
                waived = true;
                waiveReason = Text(r.Reason);
                notes.Add($"{item} waived: {waiveReason}");
            }
            if (notes.Count == 0) throw HccException.Validation("Nothing to record.");
            await desk.UpsertBookingExtraAsync(tx, resId, booked, upgrade, approver, c).ConfigureAwait(false);
            await desk.InsertBookingExtraLogAsync(tx, ClientId(ctx.IdempotencyKey, 64), resId, item, waived, waiveReason, c).ConfigureAwait(false);
            var line = $"Booking {resId} · {string.Join(" · ", notes)}";
            await tx.AuditAsync("MEAS_BOOKING_EXTRA_SET", Target("booking", Id(resId)), line, c).ConfigureAwait(false);
            return R(Id(resId), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> SetCustomerPreferenceAsync(CommandContext ctx, SetCustomerPreferenceRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyPreference, ct).ConfigureAwait(false);
        if (!DeskRules.MaySetPreference(roles)) throw Forbidden("Access denied: preferences are kept by Reception, a Manager or the Owner.");
        var key = Text(r.CustomerKey);
        if (key.Length is 0 or > 80) throw NotFound("Customer not found.");
        var blacklist = r.BlacklistReason is not null || r.ClearBlacklist == true;
        if (blacklist && !DeskRules.MayBlacklist(roles)) throw Forbidden("Access denied: only a Manager or the Owner can blacklist or clear a guest.");
        return await RunAsync(ctx, OpPreference, r, async (tx, c) =>
        {
            var cur = await desk.PreferenceAsync(tx, key, c).ConfigureAwait(false) ?? new MeasPreferenceRow(null, null, null, null, null, null, false, null, null, null, null);
            var notes = new List<string>();
            string? F(string? incoming, string? old, string name)
            {
                if (incoming is null) return old;
                var v = incoming.Trim();
                if (v.Length > 80) throw HccException.Validation("Keep each preference under 80 letters.");
                notes.Add(name);
                return v.Length == 0 ? null : v;
            }
            var next = cur with
            {
                Floor = F(r.Floor, cur.Floor, "floor"), RoomKind = F(r.RoomKind, cur.RoomKind, "roomKind"), Meal = F(r.Meal, cur.Meal, "meal"), Pillow = F(r.Pillow, cur.Pillow, "pillow"),
                BillToCompany = F(r.BillToCompany, cur.BillToCompany, "billToCompany"), Note = F(r.Note, cur.Note, "note"),
            };
            if (r.DoNotDisturb is { } dnd) { next = next with { DoNotDisturb = dnd }; notes.Add("doNotDisturb"); }
            if (notes.Count > 0) await desk.InsertPreferenceLogAsync(tx, ClientId(ctx.IdempotencyKey + ":set", 64), key, "SET", string.Join(", ", notes), null, c).ConfigureAwait(false);
            if (r.ClearBlacklist == true)
            {
                if (cur.BlacklistReason is null) throw HccException.Validation("This guest is not blacklisted.");
                if (Text(r.Reason).Length < 3) throw HccException.Validation("Write why the blacklist is cleared.");
                next = next with { BlacklistReason = null, BlacklistApprovedBy = null, BlacklistUtc = null };
                notes.Add("blacklist cleared: " + Text(r.Reason));
                await desk.InsertPreferenceLogAsync(tx, ClientId(ctx.IdempotencyKey + ":bl", 64), key, "BLACKLIST_CLEAR", Text(r.Reason), ctx.PrincipalId, c).ConfigureAwait(false);
            }
            else if (r.BlacklistReason is not null)
            {
                var ap = UserId(r.ApprovedBy);
                var apRoles = ap is { } a ? await desk.ApproverRolesAsync(tx, a, UtcNow, c).ConfigureAwait(false) : null;
                if (DeskRules.ValidateBlacklist(roles, ctx.PrincipalId, r.BlacklistReason, ap, apRoles) is { } bad) throw HccException.Validation(bad);
                var by = roles.Contains("OWN") ? ctx.PrincipalId : ap!.Value;
                next = next with { BlacklistReason = Text(r.BlacklistReason), BlacklistApprovedBy = by, BlacklistUtc = UtcNow };
                notes.Add($"blacklisted (approved by {by})");
                await desk.InsertPreferenceLogAsync(tx, ClientId(ctx.IdempotencyKey + ":bl", 64), key, "BLACKLIST", Text(r.BlacklistReason), by, c).ConfigureAwait(false);
            }
            if (notes.Count == 0) throw HccException.Validation("Nothing to record.");
            await desk.UpsertPreferenceAsync(tx, key, next, c).ConfigureAwait(false);
            var line = $"Customer {key} · {string.Join(", ", notes)}";
            await tx.AuditAsync("MEAS_PREFERENCE_SET", Target("customer", key), line, c).ConfigureAwait(false);
            return R(key, line);
        }, ct).ConfigureAwait(false);
    }

    // ═════ guest (cap-guest) ═════

    public async Task<CommandResult<MeasCaptureResult>> SurveyAddAsync(CommandContext ctx, MeasSurveyAddRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeySurvey, ct).ConfigureAwait(false);
        var via = Text(r.Via);
        if (via == "desk")
        {
            if (!Any(roles, "OWN", "MGR", "REC")) throw Forbidden("Access denied: a desk survey needs PERM-MEAS-002 (Owner, Manager or Reception).");
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BehalfPermission, AccessScopes.Property(ctx.PropertyId), ct: ct).ConfigureAwait(false);
        }
        var input = new GuestRules.SurveyInput(via, r.Overall ?? 0, r.Cleanliness ?? 0, r.Staff ?? 0, r.Value ?? 0, r.Breakfast, r.Recommend ?? -1, Opt(r.Comment), r.Consent);
        GuestRules.EnsureSurvey(input);
        var cid = ClientId(Opt(r.ClientId) ?? ctx.IdempotencyKey, 60);
        return await RunAsync(ctx, OpSurvey, r, async (tx, c) =>
        {
            if (await guest.SurveyExistsAsync(tx, cid, c).ConfigureAwait(false)) return R(cid, $"{cid} · survey already saved");
            var now = UtcNow;
            if (via == "qr" && GuestRules.RateLimited(await guest.QrSurveyTimesAsync(tx, now.AddMinutes(-GuestRules.QrWindowMinutes), c).ConfigureAwait(false), now))
                throw Reasoned(HccErrorCode.RESTRICTED, "MEAS_RATE_LIMIT", "Too many answers from the survey page just now. Please try again in a few minutes.");
            long? resId = null;
            string? roomType = null, channel = null;
            if (via != "qr" && Opt(r.Booking) is { } b)
            {
                if (!long.TryParse(b, NumberStyles.None, Inv, out var id)) throw NotFound("Booking not found.");
                var (found, rt, ch) = await guest.SurveyBookingAsync(tx, id, c).ConfigureAwait(false);
                if (!found) throw NotFound("Booking not found.");
                (resId, roomType, channel) = (id, rt, ch);
            }
            var comment = input.Comment is { Length: > GuestRules.CommentMax } x ? x[..GuestRules.CommentMax] : input.Comment;
            await guest.InsertSurveyAsync(tx, new MeasSurveyRow(cid, now, via, resId, roomType, channel, input.Overall, input.Cleanliness, input.Staff, input.Value, input.Breakfast, input.Recommend,
                comment, via == "desk" ? ctx.PrincipalId : null), c).ConfigureAwait(false);
            var line = $"{cid} · survey via {via} · overall {input.Overall}/5 · recommend {input.Recommend}/10";
            await tx.AuditAsync("MEAS_SURVEY_ADD", Target("survey", cid), line, c).ConfigureAwait(false);
            return R(cid, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> ReviewSaveAsync(CommandContext ctx, MeasReviewSaveRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyReview, ct).ConfigureAwait(false);
        if (!Any(roles, "OWN", "MGR")) throw Forbidden("Access denied: review figures are saved by the Manager or Owner.");
        var input = new GuestRules.ReviewInput(Text(r.Month), Text(r.Platform), r.Count ?? -1, r.AvgScore ?? -1, r.Score12m, r.Responded ?? -1, Opt(r.Themes));
        if (GuestRules.ReviewProblem(input) is { } bad) throw HccException.Validation(bad);
        var id = $"{input.Month}-{input.Platform}";
        return await RunAsync(ctx, OpReview, r, async (tx, c) =>
        {
            var replaced = await guest.UpsertReviewAsync(tx, new MeasReviewView(id, input.Month, input.Platform, input.Count, input.AvgScore, input.Score12m, input.Responded, input.Themes), ctx.PrincipalId, c).ConfigureAwait(false);
            var line = string.Create(Inv, $"{id} · {input.Count} reviews · average {input.AvgScore} · responded {input.Responded}{(replaced ? " (replaced)" : string.Empty)}");
            await tx.AuditAsync("MEAS_REVIEW_SAVE", Target("review", id), line, c).ConfigureAwait(false);
            return R(id, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> RootCauseListSaveAsync(CommandContext ctx, MeasRootCauseListSaveRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyRecovery, ct).ConfigureAwait(false);
        if (!Any(roles, "OWN", "MGR")) throw Forbidden("Access denied: the root-cause list is edited by the Manager or Owner.");
        var rows = (r.Rows ?? []).Select(x => new GuestRules.RootCauseRow(Text(x.Code), Text(x.Label), x.Active)).ToList();
        if (GuestRules.RootCauseListProblem(rows) is { } bad) throw HccException.Validation(bad);
        return await RunAsync(ctx, OpRootCauseList, r, async (tx, c) =>
        {
            foreach (var used in await guest.UsedRootCausesAsync(tx, c).ConfigureAwait(false))
                if (!rows.Any(x => x.Code == used)) throw HccException.Validation($"Root cause {used} is used by a complaint: retire it instead of removing it.");
            await guest.ReplaceRootCausesAsync(tx, rows.Select(x => new MeasRootCauseView(x.Code, x.Label, x.Active)).ToList(), c).ConfigureAwait(false);
            var line = $"Root causes saved · {rows.Count} ({rows.Count(x => x.Active)} active)";
            await tx.AuditAsync("MEAS_ROOTCAUSE_LIST_SAVE", "rootcause:list", line, c).ConfigureAwait(false);
            return R("rootcause", line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> RootCauseSetAsync(CommandContext ctx, MeasRootCauseSetRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyRecovery, ct).ConfigureAwait(false);
        if (!Any(roles, "OWN", "MGR")) throw Forbidden("Access denied: a root cause is set by the Manager or Owner.");
        var complaint = Text(r.Complaint);
        if (!long.TryParse(complaint, NumberStyles.None, Inv, out var complaintId)) throw NotFound("Complaint not found.");
        var code = Text(r.Code);
        return await RunAsync(ctx, OpRootCauseSet, r, async (tx, c) =>
        {
            if (!await guest.ComplaintExistsAsync(tx, complaintId, c).ConfigureAwait(false)) throw NotFound("Complaint not found.");
            string line;
            if (code.Length == 0)
            {
                await guest.SetRootCauseAsync(tx, complaint, null, ctx.PrincipalId, c).ConfigureAwait(false);
                line = $"{complaint} · root cause cleared";
            }
            else
            {
                var current = await guest.RootCauseOfAsync(tx, complaint, c).ConfigureAwait(false);
                var row = (await guest.RootCausesAsync(tx, c).ConfigureAwait(false)).FirstOrDefault(x => x.Code == code);
                if (row is null || (!row.Active && current != code)) throw HccException.Validation("Choose an active root cause from the list.");
                await guest.SetRootCauseAsync(tx, complaint, code, ctx.PrincipalId, c).ConfigureAwait(false);
                line = $"{complaint} · root cause {code} ({row.Label})";
            }
            await tx.AuditAsync("MEAS_ROOTCAUSE_SET", Target("complaint", complaint), line, c).ConfigureAwait(false);
            return R(complaint, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> LoyaltySetAsync(CommandContext ctx, MeasLoyaltySetRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyRepeat, ct).ConfigureAwait(false);
        if (!Any(roles, "OWN", "MGR", "REC")) throw Forbidden("Access denied: a loyalty tag is set by Reception, the Manager or the Owner.");
        var cust = Text(r.Customer);
        if (cust.Length is 0 or > 80) throw NotFound("Customer not found.");
        var tag = Text(r.Tag);
        if (tag.Length > GuestRules.LoyaltyTagMax) throw HccException.Validation($"A tag is at most {GuestRules.LoyaltyTagMax} characters.");
        if (tag.Length > 0 && GuestIdRules.LooksLikeIdNumber(tag)) throw HccException.Validation("A tag cannot hold an ID number.");
        return await RunAsync(ctx, OpLoyalty, r, async (tx, c) =>
        {
            await guest.SetLoyaltyTagAsync(tx, cust, tag.Length == 0 ? null : tag, c).ConfigureAwait(false);
            var line = $"{cust} · loyalty tag {(tag.Length == 0 ? "cleared" : "\"" + tag + "\"")}";
            await tx.AuditAsync("MEAS_LOYALTY_SET", Target("customer", cust), line, c).ConfigureAwait(false);
            return R(cust, line);
        }, ct).ConfigureAwait(false);
    }

    // ═════ people (cap-people) ═════

    private static void RequireHr(IReadOnlyCollection<string> roles)
    {
        // Prototype hrCanManage = can('approve'): Owner and Manager (HR-001 gate).
        if (!Any(roles, "OWN", "MGR")) throw Forbidden("Access denied: HR-001 (staff records) is held by the Owner and Managers.");
    }

    public async Task<CommandResult<MeasCaptureResult>> SaveTrainingAsync(CommandContext ctx, SaveMeasTrainingRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireHr(await OpenAsync(ctx, KeyTraining, ct).ConfigureAwait(false));
        var staff = r.StaffId ?? throw NotFound("Staff member not found.");
        var date = Date(r.Date, "Enter the training date.");
        var validTo = OptDate(r.ValidTo, "Enter a valid \"valid to\" date.");
        var course = Text(r.Course);
        var hours = r.Hours ?? 0;
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        if (PeopleRules.CheckTraining(course, date, today, hours, validTo) is { } bad) throw HccException.Validation(bad);
        if (Opt(r.CertificateNo) is { Length: > 40 } || Opt(r.EvidenceRef) is { Length: > 120 }) throw HccException.Validation("Certificate number (40) or evidence reference (120) is too long.");
        long? editId = Opt(r.TrainingId) is { } tid ? (long.TryParse(tid, NumberStyles.None, Inv, out var t) ? t : throw NotFound("Training record not found.")) : null;
        return await RunAsync(ctx, OpTraining, r, async (tx, c) =>
        {
            if (await people.StaffJoinedAsync(tx, staff, c).ConfigureAwait(false) is null) throw NotFound("Staff member not found.");
            var row = new MeasTrainingView(string.Empty, Id(staff), course, Wire(date), hours, Opt(r.CertificateNo), validTo is { } v ? Wire(v) : null, Opt(r.EvidenceRef), r.Mandatory);
            long id;
            if (editId is { } e)
            {
                if (await people.TrainingStaffAsync(tx, e, c).ConfigureAwait(false) is null) throw NotFound("Training record not found.");
                await people.UpdateTrainingAsync(tx, e, row, c).ConfigureAwait(false);
                id = e;
            }
            else id = (await people.InsertTrainingAsync(tx, KeyGuid("training", ctx.IdempotencyKey), row, c).ConfigureAwait(false)).Id;
            var line = string.Create(Inv, $"Training {id} · staff {staff} · {course} · {hours} h{(validTo is { } w ? " · valid to " + Wire(w) : string.Empty)}");
            await tx.AuditAsync("MEAS_TRAINING_SAVE", Target("training", Id(id)), line, c).ConfigureAwait(false);
            return R(Id(id), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> SetExitAsync(CommandContext ctx, SetMeasExitRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireHr(await OpenAsync(ctx, KeyExit, ct).ConfigureAwait(false));
        var staff = r.StaffId ?? throw NotFound("Staff member not found.");
        var reason = Text(r.Reason);
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var exitDate = OptDate(r.ExitDate, "Enter the exit date.") ?? today;
        return await RunAsync(ctx, OpExit, r, async (tx, c) =>
        {
            var joined = await people.StaffJoinedAsync(tx, staff, c).ConfigureAwait(false) ?? throw NotFound("Staff member not found.");
            string line;
            if (reason.Length == 0)
            {
                await people.SetExitAsync(tx, staff, null, null, c).ConfigureAwait(false);
                line = $"Staff {staff} · exit reason cleared";
            }
            else
            {
                if (PeopleRules.CheckExit(reason, exitDate, joined, today) is { } bad) throw HccException.Validation(bad);
                await people.SetExitAsync(tx, staff, reason, exitDate, c).ConfigureAwait(false);
                line = $"Staff {staff} · exit reason {reason} · {Wire(exitDate)}";
            }
            await tx.AuditAsync("MEAS_EXIT_SET", Target("staff", Id(staff)), line, c).ConfigureAwait(false);
            return R(Id(staff), line);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Anonymous pulse (PeopleRules.PulseHasNoIdentity): any signed-in user; the row stores the department and the answers only; the audit row has
    /// principal 0 and no name; no idempotency row keyed by the principal. A repeat of the same Idempotency-Key returns the first row.
    /// </summary>
    public async Task<MeasCaptureResult> AddPulseAsync(CommandContext ctx, AddMeasPulseRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        await gate.EnsureOnAsync(ctx.PropertyId, KeyPulse, ct).ConfigureAwait(false);
        var dept = Text(r.DepartmentId).ToUpperInvariant();
        if (PeopleRules.CheckPulse(r.Workload ?? 0, r.Respect ?? 0, r.Tools ?? 0, r.PayFairness ?? 0, r.Recommend ?? -1) is { } bad) throw HccException.Validation(bad);
        if (dept.Length is < 2 or > 10 || !await people.DepartmentExistsAsync(ctx.PropertyId, dept, ct).ConfigureAwait(false)) throw HccException.Validation("Choose your department.");
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var row = new MeasPulseRow(PeopleRules.QuarterKey(today), dept, r.Workload!.Value, r.Respect!.Value, r.Tools!.Value, r.PayFairness!.Value, r.Recommend!.Value);
        var (id, _) = await people.AddPulseAsync(ctx.PropertyId, KeyGuid("pulse", ctx.IdempotencyKey), row, UtcNow, ct).ConfigureAwait(false);
        return R(Id(id), $"Pulse {row.QuarterKey} · {dept} (anonymous)");
    }

    // ═════ housekeeping / engineering (cap-hk) ═════

    public async Task<CommandResult<MeasCaptureResult>> OfflineSetAsync(CommandContext ctx, MeasOfflineSetRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyOoo, ct).ConfigureAwait(false);
        if (!Hk(roles, HkRules.CanSetOffline)) throw Forbidden("Access denied: only a Manager or Supervisor records why a room is offline.");
        var roomNo = Text(r.Room);
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var clear = r.Clear == true;
        var reason = Text(r.Reason);
        var back = clear ? null : OptDate(r.ExpectedReturn, "Enter an expected return date that is not in the past.");
        var note = Opt(r.Note);
        if (!clear) Check(HkRules.CheckOffline(reason, back, note, today));
        var wo = Opt(r.WorkOrder);
        return await RunAsync(ctx, OpOffline, r, async (tx, c) =>
        {
            var room = await hk.RoomIdAsync(tx, roomNo, c).ConfigureAwait(false) ?? throw NotFound("Room not found.");
            string line;
            if (clear)
            {
                if (!await hk.OfflineExistsAsync(tx, room, c).ConfigureAwait(false)) throw NotFound("No offline reason is recorded for this room.");
                await hk.ClearOfflineAsync(tx, room, c).ConfigureAwait(false);
                line = $"Room {roomNo} · offline reason cleared";
            }
            else
            {
                if (wo is not null && (!long.TryParse(wo, NumberStyles.None, Inv, out var woId) || !await hk.WorkOrderExistsAsync(tx, woId, c).ConfigureAwait(false))) throw NotFound("Work order not found.");
                await hk.SetOfflineAsync(tx, room, ClientId(ctx.IdempotencyKey, 80), reason, back, wo, note, c).ConfigureAwait(false);
                line = $"Room {roomNo} · {reason}{(back is { } b ? " · back " + Wire(b) : string.Empty)}{(wo is null ? string.Empty : " · WO " + wo)}";
            }
            await tx.AuditAsync("MEAS_OFFLINE_SET", Target("room", roomNo), line, c).ConfigureAwait(false);
            return R(roomNo, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> MeterSaveAsync(CommandContext ctx, MeasMeterSaveRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyUtilities, ct).ConfigureAwait(false);
        if (!Hk(roles, HkRules.CanSaveMeter)) throw Forbidden("Access denied: only the Owner or Manager edits the meter master.");
        var name = Text(r.Name);
        if (name.Length is < 2 or > 60) throw HccException.Validation("Enter a meter name (2 to 60 characters).");
        var kind = Text(r.Kind);
        if (!HkRules.MeterKinds.Contains(kind)) throw HccException.Validation("Choose a meter type.");
        var unit = Opt(r.Unit) ?? MeterUnits[kind];
        if (unit.Length > 16) throw HccException.Validation("Keep the unit short.");
        var givenId = Opt(r.Id);
        if (givenId is { Length: > 30 }) throw NotFound("Meter not found.");
        return await RunAsync(ctx, OpMeter, r, async (tx, c) =>
        {
            var meters = await hk.MetersAsync(tx, c).ConfigureAwait(false);
            string id;
            if (givenId is not null)
            {
                var cur = meters.FirstOrDefault(m => m.Id == givenId) ?? throw NotFound("Meter not found.");
                if (cur.Kind != kind && await hk.MeterHasReadingsAsync(tx, cur.Id, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A meter with readings keeps its type.");
                if (meters.Any(m => m.Id != givenId && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) throw HccException.Validation("A meter with this name already exists.");
                id = givenId;
            }
            else
            {
                if (meters.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) throw HccException.Validation("A meter with this name already exists.");
                id = HashId("MTR", ctx.IdempotencyKey);
            }
            await hk.UpsertMeterAsync(tx, new MeasMeterView(id, name, kind, unit, r.Active != false), ClientId(ctx.IdempotencyKey, 80), c).ConfigureAwait(false);
            var line = $"{id} · {name} {(givenId is null ? "added" : "saved")}";
            await tx.AuditAsync("MEAS_METER_SAVE", Target("meter", id), line, c).ConfigureAwait(false);
            return R(id, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> ReadingAddAsync(CommandContext ctx, MeasReadingAddRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyUtilities, ct).ConfigureAwait(false);
        if (!Hk(roles, HkRules.CanAddReading)) throw Forbidden("Access denied: PERM-MEAS-002 is required to enter meter readings.");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BehalfPermission, AccessScopes.Property(ctx.PropertyId), ct: ct).ConfigureAwait(false);
        var meterId = Text(r.Meter);
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var date = OptDate(r.Date, "Enter a valid reading date.") ?? today;
        var value = r.Value ?? throw HccException.Validation("Enter the meter reading (zero or more, at most two decimals).");
        var photo = Opt(r.PhotoRef);
        if (photo is { Length: > 200 }) throw HccException.Validation("The photo reference is too long.");
        return await RunAsync(ctx, OpReading, r, async (tx, c) =>
        {
            var meter = (await hk.MetersAsync(tx, c).ConfigureAwait(false)).FirstOrDefault(m => m.Id == meterId) ?? throw NotFound("Meter not found.");
            var existing = await hk.ReadingsAsync(tx, meterId, c).ConfigureAwait(false);
            Check(HkRules.CheckReading(existing.Select(x => new HkReading(x.Date, x.Value)), date, value, today, meter.Active));
            var (id, existed) = await hk.InsertReadingAsync(tx, ClientId(ctx.IdempotencyKey, 80), meterId, date, value, photo, c).ConfigureAwait(false);
            var line = string.Create(Inv, $"{meter.Name} · {value} {meter.Unit} on {Wire(date)}");
            if (!existed) await tx.AuditAsync("MEAS_READING_ADD", Target("meter", meterId), line, c).ConfigureAwait(false);
            return R(Id(id), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> AssetLifeSetAsync(CommandContext ctx, MeasAssetLifeSetRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyAssets, ct).ConfigureAwait(false);
        if (!Hk(roles, HkRules.CanSetAssetLife)) throw Forbidden("Access denied: only the Owner or Accountant records asset life and replacement cost.");
        var asset = Text(r.Asset);
        if (!int.TryParse(asset, NumberStyles.None, Inv, out var assetId)) throw NotFound("Asset not found.");
        var grade = Opt(r.ConditionGrade);
        Check(HkRules.CheckAssetLife(r.ExpectedLifeMonths, grade, r.ReplacementCostPaise));
        if (r.ReplacementCostPaise is > 100_000_000_000) throw HccException.Validation("Enter the replacement cost in whole paise.");
        return await RunAsync(ctx, OpAssetLife, r, async (tx, c) =>
        {
            if (!await hk.AssetExistsAsync(tx, assetId, c).ConfigureAwait(false)) throw NotFound("Asset not found.");
            await hk.UpsertAssetLifeAsync(tx, asset, r.ExpectedLifeMonths, grade, r.ReplacementCostPaise, ClientId(ctx.IdempotencyKey, 80), c).ConfigureAwait(false);
            var line = $"Asset {asset} · life / condition saved"; // no amount in the audit line (read by roles without money)
            await tx.AuditAsync("MEAS_ASSETLIFE_SET", Target("asset", asset), line, c).ConfigureAwait(false);
            return R(asset, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> DiscardAddAsync(CommandContext ctx, MeasDiscardAddRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        var roles = await OpenAsync(ctx, KeyLinen, ct).ConfigureAwait(false);
        if (!Hk(roles, HkRules.CanAddDiscard)) throw Forbidden("Access denied: only a Supervisor or Manager records linen discards.");
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var date = OptDate(r.Date, "Enter a valid date that is not in the future.") ?? today;
        var item = Text(r.Item);
        var reason = Text(r.Reason);
        var qty = r.Qty ?? 0;
        Check(HkRules.CheckDiscard(item, qty, reason, date, today));
        return await RunAsync(ctx, OpDiscard, r, async (tx, c) =>
        {
            var (id, existed) = await hk.InsertDiscardAsync(tx, ClientId(ctx.IdempotencyKey, 80), item, qty, reason, date, c).ConfigureAwait(false);
            var line = $"{qty} × {item} discarded · {reason}";
            if (!existed) await tx.AuditAsync("MEAS_DISCARD_ADD", Target("discard", Id(id)), line, c).ConfigureAwait(false);
            return R(Id(id), line);
        }, ct).ConfigureAwait(false);
    }

    // ═════ revenue (forecast) ═════

    private static void RequireApprove(IReadOnlyCollection<string> roles, string what)
    {
        // Prototype k.req('approve'): Owner and Manager.
        if (!Any(roles, "OWN", "MGR")) throw Forbidden($"Access denied: {what} is kept by the Manager or Owner.");
    }

    public async Task<CommandResult<MeasCaptureResult>> EventSaveAsync(CommandContext ctx, MeasEventSaveRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireApprove(await OpenAsync(ctx, KeyEvents, ct).ConfigureAwait(false), "the events calendar");
        var name = Text(r.Name);
        if (name.Length is < 3 or > 80) throw HccException.Validation("Give the event a name (3–80 characters).");
        var from = Date(r.From, "Choose a valid start date.");
        var to = Date(r.To, "Choose a valid end date.");
        if (to < from) throw HccException.Validation("The end date cannot be before the start date.");
        var kind = Text(r.Kind);
        if (!EventKinds.Contains(kind)) throw HccException.Validation("Choose the event kind.");
        var impact = r.ImpactPct ?? int.MinValue;
        if (impact is < -50 or > 100) throw HccException.Validation("Expected impact must be a whole percent between −50 and +100.");
        var note = Opt(r.Note) is { } n ? (n.Length > 200 ? n[..200] : n) : null;
        var id = Opt(r.Id) ?? HashId("EVT", ctx.IdempotencyKey);
        if (id.Length > 40) throw HccException.Validation("Event id is too long.");
        return await RunAsync(ctx, OpEvent, r, async (tx, c) =>
        {
            var row = new MeasEventView(id, name, Wire(from), Wire(to), kind, impact, note);
            var cur = await revenue.EventAsync(tx, id, c).ConfigureAwait(false);
            if (cur is { Deleted: false } e && e.Event == row) return R(id, $"Event {name} unchanged");
            await revenue.UpsertEventAsync(tx, row, c).ConfigureAwait(false);
            var line = string.Create(Inv, $"Event {(cur is { Deleted: false } ? "updated" : "added")} · {name} · {row.From} to {row.To} · {kind} · {(impact > 0 ? "+" : string.Empty)}{impact}%");
            await tx.AuditAsync("MEAS_EVENT_SAVE", Target("event", id), line, c).ConfigureAwait(false);
            return R(id, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> EventDeleteAsync(CommandContext ctx, MeasEventDeleteRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireApprove(await OpenAsync(ctx, KeyEvents, ct).ConfigureAwait(false), "the events calendar");
        var id = Text(r.Id);
        return await RunAsync(ctx, OpEventDelete, r, async (tx, c) =>
        {
            var cur = await revenue.EventAsync(tx, id, c).ConfigureAwait(false);
            if (cur is not { Deleted: false } e) return R(id, $"Event {id} already removed"); // idempotent repeat
            await revenue.DeleteEventAsync(tx, id, c).ConfigureAwait(false);
            var line = $"Event removed · {e.Event.Name} · {e.Event.From} to {e.Event.To}";
            await tx.AuditAsync("MEAS_EVENT_DELETE", Target("event", id), line, c).ConfigureAwait(false);
            return R(id, line);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Mondays from <paramref name="from"/> to <paramref name="to"/> (prototype fcMondays); the snapshot day itself when there is none.</summary>
    public static IReadOnlyList<DateOnly> Mondays(DateOnly from, DateOnly to)
    {
        var list = new List<DateOnly>();
        var d = from.AddDays(((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7);
        for (; d <= to; d = d.AddDays(7)) list.Add(d);
        return list.Count == 0 ? [to] : list;
    }

    public async Task<CommandResult<MeasCaptureResult>> OtbSnapshotAsync(CommandContext ctx, MeasOtbSnapshotRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireApprove(await OpenAsync(ctx, KeyPickup, ct).ConfigureAwait(false), "the on-the-books snapshot");
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var takenOn = OptDate(r.TakenOn, "Choose a valid snapshot date.") ?? today;
        if (takenOn > today) throw HccException.Validation("A snapshot cannot be taken for a future date.");
        IReadOnlyList<DateOnly> days = [takenOn];
        if (OptDate(r.BackfillFrom, "Choose a valid back-fill start.") is { } bf)
        {
            if (bf > takenOn) throw HccException.Validation("The back-fill start must be on or before the snapshot date.");
            days = Mondays(bf, takenOn);
        }
        return await RunAsync(ctx, OpOtb, r, async (tx, c) =>
        {
            var rows = 0;
            foreach (var d in days) rows += await revenue.SnapshotOtbAsync(tx, d, OtbNights, c).ConfigureAwait(false);
            await revenue.PruneOtbAsync(tx, OtbKeep, c).ConfigureAwait(false);
            var line = $"On-the-books snapshot · {(days.Count == 1 ? Wire(days[0]) : $"{days.Count} Mondays {Wire(days[0])} to {Wire(days[^1])}")} · {rows} night rows";
            await tx.AuditAsync("MEAS_OTB_SNAPSHOT", Target("otb", Wire(takenOn)), line, c).ConfigureAwait(false);
            return R(Wire(takenOn), line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> CompetitorSaveAsync(CommandContext ctx, MeasCompetitorSaveRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireApprove(await OpenAsync(ctx, KeyRateShop, ct).ConfigureAwait(false), "the competitor list");
        var name = Text(r.Name);
        if (name.Length is < 3 or > 80) throw HccException.Validation("Give the competitor a name (3–80 characters).");
        var id = Opt(r.Id) ?? HashId("CMP", ctx.IdempotencyKey);
        if (id.Length > 40) throw HccException.Validation("Competitor id is too long.");
        var active = r.Active ?? true;
        return await RunAsync(ctx, OpCompetitor, r, async (tx, c) =>
        {
            var list = await revenue.CompetitorsAsync(tx, c).ConfigureAwait(false);
            if (list.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw HccException.Validation("A competitor with this name already exists.");
            if (active && list.Count(x => x.Active && x.Id != id) >= MaxActiveCompetitors) throw HccException.Validation("Keep at most 8 active competitors (the plan uses 4–6).");
            var existed = list.Any(x => x.Id == id);
            await revenue.UpsertCompetitorAsync(tx, new MeasCompetitorView(id, name, active), c).ConfigureAwait(false);
            var line = $"Competitor {(existed ? "updated" : "added")} · {name}{(active ? string.Empty : " (inactive)")}";
            await tx.AuditAsync("MEAS_COMPETITOR_SAVE", Target("competitor", id), line, c).ConfigureAwait(false);
            return R(id, line);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MeasCaptureResult>> RateShopAddAsync(CommandContext ctx, MeasRateShopAddRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        RequireApprove(await OpenAsync(ctx, KeyRateShop, ct).ConfigureAwait(false), "the rate shop");
        var clientId = ClientId(Opt(r.ClientId) ?? ctx.IdempotencyKey, 60);
        var stay = Date(r.StayDate, "Choose a valid stay date.");
        var rate = r.RatePaise ?? 0;
        if (rate is <= 0 or > 10_000_000) throw HccException.Validation("Enter the rate in whole rupees (up to ₹1,00,000).");
        var source = Opt(r.Source) ?? "manual";
        if (source.Length > 40) source = source[..40];
        var competitor = Text(r.Competitor);
        var roomType = Text(r.RoomType);
        var today = await common.BusinessDateAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        return await RunAsync(ctx, OpRateShop, r, async (tx, c) =>
        {
            if (await revenue.RateShopExistsAsync(tx, clientId, c).ConfigureAwait(false)) return R("RS-" + clientId, $"Rate shop row {clientId} already recorded");
            var comp = (await revenue.CompetitorsAsync(tx, c).ConfigureAwait(false)).FirstOrDefault(x => x.Id == competitor) ?? throw NotFound("Choose a competitor from the list.");
            if (!comp.Active) throw HccException.Validation("That competitor is inactive.");
            var typeId = await revenue.RoomTypeIdAsync(tx, roomType, c).ConfigureAwait(false) ?? throw HccException.Validation("Unknown room type.");
            await revenue.InsertRateShopAsync(tx, clientId, today, stay, comp.Id, typeId, rate, r.WithBreakfast, source, c).ConfigureAwait(false);
            var line = string.Create(Inv, $"Rate shop · {comp.Name} · {roomType} · {Wire(stay)}{(r.WithBreakfast ? " incl. breakfast" : string.Empty)}");
            await tx.AuditAsync("MEAS_RATESHOP_ADD", Target("rateshop", clientId), line, c).ConfigureAwait(false);
            return R("RS-" + clientId, line);
        }, ct).ConfigureAwait(false);
    }

    // ═════ read (GET /api/v1/meas/captures) ═════

    /// <summary>True when every role of the caller is a housekeeping / maintenance role (DEC-061: no hotel money).</summary>
    public static bool NoMoney(IReadOnlyCollection<string> roles) => roles.Count == 0 || roles.All(r => r is "HKP" or "MNT" or "SUP");

    /// <summary>Applies the read masks: DEC-061 money removed for Housekeeping / Maintenance / Supervisor (and guest links on surveys);
    /// training and exit rows only for the HR-001 holders (Owner, Manager). Pulse rows never carry identity (none is stored).</summary>
    public static MeasCapturesView Mask(MeasCapturesView v, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(roles);
        var hr = Any(roles, "OWN", "MGR");
        var noMoney = NoMoney(roles);
        var seesAssetCost = Hk(roles, HkRules.CanSeeAssetCost);
        return v with
        {
            Turnaway = noMoney ? [.. v.Turnaway.Select(x => x with { QuotedRate = null })] : v.Turnaway,
            Walked = noMoney ? [.. v.Walked.Select(x => x with { CostPaise = null })] : v.Walked,
            AssetLife = seesAssetCost ? v.AssetLife : [.. v.AssetLife.Select(x => x with { ReplacementCostPaise = null })],
            Surveys = noMoney ? [.. v.Surveys.Select(x => x with { Booking = null, EnteredBy = null })] : v.Surveys,
            Otb = noMoney ? [.. v.Otb.Select(x => x with { RevenuePaise = null })] : v.Otb,
            Rateshop = noMoney ? [.. v.Rateshop.Select(x => x with { RatePaise = null })] : v.Rateshop,
            Training = hr ? v.Training : [],
            Exit = hr ? v.Exit : [],
        };
    }

    public async Task<MeasCapturesView> CapturesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var roles = await common.RoleCodesAsync(propertyId, principalId, UtcNow, ct).ConfigureAwait(false);
        return Mask(await reads.CapturesAsync(propertyId, ct).ConfigureAwait(false), roles);
    }
}
