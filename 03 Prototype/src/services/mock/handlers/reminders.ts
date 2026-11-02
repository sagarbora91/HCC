// LF-ALERTS · desk reminders (LF contract §9.2). Commands RMD_SAVE, RMD_DONE, RMD_DISMISS; slice s.remindersX.
import type {Handler,Kit} from '../commands';
import type {RMDReminder,Role} from '../../../domain/types';
import {rmdCanManage,rmdSlice,rmdRoleOf,rmdRoll,rmdDueDate,rmdLeadDefault,RMD_REPEATS} from '../../../domain/streams/reminders';
import {guestLooksLikeIdNumber} from '../../../domain/streams/guests';

const DATE=/^\d{4}-\d{2}-\d{2}$/,TIME=/^([01]\d|2[0-3]):[0-5]\d$/;
const ROLES:Role[]=['Owner','Manager','Reception','Cashier','Supervisor','Housekeeper','Accountant','Maintenance','Tech admin'];
const txt=(v:unknown)=>String(v??'').trim();
const may=(k:Kit)=>{if(!rmdCanManage(k.s,k.uid))k.fail('FORBIDDEN','Access denied: PERM-RMD-001 (reminders) is required.');};
const find=(k:Kit)=>{const r=rmdSlice(k.s).reminders.find(x=>x.id===k.cmd.id);k.assert(r,'Reminder not found.','NOT_FOUND');return r!;};
const isBoss=(k:Kit)=>{const r=rmdRoleOf(k.s,k.uid);return r==='Owner'||r==='Manager';};
/** Who may act on a reminder: its author, an Owner / Manager, or anyone it is addressed to. */
const mayAct=(k:Kit,r:RMDReminder)=>isBoss(k)||r.by===k.uid||r.role==='all'||r.role===rmdRoleOf(k.s,k.uid);

export const handlers:Record<string,Handler>={
  /** Create or edit a reminder. {id?, subject, date, time?, leadDays?, role?, note?, repeat?}. */
  RMD_SAVE:k=>{
    const {s,cmd,assert}=k;may(k);
    const subject=txt(cmd.subject);assert(subject.length>=2&&subject.length<=80,'Subject: 2–80 characters.');
    assert(DATE.test(txt(cmd.date))&&!Number.isNaN(Date.parse(txt(cmd.date)+'T00:00:00Z')),'Enter the reminder date.');
    const time=txt(cmd.time);assert(!time||TIME.test(time),'Time as HH:mm.');
    const lead=cmd.leadDays===undefined||cmd.leadDays===''?rmdLeadDefault(s):Number(cmd.leadDays);assert(Number.isInteger(lead)&&lead>=0&&lead<=60,'Lead days: 0–60.');
    const role=(txt(cmd.role)||'all') as Role|'all';assert(role==='all'||ROLES.includes(role as Role),'Choose who the reminder is for.');
    const mine=rmdRoleOf(s,k.uid);if(!isBoss(k))assert(role==='all'||role===mine,'You can address a reminder to everyone or to your own role only.');
    const note=txt(cmd.note);assert(note.length<=200,'Note: at most 200 characters.');
    assert(!guestLooksLikeIdNumber(subject)&&!guestLooksLikeIdNumber(note),'This looks like an ID number. Store ID numbers only in the ID section.');
    const repeat=(txt(cmd.repeat)||'none') as RMDReminder['repeat'];assert((RMD_REPEATS as readonly string[]).includes(repeat),'Repeat: none, monthly or yearly.');
    const x=rmdSlice(s);
    if(cmd.id){
      const r=find(k);assert(r.status==='open','A finished reminder cannot be edited.','INVALID_TRANSITION');assert(isBoss(k)||r.by===k.uid,'Only the author, a Manager or the Owner can edit this reminder.');
      Object.assign(r,{subject,date:txt(cmd.date),leadDays:lead,role,repeat});delete r.nextDate;
      if(time)r.time=time;else delete r.time;if(note)r.note=note;else delete r.note;
      return `${r.id} · reminder updated · ${subject}`;
    }
    const r:RMDReminder={id:k.id('RMD'),subject,date:txt(cmd.date),leadDays:lead,role,repeat,status:'open',by:k.uid,at:s.clock,...(time?{time}:{}),...(note?{note}:{})};
    x.reminders.push(r);
    return `${r.id} · reminder set for ${r.date}${time?' '+time:''} · ${subject}`;
  },
  /** Done: a repeating reminder rolls to its next date and stays open. {id} */
  RMD_DONE:k=>{
    const {s,assert}=k;may(k);const r=find(k);assert(r.status==='open','This reminder is already finished.','INVALID_TRANSITION');assert(mayAct(k,r),'This reminder is not addressed to you.','FORBIDDEN');
    r.doneBy=k.uid;r.doneAt=s.clock;
    if(r.repeat!=='none'){const next=rmdRoll(rmdDueDate(r),r.repeat);r.nextDate=next;return `${r.id} · done · next ${next} · ${r.subject}`;}
    r.status='done';return `${r.id} · done · ${r.subject}`;
  },
  /** Dismiss with a reason (audited). {id, reason} */
  RMD_DISMISS:k=>{
    const {s,cmd,assert}=k;may(k);const r=find(k);assert(r.status==='open','This reminder is already finished.','INVALID_TRANSITION');assert(mayAct(k,r),'This reminder is not addressed to you.','FORBIDDEN');
    const reason=txt(cmd.reason);assert(reason.length>=3&&reason.length<=200,'Give the reason (3–200 characters).');
    r.status='dismissed';r.doneBy=k.uid;r.doneAt=s.clock;r.note=((r.note?r.note+' · ':'')+'Dismissed: '+reason).slice(0,200);
    return `${r.id} · dismissed · ${reason}`;
  },
};
