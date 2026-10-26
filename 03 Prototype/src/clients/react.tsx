// React binding for the feature clients. The adapter is chosen at BUILD time: `process.env.HCC_BUILD` is replaced by esbuild
// ("prototype" by default, "production" with `npm run build:production`), so the production bundle never contains the mock
// adapter and the prototype bundle never talks to a server.
import React,{useEffect,useState,useSyncExternalStore} from 'react';
import {WifiOff} from 'lucide-react';
import {useHcc,type Ctx} from '../app/context';
import {createMockClients} from './mock';
import {getSyncRuntime} from './sync/instance';
import {createConnection,type Connection,type ConnectionState} from './connection';
import {errorText} from './errors';
import {isIsoDate} from './wire';
import {availabilityView,EMPTY_AVAILABILITY,type AvailabilityView} from './view';
import type {HccClients,ReservationsClient,AvailabilityQuery,BookingDetails} from './types';

const ONLINE:ConnectionState={status:'online'};
const offlineStore=createConnection(); // mock builds: always online

/** Production: the typed clients share the sync runtime's transport, session and connection state (W4-0, sync/instance.ts;
 *  server origin from `hcc-server-url`, '' = same origin). */
let http:(HccClients&{connection:Connection})|undefined;
function httpClients(){if(!http){const rt=getSyncRuntime();http={...rt.clients,connection:rt.connection};}return http;}

/** Rejection used when the app's run() already showed the error toast. */
const REPORTED={reported:true};
function mockClients(ctx:Ctx,msg='',after?:()=>void):HccClients{
  return createMockClients({state:()=>ctx.s,uid:()=>ctx.uid,
    dispatch:cmd=>new Promise<void>((resolve,reject)=>{ctx.run(cmd,msg,{after:()=>{after?.();resolve();}}).then(ok=>{if(!ok)reject(REPORTED);},reject);})});
}

export const isProductionBuild=()=>process.env.HCC_BUILD==='production';

export function useClients():HccClients{
  const ctx=useHcc();
  return process.env.HCC_BUILD==='production'?httpClients():mockClients(ctx);
}
export function useConnection():ConnectionState{
  const store=process.env.HCC_BUILD==='production'?httpClients().connection:offlineStore;
  return useSyncExternalStore(store.subscribe,store.get,()=>ONLINE);
}

/**
 * Reservations client plus `commit`, which runs a command flow with the same feedback as the app's run(): one toast,
 * busy guard, nothing reported as saved unless the server (or execute()) committed it. Returns the result, or undefined.
 */
export function useReservations(){
  const ctx=useHcc();const conn=useConnection();
  const [pending,setPending]=useState(false);
  const readOnly=conn.status==='unavailable';
  const client=useClients().reservations;
  const commit=<R,>(flow:Flow<R>,msg:string,after?:(r:R)=>void)=>process.env.HCC_BUILD==='production'?commitHttp(ctx,pending,setPending,flow,msg,after):commitMock(ctx,flow,msg,after);
  return {client,commit,readOnly,busy:ctx.busy||pending};
}

// Top-level (not nested in the hook) so esbuild drops the unused one from each build flavour.
type Flow<R>=(c:ReservationsClient)=>Promise<R>;
async function commitHttp<R>(ctx:Ctx,pending:boolean,setPending:(v:boolean)=>void,flow:Flow<R>,msg:string,after?:(r:R)=>void):Promise<R|undefined>{
  if(pending)return undefined;
  setPending(true);
  try{
    const c=httpClients();
    if(c.connection.readOnly)await c.system.health(); // reconnect check first; throws UNAVAILABLE while still offline
    const r=await flow(c.reservations);
    // The only typed-client flow is a new booking (BOOK): the reservations area remembers the id and reloads its slices (W4-0).
    await getSyncRuntime().flowDone('BOOK',r);
    after?.(r);ctx.toast(msg||'Updated successfully');return r;
  }catch(e){ctx.toast(errorText(e),true);return undefined;}
  finally{setPending(false);}
}
/** Prototype: the command goes through the app's run() (fault simulation, duplicate-key notice, toast) via the mock adapter. */
async function commitMock<R>(ctx:Ctx,flow:Flow<R>,msg:string,after?:(r:R)=>void):Promise<R|undefined>{
  try{const r=await flow(mockClients(ctx,msg).reservations);after?.(r);return r;}
  catch(e){if(e!==REPORTED)ctx.toast(errorText(e),true);return undefined;}
}

const stayOk=(q:AvailabilityQuery)=>isIsoDate(q.arrival)&&isIsoDate(q.departure)&&q.departure>q.arrival;
/** Availability for a search, in the shape the booking screens render. Mock: computed during render (no loading state). */
export function useAvailability(q:AvailabilityQuery):{view:AvailabilityView;loading:boolean;error?:string}{
  const c=useClients().reservations;
  const sig=JSON.stringify(q);
  const [res,setRes]=useState<{sig:string;view?:AvailabilityView;error?:string}>({sig:''});
  useEffect(()=>{
    if(c.now||!stayOk(q))return;
    let live=true;
    c.getAvailability(q).then(r=>{if(live)setRes({sig,view:availabilityView(r)});},e=>{if(live)setRes({sig,error:errorText(e)});});
    return ()=>{live=false;};
  },[sig]);// eslint-disable-line react-hooks/exhaustive-deps
  if(c.now)return {view:availabilityView(c.now.getAvailability(q)),loading:false};
  if(!stayOk(q))return {view:EMPTY_AVAILABILITY,loading:false};
  return res.sig===sig?{view:res.view??EMPTY_AVAILABILITY,loading:false,error:res.error}:{view:EMPTY_AVAILABILITY,loading:true};
}
/** Booking details by id ('' = none). Mock: read during render. */
export function useBooking(id:string):{booking?:BookingDetails;loading:boolean;error?:string}{
  const c=useClients().reservations;
  const [res,setRes]=useState<{id:string;booking?:BookingDetails;error?:string}>({id:''});
  useEffect(()=>{
    if(c.now||!id)return;
    let live=true;
    c.getBooking(id).then(b=>{if(live)setRes({id,booking:b});},e=>{if(live)setRes({id,error:errorText(e)});});
    return ()=>{live=false;};
  },[id]);// eslint-disable-line react-hooks/exhaustive-deps
  if(c.now)return {booking:id?c.now.getBooking(id):undefined,loading:false};
  if(!id)return {loading:false};
  return res.id===id?{booking:res.booking,loading:false,error:res.error}:{loading:true};
}

/** Production only: shown while the server is unreachable — the app is read-only and nothing is saved on the device. */
export function ReadOnlyBanner(){
  const conn=useConnection();const [checking,setChecking]=useState(false);
  if(conn.status!=='unavailable')return null;
  const retry=()=>{setChecking(true);getSyncRuntime().refresh().catch(()=>undefined).finally(()=>setChecking(false));};
  return <div className="info-note read-only-banner" role="alert"><WifiOff size={16}/>
    <span><strong>Server unavailable — read-only.</strong> Bookings and payments are disabled until the hotel server is back. Nothing is saved on this device.</span>
    <button type="button" className="btn" disabled={checking} onClick={retry}>{checking?'Checking…':'Retry'}</button></div>;
}
