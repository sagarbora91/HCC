using System.Text.RegularExpressions;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Groups;

/// <summary>Where a share of a posted line is billed. <see cref="Follow"/> exists only for the tax rule (= the line's own net target).</summary>
public enum RouteTarget
{
    Master,
    Guest,
    Follow,
}

/// <summary>Routing categories: room nights and discounts; extras (service lines); the GST of every line.</summary>
public enum RouteCategory
{
    Room,
    Extras,
    Tax,
}

public sealed record RoutingRule(long? ReservationId, RouteCategory Category, RouteTarget Target);

/// <summary>A resolved route: each share goes to Master or Guest (never Follow).</summary>
public sealed record LineRoute(RouteTarget Net, RouteTarget Tax);

public sealed record LineShares(Paise Master, Paise Guest);

public static class GroupCodes
{
    public static string Code(RouteTarget t) => t switch
    {
        RouteTarget.Master => "master",
        RouteTarget.Guest => "guest",
        RouteTarget.Follow => "follow",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown target."),
    };

    public static string Code(RouteCategory c) => c switch
    {
        RouteCategory.Room => "room",
        RouteCategory.Extras => "extras",
        RouteCategory.Tax => "tax",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Unknown category."),
    };

    public static RouteTarget ParseTarget(string? code, bool allowFollow = false) => code switch
    {
        "master" => RouteTarget.Master,
        "guest" => RouteTarget.Guest,
        "follow" when allowFollow => RouteTarget.Follow,
        _ => throw HccException.Validation(allowFollow ? "Route the tax to master, guest or follow." : "Route to master or guest."),
    };

    public static RouteCategory ParseCategory(string? code) => code switch
    {
        "room" => RouteCategory.Room,
        "extras" => RouteCategory.Extras,
        "tax" => RouteCategory.Tax,
        _ => throw HccException.Validation("The routing category must be room, extras or tax."),
    };
}

/// <summary>
/// Routing rules (server rule set; the prototype bills a block either to the company or to the guests as a whole — <c>billTo</c>
/// company | guest — and shows a read-only master folio). Precedence: the member's override → the group default → the built-in
/// default (room → master, tax → follow, extras → guest). Discounts follow the room category; a reversal line takes the route of
/// the line it reverses.
/// </summary>
public static class GroupRouting
{
    public static readonly IReadOnlyList<RoutingRule> Defaults =
    [
        new(null, RouteCategory.Room, RouteTarget.Master),
        new(null, RouteCategory.Tax, RouteTarget.Follow),
        new(null, RouteCategory.Extras, RouteTarget.Guest),
    ];

    /// <summary>Prototype <c>billTo</c>: company → room + tax to the master folio; guest → everything on the guest folios.</summary>
    public static IReadOnlyList<RoutingRule> ForBillTo(bool company) => company
        ? Defaults
        : [new(null, RouteCategory.Room, RouteTarget.Guest), new(null, RouteCategory.Tax, RouteTarget.Follow), new(null, RouteCategory.Extras, RouteTarget.Guest)];

    public static RouteCategory CategoryOf(ChargeKind kind) => kind switch
    {
        ChargeKind.Room or ChargeKind.Discount => RouteCategory.Room,
        ChargeKind.Service => RouteCategory.Extras,
        _ => throw new InvalidOperationException("A reversal line takes the route of the line it reverses."),
    };

    public static void Validate(RoutingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.Target == RouteTarget.Follow && rule.Category != RouteCategory.Tax)
            throw HccException.Validation("Only the tax can follow the line; route room and extras to master or guest.");
    }

    public static RouteTarget Resolve(IReadOnlyCollection<RoutingRule> rules, long reservationId, RouteCategory category)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.FirstOrDefault(r => r.ReservationId == reservationId && r.Category == category)?.Target
            ?? rules.FirstOrDefault(r => r.ReservationId is null && r.Category == category)?.Target
            ?? Defaults.First(r => r.Category == category).Target;
    }

    /// <summary>The route a new (non-reversal) line of <paramref name="kind"/> gets for this member.</summary>
    public static LineRoute ForLine(IReadOnlyCollection<RoutingRule> rules, long reservationId, ChargeKind kind)
    {
        var net = Resolve(rules, reservationId, CategoryOf(kind));
        var tax = Resolve(rules, reservationId, RouteCategory.Tax);
        return new LineRoute(net, tax == RouteTarget.Follow ? net : tax);
    }

    public static LineShares Shares(Paise net, Paise tax, LineRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var master = (route.Net == RouteTarget.Master ? net : Paise.Zero) + (route.Tax == RouteTarget.Master ? tax : Paise.Zero);
        return new LineShares(master, net + tax - master);
    }

    /// <summary>
    /// Re-route guard (never below what already left the guest folio): an open folio may take any new master share ≥ transferred;
    /// a departed (settled) guest folio can only give back an untransferred share — new share between transferred and the current one.
    /// </summary>
    public static void EnsureRerouteAllowed(bool folioOpen, Paise currentShare, Paise newShare, Paise transferred)
    {
        if (newShare < transferred)
            throw new HccException(Contracts.HccErrorCode.INVALID_TRANSITION,
                $"This member's master share was already transferred ({transferred}); the new share ({newShare}) cannot be lower. Correct it on the guest folio first.");
        if (!folioOpen && newShare > currentShare)
            throw new HccException(Contracts.HccErrorCode.INVALID_TRANSITION, "The guest has checked out; a settled guest line can no longer move to the master folio.");
    }
}

