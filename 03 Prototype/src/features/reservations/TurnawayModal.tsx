// F12 MEAS · "Could not book" (turn-away log, MEAS_TURNAWAY_LOG): a 10-second form, desktop dialog and phone sheet (same component, 48 px targets).
// Opened with open({type:'meas-turnaway'}) from the reservation search ("Could not book" button, see TurnawayButton below).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {TURNAWAY_REASONS,TURNAWAY_CHANNELS,mdOn,MD_KEY} from '../../services/mock';
import {addDays} from '../../domain/money';
import type {TurnawayReason} from '../../domain/streams/measure-types';

export function TurnawayModal(){
  const {s,run,busy,closeModal}=useHcc();
  const types=[...new Set(s.rooms.map(r=>r.type))];
  const [f,setF]=useState({reason:'full' as TurnawayReason,arrival:addDays(s.date,1),nights:'1',roomType:types[0]??'',rooms:'1',rate:'',channel:'Walk-in',note:''});
  const set=(k:keyof typeof f,v:string)=>setF({...f,[k]:v});
  const paise=f.rate.trim()===''?undefined:Math.round(Number(f.rate)*100);
  function submit(e:React.FormEvent){e.preventDefault();
    run({type:'MEAS_TURNAWAY_LOG',reason:f.reason,arrival:f.arrival,nights:Number(f.nights),roomType:f.roomType,rooms:Number(f.rooms),quotedRate:paise,channel:f.channel,note:f.note,key:`meas-trn-${s.clock}-${f.arrival}-${f.roomType}`},'Turn-away logged',{close:true});}
  return <form className="form meas-form" onSubmit={submit}>
    <div className="modal-heading"><h2>Could not book</h2><p>Record the guest who could not be accommodated. It takes ten seconds and feeds the regrets report.</p></div>
    <label>Why<select value={f.reason} onChange={e=>set('reason',e.target.value)}>{TURNAWAY_REASONS.map(r=><option key={r.id} value={r.id}>{r.label}</option>)}</select></label>
    <div className="hov-form-row">
      <label>Arrival<input type="date" value={f.arrival} onChange={e=>set('arrival',e.target.value)} required/></label>
      <label>Nights<input type="number" inputMode="numeric" min="1" max="30" value={f.nights} onChange={e=>set('nights',e.target.value)} required/></label>
    </div>
    <div className="hov-form-row">
      <label>Room type<select value={f.roomType} onChange={e=>set('roomType',e.target.value)}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
      <label>Rooms<input type="number" inputMode="numeric" min="1" max="20" value={f.rooms} onChange={e=>set('rooms',e.target.value)}/></label>
    </div>
    <div className="hov-form-row">
      <label>Rate quoted (₹ per night)<input type="number" inputMode="decimal" min="0" step="0.01" value={f.rate} onChange={e=>set('rate',e.target.value)} placeholder="optional"/></label>
      <label>Channel<select value={f.channel} onChange={e=>set('channel',e.target.value)}>{TURNAWAY_CHANNELS.map(c=><option key={c}>{c}</option>)}</select></label>
    </div>
    <label>Note{f.reason==='other'?' (required)':' (optional)'}<input maxLength={120} value={f.note} onChange={e=>set('note',e.target.value)} enterKeyHint="done"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Log turn-away</button></div>
  </form>;
}

/** Button for the reservation search header; renders nothing while the capture is switched off or the role may not log. */
export function TurnawayButton(){
  const {s,open,user}=useHcc();
  if(!mdOn(s,MD_KEY.turnaway)||!['Reception','Manager','Owner'].includes(user.role))return null;
  return <button type="button" className="btn" onClick={()=>open({type:'meas-turnaway'})}>Could not book</button>;
}
