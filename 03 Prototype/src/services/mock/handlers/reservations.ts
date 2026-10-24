// Stream A · Calendar & reservations (owns BOOK, CANCEL, CONFIRM/hold commands).
// Register new or replacement command handlers here. A handler with the same name as a built-in command replaces it.
//   BOOK          (replaces built-in) — adds hold:true, phone, children, company, otaChannel; capacity check. Old minimal shape still accepted.
//   CONFIRM       (new) Held → Confirmed while the hold is running.
//   CANCEL        (replaces built-in) — also cancels Held bookings; optional reason.
//   CHANGE_DATES  (new) non-drag "Move / change dates" for Held/Confirmed reservations: room + arrival + departure, full-interval revalidation.
// Intended PERM IDs (RBAC matrix): BOOK/CONFIRM PERM-RES-002, CHANGE_DATES PERM-RES-003, CANCEL PERM-RES-004 — coarse 'reservations' for now.
import type {Handler,Kit} from '../commands';
import type {Booking} from '../../../domain/types';
import {available,rateFor,totals} from '../../../domain/selectors';
import {nights,addMinutes} from '../../../domain/money';
import {resCapacity,resHoldActive,resSweepHolds,RES_HOLD_MINUTES,RES_GSTIN,RES_PHONE} from '../../../domain/streams/reservations';
import {yldStayViolations} from '../../../domain/streams/yield';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
/** Shared stay validation for BOOK and CHANGE_DATES. Message texts match the M1 BOOK command. */
function stay({s,assert}:Kit,arrival:string,departure:string){
  assert(DATE.test(arrival)&&DATE.test(departure)&&nights(arrival,departure)>0&&nights(arrival,departure)<=30,'Departure must follow arrival (maximum 30 nights in this demo).');
  assert(arrival>=s.date,'Arrival cannot be before the current business date.');
}
function capacity({assert}:Kit,room:{id:string;type:string},people:number,children:number){
  const max=resCapacity(room as never);
  assert(people+children<=max,`Room ${room.id} (${room.type}) accommodates at most ${max} guests including children.`);
}
/** Yield stay restrictions (A2.1). A user with the approve permission may override with cmd.restrictionOverride (reason);
 *  the override is written to the booking notes and the audit detail. Returns the audit suffix. */
function restrictions(k:Kit,type:string,arrival:string,departure:string){
  const v=yldStayViolations(k.s,type,arrival,departure);
  if(!v.length)return '';
  const reason=String(k.cmd.restrictionOverride??'').trim();
  if(!reason)k.fail('RESTRICTED',v.map(x=>x.message).join(' ')+' Choose other dates, or ask an approver to override with a reason.');
  k.req('approve');
  k.assert(reason.length>=3,'Give a reason for overriding the stay restriction.');
  return ` · restriction overridden (${v.map(x=>x.code+' '+x.restriction).join(', ')}): ${reason.slice(0,200)}`;
}

