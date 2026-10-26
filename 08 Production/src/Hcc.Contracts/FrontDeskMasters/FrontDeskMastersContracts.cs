namespace Hcc.Contracts.FrontDeskMasters;

// Wire contracts of the FrontDeskMasters module (MA-5: plan Part 2 §8 M9, M11, M12, M14). Four lists on the masters framework of MA-0
// (errors: VALIDATION / INVALID_TRANSITION with details.reason = Hcc.Contracts.Masters.MasterReasons). Every row carries RowId; until a
// property's list is first changed the server answers the built-in defaults with RowId −1, −2, … and an empty Version — a command on
// such a row (or on the list) stores the defaults first. Prototype: 03 Prototype/src/domain/streams/frontdesk-master.ts, sync layer
// 03 Prototype/src/clients/sync/frontdesk-master.ts.

/// <summary>List ids (mst.master_history.master; the prototype's master spec ids).</summary>
public static class FrontDeskMasterIds
{
    public const string BookingSources = "bookingSources";
    public const string IdTypes = "idTypes";
    public const string NumberSeries = "numberSeries";
    public const string Policies = "policies";

    public static readonly IReadOnlyList<string> All = [BookingSources, IdTypes, NumberSeries, Policies];
}

/// <summary>A page of one list.</summary>
public sealed record FrontDeskList<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

/// <summary>M9 booking source. Segment DIRECT | OTA | CORPORATE | AGENT | GROUP | OTHER; Channel = the OTA channel or company it stands
/// for; CommissionBps = % of room charges (null for readers without money visibility). Usage = reservations with this source + policies
/// naming it.</summary>
public sealed record BookingSourceView(int RowId, string Code, string Name, string Segment, string? Channel, int? CommissionBps, int SortOrder, bool Active,
    string? RetiredOn, int Usage, string UpdatedUtc, string Version);

/// <summary>M11 guest ID type. Accepted = offered on new registrations; the number check stays in the guests module (first word of the
/// name). Usage = guest profiles with this type.</summary>
public sealed record IdTypeView(int RowId, string Code, string Name, bool Accepted, bool NeedsPhoto, bool ForeignerOnly, int SortOrder, bool Active,
    string? RetiredOn, int Usage, string UpdatedUtc, string Version);

/// <summary>M12 numbering series. Document INV | BOS | CRN | RCP | KOT | REG; Reset FY | NEVER; ActiveFrom yyyy-MM-dd. Issued = documents
/// numbered under it (invoices / credit notes dated from ActiveFrom until the next series of the document); NextNumber = what the next
/// document gets today (null when another series is in force).</summary>
public sealed record NumberSeriesView(int RowId, string Code, string Name, string Document, string Prefix, int StartNo, int Padding, string Reset, string ActiveFrom,
    int SortOrder, bool Active, string? RetiredOn, int Issued, string? NextNumber, int Usage, string UpdatedUtc, string Version);

/// <summary>M14 booking policy. Percentages in basis points; Sources = booking-source codes ([] = the default policy).</summary>
public sealed record BookingPolicyView(int RowId, string Code, string Name, int FreeCancelHours, int CancelChargeBps, int NoShowChargeBps, int DepositBps,
    IReadOnlyList<string> Sources, int SortOrder, bool Active, string? RetiredOn, int Usage, string UpdatedUtc, string Version);

/// <summary>SaveBookingSource K [V]: RowId null = create. A commission change needs PERM-MST-002 (Owner).</summary>
public sealed record SaveBookingSourceRequest(int? RowId, string Code, string Name, string Segment, string? Channel = null, int? CommissionBps = null,
    int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveIdType K [V].</summary>
public sealed record SaveIdTypeRequest(int? RowId, string Code, string Name, bool Accepted = true, bool NeedsPhoto = false, bool ForeignerOnly = false,
    int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveNumberSeries K [V] (Owner): a series that has issued documents keeps document, prefix, start, digits, reset and date.</summary>
public sealed record SaveNumberSeriesRequest(int? RowId, string Code, string Name, string Document, string Prefix, int StartNo, int Padding, string Reset,
    string ActiveFrom, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveBookingPolicy K [V] (Owner).</summary>
public sealed record SaveBookingPolicyRequest(int? RowId, string Code, string Name, int FreeCancelHours, int CancelChargeBps = 0, int NoShowChargeBps = 0,
    int DepositBps = 0, IReadOnlyList<string>? Sources = null, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>Retire / Reactivate / Delete one row K V (an empty version for a default row, RowId &lt; 0).</summary>
public sealed record FrontDeskRowRequest(int RowId, string? ExpectedVersion = null);

/// <summary>Reorder K: every row id of the list once, in the new order (default rows by their negative ids).</summary>
public sealed record FrontDeskReorderRequest(IReadOnlyList<int> RowIds);

/// <summary>Result of Save / Retire / Reactivate: the stored row id, code and new version.</summary>
public sealed record FrontDeskRowResult(string Master, int RowId, string Code, string Version);

public sealed record FrontDeskDeleteResult(string Master, int RowId, string Code, bool Deleted);

public sealed record FrontDeskReorderResult(string Master, int Rows);
