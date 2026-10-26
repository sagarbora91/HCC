// Guest ID "Show ID" (DEC-068, production): the full number is fetched from the server on a deliberate click (POST /commands/RevealGuestId,
// PERM-GST-003, audited server-side) and held ONLY in this controller / a React component's local state for REVEAL_MS.
// It is never written to the runtime state, the read cache, IndexedDB, logs, toasts or an Idempotency record on the client.
// This is deliberately NOT a CommandMapper: ctx.run() returns only ok/not-ok and would reload slices; the reveal changes nothing locally.
// (GUEST_ID_VIEW therefore stays in `unmapped`: the prototype's run()-based flow is replaced by guestid-ui.tsx in the production build.)
import type {HttpTransport} from '../http';
import {errorText} from '../errors';
import type {SyncMe} from './types';

/** PERM id of the ID-view permission (server: GuestIdService.IdView). */
export const ID_VIEW_PERM='PERM-GST-003';
/** How long the number stays visible. */
export const REVEAL_MS=30_000;
export const canRevealGuestId=(me:Pick<SyncMe,'permIds'>|null|undefined):boolean=>!!me&&me.permIds.includes(ID_VIEW_PERM);

/** RevealGuestIdResult (08 Production/src/Hcc.Contracts/GuestIds). Only `formatted` and `idType` are kept by callers. */
type SrvReveal={guestId:number;idType:string;idNumber:string;formatted:string;idMasked:string};
export type Revealed={idType:string;formatted:string};

/** One reveal call with a fresh Idempotency-Key per click (a key is never reused, so each reveal is audited as its own event). */
export async function revealGuestId(t:Pick<HttpTransport,'command'>,guestId:string|number):Promise<Revealed>{
  const key=`idview-${guestId}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2,10)}`;
  const r=await t.command<SrvReveal>('RevealGuestId',{guestId:Number(guestId)},{idempotencyKey:key});
  return {idType:r.idType,formatted:r.formatted||r.idNumber};
}

export type RevealView={phase:'idle'}|{phase:'loading'}|{phase:'shown';value:Revealed}|{phase:'error';text:string};
export type RevealTimers={set:(f:()=>void,ms:number)=>unknown;clear:(h:unknown)=>void};

/** Framework-free controller: reveal → shown for `ms` → idle. The number lives only in `view` (closure); `hide()` drops it. */
export function createReveal(o:{reveal:()=>Promise<Revealed>;onChange:(v:RevealView)=>void;ms?:number;timers?:RevealTimers}){
  const timers=o.timers??{set:(f:()=>void,ms:number)=>setTimeout(f,ms),clear:(h:unknown)=>clearTimeout(h as never)};
  let timer:unknown,epoch=0,view:RevealView={phase:'idle'};
  const set=(v:RevealView)=>{view=v;o.onChange(v);};
  const stop=()=>{if(timer!==undefined){timers.clear(timer);timer=undefined;}};
  function hide(){epoch++;stop();if(view.phase!=='idle')set({phase:'idle'});}
  async function show(){
    if(view.phase==='loading')return;
    stop();const mine=++epoch;set({phase:'loading'});
    try{
      const value=await o.reveal();
      if(mine!==epoch)return; // hidden / unmounted while the call was in flight: drop the number
      set({phase:'shown',value});
      timer=timers.set(hide,o.ms??REVEAL_MS);
    }catch(e){if(mine===epoch)set({phase:'error',text:errorText(e)});}
  }
  return {show,hide,get:()=>view};
}
