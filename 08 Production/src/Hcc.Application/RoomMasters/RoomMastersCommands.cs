using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.RoomMasters;

namespace Hcc.Application.RoomMasters;

/// <summary>
/// Rooms masters commands (MA-1; prototype MST_MASTER_* for masters roomTypes / amenities / occupancyCharges / rooms and RMS_ROOMS_ADD /
/// RMS_ROOM_RENUMBER): every command checks PERM-MST-001 (DEC-MA-1: Owner, Tech admin) or PERM-MST-002 for money (rack rates, the
/// occupancy-charges list: Owner), runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox "MasterChanged" in one
/// transaction), checks expectedVersion (STALE_VERSION) on edits and writes one mst.master_history row per changed field.
/// Rooms: a number or type change needs no room night from the business date on (MASTER_LOCKED_BY_BOOKINGS); retiring places an open-ended
/// inventory block from the business date (so availability and the type counts follow) and is refused while the room is booked.
/// </summary>
public sealed class RoomMastersCommands(ICommandRunner runner, IAccessPolicy policy, IRoomMastersStore store, IMastersStore masters, IRoomBlockPort blocks, TimeProvider clock)
{
    public const string OpSaveRoomType = "SaveRoomType";
    public const string OpRetireRoomType = "RetireRoomType";
    public const string OpReactivateRoomType = "ReactivateRoomType";
    public const string OpReorderRoomTypes = "ReorderRoomTypes";
    public const string OpDeleteRoomType = "DeleteRoomType";
    public const string OpSaveAmenity = "SaveAmenity";
    public const string OpRetireAmenity = "RetireAmenity";
    public const string OpReactivateAmenity = "ReactivateAmenity";
    public const string OpReorderAmenities = "ReorderAmenities";
    public const string OpDeleteAmenity = "DeleteAmenity";
    public const string OpSaveOccupancyCharge = "SaveOccupancyCharge";
    public const string OpRetireOccupancyCharge = "RetireOccupancyCharge";
    public const string OpReactivateOccupancyCharge = "ReactivateOccupancyCharge";
    public const string OpReorderOccupancyCharges = "ReorderOccupancyCharges";
    public const string OpDeleteOccupancyCharge = "DeleteOccupancyCharge";
    public const string OpAddRooms = "AddRooms";
    public const string OpSaveRoom = "SaveRoom";
    public const string OpRenumberRoom = "RenumberRoom";
    public const string OpRetireRoom = "RetireRoom";
    public const string OpReactivateRoom = "ReactivateRoom";
    public const string OpDeleteRoom = "DeleteRoom";
    public const string RetireBlockReason = "Retired room";

    private static readonly string[] Kinds = ["Adult", "Child", "Bed"];

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private Task RequireAsync(CommandContext ctx, bool money, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, money ? MastersPermissions.ManageMoney : MastersPermissions.Manage, null, null, ct);

