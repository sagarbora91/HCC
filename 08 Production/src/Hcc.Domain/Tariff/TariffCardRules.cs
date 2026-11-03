using System.Globalization;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Tariff;

/// <summary>F4-T1 refusal reasons (details.reason); the wire code is VALIDATION or INVALID_TRANSITION (F4-CONTRACT §1.5).</summary>
public static class TariffCardReasons
{
    public const string PastEffective = "TRC_PAST_EFFECTIVE";
    public const string PublishedReadOnly = "TRC_PUBLISHED_READONLY";
    public const string Conflict = "TRC_CONFLICT";
    public const string HallNotReady = "TRC_HALL_NOT_READY";
    public const string SlabAmbiguous = "TRC_SLAB_AMBIGUOUS";
    public const string Empty = "TRC_EMPTY";
    public const string DupLine = "TRC_DUP_LINE";
}

/// <summary>One card line (prototype TRCLine). Kind: room | occupancy | meal | extra | hall | day-use; DaySet: all | weekday | weekend.</summary>
public sealed record TrcLine(int LineNo, string Kind, string? RoomType, string? Plan, string? OccKind, string? MealPlan, string? Extra, string? Hall, string DaySet,
    long? RateExcl, int TaxBps, long? DisplayIncl, long? EnteredIncl, int? MinGuests = null, int? MaxGuests = null, int? AgeFrom = null, int? AgeTo = null, string? Note = null);

/// <summary>A tariff schedule row (inv.room_type_tariff): DaySet 'A' all, 'W' weekday, 'E' weekend; CardVersionId = the card version that wrote it.</summary>
public sealed record TrcTariffRow(string RoomType, DateOnly EffectiveFrom, long RatePaise, char DaySet, long? CardVersionId);

/// <summary>One master row a publish writes. Master: tariffs | occupancyCharges | mealPlans | extras. Key: what the store needs to write it.</summary>
public sealed record TrcWrite(string Master, string Ref, long? Before, long After, string Label);

public sealed record TrcPublishPlan(IReadOnlyList<TrcWrite> Writes, IReadOnlyList<string> PrintOnly, IReadOnlyList<string> Skipped);

/// <summary>
/// F4-T1 tariff card rules (prototype domain/streams/tariffcard.ts parity). The card is a versioned view + snapshot: publish writes the
/// existing masters dated effective-from (DEC-MA-102). Inclusive price = excl + CGST + SGST, each half rounded half away from zero (§1.4).
/// </summary>
public static class TariffCardRules
{
    public static readonly IReadOnlyList<int> WeekendDefault = [5, 6, 0];
    public const int PlanBasis = 2;
    private static readonly string[] Kinds = ["room", "occupancy", "meal", "extra", "hall", "day-use"];
    private static readonly string[] DaySets = ["all", "weekday", "weekend"];

