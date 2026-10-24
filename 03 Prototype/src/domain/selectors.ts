// Pure read functions over State. Every figure on screen is derived here (REQ-RPT-003).
import {Booking,State,Permission,Room} from './types';
import {addDays} from './money';
import {adminTariffOn,adminTaxScheduled} from './streams/admin';

// ── Cross-stream contract functions (stubs in M1; owners noted). Signatures must not change. ──
/** Nightly tariff for a room on a date. Owner: stream E (effective-dated tariffs). */
export function rateFor(s:State,room:Room,date:string):number{return adminTariffOn(s,room.type,date)?.rate??room.rate;}
/** Synthetic tax rate (basis points) effective on a date. Owner: stream E (effective-dated tax). */
export function taxRateFor(s:State,date:string):number{return adminTaxScheduled(s,date);}
/** Whether a room is blocked for any night in [a, d). Owner: stream D (dated blocks).
 *  A block covers nights [blockFrom, blockTo). Missing blockFrom = from now (no lower bound); missing blockTo = open-ended.
 *  `blocked` with no dates (base room 308, M1 toggles) blocks every interval. `blocked:false` never blocks. */
export function blockedDuring(room:Room,a:string,d:string):boolean{
  if(!room.blocked)return false;
  if(!room.blockFrom&&!room.blockTo)return true;
  return (!room.blockTo||a<room.blockTo)&&(!room.blockFrom||d>room.blockFrom);
}

export function totals(b:Booking){
  const net=b.lines.reduce((s,l)=>s+l.amount,0);
  const tax=b.lines.reduce((s,l)=>s+Math.round(l.amount*l.taxBps/10000),0);
  const paid=b.receipts.reduce((s,r)=>s+(r.kind==='payment'?r.amount:r.kind==='refund'?-r.amount:0),0);
  const deposit=b.receipts.reduce((s,r)=>s+(r.kind==='deposit'?r.amount:r.kind==='deposit-return'?-r.amount:0),0);
  // Company credit moves the balance to a receivable (Financial Rulebook §2); it is not a cash/net receipt.
  const credit=b.receipts.reduce((s,r)=>s+(r.kind==='company-credit'?r.amount:0),0);
  return {net,tax,total:net+tax,paid,deposit,balance:net+tax-paid-credit};
}
/** Coarse module gate. Disabled users and users whose assignment expired (expiresOn < business date) are denied. Owner: stream E. */
/** Permission check. An array means any-of (e.g. a page several roles reach through different permissions). */
export function can(s:State,uid:string,p:Permission|readonly Permission[]){const u=s.users.find(x=>x.id===uid);if(!u?.enabled||(u.expiresOn&&s.date>u.expiresOn))return false;const g=s.grants[u.role]||[];return Array.isArray(p)?p.some(x=>g.includes(x)):g.includes(p as Permission);}
/** Owner: stream A. Room free for nights [a, d): not blocked (blockedDuring), and no overlapping Confirmed / In house
 *  booking or Held booking whose hold is still running (s.clock < holdUntil; a Held booking without holdUntil never lapses). */
