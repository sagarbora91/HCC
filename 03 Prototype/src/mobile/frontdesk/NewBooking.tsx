// M05 New booking wizard (phone override of modal 'new'): Dates & party → Pick room → Guest details → Review (Hold / Confirm).
// Sends the same BOOK command as the desktop NewReservation (see logic.ts mfBookCommand). Props: room = preselected room, id = arrival date.
import {useStepBack} from '../../app/native';
import React,{useState} from 'react';
import {ArrowRight,ArrowLeft,Hourglass,Check,TriangleAlert,DoorOpen,Printer,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,nights,addDays,RES_CAPACITY,RES_SOURCES,RES_OTA_CHANNELS} from '../../services/mock';
import {useReservations,useAvailability,useBooking} from '../../clients/react';
import {quoteAndCreate,createRequestFromBook} from '../../clients/view';
import {Stepper,NumberStepper,ChipFilter} from '../../app/mobile/components';
import {shortDate,dateLabel,Empty} from '../../components/ui';
import {mfDraft,mfPick,mfWizardErrors,mfWizardSteps,mfBookCommand,mfRestriction,MfBookingDraft} from './logic';
import {RestrictionNotice} from '../../features/reservations/RestrictionNotice';
import {MfRows} from './shared';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const Err=({m}:{m?:string})=>m?<small className="mf-err" role="alert">{m}</small>:null;

