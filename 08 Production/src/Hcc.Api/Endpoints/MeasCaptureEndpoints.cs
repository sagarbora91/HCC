// F12 MEAS · f12-int-cap: HTTP surface of the measurement capture module (MEAS-CONTRACT §2). Every command route is
// POST /api/v1/commands/<Name> with an Idempotency-Key header and .RequireHccMeasurement(<key>) (FORBIDDEN + details.reason MEAS_OFF while off);
// GET /api/v1/meas/captures returns the s.measX slice, masked per caller (DEC-061; training / exit for HR-001 holders only; pulse anonymous).
// No public (login-free) survey route: the QR page posts MeasSurveyAdd through a signed-in session (via 'qr' is accepted from any signed-in
// user and rate-limited by GuestRules.RateLimited). A login-free route needs a property resolver and the web-booking rate limiter: integration item.
using Hcc.Application.Commands;
using Hcc.Application.Meas;
using Hcc.Contracts.Meas;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Meas;
using Hcc.Operations.Meas;

namespace Hcc.Api.Endpoints;

public static class MeasCaptureEndpoints
{
    /// <summary>(command name, measurement key) of every capture route — pinned by Hcc.Meas.Tests.</summary>
    public static IReadOnlyList<(string Command, string Key)> Routes { get; } =
    [
        (MeasCaptureService.OpTurnaway, MeasCaptureService.KeyTurnaway), (MeasCaptureService.OpWalked, MeasCaptureService.KeyWalked),
        (MeasCaptureService.OpAllowance, MeasCaptureService.KeyAllowance), (MeasCaptureService.OpBookingExtra, MeasCaptureService.KeyExtra),
        (MeasCaptureService.OpPreference, MeasCaptureService.KeyPreference),
        (MeasCaptureService.OpSurvey, MeasCaptureService.KeySurvey), (MeasCaptureService.OpReview, MeasCaptureService.KeyReview),
        (MeasCaptureService.OpRootCauseList, MeasCaptureService.KeyRecovery), (MeasCaptureService.OpRootCauseSet, MeasCaptureService.KeyRecovery),
        (MeasCaptureService.OpLoyalty, MeasCaptureService.KeyRepeat),
        (MeasCaptureService.OpTraining, MeasCaptureService.KeyTraining), (MeasCaptureService.OpExit, MeasCaptureService.KeyExit), (MeasCaptureService.OpPulse, MeasCaptureService.KeyPulse),
        (MeasCaptureService.OpOffline, MeasCaptureService.KeyOoo), (MeasCaptureService.OpMeter, MeasCaptureService.KeyUtilities), (MeasCaptureService.OpReading, MeasCaptureService.KeyUtilities),
        (MeasCaptureService.OpAssetLife, MeasCaptureService.KeyAssets), (MeasCaptureService.OpDiscard, MeasCaptureService.KeyLinen),
        (MeasCaptureService.OpEvent, MeasCaptureService.KeyEvents), (MeasCaptureService.OpEventDelete, MeasCaptureService.KeyEvents), (MeasCaptureService.OpOtb, MeasCaptureService.KeyPickup),
        (MeasCaptureService.OpCompetitor, MeasCaptureService.KeyRateShop), (MeasCaptureService.OpRateShop, MeasCaptureService.KeyRateShop),
    ];

