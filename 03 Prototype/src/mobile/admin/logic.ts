// MB-08 pure helpers for the phone admin / closing / reports screens. They only re-shape the SAME selectors the desktop
// pages use (closingPreflight, reportPack, adminApprovalInbox, adminDecideBlock…) so every figure is identical on both.
import {isDayClosed,closingPreflight,closingNightTargets,closingHasNight,shiftStatus,reportPack,adminDecideBlock,adminOtherActiveOwners,authorize,adminUserActive,can,reportRangeLimit} from '../../services/mock';
import type {State,User,Expense,ReportPack,ReportMetric,AdminInboxRow,AdminAuditFilter} from '../../services/mock';

// ───────────── Reports (M19) ─────────────
export const MA_REPORT_CHIPS=[{id:'daily',label:'Operations'},{id:'revenue',label:'Revenue & tax'},{id:'receipts',label:'Receipts'},{id:'expenses',label:'Expenses & cash'},{id:'occupancy',label:'Occupancy'}] as const;
export type MaReportTabId=typeof MA_REPORT_CHIPS[number]['id'];
export const MA_MAX_RANGE_DAYS=62;
/** Same validation as the desktop Reports page (ISO dates, from ≤ to ≤ business date, ≤ 62 days). */
export function maRangeCheck(from:string,to:string,today:string,maxDays=MA_MAX_RANGE_DAYS):{ok:boolean;span:number;error?:string}{
  const iso=/^\d{4}-\d{2}-\d{2}$/;
  if(!iso.test(from)||!iso.test(to)||from>to||to>today)return {ok:false,span:0,error:`Choose a valid range: “from” on or before “to”, and not after today’s business date.`};
  const span=Math.round((Date.parse(to)-Date.parse(from))/86400000)+1;
  if(span>maxDays)return {ok:false,span,error:`Choose at most ${maxDays} days in this demo.`};
  return {ok:true,span};
}
export const maMaskName=(n?:string)=>n?n.split(' ').map(w=>w[0]+'•••').join(' '):n;
/** Restricted scope (no folio/reservation access) masks guest names — identical rule to the desktop page. */
export const maMaskReports=(s:State,uid:string)=>!can(s,uid,'billing')&&!can(s,uid,'reservations');
export function maScopedPack(pack:ReportPack,mask:boolean):ReportPack{
  if(!mask)return pack;
  return {...pack,tabs:pack.tabs.map(t=>({...t,metrics:t.metrics.map(m=>({...m,rows:m.rows.map(r=>({...r,guest:maMaskName(r.guest)}))}))}))};
}
/** The report pack for the phone: reportPack() + masking, or null when the range is invalid. */
export function maReportPack(s:State,uid:string,from:string,to:string):ReportPack|null{
  return maRangeCheck(from,to,s.date,reportRangeLimit(s)).ok?maScopedPack(reportPack(s,from,to),maMaskReports(s,uid)):null;
}
/** Sum of the drill-down rows shown as cards — must equal the KPI figure. */
export const maDrillTotal=(m:ReportMetric)=>m.rows.reduce((a,r)=>a+r.amount,0);
export function maRangePresets(today:string){
  const back=(n:number)=>{const d=new Date(today+'T12:00:00Z');d.setUTCDate(d.getUTCDate()-n);return d.toISOString().slice(0,10);};
  return [{id:'today',label:'Today',from:today,to:today},{id:'3d',label:'Last 3 days',from:back(2),to:today},{id:'7d',label:'Last 7 days',from:back(6),to:today}];
}

