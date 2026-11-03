// F5-E1 · internet egress gate, prototype mirror (02 Planning/agents/EGRESS-CONTRACT.md §1, §1.1, §1.2, §5, §6).
// Values: s.configX.values['net.*'] (default OFF; missing / non-boolean = OFF, fail closed). Change: command CFG_NET_SET {setting, value, reason}
// (Owner only, reason required; services/mock/handlers/egress.ts). Mock outbound outbox: s.egressX.outbox — an item whose integration is
// OFF is 'held' (reason 'internet off', no attempt consumed); netReplay() moves held items of an integration that is ON back to 'pending'
// in original order (run after every CFG_NET_SET and by netTick()). Pure helpers; the Internet group UI is lane U1's (NET_SETTING_DEFS ready).
import type {State} from '../types';

export type NetIntegration='messaging'|'ota'|'ocr'|'payments'|'email'|'updateCheck';
export const NET_INTEGRATIONS:readonly NetIntegration[]=['messaging','ota','ocr','payments','email','updateCheck'];
export const NET_MASTER='net.internetEnabled',NET_WINDOW='net.window';
export const netKeyOf=(i:NetIntegration)=>`net.${i}`;
export const NET_KEYS:readonly string[]=[NET_MASTER,...NET_INTEGRATIONS.map(netKeyOf),NET_WINDOW];
export type NetReason='on'|'master-off'|'integration-off'|'outside-window'|'settings-unreadable';
export type NetWindow={from:string;to:string;days:number[]};
export type NETChange={id:string;key:string;before:unknown;after:unknown;reason:string;by:string;at:string};
export type NETQueue='messaging'|'ota-push'|'email'|'scheduled-report';
export type NETItem={id:string;queue:NETQueue;integration:NetIntegration;ref?:string;status:'pending'|'held'|'sent';heldReason?:string;heldAt?:string;attempts:number;at:string};
export type NETState={changes:NETChange[];outbox:NETItem[];seq:number};
export const NET_HELD_REASON='internet off';
export const NET_QUEUES:readonly NETQueue[]=['messaging','ota-push','email','scheduled-report'];

export const netSlice=(s:State):NETState=>{const x=s.egressX??(s.egressX={changes:[],outbox:[],seq:0});x.changes??=[];x.outbox??=[];x.seq??=0;return x;};
const values=(s:State):Record<string,unknown>=>s.configX?.values??{};

const HHMM=/^([01]\d|2[0-3]):[0-5]\d$/;
/** null = no window; undefined = present but invalid (→ settings-unreadable). */
export function netParseWindow(v:unknown):NetWindow|null|undefined{
  if(v===undefined||v===null||v==='')return null;
  let o:any=v;
  if(typeof v==='string'){try{o=JSON.parse(v);}catch{return undefined;}}
  if(!o||typeof o!=='object'||!HHMM.test(o.from)||!HHMM.test(o.to)||!Array.isArray(o.days)||!o.days.length)return undefined;
  if(o.days.some((d:unknown)=>!Number.isInteger(d)||(d as number)<0||(d as number)>6))return undefined;
  return {from:o.from,to:o.to,days:[...new Set<number>(o.days)]};
}
/** From inclusive, to exclusive; from > to crosses midnight (the late part counts for the start day); from == to = never. day 0 = Sunday. */
export function netInWindow(w:NetWindow,day:number,hhmm:string):boolean{
  if(w.from<w.to)return w.days.includes(day)&&hhmm>=w.from&&hhmm<w.to;
  if(w.from===w.to)return false;
  if(hhmm>=w.from)return w.days.includes(day);
  if(hhmm<w.to)return w.days.includes((day+6)%7);
  return false;
}
/** Hotel local day / time from the simulated clock (s.clock is local ISO-like 'YYYY-MM-DDTHH:mm'). */
const localNow=(s:State)=>{const c=String(s.clock??'');const d=new Date(c.slice(0,10)+'T00:00:00Z');return {day:Number.isNaN(d.getTime())?0:d.getUTCDay(),hhmm:c.slice(11,16)||'00:00'};};

