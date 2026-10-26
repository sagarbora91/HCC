// Reservations area — W4-0 reference implementation, extended by W4-1 (front desk) with the list read and the booking commands.
// Server: GET /availability, POST /commands/QuoteStay, CreateReservation, AmendReservation, CancelReservation, MarkNoShow, ConfirmHold,
// GET /reservations (W4-1 list: arrivals / in-house / departures / date range, with the stay-side facts), GET /reservations/{id}.
//
//   loaders   bookings ← GET /reservations?view=range&from=<business date − 180 d> (all pages, 500 each, at most 10 pages = 5 000 bookings).
//             Each item carries the linked guest, registration, stay (id, version) and room segments; company comes from the server's company field (older bookings: read
//             back from the notes), e-mail / OTA channel written into the server notes by BOOK are read back. Folio lines / receipts are NOT loaded here (money area W4-3 adds a
//             `kind:'patch'` loader on 'bookings'). Needs `guests` (the e-mail of the linked profile).
//   commands  BOOK → [GET /availability] → QuoteStay (key `<action>:quote`) → CreateReservation (key `<action>`); reload `bookings`.
//             CHANGE_DATES → AmendReservation (price decision keep: the agreed tariff is preserved), CANCEL → CancelReservation or
//             MarkNoShow (cmd.noShow), CONFIRM → ConfirmHold. Versions are the server versions recorded at load time.
//             MOVE (room / departure change of a booking) lives in sync/frontdesk.ts (it is a stay command once the guest is in house).
//   queries   availability and booking details for the dialogs stay on the typed client (clients/react.tsx useAvailability /
//             useBooking), which in production uses the runtime's shared transport.
import type {Booking,BookingStatus,Command} from '../../services/mock';
import type {BookingDetails} from '../types';
import {isIsoDate} from '../wire';
import {addDays} from '../../domain/money';
import {quoteAndCreate,createRequestFromBook} from '../view';
import type {SyncArea,LoaderCtx} from './types';
import {utcToClock,serverId} from './map';
import {allPages,bookingFromList,bookingOf,learnListed,roomIdOf,versionOf,type SrvReservationList} from './frontdesk/shared';

/** Server booking details (typed client DTO) → prototype Booking. Kept for the typed-client dialogs (single reads). */
export function bookingFromServer(b:BookingDetails):Booking{
  return {id:b.reservationId,guest:b.guest.name,email:b.guest.email??'',phone:b.guest.phone,people:b.adults,children:b.children,room:b.roomId,
    arrival:b.arrival,departure:b.departure,rate:b.nightlyRatePaise,source:b.source,status:b.status as BookingStatus,registered:!!b.registered,
    lines:[],receipts:[],version:1,notes:'',holdUntil:b.holdUntil?utcToClock(b.holdUntil):undefined,otaChannel:b.otaChannel,company:b.company} as Booking;
}

/** How far back the bookings window reaches (days before the business date). Customers 360 shows what is in this window. */
export const BOOKINGS_WINDOW_DAYS=180;

async function loadBookings(c:LoaderCtx):Promise<Booking[]>{
  const today=c.current('date');const from=isIsoDate(today)?addDays(today,-BOOKINGS_WINDOW_DAYS):undefined;
  const items=await allPages(async page=>{
    const r=await c.api.get<SrvReservationList>('/reservations',{view:'range',from,page,pageSize:500});
    return {items:r.items,total:r.total};
  });
  const emailOf=(gid:string|undefined)=>gid?(c.current('guests')??[]).find(g=>g.id===gid)?.email||undefined:undefined;
  const out:Booking[]=[];
  for(const x of items){learnListed(x,c.ids);out.push(bookingFromList(x,emailOf));}
  return out.sort((a,b)=>Number(a.id)-Number(b.id));
}

const learnBooking=(r:unknown,ids:LoaderCtx['ids'])=>{const id=(r as {reservationId?:string})?.reservationId;if(id&&/^\d+$/.test(id))ids.learn('booking',id,serverId(id,'Booking'));};

