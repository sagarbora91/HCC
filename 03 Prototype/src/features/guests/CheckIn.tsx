// Arrivals page, guided check-in stepper and walk-in flow (SCR-06, REQ-STAY-001, WF-STAY-001).
import React,{useState,useRef,useEffect} from 'react';
import {Check,ArrowRight,ArrowLeft,DoorOpen,AlertCircle,BedDouble,Printer,UserPlus,Info,Loader2,X} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Booking,Room,money,nights,addDays,totals,gstForRoomNight,gstStayTax,gstPct,guestForBooking,guestComplete,stayArrivals,stayRoomEligibility,stayCheckinAlternatives,stayFreeRooms,guestView,staySegments,plural} from '../../services/mock';
import {PageTitle,Pill,Empty,shortDate,SearchBox} from '../../components/ui';
import {GuestForm,emptyDraft,draftOf,hashOf} from './GuestForm';
import {GuestDetails,statusTone} from './GuestProfile';
import {bookingIdLookup} from '../../domain/streams/customers';

const stepNames=['Find reservation','Verify guest','Room eligibility','Confirm tariff','Advance / deposit','Check in'];

/** Short eligibility label for lists. */
function RoomState({b}:{b:Booking}){const {s}=useHcc();const e=stayRoomEligibility(s,b);const r=s.rooms.find(r=>r.id===b.room);
  return e.ok?<Pill tone="green">✓ Room ready</Pill>:<Pill tone="orange">{r?.blocked?'Room blocked':r&&r.hk!=='Ready'?'Room '+r.hk.toLowerCase():'Room conflict'}</Pill>;}

/** Page 'arrivals': today's arrivals (Confirmed, arrival ≤ business date) + Walk-in. */
export function ArrivalsPage(){
  const {s,uid,open,booking,permission,search,setSearch}=useHcc();
  const list=stayArrivals(s).filter(b=>`${b.guest} ${b.id} ${b.room}`.toLowerCase().includes(search.toLowerCase()));
  // Booking-ID lookup (any status): the one place to find a booking by its ID (Customers search by name or mobile only).
  const found=bookingIdLookup(s,search).filter(b=>!list.some(x=>x.id===b.id));
  const today=s.bookings.filter(b=>b.status==='In house'&&staySegments(b)[0]?.from===s.date);
  const canWalkin=permission('reservations')&&permission('checkin');
  return <><PageTitle title="Arrivals & check-in" sub={`Expected today and overdue arrivals · business date ${shortDate(s.date)} · check-in from ${s.settings.checkin}`}
      action={canWalkin?<button className="btn primary" onClick={()=>open({type:'guest-walkin'})}><UserPlus size={16}/>Walk-in</button>:<span className="muted">Walk-in needs reservation and check-in permissions.</span>}/>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Search guest, room or booking ID…"/><span className="muted">{list.length} awaiting check-in</span></div>
    {found.length>0&&<section className="card" aria-label="Booking ID lookup"><div className="section-title"><h2>Booking ID lookup</h2><span className="muted">{found.length} found</span></div>
      <ul className="gs-simple-list">{found.map(b=><li key={b.id}><strong>{b.id}</strong><span>{b.guest} · room {b.room} · {shortDate(b.arrival)} — {shortDate(b.departure)}</span><Pill tone={statusTone(b.status)}>{b.status}</Pill>
        <button className="text-btn" onClick={()=>booking(b.id)}>Open booking</button>{b.status==='Confirmed'&&b.arrival>s.date&&<small>Arrives {shortDate(b.arrival)} — check-in opens that day</small>}</li>)}</ul></section>}
    <section className="card"><div className="table-wrap"><table><thead><tr><th>Guest / booking</th><th>Room</th><th>Stay</th><th>Registration</th><th>Room status</th><th>Advance</th><th></th></tr></thead><tbody>
      {list.map(b=>{const g=guestForBooking(s,b);const v=g?guestView(s,uid,g):undefined;return <tr key={b.id}>
        <td><strong>{b.guest}</strong><small>{b.id} · {b.source}{v?' · '+v.phone:''}</small></td>
        <td><span className="room-number">{b.room}</span><small>{s.rooms.find(r=>r.id===b.room)?.type}</small></td>
        <td>{shortDate(b.arrival)} — {shortDate(b.departure)}<small>{plural(nights(b.arrival,b.departure),'night')}{b.arrival<s.date?' · overdue arrival':''}</small></td>
        <td>{b.registered?<Pill tone="green">✓ Verified</Pill>:<Pill tone="blue">Not yet</Pill>}</td>
        <td><RoomState b={b}/></td>
        <td className="number">{money(totals(b).paid)}</td>
        <td>{permission('checkin')?<button className="btn small-btn primary" onClick={()=>open({type:'guest-checkin',id:b.id})}>{b.registered?'Continue check-in':'Start check-in'}</button>:<span className="muted">Check-in permission needed</span>}</td></tr>;})}
    </tbody></table>{!list.length&&<Empty title="No arrivals waiting" body="Everyone expected today has checked in. Use Walk-in for guests without a reservation."/>}</div></section>
    {today.length>0&&<section className="card"><div className="section-title"><h2>Checked in today</h2><span className="muted">{today.length}</span></div>
      <ul className="gs-simple-list">{today.map(b=><li key={b.id}><span className="room-number">{b.room}</span><strong>{b.guest}</strong><small>{b.id}</small><button className="text-btn" onClick={()=>open({type:'stay-detail',id:b.id})}>Stay details</button></li>)}</ul></section>}
    <p className="footnote">Check-in requires a verified registration and a vacant, ready, unblocked room. All records are fictional.</p></>;
}

