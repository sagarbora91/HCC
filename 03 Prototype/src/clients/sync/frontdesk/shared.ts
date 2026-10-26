// Front desk (W4-1) shared helpers: the GET /reservations + GET /guests wire shapes and their mapping to the prototype's Booking and Guest.
// Used by sync/reservations.ts (bookings loader and booking commands) and sync/frontdesk.ts (guests loader, stay and guest commands).
import type {Booking,BookingStatus,State} from '../../../services/mock';
import {HccClientError} from '../../errors';
import {isIsoDate} from '../../wire';
import type {IdDirectory} from '../map';
import {utcToClock} from '../map';
import type {LoaderCtx,CommandCtx} from '../types';

/** Server reservation status → prototype BookingStatus. */
export const BOOKING_STATUS:Record<string,BookingStatus>={Held:'Held',Expired:'Expired',Confirmed:'Confirmed',InHouse:'In house',Departed:'Departed',Cancelled:'Cancelled',NoShow:'No show'};

/** GET /reservations item (08 Production/src/Hcc.Contracts/Reservations/ReservationContracts.cs `ReservationListItem`). */
export type SrvReservationItem={reservationId:number;publicNo:string;version:string;status:string;guestName:string;phone:string|null;roomId:number;roomNo:string;
  roomTypeId:number;roomType:string;arrival:string;departure:string;adults:number;children:number;source:string;nightlyRatePaise:number|null;holdUntil:string|null;
  groupBlockId:number|null;restrictionOverride:string|null;cancelReason:string|null;notes:string|null;company?:{name:string;gstin?:string|null}|null;guestId:number|null;registered:boolean;stayId:number|null;
  stayVersion:string|null;stayStatus:string|null;checkedInDate:string|null;checkedOutDate:string|null;segments:{roomNo:string;from:string;to:string|null}[]};
export type SrvReservationList={businessDate:string;total:number;page:number;pageSize:number;items:SrvReservationItem[]};

/** GET /guests item (`GuestView`; masked fields arrive masked and stay masked). */
export type SrvGuest={guestId:number;name:string;phone:string;email:string|null;address:string|null;nationality:string;idType:string|null;idLast4:string|null;
  company:string|null;gstin:string|null;party:{name:string;ageBand:string;relation:string|null}[];verified:boolean;masked:boolean;version:string;idMasked:string;
  idOnFile:boolean;canRevealId:boolean;preferences:string|null;notes:string|null;origin:string};
export type SrvGuestPage={total:number;page:number;pageSize:number;items:SrvGuest[]};

/** Reads every page (500 per page, at most `maxPages`), in server order. */
export async function allPages<T>(read:(page:number)=>Promise<{items:T[];total:number}>,maxPages=10):Promise<T[]>{
  const out:T[]=[];
  for(let page=1;page<=maxPages;page++){const r=await read(page);out.push(...r.items);if(r.items.length===0||out.length>=r.total)break;}
  return out;
}

/** BOOK stores the guest e-mail and OTA channel in the server's free-text notes (clients/http.ts createReservation):
 *  "<notes> · E-mail: x · OTA: y". Read them back so the prototype fields are filled again. Bookings made before the company became a real
 *  server field (W4 G5) also carry " · Company: name · GSTIN g" there: still read (fallback) and kept out of the notes. */
export function parseBookingNotes(raw:string|null|undefined):{notes:string;email?:string;otaChannel?:string;company?:{name:string;gstin?:string}}{
  const parts=String(raw??'').split(' · ');const keep:string[]=[];let email:string|undefined,ota:string|undefined,company:{name:string;gstin?:string}|undefined;
  for(let i=0;i<parts.length;i++){
    const p=parts[i];
    if(p.startsWith('E-mail: ')&&!email)email=p.slice(8).trim();
    else if(p.startsWith('OTA: ')&&!ota)ota=p.slice(5).trim();
    else if(p.startsWith('Company: ')&&!company){company={name:p.slice(9).trim()};if(parts[i+1]?.startsWith('GSTIN ')){company.gstin=parts[i+1].slice(6).trim();i++;}}
    else keep.push(p);
  }
  return {notes:keep.join(' · '),email,otaChannel:ota,company};
}