public sealed record BlockTypeCounts(string RoomType, int Allotted, int Picked, int Released, Paise Rate);

public sealed record PickupLine(string RoomType, int Allotted, int Picked, int Released, int Remaining, Paise Rate, int PickedPercent);

/// <summary>Pickup figures per room type (prototype <c>grpLines</c> / <c>grpBlockTotals</c>).</summary>
public static class GroupPickup
{
    /// <summary>Open and past the cut-off = release due (shown as released before the sweep persists it), prototype <c>grpReleaseDue</c>.</summary>
    public static bool ReleaseDue(string blockStatus, DateOnly cutoff, DateOnly businessDate) => blockStatus == "Open" && businessDate > cutoff;

    public static string EffectiveStatus(string blockStatus, DateOnly cutoff, DateOnly businessDate)
        => ReleaseDue(blockStatus, cutoff, businessDate) ? "Released" : blockStatus;

    public static PickupLine Line(BlockTypeCounts c, string blockStatus, DateOnly cutoff, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(c);
        var open = EffectiveStatus(blockStatus, cutoff, businessDate) == "Open";
        var due = ReleaseDue(blockStatus, cutoff, businessDate);
        var free = Math.Max(0, c.Allotted - c.Picked);
        var released = c.Released + (due ? free : 0);
        var baseCount = c.Allotted + c.Released;
        var pct = baseCount == 0 ? 0 : (int)Math.Round(c.Picked * 100m / baseCount, MidpointRounding.AwayFromZero);
        return new PickupLine(c.RoomType, baseCount, c.Picked, released, open ? free : 0, c.Rate, pct);
    }

    public static (int Allotted, int Picked, int Released, int Remaining, int PickedPercent) Totals(IReadOnlyCollection<PickupLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var a = lines.Sum(l => l.Allotted);
        var p = lines.Sum(l => l.Picked);
        return (a, p, lines.Sum(l => l.Released), lines.Sum(l => l.Remaining), a == 0 ? 0 : (int)Math.Round(p * 100m / a, MidpointRounding.AwayFromZero));
    }
}

public sealed record AttritionInput(string RoomType, int Allotted, int Picked, Paise Rate);

/// <summary>One attrition line: chargeable unpicked rooms of a type for one night at the per-room-night attrition price.</summary>
public sealed record AttritionLine(string RoomType, DateOnly Night, int Rooms, Paise PerRoomNight, Paise Amount);

/// <summary>
/// Attrition (server rule; the prototype only releases unpicked rooms at the cut-off and charges nothing): for every room type,
/// unpicked = contracted (allotted at contract = allotted + released) − picked; the first ⌊contracted × allowance⌋ rooms are free;
/// each further room is charged, for every night of the block, the block rate × attrition % (half away from zero). GST is taken
/// per line from the room rule on the per-room-night price (DEC-069: per unit per night on the price charged; cancellation /
/// retention charges follow the accommodation supply — CA to confirm).
/// </summary>
public static class GroupAttrition
{
    public static int FreeRooms(int contracted, int allowanceBps) => (int)((long)contracted * allowanceBps / MoneyMath.BpsScale);

    public static IReadOnlyList<AttritionLine> Lines(IReadOnlyCollection<AttritionInput> types, DateOnly arrival, DateOnly departure, int allowanceBps, int rateBps)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (allowanceBps is < 0 or > MoneyMath.BpsScale || rateBps is < 0 or > MoneyMath.BpsScale) throw HccException.Validation("Attrition terms are percentages between 0 and 100.");
        var lines = new List<AttritionLine>();
        foreach (var t in types.OrderBy(t => t.RoomType, StringComparer.Ordinal))
        {
            var unpicked = Math.Max(0, t.Allotted - t.Picked);
            var rooms = Math.Max(0, unpicked - FreeRooms(t.Allotted, allowanceBps));
            var per = MoneyMath.PercentOf(t.Rate, rateBps);
            if (rooms == 0 || per <= Paise.Zero) continue;
            for (var n = arrival; n < departure; n = n.AddDays(1)) lines.Add(new AttritionLine(t.RoomType, n, rooms, per, new Paise(checked(per.Value * rooms))));
        }

