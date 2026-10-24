// M06 Arrivals / check-in (phone): today's arrivals + Walk-in FAB; check-in as a 6-step stepper (one step per screen,
// sticky Next) overriding 'guest-checkin'; walk-in as a 3-screen flow overriding 'guest-walkin', with partial-failure recovery.
// Same commands as the desktop guests stream: REGISTER, MOVE, CHECKIN (check-in) and BOOK → REGISTER → CHECKIN (walk-in).
import {useStepBack} from '../../app/native';
import React,{useState,useRef} from 'react';
import {Check,ArrowRight,ArrowLeft,DoorOpen,AlertCircle,Printer,UserPlus,Info,Loader2,X,RotateCcw} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Booking,money,nights,addDays,totals,taxRateFor,guestForBooking,stayArrivals,stayCheckinAlternatives,stayFreeRooms,staySegments} from '../../services/mock';
import {Fab,ListCard,Stepper,NumberStepper} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {GuestForm,emptyDraft,draftOf,hashOf} from '../../features/guests/GuestForm';
import {GuestDetails} from '../../features/guests/GuestProfile';
import {mfCheckinState,mfCheckinSteps,mfInitials} from './logic';
import {MfRows,MfSearch,useMfTop} from './shared';
import {bookingIdLookup} from '../../domain/streams/customers';
import {statusTone} from '../../features/guests/GuestProfile';

/** Waits until React has re-rendered with the result of the last run() (the context's state is per render). */
function useSettle(){const renders=useRef(0);renders.current++;
  return async()=>{const start=renders.current;for(let i=0;i<50&&renders.current===start;i++)await new Promise(r=>setTimeout(r,20));};}

export function MfArrivals(){
  useMfTop();
  const {s,open,booking,permission,search,setSearch}=useHcc();
  const list=stayArrivals(s).filter(b=>`${b.guest} ${b.id} ${b.room}`.toLowerCase().includes(search.toLowerCase()));
  const found=bookingIdLookup(s,search).filter(b=>!list.some(x=>x.id===b.id));
  const today=s.bookings.filter(b=>b.status==='In house'&&staySegments(b)[0]?.from===s.date);
  const canWalkin=permission('reservations')&&permission('checkin');
  return <div className="mf-page">
    <MfSearch value={search} onChange={setSearch} placeholder="Search guest, room or booking ID"/>
    {found.length>0&&<><h2 className="m-group">BOOKING ID LOOKUP ({found.length})</h2>{found.map(b=><ListCard key={b.id} title={b.id} sub={`${b.guest} · room ${b.room} · ${shortDate(b.arrival)} – ${shortDate(b.departure)}`} meta={<Pill tone={statusTone(b.status)}>{b.status}</Pill>} onClick={()=>booking(b.id)}/>)}</>}
    <p className="mf-count">{list.length} awaiting check-in · from {s.settings.checkin}</p>
    {list.length?list.map(b=>{const st=mfCheckinState(s,b,false);
      return <ListCard key={b.id} title={<span className="mf-lc-guest"><span className="mf-initials" aria-hidden>{mfInitials(b.guest)}</span>{b.guest}</span>}
        sub={`Room ${b.room} · ${shortDate(b.arrival)} – ${shortDate(b.departure)}${b.arrival<s.date?' · overdue':''}`}
        meta={<span className="mf-inline">{b.registered?<Pill tone="green">✓ Verified</Pill>:<Pill tone="blue">Not registered</Pill>}{st.elig.ok?<Pill tone="green">Room ready</Pill>:<Pill tone="orange">Room not ready</Pill>}</span>}
        right={permission('checkin')?<span className="mf-go">{b.registered?'Continue':'Check in'}</span>:undefined}
        onClick={permission('checkin')?()=>open({type:'guest-checkin',id:b.id}):undefined}/>;})
      :<Empty title="No arrivals waiting" body="Everyone expected today has checked in. Use Walk-in for guests without a reservation."/>}
    {today.length>0&&<><h2 className="m-group">CHECKED IN TODAY ({today.length})</h2>
      {today.map(b=><ListCard key={b.id} title={b.guest} sub={`Room ${b.room} · ${b.id}`} onClick={()=>open({type:'stay-detail',id:b.id})}/>)}</>}
    {!permission('checkin')&&<p className="mf-note">Check-in needs the check-in permission.</p>}
    {canWalkin&&<Fab label="Walk-in" icon={UserPlus} onClick={()=>open({type:'guest-walkin'})}/>}
  </div>;
}

