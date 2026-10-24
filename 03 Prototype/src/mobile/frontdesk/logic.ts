// MB-05 front desk (phone) — pure helpers: agenda grouping, booking-wizard validation, attention list, check-in step state.
// No React here, so tests/stream-mobile-frontdesk.mjs can bundle and test it directly. Names start with `mf`.
import {State,Booking,Room,Permission,addDays,nights,resStatus,resSearch,resCalendarShows,resHoldActive,blockedDuring,roomHold,
  stayArrivals,stayRoomEligibility,guestForBooking,guestComplete,RES_PHONE,RES_GSTIN,RES_CAPACITY} from '../../services/mock';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const TYPE_ORDER=['Classic','Deluxe','Suite'];

/** "Ananya Rao (fictional)" → "AR". */
export function mfInitials(name:string){
  return String(name||'').replace(/\(.*?\)/g,'').trim().split(/\s+/).filter(Boolean).map(p=>p[0].toUpperCase()).slice(0,2).join('')||'?';
}
/** Room types present in the hotel, in the usual order (Classic, Deluxe, Suite, then others). */
export function mfRoomTypes(s:State){
  return [...new Set(s.rooms.map(r=>r.type))].sort((a,b)=>(TYPE_ORDER.indexOf(a)+1||99)-(TYPE_ORDER.indexOf(b)+1||99)||a.localeCompare(b));
}
/** 7 (or n) consecutive dates from start. */
export const mfWeek=(start:string,days=7)=>Array.from({length:days},(_,i)=>addDays(start,i));

// ── M03 Calendar agenda ──
export type MfAgendaRow={room:Room;kind:'stay'|'free'|'block';booking?:Booking;status?:string;arrives:boolean;past:boolean};
/** One night (date → date+1) for one room type: which booking occupies each room, or free / blocked. */
export function mfAgendaDay(s:State,date:string,type:string):MfAgendaRow[]{
  return s.rooms.filter(r=>r.type===type).sort((a,b)=>a.id.localeCompare(b.id)).map(room=>{
    const b=s.bookings.find(b=>b.room===room.id&&resCalendarShows(s,b)&&b.arrival<=date&&b.departure>date);
    const past=date<s.date;
    if(b)return {room,kind:'stay',booking:b,status:resStatus(s,b),arrives:b.arrival===date,past};
    if(blockedDuring(room,date,addDays(date,1))||roomHold(s,room.id,date,addDays(date,1)))return {room,kind:'block',arrives:false,past,status:roomHold(s,room.id,date,addDays(date,1))?.label};
    return {room,kind:'free',arrives:false,past};
  });
}
/** Day strip: free / booked / blocked counts per night for one room type. */
export function mfAgendaStrip(s:State,start:string,type:string,days=7){
  return mfWeek(start,days).map(date=>{const rows=mfAgendaDay(s,date,type);
    return {date,free:rows.filter(r=>r.kind==='free').length,booked:rows.filter(r=>r.kind==='stay').length,blocked:rows.filter(r=>r.kind==='block').length,arrivals:rows.filter(r=>r.arrives).length};});
}

// ── M05 Booking wizard ──
export type MfBookingDraft={arrival:string;departure:string;people:number;children:number;type:string;room:string;
  guest:string;phone:string;email:string;source:string;otaChannel:string;company:string;gstin:string;notes:string};
