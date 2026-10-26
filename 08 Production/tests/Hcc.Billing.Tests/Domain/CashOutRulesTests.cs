using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Billing.Tests.Domain;

/// <summary>Wave B1 · cash leaving the drawer (DEC-073) and the shared-till handover (DEC-078): the pure rules.</summary>
public class CashOutRulesTests
{
    private static byte[] Bytes(int size, params byte[] head)
    {
        var b = new byte[size];
        head.CopyTo(b, 0);
        return b;
    }

    private static readonly byte[] JpegHead = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Banking_transfers_and_drawings_leave_expected_cash_and_a_rejection_puts_it_back()
    {
        // ₹5,000 float + ₹3,000 cash − ₹6,000 banked − ₹1,000 banked + ₹1,000 rejected (back) − ₹500 to another till − ₹700 Owner drawing
        // + ₹200 transfer received = ₹1,000; the transfers part is −6,000 − 1,000 + 1,000 − 500 − 700 + 200 = −₹7,000.
        var mv = new List<CashMovement>
        {
            CashDrawer.ForReceipt(ReceiptKind.Payment, Paise.FromRupees(3000)),
            new(CashMovementKind.Banking, Paise.FromRupees(-6000)),
            new(CashMovementKind.Banking, Paise.FromRupees(-1000)),
            new(CashMovementKind.Banking, Paise.FromRupees(1000)),
            new(CashMovementKind.TransferOut, Paise.FromRupees(-500)),
            new(CashMovementKind.OwnerDrawing, Paise.FromRupees(-700)),
            new(CashMovementKind.TransferIn, Paise.FromRupees(200)),
        };
        var e = CashDrawer.Expected(Paise.FromRupees(5000), mv);
        Assert.Equal((Paise.FromRupees(1000), Paise.FromRupees(-7000), true), (e.Expected, e.Transfers, e.Consistent));
        Assert.Equal("owner-drawing", CashDrawer.Code(CashMovementKind.OwnerDrawing));
        Assert.Equal(CashMovementKind.OwnerDrawing, CashDrawer.Parse("owner-drawing"));
    }

    [Fact]
    public void Nothing_takes_the_drawer_below_zero()
    {
        CashOutRules.EnsureCovered(Paise.FromRupees(2000), Paise.FromRupees(2000), "A bank deposit"); // down to exactly ₹0 is allowed
        var ex = Assert.Throws<HccException>(() => CashOutRules.EnsureCovered(Paise.FromRupees(2000), new Paise(200_001), "A bank deposit"));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Throws<HccException>(() => CashOutRules.Amount(0));
        Assert.Throws<HccException>(() => CashOutRules.Amount(-100));
    }

    [Fact]
    public void Slip_photo_is_a_JPEG_or_PNG_under_100_KB_checked_on_the_bytes()
    {
        Assert.Equal(102_399, CashOutRules.SlipMaxBytes); // 100 KB = 102,400 bytes; "under" 100 KB
        var (type, bytes) = CashOutRules.SlipImage(Convert.ToBase64String(Bytes(102_399, JpegHead)), "image/jpeg");
        Assert.Equal(("image/jpeg", 102_399), (type, bytes.Length));
        Assert.Equal("image/png", CashOutRules.SlipImage(Convert.ToBase64String(Bytes(500, PngHead)), "IMAGE/PNG").ContentType);
        static void Refused(string? b64, string? ct) => Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => CashOutRules.SlipImage(b64, ct)).Code);
        Refused(Convert.ToBase64String(Bytes(102_400, JpegHead)), "image/jpeg");      // exactly 100 KB
        Refused(Convert.ToBase64String(Bytes(500, JpegHead)), "image/png");           // declared PNG, JPEG bytes
        Refused(Convert.ToBase64String(Bytes(500, (byte)'G', (byte)'I', (byte)'F')), "image/gif");
        Refused(Convert.ToBase64String(Bytes(500, 0x00, 0x01)), "image/jpeg");        // not a JPEG
        Refused("not base64!", "image/jpeg");
        Refused(null, "image/jpeg");
        Refused(new string('A', 200_000), "image/jpeg");                               // refused before decoding
    }

    [Fact]
    public void Slip_number_bank_and_retention()
    {
        Assert.Equal("DS/2026-001", CashOutRules.SlipNumber(" DS/2026-001 "));
        Assert.Throws<HccException>(() => CashOutRules.SlipNumber("DS<1>"));
        Assert.Throws<HccException>(() => CashOutRules.SlipNumber(""));
        Assert.Throws<HccException>(() => CashOutRules.Bank(new string('x', 101)));
        Assert.Equal(600, CashOutRules.SlipRetentionDays);
        Assert.Equal("Slip photo purged after 600 days.", CashOutRules.SlipPurgedNote);
    }

    [Fact]
    public void Deposit_and_transfer_requests_never_lapse_and_handover_difference_is_count_minus_float()
    {
        var t0 = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        Assert.False(ApprovalRules.IsExpired(ApprovalStatus.Pending, AdjustmentAction.CashBanking, t0, t0.AddDays(30)));
        Assert.False(ApprovalRules.IsExpired(ApprovalStatus.Pending, AdjustmentAction.TillTransfer, t0, t0.AddDays(30)));
        Assert.True(ApprovalRules.IsExpired(ApprovalStatus.Pending, AdjustmentAction.Discount, t0, t0.AddDays(2)));
        Assert.Equal("cash-banking", BillingCodes.Code(AdjustmentAction.CashBanking));
        Assert.Equal(AdjustmentAction.TillTransfer, BillingCodes.ParseAction("till-transfer"));
        // ₹7,900 counted at handover against ₹8,000 carried: −₹100.
        Assert.Equal(new Paise(-10_000), CashOutRules.HandoverDifference(Paise.FromRupees(7900), Paise.FromRupees(8000)));
    }
}
