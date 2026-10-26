// DEC-077 (resolves DEC-028): the Owner reopens a closed business day with a reason. While it is reopened, a correction of a line dated
// that day (requested on the folio, approved by the Owner) and a missed charge post ON that day; issued invoices and credit notes never
// change; the cash-book lock (DEC-065) stays as it is. "Close again" re-runs the day's checks and writes a new closing record (the old one
// is kept). One panel for the desktop Day closing page and the phone closing screen (M18). Commands DAY_REOPEN / DAY_RECLOSE / CHARGE {date}.
import React,{useState} from 'react';
import {RotateCcw,Lock,AlertTriangle,Info,ArrowRight} from 'lucide-react';
import {money,closingReopened,closingReopenRefusal,closingPendingCorrections,accDayStatus,cbIsLocked,GST_CHARGE_CATEGORIES,type ClosingReopen} from '../../services/mock';
import {useHcc} from '../../app/context';
import {bookingRefOf} from '../../domain/bookingRef';
import {dateLabel,shortDate} from '../../components/ui';

type Look={phone:boolean;card:string;note:string;warn:string;form:string};
const look=(phone:boolean):Look=>({phone,card:phone?'ma-card ma-reopen':'card ma-reopen-desk',note:phone?'ma-note':'info-note',warn:phone?'ma-warn':'info-note f-warn',form:phone?'ma-form':'form'});

/** Reopen form for the Owner, or the reopened day's panel for everyone who can see Day closing; nothing otherwise. */
export function ReopenDay({phone=false}:{phone?:boolean}){
  const {s,user}=useHcc();
  const open=closingReopened(s);
  if(open)return <ReopenedDay r={open} l={look(phone)}/>;
  return user.role==='Owner'?<ReopenForm l={look(phone)}/>:null;
}

function Head({l,title,sub}:{l:Look;title:string;sub:string}){
  return l.phone?<div className="ma-card-top"><div><h2 className="ma-h2">{title}</h2><span>{sub}</span></div></div>
    :<div className="section-title"><h2>{title}</h2><span className="muted">{sub}</span></div>;
}

function ReopenForm({l}:{l:Look}){
  const {s,run,busy}=useHcc();
  const dates=Object.keys(s.closings??{}).filter(d=>d<s.date).sort().reverse();
  const [date,setDate]=useState('');const [reason,setReason]=useState('');
  const picked=date||dates[0]||'';
  const refusal=picked?closingReopenRefusal(s,picked,accDayStatus(s,picked)==='Locked'):'No closed day with a closing record yet.';
  const why=refusal||(!reason.trim()?'A reason is required to reopen a closed business day.':'');
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;
    run({type:'DAY_REOPEN',date:picked,reason:reason.trim(),key:`reopen:${picked}:${s.sequence}`},`Business day ${shortDate(picked)} reopened — corrections now post on it`,{after:()=>setReason('')});}
  return <section className={l.card} aria-label="Reopen a closed day">
    <Head l={l} title="Reopen a closed day" sub="Owner only · with a reason · audited"/>
    <div className={l.phone?'ma-reopen-body':'padded'}>
      <div className={l.note}><Info size={16}/><span>Reopening lets corrections of that day’s lines and missed charges post on that day; its closing record is written again when it is closed again (the old one is kept). Invoice and credit-note numbers and their GST never change, the cash-book lock is not touched, and today cannot be closed until the reopened day is closed again. A day locked in Accounting must be unlocked first.</span></div>
      <form className={l.form} onSubmit={submit} aria-label="Reopen a closed business day">
        <label>Closed business day<select value={picked} onChange={e=>setDate(e.target.value)} disabled={!dates.length}>
          {dates.slice(0,62).map(d=><option key={d} value={d}>{dateLabel(d)}{cbIsLocked(s,d)?' · cash book locked':''}{accDayStatus(s,d)==='Locked'?' · locked in Accounting':''}</option>)}</select></label>
        <label>Reason<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Room 101 night posted to the wrong folio"/></label>
        {why&&<p className="f-why">{why}</p>}
        <div className={l.phone?undefined:'modal-actions'}><button className={'btn primary'+(l.phone?' ma-wide':'')} disabled={busy||!!why}><RotateCcw size={16}/> Reopen {picked?shortDate(picked):'day'}</button></div>
      </form>
    </div>
  </section>;
}

