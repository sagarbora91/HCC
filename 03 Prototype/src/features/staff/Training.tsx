// F12-CAP-PEOPLE · training records, certificate expiry and staff exit reasons (MEAS-CONTRACT §2). Dialogs (sheets on phones); registered by the integrator
// under the modal ids 'meas-training' and 'meas-exit' (see MEAS-CAP-PEOPLE-DONE.md). Hidden when the Payroll module or meas.people.* is off; HR-001 (Owner, Manager) to save.
import React,{useState} from 'react';
import {Info,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {dateLabel,Empty} from '../../components/ui';
import {hrSlice,hrStaffById,hrCanManage,hrMeasSlice,hrMeasPeopleOn,hrMeasTrainingExpiring,HR_MEAS_KEYS,HR_MEAS_EXIT_REASONS,HR_MEAS_EXPIRY_DAYS} from '../../services/mock';

const OffNote=()=><div className="info-note" role="status"><Info size={16}/><span>This measurement is switched off (Administration › Measurement reports, or the Payroll module is off). Saved records are kept.</span></div>;

/** 'meas-training': id = training record (edit) or undefined (new); shows the expiring certificates first. */
export function MeasTrainingModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();
  const on=hrMeasPeopleOn(s,HR_MEAS_KEYS.training),can=hrCanManage(s,uid),staff=hrSlice(s).staff.filter(t=>t.active);
  const cur=id?hrMeasSlice(s).training?.find(t=>t.id===id):undefined;
  const [f,setF]=useState({staff:cur?.staff??staff[0]?.id??'',course:cur?.course??'',date:cur?.date??s.date,hours:String(cur?.hours??''),certificateNo:cur?.certificateNo??'',validTo:cur?.validTo??'',
    evidenceRef:cur?.evidenceRef??'',mandatory:!!cur?.mandatory});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const expiring=hrMeasTrainingExpiring(s);
  const rows=hrMeasSlice(s).training??[];
  return <><div className="modal-heading"><h2>{cur?'Edit training record':'Training record'}</h2>
    <p>Course, hours and certificate. A certificate with a valid-to date raises a desk reminder {HR_MEAS_EXPIRY_DAYS} days before it ends.</p></div>
    {!on&&<OffNote/>}
    {on&&!can&&<div className="info-note" role="status"><Info size={16}/><span>Only the Owner and Managers can save training records (HR-001).</span></div>}
    {on&&expiring.length>0&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>
      <span>{expiring.length} certificate{expiring.length===1?'':'s'} expiring or expired: {expiring.slice(0,3).map(t=>`${t.course} (${hrStaffById(s,t.staff)?.name??t.staff}, ${t.daysLeft<0?'expired':t.daysLeft+' d'})`).join('; ')}{expiring.length>3?'…':''}</span></div>}
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MEAS_TRAINING_SAVE',id:cur?.id,staff:f.staff,course:f.course.trim(),date:f.date,hours:Number(f.hours),certificateNo:f.certificateNo.trim(),
      validTo:f.validTo||undefined,evidenceRef:f.evidenceRef.trim(),mandatory:f.mandatory,key:'meas-trn-'+s.audit.length+'-'+f.staff+f.date+f.course},cur?'Training record updated':'Training record saved',{close:true});}}>
      <label>Staff member<select value={f.staff} onChange={set('staff')} disabled={!!cur}>{staff.map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <label>Course<input required minLength={2} maxLength={80} value={f.course} onChange={set('course')} placeholder="e.g. Fire safety" enterKeyHint="next"/></label>
      <div className="form-row"><label>Date<input type="date" required max={s.date} value={f.date} onChange={set('date')}/></label>
        <label>Hours<input required type="number" inputMode="decimal" min="0.25" max="200" step="0.25" value={f.hours} onChange={set('hours')}/></label></div>
      <div className="form-row"><label>Certificate no. (optional)<input maxLength={40} value={f.certificateNo} onChange={set('certificateNo')}/></label>
        <label>Valid to (optional)<input type="date" min={f.date} value={f.validTo} onChange={set('validTo')}/></label></div>
      <label>Evidence reference (optional, from Documents)<input maxLength={120} value={f.evidenceRef} onChange={set('evidenceRef')} placeholder="Document number or file name"/></label>
      <label className="check"><input type="checkbox" checked={f.mandatory} onChange={e=>setF({...f,mandatory:e.target.checked})}/> Mandatory course</label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
        <button className="btn primary" disabled={busy||!on||!can}>{cur?'Save changes':'Save record'}</button></div>
    </form>
    <h3>Records</h3>
    {rows.length===0?<Empty title="No training recorded yet"/>:
      <ul className="plain-list">{[...rows].sort((a,b)=>b.date.localeCompare(a.date)).slice(0,30).map(t=>
        <li key={t.id}>{hrStaffById(s,t.staff)?.name??t.staff} · {t.course} · {t.hours} h · {dateLabel(t.date)}{t.validTo?` · valid to ${dateLabel(t.validTo)}`:''}{t.mandatory?' · mandatory':''}</li>)}</ul>}
  </>;
}

/** 'meas-exit': id = staff id. Records why the person left; the staff record and payroll are not changed. */
export function MeasExitModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();
  const on=hrMeasPeopleOn(s,HR_MEAS_KEYS.exit),can=hrCanManage(s,uid),staff=hrSlice(s).staff;
  const [who,setWho]=useState(id??staff[0]?.id??'');
  const cur=hrMeasSlice(s).exit?.[who];
  const [f,setF]=useState({reason:cur?.reason??(HR_MEAS_EXIT_REASONS[0] as string),exitDate:cur?.exitDate??s.date});
  return <><div className="modal-heading"><h2>Exit reason</h2><p>Why a staff member left. Used only for the attrition measure; their record and pay are not changed.</p></div>
    {!on&&<OffNote/>}
    {on&&!can&&<div className="info-note" role="status"><Info size={16}/><span>Only the Owner and Managers can record exit reasons (HR-001).</span></div>}
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MEAS_EXIT_SET',staff:who,reason:f.reason,exitDate:f.exitDate,key:'meas-exit-'+s.audit.length+'-'+who},'Exit reason saved',{close:true});}}>
      <label>Staff member<select value={who} onChange={e=>setWho(e.target.value)} disabled={!!id}>{staff.map(t=><option key={t.id} value={t.id}>{t.name}{t.active?'':' (left)'}</option>)}</select></label>
      <label>Reason<select value={f.reason} onChange={e=>setF({...f,reason:e.target.value})}>{HR_MEAS_EXIT_REASONS.map(r=><option key={r} value={r}>{r}</option>)}</select></label>
      <label>Exit date<input type="date" required value={f.exitDate} onChange={e=>setF({...f,exitDate:e.target.value})}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        {cur&&<button type="button" className="btn danger" disabled={busy||!on||!can} onClick={()=>run({type:'MEAS_EXIT_SET',staff:who,reason:'',key:'meas-exit-clear-'+s.audit.length+'-'+who},'Exit reason cleared',{close:true})}>Clear</button>}
        <button className="btn primary" disabled={busy||!on||!can}>Save</button></div>
    </form></>;
}
