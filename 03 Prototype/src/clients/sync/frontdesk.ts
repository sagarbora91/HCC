// Front desk area (W4-1): guests, registration, check-in / check-out, room move / extend / shorten, guest profile edits.
// (Bookings, BOOK, CHANGE_DATES, CANCEL, CONFIRM live in sync/reservations.ts; customers 360 is derived from `bookings` + `guests`.)
// Server: GET /guests (W4-1 list), POST /commands/RegisterGuest, CheckIn, CheckoutStay, MoveRoom, ExtendStay, ShortenStay,
// SaveGuestProfile (W4-1).
//
//   loaders   guests ← GET /guests (all pages, 500 each, at most 10 pages). Masked values stay masked ("Restricted", ••••1041); the full ID
//             number is never loaded ("Show ID" calls RevealGuestId directly — see guestid.ts).
//   commands  REGISTER   → RegisterGuest (guest draft, or only the chosen guestId; reservation + guest versions from the load)
//             CHECKIN    → CheckIn
//             CHECKOUT   → CheckoutStay (stay version; companyCreditApprovalId when the command carries one). Folio settlement is
//                          enforced by the server (money area W4-3 may take this mapper over — one type, one owner). Then IssueInvoice
//                          (step "invoice"); if that is refused the checkout still counts: reload + `warning` (W4 D1).
//             MOVE       → Confirmed/Held booking: AmendReservation (room and/or departure, tariff kept);
//                          In house: MoveRoom (step "room") then ExtendStay / ShortenStay (step "depart") with the stay version
//                          returned by the first call.
//             GUEST_SAVE → with a booking and `verified`: RegisterGuest; otherwise SaveGuestProfile (create / edit, versioned).
//   Customers 360 has no command of its own in the prototype (read only); the server's CRM commands (tags, notes, consent, merge)
//   have no prototype screen yet — see `unmapped`.
import type {Command,State} from '../../services/mock';
import {HccClientError} from '../errors';
import {isIsoDate} from '../wire';
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey} from './types';
import {serverId} from './map';
import {allPages,bookingOf,roomIdOf,versionOf,type SrvGuestPage} from './frontdesk/shared';
import {companyCreditApprovalId,issueInvoiceAfterCheckout,invoiceStepWarning,FOLIO_SLICES} from './money';

type Guest=NonNullable<State['guests']>[number];

async function loadGuests(c:LoaderCtx):Promise<Guest[]>{
  const items=await allPages(async page=>{const r=await c.api.get<SrvGuestPage>('/guests',{page,pageSize:500});return {items:r.items,total:r.total};});
  return items.map(g=>{
    const id=String(g.guestId);c.ids.learn('guest',id,g.guestId);c.ids.setVersion('guest',id,g.version);
    const out:Guest={id,name:g.name,phone:g.phone,email:g.email??'',address:g.address??'',nationality:g.nationality,idType:g.idType??'',idLast4:g.idLast4??'',
      preferences:g.preferences??'',notes:g.notes??'',party:g.party.map(p=>({name:p.name,ageBand:p.ageBand,relation:p.relation??''})),verified:g.verified,
      origin:g.origin==='auto'?'auto':'manual',version:1};
    if(g.company)out.company=g.company;if(g.gstin)out.gstin=g.gstin;
    return out;
  }).sort((a,b)=>Number(a.id)-Number(b.id));
}

