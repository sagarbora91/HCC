namespace Hcc.Domain.Billing;

/// <summary>F8 split folio (DEC-152, SF-CONTRACT §3): the two routing categories (the groups module's own vocabulary, migration 2200).</summary>
public enum FolioRouteCategory
{
    /// <summary>Room nights, the meal-plan line posted with a night, discounts.</summary>
    Room,
    /// <summary>Every other charge (services, laundry, food posted to the room, LCO / UPG / attrition / no-show charges).</summary>
    Extras,
}

/// <summary>One folio of a reservation (<c>bill.folio</c> row, migration 4350). Seq 1 is the primary guest folio (letter A).</summary>
public sealed record FolioRef(long FolioId, int Seq, string PayerKind, long? CompanyId, string? CustomerKey, string? PayerName, string? PayerGstin, bool Removed)
{
    /// <summary>The folio letter: seq 1 → A … seq 4 → D.</summary>
    public string Letter => FolioRouting.Letter(Seq);
}

/// <summary>A reservation's folios and its effective routes (highest <c>bill.folio_route.seq</c> per category → folio id).</summary>
public sealed record FolioRoutingSnapshot(IReadOnlyList<FolioRef> Folios, IReadOnlyDictionary<FolioRouteCategory, long> Routes)
{
    /// <summary>The primary folio (seq 1).</summary>
    public FolioRef Primary => Folios.First(f => f.Seq == 1);

    /// <summary>Live (not removed) folios in seq order.</summary>
    public IEnumerable<FolioRef> Live => Folios.Where(f => !f.Removed).OrderBy(f => f.Seq);

    /// <summary>True when more than one live folio exists (a single-folio stay takes today's path, invariant I1).</summary>
    public bool IsSplit => Live.Count() > 1;
}

/// <summary>Pure routing of a new charge to a folio (SF-CONTRACT §3). Nothing here posts or changes money.</summary>
public static class FolioRouting
{
    public const int PrimarySeq = 1;
    public const int MaxFolios = 4;

    public static string Letter(int seq) => seq is >= 1 and <= MaxFolios ? ((char)('A' + seq - 1)).ToString() : throw new ArgumentOutOfRangeException(nameof(seq));

    public static int SeqOf(string letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return letter.Length == 1 && letter[0] is >= 'A' and <= 'D' ? letter[0] - 'A' + 1 : throw new ArgumentOutOfRangeException(nameof(letter));
    }

    public static string Code(FolioRouteCategory c) => c == FolioRouteCategory.Room ? "room" : "extras";

    public static FolioRouteCategory Parse(string code) => code switch
    {
        "room" => FolioRouteCategory.Room,
        "extras" => FolioRouteCategory.Extras,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Routing category must be room or extras."),
    };

    /// <summary>Room nights, discounts and a meal-plan line → room; any other charge → extras. A correction is never routed by category
    /// (it follows the line it reverses, <see cref="Resolve"/>); for completeness it maps to extras.</summary>
    public static FolioRouteCategory CategoryOf(ChargeKind kind, bool mealPlan)
        => kind is ChargeKind.Room or ChargeKind.Discount || mealPlan ? FolioRouteCategory.Room : FolioRouteCategory.Extras;

    /// <summary>
    /// The folio a new charge lands on: an explicit folio wins (it must be live); then the reversed original's folio (I4: reversals, rate
    /// re-posts, credit-note lines sit on the folio of the line they reverse); a correction without a reversed line goes to the primary;
    /// then the effective route of the category (if that folio is live); then seq 1.
    /// </summary>
    public static long Resolve(FolioRoutingSnapshot snap, ChargeKind kind, bool mealPlan, long? reversesFolioId, long? explicitFolioId)
    {
        ArgumentNullException.ThrowIfNull(snap);
        var primary = snap.Primary.FolioId;
        if (explicitFolioId is { } e)
        {
            var f = snap.Folios.FirstOrDefault(x => x.FolioId == e);
            if (f is null || f.Removed) throw new ArgumentException($"Folio {e} is not a live folio of this reservation.", nameof(explicitFolioId));
            return e;
        }
        if (reversesFolioId is { } r) return r;
        if (kind == ChargeKind.Correction) return primary;
        if (snap.Routes.TryGetValue(CategoryOf(kind, mealPlan), out var routed) && snap.Folios.Any(x => x.FolioId == routed && !x.Removed)) return routed;
        return primary;
    }
}
