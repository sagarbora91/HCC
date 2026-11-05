// F12 MEAS · upgrade and late check-out capture (MEAS_BOOKING_EXTRA_SET). Items UPG / LCO1 / LCO2 are posted by the existing folio charge only.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {MD_ITEMS,MD_KEY,mdOn,mdExtra,mdLateCheckoutItem,mdBookedType} from '../../services/mock';
import {money} from '../../domain/money';

export function UpsellModal({id}:{id?:string}){
  const {s,run,busy,closeModal,user}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const assigned=s.rooms.find(r=>r.id===b?.room)?.type??'';
  const [kind,setKind]=useState<'LCO'|'UPG'>('LCO');
  const [time,setTime]=useState(s.clock.slice(11,16));
  const [upg,setUpg]=useState<'paid'|'free'>('paid');
  const [rate,setRate]=useState('');const [approver,setApprover]=useState('');const [waive,setWaive]=useState(false);const [reason,setReason]=useState('');
  if(!b)return <p>This booking is unavailable.</p>;
  const boss=user.role==='Manager'||user.role==='Owner';
  const approvers=s.users.filter(u=>u.enabled&&u.id!==user.id&&(u.role==='Manager'||u.role==='Owner'));
  const item=mdLateCheckoutItem(time);
  const extra=mdExtra(s,b.id);
  function submit(e:React.FormEvent){e.preventDefault();
    if(kind==='UPG')run({type:'MEAS_BOOKING_EXTRA_SET',id:b!.id,item:'UPG',upgrade:upg,amount:upg==='paid'?Math.round(Number(rate)*100):undefined,approver:upg==='free'&&!boss?approver:undefined,key:`meas-upg-${b!.id}-${s.clock}`},'Upgrade recorded',{close:true});
    else run({type:'MEAS_BOOKING_EXTRA_SET',id:b!.id,item,time,waive:waive||undefined,reason:waive?reason:undefined,key:`meas-lco-${b!.id}-${s.clock}`},waive?'Late check-out waived and recorded':'Late check-out charged',{close:true});}
  return <form className="form meas-form" onSubmit={submit}>
    <div className="modal-heading"><h2>Upgrade / late check-out</h2><p>{b.id} · {b.guest} · room {b.room} ({assigned}){extra?.bookedType&&extra.bookedType!==assigned?` · booked as ${mdBookedType(s,b)}`:''}</p></div>
    <div className="seg" role="radiogroup" aria-label="What to record">
      <button type="button" role="radio" aria-checked={kind==='LCO'} className={'btn'+(kind==='LCO'?' primary':'')} onClick={()=>setKind('LCO')}>Late check-out</button>
      <button type="button" role="radio" aria-checked={kind==='UPG'} className={'btn'+(kind==='UPG'?' primary':'')} onClick={()=>setKind('UPG')}>Room upgrade</button>
    </div>
    {kind==='LCO'?<>
      <label>Guest leaves at<input type="time" value={time} onChange={e=>setTime(e.target.value)} required/></label>
      <p className="info-note">{item?`${item} · ${MD_ITEMS[item].label} · ${money(MD_ITEMS[item].amount)} plus GST.`:'Before 14:00 there is no late check-out charge.'}</p>
      {item&&boss&&<label className="hov-inline"><input type="checkbox" checked={waive} onChange={e=>setWaive(e.target.checked)}/>Waive the charge (Manager)</label>}
      {waive&&<label>Reason for the waiver<input maxLength={80} value={reason} onChange={e=>setReason(e.target.value)} required/></label>}
    </>:<>
      <label>Upgrade is<select value={upg} onChange={e=>setUpg(e.target.value as 'paid'|'free')}><option value="paid">Paid</option><option value="free">Free (approved)</option></select></label>
      {upg==='paid'?<label>Charge (₹ before GST)<input type="number" inputMode="decimal" min="1" step="0.01" value={rate} onChange={e=>setRate(e.target.value)} required/></label>
        :!boss&&<label>Approved by<select value={approver} onChange={e=>setApprover(e.target.value)} required><option value="">Choose…</option>{approvers.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>}
    </>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||(kind==='LCO'&&!item)}>Record</button></div>
  </form>;
}

/** Booking-dialog action (slot `bookingActions`): shown for open bookings while the capture is on. */
export function UpsellButton({b}:{b:{id:string;status:string}}){
  const {s,open,user}=useHcc();
  if(!mdOn(s,MD_KEY.extra)||(b.status!=='In house'&&b.status!=='Confirmed')||!['Reception','Manager','Owner'].includes(user.role))return null;
  return <button type="button" className="btn" onClick={()=>open({type:'meas-upsell',id:b.id})}>Upgrade / late check-out</button>;
}
