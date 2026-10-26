// Closed-date corrections (DEC-028, REQ-CLOSE-006): request → Owner approves → adjustment line dated today referencing the closed date.
import React,{useState} from 'react';
import {bookingRefOf} from '../../domain/bookingRef';
import {Info} from 'lucide-react';
import {money,rbacCanRequestClosedCorrection,cbIsLocked,cbLockedMessage} from '../../services/mock';
import {useHcc} from '../../app/context';
import {CashLockNote} from '../cashbook/LockNote';
import {Pill,Empty,dateLabel} from '../../components/ui';

export function Corrections(){
  const {s,uid,user,run,busy,permission,booking}=useHcc();
  const stays=s.bookings.filter(b=>['In house','Confirmed','Departed'].includes(b.status));
  const dates=[...s.closedDates].sort().reverse();
  const [bid,setBid]=useState('');const [date,setDate]=useState('');const [dir,setDir]=useState('-');const [amt,setAmt]=useState('');const [reason,setReason]=useState('');
  const list=s.approvals.filter(a=>a.kind==='closed-date-correction').slice().reverse();
  const paise=Math.round(Number(amt)*100);
  const pickedDate=date||dates[0]||'';
  const why=!rbacCanRequestClosedCorrection(s,uid)?'Requesting a correction needs the close permission or the Accountant role (Manager, Accountant or Owner).':!stays.length?'No stays to correct.':!dates.length?'No closed dates yet.':
    !(bid||stays[0])||!amt||!(paise>0)?'Enter the folio, closed date and amount.':!reason.trim()?'A reason is required.':cbIsLocked(s,pickedDate)?cbLockedMessage(pickedDate,'a correction'):'';
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;
    run({type:'CORRECTION_ADJUST',id:bid||stays[0].id,date:date||dates[0],amount:dir==='-'?-paise:paise,reason:reason.trim()},'Correction requested — Owner approval required',{after:()=>{setAmt('');setReason('');}});}
  return <section className="card"><div className="section-title"><h2>Closed-date corrections</h2><span className="muted">Closed records never change</span></div>
    <div className="padded">
      <div className="info-note"><Info size={16}/>A correction for a closed date posts as a dated adjustment in today’s open business day, referencing the closed date. Requested by a manager or the accountant; approved by an Owner (not the requester).</div>
      <CashLockNote/>
      <form className="form" onSubmit={submit} aria-label="Request a closed-date correction">
        <div className="form-row"><label>Folio<select value={bid||stays[0]?.id||''} onChange={e=>setBid(e.target.value)} disabled={!stays.length}>{stays.map(b=><option key={b.id} value={b.id}>{b.id} · {b.guest} · room {b.room} · {b.status}</option>)}</select></label>
          <label>Closed date corrected<select value={date||dates[0]||''} onChange={e=>setDate(e.target.value)} disabled={!dates.length}>{dates.map(d=><option key={d} value={d}>{dateLabel(d)}{cbIsLocked(s,d)?' · cash book locked':''}</option>)}</select></label></div>
        <div className="form-row"><label>Direction<select value={dir} onChange={e=>setDir(e.target.value)}><option value="-">Reduce charges (credit)</option><option value="+">Add a missed charge</option></select></label>
          <label>Amount before GST (₹)<input type="number" min="0.01" step="0.01" value={amt} onChange={e=>setAmt(e.target.value)}/></label></div>
        <label>Reason<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Room rate keyed wrongly on 23 Oct"/></label>
        {why&&<p className="f-why">{why}</p>}
        <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}>Request correction</button></div>
      </form>
      {!list.length?<Empty title="No correction requests" body="Requests and their decisions appear here."/>:<div className="request-list">{list.map(a=>{
        const own=a.actor===uid,owner=user.role==='Owner',amount=Number(a.meta?.amount);
        return <div className="request" key={a.id}><div><strong><button className="text-btn" onClick={()=>booking(a.booking)}>{bookingRefOf(s.bookings,a.booking)}</button> · {money(amount)} for {String(a.meta?.date)}</strong><p>{a.id} · {a.reason} · requested by {s.users.find(u=>u.id===a.actor)?.name}</p></div>
          <Pill tone={a.status==='Pending'?'orange':a.status==='Approved'?'green':'neutral'}>{a.status}</Pill>
          {a.status==='Pending'&&(owner&&!own?<><button className="btn small-btn" disabled={busy||cbIsLocked(s,String(a.meta?.date))} title={cbIsLocked(s,String(a.meta?.date))?cbLockedMessage(String(a.meta?.date),'posting this correction'):undefined} onClick={()=>run({type:'CORRECTION_ADJUST_DECIDE',id:a.id,approve:true,key:'corr:'+a.id},'Correction approved — adjustment posted today')}>Approve</button>
            <button className="text-btn" disabled={busy} onClick={()=>run({type:'CORRECTION_ADJUST_DECIDE',id:a.id,approve:false,key:'corr:'+a.id},'Correction rejected')}>Reject</button></>
            :<span className="f-why">{own?'Your request — another Owner decides':'Owner approval required'}</span>)}
          {a.status==='Pending'&&cbIsLocked(s,String(a.meta?.date))&&<span className="f-why">{cbLockedMessage(String(a.meta?.date),'posting this correction')}</span>}</div>;})}</div>}
    </div></section>;
}
