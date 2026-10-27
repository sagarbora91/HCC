using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Application.Import;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-1 opening balances (contract §5; rulebook §G2 and the §8.1 opening of the worked month, ₹77,40,000 both sides): validation, the
/// 1400 balancing line, the journal, the lock gate, pending, and the approved entry superseding the imported opening. Same rules as the
/// prototype's tests/stream-accounting-opening.mjs.
/// </summary>
public sealed class OpeningBalanceTests
{
    private static readonly DateOnly FyStart = new(2026, 4, 1);

    private static long R(long rupees) => rupees * 100;

    /// <summary>Rulebook §8.1, 1 April 2026 (party ledgers as plain <c>&lt;control&gt;/&lt;party&gt;</c> ids).</summary>
    public static readonly IReadOnlyList<OpeningLineRequest> Rulebook =
    [
        new("1000", R(20_000), 0),
        new("1030/BA-HDFC", R(3_00_000), 0),
        new("1500", R(60_00_000), 0),
        new("1510", R(8_00_000), 0),
        new("1520", R(5_00_000), 0),
        new("1530", R(80_000), 0),
        new("1110/CMP-DECCAN", R(40_000), 0),
        new("2200/EV-MORNING", 0, R(25_000)),
        new("2000", 0, R(10_000)),
        new("2400/LN-HDFC", 0, R(20_00_000)),
        new("3100", 0, R(57_05_000)),
    ];

    public static IReadOnlyList<OpeningLineRequest> WithoutCapital => Rulebook.Where(l => l.Account != "3100").ToList();

