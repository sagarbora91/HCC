// Stream B · Guests & check-in (owns REGISTER, CHECKIN, MOVE) — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests).
// Export names are prefixed guest*/stay* to avoid clashes with other streams.
import type {State,Booking,Room} from '../types';
import {available,can,occupant,blockedDuring} from '../selectors';
import {addDays,nights} from '../money';

export type Guest=NonNullable<State['guests']>[number];
export type GuestParty=Guest['party'][number];
export type StaySegment=NonNullable<Booking['segments']>[number];
/** Editable guest fields (what GUEST_SAVE / REGISTER accept). */
export type GuestDraft=Partial<Omit<Guest,'id'|'verified'|'origin'|'version'>>;

export const guestIdTypes=['Aadhaar (demo)','Passport (demo)','Driving licence (demo)','Voter ID (demo)'];
export const guestAgeBands=['Adult','Child','Infant','Senior'];
export const guestFieldLabels:Record<string,string>={name:'Name',phone:'Phone',email:'Email',address:'Address',nationality:'Nationality',idType:'ID type',idLast4:'ID last 4',gstin:'GSTIN',party:'Party'};

// ── Normalisation and duplicates (REQ-GUEST-004, AC-032) ──
/** Digits only, last 10 (drops +91 / leading 0). */
export function guestNormalisePhone(p:string|undefined){const d=String(p||'').replace(/\D/g,'');return d.length>10?d.slice(-10):d;}
/** Lower-case, accents/punctuation stripped, "(fictional)" removed, single spaces. */
export function guestNormaliseName(n:string|undefined){
  return String(n||'').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g,'').replace(/\(fictional\)/g,'').replace(/[^a-z\s]/g,' ').replace(/\s+/g,' ').trim();
}
export type GuestMatch={guest:Guest;reason:'phone'|'name'};
/** Possible existing profiles for a draft: same normalised phone (≥7 digits), or same normalised name. Excludes the draft's own ID. */
export function guestDuplicates(s:State,draft:{id?:string;name?:string;phone?:string}):GuestMatch[]{
  const phone=guestNormalisePhone(draft.phone),name=guestNormaliseName(draft.name);const out:GuestMatch[]=[];
  for(const g of s.guests||[]){
    if(g.id===draft.id)continue;
    if(phone.length>=7&&guestNormalisePhone(g.phone)===phone)out.push({guest:g,reason:'phone'});
    else if(name.length>=2&&guestNormaliseName(g.name)===name)out.push({guest:g,reason:'name'});
  }
  return out;
}

// ── Validation (VALIDATION errors; shared by UI and handlers) ──
/** Field → message. `registration` adds the check-in requirements (address, ID type, ID last 4). */
export function guestValidate(d:GuestDraft,registration=false):Record<string,string>{
  const e:Record<string,string>={};const t=(v:unknown)=>String(v??'').trim();
  if(t(d.name).length<2)e.name='Enter the guest’s name (at least 2 characters).';
  const ph=guestNormalisePhone(d.phone);
  if(!t(d.phone))e.phone='Enter a phone number.';else if(ph.length<10||!/^[+\d\s()-]+$/.test(t(d.phone)))e.phone='Enter a 10-digit phone number (fictional).';
  if(t(d.email)&&!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(t(d.email)))e.email='Enter a valid email or leave it blank.';
  if(t(d.gstin)&&!/^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z\d]Z[A-Z\d]$/.test(t(d.gstin).toUpperCase()))e.gstin='GSTIN must be 15 characters in the format 27ABCDE1234F1Z5 (fictional).';
  if(t(d.gstin)&&!t(d.company))e.company='Enter the company name for this GSTIN.';
  if(t(d.idLast4)&&!/^[A-Za-z0-9]{4}$/.test(t(d.idLast4)))e.idLast4='Enter exactly the last 4 letters or digits of the ID — never the full number.';
  if(t(d.idLast4)&&!t(d.idType))e.idType='Choose the ID type.';
  (d.party||[]).forEach((p,i)=>{if(t(p.name).length<2)e.party=`Party member ${i+1}: enter a name.`;else if(!guestAgeBands.includes(p.ageBand))e.party=`Party member ${i+1}: choose an age band.`;});
  if((d.party||[]).length>6)e.party='At most 6 accompanying guests in this demo.';
  if(registration){
    if(!t(d.address))e.address='Enter the address.';
    if(!t(d.idType))e.idType='Choose the ID type.';
    if(!t(d.idLast4))e.idLast4='Enter the last 4 characters of the ID.';
  }
  return e;
}
/** True when the profile satisfies the registration requirements. */
export const guestComplete=(g:GuestDraft|undefined)=>!!g&&!Object.keys(guestValidate(g,true)).length;
/** Cleaned copy of a draft (trimmed strings, upper-case GSTIN, party normalised). */
export function guestClean(d:GuestDraft):GuestDraft{
  const t=(v:unknown)=>String(v??'').trim();
  // company/gstin are omitted (not undefined) when blank, so state survives a JSON round-trip unchanged.
  return {name:t(d.name),phone:t(d.phone),email:t(d.email),address:t(d.address),nationality:t(d.nationality)||'Indian',idType:t(d.idType),idLast4:t(d.idLast4).toUpperCase(),
    ...(t(d.company)?{company:t(d.company)}:{}),...(t(d.gstin)?{gstin:t(d.gstin).toUpperCase()}:{}),preferences:t(d.preferences),notes:t(d.notes),
    party:(d.party||[]).map(p=>({name:t(p.name),ageBand:p.ageBand,relation:t(p.relation)}))};
}

