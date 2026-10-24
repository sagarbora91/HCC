// Stream A · Calendar & reservations (owns BOOK, CANCEL, CONFIRM/hold commands) — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests).
// All exports are prefixed `res` / `RES_` to stay unique across streams.
import type {Booking,BookingStatus,Room,State} from '../types';
import {available,blockedDuring,roomHold} from '../selectors';
import {addDays,nights} from '../money';

/** Maximum occupants (adults + children) per room type (REQ-BOOK-002; Fixture Catalogue room types). */
export const RES_CAPACITY:Record<string,number>={Classic:2,Deluxe:3,Suite:4};
export const resCapacity=(r:Room)=>RES_CAPACITY[r.type]??4;
/** Default hold length in simulated minutes (DEC-038). */
export const RES_HOLD_MINUTES=120;
/** Booking sources (REQ-BOOK-003). "OTA" requires a named channel. Legacy "OTA · manual" stays accepted. */
export const RES_SOURCES=['Walk-in','Phone','WhatsApp','Direct','OTA','Corporate'];
export const RES_OTA_CHANNELS=['Demo Travel Hub','Example Stays','Fictional Trips'];
/** Sample GSTIN-format check (fictional data only; no compliance claim). */
export const RES_GSTIN=/^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z\d]Z[A-Z\d]$/;
export const RES_PHONE=/^\+?[\d\s-]{7,16}$/;

const minutesBetween=(a:string,b:string)=>Math.round((Date.parse(b+':00Z')-Date.parse(a+':00Z'))/60000);
/** A Held booking whose hold is still running. A Held booking without holdUntil never lapses (matches available()). */
export function resHoldActive(s:State,b:Booking){return b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil);}
/** Status as shown to users: a lapsed hold reads "Expired" even before a command sweeps it. */
export function resStatus(s:State,b:Booking):BookingStatus{return b.status==='Held'&&!resHoldActive(s,b)?'Expired':b.status;}
/** Minutes left on a running hold (0 when lapsed or not a hold). */
export function resHoldMinutesLeft(s:State,b:Booking){return resHoldActive(s,b)&&b.holdUntil?Math.max(0,minutesBetween(s.clock,b.holdUntil)):0;}
export function resHoldLeftLabel(s:State,b:Booking){
  if(b.status!=='Held')return '';if(!resHoldActive(s,b))return 'Hold expired'+(b.holdUntil?' at '+b.holdUntil.slice(11,16):'');
  const m=resHoldMinutesLeft(s,b),h=Math.floor(m/60);return `${h?h+' h ':''}${m%60} min left`;
}
/** Persist lapsed holds as Expired (called inside stream A commands; display never depends on it). Returns IDs swept. */
export function resSweepHolds(s:State){const ids:string[]=[];for(const b of s.bookings)if(b.status==='Held'&&!resHoldActive(s,b)){b.status='Expired';b.version++;ids.push(b.id);}return ids;}

export type ResReason='Blocked'|'Over capacity'|'Occupied'|'Booked'|'Held';
export type ResSearchRow={room:Room;capacity:number;ok:boolean;reasons:ResReason[];reason:string;conflict?:Booking};
/** Why a room cannot take the stay [a, d) for `party` occupants. Empty list = available. Order: blocked, capacity, occupied, booked/held. */
export function resExclusion(s:State,room:Room,a:string,d:string,party:number,except=''):{reasons:ResReason[];conflict?:Booking}{
  const reasons:ResReason[]=[];
  if(blockedDuring(room,a,d))reasons.push('Blocked');
  if(party>resCapacity(room))reasons.push('Over capacity');
  const clash=s.bookings.filter(b=>b.id!==except&&b.room===room.id&&a<b.departure&&d>b.arrival&&(b.status==='In house'||b.status==='Confirmed'||resHoldActive(s,b)));
  const inHouse=clash.find(b=>b.status==='In house'),booked=clash.find(b=>b.status==='Confirmed'),held=clash.find(b=>b.status==='Held');
  if(inHouse)reasons.push('Occupied');if(booked)reasons.push('Booked');if(held)reasons.push('Held');
  // Defensive: available() is the authority; if it disagrees without a reason found here, report "Booked".
  if(!reasons.length&&!available(s,room.id,a,d,except))reasons.push('Booked');
  return {reasons,conflict:inHouse||booked||held};
}
const reasonText=(s:State,r:ResReason,c?:Booking)=>r==='Over capacity'?'Over capacity':r==='Occupied'?`Occupied · ${c?.guest??'in-house guest'}`:r==='Held'?`Held · ${c?resHoldLeftLabel(s,c):''}`:`Booked · ${c?.guest??'another reservation'}`;
/** Availability search (REQ-BOOK-001): every room of the chosen type, with the reason each excluded room is excluded. */
export function resSearch(s:State,q:{arrival:string;departure:string;people:number;children?:number;type?:string;except?:string}){
  const party=(q.people||0)+(q.children||0),valid=/^\d{4}-\d{2}-\d{2}$/.test(q.arrival)&&/^\d{4}-\d{2}-\d{2}$/.test(q.departure)&&nights(q.arrival,q.departure)>0;
  const rows:ResSearchRow[]=s.rooms.filter(r=>!q.type||q.type==='All'||r.type===q.type).map(room=>{
    if(!valid)return {room,capacity:resCapacity(room),ok:false,reasons:[],reason:'Choose valid dates'};
    const x=resExclusion(s,room,q.arrival,q.departure,party,q.except||'');
    const first=x.reasons[0];
    const reason=!first?'Available':first==='Blocked'?'Blocked · '+((room as Room&{blockReason?:string}).blockReason||'out of service'):first==='Over capacity'?`Over capacity · sleeps ${resCapacity(room)}`:reasonText(s,first,x.conflict);
    return {room,capacity:resCapacity(room),ok:!x.reasons.length,reasons:x.reasons,reason,conflict:x.conflict};
  });
  return {valid,party,available:rows.filter(r=>r.ok),excluded:rows.filter(r=>!r.ok)};
}

