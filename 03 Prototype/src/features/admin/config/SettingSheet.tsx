// W05-A · Master configuration dialogs (desktop modal; full-screen sheet on phones).
// 'cfg-setting' (id = setting key): value editor, effective date, "what it affects", who may change / approval, history with revert,
// scheduled changes with withdraw. 'cfg-import' : configuration import from a JSON export (dry-run preview first).
import React,{useMemo,useState} from 'react';
import {Check,Info,RotateCcw,CalendarClock,Upload,AlertTriangle} from 'lucide-react';
import {cfgDef,cfgGroup,cfgCanChange,cfgValueOn,cfgShow,cfgDisplay,cfgImpact,cfgEffectiveFrom,cfgValidate,cfgClean,cfgHistory,cfgRevertBlock,cfgScheduled,
  cfgPendingRequests,cfgImportPlan,adminImpactPreview,addDays,isGstKey,gstReasonProblem,type CfgSettingDef} from '../../../services/mock';
import {useHcc} from '../../../app/context';
import {Empty,Pill,shortDate,clockTime} from '../../../components/ui';
import {Impact} from '../modals';
import {cfgToInput,cfgFromInput} from './logic';

const first=(n:string)=>n.split(' · ')[0];
function Editor({def,v,setV}:{def:CfgSettingDef;v:string;setV:(t:string)=>void}){
  const label=`New value${def.type==='money'?' (₹)':def.type==='percent'?' (%)':def.suffix?` (${def.suffix})`:''}`;
  if(def.type==='boolean')return <label>{label}<select value={v} onChange={e=>setV(e.target.value)}><option value="on">On</option><option value="off">Off</option></select></label>;
  if(def.type==='choice')return <label>{label}<select value={v} onChange={e=>setV(e.target.value)}>{(def.options||[]).map(o=><option key={String(o)} value={String(o)}>{def.show?def.show(o):String(o)}</option>)}</select></label>;
  const num=def.type==='money'||def.type==='percent'||def.type==='integer';
  return <label>{label}<input value={v} onChange={e=>setV(e.target.value)} type={def.type==='time'?'time':num?'number':def.secret?'password':'text'} autoComplete="off"
    inputMode={def.type==='money'||def.type==='percent'?'decimal':def.type==='integer'?'numeric':undefined} enterKeyHint="done"
    step={def.type==='money'||def.type==='percent'?0.01:def.type==='integer'?1:undefined} min={num&&def.min!==undefined?(def.type==='integer'?def.min:def.min/100):undefined}
    max={num&&def.max!==undefined?(def.type==='integer'?def.max:def.max/100):undefined} maxLength={def.type==='text'?def.maxLength??60:undefined}/></label>;
}
export function CfgSettingSheet({id}:{id?:string}){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const def=id?cfgDef(s,id):undefined;
  const nd=addDays(s.date,1);
  const [v,setV]=useState(()=>def?(def.secret?'':cfgToInput(def,cfgValueOn(s,def.key,def.effectiveMode==='nextDay'?nd:s.date))):'');
  const [from,setFrom]=useState(def?.effectiveMode==='nextDay'?nd:s.date);
  const [reason,setReason]=useState('');
  if(!def)return <Empty title="This setting is not in the registry"/>;
  const g=cfgGroup(def.group)!;
  const dec=cfgCanChange(s,uid,def);
  const raw=cfgClean(def,cfgFromInput(def,v));
  const eff=cfgEffectiveFrom(s,def,def.effectiveMode==='date'?from:undefined);
  const effDate='date' in eff?eff.date:s.date;
  const err=def.editable?cfgValidate(s,def,raw):undefined;
  // GST master (DEC-069): a change needs a reason (server SetGstSetting / WithdrawGstSetting; the mock refuses it the same way).
  const gst=isGstKey(def.key),reasonErr=gst?gstReasonProblem(reason):undefined;
  const legacy=effDate>s.date?undefined:def.key==='checkin'||def.key==='checkout'?adminImpactPreview(s,{kind:'times',checkin:def.key==='checkin'?String(raw):s.settings.checkin,checkout:def.key==='checkout'?String(raw):s.settings.checkout})
    :def.key==='discountLimit'?adminImpactPreview(s,{kind:'discount',limit:Number(raw)||0}):def.key==='inspection'?adminImpactPreview(s,{kind:'inspection',on:raw===true}):undefined;
  const sched=cfgScheduled(s,def.key);const pend=cfgPendingRequests(s,def.key);
  const block=dec.mode==='deny'?dec.reason:pend.length?'A change to this setting is already waiting for approval.':undefined;
  const save=()=>run({type:'SETTING_SET',setting:def.key,value:raw,...(def.effectiveMode==='date'?{effectiveFrom:from}:{}),reason,key:modal!.key},
    dec.mode==='request'?'Change sent for approval':effDate>s.date?'Change scheduled · history recorded':'Setting saved · history recorded',{close:true});
  return <><div className="modal-heading"><div className="eyebrow">{g.label.toUpperCase()} · {def.key}</div><h2>{def.label}</h2><p>{def.help}</p>
      {def.illustrative&&<p className="cfg-illus"><Info size={14}/> Illustrative — CA to confirm for a hotel.</p>}</div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(!err&&!reasonErr&&!block&&'date' in eff)save();}}>
      <div className="summary-list">
        <div><span>Current value</span><strong>{cfgDisplay(s,uid,def)}</strong></div>
        {sched.map(x=><div key={x.id}><span>From {shortDate(x.effectiveFrom)}</span><strong>{cfgShow(def,x.value)}</strong></div>)}
        <div><span>Type · unit</span><strong>{def.type} · {def.unit||'—'}</strong></div>
        <div><span>Effective</span><strong>{def.effective}</strong></div>
        <div><span>Who can change</span><strong>{def.approval}</strong></div>
      </div>
      {def.editable&&dec.mode!=='deny'&&<>
        <Editor def={def} v={v} setV={setV}/>
        {def.effectiveMode==='date'&&<label>Effective from (business date)<input type="date" min={s.date} max={addDays(s.date,366)} required value={from} onChange={e=>setFrom(e.target.value)}/></label>}
        {def.effectiveMode==='nextDay'&&<p className="cfg-eff"><CalendarClock size={15}/> Takes effect from the next business day ({shortDate(nd)}).</p>}
        <label>{gst?'Reason (required, kept in history)':'Reason (optional, kept in history)'}<input value={reason} onChange={e=>setReason(e.target.value)} maxLength={120} enterKeyHint="done" required={gst} minLength={gst?3:undefined}/></label>
        {gst&&reason.trim()!==''&&reasonErr&&<p className="adm-err" role="alert">{reasonErr}</p>}
        {err&&v!==''&&<p className="adm-err" role="alert">{err}</p>}
        {'error' in eff&&<p className="adm-err" role="alert">{eff.error}</p>}
        <div className="adm-impact cfg-affects" aria-live="polite"><h3>What it affects</h3><ul>{cfgImpact(s,def,raw,effDate).map((x,i)=><li key={i}>{x}</li>)}</ul></div>
        {legacy&&<Impact p={legacy}/>}
        {dec.mode==='request'&&<p className="info-note"><Info size={16}/>{dec.reason} You cannot approve your own request.</p>}
      </>}
      {block&&<p className="adm-lock" role="note">{block}</p>}
      <CfgHistory def={def} reason={reason}/>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
        {def.editable&&dec.mode!=='deny'&&<button className="btn primary" disabled={busy||!!err||!!reasonErr||!!block||'error' in eff}>{dec.mode==='request'?'Request approval':effDate>s.date?'Schedule change':'Save setting'} <Check size={16}/></button>}</div>
    </form></>;
}
/** History with "revert to previous value" and scheduled changes with "withdraw". A GST withdrawal takes the sheet's reason (required). */
export function CfgHistory({def,reason=''}:{def:CfgSettingDef;reason?:string}){
  const {s,uid,run,busy}=useHcc();
  const hist=cfgHistory(s,def.key);const sched=cfgScheduled(s,def.key);
  const canAct=cfgCanChange(s,uid,def).mode!=='deny';
  const r=reason.trim(),gstNoReason=isGstKey(def.key)&&!!gstReasonProblem(r);
  return <div className="adm-hist cfg-hist"><h3>History</h3>
    {sched.length>0&&<ul className="cfg-sched">{sched.map(x=><li key={x.id}><Pill tone="blue">Scheduled</Pill> <strong>{cfgShow(def,x.value)}</strong> from {shortDate(x.effectiveFrom)}
      {canAct&&cfgCanChange(s,uid,def).mode==='allow'&&<button type="button" className="text-btn" disabled={busy||gstNoReason} title={gstNoReason?'Enter a reason above to withdraw':undefined}
        onClick={()=>run({type:'CFG_WITHDRAW',id:x.id,...(r?{reason:r}:{}),key:`cfg-wd:${x.id}`},'Scheduled change withdrawn')}>Withdraw</button>}</li>)}</ul>}
    {hist.length?<ul>{hist.map((h,i)=>{const rb=cfgRevertBlock(s,h.id);return <li key={h.id}><strong>{h.before} → {h.after}</strong> · effective {shortDate(h.effectiveFrom)} · {first(h.actor)} at {shortDate(h.at.slice(0,10))} {clockTime(h.at)}{h.reason?' · '+h.reason:''}
      {i===0&&canAct&&!rb&&<button type="button" className="text-btn cfg-revert" disabled={busy} onClick={()=>run({type:'CFG_REVERT',id:h.id,...(r?{reason:r}:{}),key:`cfg-rv:${h.id}`},'Reverted to the previous value')}><RotateCcw size={13}/> Revert to previous value</button>}</li>;})}</ul>
      :<p className="muted">No changes since the scenario was loaded.</p>}
  </div>;
}