// ── Field masking (REQ-GUEST-006; intended PERM-GST-003 guest.idDetails.view) ──
export type GuestAccess={idDetails:boolean;contact:boolean;edit:boolean};
/** Coarse model: the 'checkin' permission stands in for PERM-GST-002/003 until stream E's policy layer lands. */
export function guestAccess(s:State,uid:string):GuestAccess{const ok=can(s,uid,'checkin');return {idDetails:ok,contact:ok,edit:ok};}
export const guestMaskPhone=(p:string)=>{const d=String(p||'').replace(/\D/g,'');return d?'••••••'+d.slice(-4):'—';};
export const guestMaskEmail=(e:string)=>{if(!e)return '—';const [u,dom]=e.split('@');return (u?.[0]||'')+'•••@'+(dom||'');};
/** The profile as the given user may see it: ID → "Restricted", phone → masked last 4, email masked. */
export function guestView(s:State,uid:string,g:Guest):Guest&{masked:boolean}{
  const a=guestAccess(s,uid);
  return {...g,masked:!a.idDetails,
    phone:a.contact?g.phone:guestMaskPhone(g.phone),email:a.contact?g.email:guestMaskEmail(g.email),
    idType:a.idDetails?g.idType:'Restricted',idLast4:a.idDetails?g.idLast4:'Restricted'};
}

// ── Lookups ──
export const guestById=(s:State,id?:string)=>id?(s.guests||[]).find(g=>g.id===id):undefined;
export const guestForBooking=(s:State,b:Booking)=>guestById(s,b.guestId);
/** All bookings linked to a guest, newest arrival first (REQ-GUEST-007). */
export const guestStays=(s:State,guestId:string)=>s.bookings.filter(b=>b.guestId===guestId).sort((a,b)=>b.arrival.localeCompare(a.arrival));
/** Search by name, phone digits or booking ID/room. Phone search only matches for users allowed to see phones. */
export function guestSearch(s:State,q:string,uid?:string){
  const n=guestNormaliseName(q),digits=q.replace(/\D/g,''),raw=q.trim().toLowerCase(),phoneOk=!uid||guestAccess(s,uid).contact;
  if(!raw)return [...(s.guests||[])];
  return (s.guests||[]).filter(g=>(n&&guestNormaliseName(g.name).includes(n))||(phoneOk&&digits.length>=3&&guestNormalisePhone(g.phone).includes(digits))||
    guestStays(s,g.id).some(b=>b.id.toLowerCase().includes(raw)||b.room===raw));
}

