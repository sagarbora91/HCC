// Reservations area — the W4-0 reference implementation (W4-1 extends it with the rest of the front desk).
// Server: GET /availability, POST /commands/QuoteStay, POST /commands/CreateReservation, GET /reservations/{id} (Wave 1 P2),
// through the typed client clients/http.ts (room ids = room numbers, type ids = type codes, ids as strings).
//
//   loaders   bookings ← GET /reservations/{id} for every booking this device knows (ids learned from CreateReservation +
//             the bookings already loaded). There is no list endpoint yet: W4-1 adds one (e.g. GET /reservations?from&to)
//             and replaces `knownIds` with it. Folio lines / receipts are NOT loaded here (money area W4-3 adds a
//             `kind:'patch'` loader on 'bookings'); registration / stays (registered, In house) come with W4-1's stays loader.
//   commands  BOOK → [GET /availability when the room id is not known yet] → QuoteStay (key `<action>:quote`) →
//             CreateReservation (key `<action>`), then reload `bookings`. The booking dialogs (desktop + phone wizard) run the
//             same flow through useReservations().commit (typed client) and report it with track() — same keys, same reload.
//   queries   availability and booking details for the dialogs stay on the typed client (clients/react.tsx useAvailability /
//             useBooking), which in production uses the runtime's shared transport.
import type {Booking,BookingStatus,Command} from '../../services/mock';
import type {BookingDetails} from '../types';
import {HccClientError} from '../errors';
import {isIsoDate} from '../wire';
import {quoteAndCreate,createRequestFromBook} from '../view';
import type {SyncArea,LoaderCtx} from './types';
import {utcToClock,serverId} from './map';

/** Server booking details (typed client DTO) → prototype Booking. Server-only fields the prototype does not have stay out;
 *  prototype-only fields get neutral values (version: the server's opaque version is kept in ids for expectedVersion). */
export function bookingFromServer(b:BookingDetails):Booking{
  return {id:b.reservationId,guest:b.guest.name,email:b.guest.email??'',phone:b.guest.phone,people:b.adults,children:b.children,room:b.roomId,
    arrival:b.arrival,departure:b.departure,rate:b.nightlyRatePaise,source:b.source,status:b.status as BookingStatus,registered:!!b.registered,
    lines:[],receipts:[],version:1,notes:'',holdUntil:b.holdUntil?utcToClock(b.holdUntil):undefined,otaChannel:b.otaChannel,company:b.company} as Booking;
}

/** Booking ids to read: learned on this device (CreateReservation) ∪ already loaded. Replace with a list endpoint (W4-1). */
const knownIds=(c:LoaderCtx)=>[...new Set([...c.ids.list('booking'),...(c.current('bookings')??[]).map(b=>b.id)])].filter(id=>/^\d+$/.test(id));

async function loadBookings(c:LoaderCtx):Promise<Booking[]>{
  const out:Booking[]=[];const list=knownIds(c);
  for(let i=0;i<list.length;i+=8){
    const part=await Promise.all(list.slice(i,i+8).map(id=>c.api.clients.reservations.getBooking(id).catch(e=>{
      if(e instanceof HccClientError&&e.wireCode==='NOT_FOUND')return null;throw e;})));
    for(const d of part)if(d){c.ids.setVersion('booking',d.reservationId,d.version);out.push(bookingFromServer(d));}
  }
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
  loaders:[{slice:'bookings',requires:['reservations','checkin','checkout','billing','payments'],load:loadBookings}],
  commands:[{
    type:'BOOK',reload:['bookings'],precheck:bookPrecheck,track:learnBooking,
    async send(c){
      const cmd=c.cmd;
      const r=await quoteAndCreate(c.api.clients.reservations,{arrival:cmd.arrival,departure:cmd.departure,adults:Number(cmd.people)||1,children:Number(cmd.children)||0},
        createRequestFromBook(cmd as unknown as Parameters<typeof createRequestFromBook>[0]),{idempotencyKey:c.key});
      learnBooking(r,c.ids);
      return {result:{reservationId:r.reservationId,publicNo:r.publicNo,version:r.version}};
    },
  }],
  unmapped:{
    AMEND:'W4-1 (AmendReservation K V)',CANCEL:'W4-1 (CancelReservation K V)',NOSHOW:'W4-1 (MarkNoShow K V)',CONFIRM_HOLD:'W4-1',
    REGISTER:'W4-1 (RegisterGuest)',CHECKIN:'W4-1 (CheckIn)',MOVE:'W4-1 (stay move)',CHECKOUT:'W4-1 / W4-3 (CheckoutStay)',
  },
};
