// Module 15 · Maintenance — pure helpers, selectors and the state slice type. Exported names MUST start with 'mnt' / 'MNT' (shared mock namespace).
// Builds on stream D (repair tasks, room blocks). Money = integer paise; time = simulated clock only.
import type {State,User,Task} from '../types';
import {assignedScopeRoles} from '../types';
import {can} from '../selectors';
import {addDays} from '../money';

export type MNTAssetKind='AC'|'Geyser'|'TV'|'Lift'|'Generator'|'Pump'|'RO plant'|'Other';
export const MNT_ASSET_KINDS:MNTAssetKind[]=['AC','Geyser','TV','Lift','Generator','Pump','RO plant','Other'];
export type MNTAsset={id:string;name:string;kind:MNTAssetKind;
  /** Room ID when the asset is in a guest room; otherwise `area` (Lobby, Basement…). */
  room?:string;area?:string;make:string;model:string;installedOn:string;vendorId?:string;status:'Working'|'Faulty'|'Retired'};
export type MNTVendor={id:string;name:string;trade:string;phone:string};
export type MNTAmc={id:string;vendorId:string;scope:string;from:string;to:string;visits:number;
  /** Contract value, paise (money-gated in the UI). */
  value:number};
export type MNTSpare={id:string;name:string;unit:string;stock:number;reorderAt:number;
  /** Unit cost, paise (money-gated in the UI). */
  cost:number};
export type MNTPlan={id:string;assetId:string;title:string;everyDays:number;nextDue:string;active:boolean;lastDone?:string};
export type MNTPriority='P1'|'P2'|'P3'|'P4';
export type MNTStatus='Open'|'In progress'|'On hold'|'Done'|'Verified'|'Cancelled';
export type MNTHistory={at:string;by:string;action:string;note?:string};
export type MNTWorkOrder={id:string;kind:'Corrective'|'Preventive';title:string;priority:MNTPriority;status:MNTStatus;
  room?:string;assetId?:string;area?:string;
  /** Maintenance user ID, or undefined when unassigned / external vendor. */
  assignee?:string;vendorId?:string;
  createdAt:string;createdBy:string;startedAt?:string;doneAt?:string;doneBy?:string;verifiedAt?:string;verifiedBy?:string;
  holdReason?:string;awaitingSpare?:string;note?:string;returnReason?:string;cancelReason?:string;
  parts:{spareId:string;qty:number;unitCost:number}[];
  /** Stream D repair task this WO was converted from. */
  repairTaskId?:string;
  /** Preventive: plan and occurrence due date. */
  planId?:string;dueDate?:string;
  /** Room downtime caused by this WO (simulated clock). */
  downtimeFrom?:string;downtimeTo?:string;
  history:MNTHistory[]};
/** State slice for this module, stored at s.maintenanceX (optional; absent in old saves → treat as empty). */
export type MNTState={assets:MNTAsset[];vendors:MNTVendor[];amcs:MNTAmc[];spares:MNTSpare[];plans:MNTPlan[];orders:MNTWorkOrder[]};
export const mntEmpty=():MNTState=>({assets:[],vendors:[],amcs:[],spares:[],plans:[],orders:[]});
export const mntSlice=(s:State):MNTState=>({...mntEmpty(),...(s.maintenanceX??{})});

// ── Priority / SLA ──
export const MNT_PRIORITY:Record<MNTPriority,{label:string;slaMins:number;sla:string}>={
  P1:{label:'Urgent',slaMins:240,sla:'4 h'},P2:{label:'High',slaMins:1440,sla:'24 h'},
  P3:{label:'Normal',slaMins:4320,sla:'72 h'},P4:{label:'Low',slaMins:10080,sla:'7 days'}};
export const MNT_PRIORITIES:MNTPriority[]=['P1','P2','P3','P4'];
export const MNT_ACTIVE:MNTStatus[]=['Open','In progress','On hold'];
export const MNT_PM_HORIZON_DAYS=3;

