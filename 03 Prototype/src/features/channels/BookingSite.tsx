// Module 22 · Direct booking website — phone-first guest page preview (not published) + staff panel for web booking requests.
import React,{useState,useRef,useEffect} from 'react';
import {BedDouble,CheckCircle2,ArrowLeft,Check,X,Users,Eye} from 'lucide-react';
import {money,addDays,nights,chnSlice,chnRoomTypes,chnWebQuote,chnWebSearchError,chnWebStatus,resHoldLeftLabel,CHN_TYPE_COPY,type CHNQuote} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,clockTime} from '../../components/ui';
import {NumberStepper,ChipFilter} from '../../app/mobile/components';
import {ChnPill,ChnSimulated,stay,party,firstName} from './shared';

const pct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+'%';

export function GuestPreview(){
  const {s,run,busy}=useHcc();
  const [arrival,setArrival]=useState(addDays(s.date,1));const [departure,setDeparture]=useState(addDays(s.date,2));
  const [adults,setAdults]=useState(2);const [children,setChildren]=useState(0);
  const [pick,setPick]=useState<CHNQuote|null>(null);const [doneAt,setDoneAt]=useState<number|null>(null);
  const [guest,setGuest]=useState('');const [phone,setPhone]=useState('');const [email,setEmail]=useState('');const [notes,setNotes]=useState('');
  const err=chnWebSearchError(s,arrival,departure,adults,children);
  const quotes=err?[]:chnRoomTypes(s).map(t=>chnWebQuote(s,t,arrival,departure,adults,children));
  const reqs=chnSlice(s).webRequests;const done=doneAt!==null?reqs[doneAt]:undefined;
  const reset=()=>{setPick(null);setDoneAt(null);setGuest('');setPhone('');setEmail('');setNotes('');};
  const live=pick?chnWebQuote(s,pick.type,arrival,departure,adults,children):null;
  const top=useRef<HTMLDivElement>(null);
  useEffect(()=>{if(pick||doneAt!==null)top.current?.scrollIntoView?.({block:'start'});},[pick,doneAt]);

  return <div className="chn-site" ref={top} aria-label="Guest booking page preview">
    <div className="chn-site-banner" role="note"><Eye size={15} aria-hidden/> Preview — not published · simulated</div>
    <header className="chn-site-hero"><small>Book direct with</small><h2>{s.settings.hotel}</h2><p>Best available rate · pay at the hotel · instant request</p></header>
    {done?<div className="chn-site-body chn-site-done">
      <CheckCircle2 size={44} aria-hidden/><h3>Request received</h3>
      <p>Reference <strong>{done.id}</strong> · {done.roomType} · {stay(done.arrival,done.departure)}</p>
      <p>Total {money(done.total)} incl. synthetic tax. The hotel will confirm shortly; your room is held for 2 hours (simulated). No payment taken.</p>
      <button className="btn primary chn-big" onClick={reset}>Make another request</button></div>
    :pick&&live?<form className="chn-site-body form" onSubmit={e=>{e.preventDefault();const before=reqs.length;
        run({type:'CHN_WEB_REQUEST',roomType:pick.type,arrival,departure,adults,children,guest:guest.trim(),phone:phone.trim(),email:email.trim(),notes:notes.trim()},'Web booking request received — held for staff confirmation').then(ok=>{if(ok)setDoneAt(before);});}}>
      <button type="button" className="chn-back" onClick={()=>setPick(null)}><ArrowLeft size={18}/> Change room</button>
      <div className="chn-site-sum"><strong>{pick.type} room</strong><span>{stay(arrival,departure)} · {party(adults,children)}</span>
        <div><span>{live.nights} night{live.nights===1?'':'s'} × {money(live.rate)}</span><b>{money(live.net)}</b></div>
        <div><span>Synthetic tax ({pct(live.taxBps)})</span><b>{money(live.tax)}</b></div>
        <div className="chn-site-total"><span>Total</span><b>{money(live.total)}</b></div>
        {!live.ok&&<p className="chn-err-text" role="alert">{live.reason} — choose another room.</p>}</div>
      <h3>Your details</h3>
      <label>Full name<input required minLength={2} autoComplete="name" enterKeyHint="next" value={guest} onChange={e=>setGuest(e.target.value)}/></label>
      <label>Mobile number<input required type="tel" inputMode="tel" autoComplete="tel" enterKeyHint="next" placeholder="+91 98xxx xxxxx" value={phone} onChange={e=>setPhone(e.target.value)}/></label>
      <label>Email (optional)<input type="email" inputMode="email" autoComplete="email" enterKeyHint="next" value={email} onChange={e=>setEmail(e.target.value)}/></label>
      <label>Special requests (optional)<textarea maxLength={200} rows={2} enterKeyHint="done" value={notes} onChange={e=>setNotes(e.target.value)} placeholder="e.g. Late arrival around 10 pm"/></label>
      <p className="chn-site-fine">Fictional preview — please do not enter real personal data. Pay at hotel; no card is collected.</p>
      <button className="btn primary chn-big" disabled={busy||!live.ok}>Request booking · {money(live.total)}</button>
    </form>
    :<div className="chn-site-body">
      <div className="chn-site-search form">
        <div className="chn-site-dates"><label>Check-in<input type="date" min={s.date} value={arrival} onChange={e=>{const a=e.target.value;setArrival(a);if(a&&departure<=a)setDeparture(addDays(a,1));}}/></label>
          <label>Check-out<input type="date" min={addDays(arrival||s.date,1)} value={departure} onChange={e=>setDeparture(e.target.value)}/></label></div>
        <NumberStepper label="Adults" value={adults} min={1} max={4} onChange={setAdults}/>
        <NumberStepper label="Children" value={children} min={0} max={3} onChange={setChildren}/>
      </div>
      {err?<p className="chn-err-text" role="alert">{err}</p>:<p className="chn-site-count" aria-live="polite">{nights(arrival,departure)} night{nights(arrival,departure)===1?'':'s'} · {party(adults,children)} · prices include synthetic {pct(quotes[0]?.taxBps??0)} tax in the total</p>}
      {quotes.map(q=>{const copy=CHN_TYPE_COPY[q.type];return <article key={q.type} className={'chn-room'+(q.ok?'':' off')}>
        <div className={'chn-room-img t-'+q.type.toLowerCase()} aria-hidden><BedDouble size={34}/></div>
        <div className="chn-room-body"><div className="chn-room-head"><h3>{q.type}</h3><span><Users size={14} aria-hidden/> Sleeps {q.capacity}</span></div>
          {copy&&<p>{copy.blurb}</p>}
          {copy&&<ul className="chn-feats">{copy.features.map(f=><li key={f}>{f}</li>)}</ul>}
          <div className="chn-room-price"><div><b>{money(q.rate)}</b><span> / night</span><small>{money(q.total)} total for {q.nights} night{q.nights===1?'':'s'} incl. tax</small></div>
            {q.ok?<button className="btn primary" onClick={()=>setPick(q)} aria-label={`Select ${q.type} room`}>Select</button>:<span className="chn-room-why">{q.reason}</span>}</div>
          {q.ok&&q.left<=2&&<small className="chn-left">Only {q.left} left</small>}
        </div></article>;})}
    </div>}
  </div>;
}