    public static HccException Fail(string reason, string message)
    {
        var code = reason is TariffCardReasons.PastEffective or TariffCardReasons.SlabAmbiguous or TariffCardReasons.Empty or TariffCardReasons.DupLine
            ? HccErrorCode.VALIDATION : HccErrorCode.INVALID_TRANSITION;
        return new HccException(code, message, new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(reason) });
    }

    // ── rounding ──
    public static long Half(long excl, int bps)
    {
        var p = (decimal)excl * bps;
        var a = Math.Abs(p);
        var q = Math.Floor(a / 20000m);
        var t = (long)q + (a - (q * 20000m) >= 10000m ? 1 : 0);
        return p < 0 ? -t : t;
    }

    public static long Inclusive(long excl, int bps) => excl + (2 * Half(excl, bps));

    /// <summary>Integer exclusive paise whose inclusive price is nearest <paramref name="incl"/>; ties → the lower exclusive.</summary>
    public static long ExclusiveFromInclusive(long incl, int bps)
    {
        var guess = (long)Math.Floor((decimal)incl * 10000m / (10000 + bps));
        long best = guess, bd = long.MaxValue;
        for (var e = Math.Max(0, guess - 3); e <= guess + 3; e++)
        {
            var d = Math.Abs(Inclusive(e, bps) - incl);
            if (d < bd) { bd = d; best = e; }
        }

        return best;
    }

    /// <summary>
    /// Prices a line: <paramref name="bpsFor"/> gives the rate for an exclusive amount (rooms: the slab; others constant). An inclusive
    /// entry keeps the only self-consistent slab, else TRC_SLAB_AMBIGUOUS.
    /// </summary>
    public static TrcLine Price(TrcLine input, Func<long, int> bpsFor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(bpsFor);
        if (!Kinds.Contains(input.Kind)) throw HccException.Validation("Unknown line kind " + input.Kind + ".");
        if (!DaySets.Contains(input.DaySet)) throw HccException.Validation("Nights: all, weekday or weekend.");
        long? excl = input.RateExcl;
        if (input.EnteredIncl is { } incl)
        {
            if (incl <= 0) throw Fail(TariffCardReasons.Empty, "Enter a price greater than zero.");
            var ok = new[] { bpsFor(0), bpsFor(long.MaxValue / 20000) }.Distinct()
                .Select(b => (b, e: ExclusiveFromInclusive(incl, b))).Where(x => bpsFor(x.e) == x.b).ToList();
            if (ok.Count != 1) throw Fail(TariffCardReasons.SlabAmbiguous, $"Line {input.LineNo}: the inclusive price sits on the GST slab boundary — enter the price before GST.");
            excl = ok[0].e;
        }
        else if (excl is < 0 or > 100_000_000) throw Fail(TariffCardReasons.Empty, $"Line {input.LineNo}: price out of range.");

        var bps = bpsFor(excl ?? 0);
        return input with { RateExcl = excl, TaxBps = bps, DisplayIncl = excl is { } x ? Inclusive(x, bps) : null };
    }

    // ── draft checks ──
    public static string LineKey(TrcLine l) => string.Join('|', l.Kind, l.RoomType ?? "", l.Kind == "room" ? (string.IsNullOrEmpty(l.Plan) ? "EP" : l.Plan) : "",
        l.OccKind ?? "", l.MealPlan ?? "", l.Extra ?? "", l.Hall ?? "", l.DaySet);

    public static void CheckDraft(DateOnly effectiveFrom, DateOnly businessDate, IReadOnlyList<int> weekendDays, IReadOnlyList<TrcLine> lines)
    {
        ArgumentNullException.ThrowIfNull(weekendDays);
        ArgumentNullException.ThrowIfNull(lines);
        if (effectiveFrom < businessDate)
            throw Fail(TariffCardReasons.PastEffective, $"The card cannot take effect before the business date {businessDate:yyyy-MM-dd}; bookings may already use the old prices.");
        if (weekendDays.Any(d => d is < 0 or > 6) || weekendDays.Distinct().Count() != weekendDays.Count || weekendDays.Count >= 7)
            throw HccException.Validation("Weekend nights: pick up to six days of the week.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in lines)
            if (!seen.Add(LineKey(l))) throw Fail(TariffCardReasons.DupLine, $"Line {l.LineNo} ({l.Kind} {l.RoomType} {l.DaySet}) is on the card twice.");
    }

    public static char DaySetCode(string daySet) => daySet switch { "weekday" => 'W', "weekend" => 'E', _ => 'A' };

    public static string WeekendText(IEnumerable<int> days) => string.Join(',', days.Select(d => d.ToString(CultureInfo.InvariantCulture)));

    public static IReadOnlyList<int> ParseWeekend(string? text) => string.IsNullOrWhiteSpace(text)
        ? WeekendDefault
        : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();

    // ── rate resolution (§1.3) ──
    public static char NightKind(DateOnly night, IReadOnlyCollection<int> weekendDays) => weekendDays.Contains((int)night.DayOfWeek) ? 'E' : 'W';

    /// <summary>Latest row with effective-from ≤ night whose day set matches the night (a specific row beats 'A' on the same date).</summary>
    public static TrcTariffRow? Resolve(IEnumerable<TrcTariffRow> rows, DateOnly night, Func<long?, IReadOnlyCollection<int>> weekendOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(weekendOf);
        TrcTariffRow? best = null;
        foreach (var t in rows)
        {
            if (t.EffectiveFrom > night) continue;
            if (t.DaySet != 'A' && NightKind(night, weekendOf(t.CardVersionId)) != t.DaySet) continue;
            if (best is null || t.EffectiveFrom > best.EffectiveFrom || (t.EffectiveFrom == best.EffectiveFrom && (best.DaySet == 'A' || t.DaySet != 'A'))) best = t;
        }

        return best;
    }

    // ── publish plan (§1.3) ──
    /// <summary>
    /// Writes of a publish. <paramref name="tariffs"/> = the schedule rows of the card's date; <paramref name="cardRefs"/> = refs earlier card
    /// versions wrote ("master|ref"); <paramref name="moneyOn"/> (key "occupancyCharges|TYPE|Adult", "mealPlans|CODE", "extras|CODE") gives
    /// the amount in force on the date and whether a row on exactly that date exists; null = the master row does not exist.
    /// </summary>
    public static TrcPublishPlan Plan(IReadOnlyList<TrcLine> lines, DateOnly from, IReadOnlyList<TrcTariffRow> tariffs, IReadOnlySet<string> cardRefs,
        Func<string, (long? Now, long? OnDate)?> moneyOn)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(tariffs);
        ArgumentNullException.ThrowIfNull(cardRefs);
        ArgumentNullException.ThrowIfNull(moneyOn);
        if (lines.Any(l => l.Kind == "hall")) throw Fail(TariffCardReasons.HallNotReady, "Hall prices publish after hall booking (wave F6). Remove the hall lines.");
        var writes = new List<TrcWrite>();
        var printOnly = new List<string>();
        var skipped = new List<string>();
        var day = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static bool Ep(TrcLine l) => string.IsNullOrEmpty(l.Plan) || l.Plan == "EP";
        foreach (var l in lines.Where(l => l.Kind == "room" && Ep(l)))
        {
            if (l.RateExcl is not { } rate) { skipped.Add($"line {l.LineNo}: blank"); continue; }
            var ds = DaySetCode(l.DaySet);
            var refId = $"{l.RoomType}|{day}|{l.DaySet}";
            var same = tariffs.FirstOrDefault(t => t.RoomType == l.RoomType && t.EffectiveFrom == from && t.DaySet == ds);
            if (same is { CardVersionId: null } && !cardRefs.Contains("tariffs|" + refId))
                throw Fail(TariffCardReasons.Conflict, $"A hand-entered {l.RoomType} tariff from {day} ({l.DaySet}) already exists — withdraw it or pick another date.");
            writes.Add(new TrcWrite("tariffs", refId, same?.RatePaise, rate, $"{l.RoomType} · {l.DaySet} from {day}"));
        }

        void Money(string master, string key, string field, long amount, string label)
        {
            var refId = $"{key}|{field}|{day}";
            var m = moneyOn($"{master}|{key}|{field}");
            if (m is null) { printOnly.Add(label + ": not in the master"); return; }
            var (now, onDate) = m.Value;
            if (onDate is null && now == amount) return;
            if (onDate == amount) return;
            if (onDate is not null && !cardRefs.Contains(master + "|" + refId)) throw Fail(TariffCardReasons.Conflict, $"{label}: a hand-entered amount from {day} already exists.");
            writes.Add(new TrcWrite(master, refId, now, amount, label));
        }

        foreach (var l in lines.Where(l => l.Kind == "occupancy"))
        {
            if (l.RateExcl is not { } a) { skipped.Add($"line {l.LineNo}: blank (publishes nothing)"); continue; }
            var field = l.OccKind switch { "child" => "Child", "bed" => "Bed", _ => "Adult" };
            Money("occupancyCharges", l.RoomType ?? "", field, a, $"{l.RoomType} extra {field.ToLowerInvariant()}");
        }

        var addOns = new Dictionary<string, (long Amount, int Line)>(StringComparer.Ordinal);
        foreach (var l in lines.Where(l => l.Kind == "room" && !Ep(l) && l.RateExcl is not null))
        {
            var ep = lines.FirstOrDefault(x => x.Kind == "room" && Ep(x) && x.RoomType == l.RoomType && x.DaySet == l.DaySet);
            if (ep?.RateExcl is not { } epRate) { printOnly.Add($"line {l.LineNo}: no EP line"); continue; }
            var amt = (long)Math.Round((decimal)(l.RateExcl!.Value - epRate) / (l.MinGuests ?? PlanBasis), MidpointRounding.AwayFromZero);
            if (amt < 0) throw Fail(TariffCardReasons.Conflict, $"Line {l.LineNo} is below the EP price.");
            if (addOns.TryGetValue(l.Plan!, out var prev) && prev.Amount != amt)
                throw Fail(TariffCardReasons.Conflict, $"Meal plan {l.Plan} add-on differs between line {prev.Line} and line {l.LineNo}; the master holds one add-on per plan.");
            addOns[l.Plan!] = (amt, l.LineNo);
        }

        foreach (var l in lines.Where(l => l.Kind == "meal" && l.RateExcl is not null)) addOns[l.MealPlan ?? ""] = (l.RateExcl!.Value, l.LineNo);
        foreach (var (code, a) in addOns) Money("mealPlans", code, "AddOn", a.Amount, $"Meal plan {code} add-on per guest");
        foreach (var l in lines.Where(l => l.Kind == "extra"))
        {
            if (l.RateExcl is not { } a) { skipped.Add($"line {l.LineNo}: blank"); continue; }
            Money("extras", l.Extra ?? "", "Price", a, $"Extra {l.Extra}");
        }

        foreach (var l in lines.Where(l => l.Kind == "day-use")) printOnly.Add($"line {l.LineNo}: day use prints only");
        if (writes.Count == 0) throw Fail(TariffCardReasons.Empty, "Nothing to publish: every price on this card is blank or already in the masters.");
        return new TrcPublishPlan(writes, printOnly, skipped);
    }
}
