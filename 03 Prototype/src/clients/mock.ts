// Mock adapter (prototype build): the client interfaces over today's in-browser mock service — selectors for reads,
// execute() (through the host's dispatch, i.e. the app's run()) for commands. Same figures and messages as the direct calls.
// Marker: hcc-mock-adapter. Never selected by the production build (see select.ts).
import type {State,Command,Booking} from '../services/mock';
import {resSearch,resStatus,rateFor,taxRateFor,occupant,yldStayViolations,totals,SCHEMA,nights as nightsBetween,addDays} from '../services/mock';
import {HccClientError} from './errors';
import type {HccClients,ReservationsClient,AvailabilityQuery,AvailabilityResult,AvailabilityReason,QuoteRequest,Quote,CreateReservationRequest,
  CreateReservationResult,BookingDetails,BookingStatusDto,CommandOptions} from './types';

export const MOCK_ADAPTER_ID='hcc-mock-adapter';
export type MockHost={
  state():State;uid():string;
  /** Executes one prototype command. Resolves when committed; rejects (HccError) otherwise. */
  dispatch(cmd:Command):Promise<void>;
};

const REASON:Record<string,AvailabilityReason>={'Blocked':'Blocked','Over capacity':'OverCapacity','Occupied':'Occupied','Booked':'Booked','Held':'Held'};
const QUOTE_PREFIX='mockq';
/** Mock quotes carry their request in the id, so no quote store is needed; version is always "1". */
export function mockQuoteId(q:{roomId:string;arrival:string;departure:string;adults:number;children?:number}){return [QUOTE_PREFIX,q.roomId,q.arrival,q.departure,q.adults,q.children??0].join('|');}
function decodeQuote(id:string){
  const p=String(id||'').split('|');
  if(p.length!==6||p[0]!==QUOTE_PREFIX)throw new HccClientError('NOT_FOUND','This quote is no longer available. Search again.');
  return {roomId:p[1],arrival:p[2],departure:p[3],adults:Number(p[4]),children:Number(p[5])};
}

function availability(s:State,q:AvailabilityQuery):AvailabilityResult{
  const r=resSearch(s,{arrival:q.arrival,departure:q.departure,people:q.adults,children:q.children,type:q.roomTypeId||'All'});
  const rows=[...r.available,...r.excluded];
  // Keep resSearch's room order (available first is a view concern; see availabilityView).
  const order=new Map(s.rooms.map((x,i)=>[x.id,i]));rows.sort((a,b)=>order.get(a.room.id)!-order.get(b.room.id)!);
  return {arrival:q.arrival,departure:q.departure,nights:r.valid?nightsBetween(q.arrival,q.departure):0,party:r.party,valid:r.valid,
    rooms:rows.map(x=>({roomId:x.room.id,roomTypeId:x.room.type,capacity:x.capacity,available:x.ok,reasons:x.reasons.map(c=>REASON[c]??'Booked'),
      reasonText:x.ok?undefined:x.reason,nightlyRatePaise:rateFor(s,x.room,q.arrival),occupiedNow:!!occupant(s,x.room.id)}))};
}

function quote(s:State,q:QuoteRequest):Quote{
  const room=s.rooms.find(r=>r.id===q.roomId);
  if(!room)throw new HccClientError('NOT_FOUND','Choose a room.');
  // The prototype keeps the arrival night's tariff for the whole stay (WF-BOOK-001: agreed nightly rate); tax per service date.
  const rate=rateFor(s,room,q.arrival),n=Math.max(0,nightsBetween(q.arrival,q.departure));
  const list=Array.from({length:n},(_,i)=>{const date=addDays(q.arrival,i),taxBps=taxRateFor(s,date);return {date,ratePaise:rate,taxBps,taxPaise:Math.round(rate*taxBps/10000)};});
  const net=list.reduce((a,x)=>a+x.ratePaise,0),tax=list.reduce((a,x)=>a+x.taxPaise,0);
  return {quoteId:mockQuoteId(q),version:'1',roomId:room.id,roomTypeId:room.type,arrival:q.arrival,departure:q.departure,adults:q.adults,children:q.children??0,
    nights:list,netPaise:net,taxPaise:tax,totalPaise:net+tax,violations:n?yldStayViolations(s,room.type,q.arrival,q.departure).map(v=>({code:v.code,message:v.message})):[]};
}