export function WebRequests({compact=false}:{compact?:boolean}){
  const {s,run,busy,open,booking,permission}=useHcc();
  const reqs=[...chnSlice(s).webRequests].sort((a,b)=>(a.status==='Pending'?0:1)-(b.status==='Pending'?0:1)||b.createdAt.localeCompare(a.createdAt));
  if(!reqs.length)return <Empty title="No web requests yet" body="Submit a request from the guest page preview to see it here."/>;
  return <div className={'chn-reqs'+(compact?' compact':'')}>{reqs.map(w=>{const st=chnWebStatus(s,w),b=s.bookings.find(b=>b.id===w.booking);
    return <article key={w.id} className="chn-req">
      <header><div><strong>{w.guest}</strong><small>{w.id} · {permission('reservations')?<button className="text-btn" onClick={()=>booking(w.booking)}>{w.booking}</button>:w.booking}</small></div><ChnPill status={st}/></header>
      <p>{w.roomType}{b?` · room ${b.room}`:''} · {stay(w.arrival,w.departure)} · {party(w.adults,w.children)}</p>
      <p>{w.phone}{w.email?' · '+w.email:''}</p>
      {w.notes&&<p className="chn-muted">“{w.notes}”</p>}
      <div className="chn-req-foot"><b>{money(w.total)}</b><small>incl. synthetic tax {money(w.tax)} · received {clockTime(w.createdAt)}</small></div>
      {st==='Pending'&&b&&<small className="chn-warn-text">Hold: {resHoldLeftLabel(s,b)}</small>}
      {w.reason&&<small className="chn-muted">Reason: {w.reason}</small>}
      {w.decidedBy&&<small className="chn-muted">By {firstName(s.users.find(u=>u.id===w.decidedBy)?.name)}</small>}
      {(st==='Pending'||st==='Expired')&&<div className="chn-req-actions">
        <button className="btn" disabled={busy} onClick={()=>open({type:'chn-web-decline',id:w.id})}><X size={15}/> Decline</button>
        {st==='Pending'&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'CHN_WEB_CONFIRM',id:w.id},`${w.id} confirmed — booking ${w.booking}`)}><Check size={15}/> Confirm</button>}</div>}
    </article>;})}</div>;
}

export function BookingSitePage(){
  const {s}=useHcc();const pending=chnSlice(s).webRequests.filter(w=>chnWebStatus(s,w)==='Pending').length;
  return <><PageTitle title="Direct booking site" sub="Module 22 · in-app preview of the guest booking page. Requests become held bookings that the front desk confirms."/>
    <ChnSimulated text="Preview — not published. No website is hosted and no payment is taken; requests are created in this demo only."/>
    <div className="chn-site-layout">
      <div className="chn-device"><GuestPreview/></div>
      <section className="card"><div className="section-title"><div><h2>Web booking requests</h2><p>{pending} pending confirmation · holds last 2 simulated hours</p></div></div>
        <div className="padded"><WebRequests/></div></section>
    </div></>;
}
export function BookingSiteMobile(){
  const {s}=useHcc();const [v,setV]=useState<'preview'|'requests'>('preview');
  const pending=chnSlice(s).webRequests.filter(w=>chnWebStatus(s,w)==='Pending').length;
  return <div className="chn-m">
    <ChipFilter label="Booking site view" value={v} onChange={setV} options={[{id:'preview',label:'Guest preview'},{id:'requests',label:'Requests',count:pending}]}/>
    {v==='preview'?<GuestPreview/>:<><ChnSimulated text="Preview — not published. Requests are held bookings for staff to confirm."/><WebRequests compact/></>}
  </div>;
}