/** Modal 'guest-checkin' (phone): id = booking ID (optional — step 1 lists today's arrivals). */
export function MfCheckin({id}:{id?:string}){
  const c=useHcc();const {s,run,open,closeModal,go,permission,busy,modal}=c;
  const live=useRef(c);live.current=c;
  const [bid,setBid]=useState(id||'');const b=s.bookings.find(x=>x.id===bid);
  const [tariffOk,setTariffOk]=useState(false);const [editGuest,setEditGuest]=useState(false);const [q,setQ]=useState('');
  const [fail,setFail]=useState('');
  const st=mfCheckinState(s,b,tariffOk);
  const [step,setStep]=useState(()=>st.start);
  useStepBack(step>1,()=>{setStep(x=>Math.max(1,x-1));setFail('');});
  if(!permission('checkin'))return <Empty title="Not permitted" body="Check-in needs the check-in permission."/>;
  if(b&&!['Confirmed','In house'].includes(b.status))return <><div className="modal-heading"><h2>Check-in unavailable</h2><p>{b.guest} · {b.id} is {b.status}.</p></div><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  if(b&&b.arrival>s.date&&b.status==='Confirmed')return <><div className="modal-heading"><h2>Not arriving yet</h2><p>{b.guest} arrives {shortDate(b.arrival)}. Check-in opens on the arrival business date.</p></div><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const g=b?guestForBooking(s,b):undefined;const room=b?s.rooms.find(r=>r.id===b.room):undefined;
  const goStep=(n:number)=>{setStep(n);setFail('');document.querySelector('.m-sheet')?.scrollTo?.({top:0});};
  const nextBtn=<button type="button" className="btn primary" disabled={!st.done[step-1]&&step!==5} onClick={()=>goStep(step+1)}>Next <ArrowRight size={16}/></button>;
  const prevBtn=step>1?<button type="button" className="btn" onClick={()=>goStep(step-1)}><ArrowLeft size={16}/>Back</button>:<button type="button" className="btn" onClick={closeModal}>Cancel</button>;
  async function checkin(){
    if(!b)return;setFail('');
    const ok=await run({type:'CHECKIN',id:b.id,key:'checkin:'+b.id},`Checked in to room ${b.room}. Room board updated.`);await new Promise(r=>setTimeout(r,40));
    const now=live.current.s.bookings.find(x=>x.id===b.id)?.status;
    if(!ok&&now!=='In house')setFail(live.current.error||'The check-in was not recorded.');
  }
  let body:React.ReactNode=null;
  if(b?.status==='In house')body=<><div className="mf-success"><Check size={30}/><h3>{b.guest} is checked in to room {b.room}</h3><p>The room board and arrivals list are updated.</p></div>
    <div className="mf-body-actions"><button className="btn mf-wide" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={16}/>Print registration card</button><button className="btn mf-wide" onClick={()=>open({type:'stay-detail',id:b.id})}>Stay details</button></div>
    <div className="modal-actions"><button className="btn primary" onClick={closeModal}>Done</button></div></>;
  else if(step===1){const list=stayArrivals(s).filter(x=>`${x.guest} ${x.id} ${x.room}`.toLowerCase().includes(q.toLowerCase()));
    body=<><MfSearch value={q} onChange={setQ} placeholder="Search today’s arrivals"/>
      <div className="mf-options" role="radiogroup" aria-label="Today's arrivals">{list.map(x=><label key={x.id} className={'mf-option'+(x.id===bid?' on':'')}><input type="radio" name="mf-arr" checked={x.id===bid} onChange={()=>{setBid(x.id);setTariffOk(false);}}/>
        <span><strong>{x.guest}</strong><small>{x.id} · room {x.room} · {shortDate(x.arrival)} – {shortDate(x.departure)}{x.registered?' · verified':''}</small></span></label>)}</div>
      {!list.length&&<Empty title="No matching arrivals" body="Use Walk-in for a guest without a reservation."/>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'guest-walkin'})}><UserPlus size={16}/>Walk-in</button>{nextBtn}</div></>;}
  else if(b&&step===2){
    const register=(d:ReturnType<typeof emptyDraft>,gid?:string)=>run({type:'REGISTER',id:b.id,guestId:gid??g?.id,guest:d,key:`${modal!.key}:register:${hashOf([d,gid])}`},'Guest verified. Registration saved.',{after:()=>{setEditGuest(false);goStep(3);}});
    body=st.regDone&&!editGuest&&g?<><GuestDetails g={g}/>{g.origin==='auto'&&<div className="info-note"><Info size={16}/>Minimal demo profile created from the booking name. Add fictional details with Edit.</div>}
        <div className="modal-actions">{prevBtn}<button type="button" className="btn" onClick={()=>setEditGuest(true)}>Edit</button>{nextBtn}</div></>
      :<div className="mf-guestform"><GuestForm key={g?.id||'new'} initial={g&&g.origin!=='auto'?draftOf(g):emptyDraft({name:b.guest.replace(/\s*\(fictional\)\s*/,''),phone:b.phone||'',email:b.email,party:g?.party})} guestId={g?.id} registration
        submitLabel={st.regDone?'Save & next':'Verify guest'} onSubmit={d=>register(d)} onUseExisting={x=>register(draftOf(x),x.id)}
        secondary={<>{prevBtn}{st.regDone&&<button type="button" className="btn" onClick={()=>setEditGuest(false)}>Cancel edit</button>}</>}/></div>;
  }
  else if(b&&step===3){const alts=stayCheckinAlternatives(s,b);
    body=<>{st.elig.ok?<div className="mf-ok"><Check size={18}/><div><strong>Room {b.room} · {room?.type} is vacant, ready and unblocked.</strong><p>Free for {shortDate(b.arrival)} – {shortDate(b.departure)}.</p></div></div>
      :<><div className="mf-deny" role="alert"><AlertCircle size={18}/><div><strong>Room {b.room} cannot be used now.</strong><ul>{st.elig.reasons.map(r=><li key={r}>{r}</li>)}</ul></div></div>
        <h3 className="mf-sub">Eligible {room?.type} rooms</h3>
        {alts.length?alts.map(r=><div key={r.id} className="mf-agenda mf-ag-free"><span className="mf-ag-room">{r.id}</span><span className="mf-ag-main"><strong>{r.type} · floor {r.floor}</strong><small>Ready · vacant · unblocked</small></span>
          {permission('reservations')?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'MOVE',id:b.id,room:r.id,departure:b.departure,key:`${modal!.key}:move:${r.id}`},`Allocation changed to room ${r.id}. Agreed tariff kept.`)}>Use {r.id}</button>:null}</div>)
          :<Empty title={`No eligible ${room?.type||''} rooms`} body="Ask housekeeping to finish and inspect a room."/>}
        <p className="mf-note">Switching re-checks availability; the agreed tariff ({money(b.rate)}/night) is kept.</p></>}
      <div className="modal-actions">{prevBtn}{nextBtn}</div></>;}
  else if(b&&step===4){const n=nights(b.arrival,b.departure),bps=taxRateFor(s,b.arrival),net=b.rate*n;
    body=<><MfRows rows={[['Agreed tariff',money(b.rate)+' / night'],['Nights',`${n} (${shortDate(b.arrival)} – ${shortDate(b.departure)})`],['Room charges (estimate)',money(net)],[`Synthetic tax (${bps/100}%)`,money(Math.round(net*bps/10000))],['Source',b.source]]}/>
      <label className="mf-check"><input type="checkbox" checked={tariffOk} onChange={e=>setTariffOk(e.target.checked)}/><span>The guest has confirmed the tariff and stay dates.</span></label>
      <div className="modal-actions">{prevBtn}{nextBtn}</div></>;}
  else if(b&&step===5){const t=totals(b);
    body=<><MfRows rows={[['Advance received',money(t.paid)],['Deposit held',money(t.deposit)]]}/>
      <div className="info-note"><Info size={16}/>Optional. Recording a payment opens the payment sheet; reopen this check-in from Arrivals afterwards — completed steps are remembered.</div>
      {permission('payments')&&<button type="button" className="btn mf-wide" onClick={()=>open({type:'payment',id:b.id})}>Record advance / deposit</button>}
      <div className="modal-actions">{prevBtn}<button type="button" className="btn primary" onClick={()=>goStep(6)}>{t.paid||t.deposit?'Next':'Skip'} <ArrowRight size={16}/></button></div></>;}
  else if(b&&step===6){
    body=<><MfRows rows={[['Guest',b.guest],['Room',`${b.room} · ${room?.type||''}`],['Stay',`${shortDate(b.arrival)} – ${shortDate(b.departure)}`],['Tariff',money(b.rate)+' / night']]}/>
      {st.missing.length>0&&<div className="mf-deny" role="alert"><AlertCircle size={18}/><div><strong>Complete these steps first:</strong> {st.missing.join(', ')}.</div></div>}
      {fail&&<div className="mf-recover" role="alert"><AlertCircle size={18}/><div><strong>Check-in was not completed — nothing was changed.</strong><p>{fail}</p>
        <div className="mf-inline"><button className="btn small-btn primary" disabled={busy} onClick={checkin}><RotateCcw size={15}/>Retry</button>
          <button className="btn small-btn" onClick={()=>goStep(3)}>Choose another room</button>
          <button className="btn small-btn" onClick={()=>{closeModal();go('arrivals');}}>Back to arrivals</button></div></div></div>}
      <div className="modal-actions">{prevBtn}<button type="button" className="btn primary" disabled={busy||st.missing.length>0} onClick={checkin}><DoorOpen size={16}/>Check in</button></div></>;}
  return <><div className="modal-heading"><div className="eyebrow">CHECK-IN{b?' · '+b.id:''}</div><h2>{b?b.guest:'Find the reservation'}</h2>{b&&<p>Room {b.room} · {shortDate(b.arrival)} – {shortDate(b.departure)} · {b.people} guest{b.people>1?'s':''}</p>}</div>
    {b?.status!=='In house'&&<Stepper steps={mfCheckinSteps} current={step-1}/>}
    {b?.status!=='In house'&&<h3 className="mf-sub">Step {step} of 6 · {mfCheckinSteps[step-1]}</h3>}
    {body}</>;
}