const MASKED=/[•*]/;
/** Guest draft (prototype) → GuestDraftDto. A masked placeholder ("Restricted") is never sent back as a value. */
function guestDto(d:Record<string,unknown>):Record<string,unknown>{
  const s=(v:unknown)=>{const t=String(v??'').trim();return t===''||t==='Restricted'?undefined:t;};
  const party=Array.isArray(d.party)?(d.party as {name?:unknown;ageBand?:unknown;relation?:unknown}[]).map(p=>({name:String(p.name??''),ageBand:String(p.ageBand??''),relation:s(p.relation)})):undefined;
  return {name:s(d.name),phone:s(d.phone),email:s(d.email),address:s(d.address),nationality:s(d.nationality),idType:s(d.idType),idLast4:s(d.idLast4),
    company:s(d.company),gstin:s(d.gstin),preferences:s(d.preferences),notes:s(d.notes),party,idNumber:s(d.idNumber)};
}
const maskedContact=(d:Record<string,unknown>)=>MASKED.test(String(d.phone??''))||MASKED.test(String(d.email??''));
const MASK_MSG='Contact details are hidden for your role — ask someone with guest-registration access to save this profile.';

const stayIdOf=(c:Pick<CommandCtx,'ids'>,bookingId:string)=>{
  const s=c.ids.server('stay',bookingId);
  if(s===undefined)throw new HccClientError('VALIDATION','This stay is not in house on the server. Refresh and try again.');
  return s;
};

/** Booking edit from the stay dialogs: Confirmed/Held → AmendReservation; In house → MoveRoom + ExtendStay/ShortenStay. */
async function move(c:CommandCtx):Promise<void>{
  const cmd=c.cmd,id=String(cmd.id),b=bookingOf(c,id),room=String(cmd.room),departure=String(cmd.departure);
  if(b.status==='Confirmed'||b.status==='Held'){
    const roomId=await roomIdOf(c,room,b.arrival,departure);
    await c.api.command('AmendReservation',{reservationId:serverId(id,'Booking'),arrival:b.arrival,departure,roomId,priceDecision:'keep'},{expectedVersion:versionOf(c,'booking',id,'Booking')});
    return;
  }
  if(b.status!=='In house')throw new HccClientError('VALIDATION','Only a confirmed, held or in-house stay can be changed here.');
  const stayId=stayIdOf(c,id);let ver=versionOf(c,'stay',id,'Stay');
  const reason=String(cmd.reason??'').trim();
  if(room!==b.room){
    const toRoomId=await roomIdOf(c,room,c.state.date,departure);
    const r=await c.api.command<{stayVersion:string}>('MoveRoom',{stayId,toRoomId,reason:reason||'Room move',rateDecision:'keep'},{expectedVersion:ver,step:'room'});
    ver=r.stayVersion||ver;
  }
  if(departure!==b.departure){
    const longer=departure>b.departure;
    await c.api.command(longer?'ExtendStay':'ShortenStay',
      longer?{stayId,newDeparture:departure,...(reason?{reason}:{}),rateDecision:'keep'}:{stayId,newDeparture:departure,reason:reason||'Stay shortened'},{expectedVersion:ver,step:'depart'});
  }
}

