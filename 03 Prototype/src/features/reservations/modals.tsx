// Stream A · reservation dialogs: new booking (availability search + hold), cancel, move / change dates.
import React,{useState} from 'react';
import {ArrowRight,Info,Hourglass,TriangleAlert} from 'lucide-react';
import {money,addDays,nights,totals,resSearch,resHoldActive,resHoldLeftLabel,resStatus,RES_CAPACITY,RES_SOURCES,RES_OTA_CHANNELS,RES_GSTIN,RES_PHONE} from '../../services/mock';
import {useHcc} from '../../app/context';
import {field,submit,shortDate,Empty} from '../../components/ui';
import {RestrictionNotice} from './RestrictionNotice';
import {rstState} from './restriction';
import {useReservations,useAvailability} from '../../clients/react';
import {quoteAndCreate} from '../../clients/view';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const err=(m?:string)=>m?<small className="res-field-error" role="alert">{m}</small>:null;

/**
 * New booking (REQ-BOOK-001/002/004). Modal props: `room` = preselected room; `id` = prefilled arrival date (YYYY-MM-DD),
 * used by the calendar's empty cells. Saves as Confirmed, or as Held for 2 h of simulated time.
 */
export function NewReservation({room,id}:{room?:string;id?:string}){
  const {s,uid,modal,closeModal,booking}=useHcc();
  const {commit,busy:cbusy,readOnly}=useReservations();const busy=cbusy||readOnly;
  const pre=s.rooms.find(r=>r.id===room);
  const start=id&&DATE.test(id)&&id>=s.date?id:s.date;
  const [arrival,setArrival]=useState(start),[departure,setDeparture]=useState(addDays(start,id?1:2));
  const [people,setPeople]=useState(1),[children,setChildren]=useState(0),[type,setType]=useState(pre?.type||'All');
  const [pick,setPick]=useState(room||''),[source,setSource]=useState('Direct');
  const [errors,setErrors]=useState<Record<string,string>>({});
  const {view:q}=useAvailability({arrival,departure,adults:people,children,roomTypeId:type==='All'?undefined:type});
  // Room is optional: with none selected, the first available room of the search is auto-assigned (SCR-04).
  const chosenRow=pick?q.available.find(r=>r.room.id===pick):q.available[0],chosen=chosenRow?.room,chosenRate=chosenRow?.ratePaise??0;
  const pickedExcluded=pick&&!chosen?q.excluded.find(r=>r.room.id===pick):undefined;
  const dateError=!DATE.test(arrival)||!DATE.test(departure)?'Choose both dates.':arrival<s.date?'Arrival cannot be before the current business date.':nights(arrival,departure)<=0?'Departure must follow arrival.':nights(arrival,departure)>30?'Maximum 30 nights in this demo.':'';
  const n=dateError?0:nights(arrival,departure);
  // Stay restrictions (DEC-053) for the room that would be booked; approvers may override with a reason.
  const [rstOn,setRstOn]=useState(false),[rstReason,setRstReason]=useState('');
  const rst=rstState(s,uid,dateError?'':chosen?.type||'',arrival,departure,rstOn?rstReason:null);
  const save=(form:HTMLFormElement,hold:boolean)=>{
    const f=new FormData(form),e:Record<string,string>={};
    const guest=field(f,'guest').trim(),phone=field(f,'phone').trim(),ota=field(f,'otaChannel').trim(),company=field(f,'companyName').trim(),gstin=field(f,'gstin').trim().toUpperCase();
    if(guest.length<2)e.guest='Enter the guest’s name (at least 2 characters).';
    if(!phone)e.phone='Phone is required.';else if(!RES_PHONE.test(phone))e.phone='Enter 7–16 digits (fictional number).';
    if(source==='OTA'&&ota.length<2)e.otaChannel='Name the OTA channel.';
    if(gstin&&!company)e.companyName='Enter the company name for the GSTIN.';
    if(gstin&&!RES_GSTIN.test(gstin))e.gstin='Use the 15-character format, e.g. 27AAAAA0000A1Z5.';
    if(dateError)e.dates=dateError;
    if(!rst.canSubmit)e.restriction='Resolve the stay restriction first.';
    if(!chosen)e.room=q.available.length?'Select one of the available rooms, or Auto-assign.':'No room is available for this search. Change dates, party size or type.';
    setErrors(e);if(Object.keys(e).length)return;
    commit(c=>quoteAndCreate(c,{arrival,departure,adults:people,children},{roomId:chosen!.id,guest:{name:guest,phone,email:field(f,'email')},source,otaChannel:source==='OTA'?ota:undefined,
      company:company?{name:company,gstin:gstin||undefined}:undefined,notes:field(f,'notes'),hold,restrictionOverride:rst.restrictionOverride},{idempotencyKey:modal!.key}),
      hold?'Room held for 2 hours of simulated time. Confirm it before the hold expires.':'Reservation confirmed. Open it to register the guest and check in.',
      r=>booking(r.reservationId));
  };
  return <><div className="modal-heading"><div className="eyebrow">FRONT DESK</div><h2>New reservation</h2><p>Search availability, then hold or confirm a room.</p></div>
    <form className="form" noValidate onSubmit={e=>{e.preventDefault();save(e.currentTarget,false);}}>
      <fieldset className="res-fieldset"><legend>1 · Availability search</legend>
        <div className="res-search-row">
          <label>Arrival<input type="date" required min={s.date} value={arrival} onChange={e=>{const v=e.target.value;setArrival(v);if(DATE.test(v)&&departure<=v)setDeparture(addDays(v,1));}}/></label>
          <label>Departure<input type="date" required min={addDays(arrival||s.date,1)} value={departure} onChange={e=>setDeparture(e.target.value)}/></label>
          <label>Adults<select value={people} onChange={e=>setPeople(Number(e.target.value))}>{[1,2,3,4].map(n=><option key={n}>{n}</option>)}</select></label>
          <label>Children<select value={children} onChange={e=>setChildren(Number(e.target.value))}>{[0,1,2,3].map(n=><option key={n}>{n}</option>)}</select></label>
          <label>Room type<select value={type} onChange={e=>setType(e.target.value)}><option value="All">Any type</option>{Object.entries(RES_CAPACITY).map(([t,c])=><option key={t} value={t}>{t} · sleeps {c}</option>)}</select></label>
        </div>
        {err(errors.dates||dateError)}
        {!dateError&&<div className="res-results" aria-live="polite">
          <div className="res-results-head"><strong>{q.available.length} available</strong><span className="muted">{n} night{n===1?'':'s'} · party of {q.party}</span></div>
          {q.available.length?<div className="res-room-options" role="radiogroup" aria-label="Available rooms">
            <label className={'res-room-option '+(!pick?'selected':'')}><input type="radio" name="room" value="" checked={!pick} onChange={()=>setPick('')}/><span><strong>Auto-assign</strong><small>First available{q.available[0]?' · '+q.available[0].room.id:''}</small></span></label>
            {q.available.map(({room:r,capacity,ratePaise,occupiedNow})=>
            <label key={r.id} className={'res-room-option '+(pick===r.id?'selected':'')}><input type="radio" name="room" value={r.id} checked={pick===r.id} onChange={()=>setPick(r.id)}/>
              <span><strong>{r.id} · {r.type}</strong><small>Sleeps {capacity} · {money(ratePaise)}/night{occupiedNow?' · occupied now, free on your dates':''}</small></span></label>)}</div>
            :<Empty title="No availability" body="No room of this type fits these dates and party size. Try other dates, fewer guests or another type."/>}
          {pickedExcluded&&<div className="info-note"><TriangleAlert size={16}/>Room {pickedExcluded.room.id} is not available: {pickedExcluded.reason}. Choose another room.</div>}
          {q.excluded.length>0&&<details className="res-excluded"><summary>{q.excluded.length} room{q.excluded.length===1?'':'s'} excluded — see why</summary>
            <ul>{q.excluded.map(x=><li key={x.room.id}><span className="room-number">{x.room.id}</span><span>{x.room.type}</span><span className="res-reason">{x.reasons.length>1?x.reason+' · '+x.reasons.slice(1).join(', ').toLowerCase():x.reason}</span></li>)}</ul></details>}
          {err(errors.room)}
        </div>}
      </fieldset>
      <fieldset className="res-fieldset"><legend>2 · Guest and booking details</legend>
        <div className="form-row"><label>Guest name *<input name="guest" placeholder="e.g. Demo Guest" maxLength={80} aria-invalid={!!errors.guest}/>{err(errors.guest)}</label>
          <label>Phone *<input name="phone" type="tel" inputMode="tel" placeholder="+91 90000 00000" maxLength={16} aria-invalid={!!errors.phone}/>{err(errors.phone)}</label></div>
        <div className="form-row"><label>Fictional email (optional)<input type="email" name="email" placeholder="guest@example.test"/></label>
          <label>Booking source *<select name="source" value={source} onChange={e=>setSource(e.target.value)}>{RES_SOURCES.map(x=><option key={x}>{x}</option>)}</select></label></div>
        {source==='OTA'&&<label>OTA channel name *<input name="otaChannel" list="res-ota" placeholder="e.g. Demo Travel Hub" maxLength={60} aria-invalid={!!errors.otaChannel}/><datalist id="res-ota">{RES_OTA_CHANNELS.map(x=><option key={x} value={x}/>)}</datalist>{err(errors.otaChannel)}</label>}
        <div className="form-row"><label>Company (optional)<input name="companyName" placeholder="e.g. Demo Industries (fictional)" maxLength={80} aria-invalid={!!errors.companyName}/>{err(errors.companyName)}</label>
          <label>GSTIN (optional, fictional)<input name="gstin" placeholder="27AAAAA0000A1Z5" maxLength={15} style={{textTransform:'uppercase'}} aria-invalid={!!errors.gstin}/>{err(errors.gstin)}</label></div>
        <label>Notes<textarea name="notes" rows={2} placeholder="Preferences or arrival notes…" maxLength={300}/></label>
      </fieldset>
      <RestrictionNotice st={rst} on={rstOn} reason={rstReason} onToggle={setRstOn} onReason={setRstReason} subject={chosen?`room ${chosen.id} (${chosen.type})`:undefined}/>
      {chosen&&<div className="summary-list res-summary"><div><span>Room {chosen.id} · {chosen.type}{pick?'':' (auto-assigned)'}</span><strong>{money(chosenRate)} / night</strong></div><div><span>{n} night{n===1?'':'s'} · {shortDate(arrival)} — {shortDate(departure)}</span><strong>{money(chosenRate*n)} + synthetic tax</strong></div></div>}
      <div className="info-note"><Hourglass size={16}/>A hold reserves the room for 2 hours of simulated time, then releases it automatically. Availability is rechecked for the full date range when you save. No external booking or message is sent.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button type="button" className="btn" disabled={busy||!rst.canSubmit} onClick={e=>save(e.currentTarget.form!,true)}><Hourglass size={15}/> Hold 2 hours</button>
        <button className="btn primary" disabled={busy||!rst.canSubmit}>Confirm reservation <ArrowRight size={16}/></button></div>
    </form></>;
}

