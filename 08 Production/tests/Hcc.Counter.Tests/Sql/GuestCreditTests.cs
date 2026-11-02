using Hcc.Application.Accounting;
using Hcc.Application.Stays;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Stays;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF B2 on a disposable HccTest_* hotel: the guest's credit account (Owner) → RequestGuestCredit (Reception) → Manager approves within
/// PERM-PAY-005 → CheckoutStay names the approval: receipt 'guest-credit' (no cash), an Udhaar receivable linked to guest / folio / receipt (4691),
/// the back-fill skips it; over the limit is refused; the receipt's voucher is a balanced Journal Dr 1120/UD-&lt;key&gt; · Cr guest ledger.</summary>
public sealed class GuestCreditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(long Stay, long Folio, long Guest)> DueOutAsync(Hotel h, string room, string phone)
    {
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        var guest = await h.LongAsync($"SELECT guest_id FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.ScalarAsync($"UPDATE gst.guest SET phone = '{phone}' WHERE guest_id = {guest}", Ct);
        return (stay, folio, guest);
    }

    [Fact]
    public async Task Approved_guest_credit_settles_check_out_on_an_udhaar_receivable()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (stay, folio, guest) = await DueOutAsync(h, "101", "+91 98200 07001");
        var money = h.Get<LfMoneyGuestCommands>();
        await money.SetCreditAccountAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpSetCreditAccount), new(guest, 1_000_000, true, null), Ct);
        var c = h.Get<GuestCreditCommands>();

        var req = await c.RequestAsync(h.Ctx(h.Reception, GuestCreditCommands.OpRequest), new(stay, "Regular, pays monthly"), Ct);
        Assert.Equal(210000, req.Value.AmountPaise); // ₹2,000 + 5 % GST
        await Assert.ThrowsAsync<HccException>(() => c.RequestAsync(h.Ctx(h.Reception, GuestCreditCommands.OpRequest), new(stay, "again"), Ct));
        var own = await Assert.ThrowsAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Reception), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, own.Code);
        var beyond = await Assert.ThrowsAsync<HccException>(() => h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct));
        Assert.Equal(HccErrorCode.APPROVAL_REQUIRED, beyond.Code); // ₹2,100 is above the Manager's decision limit: the Owner decides
        var ok = await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(req.Value.ApprovalId, true, null, req.Value.ApprovalVersion), Ct);
        Assert.Equal("Approved", ok.Value.Status);

        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, version, req.Value.ApprovalId), Ct);

        var receipt = await h.LongAsync($"SELECT receipt_id FROM pay.receipt WHERE folio_id = {folio} AND kind = 'guest-credit' AND amount_paise = 210000 AND till_id IS NULL", Ct);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE receipt_id = {receipt} AND guest_id = {guest} AND folio_id = {folio} AND mobile = '9820007001' AND kind = 'room' AND amount_paise = 210000", Ct));
        Assert.Equal("CheckedOut", await h.ScalarAsync($"SELECT status FROM gst.stay WHERE stay_id = {stay}", Ct));
    }

    [Fact]
    public async Task Over_the_limit_or_without_an_account_is_refused()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (stay, _, guest) = await DueOutAsync(h, "102", "+91 98200 07002");
        var c = h.Get<GuestCreditCommands>();
        var none = await Assert.ThrowsAsync<HccException>(() => c.RequestAsync(h.Ctx(h.Reception, GuestCreditCommands.OpRequest), new(stay, "x"), Ct));
        Assert.Contains("no active credit account", none.Message, StringComparison.Ordinal);
        await h.Get<LfMoneyGuestCommands>().SetCreditAccountAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpSetCreditAccount), new(guest, 100_000, true, null), Ct);
        var over = await Assert.ThrowsAsync<HccException>(() => c.RequestAsync(h.Ctx(h.Reception, GuestCreditCommands.OpRequest), new(stay, "x"), Ct));
        Assert.Contains("Credit limit exceeded", over.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guest_credit_receipt_is_a_balanced_journal_on_the_udhaar_party()
    {
        var s = AccountingEventMapper.Receipt(7, 41, "guest-credit", "Guest credit", new DateOnly(2026, 10, 24), 210000, new PartyContext(Udhaar: "UD-9820007001"));
        Assert.Equal(VoucherType.Journal, s.Type);
        Assert.Equal(s.Debit, s.Credit);
        Assert.Equal(210000, s.Debit);
        var dr = Assert.Single(s.Entries, e => e.DebitPaise > 0);
        Assert.Equal(("receipt:udhaar", "UD-9820007001"), (dr.MapKey, dr.Party));
        Assert.Equal("folio:guest", Assert.Single(s.Entries, e => e.CreditPaise > 0).MapKey);
    }
}
