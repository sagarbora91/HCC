// Module 19 · Checklists & shift handover — pure helpers, selectors and the state slice type. Exported names MUST start with 'hov' / 'HOV' (shared mock namespace).
import type {State,Role} from '../types';
import {can} from '../selectors';
import {addDays} from '../money';

export type HOVShift='Morning'|'Evening'|'Night';
export type HOVDesk='Front desk'|'Housekeeping'|'Maintenance';
export const HOV_SHIFTS:HOVShift[]=['Morning','Evening','Night'];
export const HOV_DESKS:HOVDesk[]=['Front desk','Housekeeping','Maintenance'];
/** Shift windows on the simulated clock (HH:mm). Night ends next calendar day. */
export const HOV_SHIFT_TIMES:Record<HOVShift,{start:string;end:string}>={Morning:{start:'07:00',end:'15:00'},Evening:{start:'15:00',end:'23:00'},Night:{start:'23:00',end:'07:00'}};
/** Roles that belong to each handover desk (Owner/Manager see every desk). */
export const HOV_DESK_ROLES:Record<HOVDesk,Role[]>={'Front desk':['Reception','Cashier','Manager'],Housekeeping:['Supervisor','Housekeeper'],Maintenance:['Maintenance','Supervisor']};
export const HOV_MAX_ITEMS=25,HOV_MAX_CARRY=8,HOV_NOTE_MAX=200;

export type HOVItem={id:string;text:string;mandatory:boolean;/** HH:mm */due?:string;/** a photo file name is required to tick */photo?:boolean;/** page ID for a go() link-out */link?:string};
export type HOVTemplate={id:string;name:string;desk:HOVDesk;shift:HOVShift;roles:Role[];items:HOVItem[];active:boolean;version:number;updatedBy?:string;updatedAt?:string};
export type HOVRunItem=HOVItem&{done:boolean;by?:string;at?:string;note?:string;photoName?:string};
export type HOVRun={id:string;template:string;templateVersion:number;name:string;desk:HOVDesk;shift:HOVShift;roles:Role[];date:string;items:HOVRunItem[];
  status:'Open'|'Submitted';startedBy:string;startedAt:string;submittedBy?:string;submittedAt?:string;late?:boolean;reopenReason?:string};
export type HOVCarry={id:string;text:string;done:boolean;doneBy?:string;doneAt?:string};
export type HOVNote={id:string;desk:HOVDesk;date:string;shift:HOVShift;toShift:HOVShift;author:string;at:string;
  issues:string;nothing?:boolean;keys:string;guests:string;
  /** Front desk only: expected cash float handed over (paise) and a remark. Never shown to housekeeping/maintenance. */
  cash?:{amount:number;note:string};
  carry:HOVCarry[];status:'Submitted'|'Acknowledged';ackBy?:string;ackAt?:string;ackRemark?:string};
/** State slice for this module, stored at s.handoverX (optional; absent in old saves → treat as empty). */
export type HOVState={templates:HOVTemplate[];runs:HOVRun[];notes:HOVNote[]};
export const hovEmpty=():HOVState=>({templates:[],runs:[],notes:[]});
export const hovSlice=(s:State):HOVState=>{const x=s.handoverX as Partial<HOVState>|undefined;return {templates:x?.templates??[],runs:x?.runs??[],notes:x?.notes??[]};};

