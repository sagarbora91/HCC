using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// W1a LG (LG-HISTORY-CONTRACT §9): Customers 360 merges the old program's stays (lgc.v_customer_stay). Live fixture = Asha Rao (3 stays, 6 nights,
/// 1,732,500 paise, first 2026-10-10). Fictional legacy rows for her key: 2 checked-out stays with bills (3 + 1 nights, 945,000 + 210,000 paise)
/// and one Duplicate copy (hidden). Summary Stays stay live-only; LegacyStays, Nights, LifetimePaise and FirstStay include legacy; money masked as live.
/// </summary>
[Trait("Category", "Sql")]
public class CustomersLegacySqlTests : IAsyncLifetime
{
    private CrmWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await CrmWorld.CreateAsync(Ct);
        await StayAsync("2024-25", 101, CrmWorld.Asha, "2024-08-01", "2024-08-04", 3, "CheckedOut", 900, 945_000);
        await StayAsync("2025-26", 55, CrmWorld.Asha, "2025-12-20", "2025-12-21", 1, "CheckedOut", 300, 210_000);
        await StayAsync("2024-25", 140, CrmWorld.Asha, "2025-12-20", "2025-12-21", 1, "Duplicate", null, null);
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task StayAsync(string fy, long booking, string key, string arrival, string departure, int nights, string status, int? billNo, long? net)
    {
        var p = _w.PropertyId;
        await _w.ExecAsync($"""
            INSERT lgc.stay(property_id, legacy_fy, legacy_booking_id, legacy_guest_id, customer_key, guest_name_snapshot, arrival_date, arrival_utc, departure_date, departure_utc,
                            nights, adults, children, room_no, room_count, status, legacy_bill_no)
            VALUES ({p}, '{fy}', {booking}, 8001, '{key}', N'Asha Rao', '{arrival}', '{arrival}', '{departure}', '{departure}', {nights}, 2, 0, '101', 1, '{status}', {(billNo is null ? "NULL" : billNo.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))});
            """, Ct);
        if (net is null) return;
        await _w.ExecAsync($"""
            INSERT lgc.bill(property_id, legacy_fy, legacy_bill_no, legacy_transaction_id, stay_id, legacy_guest_id, customer_key, bill_date, bill_utc, room_no, nights,
                            room_charge_paise, extra_paise, fb_paise, laundry_paise, other_paise, discount_paise, room_gst_bps, room_gst_paise, fb_gst_paise, laundry_gst_paise,
                            other_gst_paise, advance_paise, net_paise, balance_paise, refund_paise, data_flag, paid_flag, b2b, out_of_fy)
            SELECT {p}, '{fy}', {billNo}, {booking}, stay_id, 8001, customer_key, departure_date, departure_utc, '101', nights, {net}, 0, 0, 0, 0, 0, 1200, 0, 0, 0, 0, 0, {net}, 0, 0, 'C', 'Y', 0, 0
              FROM lgc.stay WHERE property_id = {p} AND legacy_fy = '{fy}' AND legacy_booking_id = {booking};
            """, Ct);
    }

    [Fact]
    public async Task Profile_and_list_include_legacy_history_with_live_money_masking()
    {
        var p = await _w.Queries.ProfileAsync(_w.PropertyId, _w.Users["owner"], CrmWorld.Asha, Ct);
        Assert.Equal(3, p.Stays.Count);                                     // live only
        Assert.Equal(["2025-26", "2024-25"], p.LegacyStays!.Select(s => s.LegacyFy));   // newest first, Duplicate hidden
        Assert.Equal((300, 210_000L, "2025-12-21", 1, "101", "CheckedOut"), (p.LegacyStays![0].LegacyBillNo!.Value, p.LegacyStays[0].NetPaise!.Value, p.LegacyStays[0].Departure!, p.LegacyStays[0].Nights, p.LegacyStays[0].Room, p.LegacyStays[0].Status));
        Assert.Equal(6 + 4, p.Nights);
        Assert.Equal(1_732_500 + 1_155_000, p.LifetimePaise);
        Assert.Equal(1_155_000, p.LegacyLifetimePaise);
        Assert.Equal("2024-08-01", p.FirstStay);
        Assert.Equal("2026-10-23", p.LastStay);                             // last stay stays the live one
        Assert.Equal(416_667, p.AvgRatePaise);                              // live only

        var ashaToken = await _w.Queries.TokenAsync(_w.PropertyId, CrmWorld.Asha, Ct);
        var row = (await _w.Queries.ListAsync(_w.PropertyId, _w.Users["owner"], null, null, 1, 50, Ct)).Customers.Single(c => c.Key == ashaToken);
        Assert.Equal((3, 2, 10, 2_887_500L, "2024-08-01"), (row.Stays, row.LegacyStays, row.Nights, row.LifetimePaise!.Value, row.FirstStay!));

        // Reception and the Cashier see the legacy rows without money, like live stays.
        foreach (var user in new[] { "reception", "cashier" })
        {
            var masked = await _w.Queries.ProfileAsync(_w.PropertyId, _w.Users[user], ashaToken, Ct); // AF-CRM: a masked role sends the ck_ token, not the mobile key
            Assert.Equal(2, masked.LegacyStays!.Count);
            Assert.All(masked.LegacyStays, s => Assert.Null(s.NetPaise));
            Assert.Null(masked.LegacyLifetimePaise);
            Assert.Null(masked.LifetimePaise);
            Assert.Equal(10, masked.Nights);
        }

        // A customer without legacy history is unchanged.
        var vik = await _w.Queries.ProfileAsync(_w.PropertyId, _w.Users["owner"], CrmWorld.VikramName, Ct);
        Assert.Empty(vik.LegacyStays!);
        Assert.Equal(0, vik.LegacyLifetimePaise);
        Assert.Equal(315_000, vik.LifetimePaise);
    }

    [Fact]
    public async Task Legacy_rows_on_a_merged_away_key_follow_the_merge_target()
    {
        await StayAsync("2023-24", 7, "n:asha r legacy", "2023-05-01", "2023-05-03", 2, "CheckedOut", 12, 100_000);
        await _w.ExecAsync($"""
            INSERT crm.customer_merge(property_id, source_key, target_key, source_name, target_name, reason, status, proposed_by, proposed_utc, decided_by, decided_utc)
            VALUES ({_w.PropertyId}, 'n:asha r legacy', '{CrmWorld.Asha}', N'Asha R', N'Asha Rao', N'Old program name', 'Approved', {_w.Users["reception"]}, '2026-10-20', {_w.Users["manager"]}, '2026-10-20');
            INSERT crm.customer_alias(property_id, alias_key, target_key, merge_id, created_utc) SELECT {_w.PropertyId}, 'n:asha r legacy', '{CrmWorld.Asha}', MAX(merge_id), '2026-10-20' FROM crm.customer_merge;
            """, Ct);
        var p = await _w.Queries.ProfileAsync(_w.PropertyId, _w.Users["owner"], CrmWorld.Asha, Ct);
        Assert.Equal(3, p.LegacyStays!.Count);
        Assert.Equal("2023-05-01", p.FirstStay);
        var ashaToken = await _w.Queries.TokenAsync(_w.PropertyId, CrmWorld.Asha, Ct);
        var row = (await _w.Queries.ListAsync(_w.PropertyId, _w.Users["owner"], null, null, 1, 50, Ct)).Customers.Single(c => c.Key == ashaToken);
        Assert.Equal((3, 12), (row.LegacyStays, row.Nights));
    }
}
