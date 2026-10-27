// Compliance calendar — one SCC card (renderMonth L2452–2469): meta row, name, form, ≈₹ chip, QRMP advisory, filed line, control strip, the
// done-capture row (filed on, notes, evidence, reviewer, files — all optional) and the "Controls" panel (NA toggle, ownership, deadline /
// escalation / evidence, attached files, input checklist, workflow, reference & penalty). Same component on the desktop and the phone.
import React,{useState} from 'react';
import {CheckCircle2,ChevronDown,ChevronUp,Paperclip,X,Download,RotateCcw} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpCatLabel,cmpCanManage,cmpHasAccess,cmpSlice,cmpQrmpAdvisory,cmpPayableAmount,cmpShortDate,CMP_FILE_MAX_BYTES,type CMPView,type CMPPayableFeed} from '../../services/mock';
import {dateLabel} from '../../components/ui';
import {CmpText,CmpDaysPill,cmpActionKey,cmpName,useCmpOpenFile,cmpReadFile,cmpBytes} from './shared';

export function CmpCard({v,feed,phone}:{v:CMPView;feed?:CMPPayableFeed;phone?:boolean}){
  const {s,uid,run,busy,open,toast}=useHcc();const x=cmpSlice(s);const [cap,setCap]=useState(false);const [more,setMore]=useState(false);
  const c=v.card,custom=!!c._custom,owner=cmpCanManage(s,uid),may=cmpHasAccess(s,uid)&&!!x.profile;
  const amt=cmpPayableAmount(feed,c.name,v.month,v.fy),adv=cmpQrmpAdvisory(c,x.profile),files=x.files.filter(f=>f.fy===v.fy&&f.itemId===c.id);
  const openFile=useCmpOpenFile();
  const [filedOn,setFiledOn]=useState(s.date),[notes,setNotes]=useState(''),[ev,setEv]=useState(''),[rev,setRev]=useState('');const [pick,setPick]=useState<File[]>([]);
  const attach=async(list:File[])=>{for(const f of list){if(f.size>CMP_FILE_MAX_BYTES){toast(`${f.name}: an evidence file can be at most 5 MB.`,true);continue;}
    const dataUrl=await cmpReadFile(f);await run({type:'CMP_EVIDENCE_ADD',fy:v.fy,itemId:c.id,name:f.name,dataUrl,key:cmpActionKey(s,'ev',c.id,f.name,f.size)},'Evidence attached');}};
  const confirm=async()=>{const ok=await run({type:'CMP_DONE',fy:v.fy,itemId:c.id,filedOn,notes,evidenceRef:ev,reviewerNote:rev,key:cmpActionKey(s,'done',v.fy,c.id)},'Marked filed');
    if(ok){setCap(false);if(pick.length)await attach(pick);setPick([]);}};
  const doneBtn=v.isNA?<button type="button" className="cmp-done-btn" disabled title="Not Applicable — read-only" aria-label="Not applicable — read-only"/>
    :v.isDone?<button type="button" className="cmp-done-btn done" disabled={!owner||busy} title={owner?'Undo completion (Owner: reopen with a reason)':'Only the Owner can reopen a filing'}
        aria-label="Filed — reopen" onClick={()=>open({type:'cmp-reopen',id:c.id,room:String(v.fy)})}>✓</button>
    :<button type="button" className="cmp-done-btn" disabled={!may||busy} title={may?'Mark as completed':'Set the firm profile first'} aria-label="Mark as completed" onClick={()=>setCap(o=>!o)}/>;
  return <article id={'cmp-card-'+c.id} className={`cmp-card cmp-cat-${c.cat}${v.isDone?' is-done':''}${v.isOverdue?' is-overdue':''}${v.isNA?' is-na':''}${phone?' cmp-card-m':''}`}>
    <div className="cmp-card-top"><div className="cmp-card-main">
      <div className="cmp-card-meta"><span className="cmp-cat-badge">{cmpCatLabel(c.cat)}</span>{v.isNA&&<span className="cmp-na-chip">NA</span>}
        <span className={'cmp-due-tag'+(c.urgent?' urgent':'')+(v.isOverdue?' overdue':'')}>📅 {c.due}</span><CmpDaysPill tag={v.tag}/><span className="cmp-freq-tag">{c.freq}</span>
        {files.length>0&&<span className="cmp-ev-count" aria-label={`${files.length} evidence file(s)`}>📎 {files.length}</span>}</div>
      <h3 className="cmp-card-name"><CmpText text={v.name} custom={custom}/></h3>
      <div className="cmp-card-form"><span>Form / Portal:</span> <CmpText text={v.form} custom={custom}/></div>
      {amt!==null&&<div className="cmp-payable-chip">≈₹{amt.toLocaleString('en-IN')} payable <span>· estimate — verify with CA</span></div>}
      {adv&&<div className={'cmp-advisory cmp-adv-'+adv.tone}>{adv.text}</div>}
      {v.isDone&&v.rec.filedOn&&<div className="cmp-filed">✓ Filed: {dateLabel(v.rec.filedOn)}{v.rec.evidenceRef?` · Evidence: ${v.rec.evidenceRef}`:''}{v.rec.reviewerNote?` · Reviewed by: ${v.rec.reviewerNote}`:''}
        {v.rec.by&&<small> · recorded by {cmpName(s,v.rec.by)}</small>}</div>}
      <div className="cmp-control-strip"><span className={'cmp-pill cmp-risk-'+v.control.risk.toLowerCase()}>{v.control.risk} Risk</span>
        <span className={'cmp-pill cmp-cash-'+v.control.cashFlow.toLowerCase()}>{v.control.cashFlow} Cash Flow</span>
        <span className={'cmp-pill cmp-'+v.control.escalation.cls}>{v.control.escalation.label}</span><span className="cmp-pill">Internal due {cmpShortDate(v.control.internalDate)}</span></div>
    </div>
    <div className="cmp-card-right"><button type="button" className="btn small-btn cmp-exp-btn" aria-expanded={more} onClick={()=>setMore(o=>!o)}>{more?<ChevronUp size={14}/>:<ChevronDown size={14}/>}Controls</button>{doneBtn}</div></div>
    {cap&&!v.isDone&&!v.isNA&&<div className="cmp-capture" role="group" aria-label="Record the filing">
      <label>📅 Filed on<input type="date" value={filedOn} onChange={e=>setFiledOn(e.target.value)}/></label>
      <label>📝 Notes<input value={notes} maxLength={500} placeholder="Ack no. or filing note" onChange={e=>setNotes(e.target.value)}/></label>
      <label>📁 Evidence<input value={ev} maxLength={200} placeholder="Challan / return / working file ref" onChange={e=>setEv(e.target.value)}/></label>
      <label>👀 Reviewer<input value={rev} maxLength={200} placeholder="Reviewer or sign-off" onChange={e=>setRev(e.target.value)}/></label>
      <label>📎 Files<input type="file" multiple onChange={e=>setPick(Array.from(e.target.files??[]))}/></label>
      <div className="cmp-capture-btns"><button type="button" className="btn primary" disabled={busy} onClick={()=>{void confirm();}}><CheckCircle2 size={16}/>Confirm</button>
        <button type="button" className="btn" onClick={()=>setCap(false)}>Cancel</button></div>
      <small>All fields are optional (SCC). Recorded with your name and the time.</small></div>}
    {more&&<div className="cmp-notes">
      <div className="cmp-na-row"><span>{v.isNA?<>Marked <b>Not Applicable</b> — this item is excluded from the score and is read-only.</>:'Mark this item Not Applicable if it does not apply to this firm.'}</span>
        {v.isNA?<button type="button" className="btn small-btn cmp-na-on" disabled={!owner||busy} title={owner?'Remove NA (Owner: reopen with a reason)':'Only the Owner can remove NA'}
            onClick={()=>open({type:'cmp-reopen',id:c.id,room:String(v.fy)})}><RotateCcw size={14}/>✓ Not Applicable (NA)</button>
          :<button type="button" className="btn small-btn" disabled={!may||busy} onClick={()=>{void run({type:'CMP_NA',fy:v.fy,itemId:c.id,key:cmpActionKey(s,'na',v.fy,c.id)},'Marked not applicable');}}>Mark Not Applicable (NA)</button>}</div>
      <details open><summary>Ownership</summary><div className="cmp-owner-grid"><div><span>Responsible</span><strong>{v.control.responsible}</strong></div><div><span>Reviewer</span><strong>{v.control.reviewer}</strong></div></div></details>
      <details><summary>Deadline · Escalation · Evidence</summary><div className="cmp-cos-grid">
        <div><span>Internal Deadline</span><strong>T-{v.control.buffer} · {cmpShortDate(v.control.internalDate)}</strong><small>Prevents last-day filing and portal dependency.</small></div>
        <div><span>Escalation Rule</span><strong>{v.control.escalation.label}</strong><small>7 days reminder, 3 days escalation, 1 day critical.</small></div>
        <div><span>Evidence Pack</span><strong>{v.control.evidence.length} required artifacts</strong><small>{v.control.evidence.join(' · ')}</small></div></div></details>
      <div className="cmp-ev"><div className="cmp-ev-hd"><Paperclip size={14}/> Attached Evidence {files.length?`(${files.length})`:''}</div>
        {files.length?<ul className="cmp-ev-list">{files.map(f=><li key={f.id}><span title={f.name}>📄 {f.name}</span><small>{cmpBytes(f.size)}</small>
          <button type="button" className="btn small-btn" onClick={()=>{void openFile(f.id);}}><Download size={14}/>View</button>
          <button type="button" className="btn small-btn" disabled={!cmpHasAccess(s,uid)||busy} aria-label={'Remove '+f.name} onClick={()=>{void run({type:'CMP_EVIDENCE_DELETE',id:f.id,key:cmpActionKey(s,'evdel',f.id)},'Evidence removed');}}><X size={14}/></button></li>)}</ul>
          :<p className="cmp-ev-empty">No files attached yet.</p>}
        {may&&<label className="btn small-btn cmp-ev-add">+ Attach file<input type="file" multiple className="cmp-sr" onChange={e=>{const l=Array.from(e.target.files??[]);e.target.value='';void attach(l);}}/></label>}</div>
      <details><summary>Input Checklist</summary><ol className="cmp-steps">{v.control.checklist.map((t,n)=><li key={n}><CmpText text={t} custom={custom}/></li>)}</ol></details>
      <details><summary>Workflow Mapping</summary><ol className="cmp-steps">{v.control.workflow.map((t,n)=><li key={n}><CmpText text={t} custom={custom}/></li>)}</ol><p className="cmp-control-note">{v.control.escalation.note}</p></details>
      <details><summary>Reference &amp; Penalty</summary><dl className="cmp-ref">
        <dt>📌 What This Is</dt><dd><CmpText text={v.whatText} custom={custom}/></dd><dt>💡 Why It Must Be Done</dt><dd><CmpText text={v.whyText} custom={custom}/></dd>
        <dt>👥 Applicable To</dt><dd><CmpText text={v.whoText} custom={custom}/></dd><dt className="cmp-pen">⚠ Penalty for Non-Compliance</dt><dd className="cmp-pen"><CmpText text={v.penaltyText} custom={custom}/></dd></dl></details>
    </div>}
  </article>;
}