/** The real server company (name + GSTIN) of a list item or details; undefined when none. */
export function companyOf(x:{company?:{name:string;gstin?:string|null}|null}):{name:string;gstin?:string}|undefined{
  const name=x.company?.name?.trim();if(!name)return undefined;
  const gstin=x.company?.gstin?.trim();return gstin?{name,gstin}:{name};
}

/** Server list item → prototype Booking. Folio lines/receipts are the money area's (patch loader); `version` stays neutral (the server version is in `ids`). */
export function bookingFromList(x:SrvReservationItem,emailOf:(guestId:string|undefined)=>string|undefined):Booking{
  const gid=x.guestId==null?undefined:String(x.guestId);const n=parseBookingNotes(x.notes);
  const b:Booking={id:String(x.reservationId),guest:x.guestName,email:emailOf(gid)??n.email??'',people:x.adults,room:x.roomNo,arrival:x.arrival,departure:x.departure,
    rate:x.nightlyRatePaise??0,source:x.source,status:BOOKING_STATUS[x.status]??(x.status as BookingStatus),registered:x.registered,lines:[],receipts:[],version:1,notes:n.notes};
  if(x.phone)b.phone=x.phone;if(x.children)b.children=x.children;if(x.holdUntil)b.holdUntil=utcToClock(x.holdUntil);
  if(n.otaChannel)b.otaChannel=n.otaChannel;
  const company=companyOf(x)??n.company;if(company)b.company=company;if(x.cancelReason)b.cancelReason=x.cancelReason;
  if(gid)b.guestId=gid;
  if(x.segments.length)b.segments=x.segments.map(g=>g.to?{room:g.roomNo,from:g.from,to:g.to}:{room:g.roomNo,from:g.from});
  return b;
}

/** Remember ids and versions of one listed reservation (booking, stay, guest, room). */
export function learnListed(x:SrvReservationItem,ids:IdDirectory):void{
  const id=String(x.reservationId);
  ids.learn('booking',id,x.reservationId);ids.setVersion('booking',id,x.version);ids.learn('room',x.roomNo,x.roomId);
  if(x.stayId!=null){ids.learn('stay',id,x.stayId);if(x.stayVersion)ids.setVersion('stay',id,x.stayVersion);}
  if(x.guestId!=null)ids.learn('guest',String(x.guestId),x.guestId);
}

/** The booking a command acts on, from the assembled state. */
export function bookingOf(c:{state:State},id:unknown):Booking{
  const b=c.state.bookings.find(x=>x.id===String(id));
  if(!b)throw new HccClientError('NOT_FOUND',`Booking ${id} is not loaded. Refresh and try again.`);
  return b;
}
/** The server version of a record loaded earlier (STALE_VERSION protection); refuses when nothing was loaded. */
export function versionOf(c:Pick<CommandCtx,'ids'>,kind:string,id:string,what:string):string{
  const v=c.ids.version(kind,id);
  if(!v)throw new HccClientError('VALIDATION',`${what} ${id} is not loaded from the server. Refresh and try again.`);
  return v;
}
/** Numeric server room id of a room number; asks GET /availability for the stay dates when no loader has seen the room yet. */
export async function roomIdOf(c:Pick<CommandCtx,'ids'|'api'>,roomNo:string,arrival:string,departure:string):Promise<number>{
  let id=c.ids.server('room',roomNo);
  if(id===undefined&&isIsoDate(arrival)&&isIsoDate(departure)&&departure>arrival){
    const r=await c.api.get<{rooms:{roomId:number;roomNo:string}[]}>('/availability',{arrival,departure,adults:1,children:0});
    for(const x of r.rooms)c.ids.learn('room',x.roomNo,x.roomId);
    id=c.ids.server('room',roomNo);
  }
  if(id===undefined)throw new HccClientError('VALIDATION',`Room ${roomNo} is not known to the server.`);
  return id;
}
export type {LoaderCtx};