export const mfWizardSteps=['Dates & party','Pick room','Guest details','Review'];
export function mfDraft(s:State,room?:string,arrival?:string):MfBookingDraft{
  const pre=s.rooms.find(r=>r.id===room);const start=arrival&&DATE.test(arrival)&&arrival>=s.date?arrival:s.date;
  return {arrival:start,departure:addDays(start,arrival?1:2),people:1,children:0,type:pre?.type||'All',room:pre?.id||'',
    guest:'',phone:'',email:'',source:'Direct',otaChannel:'',company:'',gstin:'',notes:''};
}
/** Same rules and messages as the desktop NewReservation dialog. */
export function mfDateError(s:State,d:Pick<MfBookingDraft,'arrival'|'departure'>){
  return !DATE.test(d.arrival)||!DATE.test(d.departure)?'Choose both dates.':d.arrival<s.date?'Arrival cannot be before the current business date.'
    :nights(d.arrival,d.departure)<=0?'Departure must follow arrival.':nights(d.arrival,d.departure)>30?'Maximum 30 nights in this demo.':'';
}
/** Availability for the draft; `chosen` = picked room if available, otherwise the first available (auto-assign). */
export function mfPick(s:State,d:MfBookingDraft){
  const q=resSearch(s,{arrival:d.arrival,departure:d.departure,people:d.people,children:d.children,type:d.type});
  const chosen=d.room?q.available.find(r=>r.room.id===d.room)?.room:q.available[0]?.room;
  const pickedExcluded=d.room&&!chosen?q.excluded.find(r=>r.room.id===d.room):undefined;
  return {...q,chosen,pickedExcluded,auto:!d.room};
}
export function mfGuestErrors(d:MfBookingDraft){
  const e:Record<string,string>={};const t=(v:string)=>String(v||'').trim();
  if(t(d.guest).length<2)e.guest='Enter the guest’s name (at least 2 characters).';
  if(!t(d.phone))e.phone='Phone is required.';else if(!RES_PHONE.test(t(d.phone)))e.phone='Enter 7–16 digits (fictional number).';
  if(d.source==='OTA'&&t(d.otaChannel).length<2)e.otaChannel='Name the OTA channel.';
  const gstin=t(d.gstin).toUpperCase();
  if(gstin&&!t(d.company))e.company='Enter the company name for the GSTIN.';
  if(gstin&&!RES_GSTIN.test(gstin))e.gstin='Use the 15-character format, e.g. 27AAAAA0000A1Z5.';
  return e;
}
/** Errors that stop the wizard from leaving `step` (0-based). */
export function mfWizardErrors(s:State,step:number,d:MfBookingDraft):Record<string,string>{
  if(step===0){const de=mfDateError(s,d);if(de)return {dates:de};
    if(d.people<1)return {party:'At least one adult.'};
    if(d.type!=='All'&&d.people+d.children>(RES_CAPACITY[d.type]??4))return {party:`A ${d.type} room sleeps ${RES_CAPACITY[d.type]??4}. Choose another type or fewer guests.`};
    return {};}
  if(step===1){const p=mfPick(s,d);if(p.chosen)return {};
    return {room:p.pickedExcluded?`Room ${p.pickedExcluded.room.id} is not available: ${p.pickedExcluded.reason}.`:p.available.length?'Select one of the available rooms, or Auto-assign.':'No room is available for this search. Change dates, party size or type.'};}
  if(step===2)return mfGuestErrors(d);
  // Review: everything must still hold (availability may have changed meanwhile).
  return {...mfWizardErrors(s,0,d),...mfWizardErrors(s,1,d),...mfGuestErrors(d)};
}
/** The BOOK command the wizard sends — identical fields to the desktop dialog. */
export function mfBookCommand(s:State,d:MfBookingDraft,hold:boolean,key?:string){
  const room=mfPick(s,d).chosen;const t=(v:string)=>String(v||'').trim();const company=t(d.company),gstin=t(d.gstin).toUpperCase();
  return {type:'BOOK' as const,guest:t(d.guest),phone:t(d.phone),email:t(d.email),people:d.people,children:d.children,room:room?.id||'',arrival:d.arrival,departure:d.departure,
    source:d.source,otaChannel:d.source==='OTA'?t(d.otaChannel):undefined,company:company?{name:company,gstin:gstin||undefined}:undefined,notes:t(d.notes),hold,key};
}

// ── M04 Reservations ──
export const mfStatuses=['All','Held','Expired','Confirmed','In house','Departed','Cancelled','No show'];
export function mfFilterBookings(s:State,q:string,status:string){
  const x=q.trim().toLowerCase();
  return s.bookings.filter(b=>(status==='All'||resStatus(s,b)===status)&&`${b.guest} ${b.id} ${b.room} ${b.phone||''} ${b.company?.name||''}`.toLowerCase().includes(x))
    .sort((a,b)=>a.arrival.localeCompare(b.arrival)||a.id.localeCompare(b.id));
}

