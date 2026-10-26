// HTTP adapter (production): the same client interfaces over /api/v1 (contracts §2–§3). Marker: hcc-http-adapter.
// Routes (Wave 1 integration, verified against the .NET API): GET /system/health, GET /me/capabilities (P1), GET /availability,
// POST /commands/QuoteStay, POST /commands/CreateReservation, GET /reservations/{id} (P2). Session: HttpOnly cookie from
// POST /auth/sign-in (credentials:'include'); no session → 401 UNAUTHENTICATED. Server DTOs (./server.ts) are mapped here to
// the client types: room ids are room numbers and type ids type codes on the client, numeric ids on the server.
import {HccClientError,errorFromResponse} from './errors';
import {API_BASE,IDEMPOTENCY_HEADER,checkIdempotencyKey,checkWire,isIsoDate,newIdempotencyKey} from './wire';
import {createConnection,type Connection} from './connection';
import type {HccClients,ReservationsClient,SystemClient,MeClient,AvailabilityQuery,AvailabilityResult,AvailabilityReason,QuoteRequest,Quote,CreateReservationRequest,
  CreateReservationResult,BookingDetails,BookingStatusDto,HealthResult,Capabilities,CommandOptions} from './types';
import {SERVER_REASON,SERVER_STATUS,type SrvAvailability,type SrvQuote,type SrvCreateReservation,type SrvReservationResult,type SrvReservationDetails,
  type SrvCapabilities} from './server';

export const HTTP_ADAPTER_ID='hcc-http-adapter';
export type FetchLike=(input:string,init?:{method?:string;headers?:Record<string,string>;body?:string;credentials?:'include'|'same-origin'|'omit';signal?:AbortSignal})=>Promise<{status:number;ok:boolean;text():Promise<string>}>;
export type HttpOptions={
  /** Server origin, e.g. "https://hcc-server.local:8443"; '' = same origin (web app served by the API host). */
  baseUrl?:string;
  fetch?:FetchLike;
  /** Extra attempts for a command after a network failure / 503 — always with the same Idempotency-Key and body. Default 1. */
  commandRetries?:number;
  /** Shared reachability state (read-only banner). */
  connection?:Connection;
  timeoutMs?:number;
  /** Rooms the adapter already knows (server id ↔ room number ↔ type code); otherwise learned from availability / booking reads. */
  rooms?:{roomId:number;roomNo:string;roomType:string}[];
  /** Extra headers on every request (W4-0 auth seam: the APK's bearer token / device token, sync/auth.ts). */
  headers?:()=>Record<string,string>;
};
type Query=Record<string,string|number|boolean|undefined>;

