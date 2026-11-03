// F7-R3 · helpers shared by the guests + rooms reports (pure; read-only over State). The ID number never leaves these helpers in full:
// reports hand the engine the last four characters only, and the engine masks them again (`XXXX-XXXX-1234`, every format).
import type {State,Booking} from '../../types';
import {guestById,type Guest} from '../../streams/guests';
import {HccError} from '../../errors';
import {addDays,nights} from '../../money';
import {rptHolds} from '../mask';

export const userLabel=(s:State,id?:string)=>id?(s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id):'';
export const guestOfBooking=(s:State,b:Booking):Guest|undefined=>guestById(s,b.guestId);
export const guestNameOf=(s:State,b:Booking)=>guestOfBooking(s,b)?.name??b.guest;
export const bookingNo=(b:Booking)=>b.publicNo??b.id;
export const paxOf=(b:Booking)=>(b.people||0)+(b.children??0);
/** Check-in instant of a stay: the recorded one, else arrival + the property check-in time (old bookings, LF A6). Empty before check-in. */
export const checkInAtOf=(s:State,b:Booking)=>b.status==='In house'||b.status==='Departed'?(b.checkedInAt??`${b.arrival}T${s.settings.checkin}`).replace('T',' ').slice(0,16):'';
/** Check-out instant: the prototype records no instant, so a departed stay shows the scheduled time on its departure date (server: the recorded instant). */
export const checkOutAtOf=(s:State,b:Booking)=>b.status==='Departed'?`${b.departure} ${s.settings.checkout}`:'';
/** The last four characters of the guest's ID number — all a report ever receives; the engine masks it to `XXXX-XXXX-1234`. */
export const idLast4Of=(g?:Pick<Guest,'idNumber'|'idLast4'>)=>g?(g.idLast4||String(g.idNumber??'').slice(-4)):'';
/** "City" of an address: the segment before the state (`10 Demo Street, Bengaluru, Karnataka (fictional)` → `Bengaluru`); no city field exists yet. */
export function cityOfAddress(address?:string):string{
  const parts=String(address??'').replace(/\([^)]*\)/g,'').split(',').map(x=>x.trim()).filter(Boolean);
  return parts.length>=3?parts[parts.length-2]:parts.length===2?parts[0]:'';
}
/** Selector-side range guard (the engine's class limits are wider than these entries'). */
export function rangeGuard(from:string|undefined,to:string|undefined,maxDays:number,today:string,lookAhead=0){
  if(from&&to&&nights(from,to)+1>maxDays)throw new HccError('VALIDATION',`The range is longer than ${maxDays} days. Narrow the range.`);
  if(to&&to>addDays(today,lookAhead))throw new HccError('VALIDATION',lookAhead?`This report can look at most ${lookAhead} days ahead of the business date (${today}).`:`The report cannot run for a date after the business date (${today}).`);
}
/** Guest names on room status: only those holding PERM-RES-001 (Owner, Manager, Reception — catalogue entry). */
export const rptNamesVisible=(s:State,uid:string)=>rptHolds(s,uid,'PERM-RES-001');
