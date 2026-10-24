// Module 18 · Guest services — dialogs (desktop modal / phone full-screen sheet). Modal IDs prefixed gsv-.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Info,ShieldCheck,MessageSquareText,Printer} from 'lucide-react';
import {money,
  GSV_REQUEST_TYPES,GSV_DEPTS,GSV_COMPLAINT_CATEGORIES,GSV_SEVERITIES,GSV_ITEM_CATEGORIES,GSV_STORAGE,GSV_SAFE,GSV_ID_TYPES,GSV_DISPOSAL,
  gsvSlice,gsvDefaultDept,gsvAssignable,gsvSlaMinutes,gsvSlaLabel,gsvCanWork,gsvIsOpen,gsvRecoveryStatus,gsvIsValuable,gsvDisposable,gsvDisposeOn,
  gsvItemAgeDays,gsvRetentionDays,gsvFollowUpDue,gsvResponseReport,gsvResponseTotals,gsvComplaintReport,gsvLostFoundSummary,can,
  type GSVRequestType,type GSVDept,type GSVLog} from '../../services/mock';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {Pill,Empty,dateLabel,clockTime,field,amount,submit} from '../../components/ui';

export const gsvWhen=(c:string)=>`${dateLabel(c.slice(0,10)).replace(/ \d{4}$/,'')} · ${clockTime(c)}`;
export const gsvUserName=(s:ReturnType<typeof useHcc>['s'],uid?:string)=>uid?(s.users.find(u=>u.id===uid)?.name.split(' · ')[0]??uid):'Unassigned';
export const gsvStatusTone=(st:string)=>st==='Done'||st==='Closed'||st==='Handed over'?'green':st==='Cancelled'||st==='Disposed'?'neutral':st==='In progress'||st==='Investigating'||st==='Claimed'?'blue':'orange';
export const gsvSevTone=(v:string)=>v==='Critical'||v==='High'?'gsv-red':v==='Medium'?'orange':'neutral';
function Missing(){return <Empty title="This record is unavailable"/>;}
function History({log}:{log:GSVLog[]}){const {s}=useHcc();
  return <details className="gsv-history"><summary>History ({log.length})</summary><ul>{log.slice().reverse().map((l,i)=><li key={i}><span>{gsvWhen(l.at)}</span> {gsvUserName(s,l.by)} · {l.what}</li>)}</ul></details>;}

// ── Service request: new ──
export function GsvRequestNew({id}:ModalProps){
  const {s,run,modal,busy,closeModal}=useHcc();
  const inHouse=s.bookings.filter(b=>b.status==='In house');
  const [bid,setBid]=useState(id&&inHouse.some(b=>b.id===id)?id:inHouse[0]?.id||'');
  const [type,setType]=useState<GSVRequestType>('Extra towels');
  const [dept,setDept]=useState<GSVDept>(gsvDefaultDept('Extra towels'));
  const [priority,setPriority]=useState<'Normal'|'Urgent'>('Normal');
  if(!inHouse.length)return <><div className="modal-heading"><h2>New guest request</h2></div><Empty title="No in-house guests" body="Requests can be logged only for guests who are checked in."/></>;
  const pool=gsvAssignable(s,dept);
  return <><div className="modal-heading"><h2>New guest request</h2><p>Logged on the simulated clock · SLA {type==='Wake-up call'?'from the wake-up time':gsvSlaMinutes(s,type,priority)+' min'}</p></div>
    <form className="form gsv-form" onSubmit={e=>submit(e,f=>run({type:'GSV_REQUEST_CREATE',id:bid,requestType:type,dept,priority,assignee:field(f,'assignee'),detail:field(f,'detail'),time:field(f,'time'),key:modal!.key},'Guest request logged',{close:true}))}>
      <label>Guest / room<select value={bid} onChange={e=>setBid(e.target.value)}>{inHouse.map(b=><option key={b.id} value={b.id}>Room {b.room} · {b.guest} · {b.id}</option>)}</select></label>
      <div className="form-row"><label>Request<select value={type} onChange={e=>{const t=e.target.value as GSVRequestType;setType(t);setDept(gsvDefaultDept(t));}}>{GSV_REQUEST_TYPES.map(t=><option key={t}>{t}</option>)}</select></label>
        <label>Priority<select value={priority} onChange={e=>setPriority(e.target.value as 'Normal'|'Urgent')}><option>Normal</option><option>Urgent</option></select></label></div>
      {type==='Wake-up call'&&<label>Wake-up time<input name="time" type="time" required defaultValue="06:30" enterKeyHint="next"/><small>A time earlier than now is scheduled for tomorrow.</small></label>}
      <label>Detail{type==='Other'?'':' (optional)'}<input name="detail" maxLength={160} required={type==='Other'} placeholder="e.g. 2 bath towels, 4 bottles" enterKeyHint="next"/></label>
      <div className="form-row"><label>Department<select value={dept} onChange={e=>setDept(e.target.value as GSVDept)}>{GSV_DEPTS.map(d=><option key={d}>{d}</option>)}</select></label>
        <label>Assign to<select name="assignee" key={dept} defaultValue=""><option value="">Not yet assigned</option>{pool.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!bid}>Log request</button></div>
    </form></>;
}

