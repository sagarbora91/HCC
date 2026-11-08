// F8-QW-NA · Night-audit checklist: one pure list of lines for Day closing › Preflight (SF-CONTRACT §13 row qw-nightaudit).
// SHOW ONLY (owner question QWQ-1, default): a line carries `blocking:true` only when closingPreflight ALREADY blocks the close on the same
// records; nothing here adds a blocker, and the measurement group never blocks (MEAS-CONTRACT §1.4). The lines are computed from the state,
// so nothing in the BASE fixture changes. The same lines auto-tick the checklist HOVT-NA-PRE (na1…na5) through the existing HOV_ITEM command.
import type {State,Booking} from '../types';
import {closingPreflight,shiftStatus} from './closing';
import {keyActiveCards} from './keys';
import {measNightAuditLines} from '../reports/meas-daily/night-audit';
import {hovSlice} from './handover';

export type NALine={key:string;label:string;ok:boolean;detail:string;/** true only when closingPreflight blocks the close on the same records */blocking:boolean;/** page to open */link?:string;group:'operational'|'measurement'};

const plural=(n:number,w:string)=>`${n} ${w}${n===1?'':'s'}`;
const issue=(s:State,id:string)=>closingPreflight(s).find(p=>p.id===id);
const fromIssue=(s:State,id:string,key:string,label:string,okText:string,noun:string,link:string):NALine=>{
  const n=issue(s,id)?.items.length??0;
  return {key,label,ok:n===0,detail:n===0?okText:`${plural(n,noun)} to resolve`,blocking:true,link,group:'operational'};
};
/** Departed today (or still marked checked-out on the business date) with a key card that is still active or manual. */
export function naDepartedWithKeys(s:State):Booking[]{
  return s.bookings.filter(b=>b.status==='Departed'&&b.departure===s.date&&keyActiveCards(s,b.id).length>0);
}
/** A successful backup taken on the business date (system health: lastOkBackupAt, else the last backup run when it succeeded). */
export function naBackupToday(s:State):{ok:boolean;at?:string}{
  const h=s.sysHealthX,at=h?.lastOkBackupAt??(h?.lastBackup?.ok?h.lastBackup.at:undefined);
  return {ok:!!at&&at.slice(0,10)===s.date,at};
}

/** The night-audit lines in checklist order. Never throws on an old save (absent slices read as empty). */
export function naChecklist(s:State):NALine[]{
  const st=shiftStatus(s),keys=naDepartedWithKeys(s),bk=naBackupToday(s);
  const ap=issue(s,'approvals')?.items.length??0,ex=issue(s,'expenses')?.items.length??0;
  const lines:NALine[]=[
    fromIssue(s,'arrivals','na.arrivals','Arrivals resolved (checked in, cancelled or no-show)','Every arrival is resolved','arrival','reservations'),
    fromIssue(s,'departures','na.departures','Departures out','Every departure is checked out','overdue departure','folios'),
    fromIssue(s,'nights','na.nights','Room nights posted','Tonight’s room line is on every in-house stay','stay without tonight’s room line','closing'),
    {key:'na.approvals',label:'Pending approvals and expenses cleared',ok:ap+ex===0,detail:ap+ex===0?'Nothing waiting':`${plural(ap,'approval')} and ${plural(ex,'expense')} waiting`,blocking:true,link:'approvals',group:'operational'},
    fromIssue(s,'pos','na.pos','POS checks closed','No open restaurant check','open check','pos'),
    {key:'na.cash',label:'Cash count submitted',ok:st!=='Open',detail:st==='Open'?'The drawer is not counted yet':st==='Submitted'?'Counted · the difference awaits approval':'Counted and reconciled',blocking:true,link:'cashier',group:'operational'},
    {key:'na.keys',label:'Key cards cancelled for departed rooms',ok:keys.length===0,detail:keys.length===0?'No active card on a departed room':`${plural(keys.length,'room')} still with an active card · ${keys.map(b=>b.room).join(', ')}`,blocking:false,link:'keys',group:'operational'},
    {key:'na.backup',label:'Backup OK today',ok:bk.ok,detail:bk.ok?`Backup at ${bk.at!.slice(11,16)||bk.at}`:bk.at?`Last good backup ${bk.at.replace('T',' ')}`:'No backup recorded',blocking:false,link:'syshealth',group:'operational'},
  ];
  for(const m of measNightAuditLines(s))lines.push({key:m.key,label:m.label,ok:m.ok,detail:m.detail,blocking:false,group:'measurement'});
  return lines;
}

/** HOVT-NA-PRE items ticked from the lines (item id → the line keys that must all be ok). na6 (security round) stays manual. */
export const NA_AUTO_TICKS:Record<string,string[]>={na1:['na.arrivals'],na2:['na.nights'],na3:['na.approvals'],na4:['na.cash'],na5:['na.backup']};
export const NA_TEMPLATE='HOVT-NA-PRE';
export type NAAutoTick={item:string;text:string;ok:boolean;done:boolean};
/** Auto-tick view: for each of na1…na5, whether the lines pass and whether today's run already has it ticked. */
export function naAutoTicks(s:State):NAAutoTick[]{
  const lines=naChecklist(s),x=hovSlice(s),t=x.templates.find(t=>t.id===NA_TEMPLATE),run=x.runs.find(r=>r.template===NA_TEMPLATE&&r.date===s.date);
  if(!t)return [];
  return Object.entries(NA_AUTO_TICKS).flatMap(([item,keys])=>{
    const ti=(run?.items??t.items).find(i=>i.id===item);if(!ti)return [];
    return [{item,text:ti.text,ok:keys.every(k=>lines.find(l=>l.key===k)?.ok===true),done:!!(ti as {done?:boolean}).done}];
  });
}
/** Items to tick now: passing and not yet done. The caller sends HOV_ITEM {template:NA_TEMPLATE,item,done:true,note:'auto'} for each. */
export const naTicksToSend=(s:State)=>naAutoTicks(s).filter(t=>t.ok&&!t.done).map(t=>t.item);
