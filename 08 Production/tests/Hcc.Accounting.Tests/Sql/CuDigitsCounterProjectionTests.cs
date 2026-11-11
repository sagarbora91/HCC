using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Counter;
using Hcc.Contracts.Accounting;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>CU digits: the counter-bill outbox payload carries only the masked phone; the projector still names the Udhaar party UD-&lt;mobile&gt; from ctr.bill.</summary>
[Trait("Category", "Sql")]
public sealed class CuDigitsCounterProjectionTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    [Fact]
    public async Task Udhaar_counter_bill_with_a_masked_payload_still_lands_on_the_UD_mobile_party()
    {
        var billId = await db.LongAsync($"""
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, party_phone, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, status)
            OUTPUT INSERTED.bill_id VALUES ({db.PropertyId}, 'INV/2627/9901', '2026-10-24', SYSUTCDATETIME(), {db.Users["OWN"]}, N'Walk-in (fictional)', '9820005555', 50000, 2500, 1250, 1250, 52500, 'udhaar', 'Issued');
            """);
        var e = new CounterBillPostedEvent(billId, "INV/2627/9901", AccountingDb.Day, "udhaar", null, "Walk-in (fictional)", "••••••5555", null,
            FoodPaise: 50000, RoomPaise: 0, ServicePaise: 0, DiscountPaise: 0, CgstPaise: 1250, SgstPaise: 1250, TotalPaise: 52500);
        await db.PublishAsync(AccountingEvents.CounterBillIssued, billId, e);
        Assert.True(await db.ProjectAsync() >= 1);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.source_entry WHERE party_id = 'UD-9820005555';"));
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CounterBillIssued' AND payload LIKE '%9820005555%';"));
    }
}
