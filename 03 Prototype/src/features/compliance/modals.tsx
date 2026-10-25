// Wave 0.5 · W05-C dialogs: mark filed, not applicable, reopen, manage obligations, edit obligation, CA pack (simulated).
import React,{useState} from 'react';
import {CheckCircle2,Ban,RotateCcw,Send,Plus,Pencil,Power,Mail} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {cmpItems,cmpSlice,cmpKey,cmpRuleText,cmpRuleFor,cmpMonthAdd,cmpPackLines,CMP_KINDS,CMP_ILLUSTRATIVE,CMP_MAX_PACKS} from '../../services/mock';
import type {CMPObligation,CMPKind} from '../../services/mock';
import {Empty,Pill,dateLabel} from '../../components/ui';
import {cmpActionKey,cmpName,CmpStatusPill} from './shared';

const useItem=(id?:string,room?:string)=>{const {s}=useHcc();return cmpItems(s,true).find(i=>i.key===cmpKey(id||'',room||''));};

export function CmpFileModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const i=useItem(id,room);const [ref,setRef]=useState('');
  if(!i)return <Empty title="This filing is unavailable"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CMP_FILE',obligation:i.obligation.id,period:i.period,reference:ref,key:cmpActionKey(s,'file',i.key)},'Marked filed',{close:true});}}>
    <div className="modal-heading"><h2>Mark filed</h2><p>{i.obligation.name} · {i.periodLabel} · due {dateLabel(i.due)} · <CmpStatusPill item={i}/></p></div>
    <label>Acknowledgement / challan reference<input required minLength={4} maxLength={40} value={ref} onChange={e=>setRef(e.target.value)} placeholder="e.g. the ARN or CIN shown on the portal" enterKeyHint="done" autoComplete="off"/></label>
    <small>Recorded with your name and the time ({s.clock.replace('T',' ')}). {i.days<0?`This filing is ${-i.days} day(s) late; the record keeps that.`:''}</small>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||ref.trim().length<4}><CheckCircle2 size={16}/>Mark filed</button></div>
  </form>;}

export function CmpNaModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const i=useItem(id,room);const [reason,setReason]=useState('');
  if(!i)return <Empty title="This filing is unavailable"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CMP_NA',obligation:i.obligation.id,period:i.period,reason,key:cmpActionKey(s,'na',i.key)},'Marked not applicable',{close:true});}}>
    <div className="modal-heading"><h2>Mark not applicable</h2><p>{i.obligation.name} · {i.periodLabel}. It leaves the open list; the Owner can reopen it.</p></div>
    <label>Reason (required)<textarea required minLength={5} maxLength={120} rows={3} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. No deductions made this month"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<5}><Ban size={16}/>Not applicable</button></div>
  </form>;}

export function CmpReopenModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const i=useItem(id,room);const [reason,setReason]=useState('');
  if(!i||!i.filing)return <Empty title="This filing is unavailable"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CMP_REOPEN',obligation:i.obligation.id,period:i.period,reason,key:cmpActionKey(s,'reopen',i.key)},'Reopened',{close:true});}}>
    <div className="modal-heading"><h2>Reopen filing</h2><p>{i.obligation.name} · {i.periodLabel} · {i.filing.status} by {cmpName(s,i.filing.by)}{i.filing.reference?` · ref ${i.filing.reference}`:''}. The reason goes to the audit trail.</p></div>
    <label>Reason (required)<textarea required minLength={5} maxLength={120} rows={3} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Wrong reference entered"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<5}><RotateCcw size={16}/>Reopen</button></div>
  </form>;}

