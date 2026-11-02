// LF-ALERTS · lane lf-alerts (LF contract §9): B3 wake-up call alerts, B4 desk reminders ("alert days"), A8 function keys (domain half: shortcuts.ts).
// State slice s.remindersX (RMDState, declared in domain/types.ts). Pure helpers; commands in services/mock/handlers/reminders.ts.
// Roles that see the desk banner: Owner, Manager, Reception, Cashier (front desk). Housekeeping and maintenance never.
import type {State,Role,RMDReminder,RMDState} from '../types';
import {addDays} from '../money';
import {cfgValueOn} from './config';
import {gsvSlice} from './services';
import type {GSVRequest} from './services';

export const RMD_DEFAULT_LEAD_DAYS=3;
export const RMD_FRONT_DESK_ROLES:Role[]=['Owner','Manager','Reception','Cashier'];
export const RMD_REPEATS=['none','monthly','yearly'] as const;
export const rmdSlice=(s:State):RMDState=>{const x=s.remindersX??(s.remindersX={reminders:[],leadDaysDefault:RMD_DEFAULT_LEAD_DAYS});x.reminders??=[];return x;};
export const rmdRoleOf=(s:State,uid:string):Role|undefined=>s.users.find(u=>u.id===uid)?.role;
/** Owner setting reminders.leadDaysDefault (0–60, default 3). */
export function rmdLeadDefault(s:State):number{try{const v=Number(cfgValueOn(s,'reminders.leadDaysDefault'));return Number.isInteger(v)&&v>=0&&v<=60?v:RMD_DEFAULT_LEAD_DAYS;}catch{return RMD_DEFAULT_LEAD_DAYS;}}
export const rmdSettingDefs=[{key:'reminders.leadDaysDefault',label:'Reminder lead days (default)',type:'integer' as const,min:0,max:60,default:RMD_DEFAULT_LEAD_DAYS,suffix:'days',unit:'days before the date',
  help:'A new desk reminder shows on the desk banner this many days before its date unless it names its own lead time.',affects:'Reminders saved from now on.'}];

/** The date a reminder is next due: the rolled date of a repeating reminder, else its date. */
export const rmdDueDate=(r:RMDReminder)=>r.nextDate??r.date;
/** Date + one month / one year (day clamped to the month's last day). */
export function rmdRoll(date:string,repeat:RMDReminder['repeat']):string{
  if(repeat==='none')return date;
  const [y,m,d]=date.split('-').map(Number);
  const months=repeat==='yearly'?12:1,total=(y*12+(m-1))+months,ny=Math.floor(total/12),nm=total%12+1;
  const last=new Date(Date.UTC(ny,nm,0)).getUTCDate();
  return `${ny}-${String(nm).padStart(2,'0')}-${String(Math.min(d,last)).padStart(2,'0')}`;
}
/** May this user see a reminder (addressed to all, or to their role)? */
export const rmdVisibleTo=(s:State,uid:string,r:RMDReminder)=>{const role=rmdRoleOf(s,uid);return !!role&&(r.role==='all'||r.role===role||r.by===uid||role==='Owner');};
/** Date from which the banner shows the reminder. */
export const rmdShowFrom=(r:RMDReminder)=>addDays(rmdDueDate(r),-r.leadDays);
export type RMDBucket='today'|'upcoming'|'done';
/** Reminders of a tab: today = open and due within the lead window (incl. overdue); upcoming = open and later; done = done / dismissed. */
export function rmdList(s:State,uid:string,bucket:RMDBucket):RMDReminder[]{
  const all=rmdSlice(s).reminders.filter(r=>rmdVisibleTo(s,uid,r));
  const by=(a:RMDReminder,b:RMDReminder)=>rmdDueDate(a).localeCompare(rmdDueDate(b))||(a.time??'').localeCompare(b.time??'');
  if(bucket==='done')return all.filter(r=>r.status!=='open').sort((a,b)=>(b.doneAt??b.at).localeCompare(a.doneAt??a.at));
  const open=all.filter(r=>r.status==='open');
  return (bucket==='today'?open.filter(r=>rmdShowFrom(r)<=s.date):open.filter(r=>rmdShowFrom(r)>s.date)).sort(by);
}
/** Is this user shown the desk banner at all? */
export const rmdSeesBanner=(s:State,uid:string)=>{const r=rmdRoleOf(s,uid);return !!r&&RMD_FRONT_DESK_ROLES.includes(r);};
/** Wake-up calls due now: wakeAt ≤ the clock and the request not finished (oldest first). */
export function rmdWakeDue(s:State):GSVRequest[]{
  return gsvSlice(s).requests.filter(r=>r.type==='Wake-up call'&&r.wakeAt&&r.wakeAt<=s.clock&&r.status!=='Done'&&r.status!=='Cancelled').sort((a,b)=>a.wakeAt!.localeCompare(b.wakeAt!));
}
/** All wake-up calls not finished (the Wake-ups tab): due ones first. */
export const rmdWakeOpen=(s:State)=>gsvSlice(s).requests.filter(r=>r.type==='Wake-up call'&&r.wakeAt&&r.status!=='Done'&&r.status!=='Cancelled').sort((a,b)=>a.wakeAt!.localeCompare(b.wakeAt!));
export const rmdWakeDone=(s:State)=>gsvSlice(s).requests.filter(r=>r.type==='Wake-up call'&&r.status==='Done').sort((a,b)=>(b.doneAt??'').localeCompare(a.doneAt??'')).slice(0,30);
export type RMDAlerts={wake:GSVRequest[];reminders:RMDReminder[];count:number};
/** What the desk banner shows for this user at the simulated clock. `later` = ids hidden until tomorrow by the user ("Later"). */
export function rmdDue(s:State,uid:string,later:readonly string[]=[]):RMDAlerts{
  if(!rmdSeesBanner(s,uid))return {wake:[],reminders:[],count:0};
  const wake=rmdWakeDue(s);
  const reminders=rmdList(s,uid,'today').filter(r=>!later.includes(r.id));
  return {wake,reminders,count:wake.length+reminders.length};
}
/** Dashboard attention count (reminders in the window + due wake-ups) for the signed-in user. */
export const rmdAttentionCount=(s:State,uid:string)=>rmdDue(s,uid).count;
export const rmdLabel=(r:RMDReminder)=>`${r.subject} · ${rmdDueDate(r)}${r.time?' '+r.time:''}`;
