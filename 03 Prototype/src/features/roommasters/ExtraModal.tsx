// MA-1 · "Extra person / bed" on a folio (modal 'rms-extra', id = booking id; desktop dialog and phone sheet). Prefills from the
// occupancy-charges master (per room type, dated amount in force today) and posts the existing CHARGE command with GST class
// "other services" — no new money command (MA-2's extras catalogue will take this over).
import React,{useState} from 'react';
import {money,rmsExtraQuote,rmsNightsLeft,rmsOccFor,RMS_EXTRA_LABEL,type RmsExtraKind} from '../../services/mock';
import {useHcc} from '../../app/context';

export function ExtraModal({id}:{id?:string}){
  const {s,run,busy,modal,booking,permission}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const [kind,setKind]=useState<RmsExtraKind>('adult');
  const [qty,setQty]=useState(1);
  const [n,setN]=useState(()=>b?rmsNightsLeft(s,b):1);
  if(!b)return <p className="muted">This booking is no longer available.</p>;
  const room=s.rooms.find(r=>r.id===b.room);
  const occ=room?rmsOccFor(s,room.type):undefined;
  const q=rmsExtraQuote(s,b.id,kind,qty,n);
  const post=()=>{if('error' in q||!permission('billing'))return;
    void run({type:'CHARGE',id:b.id,label:q.label,amount:q.amount,gst:q.gst,key:modal?.key??`rms-extra:${b.id}:${s.sequence}`},'Extra charge posted',{after:()=>booking(b.id)});};
  return <><div className="modal-heading"><div className="eyebrow">FOLIO · {b.id}</div><h2>Extra person or bed</h2><p>{b.guest} · room {b.room}{occ?` · ${occ.name}`:''}</p></div>
    <form className="form" noValidate onSubmit={e=>{e.preventDefault();post();}}>
      <fieldset className="rms-kinds"><legend>Charge</legend>{(['adult','child','bed'] as RmsExtraKind[]).map(k=><label key={k} className="mst-toggle">
        <input type="radio" name="rms-kind" checked={kind===k} onChange={()=>setKind(k)}/> <span>{RMS_EXTRA_LABEL[k]}</span></label>)}</fieldset>
      {kind==='child'&&occ&&occ.childAgeFrom!==undefined&&<p className="muted">Children aged {occ.childAgeFrom}–{occ.childAgeTo}; older guests are charged as adults.</p>}
      <div className="form-row"><label>Quantity<input type="number" inputMode="numeric" min={1} max={4} value={qty} onChange={e=>setQty(Number(e.target.value)||0)}/></label>
        <label>Nights<input type="number" inputMode="numeric" min={1} max={30} value={n} onChange={e=>setN(Number(e.target.value)||0)}/></label></div>
      {'error' in q?<p className="mst-err" role="alert">{q.error}</p>
        :<p className="info-note">{q.label}: {money(q.perNight)} × {qty} × {n} = <strong>{money(q.amount)}</strong> + GST (other services).</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button>
        <button className="btn primary" disabled={busy||'error' in q||!permission('billing')}>Post charge</button></div>
    </form></>;
}

/** Button placed next to "Add charge" on the folio (billing holders, open stays). */
export function RmsExtraButton({id,className='text-btn'}:{id:string;className?:string}){
  const {open,permission}=useHcc();
  if(!permission('billing'))return null;
  return <button type="button" className={className} onClick={()=>open({type:'rms-extra',id})}>Extra person / bed</button>;
}