        return lines;
    }
}

/// <summary>Master folio figures: routed master shares + attrition lines − (payments + deposits − refunds). Transfers only move the receivable.</summary>
public sealed record MasterFigures(Paise Net, Paise Tax, Paise Charges, Paise Received, Paise Deposits, Paise Refunded, Paise Balance);

public static class GroupMaster
{
    public static MasterFigures Figures(Paise routedNet, Paise routedTax, Paise attritionNet, Paise attritionTax, Paise payments, Paise deposits, Paise refunds)
    {
        var net = routedNet + attritionNet;
        var tax = routedTax + attritionTax;
        var charges = net + tax;
        var received = payments + deposits - refunds;
        return new MasterFigures(net, tax, charges, received, deposits, refunds, charges - received);
    }
}

/// <summary>Estimates ported from the prototype group stream (<c>grpBookingExposure</c>, <c>grpStayValue</c>).</summary>
public static class GroupEstimate
{
    /// <summary>Statuses that still count (prototype <c>GRP_ACTIVE</c>: Held, Confirmed, In house, Departed).</summary>
    public static bool IsActive(string status) => status is "Held" or "Confirmed" or "InHouse" or "Departed";

    /// <summary>
    /// Estimated unpaid value of a stay: posted lines + unposted agreed nights (GST per night), less net payments (payments − refunds;
    /// company credit stays owed). Departed stays add no unposted nights. Never below zero.
    /// </summary>
    public static Paise Exposure(string status, IReadOnlyCollection<PostedLine> posted, IReadOnlyCollection<(DateOnly Night, Paise Rate, int Bps)> agreed, Paise netPaid)
    {
        ArgumentNullException.ThrowIfNull(posted);
        ArgumentNullException.ThrowIfNull(agreed);
        if (!IsActive(status)) return Paise.Zero;
        var net = posted.Aggregate(Paise.Zero, (a, l) => a + l.Amount);
        var tax = posted.Aggregate(Paise.Zero, (a, l) => a + l.Tax);
        if (status != "Departed")
        {
            var nights = posted.Where(l => l.Kind == ChargeKind.Room).Select(l => l.ServiceDate).ToHashSet();
            foreach (var (night, rate, bps) in agreed)
            {
                if (nights.Contains(night)) continue;
                net += rate;
                tax += GstMath.LineTax(rate, bps);
            }
        }

        var owed = net + tax - netPaid;
        return owed < Paise.Zero ? Paise.Zero : owed;
    }

    /// <summary>New stay at a block rate: nights × rate + GST per night (prototype <c>grpStayValue</c>).</summary>
    public static Paise StayValue(IReadOnlyCollection<(DateOnly Night, Paise Rate, int Bps)> nights)
    {
        ArgumentNullException.ThrowIfNull(nights);
        return nights.Aggregate(Paise.Zero, (a, n) => a + n.Rate + GstMath.LineTax(n.Rate, n.Bps));
    }
}

public static partial class GroupText
{
    [GeneratedRegex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z\d]Z[A-Z\d]$")]
    private static partial Regex GstinPattern();

    [GeneratedRegex(@"^\+?[0-9 ]{7,20}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>GSTIN shape (prototype <c>GRP_GSTIN</c>), upper-cased; empty → null. Format only — no checksum or portal lookup.</summary>
    public static string? Gstin(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(v)) return null;
        if (!GstinPattern().IsMatch(v)) throw HccException.Validation("Enter the GSTIN in the 15-character format, e.g. 27ZZFIC0009F1Z5.");
        return v;
    }

    public static string? Phone(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (!PhonePattern().IsMatch(v) || v.Count(char.IsAsciiDigit) is < 7 or > 16) throw HccException.Validation("Enter a valid phone number (7–16 digits).");
        return v;
    }

    public static string? Email(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > 120 || !EmailPattern().IsMatch(v)) throw HccException.Validation("Enter a valid e-mail address.");
        return v;
    }

    public static string Required(string? value, int max, string message)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw HccException.Validation(message);
        if (v.Length > max) throw HccException.Validation($"Keep it to {max} characters.");
        return v;
    }

    public static string? Optional(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw HccException.Validation($"Keep it to {max} characters.");
        return v;
    }

    public static int Bps(int value, string what)
    {
        if (value is < 0 or > MoneyMath.BpsScale) throw HccException.Validation($"{what} must be between 0 % and 100 %.");
        return value;
    }
}
