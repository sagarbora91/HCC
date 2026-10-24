// M03 Calendar (phone): agenda view — one room type at a time, swipeable 7-day strip, room cards for the chosen night.
// Free night → "Book" opens the booking wizard prefilled (room + arrival). No drag or long-press needed.
import React,{useState,useRef} from 'react';
import {ChevronLeft,ChevronRight,Plus,Lock,CalendarPlus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {addDays,resHoldLeftLabel} from '../../services/mock';
import {ChipFilter,Fab} from '../../app/mobile/components';
import {shortDate,dateLabel} from '../../components/ui';
import {mfRoomTypes,mfAgendaDay,mfAgendaStrip,mfInitials} from './logic';
import {MfStatus,useMfTop} from './shared';

const weekday=(d:string)=>new Date(d+'T12:00:00').toLocaleDateString('en-IN',{weekday:'short'});

export function MfCalendar(){
  useMfTop();
  const {s,open,booking,permission}=useHcc();
  const types=mfRoomTypes(s);
  const [type,setType]=useState(types[0]||'');
  const [start,setStart]=useState(s.date);const [day,setDay]=useState(s.date);
  const strip=mfAgendaStrip(s,start,type);
  const sel=strip.some(x=>x.date===day)?day:start;
  const rows=mfAgendaDay(s,sel,type);const canBook=permission('reservations');
  const shift=(n:number)=>{const ns=addDays(start,n);setStart(ns);setDay(ns);};
  // Horizontal swipe on the strip moves a week (buttons do the same).
  const touch=useRef<number|null>(null);
  const onEnd=(e:React.TouchEvent)=>{if(touch.current===null)return;const dx=e.changedTouches[0].clientX-touch.current;touch.current=null;if(Math.abs(dx)>60)shift(dx<0?7:-7);};
  return <div className="mf-page">
    <ChipFilter label="Room type" value={type} onChange={setType} options={types.map(t=>({id:t,label:t,count:s.rooms.filter(r=>r.type===t).length}))}/>
    <div className="mf-week-nav"><button className="btn" aria-label="Previous week" onClick={()=>shift(-7)}><ChevronLeft size={18}/></button>
      <span>{shortDate(start)} – {shortDate(addDays(start,6))}</span>
      <button className="btn" disabled={start===s.date} onClick={()=>{setStart(s.date);setDay(s.date);}}>Today</button>
      <button className="btn" aria-label="Next week" onClick={()=>shift(7)}><ChevronRight size={18}/></button></div>
    <div className="mf-strip" role="group" aria-label="Choose a night (swipe for another week)" onTouchStart={e=>{touch.current=e.touches[0].clientX;}} onTouchEnd={onEnd}>
      {strip.map(d=><button key={d.date} type="button" aria-pressed={d.date===sel} className={(d.date===sel?'on ':'')+(d.date===s.date?'today ':'')+(d.date<s.date?'past':'')} onClick={()=>setDay(d.date)}
        aria-label={`${dateLabel(d.date)}: ${d.free} free, ${d.booked} booked`}>
        <small>{weekday(d.date)}</small><strong>{d.date.slice(8)}</strong><em>{d.free} free</em></button>)}
    </div>
    <h2 className="m-group">{type.toUpperCase()} · NIGHT OF {dateLabel(sel).toUpperCase()}</h2>
    {rows.map(r=>r.kind==='stay'&&r.booking
      ?<button key={r.room.id} type="button" className={'mf-agenda mf-ag-'+(r.status||'').toLowerCase().replace(/\s/g,'')} onClick={()=>booking(r.booking!.id)}>
        <span className="mf-ag-room">{r.room.id}</span>
        <span className="mf-ag-main"><strong><span className="mf-initials" aria-hidden>{mfInitials(r.booking.guest)}</span>{r.booking.guest}</strong>
          <small>{shortDate(r.booking.arrival)} – {shortDate(r.booking.departure)}{r.arrives?' · arrives':''}{r.booking.status==='Held'?' · '+resHoldLeftLabel(s,r.booking):''}</small></span>
        <MfStatus status={r.status||''}/></button>
      :r.kind==='block'?<div key={r.room.id} className="mf-agenda mf-ag-block"><span className="mf-ag-room">{r.room.id}</span><span className="mf-ag-main"><strong><Lock size={14}/> Blocked</strong><small>{r.room.blockReason||'Out of service'}</small></span></div>
      :<div key={r.room.id} className="mf-agenda mf-ag-free"><span className="mf-ag-room">{r.room.id}</span><span className="mf-ag-main"><strong>Free</strong><small>{r.past?'Past night':'No booking this night'}</small></span>
        {canBook&&!r.past&&<button className="btn small-btn" onClick={()=>open({type:'new',room:r.room.id,id:sel})}><CalendarPlus size={15}/>Book</button>}</div>)}
    <p className="mf-note">A stay covers nights from arrival up to (not including) departure. Open a booking to move it or change dates.</p>
    {canBook&&<Fab label="New booking" icon={Plus} onClick={()=>open({type:'new',id:sel>=s.date?sel:undefined})}/>}
  </div>;
}
