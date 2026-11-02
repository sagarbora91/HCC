// LF-ALERTS · desk reminders (B4) in the production sync layer.
//   server    POST /commands/SaveReminder | MarkReminderDone | DismissReminder (PERM-RMD-001: Owner, Manager, Reception, Accountant), GET /reminders?bucket=
//   commands  RMD_SAVE → SaveReminder (reminder id = the server's number after "RMD-"), RMD_DONE → MarkReminderDone, RMD_DISMISS → DismissReminder
//   unmapped  GSV_WAKE_SNOOZE (no server command: the wake-up time moves with the request edit, not built in LF); the reminders slice has no
//             loader yet — it needs a GET /reminders route in sync/reads.ts and the role-read matrix (Hcc.Integration.Tests) and is left to the integrator.
import {HccClientError} from '../errors';
import type {SyncArea} from './types';

const rid=(v:unknown)=>{const m=/^(?:RMD-)?(\d+)$/.exec(String(v??''));if(!m)throw new HccClientError('VALIDATION','Choose the reminder.');return Number(m[1]);};

export const reminders:SyncArea={
  id:'reminders',
  loaders:[],
  commands:[
    {type:'RMD_SAVE',reload:[],async send(c){const k=c.cmd;
      await c.api.command('SaveReminder',{reminderId:k.id?rid(k.id):undefined,subject:String(k.subject??''),date:String(k.date??''),time:k.time?String(k.time):undefined,
        leadDays:k.leadDays===undefined||k.leadDays===''?undefined:Number(k.leadDays),role:k.role?roleCode(String(k.role)):undefined,note:k.note?String(k.note):undefined,repeat:k.repeat?String(k.repeat):undefined});}},
    {type:'RMD_DONE',reload:[],async send(c){await c.api.command('MarkReminderDone',{reminderId:rid(c.cmd.id)});}},
    {type:'RMD_DISMISS',reload:[],async send(c){await c.api.command('DismissReminder',{reminderId:rid(c.cmd.id),reason:String(c.cmd.reason??'')});}},
  ],
  unmapped:{GSV_WAKE_SNOOZE:'No server snooze command in LF (wake_at_utc is written at creation, migration 4703).'},
};

/** Prototype role name → server role code (plat.role). */
export function roleCode(role:string):string{
  const m:Record<string,string>={all:'all',Owner:'OWN',Manager:'MGR',Reception:'REC',Cashier:'CSH',Accountant:'ACC',Supervisor:'SUP',Housekeeper:'HKP',Maintenance:'MNT','Tech admin':'TAD',Restaurant:'RST'};
  return m[role]??role;
}