    public static IServiceCollection AddMeasCaptureModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlMeasCaptureStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IMeasCaptureCommon>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasDeskStore>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasGuestStore>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasPeopleStore>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasHkStore>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasRevenueStore>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<IMeasCaptureReads>(sp => sp.GetRequiredService<SqlMeasCaptureStore>());
        services.AddSingleton<MeasCaptureService>();
        return services;
    }

    private static RouteHandlerBuilder Command<TReq>(RouteGroupBuilder g, string name, string key,
        Func<MeasCaptureService, CommandContext, TReq, CancellationToken, Task<CommandResult<MeasCaptureResult>>> run) =>
        g.MapPost("/commands/" + name, async (HttpContext http, TReq body, MeasCaptureService svc, CancellationToken ct) =>
            http.HccReply(await run(svc, http.HccCommand(name), body, ct).ConfigureAwait(false))).RequireHccMeasurement(key);

    public static RouteGroupBuilder MapMeasCaptureEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var s = api.MapHccSecured();
        // desk
        Command<LogTurnawayRequest>(s, MeasCaptureService.OpTurnaway, MeasCaptureService.KeyTurnaway, (v, c, b, t) => v.LogTurnawayAsync(c, b, t));
        Command<LogWalkedGuestRequest>(s, MeasCaptureService.OpWalked, MeasCaptureService.KeyWalked, (v, c, b, t) => v.LogWalkedGuestAsync(c, b, t));
        Command<SaveAllowanceReasonRequest>(s, MeasCaptureService.OpAllowance, MeasCaptureService.KeyAllowance, (v, c, b, t) => v.SaveAllowanceReasonAsync(c, b, t));
        Command<SetBookingExtraRequest>(s, MeasCaptureService.OpBookingExtra, MeasCaptureService.KeyExtra, (v, c, b, t) => v.SetBookingExtraAsync(c, b, t));
        Command<SetCustomerPreferenceRequest>(s, MeasCaptureService.OpPreference, MeasCaptureService.KeyPreference, (v, c, b, t) => v.SetCustomerPreferenceAsync(c, b, t));
        // guest
        Command<MeasSurveyAddRequest>(s, MeasCaptureService.OpSurvey, MeasCaptureService.KeySurvey, (v, c, b, t) => v.SurveyAddAsync(c, b, t));
        Command<MeasReviewSaveRequest>(s, MeasCaptureService.OpReview, MeasCaptureService.KeyReview, (v, c, b, t) => v.ReviewSaveAsync(c, b, t));
        Command<MeasRootCauseListSaveRequest>(s, MeasCaptureService.OpRootCauseList, MeasCaptureService.KeyRecovery, (v, c, b, t) => v.RootCauseListSaveAsync(c, b, t));
        Command<MeasRootCauseSetRequest>(s, MeasCaptureService.OpRootCauseSet, MeasCaptureService.KeyRecovery, (v, c, b, t) => v.RootCauseSetAsync(c, b, t));
        Command<MeasLoyaltySetRequest>(s, MeasCaptureService.OpLoyalty, MeasCaptureService.KeyRepeat, (v, c, b, t) => v.LoyaltySetAsync(c, b, t));
        // people (the pulse is anonymous: no command runner, reply without the replay header)
        Command<SaveMeasTrainingRequest>(s, MeasCaptureService.OpTraining, MeasCaptureService.KeyTraining, (v, c, b, t) => v.SaveTrainingAsync(c, b, t));
        Command<SetMeasExitRequest>(s, MeasCaptureService.OpExit, MeasCaptureService.KeyExit, (v, c, b, t) => v.SetExitAsync(c, b, t));
        s.MapPost("/commands/" + MeasCaptureService.OpPulse, async (HttpContext http, AddMeasPulseRequest body, MeasCaptureService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddPulseAsync(http.HccCommand(MeasCaptureService.OpPulse), body, ct).ConfigureAwait(false))).RequireHccMeasurement(MeasCaptureService.KeyPulse);
        // housekeeping / engineering
        Command<MeasOfflineSetRequest>(s, MeasCaptureService.OpOffline, MeasCaptureService.KeyOoo, (v, c, b, t) => v.OfflineSetAsync(c, b, t));
        Command<MeasMeterSaveRequest>(s, MeasCaptureService.OpMeter, MeasCaptureService.KeyUtilities, (v, c, b, t) => v.MeterSaveAsync(c, b, t));
        Command<MeasReadingAddRequest>(s, MeasCaptureService.OpReading, MeasCaptureService.KeyUtilities, (v, c, b, t) => v.ReadingAddAsync(c, b, t));
        Command<MeasAssetLifeSetRequest>(s, MeasCaptureService.OpAssetLife, MeasCaptureService.KeyAssets, (v, c, b, t) => v.AssetLifeSetAsync(c, b, t));
        Command<MeasDiscardAddRequest>(s, MeasCaptureService.OpDiscard, MeasCaptureService.KeyLinen, (v, c, b, t) => v.DiscardAddAsync(c, b, t));
        // revenue
        Command<MeasEventSaveRequest>(s, MeasCaptureService.OpEvent, MeasCaptureService.KeyEvents, (v, c, b, t) => v.EventSaveAsync(c, b, t));
        Command<MeasEventDeleteRequest>(s, MeasCaptureService.OpEventDelete, MeasCaptureService.KeyEvents, (v, c, b, t) => v.EventDeleteAsync(c, b, t));
        Command<MeasOtbSnapshotRequest>(s, MeasCaptureService.OpOtb, MeasCaptureService.KeyPickup, (v, c, b, t) => v.OtbSnapshotAsync(c, b, t));
        Command<MeasCompetitorSaveRequest>(s, MeasCaptureService.OpCompetitor, MeasCaptureService.KeyRateShop, (v, c, b, t) => v.CompetitorSaveAsync(c, b, t));
        Command<MeasRateShopAddRequest>(s, MeasCaptureService.OpRateShop, MeasCaptureService.KeyRateShop, (v, c, b, t) => v.RateShopAddAsync(c, b, t));
        // read (reads stay open while an item is off; data is kept)
        s.MapGet("/meas/captures", async (HttpContext http, MeasCaptureService svc, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await svc.CapturesAsync(p, u, ct).ConfigureAwait(false));
        });
        return api;
    }
}
