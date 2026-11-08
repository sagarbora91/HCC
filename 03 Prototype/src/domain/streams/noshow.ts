// F8-QW-NS · Due no-shows (QWQ-7 default: the system never marks by itself; after fd.noShowTime the desk sees the list and marks each booking).
// Pure. The no-show charge of the booking's policy is SHOWN, never posted (DEC-MA-5b): marking goes through CANCEL {noShow:true, reason}.
import type {State,Booking} from '../types';
import {money} from '../money';
import {totals} from '../selectors';
import {cfgValueOn} from './config';
import {fdmPolicyFor,fdmNoShowCharge} from './frontdesk-master';

/** The cut-off as HH:mm (fd.noShowTime, default 23:00). */
export function nsCutoff(s:State):string{const v=cfgValueOn(s,'fd.noShowTime');return typeof v==='string'&&/^\d{2}:\d{2}$/.test(v)?v:'23:00';}
/** True from the cut-off minute onward on the business date (the simulated clock, s.clock = YYYY-MM-DDTHH:mm). */
export function nsPastCutoff(s:State):boolean{return String(s.clock).slice(0,10)>s.date||(String(s.clock).slice(0,10)===s.date&&String(s.clock).slice(11,16)>=nsCutoff(s));}
/** Policy line shown with the mark: "Policy <name>: no-show charge ₹… — not posted". */
export function nsChargeText(s:State,b:Booking):string{
  const p=fdmPolicyFor(s,b);
  return `Policy ${p?p.name:'none'}: no-show charge ${money(fdmNoShowCharge(s,b))} — not posted`;
}
export type NSRow={booking:Booking;charge:number;text:string;/** An advance or deposit is still held: CANCEL refuses until it is refunded. */blocked:string|undefined};
/** Confirmed arrivals of the business date that are not checked in, once the cut-off has passed. Empty before the cut-off. */
export function nsDue(s:State):NSRow[]{
  if(!nsPastCutoff(s))return [];
  return s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival===s.date)
    .sort((a,b)=>a.id<b.id?-1:1)
    .map(b=>{
      const t=totals(b);
      return {booking:b,charge:fdmNoShowCharge(s,b),text:nsChargeText(s,b),blocked:(t.deposit||t.paid!==0)?'An advance is held: refund it before marking no-show.':undefined};
    });
}
/** The reason CANCEL stores: the desk's words plus the shown policy charge (idempotent). */
export function nsReason(s:State,b:Booking,reason:string):string{
  const t=nsChargeText(s,b),r=String(reason??'').trim().slice(0,120);
  return r.includes('— not posted')?r:(r?r+' · ':'')+t;
}
