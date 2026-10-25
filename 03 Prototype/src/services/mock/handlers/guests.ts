// Stream B · Guests & check-in (owns REGISTER, CHECKIN, MOVE; adds GUEST_SAVE).
// Register new or replacement command handlers here. A handler with the same name as a built-in command replaces it.
import type {Handler,Kit} from '../commands';
import type {Booking} from '../../../domain/types';
import {blockedTonight,available,occupant} from '../../../domain/selectors';
import {nights} from '../../../domain/money';
import {Guest,GuestDraft,guestValidate,guestClean,guestFieldLabels,staySegments,guestIdNumberError} from '../../../domain/streams/guests';

/** Throws VALIDATION with the first message, prefixed by its field label. */
function validate(k:Kit,d:GuestDraft,registration:boolean){
  const e=guestValidate(d,registration),keys=Object.keys(e);
  if(keys.length)k.fail('VALIDATION',keys.length===1?e[keys[0]]:`${e[keys[0]]} (${keys.length-1} more: ${keys.slice(1).map(x=>guestFieldLabels[x]||x).join(', ')})`);
}
/** Create or update a profile from a draft. Returns the stored guest. */
function upsert(k:Kit,d:GuestDraft,existingId:string|undefined,verified:boolean):Guest{
  const s=k.s;s.guests=s.guests||[];
  const clean=guestClean(d);
  if(existingId){
    const g=s.guests.find(g=>g.id===existingId);k.assert(g,'Guest profile not found.','NOT_FOUND');
    if(k.cmd.version!==undefined)k.assert(g!.version===k.cmd.version,'This guest profile changed since you opened it. Reload and try again.','STALE_VERSION');
    delete g!.company;delete g!.gstin;
    // No new number typed: keep the stored one (the edit form never carries it); it must still fit a changed ID type.
    if(!clean.idNumber&&g!.idNumber){
      k.assert(!guestIdNumberError(clean.idType,g!.idNumber),'The ID type changed — enter the ID number for the new type.');
      clean.idNumber=g!.idNumber;clean.idLast4=g!.idLast4;
    }else if(!clean.idNumber&&g!.idType===clean.idType)clean.idLast4=clean.idLast4||g!.idLast4;
    Object.assign(g!,clean,{verified:verified||(g!.verified&&!!clean.idLast4&&!!clean.address),origin:g!.origin==='auto'?'manual':g!.origin,version:(g!.version||1)+1});
    return g!;
  }
  const g:Guest={id:k.id('G'),...(clean as Required<Pick<Guest,'name'|'phone'|'email'|'address'|'nationality'|'idType'|'idLast4'|'preferences'|'notes'|'party'>>),
    ...(clean.company?{company:clean.company}:{}),...(clean.gstin?{gstin:clean.gstin}:{}),verified,origin:'manual',version:1};
  s.guests.push(g);return g;
}
const link=(x:Booking,g:Guest)=>{if(x.guestId!==g.id){x.guestId=g.id;x.version++;}};