export const handlers:Record<string,Handler>={
  BOOK:k=>{
    const {s,cmd,req,open,assert,id}=k;
    req('reservations');open();resSweepHolds(s);
    assert(cmd.guest?.trim().length>=2,'Enter the guest’s name.');
    stay(k,cmd.arrival,cmd.departure);
    assert(Number.isInteger(cmd.people)&&cmd.people>=1&&cmd.people<=4,'Choose 1–4 guests.');
    const children=cmd.children===undefined||cmd.children===''?0:cmd.children;
    assert(Number.isInteger(children)&&children>=0&&children<=3,'Children must be between 0 and 3.');
    const phone=String(cmd.phone??'').trim();
    assert(!phone||RES_PHONE.test(phone),'Enter a valid phone number (7–16 digits; fictional).');
    const source=String(cmd.source||'Direct');
    const ota=String(cmd.otaChannel??'').trim();
    assert(source!=='OTA'||ota.length>=2,'Name the OTA channel for an OTA booking.');
    const companyName=String(cmd.company?.name??cmd.companyName??'').trim(),gstin=String(cmd.company?.gstin??cmd.gstin??'').trim().toUpperCase();
    assert(!gstin||companyName,'Enter the company name for the GSTIN.');
    assert(!gstin||RES_GSTIN.test(gstin),'Enter the GSTIN in the 15-character format, e.g. 27AAAAA0000A1Z5 (fictional).');
    const r=s.rooms.find(r=>r.id===cmd.room);assert(r,'Choose a room.');
    capacity(k,r!,cmd.people,children);
    assert(available(s,cmd.room,cmd.arrival,cmd.departure),'This room is not available for those dates.','NOT_AVAILABLE');
    const overridden=restrictions(k,r!.type,cmd.arrival,cmd.departure);
    const bid=id('HCC'),hold=!!cmd.hold;
    const x:Booking={id:bid,guest:cmd.guest.trim(),email:cmd.email||'',people:cmd.people,room:r!.id,arrival:cmd.arrival,departure:cmd.departure,rate:rateFor(s,r!,cmd.arrival),source,status:hold?'Held':'Confirmed',registered:false,lines:[],receipts:[],version:1,notes:[cmd.notes||'',overridden.slice(3)].filter(Boolean).join(' · ')};
    if(hold)x.holdUntil=addMinutes(s.clock,RES_HOLD_MINUTES);
    if(phone)x.phone=phone;
    if(children)x.children=children;
    if(ota&&source.startsWith('OTA'))x.otaChannel=ota;
    if(companyName)x.company=gstin?{name:companyName,gstin}:{name:companyName};
    s.bookings.push(x);
    return `${bid} · ${cmd.guest} · room ${cmd.room}${hold?' · held until '+x.holdUntil!.slice(11,16):''}${overridden}`;
  },
  CONFIRM:({s,cmd,req,open,assert,b})=>{
    req('reservations');open();const x=b();
    assert(x.status==='Held'||x.status==='Expired','Only a held reservation can be confirmed.','INVALID_TRANSITION');
    assert(resHoldActive(s,x),`This hold expired${x.holdUntil?' at '+x.holdUntil.slice(11,16):''}. Check availability and create a new booking.`,'INVALID_TRANSITION');
    // Defensive: inventory was reserved by the hold, but revalidate the interval anyway (AC-002).
    assert(available(s,x.room,x.arrival,x.departure,x.id),'This room is not available for those dates.','NOT_AVAILABLE');
    resSweepHolds(s);
    x.status='Confirmed';delete x.holdUntil;x.version++;
    return `${x.id} · hold confirmed`;
  },
  CANCEL:({s,cmd,req,open,assert,b})=>{
    req('reservations');open();const x=b();
    const held=x.status==='Held'&&resHoldActive(s,x);
    assert(x.status==='Confirmed'||held,'Only a confirmed reservation may be cancelled or marked no-show.','INVALID_TRANSITION');
    if(cmd.noShow){
      assert(!held,'A hold cannot be marked no-show. Cancel the hold instead.','INVALID_TRANSITION');
      assert(x.arrival<=s.date,'A no-show can be recorded only on or after the arrival date.','INVALID_TRANSITION');
    }
    assert(!totals(x).deposit&&totals(x).paid===0,'Refund the advance and return any deposit before cancelling.','INVALID_TRANSITION');
    resSweepHolds(s);
    x.status=cmd.noShow?'No show':'Cancelled';
    const reason=String(cmd.reason??'').trim();if(reason)x.cancelReason=reason.slice(0,200);
    if(held)delete x.holdUntil;
    x.version++;return x.id+(held?' · hold released':'')+(reason?' · '+reason:'');
  },
  CHANGE_DATES:k=>{
    const {s,cmd,req,open,assert,b,fail}=k;
    req('reservations');open();const x=b();
    const held=x.status==='Held'&&resHoldActive(s,x);
    assert(x.status==='Confirmed'||held,'Only a confirmed or held reservation can change dates here. In-house stays use the stay move dialog.','INVALID_TRANSITION');
    if(cmd.version!==undefined&&cmd.version!==x.version)fail('STALE_VERSION','This reservation changed after you opened it. Reopen it and try again.');
    const r=s.rooms.find(r=>r.id===cmd.room);assert(r,'Select a room.');
    stay(k,cmd.arrival,cmd.departure);
    capacity(k,r!,x.people,x.children||0);
    assert(available(s,r!.id,cmd.arrival,cmd.departure,x.id),'Target room is unavailable for the requested stay.','NOT_AVAILABLE');
    assert(r!.id!==x.room||cmd.arrival!==x.arrival||cmd.departure!==x.departure,'Nothing changed. Choose a different room or dates.');
    const overridden=restrictions(k,r!.type,cmd.arrival,cmd.departure);
    resSweepHolds(s);
    const detail=`${x.id} · room ${x.room} → ${r!.id}; ${x.arrival}–${x.departure} → ${cmd.arrival}–${cmd.departure}; agreed tariff preserved${overridden}`;
    if(overridden)x.notes=[x.notes,overridden.slice(3)].filter(Boolean).join(' · ');
    // Agreed nightly rate is kept (WF-BOOK-001). A hold keeps its original expiry: changing dates never extends it.
    x.room=r!.id;x.arrival=cmd.arrival;x.departure=cmd.departure;x.version++;
    return detail;
  },
};