// ── Service request: view / assign / start / complete / cancel ──
export function GsvRequestView({id}:ModalProps){
  const {s,uid,run,busy,permission,modal}=useHcc();
  const r=gsvSlice(s).requests.find(r=>r.id===id);
  const [assignee,setAssignee]=useState('');const [note,setNote]=useState('');const [reason,setReason]=useState('');
  if(!r)return <Missing/>;
  const sla=gsvSlaLabel(s,r),work=gsvCanWork(s,uid,r),open=gsvIsOpen(r);
  const manage=permission('reservations')||(permission('inspect')&&r.dept!=='Front desk');
  const pool=gsvAssignable(s,r.dept);
  return <><div className="modal-heading"><h2>{r.type} · Room {r.room}</h2><p>{r.id} · {r.guest}</p></div>
    <div className="gsv-tags"><Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill><Pill tone={sla.tone}>{sla.label}</Pill>{r.priority==='Urgent'&&<Pill tone="gsv-red">Urgent</Pill>}</div>
    <div className="summary-list gsv-summary">
      <div><span>Detail</span><strong>{r.detail||'—'}</strong></div>
      <div><span>Department · assignee</span><strong>{r.dept} · {gsvUserName(s,r.assignee)}</strong></div>
      <div><span>Logged</span><strong>{gsvWhen(r.createdAt)} by {gsvUserName(s,r.by)}</strong></div>
      {r.scheduledFor&&<div><span>Scheduled for</span><strong>{gsvWhen(r.scheduledFor)}</strong></div>}
      <div><span>Due (SLA)</span><strong>{gsvWhen(r.dueAt)}</strong></div>
      {r.doneAt&&<div><span>Completed</span><strong>{gsvWhen(r.doneAt)}</strong></div>}
      {r.cancelReason&&<div><span>Cancelled</span><strong>{r.cancelReason}</strong></div>}
    </div>
    {r.note&&<div className="info-note"><MessageSquareText size={16}/><span><b>Guest message (Simulated — not sent):</b> “{r.note}”</span></div>}
    {open&&manage&&<form className="form gsv-inline" onSubmit={e=>{e.preventDefault();if(assignee)run({type:'GSV_REQUEST_ASSIGN',id:r.id,assignee,key:modal!.key+':a:'+r.log.length},'Request assigned');}}>
      <label>Assign / reassign<select value={assignee} onChange={e=>setAssignee(e.target.value)}><option value="">Choose a {r.dept.toLowerCase()} team member</option>{pool.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>
      <button className="btn" disabled={busy||!assignee}>Assign</button></form>}
    {open&&work&&<form className="form" onSubmit={e=>{e.preventDefault();run({type:'GSV_REQUEST_COMPLETE',id:r.id,note,key:modal!.key+':c'},'Request completed · guest note recorded',{close:true});}}>
      <label>Note for the guest<textarea rows={2} maxLength={200} value={note} onChange={e=>setNote(e.target.value)} placeholder="e.g. Two bath towels delivered to your room." enterKeyHint="done"/></label>
      <div className="modal-actions">{(r.status==='Open'||r.status==='Assigned')&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'GSV_REQUEST_START',id:r.id,key:modal!.key+':s'},'Request started')}>Start</button>}
        <button className="btn primary" disabled={busy||note.trim().length<3}>Mark done</button></div></form>}
    {open&&permission('reservations')&&<form className="form gsv-inline" onSubmit={e=>{e.preventDefault();run({type:'GSV_REQUEST_CANCEL',id:r.id,reason,key:modal!.key+':x'},'Request cancelled',{close:true});}}>
      <label>Cancel with reason<input value={reason} maxLength={100} onChange={e=>setReason(e.target.value)} placeholder="e.g. Guest no longer needs it"/></label>
      <button className="btn" disabled={busy||reason.trim().length<3}>Cancel request</button></form>}
    <History log={r.log}/></>;
}

