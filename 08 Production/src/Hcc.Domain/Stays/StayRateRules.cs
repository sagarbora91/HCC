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
