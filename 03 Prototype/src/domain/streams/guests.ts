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

export const guestIdTypes=['Aadhaar (demo)','Passport (demo)','Driving licence (demo)','Voter ID (demo)','PAN (demo)','Other (demo)'];
export const guestAgeBands=['Adult','Child','Infant','Senior'];
export const guestFieldLabels:Record<string,string>={name:'Name',phone:'Phone',email:'Email',address:'Address',nationality:'Nationality',idType:'ID type',idLast4:'ID last 4',idNumber:'ID number',gstin:'GSTIN',party:'Party'};

// ── Full ID number (DEC-068): stored for every ID type, validated per type, shown masked (last 4) everywhere ──
// LEGAL RISK (DEC-068): UIDAI rules expect hotels to keep only a masked Aadhaar; storing the full number can be an offence under the
// Aadhaar Act 2016. Stored by owner decision — confirm with the CA/lawyer before production (production encrypts at rest).
// The full number lives only in Guest.idNumber. It is never written to audit text, logs, exports, messages, assistant answers or print-outs.
export type GuestIdKind='aadhaar'|'passport'|'dl'|'voter'|'pan'|'other';
export const guestIdKind=(type:string|undefined):GuestIdKind=>{
  const t=String(type||'').toLowerCase();
  return t.startsWith('aadhaar')?'aadhaar':t.startsWith('passport')?'passport':t.startsWith('driving')?'dl':t.startsWith('voter')?'voter':t.startsWith('pan')?'pan':'other';
};
/** Upper-case, spaces/hyphens removed. */
export const guestIdNormalise=(v:unknown)=>String(v??'').replace(/[\s-]/g,'').toUpperCase();
const VD=[[0,1,2,3,4,5,6,7,8,9],[1,2,3,4,0,6,7,8,9,5],[2,3,4,0,1,7,8,9,5,6],[3,4,0,1,2,8,9,5,6,7],[4,0,1,2,3,9,5,6,7,8],[5,9,8,7,6,0,4,3,2,1],[6,5,9,8,7,1,0,4,3,2],[7,6,5,9,8,2,1,0,4,3],[8,7,6,5,9,3,2,1,0,4],[9,8,7,6,5,4,3,2,1,0]];
const VP=[[0,1,2,3,4,5,6,7,8,9],[1,5,7,6,2,8,3,0,9,4],[5,8,0,3,7,9,6,1,4,2],[8,9,1,6,0,4,3,5,2,7],[9,4,5,3,1,2,6,8,7,0],[4,2,8,6,5,7,3,9,0,1],[2,7,9,3,8,0,6,4,1,5],[7,0,4,6,9,1,3,2,5,8]];
const VINV=[0,4,3,2,1,5,6,7,8,9];
/** Verhoeff check of a digit string (checksum digit last). */
export function guestVerhoeff(digits:string):boolean{
  if(!/^\d+$/.test(digits))return false;let c=0;const a=digits.split('').reverse();
  for(let i=0;i<a.length;i++)c=VD[c][VP[i%8][+a[i]]];
  return c===0;
}
/** Verhoeff check digit for a digit string without its checksum. */
export function guestVerhoeffDigit(digits:string):number{
  let c=0;const a=digits.split('').reverse();
  for(let i=0;i<a.length;i++)c=VD[c][VP[(i+1)%8][+a[i]]];
  return VINV[c];
}
/** Error text for an ID number against its type; '' when valid. Format only — nothing is checked against any registry. */
export function guestIdNumberError(type:string|undefined,raw:unknown):string{
  const n=guestIdNormalise(raw);if(!n)return '';
  switch(guestIdKind(type)){
    case 'aadhaar':
      if(!/^\d{12}$/.test(n))return 'Aadhaar must be exactly 12 digits.';
      return guestVerhoeff(n)?'':'This Aadhaar number fails the checksum — check the digits.';
    case 'passport':return /^[A-Z]\d{7}$/.test(n)?'':'Passport number is 1 letter followed by 7 digits (e.g. Z1234567).';
    case 'dl':return /^[A-Z]{2}\d{13}$/.test(n)?'':'Driving licence is 2 letters, then 13 digits (e.g. MH1220110012345).';
    case 'voter':return /^[A-Z]{3}\d{7}$/.test(n)?'':'Voter ID (EPIC) is 3 letters followed by 7 digits.';
    case 'pan':return /^[A-Z]{5}\d{4}[A-Z]$/.test(n)?'':'PAN is 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F).';
    default:return /^[A-Z0-9]{4,20}$/.test(n)?'':'ID number is 4 to 20 letters or digits.';
  }
}
export const guestIdLast4=(n:unknown)=>guestIdNormalise(n).slice(-4);
/** Masked form shown everywhere: XXXX-XXXX-1234 (last 4 only). */
export const guestIdMask=(last4:string|undefined)=>last4?'XXXX-XXXX-'+String(last4).toUpperCase().slice(-4):'';
/** Readable grouping of a full number — only for the deliberate "Show ID" reveal. */
export const guestIdFormat=(type:string|undefined,n:string)=>guestIdKind(type)==='aadhaar'&&/^\d{12}$/.test(n)?n.replace(/(\d{4})(?=\d)/g,'$1 '):n;
/** FICTIONAL ID numbers for generated data, obviously fake: Aadhaar starts with 1 (real numbers never start with 0 or 1) and keeps a valid
 *  Verhoeff checksum; passport Z+7 digits (Z is not issued), licence ZZ…, voter ZZZ…, PAN ZZZZZ…Z, other FICT…. `n` varies the number;
 *  `last4` (optional) is preserved for Aadhaar where possible. Deterministic. */
