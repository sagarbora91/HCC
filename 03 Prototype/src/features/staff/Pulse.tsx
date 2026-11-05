// F12-CAP-PEOPLE · anonymous staff pulse (MEAS-CONTRACT §2). Five questions, department only. The form never sends or shows a user or staff id.
// Registered by the integrator under the modal id 'meas-pulse' (phone and desktop). Any signed-in user may answer; hidden when Payroll or meas.people.pulse is off.
import React,{useState} from 'react';
import {Info,ShieldCheck} from 'lucide-react';
import {useHcc} from '../../app/context';
import {hrSlice,hrMeasPeopleOn,hrMeasQuarter,HR_MEAS_KEYS,HR_MEAS_PULSE_QUESTIONS} from '../../services/mock';

export function MeasPulseModal(){
  const {s,run,closeModal,busy}=useHcc();
  const on=hrMeasPeopleOn(s,HR_MEAS_KEYS.pulse),depts=hrSlice(s).depts;
  const [dept,setDept]=useState(depts[0]?.id??'');
  const [a,setA]=useState<Record<string,number>>({});
  const [rec,setRec]=useState<number|undefined>();
  const [formKey]=useState(()=>'meas-pulse-'+(globalThis.crypto?.randomUUID?.()??String(Math.random()))); // idempotency only, not an identity
  const done=HR_MEAS_PULSE_QUESTIONS.every(q=>a[q.key])&&rec!==undefined&&!!dept;
  return <><div className="modal-heading"><h2>Staff pulse · {hrMeasQuarter(s.date)}</h2>
    <p><ShieldCheck size={16}/> Anonymous. Only your department is kept: your name, login and staff number are never saved with your answers.</p></div>
    {!on&&<div className="info-note" role="status"><Info size={16}/><span>The staff pulse is switched off.</span></div>}
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MEAS_PULSE_ADD',dept,workload:a.workload,respect:a.respect,tools:a.tools,payFairness:a.payFairness,recommend:rec,key:formKey},
      'Thank you. Your answer was saved without your name',{close:true});}}>
      <label>Your department<select value={dept} onChange={e=>setDept(e.target.value)}>{depts.map(d=><option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
      {HR_MEAS_PULSE_QUESTIONS.map(q=><fieldset key={q.key} className="scale"><legend>{q.label}</legend>
        <div className="seg" role="radiogroup" aria-label={q.label}>{[1,2,3,4,5].map(n=><button type="button" key={n} role="radio" aria-checked={a[q.key]===n} className={'btn'+(a[q.key]===n?' primary':'')} onClick={()=>setA({...a,[q.key]:n})}>{n}</button>)}</div>
        <small>1 = disagree · 5 = agree</small></fieldset>)}
      <fieldset className="scale"><legend>How likely are you to recommend working here? (0-10)</legend>
        <div className="seg" role="radiogroup" aria-label="Recommend 0 to 10">{Array.from({length:11},(_,n)=><button type="button" key={n} role="radio" aria-checked={rec===n} className={'btn'+(rec===n?' primary':'')} onClick={()=>setRec(n)}>{n}</button>)}</div></fieldset>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!on||!done}>Submit anonymously</button></div>
    </form></>;
}