function Progress({step,done,setStep,reachable}:{step:number;done:boolean[];setStep:(n:number)=>void;reachable:number}){
  return <ol className="gs-steps" aria-label="Check-in progress">{stepNames.map((n,i)=>{const k=i+1;return <li key={n} className={(k===step?'current ':'')+(done[i]?'done':'')}>
    <button type="button" disabled={k>reachable} aria-current={k===step?'step':undefined} onClick={()=>setStep(k)}><span className="gs-step-no">{done[i]?<Check size={12}/>:k}</span><span className="gs-step-name">{n}</span><span className="sr-only">{done[i]?' (complete)':''}</span></button></li>;})}</ol>;
}

/** Modal 'guest-checkin': id = booking ID (optional; without it step 1 lists today's arrivals). */
export function CheckinModal({id}:{id?:string}){
  const {s,run,open,closeModal,permission,busy,modal}=useHcc();
  const [bid,setBid]=useState(id||'');const b=s.bookings.find(x=>x.id===bid);
  const [tariffOk,setTariffOk]=useState(false);const [editGuest,setEditGuest]=useState(false);const [q,setQ]=useState('');
  const g=b?guestForBooking(s,b):undefined;
  const regDone=!!b&&b.registered&&(!g||g.origin==='auto'||guestComplete(g));
  const elig=b?stayRoomEligibility(s,b):{ok:false,reasons:[]};
  const done=[!!b,regDone,elig.ok,tariffOk,true,b?.status==='In house'];
  const firstOpen=done.findIndex((x,i)=>!x&&i!==4);
  const [step,setStep]=useState(()=>!b?1:b.status==='In house'?6:!b.registered?2:!stayRoomEligibility(s,b).ok?3:4);
  const reachable=firstOpen<0?6:firstOpen+1;
  if(!permission('checkin'))return <Empty title="Not permitted" body="Check-in needs the check-in permission (PERM-STY-001)."/>;
  if(b&&!['Confirmed','In house'].includes(b.status))return <><div className="modal-heading"><h2>Check-in unavailable</h2><p>{b.guest} · {b.id} is {b.status}.</p></div><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  if(b&&b.arrival>s.date&&b.status==='Confirmed')return <><div className="modal-heading"><h2>Not arriving yet</h2><p>{b.guest} arrives {shortDate(b.arrival)}. Check-in opens on the arrival business date.</p></div><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const next=<button type="button" className="btn primary" disabled={!done[step-1]&&step!==5} onClick={()=>setStep(step+1)}>Continue <ArrowRight size={16}/></button>;
  const prev=step>1&&<button type="button" className="btn" onClick={()=>setStep(step-1)}><ArrowLeft size={16}/>Back</button>;
  const room=b?s.rooms.find(r=>r.id===b.room):undefined;
  let body:React.ReactNode=null;
  if(b?.status==='In house')body=<div className="gs-success"><Check size={30}/><h3>{b.guest} is checked in to room {b.room}</h3><p>Room segment started {shortDate(staySegments(b)[0]?.from||s.date)}. The room board and arrivals list are updated.</p>
    <div className="modal-actions"><button className="btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={15}/>Registration card</button><button className="btn" onClick={()=>open({type:'stay-detail',id:b.id})}>Stay details</button><button className="btn primary" onClick={closeModal}>Done</button></div></div>;
  else if(step===1){const list=stayArrivals(s).filter(x=>`${x.guest} ${x.id} ${x.room}`.toLowerCase().includes(q.toLowerCase()));
    body=<><SearchBox value={q} onChange={setQ} placeholder="Search today’s arrivals…"/>
      <ul className="gs-pick">{list.map(x=><li key={x.id}><button type="button" className={x.id===bid?'active':''} aria-pressed={x.id===bid} onClick={()=>{setBid(x.id);setTariffOk(false);}}><strong>{x.guest}</strong><small>{x.id} · room {x.room} · {shortDate(x.arrival)} — {shortDate(x.departure)}</small>{x.registered&&<Pill tone="green">Verified</Pill>}</button></li>)}</ul>
      {!list.length&&<Empty title="No matching arrivals" body="Use Walk-in for a guest without a reservation."/>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'guest-walkin'})}><UserPlus size={15}/>Walk-in instead</button>{next}</div></>;}
  else if(b&&step===2){
    const register=(d:ReturnType<typeof emptyDraft>,gid?:string)=>run({type:'REGISTER',id:b.id,guestId:gid??g?.id,guest:d,key:`${modal!.key}:register:${hashOf([d,gid])}`},'Guest verified. Registration saved.',{after:()=>{setEditGuest(false);setStep(3);}});
    body=regDone&&!editGuest&&g?<><GuestDetails g={g}/>{g.origin==='auto'&&<div className="info-note"><Info size={16}/>Minimal demo profile created from the booking name. Add real-looking fictional details with Edit details.</div>}
        <div className="modal-actions">{prev}<button type="button" className="btn" onClick={()=>setEditGuest(true)}>Edit details</button>{next}</div></>
      :<GuestForm key={g?.id||'new'} initial={g&&g.origin!=='auto'?draftOf(g):emptyDraft({name:b.guest.replace(/\s*\(fictional\)\s*/,''),phone:b.phone||'',email:b.email,party:g?.party})} guestId={g?.id} registration
        submitLabel={regDone?'Save and continue':'Verify registration'} onSubmit={d=>register(d)} onUseExisting={x=>register(draftOf(x),x.id)}
        secondary={<>{prev}{regDone&&<button type="button" className="btn" onClick={()=>setEditGuest(false)}>Cancel edit</button>}</>}/>;
  }
  else if(b&&step===3){const alts=stayCheckinAlternatives(s,b);
    body=<>{elig.ok?<div className="gs-ok"><Check size={18}/><div><strong>Room {b.room} · {room?.type} is vacant, ready and unblocked.</strong><p>Free for {shortDate(b.arrival)} — {shortDate(b.departure)}.</p></div></div>
      :<><div className="gs-deny" role="alert"><AlertCircle size={18}/><div><strong>Check-in to room {b.room} is not possible now.</strong><ul>{elig.reasons.map(r=><li key={r}>{r}</li>)}</ul></div></div>
        <h3 className="gs-sub">Eligible {room?.type} rooms for these dates</h3>
        {alts.length?<ul className="gs-rooms">{alts.map((r:Room)=><li key={r.id}><span className="room-number">{r.id}</span><span>{r.type} · floor {r.floor}<small>Ready · vacant · unblocked</small></span>
          {permission('reservations')?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'MOVE',id:b.id,room:r.id,departure:b.departure,key:`${modal!.key}:move:${r.id}`},`Allocation changed to room ${r.id}. Agreed tariff kept.`)}>Switch to {r.id}</button>:<span className="muted">Needs reservations permission</span>}</li>)}</ul>
          :<Empty title={`No eligible ${room?.type||''} rooms`} body="Ask housekeeping to finish and inspect a room, or change the room type from Reservations."/>}
        <p className="footnote">Switching uses the room-move command: availability is rechecked and the agreed tariff ({money(b.rate)}/night) is preserved.</p></>}
      <div className="modal-actions">{prev}{next}</div></>;}
  else if(b&&step===4){const n=nights(b.arrival,b.departure),bps=gstForRoomNight(s,b.arrival,b.rate).bps,net=b.rate*n;
    body=<><div className="summary-list"><div><span>Agreed nightly tariff</span><strong>{money(b.rate)}</strong></div><div><span>Nights</span><strong>{n} ({shortDate(b.arrival)} — {shortDate(b.departure)})</strong></div>
      <div><span>Room charges (estimate)</span><strong>{money(net)}</strong></div><div><span>GST ({gstPct(bps)} · CGST + SGST)</span><strong>{money(gstStayTax(s,b.rate,b.arrival,b.departure))}</strong></div><div><span>Source</span><strong>{b.source}</strong></div></div>
      <label className="gs-check"><input type="checkbox" checked={tariffOk} onChange={e=>setTariffOk(e.target.checked)}/>The guest has confirmed the tariff and stay dates.</label>
      <div className="info-note"><Info size={16}/>Rate overrides need approval and are handled from the reservation. Room nights are posted nightly by the day close.</div>
      <div className="modal-actions">{prev}{next}</div></>;}
  else if(b&&step===5){const t=totals(b);
    body=<><div className="summary-list"><div><span>Advance received</span><strong>{money(t.paid)}</strong></div><div><span>Refundable deposit held</span><strong>{money(t.deposit)}</strong></div></div>
      <div className="info-note"><Info size={16}/>Optional. Recording a payment opens the payment dialog; reopen this check-in from Arrivals afterwards — completed steps are remembered from the record.</div>
      <div className="modal-actions">{prev}{permission('payments')?<button type="button" className="btn" onClick={()=>open({type:'payment',id:b.id})}>Record advance / deposit</button>:<span className="muted">Payments permission needed to record an advance.</span>}<button type="button" className="btn primary" onClick={()=>setStep(6)}>{t.paid||t.deposit?'Continue':'Skip'} <ArrowRight size={16}/></button></div></>;}
  else if(b&&step===6){const missing=done.slice(0,4).map((x,i)=>x?null:stepNames[i]).filter(Boolean);
    body=<><div className="summary-list"><div><span>Guest</span><strong>{b.guest}</strong></div><div><span>Room</span><strong>{b.room} · {room?.type}</strong></div><div><span>Stay</span><strong>{shortDate(b.arrival)} — {shortDate(b.departure)}</strong></div><div><span>Tariff</span><strong>{money(b.rate)}/night</strong></div></div>
      {missing.length>0&&<div className="gs-deny" role="alert"><AlertCircle size={18}/><div><strong>Complete these steps first:</strong> {missing.join(', ')}.</div></div>}
      <div className="modal-actions">{prev}<button type="button" className="btn primary" disabled={busy||missing.length>0} onClick={()=>run({type:'CHECKIN',id:b.id,key:'checkin:'+b.id},`Checked in to room ${b.room}. Room board updated.`)}><DoorOpen size={16}/>Check in</button></div></>;}
  return <><div className="modal-heading"><div className="eyebrow">ARRIVALS · GUIDED CHECK-IN{b?' · '+b.id:''}</div><h2>{b?b.guest:'Find the reservation'}</h2>{b&&<p>Room {b.room} · {shortDate(b.arrival)} — {shortDate(b.departure)} · {b.people} guest{b.people>1?'s':''}</p>}</div>
    {b?.status!=='In house'&&<Progress step={step} done={done} setStep={setStep} reachable={reachable}/>}
    <div className="gs-step-body"><h3 className="gs-sub">{b?.status==='In house'?'Checked in':`Step ${step} of 6 · ${stepNames[step-1]}`}</h3>{body}</div></>;
}

