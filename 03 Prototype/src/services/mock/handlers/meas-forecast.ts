// F12 MEAS · meas-forecast command handlers (MEAS-CONTRACT §2): MEAS_EVENT_SAVE / _DELETE (2.1), MEAS_OTB_SNAPSHOT (2.3, scheduled Monday
// 06:00 + back-fill), MEAS_COMPETITOR_SAVE / MEAS_RATESHOP_ADD (2.7). Every command refuses first on measOn (MEAS_OFF), is idempotent on its
// client id, uses the simulated clock and returns the audit text. Writers: Manager and Owner (permission 'approve'); the snapshot also runs from
// the scheduler as the Owner. Restriction counters (2.6) have no command: the yield guard caller stores yldRestrictionCount(...) (DONE).
import type {Handler,Kit} from '../commands';
import type {MeasState,MeasEventKind,MeasEvent} from '../../../domain/streams/measure-types';
import {measOn} from '../../../domain/streams/measure';
import {fcOtbRows,fcMondays} from '../../../domain/forecast';
import {yldRoomTypes} from '../../../domain/streams/yield';
import {money} from '../../../domain/money';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
export const MEAS_FC_EVENT_KINDS:MeasEventKind[]=['wedding-season','exam','fair','corporate-visit','holiday','other'];
/** Snapshot horizon (nights from the snapshot day) and the number of weekly snapshots kept. */
export const MEAS_FC_OTB_NIGHTS=120,MEAS_FC_OTB_KEEP=60;
const slice=(k:Kit):MeasState=>{const s=k.s;if(!s.measX)s.measX={};return s.measX;};
/** Refuses with MEAS_OFF when the item is switched off (contract §1.4; meas-settings' measGuard does the same once merged). */
function guard(k:Kit,key:string){if(!measOn(k.s,key))k.fail('MEAS_OFF' as never,`This measurement (${key}) is switched off in Master configuration › Measurement reports. Data is kept.`);}
const text=(v:unknown,max:number)=>String(v??'').trim().slice(0,max);
const dateOk=(k:Kit,v:unknown,label:string)=>k.assert(typeof v==='string'&&DATE.test(v),`Choose a valid ${label}.`);