export function CfgImportSheet(){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const [text,setText]=useState('');const [name,setName]=useState('');
  const parsed=useMemo(()=>{if(!text.trim())return undefined;try{return {data:JSON.parse(text) as unknown};}catch{return {error:'This file is not valid JSON.'};}},[text]);
  const plan=parsed&&'data' in parsed?cfgImportPlan(s,uid,parsed.data):undefined;
  const onFile=(f:File|undefined)=>{if(!f)return;setName(f.name);f.text().then(setText).catch(()=>setText(''));};
  return <><div className="modal-heading"><div className="eyebrow">DATA AND SYSTEM</div><h2>Import configuration</h2><p>Choose a Master configuration export (JSON). Secrets are never imported; settings you may not change are skipped with the reason. Nothing changes until you press Import.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(plan&&!plan.errors.length&&plan.apply.length)run({type:'CFG_IMPORT',config:(parsed as {data:unknown}).data,key:modal!.key},'Configuration imported · history recorded',{close:true});}}>
      <label>Export file<input type="file" accept="application/json,.json" onChange={e=>onFile(e.target.files?.[0])}/></label>
      <label>…or paste the JSON<textarea rows={4} value={text} onChange={e=>{setText(e.target.value);setName('');}} spellCheck={false}/></label>
      {name&&<p className="muted">{name}</p>}
      {parsed&&'error' in parsed&&<p className="adm-err" role="alert">{parsed.error}</p>}
      {plan&&<div className="adm-impact" aria-live="polite"><h3>Preview</h3>
        {plan.errors.length>0&&<p className="adm-warn" role="alert"><AlertTriangle size={15}/> {plan.errors.length} problem{plan.errors.length>1?'s':''} — fix the file first: {plan.errors.slice(0,3).join(' · ')}</p>}
        <ul><li>{plan.apply.length} setting{plan.apply.length===1?'':'s'} will change</li><li>{plan.unchanged} unchanged</li><li>{plan.skipped.length} skipped</li></ul>
        {plan.apply.length>0&&<details open><summary>Will change</summary><ul>{plan.apply.map(p=><li key={p.def.key}>{p.def.label} → {cfgShow(p.def,p.value)}{p.effectiveFrom>s.date?` (from ${shortDate(p.effectiveFrom)})`:''}</li>)}</ul></details>}
        {plan.skipped.length>0&&<details><summary>Skipped</summary><ul>{plan.skipped.map(k=><li key={k.key}>{k.key} — {k.reason}</li>)}</ul></details>}
      </div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!plan||!!plan.errors.length||!plan.apply.length}><Upload size={16}/> Import</button></div>
    </form></>;
}
