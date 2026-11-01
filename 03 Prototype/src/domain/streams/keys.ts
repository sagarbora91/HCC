// Room keys (HCC_KeyAgent_Spec_v1) — pure helpers, selectors and the state slice type. Exported names MUST start with 'key' / 'KEY' (shared mock namespace).
// The prototype plays both sides: staff commands write jobs, a simulated front-desk agent (KEY_AGENT_STEP, the keyagent MockEncoder) works them.
import type {State} from '../types';
import {can} from '../selectors';
import {addMinutes} from '../money';

export type KEYEvent='issued'|'reissued'|'extended'|'reactivated'|'cancelled'|'read';
export type KEYReason='extension'|'lost'|'damaged'|'other';
export type KEYKind='issue_guest'|'extend'|'cancel'|'read'|'manual';
export type KEYState='queued'|'picked'|'waiting_card'|'done'|'failed'|'expired';
export const KEY_EVENTS:KEYEvent[]=['issued','reissued','extended','reactivated','cancelled','read'];
export const KEY_REASONS:KEYReason[]=['extension','lost','damaged','other'];
export const KEY_EVENT_LABEL:Record<KEYEvent,string>={issued:'Issued',reissued:'Re-issued',extended:'Extended',reactivated:'Reactivated',cancelled:'Cancelled',read:'Read'};
export const KEY_REASON_LABEL:Record<KEYReason,string>={extension:'Stay extension',lost:'Lost card',damaged:'Damaged card',other:'Other'};
export const KEY_STATE_LABEL:Record<KEYState,string>={queued:'Waiting for the encoder',picked:'Encoder picked it up',waiting_card:'Place the card on the encoder',done:'Done',failed:'Failed',expired:'Expired (not picked up in 2 minutes)'};
/** Queue time before a job expires (spec §6.2) and the heartbeat age after which the encoder counts as offline (§6.1). */
export const KEY_QUEUE_MINUTES=2,KEY_HEARTBEAT_SECONDS=30;
/** BIS Speed Check-in fixes departure at 12:00 (days only). */
export const KEY_CARD_TIME='12:00';
export const KEY_RESULT_HINT:Record<string,string>={
  NoLockRoom:'This room has no electronic lock: issue the manual key.',
  EncoderOffline:'Key service offline - use BIS Hotel manually, then record "key issued manually".',
  LoginRequired:'Key service offline - use BIS Hotel manually, then record "key issued manually".',
  NotSupported:'This encoder cannot do that yet: re-issue a card instead.',
  NoCard:'Place a card on the encoder, then retry.',
  Expired:'No encoder took the job. Check the encoder status, then retry or record the key manually.',
};

export type KEYLock={hasLock:boolean;lockNo?:number;doorName?:string;note?:string};
export type KEYJob={id:string;event:KEYEvent;reason?:KEYReason;kind:KEYKind;booking?:string;room?:string;state:KEYState;code?:string;message?:string;cardNo?:string;
  by:string;at:string;timeoutAt:string;/** key-minute (KEYSlice.kmin) after which a queued job expires */timeoutK?:number;finishedAt?:string;validTo?:string;warning?:string;note?:string};
export type KEYCard={id:string;job:string;event:KEYEvent;reason?:KEYReason;cardNo?:string;booking?:string;room:string;from:string;to:string;by:string;at:string;status:'active'|'superseded'|'checked_out'|'lost'|'manual'};
export type KEYSim={cardPresent:boolean;failNext:boolean;supportsExtend:boolean;serial:number};
/** kmin = key minutes: advanced only by KEY_CLOCK (the demo clock), because the simulated business clock ticks one minute per command. Job expiry and the heartbeat age are measured in kmin. */
export type KEYSlice={locks:Record<string,KEYLock>;encoderOnline:boolean;encoderSeen?:string;encoderSeenK?:number;kmin:number;jobs:KEYJob[];cards:KEYCard[];sim:KEYSim;seq:number};
export const keyEmpty=():KEYSlice=>({locks:{},encoderOnline:false,kmin:0,jobs:[],cards:[],sim:{cardPresent:true,failNext:false,supportsExtend:false,serial:846716912},seq:0});
export const keySlice=(s:State):KEYSlice=>{const x=s.keysX as Partial<KEYSlice>|undefined;const e=keyEmpty();return {locks:x?.locks??e.locks,encoderOnline:x?.encoderOnline??false,encoderSeen:x?.encoderSeen,encoderSeenK:x?.encoderSeenK,kmin:x?.kmin??0,jobs:x?.jobs??e.jobs,cards:x?.cards??e.cards,sim:{...e.sim,...(x?.sim??{})},seq:x?.seq??0};};