export const frontdesk:SyncArea={
  id:'frontdesk',
  loaders:[{slice:'guests',requires:['reservations','checkin','checkout','billing'],reads:['GET /guests'],load:loadGuests}],
  commands:[{
    type:'REGISTER',reload:['bookings','guests'],
    precheck:cmd=>cmd.guest&&typeof cmd.guest==='object'&&maskedContact(cmd.guest as Record<string,unknown>)?MASK_MSG:undefined,
    async send(c){
      const cmd=c.cmd,id=String(cmd.id),b=bookingOf(c,id);
      const target=cmd.guestId?String(cmd.guestId):b.guestId;
      const body:Record<string,unknown>={reservationId:serverId(id,'Booking')};
      if(cmd.guest&&typeof cmd.guest==='object'){
        body.guest=guestDto(cmd.guest as Record<string,unknown>);
        if(target){body.guestId=serverId(target,'Guest');body.guestExpectedVersion=versionOf(c,'guest',target,'Guest profile');}
      }else if(cmd.guestId)body.guestId=serverId(cmd.guestId,'Guest');
      await c.api.command('RegisterGuest',body,{expectedVersion:versionOf(c,'booking',id,'Booking')});
    },
  },{
    type:'CHECKIN',reload:['bookings','guests'],
    async send(c){
      const id=String(c.cmd.id);bookingOf(c,id);
      await c.api.command('CheckIn',{reservationId:serverId(id,'Booking')},{expectedVersion:versionOf(c,'booking',id,'Booking')});
      return {reload:['rooms','tasks']};
    },
  },{
    type:'CHECKOUT',reload:['bookings'],
    async send(c){
      const id=String(c.cmd.id);bookingOf(c,id);
      // Money (W4-3): the approved company credit for exactly the balance (DEC-041) unless the dialog names one; after the checkout the
      // tax invoice is issued (key <action>:invoice) and the folio slices reload.
      const given=c.cmd.companyCreditApprovalId;
      const approval=given?serverId(given,'Approval'):companyCreditApprovalId(c.state,id);
      await c.api.command('CheckoutStay',{stayId:stayIdOf(c,id),...(approval?{companyCreditApprovalId:approval}:{})},{expectedVersion:versionOf(c,'stay',id,'Stay')});
      // W4 D1: the checkout has happened on the server. An invoice refusal (e.g. no hotel GSTIN yet) must not report the checkout as
      // failed: the slices still reload (the stay shows Departed) and the screen shows the warning; "Issue invoice" retries later.
      const reload:SliceKey[]=['rooms','tasks',...FOLIO_SLICES];
      let invoice:unknown;
      try{invoice=await issueInvoiceAfterCheckout(c,id);}
      catch(e){if(e instanceof HccClientError&&e.wireCode==='UNAUTHENTICATED')throw e;return {reload,warning:invoiceStepWarning(e)};}
      return {reload,...(invoice!==undefined?{result:{invoice}}:{})};
    },
  },{
    type:'MOVE',reload:['bookings'],
    precheck:(cmd:Command,s:State)=>{
      const b=s.bookings.find(x=>x.id===String(cmd.id));
      if(!cmd.room)return 'Select a room.';
      if(!isIsoDate(cmd.departure))return 'Departure must be a date (YYYY-MM-DD).';
      return b&&b.room===cmd.room&&b.departure===cmd.departure?'Nothing changed. Choose a different room or departure date.':undefined;
    },
    async send(c){await move(c);return {reload:['rooms','tasks']};},
  },{
    type:'GUEST_SAVE',reload:['guests','bookings'],
    precheck:cmd=>!cmd.guest||typeof cmd.guest!=='object'?'Enter the guest details.':maskedContact(cmd.guest as Record<string,unknown>)?MASK_MSG:undefined,
    async send(c){
      const cmd=c.cmd,draft=guestDto(cmd.guest as Record<string,unknown>),verified=!!cmd.verified;
      const gid=cmd.id?String(cmd.id):undefined;
      if(cmd.booking&&verified){
        // Linking a profile to a booking is registration on the server (RegisterGuest verifies it).
        const bid=String(cmd.booking),b=bookingOf(c,bid),target=gid??b.guestId;
        const body:Record<string,unknown>={reservationId:serverId(bid,'Booking'),guest:draft};
        if(target){body.guestId=serverId(target,'Guest');body.guestExpectedVersion=versionOf(c,'guest',target,'Guest profile');}
        await c.api.command('RegisterGuest',body,{expectedVersion:versionOf(c,'booking',bid,'Booking')});
        return;
      }
      const body:Record<string,unknown>={guest:draft,verified};
      if(gid){body.guestId=serverId(gid,'Guest');body.guestExpectedVersion=versionOf(c,'guest',gid,'Guest profile');}
      await c.api.command('SaveGuestProfile',body);
    },
  }],
  unmapped:{
    GUEST_ID_VIEW:'deliberately not a mapper: run() returns no result and the number must never enter state. Production uses guestid.ts / guestid-ui.tsx (GuestIdRevealButton calls RevealGuestId: PERM-GST-003, transient dialog, nothing stored)',
  },
};