/** Minutes between two simulated clock values (YYYY-MM-DDTHH:mm). */
export const mntMinutes=(from:string,to:string)=>Math.round((Date.parse(to+':00Z')-Date.parse(from+':00Z'))/60000);
export function mntDuration(mins:number){
  if(mins<60)return `${Math.max(0,mins)} min`;
  if(mins<1440)return `${Math.floor(mins/60)} h ${mins%60?(mins%60)+' min':''}`.trim();
  const d=Math.floor(mins/1440),h=Math.floor((mins%1440)/60);return `${d} d${h?` ${h} h`:''}`;
}
export const mntSlaDue=(w:MNTWorkOrderView)=>new Date(Date.parse(w.createdAt+':00Z')+MNT_PRIORITY[w.priority].slaMins*60000).toISOString().slice(0,16);
/** SLA state: met / breached (done late or still open past due) / due soon (≤ 25 % left) / on track. Cancelled = n/a. */
export function mntSla(w:MNTWorkOrderView,clock:string):'Met'|'Breached'|'Due soon'|'On track'|'n/a'{
  if(w.status==='Cancelled')return 'n/a';
  const due=mntSlaDue(w);
  if(w.doneAt)return w.doneAt<=due?'Met':'Breached';
  if(clock>due)return 'Breached';
  return mntMinutes(clock,due)<=MNT_PRIORITY[w.priority].slaMins/4?'Due soon':'On track';
}
export const mntIsActive=(w:MNTWorkOrderView)=>MNT_ACTIVE.includes(w.status);
export const mntAge=(w:MNTWorkOrderView,clock:string)=>mntMinutes(w.createdAt,clock);

// ── Access ──
/** Supervisor-level control (assign, verify, block, registers). */
export const mntCanManage=(s:State,uid:string)=>can(s,uid,'inspect');
/** Costs: only roles with reports or expenses (never housekeeping / maintenance by default). */
export const mntCanSeeCost=(s:State,uid:string)=>can(s,uid,'reports')||can(s,uid,'expenses');
export const mntIsTech=(u:User)=>u.role==='Maintenance';
export const mntScoped=(u:User)=>assignedScopeRoles.includes(u.role);
/** A work order as a user may receive it: part unit costs stripped for users without cost access (DEC-063 G12 — hidden on
 * screen is not enough; the selector / future server API never sends money to housekeeping or maintenance). */
export type MNTWorkOrderView=Omit<MNTWorkOrder,'parts'>&{parts:{spareId:string;qty:number;unitCost?:number}[]};
/** Work orders a user may see: managers all; maintenance own; housekeepers open WOs on rooms of their own open tasks. */
export function mntVisibleOrders(s:State,uid:string):MNTWorkOrderView[]{
  const u=s.users.find(x=>x.id===uid);const all=mntSlice(s).orders;
  if(!u)return [];
  const strip=(list:MNTWorkOrder[]):MNTWorkOrderView[]=>mntCanSeeCost(s,uid)?list:list.map(w=>({...w,parts:w.parts.map(p=>({spareId:p.spareId,qty:p.qty}))}));
  if(mntCanManage(s,uid))return strip(all);
  if(u.role==='Maintenance')return strip(all.filter(w=>w.assignee===uid));
  const rooms=new Set(s.tasks.filter(t=>t.assignee===uid&&t.status!=='Done').map(t=>t.room));
  return strip(all.filter(w=>w.room&&rooms.has(w.room)&&mntIsActive(w)));
}
export const mntTechnicians=(s:State)=>s.users.filter(u=>u.enabled&&u.role==='Maintenance');

