using System.Text.Json;
using Hcc.Accounting.Tests.Golden;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Expenses;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 (lane acc1-tests) + ACC-2 (lane acc2-tests): the rulebook golden month through the real outbox → accounting consumer → vouchers → cumulative trial balance
/// (PERM-ACC-001), on a disposable database with the real P1 policy. The events and the expectation are those of <c>Golden/rulebook-month.json</c> (rulebook §8, April 2026,
/// fictional): the month's flow is checked first (<c>expected.flow</c>), then the §8.1 opening goes through the opening service and the cumulative trial balance equals
/// <c>expected.closing</c> (opening + the 19 event-driven steps; the HDFC loan is the party <c>2400/LN-HDFC</c>). The 7 steps that waited for an ACC-2 lane (T11 T14a T14b
/// T16 T17 T18 T19) and the whole month (₹84,27,500, <c>expected.full</c>) are <see cref="RulebookFullMonthSqlTests"/> on their own database.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RulebookMonthSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static JsonDocument Month => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-month.json")));

    [Fact]
    [Trait("Lane", "acc2")]
    public async Task The_months_events_become_balanced_vouchers_and_the_cumulative_trial_balance_equals_the_rulebook_flow()
    {
        using var month = Month;
        var root = month.RootElement;
        var events = root.GetProperty("events").EnumerateArray().ToList();

        // ── publish every event of the month (dates inside April 2026, closed days of the harness property) and run the consumer once ──
        var id = 0L;
        foreach (var ev in events)
            await db.PublishAsync(ev.GetProperty("type").GetString()!, ++id, ev.GetProperty("payload").Clone());
        Assert.Equal(events.Count, await db.ProjectAsync());   // exactly one source per event
        Assert.Equal(0, await db.ProjectAsync());              // and exactly once

        // ── the Owner's mapping of the month (Owner only, DEC-063 G11): none in ACC-2 (a folio restaurant line is `food` → 4200 by default, the opening advance is a deposit-apply receipt) ──
        foreach (var o in root.GetProperty("mappingOverrides").EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Reverse())
            await db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest(o.Name, o.Value.GetString()!), Ct);

        // ── the day views: every voucher balances, no error on any date that has vouchers, the bank deposit and the cash shortage sit on their rulebook dates ──
        var dates = events.Select(e => DateOnly.Parse(e.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToList();
        foreach (var d in dates)
        {
            var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
            Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
            Assert.Equal(0, day.Errors);
            Assert.Equal("Closed", day.Status);   // April is before the harness business date (24 Oct 2026)
        }

        Assert.Equal("bank-deposit", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 25), Ct)).Vouchers.Single().Sub);
        Assert.Equal("cash-difference", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 26), Ct)).Vouchers.Single().Sub);
        Assert.Equal("company-payment", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 10), Ct)).Vouchers.Single().Sub);

        // ── cumulative trial balance at 30 April = the expectation worked out from the rulebook (control ledgers; parties sum into them) ──
        var tb = await db.Queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(0, tb.Rows.Sum(r => r.BalancePaise));
        var got = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in tb.Rows) got[RulebookMonthTests.Control(r.Account)] = got.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in got.Where(x => x.Value == 0).Select(x => x.Key).ToList()) got.Remove(k);
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("flow").GetProperty("control").EnumerateObject()) want[p.Name] = p.Value.GetInt64();
        Assert.False(got.ContainsKey("9999"), "something posted to 9999 Suspense");
        Assert.Equal(want, got);

        // ── ACC-1 integration (lanes acc1-opening / acc1-statements / acc1-parties): the §8.1 opening through the opening service, the lock
        //    gate of coordinator ruling 5, and the cumulative / six-column trial balance with the opening ──
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var openingStore = new SqlOpeningStore(db.Platform.Connections);
        var opening = new OpeningBalanceService(db.Platform.Commands, openingStore, db.Store, policy, db.Clock);
        var gated = new AccountingService(db.Platform.Commands, db.Store, policy, db.Clock, openingStore);
        var queries = new AccountingQueries(db.Store, policy, null, null, db.Clock, openingStore);
        var reports = new AccountingReportQueries(db.Store, policy, null, new OpeningReportSource(openingStore), new SqlReportPartyNames(db.Platform.Connections));

        // no entry at all: days still lock (DEC-148, ruling 5) and the six-column trial balance says "opening balances pending"
        Assert.Equal("Locked", (await gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 2)), Ct)).Value.Status);
        Assert.True((await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), false, Ct)).OpeningPending);

        var asOf = DateOnly.Parse(root.GetProperty("asOf").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var lines = root.GetProperty("opening").EnumerateArray()
            .Select(x => new OpeningLineRequest(RulebookMonthTests.ServerLedger(x.GetProperty("ledger").GetString()!), x.GetProperty("debitPaise").GetInt64(), x.GetProperty("creditPaise").GetInt64()))
            .ToList();
        var saved = (await opening.SaveAsync(db.As("MGR"), Key(), new SaveOpeningBalancesRequest(asOf, lines, "Rulebook §8.1 (fictional)"), Ct)).Value;
        Assert.Equal(("draft", 0L, 774_000_000L), (saved.Status, saved.DifferencePaise, saved.DebitPaise));

        // a draft gates every lock on or after asOf
        var refused = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 3)), Ct));
        Assert.StartsWith("Opening balances pending", refused.Message, StringComparison.Ordinal);
        await opening.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest(null), Ct);
        Assert.Equal("Locked", (await gated.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(new DateOnly(2026, 4, 3)), Ct)).Value.Status);

        // cumulative trial balance at 30 April = the rulebook closing (opening included), control level
        var cum = await queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(cum.Balanced);
        var closing = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in cum.Rows) closing[RulebookMonthTests.Control(r.Account)] = closing.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in closing.Where(x => x.Value == 0).Select(x => x.Key).ToList()) closing.Remove(k);
        var wantClosing = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("closing").GetProperty("control").EnumerateObject()) wantClosing[p.Name] = p.Value.GetInt64();
        Assert.Equal(wantClosing, closing);

        // the six-column trial balance of April (the opening journal dated 1 April is the opening): balanced in every column pair, no longer pending
        foreach (var detail in new[] { false, true })
        {
            var tb6 = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), detail, Ct);
            Assert.True(tb6.Balanced, "detail " + detail);
            Assert.False(tb6.OpeningPending);
            Assert.Equal((774_000_000L, 774_000_000L), (tb6.Totals.OpeningDebitPaise, tb6.Totals.OpeningCreditPaise));
        }

        // a party row of the opening (2200/EV-MORNING) sits under its control in the detail view
        var detailTb = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), true, Ct);
        Assert.Contains(detailTb.Rows, r => r.Account == "2200/EV-MORNING" && r.Control == "2200");
        // ACC-2: the HDFC term loan is a party under 2400 (the opening line `2400/LN-HDFC`), 19,60,000 Cr after the EMI once T17 exists — 20,00,000 Cr from the opening alone today
        Assert.Contains(detailTb.Rows, r => r.Account == "2400/LN-HDFC" && r.Control == "2400" && r.OpeningCreditPaise == 200_000_000L);
    }
}

