// LF-DESK · lane lf-desk (LF contract §8). Commands: STAY_CHECKOUT_BASIS, STAY_FORM_C, STAY_LATE_CHARGE; helpers called from the one marked block
// of BOOK (reservations.ts), CHECKIN (guests.ts) and CHECKOUT (folio.ts).
import type {Handler,Kit} from '../commands';
import type {Booking,FormC} from '../../../domain/types';
import {guestById} from '../../../domain/streams/guests';
import {gstForRoomNight} from '../../../domain/gst';
import {money} from '../../../domain/money';
import {
  deskPurposeProblem,deskPurposeRequired,deskVehicleOf,deskFormCNeeded,deskFormCValidate,deskFormCClean,deskIsForeign,deskDefaultBasis,
  stayLateCheckout,deskLateLineId,deskLateWaivedId,deskLateDecided,stayDueOut,deskFormCRequired,deskBasis,
} from '../../../domain/streams/desk-fields';

const BASIS=['fixed','24h'];
/** BOOK block: optional purpose, vehicle and check-out basis (all optional at booking). */
export function deskBook(k:Kit,x:Booking){
  const {s,cmd,assert}=k;
  if(cmd.purpose!==undefined&&String(cmd.purpose).trim()){const m=deskPurposeProblem(s,cmd.purpose);assert(!m,m!);x.purpose=String(cmd.purpose).trim().toUpperCase();}
  const v=deskVehicleOf(s,cmd.vehicle);assert(v.ok,(v as {message:string}).message);if(v.ok&&v.vehicle)x.vehicle=v.vehicle;
  if(cmd.checkoutBasis!==undefined&&cmd.checkoutBasis!==''){assert(BASIS.includes(cmd.checkoutBasis),'Check-out basis: fixed time or 24 hours.');x.checkoutBasis=cmd.checkoutBasis;}
  else if(deskDefaultBasis(s)==='24h')x.checkoutBasis='24h';
}
/** CHECKIN block: purpose (required by setting), vehicle, Form C (foreign guests), basis, checkedInAt. Returns an audit suffix. */
export function deskCheckin(k:Kit,x:Booking):string{
  const {s,cmd,assert}=k;let note='';
  const purpose=cmd.purpose!==undefined&&String(cmd.purpose).trim()?String(cmd.purpose).trim().toUpperCase():x.purpose;
  if(purpose){const m=deskPurposeProblem(s,purpose);assert(!m,m!);}
  // The required rule applies to the desk forms (cmd.desk, sent by every check-in screen); fixtures and programmatic callers that predate A3 omit it.
  assert(purpose||!cmd.desk||!deskPurposeRequired(s),'Choose the purpose of visit.');
  if(cmd.vehicle!==undefined){const v=deskVehicleOf(s,cmd.vehicle);assert(v.ok,(v as {message:string}).message);if(v.ok){if(v.vehicle)x.vehicle=v.vehicle;else delete x.vehicle;}}
  const g=guestById(s,x.guestId);
  const fcIn=(cmd.formC??x.formC) as FormC|undefined;
  if(deskFormCNeeded(s,x)){const m=deskFormCValidate(s,x,g,fcIn);assert(!m,m!);}
  else if(cmd.formC!==undefined&&deskIsForeign(g)){const m=deskFormCValidate(s,x,g,fcIn);assert(!m,m!);}
  if(fcIn&&(deskFormCNeeded(s,x)||deskIsForeign(g))){x.formC=deskFormCClean(fcIn);note+=' · Form C recorded';}
  if(purpose)x.purpose=purpose;
  if(cmd.checkoutBasis!==undefined&&cmd.checkoutBasis!==''){assert(BASIS.includes(cmd.checkoutBasis),'Check-out basis: fixed time or 24 hours.');x.checkoutBasis=cmd.checkoutBasis;}
  x.checkedInAt=s.clock;
  return note+(deskBasis(s,x)==='24h'?' · 24-hour stay, due out '+stayDueOut(s,x).slice(11,16):'');
}
/** CHECKOUT block: refuse past the grace unless a late-charge decision exists or `cmd.lateCharge` gives one. Returns an audit suffix. */
export function deskCheckoutLate(k:Kit,x:Booking):string{
  const {s,cmd}=k;
  const lc=stayLateCheckout(s,x);
  if(!lc.late||deskLateDecided(x))return '';
  const how=String(cmd.lateCharge??'');
  if(!how)k.fail('INVALID_TRANSITION',lc.message);
  return postLate(k,x,how,String(cmd.lateReason??''));
}
function postLate(k:Kit,x:Booking,how:string,reasonIn:string):string{
  const {s,assert}=k;const lc=stayLateCheckout(s,x);
  if(how==='half-day'){
    assert(lc.halfDay.amount>0,'The half-day charge is ₹0 (check “Half-day late check-out charge” in Master configuration).');
    x.lines.push({id:deskLateLineId(x.id),label:lc.halfDay.label,amount:lc.halfDay.amount,date:s.date,kind:'room',taxBps:gstForRoomNight(s,s.date,x.rate).bps});
    return ` · late check-out half day ${money(lc.halfDay.amount)}`;
  }
  if(how==='waived'){
    k.req('approve');const reason=reasonIn.trim();assert(reason.length>=3,'Give the reason for waiving the late check-out charge.');
    x.lines.push({id:deskLateWaivedId(x.id),label:('Late check-out waived · '+reason).slice(0,80),amount:0,date:s.date,kind:'adjustment',taxBps:0});
    return ` · late check-out charge waived (${reason.slice(0,80)})`;
  }
  if(how==='full-night')k.fail('INVALID_TRANSITION','A full night is an extension: use Move / extend to add one night, then check out.');
  return k.fail('VALIDATION','Late check-out: choose half-day, full-night or waived.');
}

