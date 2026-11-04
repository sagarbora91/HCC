// AF-SYNC-OPS (OPS-01) · Room keys in the production sync layer.
//   server  POST /commands/RequestKey | CancelKey | ReadKey | RecordManualKey | SaveKeyLock
//           GET /keys/log · /keys/locks · /keys/encoder · /keys/panel/{reservationId}   (Hcc.Api KeysEndpoints, contracts Hcc.Contracts/Keys)
// The prototype plays the front-desk encoder itself (KEY_AGENT_STEP, KEY_CLOCK, KEY_SIM, KEY_ENCODER): those stay unmapped, the real Key Agent
// runs beside the hotel PC and reports through the server. Events and reasons are the same words on both sides (KEY_EVENTS / KEY_REASONS =
// KeyEvents.All / KeyReasons.All), so they are sent as they are.
// Slice keysX is built from the server: jobs from the log (oldest first), locks by room number, encoder from /keys/encoder, active cards from
// the panel of the bookings that have a finished issue in the log (newest 25). Demo-only fields keep their defaults and are never shown.
import {HccClientError} from '../errors';
import {serverId,protoId,protoUserId,utcToClock} from './map';
import type {SyncArea,CommandCtx,LoaderCtx} from './types';
import type {KEYSlice,KEYJob,KEYCard,KEYLock,KEYEvent,KEYReason,KEYKind,KEYState} from '../../domain/streams/keys';

const text=(v:unknown)=>String(v??'').trim();
const KEY_PERM_READ='PERM-KEY-001',KEY_PERM_CONFIGURE='PERM-KEY-004';
const holds=(c:Pick<LoaderCtx,'me'>,perm:string)=>c.me.permIds.includes(perm);
const PANEL_BOOKINGS=25;

export type SrvKeyLogRow={jobId:string;requestedUtc:string;event:string;reason:string|null;roomNo:string|null;bookingNo:string|null;reservationId:number|null;
  requestedBy:number;requestedByName:string;state:string;errorCode:string|null;cardNo:string|null};
export type SrvKeyLog={rows:SrvKeyLogRow[];totals:unknown;total:number};
export type SrvKeyLock={roomId:number;roomNo:string;hasLock:boolean;lockNo:number|null;doorId:string|null;doorName:string|null;note:string|null;version:string};
export type SrvKeyEncoder={encoderId:string|null;pcName:string|null;online:boolean;adapter:string|null;detail:string|null;lastSeenUtc:string|null};
export type SrvKeyCard={cardId:number;cardNo:string|null;roomId:number;roomNo:string;event:string;reason:string|null;beginAt:string;endAt:string;status:string;issuedBy:number;issuedUtc:string};
export type SrvKeyPanel={reservationId:number;activeCards:SrvKeyCard[]};

/** The prototype job kind of an event (KeyRules.KindFor). The log row does not carry the kind, so a manually recorded key reads as its event's kind. */
const kindOf=(ev:string):KEYKind=>ev==='extended'?'extend':ev==='cancelled'?'cancel':ev==='read'?'read':'issue_guest';
const DEMO_SIM={cardPresent:true,failNext:false,supportsExtend:false,serial:0};

export function keyJobsFromServer(rows:SrvKeyLogRow[]):KEYJob[]{
  // The server lists newest first; the prototype appends, so the list is oldest first.
  return rows.map(r=>({id:r.jobId,event:r.event as KEYEvent,...(r.reason?{reason:r.reason as KEYReason}:{}),kind:kindOf(r.event),
    ...(r.reservationId?{booking:protoId(r.reservationId)}:{}),...(r.roomNo?{room:r.roomNo}:{}),state:r.state as KEYState,...(r.errorCode?{code:r.errorCode}:{}),
    ...(r.cardNo?{cardNo:r.cardNo}:{}),by:protoUserId(r.requestedBy),at:utcToClock(r.requestedUtc),timeoutAt:''})).reverse();
}
export function keyCardsFromServer(panels:SrvKeyPanel[]):KEYCard[]{
  return panels.flatMap(p=>(p.activeCards??[]).map(k=>({id:protoId(k.cardId),job:'',event:k.event as KEYEvent,...(k.reason?{reason:k.reason as KEYReason}:{}),
    ...(k.cardNo?{cardNo:k.cardNo}:{}),booking:protoId(p.reservationId),room:k.roomNo,from:k.beginAt,to:k.endAt,by:protoUserId(k.issuedBy),at:utcToClock(k.issuedUtc),
    status:k.status as KEYCard['status']})));
}
export function keyLocksFromServer(rows:SrvKeyLock[]):Record<string,KEYLock>{
  return Object.fromEntries(rows.map(l=>[l.roomNo,{hasLock:l.hasLock,...(l.lockNo?{lockNo:l.lockNo}:{}),...(l.doorName?{doorName:l.doorName}:{}),...(l.note?{note:l.note}:{})}]));
}

