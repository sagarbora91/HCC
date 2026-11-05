// F12-CAP-GUEST · Loyalty tag on a customer (MEAS_LOYALTY_SET, item meas.guest.repeat). id = guest profile id. Tag only: no ID data.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {measOn,GSV_MEAS_KEYS,GSV_LOYALTY_TAG_MAX} from '../../services/mock';
import {Empty,submit} from '../../components/ui';

export const LOYALTY_TAGS=['Regular','Corporate','VIP','Friend of hotel'] as const;

export function MeasLoyaltyTag({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();
  const g=(s.guests??[]).find(x=>x.id===id);
  const cur=s.measX?.preference?.[id||'']?.loyaltyTag??'';
  const [tag,setTag]=useState(cur);
  if(!g||!measOn(s,GSV_MEAS_KEYS.repeat))return <Empty title="Loyalty tags are unavailable"/>;
  return <><div className="modal-heading"><h2>Loyalty tag</h2><p>{g.name}</p></div>
    <form className="form" onSubmit={e=>submit(e,()=>run({type:'MEAS_LOYALTY_SET',customer:g.id,tag}))}>
      <label>Tag<input value={tag} maxLength={GSV_LOYALTY_TAG_MAX} list="meas-loyalty-tags" onChange={e=>setTag(e.target.value)} autoComplete="off"/></label>
      <datalist id="meas-loyalty-tags">{LOYALTY_TAGS.map(t=><option key={t} value={t}/>)}</datalist>
      <small>Leave empty to remove the tag.</small>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save tag</button></div>
    </form></>;
}
