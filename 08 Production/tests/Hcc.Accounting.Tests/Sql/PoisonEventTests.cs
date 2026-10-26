using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Billing;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Outbox;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// Fix wave A (audit A03b-04 / A08-02): an accounting event whose payload cannot be read no longer stops every later voucher. It is
/// retried, parked after <see cref="OutboxConsumer.ParkAfterFailures"/> failures, and — because a parked event is still not booked —
/// it keeps blocking the period lock exactly as an unprocessed event does. Own database (a parked event blocks the whole property).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PoisonEventTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D = AccountingDb.Day;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private Task<long> SourcesAsync(long chargeId) => db.LongAsync("SELECT COUNT(*) FROM acc.source WHERE source_key = @k;",
        ("@k", SqlDbType.VarChar, Hcc.Application.Accounting.AccountingEventMapper.ChargeKey(chargeId)));

    [Fact]
    public async Task Parked_poison_event_lets_later_vouchers_through_and_still_blocks_locking_the_period()
    {
        var f = await db.NewFolioAsync();
        var first = (await db.Billing.PostChargeAsync(db.As("OWN"), Key(), new PostChargeRequest(f, "Minibar", 30_000), Ct)).Value;
        await db.PublishAsync(AccountingEvents.ChargePosted, f, new { folioId = "not-a-number" }); // unreadable payload
        var poison = await db.LongAsync("SELECT MAX(outbox_id) FROM plat.outbox WHERE event_type = 'ChargePosted';");
        var later = (await db.Billing.PostChargeAsync(db.As("OWN"), Key(), new PostChargeRequest(f, "Laundry", 50_000), Ct)).Value;

        // Before the fix the poison event aborted every poll before the later charge; now the later charge is booked on the first poll.
        await db.Projector.RunOnceAsync(ct: Ct);
        Assert.Equal(1, await SourcesAsync(first.ChargeId));
        Assert.Equal(1, await SourcesAsync(later.ChargeId));
        Assert.Equal(1, await db.LongAsync("SELECT attempts FROM plat.consumer_failure WHERE consumer = @c AND outbox_id = @o;",
            ("@c", SqlDbType.VarChar, AccountingVoucherProjector.ConsumerName), ("@o", SqlDbType.BigInt, poison)));
        Assert.StartsWith("JsonException", (string)(await db.ScalarAsync("SELECT last_error FROM plat.outbox WHERE outbox_id = @o;", ("@o", SqlDbType.BigInt, poison)))!,
            StringComparison.Ordinal);

        for (var i = 2; i <= OutboxConsumer.ParkAfterFailures + 1; i++) await db.Projector.RunOnceAsync(ct: Ct);
        var parked = Assert.Single(await db.Platform.Outbox.ParkedAsync(AccountingVoucherProjector.ConsumerName, Ct));
        Assert.Equal((poison, AccountingEvents.ChargePosted, db.PropertyId, OutboxConsumer.ParkAfterFailures), (parked.OutboxId, parked.EventType, parked.PropertyId, parked.Attempts));

        // Parked = still not booked: the closed day reports it as pending and cannot be locked.
        await db.SetBusinessDateAsync(D.AddDays(1));
        var day = await db.Queries.DayAsync(db.As("ACC"), D, Ct);
        Assert.Contains(day.Exceptions, x => x.Kind == "pending" && x.Severity == "error");
        var refused = await Fails(HccErrorCode.VALIDATION, () => db.Accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct));
        Assert.Contains("still being processed", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM acc.period_lock WHERE business_date = @d;", ("@d", SqlDbType.Date, D.ToDateTime(TimeOnly.MinValue))));

        // Once the event is dealt with (here: marked processed by hand), the same day locks: the parked event was the only blocker.
        await db.ScalarAsync("INSERT plat.consumer_dedup(consumer, outbox_id) VALUES (@c, @o);",
            ("@c", SqlDbType.VarChar, AccountingVoucherProjector.ConsumerName), ("@o", SqlDbType.BigInt, poison));
        Assert.Empty(await db.Platform.Outbox.ParkedAsync(AccountingVoucherProjector.ConsumerName, Ct));
        Assert.Equal(2, (await db.Accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct)).Value.VoucherCount);
    }
}