// ───────────── Day closing (M18) ─────────────
export type MaStepId='preflight'|'nights'|'cash'|'close'|'summary'|'next';
export type MaStepState='done'|'current'|'blocked'|'waiting';
export type MaBlocker={label:string;hint?:string;booking?:string;page?:string;step?:MaStepId};
export type MaStep={id:MaStepId;label:string;state:MaStepState;summary:string;blockers:MaBlocker[]};
export const MA_STEP_LABELS:Record<MaStepId,string>={preflight:'Preflight',nights:'Nights',cash:'Cash',close:'Close',summary:'Summary',next:'Next day'};
/** Six-step closing model with the same state rules as the desktop Day closing page. */
export function maClosingModel(s:State){
  const closed=isDayClosed(s),pf=closingPreflight(s),st=shiftStatus(s);
  const general=pf.filter(x=>x.id!=='nights'&&x.id!=='shift'),generalOpen=general.filter(x=>x.items.length);
  const targets=closingNightTargets(s),toPost=targets.filter(b=>!closingHasNight(b,s.date));
  const prog=s.closeProgress?.date===s.date?s.closeProgress:undefined;
  const open=pf.filter(x=>x.items.length);
  const rec=s.closings?.[s.date];
  const resume=!closed&&prog?.interrupted&&toPost.length?{posted:prog.posted.length,total:prog.total,remaining:toPost.length}:undefined;
  const stepFor=(id:string):MaStepId|undefined=>id==='nights'?'nights':id==='shift'?'cash':undefined;
  const steps:MaStep[]=[
    {id:'preflight',label:'Preflight',state:closed||!generalOpen.length?'done':'blocked',
      summary:closed?'Completed before close.':generalOpen.length?`${generalOpen.reduce((a,c)=>a+c.items.length,0)} item${generalOpen.reduce((a,c)=>a+c.items.length,0)===1?'':'s'} to resolve in ${generalOpen.length} check${generalOpen.length===1?'':'s'}`:'All checks clear',
      blockers:closed?[]:generalOpen.flatMap(c=>c.items.map(it=>({label:it.label,hint:c.title,booking:it.booking,page:it.booking?undefined:it.page??c.page})))},
    {id:'nights',label:'Post room nights',state:closed||!toPost.length?'done':resume?'blocked':'current',
      summary:closed?`Nights posted for ${s.date}.`:resume?`Interrupted after ${resume.posted} of ${resume.total} — resume posts the remaining ${resume.remaining}`:`${targets.length} in-house · ${toPost.length} to post · ${targets.length-toPost.length} already posted`,
      blockers:closed?[]:toPost.map(b=>({label:`${b.guest} · room ${b.room}`,hint:'Night to post',booking:b.id}))},
    {id:'cash',label:'Cash shift',state:st==='Closed'?'done':st==='Submitted'?'blocked':'current',
      summary:st==='Closed'?'Shift reconciled':st==='Submitted'?'Count submitted — difference awaiting approval':'Count the drawer and submit the shift',
      blockers:st==='Closed'?[]:[{label:st==='Submitted'?'Difference awaiting approval':'Cash shift open',hint:'Cashier shift',page:'cashier'}]},
    {id:'close',label:'Close business date',state:closed?'done':open.length?'waiting':'current',
      summary:closed?'Day closed — closing record saved':open.length?`${open.length} check${open.length===1?'':'s'} still blocking`:'All checks clear — ready to close',
      blockers:closed?[]:open.map(c=>({label:c.title,hint:`${c.items.length} to resolve · ${c.hint}`,step:stepFor(c.id)??'preflight'}))},
    {id:'summary',label:'Closing summary',state:rec?'done':'waiting',summary:rec?'Final closing record (immutable)':'Preview — final when the day is closed',blockers:[]},
    {id:'next',label:'Open next day',state:closed?'current':'waiting',summary:closed?'Ready to open the next business date':'Available after the day is closed',blockers:[]},
  ];
  const focus=Math.max(0,steps.findIndex(x=>x.state!=='done'));
  return {closed,steps,focus,resume,toPost:toPost.length,targets:targets.length,blocking:open.length,shift:st,record:rec};
}

// ───────────── Approvals (M17) & audit (M22) ─────────────
/** Approve / reject availability for one inbox card — same wording as the desktop inbox. */
export function maApprovalActions(s:State,uid:string,r:AdminInboxRow){
  const block=adminDecideBlock(s,uid,r);
  const live=r.a.status==='Pending';
  const approve=block||(r.status==='Expired'?'Expired — reject it and ask for a new request.':r.status==='Invalidated'?(r.derived||'Target changed')+' — reject it and request again.':undefined);
  return {live,own:r.a.actor===uid,approveBlock:live?approve:'Already decided',rejectBlock:live?block:'Already decided'};
}
export function maExpenseBlock(s:State,uid:string,e:Expense):string|undefined{
  const u=s.users.find(x=>x.id===uid);
  return !u||!adminUserActive(s,u)?'Your account is not active.':e.actor===uid?'You submitted this expense — another user must decide it.':!can(s,uid,'approve')?`Role ${u.role} lacks the approve permission.`:undefined;
}
export const maAuditFilterCount=(f:AdminAuditFilter)=>(['actor','action','text','from','to'] as const).filter(k=>!!(f[k]||'').trim()).length;
export function maAuditOptions(s:State){return {actors:[...new Set(s.audit.map(a=>a.actor))].sort(),actions:[...new Set(s.audit.map(a=>a.action))].sort()};}

// ───────────── Access control (M20) ─────────────
/** Why the current user may not enable/disable this user (same rules as desktop). */
export function maUserBlock(s:State,uid:string,u:User):string|undefined{
  const d=authorize(s,uid,'PERM-ADM-001');
  if(d.decision!=='allow')return d.reason;
  if(u.id===uid)return 'This is your own access — another administrator must change it.';
  if(u.role==='Owner'&&u.enabled&&!adminOtherActiveOwners(s,u.id).length)return 'Last active owner — cannot be disabled (DEC-033).';
  return undefined;
}
