// LG-UI · production client mapping of the legacy part of GET /customers/profile (contract LG-HISTORY-CONTRACT §9).
//   server  CustomerProfile.LegacyStays: LegacyStayView[] (newest first), LegacyLifetimePaise (null when money is masked for the caller);
//           CustomerSummary.LegacyStays (count) and FirstStay (earlier of legacy and live).
//   client  LegacyStay (domain/streams/customers.ts) — Customers 360 renders it with a "Legacy" badge and no folio link.
// Not a SyncArea: no State slice holds legacy rows. features/customers/legacy-source.ts remembers them per customer key.
import type {LegacyStay} from '../../domain/streams/customers';

/** Server wire shape (08 Production/src/Hcc.Contracts/Customers/CustomerContracts.cs, camelCase JSON). */
export type SrvLegacyStay={stayId:number;legacyFy:string;legacyBookingId:number;legacyBillNo:number|null;arrival:string;departure:string|null;nights:number;room:string;roomCount:number;status:string;netPaise:number|null};
export type SrvLegacyProfilePart={legacyStays?:SrvLegacyStay[]|null;legacyLifetimePaise?:number|null};

const day=(v:string)=>String(v).slice(0,10);
export function legacyStayFromServer(v:SrvLegacyStay):LegacyStay{
  const out:LegacyStay={fy:v.legacyFy,bookingId:v.legacyBookingId,arrival:day(v.arrival),nights:v.nights,room:v.room,roomCount:v.roomCount,status:v.status};
  if(v.legacyBillNo!==null&&v.legacyBillNo!==undefined)out.billNo=v.legacyBillNo;
  if(v.departure)out.departure=day(v.departure);
  if(v.netPaise!==null&&v.netPaise!==undefined)out.net=v.netPaise;
  return out;
}
/** `LegacyStays` of a profile read → prototype shape, newest arrival first. Missing / null list = no legacy history. */
export function legacyStaysFromProfile(p:SrvLegacyProfilePart|null|undefined):LegacyStay[]{
  return (p?.legacyStays??[]).map(legacyStayFromServer).sort((a,b)=>b.arrival.localeCompare(a.arrival)||b.bookingId-a.bookingId);
}
