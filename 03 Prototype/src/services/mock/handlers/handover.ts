// Module 19 · Checklists & shift handover — command handlers. Command types MUST start with 'HOV_'. Return value = audit/summary text.
import type {Handler,Kit} from '../commands';
import type {Role} from '../../../domain/types';
import {roles as allRoles} from '../../../domain/types';
import {money,addMinutes} from '../../../domain/money';
import {
  HOVState,HOVTemplate,HOVItem,HOVDesk,HOVShift,HOV_DESKS,HOV_SHIFTS,HOV_DESK_ROLES,HOV_MAX_ITEMS,HOV_MAX_CARRY,HOV_NOTE_MAX,
  hovSlice,hovNewRun,hovCanUseTemplate,hovInDesk,hovIsManager,hovHasAccess,hovIncoming,hovShiftAt,hovNextShift,hovShiftEnd,hovNoteId,hovValidHHMM,hovClockCap,
} from '../../../domain/streams/handover';

const slice=(k:Kit):HOVState=>{const x=hovSlice(k.s);k.s.handoverX=x;return x;};
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const dayOpen=(k:Kit)=>k.assert(!k.s.closedDates.includes(k.s.date),'This business day is closed. Open the next business day first.','DAY_CLOSED');
const template=(k:Kit,id:unknown)=>{const t=slice(k).templates.find(t=>t.id===id);k.assert(t,'Checklist template not found.','NOT_FOUND');return t!;};
const access=(k:Kit)=>{if(!hovHasAccess(k.s,k.uid))k.fail('FORBIDDEN','Access denied: checklists need the housekeeping, payments or approve permission.');};

