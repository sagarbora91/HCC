using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// Wave B1 · cash leaving the drawer (DEC-073: bank deposit maker-checker with slip photo, till transfer, Owner drawing; DEC-065 locked day)
/// and the shared till next morning (DEC-078, APK12-3), through the REAL composition and access seed (migrations 3100/3101). Audit A03b-01,
/// A03b-06. Every drawer figure is worked out by hand in the comments (₹ = 100 paise).
/// </summary>
[Trait("Category", "Sql")]
public sealed class CashLeavingDrawerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);

    /// <summary>A JPEG of exactly <paramref name="size"/> bytes (SOI marker, filler, EOI marker): what the app's compression produces.</summary>
    private static byte[] Jpeg(int size)
    {
        var b = new byte[size];
        b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xE0;
        for (var i = 4; i < size - 2; i++) b[i] = (byte)(i * 31 % 251);
        b[^2] = 0xFF; b[^1] = 0xD9;
        return b;
    }

    private static CashOutService CashOut(Hotel h) => h.Get<CashOutService>();

    private static Task<CommandResult<CashTransferResult>> BankAsync(Hotel h, int who, int till, long amount, string slip = "DS-1001", byte[]? photo = null, string type = "image/jpeg")
        => CashOut(h).RecordCashBankingAsync(h.As(who), Hotel.Key(), new RecordCashBankingRequest(till, amount, "State Bank of India · current a/c ••4321 (fictional)", slip,
            Convert.ToBase64String(photo ?? Jpeg(2_048)), type), Ct);

    private static Task<CommandResult<CashTransferResult>> ConfirmAsync(Hotel h, int who, CashTransferResult t, bool confirm, string? reason = null)
        => CashOut(h).ConfirmCashBankingAsync(h.As(who), Hotel.Key(), new ConfirmCashBankingRequest(t.TransferId, confirm, reason, t.Version), Ct);

    private static async Task<(int TillId, ShiftResult Shift)> TillAsync(Hotel h, string code, int opener, long floatPaise)
    {
        var till = (await h.Till.CreateTillAsync(h.As(h.Owner), Hotel.Key(), new CreateTillRequest(code, code + " till"), Ct)).Value;
        return (till.TillId, (await h.Till.OpenShiftAsync(h.As(opener), Hotel.Key(), new OpenShiftRequest(till.TillId, floatPaise), Ct)).Value);
    }

    private static async Task<ShiftResult> CountExactAsync(Hotel h, int who, long shiftId)
    {
        var v = await h.BillingQueries.GetShiftAsync(h.As(h.Owner), shiftId, Ct);
        return (await h.Till.SubmitShiftCountAsync(h.As(who), Hotel.Key(), new SubmitShiftCountRequest(shiftId, v.Expected.ExpectedPaise, null, null, null, v.Shift.Version), Ct)).Value;
    }

    private static async Task<AccountingDayView> ProjectedDayAsync(Hotel h, DateOnly date)
    {
        for (var i = 0; i < 10 && await h.Get<AccountingVoucherProjector>().RunOnceAsync(ct: Ct) > 0; i++) { }
        return await h.Get<AccountingQueries>().DayAsync(h.As(h.Accountant), date, Ct);
    }

    [Fact]
    public async Task Bank_deposit_drops_expected_at_record_needs_a_different_Manager_or_Owner_and_a_rejection_puts_the_cash_back()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, _, folio) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-26", ct: Ct);
        var (till, shift) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        await h.PayAsync(folio, 300000, "Cash", till, ct: Ct);
        Assert.Equal(800000, await h.ExpectedAsync(shift.ShiftId, Ct)); // ₹5,000 float + ₹3,000 cash = ₹8,000

        // Who may record: Cashier / Manager from their OWN open shift; never Reception; never from someone else's shift.
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => BankAsync(h, h.Reception, till, 100000));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => BankAsync(h, h.Manager, till, 100000));
        // Nothing below zero: ₹8,000.01 out of an ₹8,000 drawer.
        var below = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => BankAsync(h, h.Cashier, till, 800001));
        Assert.Contains("below zero", below.Message, StringComparison.Ordinal);
        // The slip photo: under 100 KB (102,399 bytes is the largest accepted), JPEG / PNG bytes matching the declared type; slip number required.
        await Expect.Fails(HccErrorCode.VALIDATION, () => BankAsync(h, h.Cashier, till, 100000, photo: Jpeg(102_400)));
        await Expect.Fails(HccErrorCode.VALIDATION, () => BankAsync(h, h.Cashier, till, 100000, photo: "GIF89a-not-a-slip"u8.ToArray(), type: "image/gif"));
        await Expect.Fails(HccErrorCode.VALIDATION, () => BankAsync(h, h.Cashier, till, 100000, photo: Jpeg(4_000), type: "image/png"));
        await Expect.Fails(HccErrorCode.VALIDATION, () => BankAsync(h, h.Cashier, till, 100000, slip: " "));
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_transfer", Ct));

        // ₹6,000 banked with a 102,399-byte photo: expected drops at once, ₹8,000 − ₹6,000 = ₹2,000.
        var big = (await BankAsync(h, h.Cashier, till, 600000, slip: "DS-1001", photo: Jpeg(102_399))).Value;
        Assert.Equal(("banking", "Pending", 600000L), (big.Kind, big.Status, big.AmountPaise));
        Assert.Equal(200000, await h.ExpectedAsync(shift.ShiftId, Ct));
        Assert.Equal(102_399, await h.LongAsync($"SELECT size_bytes FROM pay.banking_slip WHERE transfer_id = {big.TransferId}", Ct));

        // Maker-checker: the Cashier cannot confirm (no PERM-DRW-002); the Manager confirms. A decided deposit stays decided.
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => ConfirmAsync(h, h.Cashier, big, true));
        var confirmed = (await ConfirmAsync(h, h.Manager, big, true)).Value;
        Assert.Equal("Confirmed", confirmed.Status);
        await Expect.Fails(HccErrorCode.STALE_VERSION, () => ConfirmAsync(h, h.Owner, big, false, "late"));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ConfirmAsync(h, h.Owner, confirmed, false, "late"));
        Assert.Equal(200000, await h.ExpectedAsync(shift.ShiftId, Ct)); // confirming moves no cash

        // ₹1,000 banked, then rejected by the Owner (a reason is required): the cash goes back, ₹2,000 − ₹1,000 + ₹1,000 = ₹2,000.
        var small = (await BankAsync(h, h.Cashier, till, 100000, slip: "DS-1002")).Value;
        Assert.Equal(100000, await h.ExpectedAsync(shift.ShiftId, Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => ConfirmAsync(h, h.Owner, small, false));
        Assert.Equal("Rejected", (await ConfirmAsync(h, h.Owner, small, false, "Slip shows ₹100, not ₹1,000 — recount")).Value.Status);
        Assert.Equal(200000, await h.ExpectedAsync(shift.ShiftId, Ct));
        var mv = (await h.BillingQueries.GetShiftAsync(h.As(h.Owner), shift.ShiftId, Ct)).Movements.Where(m => m.Kind == "banking").Select(m => (m.SourceType, m.AmountPaise)).ToList();
        Assert.Equal([("banking", -600000L), ("banking", -100000L), ("banking-return", 100000L)], mv);

        // A Manager banks from their own shift on a second till and cannot confirm their own deposit; another Manager can (via DecideApproval).
        var (till2, shift2) = await TillAsync(h, "BO-1", h.Manager, 100000);
        var own = (await BankAsync(h, h.Manager, till2, 40000, slip: "DS-2001")).Value;
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => ConfirmAsync(h, h.Manager, own, true));
        var viaApproval = await h.Billing.DecideApprovalAsync(h.As(h.Manager2), Hotel.Key(), new DecideApprovalRequest(own.ApprovalId!.Value, true, null, own.Version), Ct);
        Assert.Equal(("cash-banking", "Approved"), (viaApproval.Value.Action, viaApproval.Value.Status));
        Assert.Equal(60000, await h.ExpectedAsync(shift2.ShiftId, Ct)); // ₹1,000 − ₹400 = ₹600

        // ₹500 more banked from FD-1 and left pending: ₹2,000 − ₹500 = ₹1,500 is the count. The count is exact (banked cash is NOT a
        // shortage, A03b-01) and a pending deposit does not hold up the close; it is confirmed the next day.
        var pending = (await BankAsync(h, h.Cashier, till, 50000, slip: "DS-1003")).Value;
        var counted = await CountExactAsync(h, h.Cashier, shift.ShiftId);
        Assert.Equal(("Closed", 150000L, 150000L), (counted.Status, counted.ExpectedPaise!.Value, counted.CountedPaise!.Value));
        await CountExactAsync(h, h.Manager, shift2.ShiftId);
        var preflight = await h.Closing.GetPreflightAsync(h.As(h.Manager), D24, Ct);
        Assert.Empty(preflight.Checks.Single(c => c.Id == "approvals").Items);
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
        Assert.Equal("Confirmed", (await ConfirmAsync(h, h.Owner, pending, true)).Value.Status);

        // Cash book D24 (FD-1 + BO-1): banked net = ₹6,000 + ₹1,000 − ₹1,000 + ₹400 + ₹500 = ₹6,900; FD-1 carries ₹1,500 and BO-1 ₹600.
        var book = await h.Get<CashBookQueries>().GetRangeAsync(h.As(h.Accountant), D24, D24, Ct);
        var day = Assert.Single(book.Days);
        Assert.Equal((690000L, 210000L, 0L, true), (day.BankedPaise, day.CarriedPaise, day.DifferencePaise!.Value, day.Reconciles));
        Assert.Contains(day.Entries, e => e is { Kind: "banking", OutPaise: 600000 } && e.Label.Contains("slip DS-1001", StringComparison.Ordinal));
        Assert.Contains(day.Entries, e => e is { Kind: "banking", InPaise: 100000 });

        // Accounting: one balanced Journal per deposit (Dr 1030 bank / Cr 1000 cash) and the rejection reversed (Dr 1000 / Cr 1030).
        var acc = await ProjectedDayAsync(h, D24);
        var banking = acc.Vouchers.Where(v => v.SourceKey.StartsWith("banking:", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, banking.Count);
        Assert.All(banking, v => Assert.True(v.Balanced && v.Type == "Journal", v.Number));
        var rejected = banking.Single(v => v.SourceKey == $"banking:{small.TransferId}/rejected");
        Assert.Equal([("1000", 100000L, 0L), ("1030", 0L, 100000L)], rejected.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToList());
        Assert.Equal([("1030", 600000L, 0L), ("1000", 0L, 600000L)],
            banking.Single(v => v.SourceKey == $"banking:{big.TransferId}").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToList());
        var bankNet = banking.SelectMany(v => v.Entries).Where(e => e.Account == "1030").Sum(e => e.DebitPaise - e.CreditPaise);
        Assert.Equal(690000, bankNet); // equals the cash book's banked figure
        Assert.True(acc.Balanced);
    }

    [Fact]
    public async Task Locked_cash_book_day_refuses_deciding_its_deposit_until_the_Owner_reopens_it_DEC_065()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (till, shift) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        var deposit = (await BankAsync(h, h.Cashier, till, 200000)).Value;
        await CountExactAsync(h, h.Cashier, shift.ShiftId); // ₹5,000 − ₹2,000 = ₹3,000 counted
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
        await h.Closing.LockCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new LockCashBookDayRequest(D24), Ct);

        var refused = await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ConfirmAsync(h, h.Owner, deposit, true));
        Assert.Contains("2026-10-24", refused.Message, StringComparison.Ordinal);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ConfirmAsync(h, h.Owner, deposit, false, "Not on the bank statement"));
        await h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "Confirm the 24 Oct deposit"), Ct);
        Assert.Equal("Confirmed", (await ConfirmAsync(h, h.Owner, deposit, true)).Value.Status);
        Assert.Equal(D25, await h.BusinessDateAsync(Ct));
    }

    [Fact]
    public async Task Till_transfer_drops_the_giver_now_raises_the_receiver_on_accept_and_returns_on_reject_and_blocks_the_close_while_pending()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (front, giver) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        var (back, receiver) = await TillAsync(h, "BO-1", h.Reception, 100000);
        var svc = CashOut(h);
        Task<CommandResult<CashTransferResult>> Send(int who, int from, int to, long amount) =>
            svc.TransferTillCashAsync(h.As(who), Hotel.Key(), new TransferTillCashRequest(from, to, amount, "Change for the back office"), Ct);
        Task<CommandResult<CashTransferResult>> Decide(int who, CashTransferResult t, bool accept, string? reason = null) =>
            svc.AcceptTillTransferAsync(h.As(who), Hotel.Key(), new AcceptTillTransferRequest(t.TransferId, accept, reason, t.Version), Ct);

        await Expect.Fails(HccErrorCode.VALIDATION, () => Send(h.Cashier, front, front, 1000));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Send(h.Reception, front, back, 1000)); // not Reception's shift
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => Send(h.Cashier, front, back, 500001)); // ₹5,000.01 > ₹5,000

        // ₹2,000 from the Cashier's FD-1 to Reception's BO-1: the giver drops at once (₹5,000 − ₹2,000 = ₹3,000), the receiver waits (₹1,000).
        var t1 = (await Send(h.Cashier, front, back, 200000)).Value;
        Assert.Equal(("till", "Pending", back), (t1.Kind, t1.Status, t1.ToTillId!.Value));
        Assert.Equal((300000L, 100000L), (await h.ExpectedAsync(giver.ShiftId, Ct), await h.ExpectedAsync(receiver.ShiftId, Ct)));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Decide(h.Cashier, t1, true)); // the giver, not on the receiving shift
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Decide(h.Manager, t1, true)); // not the receiver's own shift
        Assert.Equal("Accepted", (await Decide(h.Reception, t1, true)).Value.Status);
        Assert.Equal((300000L, 300000L), (await h.ExpectedAsync(giver.ShiftId, Ct), await h.ExpectedAsync(receiver.ShiftId, Ct))); // ₹1,000 + ₹2,000

        // ₹500 sent and rejected by the receiver (reason required): back to the giver, ₹3,000 − ₹500 + ₹500 = ₹3,000.
        var t2 = (await Send(h.Cashier, front, back, 50000)).Value;
        Assert.Equal(250000, await h.ExpectedAsync(giver.ShiftId, Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => Decide(h.Reception, t2, false));
        Assert.Equal("Rejected", (await Decide(h.Reception, t2, false, "Not needed after all")).Value.Status);
        Assert.Equal((300000L, 300000L), (await h.ExpectedAsync(giver.ShiftId, Ct), await h.ExpectedAsync(receiver.ShiftId, Ct)));

        // A pending transfer blocks the day close; a Manager sends it back, then both drawers count exactly and the day closes.
        var t3 = (await Send(h.Reception, back, front, 10000)).Value;
        var preflight = await h.Closing.GetPreflightAsync(h.As(h.Manager), D24, Ct);
        var item = Assert.Single(preflight.Checks.Single(c => c.Id == "approvals").Items);
        Assert.StartsWith("Till transfer awaiting the receiver", item.Label, StringComparison.Ordinal);
        Assert.Equal("Rejected", (await Decide(h.Manager, t3, false, "Day close: counted separately")).Value.Status);
        Assert.Equal(300000L, (await CountExactAsync(h, h.Cashier, giver.ShiftId)).CountedPaise);
        Assert.Equal(300000L, (await CountExactAsync(h, h.Reception, receiver.ShiftId)).CountedPaise);
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);

        // Cash book: ₹6,000 in both drawers together before and after; no accounting voucher for cash moved between tills.
        var day = Assert.Single((await h.Get<CashBookQueries>().GetRangeAsync(h.As(h.Accountant), D24, D24, Ct)).Days);
        Assert.Equal((600000L, 600000L), (day.OpeningPaise, day.CarriedPaise));
        Assert.Contains(day.Entries, e => e is { Kind: "transfer-out", OutPaise: 200000 });
        Assert.Contains(day.Entries, e => e is { Kind: "transfer-in", InPaise: 200000 });
        var acc = await ProjectedDayAsync(h, D24);
        Assert.DoesNotContain(acc.Vouchers, v => v.SourceKey.StartsWith("transfer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owner_drawing_is_Owner_only_needs_a_reason_and_books_Dr_drawings_Cr_cash()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (till, shift) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        var svc = CashOut(h);
        Task<CommandResult<CashTransferResult>> Draw(int who, long amount, string reason) =>
            svc.RecordOwnerDrawingAsync(h.As(who), Hotel.Key(), new RecordOwnerDrawingRequest(till, amount, reason), Ct);

        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Draw(h.Manager, 150000, "Household"));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Draw(h.Cashier, 150000, "Household"));
        await Expect.Fails(HccErrorCode.VALIDATION, () => Draw(h.Owner, 150000, "  "));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => Draw(h.Owner, 600000, "Household"));
        var d = (await Draw(h.Owner, 150000, "Household expenses (fictional)")).Value;
        Assert.Equal(("drawing", "Posted"), (d.Kind, d.Status));
        // ₹5,000 − ₹1,500 = ₹3,500 expected; the Cashier counts ₹3,500: no difference.
        Assert.Equal(350000, await h.ExpectedAsync(shift.ShiftId, Ct));
        Assert.Equal(350000L, (await CountExactAsync(h, h.Cashier, shift.ShiftId)).CountedPaise);

        var acc = await ProjectedDayAsync(h, D24);
        var v = Assert.Single(acc.Vouchers, x => x.SourceKey == $"drawing:{d.TransferId}");
        Assert.Equal(("Payment", true), (v.Type, v.Balanced));
        Assert.Equal([("3000", 150000L, 0L), ("1000", 0L, 150000L)], v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToList());
        Assert.Equal("Owner's drawings", v.Entries[0].Ledger);
    }

    [Fact]
    public async Task Next_morning_Reception_opens_her_own_shift_with_a_handover_count_and_takes_cash_without_a_Manager_DEC_078()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, _, folio) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-27", ct: Ct);
        var (till, shift) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        await h.PayAsync(folio, 300000, "Cash", till, ct: Ct);
        await CountExactAsync(h, h.Cashier, shift.ShiftId);   // ₹8,000 counted by the Cashier
        Assert.Equal("Completed", (await h.CloseAsync(who: h.Manager, ct: Ct)).Value.Status);

        // The close opened nothing: no shift on 25 Oct, in nobody's name.
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.shift WHERE status IN ('Open','Submitted')", Ct));
        // Reception takes over a drawer the Cashier counted: the handover count is required; a different count needs a reason.
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Till.OpenShiftAsync(h.As(h.Reception), Hotel.Key(), new OpenShiftRequest(till, null), Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Till.OpenShiftAsync(h.As(h.Reception), Hotel.Key(), new OpenShiftRequest(till, null, 790000), Ct));
        var mine = (await h.Till.OpenShiftAsync(h.As(h.Reception), Hotel.Key(), new OpenShiftRequest(till, null, 790000, "One ₹100 note short at handover"), Ct)).Value;
        Assert.Equal((800000L, D25), (mine.OpeningFloatPaise, mine.BusinessDate)); // DEC-035: the float stays the counted ₹8,000
        var desk = (await h.BillingQueries.GetCashDeskAsync(h.As(h.Reception), Ct)).Tills.Single().Active!;
        Assert.Equal((h.Reception, 790000L, -10000L), (desk.OpenedBy!.Value, desk.HandoverCountPaise!.Value, desk.HandoverDifferencePaise!.Value)); // ₹7,900 − ₹8,000
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'SHIFT_HANDOVER_DIFFERENCE' AND target = 'shift:{mine.ShiftId}'", Ct));

        // Reception records cash on her own shift (₹8,000 + ₹1,200 = ₹9,200); the Cashier cannot use her shift; the Manager may (property scope).
        Assert.Equal(mine.ShiftId, (await h.PayAsync(folio, 120000, "Cash", till, who: h.Reception, ct: Ct)).Value.ShiftId);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.PayAsync(folio, 10000, "Cash", till, who: h.Cashier, ct: Ct));
        await h.PayAsync(folio, 10000, "Cash", till, who: h.Manager, ct: Ct);
        Assert.Equal(930000, await h.ExpectedAsync(mine.ShiftId, Ct)); // ₹9,200 + ₹100
        // The same person re-opening after their own count needs no handover count.
        await CountExactAsync(h, h.Reception, mine.ShiftId);
        Assert.Equal(930000, (await h.Till.OpenShiftAsync(h.As(h.Reception), Hotel.Key(), new OpenShiftRequest(till, null), Ct)).Value.OpeningFloatPaise);
    }

    [Fact]
    public async Task Slip_photo_is_viewable_by_Manager_Owner_Accountant_and_purged_after_600_days_while_the_deposit_record_stays()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (till, _) = await TillAsync(h, "FD-1", h.Cashier, 500000);
        var photo = Jpeg(9_000);
        var t = (await BankAsync(h, h.Cashier, till, 100000, photo: photo)).Value;
        var svc = CashOut(h);
        Assert.Equal(photo, Convert.FromBase64String((await svc.SlipAsync(h.As(h.Accountant), t.TransferId, Ct)).ImageBase64));
        Assert.Equal("image/jpeg", (await svc.SlipAsync(h.As(h.Manager), t.TransferId, Ct)).ContentType);
        await svc.SlipAsync(h.As(h.Owner), t.TransferId, Ct);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => svc.SlipAsync(h.As(h.Reception), t.TransferId, Ct));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => svc.SlipAsync(h.As(h.Cashier), t.TransferId, Ct));

        var options = new Hcc.Application.Access.AccessOptions();
        Assert.Equal(TimeSpan.FromDays(600), options.BankingSlipRetention);
        var at = (DateTime)(await h.ScalarAsync($"SELECT recorded_utc FROM pay.cash_transfer WHERE transfer_id = {t.TransferId}", Ct))!;
        var recorded = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc));
        Assert.Equal(0, (await new SqlRetentionPurge(h.Platform.Connections, options, new FixedClock(recorded.AddDays(599))).RunOnceAsync(Ct)).BankingSlips);
        Assert.Equal(1, (await new SqlRetentionPurge(h.Platform.Connections, options, new FixedClock(recorded.AddDays(600).AddMinutes(1))).RunOnceAsync(Ct)).BankingSlips);

        // The photo is gone, the deposit record and its movement stay, and the list says why.
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.banking_slip", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM pay.cash_transfer WHERE transfer_id = {t.TransferId} AND slip_purged_utc IS NOT NULL", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'banking' AND source_id = {t.TransferId}", Ct));
        var row = Assert.Single((await svc.ListAsync(h.As(h.Accountant), D24, D24, Ct)).Items);
        Assert.Equal((false, "Slip photo purged after 600 days.", "DS-1001", "Pending"), (row.SlipStored, row.SlipNote, row.SlipNo, row.Status));
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => svc.SlipAsync(h.As(h.Owner), t.TransferId, Ct));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RETENTION_PURGE' AND target = 'banking-slip'", Ct));
        // Cash-shift holders read the list too (PERM-SHF-001, own scope is enough).
        Assert.Single((await svc.ListAsync(h.As(h.Reception), D24, D24, Ct)).Items);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