function ReopenedDay({r,l}:{r:ClosingReopen;l:Look}){
  const {s,run,busy,permission,booking}=useHcc();
  const d=r.date,canClose=permission('close');
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name??id??'—';
  const stays=s.bookings.filter(b=>(b.status==='In house'||b.status==='Confirmed')&&b.arrival<=d&&d<=b.departure);
  const withLines=s.bookings.filter(b=>(b.status==='In house'||b.status==='Confirmed')&&b.lines.some(x=>x.date===d));
  const pending=closingPendingCorrections(s,d),cashLocked=cbIsLocked(s,d);
  const [bid,setBid]=useState('');const [label,setLabel]=useState('');const [amt,setAmt]=useState('');const [gst,setGst]=useState('other');
  const paise=Math.round(Number(amt)*100),pick=bid||stays[0]?.id||'';
  const chargeWhy=!permission('billing')?'Posting a charge needs the billing permission.':!stays.length?`No open stay covers ${shortDate(d)}.`:cashLocked?`The cash book of ${shortDate(d)} is locked: the Owner reopens it first (Cash book).`
    :!label.trim()?'Describe the service.':!(paise>0)?'Enter an amount greater than zero.':'';
  const closeWhy=!canClose?'Closing needs the close permission (Manager or Owner).':pending.length?`Decide the ${pending.length} pending correction${pending.length===1?'':'s'} of lines dated ${shortDate(d)} first.`:'';
  function charge(e:React.FormEvent){e.preventDefault();if(chargeWhy)return;
    run({type:'CHARGE',id:pick,label:label.trim(),amount:paise,gst,date:d},`Charge posted on ${shortDate(d)}`,{after:()=>{setLabel('');setAmt('');}});}
  const reclose=()=>run({type:'DAY_RECLOSE',date:d,key:`reclose:${d}:${r.id}`},`Business day ${shortDate(d)} closed again — closing record replaced, the earlier one kept`);
  return <section className={l.card} aria-label={`Reopened day ${d}`}>
    <Head l={l} title={`${dateLabel(d)} is reopened`} sub={`By ${name(r.by)} at ${r.at.replace('T',' ')} · ${r.reason}`}/>
    <div className={l.phone?'ma-reopen-body':'padded'}>
      <div className={l.warn} role="status"><AlertTriangle size={16}/><span>Corrections of lines dated {shortDate(d)} and missed charges now post on {shortDate(d)}. Close it again before closing today ({shortDate(s.date)}). Issued invoices and credit notes never change: a checked-out stay is corrected with a credit note.</span></div>
      {r.cashBookLocked||cashLocked?<div className={l.note}><Lock size={16}/><span>The cash book of {shortDate(d)} {cashLocked?'stays locked':'was locked when the day was reopened'} (DEC-065): reopening the day does not unlock it; a correction or charge touching {shortDate(d)} is refused while it is locked.</span></div>:null}
      {r.tallyReexport&&<div className={l.note}><Info size={16}/><span>{shortDate(d)} was exported to Tally: its vouchers are marked changed and the next Tally export carries them again.</span></div>}
      <h3 className={l.phone?'ma-h2':undefined}>Correct a line dated {shortDate(d)}</h3>
      <p className={l.phone?'ma-reopen-text':'muted'}>Open the folio and request a correction of the line; once the Owner approves it, the reversal posts on {shortDate(d)}.</p>
      {withLines.length?<ul className="ma-reopen-list">{withLines.slice(0,12).map(b=><li key={b.id}><button type="button" className={l.phone?'btn ma-wide':'text-btn'} onClick={()=>booking(b.id)}>{bookingRefOf(s.bookings,b.id)} · {b.guest} · room {b.room} <ArrowRight size={13}/></button></li>)}</ul>
        :<p className="muted">No open folio has a line dated {shortDate(d)}.</p>}
      {pending.length>0&&<p className="f-why">{pending.length} correction request{pending.length===1?'':'s'} for {shortDate(d)} awaiting a decision: {pending.map(a=>`${a.id} (${money(a.amount)})`).join(', ')}.</p>}
      <h3 className={l.phone?'ma-h2':undefined}>Post a missed charge on {shortDate(d)}</h3>
      <form className={l.form} onSubmit={charge} aria-label={`Post a missed charge on ${d}`}>
        <label>Folio<select value={pick} onChange={e=>setBid(e.target.value)} disabled={!stays.length}>{stays.map(b=><option key={b.id} value={b.id}>{bookingRefOf(s.bookings,b.id)} · {b.guest} · room {b.room}</option>)}</select></label>
        <label>Service<input maxLength={120} value={label} onChange={e=>setLabel(e.target.value)} placeholder="e.g. Laundry missed on the day"/></label>
        <label>GST category<select value={gst} onChange={e=>setGst(e.target.value)}>{GST_CHARGE_CATEGORIES.map(c=><option key={c.id} value={c.id}>{c.label}</option>)}</select></label>
        <label>Amount before GST (₹)<input type="number" inputMode="decimal" min="0.01" step="0.01" value={amt} onChange={e=>setAmt(e.target.value)}/></label>
        {chargeWhy&&<p className="f-why">{chargeWhy}</p>}
        <div className={l.phone?undefined:'modal-actions'}><button className={'btn'+(l.phone?' ma-wide':'')} disabled={busy||!!chargeWhy}>Post on {shortDate(d)}</button></div>
      </form>
      <div className={l.phone?'ma-reopen-close':'card-footer f-actions'}><button type="button" className={'btn primary'+(l.phone?' ma-wide':'')} disabled={busy||!!closeWhy} onClick={reclose}>Close {shortDate(d)} again <ArrowRight size={15}/></button>{closeWhy&&<span className="f-why">{closeWhy}</span>}</div>
    </div>
  </section>;
}