/// <summary>
/// ACC-2 integration: the WHOLE rulebook month on its own database — the 19 driven steps and the 7 that waited for a lane (T11 vendor payment,
/// T14a T14b T16 T17 T18 manual vouchers, T19 OTA payout) as the outbox events their real commands publish (VendorPaymentRecorded,
/// ManualVoucherApproved, OtaPayoutRecorded), the §8.1 opening through the opening service, then the cumulative trial balance at 30 April =
/// <c>expected.full</c> (₹84,27,500 both sides), the six-column trial balance, the GST month of §8.6 (challan ₹28,900), the month lock refused
/// until the set-off is posted and then allowed, and a ₹2,00,000 cash receipt refused by the cash guard (B9).
/// </summary>
[Trait("Category", "Sql")]
public sealed class RulebookFullMonthSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static JsonDocument Month => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-month.json")));

    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();

    private static string S(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    /// <summary>A waiting step's payload as its server command publishes it.</summary>
    private static (string Type, object Payload) Published(JsonElement ev)
    {
        var p = ev.GetProperty("payload");
        return S(ev, "type") switch
        {
            "VendorPaymentRecorded" => (Hcc.Contracts.Accounting.PurchaseEvents.VendorPaymentRecorded, new
            {
                paymentId = L(p, "paymentId"), businessDate = S(p, "businessDate"), vendorCode = S(p, "vendorCode"), vendor = S(p, "vendor"), tender = S(p, "tender"),
                amountPaise = L(p, "amountPaise"), allocatedPaise = p.GetProperty("allocations").EnumerateArray().Sum(a => L(a, "amountPaise")), reference = S(p, "reference"),
            }),
            "ManualVoucherApproved" => (AccountingEvents.ManualVoucherApproved, new
            {
                voucherId = L(p, "voucherId"), date = S(p, "businessDate"), type = S(p, "voucherType"), narration = S(p, "narration"), reference = (string?)null, template = S(p, "template"),
                lines = p.GetProperty("lines").EnumerateArray().Select(l => new { account = S(l, "ledger"), debitPaise = L(l, "debitPaise"), creditPaise = L(l, "creditPaise") }).ToList(),
            }),
            "OtaPayoutRecorded" => (AccountingEvents.OtaPayoutRecorded, new
            {
                payoutId = L(p, "payoutId"), channelId = 3, channelCode = RulebookMonthTests.Goibibo, channel = "Goibibo", businessDate = S(p, "businessDate"),
                statementRef = S(p, "statementRef"), grossPaise = L(p, "grossPaise"), commissionPaise = L(p, "commissionPaise"), commissionGstPaise = L(p, "commissionGstPaise"),
                tcsPaise = L(p, "tcsPaise"), netPaise = L(p, "netPaise"), bankAccount = "BA-HDFC", itcAllowed = false, bookings = 1,
            }),
            var t => throw new InvalidOperationException("Unknown waiting event " + t),
        };
    }

    [Fact]
    [Trait("Lane", "acc2")]
    public async Task The_whole_month_through_the_real_commands()
    {
        using var month = Month;
        var root = month.RootElement;
        var events = root.GetProperty("events").EnumerateArray().ToList();
        var waiting = root.GetProperty("waiting").GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(7, waiting.Count);

        // ── every event of the month through the real outbox consumer, exactly once ──
        var id = 0L;
        foreach (var ev in events)
            await db.PublishAsync(S(ev, "type"), ++id, ev.GetProperty("payload").Clone());
        foreach (var ev in waiting)
        {
            var (type, payload) = Published(ev);
            await db.PublishAsync(type, ++id, payload);
        }

        Assert.Equal(events.Count + waiting.Count, await db.ProjectAsync());
        Assert.Equal(0, await db.ProjectAsync());

        // ── every day of the month balances with no error; the waiting steps sit on their rulebook dates with their voucher types ──
        foreach (var d in events.Concat(waiting).Select(e => DateOnly.Parse(S(e, "date"), System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order())
        {
            var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
            Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
            Assert.Equal(0, day.Errors);
        }

        var d27 = await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 27), Ct);
        Assert.Contains(d27.Vouchers, v => v.Number == "CTR/20260427/001");                        // T14a Contra
        Assert.Contains(d27.Vouchers, v => v.Sub == "journal" && v.Number.StartsWith("JRN/", StringComparison.Ordinal)); // T14b Journal (ruling 1)
        Assert.Contains((await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 18), Ct)).Vouchers, v => v.Sub == "vendor-payment");
        Assert.Contains((await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 29), Ct)).Vouchers, v => v.Sub == "ota-payout");

        // ── the §8.1 opening (Manager saves, Owner approves) ──
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var openingStore = new SqlOpeningStore(db.Platform.Connections);
        var opening = new OpeningBalanceService(db.Platform.Commands, openingStore, db.Store, policy, db.Clock);
        var asOf = DateOnly.Parse(S(root, "asOf"), System.Globalization.CultureInfo.InvariantCulture);
        var lines = root.GetProperty("opening").EnumerateArray()
            .Select(x => new OpeningLineRequest(RulebookMonthTests.ServerLedger(S(x, "ledger")), L(x, "debitPaise"), L(x, "creditPaise"))).ToList();
        await opening.SaveAsync(db.As("MGR"), Key(), new SaveOpeningBalancesRequest(asOf, lines, "Rulebook §8.1 (fictional)"), Ct);
        await opening.ApproveAsync(db.As("OWN"), Key(), new ApproveOpeningBalancesRequest(null), Ct);

        // ── cumulative trial balance at 30 April = expected.full (control ledgers): ₹84,27,500 both sides ──
        var queries = new AccountingQueries(db.Store, policy, null, null, db.Clock, openingStore);
        var cum = await queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(cum.Balanced);
        var got = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in cum.Rows) got[RulebookMonthTests.Control(r.Account)] = got.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in got.Where(x => x.Value == 0).Select(x => x.Key).ToList()) got.Remove(k);
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("full").GetProperty("control").EnumerateObject()) want[p.Name] = p.Value.GetInt64();
        Assert.Equal(want, got);
        Assert.Equal(842_750_000, got.Values.Where(v => v > 0).Sum());

        // ── the six-column trial balance of April: balanced, the opening as its opening columns ──
        var reports = new AccountingReportQueries(db.Store, policy, null, new OpeningReportSource(openingStore), new SqlReportPartyNames(db.Platform.Connections));
        foreach (var detail in new[] { false, true })
        {
            var tb6 = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), detail, Ct);
            Assert.True(tb6.Balanced, "detail " + detail);
            Assert.Equal((774_000_000L, 774_000_000L), (tb6.Totals.OpeningDebitPaise, tb6.Totals.OpeningCreditPaise));
        }

        var detailTb = await reports.TrialBalanceAsync(db.As("ACC"), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), true, Ct);
        Assert.Contains(detailTb.Rows, r => r.Account == "2400/LN-HDFC" && r.ClosingCreditPaise - r.ClosingDebitPaise == 196_000_000L);   // after the T17 EMI principal
        Assert.Contains(detailTb.Rows, r => r.Account == "1115/CHN-GOIBIBO" && r.ClosingDebitPaise == r.ClosingCreditPaise);             // T19 settled the T4 credit

        // ── §8.6 GST month: challan ₹28,900; the month lock is refused until the set-off is posted, then every April day locks ──
        var gst = new GstMonthService(db.Platform.Commands, db.Store, new SqlGstMonthStore(db.Platform.Connections), policy, db.Clock, new FakeGstManualVouchers(),
            new ExpenseMonthLockAdapter(new SqlExpenseStore(db.Platform.Connections), new SqlExpenseRegisterStore(db.Platform.Connections)), null, openingStore);
        var april = await gst.MonthAsync(db.As("ACC"), "2026-04", Ct);
        var g86 = root.GetProperty("expected").GetProperty("gst86");
        Assert.Equal((L(g86, "cgst"), L(g86, "sgst"), L(g86, "tcs"), L(g86, "challan")), (april.Output.CgstPaise, april.Output.SgstPaise, april.TcsPaise, april.ChallanPaise));
        var refused = await Assert.ThrowsAsync<HccException>(() => gst.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-04"), Ct));
        Assert.Equal("Post the GST set-off for 2026-04 first (rulebook E1).", refused.Message);
        await gst.SetoffAsync(db.As("ACC"), Key(), new PostGstSetoffRequest("2026-04"), Ct);
        var locked = (await gst.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-04"), Ct)).Value;
        Assert.Equal(("Locked", 30), (locked.Status, locked.DaysLocked));
        // a day inside the locked month cannot be unlocked on its own (the Owner unlocks the month first)
        var accounting = new AccountingService(db.Platform.Commands, db.Store, policy, db.Clock, openingStore, new SqlGstMonthStore(db.Platform.Connections));
        var lockedDay = await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 27), Ct);
        var dayRefused = await Assert.ThrowsAsync<HccException>(() => accounting.UnlockAsync(db.As("OWN"), Key(), new UnlockPeriodRequest(new DateOnly(2026, 4, 27), "Late entry", lockedDay.Lock!.Version), Ct));
        Assert.Contains("Month 2026-04 is locked", dayRefused.Message, StringComparison.Ordinal);

        // ── B9: a ₹2,00,000 cash receipt from one guest is refused (the seed limit) ──
        var cash = CashGuard.Receipt(0, 0, false, 20_000_000, CashGuardLimits.Seed.ReceiptLimitPaise, "Fictional Guest", new DateOnly(2026, 4, 30));
        Assert.True(cash.Refused);
        Assert.Equal(CashGuard.Reason, CashGuard.Refusal(cash).Details!["reason"].GetString());
    }
}