// ── Clock & shifts ──
export const hovTime=(clock:string)=>clock.slice(11,16);
/** Shift running at a simulated clock value. */
export function hovShiftAt(clock:string):HOVShift{const t=hovTime(clock);return t>='07:00'&&t<'15:00'?'Morning':t>='15:00'&&t<'23:00'?'Evening':'Night';}
export const hovNextShift=(x:HOVShift):HOVShift=>x==='Morning'?'Evening':x==='Evening'?'Night':'Morning';
export const hovPrevShift=(x:HOVShift):HOVShift=>x==='Morning'?'Night':x==='Evening'?'Morning':'Evening';
/** Absolute clock value of a HH:mm time inside a shift of a business date (Night times before 23:00 roll to the next day). */
export function hovAt(date:string,shift:HOVShift,hhmm:string){return (shift==='Night'&&hhmm<HOV_SHIFT_TIMES.Night.start?addDays(date,1):date)+'T'+hhmm;}
export const hovShiftEnd=(date:string,shift:HOVShift)=>hovAt(date,shift,HOV_SHIFT_TIMES[shift].end);
export const hovShiftStart=(date:string,shift:HOVShift)=>hovAt(date,shift,HOV_SHIFT_TIMES[shift].start);
/** Last clock value HOV_CLOCK may reach for the current business date. */
export const hovClockCap=(date:string)=>addDays(date,1)+'T06:59';

// ── Access ──
/** Owner/Manager: edit templates, see every desk, reopen runs, dashboard. */
export const hovIsManager=(s:State,uid:string)=>can(s,uid,'approve');
/** Module access at all (checklist staff). Accountant / Tech admin have none. */
export const hovHasAccess=(s:State,uid:string)=>can(s,uid,'housekeeping')||can(s,uid,'payments')||can(s,uid,'approve');
const roleOf=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.role;
export function hovCanUseTemplate(s:State,uid:string,t:{roles:Role[]}){const r=roleOf(s,uid);return hovHasAccess(s,uid)&&(hovIsManager(s,uid)||!!r&&t.roles.includes(r));}
export function hovInDesk(s:State,uid:string,desk:HOVDesk){const r=roleOf(s,uid);return hovHasAccess(s,uid)&&(hovIsManager(s,uid)||!!r&&HOV_DESK_ROLES[desk].includes(r));}
/** Desks a user may read/write handover notes for. */
export const hovDesksFor=(s:State,uid:string)=>HOV_DESKS.filter(d=>hovInDesk(s,uid,d));
/** Cash lines are visible only to money roles (never housekeeping / maintenance). */
export const hovSeesCash=(s:State,uid:string)=>can(s,uid,'payments')||can(s,uid,'reports');

// ── Runs ──
export const hovRunId=(template:string,date:string)=>`HOVR-${template}-${date}`;
export function hovRunFor(s:State,template:string,date=s.date){return hovSlice(s).runs.find(r=>r.template===template&&r.date===date);}
/** A fresh (unsaved) run built from a template — used for display before the first tick and by HOV_ITEM. */
export function hovNewRun(t:HOVTemplate,date:string,uid:string,clock:string):HOVRun{
  return {id:hovRunId(t.id,date),template:t.id,templateVersion:t.version,name:t.name,desk:t.desk,shift:t.shift,roles:[...t.roles],date,
    items:t.items.map(i=>({...i,done:false})),status:'Open',startedBy:uid,startedAt:clock};}
export function hovItemOverdue(s:State,r:HOVRun,i:HOVRunItem){return !i.done&&!!i.due&&r.status==='Open'&&hovAt(r.date,r.shift,i.due)<s.clock;}
export type HOVProgress={total:number;done:number;pct:number;mandatory:number;mandatoryDone:number;overdue:number;late:boolean};
export function hovProgress(s:State,r:HOVRun):HOVProgress{
  const total=r.items.length,done=r.items.filter(i=>i.done).length,m=r.items.filter(i=>i.mandatory);
  const overdue=r.items.filter(i=>hovItemOverdue(s,r,i)).length;
  const late=r.status==='Open'?hovShiftEnd(r.date,r.shift)<s.clock:!!r.late;
  return {total,done,pct:total?Math.round(done*100/total):0,mandatory:m.length,mandatoryDone:m.filter(i=>i.done).length,overdue,late};}
