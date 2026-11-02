using Hcc.GuestServices.Tests.Harness;
using Hcc.Infrastructure.SqlServer.GuestServices;

namespace Hcc.GuestServices.Tests.Sql;

/// <summary>F4-int: SqlFeedbackStore against SQL Server (ops.guest_feedback / ops.feedback_followup, migrations 4393-4394).</summary>
[Trait("Category", "Sql")]
public class FeedbackSqlTests : IAsyncLifetime
{
    private GsWorld _w = null!;
    private SqlFeedbackStore _s = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await GsWorld.CreateAsync(Ct);
        _s = new SqlFeedbackStore(_w.Db.Platform.Connections);
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [Fact]
    public async Task Feedback_is_stored_once_per_stay_and_a_low_score_opens_a_complaint()
    {
        var stay = await _s.StayAsync(_w.PropertyId, _w.Stays["HCC-1041"], Ct);
        Assert.NotNull(stay);
        Assert.Null(await _s.StayAsync(_w.PropertyId, 987_654_321, Ct));
        var at = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var r = await _s.InsertAsync(_w.PropertyId, Who.Reception, stay!, 2, "Noisy AC", new("Maintenance", "Medium", "Noisy AC"), at, Ct);
        Assert.NotNull(r.ComplaintId);
        Assert.Equal("Maintenance", await _w.Db.StringAsync($"SELECT category FROM ops.guest_complaint WHERE complaint_id = {r.ComplaintId};", Ct));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _s.InsertAsync(_w.PropertyId, Who.Reception, stay!, 5, null, null, at, Ct)));
        var list = await _s.ListAsync(_w.PropertyId, null, null, Ct);
        var row = Assert.Single(list);
        Assert.Equal((2, "Noisy AC", r.ComplaintId), (row.Stars, row.Comment, row.ComplaintId));
        Assert.True(await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE target LIKE '%{r.FeedbackId}%';", Ct) >= 1);
    }

    [Fact]
    public async Task Open_follow_up_is_listed_and_resolved_once()
    {
        var id = await _w.Db.LongAsync($"INSERT ops.feedback_followup(property_id, source, ref_id, since_date) OUTPUT INSERTED.followup_id VALUES ({_w.PropertyId}, 'complaint', 1, '2026-10-20');", Ct);
        var open = await _s.FollowUpsAsync(_w.PropertyId, true, Ct);
        Assert.Equal(("complaint", "open"), (Assert.Single(open).Source, open[0].Status));
        Assert.True(await _s.ResolveFollowUpAsync(_w.PropertyId, Who.Owner, id, new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc), Ct));
        Assert.False(await _s.ResolveFollowUpAsync(_w.PropertyId, Who.Owner, id, new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc), Ct));
        Assert.Empty(await _s.FollowUpsAsync(_w.PropertyId, true, Ct));
        Assert.Equal("done", Assert.Single(await _s.FollowUpsAsync(_w.PropertyId, false, Ct)).Status);
    }
}
