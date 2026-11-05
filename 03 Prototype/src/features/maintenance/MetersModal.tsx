// F12 MEAS · meas-cap-hk — utilities meters (PERM-MEAS-002) and asset life. Modal content, wired by the integrator ('meas-reading', 'meas-meters',
// 'meas-assetlife' in features/maintenance/register.tsx; see MEAS-CAP-HK-DONE.md). Phone: one column, 48 px targets, 16 px text (the shared .form).
// DEC-061: no money anywhere in the reading forms; the asset-life cost field only renders for roles that may see hotel money.
import React,{useState} from 'react';
import {Gauge,Plus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,dateLabel} from '../../components/ui';
import {measOn} from '../../domain/streams/measure';
import {measMeters,measReadingsOf,measConsumption,measMetersDue,measCanSeeAssetCost,measAssetLifeView,MEAS_METER_KINDS,MEAS_METER_UNIT,MEAS_CONDITION_GRADES,mntSlice,money} from '../../services/mock';

const Off=({what}:{what:string})=><Empty title={`${what} is switched off`} body="The Owner can switch it on under Administration, Measurement reports."/>;

/** Enter today's reading for one meter; lists the meters still due (the daily 08:00 checklist line). */
export function MeterReadingModal({meter:preset}:{meter?:string}){
  const {s,run,modal,busy}=useHcc();
  const meters=measMeters(s).filter(m=>m.active),due=measMetersDue(s).map(m=>m.id);
  const [meter,setMeter]=useState(preset??meters.find(m=>due.includes(m.id))?.id??meters[0]?.id??''),[value,setValue]=useState(''),[date,setDate]=useState(s.date),[photoRef,setPhotoRef]=useState('');
  if(!measOn(s,'meas.hk.utilities'))return <Off what="Utilities readings"/>;
  const m=meters.find(x=>x.id===meter),last=measReadingsOf(s,meter).at(-1),n=Number(value),ok=!!m&&value.trim()!==''&&Number.isFinite(n)&&n>=0;
  return <><div className="modal-heading"><div className="eyebrow">UTILITIES</div><h2>Meter reading</h2><p>Readings only go up. {due.length?`${due.length} meter${due.length===1?'':'s'} still to read today.`:'All meters are read for today.'}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MEAS_READING_ADD',meter,value:n,date,photoRef:photoRef.trim()||undefined,key:modal!.key},'Reading saved',{close:true});}}>
      <label>Meter<select value={meter} onChange={e=>setMeter(e.target.value)}>{meters.map(x=><option key={x.id} value={x.id}>{x.name}{due.includes(x.id)?' · due':''}</option>)}</select></label>
      <div className="form-row">
        <label>Reading{m?` (${m.unit})`:''}<input inputMode="decimal" value={value} onChange={e=>setValue(e.target.value)} placeholder={last?`last ${last.value}`:'e.g. 48210'} enterKeyHint="done"/></label>
        <label>Date<input type="date" value={date} max={s.date} onChange={e=>setDate(e.target.value)}/></label>
      </div>
      <label>Photo reference (optional)<input value={photoRef} maxLength={200} onChange={e=>setPhotoRef(e.target.value)} placeholder="Document number of the meter photo"/></label>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}><Gauge size={15}/> Save reading</button></div>
    </form></>;
}

/** The meter master and the last readings with consumption; Owner / Manager add meters. */
export function MetersModal(){
  const {s,run,modal,busy,user}=useHcc();
  const [name,setName]=useState(''),[kind,setKind]=useState<typeof MEAS_METER_KINDS[number]>('electricity');
  if(!measOn(s,'meas.hk.utilities'))return <Off what="Utilities readings"/>;
  const canEdit=user.role==='Owner'||user.role==='Manager';
  return <><div className="modal-heading"><div className="eyebrow">UTILITIES</div><h2>Meters</h2><p>Electricity, water, LPG cylinders and generator hours.</p></div>
    <div className="summary-list">{measMeters(s).map(m=>{const last=measReadingsOf(s,m.id).at(-1),use=measConsumption(s,m.id).at(-1);
      return <div key={m.id}><span>{m.name}{m.active?'':' (off)'}</span><strong>{last?`${last.value} ${m.unit} · ${dateLabel(last.date)}`:'No reading yet'}{use?` · used ${use.used}`:''}</strong></div>;})}</div>
    {canEdit&&<form className="form" onSubmit={e=>{e.preventDefault();if(name.trim().length>=2)run({type:'MEAS_METER_SAVE',name:name.trim(),kind,key:modal!.key},'Meter added');}}>
      <div className="form-row"><label>New meter<input value={name} maxLength={60} onChange={e=>setName(e.target.value)} placeholder="e.g. Floor 2 sub-meter"/></label>
        <label>Type<select value={kind} onChange={e=>setKind(e.target.value as typeof kind)}>{MEAS_METER_KINDS.map(k=><option key={k} value={k}>{k} ({MEAS_METER_UNIT[k]})</option>)}</select></label></div>
      <div className="modal-actions"><button className="btn" disabled={busy||name.trim().length<2}><Plus size={15}/> Add meter</button></div>
    </form>}</>;
}

/** Expected life, condition grade and replacement cost of one asset (Owner / Accountant write; the cost shows only to roles with money access). */
export function MeterAssetLifeModal({asset:preset}:{asset?:string}){
  const {s,uid,run,modal,busy,user}=useHcc();
  const assets=mntSlice(s).assets,[asset,setAsset]=useState(preset??assets[0]?.id??'');
  const cur=measAssetLifeView(s,uid)[asset]??{};
  const [life,setLife]=useState(String(cur.expectedLifeMonths??'')),[grade,setGrade]=useState(cur.conditionGrade??''),[cost,setCost]=useState(cur.replacementCostPaise!==undefined?String(cur.replacementCostPaise/100):'');
  if(!measOn(s,'meas.hk.assets'))return <Off what="Asset life"/>;
  const canWrite=user.role==='Owner'||user.role==='Accountant',showCost=measCanSeeAssetCost(s,uid);
  const paise=cost.trim()===''?undefined:Math.round(Number(cost)*100);
  return <><div className="modal-heading"><div className="eyebrow">ASSETS</div><h2>Asset life</h2><p>Expected life and condition feed the replacement reserve.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MEAS_ASSETLIFE_SET',asset,expectedLifeMonths:life.trim()||undefined,conditionGrade:grade||undefined,replacementCostPaise:showCost?paise:undefined,key:modal!.key},'Asset life saved',{close:true});}}>
      <label>Asset<select value={asset} onChange={e=>setAsset(e.target.value)}>{assets.map(a=><option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
      <div className="form-row"><label>Expected life (months)<input inputMode="numeric" value={life} onChange={e=>setLife(e.target.value)} disabled={!canWrite}/></label>
        <label>Condition<select value={grade} onChange={e=>setGrade(e.target.value as typeof grade)} disabled={!canWrite}><option value="">—</option>{MEAS_CONDITION_GRADES.map(g=><option key={g} value={g}>{g}</option>)}</select></label></div>
      {showCost&&<label>Replacement cost (₹){cur.replacementCostPaise!==undefined?` · now ${money(cur.replacementCostPaise)}`:''}<input inputMode="decimal" value={cost} onChange={e=>setCost(e.target.value)} disabled={!canWrite}/></label>}
      <div className="modal-actions"><button className="btn primary" disabled={busy||!canWrite}>Save</button></div>
    </form></>;
}