/** EffectiveNow(i) = master && net.<i> && (window == null || now ∈ window). Never throws. */
export function netDecide(s:State,i:NetIntegration,now=localNow(s)):{allowed:boolean;reason:NetReason}{
  try{
    const v=values(s);const m=v[NET_MASTER]??false;const sw=v[netKeyOf(i)]??false;
    if(typeof m!=='boolean')return {allowed:false,reason:'settings-unreadable'};
    if(!m)return {allowed:false,reason:'master-off'};
    if(typeof sw!=='boolean')return {allowed:false,reason:'settings-unreadable'};
    if(!sw)return {allowed:false,reason:'integration-off'};
    const w=netParseWindow(v[NET_WINDOW]);
    if(w===undefined)return {allowed:false,reason:'settings-unreadable'};
    if(w&&!netInWindow(w,now.day,now.hhmm))return {allowed:false,reason:'outside-window'};
    return {allowed:true,reason:'on'};
  }catch{return {allowed:false,reason:'settings-unreadable'};}
}
export const netOn=(s:State,i:NetIntegration)=>netDecide(s,i).allowed;

/** EgressState (§1) for System health and clients; changedBy only for the Owner. */
export function netState(s:State,uid?:string){
  const v=values(s);const owner=s.users.find(u=>u.id===uid)?.role==='Owner';
  const last=netSlice(s).changes.at(-1);
  const integrations=Object.fromEntries(NET_INTEGRATIONS.map(i=>[i,v[netKeyOf(i)]===true])) as Record<NetIntegration,boolean>;
  const decisions=Object.fromEntries(NET_INTEGRATIONS.map(i=>[i,netDecide(s,i)])) as Record<NetIntegration,{allowed:boolean;reason:NetReason}>;
  return {masterOn:v[NET_MASTER]===true,integrations,decisions,window:netParseWindow(v[NET_WINDOW])??null,effectiveNow:NET_INTEGRATIONS.some(i=>decisions[i].allowed),
    changedAt:last?.at,changedBy:owner?last?.by:undefined,reason:last?.reason};
}
/** Held / pending per queue (System health §5, Messages held badge). */
export const netHeldCounts=(s:State)=>NET_QUEUES.map(q=>{const it=netSlice(s).outbox.filter(x=>x.queue===q);return {queue:q,held:it.filter(x=>x.status==='held').length,pending:it.filter(x=>x.status==='pending').length};});

/** Queue outbound work; held at once when the integration is OFF. Call only inside a command (mutates the slice). */
export function netEnqueue(s:State,queue:NETQueue,integration:NetIntegration,ref?:string):NETItem{
  const x=netSlice(s);x.seq+=1;const on=netOn(s,integration);
  const it:NETItem={id:`NET-${String(x.seq).padStart(5,'0')}`,queue,integration,status:on?'pending':'held',attempts:0,at:s.clock,...(ref?{ref}:{}),...(on?{}:{heldReason:NET_HELD_REASON,heldAt:s.clock})};
  x.outbox.push(it);return it;
}
/** Replay hook: ON → held back to pending (original order kept: the array order); OFF → pending held. Returns counts. */
export function netReplay(s:State):{released:number;held:number}{
  let released=0,held=0;
  for(const it of netSlice(s).outbox){
    if(it.status==='sent')continue;
    const on=netOn(s,it.integration);
    if(on&&it.status==='held'){it.status='pending';delete it.heldReason;delete it.heldAt;released++;}
    else if(!on&&it.status==='pending'){it.status='held';it.heldReason=NET_HELD_REASON;it.heldAt=s.clock;held++;}
  }
  return {released,held};
}

/** Definitions for the Master configuration › Internet group (lane U1 registers the group; Owner only, reason required). */
export const NET_SETTING_DEFS=[
  {key:NET_MASTER,label:'Internet (master switch)',type:'boolean' as const,default:false,help:'OFF = HCC makes no internet call at all. Default OFF at go-live (A28).'},
  {key:'net.messaging',label:'Guest messaging (WhatsApp / SMS)',type:'boolean' as const,default:false,help:'OFF = messages queue as held.'},
  {key:'net.ota',label:'OTA channel manager',type:'boolean' as const,default:false,help:'OTA bookings in, rates / inventory out.'},
  {key:'net.ocr',label:'ID scan (OCR)',type:'boolean' as const,default:false,help:'OFF = enter the ID by hand.'},
  {key:'net.payments',label:'Payment links',type:'boolean' as const,default:false,help:'OFF = record the deposit by hand (DEC-102).'},
  {key:'net.email',label:'E-mail',type:'boolean' as const,default:false,help:'E-mails and scheduled reports.'},
  {key:'net.updateCheck',label:'Update check',type:'boolean' as const,default:false,help:'Ask for a newer version.'},
  {key:NET_WINDOW,label:'Allowed time window',type:'text' as const,default:'',help:'{"from":"HH:mm","to":"HH:mm","days":[0-6]} hotel time, from inclusive, to exclusive, may cross midnight; empty = no window.'},
];