/** The prototype BOOK command for a CreateReservation request — the exact fields the dialogs used to send. */
export function bookCommandFor(r:CreateReservationRequest,key:string):Command{
  const q=decodeQuote(r.quoteId);
  if(q.roomId!==r.roomId)throw new HccClientError('VALIDATION','The quote is for another room. Search again.');
  return {type:'BOOK',guest:r.guest.name,phone:r.guest.phone,email:r.guest.email,people:q.adults,children:q.children,room:r.roomId,arrival:q.arrival,departure:q.departure,
    source:r.source,otaChannel:r.otaChannel,company:r.company,notes:r.notes,hold:r.hold,restrictionOverride:r.restrictionOverride,key};
}

export function bookingDetails(s:State,b:Booking):BookingDetails{
  const t=totals(b);
  return {reservationId:b.id,publicNo:b.id,version:String(b.version),status:resStatus(s,b) as BookingStatusDto,guest:{name:b.guest,phone:b.phone,email:b.email},
    roomId:b.room,roomTypeId:s.rooms.find(r=>r.id===b.room)?.type??'',arrival:b.arrival,departure:b.departure,adults:b.people,children:b.children??0,source:b.source,
    otaChannel:b.otaChannel,company:b.company,nightlyRatePaise:b.rate,holdUntil:b.holdUntil,registered:b.registered,
    totals:{netPaise:t.net,taxPaise:t.tax,totalPaise:t.total,paidPaise:t.paid,depositPaise:t.deposit,balancePaise:t.balance}};
}

export function createMockReservationsClient(h:MockHost):ReservationsClient{
  const getBookingNow=(id:string)=>{const s=h.state(),b=s.bookings.find(x=>x.id===id);return b?bookingDetails(s,b):undefined;};
  return {
    now:{getAvailability:q=>availability(h.state(),q),getBooking:getBookingNow},
    getAvailability:async q=>availability(h.state(),q),
    quoteStay:async q=>quote(h.state(),q),
    async createReservation(r:CreateReservationRequest,opts:CommandOptions):Promise<CreateReservationResult>{
      const cmd=bookCommandFor(r,opts.idempotencyKey);
      // execute() numbers bookings HCC-<sequence+1>; the dialogs predicted the id the same way before this client existed.
      const id='HCC-'+(h.state().sequence+1);
      await h.dispatch(cmd);
      const b=h.state().bookings.find(x=>x.id===id);
      return {reservationId:id,publicNo:id,version:String(b?.version??1)};
    },
    async getBooking(id){const b=getBookingNow(id);if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');return b;},
  };
}

export function createMockClients(h:MockHost):HccClients&{adapter:typeof MOCK_ADAPTER_ID}{
  return {kind:'mock',adapter:MOCK_ADAPTER_ID,reservations:createMockReservationsClient(h),
    system:{health:async()=>({status:'ok',database:'in-browser mock',schemaVersion:SCHEMA,serverTimeUtc:new Date(h.state().clock+':00+05:30').toISOString()})},
    me:{capabilities:async()=>{const s=h.state(),u=s.users.find(x=>x.id===h.uid());if(!u)throw new HccClientError('FORBIDDEN','Unknown demo user.');
      return {userId:u.id,displayName:u.name,role:u.role,permissions:u.enabled?[...(s.grants[u.role]||[])]:[],businessDate:s.date};}},
  };
}

/** Host for tests and non-React callers: keeps its own state and runs execute() directly. */
export function createLocalHost(initial:State,uid:string,execute:(s:State,uid:string,cmd:Command)=>State){
  let s=initial;let user=uid;
  return {state:()=>s,uid:()=>user,setUid:(u:string)=>{user=u;},dispatch:async(cmd:Command)=>{s=execute(s,user,cmd);}};
}
