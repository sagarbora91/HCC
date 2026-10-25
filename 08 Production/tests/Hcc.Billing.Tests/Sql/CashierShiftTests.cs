using System.Data;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>Tills, shifts and counts (DEC-035, DEC-052; PERM-SHF-001/002, PERM-SOD-004).</summary>
[Trait("Category", "Sql")]
public class CashierShiftTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ShiftView> ViewAsync(long shiftId) => await db.Queries.GetShiftAsync(db.As(Who.Owner), shiftId, Ct);

    [Fact]
    public async Task First_shift_needs_a_float_later_shifts_carry_the_last_count_and_one_shift_is_active_per_till()
    {
        var till = (await db.Cashier.CreateTillAsync(db.As(Who.Owner), BillingDb.Key(), new CreateTillRequest("front", "Front desk"), Ct)).Value;
        Assert.Equal("FRONT", till.Code);
        await Fails(HccErrorCode.VALIDATION, () => db.Cashier.CreateTillAsync(db.As(Who.Owner), BillingDb.Key(), new CreateTillRequest("FRONT", "Duplicate"), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Cashier.CreateTillAsync(db.As(Who.Manager), BillingDb.Key(), new CreateTillRequest("X1", "No config right"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till.TillId, null), Ct));
        var s1 = (await db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till.TillId, 500_000), Ct)).Value;
        Assert.Equal((500_000L, "Open", BillingDb.Day), (s1.OpeningFloatPaise, s1.Status, s1.BusinessDate));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Cashier.OpenShiftAsync(db.As(Who.Cashier2), BillingDb.Key(), new OpenShiftRequest(till.TillId, null), Ct));

        var (_, folio) = await db.NewFolioAsync();
        await db.PayAsync(folio, 120_000, "Cash", till: till.TillId);
        var closed = await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(), new SubmitShiftCountRequest(s1.ShiftId, 620_000, null, null, null, (await ViewAsync(s1.ShiftId)).Shift.Version), Ct);
        Assert.Equal(("Closed", 620_000L, 620_000L), (closed.Value.Status, closed.Value.ExpectedPaise!.Value, closed.Value.CountedPaise!.Value));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.PayAsync(folio, 1_000, "Cash", till: till.TillId));

        await Fails(HccErrorCode.VALIDATION, () => db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till.TillId, 100), Ct));
        var s2 = (await db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till.TillId, 620_000), Ct)).Value;
        Assert.Equal(620_000, s2.OpeningFloatPaise);
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM pay.shift WHERE till_id = @t", ("@t", SqlDbType.Int, till.TillId)));
    }

    [Fact]
    public async Task Count_difference_needs_a_reason_and_another_persons_approval_within_their_limit()
    {
        var (till, shift) = await db.OpenTillAsync(100_000);
        var (_, folio) = await db.NewFolioAsync();
        await db.PayAsync(folio, 50_000, "Cash", till: till);
        var v = (await ViewAsync(shift.ShiftId)).Shift.Version;
        await Fails(HccErrorCode.VALIDATION, () => db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(), new SubmitShiftCountRequest(shift.ShiftId, 120_000, null, null, null, v), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 120_000, new Dictionary<string, int> { ["500"] = 2 }, 0, "Short", v), Ct));

        // Short by ₹300 (> manager's ₹200 limit).
        var submitted = (await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 120_000, new Dictionary<string, int> { ["500"] = 2, ["200"] = 1 }, 0, "Change given twice", v), Ct)).Value;
        Assert.Equal(("Submitted", 150_000L), (submitted.Status, submitted.ExpectedPaise!.Value));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.PayAsync(folio, 1_000, "Cash", till: till));
        var approval = (await db.LongAsync("SELECT approval_id FROM pay.shift WHERE shift_id = @s", ("@s", SqlDbType.BigInt, shift.ShiftId)));
        var aVersion = Convert.ToBase64String((byte[])(await db.ScalarAsync("SELECT row_ver FROM bill.approval WHERE approval_id = @a", ("@a", SqlDbType.BigInt, approval)))!);
        Assert.Equal(30_000, await db.LongAsync("SELECT amount_paise FROM bill.approval WHERE approval_id = @a", ("@a", SqlDbType.BigInt, approval)));

        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.DecideApprovalAsync(db.As(Who.Cashier), BillingDb.Key(), new DecideApprovalRequest(approval, true, null, aVersion), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.DecideApprovalAsync(db.As(Who.Cashier2), BillingDb.Key(), new DecideApprovalRequest(approval, true, null, aVersion), Ct));
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(approval, true, null, aVersion), Ct));

        // Rejected → shift reopens for a recount; the recount is exact and closes it.
        var rejected = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(approval, false, "Recount please", aVersion), Ct);
        Assert.Equal("Rejected", rejected.Value.Status);
        var reopened = await ViewAsync(shift.ShiftId);
        Assert.Equal("Open", reopened.Shift.Status);
        var shortAgain = (await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 149_900, null, null, "₹1 coin missing", reopened.Shift.Version), Ct)).Value;
        var a2 = shortAgain.ApprovalId!.Value;
        var a2Version = Convert.ToBase64String((byte[])(await db.ScalarAsync("SELECT row_ver FROM bill.approval WHERE approval_id = @a", ("@a", SqlDbType.BigInt, a2)))!);
        var accepted = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(a2, true, null, a2Version), Ct);
        Assert.Equal("Approved", accepted.Value.Status);
        var final = await ViewAsync(shift.ShiftId);
        Assert.Equal(("Closed", 149_900L, 150_000L), (final.Shift.Status, final.Shift.CountedPaise!.Value, final.Shift.ExpectedPaise!.Value));
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM pay.shift_count WHERE shift_id = @s", ("@s", SqlDbType.BigInt, shift.ShiftId)));

        // DEC-035: the next shift's float is the counted cash, not the expected figure.
        var next = await db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till, null), Ct);
        Assert.Equal(149_900, next.Value.OpeningFloatPaise);
    }

    [Fact]
    public async Task Outlet_cash_and_expenses_move_the_shared_drawer_through_the_port_with_sign_checks()
    {
        var (till, shift) = await db.OpenTillAsync(10_000);
        await db.Runner.RunAsync(new(db.PropertyId, Who.Cashier, "TestPos", BillingDb.Key()), new { t = 1 }, async (tx, ct) =>
        {
            await db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.PosSale, "pos-receipt", 9001, new Paise(55_000), "Restaurant · BILL-1", "Table 4", ct);
            await db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.PosVoid, "pos-receipt", 9002, new Paise(-5_000), "Restaurant void · BILL-1", "Table 4", ct);
            await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.Expense, "expense", 9003, new Paise(1_000), "Wrong sign", "", ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.Payment, "receipt", 9004, new Paise(1_000), "Folio cash", "", ct));
            await tx.AuditAsync("TEST", "till", "pos", ct);
            return new { ok = true };
        }, Ct);
        var v = await ViewAsync(shift.ShiftId);
        Assert.Equal((50_000L, 60_000L), (v.Expected.RestaurantPaise, v.Expected.ExpectedPaise));
        Assert.Equal(2, v.Movements.Count);
    }
}
