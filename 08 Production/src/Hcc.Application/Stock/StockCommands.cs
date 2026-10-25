using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Stock;
using Hcc.Domain.Errors;
using Hcc.Domain.Stock;

namespace Hcc.Application.Stock;

/// <summary>
/// Linen, laundry and stock use cases (W2-8, prototype module 16 handlers). Every command goes through <see cref="ICommandRunner"/>
/// (Idempotency-Key, audit, outbox); the actor is the session principal. Items are locked in ascending code order before their
/// quantities are read or changed, so concurrent commands cannot drive a location below zero or deadlock.
/// Writes to a closed business date are refused with DAY_CLOSED (P6 IBusinessDayGuard, optional; the DI runner is also guarded).
/// </summary>
public sealed partial class StockCommands(ICommandRunner runner, IAccessPolicy policy, IStockStore store, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null)
{
    public const string OpReceiveGrn = "StockReceiveGrn";
    public const string OpIssue = "StockIssue";
    public const string OpUse = "StockUse";
    public const string OpRoomChange = "StockRoomChange";
    public const string OpLaundrySend = "StockLaundrySend";
    public const string OpLaundryReceive = "StockLaundryReceive";
    public const string OpRecordLoss = "StockRecordLoss";
    public const string OpCountSubmit = "StockCountSubmit";
    public const string OpCountDecide = "StockCountDecide";
    public const string OpItemCreate = "StockItemCreate";
    public const string OpItemSet = "StockItemSet";
    public const string OpSettingsSet = "StockSettingsSet";

    private const string DefaultVendor = "Laundry vendor";