// ── Complaints ──
export function GsvComplaintNew({id}:ModalProps){
  const {s,uid,run,modal,busy,closeModal}=useHcc();
  const stays=s.bookings.filter(b=>b.status==='In house'||b.status==='Departed'||b.status==='Confirmed');
  const [bid,setBid]=useState(id||'');
  const owners=s.users.filter(u=>u.enabled&&can(s,u.id,'reservations'));
  const b=s.bookings.find(x=>x.id===bid);
  return <><div className="modal-heading"><h2>Log a complaint</h2><p>Every complaint gets an owner, a root cause and a recovery action.</p></div>
    <form className="form gsv-form" onSubmit={e=>submit(e,f=>run({type:'GSV_COMPLAINT_LOG',booking:bid||undefined,guest:field(f,'guest'),category:field(f,'category'),severity:field(f,'severity'),description:field(f,'description'),owner:field(f,'owner'),key:modal!.key},'Complaint logged',{close:true}))}>
      <label>Stay (optional)<select value={bid} onChange={e=>setBid(e.target.value)}><option value="">Not linked to a stay</option>{stays.map(b=><option key={b.id} value={b.id}>Room {b.room} · {b.guest} · {b.status}</option>)}</select></label>
      <label>Guest name<input name="guest" key={bid} defaultValue={b?.guest||''} required minLength={2} maxLength={60} autoComplete="off" enterKeyHint="next"/></label>
      <div className="form-row"><label>Category<select name="category">{GSV_COMPLAINT_CATEGORIES.map(c=><option key={c}>{c}</option>)}</select></label>
        <label>Severity<select name="severity" defaultValue="Medium">{GSV_SEVERITIES.map(c=><option key={c}>{c}</option>)}</select></label></div>
      <label>What happened<textarea name="description" rows={3} required minLength={5} maxLength={400}/></label>
      <label>Owner<select name="owner" defaultValue={owners.some(u=>u.id===uid)?uid:owners[0]?.id}>{owners.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Log complaint</button></div>
    </form></>;
}
export function GsvComplaintView({id}:ModalProps){
  const {s,run,busy,permission,modal,booking}=useHcc();
  const c=gsvSlice(s).complaints.find(c=>c.id===id);
  const [rc,setRc]=useState(c?.rootCause||'');const [ra,setRa]=useState(c?.recoveryAction||'');const [amt,setAmt]=useState('');const [outcome,setOutcome]=useState('');
  if(!c)return <Missing/>;
  const rec=gsvRecoveryStatus(s,c),openC=c.status==='Open'||c.status==='Investigating';
  const b=c.booking?s.bookings.find(x=>x.id===c.booking):undefined;
  const owners=s.users.filter(u=>u.enabled&&can(s,u.id,'reservations'));
  const paise=Math.round(Number(amt)*100);
  const high=c.severity==='High'||c.severity==='Critical';
  const k=(x:string)=>modal!.key+':'+x+':'+c.log.length;
  return <><div className="modal-heading"><h2>{c.category} · {c.guest}</h2><p>{c.id}{c.room?' · Room '+c.room:''} · logged {gsvWhen(c.createdAt)}</p></div>
    <div className="gsv-tags"><Pill tone={gsvStatusTone(c.status)}>{c.status}</Pill><Pill tone={gsvSevTone(c.severity)}>{c.severity}</Pill>{gsvFollowUpDue(s,c)&&<Pill tone="orange">Follow-up due</Pill>}{rec!=='None'&&<Pill tone={rec==='Posted'||rec==='Approved'?'green':rec==='Rejected'?'neutral':'orange'}>Recovery · {rec}</Pill>}</div>
    <p className="gsv-desc">{c.description}</p>
    <div className="summary-list gsv-summary">
      {!(openC&&permission('reservations'))&&<div><span>Owner</span><strong>{gsvUserName(s,c.owner)}</strong></div>}
      {c.booking&&<div><span>Stay</span><strong>{permission('billing')?<button className="text-btn" onClick={()=>booking(c.booking!)}>{c.booking}</button>:c.booking} · {b?.status}</strong></div>}
      {c.rootCause&&<div><span>Root cause</span><strong>{c.rootCause}</strong></div>}
      {c.recoveryAction&&<div><span>Recovery action</span><strong>{c.recoveryAction}</strong></div>}
      {c.recovery&&permission('billing')&&<div><span>Recovery discount</span><strong>{money(c.recovery.amount)} · {c.recovery.approvalId||'posted'}</strong></div>}
      {c.followUpOn&&<div><span>Follow-up on</span><strong>{dateLabel(c.followUpOn)}</strong></div>}
      {c.followUp&&<div><span>Follow-up</span><strong>{c.followUp.satisfied?'Satisfied':'Not satisfied'} · “{c.followUp.outcome}”</strong></div>}
    </div>
    {openC&&permission('reservations')&&<form className="form" onSubmit={e=>{e.preventDefault();}}>
      <label>Owner<select value={c.owner} onChange={e=>run({type:'GSV_COMPLAINT_UPDATE',id:c.id,owner:e.target.value,key:k('o')},'Owner changed')}>{owners.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>
      <label>Root cause<input value={rc} maxLength={160} onChange={e=>setRc(e.target.value)} placeholder="Why did it happen?"/></label>
      <label>Recovery action<input value={ra} maxLength={160} onChange={e=>setRa(e.target.value)} placeholder="What did we do for the guest?"/></label>
      <div className="gsv-actions"><button type="button" className="btn" disabled={busy||(rc===(c.rootCause||'')&&ra===(c.recoveryAction||'')&&c.status!=='Open')} onClick={()=>run({type:'GSV_COMPLAINT_UPDATE',id:c.id,rootCause:rc,recoveryAction:ra,key:k('u')},'Complaint updated')}>{c.status==='Open'?'Start investigating':'Save notes'}</button>
        <button type="button" className="btn primary" disabled={busy||rc.trim().length<3||ra.trim().length<3} onClick={()=>run({type:'GSV_COMPLAINT_RESOLVE',id:c.id,rootCause:rc,recoveryAction:ra,key:k('r')},'Complaint resolved · follow-up scheduled')}>Resolve</button></div>
    </form>}
    {c.status!=='Closed'&&c.booking&&permission('billing')&&(rec==='None'||rec==='Rejected')&&<form className="form gsv-recovery" onSubmit={e=>{e.preventDefault();run({type:'GSV_COMPLAINT_RECOVERY',id:c.id,amount:paise,key:k('d')},'Recovery discount recorded or sent for approval',{after:()=>setAmt('')});}}>
      <h3>Service-recovery discount</h3>
      <div className="info-note"><ShieldCheck size={16}/>Uses the normal folio discount on {c.booking}. Above {money(s.settings.discountLimit)} it becomes a discount approval request that another authorised user must approve — you cannot approve your own.</div>
      <label>Amount before synthetic tax (₹)<input type="number" inputMode="decimal" min="0.01" step="0.01" value={amt} onChange={e=>setAmt(e.target.value)} enterKeyHint="done"/></label>
      <div className="gsv-actions"><button className="btn" disabled={busy||!(paise>0)}>Apply through discount approval</button></div></form>}
    {c.status==='Resolved'&&permission('reservations')&&<form className="form" onSubmit={e=>e.preventDefault()}>
      <h3>Guest follow-up</h3>
      <label>What the guest said<input value={outcome} maxLength={160} onChange={e=>setOutcome(e.target.value)} placeholder="e.g. Slept well last night, thanked the team"/></label>
      {high&&!permission('approve')&&<p className="f-why">{c.severity} complaints are closed by a manager or owner.</p>}
      <div className="gsv-actions"><button type="button" className="btn" disabled={busy||outcome.trim().length<3} onClick={()=>run({type:'GSV_COMPLAINT_FOLLOWUP',id:c.id,outcome,satisfied:false,key:k('n')},'Not satisfied · complaint re-opened')}>Not satisfied · re-open</button>
        <button type="button" className="btn primary" disabled={busy||outcome.trim().length<3||(high&&!permission('approve'))} onClick={()=>run({type:'GSV_COMPLAINT_FOLLOWUP',id:c.id,outcome,satisfied:true,key:k('y')},'Guest satisfied · complaint closed',{close:true})}>Satisfied · close</button></div></form>}
    <History log={c.log}/></>;
}

// ── Lost & found ──
export function GsvItemNew(){
  const {s,uid,run,modal,busy,closeModal,permission}=useHcc();
  const [cat,setCat]=useState<typeof GSV_ITEM_CATEGORIES[number]>('Clothing');
  const valuable=gsvIsValuable(cat);
  const [storage,setStorage]=useState<string>('Housekeeping store');
  const front=permission('reservations');
  return <><div className="modal-heading"><h2>Log a found item</h2><p>Found {gsvWhen(s.clock)} by {gsvUserName(s,uid)}</p></div>
    <form className="form gsv-form" onSubmit={e=>submit(e,f=>run({type:'GSV_LF_LOG',description:field(f,'description'),category:cat,foundWhere:field(f,'foundWhere'),room:field(f,'room'),storage:valuable?GSV_SAFE:storage,foundBy:front?field(f,'foundBy')||uid:uid,key:modal!.key},'Found item logged',{close:true}))}>
      <label>Item<input name="description" required minLength={3} maxLength={160} placeholder="e.g. Black umbrella with wooden handle" enterKeyHint="next"/></label>
      <div className="form-row"><label>Category<select value={cat} onChange={e=>setCat(e.target.value as typeof cat)}>{GSV_ITEM_CATEGORIES.map(c=><option key={c}>{c}</option>)}</select></label>
        <label>Room (if found in a room)<select name="room" defaultValue=""><option value="">Not in a room</option>{s.rooms.map(r=><option key={r.id} value={r.id}>{r.id}</option>)}</select></label></div>
      <label>Found where<input name="foundWhere" required minLength={2} maxLength={80} placeholder="e.g. Under the bed, lobby sofa" enterKeyHint="next"/></label>
      <label>Storage location<select value={valuable?GSV_SAFE:storage} disabled={valuable} onChange={e=>setStorage(e.target.value)}>{GSV_STORAGE.map(x=><option key={x}>{x}</option>)}</select>
        {valuable&&<small>Valuables always go to the {GSV_SAFE}.</small>}</label>
      {front&&<label>Found by<select name="foundBy" defaultValue={uid}>{s.users.filter(u=>u.enabled).map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Log item</button></div>
    </form></>;
}
export function GsvItemView({id}:ModalProps){
  const {s,run,busy,permission,modal}=useHcc();
  const i=gsvSlice(s).items.find(i=>i.id===id);
  const [method,setMethod]=useState<string>('Donated');
  if(!i)return <Missing/>;
  const k=(x:string)=>modal!.key+':'+x+':'+i.log.length;
  const disposable=gsvDisposable(s,i);
  return <><div className="modal-heading"><h2>{i.description}</h2><p>{i.id} · {i.category}{i.valuable?' · valuable':''}</p></div>
    <div className="gsv-tags"><Pill tone={gsvStatusTone(i.status)}>{i.status}</Pill>{disposable&&<Pill tone="orange">Retention over</Pill>}</div>
    <div className="summary-list gsv-summary">
      <div><span>Found</span><strong>{gsvWhen(i.foundAt)} · {i.foundWhere}{i.room?' (room '+i.room+')':''}</strong></div>
      <div><span>Found by</span><strong>{gsvUserName(s,i.foundBy)}</strong></div>
      <div><span>Stored in</span><strong>{i.storage}</strong></div>
      {i.booking&&<div><span>Last stay in room</span><strong>{i.booking} · {s.bookings.find(b=>b.id===i.booking)?.guest}</strong></div>}
      <div><span>Age · retention</span><strong>{gsvItemAgeDays(s,i)} days · {gsvRetentionDays(s,i)} days (from {dateLabel(gsvDisposeOn(s,i))})</strong></div>
      {i.claim&&<div><span>Claim</span><strong>{i.claim.name} · {i.claim.phone} · {i.claim.idType} · {i.claim.idNote}</strong></div>}
      {i.claim&&<div><span>Description match</span><strong>{i.claim.matchNote}</strong></div>}
      {i.handover&&<div><span>Handed over</span><strong>{gsvWhen(i.handover.at)} to {i.handover.to}{i.handover.note?' · '+i.handover.note:''}</strong></div>}
      {i.disposal&&<div><span>Disposed</span><strong>{gsvWhen(i.disposal.at)} · {i.disposal.method}{i.disposal.note?' · '+i.disposal.note:''}</strong></div>}
    </div>
    {i.status==='Stored'&&permission('reservations')&&<form className="form gsv-form" onSubmit={e=>submit(e,f=>run({type:'GSV_LF_CLAIM',id:i.id,name:field(f,'name'),phone:field(f,'phone'),idType:field(f,'idType'),idNote:field(f,'idNote'),matchNote:field(f,'matchNote'),key:k('c')},'Claim recorded · ID checked'))}>
      <h3>Record a claim</h3>
      <div className="form-row"><label>Claimant name<input name="name" required minLength={2} maxLength={60} autoComplete="off" enterKeyHint="next"/></label>
        <label>Phone<input name="phone" type="tel" inputMode="tel" required maxLength={14} placeholder="98xxxxxxxx" enterKeyHint="next"/></label></div>
      <div className="form-row"><label>ID checked<select name="idType">{GSV_ID_TYPES.map(x=><option key={x}>{x}</option>)}</select></label>
        <label>ID check note<input name="idNote" required maxLength={60} placeholder="e.g. Photo matched · ends 4417" enterKeyHint="next"/></label></div>
      <div className="info-note"><Info size={16}/>Record only the last 4 characters of the ID. Never store a full ID number.</div>
      <label>How the claimant described the item<input name="matchNote" required minLength={3} maxLength={160} enterKeyHint="done"/></label>
      <div className="gsv-actions"><button className="btn primary" disabled={busy}>Record claim</button></div></form>}
    {i.status==='Claimed'&&permission('reservations')&&<form className="form" onSubmit={e=>submit(e,f=>run({type:'GSV_LF_HANDOVER',id:i.id,to:field(f,'to'),note:field(f,'note'),key:k('h')},'Item handed over',{close:true}))}>
      <h3>Handover</h3>
      <label>Received by<input name="to" defaultValue={i.claim?.name} required maxLength={60}/></label>
      <label>Note (optional)<input name="note" maxLength={120} placeholder="e.g. Collected in person, signed register"/></label>
      <div className="gsv-actions"><button className="btn primary" disabled={busy}>Hand over</button></div></form>}
    {i.status==='Stored'&&permission('reservations')&&<form className="form" onSubmit={e=>submit(e,f=>run({type:'GSV_LF_DISPOSE',id:i.id,method,note:field(f,'note'),key:k('d')},'Item disposed',{close:true}))}>
      <h3>Disposal</h3>
      {!disposable?<p className="f-why">Unclaimed items can be disposed from {dateLabel(gsvDisposeOn(s,i))} ({gsvRetentionDays(s,i)} days after finding).</p>
      :i.valuable&&!permission('approve')?<p className="f-why">Valuables are disposed by a manager or owner.</p>:<>
        <div className="form-row"><label>Method<select value={method} onChange={e=>setMethod(e.target.value)}>{GSV_DISPOSAL.filter(m=>!i.valuable||m!=='Destroyed').map(m=><option key={m}>{m}</option>)}</select></label>
          <label>Note<input name="note" maxLength={120} placeholder="e.g. Given to the city shelter"/></label></div>
        <div className="gsv-actions"><button className="btn" disabled={busy}>Dispose item</button></div></>}
    </form>}
    <History log={i.log}/></>;
}

// ── Reports (printable) ──
export function GsvReportBody(){
  const {s}=useHcc();
  const t=gsvResponseTotals(s),types=gsvResponseReport(s,'type'),depts=gsvResponseReport(s,'dept'),cats=gsvComplaintReport(s),lf=gsvLostFoundSummary(s);
  const row=(r:ReturnType<typeof gsvResponseReport>[number])=><tr key={r.key}><td>{r.key}</td><td className="number">{r.count}</td><td className="number">{r.done}</td><td className="number">{r.done?r.avg+' min':'—'}</td><td className="number">{r.done?r.pct+'%':'—'}</td><td className="number">{r.open}</td><td className="number">{r.overdue}</td></tr>;
  const head=<thead><tr><th>Group</th><th className="number">Requests</th><th className="number">Done</th><th className="number">Avg response</th><th className="number">Within SLA</th><th className="number">Open</th><th className="number">Overdue</th></tr></thead>;
  return <div className="gsv-report">
    <div className="small-stats"><div><span>Completed</span><strong>{t.done}</strong></div><div><span>Avg response</span><strong>{t.avg} min</strong></div><div><span>Within SLA</span><strong>{t.pct}%</strong></div><div><span>Open now</span><strong>{t.open}</strong></div><div><span>Overdue</span><strong>{t.overdue}</strong></div></div>
    <h3>Response times by request type</h3><div className="table-wrap"><table>{head}<tbody>{types.map(row)}</tbody></table></div>
    <h3>Response times by department</h3><div className="table-wrap"><table>{head}<tbody>{depts.map(row)}</tbody></table></div>
    <h3>Complaints by category</h3>
    {!cats.length?<p>No complaints recorded.</p>:<div className="table-wrap"><table><thead><tr><th>Category</th><th className="number">Total</th><th className="number">Open</th>{GSV_SEVERITIES.map(v=><th key={v} className="number">{v}</th>)}<th className="number">With recovery</th></tr></thead>
      <tbody>{cats.map(c=><tr key={c.category}><td>{c.category}</td><td className="number">{c.total}</td><td className="number">{c.open}</td>{GSV_SEVERITIES.map(v=><td key={v} className="number">{c.bySeverity[v]}</td>)}<td className="number">{c.recovery}</td></tr>)}</tbody></table></div>}
    <h3>Lost & found</h3>
    <div className="small-stats"><div><span>Stored</span><strong>{lf.stored}</strong></div><div><span>Claimed</span><strong>{lf.claimed}</strong></div><div><span>Handed over</span><strong>{lf.handed}</strong></div><div><span>Disposed</span><strong>{lf.disposed}</strong></div><div><span>Due for disposal</span><strong>{lf.dueDisposal}</strong></div></div>
  </div>;
}
export function GsvPrint(){
  const {s}=useHcc();
  return <div className="gsv-print"><div className="modal-heading"><h2>Guest services report</h2><p>{s.settings.hotel} · as at {gsvWhen(s.clock)} (simulated clock) · fictional data</p></div>
    <GsvReportBody/>
    <div className="modal-actions no-print"><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print</button></div></div>;
}