    private static OpeningEntry Entry(IReadOnlyList<OpeningLineRequest> lines, string status = OpeningBalanceRules.Draft)
        => new(1, FyStart, status, OpeningBalanceRules.WithBalance(lines), 7, new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc),
            status == OpeningBalanceRules.Approved ? 1 : null, status == OpeningBalanceRules.Approved ? new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc) : null, null, "AAAAAAAAB9E=");

    [Fact]
    public void Rulebook_opening_balances_at_77_40_000_with_no_1400_line()
    {
        Assert.Empty(OpeningBalanceRules.Errors(Rulebook));
        var lines = OpeningBalanceRules.WithBalance(Rulebook);
        Assert.Equal(11, lines.Count);
        Assert.Equal(R(77_40_000), lines.Sum(l => l.Debit));
        Assert.Equal(R(77_40_000), lines.Sum(l => l.Credit));
        Assert.DoesNotContain(lines, l => l.Account == OpeningBalanceRules.DifferenceAccount);
        Assert.Equal(0, OpeningBalanceRules.Difference(Entry(Rulebook)));
    }

    [Fact]
    public void An_unbalanced_draft_parks_the_gap_in_1400_on_the_short_side_and_a_1400_line_sent_is_recomputed()
    {
        var lines = OpeningBalanceRules.WithBalance(WithoutCapital);
        Assert.Equal(new OpeningLine("1400", 0, R(57_05_000)), lines[^1]);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Equal(-R(57_05_000), OpeningBalanceRules.Difference(Entry(WithoutCapital)));

        var resent = OpeningBalanceRules.WithBalance([.. WithoutCapital, new("1400", 0, R(57_05_000)), new("3100", 0, R(50_00_000))]);
        Assert.Equal([new OpeningLine("1400", 0, R(7_05_000))], resent.Where(l => l.Account == "1400").ToList());

        var over = OpeningBalanceRules.WithBalance([.. WithoutCapital, new("3100", 0, R(58_05_000))]);
        Assert.Equal(new OpeningLine("1400", R(1_00_000), 0), over[^1]);
    }

    [Theory]
    [InlineData("2026-10-24", "2026-04-01")]
    [InlineData("2026-04-01", "2026-04-01")]
    [InlineData("2026-03-31", "2025-04-01")]
    [InlineData("2027-01-15", "2026-04-01")]
    public void Opening_is_dated_the_start_of_the_financial_year(string business, string fy)
        => Assert.Equal(DateOnly.Parse(fy, System.Globalization.CultureInfo.InvariantCulture),
            OpeningBalanceRules.FyStart(DateOnly.Parse(business, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Validation_one_side_per_line_one_line_per_ledger_or_party_no_suspense_or_1400_and_codes_or_party_ids()
    {
        Assert.Equal(["Enter at least one opening line."], OpeningBalanceRules.Errors([]));
        Assert.Equal(["Enter at least one opening line."], OpeningBalanceRules.Errors(null));
        Assert.Equal(["Enter at least one opening line."], OpeningBalanceRules.Errors([new("1400", 5, 0)]));
        Assert.Contains("either a debit or a credit", OpeningBalanceRules.Errors([new("1000", 100, 100)])[0], StringComparison.Ordinal);
        Assert.Contains("either a debit or a credit", OpeningBalanceRules.Errors([new("1000", 0, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("whole paise", OpeningBalanceRules.Errors([new("1000", -5, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("entered twice", OpeningBalanceRules.Errors([new("1000", 5, 0), new("1000", 0, 5)])[0], StringComparison.Ordinal);
        Assert.Contains("Suspense", OpeningBalanceRules.Errors([new("9999", 5, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("filled automatically", OpeningBalanceRules.Errors([new("1400/X", 5, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("ledger code or a party ledger", OpeningBalanceRules.Errors([new("CASH", 5, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("ledger code or a party ledger", OpeningBalanceRules.Errors([new("1110/", 5, 0)])[0], StringComparison.Ordinal);
        Assert.Contains("too large", OpeningBalanceRules.Errors([new("1000", OpeningBalanceRules.MaxPaise + 1, 0)])[0], StringComparison.Ordinal);
        Assert.Empty(OpeningBalanceRules.Errors([new("1110/CMP-0007", 500, 0), new("1234", 0, 500)]));
        Assert.True(OpeningBalanceRules.IsAccount("1120/UD-9876543210"));
        Assert.Equal("1110", OpeningBalanceRules.CodeOf("1110/CMP-0007"));
    }

    [Fact]
    public void The_opening_journal_is_JRN_asOf_000_dated_asOf_and_balanced()
    {
        var v = OpeningBalanceRules.Voucher(Entry(Rulebook, OpeningBalanceRules.Approved));
        Assert.Equal(("JRN/20260401/000", FyStart, VoucherType.Journal, "opening", "opening", "opening-entry"), (v.Number, v.Date, v.Type, v.SourceKey, v.SourceKind, v.Sub));
        Assert.True(v.Balanced);
        Assert.Equal(R(77_40_000), v.Debit);
        Assert.Equal("Opening balances as of 2026-04-01", v.Narration);
        Assert.EndsWith("(draft, not approved)", OpeningBalanceRules.Voucher(Entry(Rulebook)).Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_gate_refuses_dates_on_or_after_asOf_while_draft_or_1400_is_not_zero()
    {
        var d = new DateOnly(2026, 10, 24);
        Assert.Null(OpeningBalanceRules.LockBlocker(null, d)); // no entry: ledgers run from zero (DONE open item 1)
        Assert.StartsWith("Opening balances pending: the opening entry as of 2026-04-01 is a draft", OpeningBalanceRules.LockBlocker(Entry(Rulebook), d), StringComparison.Ordinal);
        Assert.Null(OpeningBalanceRules.LockBlocker(Entry(Rulebook), new DateOnly(2026, 3, 31)));
        Assert.Null(OpeningBalanceRules.LockBlocker(Entry(Rulebook, OpeningBalanceRules.Approved), d));
        Assert.StartsWith("Opening balances pending: ₹5705000.00 Cr sits in 1400", OpeningBalanceRules.LockBlocker(Entry(WithoutCapital, OpeningBalanceRules.Approved), d), StringComparison.Ordinal);
        Assert.True(OpeningBalanceRules.Pending(null));
        Assert.True(OpeningBalanceRules.Pending(Entry(Rulebook)));
        Assert.True(OpeningBalanceRules.Pending(Entry(WithoutCapital, OpeningBalanceRules.Approved)));
        Assert.False(OpeningBalanceRules.Pending(Entry(Rulebook, OpeningBalanceRules.Approved)));
    }

    [Fact]
    public void Names_of_party_ledgers_and_of_1400()
    {
        Assert.Equal("Company receivables (city ledger) · CMP-DECCAN", OpeningBalanceRules.NameOf("1110/CMP-DECCAN"));
        Assert.Equal("Cash in hand", OpeningBalanceRules.NameOf("1000"));
        Assert.Equal(ChartOfAccounts.Find("1400")?.Name ?? "Opening-balance difference", OpeningBalanceRules.NameOf("1400"));
    }

    [Fact]
    public async Task The_approved_entry_supersedes_the_imported_opening_a_draft_never_does()
    {
        var imported = new FakeImport();
        var store = new FakeStore();
        Assert.Same(FakeImport.View, await OpeningBalanceQueries.EffectiveOpeningAsync(store, imported, 1, TestContext.Current.CancellationToken));
        Assert.Empty((await OpeningBalanceQueries.EffectiveOpeningAsync(null, null, 1, TestContext.Current.CancellationToken)).Rows);

        store.Entry = Entry(Rulebook);
        Assert.Same(FakeImport.View, await OpeningBalanceQueries.EffectiveOpeningAsync(store, imported, 1, TestContext.Current.CancellationToken));

        store.Entry = Entry(WithoutCapital, OpeningBalanceRules.Approved);
        var ob = await OpeningBalanceQueries.EffectiveOpeningAsync(store, imported, 1, TestContext.Current.CancellationToken);
        Assert.Equal((FyStart, R(77_40_000), R(77_40_000), true, 11), (ob.AsOfDate!.Value, ob.DebitPaise, ob.CreditPaise, ob.Balanced, ob.Rows.Count));
        var party = ob.Rows.Single(r => r.LedgerCode == "1110/CMP-DECCAN");
        Assert.Equal(("1110/CMP-DECCAN", "Asset", R(40_000)), (party.HccAccount, party.Group, party.DebitPaise));
        var diff = ob.Rows.Single(r => r.LedgerCode == "1400");
        Assert.Equal(("Suspense", R(57_05_000)), (diff.Group, diff.CreditPaise));
    }

    private sealed class FakeImport : IOpeningBalanceSource
    {
        public static readonly OpeningBalancesView View = new(new DateOnly(2026, 4, 1), 5, 100, 100, true,
            [new OpeningBalanceRowView("CAP", "Capital", "Equity", null, 0, 100), new OpeningBalanceRowView("CASH", "Cash", "Asset", "1000", 100, 0)]);

        public Task<OpeningBalancesView> GetAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(View);
    }

    private sealed class FakeStore : IOpeningEntryStore
    {
        public OpeningEntry? Entry { get; set; }

        public Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct) => Task.CompletedTask;

        public Task<OpeningEntry?> GetAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) => Task.FromResult(Entry);

        public Task<string> SaveDraftAsync(ICommandTransaction tx, int propertyId, DateOnly asOf, IReadOnlyList<OpeningLine> lines, string? note, int actor, DateTime now, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> ApproveAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, int actor, DateTime now, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> ReopenAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, string reason, int actor, DateTime now, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DateOnly?> FirstLockOnOrAfterAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct) => Task.FromResult<DateOnly?>(null);
    }
}
