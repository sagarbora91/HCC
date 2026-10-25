using System.Text.Json;
using Hcc.Application.Payments;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Billing.Tests.Golden;

/// <summary>Minimal reader for the P0 golden export (08 Production/tools/golden/export-golden.mjs).</summary>
public sealed record Golden(GoldenBase Base, GoldenJourney Journey)
{
    public static Golden Load() => JsonSerializer.Deserialize<Golden>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "base-fixture.golden.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

public sealed record GReceipt(string? BookingId, string Date, string Method, string Kind, long AmountPaise);

public sealed record GOutlet(string Date, string Method, long AmountPaise);

public sealed record GExpense(string Id, string Date, string Status, string Method, long AmountPaise);

public sealed record GBaseExpected(long DrawerPaise, long CollectionsPaise, long OutletCashPaise);

public sealed record GoldenBase(string BusinessDate, long OpeningCashPaise, IReadOnlyList<GReceipt> FolioReceipts, IReadOnlyList<GOutlet> OutletReceipts,
    IReadOnlyList<GExpense> Expenses, GBaseExpected Expected);

public sealed record GLine(string Kind, string Label, string Date, long AmountPaise, int TaxBps);

public sealed record GJourneyExpected(long BalanceBeforeSettlementPaise, long NetPaise, long TaxPaise, long TotalPaise, long PaidPaise, long DepositPaise, long BalancePaise);

public sealed record GoldenJourney(string Room, IReadOnlyList<GLine> Lines, IReadOnlyList<GReceipt> Receipts, GJourneyExpected Expected);

/// <summary>
/// Golden figures reproduced end-to-end through the P4 commands on SQL Server (not just the kernel): the prototype BASE drawer
/// ₹10,750 and collections ₹21,500, and the Room 105 journey invoice ₹4,400 incl. ₹400 tax with the ₹500 deposit kept separate.
/// Constants are the prototype tests' own assertions (03 Prototype/tests/run.mjs, stream-a2-till.mjs).
/// </summary>
[Trait("Category", "Sql")]
public class GoldenBillingTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static readonly Golden G = Golden.Load();

