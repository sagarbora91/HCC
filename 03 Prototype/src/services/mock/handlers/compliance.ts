// Wave 0.5 · W05-C Compliance calendar — command handlers. Command types MUST start with 'CMP_'. Return value = audit/summary text.
// Access: Owner and Accountant (cmpHasAccess). Managing obligations, reopening and settings need the Owner's 'approve' permission.
import type {Handler,Kit} from '../commands';
import {
  CMPState,CMPObligation,CMPRule,CMPKind,CMPFreq,CMP_KINDS,CMP_MAX_PACKS,CMP_MAX_REOPENED,
  cmpSlice,cmpHasAccess,cmpItems,cmpCounts,cmpIsOpen,cmpPackLines,cmpValidDate,cmpMonthAdd,cmpFyLabel,cmpKey,cmpPeriodLabel,
} from '../../../domain/streams/compliance';

/** Longest length of each calendar month (index 0 = Jan); February 29 so a leap-year date is accepted and clamped where needed. */
const CMP_MONTH_MAX_DAYS=[31,29,31,30,31,30,31,31,30,31,30,31];
const slice=(k:Kit):CMPState=>{const x=cmpSlice(k.s);k.s.complianceX=x;return x;};
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const access=(k:Kit)=>{if(!cmpHasAccess(k.s,k.uid))k.fail('FORBIDDEN','Access denied: the compliance calendar is for the Owner and the Accountant.');};
const manage=(k:Kit)=>{access(k);k.req('approve');};
const item=(k:Kit)=>{const o=txt(k.cmd.obligation),p=txt(k.cmd.period);
  const i=cmpItems(k.s,true).find(x=>x.key===cmpKey(o,p));k.assert(i,'That obligation period is not on the calendar.','NOT_FOUND');return i!;};

function parseRule(k:Kit,freq:CMPFreq,inp:any):CMPRule{
  const t=txt(inp?.type);
  if(freq==='Annual'){const month=Number(inp?.month),day=Number(inp?.day);
    k.assert(Number.isInteger(month)&&month>=1&&month<=12&&Number.isInteger(day)&&day>=1&&day<=31,'Annual due date: choose a valid month and day.');
    return {type:'fixed',month,day};}
  if(t==='month-end')return {type:'month-end'};
  const day=Number(inp?.day);k.assert(t==='day'&&Number.isInteger(day)&&day>=1&&day<=28,'Choose “day of the next month” 1–28, or “last day of the next month”.');
  if(inp?.special){const sm=Number(inp.special.month),sd=Number(inp.special.day);
    // F3: the special date falls in the month after the special month, so it is checked against that month's real length (Feb: 29, clamped in leap-less years by cmpDueDate).
    k.assert(Number.isInteger(sm)&&sm>=1&&sm<=12&&Number.isInteger(sd)&&sd>=1&&sd<=CMP_MONTH_MAX_DAYS[sm%12],'Special-month date is not valid.');return {type:'day',day,special:{month:sm,day:sd}};}
  return {type:'day',day};}