export const handlers:Record<string,Handler>={
  /** Create/edit a guest profile. {guest:GuestDraft, id?:guestId, booking?:bookingId to link, verified?:boolean, version?}. Intended PERM-GST-002. */
  GUEST_SAVE:k=>{
    k.req('checkin');const {cmd,s}=k;
    k.assert(cmd.guest&&typeof cmd.guest==='object','Enter the guest details.');
    if(cmd.party!==undefined)cmd.guest.party=cmd.party;
    validate(k,cmd.guest,!!cmd.verified);
    let bk:Booking|undefined;
    if(cmd.booking){bk=s.bookings.find(b=>b.id===cmd.booking);k.assert(bk,'Booking not found.','NOT_FOUND');k.mutable(bk!);}
    const before=cmd.id?s.guests?.find(x=>x.id===cmd.id)?.idNumber:undefined;
    const g=upsert(k,cmd.guest,cmd.id,!!cmd.verified);
    const idNote=g.idNumber&&g.idNumber!==before?' · ID number '+(before?'changed':'recorded'):'';
    if(bk){link(bk,g);if(bk.guest!==g.name&&bk.status==='Confirmed'){bk.guest=g.name;}}
    return `${g.id} · ${g.name}${cmd.id?' · profile updated':' · profile created'}${g.verified?' · verified':''}${idNote}${bk?' · linked to '+bk.id:''}`;
  },
  /** Deliberate "Show ID" (DEC-068): logs who/when in the audit trail — never the number. {id:guestId}. Intended PERM-GST-003 (guest.idDetails.view). */
  GUEST_ID_VIEW:k=>{
    k.req('checkin');const g=(k.s.guests||[]).find(x=>x.id===k.cmd.id);k.assert(g,'Guest profile not found.','NOT_FOUND');
    k.assert(g!.idNumber,'The full ID number is not on file for this guest.','NOT_FOUND');
    return `${g!.id} · ID number viewed (${g!.idType})`;
  },
  /** Verify registration. Backward compatible: REGISTER {id} with no details auto-creates a minimal demo-verified profile from the booking name. */
  REGISTER:k=>{
    k.req('checkin');const x=k.b();k.mutable(x);const s=k.s;s.guests=s.guests||[];
    let g:Guest|undefined=x.guestId?s.guests.find(g=>g.id===x.guestId):undefined;
    if(k.cmd.guest&&typeof k.cmd.guest==='object'){
      validate(k,k.cmd.guest,true);g=upsert(k,k.cmd.guest,k.cmd.guestId||g?.id,true);link(x,g);
    }else if(!g){
      g={id:k.id('G'),name:x.guest,phone:'',email:x.email||'',address:'Demo address (fictional)',nationality:'Indian',idType:'Demo verification',idLast4:'DEMO',
        preferences:'',notes:'Minimal demo profile created at registration from the booking name.',party:[],verified:true,origin:'auto',version:1};
      s.guests.push(g);x.guestId=g.id;
    }else{
      // Linked profile without new details: it must already satisfy the registration requirements.
      validate(k,g,true);g.verified=true;g.version=(g.version||1)+1;
    }
    x.registered=true;x.version++;return x.guest+' · fictional registration verified';
  },
  /** Check in (WF-STAY-001). Same rules and messages as M1; starts the first room segment. Intended PERM-STY-001. */
  CHECKIN:k=>{
    const {s}=k;k.req('checkin');k.open();const x=k.b(),r=s.rooms.find(r=>r.id===x.room)!;
    k.assert(x.status==='Confirmed','Only a confirmed reservation can check in.','INVALID_TRANSITION');
    k.assert(x.arrival<=s.date&&x.departure>s.date,'Check-in must fall within the booked dates.','INVALID_TRANSITION');
    k.assert(x.registered,'Verify the fictional guest registration first.');
    k.assert(!blockedTonight(s,r)&&r.hk==='Ready'&&!occupant(s,r.id),'The room must be vacant, ready and unblocked.','NOT_AVAILABLE');
    k.assert(available(s,r.id,x.arrival,x.departure,x.id),'The room allocation conflicts with another booking.','NOT_AVAILABLE');
    x.status='In house';x.segments=[{room:r.id,from:s.date}];x.version++;return x.guest+' → room '+r.id;
  },
  /** Move room / extend / shorten. Confirmed bookings behave exactly as M1. In-house: target validated (reserved) before the old room
   *  is released; old segment closed, new one opened; old room Dirty + one HK task. Any failure throws before mutation (AC-009). Intended PERM-STY-002. */
  MOVE:k=>{
    const {s,cmd}=k;k.req('reservations');k.open();const x=k.b();k.mutable(x);
    const r=s.rooms.find(r=>r.id===cmd.room);k.assert(r,'Select a room.');
    const inHouse=x.status==='In house',arrival=inHouse?s.date:x.arrival;
    k.assert(cmd.departure>arrival&&nights(arrival,cmd.departure)<=30,'Departure must follow the active arrival date (maximum 30 nights).');
    k.assert(available(s,r!.id,arrival,cmd.departure,x.id),'Target room is unavailable for the requested stay.','NOT_AVAILABLE');
    const moving=inHouse&&r!.id!==x.room;
    if(moving)k.assert(r!.hk==='Ready'&&!occupant(s,r!.id),'Target room must be vacant and ready.','NOT_AVAILABLE');
    if(inHouse&&cmd.departure<x.departure)
      k.assert(!x.lines.some(l=>l.kind==='room'&&l.date>=cmd.departure),'Room nights are already posted on or after the new departure date. Reverse them before shortening.','INVALID_TRANSITION');
    // All checks passed — the target is now reserved for [arrival, departure); release the old room.
    if(moving){
      const segs=staySegments(x).map(g=>({...g}));const cur=segs.find(g=>!g.to);
      if(cur)cur.to=s.date;segs.push({room:r!.id,from:s.date});x.segments=segs;
      const old=s.rooms.find(o=>o.id===x.room)!;old.hk='Dirty';
      s.tasks.push({id:k.id('task'),room:old.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
    }else if(inHouse&&!x.segments?.length)x.segments=staySegments(x).map(g=>({...g}));
    const detail=`${x.id} · room ${x.room} → ${cmd.room}; departure ${cmd.departure}; agreed tariff preserved`;
    x.room=cmd.room;x.departure=cmd.departure;x.version++;return detail;
  },
};
