// F12 MEAS · meas-forecast · Displacement calculator (plan 2.4): a group / long-stay / hall enquiry vs the transient revenue it pushes out at the
// forecast occupancy and ADR (the night's suggested BAR). Pure maths in domain/forecast.ts fcDisplacement; money columns need PERM-RPT-002.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {FitPage,ScrollPanel,FitNote} from '../../components/fit';
import {money,addDays} from '../../domain/money';
import {fcDisplacement} from '../../domain/forecast';
import {yldOccReal,yldNight,yldRoomTypes} from '../../domain/streams/yield';

export function MeasDisplacementPage(){
  const {s}=useHcc();
  const types=yldRoomTypes(s);
  const [f,setF]=useState({type:types[0]??'',arrival:s.date,nights:'2',rooms:'5',rate:'1800',extra:'0'});
  const n=Math.max(1,Math.min(30,Number(f.nights)|0)),rooms=Math.max(0,Number(f.rooms)|0);
  const nights=Array.from({length:n},(_,i)=>{const d=addDays(f.arrival,i);const o=yldOccReal(s,d);
    return {date:d,sellable:o.sellable,forecastRooms:Math.max(o.onBooks,o.forecast),adrPaise:yldNight(s,f.type,d).suggested};});
  let r:ReturnType<typeof fcDisplacement>|null=null,err='';
  try{r=fcDisplacement(nights,rooms,Math.round(Number(f.rate)*100),Math.round(Number(f.extra)*100));}catch(e){err=(e as Error).message;}
  return <FitPage title="Displacement" sub="Does a group pay more than the transient guests it would push out? Forecast rooms and suggested BAR per night." layout="split">
    <ScrollPanel title="Enquiry" label="Enquiry form">
      <label>Room type (rate basis)<select value={f.type} onChange={e=>setF({...f,type:e.target.value})}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
      <label>Arrival<input type="date" value={f.arrival} onChange={e=>setF({...f,arrival:e.target.value})}/></label>
      <label>Nights<input inputMode="numeric" value={f.nights} onChange={e=>setF({...f,nights:e.target.value})}/></label>
      <label>Rooms a night<input inputMode="numeric" value={f.rooms} onChange={e=>setF({...f,rooms:e.target.value})}/></label>
      <label>Group rate (₹ a room-night)<input inputMode="decimal" value={f.rate} onChange={e=>setF({...f,rate:e.target.value})}/></label>
      <label>Other spend (₹ a room-night)<input inputMode="decimal" value={f.extra} onChange={e=>setF({...f,extra:e.target.value})}/></label>
    </ScrollPanel>
    <ScrollPanel title="Result" label="Displacement result">
      {err?<FitNote text={err}/>:r&&<>
        {r.rows.map(x=><div key={x.date} className="card meas-fc-row">{x.date} · free {x.free} · displaced {x.displaced} · lost {money(x.displacedPaise)} · group {money(x.groupPaise)}</div>)}
        <p><strong>{r.decision==='accept'?'Accept':'Counter-offer'}</strong> · net {r.netPaise>=0?'gain':'loss'} {money(Math.abs(r.netPaise))}{r.decision==='counter'?` · break-even rate ${money(r.breakEvenRatePaise)} a room-night`:''}</p>
      </>}
    </ScrollPanel>
  </FitPage>;
}