    [Fact]
    public async Task BASE_drawer_is_10750_and_collections_21500_through_commands()
    {
        Assert.Equal(1_075_000, G.Base.Expected.DrawerPaise);
        Assert.Equal(2_150_000, G.Base.Expected.CollectionsPaise);
        var date = DateOnly.Parse(G.Base.BusinessDate, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(BillingDb.Day, date);

        var (till, shift) = await db.OpenTillAsync(G.Base.OpeningCashPaise, "BASE");
        var folios = new Dictionary<string, long>();
        foreach (var r in G.Base.FolioReceipts)
        {
            if (!folios.TryGetValue(r.BookingId!, out var folio)) folios[r.BookingId!] = folio = (await db.NewFolioAsync()).FolioId;
            await db.PayAsync(folio, r.AmountPaise, r.Method, r.Kind, r.Method == "Cash" ? till : null);
        }

        // Shared till (DEC-052): restaurant cash enters the front-desk drawer once; UPI outlet sales and the pending UPI expense do not.
        await db.Runner.RunAsync(new(db.PropertyId, Who.Cashier, "GoldenOutletAndExpenses", BillingDb.Key()), new { fixture = "BASE" }, async (tx, ct) =>
        {
            var n = 0;
            foreach (var o in G.Base.OutletReceipts.Where(o => o.Method == "Cash"))
                await db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.PosSale, "pos-receipt", ++n, new Paise(o.AmountPaise), "Restaurant · BASE", "POS", ct);
            foreach (var e in G.Base.Expenses.Where(e => e.Method == "Cash" && e.Status is "Posted" or "Reversed"))
                await db.Cashier.RecordMovementAsync(tx, till, CashMovementKind.Expense, "expense", ++n, new Paise(-e.AmountPaise), "Cash expense · " + e.Id, e.Id, ct);
            await tx.AuditAsync("TEST_FIXTURE", "shift", "BASE outlet cash + posted cash expense", ct);
            return new { movements = n };
        }, TestContext.Current.CancellationToken);

        var view = await db.Queries.GetShiftAsync(db.As(Who.Cashier), shift.ShiftId, TestContext.Current.CancellationToken);
        Assert.Equal(1_075_000, view.Expected.ExpectedPaise);
        Assert.Equal("₹10,750", new Paise(view.Expected.ExpectedPaise).ToString());
        Assert.Equal(G.Base.Expected.OutletCashPaise, view.Expected.RestaurantPaise); // ₹550
        Assert.Equal(700_000, view.Expected.PaymentsPaise);
        Assert.Equal(180_000, view.Expected.ExpensesPaise);
        Assert.True(view.Expected.Consistent);

        // Same figure from the P0 kernel over the raw golden records (the module must keep the P0 numbers).
        var kernel = CashFigures.ExpectedDrawer(new Paise(G.Base.OpeningCashPaise), date,
            G.Base.FolioReceipts.Select(r => new DatedReceipt(date, r.Method, FolioTotals.ParseKind(r.Kind), new Paise(r.AmountPaise))),
            G.Base.OutletReceipts.Select(o => new OutletReceipt(date, o.Method, new Paise(o.AmountPaise))),
            G.Base.Expenses.Select(e => new CashExpense(date, Enum.Parse<ExpenseStatus>(e.Status), e.Method, new Paise(e.AmountPaise))));
        Assert.Equal(kernel.Value, view.Expected.ExpectedPaise);

        var collections = await db.Queries.GetCollectionsAsync(db.As(Who.Owner), date, TestContext.Current.CancellationToken);
        Assert.Equal(2_150_000, collections.CollectionsPaise);
        Assert.Equal("₹21,500", new Paise(collections.CollectionsPaise).ToString());
        Assert.Equal(700_000, collections.ByTenderPaise["Cash"]);
        Assert.Equal(1_450_000, collections.ByTenderPaise["UPI"]);

        // The count reconciles exactly and closes the shift; the next shift opens with that cash as its float (DEC-035).
        var closed = await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 1_075_000, new Dictionary<string, int> { ["500"] = 21, ["200"] = 1, ["50"] = 1 }, 0, null, view.Shift.Version), TestContext.Current.CancellationToken);
        Assert.Equal("Closed", closed.Value.Status);
        var next = await db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till, null), TestContext.Current.CancellationToken);
        Assert.Equal(1_075_000, next.Value.OpeningFloatPaise);
    }
}