// ── Calendar (SCR-03) ──
export type ResCalCell={date:string;kind:'free'|'stay'|'block';booking?:string;past:boolean};
export type ResCalBar={kind:'stay'|'block';booking?:string;status:string;guest?:string;label:string;/** Short status text shown under the guest name. */sub:string;start:number;span:number;clipStart:boolean;clipEnd:boolean};
export type ResCalRow={room:Room;cells:ResCalCell[];bars:ResCalBar[]};
const TYPE_ORDER=['Classic','Deluxe','Suite'];
/** Bookings drawn on the calendar: running holds, confirmed, in-house and departed stays. */
export function resCalendarShows(s:State,b:Booking){return b.status==='Confirmed'||b.status==='In house'||b.status==='Departed'||resHoldActive(s,b);}
/** Rooms × `days` nights from `start`. Bars are clipped to the window; clipStart/clipEnd mark stays continuing outside it. */
export function resCalendar(s:State,start:string,days=14,type='All'){
  const dates=Array.from({length:days},(_,i)=>addDays(start,i)),end=addDays(start,days);
  const rooms=s.rooms.filter(r=>type==='All'||r.type===type);
  const types=[...new Set(rooms.map(r=>r.type))].sort((a,b)=>(TYPE_ORDER.indexOf(a)+1||99)-(TYPE_ORDER.indexOf(b)+1||99)||a.localeCompare(b));
  const row=(room:Room):ResCalRow=>{
    const stays=s.bookings.filter(b=>b.room===room.id&&resCalendarShows(s,b)&&b.arrival<end&&b.departure>start).sort((a,b)=>a.arrival.localeCompare(b.arrival));
    const held=dates.map(d=>blockedDuring(room,d,addDays(d,1))?undefined:roomHold(s,room.id,d,addDays(d,1)));
    const blocked=dates.map((d,i)=>blockedDuring(room,d,addDays(d,1))||!!held[i]);
    const cells:ResCalCell[]=dates.map((date,i)=>{const b=stays.find(b=>b.arrival<=date&&b.departure>date);
      return {date,kind:b?'stay':blocked[i]?'block':'free',booking:b?.id,past:date<s.date};});
    const bars:ResCalBar[]=stays.map(b=>{const from=b.arrival>start?b.arrival:start,to=b.departure<end?b.departure:end,status=resStatus(s,b);
      return {kind:'stay',booking:b.id,status,guest:b.guest,label:`${b.guest} · ${status}${b.status==='Held'?' · '+resHoldLeftLabel(s,b):''}`,sub:b.status==='Held'?`Held · ${resHoldLeftLabel(s,b).replace(' min left','m').replace(' h ','h ')}`:status,start:nights(start,from),span:nights(from,to),clipStart:b.arrival<start,clipEnd:b.departure>end};});
    const reason=(room as Room&{blockReason?:string}).blockReason||'Out of service';
    for(let i=0;i<days;i++)if(blocked[i]&&cells[i].kind!=='stay'){let j=i;while(j+1<days&&blocked[j+1]&&cells[j+1].kind!=='stay')j++;
      bars.push({kind:'block',status:'Blocked',label:held[i]?held[i]!.label:`Blocked · ${reason}`,sub:'Blocked',start:i,span:j-i+1,clipStart:i===0&&blockedDuring(room,addDays(start,-1),start),clipEnd:j===days-1&&blockedDuring(room,end,addDays(end,1))});i=j;}
    return {room,cells,bars};
  };
  return {dates,end,groups:types.map(t=>({type:t,capacity:RES_CAPACITY[t]??4,rows:rooms.filter(r=>r.type===t).map(row)}))};
}
