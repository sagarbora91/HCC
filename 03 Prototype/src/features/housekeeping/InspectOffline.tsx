// F12 MEAS · meas-cap-hk — housekeeping captures: why a room is offline, linen discards, and the task-time line on the inspection card.
// Modal content wired by the integrator ('meas-offline', 'meas-discard' in features/housekeeping/register.tsx; see MEAS-CAP-HK-DONE.md).
// DEC-061: nothing on these forms shows or asks for money. Phone: one column, 48 px targets, 16 px text (shared .form).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {Empty} from '../../components/ui';
import {measOn} from '../../domain/streams/measure';
import {MEAS_OFFLINE_REASONS,MEAS_OFFLINE_LABEL,MEAS_DISCARD_REASONS,measTaskMinutes} from '../../services/mock';

/** Offline reason for one room (Manager / Supervisor / Owner): OOO, OOS, house use or renovation, expected return, work order. */
export function InspectOfflineModal({room:preset}:{room?:string}){
  const {s,run,modal,busy}=useHcc();
  const cur=preset?s.measX?.offline?.[preset]:undefined;
  const [room,setRoom]=useState(preset??''),[reason,setReason]=useState<string>(cur?.reason??'OOO'),[back,setBack]=useState(cur?.expectedReturn??''),[wo,setWo]=useState(cur?.workOrder??''),[note,setNote]=useState(cur?.note??'');
  if(!measOn(s,'meas.hk.ooo'))return <Empty title="Offline reasons are switched off" body="The Owner can switch them on under Administration, Measurement reports."/>;
  const orders=(s.maintenanceX?.orders??[]).filter(w=>!['Verified','Cancelled'].includes(w.status));
  return <><div className="modal-heading"><div className="eyebrow">ROOMS</div><h2>Why is the room offline?</h2><p>Recorded for the occupancy and downtime reports.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(room)run({type:'MEAS_OFFLINE_SET',room,reason,expectedReturn:back||undefined,workOrder:wo||undefined,note:note.trim()||undefined,key:modal!.key},'Offline reason saved',{close:true});}}>
      <div className="form-row"><label>Room<select value={room} onChange={e=>setRoom(e.target.value)}><option value="">Choose room…</option>{s.rooms.map(r=><option key={r.id} value={r.id}>{r.id} · {r.type}</option>)}</select></label>
        <label>Reason<select value={reason} onChange={e=>setReason(e.target.value)}>{MEAS_OFFLINE_REASONS.map(r=><option key={r} value={r}>{MEAS_OFFLINE_LABEL[r]}</option>)}</select></label></div>
      <div className="form-row"><label>Expected back<input type="date" min={s.date} value={back} onChange={e=>setBack(e.target.value)}/></label>
        <label>Work order (optional)<select value={wo} onChange={e=>setWo(e.target.value)}><option value="">—</option>{orders.map(w=><option key={w.id} value={w.id}>{w.id} · {w.title}</option>)}</select></label></div>
      <label>Note (optional)<input value={note} maxLength={200} onChange={e=>setNote(e.target.value)}/></label>
      <div className="modal-actions">{cur&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'MEAS_OFFLINE_SET',room,clear:true},'Offline reason cleared',{close:true})}>Clear</button>}<button className="btn primary" disabled={busy||!room}>Save</button></div>
    </form></>;
}

/** Linen discard: item, quantity and reason (Supervisor / Manager / Owner). */
export function InspectDiscardModal(){
  const {s,run,modal,busy}=useHcc();
  const [item,setItem]=useState(''),[qty,setQty]=useState('1'),[reason,setReason]=useState<string>(MEAS_DISCARD_REASONS[0]);
  if(!measOn(s,'meas.hk.linen'))return <Empty title="Linen discards are switched off" body="The Owner can switch them on under Administration, Measurement reports."/>;
  const n=Number(qty),ok=item.trim().length>=2&&Number.isInteger(n)&&n>=1&&n<=1000;
  return <><div className="modal-heading"><div className="eyebrow">LINEN</div><h2>Discard linen</h2><p>Worn-out or lost items, for the replacement and loss report.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MEAS_DISCARD_ADD',item:item.trim(),qty:n,reason,key:modal!.key},'Discard recorded',{close:true});}}>
      <label>Item<input value={item} maxLength={60} onChange={e=>setItem(e.target.value)} placeholder="e.g. Bath towel" enterKeyHint="next"/></label>
      <div className="form-row"><label>Quantity<input inputMode="numeric" value={qty} onChange={e=>setQty(e.target.value)}/></label>
        <label>Reason<select value={reason} onChange={e=>setReason(e.target.value)}>{MEAS_DISCARD_REASONS.map(r=><option key={r}>{r}</option>)}</select></label></div>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>Record discard</button></div>
    </form></>;
}

/** One line for the inspection card: how long the clean took and the last inspection result. Renders nothing when there is no capture. */
export function InspectTimes({taskId}:{taskId:string}){
  const {s}=useHcc();const t=s.measX?.taskTimes?.[taskId];
  if(!t||!measOn(s,'meas.hk.productivity'))return null;
  const m=measTaskMinutes(t);
  return <p className="muted">{m!==null?`Cleaned in ${m} min`:t.startedAt?'In progress':''}{t.inspectResult?` · inspection ${t.inspectResult==='pass'?'passed':'failed'}${t.rejectReason&&t.inspectResult==='fail'?' ('+t.rejectReason+')':''}`:''}</p>;
}