/** Availability ignoring soft holds (group blocks): out-of-service blocks and live bookings only. */
export function availableRaw(s:State,room:string,a:string,d:string,except=''){
  const r=s.rooms.find(r=>r.id===room);
  const holds=(b:Booking)=>b.status==='Confirmed'||b.status==='In house'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil));
  return !!r&&!blockedDuring(r,a,d)&&!s.bookings.some(b=>b.id!==except&&b.room===room&&holds(b)&&a<b.departure&&d>b.arrival);
}
// ── Room holds (soft allocations such as group blocks, module 14). Providers register at module load. ──
export type RoomHold={id:string;label:string};
type RoomHoldProvider=(s:State,room:string,a:string,d:string,exempt:string)=>RoomHold|undefined;
const roomHoldProviders:RoomHoldProvider[]=[];
let holdExempt='';
export function registerRoomHold(fn:RoomHoldProvider){if(!roomHoldProviders.includes(fn))roomHoldProviders.push(fn);}
/** The hold (if any) on a room for any night in [a, d). */
export function roomHold(s:State,room:string,a:string,d:string):RoomHold|undefined{for(const f of roomHoldProviders){const h=f(s,room,a,d,holdExempt);if(h)return h;}return undefined;}
/** Run fn with one hold owner exempted (e.g. a group picking up its own held rooms). */
export function withHoldExempt<T>(id:string,fn:()=>T):T{const prev=holdExempt;holdExempt=id;try{return fn();}finally{holdExempt=prev;}}
/** Bookable: free of blocks, live bookings AND holds (group rooms are shown blocked to everyone else). */
export function available(s:State,room:string,a:string,d:string,except=''){return availableRaw(s,room,a,d,except)&&!roomHold(s,room,a,d);}
export function occupant(s:State,room:string){return s.bookings.find(b=>b.room===room&&b.status==='In house');}
/** A reversed expense still counts on its own date; its negative reversal record offsets it on the reversal date (stream F, BL-029). */
/** Expected drawer cash = opening float + cash receipts/deposits − cash refunds/returns + restaurant / room-service cash (net of same-day voids)
 *  − posted cash expenses (Financial Rulebook §4; shared till, DEC-052). */
export function drawer(s:State){
  const receipts=s.bookings.flatMap(b=>b.receipts).filter(r=>r.date===s.date&&r.method==='Cash').reduce((a,r)=>a+(['payment','deposit'].includes(r.kind)?r.amount:-r.amount),0);
  const outlet=drawerOutletCash(s);
  const expenses=s.expenses.filter(e=>e.date===s.date&&(e.status==='Posted'||e.status==='Reversed')&&e.method==='Cash').reduce((a,e)=>a+e.amount,0);
  return s.openingCash+receipts+outlet-expenses;
}
/** POS cash settled today (sale receipts +, void receipts −). Room-service bills charged to a folio have no POS receipt. */
export function drawerOutletCash(s:State){return (s.posX?.receipts??[]).filter(r=>r.date===s.date&&r.method==='Cash').reduce((a,r)=>a+r.amount,0);}
export function metrics(s:State){
  const active=s.bookings.filter(b=>b.status==='In house'),sellable=s.rooms.filter(r=>!blockedTonight(s,r)).length;
  const lines=s.bookings.flatMap(b=>b.lines).filter(l=>l.date===s.date),receipts=s.bookings.flatMap(b=>b.receipts).filter(r=>r.date===s.date);
  return {
    occupied:active.length,
    available:s.rooms.filter(r=>!blockedTonight(s,r)&&!occupant(s,r.id)&&r.hk==='Ready').length,
    sellable,
    occupancy:sellable?Math.round(active.length/sellable*100):0,
    revenue:lines.reduce((a,l)=>a+l.amount,0),
    tax:lines.reduce((a,l)=>a+Math.round(l.amount*l.taxBps/10000),0),
    collections:receipts.reduce((a,r)=>a+(r.kind==='payment'?r.amount:r.kind==='refund'?-r.amount:0),0),
    deposits:s.bookings.reduce((a,b)=>a+totals(b).deposit,0),
    outstanding:s.bookings.reduce((a,b)=>a+Math.max(0,totals(b).balance),0),
    expenses:s.expenses.filter(e=>(e.status==='Posted'||e.status==='Reversed')&&e.date===s.date).reduce((a,e)=>a+e.amount,0),
  };
}
export type RoomStatus='Blocked'|'Occupied'|'Ready'|'Inspection'|'Dirty'|'Cleaning';
/** Out of service for the night of the business date (a later dated block does not count yet). */
export function blockedTonight(s:State,r:Room){return blockedDuring(r,s.date,addDays(s.date,1));}
/** Group/soft hold on the room for tonight (shown as Blocked on the room board). */
export function holdTonight(s:State,r:Room){return roomHold(s,r.id,s.date,addDays(s.date,1));}
export function roomStatus(s:State,r:Room):RoomStatus{return blockedTonight(s,r)||(!occupant(s,r.id)&&holdTonight(s,r))?'Blocked':occupant(s,r.id)?'Occupied':r.hk;}
export const isDayClosed=(s:State)=>s.closedDates.includes(s.date);
