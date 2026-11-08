// F8 QW-PM · Preventive-maintenance calendar — pure selectors over module 15 (plans, orders, AMCs, assets). Exported names start with 'pmc'.
// Maintenance never sees money (DEC-061): nothing here reads AMC value, part costs or asset cost; the register link carries number and status only.
import type {State} from '../types';
import {addDays} from '../money';
import {mntSlice,mntAsset,mntVendor,mntWhere,mntAmcStatus,mntIsActive,MNT_PM_HORIZON_DAYS,type MNTPlan} from './maintenance';
import {accAssetRegister} from './accounting-assets';

export type PmcState='Done'|'Overdue'|'Due'|'Scheduled';
export type PmcOccurrence={planId:string;title:string;due:string;state:PmcState;assetId:string;assetName:string;where:string;
  /** Work order for this occurrence, when one exists. */workOrder?:string;
  /** True when the plan is paused (only past work orders of a paused plan show). */paused:boolean;
  /** Fixed-asset register number when the asset matches a register row (no cost). */register?:{no:string;status:string}};
export type PmcDay={date:string;items:PmcOccurrence[]};
export type PmcAmcCard={id:string;vendor:string;scope:string;from:string;to:string;visits:number;status:'Active'|'Expiring'|'Expired'|'Not started';daysLeft:number};

const MAX_STEPS=400;
const monthEnd=(month:string)=>{const [y,m]=month.split('-').map(Number);return new Date(Date.UTC(y,m,0)).toISOString().slice(0,10);};
const daysBetween=(a:string,b:string)=>Math.round((Date.parse(b+'T00:00:00Z')-Date.parse(a+'T00:00:00Z'))/86400000);
export const pmcMonthOf=(date:string)=>date.slice(0,7);
export const pmcShiftMonth=(month:string,by:number)=>{const [y,m]=month.split('-').map(Number);const d=new Date(Date.UTC(y,m-1+by,1));return d.toISOString().slice(0,7);};

/** State of a plan occurrence on `date`: same boundaries as mntPlanState / mntPmDue (overdue = before the business date; due = within the 3-day horizon). */
export function pmcState(s:State,due:string,hasOrder:{done:boolean}|null):PmcState{
  if(hasOrder?.done)return 'Done';
  if(due<s.date)return 'Overdue';
  return due<=addDays(s.date,MNT_PM_HORIZON_DAYS)?'Due':'Scheduled';
}
function registerLink(s:State,assetName:string){
  const n=assetName.trim().toLowerCase();
  const row=accAssetRegister(s).find(a=>a.name.trim().toLowerCase()===n);
  return row?{no:row.no,status:row.status}:undefined;
}
/** Plan occurrences falling in `month` (YYYY-MM), per day, sorted by date then title. Future occurrences are projected from nextDue by everyDays;
 *  occurrences that already have a work order show that order's state. Cancelled orders are left out. */
export function pmcMonth(s:State,month:string):PmcDay[]{
  const x=mntSlice(s),first=month+'-01',last=monthEnd(month),byDate=new Map<string,PmcOccurrence[]>();
  const add=(p:MNTPlan,due:string,wo?:{id:string;done:boolean})=>{
    const a=mntAsset(s,p.assetId);
    const o:PmcOccurrence={planId:p.id,title:p.title,due,state:pmcState(s,due,wo?{done:wo.done}:null),assetId:p.assetId,assetName:a?.name??p.assetId,where:a?mntWhere(s,a):'—',
      ...(wo?{workOrder:wo.id}:{}),paused:!p.active,...(a?{register:registerLink(s,a.name)}:{})};
    if(!o.register)delete o.register;
    (byDate.get(due)??byDate.set(due,[]).get(due)!).push(o);
  };
  for(const p of x.plans){
    const seen=new Set<string>();
    for(const w of x.orders.filter(w=>w.planId===p.id&&w.dueDate&&w.dueDate>=first&&w.dueDate<=last&&w.status!=='Cancelled')){
      seen.add(w.dueDate!);add(p,w.dueDate!,{id:w.id,done:!mntIsActive(w)});
    }
    if(!p.active)continue;
    let d=p.nextDue,n=0;
    while(d<=last&&n<MAX_STEPS){if(d>=first&&!seen.has(d)&&!x.orders.some(w=>w.planId===p.id&&w.dueDate===d))add(p,d,undefined);d=addDays(d,p.everyDays);n++;}
  }
  return [...byDate.entries()].sort((a,b)=>a[0]<b[0]?-1:1).map(([date,items])=>({date,items:items.sort((a,b)=>a.title.localeCompare(b.title)||a.planId.localeCompare(b.planId))}));
}
export const pmcCounts=(days:PmcDay[])=>{const c={Done:0,Overdue:0,Due:0,Scheduled:0} as Record<PmcState,number>;for(const d of days)for(const i of d.items)c[i.state]++;return c;};

/** AMC expiry cards (no contract value): Active / Expiring (within 30 days) / Expired, soonest end first. */
export function pmcAmcCards(s:State):PmcAmcCard[]{
  return mntSlice(s).amcs.map(a=>({id:a.id,vendor:mntVendor(s,a.vendorId)?.name??a.vendorId,scope:a.scope,from:a.from,to:a.to,visits:a.visits,
    status:mntAmcStatus(a,s.date),daysLeft:daysBetween(s.date,a.to)})).sort((a,b)=>a.to<b.to?-1:a.to>b.to?1:a.id.localeCompare(b.id));
}

// ── Standard CCTV / DG shift checklist (QWQ-6 default) ──
export const PMC_CCTV_DG_NAME='CCTV and DG shift check';
export const PMC_CCTV_DG_ITEMS=['CCTV recorder running and all cameras visible','DG diesel level noted','DG weekly test run done','Battery / UPS status checked','Fire panel normal'];
export const PMC_CCTV_DG_SHIFTS=['Morning','Evening','Night'] as const;
export const pmcTemplateName=(shift:string)=>`${PMC_CCTV_DG_NAME} · ${shift}`;
/** Template payloads for HOV_TEMPLATE_SAVE: Maintenance desk, Maintenance + Supervisor roles, every item mandatory. */
export const pmcTemplates=()=>PMC_CCTV_DG_SHIFTS.map(shift=>({name:pmcTemplateName(shift),desk:'Maintenance' as const,shift,roles:['Maintenance','Supervisor'],
  items:PMC_CCTV_DG_ITEMS.map((text,n)=>({id:`cd${n+1}`,text,mandatory:true}))}));
/** Shifts whose standard template is not yet saved (matched by name, case-insensitive); empty = button hidden / disabled. */
export function pmcMissingTemplates(s:State){
  const have=new Set(((s.handoverX as {templates?:{name:string}[]}|undefined)?.templates??[]).map(t=>t.name.toLowerCase()));
  return pmcTemplates().filter(t=>!have.has(t.name.toLowerCase()));
}