// ── M01 Home: needs attention ──
export type MfAttention={id:string;title:string;detail:string;count:number;page:string;filter?:string;tone:'orange'|'blue'|'green'};
/** Items worth a tap on the home screen, only those the user can act on (page permission) and with count > 0. */
export function mfAttention(s:State,can:(p:Permission)=>boolean,pages:string[]):MfAttention[]{
  const out:MfAttention[]=[];
  const approvals=s.approvals.filter(a=>a.status==='Pending').length+s.expenses.filter(e=>e.status==='Pending').length;
  const unregistered=stayArrivals(s).filter(b=>!b.registered).length;
  const notReady=stayArrivals(s).filter(b=>!stayRoomEligibility(s,b).ok).length;
  const departures=s.bookings.filter(b=>b.status==='In house'&&b.departure<=s.date).length;
  const holds=s.bookings.filter(b=>resHoldActive(s,b)).length;
  const inspection=s.tasks.filter(t=>t.status==='Inspection').length;
  const blocked=s.rooms.filter(r=>blockedDuring(r,s.date,addDays(s.date,1))).length;
  const add=(x:MfAttention)=>{if(x.count>0&&pages.includes(x.page))out.push(x);};
  add({id:'approvals',title:`${approvals} pending approval${approvals===1?'':'s'}`,detail:'Expenses and requested adjustments',count:approvals,page:pages.includes('approvals')?'approvals':'expenses',tone:'orange'});
  add({id:'unregistered',title:`${unregistered} arrival${unregistered===1?'':'s'} to register`,detail:'Verify the guest before check-in',count:unregistered,page:'arrivals',tone:'blue'});
  add({id:'notready',title:`${notReady} arrival room${notReady===1?'':'s'} not ready`,detail:'Dirty, blocked or conflicting room',count:notReady,page:'arrivals',tone:'orange'});
  add({id:'departures',title:`${departures} departure${departures===1?'':'s'} due`,detail:'Settle the folio and check out',count:departures,page:'inhouse',tone:'blue'});
  add({id:'holds',title:`${holds} hold${holds===1?'':'s'} running`,detail:'Confirm before the hold expires',count:holds,page:'reservations',filter:'Held',tone:'orange'});
  add({id:'inspection',title:`${inspection} room${inspection===1?'':'s'} awaiting inspection`,detail:'A final check before the next guest',count:inspection,page:'housekeeping',tone:'blue'});
  add({id:'blocked',title:`${blocked} room${blocked===1?'':'s'} blocked tonight`,detail:'Out of service',count:blocked,page:'rooms',filter:'Blocked',tone:'orange'});
  void can;
  return out;
}

// ── M02 Room board ──
export function mfRoomList(s:State,floor:number,status:string,statusOf:(r:Room)=>string){
  return s.rooms.filter(r=>(!floor||r.floor===floor)&&(status==='All'||statusOf(r)===status)).sort((a,b)=>a.id.localeCompare(b.id));
}
/** Today's confirmed arrival for a room (for "Check in" on the room sheet). */
export function mfArrivalFor(s:State,room:string){return stayArrivals(s).find(b=>b.room===room);}
/** Next confirmed reservation or running hold for a room. */
export function mfNextFor(s:State,room:string){return s.bookings.filter(b=>b.room===room&&(b.status==='Confirmed'||resHoldActive(s,b))).sort((a,c)=>a.arrival.localeCompare(c.arrival))[0];}

// ── M06 Check-in stepper ──
export const mfCheckinSteps=['Find','Verify guest','Room','Tariff','Advance','Check in'];
/** Completion of each of the 6 check-in steps, the furthest reachable step and a sensible starting step (all 1-based). */
export function mfCheckinState(s:State,b:Booking|undefined,tariffOk:boolean){
  const g=b?guestForBooking(s,b):undefined;
  const regDone=!!b&&b.registered&&(!g||g.origin==='auto'||guestComplete(g));
  const elig=b?stayRoomEligibility(s,b):{ok:false,reasons:[] as string[]};
  const done=[!!b,regDone,elig.ok,tariffOk,true,b?.status==='In house'];
  const firstOpen=done.findIndex((x,i)=>!x&&i!==4);
  const start=!b?1:b.status==='In house'?6:!b.registered?2:!elig.ok?3:4;
  return {done,regDone,elig,reachable:firstOpen<0?6:firstOpen+1,start,missing:done.slice(0,4).map((x,i)=>x?null:mfCheckinSteps[i]).filter(Boolean) as string[]};
}
