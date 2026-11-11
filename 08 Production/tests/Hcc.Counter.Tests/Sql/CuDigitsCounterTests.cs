using System.Text.RegularExpressions;
using Hcc.Application.Accounting;
using Hcc.Application.Counter;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Audit;

namespace Hcc.Counter.Tests.Sql;

/// <summary>CU digits (V-CRM-11 follow-up): counter bill audit text and outbox payloads carry no 10-digit phone; the customer's own bill data and the Udhaar party ledger still use the real number.</summary>
public sealed class CuDigitsCounterTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Regex TenDigits = new(@"\d(?:[\s\-()+]*\d){9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Theory]
    [InlineData("9820005555", true)]
    [InlineData("+91 98200 05555", true)]
    [InlineData("(98200) 05-555", true)]
    [InlineData("name 98200-05555 end", true)]
    [InlineData("INV/2627/0007 · net 50000 + tax 2500 = 52500 · 2026-11-11 2026-11-12", false)]
    [InlineData("12345678", false)]
    [InlineData("Dinner 3 line(s) · ₹1,23,456", false)]
    public void Scrub_masks_only_runs_of_ten_or_more_digits(string text, bool masked)
    {
        Assert.Equal(masked, PhoneDigitsScrub.HasPhoneDigits(text));
        Assert.DoesNotMatch(TenDigits, PhoneDigitsScrub.Scrub(text));
        if (!masked) Assert.Equal(text, PhoneDigitsScrub.Scrub(text));
    }

    [Fact]
    public void Mapper_uses_the_party_the_projector_supplies_and_still_reads_old_payloads()
    {
        var e = new CounterBillPostedEvent(5, "INV/2627/0007", new DateOnly(2026, 10, 24), "udhaar", null, "Walk-in (fictional)", "••••••5555", null,
            FoodPaise: 50000, RoomPaise: 0, ServicePaise: 0, DiscountPaise: 0, CgstPaise: 1250, SgstPaise: 1250, TotalPaise: 52500);
        var viaProjector = AccountingEventMapper.CounterBill(e, false, new PartyContext(Udhaar: "UD-9820005555"));
        Assert.Equal("UD-9820005555", Assert.Single(viaProjector.Entries, x => x.MapKey == SourceRules.UdhaarKey).Party);
        var old = AccountingEventMapper.CounterBill(e with { PartyPhone = "9820005555" }, false);
        Assert.Equal("UD-9820005555", Assert.Single(old.Entries, x => x.MapKey == SourceRules.UdhaarKey).Party);
    }

    [Fact]
    public async Task Issued_bills_leave_no_phone_digits_in_audit_or_outbox_and_the_udhaar_party_still_resolves()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var c = h.Get<CounterBillService>();
        var ud = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in C (fictional)", "+91 98200 05555"),
            [new("Dinner", 2, 40000, "food")], new("udhaar")), Ct);
        var named = await c.IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("98200 06666", "9820006666"),
            [new("Tea", 1, 2500, "food")], new("paid", "UPI", "UPI-REF-9")), Ct);

        var texts = (string?)await h.ScalarAsync("SELECT STRING_AGG(CONCAT(target, '|', detail), CHAR(10)) FROM plat.audit_event WHERE action = 'COUNTER_BILL_ISSUED'", Ct) ?? "";
        var payloads = (string?)await h.ScalarAsync("SELECT STRING_AGG(CAST(payload AS nvarchar(max)), CHAR(10)) FROM plat.outbox WHERE event_type = 'CounterBillIssued'", Ct) ?? "";
        Assert.Contains("Walk-in C (fictional)", texts, StringComparison.Ordinal);
        Assert.Contains("INV/", payloads, StringComparison.Ordinal);
        foreach (var blob in new[] { texts, payloads })
        {
            Assert.DoesNotMatch(TenDigits, blob);
            Assert.DoesNotContain("9820005555", blob, StringComparison.Ordinal);
            Assert.DoesNotContain("9820006666", blob, StringComparison.Ordinal);
        }
        Assert.Contains("5555", payloads, StringComparison.Ordinal); // the display mask keeps the last four digits

        // what the customer-facing bill and the Udhaar receivable hold is unchanged
        Assert.Equal("9820005555", (string?)await h.ScalarAsync($"SELECT party_phone FROM ctr.bill WHERE bill_id = {ud.Value.BillId}", Ct));
        Assert.Equal("9820006666", (string?)await h.ScalarAsync($"SELECT party_phone FROM ctr.bill WHERE bill_id = {named.Value.BillId}", Ct));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE receivable_id = {ud.Value.UdhaarId} AND mobile = '9820005555'", Ct));
    }
}