export function MfNewBooking({room,id}:{room?:string;id?:string}){
  const {s,uid,modal,closeModal,booking,open,permission,setDirty}=useHcc();
  const {commit,busy:cbusy,readOnly}=useReservations();const busy=cbusy||readOnly;
  const [d,setD]=useState<MfBookingDraft>(()=>mfDraft(s,room,id));
  const [step,setStep]=useState(0);const [tried,setTried]=useState(-1);const [done,setDone]=useState('');
  useStepBack(step>0&&!done,()=>{setTried(-1);setStep(x=>Math.max(0,x-1));});
  const set=<K extends keyof MfBookingDraft>(k:K,v:MfBookingDraft[K])=>setD(x=>({...x,[k]:v}));
  const {view:av}=useAvailability({arrival:d.arrival,departure:d.departure,adults:d.people,children:d.children,roomTypeId:d.type==='All'?undefined:d.type});
  const {booking:saved}=useBooking(done);
  const errors=mfWizardErrors(s,step,d,uid,av);const shown=tried===step?errors:{};
  const rst=mfRestriction(s,d,uid,av);
  const p=mfPick(s,d,av);const n=p.valid?nights(d.arrival,d.departure):0;
  if(!permission('reservations'))return <Empty title="Not permitted" body="New bookings need the reservations permission."/>;
  const next=()=>{setTried(step);if(Object.keys(errors).length)return;setTried(-1);setDirty(true);setStep(step+1);document.querySelector('.m-sheet')?.scrollTo?.({top:0});};
  const back=()=>{setTried(-1);setStep(step-1);};
  const save=(hold:boolean)=>{setTried(3);if(Object.keys(mfWizardErrors(s,3,d,uid,av)).length)return;
    const cmd=mfBookCommand(s,d,hold,modal!.key,av);
    commit(c=>quoteAndCreate(c,{arrival:cmd.arrival,departure:cmd.departure,adults:cmd.people,children:cmd.children},createRequestFromBook(cmd),{idempotencyKey:modal!.key}),
      hold?'Room held for 2 hours of simulated time. Confirm it before the hold expires.':'Reservation confirmed.',r=>{setDirty(false);setDone(r.reservationId);});};

  if(done){const b=saved;
    if(!b)return <Empty title="Saved" body="The reservation was recorded. Find it in Reservations."/>;
    const canCheckin=b.status==='Confirmed'&&b.arrival<=s.date&&permission('checkin');
    return <><div className="modal-heading"><div className="eyebrow">NEW BOOKING · {b.publicNo}</div><h2>{b.status==='Held'?'Room held':'Reservation confirmed'}</h2><p>{b.guest.name} · Room {b.roomId} · {shortDate(b.arrival)} – {shortDate(b.departure)}</p></div>
      <div className="mf-success"><Check size={30}/><p>{b.status==='Held'?'The hold reserves the room for 2 hours of simulated time. Confirm it from the booking before it expires.':canCheckin?'Arriving today — continue straight to check-in.':'The guest can be checked in from Arrivals on the arrival date.'}</p></div>
      <div className="mf-body-actions"><button className="btn mf-wide" onClick={()=>open({type:'confirmation',id:b.reservationId})}><Printer size={16}/>Print confirmation</button>
        {canCheckin&&<button className="btn mf-wide" onClick={()=>booking(b.reservationId)}>Open booking</button>}</div>
      <div className="modal-actions">
        {canCheckin?<button className="btn primary" onClick={()=>open({type:'guest-checkin',id:b.reservationId})}><DoorOpen size={16}/>Check in now</button>
          :<button className="btn primary" onClick={()=>booking(b.reservationId)}>Open booking</button>}
      </div></>;}

  const chosenRate=p.chosen?p.available.find(r=>r.room.id===p.chosen!.id)?.ratePaise??0:0;
  const actions=<div className="modal-actions">
    {step===0?<button type="button" className="btn" onClick={closeModal}>Cancel</button>:<button type="button" className="btn" onClick={back}><ArrowLeft size={16}/>Back</button>}
    {step<3?<button type="button" className="btn primary" onClick={next}>Next <ArrowRight size={16}/></button>
      :<><button type="button" className="btn" disabled={busy||!rst.canSubmit} onClick={()=>save(true)}><Hourglass size={16}/>Hold 2 h</button>
        <button type="button" className="btn primary" disabled={busy||!rst.canSubmit} onClick={()=>save(false)}><Check size={16}/>Confirm</button></>}
  </div>;
  return <><div className="modal-heading"><div className="eyebrow">FRONT DESK · NEW BOOKING</div><h2>{mfWizardSteps[step]}</h2><p>Step {step+1} of 4</p></div>
    <Stepper steps={mfWizardSteps} current={step}/>
    <form className="form mf-form" noValidate onSubmit={e=>{e.preventDefault();e.stopPropagation();if(step<3)next();}}>
      {step===0&&<>
        <label>Arrival<input type="date" required min={s.date} value={d.arrival} enterKeyHint="next" onChange={e=>{const v=e.target.value;setD(x=>({...x,arrival:v,departure:DATE.test(v)&&x.departure<=v?addDays(v,1):x.departure}));}}/></label>
        <label>Departure<input type="date" required min={addDays(DATE.test(d.arrival)?d.arrival:s.date,1)} value={d.departure} enterKeyHint="next" onChange={e=>set('departure',e.target.value)}/></label>
        <Err m={shown.dates}/>
        {p.valid&&<p className="mf-count">{n} night{n===1?'':'s'} · {dateLabel(d.arrival)} – {dateLabel(d.departure)}</p>}
        <NumberStepper label="Adults" value={d.people} min={1} max={4} onChange={v=>set('people',v)}/>
        <NumberStepper label="Children" value={d.children} min={0} max={3} onChange={v=>set('children',v)}/>
        <div><span className="m-field-label">Room type</span>
          <ChipFilter label="Room type" value={d.type} onChange={v=>setD(x=>({...x,type:v,room:v!=='All'&&s.rooms.find(r=>r.id===x.room)?.type!==v?'':x.room}))} options={[{id:'All',label:'Any type'},...Object.entries(RES_CAPACITY).map(([t,c])=>({id:t,label:`${t} · sleeps ${c}`}))]}/></div>
        <Err m={shown.party}/>
      </>}
      {step===1&&<>
        <p className="mf-count"><strong>{p.available.length} available</strong> · {n} night{n===1?'':'s'} · party of {p.party}</p>
        {p.available.length?<div className="mf-options" role="radiogroup" aria-label="Available rooms">
          <label className={'mf-option'+(!d.room?' on':'')}><input type="radio" name="mf-room" checked={!d.room} onChange={()=>set('room','')}/><span><strong>Auto-assign</strong><small>First available{p.available[0]?' · room '+p.available[0].room.id:''}</small></span></label>
          {p.available.map(({room:r,capacity,ratePaise,occupiedNow})=><label key={r.id} className={'mf-option'+(d.room===r.id?' on':'')}><input type="radio" name="mf-room" checked={d.room===r.id} onChange={()=>set('room',r.id)}/>
            <span><strong>{r.id} · {r.type}</strong><small>Sleeps {capacity} · {money(ratePaise)}/night{occupiedNow?' · occupied now, free on your dates':''}</small></span></label>)}
        </div>:<Empty title="No availability" body="No room fits these dates and party size. Go back and change dates, guests or type."/>}
        {p.pickedExcluded&&<div className="info-note"><TriangleAlert size={16}/>Room {p.pickedExcluded.room.id} is not available: {p.pickedExcluded.reason}. Choose another room.</div>}
        <Err m={shown.room}/>
        {p.chosen&&<RestrictionNotice st={rst} on={!!d.rstOverride} reason={d.rstReason||''} onToggle={v=>set('rstOverride',v)} onReason={v=>set('rstReason',v)} subject={`room ${p.chosen.id} (${p.chosen.type})`}/>}
        <Err m={shown.restriction}/>
        {p.excluded.length>0&&<details className="mf-excluded"><summary>{p.excluded.length} room{p.excluded.length===1?'':'s'} excluded — see why</summary>
          <ul>{p.excluded.map(x=><li key={x.room.id}><strong>{x.room.id}</strong><span>{x.room.type}</span><em>{x.reasons.length>1?x.reason+' · '+x.reasons.slice(1).join(', ').toLowerCase():x.reason}</em></li>)}</ul></details>}
      </>}
      {step===2&&<>
        <label>Guest name *<input value={d.guest} maxLength={80} autoComplete="off" enterKeyHint="next" placeholder="e.g. Demo Guest" aria-invalid={!!shown.guest} onChange={e=>set('guest',e.target.value)}/><Err m={shown.guest}/></label>
        <label>Phone *<input type="tel" inputMode="tel" value={d.phone} maxLength={16} enterKeyHint="next" placeholder="+91 90000 00000" aria-invalid={!!shown.phone} onChange={e=>set('phone',e.target.value)}/><Err m={shown.phone}/></label>
        <label>Fictional email (optional)<input type="email" inputMode="email" value={d.email} enterKeyHint="next" placeholder="guest@example.test" onChange={e=>set('email',e.target.value)}/></label>
        <label>Booking source *<select value={d.source} onChange={e=>set('source',e.target.value)}>{RES_SOURCES.map(x=><option key={x}>{x}</option>)}</select></label>
        {d.source==='OTA'&&<label>OTA channel name *<input list="mf-ota" value={d.otaChannel} maxLength={60} enterKeyHint="next" aria-invalid={!!shown.otaChannel} onChange={e=>set('otaChannel',e.target.value)}/><datalist id="mf-ota">{RES_OTA_CHANNELS.map(x=><option key={x} value={x}/>)}</datalist><Err m={shown.otaChannel}/></label>}
        <label>Company (optional)<input value={d.company} maxLength={80} enterKeyHint="next" aria-invalid={!!shown.company} onChange={e=>set('company',e.target.value)}/><Err m={shown.company}/></label>
        <label>GSTIN (optional, fictional)<input value={d.gstin} maxLength={15} autoCapitalize="characters" enterKeyHint="next" placeholder="27AAAAA0000A1Z5" aria-invalid={!!shown.gstin} onChange={e=>set('gstin',e.target.value.toUpperCase())}/><Err m={shown.gstin}/></label>
        <label>Notes<textarea rows={2} value={d.notes} maxLength={300} enterKeyHint="done" onChange={e=>set('notes',e.target.value)}/></label>
      </>}
      {step===3&&<>
        <MfRows rows={[['Guest',d.guest],['Phone',d.phone],['Stay',`${shortDate(d.arrival)} – ${shortDate(d.departure)} · ${n} night${n===1?'':'s'}`],
          ['Party',`${d.people} adult${d.people===1?'':'s'}${d.children?` · ${d.children} child${d.children===1?'':'ren'}`:''}`],
          ['Room',p.chosen?`${p.chosen.id} · ${p.chosen.type}${d.room?'':' (auto-assigned)'}`:'—'],
          ['Tariff',p.chosen?`${money(chosenRate)} / night`:'—'],['Room charges',p.chosen?`${money(chosenRate*n)} + GST`:'—'],
          ['Source',d.source+(d.source==='OTA'&&d.otaChannel?' · '+d.otaChannel:'')],...(d.company?[['Company',d.company+(d.gstin?' · '+d.gstin:'')]] as [string,string][]:[]),
          ...(rst.overridden?[['Restriction',`Overridden: ${rst.restrictionOverride}`]] as [string,string][]:[])]}/>
        {Object.values(shown).map(m=><Err key={m} m={m}/>)}
        <div className="info-note"><Info size={16}/>A hold reserves the room for 2 hours of simulated time, then releases it. Availability is rechecked when you save. No message is sent (simulated).</div>
      </>}
      {actions}
    </form></>;
}