async function loadKeys(c:LoaderCtx):Promise<KEYSlice>{
  const prev=c.current('keysX') as Partial<KEYSlice>|undefined;
  const out:KEYSlice={locks:{},encoderOnline:false,kmin:0,jobs:[],cards:[],sim:{...DEMO_SIM},seq:0};
  if(holds(c,KEY_PERM_READ)){
    const log=await c.api.get<SrvKeyLog>('/keys/log');
    out.jobs=keyJobsFromServer(log.rows??[]);
    const enc=await c.api.get<SrvKeyEncoder>('/keys/encoder');
    out.encoderOnline=!!enc.online;
    if(enc.lastSeenUtc)out.encoderSeen=utcToClock(enc.lastSeenUtc);
    if(enc.online)out.encoderSeenK=0; // kmin stays 0 in production: the server's heartbeat age decides `online`
    // Active cards: the log has none, the booking panel does. Newest bookings with a finished issue / extension, a bounded number of reads.
    const ids=[...new Set((log.rows??[]).filter(r=>r.state==='done'&&r.reservationId&&['issued','reissued','extended','reactivated'].includes(r.event)).map(r=>r.reservationId as number))].slice(0,PANEL_BOOKINGS);
    const panels=await Promise.allSettled(ids.map(id=>c.api.get<SrvKeyPanel>('/keys/panel/'+id)));
    out.cards=keyCardsFromServer(panels.flatMap(p=>p.status==='fulfilled'?[p.value]:[]));
  }else if(prev)Object.assign(out,{jobs:[],cards:[]});
  if(holds(c,KEY_PERM_CONFIGURE)){
    const res=await c.api.get<{locks:SrvKeyLock[]}>('/keys/locks');
    out.locks=keyLocksFromServer(res.locks??[]);
    for(const l of res.locks??[]){c.ids.learn('room',l.roomNo,l.roomId);c.ids.setVersion('keyLock',l.roomNo,l.version);}
  }
  return out;
}

const roomIdOf=(c:CommandCtx,room:string):number=>{
  const id=c.ids.server('room',room);
  if(id===undefined)throw new HccClientError('VALIDATION',`Room ${room} is not known to the server. Refresh and try again.`);
  return id;
};
const optInt=(v:unknown)=>{const n=Number(text(v));return text(v)&&Number.isSafeInteger(n)&&n>0?n:undefined;};

export const keys:SyncArea={
  id:'keys',
  loaders:[{slice:'keysX',async load(c){return loadKeys(c);}}],
  commands:[
    {type:'KEY_REQUEST',reload:['keysX'],async send(c){const k=c.cmd;
      await c.api.command('RequestKey',{reservationId:serverId(k.booking,'Booking'),event:text(k.event),reason:text(k.reason)||undefined,validTo:text(k.validTo)||undefined,cards:1});}},
    {type:'KEY_MANUAL',reload:['keysX'],async send(c){const k=c.cmd;
      await c.api.command('RecordManualKey',{reservationId:serverId(k.booking,'Booking'),event:text(k.event),reason:text(k.reason)||undefined,note:text(k.note)||undefined});}},
    {type:'KEY_CANCEL',reload:['keysX'],async send(c){await c.api.command('CancelKey',{reservationId:serverId(c.cmd.booking,'Booking')});}},
    {type:'KEY_READ',reload:['keysX'],async send(c){await c.api.command('ReadKey',{note:text(c.cmd.note)||undefined});}},
    {type:'KEY_LOCK_SAVE',reload:['keysX'],async send(c){const k=c.cmd,room=text(k.room);
      const version=c.ids.version('keyLock',room);
      if(!version)throw new HccClientError('VALIDATION',`The lock mapping of room ${room} has not been loaded. Refresh and try again.`);
      await c.api.command('SaveKeyLock',{roomId:roomIdOf(c,room),hasLock:k.hasLock===true,lockNo:optInt(k.lockNo),doorName:text(k.doorName)||undefined,note:text(k.note)||undefined},
        {expectedVersion:version});}},
  ],
  unmapped:{
    KEY_AGENT_STEP:'Demo encoder: the real Key Agent runs beside the hotel PC and reports through the server.',
    KEY_CLOCK:'Demo clock for the simulated encoder; not on the server.',
    KEY_SIM:'Demo encoder settings; not on the server.',
    KEY_ENCODER:'Demo encoder switch; the real agent reports its own heartbeat.',
  },
};