const MEAS_EVENT_SAVE:Handler=k=>{
  guard(k,'meas.rev.demand-calendar');k.req('approve');
  const {cmd,assert}=k;const m=slice(k);
  const name=text(cmd.name,80);assert(name.length>=3,'Give the event a name (3–80 characters).');
  dateOk(k,cmd.from,'start date');dateOk(k,cmd.to,'end date');assert(cmd.to>=cmd.from,'The end date cannot be before the start date.');
  const kind=(MEAS_FC_EVENT_KINDS as string[]).includes(String(cmd.kind))?cmd.kind as MeasEventKind:k.fail('VALIDATION','Choose the event kind.');
  const impact=Number(cmd.impactPct);assert(Number.isInteger(impact)&&impact>=-50&&impact<=100,'Expected impact must be a whole percent between −50 and +100.');
  const list=m.events=[...(m.events??[])];
  const id=cmd.id?text(cmd.id,40):k.id('EVT');
  const row:MeasEvent={id,name,from:cmd.from,to:cmd.to,kind,impactPct:impact,...(text(cmd.note,200)?{note:text(cmd.note,200)}:{})};
  const i=list.findIndex(e=>e.id===id);
  if(i>=0){if(JSON.stringify(list[i])===JSON.stringify(row))return `Event ${name} unchanged`;list[i]=row;}else list.push(row);
  list.sort((a,b)=>a.from<b.from?-1:a.from>b.from?1:0);
  return `Event ${i>=0?'updated':'added'} · ${name} · ${cmd.from} to ${cmd.to} · ${kind} · ${impact>0?'+':''}${impact}%`;
};
const MEAS_EVENT_DELETE:Handler=k=>{
  guard(k,'meas.rev.demand-calendar');k.req('approve');
  const m=slice(k);const id=text(k.cmd.id,40);const e=(m.events??[]).find(x=>x.id===id);
  if(!e)return `Event ${id} already removed`; // idempotent repeat
  m.events=(m.events??[]).filter(x=>x.id!==id);
  return `Event removed · ${e.name} · ${e.from} to ${e.to}`;
};
const MEAS_OTB_SNAPSHOT:Handler=k=>{
  guard(k,'meas.rev.pickup');k.req('approve');
  const {cmd,s}=k;const m=slice(k);
  const takenOn=cmd.takenOn===undefined?s.date:String(cmd.takenOn);dateOk(k,takenOn,'snapshot date');k.assert(takenOn<=s.date,'A snapshot cannot be taken for a future date.');
  let days=[takenOn];
  if(cmd.backfillFrom!==undefined){dateOk(k,cmd.backfillFrom,'back-fill start');k.assert(cmd.backfillFrom<=takenOn,'The back-fill start must be on or before the snapshot date.');days=fcMondays(String(cmd.backfillFrom),takenOn);}
  const keep=(m.otb??[]).filter(x=>!days.includes(x.takenOn));
  let rows=0;const add=days.flatMap(d=>fcOtbRows(s,d,MEAS_FC_OTB_NIGHTS).map(r=>({id:`OTB-${d}-${r.night}`,takenOn:d,...r})));rows=add.length;
  const all=[...keep,...add].sort((a,b)=>a.takenOn<b.takenOn?-1:a.takenOn>b.takenOn?1:a.night<b.night?-1:1);
  const taken=[...new Set(all.map(x=>x.takenOn))];const cut=taken.slice(0,Math.max(0,taken.length-MEAS_FC_OTB_KEEP));
  m.otb=all.filter(x=>!cut.includes(x.takenOn));
  return `On-the-books snapshot · ${days.length===1?days[0]:days.length+' Mondays '+days[0]+' to '+days.at(-1)} · ${rows} night rows`;
};
const MEAS_COMPETITOR_SAVE:Handler=k=>{
  guard(k,'meas.rev.rate-shop');k.req('approve');
  const {cmd,assert}=k;const m=slice(k);const name=text(cmd.name,80);assert(name.length>=3,'Give the competitor a name (3–80 characters).');
  const list=m.competitors=[...(m.competitors??[])];const id=cmd.id?text(cmd.id,40):k.id('CMP');
  assert(!list.some(c=>c.id!==id&&c.name.toLowerCase()===name.toLowerCase()),'A competitor with this name already exists.');
  const active=cmd.active===undefined?true:!!cmd.active;
  if(active)assert(list.filter(c=>c.active&&c.id!==id).length<8,'Keep at most 8 active competitors (the plan uses 4–6).');
  const i=list.findIndex(c=>c.id===id);const row={id,name,active};
  if(i>=0)list[i]=row;else list.push(row);
  return `Competitor ${i>=0?'updated':'added'} · ${name}${active?'':' (inactive)'}`;
};
const MEAS_RATESHOP_ADD:Handler=k=>{
  guard(k,'meas.rev.rate-shop');k.req('approve');
  const {cmd,assert,s}=k;const m=slice(k);
  const clientId=text(cmd.clientId,60);assert(clientId.length>0,'Missing client id.');
  const id='RS-'+clientId;if((m.rateshop??[]).some(r=>r.id===id))return `Rate shop row ${clientId} already recorded`;
  dateOk(k,cmd.stayDate,'stay date');
  const comp=(m.competitors??[]).find(c=>c.id===cmd.competitor);assert(comp,'Choose a competitor from the list.','NOT_FOUND');assert(comp!.active,'That competitor is inactive.');
  assert(yldRoomTypes(s).includes(String(cmd.roomType)),'Unknown room type.');
  const rate=cmd.ratePaise;assert(Number.isInteger(rate)&&rate>0&&rate<=10000000,'Enter the rate in whole rupees (up to ₹1,00,000).');
  const source=text(cmd.source,40)||'manual';
  m.rateshop=[...(m.rateshop??[]),{id,date:s.date,stayDate:cmd.stayDate,competitor:comp!.id,roomType:String(cmd.roomType),ratePaise:rate,withBreakfast:!!cmd.withBreakfast,source,by:k.uid}];
  return `Rate shop · ${comp!.name} · ${cmd.roomType} · ${cmd.stayDate} · ${money(rate)}${cmd.withBreakfast?' incl. breakfast':''}`;
};

export const handlers:Record<string,Handler>={MEAS_EVENT_SAVE,MEAS_EVENT_DELETE,MEAS_OTB_SNAPSHOT,MEAS_COMPETITOR_SAVE,MEAS_RATESHOP_ADD};
