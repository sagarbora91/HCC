// Module 24–25 · shared UI pieces (desktop + phone): status pill, chat bubble, simulated banner, document illustration, document review form.
import React,{useState,useMemo} from 'react';
import {Check,CheckCheck,Clock,AlertCircle,Ban,MessageCircle,Smartphone,Mail,Info,AlertTriangle,IdCard,Receipt,Building2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,clockTime,shortDate} from '../../components/ui';
import {money,MSGMessage,MSGStatus,MSGChannel,MSGDoc,MSGDocKind,MSG_STATUS_TONE,MSG_LOW_CONFIDENCE,MSG_DOC_KIND_LABEL,msgStatus,msgLowFields,msgParseRupees,
  msgLooksLikeFullId,guestIdTypes,guestNormaliseName,EXPENSE_METHODS,EXPENSE_CATEGORIES} from '../../services/mock';
import {msgSampleDocs} from '../../fixtures/ext/messaging';

export const msgChannelIcon=(c:MSGChannel,size=16)=>c==='WhatsApp'?<MessageCircle size={size} aria-hidden/>:c==='SMS'?<Smartphone size={size} aria-hidden/>:<Mail size={size} aria-hidden/>;
const statusIcon:Record<MSGStatus,React.ReactNode>={Queued:<Clock size={14} aria-hidden/>,Sent:<Check size={14} aria-hidden/>,Delivered:<CheckCheck size={14} aria-hidden/>,
  Read:<CheckCheck size={14} aria-hidden className="msg-read"/>,Failed:<AlertCircle size={14} aria-hidden/>,'Not sent':<Ban size={14} aria-hidden/>};
export function MsgStatusPill({st}:{st:MSGStatus}){return <Pill tone={MSG_STATUS_TONE[st]}><span className="msg-status">{statusIcon[st]}{st}</span></Pill>;}
export function MsgSimulated({children}:{children?:React.ReactNode}){
  if(process.env.HCC_BUILD==='production')return null;
  return <div className="info-note msg-sim" role="note"><Info size={16}/><span><b>Simulated.</b> {children||'Nothing is sent — no WhatsApp, SMS or email leaves this prototype, and there is no network access.'}</span></div>;
}
export const msgTemplateName=(s:ReturnType<typeof useHcc>['s'],id:string)=>id==='manual'?'Manual message':(s.messagingX?.templates||[]).find(t=>t.id===id)?.name||id;
/** Chat-style preview bubble with status ticks + text label. */
export function MsgBubble({m,onClick}:{m:MSGMessage;onClick?:()=>void}){
  const {s}=useHcc();const st=msgStatus(m,s.clock);
  const inner=<><div className="msg-bubble-head">{msgChannelIcon(m.channel,14)}<span>{m.channel} · {m.guest}</span><small>{m.to||'no recipient'}</small></div>
    {m.subject&&<strong className="msg-bubble-subject">{m.subject}</strong>}
    <p>{m.body}</p>
    <div className="msg-bubble-foot"><span>{shortDate(m.queuedAt.slice(0,10))} {clockTime(m.queuedAt)}</span><span className={'msg-tick msg-tick-'+st.replace(' ','-').toLowerCase()}>{statusIcon[st]}{st}</span></div>
    {m.blocked&&<small className="msg-bubble-why">{m.blocked}</small>}</>;
  const cls='msg-bubble msg-ch-'+m.channel.toLowerCase();
  return onClick?<button type="button" className={cls} onClick={onClick} aria-label={`${m.channel} message to ${m.guest}, ${st}`}>{inner}</button>:<div className={cls}>{inner}</div>;
}
const kindIcon=(k:MSGDocKind)=>k==='id-card'?<IdCard size={18} aria-hidden/>:k==='vendor-bill'?<Receipt size={18} aria-hidden/>:<Building2 size={18} aria-hidden/>;
/** Drawn illustration of a fictional sample (no image is stored or uploaded). */
export function MsgDocArt({sample}:{sample:string}){
  const d=msgSampleDocs.find(x=>x.id===sample);if(!d)return null;
  return <div className={'msg-art msg-art-'+d.kind} aria-hidden>
    <div className="msg-art-top">{kindIcon(d.kind)}<span>{d.title}</span></div>
    {d.kind==='id-card'&&<div className="msg-art-id"><div className="msg-art-photo">Photo<br/>(none)</div><div>{d.fields.filter(f=>f.key!=='idLast4').map(f=><div key={f.key} className="msg-art-line"><em>{f.label}</em>{f.value}</div>)}</div></div>}
    {d.kind!=='id-card'&&d.fields.map(f=><div key={f.key} className="msg-art-line"><em>{f.label}</em>{f.value}</div>)}
    <div className="msg-art-stamp">FICTIONAL SAMPLE · NOT A REAL DOCUMENT</div>
  </div>;
}
export function MsgKindLabel({kind}:{kind:MSGDocKind}){return <span className="msg-kind">{kindIcon(kind)}{MSG_DOC_KIND_LABEL[kind]}</span>;}
export function MsgConfidence({value,locked}:{value:number;locked?:boolean}){
  const low=!locked&&value<MSG_LOW_CONFIDENCE,pct=Math.round(value*100);
  return <span className={'msg-conf'+(low?' low':'')}><span className="msg-conf-bar" aria-hidden><i style={{width:pct+'%'}}/></span>{pct}%{low?' · check':''}</span>;
}