export function CmpPackModal(){
  const {s,run,busy,closeModal}=useHcc();const x=cmpSlice(s);const lines=cmpPackLines(s);const last=x.packs[0];
  return <div className="form cmp-pack">
    <div className="modal-heading"><h2>Send CA pack</h2><p><Pill tone="orange">Simulated</Pill> Nothing is e-mailed. The pack is saved here as if sent to {x.settings.caName} ({x.settings.caEmail}).</p></div>
    <ul className="cmp-pack-lines" aria-label="Pack contents">{lines.map((l,n)=><li key={n}>{l}</li>)}</ul>
    {last&&<small>Last pack: {last.at.replace('T',' ')} by {cmpName(s,last.by)} · {last.counts.overdue} overdue at the time. Kept: the latest {CMP_MAX_PACKS}.</small>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      <button className="btn primary" disabled={busy} onClick={()=>run({type:'CMP_CA_PACK',key:cmpActionKey(s,'pack')},'CA pack saved as sent (simulated)',{close:true})}><Send size={16}/>Send (simulated)</button></div>
  </div>;}

export function CmpManageModal(){
  const {s,run,busy,open,closeModal}=useHcc();const x=cmpSlice(s);
  const [caName,setCaName]=useState(x.settings.caName),[caEmail,setCaEmail]=useState(x.settings.caEmail),[days,setDays]=useState(String(x.settings.dueSoonDays));
  const prev=cmpMonthAdd(s.date.slice(0,7),-1);
  return <div className="form cmp-manage">
    <div className="modal-heading"><h2>Manage obligations</h2><p>{CMP_ILLUSTRATIVE} A changed due rule applies from the date you choose; earlier periods keep theirs.</p></div>
    <ul className="cmp-ob-list">{x.obligations.map(o=><li key={o.id} className={o.active?'':'cmp-off'}>
      <div><strong>{o.name}</strong><small>{cmpRuleText(cmpRuleFor(o,prev))} · {o.owner}{o.active?'':' · switched off'}</small></div>
      <span className="cmp-ob-btns"><button type="button" className="btn small-btn" onClick={()=>open({type:'cmp-obligation',id:o.id})}><Pencil size={14}/>Edit</button>
        <button type="button" className="btn small-btn" disabled={busy} onClick={()=>run({type:'CMP_OBLIGATION_TOGGLE',id:o.id,key:cmpActionKey(s,'tog',o.id)},o.active?'Switched off':'Switched on')}><Power size={14}/>{o.active?'Switch off':'Switch on'}</button></span></li>)}</ul>
    <button type="button" className="btn" onClick={()=>open({type:'cmp-obligation'})}><Plus size={15}/>Add obligation</button>
    <fieldset className="cmp-fieldset"><legend>CA and reminders</legend>
      <label>CA / consultant<input value={caName} maxLength={60} onChange={e=>setCaName(e.target.value)}/></label>
      <label>CA e-mail (simulated send)<input type="email" inputMode="email" value={caEmail} maxLength={80} onChange={e=>setCaEmail(e.target.value)}/></label>
      <label>“Due this week” window (days)<input inputMode="numeric" value={days} onChange={e=>setDays(e.target.value.replace(/\D/g,''))}/></label>
      <button type="button" className="btn" disabled={busy} onClick={()=>run({type:'CMP_SETTINGS_SET',caName,caEmail,dueSoonDays:Number(days),key:cmpActionKey(s,'set')},'Settings saved')}><Mail size={15}/>Save CA and reminder settings</button></fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div>
  </div>;}

export function CmpObligationModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const o:CMPObligation|undefined=cmpSlice(s).obligations.find(o=>o.id===id);
  const cur=o?cmpRuleFor(o,cmpMonthAdd(s.date.slice(0,7),-1)):undefined;
  const [kind,setKind]=useState<CMPKind>(o?.kind??'TDS');const [name,setName]=useState(o?.name??'');const [form,setForm]=useState(o?.form??'');const [owner,setOwner]=useState(o?.owner??'Accountant');
  const annual=(o?.freq??(kind==='GSTR-9'?'Annual':'Monthly'))==='Annual';
  const [rt,setRt]=useState<'day'|'month-end'>(cur?.type==='month-end'?'month-end':'day');
  const [day,setDay]=useState(String(cur?.type==='day'?cur.day:cur?.type==='fixed'?cur.day:15));const [month,setMonth]=useState(String(cur?.type==='fixed'?cur.month:12));
  const [from,setFrom]=useState(s.date);
  if(id&&!o)return <Empty title="This obligation is unavailable"/>;
  const rule=annual?{type:'fixed',month:Number(month),day:Number(day)}:rt==='month-end'?{type:'month-end'}:{type:'day',day:Number(day),...(cur?.type==='day'&&cur.special?{special:cur.special}:{})};
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CMP_OBLIGATION_SAVE',id:o?.id,kind,name,form,owner,rule,effectiveFrom:from,key:cmpActionKey(s,'ob',o?.id??name)},o?'Obligation saved':'Obligation added',{close:true});}}>
    <div className="modal-heading"><h2>{o?'Edit obligation':'Add obligation'}</h2><p>{CMP_ILLUSTRATIVE}</p></div>
    {!o&&<label>Type<select value={kind} onChange={e=>setKind(e.target.value as CMPKind)}>{CMP_KINDS.map(k=><option key={k}>{k}</option>)}</select></label>}
    <label>Name<input required minLength={3} maxLength={60} value={name} onChange={e=>setName(e.target.value)} placeholder="e.g. PT — second establishment"/></label>
    <label>Form / portal<input maxLength={60} value={form} onChange={e=>setForm(e.target.value)}/></label>
    <label>Owner<input maxLength={30} value={owner} onChange={e=>setOwner(e.target.value)}/></label>
    <fieldset className="cmp-fieldset"><legend>Due date rule</legend>
      {annual?<div className="form-row"><label>Month<select value={month} onChange={e=>setMonth(e.target.value)}>{['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'].map((m,n)=><option key={m} value={n+1}>{m}</option>)}</select></label>
        <label>Day<input inputMode="numeric" value={day} onChange={e=>setDay(e.target.value.replace(/\D/g,''))}/></label></div>:
      <><label>Rule<select value={rt} onChange={e=>setRt(e.target.value as 'day'|'month-end')}><option value="day">A fixed day of the next month</option><option value="month-end">Last day of the next month</option></select></label>
        {rt==='day'&&<label>Day of the month (1–28)<input inputMode="numeric" value={day} onChange={e=>setDay(e.target.value.replace(/\D/g,''))}/></label>}</>}
      {o&&<label>Effective from<input type="date" min={s.date} value={from} onChange={e=>setFrom(e.target.value)}/></label>}
      {o&&<small>Periods that began before this date keep the current rule.</small>}</fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||name.trim().length<3}>{o?'Save':'Add'}</button></div>
  </form>;}