// ── Lookups / labels ──
export const mntAsset=(s:State,id?:string)=>id?mntSlice(s).assets.find(a=>a.id===id):undefined;
export const mntSpare=(s:State,id?:string)=>id?mntSlice(s).spares.find(x=>x.id===id):undefined;
export const mntVendor=(s:State,id?:string)=>id?mntSlice(s).vendors.find(x=>x.id===id):undefined;
export function mntWhere(s:State,w:{room?:string;area?:string;assetId?:string}){
  const a=mntAsset(s,w.assetId);const room=w.room??a?.room,area=w.area??a?.area;
  return room?`Room ${room}`:area||'—';
}
export function mntAssigneeLabel(s:State,w:MNTWorkOrderView){
  if(w.vendorId)return (mntVendor(s,w.vendorId)?.name??w.vendorId)+' (vendor)';
  if(!w.assignee)return 'Unassigned';
  return s.users.find(u=>u.id===w.assignee)?.name.split(' · ')[0]??w.assignee;
}
export const mntPartsCost=(w:MNTWorkOrderView)=>w.parts.reduce((t,p)=>t+p.qty*(p.unitCost??0),0);
/** Open stream-D repair tasks that have no work order yet. */
export function mntPendingRepairs(s:State):Task[]{
  const linked=new Set(mntSlice(s).orders.map(w=>w.repairTaskId).filter(Boolean));
  return s.tasks.filter(t=>t.kind==='repair'&&t.status!=='Done'&&!linked.has(t.id));
}
export const mntOrderForRepair=(s:State,taskId:string)=>mntSlice(s).orders.find(w=>w.repairTaskId===taskId);
export const mntLowSpares=(s:State)=>mntSlice(s).spares.filter(x=>x.stock<=x.reorderAt);
export function mntAmcStatus(a:MNTAmc,date:string):'Active'|'Expiring'|'Expired'|'Not started'{
  if(date>a.to)return 'Expired';if(date<a.from)return 'Not started';return addDays(date,30)>=a.to?'Expiring':'Active';
}
export const mntAmcFor=(s:State,vendorId?:string)=>vendorId?mntSlice(s).amcs.filter(a=>a.vendorId===vendorId):[];

// ── Preventive maintenance ──
/** Plan occurrences due on or before date + horizon that have no WO yet (what MNT_PM_GENERATE would create). */
export function mntPmDue(s:State):{plan:MNTPlan;due:string}[]{
  const x=mntSlice(s),horizon=addDays(s.date,MNT_PM_HORIZON_DAYS),out:{plan:MNTPlan;due:string}[]=[];
  for(const p of x.plans.filter(p=>p.active)){
    let d=p.nextDue,n=0;
    while(d<=horizon&&n<12){if(!x.orders.some(w=>w.planId===p.id&&w.dueDate===d))out.push({plan:p,due:d});d=addDays(d,p.everyDays);n++;}
  }
  return out;
}
export function mntPlanState(s:State,p:MNTPlan):'Overdue'|'Due'|'Scheduled'|'Paused'{
  if(!p.active)return 'Paused';
  const open=mntSlice(s).orders.filter(w=>w.planId===p.id&&mntIsActive(w));
  if(open.some(w=>w.dueDate!<s.date)||p.nextDue<s.date)return 'Overdue';
  if(open.length||p.nextDue<=addDays(s.date,MNT_PM_HORIZON_DAYS))return 'Due';
  return 'Scheduled';
}

