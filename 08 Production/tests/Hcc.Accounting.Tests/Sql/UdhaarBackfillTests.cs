using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-2 parity (contract §8; ACC1-DONE §7 item 6): BackfillUdhaarCredits (Owner) publishes UdhaarCreditRaised for the Udhaar credits raised
/// before migration 4215 (no event, no sale voucher) — dated the credit's own date, kind absent → other revenue, no GST — and is idempotent.
/// </summary>
[Trait("Category", "Sql")]
public sealed class UdhaarBackfillTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly Old = new(2026, 9, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UdhaarCreditBackfill Backfill => new(db.Platform.Commands, new SqlUdhaarBackfillStore(),
        new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]));

    private Task<long> InsertCreditAsync(string name, string? mobile, long amount, string? kind = null, int gstBps = 0)
        => db.LongAsync("""
            INSERT ops.udhaar_receivable(property_id, customer_name, mobile, amount_paise, credit_date, due_date, created_by, created_utc, kind, gst_bps)
            OUTPUT INSERTED.receivable_id VALUES (@p, @n, @m, @a, @d, @d, @u, '2026-09-02', @k, @g);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name), ("@m", SqlDbType.Char, (object?)mobile ?? DBNull.Value), ("@a", SqlDbType.BigInt, amount),
            ("@d", SqlDbType.Date, Old.ToDateTime(TimeOnly.MinValue)), ("@u", SqlDbType.Int, db.Users["OWN"]), ("@k", SqlDbType.VarChar, (object?)kind ?? DBNull.Value), ("@g", SqlDbType.Int, gstBps));

    [Fact]
    public async Task Owner_back_fills_the_credits_without_a_sale_once_dated_their_own_day_as_other_revenue()
    {
        var before4215 = await InsertCreditAsync("Ramesh Patil (fictional)", "9876543210", 250_000);
        var raised = await InsertCreditAsync("Sunita Rao (fictional)", "9123456780", 105_000, "restaurant", 500);
        await db.PublishAsync(AccountingEvents.UdhaarCreditRaised, raised, new UdhaarCreditRaisedEvent(raised, Old, "Sunita Rao (fictional)", "9123456780", 105_000, "restaurant", 500));

        await Fails(HccErrorCode.FORBIDDEN, () => Backfill.RunAsync(db.As("ACC"), AccountingDb.Key(), new BackfillUdhaarCreditsRequest(), Ct));
        var r = (await Backfill.RunAsync(db.As("OWN"), AccountingDb.Key(), new BackfillUdhaarCreditsRequest(), Ct)).Value;
        Assert.Equal((1, 250_000L, (IReadOnlyList<long>)[before4215]), (r.Published, r.AmountPaise, r.ReceivableIds), new BackfillComparer());
        Assert.Contains("other revenue, no GST", r.Message, StringComparison.Ordinal);
        await db.ProjectAsync();

        var day = await db.Queries.DayAsync(db.As("OWN"), Old, Ct);
        var sale = day.Vouchers.Single(v => v.SourceKey == "udhaar:" + before4215);
        Assert.Equal(("Sales", "udhaar-credit"), (sale.Type, sale.Sub));
        Assert.Equal([("1120/UD-9876543210", 250_000L, 0L), ("4100", 0L, 250_000L)], sale.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Single(day.Vouchers, v => v.SourceKey == "udhaar:" + raised);   // booked once, by its own raise

        // idempotent: the event is in the outbox (and the sale booked) → nothing more
        var again = (await Backfill.RunAsync(db.As("OWN"), AccountingDb.Key(), new BackfillUdhaarCreditsRequest(), Ct)).Value;
        Assert.Equal(0, again.Published);
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'UdhaarCreditRaised' AND property_id = @p;", ("@p", SqlDbType.Int, db.PropertyId)));
        // after the outbox is purged the booked sale alone keeps it idempotent
        await db.ScalarAsync("UPDATE plat.outbox SET event_type = 'PurgedForTest' WHERE event_type = 'UdhaarCreditRaised' AND property_id = @p;", ("@p", SqlDbType.Int, db.PropertyId));
        Assert.Equal(0, (await Backfill.RunAsync(db.As("OWN"), AccountingDb.Key(), new BackfillUdhaarCreditsRequest(), Ct)).Value.Published);
    }

    private sealed class BackfillComparer : IEqualityComparer<(int, long, IReadOnlyList<long>)>
    {
        public bool Equals((int, long, IReadOnlyList<long>) x, (int, long, IReadOnlyList<long>) y) => x.Item1 == y.Item1 && x.Item2 == y.Item2 && x.Item3.SequenceEqual(y.Item3);

        public int GetHashCode((int, long, IReadOnlyList<long>) obj) => HashCode.Combine(obj.Item1, obj.Item2);
    }
}