export const handlers:Record<string,Handler>={
  /** Change the due-out basis while the stay is open. {id, basis:'fixed'|'24h', reason}. Needs the checkin permission. */
  STAY_CHECKOUT_BASIS:k=>{
    const {cmd,assert}=k;k.req('checkin');k.open();const x=k.b();k.mutable(x);
    assert(BASIS.includes(cmd.basis),'Check-out basis: fixed time or 24 hours.');
    const reason=String(cmd.reason??'').trim();assert(reason.length>=3,'Enter the reason for the change.');assert(reason.length<=200,'The reason is too long (maximum 200 characters).');
    assert(x.status==='In house'||x.status==='Confirmed','Only an open stay can change basis.','INVALID_TRANSITION');
    const was=deskBasis(k.s,x);assert(was!==cmd.basis,'The stay already uses this basis.');
    x.checkoutBasis=cmd.basis;x.version++;
    return `${x.id} · check-out basis ${was} → ${cmd.basis} (due out ${stayDueOut(k.s,x).replace('T',' ')}) · ${reason}`;
  },
  /** Save the Form C of a Confirmed or In-house stay. {id, formC}. The passport number is the guest's ID number (never here). */
  STAY_FORM_C:k=>{
    const {s,cmd,assert}=k;k.req('checkin');k.open();const x=k.b();k.mutable(x);
    const g=guestById(s,x.guestId);assert(g,'Register the guest first (the passport goes in the ID details).');
    assert(deskIsForeign(g)||!deskFormCRequired(s),'This guest is registered as an Indian national: Form C is for foreign guests.');
    const m=deskFormCValidate(s,x,g,cmd.formC);assert(!m,m!);
    x.formC=deskFormCClean(cmd.formC);x.version++;
    return `${x.id} · Form C saved for ${x.guest}`; // never the visa or passport number
  },
  /** Post (or waive) the late check-out charge before check-out, so the folio can be settled. {id, charge:'half-day'|'waived', reason?}. */
  STAY_LATE_CHARGE:k=>{
    const {s,cmd}=k;k.req('checkout');k.open();const x=k.b();
    k.assert(x.status==='In house','This stay is not in house.','INVALID_TRANSITION');
    k.assert(!deskLateDecided(x),'The late check-out charge is already decided for this stay.','INVALID_TRANSITION');
    k.assert(stayLateCheckout(s,x).late,'This stay is not past its due-out time.','INVALID_TRANSITION');
    const note=postLate(k,x,String(cmd.charge??''),String(cmd.reason??''));x.version++;
    return `${x.id}${note}`;
  },
};
