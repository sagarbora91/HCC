using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Closing.Tests.Unit;

/// <summary>DEC-077 reopen rules without a database: who/which day, the messages, the preflight and re-close checks, accounting status, Tally notes.</summary>
public sealed class ReopenRulesTests
{
    private static readonly DateOnly D23 = new(2026, 10, 23);
    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);

    private static HccException Fails(HccErrorCode code, Action act)
    {
        var ex = Assert.Throws<HccException>(act);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    [Fact]
    public void A_reason_is_required_and_kept_to_200_characters()
    {
        Fails(HccErrorCode.VALIDATION, () => ReopenRules.EnsureReason(null));
        Fails(HccErrorCode.VALIDATION, () => ReopenRules.EnsureReason("   "));
        Fails(HccErrorCode.VALIDATION, () => ReopenRules.EnsureReason(new string('x', 201)));
        Assert.Equal("Wrong folio", ReopenRules.EnsureReason("  Wrong folio "));
    }

    [Fact]
    public void Only_a_closed_finished_unlocked_day_reopens_and_only_one_at_a_time()
    {
        ReopenRules.EnsureCanReopen(D24, D25, closed: true, reopenedDate: null, accountingLocked: false);
        Assert.Contains("already reopened", Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenRules.EnsureCanReopen(D24, D25, true, D24, false)).Message, StringComparison.Ordinal);
        Assert.Contains("is not closed", Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenRules.EnsureCanReopen(D25, D25, false, null, false)).Message, StringComparison.Ordinal);
        // Closed but the next day not opened yet (the close stopped after its barrier).
        Assert.Contains("Finish closing", Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenRules.EnsureCanReopen(D25, D25, true, null, false)).Message, StringComparison.Ordinal);
        Assert.Contains("2026-10-23 is already reopened. Close it again", Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenRules.EnsureCanReopen(D24, D25, true, D23, false)).Message, StringComparison.Ordinal);
        Assert.Contains("locked in Accounting", Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenRules.EnsureCanReopen(D24, D25, true, null, true)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_says_the_cash_book_lock_stays_and_names_the_Tally_re_export()
    {
        var locked = ReopenRules.Message(D24, cashBookLocked: true, tallyReexport: true);
        Assert.Contains("The cash book of 2026-10-24 stays locked (DEC-065): reopening the day does not unlock it", locked, StringComparison.Ordinal);
        Assert.Contains("never change", locked, StringComparison.Ordinal);
        Assert.Contains("next Tally export carries them again", locked, StringComparison.Ordinal);
        var open = ReopenRules.Message(D24, cashBookLocked: false, tallyReexport: false);
        Assert.Contains("does not change cash-book locks", open, StringComparison.Ordinal);
        Assert.DoesNotContain("Tally", open, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reopened_day_blocks_the_current_close_and_pending_corrections_block_the_re_close()
    {
        var data = new PreflightData(D25, [], [], [], [], [], [], Reopened: [D24]);
        var check = CloseRules.Evaluate(data).Single(c => c.Id == CloseRules.Reopened);
        Assert.True(check.Blocking);
        Assert.Equal("day:2026-10-24", Assert.Single(check.Items).Ref);
        Assert.Contains(CloseRules.Reopened, CloseRules.PreflightBlocking);
        Assert.False(CloseRules.Evaluate(new PreflightData(D25, [], [], [], [], [], [])).Single(c => c.Id == CloseRules.Reopened).Blocking);

        Assert.Empty(CloseRules.Blockers(ReopenRules.RecloseChecks(D24, [])));
        var blockers = CloseRules.Blockers(ReopenRules.RecloseChecks(D24, [new PendingApproval(7, "correction", new Paise(200000), "Wrong folio")]));
        var ex = CloseRules.Blocked(D24, blockers);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Contains("corrections of 2026-10-24 decided", ex.Message, StringComparison.Ordinal);
        Assert.Equal("approval:7", blockers[0].Items[0].Ref);
    }

    [Fact]
    public void The_closing_record_notes_each_reopen_with_who_and_when()
    {
        var at = new DateTime(2026, 10, 25, 4, 30, 0, DateTimeKind.Utc);
        Assert.Equal("Reopened 2026-10-25 04:30 UTC by user 1 (DEC-077): Wrong folio", ReopenRules.ExceptionNote(at, 1, "Wrong folio", null, null));
        Assert.Equal("Reopened 2026-10-25 04:30 UTC by user 1 (DEC-077): Wrong folio · closed again 2026-10-25 05:00 UTC by user 2",
            ReopenRules.ExceptionNote(at, 1, "Wrong folio", at.AddMinutes(30), 2));
    }

    [Fact]
    public void Accounting_treats_the_reopened_day_as_open_until_it_is_closed_again()
    {
        Assert.Equal(DayStatus.Open, AccountingDays.Status(false, D24, D25, reopened: D24));
        Assert.Equal(DayStatus.Closed, AccountingDays.Status(false, D23, D25, reopened: D24));
        Assert.Equal(DayStatus.Closed, AccountingDays.Status(false, D24, D25));
        Assert.Equal("acc:period:3:2026-10-24", AccountingDays.PeriodResource(3, D24));
    }

    [Fact]
    public void Tally_notes_follow_the_simulated_comment_and_cannot_break_out_of_an_XML_comment()
    {
        var plain = VoucherExport.TallyXml([], "HCC Demo");
        Assert.Equal(plain, VoucherExport.TallyXml([], "HCC Demo", []));
        var noted = VoucherExport.TallyXml([], "HCC Demo", ["RE-EXPORT · 2026-10-24 changed -- see <reason> & more"]);
        var lines = noted.Split('\n');
        Assert.Equal(VoucherExport.TallyComment, lines[1]);
        Assert.Equal("<!-- RE-EXPORT · 2026-10-24 changed - - see &lt;reason&gt; &amp; more -->", lines[2]);
        Assert.Equal(plain.Split('\n').Length + 1, lines.Length);
    }
}
