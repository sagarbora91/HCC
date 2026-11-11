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

    private static CounterBillPostedEvent Event(long billId, string invoice, string name, string? phone) =>
        new(billId, invoice, AccountingDb.Day, "udhaar", null, name, phone, null, FoodPaise: 50000, RoomPaise: 0, ServicePaise: 0, DiscountPaise: 0, CgstPaise: 1250, SgstPaise: 1250, TotalPaise: 52500);

    [Fact]
    public async Task Missing_bill_row_falls_back_to_the_payload_phone_for_old_payloads_and_to_the_name_for_masked_ones()
    {
        await db.PublishAsync(AccountingEvents.CounterBillIssued, 987001, Event(987001, "INV/2627/9902", "Old Payload (fictional)", "9820004444"));      // old: raw number
        await db.PublishAsync(AccountingEvents.CounterBillIssued, 987002, Event(987002, "INV/2627/9903", "Masked Payload (fictional)", "••••••4444")); // new: mask, no row
        Assert.True(await db.ProjectAsync() >= 2);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.source_entry WHERE party_id = 'UD-9820004444';"));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.source_entry WHERE party_id LIKE 'UD-masked-payload%';"));
    }

    [Fact]
    public async Task Bill_row_of_another_property_is_not_read()
    {
        var other = await db.LongAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('CU9', N'Other (fictional)', 'Asia/Kolkata', '2026-10-24');");
        var billId = await db.LongAsync($"""
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, party_phone, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, status)
            OUTPUT INSERTED.bill_id VALUES ({other}, 'INV/2627/9904', '2026-10-24', SYSUTCDATETIME(), {db.Users["OWN"]}, N'Foreign Bill (fictional)', '9820003333', 50000, 2500, 1250, 1250, 52500, 'udhaar', 'Issued');
            """);
        await db.PublishAsync(AccountingEvents.CounterBillIssued, billId, Event(billId, "INV/2627/9904", "Foreign Bill (fictional)", "••••••3333"));
        Assert.True(await db.ProjectAsync() >= 1);
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM acc.source_entry WHERE party_id = 'UD-9820003333';"));
    }
}
