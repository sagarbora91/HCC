using System.Data;
using System.Text.RegularExpressions;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// A03b-05 on SQL: every voucher of the Tally XML carries REMOTEID = GUID = HCC-&lt;property identity&gt;-&lt;source key&gt; (migration 3950),
/// the same in every export of that voucher (live or locked snapshot) and unique per voucher; an export made before the ids is named in
/// a LEGACY note. Own database. September dates are closed (business date 24 Oct).
/// </summary>
[Trait("Category", "Sql")]
public sealed partial class TallyVoucherIdSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D1 = new(2026, 9, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<VOUCHER REMOTEID=\"([^\"]+)\"[^>]*>\n    <DATE>\\d{8}</DATE><GUID>([^<]+)</GUID>")]
    private static partial Regex VoucherIds();

    private static List<string> Ids(TallyExportView v)
    {
        var m = VoucherIds().Matches(v.Xml);
        Assert.All(m, x => Assert.Equal(x.Groups[1].Value, x.Groups[2].Value)); // REMOTEID = GUID
        return m.Select(x => x.Groups[1].Value).ToList();
    }

    private AccountingQueries Recording() => new(db.Store, new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]),
        null, db.Platform.Commands, db.Clock);

    private Task ExpenseAsync(int id, DateOnly date, long amount) =>
        db.PublishAsync(AccountingEvents.ExpensePosted, id, new ExpensePostedEvent(id, date, "Utilities", "Cash", amount, "Demo Power (fictional)", null));

    [Fact]
    public async Task REMOTEIDs_are_stable_across_re_exports_live_and_locked_and_unique_per_voucher()
    {
        foreach (var (id, amount) in new[] { (501, 75_000L), (502, 12_000L), (503, 3_000L) }) await ExpenseAsync(id, D1, amount);
        await db.ProjectAsync();
        var q = Recording();

        var first = await q.TallyAsync(db.As("ACC"), D1, D1, Ct);
        var ids = Ids(first);
        var identity = await db.ScalarAsync("SELECT LOWER(REPLACE(CONVERT(CHAR(36), tally_guid), '-', '')) FROM acc.tally_identity WHERE property_id = @p;",
            ("@p", SqlDbType.Int, db.PropertyId)) as string;
        Assert.Matches("^[0-9a-f]{32}$", identity!);
        Assert.Equal(new[] { "expense:501", "expense:502", "expense:503" }.Select(k => $"HCC-{identity}-{k}"), ids);
        Assert.Equal(3, first.VoucherCount);
        Assert.Contains(VoucherExport.TallyIdNote, first.Xml, StringComparison.Ordinal);
        Assert.Equal(0, first.PendingEvents);
        Assert.Empty(first.ProvisionalDates!);

        // The same export again (recorded or not): the same ids, still one identity for the property.
        Assert.Equal(ids, Ids(await q.TallyAsync(db.As("OWN"), D1, D1, Ct)));
        Assert.Equal(ids, Ids(await db.Queries.TallyAsync(db.As("ACC"), D1, D1, Ct)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.tally_identity;"));

        // A later voucher on the same day keeps the earlier ids; locking the day keeps them in the snapshot.
        await ExpenseAsync(504, D1, 1_500);
        await db.ProjectAsync();
        var more = Ids(await q.TallyAsync(db.As("ACC"), D1, D1, Ct));
        Assert.Equal(ids, more.Take(3));
        Assert.Equal($"HCC-{identity}-expense:504", more[3]);
        Assert.Equal(4, more.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Locked", (await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(D1), Ct)).Value.Status);
        Assert.Equal(more, Ids(await q.TallyAsync(db.As("ACC"), D1, D1, Ct)));

        // Every recorded export is marked as carrying ids; the identity never changes.
        Assert.Equal((0L, 4L), (await db.LongAsync("SELECT COUNT(*) FROM acc.tally_export WHERE from_date = '2026-09-10' AND remote_ids = 0;"),
            await db.LongAsync("SELECT COUNT(*) FROM acc.tally_export WHERE from_date = '2026-09-10' AND remote_ids = 1;")));
        Assert.Equal(51108, await SqlErrorAsync("UPDATE acc.tally_identity SET tally_guid = NEWID();"));
        Assert.Equal(51108, await SqlErrorAsync("DELETE acc.tally_identity;"));
    }

    [Fact]
    public async Task A_day_an_export_took_to_Tally_before_the_ids_gets_a_LEGACY_note_until_it_is_no_longer_exported()
    {
        var d = new DateOnly(2026, 9, 20);
        await ExpenseAsync(601, d, 40_000);
        await db.ProjectAsync();
        // An export recorded before migration 3950 (remote_ids = 0 is the column default for those rows).
        await db.ScalarAsync("""
            INSERT acc.tally_export(property_id, from_date, to_date, voucher_count, debit_paise, credit_paise, exported_by, exported_utc)
            VALUES (@p, @d, @d, 1, 40000, 40000, @u, '2026-09-21');
            INSERT acc.tally_export_day(export_id, property_id, business_date, voucher_count, reexport, provisional) VALUES (SCOPE_IDENTITY(), @p, @d, 1, 0, 0);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@d", SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue)), ("@u", SqlDbType.Int, db.Users["ACC"]));

        var legacy = await Recording().TallyAsync(db.As("ACC"), d, d, Ct);
        Assert.Equal([d], legacy.LegacyDates!);
        Assert.Contains(VoucherExport.TallyNote(VoucherExport.LegacyNote(d)), legacy.Xml, StringComparison.Ordinal);
        Assert.Single(Ids(legacy));
        var other = await Recording().TallyAsync(db.As("ACC"), d.AddDays(1), d.AddDays(1), Ct);
        Assert.Empty(other.LegacyDates!);
        Assert.DoesNotContain("LEGACY", other.Xml, StringComparison.Ordinal);
    }

    private async Task<int> SqlErrorAsync(string sql)
    {
        try
        {
            await db.ScalarAsync(sql);
            return 0;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }
}

/// <summary>
/// A03b-05: while accounting events are pending (not yet processed) or parked (after errors), every unlocked date of a Tally export is
/// PROVISIONAL with the count in the XML and in the response, and is flagged so that the first export after they are processed carries
/// it again; a locked date is its snapshot and never provisional. Own database (a parked event affects the whole property).
/// </summary>
[Trait("Category", "Sql")]
public sealed class TallyProvisionalSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D2 = new(2026, 9, 12);

    private static readonly DateOnly D3 = new(2026, 9, 13);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task ExpenseAsync(int id, DateOnly date, long amount) =>
        db.PublishAsync(AccountingEvents.ExpensePosted, id, new ExpensePostedEvent(id, date, "Utilities", "Cash", amount, "Demo Power (fictional)", null));

    private Task<long> OpenFlagsAsync(DateOnly d) => db.LongAsync("SELECT COUNT(*) FROM acc.tally_reexport WHERE business_date = @d AND cleared_export_id IS NULL AND reopen_id IS NULL;",
        ("@d", SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue)));

    [Fact]
    public async Task Pending_and_parked_events_make_unlocked_dates_provisional_flag_them_and_the_first_export_after_they_are_processed_carries_them_again()
    {
        var q = new AccountingQueries(db.Store, new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]), null,
            db.Platform.Commands, db.Clock);
        await ExpenseAsync(701, D2, 10_000);
        await ExpenseAsync(702, D3, 20_000);
        await db.ProjectAsync();
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(D3), Ct);

        // One poison event parked after its failures, then one event not yet processed: 2 pending for the property.
        await db.PublishAsync(AccountingEvents.ChargePosted, 9, new { folioId = "not-a-number" });
        var poison = await db.LongAsync("SELECT MAX(outbox_id) FROM plat.outbox WHERE event_type = 'ChargePosted';");
        for (var i = 0; i <= OutboxConsumer.ParkAfterFailures; i++) await db.Projector.RunOnceAsync(ct: Ct);
        Assert.Single(await db.Platform.Outbox.ParkedAsync(AccountingVoucherProjector.ConsumerName, Ct));
        await ExpenseAsync(703, D2, 5_000);
        Assert.Equal(2, await db.Store.PendingEventsAsync(null, db.PropertyId, Ct));

        var during = await q.TallyAsync(db.As("ACC"), D2, D3, Ct);
        Assert.Equal(2, during.PendingEvents);
        Assert.Equal([D2], during.ProvisionalDates!);   // D3 is locked: its snapshot, not provisional
        Assert.Contains(VoucherExport.TallyNote(VoucherExport.PendingNote(D2, 2)), during.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("PROVISIONAL · 2026-09-13", during.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("expense:703", during.Xml, StringComparison.Ordinal);
        Assert.Equal(1, await OpenFlagsAsync(D2));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.tally_export_day WHERE business_date = '2026-09-12' AND provisional = 1;"));
        Assert.Contains("2 accounting event(s) pending", (string)(await db.ScalarAsync("SELECT TOP (1) detail FROM plat.audit_event WHERE action = 'TALLY_EXPORTED' ORDER BY audit_id DESC;"))!,
            StringComparison.Ordinal);

        // The unprocessed one is booked, the parked one still pends: another range carries D2 again, still provisional; the flag stays.
        await db.ProjectAsync();
        var still = await q.TallyAsync(db.As("ACC"), D3, D3, Ct);
        Assert.Equal([D2], still.ReexportDates!);
        Assert.Equal([D2], still.ProvisionalDates!);
        Assert.Equal(1, still.PendingEvents);
        Assert.Contains("RE-EXPORT · 2026-09-12 was exported while it was provisional (2 accounting event(s) were still pending)", still.Xml, StringComparison.Ordinal);
        Assert.Contains("expense:703", still.Xml, StringComparison.Ordinal);
        Assert.Equal(1, await OpenFlagsAsync(D2));

        // The parked event is dealt with (marked processed): the next export carries D2 once more, complete, and clears the flag.
        await db.ScalarAsync("INSERT plat.consumer_dedup(consumer, outbox_id) VALUES (@c, @o);",
            ("@c", SqlDbType.VarChar, AccountingVoucherProjector.ConsumerName), ("@o", SqlDbType.BigInt, poison));
        var after = await q.TallyAsync(db.As("ACC"), D3, D3, Ct);
        Assert.Equal([D2], after.ReexportDates!);
        Assert.Empty(after.ProvisionalDates!);
        Assert.Equal(0, after.PendingEvents);
        Assert.DoesNotContain("PROVISIONAL", after.Xml, StringComparison.Ordinal);
        Assert.Equal(0, await OpenFlagsAsync(D2));
        var next = await q.TallyAsync(db.As("ACC"), D3, D3, Ct);
        Assert.Empty(next.ReexportDates!);
        Assert.DoesNotContain("<DATE>20260912</DATE>", next.Xml, StringComparison.Ordinal);
    }
}
