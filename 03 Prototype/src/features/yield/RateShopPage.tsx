// F12 MEAS · meas-forecast · Rate shop entry (plan 2.7, MEAS_COMPETITOR_SAVE / MEAS_RATESHOP_ADD): the Manager's weekly task. Money in paise.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {FitPage,ScrollPanel} from '../../components/fit';
import {Empty,shortDate} from '../../components/ui';
import {money} from '../../domain/money';
import {yldRoomTypes} from '../../domain/streams/yield';

export function MeasRateShopPage(){
  const {s,run,busy}=useHcc();
  const comps=s.measX?.competitors??[],rows=(s.measX?.rateshop??[]).slice().reverse();
  const types=yldRoomTypes(s);
  const [name,setName]=useState('');
  const [f,setF]=useState({competitor:comps.find(c=>c.active)?.id??'',roomType:types[0]??'',stayDate:s.date,rupees:'',withBreakfast:false,source:'phone'});
  const add=()=>{const clientId=`${f.competitor}-${f.roomType}-${f.stayDate}-${s.clock}`;
    run({type:'MEAS_RATESHOP_ADD',clientId,competitor:f.competitor,roomType:f.roomType,stayDate:f.stayDate,ratePaise:Math.round(Number(f.rupees)*100),withBreakfast:f.withBreakfast,source:f.source,key:'meas-rs:'+clientId},'Rate recorded');};
  return <FitPage title="Rate shop" sub="Competitors' rates by stay date and room type, entered by hand each week. Used by the rate parity report." layout="split">
    <ScrollPanel title="Recorded rates" label="Recorded rates">
      {rows.length?rows.map(r=><div key={r.id} className="card meas-fc-row"><strong>{comps.find(c=>c.id===r.competitor)?.name??r.competitor}</strong>
        <span> · {r.roomType} · stay {shortDate(r.stayDate)} · {money(r.ratePaise)}{r.withBreakfast?' incl. breakfast':''} · {r.source} · entered {shortDate(r.date)}</span></div>):<Empty title="No rates recorded yet."/>}
    </ScrollPanel>
    <ScrollPanel title="Add a rate" label="Rate form" footer={<button className="btn primary" disabled={busy||!f.competitor} onClick={add}>Record rate</button>}>
      <label>Competitor<select value={f.competitor} onChange={e=>setF({...f,competitor:e.target.value})}><option value="">Choose…</option>{comps.filter(c=>c.active).map(c=><option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      <label>Room type<select value={f.roomType} onChange={e=>setF({...f,roomType:e.target.value})}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
      <label>Stay date<input type="date" value={f.stayDate} onChange={e=>setF({...f,stayDate:e.target.value})}/></label>
      <label>Rate (₹ a night)<input inputMode="decimal" value={f.rupees} onChange={e=>setF({...f,rupees:e.target.value})}/></label>
      <label className="inline"><input type="checkbox" checked={f.withBreakfast} onChange={e=>setF({...f,withBreakfast:e.target.checked})}/> Includes breakfast</label>
      <label>Source<select value={f.source} onChange={e=>setF({...f,source:e.target.value})}>{['phone','ota-site','website','walk-in'].map(x=><option key={x}>{x}</option>)}</select></label>
      <h3>Competitors</h3>
      {comps.map(c=><div key={c.id} className="inline meas-fc-row">{c.name}{c.active?'':' (inactive)'}<button className="btn" disabled={busy} onClick={()=>run({type:'MEAS_COMPETITOR_SAVE',id:c.id,name:c.name,active:!c.active,key:'meas-cmp:'+c.id+':'+!c.active},c.active?'Competitor set inactive':'Competitor active')}>{c.active?'Set inactive':'Set active'}</button></div>)}
      <label>New competitor<input value={name} maxLength={80} onChange={e=>setName(e.target.value)}/></label>
      <button className="btn" disabled={busy||name.trim().length<3} onClick={()=>{run({type:'MEAS_COMPETITOR_SAVE',name,key:'meas-cmp-new:'+name},'Competitor added');setName('');}}>Add competitor</button>
    </ScrollPanel>
  </FitPage>;
}