function bookPrecheck(cmd:Command):string|undefined{
  if(String(cmd.guest??'').trim().length<2)return 'Enter the guest’s name (at least 2 characters).';
  if(!cmd.room)return 'Choose a room.';
  if(!isIsoDate(cmd.arrival)||!isIsoDate(cmd.departure)||cmd.departure<=cmd.arrival)return 'Departure must follow arrival (dates in YYYY-MM-DD).';
  return undefined;
}

export const reservations:SyncArea={
  id:'reservations',
  loaders:[{slice:'bookings',needs:['date','guests'],requires:['reservations','checkin','checkout','billing','payments'],load:loadBookings}],
  commands:[{
    type:'BOOK',reload:['bookings'],precheck:bookPrecheck,track:learnBooking,
    async send(c){
      const cmd=c.cmd;
      const r=await quoteAndCreate(c.api.clients.reservations,{arrival:cmd.arrival,departure:cmd.departure,adults:Number(cmd.people)||1,children:Number(cmd.children)||0},
        createRequestFromBook(cmd as unknown as Parameters<typeof createRequestFromBook>[0]),{idempotencyKey:c.key});
      learnBooking(r,c.ids);
      return {result:{reservationId:r.reservationId,publicNo:r.publicNo,version:r.version}};
    },
  },{
    // Move / change dates of a Confirmed or Held reservation (prototype CHANGE_DATES) → AmendReservation, price decision "keep".
    type:'CHANGE_DATES',reload:['bookings'],
    precheck:cmd=>!cmd.room?'Select a room.':!isIsoDate(cmd.arrival)||!isIsoDate(cmd.departure)||cmd.departure<=cmd.arrival?'Departure must follow arrival (dates in YYYY-MM-DD).':undefined,
    async send(c){
      const cmd=c.cmd,id=String(cmd.id);bookingOf(c,id);
      const roomId=await roomIdOf(c,String(cmd.room),cmd.arrival,cmd.departure);
      const body:Record<string,unknown>={reservationId:serverId(id,'Booking'),arrival:cmd.arrival,departure:cmd.departure,roomId,priceDecision:'keep'};
      if(cmd.restrictionOverride)body.restrictionOverride=String(cmd.restrictionOverride);
      // The company (name + GSTIN) is a server field; only sent when the dialog carries one (a missing company leaves the stored one).
      const co=cmd.company as {name?:string;gstin?:string}|undefined;if(co&&String(co.name??'').trim())body.company={name:String(co.name).trim(),...(String(co.gstin??'').trim()?{gstin:String(co.gstin).trim()}:{})};
      await c.api.command('AmendReservation',body,{expectedVersion:versionOf(c,'booking',id,'Booking')});
    },
  },{
    // Cancel (also a running hold) or record a no-show (cmd.noShow) → CancelReservation / MarkNoShow.
    type:'CANCEL',reload:['bookings'],
    async send(c){
      const cmd=c.cmd,id=String(cmd.id);bookingOf(c,id);
      const reason=String(cmd.reason??'').trim();
      await c.api.command(cmd.noShow?'MarkNoShow':'CancelReservation',{reservationId:serverId(id,'Booking'),...(reason?{reason:reason.slice(0,200)}:{})},
        {expectedVersion:versionOf(c,'booking',id,'Booking')});
    },
  },{
    type:'CONFIRM',reload:['bookings'],
    async send(c){
      const id=String(c.cmd.id);bookingOf(c,id);
      await c.api.command('ConfirmHold',{reservationId:serverId(id,'Booking')},{expectedVersion:versionOf(c,'booking',id,'Booking')});
    },
  }],
  unmapped:{
    // MOVE, REGISTER, CHECKIN, CHECKOUT, GUEST_SAVE, GUEST_ID_VIEW: see sync/frontdesk.ts.
  },
};