    private static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static string I(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static string Yn(bool v) => v ? "Yes" : "No";

    private static string L(IReadOnlyList<string> v) => v.Count == 0 ? "—" : string.Join(", ", v);

    /// <summary>Field changes in a fixed order (create: every field with old = null).</summary>
    private static List<(string Field, string? Old, string? New)> Diff(bool create, params (string Field, string? Old, string? New)[] fields) =>
        fields.Where(f => create || !string.Equals(f.Old, f.New, StringComparison.Ordinal)).Select(f => (f.Field, create ? null : f.Old, f.New)).ToList();

    private async Task HistoryAsync(ICommandTransaction tx, CommandContext ctx, string master, long rowId, string code, IEnumerable<(string Field, string? Old, string? New)> changes, CancellationToken ct)
    {
        foreach (var (field, o, n) in changes)
            await masters.AddHistoryAsync(tx, ctx.PropertyId, master, rowId, code, field, o, n, ctx.PrincipalId, UtcNow, ct).ConfigureAwait(false);
    }

    private static async Task FinishAsync(ICommandTransaction tx, string action, string master, string target, long rowId, string what, string detail, CancellationToken ct)
    {
        await tx.AuditAsync(action, target, detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", rowId, new { master, rowId, action = what }, ct).ConfigureAwait(false);
    }

    // ───────────────────────────── Room types (M2) ─────────────────────────────
    public async Task<CommandResult<RoomTypeView>> SaveRoomTypeAsync(CommandContext ctx, SaveRoomTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var f = RoomMastersRules.ValidateRoomType(req.MaxAdults, req.MaxChildren, req.ExtraBedAllowed, req.BedType, req.SizeSqft, req.Amenities, req.Description);
        var newRates = req.RackRates is null ? null : RoomMastersRules.Dated(req.RackRates, "rackRates", "Rack rate");
        if (newRates is not null && newRates.Any(x => x.Paise <= 0)) throw MastersRules.Fail(MasterReasons.Invalid, "Rack rate: each amount must be above ₹0.", "rackRates");
        byte[]? expected = req.RoomTypeId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveRoomType }, req, async (tx, c) =>
        {
            var types = await store.LockRoomTypesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            RoomTypeRec? old = null;
            var code = clean.Code;
            if (req.RoomTypeId is { } id)
            {
                old = types.FirstOrDefault(t => t.RoomTypeId == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This room type was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This room type");
                MastersRules.EnsureActive(old.Active, old.Name);
                if (string.Equals(old.Code, code, StringComparison.OrdinalIgnoreCase)) code = old.Code; // imported mixed-case codes keep their spelling
                var used = await store.RoomTypeUsageAsync(tx, id, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, code, used.Total, "rooms or bookings");
            }

            if (types.Any(t => t.RoomTypeId != old?.RoomTypeId && string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {code} is already used in Room types (retired types keep their code).", "code");
            if (types.Any(t => t.RoomTypeId != old?.RoomTypeId && t.Active && string.Equals(t.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active room type is already called “{clean.Name}”.", "name");
            var amen = await store.LockAmenitiesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            foreach (var a in f.Amenities)
            {
                var row = amen.FirstOrDefault(x => x.Code == a) ?? throw MastersRules.Fail(MasterReasons.Invalid, $"Amenities: {a} is not in Amenities.", "amenities");
                if (!row.Active && !(old?.Amenities.Contains(a) ?? false)) throw MastersRules.Fail(MasterReasons.Retired, $"Amenities: {row.Name} is retired — choose an active one.", "amenities");
            }

            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var tariffs = old is null ? [] : await store.TariffsAsync(tx, old.RoomTypeId, c).ConfigureAwait(false);
            var before = RackList(old?.RackPaise ?? 0, tariffs);
            var after = newRates ?? before;
            var ratesChanged = RoomMastersRules.DatedText(before) != RoomMastersRules.DatedText(after);
            if (ratesChanged)
            {
                await RequireAsync(ctx, true, c).ConfigureAwait(false);   // DEC-058: rack rate = money, Owner only
                RoomMastersRules.EnsurePastUnchanged(before, after, bd, "rackRates", "Rack rate");
            }

            var baseRow = after.FirstOrDefault(x => x.From == RoomMastersRules.BaseFrom);
            var rack = baseRow.Paise > 0 ? baseRow.Paise : (old?.RackPaise ?? 0) > 0 ? old!.RackPaise : after.FirstOrDefault().Paise;
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(types.Select(t => t.SortOrder));
            var next = new RoomTypeRec(old?.RoomTypeId ?? 0, code, clean.Name, f.MaxAdults, f.MaxChildren, f.ExtraBedAllowed, f.BedType, f.SizeSqft, f.Amenities, f.Description,
                sort, old?.Active ?? true, old?.RetiredOn, rack, UtcNow, old?.RowVer ?? []);
            var changes = Diff(old is null,
                ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("sortOrder", I(old?.SortOrder), I(next.SortOrder)),
                ("maxAdults", I(old?.MaxAdults), I(next.MaxAdults)), ("maxChildren", I(old?.MaxChildren), I(next.MaxChildren)),
                ("extraBedAllowed", old is null ? null : Yn(old.ExtraBedAllowed), Yn(next.ExtraBedAllowed)), ("bedType", old?.BedType ?? "—", next.BedType ?? "—"),
                ("sizeSqft", I(old?.SizeSqft), I(next.SizeSqft)), ("amenities", old is null ? null : L(old.Amenities), L(next.Amenities)),
                ("description", old?.Description ?? "—", next.Description ?? "—"), ("rackRate", RoomMastersRules.DatedText(before), RoomMastersRules.DatedText(after)));
            if (old is null) changes.RemoveAll(x => x.New is "—" or "" || x.Field == "rackRate" && after.Count == 0);
            int typeId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                typeId = old.RoomTypeId;
                await store.UpdateRoomTypeAsync(tx, next, expected!, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText("Room types", changes);
            }
            else
            {
                typeId = await store.InsertRoomTypeAsync(tx, ctx.PropertyId, next, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master Room types: added {next.Code} · {next.Name}";
            }

            if (ratesChanged)
                await store.ReplaceTariffsFromAsync(tx, typeId, bd, after.Where(x => x.From >= bd && x.From != RoomMastersRules.BaseFrom).ToList(), c).ConfigureAwait(false);
            await HistoryAsync(tx, ctx, RoomMasterIds.RoomTypes, typeId, next.Code, changes, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_ROOM_TYPE", RoomMasterIds.RoomTypes, "mst-room-type:" + typeId.ToString(CultureInfo.InvariantCulture), typeId, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
            return await RoomTypeViewAsync(tx, ctx.PropertyId, typeId, true, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Base rack rate (from 2000-01-01, when set) + the dated tariffs.</summary>
    public static IReadOnlyList<(DateOnly From, long Paise)> RackList(long rackPaise, IReadOnlyList<(DateOnly From, long Paise)> tariffs)
    {
        ArgumentNullException.ThrowIfNull(tariffs);
        var list = new List<(DateOnly From, long Paise)>();
        if (rackPaise > 0) list.Add((RoomMastersRules.BaseFrom, rackPaise));
        list.AddRange(tariffs.Where(t => t.From != RoomMastersRules.BaseFrom));
        return list.OrderBy(x => x.From).ToList();
    }

    private async Task<RoomTypeView> RoomTypeViewAsync(ICommandTransaction tx, int propertyId, int typeId, bool money, CancellationToken ct)
    {
        var t = (await store.LockRoomTypesAsync(tx, propertyId, ct).ConfigureAwait(false)).First(x => x.RoomTypeId == typeId);
        var used = await store.RoomTypeUsageAsync(tx, typeId, ct).ConfigureAwait(false);
        return View(t, used.Rooms, money ? RackList(t.RackPaise, await store.TariffsAsync(tx, typeId, ct).ConfigureAwait(false)) : []);
    }

    internal static RoomTypeView View(RoomTypeRec t, int rooms, IReadOnlyList<(DateOnly From, long Paise)> rack) => new(t.RoomTypeId, t.Code, t.Name, t.MaxAdults, t.MaxChildren,
        t.ExtraBedAllowed, t.BedType, t.SizeSqft, t.Amenities, t.Description, t.SortOrder, t.Active, t.RetiredOn is null ? null : D(t.RetiredOn), rooms,
        rack.Select(x => new DatedAmount(x.Paise, D(x.From))).ToList(), t.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), VersionToken.ToWire(t.RowVer));

    public Task<CommandResult<StatusResult>> RetireRoomTypeAsync(CommandContext ctx, RoomTypeIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpRetireRoomType, req, RoomMasterKind.RoomType, req?.RoomTypeId ?? 0, req?.ExpectedVersion, false, ct);

    public Task<CommandResult<StatusResult>> ReactivateRoomTypeAsync(CommandContext ctx, RoomTypeIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpReactivateRoomType, req, RoomMasterKind.RoomType, req?.RoomTypeId ?? 0, req?.ExpectedVersion, true, ct);

    public Task<CommandResult<StatusResult>> ReorderRoomTypesAsync(CommandContext ctx, ReorderRoomTypesRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderRoomTypes, req, RoomMasterKind.RoomType, req?.RoomTypeIds, ct);

    public Task<CommandResult<DeleteMasterResult>> DeleteRoomTypeAsync(CommandContext ctx, RoomTypeIdRequest req, CancellationToken ct = default) =>
        DeleteListAsync(ctx, OpDeleteRoomType, req, RoomMasterKind.RoomType, req?.RoomTypeId ?? 0, req?.ExpectedVersion, ct);

    // ───────────────────────────── Amenities (M4) ─────────────────────────────
    public async Task<CommandResult<AmenityView>> SaveAmenityAsync(CommandContext ctx, SaveAmenityRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var icon = RoomMastersRules.ValidateIcon(req.Icon);
        byte[]? expected = req.AmenityId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveAmenity }, req, async (tx, c) =>
        {
            var rows = await store.LockAmenitiesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            AmenityRec? old = null;
            if (req.AmenityId is { } id)
            {
                old = rows.FirstOrDefault(a => a.AmenityId == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This amenity was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This amenity");
                MastersRules.EnsureActive(old.Active, old.Name);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, await store.AmenityUsageAsync(tx, ctx.PropertyId, old.Code, c).ConfigureAwait(false), "room types or rooms");
            }

            if (rows.Any(a => a.AmenityId != old?.AmenityId && a.Code == clean.Code))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in Amenities (retired amenities keep their code).", "code");
            if (rows.Any(a => a.AmenityId != old?.AmenityId && a.Active && string.Equals(a.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active amenity is already called “{clean.Name}”.", "name");
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(a => a.SortOrder));
            var next = new AmenityRec(old?.AmenityId ?? 0, clean.Code, clean.Name, icon, req.ShowOnWebsite, sort, old?.Active ?? true, old?.RetiredOn, old?.RowVer ?? []);
            var changes = Diff(old is null, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("sortOrder", I(old?.SortOrder), I(next.SortOrder)),
                ("icon", old?.Icon ?? "—", next.Icon ?? "—"), ("showOnWebsite", old is null ? null : Yn(old.ShowOnWebsite), Yn(next.ShowOnWebsite)));
            int amenityId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                amenityId = old.AmenityId;
                await store.UpdateAmenityAsync(tx, next, expected!, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText("Amenities", changes);
            }
            else
            {
                amenityId = await store.InsertAmenityAsync(tx, ctx.PropertyId, next, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master Amenities: added {next.Code} · {next.Name}";
            }

            await HistoryAsync(tx, ctx, RoomMasterIds.Amenities, amenityId, next.Code, changes, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_AMENITY", RoomMasterIds.Amenities, "mst-amenity:" + amenityId.ToString(CultureInfo.InvariantCulture), amenityId, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
            var saved = (await store.LockAmenitiesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(a => a.AmenityId == amenityId);
            return View(saved);
        }, ct).ConfigureAwait(false);
    }

    internal static AmenityView View(AmenityRec a) => new(a.AmenityId, a.Code, a.Name, a.Icon, a.ShowOnWebsite, a.SortOrder, a.Active, a.RetiredOn is null ? null : D(a.RetiredOn), VersionToken.ToWire(a.RowVer));

    public Task<CommandResult<StatusResult>> RetireAmenityAsync(CommandContext ctx, AmenityIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpRetireAmenity, req, RoomMasterKind.Amenity, req?.AmenityId ?? 0, req?.ExpectedVersion, false, ct);

    public Task<CommandResult<StatusResult>> ReactivateAmenityAsync(CommandContext ctx, AmenityIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpReactivateAmenity, req, RoomMasterKind.Amenity, req?.AmenityId ?? 0, req?.ExpectedVersion, true, ct);

    public Task<CommandResult<StatusResult>> ReorderAmenitiesAsync(CommandContext ctx, ReorderAmenitiesRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderAmenities, req, RoomMasterKind.Amenity, req?.AmenityIds, ct);

    public Task<CommandResult<DeleteMasterResult>> DeleteAmenityAsync(CommandContext ctx, AmenityIdRequest req, CancellationToken ct = default) =>
        DeleteListAsync(ctx, OpDeleteAmenity, req, RoomMasterKind.Amenity, req?.AmenityId ?? 0, req?.ExpectedVersion, ct);

    // ───────────────────────────── Occupancy and extra charges (M5, money list: Owner) ─────────────────────────────
    public async Task<CommandResult<OccupancyChargeView>> SaveOccupancyChargeAsync(CommandContext ctx, SaveOccupancyChargeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, true, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        RoomMastersRules.ValidateAges(req.ChildAgeFrom, req.ChildAgeTo);
        var lists = new Dictionary<string, IReadOnlyList<(DateOnly From, long Paise)>>
        {
            ["Adult"] = RoomMastersRules.Dated(req.ExtraAdult, "extraAdult", "Extra adult"),
            ["Child"] = RoomMastersRules.Dated(req.ExtraChild, "extraChild", "Extra child"),
            ["Bed"] = RoomMastersRules.Dated(req.ExtraBed, "extraBed", "Extra bed"),
        };
        byte[]? expected = req.ChargeId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveOccupancyCharge }, req, async (tx, c) =>
        {
            var rows = await store.LockOccupancyChargesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            OccupancyRec? old = null;
            if (req.ChargeId is { } id)
            {
                old = rows.FirstOrDefault(a => a.ChargeId == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This occupancy charge was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This occupancy charge");
                MastersRules.EnsureActive(old.Active, old.Name);
            }

            if (rows.Any(a => a.ChargeId != old?.ChargeId && a.Code == clean.Code))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in Occupancy and extra charges.", "code");
            var types = await store.LockRoomTypesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var type = types.FirstOrDefault(t => t.RoomTypeId == req.RoomTypeId) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose a room type.", "roomTypeId");
            if (!type.Active && old?.RoomTypeId != type.RoomTypeId) throw MastersRules.Fail(MasterReasons.Retired, $"{type.Name} is retired — choose an active room type.", "roomTypeId");
            var other = rows.FirstOrDefault(a => a.ChargeId != old?.ChargeId && a.Active && a.RoomTypeId == type.RoomTypeId);
            if (other is not null) throw MastersRules.Fail(MasterReasons.Invalid, $"{other.Name} already holds the charges of this room type — edit or retire it first.", "roomTypeId");
            if (!type.ExtraBedAllowed && lists["Bed"].Count > 0) throw MastersRules.Fail(MasterReasons.Invalid, $"{type.Name} does not allow an extra bed (room types master).", "extraBed");
            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            foreach (var k in Kinds)
                RoomMastersRules.EnsurePastUnchanged(old?.Rates.GetValueOrDefault(k) ?? [], lists[k], bd, "extra" + k, "Extra " + k.ToLowerInvariant());
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(a => a.SortOrder));
            var next = new OccupancyRec(old?.ChargeId ?? 0, clean.Code, clean.Name, type.RoomTypeId, type.Code, req.ChildAgeFrom, req.ChildAgeTo, sort, old?.Active ?? true, old?.RetiredOn,
                old?.RowVer ?? [], lists);
            string R(OccupancyRec? r, string k) => r is null ? "—" : RoomMastersRules.DatedText(r.Rates.GetValueOrDefault(k) ?? []) is { Length: > 0 } s ? s : "—";
            var changes = Diff(old is null, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("sortOrder", I(old?.SortOrder), I(next.SortOrder)),
                ("roomType", old?.RoomTypeCode, next.RoomTypeCode), ("childAgeFrom", I(old?.ChildAgeFrom), I(next.ChildAgeFrom)), ("childAgeTo", I(old?.ChildAgeTo), I(next.ChildAgeTo)),
                ("extraAdult", R(old, "Adult"), R(next, "Adult")), ("extraChild", R(old, "Child"), R(next, "Child")), ("extraBed", R(old, "Bed"), R(next, "Bed")));
            if (old is null) changes.RemoveAll(x => x.New is "—");
            int chargeId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                chargeId = old.ChargeId;
                await store.UpdateOccupancyChargeAsync(tx, next, expected!, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText("Occupancy and extra charges", changes);
            }
            else
            {
                chargeId = await store.InsertOccupancyChargeAsync(tx, ctx.PropertyId, next, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master Occupancy and extra charges: added {next.Code} · {next.Name}";
            }

            await HistoryAsync(tx, ctx, RoomMasterIds.OccupancyCharges, chargeId, next.Code, changes, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_OCC_CHARGE", RoomMasterIds.OccupancyCharges, "mst-occ:" + chargeId.ToString(CultureInfo.InvariantCulture), chargeId, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
            return View((await store.LockOccupancyChargesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(a => a.ChargeId == chargeId), true);
        }, ct).ConfigureAwait(false);
    }

    internal static OccupancyChargeView View(OccupancyRec o, bool money)
    {
        IReadOnlyList<DatedAmount> L(string k) => money ? (o.Rates.GetValueOrDefault(k) ?? []).Select(x => new DatedAmount(x.Paise, D(x.From))).ToList() : [];
        return new(o.ChargeId, o.Code, o.Name, o.RoomTypeId, o.RoomTypeCode, o.ChildAgeFrom, o.ChildAgeTo, L("Adult"), L("Child"), L("Bed"), o.SortOrder, o.Active,
            o.RetiredOn is null ? null : D(o.RetiredOn), VersionToken.ToWire(o.RowVer));
    }

    public Task<CommandResult<StatusResult>> RetireOccupancyChargeAsync(CommandContext ctx, OccupancyChargeIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpRetireOccupancyCharge, req, RoomMasterKind.OccupancyCharge, req?.ChargeId ?? 0, req?.ExpectedVersion, false, ct);

    public Task<CommandResult<StatusResult>> ReactivateOccupancyChargeAsync(CommandContext ctx, OccupancyChargeIdRequest req, CancellationToken ct = default) =>
        StatusAsync(ctx, OpReactivateOccupancyCharge, req, RoomMasterKind.OccupancyCharge, req?.ChargeId ?? 0, req?.ExpectedVersion, true, ct);

    public Task<CommandResult<StatusResult>> ReorderOccupancyChargesAsync(CommandContext ctx, ReorderOccupancyChargesRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderOccupancyCharges, req, RoomMasterKind.OccupancyCharge, req?.ChargeIds, ct);

    public Task<CommandResult<DeleteMasterResult>> DeleteOccupancyChargeAsync(CommandContext ctx, OccupancyChargeIdRequest req, CancellationToken ct = default) =>
        DeleteListAsync(ctx, OpDeleteOccupancyCharge, req, RoomMasterKind.OccupancyCharge, req?.ChargeId ?? 0, req?.ExpectedVersion, ct);

    // ───────────────────────────── Shared status / order / delete (room types, amenities, occupancy charges) ─────────────────────────────
    private static (string Master, string Label, string Singular, string Audit, bool Money) Meta(RoomMasterKind list) => list switch
    {
        RoomMasterKind.RoomType => (RoomMasterIds.RoomTypes, "Room types", "room type", "MST_ROOM_TYPE", false),
        RoomMasterKind.Amenity => (RoomMasterIds.Amenities, "Amenities", "amenity", "MST_AMENITY", false),
        RoomMasterKind.OccupancyCharge => (RoomMasterIds.OccupancyCharges, "Occupancy and extra charges", "occupancy charge", "MST_OCC_CHARGE", true),
        _ => (RoomMasterIds.Rooms, "Rooms", "room", "MST_ROOM", false),
    };

    private sealed record ListRow(int Id, string Code, string Name, int SortOrder, bool Active, byte[] RowVer);

    private async Task<IReadOnlyList<ListRow>> RowsAsync(ICommandTransaction tx, int propertyId, RoomMasterKind list, CancellationToken ct) => list switch
    {
        RoomMasterKind.RoomType => (await store.LockRoomTypesAsync(tx, propertyId, ct).ConfigureAwait(false)).Select(t => new ListRow(t.RoomTypeId, t.Code, t.Name, t.SortOrder, t.Active, t.RowVer)).ToList(),
        RoomMasterKind.Amenity => (await store.LockAmenitiesAsync(tx, propertyId, ct).ConfigureAwait(false)).Select(t => new ListRow(t.AmenityId, t.Code, t.Name, t.SortOrder, t.Active, t.RowVer)).ToList(),
        RoomMasterKind.OccupancyCharge => (await store.LockOccupancyChargesAsync(tx, propertyId, ct).ConfigureAwait(false)).Select(t => new ListRow(t.ChargeId, t.Code, t.Name, t.SortOrder, t.Active, t.RowVer)).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(list)),
    };

    /// <summary>(count, where) of a row for delete / retire decisions.</summary>
    private async Task<(int Count, int InService)> UsageAsync(ICommandTransaction tx, int propertyId, RoomMasterKind list, ListRow row, CancellationToken ct)
    {
        switch (list)
        {
            case RoomMasterKind.RoomType:
                var u = await store.RoomTypeUsageAsync(tx, row.Id, ct).ConfigureAwait(false);
                return (u.Total, u.RoomsInService);
            case RoomMasterKind.Amenity:
                var n = await store.AmenityUsageAsync(tx, propertyId, row.Code, ct).ConfigureAwait(false);
                return (n, 0);
            default:
                return (0, 0);   // occupancy charges are not posted by the server yet (the folio CHARGE carries the label)
        }
    }

    private async Task<CommandResult<StatusResult>> StatusAsync<TReq>(CommandContext ctx, string op, TReq req, RoomMasterKind list, int id, string? version, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var meta = Meta(list);
        await RequireAsync(ctx, meta.Money, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await RowsAsync(tx, ctx.PropertyId, list, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {meta.Singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, $"This {meta.Singular}");
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{r.Name} is already active." : $"{r.Name} is already retired.");
            var used = await UsageAsync(tx, ctx.PropertyId, list, r, c).ConfigureAwait(false);
            if (!active && list == RoomMasterKind.RoomType && used.InService > 0)
                throw MastersRules.Fail(MasterReasons.InUse, $"{r.Name} still has {used.InService} room(s) in service — change their type or retire them first.");
            if (active && rows.Any(x => x.Id != r.Id && x.Active && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {meta.Singular} is already called “{r.Name}” — rename one first.", "name");
            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, list, r.Id, expected, active, active ? null : bd, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, meta.Master, r.Id, r.Code, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var detail = $"Master {meta.Label}: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.Code} · {r.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            await FinishAsync(tx, meta.Audit, meta.Master, $"{meta.Master}:{r.Id}", r.Id, active ? "reactivated" : "retired", detail, c).ConfigureAwait(false);
            return new StatusResult(r.Id, r.Code, active);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<StatusResult>> ReorderAsync<TReq>(CommandContext ctx, string op, TReq req, RoomMasterKind list, IReadOnlyList<int>? ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var meta = Meta(list);
        await RequireAsync(ctx, meta.Money, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await RowsAsync(tx, ctx.PropertyId, list, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(r => r.Id).ToList(), ids, meta.Singular);
            var moved = new List<string>();
            foreach (var r in rows)
            {
                var n = order[r.Id];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, list, r.Id, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await masters.AddHistoryAsync(tx, ctx.PropertyId, meta.Master, r.Id, r.Code, "sortOrder", r.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(r => order[r.Id]).Select(r => r.Code);
            await FinishAsync(tx, meta.Audit, meta.Master, meta.Master + ":order", 0, "reordered",
                $"Master {meta.Label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            return new StatusResult(0, string.Join(",", codes), true);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<DeleteMasterResult>> DeleteListAsync<TReq>(CommandContext ctx, string op, TReq req, RoomMasterKind list, int id, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var meta = Meta(list);
        await RequireAsync(ctx, meta.Money, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await RowsAsync(tx, ctx.PropertyId, list, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {meta.Singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, $"This {meta.Singular}");
            var used = await UsageAsync(tx, ctx.PropertyId, list, r, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(r.Name, used.Count, list == RoomMasterKind.RoomType ? "rooms or bookings" : "room types or rooms");
            await store.DeleteAsync(tx, list, r.Id, expected, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, meta.Master, r.Id, r.Code, "deleted", r.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await FinishAsync(tx, meta.Audit, meta.Master, $"{meta.Master}:{r.Id}", r.Id, "deleted", $"Master {meta.Label}: {r.Code} · {r.Name} deleted (never used)", c).ConfigureAwait(false);
            return new DeleteMasterResult(r.Id, r.Code, true);
        }, ct).ConfigureAwait(false);
    }

    // ───────────────────────────── Rooms (M3) ─────────────────────────────
    internal static RoomMasterView View(RoomRec r) => new(r.RoomId, r.RoomNo, r.FloorId, r.FloorCode, r.RoomTypeId, r.RoomTypeCode, r.Features, r.Smoking, r.ConnectingRoomNo,
        r.Extension, r.Notes, r.Active, r.RetiredOn is null ? null : D(r.RetiredOn), VersionToken.ToWire(r.RowVer));

    private static string LockText(LiveNights n) => $"{string.Join(", ", n.Reservations.Take(4))}{(n.Reservations.Count > 4 ? $" +{n.Reservations.Count - 4}" : "")} (booked until {D(n.Last?.AddDays(1))})";

    public async Task<CommandResult<AddRoomsResult>> AddRoomsAsync(CommandContext ctx, AddRoomsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var nos = RoomMastersRules.RoomNos(req.RoomNos);
        return await runner.RunAsync(ctx with { Operation = OpAddRooms }, req, async (tx, c) =>
        {
            var floor = (await masters.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(f => f.FloorId == req.FloorId)
                ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose a floor.", "floorId");
            if (!floor.Active) throw MastersRules.Fail(MasterReasons.Retired, $"{floor.Name} is retired — choose an active floor.", "floorId");
            var type = (await store.LockRoomTypesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(t => t.RoomTypeId == req.RoomTypeId)
                ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose a room type.", "roomTypeId");
            if (!type.Active) throw MastersRules.Fail(MasterReasons.Retired, $"{type.Name} is retired — choose an active room type.", "roomTypeId");
            if (type.RackPaise <= 0 && (await store.TariffsAsync(tx, type.RoomTypeId, c).ConfigureAwait(false)).Count == 0)
                throw MastersRules.Fail(MasterReasons.Invalid, $"Set the rack rate of {type.Name} before adding rooms of that type.", "roomTypeId");
            var rooms = await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            foreach (var no in nos)
            {
                var taken = rooms.FirstOrDefault(r => string.Equals(r.RoomNo, no, StringComparison.OrdinalIgnoreCase));
                if (taken is not null) throw MastersRules.Fail(MasterReasons.CodeTaken, $"Room {no} already exists{(taken.Active ? "" : " (retired)")}.", "roomNos");
            }

            var ids = new List<int>();
            foreach (var no in nos)
            {
                var id = await store.InsertRoomAsync(tx, ctx.PropertyId, no, floor.FloorId, type.RoomTypeId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                ids.Add(id);
                await masters.AddHistoryAsync(tx, ctx.PropertyId, RoomMasterIds.Rooms, id, no, "added", null, $"{floor.Name} · {type.Name}", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            }

            var label = nos.Count > 2 && nos.All(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                && nos.Select((x, i) => i == 0 || int.Parse(x, CultureInfo.InvariantCulture) == int.Parse(nos[i - 1], CultureInfo.InvariantCulture) + 1).All(b => b)
                ? $"{nos[0]}–{nos[^1]}" : string.Join(", ", nos);
            await FinishAsync(tx, "MST_ROOM", RoomMasterIds.Rooms, "mst-rooms:add", ids[0], "created",
                $"Master Rooms: added {label} ({nos.Count} room{(nos.Count == 1 ? "" : "s")}) on {floor.Name} · {type.Name}", c).ConfigureAwait(false);
            var after = await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            return new AddRoomsResult(after.Where(r => ids.Contains(r.RoomId)).Select(View).ToList());
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RoomMasterView>> SaveRoomAsync(CommandContext ctx, SaveRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var no = RoomMastersRules.RoomNo(req.RoomNo);
        var f = RoomMastersRules.ValidateRoom(no, req.Features, req.Smoking, req.ConnectingRoomNo, req.Extension, req.Notes);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveRoom }, req, async (tx, c) =>
        {
            var rooms = await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var old = rooms.FirstOrDefault(r => r.RoomId == req.RoomId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This room was not found.");
            VersionToken.EnsureMatches(old.RowVer, expected, "This room");
            if (!old.Active) throw MastersRules.Fail(MasterReasons.Retired, $"Room {old.RoomNo} is retired — reactivate it first.");
            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var live = await store.LiveNightsAsync(tx, old.RoomId, bd, c).ConfigureAwait(false);
            if (!string.Equals(no, old.RoomNo, StringComparison.Ordinal)) EnsureRenumber(rooms, old, no, live);
            var floors = await masters.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var floor = floors.FirstOrDefault(x => x.FloorId == req.FloorId) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose a floor.", "floorId");
            if (!floor.Active && old.FloorId != floor.FloorId) throw MastersRules.Fail(MasterReasons.Retired, $"{floor.Name} is retired — choose an active floor.", "floorId");
            var types = await store.LockRoomTypesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var type = types.FirstOrDefault(t => t.RoomTypeId == req.RoomTypeId) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose a room type.", "roomTypeId");
            if (type.RoomTypeId != old.RoomTypeId)
            {
                if (!type.Active) throw MastersRules.Fail(MasterReasons.Retired, $"{type.Name} is retired — choose an active room type.", "roomTypeId");
                if (live.Count > 0)
                    throw MastersRules.Fail(MasterReasons.LockedByBookings, $"Room {old.RoomNo} has bookings {LockText(live)} — change its type from a date with no booking (move them first).", "roomTypeId");
            }

            if (f.ConnectingRoomNo is { } con && !rooms.Any(r => r.RoomNo == con && r.RoomId != old.RoomId))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Connecting room {con}: choose another existing room.", "connectingRoomNo");
            var amen = await store.LockAmenitiesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            foreach (var a in f.Features)
                if (!amen.Any(x => x.Code == a && (x.Active || old.Features.Contains(a)))) throw MastersRules.Fail(MasterReasons.Invalid, $"Extra amenities: {a} is not an active amenity.", "features");
            var next = old with { RoomNo = no, FloorId = floor.FloorId, FloorCode = floor.Code, RoomTypeId = type.RoomTypeId, RoomTypeCode = type.Code, Features = f.Features,
                Smoking = f.Smoking, ConnectingRoomNo = f.ConnectingRoomNo, Extension = f.Extension, Notes = f.Notes };
            var changes = Diff(false, ("code", old.RoomNo, next.RoomNo), ("floor", old.FloorCode, next.FloorCode), ("type", old.RoomTypeCode, next.RoomTypeCode),
                ("features", L(old.Features), L(next.Features)), ("smoking", Yn(old.Smoking), Yn(next.Smoking)), ("connecting", old.ConnectingRoomNo ?? "—", next.ConnectingRoomNo ?? "—"),
                ("extension", old.Extension ?? "—", next.Extension ?? "—"), ("notes", old.Notes ?? "—", next.Notes ?? "—"));
            if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
            if (type.RoomTypeId != old.RoomTypeId) await store.ChangeRoomTypeAsync(tx, ctx.PropertyId, old.RoomId, old.RoomTypeId, type.RoomTypeId, bd, c).ConfigureAwait(false);
            await store.UpdateRoomAsync(tx, next, expected, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await HistoryAsync(tx, ctx, RoomMasterIds.Rooms, old.RoomId, next.RoomNo, changes, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_ROOM", RoomMasterIds.Rooms, "mst-room:" + old.RoomId.ToString(CultureInfo.InvariantCulture), old.RoomId, "changed", MastersRules.AuditText("Rooms", changes), c).ConfigureAwait(false);
            return View((await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(r => r.RoomId == old.RoomId));
        }, ct).ConfigureAwait(false);
    }

    private static void EnsureRenumber(IReadOnlyList<RoomRec> rooms, RoomRec old, string no, LiveNights live)
    {
        var taken = rooms.FirstOrDefault(r => r.RoomId != old.RoomId && string.Equals(r.RoomNo, no, StringComparison.OrdinalIgnoreCase));
        if (taken is not null) throw MastersRules.Fail(MasterReasons.CodeTaken, $"Room {no} already exists{(taken.Active ? "" : " (retired)")}.", "roomNo");
        if (live.Count > 0) throw MastersRules.Fail(MasterReasons.LockedByBookings, $"Room {old.RoomNo} has bookings {LockText(live)} — renumber it when no booking remains (move them first).", "roomNo");
    }

    public async Task<CommandResult<RoomMasterView>> RenumberRoomAsync(CommandContext ctx, RenumberRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var no = RoomMastersRules.RoomNo(req.RoomNo);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpRenumberRoom }, req, async (tx, c) =>
        {
            var rooms = await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var old = rooms.FirstOrDefault(r => r.RoomId == req.RoomId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This room was not found.");
            VersionToken.EnsureMatches(old.RowVer, expected, "This room");
            if (!old.Active) throw MastersRules.Fail(MasterReasons.Retired, $"Room {old.RoomNo} is retired — reactivate it first.");
            if (string.Equals(no, old.RoomNo, StringComparison.Ordinal)) throw HccException.Validation("Nothing changed.");
            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            EnsureRenumber(rooms, old, no, await store.LiveNightsAsync(tx, old.RoomId, bd, c).ConfigureAwait(false));
            await store.UpdateRoomAsync(tx, old with { RoomNo = no }, expected, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, RoomMasterIds.Rooms, old.RoomId, no, "code", old.RoomNo, no, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_ROOM", RoomMasterIds.Rooms, "mst-room:" + old.RoomId.ToString(CultureInfo.InvariantCulture), old.RoomId, "changed",
                $"Master Rooms: code {old.RoomNo} → {no} (renumbered; past stays stay with the room)", c).ConfigureAwait(false);
            return View((await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(r => r.RoomId == old.RoomId));
        }, ct).ConfigureAwait(false);
    }

    public Task<CommandResult<RoomMasterView>> RetireRoomAsync(CommandContext ctx, RoomIdRequest req, CancellationToken ct = default) => RoomStatusAsync(ctx, OpRetireRoom, req, false, ct);

    public Task<CommandResult<RoomMasterView>> ReactivateRoomAsync(CommandContext ctx, RoomIdRequest req, CancellationToken ct = default) => RoomStatusAsync(ctx, OpReactivateRoom, req, true, ct);

    private async Task<CommandResult<RoomMasterView>> RoomStatusAsync(CommandContext ctx, string op, RoomIdRequest req, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rooms = await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var r = rooms.FirstOrDefault(x => x.RoomId == req.RoomId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This room was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This room");
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"Room {r.RoomNo} is already active." : $"Room {r.RoomNo} is already retired.");
            var bd = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            long? block = null;
            if (!active)
            {
                var live = await store.LiveNightsAsync(tx, r.RoomId, bd, c).ConfigureAwait(false);
                if (live.Count > 0) throw MastersRules.Fail(MasterReasons.InUse, $"Room {r.RoomNo} has bookings {LockText(live)} — move them before retiring the room.");
                block = await blocks.PlaceAsync(tx, ctx.PropertyId, r.RoomId, bd, null, RetireBlockReason, "Inventory", null, c).ConfigureAwait(false);
            }
            else if (r.RetireBlockId is { } b)
            {
                try { await blocks.RemoveAsync(tx, ctx.PropertyId, b, c).ConfigureAwait(false); }
                catch (HccException e) when (e.Code == HccErrorCode.NOT_FOUND) { /* released elsewhere (Unblock): nothing to give back */ }
            }

            var current = (await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(x => x.RoomId == r.RoomId);
            await store.SetRoomActiveAsync(tx, r.RoomId, current.RowVer, active, active ? null : bd, block, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, RoomMasterIds.Rooms, r.RoomId, r.RoomNo, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_ROOM", RoomMasterIds.Rooms, "mst-room:" + r.RoomId.ToString(CultureInfo.InvariantCulture), r.RoomId, active ? "reactivated" : "retired",
                $"Master Rooms: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.RoomNo} · Room {r.RoomNo} {(active ? "reactivated" : "retired; past stays keep the room")})", c).ConfigureAwait(false);
            return View((await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).First(x => x.RoomId == r.RoomId));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<DeleteMasterResult>> DeleteRoomAsync(CommandContext ctx, RoomIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, false, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpDeleteRoom }, req, async (tx, c) =>
        {
            var r = (await store.LockRoomsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.RoomId == req.RoomId)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "This room was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This room");
            MastersRules.EnsureDeletable("Room " + r.RoomNo, await store.RoomReferencesAsync(tx, r.RoomId, c).ConfigureAwait(false), "stays, blocks or housekeeping records");
            await store.DeleteRoomAsync(tx, ctx.PropertyId, r.RoomId, r.RoomTypeId, expected, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, RoomMasterIds.Rooms, r.RoomId, r.RoomNo, "deleted", "Room " + r.RoomNo, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await FinishAsync(tx, "MST_ROOM", RoomMasterIds.Rooms, "mst-room:" + r.RoomId.ToString(CultureInfo.InvariantCulture), r.RoomId, "deleted", $"Master Rooms: {r.RoomNo} · Room {r.RoomNo} deleted (never used)", c).ConfigureAwait(false);
            return new DeleteMasterResult(r.RoomId, r.RoomNo, true);
        }, ct).ConfigureAwait(false);
    }
}