export function CancelReservation({id}:{id:string}){
  const {s,run,booking,busy}=useHcc();const b=s.bookings.find(b=>b.id===id)!;
  const [reason,setReason]=useState('');
  const held=resHoldActive(s,b)&&b.status==='Held',t=totals(b),money_=t.paid!==0||t.deposit!==0;
  const open_=b.status==='Confirmed'||held;
  const noShowWhy=held?'Holds cannot be marked no-show.':b.arrival>s.date?`No-show is available from the arrival date (${shortDate(b.arrival)}).`:'';
  return <><div className="modal-heading"><h2>{held?'Cancel hold':'Cancel or mark no-show'}</h2><p>{b.guest} · {b.id} · Room {b.room} · {shortDate(b.arrival)} — {shortDate(b.departure)}</p></div>
    {!open_&&<div className="info-note"><Info size={16}/>This booking is {resStatus(s,b)} and can no longer be cancelled.</div>}
    {money_&&<div className="info-note"><TriangleAlert size={16}/>Money is held on this booking ({money(t.paid)} paid, {money(t.deposit)} deposit). Refund the advance and return any deposit first.</div>}
    <div className="form"><label>Reason (optional)<input value={reason} onChange={e=>setReason(e.target.value)} maxLength={200} placeholder="e.g. Guest changed plans"/></label></div>
    <div className="info-note"><Info size={16}/>This demo does not calculate cancellation penalties. Cancelling releases the room immediately.</div>
    {noShowWhy&&b.status==='Confirmed'&&<p className="muted">{noShowWhy}</p>}
    <div className="modal-actions"><button className="btn" onClick={()=>booking(b.id)}>Back</button>
      {b.status==='Confirmed'&&<button className="btn" disabled={busy||!!noShowWhy||money_} title={noShowWhy||undefined} onClick={()=>run({type:'CANCEL',id:b.id,noShow:true,reason,key:'noshow:'+b.id},'Marked no-show',{after:()=>booking(b.id)})}>No-show</button>}
      <button className="btn danger" disabled={busy||!open_||money_} onClick={()=>run({type:'CANCEL',id:b.id,reason,key:'cancel:'+b.id},held?'Hold cancelled. Room released.':'Reservation cancelled',{after:()=>booking(b.id)})}>{held?'Cancel hold':'Cancel booking'}</button></div></>;
}