/** Today's checklist cards for a user: existing runs plus not-yet-started active templates (as unsaved runs). Current shift first. */
export function hovRunsFor(s:State,uid:string,date=s.date):HOVRun[]{
  const x=hovSlice(s),cur=hovShiftAt(s.clock);
  const list:HOVRun[]=[];
  for(const t of x.templates){
    const r=x.runs.find(r=>r.template===t.id&&r.date===date);
    if(!hovCanUseTemplate(s,uid,{roles:r?.roles??t.roles}))continue;
    if(r)list.push(r);else if(t.active)list.push(hovNewRun(t,date,uid,s.clock));
  }
  const order=(r:HOVRun)=>(r.shift===cur?0:1)*10+HOV_SHIFTS.indexOf(r.shift);
  return list.sort((a,b)=>order(a)-order(b)||a.name.localeCompare(b.name));
}
/** Only the runs whose roles include the user's own role (managers: everything) — "My checklists". */
export function hovMyRuns(s:State,uid:string,date=s.date){const r=roleOf(s,uid);if(!hovHasAccess(s,uid))return [];
  return hovRunsFor(s,uid,date).filter(x=>hovIsManager(s,uid)||!!r&&x.roles.includes(r));}

// ── Handover notes ──
export const hovNoteId=(desk:HOVDesk,date:string,shift:HOVShift)=>`HOVN-${desk.split(' ')[0].toUpperCase()}-${date}-${shift[0]}`;
/** Notes awaiting acknowledgement by this user (desk member, not the author). */
export function hovIncoming(s:State,uid:string,desk?:HOVDesk){
  return hovSlice(s).notes.filter(n=>n.status==='Submitted'&&n.author!==uid&&(!desk||n.desk===desk)&&hovInDesk(s,uid,n.desk));}
/** Notes a user may read (by desk), newest first. */
export function hovNotesFor(s:State,uid:string){const d=hovDesksFor(s,uid);return hovSlice(s).notes.filter(n=>d.includes(n.desk)).sort((a,b)=>b.at.localeCompare(a.at));}
/** Open carry-forward items for a desk (from every note). */
export function hovOpenCarry(s:State,desk:HOVDesk){return hovSlice(s).notes.filter(n=>n.desk===desk).flatMap(n=>n.carry.filter(c=>!c.done).map(c=>({...c,note:n.id,from:n.author,date:n.date})));}

// ── Dashboard ──
export type HOVDashRow={key:string;label:string;runs:number;submitted:number;items:number;done:number;pct:number;overdue:number;late:number;notStarted:number};
/** Completion by role (or desk) for a business date. Includes active templates not yet started (0 %). */
export function hovDashboard(s:State,date:string,by:'role'|'desk'):HOVDashRow[]{
  const x=hovSlice(s),rows=new Map<string,HOVDashRow>();
  const runs:HOVRun[]=x.templates.map(t=>x.runs.find(r=>r.template===t.id&&r.date===date)??(t.active?hovNewRun(t,date,'',s.clock):null)).filter(Boolean) as HOVRun[];
  for(const r of runs){const started=x.runs.some(q=>q.id===r.id),p=hovProgress(s,r);
    for(const k of by==='role'?r.roles:[r.desk]){
      const row=rows.get(k)??{key:k,label:k,runs:0,submitted:0,items:0,done:0,pct:0,overdue:0,late:0,notStarted:0};
      row.runs++;if(r.status==='Submitted')row.submitted++;if(!started)row.notStarted++;row.items+=p.total;row.done+=p.done;row.overdue+=p.overdue;if(p.late)row.late++;
      rows.set(k,row);}}
  return [...rows.values()].map(r=>({...r,pct:r.items?Math.round(r.done*100/r.items):0}));
}
/** Business dates with any run (plus today), newest first. */
export function hovDates(s:State){return [...new Set([s.date,...hovSlice(s).runs.map(r=>r.date)])].sort().reverse();}
/** Overdue items across all runs of a date (manager list). */
export function hovOverdueList(s:State,date=s.date){
  return hovSlice(s).runs.filter(r=>r.date===date).flatMap(r=>r.items.filter(i=>hovItemOverdue(s,r,i)).map(i=>({run:r,item:i})));}
export const hovValidHHMM=(v:string)=>/^([01]\d|2[0-3]):[0-5]\d$/.test(v);