// ── Access (server: PERM-KEY-001…004; Manager and Owner hold all four, no separate key login) ──
export const keyCanRead=(s:State,uid:string)=>can(s,uid,['checkin','checkout']);
export const keyCanIssue=(s:State,uid:string)=>can(s,uid,'checkin');
export const keyCanCancel=(s:State,uid:string)=>can(s,uid,'checkout');
export const keyCanConfigure=(s:State,uid:string)=>can(s,uid,'approve');

// ── Lock mapping (BIS doors 101-107, 201-214, 301-314; room 106 is the manual-key room; unknown rooms have no lock until the Manager maps them) ──
const BIS=new Set([...Array.from({length:7},(_,i)=>String(101+i)),...Array.from({length:14},(_,i)=>String(201+i)),...Array.from({length:14},(_,i)=>String(301+i))]);
export const keyBisDoor=(room:string)=>BIS.has(room);
export const keyDefaultHasLock=(room:string)=>BIS.has(room)&&room!=='106';
export function keyLockOf(s:State,room:string):KEYLock{
  const o=keySlice(s).locks[room];if(o)return o;
  const has=keyDefaultHasLock(room);
  return {hasLock:has,doorName:BIS.has(room)?room:undefined,note:room==='106'?'Owner ruling: no working lock, manual key':!BIS.has(room)?'No BIS door known; the Manager can map it':undefined};}

// ── Clock-derived state ──
export const keyEncoderOnline=(s:State)=>{const x=keySlice(s);return x.encoderOnline&&x.encoderSeenK!==undefined&&x.kmin-x.encoderSeenK<1;};
/** A queued job past its time-out reads as expired even before a command records it. */
export const keyJobState=(s:State,j:KEYJob):KEYState=>j.state==='queued'&&j.timeoutK!==undefined&&keySlice(s).kmin>=j.timeoutK?'expired':j.state;

// ── Validity and the visible warning ──
/** Card expiry: the last day at 12:00. */
export const keyCardExpiry=(lastDay:string)=>lastDay+'T'+KEY_CARD_TIME;
export const keyFolioCheckout=(s:State):string=>{const v=(s.settings as unknown as {checkout?:string}|undefined)?.checkout;return typeof v==='string'&&/^\d\d:\d\d$/.test(v)?v:'11:00';};
export function keyMismatchWarning(departure:string,checkout:string):string|undefined{
  if(checkout===KEY_CARD_TIME)return undefined;
  return `Card expires ${departure} ${KEY_CARD_TIME} but the folio check-out is ${checkout}: the card will work ${checkout<KEY_CARD_TIME?'past':'only until'} the folio check-out.`;}

// ── Counts (derived from the append-only job log: successful events only) ──
export type KEYCounts={issued:number;reissued:number;extended:number;reactivated:number;cancelled:number;read:number;/** extended + reactivated, the panel's "Reactivated" */reactivatedTotal:number};
export function keyCounts(s:State,by:{booking?:string;room?:string}):KEYCounts{
  const c={issued:0,reissued:0,extended:0,reactivated:0,cancelled:0,read:0,reactivatedTotal:0};
  for(const j of keySlice(s).jobs){if(j.state!=='done')continue;if(by.booking&&j.booking!==by.booking)continue;if(by.room&&j.room!==by.room)continue;c[j.event]++;}
  c.reactivatedTotal=c.extended+c.reactivated;return c;}
export const keyCountLine=(c:KEYCounts)=>`Issued ${c.issued}, Re-issued ${c.reissued}, Reactivated ${c.reactivatedTotal}`;
export const keyActiveCards=(s:State,booking:string)=>keySlice(s).cards.filter(c=>c.booking===booking&&(c.status==='active'||c.status==='manual'));
export const keyJobsOf=(s:State,booking:string)=>[...keySlice(s).jobs].filter(j=>j.booking===booking).reverse();

// ── Key log report ──
export type KEYLogFilter={from?:string;to?:string;room?:string;booking?:string;user?:string;event?:string};
export function keyLog(s:State,f:KEYLogFilter={}){
  const rows=[...keySlice(s).jobs].reverse().filter(j=>(!f.from||j.at.slice(0,10)>=f.from)&&(!f.to||j.at.slice(0,10)<=f.to)&&(!f.room||j.room===f.room)&&(!f.booking||j.booking===f.booking)&&(!f.user||j.by===f.user)&&(!f.event||j.event===f.event));
  const totals={issued:0,reissued:0,extended:0,reactivated:0,cancelled:0,read:0,reactivatedTotal:0};
  for(const j of rows)if(j.state==='done')totals[j.event]++;
  totals.reactivatedTotal=totals.extended+totals.reactivated;
  return {rows,totals,total:rows.length};}

