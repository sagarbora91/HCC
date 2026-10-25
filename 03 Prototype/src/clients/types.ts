// Typed feature clients (contracts §3, §6). Both adapters (mock over execute(), HTTP over /api/v1) implement these.
// Money = integer paise (safe integers); dates = YYYY-MM-DD strings; versions = opaque strings (server: base64 rowversion).

/** Why a room cannot take a stay. Wire codes per contracts §3 GetAvailability (+ Occupied = in-house guest, as the prototype shows it). */
export type AvailabilityReason='Blocked'|'OverCapacity'|'Occupied'|'Booked'|'Held'|'GroupHold'|'Restricted';
export const REASON_LABEL:Record<AvailabilityReason,string>={Blocked:'Blocked',OverCapacity:'Over capacity',Occupied:'Occupied',Booked:'Booked',Held:'Held',GroupHold:'Group hold',Restricted:'Restricted'};

export type AvailabilityQuery={arrival:string;departure:string;adults:number;children?:number;roomTypeId?:string;ratePlanId?:string};
export type AvailabilityRoom={roomId:string;roomTypeId:string;capacity:number;available:boolean;
  /** Empty when available; first entry is the main reason. */
  reasons:AvailabilityReason[];
  /** Optional human text for the main reason (e.g. "Booked · guest", "Held · 40 min left"). Clients fall back to REASON_LABEL. */
  reasonText?:string;
  /** Tariff for the arrival night. */
  nightlyRatePaise:number;
  /** Room has an in-house guest now (free on the searched dates). */
  occupiedNow?:boolean};
export type AvailabilityResult={arrival:string;departure:string;nights:number;party:number;
  /** False when the dates do not form a valid stay (no rooms are then available). */
  valid:boolean;rooms:AvailabilityRoom[]};

export type QuoteRequest={arrival:string;departure:string;adults:number;children?:number;roomId:string;ratePlanId?:string};
export type QuoteNight={date:string;ratePaise:number;taxBps:number;taxPaise:number};
export type QuoteViolation={code:string;message:string};
export type Quote={quoteId:string;version:string;roomId:string;roomTypeId:string;arrival:string;departure:string;adults:number;children:number;
  nights:QuoteNight[];netPaise:number;taxPaise:number;totalPaise:number;
  /** ISO-8601 UTC, or undefined when the quote does not expire (mock). */
  expiresAt?:string;violations:QuoteViolation[]};

export type GuestInput={name:string;phone?:string;email?:string};
export type CreateReservationRequest={quoteId:string;quoteVersion:string;roomId:string;guest:GuestInput;
  source:string;otaChannel?:string;company?:{name:string;gstin?:string};notes?:string;
  /** Save as a 2-hour hold instead of Confirmed. */
  hold?:boolean;
  /** Approver's reason for overriding a stay restriction (needs the approve grant). */
  restrictionOverride?:string};
export type CreateReservationResult={reservationId:string;publicNo:string;version:string};

export type BookingStatusDto='Held'|'Expired'|'Confirmed'|'In house'|'Departed'|'Cancelled'|'No show';
export type BookingDetails={reservationId:string;publicNo:string;version:string;status:BookingStatusDto;
  guest:GuestInput;roomId:string;roomTypeId:string;arrival:string;departure:string;adults:number;children:number;
  source:string;otaChannel?:string;company?:{name:string;gstin?:string};nightlyRatePaise:number;holdUntil?:string;
  /** Mock only: the server keeps registration on the stay side (P3 RegisterGuest), not on the reservation read. */
  registered?:boolean;
  /** Agreed price. Paid / deposit / balance come from the folio (GET /folios/{id}, billing) — the mock fills them, the server read does not. */
  totals:{netPaise:number;taxPaise:number;totalPaise:number;paidPaise?:number;depositPaise?:number;balancePaise?:number}};

/** Per command: the key is chosen once per user action and reused on every retry of it. */
export type CommandOptions={idempotencyKey:string;/** Record version the command was based on (STALE_VERSION on mismatch). */expectedVersion?:string};

export interface ReservationsClient{
  getAvailability(q:AvailabilityQuery):Promise<AvailabilityResult>;
  quoteStay(q:QuoteRequest,opts?:Partial<CommandOptions>):Promise<Quote>;
  createReservation(r:CreateReservationRequest,opts:CommandOptions):Promise<CreateReservationResult>;
  getBooking(reservationId:string):Promise<BookingDetails>;
  /** Synchronous reads — mock adapter only, so screens render without a loading flash. HTTP adapters leave this undefined. */
  readonly now?:{getAvailability(q:AvailabilityQuery):AvailabilityResult;getBooking(reservationId:string):BookingDetails|undefined};
}

export type HealthResult={status:string;database:string;schemaVersion:number;serverTimeUtc:string};
export interface SystemClient{health():Promise<HealthResult>}

export type Capabilities={userId:string;displayName:string;role?:string;
  /** Granted permissions (prototype: coarse permission names; server: PERM ids). */
  permissions:string[];businessDate?:string;
  /** Server only: role codes, the grant cells (mode A/L/R/V/M, scope P/S/A/O, limit) and field masks (RBAC §9). */
  roles?:string[];grants?:{permissionId:string;mode:string;scope:string;limit?:number|null;limitUnit?:string|null}[];fieldMasks?:Record<string,string>};
export interface MeClient{capabilities():Promise<Capabilities>}

export type HccClients={kind:'mock'|'http';reservations:ReservationsClient;system:SystemClient;me:MeClient};