type St='pending'|'running'|'done'|'failed';
/** Modal 'guest-walkin' (phone): Stay → Guest → Progress. BOOK, REGISTER, CHECKIN are separate recorded steps; failures are recoverable. */
export function MfWalkin(){
  const c=useHcc();const {s,open,closeModal,permission,busy,modal}=c;
  const live=useRef(c);live.current=c;const settle=useSettle();
  const [screen,setScreen]=useState(0);
  const [departure,setDeparture]=useState(addDays(s.date,1));const [people,setPeople]=useState(1);
  const free=departure>s.date?stayFreeRooms(s,s.date,departure):[];
  const [room,setRoom]=useState('');const chosen=free.some(r=>r.id===room)?room:free[0]?.id||'';
  const [p,setP]=useState<{book:St;register:St;checkin:St;bid?:string;room?:string;msg?:string;note?:string}>({book:'pending',register:'pending',checkin:'pending'});
  if(!permission('reservations')||!permission('checkin'))return <Empty title="Not permitted" body="A walk-in needs both reservation and check-in permissions."/>;
  const key=modal!.key;
  async function doCheckin(bid:string){
    setP(x=>({...x,checkin:'running',msg:undefined}));await settle();
    const ok=await live.current.run({type:'CHECKIN',id:bid,key:key+':checkin'},'Walk-in checked in.');await settle();
    const now=live.current.s.bookings.find(b=>b.id===bid)?.status;
    if(ok||now==='In house')setP(x=>({...x,checkin:'done',note:ok?x.note:'The confirmation was lost, but the record shows the guest checked in.'}));else setP(x=>({...x,checkin:'failed',msg:live.current.error}));
  }
  async function start(d:ReturnType<typeof emptyDraft>,gid?:string){
    setScreen(2);let bid=p.bid;
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
  const icon=(st:St)=>st==='done'?<Check size={16}/>:st==='running'?<Loader2 size={16} className="mf-spin"/>:st==='failed'?<X size={16}/>:<span className="mf-dot"/>;
  const label=(st:St)=>({pending:'Waiting',running:'In progress',done:'Done',failed:'Failed'})[st];
  const b=p.bid?s.bookings.find(x=>x.id===p.bid):undefined;
  const failed=p.book==='failed'||p.register==='failed'||p.checkin==='failed';
  const steps=['Stay','Guest','Check in'];
  return <><div className="modal-heading"><div className="eyebrow">ARRIVALS · WALK-IN</div><h2>Walk-in guest</h2><p>Creates a reservation, registers the guest and checks in — three recorded steps.</p></div>
    <Stepper steps={steps} current={screen}/>
    {screen===0&&<><div className="form mf-form">
        <label>Departure<input type="date" value={departure} min={addDays(s.date,1)} max={addDays(s.date,30)} onChange={e=>setDeparture(e.target.value)}/></label>
        <NumberStepper label="Guests" value={people} min={1} max={4} onChange={setPeople}/>
        <p className="mf-count">{nights(s.date,departure)>0?`${nights(s.date,departure)} night${nights(s.date,departure)===1?'':'s'} · arrival today ${shortDate(s.date)}`:'Choose a departure after today.'}</p>
      </div>
      <h3 className="mf-sub">Room — vacant, ready and free until departure</h3>
      {free.length?<div className="mf-options" role="radiogroup" aria-label="Room">{free.map(r=><label key={r.id} className={'mf-option'+(chosen===r.id?' on':'')}><input type="radio" name="mf-walk-room" checked={chosen===r.id} onChange={()=>setRoom(r.id)}/>
          <span><strong>{r.id} · {r.type}</strong><small>Floor {r.floor}{permission('billing')?` · ${money(r.rate)}/night`:''}</small></span></label>)}</div>
        :<Empty title="No room available" body="No room is vacant, ready and free for these dates. Try a shorter stay."/>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'guest-checkin'})}>Has a reservation?</button>
        <button type="button" className="btn primary" disabled={!chosen} onClick={()=>setScreen(1)}>Next <ArrowRight size={16}/></button></div></>}
    {screen===1&&<div className="mf-guestform"><GuestForm initial={emptyDraft()} registration disabled={!chosen&&!p.bid} submitLabel={p.bid?'Retry':'Create & check in'} onSubmit={d=>start(d)} onUseExisting={x=>start(draftOf(x),x.id)}
      secondary={!p.bid?<button type="button" className="btn" onClick={()=>setScreen(0)}><ArrowLeft size={16}/>Back</button>:<button type="button" className="btn" onClick={()=>setScreen(2)}>Progress</button>}/></div>}
    {screen===2&&<><ol className="mf-seq" aria-live="polite">
        {([['book','Create reservation'],['register','Register guest'],['checkin','Check in']] as const).map(([k,l])=><li key={k} className={'mf-seq-'+p[k]}>{icon(p[k])}<span>{l}</span><strong>{label(p[k])}</strong></li>)}</ol>
      {p.note&&<div className="info-note"><Info size={16}/>{p.note}</div>}
      {failed&&<div className="mf-recover" role="alert"><AlertCircle size={18}/><div>
        {p.book==='failed'&&<><strong>No reservation was created.</strong><p>{p.msg} Go back, correct the details and try again.</p></>}
        {p.register==='failed'&&<><strong>Reservation {p.bid} was created for room {p.room}, but registration was not saved.</strong><p>{p.msg} Correct the guest details and retry, continue in guided check-in, or cancel the reservation.</p></>}
        {p.checkin==='failed'&&<><strong>Reservation {p.bid} is registered but not checked in.</strong><p>{p.msg} Retry, or continue in guided check-in to pick another eligible room.</p></>}
        <div className="mf-inline">
          {(p.book==='failed'||p.register==='failed')&&<button className="btn small-btn primary" onClick={()=>setScreen(p.book==='failed'?0:1)}>Correct details</button>}
          {p.checkin==='failed'&&<button className="btn small-btn primary" disabled={busy} onClick={()=>doCheckin(p.bid!)}><RotateCcw size={15}/>Retry check-in</button>}
          {p.bid&&<button className="btn small-btn" onClick={()=>open({type:'guest-checkin',id:p.bid})}>Guided check-in</button>}
          {b?.status==='Confirmed'&&<button className="btn small-btn danger" disabled={busy} onClick={()=>c.run({type:'CANCEL',id:p.bid!,key:key+':cancel'},`Walk-in reservation ${p.bid} cancelled.`,{close:true})}>Cancel {p.bid}</button>}
        </div></div></div>}
      {p.checkin==='done'&&b&&<div className="mf-success"><Check size={30}/><h3>{b.guest} is checked in to room {b.room}</h3></div>}
      {p.checkin==='done'&&b?<><div className="mf-body-actions"><button className="btn mf-wide" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={16}/>Print registration card</button>
          {permission('payments')&&<button className="btn mf-wide" onClick={()=>open({type:'payment',id:b.id})}>Record advance / deposit</button>}</div><div className="modal-actions"><button className="btn primary" onClick={closeModal}>Done</button></div></>
        :!failed&&<p className="mf-note"><Loader2 size={14} className="mf-spin"/> Working… each step is recorded separately.</p>}</>}
  </>;
}
