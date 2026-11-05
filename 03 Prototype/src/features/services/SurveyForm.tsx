// F12-CAP-GUEST · Guest survey (MEAS_SURVEY_ADD). One form for the desk (entered on behalf of the guest, PERM-MEAS-002) and for the QR card page
// (via 'qr': no login, rate-limited, follows the Direct booking site module and the meas.guest.survey switch). The form has no field for a name,
// phone, ID number or photo: a survey cannot store ID data. Phone: one column, 48 px targets (the shared .form / .btn classes).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {measOn,gsvSurveyChannelOn,GSV_MEAS_KEYS,GSV_SURVEY_COMMENT_MAX} from '../../services/mock';
import {Empty,submit} from '../../components/ui';

const SCORES=[['overall','Overall stay'],['cleanliness','Cleanliness'],['staff','Staff and service'],['value','Value for money']] as const;

function Scale({name,label,max=5,min=1,value,onChange}:{name:string;label:string;max?:number;min?:number;value?:number;onChange:(n:number)=>void}){
  const n=Array.from({length:max-min+1},(_,i)=>min+i);
  return <fieldset className="meas-scale" aria-label={label}><legend>{label}</legend>
    <div role="radiogroup" style={{display:'flex',flexWrap:'wrap',gap:8}}>{n.map(v=>
      <button type="button" role="radio" aria-checked={value===v} key={v} className={'btn'+(value===v?' primary':'')} style={{minWidth:48,minHeight:48}} onClick={()=>onChange(v)}>{v}</button>)}</div></fieldset>;
}

/** id = booking id (optional, desk only). */
export function MeasSurveyNew({id,via='desk'}:ModalProps&{via?:'desk'|'qr'}){
  const {s,run,busy,closeModal}=useHcc();
  const [v,setV]=useState<Record<string,number|undefined>>({});
  const [consent,setConsent]=useState(false);
  const [bid,setBid]=useState(id||'');
  if(!measOn(s,GSV_MEAS_KEYS.survey)||!gsvSurveyChannelOn(s,via))return <Empty title="The guest survey is switched off" body="The Owner can switch it on in Measurement reports settings."/>;
  const stays=s.bookings.filter(b=>b.status==='In house'||b.status==='Departed');
  const ready=[...SCORES.map(x=>x[0]),'recommend'].every(k=>v[k]!==undefined)&&consent;
  const set=(k:string)=>(n:number)=>setV(p=>({...p,[k]:n}));
  return <><div className="modal-heading"><h2>{via==='qr'?'How was your stay?':'Guest survey'}</h2>
      <p>{via==='qr'?'Five quick questions. We do not ask for your name or any ID.':'Enter the guest’s answers. Do not write a name, phone or ID number.'}</p></div>
    <form className="form gsv-form" onSubmit={e=>submit(e,f=>run({type:'MEAS_SURVEY_ADD',via,booking:via==='desk'&&bid?bid:undefined,...v,
        comment:String(f.get('comment')||'').trim()||undefined,consent}))}>
      {via==='desk'&&<label>Stay (optional)<select value={bid} onChange={e=>setBid(e.target.value)}><option value="">Not linked to a stay</option>
        {stays.map(b=><option key={b.id} value={b.id}>Room {b.room} · {b.id}</option>)}</select></label>}
      {SCORES.map(([k,l])=><Scale key={k} name={k} label={`${l} (1 poor, 5 excellent)`} value={v[k]} onChange={set(k)}/>)}
      <Scale name="breakfast" label="Breakfast (optional)" value={v.breakfast} onChange={set('breakfast')}/>
      <Scale name="recommend" label="How likely are you to recommend us? (0 no, 10 certainly)" min={0} max={10} value={v.recommend} onChange={set('recommend')}/>
      <label>Anything else? (optional)<textarea name="comment" rows={3} maxLength={GSV_SURVEY_COMMENT_MAX}/></label>
      <label className="check"><input type="checkbox" checked={consent} onChange={e=>setConsent(e.target.checked)}/> The guest agrees that these answers are kept (no name or ID is stored).</label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ready}>Save answers</button></div>
    </form></>;
}
export const MeasSurveyQr=(p:ModalProps)=><MeasSurveyNew {...p} via="qr"/>;