// ── Mutations on a command draft (used by handlers/keys.ts and the check-out hook in commands.ts) ──
const own=(s:State):KEYSlice=>{const x=keySlice(s);s.keysX=x;return x;};
export function keyExpire(s:State){
  const x=own(s);
  for(const j of x.jobs)if(j.state==='queued'&&j.timeoutK!==undefined&&x.kmin>=j.timeoutK){j.state='expired';j.code='Expired';j.message=KEY_RESULT_HINT.Expired;j.finishedAt=s.clock;}
  return x;}
export type KEYEnqueue={event:KEYEvent;kind:KEYKind;reason?:KEYReason;booking?:string;room?:string;validTo?:string;warning?:string;note?:string;idem?:string};
/** Writes a job: failed at once for a room without a lock or an offline encoder (log row + manual-key hint), otherwise queued for the agent for two minutes. */
export function keyEnqueue(s:State,uid:string,a:KEYEnqueue):KEYJob{
  const x=keyExpire(s);
  if(a.idem){const dup=x.jobs.find(j=>j.note===a.idem);if(dup)return dup;}
  let code:string|undefined;
  if(a.room&&a.kind!=='read'&&!keyLockOf(s,a.room).hasLock)code='NoLockRoom';
  else if(!keyEncoderOnline(s))code='EncoderOffline';
  const j:KEYJob={id:'KEYJ-'+(++x.seq),event:a.event,kind:a.kind,reason:a.reason,booking:a.booking,room:a.room,state:code?'failed':'queued',code,message:code?KEY_RESULT_HINT[code]:undefined,
    by:uid,at:s.clock,timeoutAt:addMinutes(s.clock,KEY_QUEUE_MINUTES),timeoutK:x.kmin+KEY_QUEUE_MINUTES,finishedAt:code?s.clock:undefined,validTo:a.validTo,warning:a.warning,note:a.idem};
  x.jobs.push(j);return j;}
/** Check-out hook: queue a software cancel when the room has an active card. Never throws into check-out (the caller wraps it too). */
export function keyAfterCheckout(s:State,uid:string,booking:string){
  const b=s.bookings.find(b=>b.id===booking);if(!b)return;
  const x=own(s);
  if(!x.cards.some(c=>c.room===b.room&&c.status==='active'))return;
  keyEnqueue(s,uid,{event:'cancelled',kind:'cancel',booking,room:b.room,idem:'checkout:'+booking});}
/** One step of the simulated front-desk agent (the keyagent MockEncoder semantics): pick, wait for a card, or finish the oldest queued job. */
export function keyAgentStep(s:State,uid:string,stage:'pick'|'wait'|'finish'):string{
  const x=keyExpire(s);x.encoderOnline=true;x.encoderSeen=s.clock;x.encoderSeenK=x.kmin;
  let j=x.jobs.find(j=>j.state==='picked'||j.state==='waiting_card')??x.jobs.find(j=>j.state==='queued');
  if(!j)return 'Encoder idle: no job waiting';
  if(j.state==='queued')j.state='picked';
  if(stage==='pick')return `${j.id} picked up`;
  if(j.state==='picked')j.state='waiting_card';
  if(stage==='wait')return `${j.id} waiting for a card`;
  const sim=x.sim;let code='Ok',cardNo:string|undefined;
  if(j.kind==='issue_guest'||j.kind==='extend'){
    if(j.kind==='extend'&&!sim.supportsExtend)code='NotSupported';
    else if(j.room&&!keyDefaultHasLock(j.room)&&!keyLockOf(s,j.room).hasLock)code='NoLockRoom';
    else if(!sim.cardPresent)code='NoCard';
    else if(sim.failNext){sim.failNext=false;code='WriteFailed';}
    else cardNo=String(++sim.serial);
  }else if(j.kind==='cancel'){if(!x.cards.some(c=>c.room===j!.room&&c.status==='active'))code='NotFound';}
  else if(j.kind==='read'&&!sim.cardPresent)code='NoCard';
  j.finishedAt=s.clock;j.code=code;j.cardNo=cardNo;
  j.message=code==='Ok'?'ok':KEY_RESULT_HINT[code]??code;
  if(code!=='Ok'){j.state='failed';return `${j.id} failed: ${code}`;}
  j.state='done';
  if((j.kind==='issue_guest'||j.kind==='extend')&&j.room&&j.booking){
    const b=s.bookings.find(b=>b.id===j!.booking);
    const lastDay=j.validTo??b?.departure??s.date;
    for(const c of x.cards)if(c.room===j.room&&(c.status==='active'||c.status==='manual'))c.status=j.reason==='lost'?'lost':'superseded';
    x.cards.push({id:'KEYC-'+(++x.seq),job:j.id,event:j.event,reason:j.reason,cardNo,booking:j.booking,room:j.room,from:s.clock,to:keyCardExpiry(lastDay),by:j.by,at:s.clock,status:'active'});
  }else if(j.kind==='cancel')for(const c of x.cards)if(c.room===j.room&&c.status==='active')c.status='checked_out';
  return `${j.id} done`;}