/// <summary>Room 105 worked journey (Financial Rulebook §3) through the P4 commands; own database so business-date moves stay isolated.</summary>
[Trait("Category", "Sql")]
public class GoldenJourneyTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static readonly Golden G = Golden.Load();

    [Fact]
    public async Task Room_105_journey_invoice_is_4400_with_400_tax_and_the_500_deposit_kept_separate()
    {
        var j = G.Journey;
        Assert.Equal("105", j.Room);
        var day = BillingDb.Day;
        await db.SetBusinessDateAsync(day);
        var (till, _) = await db.OpenTillAsync(0, "J105");
        var (rid, folio) = await db.NewFolioAsync("InHouse", day, day.AddDays(2));
        var room = j.Lines.Where(l => l.Kind == "room").ToList();
        db.Stays[rid] = (folio, room[0].AmountPaise, room[0].TaxBps);
        var ct = TestContext.Current.CancellationToken;
        var receipts = j.Receipts;

        // Financial Rulebook §3 order. 24 Oct: advance + refundable deposit (cash), night 1.
        await db.PayAsync(folio, receipts[0].AmountPaise, receipts[0].Method, receipts[0].Kind, till);
        await db.PayAsync(folio, receipts[1].AmountPaise, receipts[1].Method, receipts[1].Kind, till);
        var n1 = await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, day), ct);
        Assert.Equal((200_000L, 20_000L, "Room night · 2026-10-24"), (n1.Value.AmountPaise, n1.Value.TaxPaise, n1.Value.Label));

        // Laundry + discount above the reception limit (₹250) → approval by the manager (approver ≠ requester, DEC-041).
        var service = j.Lines.Single(l => l.Kind == "service");
        await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(folio, service.Label, service.AmountPaise), ct);
        var discount = j.Lines.Single(l => l.Kind == "discount");
        var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, -discount.AmountPaise, "Service recovery", await db.FolioVersionAsync(folio)), ct);
        Assert.Equal("PendingApproval", req.Value.Outcome);
        var approved = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(req.Value.ApprovalId!.Value, true, null, req.Value.ApprovalVersion!), ct);
        Assert.Equal("Executed", approved.Value.Status);

        // 25 Oct: night 2. Then the balance before settlement is ₹3,400 — the ₹500 deposit does not reduce it.
        await db.SetBusinessDateAsync(day.AddDays(1));
        await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(), await CountExactAsync(till, ct), ct);
        await db.Cashier.OpenShiftAsync(db.As(Who.Cashier), BillingDb.Key(), new OpenShiftRequest(till, null), ct);
        await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, day.AddDays(1)), ct);
        var mid = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, ct);
        Assert.Equal(j.Expected.BalanceBeforeSettlementPaise, mid.Totals.BalancePaise);
        Assert.Equal(340_000, mid.Totals.BalancePaise);
        Assert.Equal(50_000, mid.Totals.DepositPaise);

        // Settlement and deposit return.
        await db.PayAsync(folio, receipts[2].AmountPaise, receipts[2].Method, receipts[2].Kind, receipts[2].Method == "Cash" ? till : null);
        await db.PayAsync(folio, receipts[3].AmountPaise, receipts[3].Method, receipts[3].Kind, receipts[3].Method == "Cash" ? till : null);

        var f = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, ct);
        var e = j.Expected;
        Assert.Equal((e.NetPaise, e.TaxPaise, e.TotalPaise, e.PaidPaise, e.DepositPaise, e.BalancePaise),
            (f.Totals.NetPaise, f.Totals.TaxPaise, f.Totals.TotalPaise, f.Totals.PaidPaise, f.Totals.DepositPaise, f.Totals.BalancePaise));
        Assert.Equal(440_000, f.Totals.TotalPaise);
        Assert.Equal(40_000, f.Totals.TaxPaise);
        Assert.Equal("₹4,400", new Paise(f.Totals.TotalPaise).ToString());

        // Same lines as the prototype (amount, tax rate, kind, label); dates follow the nightly posting (prototype M1 pre-posted both nights on day 1).
        static string Sig(string kind, string label, long amount, int bps) => $"{kind}|{label}|{amount}|{bps}";
        Assert.Equal(j.Lines.Select(l => Sig(l.Kind, l.Label, l.AmountPaise, l.TaxBps)).Order(), f.Lines.Select(l => Sig(l.Kind, l.Label, l.AmountPaise, l.TaxBps)).Order());
        Assert.Equal(j.Receipts.Select(r => $"{r.Kind}|{r.Method}|{r.AmountPaise}").Order(), f.Receipts.Select(r => $"{r.Kind}|{r.Tender}|{r.AmountPaise}").Order());
    }

    private async Task<SubmitShiftCountRequest> CountExactAsync(int till, CancellationToken ct)
    {
        var id = await db.LongAsync("SELECT shift_id FROM pay.shift WHERE till_id = @t AND status = 'Open';", ("@t", System.Data.SqlDbType.Int, till));
        var v = await db.Queries.GetShiftAsync(db.As(Who.Cashier), id, ct);
        return new SubmitShiftCountRequest(id, v.Expected.ExpectedPaise, null, null, null, v.Shift.Version);
    }
}