/** Review + confirm form for one extracted capture. Used inline on desktop and inside the 'msg-doc' sheet on phones. */
export function MsgDocReview({doc,onDone,sheet}:{doc:MSGDoc;onDone?:()=>void;sheet?:boolean}){
  const {s,run,busy,permission,go,closeModal,modal}=useHcc();
  const [vals,setVals]=useState<Record<string,string>>(()=>Object.fromEntries(doc.fields.map(f=>[f.key,f.value])));
  const [reviewed,setReviewed]=useState<string[]>([]);
  const nameField=doc.fields.find(f=>f.key==='name'||f.key==='guest')?.value||'';
  const guess=useMemo(()=>(s.guests||[]).find(g=>guestNormaliseName(g.name)===guestNormaliseName(nameField))?.id||'',[s.guests,nameField]);
  const [guestId,setGuestId]=useState(guess);const [method,setMethod]=useState('Cash');
  const low=msgLowFields(doc.fields);
  const changed=(k:string)=>vals[k].trim()!==doc.fields.find(f=>f.key===k)!.value;
  const pending=low.filter(k=>!reviewed.includes(k)&&!changed(k));
  const g=(s.guests||[]).find(x=>x.id===guestId);
  const mismatch=g&&(vals.name??vals.guest)&&guestNormaliseName(g.name)!==guestNormaliseName(vals.name??vals.guest);
  const errors:string[]=[];
  if(doc.kind==='id-card'){
    if(!/^[A-Za-z0-9]{4}$/.test(vals.idLast4.trim()))errors.push('ID last 4 must be exactly 4 letters or digits.');
    if(doc.fields.some(f=>!f.locked&&msgLooksLikeFullId(vals[f.key])))errors.push('Do not type a full ID number — only the last 4 characters are kept.');
    if(!guestIdTypes.includes(vals.idType))errors.push(`ID type must be one of: ${guestIdTypes.join(', ')}.`);
  }
  if(doc.kind==='vendor-bill'){const a=msgParseRupees(vals.amount);if(!(a>0))errors.push('Enter the amount in rupees, e.g. 1240.00.');
    if(!EXPENSE_CATEGORIES.includes(vals.category))errors.push(`Category must be one of: ${EXPENSE_CATEGORIES.join(', ')}.`);}
  if(doc.kind!=='vendor-bill'&&!g)errors.push('Choose the guest profile to update.');
  if(pending.length)errors.push(`Tick or correct the low-confidence field${pending.length>1?'s':''}: ${pending.map(k=>doc.fields.find(f=>f.key===k)!.label).join(', ')}.`);
  const noExpense=doc.kind==='vendor-bill'&&!permission('expenses');
  const key=(modal?.key||'doc')+':'+doc.id;
  async function confirm(){
    const edits=Object.fromEntries(doc.fields.filter(f=>!f.locked&&changed(f.key)).map(f=>[f.key,vals[f.key]]));
    const ok=await run({type:'MSG_DOC_CONFIRM',id:doc.id,fields:edits,reviewed,guestId:doc.kind==='vendor-bill'?undefined:guestId,method:doc.kind==='vendor-bill'?method:undefined,key:key+':confirm'},
      doc.kind==='vendor-bill'?'Expense draft created — submit it from Expenses':'Guest profile updated',{close:!!sheet});
    if(ok)onDone?.();
  }
  async function discard(){const ok=await run({type:'MSG_DOC_DISCARD',id:doc.id,key:key+':discard'},'Capture discarded',{close:!!sheet});if(ok)onDone?.();}
  return <div className="msg-review">
    <div className="msg-review-grid">
      <MsgDocArt sample={doc.sample}/>
      <div className="form msg-fields">
        {doc.fields.map(f=>{const isLow=low.includes(f.key);
          return <div key={f.key} className={'msg-field'+(isLow?' low':'')}>
            <label>{f.label}{f.locked?' (read-only)':''}
              {f.key==='idType'?<select value={vals[f.key]} onChange={e=>setVals({...vals,[f.key]:e.target.value})}>{!guestIdTypes.includes(vals[f.key])&&<option>{vals[f.key]}</option>}{guestIdTypes.map(t=><option key={t}>{t}</option>)}</select>
              :f.key==='category'?<select value={vals[f.key]} onChange={e=>setVals({...vals,[f.key]:e.target.value})}>{!EXPENSE_CATEGORIES.includes(vals[f.key])&&<option>{vals[f.key]}</option>}{EXPENSE_CATEGORIES.map(t=><option key={t}>{t}</option>)}</select>
              :<input value={vals[f.key]} readOnly={f.locked} maxLength={f.key==='idLast4'?4:120} enterKeyHint="next"
                  inputMode={f.key==='amount'?'decimal':undefined} autoCapitalize={f.key==='idLast4'||f.key==='gstin'?'characters':undefined}
                  onChange={e=>setVals({...vals,[f.key]:e.target.value})}/>}</label>
            <div className="msg-field-meta"><MsgConfidence value={f.confidence} locked={f.locked}/>{changed(f.key)&&<Pill tone="blue">Corrected</Pill>}
              {isLow&&!changed(f.key)&&<label className="msg-check"><input type="checkbox" checked={reviewed.includes(f.key)} onChange={e=>setReviewed(e.target.checked?[...reviewed,f.key]:reviewed.filter(k=>k!==f.key))}/>Checked against the document</label>}</div>
          </div>;})}
        {doc.kind!=='vendor-bill'&&<label>Apply to guest profile<select value={guestId} onChange={e=>setGuestId(e.target.value)}><option value="">Choose a guest…</option>{(s.guests||[]).map(x=><option key={x.id} value={x.id}>{x.name} · {x.id}</option>)}</select></label>}
        {doc.kind==='vendor-bill'&&<label>Funding source<select value={method} onChange={e=>setMethod(e.target.value)}>{EXPENSE_METHODS.map(m=><option key={m}>{m}</option>)}</select></label>}
      </div>
    </div>
    {doc.kind==='id-card'&&<div className="info-note"><Info size={16}/><span>Only the <b>ID type</b> and <b>last 4 characters</b> are saved to the profile. No image and no full ID number are stored.</span></div>}
    {doc.kind==='company-letter'&&<div className="info-note"><Info size={16}/>Saves the company name and GSTIN to the guest profile for company billing.</div>}
    {doc.kind==='vendor-bill'&&<div className="info-note"><Info size={16}/><span>Creates an <b>expense draft</b> ({msgParseRupees(vals.amount)>0?money(msgParseRupees(vals.amount)):'amount?'}) through the normal expense flow. Submit it from Expenses; another person approves before it is posted.</span>
      {permission('expenses')&&<button type="button" className="text-btn" onClick={()=>{closeModal();go('expenses');}}>Open Expenses</button>}</div>}
    {mismatch&&<div className="info-note msg-warn" role="alert"><AlertTriangle size={16}/>The name on the document does not match {g!.name}. Check you chose the right profile (you can still continue).</div>}
    {noExpense&&<p className="f-why">Your role cannot record expenses — ask Reception or a Manager to confirm this bill.</p>}
    {!!errors.length&&<ul className="msg-errors" aria-live="polite">{errors.map(e=><li key={e}>{e}</li>)}</ul>}
    <div className="modal-actions msg-actions"><button type="button" className="btn" disabled={busy} onClick={discard}>Discard</button>
      <button type="button" className="btn primary" disabled={busy||!!errors.length||noExpense} onClick={confirm}>{doc.kind==='vendor-bill'?'Confirm → expense draft':'Confirm → guest profile'}</button></div>
  </div>;
}
