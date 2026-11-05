using Hcc.Domain.Money;

namespace Hcc.Domain.Stays;

/// <summary>Scope of a rate / guest-count change (LF-CONTRACT §2): <c>next</c> = from the next business date, <c>today-next</c> = tonight and later,
/// <c>all</c> = every night of the stay that is not on a closed business date (DEC-077).</summary>
public static class StayRateScopes
{
    public const string Next = "next", TodayNext = "today-next", All = "all";

    public static readonly IReadOnlyList<string> Values = [Next, TodayNext, All];
}

/// <summary>A posted room night of the folio (charge id, the night it bills, amount, whether it is already reversed).</summary>
public sealed record StayRoomNight(long ChargeId, DateOnly Night, Paise Amount, bool Reversed);

/// <summary>A requested change: new nightly rate (before GST), adults, children, scope.</summary>
public sealed record StayRateChangeInput(Paise Rate, int People, int Children, string Scope, string Reason);

/// <summary>
/// LF A1 rules (legacy "Rate And Pax Change", F9) — the same as the prototype's domain/streams/stay-rate.ts: refusals, who applies directly
/// (the Owner; a Manager within PERM-RATE-001 below the tariff — any raise of guests on a room type with an extra-person charge needs an
/// approval), who approves, the first night the new rate applies and which posted nights re-post (reversal + new line at the new rate with
/// the GST slab of that night's actual price, DEC-069).
/// </summary>
public static class StayRateRules
{
    /// <summary>Refusal text, or null. <paramref name="capacity"/> = adults + children the room type takes.</summary>
    public static string? Refusal(bool inHouse, DateOnly businessDate, DateOnly departure, Paise oldRate, int oldPeople, int oldChildren, StayRateChangeInput c, int capacity)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!inHouse) return "A rate or guest-count change applies to an in-house stay only.";
        if (!StayRateScopes.Values.Contains(c.Scope)) return "Choose when the change applies: next day, tonight and later, or every open night.";
        if (string.IsNullOrWhiteSpace(c.Reason)) return "A reason is required.";
        if (c.Rate.Value < 0) return "Enter the new rate in whole paise (0 or more).";
        if (c.People is < 1 or > 4) return "Choose 1–4 guests.";
        if (c.Children is < 0 or > 3) return "Children must be between 0 and 3.";
        if (c.Rate == oldRate && c.People == oldPeople && c.Children == oldChildren) return "Nothing changes: give a new rate or a new number of guests.";
        if (c.People + c.Children > capacity) return $"The room accommodates at most {capacity} guests including children.";
        if (c.Scope == StayRateScopes.Next && businessDate.AddDays(1) >= departure) return "The stay ends tomorrow: there is no later night to change. Choose “Tonight and later”.";
        return null;
    }

    /// <summary>The lowest rate a Manager sets without an approval: tariff × (10000 − limitBps) / 10000, rounded up.</summary>
    public static Paise ManagerFloor(Paise tariff, int limitBps) => new((tariff.Value * (10_000 - limitBps) + 9_999) / 10_000);

    /// <summary>Owner: always. Manager: rate at or above the tariff, or at or above the floor — unless guests rise on a type with an extra-person charge.</summary>
    public static bool CanApplyDirectly(bool owner, bool manager, Paise tariff, int managerLimitBps, Paise newRate, bool guestsRise, bool extraPersonCharge)
    {
        if (owner) return true;
        if (!manager) return false;
        if (guestsRise && extraPersonCharge) return false;
        return newRate >= tariff || newRate >= ManagerFloor(tariff, managerLimitBps);
    }

    /// <summary>Who may approve a 'rate-change' request: the Owner anything; a Manager within the same limit.</summary>
    public static bool CanApprove(bool owner, bool manager, Paise tariff, int managerLimitBps, Paise newRate) =>
        owner || (manager && (newRate >= tariff || newRate >= ManagerFloor(tariff, managerLimitBps)));

    // ── CTL-01 (audit; AUDITFIX-PLAN §2 Q4 default (a)): a reduction is judged against the AGREED rate as well as the tariff — the stricter wins. ──

    /// <summary>The lowest rate a Manager sets or approves against the agreed rate: agreed × (10000 − thresholdBps) / 10000, rounded up.
    /// A rate AT the floor passes; one paisa below it needs the Owner.</summary>
    public static Paise AgreedFloor(Paise agreed, int thresholdBps) => ManagerFloor(agreed, Math.Clamp(thresholdBps, 0, 10_000));

    /// <summary>The agreed rate of a stay: the agreed rate recorded after the latest change (an Owner-set or Owner-approved rate becomes the new
    /// agreed rate; a Manager's change does not, so a stay cannot be walked down in steps), else the rate before the first change, else the current rate.</summary>
    public static Paise AgreedBase(long? latestAgreedAfter, long? firstOldRate, long currentRate) => new(latestAgreedAfter ?? firstOldRate ?? currentRate);

    /// <summary>True when the new rate is more than <paramref name="thresholdBps"/> below the agreed rate (below <see cref="AgreedFloor"/>).</summary>
    public static bool OverThreshold(Paise agreed, int thresholdBps, Paise newRate) => newRate < AgreedFloor(agreed, thresholdBps);

    /// <summary>How far below the agreed rate the new rate is, in basis points (0 for a raise), rounded down.</summary>
    public static int ReductionBps(Paise agreed, Paise newRate) =>
        agreed.Value <= 0 || newRate >= agreed ? 0 : (int)((agreed.Value - newRate.Value) * 10_000 / agreed.Value);

    /// <summary>CTL-01 (af2-fix finding 2): the agreed-rate threshold binds only a change that MOVES the rate — a guest-count or scope-only change
    /// (new rate = current rate) is never blocked by it, even on a stay already below the agreed floor. Same rule as the prototype's
    /// <c>change.rate!==b.rate&amp;&amp;strOverThreshold(…)</c>. <paramref name="currentRate"/> null = treat the rate as moving.</summary>
    public static bool NeedsOwner(Paise agreed, int thresholdBps, Paise? currentRate, Paise newRate) =>
        (currentRate is null || newRate != currentRate.Value) && OverThreshold(agreed, thresholdBps, newRate);

    /// <summary>The CTL-01 refusal / approval text when only the Owner may set the rate (ChangeStayRate decide, move room, extend stay).</summary>
    public static string OwnerDecidesText(Paise agreed, int thresholdBps) =>
        $"This rate is more than {Hcc.Domain.Configuration.SettingRules.Percent(thresholdBps)} below the agreed rate {agreed}: the Owner decides it (CTL-01).";

    /// <summary>CTL-01 direct rule: the tariff rule above AND, for a Manager, not more than the threshold below the agreed rate when the rate moves
    /// (<see cref="NeedsOwner"/>). The Owner: always.</summary>
    public static bool CanApplyDirectly(bool owner, bool manager, Paise tariff, int managerLimitBps, Paise agreed, int thresholdBps, Paise newRate, bool guestsRise, bool extraPersonCharge,
        Paise? currentRate = null) =>
        owner || (CanApplyDirectly(false, manager, tariff, managerLimitBps, newRate, guestsRise, extraPersonCharge) && !NeedsOwner(agreed, thresholdBps, currentRate, newRate));

    /// <summary>CTL-01 approval rule: the Owner anything; a Manager within the tariff limit AND (when the rate moves) the agreed-rate threshold.</summary>
    public static bool CanApprove(bool owner, bool manager, Paise tariff, int managerLimitBps, Paise agreed, int thresholdBps, Paise newRate, Paise? currentRate = null) =>
        owner || (CanApprove(false, manager, tariff, managerLimitBps, newRate) && !NeedsOwner(agreed, thresholdBps, currentRate, newRate));

    /// <summary>First night at the new rate. <paramref name="closed"/> = a night on a closed business date (not reopened).</summary>
    public static DateOnly EffectiveFrom(string scope, DateOnly businessDate, DateOnly arrival, DateOnly departure, Func<DateOnly, bool> closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        if (scope == StayRateScopes.Next) return businessDate.AddDays(1);
        if (scope == StayRateScopes.TodayNext) return businessDate;
        for (var d = arrival; d < departure; d = d.AddDays(1)) if (!closed(d)) return d;
        return businessDate;
    }

    /// <summary>The posted room nights that re-post: live, positive, from <see cref="EffectiveFrom"/>, before departure, never on a closed date.</summary>
    public static IReadOnlyList<StayRoomNight> RepostPlan(IEnumerable<StayRoomNight> nights, string scope, DateOnly businessDate, DateOnly arrival, DateOnly departure, Func<DateOnly, bool> closed)
    {
        ArgumentNullException.ThrowIfNull(nights);
        var from = EffectiveFrom(scope, businessDate, arrival, departure, closed);
        return nights.Where(n => !n.Reversed && n.Amount.Value > 0 && n.Night >= from && n.Night < departure && !closed(n.Night)).OrderBy(n => n.Night).ToList();
    }
}

/// <summary>A guest's agreed rate for a room type (LF B1, gst.guest_agreed_rate).</summary>
public sealed record GuestAgreedRate(long GuestId, int RoomTypeId, Paise Rate, string? PlanCode, DateOnly? ValidTo, string? Note);

public static class GuestAgreedRateRules
{
    /// <summary>The rate valid on the arrival date (valid_to inclusive), or null.</summary>
    public static GuestAgreedRate? ValidOn(IEnumerable<GuestAgreedRate> rates, int roomTypeId, DateOnly arrival) =>
        rates?.FirstOrDefault(r => r.RoomTypeId == roomTypeId && (r.ValidTo is null || arrival <= r.ValidTo));

    /// <summary>DEC-MA-11e: the role codes that may receive the agreed rates (money roles).</summary>
    public static readonly IReadOnlySet<string> MoneyRoles = new HashSet<string>(StringComparer.Ordinal) { "OWN", "MGR", "REC", "CSH", "ACC" };
}
