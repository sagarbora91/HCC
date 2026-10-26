// M08 In-house stays (phone): list cards → 'stay-detail' sheet override with header card (room, dates, balance),
// room-segment timeline and an actions row. Move / extend / Leaves today use the guests stream's 'stay-move' sheet (conflicts + alternatives; STAY_EARLY_DEPARTURE).
import React from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {MoveRight,CalendarPlus,Printer,ArrowRight,UserRound,LogOut} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,nights,totals,guestForBooking,guestView,staySegments,staySegmentLabel,stayInHouse,plural} from '../../services/mock';
import {ListCard} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {mfInitials} from './logic';
import {MfSearch,MfStatus,useMfTop} from './shared';

export function MfInHouse(){
  useMfTop();
  const {s,open,search,setSearch,permission}=useHcc();const all=stayInHouse(s);
  const list=all.filter(b=>`${b.guest} ${b.id} ${b.room} ${staySegments(b).map(x=>x.room).join(' ')}`.toLowerCase().includes(search.toLowerCase()));
  const finance=permission('billing');
  return <div className="mf-page">
    <MfSearch value={search} onChange={setSearch} placeholder="Search guest, room or booking"/>
    <p className="mf-count">{all.length} stay{all.length===1?'':'s'} in house · business date {shortDate(s.date)}</p>
    {list.length?list.map(b=>{const due=b.departure<=s.date;
      return <ListCard key={b.id} title={<span className="mf-lc-guest"><span className="mf-room-no">{b.room}</span>{b.guest}</span>}
        sub={`${shortDate(b.arrival)} – ${shortDate(b.departure)}${staySegments(b).length>1?' · '+staySegmentLabel(b):''}`} meta={bookingRef(b)}
        right={<span className="mf-stack">{due?<Pill tone="orange">Departure due</Pill>:<Pill tone="blue">to {shortDate(b.departure)}</Pill>}{finance&&<small className="mf-bal">Bal {money(totals(b).balance)}</small>}</span>}
        onClick={()=>open({type:'stay-detail',id:b.id})}/>;})
      :<Empty title={all.length?'No matching stays':'No guests in house'} body={all.length?'Try another guest, room or booking.':'Check in an arrival from Arrivals.'}/>}
  </div>;
}

/** Modal 'stay-detail' (phone): id = booking ID. */
export function MfStayDetail({id}:{id?:string}){
  const {s,uid,open,permission,booking,closeModal}=useHcc();const b=s.bookings.find(b=>b.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const g=guestForBooking(s,b),v=g?guestView(s,uid,g):undefined,t=totals(b),segs=staySegments(b),finance=permission('billing');
  const left=b.status==='In house'?Math.max(0,nights(s.date,b.departure)):0;const inHouse=b.status==='In house';
  return <><div className="mf-stay-head">
      <div className="mf-stay-room"><span>ROOM</span><strong>{b.room}</strong></div>
      <div className="mf-stay-main"><div className="eyebrow">{bookingRef(b)}</div><h2>{b.guest}</h2>
        <p>{shortDate(b.arrival)} – {shortDate(b.departure)} · {plural(nights(b.arrival,b.departure),'night')}{inHouse?` · ${left} left`:''}</p>
        <MfStatus status={b.status}/></div>
      {finance&&<div className={'mf-stay-bal '+(t.balance>0?'due':'clear')}><span>{t.balance<0?'Credit':'Balance due'}</span><strong>{money(t.balance)}</strong>{t.deposit>0&&<small>Deposit held {money(t.deposit)}</small>}</div>}
    </div>
    {inHouse&&<div className="mf-actions-row">
      {permission('reservations')&&<><button className="btn" onClick={()=>open({type:'stay-move',id:b.id,room:'move'})}><MoveRight size={16}/>Move</button>
        <button className="btn" onClick={()=>open({type:'stay-move',id:b.id,room:'dates'})}><CalendarPlus size={16}/>Extend</button>
        {b.departure>s.date&&<button className="btn" onClick={()=>open({type:'stay-move',id:b.id,room:'today'})}><LogOut size={16}/>Leaves today</button>}</>}
      {permission('billing')&&<button className="btn" onClick={()=>open({type:'charge',id:b.id})}>Add charge</button>}
      {permission('checkout')&&<button className="btn" onClick={()=>open({type:'checkout',id:b.id})}>Checkout</button>}
    </div>}
    <h3 className="mf-sub">Room segments</h3>
    <ol className="mf-timeline">{segs.map((x,i)=><li key={i} className={x.to?'':'open'}><span className="mf-room-no">{x.room}</span>
      <span><strong>{shortDate(x.from)} – {x.to?shortDate(x.to):'now'}</strong><small>{x.to?(nights(x.from,x.to)?`${nights(x.from,x.to)} night${nights(x.from,x.to)>1?'s':''}`:'same-day move'):`current room · to ${shortDate(b.departure)}`}</small></span></li>)}</ol>
    {!segs.length&&<p className="mf-note">No room segments yet (not checked in).</p>}
    <h3 className="mf-sub">Occupants ({b.people})</h3>
    <ul className="mf-party"><li><strong><span className="mf-initials" aria-hidden>{mfInitials(v?.name||b.guest)}</span>{v?.name||b.guest}</strong><span>Primary guest{v?' · '+v.phone:''}</span></li>
      {g?.party.map((p,i)=><li key={i}><strong>{p.name}</strong><span>{p.ageBand}{p.relation?' · '+p.relation:''}</span></li>)}</ul>
    {finance&&<p className="mf-note">Agreed tariff {money(b.rate)}/night · charges {money(t.total)} · paid {money(t.paid)}</p>}
    <div className="mf-links">
      {(finance||permission('reservations'))&&<button className="text-btn" onClick={()=>booking(b.id)}>Open folio <ArrowRight size={14}/></button>}
      {g&&<button className="text-btn" onClick={()=>open({type:'guest-profile',id:g.id})}><UserRound size={14}/>Guest profile</button>}
      {b.registered&&<button className="text-btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={14}/>Registration card</button>}
    </div>
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button>
      {inHouse&&permission('checkout')&&b.departure<=s.date&&<button className="btn primary" onClick={()=>open({type:'checkout',id:b.id})}>Checkout</button>}</div></>;
}
