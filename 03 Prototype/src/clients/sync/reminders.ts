// LF-ALERTS · desk reminders (B4) in the production sync layer.
//   server    POST /commands/SaveReminder | MarkReminderDone | DismissReminder (PERM-RMD-001: Owner, Manager, Reception, Accountant), GET /reminders?bucket=
//   commands  RMD_SAVE → SaveReminder (reminder id = the server's number after "RMD-"), RMD_DONE → MarkReminderDone, RMD_DISMISS → DismissReminder
//   GSV_WAKE_SNOOZE is mapped in services/guest.ts (SnoozeWakeUp).
import {HccClientError} from '../errors';
import type {SyncArea} from './types';
import type {RMDReminder,RMDState} from '../../domain/types';
import {protoUserId,utcToClock} from './map';

type SrvReminder={reminderId:number;subject:string;date:string;time:string|null;leadDays:number;role:string;note:string|null;repeat:string;status:string;nextDate:string|null;
  createdBy:number;createdUtc:string;doneBy:number|null;doneUtc:string|null};
type SrvReminderList={bucket:string;businessDate:string;items:SrvReminder[]};
const ROLE_NAME:Record<string,string>={OWN:'Owner',MGR:'Manager',REC:'Reception',CSH:'Cashier',ACC:'Accountant',SUP:'Supervisor',HKP:'Housekeeper',MNT:'Maintenance',TAD:'Tech admin',RST:'Restaurant'};
/** GET /reminders (3 buckets) → s.remindersX. The lead-days default stays the Owner setting (reminders.leadDaysDefault), read by rmdLeadDefault. */
export function remindersFromServer(lists:SrvReminderList[],leadDaysDefault=3):RMDState{
  const seen=new Map<number,RMDReminder>();
  for(const l of lists)for(const r of l.items)seen.set(r.reminderId,{id:'RMD-'+r.reminderId,subject:r.subject,date:r.date,...(r.time?{time:r.time}:{}),leadDays:r.leadDays,
    role:(r.role==='all'?'all':ROLE_NAME[r.role]??r.role) as RMDReminder['role'],...(r.note?{note:r.note}:{}),repeat:r.repeat as RMDReminder['repeat'],status:r.status as RMDReminder['status'],
    by:protoUserId(r.createdBy),at:utcToClock(r.createdUtc),...(r.doneBy!==null?{doneBy:protoUserId(r.doneBy)}:{}),...(r.doneUtc?{doneAt:utcToClock(r.doneUtc)}:{}),...(r.nextDate?{nextDate:r.nextDate}:{})});
  return {reminders:[...seen.values()],leadDaysDefault};
}

const rid=(v:unknown)=>{const m=/^(?:RMD-)?(\d+)$/.exec(String(v??''));if(!m)throw new HccClientError('VALIDATION','Choose the reminder.');return Number(m[1]);};

export const reminders:SyncArea={
  id:'reminders',
  loaders:[{slice:'remindersX',reads:['GET /reminders'],async load(c){
    const lists=await Promise.all(['today','upcoming','done'].map(b=>c.api.get<SrvReminderList>('/reminders',{bucket:b})));
    return remindersFromServer(lists,c.current('remindersX')?.leadDaysDefault);}}],
  commands:[
    {type:'RMD_SAVE',reload:['remindersX'],async send(c){const k=c.cmd;
      await c.api.command('SaveReminder',{reminderId:k.id?rid(k.id):undefined,subject:String(k.subject??''),date:String(k.date??''),time:k.time?String(k.time):undefined,
        leadDays:k.leadDays===undefined||k.leadDays===''?undefined:Number(k.leadDays),role:k.role?roleCode(String(k.role)):undefined,note:k.note?String(k.note):undefined,repeat:k.repeat?String(k.repeat):undefined});}},
    {type:'RMD_DONE',reload:['remindersX'],async send(c){await c.api.command('MarkReminderDone',{reminderId:rid(c.cmd.id)});}},
    {type:'RMD_DISMISS',reload:['remindersX'],async send(c){await c.api.command('DismissReminder',{reminderId:rid(c.cmd.id),reason:String(c.cmd.reason??'')});}},
  ],
};

/** Prototype role name → server role code (plat.role). */
export function roleCode(role:string):string{
  const m:Record<string,string>={all:'all',Owner:'OWN',Manager:'MGR',Reception:'REC',Cashier:'CSH',Accountant:'ACC',Supervisor:'SUP',Housekeeper:'HKP',Maintenance:'MNT','Tech admin':'TAD',Restaurant:'RST'};
  return m[role]??role;
}
