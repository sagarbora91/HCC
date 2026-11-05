// F12 MEAS · meas-forecast · Local events master (plan 2.1, MEAS_EVENT_SAVE / _DELETE) with the real forecast curve (2.2) beside it.
// Desktop FitPage; the same component works on the phone (cards, 48 px buttons, no sideways scroll). Registration: DONE "Integration needed".
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {FitPage,ScrollPanel,FitNote} from '../../components/fit';
import {Empty,shortDate} from '../../components/ui';
import {yldRealCurve,yldRealForecastOn} from '../../domain/streams/yield';
import {MEAS_FC_EVENT_KINDS} from '../../services/mock/handlers/meas-forecast';
import type {MeasEventKind} from '../../domain/streams/measure-types';

export function MeasEventsPage(){
  const {s,run,busy}=useHcc();
  const events=s.measX?.events??[];
  const [f,setF]=useState({id:'',name:'',from:s.date,to:s.date,kind:'fair' as MeasEventKind,impactPct:'10',note:''});
  const curve=yldRealCurve(s,30);
  const save=()=>run({type:'MEAS_EVENT_SAVE',...f,id:f.id||undefined,impactPct:Number(f.impactPct),key:'meas-event:'+(f.id||f.name+f.from)},'Event saved');
  return <FitPage title="Local events" sub="Weddings, exams, fairs and holidays that move demand. The forecast curve adds each event's expected impact on the nights it covers." layout="split">
    <ScrollPanel title="Events" label="Events list">
      {events.length?events.map(e=><div key={e.id} className="card meas-fc-row">
        <strong>{e.name}</strong> <span>{shortDate(e.from)} – {shortDate(e.to)} · {e.kind} · {e.impactPct>0?'+':''}{e.impactPct}%</span>
        <div className="inline"><button className="btn" disabled={busy} onClick={()=>setF({id:e.id,name:e.name,from:e.from,to:e.to,kind:e.kind,impactPct:String(e.impactPct),note:e.note??''})}>Edit</button>
          <button className="btn" disabled={busy} onClick={()=>run({type:'MEAS_EVENT_DELETE',id:e.id,key:'meas-event-del:'+e.id},'Event removed')}>Remove</button></div>
      </div>):<Empty title="No events yet."/>}
    </ScrollPanel>
    <ScrollPanel title={f.id?'Edit event':'Add event'} label="Event form" footer={<button className="btn primary" disabled={busy} onClick={save}>Save event</button>}>
      <label>Name<input value={f.name} maxLength={80} onChange={e=>setF({...f,name:e.target.value})}/></label>
      <label>From<input type="date" value={f.from} onChange={e=>setF({...f,from:e.target.value})}/></label>
      <label>To<input type="date" value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label>
      <label>Kind<select value={f.kind} onChange={e=>setF({...f,kind:e.target.value as MeasEventKind})}>{MEAS_FC_EVENT_KINDS.map(k=><option key={k} value={k}>{k}</option>)}</select></label>
      <label>Expected impact (%)<input inputMode="numeric" value={f.impactPct} onChange={e=>setF({...f,impactPct:e.target.value})}/></label>
      <label>Note<input value={f.note} maxLength={200} onChange={e=>setF({...f,note:e.target.value})}/></label>
      <h3>Forecast, next 30 nights</h3>
      {yldRealForecastOn(s)?curve.map(n=><div key={n.date} className="meas-fc-night" title={n.explain}>{shortDate(n.date)} · {n.pct}% ({n.rooms} of {n.sellable}){n.eventPct?` · event ${n.eventPct>0?'+':''}${n.eventPct}%`:''}</div>)
        :<FitNote text="The real forecast curve is switched off (Measurement reports › Revenue › Forecast)."/>}
    </ScrollPanel>
  </FitPage>;
}