// ── Reports ──
export const MNT_AGE_BUCKETS=[{label:'Under 1 day',max:1440},{label:'1–3 days',max:4320},{label:'3–7 days',max:10080},{label:'Over 7 days',max:Infinity}];
export function mntAgeBuckets(s:State,orders=mntSlice(s).orders){
  const open=orders.filter(mntIsActive);
  return MNT_AGE_BUCKETS.map((b,i)=>({label:b.label,count:open.filter(w=>{const a=mntAge(w,s.clock);return a<b.max&&(i===0||a>=MNT_AGE_BUCKETS[i-1].max);}).length}));
}
/** Mean time to repair (minutes), corrective WOs created → done. null when none are done. */
export function mntMttr(s:State,orders=mntSlice(s).orders):number|null{
  const done=orders.filter(w=>w.kind==='Corrective'&&w.doneAt);
  if(!done.length)return null;
  return Math.round(done.reduce((t,w)=>t+mntMinutes(w.createdAt,w.doneAt!),0)/done.length);
}
/** PM compliance: preventive WOs due on/before business date, done on/before their due date. */
export function mntPmCompliance(s:State,orders=mntSlice(s).orders){
  const due=orders.filter(w=>w.kind==='Preventive'&&w.dueDate&&w.dueDate<=s.date&&w.status!=='Cancelled');
  const onTime=due.filter(w=>w.doneAt&&w.doneAt.slice(0,10)<=w.dueDate!).length;
  return {due:due.length,onTime,pct:due.length?Math.round(onTime*100/due.length):null};
}
export const mntSlaBreaches=(s:State,orders=mntSlice(s).orders)=>orders.filter(w=>mntSla(w,s.clock)==='Breached').length;
/** End of a WO's downtime: explicit end; else still running while the room is blocked; else when the work finished. */
export function mntDowntimeEnd(s:State,w:MNTWorkOrderView){
  if(!w.downtimeFrom)return undefined;if(w.downtimeTo)return w.downtimeTo;
  const r=s.rooms.find(r=>r.id===w.room);if(r?.blocked)return s.clock;
  return w.verifiedAt??w.doneAt??s.clock;
}
export function mntDowntimeByRoom(s:State){
  const m=new Map<string,{room:string;minutes:number;orders:number;running:boolean}>();
  for(const w of mntSlice(s).orders.filter(w=>w.room&&w.downtimeFrom)){
    const end=mntDowntimeEnd(s,w)!;const row=m.get(w.room!)??{room:w.room!,minutes:0,orders:0,running:false};
    row.minutes+=Math.max(0,mntMinutes(w.downtimeFrom!,end));row.orders++;
    if(!w.downtimeTo&&s.rooms.find(r=>r.id===w.room)?.blocked)row.running=true;
    m.set(w.room!,row);
  }
  return [...m.values()].sort((a,b)=>b.minutes-a.minutes);
}
export function mntByKind(s:State){
  const x=mntSlice(s);const out=new Map<string,{kind:string;open:number;total:number}>();
  for(const w of x.orders){const k=mntAsset(s,w.assetId)?.kind??'Other';const r=out.get(k)??{kind:k,open:0,total:0};r.total++;if(mntIsActive(w))r.open++;out.set(k,r);}
  return [...out.values()].sort((a,b)=>b.total-a.total);
}
/** Parts cost of WOs completed in the business month (paise). */
export function mntMonthPartsCost(s:State){
  const month=s.date.slice(0,7);
  return mntSlice(s).orders.filter(w=>(w.doneAt??'').startsWith(month)).reduce((t,w)=>t+mntPartsCost(w),0);
}
export function mntSummary(s:State,orders=mntSlice(s).orders){
  const c=(st:MNTStatus)=>orders.filter(w=>w.status===st).length;
  return {open:c('Open'),inProgress:c('In progress'),onHold:c('On hold'),done:c('Done'),verified:c('Verified'),breached:mntSlaBreaches(s,orders)};
}
/** Sort: active first (P1 first, oldest first), then Done (awaiting verification), then closed (newest first). */
export function mntSort<T extends MNTWorkOrderView>(orders:T[]){
  const rank=(w:MNTWorkOrderView)=>w.status==='In progress'?0:w.status==='Open'?1:w.status==='On hold'?2:w.status==='Done'?3:4;
  return [...orders].sort((a,b)=>rank(a)-rank(b)||(rank(a)<4?a.priority.localeCompare(b.priority)||a.createdAt.localeCompare(b.createdAt):b.createdAt.localeCompare(a.createdAt)));
}