type St='pending'|'running'|'done'|'failed';
/** Modal 'guest-walkin': BOOK → REGISTER(details) → CHECKIN, each a separate run() with its own key; partial failures are recoverable. */
export function WalkinModal(){
  const c=useHcc();const {s,open,closeModal,permission,busy,modal}=c;
  // Latest context values for the sequential flow (run's fault mode and the state are per render).
  const live=useRef(c);live.current=c;const renders=useRef(0);renders.current++;
  const settle=async()=>{const start=renders.current;for(let i=0;i<50&&renders.current===start;i++)await new Promise(r=>setTimeout(r,20));};
  const [departure,setDeparture]=useState(addDays(s.date,1));const [people,setPeople]=useState(1);
  const free=departure>s.date?stayFreeRooms(s,s.date,departure):[];
  const [room,setRoom]=useState('');const chosen=free.some(r=>r.id===room)?room:free[0]?.id||'';
  const [p,setP]=useState<{book:St;register:St;checkin:St;bid?:string;room?:string;msg?:string;note?:string}>({book:'pending',register:'pending',checkin:'pending'});
  // Bring the progress/recovery panel into view whenever a step finishes or fails.
  const seqRef=useRef<HTMLDivElement>(null);const sig=[p.book,p.register,p.checkin].join();
  useEffect(()=>{if(/failed|done/.test(sig)){const t=setTimeout(()=>seqRef.current?.scrollIntoView?.({block:'start'}),60);return ()=>clearTimeout(t);}},[sig]);
  if(!permission('reservations')||!permission('checkin'))return <Empty title="Not permitted" body="A walk-in needs both reservation and check-in permissions."/>;
  const key=modal!.key;
  async function doCheckin(bid:string){
    setP(x=>({...x,checkin:'running',msg:undefined}));await settle();
    const ok=await live.current.run({type:'CHECKIN',id:bid,key:key+':checkin'},'Walk-in checked in.');await settle();
    const st=live.current.s.bookings.find(b=>b.id===bid)?.status;
    if(ok||st==='In house')setP(x=>({...x,checkin:'done',note:ok?x.note:'The confirmation was lost, but the record shows the guest checked in.'}));else setP(x=>({...x,checkin:'failed',msg:live.current.error}));
  }
  async function start(d:ReturnType<typeof emptyDraft>,gid?:string){
    let bid=p.bid;
    if(!bid){
      const known=new Set(s.bookings.map(b=>b.id));setP(x=>({...x,book:'running',msg:undefined}));
      const ok=await live.current.run({type:'BOOK',guest:d.name,email:d.email,people,room:chosen,arrival:s.date,departure,source:'Walk-in',notes:'Walk-in guest',key:key+':book'},'Walk-in reservation created.');
      await settle();
      bid=live.current.s.bookings.find(b=>!known.has(b.id)&&b.room===chosen&&b.source==='Walk-in')?.id;
      if(!bid){setP(x=>({...x,book:'failed',msg:live.current.error||'The reservation was not created.'}));return;}
      setP(x=>({...x,book:'done',bid,room:chosen,note:ok?undefined:'The confirmation was lost, but the reservation is on record — continuing.'}));
    }
    setP(x=>({...x,register:'running',msg:undefined}));await settle();
    const ok=await live.current.run({type:'REGISTER',id:bid,guest:d,guestId:gid,key:`${key}:register:${hashOf([d,gid])}`},'Walk-in guest registered.');await settle();
    if(!ok&&!live.current.s.bookings.find(b=>b.id===bid)?.registered){setP(x=>({...x,register:'failed',msg:live.current.error}));return;}
    setP(x=>({...x,register:'done'}));await doCheckin(bid);
  }
  const icon=(st:St)=>st==='done'?<Check size={14}/>:st==='running'?<Loader2 size={14} className="gs-spin"/>:st==='failed'?<X size={14}/>:<span className="gs-dot"/>;
  const label=(st:St)=>({pending:'Waiting',running:'In progress',done:'Done',failed:'Failed'})[st];
  const b=p.bid?s.bookings.find(x=>x.id===p.bid):undefined;const failed=p.book==='failed'||p.register==='failed'||p.checkin==='failed';const started=p.book!=='pending';
  return <><div className="modal-heading"><div className="eyebrow">ARRIVALS · WALK-IN</div><h2>Walk-in guest</h2><p>Creates a reservation, registers the guest and checks in — three recorded steps.</p></div>
    {started&&<div className="gs-seq" aria-live="polite" ref={seqRef}><ol>
      {([['book','1 · Create reservation (BOOK)'],['register','2 · Register guest (REGISTER)'],['checkin','3 · Check in (CHECKIN)']] as const).map(([k,l])=><li key={k} className={'gs-seq-'+p[k]}>{icon(p[k])}<span>{l}</span><strong>{label(p[k])}</strong></li>)}</ol>
      {p.note&&<div className="info-note"><Info size={16}/>{p.note}</div>}
      {failed&&<div className="gs-deny" role="alert"><AlertCircle size={18}/><div>
        {p.book==='failed'&&<><strong>No reservation was created.</strong><p>{p.msg} Correct the details below and submit again.</p></>}
        {p.register==='failed'&&<><strong>Reservation {p.bid} was created for room {p.room}, but registration was not saved.</strong><p>{p.msg} Correct the guest details below and retry, continue in guided check-in, or cancel the reservation.</p></>}
        {p.checkin==='failed'&&<><strong>Reservation {p.bid} is registered but not checked in.</strong><p>{p.msg} Retry, or continue in guided check-in to pick another eligible room.</p></>}
        {p.bid&&<div className="inline">{p.checkin==='failed'&&<button className="btn small-btn primary" disabled={busy} onClick={()=>doCheckin(p.bid!)}>Retry check-in</button>}
          <button className="btn small-btn" onClick={()=>open({type:'guest-checkin',id:p.bid})}>Continue in guided check-in</button>
          {b?.status==='Confirmed'&&<button className="btn small-btn danger" disabled={busy} onClick={()=>c.run({type:'CANCEL',id:p.bid!,key:key+':cancel'},`Walk-in reservation ${p.bid} cancelled.`,{close:true})}>Cancel reservation {p.bid}</button>}</div>}
      </div></div>}
      {p.checkin==='done'&&b&&<div className="gs-success"><Check size={30}/><h3>{b.guest} is checked in to room {b.room}</h3>
        <div className="modal-actions"><button className="btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={15}/>Registration card</button>{permission('payments')&&<button className="btn" onClick={()=>open({type:'payment',id:b.id})}>Record advance / deposit</button>}<button className="btn" onClick={()=>open({type:'stay-detail',id:b.id})}>Stay details</button><button className="btn primary" onClick={closeModal}>Done</button></div></div>}
    </div>}
    {p.checkin!=='done'&&p.register!=='done'&&<>
      <div className="form gs-walkin-stay">
        <div className="form-row"><label>Departure<input type="date" value={departure} min={addDays(s.date,1)} max={addDays(s.date,30)} disabled={!!p.bid} onChange={e=>setDeparture(e.target.value)}/></label>
          <label>Guests<select value={people} disabled={!!p.bid} onChange={e=>setPeople(Number(e.target.value))}>{[1,2,3,4].map(n=><option key={n} value={n}>{n}</option>)}</select></label></div>
        <label>Room (vacant, ready and free until departure)<select value={p.bid?p.room:chosen} disabled={!!p.bid||!free.length} onChange={e=>setRoom(e.target.value)}>{p.bid?<option>{p.room}</option>:free.map(r=><option key={r.id} value={r.id}>{r.id} · {r.type} · {money(r.rate)}/night</option>)}</select>
          {!free.length&&!p.bid&&<span className="gs-field-error">No room is vacant, ready and free for these dates. Try a shorter stay.</span>}</label>
        {!p.bid&&chosen&&<p className="muted"><BedDouble size={12}/> {nights(s.date,departure)||0} night{nights(s.date,departure)===1?'':'s'} · arrival today {shortDate(s.date)}</p>}
      </div>
      <GuestForm initial={emptyDraft()} registration disabled={!chosen&&!p.bid} submitLabel={p.bid?'Retry registration and check-in':'Create walk-in and check in'} onSubmit={d=>start(d)} onUseExisting={x=>start(draftOf(x),x.id)}
        secondary={<button type="button" className="btn" onClick={()=>open({type:'guest-checkin'})}>Has a reservation?</button>}/>
    </>}
  </>;
}