export function createHttpTransport(o:HttpOptions={}){
  const doFetch:FetchLike=o.fetch??((input,init)=>(globalThis as unknown as {fetch:FetchLike}).fetch(input,init));
  const base=(o.baseUrl??'').replace(/\/+$/,'')+API_BASE;
  const connection=o.connection??createConnection();
  const retries=Math.max(0,o.commandRetries??1),timeoutMs=o.timeoutMs??15000;
  async function once<T>(method:'GET'|'POST',path:string,body:unknown,headers:Record<string,string>):Promise<T>{
    let res:{status:number;ok:boolean;text():Promise<string>};
    const ctl=typeof AbortController!=='undefined'?new AbortController():undefined;
    const timer=ctl?setTimeout(()=>ctl.abort(),timeoutMs):undefined;
    try{
      res=await doFetch(base+path,{method,headers:{Accept:'application/json',...(body!==undefined?{'Content-Type':'application/json'}:{}),...(o.headers?.()??{}),...headers},
        body:body===undefined?undefined:JSON.stringify(body),credentials:'include',signal:ctl?.signal});
    }catch(e){
      const msg='The hotel server cannot be reached. Check the network; nothing was saved on this device.';
      connection.markUnavailable(msg);throw new HccClientError('UNAVAILABLE',msg,{cause:String((e as Error)?.message??e)},0);
    }finally{if(timer)clearTimeout(timer);}
    const text=await res.text();
    let json:unknown=undefined;
    if(text){try{json=JSON.parse(text);}catch{if(res.ok)throw new HccClientError('PROTOCOL','The server sent a response that is not JSON.',undefined,res.status);}}
    if(!res.ok){
      const err=errorFromResponse(res.status,json);
      if(err.wireCode==='UNAVAILABLE')connection.markUnavailable(err.message);else connection.markOnline();
      throw err;
    }
    connection.markOnline();
    checkWire(json,'response');
    return json as T;
  }
  const qs=(q:Query)=>{const p=Object.entries(q).filter(([,v])=>v!==undefined&&v!=='').map(([k,v])=>`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);return p.length?'?'+p.join('&'):'';};
  return {
    connection,
    get:<T>(path:string,query:Query={})=>once<T>('GET',path+qs(query),undefined,{}),
    /** Plain POST without Idempotency-Key — only for non-command routes (POST /auth/sign-in, /auth/sign-out). Never retried. */
    post:<T>(path:string,body?:unknown)=>once<T>('POST',path,body,{}),
    /** POST /commands/<name> with Idempotency-Key; retried on UNAVAILABLE with the identical key and body. */
    async command<T>(name:string,body:Record<string,unknown>,opts:Partial<CommandOptions>={}):Promise<T>{
      const key=checkIdempotencyKey(opts.idempotencyKey??newIdempotencyKey(name));
      const payload=opts.expectedVersion!==undefined?{...body,expectedVersion:opts.expectedVersion}:body;
      checkWire(payload,'request');
      for(let attempt=0;;attempt++){
        try{return await once<T>('POST','/commands/'+name,payload,{[IDEMPOTENCY_HEADER]:key});}
        catch(e){if(!(e instanceof HccClientError)||e.wireCode!=='UNAVAILABLE'||attempt>=retries)throw e;}
      }
    },
  };
}
export type HttpTransport=ReturnType<typeof createHttpTransport>;

const need=(ok:unknown,msg:string)=>{if(!ok)throw new HccClientError('VALIDATION',msg);};
const stay=(a:string,d:string)=>{need(isIsoDate(a)&&isIsoDate(d),'Arrival and departure must be dates in YYYY-MM-DD.');need(d>a,'Departure must follow arrival.');};
const party=(adults:number,children=0)=>{need(Number.isSafeInteger(adults)&&adults>=1,'At least one adult.');need(Number.isSafeInteger(children)&&children>=0,'Children must be zero or more.');};
/** Result shape checks the UI depends on (beyond checkWire). */
function shape<T>(x:T,ok:(x:T)=>boolean,what:string):T{if(!x||typeof x!=='object'||!ok(x))throw new HccClientError('PROTOCOL',`The server response for ${what} is missing required fields.`);return x;}

/** Server room id ↔ client room id (= room number, as in the prototype), learned from availability / quote / booking reads. */
function roomDirectory(){
  const byNo=new Map<string,{id:number;type:string}>(),byId=new Map<number,string>();
  return {
    learn(id:number,no:string,type:string){byNo.set(no,{id,type});byId.set(id,no);},
    id:(no:string)=>byNo.get(no)?.id,type:(no:string)=>byNo.get(no)?.type,no:(id:number|null|undefined)=>id==null?undefined:byId.get(id),
  };
}
const num=(v:string,what:string)=>{const n=Number(v);need(Number.isSafeInteger(n)&&n>0,`${what} is not a server id.`);return n;};

export function createHttpReservationsClient(t:HttpTransport,known:HttpOptions['rooms']=[]):ReservationsClient{
  const rooms=roomDirectory();
  for(const r of known)rooms.learn(r.roomId,r.roomNo,r.roomType);
  async function availability(q:AvailabilityQuery):Promise<AvailabilityResult>{
    stay(q.arrival,q.departure);party(q.adults,q.children);
    // Client room-type ids are type codes ("Deluxe"); the server filters by numeric id, so a code is filtered here.
    const byServerId=q.roomTypeId!==undefined&&/^\d+$/.test(q.roomTypeId);
    const r=shape(await t.get<SrvAvailability>('/availability',{arrival:q.arrival,departure:q.departure,adults:q.adults,children:q.children??0,
      roomTypeId:byServerId?q.roomTypeId:undefined,ratePlanId:q.ratePlanId}),
      x=>Array.isArray(x.rooms)&&x.rooms.every(r=>typeof r.roomId==='number'&&typeof r.roomNo==='string'&&typeof r.available==='boolean'&&Array.isArray(r.reasons)),'availability');
    r.rooms.forEach(x=>rooms.learn(x.roomId,x.roomNo,x.roomType));
    const list=r.rooms.filter(x=>q.roomTypeId===undefined||byServerId||x.roomType===q.roomTypeId);
    return {arrival:r.arrival,departure:r.departure,nights:r.nights,party:r.party,valid:true,
      rooms:list.map(x=>({roomId:x.roomNo,roomTypeId:x.roomType,capacity:x.capacity,available:x.available,
        reasons:x.reasons.map(c=>(SERVER_REASON[c]??c) as AvailabilityReason),reasonText:x.heldForBlock?`Group hold · ${x.heldForBlock}`:undefined,
        nightlyRatePaise:x.nightlyRatePaise}))};
  }
  async function serverRoomId(roomNo:string,stayQ:{arrival:string;departure:string;adults:number;children?:number}){
    if(rooms.id(roomNo)===undefined)await availability({arrival:stayQ.arrival,departure:stayQ.departure,adults:stayQ.adults,children:stayQ.children});
    const id=rooms.id(roomNo);need(id!==undefined,`Room ${roomNo} is not known to the server.`);return id!;
  }
  return {
    getAvailability:availability,
    async quoteStay(q:QuoteRequest,opts={}){
      stay(q.arrival,q.departure);party(q.adults,q.children);need(q.roomId,'Choose a room.');
      const roomId=await serverRoomId(q.roomId,q);
      const r=shape(await t.command<SrvQuote>('QuoteStay',{arrival:q.arrival,departure:q.departure,adults:q.adults,children:q.children??0,roomId,ratePlanId:q.ratePlanId},opts),
        x=>typeof x.quoteId==='number'&&typeof x.version==='string'&&Array.isArray(x.nights),'the quote');
      const no=rooms.no(r.roomId)??q.roomId;
      return {quoteId:String(r.quoteId),version:r.version,roomId:no,roomTypeId:rooms.type(no)??String(r.roomTypeId),arrival:r.arrival,departure:r.departure,
        adults:r.adults,children:r.children,nights:r.nights,netPaise:r.netPaise,taxPaise:r.taxPaise,totalPaise:r.totalPaise,expiresAt:r.expiresAt,
        violations:(r.violations??[]).map(v=>({code:v.code,message:v.message}))} satisfies Quote;
    },
    async createReservation(r:CreateReservationRequest,opts:CommandOptions){
      need(opts&&opts.idempotencyKey,'An Idempotency-Key is required for CreateReservation.');
      need(r.quoteId&&r.quoteVersion,'A current quote is required.');need(String(r.guest?.name??'').trim().length>=2,'Enter the guest’s name.');
      const roomId=rooms.id(r.roomId);need(roomId!==undefined,'Quote the room before booking it.');
      // The company is a real server field (W4 G5). Guest e-mail and OTA channel are not on the server contract yet — kept in notes.
      const extra=[r.guest.email&&`E-mail: ${r.guest.email}`,r.otaChannel&&`OTA: ${r.otaChannel}`].filter(Boolean).join(' · ');
      const company=r.company&&r.company.name.trim()?{name:r.company.name.trim(),...(r.company.gstin?.trim()?{gstin:r.company.gstin.trim()}:{})}:undefined;
      const body:SrvCreateReservation={quoteId:num(r.quoteId,'Quote'),quoteVersion:r.quoteVersion,guest:{name:r.guest.name,phone:r.guest.phone},roomId:roomId!,
        hold:r.hold,restrictionOverride:r.restrictionOverride,source:r.source,notes:[r.notes,extra].filter(Boolean).join(' · ')||undefined,company};
      const res=shape(await t.command<SrvReservationResult>('CreateReservation',body as unknown as Record<string,unknown>,opts),
        x=>typeof x.reservationId==='number'&&typeof x.version==='string','the reservation');
      return {reservationId:String(res.reservationId),publicNo:res.publicNo,version:res.version} satisfies CreateReservationResult;
    },
    async getBooking(id:string){
      need(id,'Booking id is required.');
      const b=shape(await t.get<SrvReservationDetails>('/reservations/'+num(id,'Booking')),x=>typeof x.reservationId==='number'&&typeof x.roomNo==='string','the booking');
      rooms.learn(b.roomId,b.roomNo,b.roomType);
      return {reservationId:String(b.reservationId),publicNo:b.publicNo,version:b.version,status:(SERVER_STATUS[b.status]??b.status) as BookingStatusDto,
        guest:{name:b.guestName,phone:b.phone??undefined},roomId:b.roomNo,roomTypeId:b.roomType,arrival:b.arrival,departure:b.departure,adults:b.adults,children:b.children,
        source:b.source,nightlyRatePaise:b.nightlyRatePaise??b.nights[0]?.ratePaise??0,holdUntil:b.holdUntil??undefined,
        ...(b.company?.name?{company:{name:b.company.name,...(b.company.gstin?{gstin:b.company.gstin}:{})}}:{}),
        totals:{netPaise:b.netPaise,taxPaise:b.taxPaise,totalPaise:b.totalPaise}} satisfies BookingDetails;
    },
  };
}
export const createHttpSystemClient=(t:HttpTransport):SystemClient=>({health:()=>t.get<HealthResult>('/system/health')});
export const createHttpMeClient=(t:HttpTransport):MeClient=>({async capabilities(){
  const c=shape(await t.get<SrvCapabilities>('/me/capabilities'),x=>typeof x.userId==='number'&&Array.isArray(x.permissions),'capabilities');
  return {userId:String(c.userId),displayName:c.displayName,role:c.roles[0],roles:c.roles,permissions:c.permissions.map(p=>p.permissionId),grants:c.permissions,
    fieldMasks:c.fieldMasks} satisfies Capabilities;
}});

export function createHttpClients(o:HttpOptions={}):HccClients&{connection:Connection;adapter:typeof HTTP_ADAPTER_ID}{
  const t=createHttpTransport(o);
  return {kind:'http',adapter:HTTP_ADAPTER_ID,connection:t.connection,reservations:createHttpReservationsClient(t,o.rooms),system:createHttpSystemClient(t),me:createHttpMeClient(t)};
}
