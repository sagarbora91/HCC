// HTTP adapter (production): the same client interfaces over /api/v1 (contracts §2–§3). Marker: hcc-http-adapter.
// Assumed routes (P1/P2 build them in parallel — reconcile): GET /system/health, GET /me/capabilities, GET /availability,
// POST /commands/QuoteStay, POST /commands/CreateReservation, GET /reservations/{id}.
import {HccClientError,errorFromResponse} from './errors';
import {API_BASE,IDEMPOTENCY_HEADER,checkIdempotencyKey,checkWire,isIsoDate,newIdempotencyKey} from './wire';
import {createConnection,type Connection} from './connection';
import type {HccClients,ReservationsClient,SystemClient,MeClient,AvailabilityQuery,AvailabilityResult,QuoteRequest,Quote,CreateReservationRequest,
  CreateReservationResult,BookingDetails,HealthResult,Capabilities,CommandOptions} from './types';

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
};
type Query=Record<string,string|number|undefined>;

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
      res=await doFetch(base+path,{method,headers:{Accept:'application/json',...(body!==undefined?{'Content-Type':'application/json'}:{}),...headers},
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

export function createHttpReservationsClient(t:HttpTransport):ReservationsClient{
  return {
    async getAvailability(q:AvailabilityQuery){
      stay(q.arrival,q.departure);party(q.adults,q.children);
      const r=await t.get<AvailabilityResult>('/availability',{arrival:q.arrival,departure:q.departure,adults:q.adults,children:q.children??0,roomTypeId:q.roomTypeId,ratePlanId:q.ratePlanId});
      return shape(r,x=>Array.isArray(x.rooms)&&x.rooms.every(r=>typeof r.roomId==='string'&&typeof r.available==='boolean'&&Array.isArray(r.reasons)),'availability');
    },
    async quoteStay(q:QuoteRequest,opts={}){
      stay(q.arrival,q.departure);party(q.adults,q.children);need(q.roomId,'Choose a room.');
      const r=await t.command<Quote>('QuoteStay',{arrival:q.arrival,departure:q.departure,adults:q.adults,children:q.children??0,roomId:q.roomId,ratePlanId:q.ratePlanId},opts);
      return shape(r,x=>typeof x.quoteId==='string'&&typeof x.version==='string'&&Array.isArray(x.nights),'the quote');
    },
    async createReservation(r:CreateReservationRequest,opts:CommandOptions){
      need(opts&&opts.idempotencyKey,'An Idempotency-Key is required for CreateReservation.');
      need(r.quoteId&&r.quoteVersion,'A current quote is required.');need(String(r.guest?.name??'').trim().length>=2,'Enter the guest’s name.');
      const res=await t.command<CreateReservationResult>('CreateReservation',{...r},opts);
      return shape(res,x=>typeof x.reservationId==='string'&&typeof x.version==='string','the reservation');
    },
    async getBooking(id:string){
      need(id,'Booking id is required.');
      const b=await t.get<BookingDetails>('/reservations/'+encodeURIComponent(id));
      return shape(b,x=>typeof x.reservationId==='string'&&!!x.totals,'the booking');
    },
  };
}
export const createHttpSystemClient=(t:HttpTransport):SystemClient=>({health:()=>t.get<HealthResult>('/system/health')});
export const createHttpMeClient=(t:HttpTransport):MeClient=>({capabilities:()=>t.get<Capabilities>('/me/capabilities')});

export function createHttpClients(o:HttpOptions={}):HccClients&{connection:Connection;adapter:typeof HTTP_ADAPTER_ID}{
  const t=createHttpTransport(o);
  return {kind:'http',adapter:HTTP_ADAPTER_ID,connection:t.connection,reservations:createHttpReservationsClient(t),system:createHttpSystemClient(t),me:createHttpMeClient(t)};
}