export const handlers:Record<string,Handler>={
  CMP_FILE:k=>{
    access(k);const x=slice(k),i=item(k);
    k.assert(i.obligation.active,'This obligation is switched off.','INVALID_TRANSITION');
    k.assert(!i.filing,`Already ${i.filing?.status==='Filed'?'marked filed':'marked not applicable'}. The Owner can reopen it.`,'INVALID_TRANSITION');
    const reference=txt(k.cmd.reference);
    k.assert(reference.length>=4&&reference.length<=40&&/^[\w\-/. ]+$/.test(reference),'Enter the acknowledgement / challan reference (4–40 characters: letters, digits and - / .).');
    x.filings.push({obligation:i.obligation.id,period:i.period,status:'Filed',by:k.uid,at:k.s.clock,reference});
    return `${i.obligation.name} · ${i.periodLabel} filed · ref ${reference}${i.days<0?` · ${-i.days} day(s) late`:''}`;},

  CMP_NA:k=>{
    access(k);const x=slice(k),i=item(k);
    k.assert(i.obligation.active,'This obligation is switched off.','INVALID_TRANSITION');
    k.assert(!i.filing,'This period is already closed. The Owner can reopen it.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=5&&reason.length<=120,'Give a reason of 5–120 characters.');
    x.filings.push({obligation:i.obligation.id,period:i.period,status:'Not applicable',by:k.uid,at:k.s.clock,reason});
    return `${i.obligation.name} · ${i.periodLabel} not applicable · ${reason}`;},

  CMP_REOPEN:k=>{
    manage(k);const x=slice(k),i=item(k);
    k.assert(i.filing,'This period is not filed or marked not applicable.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=5&&reason.length<=120,'Give a reason of 5–120 characters.');
    x.filings=x.filings.filter(f=>cmpKey(f.obligation,f.period)!==i.key);
    x.reopened=[{...i.filing!,reopenedBy:k.uid,reopenedAt:k.s.clock,reopenReason:reason},...(x.reopened??[])].slice(0,CMP_MAX_REOPENED);
    return `${i.obligation.name} · ${i.periodLabel} reopened (was ${i.filing!.status}${i.filing!.reference?' · ref '+i.filing!.reference:''}) · ${reason}`;},

  CMP_OBLIGATION_SAVE:k=>{
    manage(k);const x=slice(k);
    const name=txt(k.cmd.name),form=txt(k.cmd.form),owner=txt(k.cmd.owner)||'Accountant';
    k.assert(name.length>=3&&name.length<=60,'Obligation name must be 3–60 characters.');
    k.assert(form.length<=60,'Form / portal is limited to 60 characters.');
    k.assert(owner.length<=30,'Owner is limited to 30 characters.');
    const effective=txt(k.cmd.effectiveFrom)||k.s.date;
    k.assert(cmpValidDate(effective)&&effective>=k.s.date,'Effective-from must be a valid date, today or later.');
    if(k.cmd.id){
      const o=x.obligations.find(o=>o.id===k.cmd.id);k.assert(o,'Obligation not found.','NOT_FOUND');
      k.assert(!x.obligations.some(z=>z.id!==o!.id&&z.name.toLowerCase()===name.toLowerCase()),'An obligation with this name already exists.');
      const rule=parseRule(k,o!.freq,k.cmd.rule),cur=JSON.stringify(o!.rules[o!.rules.length-1].rule);
      Object.assign(o!,{name,form,owner,updatedBy:k.uid,updatedAt:k.s.clock});const nt=txt(k.cmd.note);if(nt)o!.note=nt;else delete o!.note;
      if(JSON.stringify(rule)!==cur){
        o!.rules=[...o!.rules.filter(r=>r.from!==effective),{from:effective,rule}].sort((a,b)=>a.from.localeCompare(b.from));
        return `${name} · due rule changed from ${effective} (periods that began earlier keep the old rule)`;}
      return `${name} updated`;}
    const kind=txt(k.cmd.kind) as CMPKind;k.assert(CMP_KINDS.includes(kind),'Choose a type: TDS, GSTR-1, GSTR-3B, GSTR-9, EPF, ESIC or PT.');
    k.assert(!x.obligations.some(z=>z.name.toLowerCase()===name.toLowerCase()),'An obligation with this name already exists.');
    k.assert(x.obligations.length<30,'The calendar is limited to 30 obligations.');
    const freq:CMPFreq=kind==='GSTR-9'?'Annual':'Monthly',rule=parseRule(k,freq,k.cmd.rule);
    // Tracking starts at the latest finished period, so adding an obligation never creates a backlog of "overdue" rows.
    const fyStart=+k.s.date.slice(0,4)-(k.s.date.slice(5,7)<'04'?1:0);
    const from=freq==='Annual'?cmpFyLabel(fyStart-1):cmpMonthAdd(k.s.date.slice(0,7),-1);
    const o:CMPObligation={id:k.id('CMPO'),kind,name,form,freq,rules:[{from:'2000-01-01',rule}],owner,from,active:true,...(txt(k.cmd.note)?{note:txt(k.cmd.note)}:{}),updatedBy:k.uid,updatedAt:k.s.clock};
    x.obligations.push(o);return `${o.id} · ${name} added (tracked from ${cmpPeriodLabel(o,from)})`;},

  CMP_OBLIGATION_TOGGLE:k=>{
    manage(k);const x=slice(k),o=x.obligations.find(o=>o.id===k.cmd.id);k.assert(o,'Obligation not found.','NOT_FOUND');
    o!.active=!o!.active;o!.updatedBy=k.uid;o!.updatedAt=k.s.clock;
    return `${o!.name} · ${o!.active?'switched on':'switched off (history kept)'}`;},

  CMP_SETTINGS_SET:k=>{
    manage(k);const x=slice(k),c=k.cmd;
    if(c.caName!==undefined){const v=txt(c.caName);k.assert(v.length>=3&&v.length<=60,'CA name must be 3–60 characters.');x.settings.caName=v;}
    if(c.caEmail!==undefined){const v=txt(c.caEmail);k.assert(/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v)&&v.length<=80,'Enter a valid e-mail address.');x.settings.caEmail=v;}
    if(c.dueSoonDays!==undefined){const v=Number(c.dueSoonDays);k.assert(Number.isInteger(v)&&v>=1&&v<=30,'“Due this week” window must be 1–30 days.');x.settings.dueSoonDays=v;}
    return `Compliance settings saved · CA ${x.settings.caName} · window ${x.settings.dueSoonDays} days`;},

  CMP_CA_PACK:k=>{
    access(k);const x=slice(k);
    k.assert(x.settings.caEmail,'Set the CA e-mail in Manage obligations first.');
    const items=cmpItems(k.s),c=cmpCounts(items);
    const pack={id:k.id('CMPP'),at:k.s.clock,by:k.uid,to:x.settings.caEmail,toName:x.settings.caName,counts:{overdue:c.overdue,due:c.due,filed:c.filed,na:c.na},lines:cmpPackLines(k.s)};
    x.packs=[pack,...x.packs].slice(0,CMP_MAX_PACKS);
    return `${pack.id} · CA pack to ${x.settings.caName} (simulated — nothing sent) · ${c.overdue} overdue, ${items.filter(cmpIsOpen).length} open`;},
};