// ── Stays ──
/** Today's arrivals: Confirmed with arrival ≤ business date. */
export const stayArrivals=(s:State)=>s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date).sort((a,b)=>a.arrival.localeCompare(b.arrival)||a.room.localeCompare(b.room));
export const stayInHouse=(s:State)=>s.bookings.filter(b=>b.status==='In house').sort((a,b)=>a.room.localeCompare(b.room));
/** Room segments; a stay checked in before segments existed gets one derived open segment. */
export function staySegments(b:Booking):StaySegment[]{
  if(b.segments?.length)return b.segments;
  return b.status==='In house'||b.status==='Departed'?[{room:b.room,from:b.arrival}]:[];
}
const dm=(d:string)=>{const [,m,day]=d.split('-');return `${Number(day)} ${['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][Number(m)-1]}`;};
/** "101: 23–24 Oct → 105: 24 Oct–" (open segment ends with "–"). */
export function staySegmentLabel(b:Booking){
  return staySegments(b).map(g=>{
    if(!g.to)return `${g.room}: ${dm(g.from)}–`;
    const [,fm]=g.from.split('-'),[,tm]=g.to.split('-');
    return `${g.room}: ${fm===tm?Number(g.from.slice(8)):dm(g.from)}–${dm(g.to)}`;
  }).join(' → ');
}
export type StayEligibility={ok:boolean;reasons:string[]};
/** Whether the booking's current room can be checked into now (vacant, Ready, unblocked, allocation free). */
export function stayRoomEligibility(s:State,b:Booking):StayEligibility{
  const r=s.rooms.find(r=>r.id===b.room);const reasons:string[]=[];
  if(!r)return {ok:false,reasons:['Room not found.']};
  const from=b.arrival<s.date?s.date:b.arrival;
  if(r.blocked||blockedDuring(r,from,b.departure))reasons.push(`Room ${r.id} is blocked${(r as Room&{blockReason?:string}).blockReason?' ('+(r as Room&{blockReason?:string}).blockReason+')':''}.`);
  if(r.hk!=='Ready')reasons.push(`Room ${r.id} is ${r.hk.toLowerCase()} — housekeeping must finish and inspect it.`);
  const occ=occupant(s,r.id);if(occ&&occ.id!==b.id)reasons.push(`Room ${r.id} is occupied by ${occ.guest}.`);
  else if(!available(s,r.id,b.arrival,b.departure,b.id))reasons.push(`Room ${r.id} conflicts with another booking for these dates.`);
  return {ok:!reasons.length,reasons};
}
/** Rooms that are vacant, Ready, unblocked and free for [from, to) — `sameType` filters to the given room type. */
export function stayFreeRooms(s:State,from:string,to:string,opts:{except?:string;excludeRoom?:string;type?:string}={}):Room[]{
  return s.rooms.filter(r=>r.id!==opts.excludeRoom&&(!opts.type||r.type===opts.type)&&!r.blocked&&r.hk==='Ready'&&!occupant(s,r.id)&&available(s,r.id,from,to,opts.except||''));
}
/** Check-in alternatives: same type as the booked room, eligible for the booking's dates. */
export function stayCheckinAlternatives(s:State,b:Booking):Room[]{
  const type=s.rooms.find(r=>r.id===b.room)?.type;
  return stayFreeRooms(s,b.arrival,b.departure,{except:b.id,excludeRoom:b.room,type});
}
/** Move/extend alternatives for an in-house stay: rooms free for [business date, departure). Same type first. */
export function stayMoveAlternatives(s:State,b:Booking,departure=b.departure):Room[]{
  const from=b.status==='In house'?s.date:b.arrival;if(!(departure>from))return [];
  const type=s.rooms.find(r=>r.id===b.room)?.type;
  return stayFreeRooms(s,from,departure,{except:b.id,excludeRoom:b.room}).sort((x,y)=>(x.type===type?0:1)-(y.type===type?0:1)||x.id.localeCompare(y.id));
}
/** Earliest departure an in-house stay may be shortened to (business date + 1). */
export const stayShortenMin=(s:State)=>addDays(s.date,1);
/** Departure bounds for MOVE: [min, max] (max 30 nights from the active arrival). */
export function stayDepartureBounds(s:State,b:Booking){const from=b.status==='In house'?s.date:b.arrival;return {min:addDays(from,1),max:addDays(from,30)};}
/** Whether the current room can keep the stay to `departure` (extension check). */
export const stayCanExtend=(s:State,b:Booking,departure:string)=>available(s,b.room,b.status==='In house'?s.date:b.arrival,departure,b.id);
/** Nights in the stay, for display. */
export const stayNights=(b:Booking)=>nights(b.arrival,b.departure);