/** M1 move/extend dialog — still used for in-house stays until stream B's stay-move UI replaces it. */
export function MoveStay({id}:{id:string}){
  const {s,run,booking,modal,busy}=useHcc();const b=s.bookings.find(b=>b.id===id)!;
  return <><div className="modal-heading"><h2>Move room or extend stay</h2><p>{b.guest} · Current room {b.room}. The agreed nightly tariff is preserved.</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'MOVE',id:b.id,room:field(f,'room'),departure:field(f,'departure'),key:modal!.key},'Stay updated. Availability revalidated.',{after:()=>booking(b.id)}))}>
      <label>Room<select name="room" defaultValue={b.room}>{s.rooms.map(r=><option key={r.id} value={r.id}>{r.id} · {r.type}{r.blocked?' · Blocked':''}</option>)}</select></label>
      <label>Departure<input name="departure" type="date" required defaultValue={b.departure}/></label>
      <div className="info-note"><Info size={16}/>Conflicts are rejected and the original stay is kept. Moving an in-house guest creates a cleaning task for the old room. Room-segment history arrives in M2.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>Save changes</button></div></form></>;
}

/** 'move' modal: non-drag "Move / change dates" for Confirmed (and running Held) reservations — CHANGE_DATES (REQ-RACK-005, REQ-BOOK-005). */
export function MoveReservation({id}:{id?:string}){
  const {s}=useHcc();const b=s.bookings.find(b=>b.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  if(b.status==='In house')return <MoveStay id={b.id}/>;
  return <ChangeDates id={b.id}/>;
}
function ChangeDates({id}:{id:string}){
  const {s,uid,run,booking,modal,busy}=useHcc();const b=s.bookings.find(b=>b.id===id)!;
  const [version]=useState(b.version);
  const [room,setRoom]=useState(b.room),[arrival,setArrival]=useState(b.arrival<s.date?s.date:b.arrival),[departure,setDeparture]=useState(b.departure);
  const editable=b.status==='Confirmed'||(b.status==='Held'&&resHoldActive(s,b));
  const q=resSearch(s,{arrival,departure,people:b.people,children:b.children||0,except:b.id});
  const rows=[...q.available,...q.excluded].sort((a,c)=>a.room.id.localeCompare(c.room.id)),target=rows.find(r=>r.room.id===room);
  const unchanged=room===b.room&&arrival===b.arrival&&departure===b.departure,stale=b.version!==version;
  const [rstOn,setRstOn]=useState(false),[rstReason,setRstReason]=useState('');
  const rst=rstState(s,uid,q.valid&&!unchanged?target?.room.type||'':'',arrival,departure,rstOn?rstReason:null);
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {resStatus(s,b).toUpperCase()}</div><h2>Move / change dates</h2><p>{b.guest} · now Room {b.room}, {shortDate(b.arrival)} — {shortDate(b.departure)}. The agreed tariff {money(b.rate)}/night is kept.</p></div>
    {!editable&&<div className="info-note"><Info size={16}/>This booking is {resStatus(s,b)}; its dates can no longer be changed here.</div>}
    {stale&&<div className="info-note"><TriangleAlert size={16}/>This reservation changed after you opened this dialog. Saving will be rejected — reopen it to see the latest version.</div>}
    <form className="form" noValidate onSubmit={e=>{e.preventDefault();if(!rst.canSubmit)return;run({type:'CHANGE_DATES',id:b.id,room,arrival,departure,version,restrictionOverride:rst.restrictionOverride,key:modal!.key},'Reservation updated. Full interval revalidated.',{after:()=>booking(b.id)});}}>
      <div className="form-row"><label>Arrival<input type="date" required min={s.date} value={arrival} onChange={e=>setArrival(e.target.value)}/></label><label>Departure<input type="date" required min={addDays(arrival||s.date,1)} value={departure} onChange={e=>setDeparture(e.target.value)}/></label></div>
      <label>Room<select value={room} onChange={e=>setRoom(e.target.value)}>{rows.map(r=><option key={r.room.id} value={r.room.id}>{r.room.id} · {r.room.type} · {r.ok?'Available':r.reason}{r.room.id===b.room?' (current)':''}</option>)}</select></label>
      {q.valid&&target&&!target.ok&&<div className="info-note res-conflict" role="alert"><TriangleAlert size={16}/>Conflict: {target.reason}. Saving will be rejected and the original reservation kept.</div>}
      {!q.valid&&<small className="res-field-error" role="alert">Departure must follow arrival.</small>}
      <RestrictionNotice st={rst} on={rstOn} reason={rstReason} onToggle={setRstOn} onReason={setRstReason} subject={target?`room ${target.room.id} (${target.room.type})`:undefined}/>
      {b.status==='Held'&&<div className="info-note"><Hourglass size={16}/>Changing dates does not extend the hold ({resHoldLeftLabel(s,b)}).</div>}
      <div className="info-note"><Info size={16}/>Non-drag alternative to moving a bar on the calendar. The whole new interval is revalidated; capacity for {b.people+(b.children||0)} guest(s) is checked.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy||!editable||unchanged||!rst.canSubmit}>{unchanged?'No changes yet':'Save changes'}</button></div></form></>;
}