export function guestFictionalId(type:string,n:number,last4?:string):string{
  const k=guestIdKind(type),pad=(v:number,w:number)=>String(Math.abs(v)%10**w).padStart(w,'0');
  switch(k){
    case 'aadhaar':{
      const fixed=last4&&/^\d{4}$/.test(last4)?last4:undefined;
      for(let i=0;i<1000;i++){
        const mid=pad(n*7919+i*104729+13,fixed?7:10);
        const body='1'+(fixed?mid+fixed.slice(0,3):mid);
        const d=guestVerhoeffDigit(body);
        if(!fixed||String(d)===fixed[3])return body+d;
      }
      return '1'+pad(n,10)+guestVerhoeffDigit('1'+pad(n,10));
    }
    case 'passport':return 'Z'+pad(n*104729+7,7);
    case 'dl':return 'ZZ'+pad(n*31+5,2)+pad(n*7919+11,11);
    case 'voter':return 'ZZZ'+pad(n*104729+3,7);
    case 'pan':return 'ZZZZZ'+pad(n*7919+1,4)+'Z';
    default:return 'FICT'+pad(n*104729+9,8);
  }
}

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
  if(t(d.idLast4)&&!/^[A-Za-z0-9]{4}$/.test(t(d.idLast4)))e.idLast4='Enter exactly the last 4 letters or digits of the ID (the full number goes in the ID number field).';
  if((t(d.idLast4)||t(d.idNumber))&&!t(d.idType))e.idType='Choose the ID type.';
  if(t(d.idNumber)&&t(d.idType)){const m=guestIdNumberError(d.idType,d.idNumber);if(m)e.idNumber=m;}
  (d.party||[]).forEach((p,i)=>{if(t(p.name).length<2)e.party=`Party member ${i+1}: enter a name.`;else if(!guestAgeBands.includes(p.ageBand))e.party=`Party member ${i+1}: choose an age band.`;});
  if((d.party||[]).length>6)e.party='At most 6 accompanying guests in this demo.';
  if(registration){
    if(!t(d.address))e.address='Enter the address.';
    if(!t(d.idType))e.idType='Choose the ID type.';
    // Profiles saved before DEC-068 hold only the last 4 characters; they stay valid.
    if(!t(d.idNumber)&&!t(d.idLast4))e.idNumber='Enter the ID number.';
  }
  return e;
}
/** True when the profile satisfies the registration requirements. */
export const guestComplete=(g:GuestDraft|undefined)=>!!g&&!Object.keys(guestValidate(g,true)).length;
/** Cleaned copy of a draft (trimmed strings, upper-case GSTIN, party normalised). */
export function guestClean(d:GuestDraft):GuestDraft{
  const t=(v:unknown)=>String(v??'').trim();
  // company/gstin are omitted (not undefined) when blank, so state survives a JSON round-trip unchanged.
  return {name:t(d.name),phone:t(d.phone),email:t(d.email),address:t(d.address),nationality:t(d.nationality)||'Indian',idType:t(d.idType),idLast4:(t(d.idNumber)?guestIdLast4(d.idNumber):t(d.idLast4)).toUpperCase(),
    ...(t(d.idNumber)?{idNumber:guestIdNormalise(d.idNumber)}:{}),
    ...(t(d.company)?{company:t(d.company)}:{}),...(t(d.gstin)?{gstin:t(d.gstin).toUpperCase()}:{}),preferences:t(d.preferences),notes:t(d.notes),
    party:(d.party||[]).map(p=>({name:t(p.name),ageBand:p.ageBand,relation:t(p.relation)}))};
}

// ── Field masking (REQ-GUEST-006; intended PERM-GST-003 guest.idDetails.view) ──
export type GuestAccess={idDetails:boolean;contact:boolean;edit:boolean};
/** Coarse model: the 'checkin' permission stands in for PERM-GST-002/003 until stream E's policy layer lands. */
export function guestAccess(s:State,uid:string):GuestAccess{const ok=can(s,uid,'checkin');return {idDetails:ok,contact:ok,edit:ok};}
export const guestMaskPhone=(p:string)=>{const d=String(p||'').replace(/\D/g,'');return d?'••••••'+d.slice(-4):'—';};
export const guestMaskEmail=(e:string)=>{if(!e)return '—';const [u,dom]=e.split('@');return (u?.[0]||'')+'•••@'+(dom||'');};
/** The profile as the given user may see it: ID → masked last 4 ("Restricted" without the ID permission), phone → masked last 4, email masked.
 *  The full ID number is NEVER part of the view; `idOnFile` says whether one is stored. Reveal it only via guestRevealId after GUEST_ID_VIEW (audited). */
export function guestView(s:State,uid:string,g:Guest):Omit<Guest,'idNumber'>&{masked:boolean;idMasked:string;idOnFile:boolean;canShowId:boolean}{
  const a=guestAccess(s,uid);const {idNumber,...rest}=g;
  return {...rest,masked:!a.idDetails,idMasked:a.idDetails?guestIdMask(g.idLast4):'Restricted',idOnFile:!!idNumber,canShowId:a.idDetails&&!!idNumber,
    phone:a.contact?g.phone:guestMaskPhone(g.phone),email:a.contact?g.email:guestMaskEmail(g.email),
    idType:a.idDetails?g.idType:'Restricted',idLast4:a.idDetails?g.idLast4:'Restricted'};
}

/** The full ID number for the deliberate "Show ID" action — only for roles with the ID-view permission, else undefined. The caller must have run GUEST_ID_VIEW (audit) first. */
export const guestRevealId=(s:State,uid:string,g:Guest):string|undefined=>guestAccess(s,uid).idDetails?g.idNumber:undefined;

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
