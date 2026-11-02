using Hcc.Application.Access;
using Hcc.Application.GuestServices;
using Hcc.Contracts;
using Hcc.Contracts.GuestServices;
using Hcc.Domain.Audit;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.GuestServices;

namespace Hcc.GuestServices.Tests.Kernel;

/// <summary>F4-T3 feedback service (record, list, follow-ups) over in-memory fakes, the sweep SQL shape and the audit viewer rules. No database.</summary>
public class FeedbackServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Policy(params string[] allowed) : IAccessPolicy
    {
        public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
            allowed.Contains(permissionId) ? Task.CompletedTask : throw new HccException(HccErrorCode.FORBIDDEN, "Access denied.");
        public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(allowed.Contains(permissionId));
    }

    private sealed class Store : IFeedbackStore
    {
        public string StayStatus = "InHouse";
        public readonly List<(int Stars, string? Comment, FeedbackComplaint? Complaint)> Rows = [];
        public readonly List<FeedbackFollowUpView> Follow = [new(1, "complaint", 7, new DateOnly(2026, 10, 24), "open", DateTime.UtcNow, null)];
        public Task<FeedbackStay?> StayAsync(int propertyId, long stayId, CancellationToken ct = default) =>
            Task.FromResult<FeedbackStay?>(stayId == 9 ? new FeedbackStay(9, 5, "105", "Test Guest", StayStatus) : null);
        public Task<FeedbackResult> InsertAsync(int propertyId, int principalId, FeedbackStay stay, int stars, string? comment, FeedbackComplaint? complaint, DateTime utc, CancellationToken ct = default)
        {
            if (Rows.Count > 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Feedback for this stay is already recorded.");
            Rows.Add((stars, comment, complaint));
            return Task.FromResult(new FeedbackResult(1, complaint is null ? null : 77));
        }
        public Task<IReadOnlyList<FeedbackView>> ListAsync(int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FeedbackView>>(Rows.Select((r, i) => new FeedbackView(i + 1, 9, "105", "Test Guest", r.Stars, r.Comment, null, DateTime.UtcNow, 1)).ToList());
        public Task<IReadOnlyList<FeedbackFollowUpView>> FollowUpsAsync(int propertyId, bool openOnly, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeedbackFollowUpView>>(Follow);
        public Task<bool> ResolveFollowUpAsync(int propertyId, int principalId, long followUpId, DateTime utc, CancellationToken ct = default)
        {
            var i = Follow.FindIndex(f => f.FollowUpId == followUpId && f.Status == "open");
            if (i < 0) return Task.FromResult(false);
            Follow[i] = Follow[i] with { Status = "done" };
            return Task.FromResult(true);
        }
    }

    private static (FeedbackService Svc, Store Store) Make(params string[] perms)
    {
        var store = new Store();
        return (new FeedbackService(new Policy(perms), store, TimeProvider.System), store);
    }

    [Fact]
    public async Task Four_stars_records_without_a_complaint_and_a_second_entry_is_refused()
    {
        var (svc, store) = Make(FeedbackService.RecordPermission);
        var r = await svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 4, "Lovely", null), Ct);
        Assert.Null(r.ComplaintId);
        Assert.Null(store.Rows[0].Complaint);
        var ex = await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 5, null, null), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        var page = await svc.ListAsync(1, 1, null, null, Ct);
        Assert.Equal(1, page.Total);
        Assert.Equal(4.0, page.Average);
    }

    [Fact]
    public async Task Low_stars_or_a_categorised_comment_open_a_complaint()
    {
        var (a, sa) = Make(FeedbackService.RecordPermission);
        var r = await a.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 1, "Noisy room", null), Ct);
        Assert.Equal(77, r.ComplaintId);
        Assert.Equal(("Other", "High"), (sa.Rows[0].Complaint!.Category, sa.Rows[0].Complaint!.Severity));
        var (b, sb) = Make(FeedbackService.RecordPermission);
        await b.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 4, "AC was weak", "Maintenance"), Ct);
        Assert.Equal("Maintenance", sb.Rows[0].Complaint!.Category);
    }

    [Fact]
    public async Task Validation_role_and_stay_state_are_enforced()
    {
        var (svc, store) = Make(FeedbackService.RecordPermission);
        await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 6, null, null), Ct));
        await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 3, null, "Not a category"), Ct));
        await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 3, new string('x', 401), null), Ct));
        var nf = await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(404, 3, null, null), Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, nf.Code);
        store.StayStatus = "Reserved";
        await Assert.ThrowsAsync<HccException>(() => svc.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 3, null, null), Ct));
        var (none, _) = Make();
        var fx = await Assert.ThrowsAsync<HccException>(() => none.RecordAsync(1, 1, new RecordGuestFeedbackRequest(9, 3, null, null), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, fx.Code);
        await Assert.ThrowsAsync<HccException>(() => none.ListAsync(1, 1, null, null, Ct));
    }

    [Fact]
    public async Task Follow_ups_are_for_the_owner_permission_and_resolve_once()
    {
        var (rec, _) = Make(FeedbackService.RecordPermission);
        await Assert.ThrowsAsync<HccException>(() => rec.FollowUpsAsync(1, 1, true, Ct));
        var (own, _) = Make(FeedbackService.FollowUpPermission);
        Assert.Single(await own.FollowUpsAsync(1, 1, true, Ct));
        await own.ResolveFollowUpAsync(1, 1, new ResolveFeedbackFollowUpRequest(1), Ct);
        var nf = await Assert.ThrowsAsync<HccException>(() => own.ResolveFollowUpAsync(1, 1, new ResolveFeedbackFollowUpRequest(1), Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, nf.Code);
    }

    [Fact]
    public void Sweep_sql_covers_complaints_and_lost_items_at_seven_days_and_closes_resolved_ones()
    {
        var sql = SqlFeedbackStore.SweepSql;
        Assert.Contains("ops.guest_complaint", sql);
        Assert.Contains("ops.lost_item", sql);
        Assert.Contains(">= 7", sql);
        Assert.Contains("SET status = 'done'", sql);
        Assert.Equal("feedback-followups", FeedbackFollowUpProjector.ConsumerName);
    }

    [Fact]
    public void Audit_viewer_masks_money_and_splits_the_target()
    {
        Assert.Equal("Rate Deluxe · ₹ •• → ₹ ••", AuditViewRules.MaskMoney("Rate Deluxe · ₹1,500 → ₹1,800.50"));
        Assert.Equal("no money here", AuditViewRules.MaskMoney("no money here"));
        Assert.Equal(("user", "12"), (AuditViewRules.EntityOf("user:12"), AuditViewRules.EntityIdOf("user:12")));
        Assert.Equal(("setting", string.Empty), (AuditViewRules.EntityOf("setting"), AuditViewRules.EntityIdOf("setting")));
    }
}
