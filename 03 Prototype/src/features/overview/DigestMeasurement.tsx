// F12 MEAS · the "Measurement" block of the Owner digest (lane meas-digest): red rows as short lines that drill into their report, and the
// subscription rows. Mount inside OwnerDigest (one line, listed in MEAS-DIGEST-DONE). Owner only: the caller has checked dgsCanView.
import React from 'react';
import {useHcc} from '../../app/context';
import {measDigestBlock,measDigestSubscriptions,measDigestLineKey} from '../../domain/streams/measure-digest';
import {Pill} from '../../components/ui';

export function DigestMeasurement({date,onOpenReport}:{date:string;onOpenReport?:(reportId:string)=>void}){
  const {s,run,busy}=useHcc();
  const b=measDigestBlock(s,date);if(!b)return null;
  const subs=measDigestSubscriptions(s);
  return <section aria-label="Measurement"><h3>Measurement</h3>
    {b.rows.length===0?<p>No measurement exceptions on {date}.</p>:<ul>{b.rows.map((r,i)=><li key={i}>
      <Pill tone={r.severity==='red'?'red':'orange'}>{r.severity}</Pill>{' '}{onOpenReport?<button className="text-btn" onClick={()=>onOpenReport(r.reportId)}>{r.text}</button>:r.text}</li>)}</ul>}
    <fieldset><legend>Lines in this digest</legend>{subs.map(x=><label key={x.key} style={{display:'flex',minHeight:48,gap:8,alignItems:'center'}}>
      <input type="checkbox" checked={x.subscribed} disabled={busy} onChange={e=>run({type:'SETTING_SET',setting:measDigestLineKey(x.key),value:e.target.checked})}/>{x.label}</label>)}</fieldset>
  </section>;
}