    [GeneratedRegex(@"^[A-Z0-9]{2,6}-[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    // ── permission helpers ──

    private Task RequireWorkAsync(CommandContext ctx, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StockPermissions.Work, AccessScopes.Any, null, ct);

    private Task RequireSuperviseAsync(CommandContext ctx, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StockPermissions.Supervise, null, null, ct);

    private Task<bool> HasAsync(CommandContext ctx, string perm, CancellationToken ct) =>
        policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, perm, AccessScopes.Property(ctx.PropertyId), ct);

    // ── goods receipt ──

    public async Task<CommandResult<ReceiveGrnResult>> ReceiveGrnAsync(CommandContext ctx, ReceiveGrnRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        if (!await HasAsync(ctx, StockPermissions.Supervise, ct).ConfigureAwait(false) && !await HasAsync(ctx, StockPermissions.Money, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Receiving stock needs a supervisor or manager.");
        var supplier = (req.Supplier ?? string.Empty).Trim();
        if (supplier.Length < 2) throw HccException.Validation("Enter the supplier.");
        if (supplier.Length > 120) throw HccException.Validation("The supplier name is at most 120 characters.");
        var reference = string.IsNullOrWhiteSpace(req.Ref) ? null : req.Ref.Trim();
        if (reference is { Length: > 60 }) throw HccException.Validation("The reference is at most 60 characters.");
        var lines = Lines(req.Lines?.Select(l => l?.ItemCode), req.Lines?.Count ?? 0);
        if (req.Lines!.Any(l => l.UnitCostPaise is not null) && !await HasAsync(ctx, StockPermissions.Money, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only roles that see stock values may enter a unit cost.");
        foreach (var l in req.Lines!)
        {
            StockRules.RequireQty(l.Qty);
            if (l.UnitCostPaise is { } c && (c <= 0 || c > StockRules.MaxUnitCostPaise)) throw HccException.Validation("Unit cost must be greater than zero.");
        }

        var byCode = req.Lines!.ToDictionary(l => l.ItemCode.Trim(), StringComparer.Ordinal);
        return await runner.RunAsync(ctx with { Operation = OpReceiveGrn }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var (grnId, grnRef) = await store.InsertGrnAsync(tx, ctx.PropertyId, bd, supplier, reference, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            long value = 0;
            var costed = false;
            foreach (var code in lines)
            {
                var l = byCode[code];
                var it = await LockAsync(tx, ctx, code, c).ConfigureAwait(false);
                var cost = it.CostPaise;
                if (l.UnitCostPaise is { } uc)
                {
                    cost = StockRules.WeightedAverageCost(it.CostPaise, StockRules.Total(it.Stock), uc, l.Qty);
                    await store.SetCostAsync(tx, it.ItemId, cost, c).ConfigureAwait(false);
                    value = checked(value + uc * l.Qty);
                    costed = true;
                }

                await store.AdjustAsync(tx, it.ItemId, StockLocations.Store, l.Qty, c).ConfigureAwait(false);
                await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "grn", it.ItemId, l.Qty, supplier, StockLocations.Store, "GRN-" + Id(grnId), null, ctx.PrincipalId, c).ConfigureAwait(false);
                await store.InsertGrnLineAsync(tx, grnId, it.ItemId, l.Qty, l.UnitCostPaise ?? cost, c).ConfigureAwait(false);
            }

            var detail = $"GRN-{Id(grnId)} · {supplier} · {lines.Count} line(s) received to Main store (simulated GRN)";
            await tx.AuditAsync("STOCK_GRN", Target("stock-grn", grnId), detail, c).ConfigureAwait(false);
            await tx.PublishAsync("StockGoodsReceived", grnId, new { grnId, supplier, lines = lines.Count, valuePaise = costed ? value : (long?)null }, c).ConfigureAwait(false);
            return new ReceiveGrnResult(grnId, grnRef, lines.Count, detail);
        }, ct).ConfigureAwait(false);
    }

    // ── movements ──

    public async Task<CommandResult<StockMoveResult>> IssueAsync(CommandContext ctx, IssueStockRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        StockRules.RequireQty(req.Qty);
        var from = StockRules.RequireLocation(req.From, StockRules.Countable);
        var to = StockRules.RequireLocation(req.To, StockRules.Countable);
        if (from == to) throw HccException.Validation("Choose two different locations.");
        return await runner.RunAsync(ctx with { Operation = OpIssue }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var it = await LockAsync(tx, ctx, req.ItemCode, c).ConfigureAwait(false);
            Have(it, from, req.Qty);
            await store.AdjustAsync(tx, it.ItemId, from, -req.Qty, c).ConfigureAwait(false);
            await store.AdjustAsync(tx, it.ItemId, to, req.Qty, c).ConfigureAwait(false);
            var move = await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "issue", it.ItemId, req.Qty, from, to, null, null, ctx.PrincipalId, c).ConfigureAwait(false);
            var detail = $"{it.Name} × {req.Qty} · {Label(from)} → {Label(to)}";
            await tx.AuditAsync("STOCK_ISSUE", Target("stock-item", it.Code), detail, c).ConfigureAwait(false);
            return new StockMoveResult(move, detail);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<StockMoveResult>> UseAsync(CommandContext ctx, UseStockRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        StockRules.RequireQty(req.Qty);
        var floor = StockRules.RequireLocation(req.Floor, StockRules.Floors);
        var room = RoomRef(req.Room);
        return await runner.RunAsync(ctx with { Operation = OpUse }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var it = await LockAsync(tx, ctx, req.ItemCode, c).ConfigureAwait(false);
            if (it.Kind == StockKinds.Linen) throw HccException.Validation("Linen is not consumed — record a room linen change instead.");
            Have(it, floor, req.Qty);
            await store.AdjustAsync(tx, it.ItemId, floor, -req.Qty, c).ConfigureAwait(false);
            var move = await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "use", it.ItemId, req.Qty, floor, "used", room, null, ctx.PrincipalId, c).ConfigureAwait(false);
            var detail = $"{it.Name} × {req.Qty} used from {Label(floor)}";
            await tx.AuditAsync("STOCK_USE", Target("stock-item", it.Code), detail, c).ConfigureAwait(false);
            return new StockMoveResult(move, detail);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<StockMoveResult>> RoomChangeAsync(CommandContext ctx, RoomChangeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        StockRules.RequireQty(req.Qty);
        var floor = StockRules.RequireLocation(req.Floor, StockRules.Floors);
        var room = RoomRef(req.Room);
        return await runner.RunAsync(ctx with { Operation = OpRoomChange }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var it = await LockAsync(tx, ctx, req.ItemCode, c).ConfigureAwait(false);
            if (it.Kind != StockKinds.Linen) throw HccException.Validation("Only linen is changed in rooms.");
            Have(it, floor, req.Qty);
            Have(it, StockLocations.Rooms, req.Qty);
            await store.AdjustAsync(tx, it.ItemId, floor, -req.Qty, c).ConfigureAwait(false);
            await store.AdjustAsync(tx, it.ItemId, StockLocations.Soiled, req.Qty, c).ConfigureAwait(false);
            var move = await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "room-change", it.ItemId, req.Qty, floor, StockLocations.Rooms, room, null, ctx.PrincipalId, c).ConfigureAwait(false);
            var detail = $"{it.Name} × {req.Qty} changed ({Label(floor)} → rooms; used pieces → soiled)";
            await tx.AuditAsync("STOCK_ROOM_CHANGE", Target("stock-item", it.Code), detail, c).ConfigureAwait(false);
            return new StockMoveResult(move, detail);
        }, ct).ConfigureAwait(false);
    }

    // ── laundry ──

    public async Task<CommandResult<SendLaundryResult>> SendLaundryAsync(CommandContext ctx, SendLaundryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        var lines = Lines(req.Lines?.Select(l => l?.ItemCode), req.Lines?.Count ?? 0);
        var note = Text(req.Note, 400, "The note");
        var byCode = req.Lines!.ToDictionary(l => l.ItemCode.Trim(), StringComparer.Ordinal);
        return await runner.RunAsync(ctx with { Operation = OpLaundrySend }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var items = new List<(StockItemRecord Item, int Qty)>();
            foreach (var code in lines)
            {
                var it = await LockAsync(tx, ctx, code, c).ConfigureAwait(false);
                if (it.Kind != StockKinds.Linen) throw HccException.Validation("Only linen goes to the laundry.");
                var qty = byCode[code].Qty;
                StockRules.RequireQty(qty);
                Have(it, StockLocations.Soiled, qty);
                items.Add((it, qty));
            }

            var settings = await store.SettingsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var vendor = settings.VendorName ?? DefaultVendor;
            var (batchId, version) = await store.InsertBatchAsync(tx, ctx.PropertyId, vendor, bd, UtcNow, ctx.PrincipalId, note, c).ConfigureAwait(false);
            foreach (var (it, qty) in items)
            {
                await store.AdjustAsync(tx, it.ItemId, StockLocations.Soiled, -qty, c).ConfigureAwait(false);
                await store.AdjustAsync(tx, it.ItemId, StockLocations.Laundry, qty, c).ConfigureAwait(false);
                await store.InsertBatchLineAsync(tx, batchId, it.ItemId, qty, c).ConfigureAwait(false);
                await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "laundry-out", it.ItemId, qty, StockLocations.Soiled, StockLocations.Laundry, "LB-" + Id(batchId), null, ctx.PrincipalId, c).ConfigureAwait(false);
            }

            var pieces = items.Sum(i => i.Qty);
            await tx.AuditAsync("STOCK_LAUNDRY_SEND", Target("stock-batch", batchId), $"LB-{Id(batchId)} · {pieces} pieces sent to {vendor} (simulated pickup)", c).ConfigureAwait(false);
            var fresh = await store.LockBatchAsync(tx, ctx.PropertyId, batchId, c).ConfigureAwait(false);
            return new SendLaundryResult(batchId, vendor, pieces, VersionToken.ToWire(fresh?.Version ?? version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReceiveLaundryResult>> ReceiveLaundryAsync(CommandContext ctx, ReceiveLaundryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var returned = (req.Lines ?? []).ToList();
        if (returned.Any(l => l is null)) throw HccException.Validation("Enter the returned count for every item in the batch.");
        var note = Text(req.Note, 400, "The note");
        var money = await HasAsync(ctx, StockPermissions.Money, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpLaundryReceive }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var batch = await store.LockBatchAsync(tx, ctx.PropertyId, req.BatchId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Laundry batch not found.");
            if (batch.Status != "Out") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This batch has already been received.");
            VersionToken.EnsureMatches(batch.Version, expected, "This laundry batch");
            if (returned.Any(r => batch.Lines.All(l => l.ItemCode != r.ItemCode))) throw HccException.Validation("A returned item is not part of this batch.");
            foreach (var l in batch.Lines)
            {
                var r = returned.FirstOrDefault(z => z.ItemCode == l.ItemCode) ?? throw HccException.Validation("Enter the returned count for every item in the batch.");
                if (r.Received < 0 || r.Damaged < 0) throw HccException.Validation("Counts must be whole numbers (0 or more).");
                if ((long)r.Received + r.Damaged > l.Sent) throw HccException.Validation($"Received + damaged cannot exceed the {l.Sent} sent.");
            }

            long charge = 0;
            var short_ = false;
            var lossLines = 0;
            var clean = 0;
            var batchRef = "LB-" + Id(batch.BatchId);
            foreach (var l in batch.Lines.OrderBy(x => x.ItemCode, StringComparer.Ordinal))
            {
                var r = returned.First(z => z.ItemCode == l.ItemCode);
                var it = await LockAsync(tx, ctx, l.ItemCode, c).ConfigureAwait(false);
                var miss = l.Sent - r.Received - r.Damaged;
                await store.SetBatchLineReturnAsync(tx, batch.BatchId, it.ItemId, r.Received, r.Damaged, c).ConfigureAwait(false);
                await store.AdjustAsync(tx, it.ItemId, StockLocations.Laundry, -l.Sent, c).ConfigureAwait(false);
                await store.AdjustAsync(tx, it.ItemId, StockLocations.Store, r.Received, c).ConfigureAwait(false);
                clean += r.Received;
                if (r.Received > 0)
                    await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "laundry-in", it.ItemId, r.Received, StockLocations.Laundry, StockLocations.Store, batchRef, null, ctx.PrincipalId, c).ConfigureAwait(false);
                charge = checked(charge + StockRules.LaundryCharge(r.Received, r.Damaged, it.LaundryRatePaise ?? 0));
                if (r.Damaged > 0)
                {
                    short_ = true;
                    lossLines++;
                    await store.InsertLossAsync(tx, ctx.PropertyId, bd, it.ItemId, StockLocations.Laundry, r.Damaged, StockRules.LossDamagedAtLaundry, null, ctx.PrincipalId, checked(r.Damaged * it.CostPaise), batchRef, UtcNow, c).ConfigureAwait(false);
                    await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "loss", it.ItemId, r.Damaged, StockLocations.Laundry, null, batchRef, StockRules.LossDamagedAtLaundry, ctx.PrincipalId, c).ConfigureAwait(false);
                }

                if (miss > 0)
                {
                    short_ = true;
                    lossLines++;
                    await store.InsertLossAsync(tx, ctx.PropertyId, bd, it.ItemId, StockLocations.Laundry, miss, StockRules.LossMissingFromLaundry, null, ctx.PrincipalId, checked(miss * it.CostPaise), batchRef, UtcNow, c).ConfigureAwait(false);
                    await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "loss", it.ItemId, miss, StockLocations.Laundry, null, batchRef, "Missing from laundry batch", ctx.PrincipalId, c).ConfigureAwait(false);
                }
            }

            var status = short_ ? "Returned short" : "Returned";
            var mergedNote = note is null ? batch.Note : string.Join(" · ", new[] { batch.Note, note }.Where(s => !string.IsNullOrEmpty(s)));
            await store.CompleteBatchAsync(tx, batch.BatchId, expected, status, bd, ctx.PrincipalId, mergedNote, charge, c).ConfigureAwait(false);
            await tx.AuditAsync("STOCK_LAUNDRY_RECEIVE", Target("stock-batch", batch.BatchId), $"{batchRef} · {status.ToLowerInvariant()} · {clean} clean pieces to Main store", c).ConfigureAwait(false);
            await tx.PublishAsync("StockLaundryReturned", batch.BatchId, new { batchId = batch.BatchId, status, chargePaise = charge, lossLines }, c).ConfigureAwait(false);
            return new ReceiveLaundryResult(batch.BatchId, status, clean, lossLines, money ? charge : null);
        }, ct).ConfigureAwait(false);
    }

    // ── losses ──

    public async Task<CommandResult<RecordLossResult>> RecordLossAsync(CommandContext ctx, RecordLossRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        StockRules.RequireQty(req.Qty);
        var loc = StockRules.RequireLocation(req.Loc, StockRules.LossLocations);
        if (req.Reason is null || !StockRules.LossReasons.Contains(req.Reason)) throw HccException.Validation("Choose a reason.");
        var note = Text(req.Note, 400, "The note");
        if (req.Reason == "Other" && (note is null || note.Length < 3)) throw HccException.Validation("Describe the reason.");
        return await runner.RunAsync(ctx with { Operation = OpRecordLoss }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var it = await LockAsync(tx, ctx, req.ItemCode, c).ConfigureAwait(false);
            Have(it, loc, req.Qty);
            await store.AdjustAsync(tx, it.ItemId, loc, -req.Qty, c).ConfigureAwait(false);
            var value = checked(req.Qty * it.CostPaise);
            var lossId = await store.InsertLossAsync(tx, ctx.PropertyId, bd, it.ItemId, loc, req.Qty, req.Reason, note, ctx.PrincipalId, value, null, UtcNow, c).ConfigureAwait(false);
            await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "loss", it.ItemId, req.Qty, loc, null, "LOSS-" + Id(lossId), req.Reason, ctx.PrincipalId, c).ConfigureAwait(false);
            var detail = $"LOSS-{Id(lossId)} · {it.Name} × {req.Qty} written off at {Label(loc)} · {req.Reason}";
            await tx.AuditAsync("STOCK_LOSS", Target("stock-loss", lossId), detail, c).ConfigureAwait(false);
            await tx.PublishAsync("StockLossRecorded", lossId, new { lossId, item = it.Code, qty = req.Qty, valuePaise = value, reason = req.Reason }, c).ConfigureAwait(false);
            return new RecordLossResult(lossId, detail);
        }, ct).ConfigureAwait(false);
    }

    // ── counts ──

    public async Task<CommandResult<SubmitCountResult>> SubmitCountAsync(CommandContext ctx, SubmitCountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireWorkAsync(ctx, ct).ConfigureAwait(false);
        var loc = StockRules.RequireLocation(req.Loc, StockRules.Countable);
        var note = Text(req.Note, 400, "The note");
        var counts = req.Counts ?? new Dictionary<string, int>();
        return await runner.RunAsync(ctx with { Operation = OpCountSubmit }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (await store.HasPendingCountAsync(tx, ctx.PropertyId, loc, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"A count for {Label(loc)} is already awaiting review.");
            var levels = await store.LevelsAtAsync(tx, ctx.PropertyId, loc, c).ConfigureAwait(false);
            if (counts.Keys.Any(k => levels.All(l => l.Code != k))) throw HccException.Validation("A counted item is not a stock item.");
            var lines = new List<(int ItemId, int System, int Counted)>();
            foreach (var l in levels)
            {
                if (!counts.TryGetValue(l.Code, out var n) || n < 0 || n > StockRules.MaxQty)
                    throw HccException.Validation($"Enter a whole-number count for {l.Name}.");
                lines.Add((l.ItemId, l.Qty, n));
            }

            var variance = lines.Count(z => z.Counted != z.System);
            var status = variance > 0 ? "Submitted" : "Posted";
            var (countId, version) = await store.InsertCountAsync(tx, ctx.PropertyId, bd, UtcNow, loc, status, ctx.PrincipalId, note, lines, c).ConfigureAwait(false);
            await tx.AuditAsync("STOCK_COUNT", Target("stock-count", countId),
                $"CNT-{Id(countId)} · {Label(loc)} · {(variance > 0 ? variance.ToString(CultureInfo.InvariantCulture) + " variance line(s) — review required" : "matches system stock")}", c).ConfigureAwait(false);
            return new SubmitCountResult(countId, status, variance, VersionToken.ToWire(version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<DecideCountResult>> DecideCountAsync(CommandContext ctx, DecideCountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireSuperviseAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required.");
        if (reason.Length > 400) throw HccException.Validation("The reason is at most 400 characters.");
        return await runner.RunAsync(ctx with { Operation = OpCountDecide }, req, async (tx, c) =>
        {
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var count = await store.LockCountAsync(tx, ctx.PropertyId, req.CountId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Count not found.");
            if (count.Status != "Submitted") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This count has already been decided.");
            if (count.ActorId == ctx.PrincipalId)
                throw new HccException(HccErrorCode.FORBIDDEN, "You cannot review your own count. Switch to another supervisor or manager.");
            VersionToken.EnsureMatches(count.Version, expected, "This count");
            var countRef = "CNT-" + Id(count.CountId);
            var lossLines = 0;
            long lossValue = 0;
            if (req.Approve)
            {
                foreach (var l in count.Lines.Where(x => x.Counted != x.System).OrderBy(x => x.ItemCode, StringComparer.Ordinal))
                {
                    var d = l.Counted - l.System;
                    var it = await LockAsync(tx, ctx, l.ItemCode, c).ConfigureAwait(false);
                    if (StockRules.Qty(it.Stock, count.Loc) + d < 0) throw HccException.Validation($"{it.Name}: stock moved since the count — count again.");
                    await store.AdjustAsync(tx, it.ItemId, count.Loc, d, c).ConfigureAwait(false);
                    await store.InsertMoveAsync(tx, ctx.PropertyId, bd, UtcNow, "count", it.ItemId, d, count.Loc, null, countRef, reason, ctx.PrincipalId, c).ConfigureAwait(false);
                    if (d < 0)
                    {
                        var v = checked(-d * it.CostPaise);
                        await store.InsertLossAsync(tx, ctx.PropertyId, bd, it.ItemId, count.Loc, -d, StockRules.LossCountShortage, reason, count.ActorId, v, countRef, UtcNow, c).ConfigureAwait(false);
                        lossLines++;
                        lossValue += v;
                    }
                }
            }

            var status = req.Approve ? "Posted" : "Rejected";
            await store.DecideCountAsync(tx, count.CountId, expected, status, ctx.PrincipalId, reason, c).ConfigureAwait(false);
            await tx.AuditAsync("STOCK_COUNT_DECIDE", Target("stock-count", count.CountId), $"{countRef} · {Label(count.Loc)} · variance {(req.Approve ? "posted" : "rejected")}", c).ConfigureAwait(false);
            await tx.PublishAsync("StockCountDecided", count.CountId, new { countId = count.CountId, approved = req.Approve, lossLines, lossValuePaise = lossValue }, c).ConfigureAwait(false);
            return new DecideCountResult(count.CountId, status, lossLines);
        }, ct).ConfigureAwait(false);
    }

    // ── items and settings ──

    public async Task<CommandResult<StockItemWriteResult>> CreateItemAsync(CommandContext ctx, CreateStockItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireSuperviseAsync(ctx, ct).ConfigureAwait(false);
        var code = (req.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code)) throw HccException.Validation("The code looks like LIN-BS (letters/digits, a dash, letters/digits).");
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 2 || name.Length > 120) throw HccException.Validation("Enter the item name (2–120 characters).");
        if (req.Kind is null || !StockRules.Kinds.Contains(req.Kind)) throw HccException.Validation("Choose a kind: linen, amenity or cleaning.");
        var unit = (req.Unit ?? string.Empty).Trim();
        if (unit.Length < 1 || unit.Length > 20) throw HccException.Validation("Enter the unit (1–20 characters).");
        CheckLevels(req.MinQty, req.ReorderQty, req.StdX100);
        var supplier = Text(req.Supplier, 120, "The supplier");
        if (req.UnitCostPaise is not null || req.LaundryRatePaise is not null)
        {
            if (!await HasAsync(ctx, StockPermissions.Money, ct).ConfigureAwait(false))
                throw new HccException(HccErrorCode.FORBIDDEN, "Only roles that see stock values may enter a unit cost or laundry rate.");
            if (req.UnitCostPaise is { } uc && (uc <= 0 || uc > StockRules.MaxUnitCostPaise)) throw HccException.Validation("Unit cost must be greater than zero.");
            if (req.LaundryRatePaise is { } lr && (lr < 0 || lr > StockRules.MaxUnitCostPaise)) throw HccException.Validation("The laundry rate must be 0 or more.");
        }

        if (req.Kind != StockKinds.Linen && (req.LaundryRatePaise is not null || req.Par is { Count: > 0 }))
            throw HccException.Validation("Laundry rate and par levels apply to linen only.");
        CheckPar(req.Par);
        return await runner.RunAsync(ctx with { Operation = OpItemCreate }, req, async (tx, c) =>
        {
            var types = req.Par is { Count: > 0 } ? await RoomTypesAsync(tx, ctx, req.Par.Keys, c).ConfigureAwait(false) : null;
            var (created, itemId, version) = await store.InsertItemAsync(tx, ctx.PropertyId, code, name, req.Kind, unit, req.UnitCostPaise ?? 0, req.MinQty, req.ReorderQty,
                req.StdX100, req.LaundryRatePaise, supplier, UtcNow, c).ConfigureAwait(false);
            if (!created) throw HccException.Validation("A stock item with this code already exists.");
            if (types is not null)
            {
                await store.UpdateItemAsync(tx, itemId, version, req.MinQty, req.ReorderQty, req.StdX100, req.Par!.ToDictionary(p => types[p.Key], p => p.Value), c).ConfigureAwait(false);
            }

            var detail = $"{code} · {name} added ({req.Kind}, {unit})";
            await tx.AuditAsync("STOCK_ITEM_CREATE", Target("stock-item", code), detail, c).ConfigureAwait(false);
            var fresh = await store.LockItemAsync(tx, ctx.PropertyId, code, c).ConfigureAwait(false);
            return new StockItemWriteResult(code, detail, VersionToken.ToWire(fresh?.Version ?? version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<StockItemWriteResult>> SetItemAsync(CommandContext ctx, SetStockItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireSuperviseAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        CheckLevels(req.MinQty, req.ReorderQty, req.StdX100);
        CheckPar(req.Par);
        return await runner.RunAsync(ctx with { Operation = OpItemSet }, req, async (tx, c) =>
        {
            var it = await LockAsync(tx, ctx, req.Code, c).ConfigureAwait(false);
            VersionToken.EnsureMatches(it.Version, expected, "This item");
            Dictionary<int, int>? par = null;
            if (req.Par is not null)
            {
                if (it.Kind != StockKinds.Linen) throw HccException.Validation("Par levels apply to linen only.");
                var types = await RoomTypesAsync(tx, ctx, req.Par.Keys, c).ConfigureAwait(false);
                par = req.Par.ToDictionary(p => types[p.Key], p => p.Value);
            }

            var version = await store.UpdateItemAsync(tx, it.ItemId, expected, req.MinQty, req.ReorderQty, req.StdX100 ?? it.StdX100, par, c).ConfigureAwait(false);
            var detail = $"{it.Name} · minimum {req.MinQty}, reorder {req.ReorderQty}" + (req.Par is null ? string.Empty : " · par " + string.Join(", ", req.Par.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}")));
            await tx.AuditAsync("STOCK_ITEM_SET", Target("stock-item", it.Code), detail, c).ConfigureAwait(false);
            return new StockItemWriteResult(it.Code, detail, VersionToken.ToWire(version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<StockSettingsResult>> SetSettingsAsync(CommandContext ctx, SetStockSettingsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireSuperviseAsync(ctx, ct).ConfigureAwait(false);
        if (req.ParSets is < 1 or > 10) throw HccException.Validation("Par sets in circulation must be 1–10.");
        if (req.TurnaroundDays is < 0 or > 30) throw HccException.Validation("Laundry turnaround must be 0–30 days.");
        var vendor = Text(req.VendorName, 120, "The vendor name");
        return await runner.RunAsync(ctx with { Operation = OpSettingsSet }, req, async (tx, c) =>
        {
            await store.UpsertSettingsAsync(tx, ctx.PropertyId, new StockSettingsRecord(req.ParSets, vendor, req.TurnaroundDays), c).ConfigureAwait(false);
            await tx.AuditAsync("STOCK_SETTINGS", Target("stock-settings", ctx.PropertyId), $"Par sets {req.ParSets} · vendor {vendor ?? "(none)"} · turnaround {req.TurnaroundDays} d", c).ConfigureAwait(false);
            return new StockSettingsResult(req.ParSets, vendor, req.TurnaroundDays);
        }, ct).ConfigureAwait(false);
    }

    // ── helpers ──

    private async Task<StockItemRecord> LockAsync(ICommandTransaction tx, CommandContext ctx, string? code, CancellationToken ct) =>
        await store.LockItemAsync(tx, ctx.PropertyId, (code ?? string.Empty).Trim(), ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Choose a stock item.");

    private async Task<IReadOnlyDictionary<string, int>> RoomTypesAsync(ICommandTransaction tx, CommandContext ctx, IEnumerable<string> keys, CancellationToken ct)
    {
        var types = await store.RoomTypeIdsAsync(tx, ctx.PropertyId, ct).ConfigureAwait(false);
        if (keys.Any(k => !types.ContainsKey(k))) throw HccException.Validation("Par levels need an existing room type.");
        return types;
    }

    private static void Have(StockItemRecord it, string loc, int qty)
    {
        var have = StockRules.Qty(it.Stock, loc);
        if (have < qty) throw HccException.Validation($"Only {have} {it.Name} at {Label(loc)}.");
    }

    /// <summary>Distinct, trimmed item codes of a line list in the lock order (ordinal).</summary>
    private static List<string> Lines(IEnumerable<string?>? codes, int count)
    {
        if (codes is null || count < 1) throw HccException.Validation("Add at least one item line.");
        if (count > 100) throw HccException.Validation("At most 100 lines per command.");
        var list = codes.Select(c => (c ?? string.Empty).Trim()).ToList();
        if (list.Any(c => c.Length == 0)) throw HccException.Validation("Choose a stock item.");
        if (list.Distinct(StringComparer.Ordinal).Count() != list.Count) throw HccException.Validation("Each item may appear only once.");
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void CheckLevels(int min, int reorder, int? std)
    {
        if (min < 0 || min > StockRules.MaxQty || reorder < 0 || reorder > StockRules.MaxQty)
            throw HccException.Validation("Minimum and reorder quantity must be whole numbers (0 or more).");
        if (std is < 0 or > 10000) throw HccException.Validation("Standard use per room-night must be 0–100.");
    }

    private static void CheckPar(IReadOnlyDictionary<string, int>? par)
    {
        if (par is not null && par.Values.Any(v => v is < 0 or > 20)) throw HccException.Validation("Par per room must be 0–20 pieces.");
    }

    private static string? Text(string? value, int max, string what)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > max) throw HccException.Validation($"{what} is at most {max} characters.");
        return t;
    }

    private static string? RoomRef(string? room) => Text(room, 20, "The room");

    internal static string Label(string loc) => loc switch
    {
        "store" => "Main store", "floor1" => "Floor 1 pantry", "floor2" => "Floor 2 pantry", "floor3" => "Floor 3 pantry",
        "rooms" => "In rooms", "soiled" => "Soiled (to send)", "laundry" => "At laundry", _ => loc,
    };

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Target(string kind, object id) => kind + "-" + Convert.ToString(id, CultureInfo.InvariantCulture);
}