export const handlers:Record<string,Handler>={
  HOV_TEMPLATE_SAVE:k=>{
    k.req('approve');const x=slice(k),inp=k.cmd.template||{};
    const name=txt(inp.name);k.assert(name.length>=3&&name.length<=60,'Template name must be 3–60 characters.');
    k.assert(HOV_DESKS.includes(inp.desk),'Choose a desk.');k.assert(HOV_SHIFTS.includes(inp.shift),'Choose a shift.');
    const desk=inp.desk as HOVDesk,shift=inp.shift as HOVShift;
    const roles=[...new Set((Array.isArray(inp.roles)?inp.roles:[]) as Role[])];
    k.assert(roles.length>0,'Choose at least one role for this checklist.');
    k.assert(roles.every(r=>allRoles.includes(r)&&(HOV_DESK_ROLES[desk].includes(r)||r==='Owner')),`Roles must belong to the ${desk} desk (${HOV_DESK_ROLES[desk].join(', ')}).`);
    const items=(Array.isArray(inp.items)?inp.items:[]) as Partial<HOVItem>[];
    k.assert(items.length>=1&&items.length<=HOV_MAX_ITEMS,`A checklist needs 1–${HOV_MAX_ITEMS} items.`);
    const clean:HOVItem[]=items.map((i,n)=>{const text=txt(i.text);
      k.assert(text.length>=3&&text.length<=120,`Item ${n+1}: text must be 3–120 characters.`);
      const due=txt(i.due);k.assert(!due||hovValidHHMM(due),`Item ${n+1}: due time must be HH:mm (24-hour).`);
      return {id:txt(i.id)||`i${n+1}-${k.s.sequence+1}`,text,mandatory:!!i.mandatory,...(due?{due}:{}),...(i.photo?{photo:true}:{}),...(txt(i.link)?{link:txt(i.link)}:{})};});
    k.assert(new Set(clean.map(i=>i.id)).size===clean.length,'Duplicate item IDs.');
    if(inp.id){const t=template(k,inp.id);Object.assign(t,{name,desk,shift,roles,items:clean,version:t.version+1,updatedBy:k.uid,updatedAt:k.s.clock});return `${t.id} · ${name} · v${t.version} · existing runs keep their snapshot`;}
    k.assert(!x.templates.some(t=>t.name.toLowerCase()===name.toLowerCase()),'A template with this name already exists.');
    const t:HOVTemplate={id:k.id('HOVT'),name,desk,shift,roles,items:clean,active:true,version:1,updatedBy:k.uid,updatedAt:k.s.clock};
    x.templates.push(t);return `${t.id} · ${name} created (${clean.length} items)`;},

  HOV_TEMPLATE_TOGGLE:k=>{k.req('approve');const t=template(k,k.cmd.id);t.active=!t.active;t.updatedBy=k.uid;t.updatedAt=k.s.clock;return `${t.name} · ${t.active?'activated':'deactivated'}`;},

  HOV_ITEM:k=>{
    access(k);dayOpen(k);const x=slice(k),t=template(k,k.cmd.template);
    let r=x.runs.find(r=>r.template===t.id&&r.date===k.s.date);
    if(!hovCanUseTemplate(k.s,k.uid,{roles:r?.roles??t.roles}))k.fail('FORBIDDEN','This checklist belongs to another role.');
    if(!r){k.assert(t.active,'This checklist template is inactive.','INVALID_TRANSITION');r=hovNewRun(t,k.s.date,k.uid,k.s.clock);x.runs.push(r);}
    k.assert(r.status==='Open','This checklist was submitted. A manager can reopen it.','INVALID_TRANSITION');
    const i=r.items.find(i=>i.id===k.cmd.item);k.assert(i,'Checklist item not found.','NOT_FOUND');
    if(k.cmd.note!==undefined){const n=txt(k.cmd.note);k.assert(n.length<=HOV_NOTE_MAX,`Notes are limited to ${HOV_NOTE_MAX} characters.`);i!.note=n||undefined;}
    if(k.cmd.photo!==undefined){const p=txt(k.cmd.photo);k.assert(p.length<=80&&(!p||/^[\w .()-]+\.(jpe?g|png|heic)$/i.test(p)),'Photo name must be a file name ending .jpg, .png or .heic (simulated — no upload).');i!.photoName=p||undefined;}
    if(k.cmd.done!==undefined){
      const done=!!k.cmd.done;
      if(done)k.assert(!i!.photo||i!.photoName,'Add a photo file name for this item first (simulated photo).');
      i!.done=done;if(done){i!.by=k.uid;i!.at=k.s.clock;}else{i!.by=undefined;i!.at=undefined;}}
    return `${r.name} · ${r.date} · ${i!.text}${k.cmd.done===undefined?' · note/photo updated':i!.done?' · done':' · unticked'}`;},

  HOV_RUN_SUBMIT:k=>{
    access(k);dayOpen(k);const x=slice(k),t=template(k,k.cmd.template);
    const r=x.runs.find(r=>r.template===t.id&&r.date===k.s.date);
    k.assert(r,'Tick at least the mandatory items before submitting.','INVALID_TRANSITION');
    if(!hovCanUseTemplate(k.s,k.uid,r!))k.fail('FORBIDDEN','This checklist belongs to another role.');
    k.assert(r!.status==='Open','This checklist is already submitted.','INVALID_TRANSITION');
    const missing=r!.items.filter(i=>i.mandatory&&!i.done);
    k.assert(!missing.length,`${missing.length} mandatory item${missing.length===1?' is':'s are'} not done: ${missing.map(i=>i.text).slice(0,3).join('; ')}.`,'INVALID_TRANSITION');
    const inc=hovIncoming(k.s,k.uid,r!.desk);
    k.assert(!inc.length,`Acknowledge the incoming ${r!.desk} handover note first (${inc[0]?.id}).`,'INVALID_TRANSITION');
    r!.status='Submitted';r!.submittedBy=k.uid;r!.submittedAt=k.s.clock;r!.late=hovShiftEnd(r!.date,r!.shift)<k.s.clock;
    const done=r!.items.filter(i=>i.done).length;
    return `${r!.name} · ${r!.date} submitted · ${done}/${r!.items.length} items${r!.late?' · late':''}`;},

  HOV_RUN_REOPEN:k=>{
    k.req('approve');dayOpen(k);const x=slice(k),r=x.runs.find(r=>r.template===k.cmd.template&&r.date===(k.cmd.date||k.s.date));
    k.assert(r,'Checklist run not found.','NOT_FOUND');k.assert(r!.status==='Submitted','Only a submitted checklist can be reopened.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=3,'A reason is required to reopen a checklist.');
    r!.status='Open';r!.reopenReason=reason;r!.submittedBy=undefined;r!.submittedAt=undefined;r!.late=undefined;
    return `${r!.name} · ${r!.date} reopened · ${reason}`;},

  HOV_NOTE_SUBMIT:k=>{
    access(k);dayOpen(k);const x=slice(k);
    k.assert(HOV_DESKS.includes(k.cmd.desk),'Choose a desk.');const desk=k.cmd.desk as HOVDesk;
    if(!hovInDesk(k.s,k.uid,desk))k.fail('FORBIDDEN',`Only ${desk} staff can write this handover note.`);
    const shift:HOVShift=HOV_SHIFTS.includes(k.cmd.shift)?k.cmd.shift:hovShiftAt(k.s.clock);
    const issues=txt(k.cmd.issues),keys=txt(k.cmd.keys),guests=txt(k.cmd.guests),nothing=!!k.cmd.nothing;
    k.assert(nothing||issues.length>=3,'Describe the open issues, or tick “Nothing to report”.');
    for(const [l,v] of [['Open issues',issues],['Keys',keys],['Guest notes',guests]] as const)k.assert(v.length<=400,`${l}: maximum 400 characters.`);
    let cash:{amount:number;note:string}|undefined;
    if(k.cmd.cash&&(k.cmd.cash.amount!==undefined&&k.cmd.cash.amount!==null)){
      k.assert(desk==='Front desk','Cash lines belong only on the Front desk handover.');k.req('payments');
      const a=k.cmd.cash.amount;k.assert(Number.isSafeInteger(a)&&a>=0&&a<=100000000,'Enter the cash float handed over (₹0 – ₹10,00,000).');
      cash={amount:a,note:txt(k.cmd.cash.note).slice(0,HOV_NOTE_MAX)};}
    const carryIn=(Array.isArray(k.cmd.carry)?k.cmd.carry:[]).map(txt).filter(Boolean) as string[];
    k.assert(carryIn.length<=HOV_MAX_CARRY,`Up to ${HOV_MAX_CARRY} carry-forward items.`);
    k.assert(carryIn.every(c=>c.length>=3&&c.length<=120),'Each carry-forward item must be 3–120 characters.');
    const id=hovNoteId(desk,k.s.date,shift),old=x.notes.find(n=>n.id===id);
    if(old){k.assert(old.status==='Submitted','This handover note was already acknowledged by the next shift.','INVALID_TRANSITION');
      k.assert(old.author===k.uid||hovIsManager(k.s,k.uid),'Only the author or a manager can replace this note.','FORBIDDEN');x.notes=x.notes.filter(n=>n.id!==id);}
    x.notes.push({id,desk,date:k.s.date,shift,toShift:hovNextShift(shift),author:k.uid,at:k.s.clock,issues,nothing,keys,guests,cash,
      carry:carryIn.map((text,n)=>({id:`${id}-c${n+1}`,text,done:false})),status:'Submitted'});
    return `${id} · ${desk} ${shift} → ${hovNextShift(shift)}${cash?' · float '+money(cash.amount):''} · ${carryIn.length} carry-forward${old?' · replaced':''}`;},

  HOV_NOTE_ACK:k=>{
    access(k);const x=slice(k),n=x.notes.find(n=>n.id===k.cmd.id);k.assert(n,'Handover note not found.','NOT_FOUND');
    if(!hovInDesk(k.s,k.uid,n!.desk))k.fail('FORBIDDEN',`Only ${n!.desk} staff can acknowledge this note.`);
    k.assert(n!.status==='Submitted','This note is already acknowledged.','INVALID_TRANSITION');
    k.assert(n!.author!==k.uid,'You cannot acknowledge your own handover note. The next shift must acknowledge it.','FORBIDDEN');
    const remark=txt(k.cmd.remark);k.assert(remark.length<=HOV_NOTE_MAX,`Remark: maximum ${HOV_NOTE_MAX} characters.`);
    n!.status='Acknowledged';n!.ackBy=k.uid;n!.ackAt=k.s.clock;n!.ackRemark=remark||undefined;
    return `${n!.id} · acknowledged · ${n!.carry.filter(c=>!c.done).length} carry-forward open`;},

  HOV_CARRY_DONE:k=>{
    access(k);const x=slice(k),n=x.notes.find(n=>n.id===k.cmd.note);k.assert(n,'Handover note not found.','NOT_FOUND');
    if(!hovInDesk(k.s,k.uid,n!.desk))k.fail('FORBIDDEN',`Only ${n!.desk} staff can close this item.`);
    const c=n!.carry.find(c=>c.id===k.cmd.item);k.assert(c,'Carry-forward item not found.','NOT_FOUND');
    k.assert(!c!.done,'This item is already done.','INVALID_TRANSITION');
    c!.done=true;c!.doneBy=k.uid;c!.doneAt=k.s.clock;return `${n!.desk} carry-forward done · ${c!.text}`;},

  HOV_CLOCK:k=>{
    k.req('close');dayOpen(k);const m=Number(k.cmd.minutes);
    k.assert(Number.isInteger(m)&&m>=15&&m<=120,'Advance the demo clock by 15–120 minutes.');
    const next=addMinutes(k.s.clock,m),cap=hovClockCap(k.s.date);
    k.assert(next<=cap,'The demo clock cannot pass 06:59 of the next morning. Close the business day instead.','INVALID_TRANSITION');
    const from=k.s.clock;k.s.clock=next;return `Demo · simulated clock ${from.slice(11)} → ${next.slice(11)} (+${m} min)`;},
};
