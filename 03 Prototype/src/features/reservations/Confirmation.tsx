// Stream A · printable booking confirmation (REQ-BOOK-008) and booking-dialog actions for holds.
import {printDocument} from '../../app/native';
import React from 'react';
import {Hotel,Printer,Hourglass,Info} from 'lucide-react';
import {Booking,money,nights,totals,resStatus,resHoldActive,resHoldLeftLabel} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,Pill,dateLabel} from '../../components/ui';
import {RestrictionOverrideNote} from './RestrictionNotice';

/** 'confirmation' modal (wide). Props: id = booking ID. */
export function BookingConfirmation({id}:{id?:string}){
  const {s,booking}=useHcc();const b=s.bookings.find(b=>b.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const room=s.rooms.find(r=>r.id===b.room),n=nights(b.arrival,b.departure),status=resStatus(s,b),t=totals(b);
  return <><div className="invoice res-confirmation" data-watermark="DEMONSTRATION — FICTIONAL">
    <div className="invoice-top"><Hotel size={34}/><div><h2>{s.settings.hotel}</h2><p>Hotel Control Centre · Fictional property</p></div></div>
    <div className="invoice-stamp">DEMONSTRATION — FICTIONAL · BOOKING CONFIRMATION · NOT A TAX INVOICE</div>
    <div className="invoice-meta"><div><small>GUEST</small><h3>{b.guest}</h3><p>{b.phone||'No phone recorded'}{b.email?' · '+b.email:''}</p>{b.company&&<p>{b.company.name}{b.company.gstin?' · GSTIN '+b.company.gstin+' (fictional)':''}</p>}</div>
      <div><small>{status==='Held'?'PROVISIONAL HOLD':'CONFIRMATION'}</small><h3>DEMO-{b.id}</h3><p>Status: {status}{b.status==='Held'&&b.holdUntil?` · hold until ${b.holdUntil.slice(11,16)} on ${dateLabel(b.holdUntil.slice(0,10))}`:''}</p></div></div>
    <div className="summary-list"><div><span>Arrival</span><strong>{dateLabel(b.arrival)} · from {s.settings.checkin}</strong></div><div><span>Departure</span><strong>{dateLabel(b.departure)} · by {s.settings.checkout}</strong></div>
      <div><span>Room</span><strong>{b.room}{room?' · '+room.type:''}</strong></div><div><span>Guests</span><strong>{b.people} adult{b.people===1?'':'s'}{b.children?` · ${b.children} child${b.children===1?'':'ren'}`:''}</strong></div>
      <div><span>Agreed nightly rate</span><strong>{money(b.rate)}</strong></div><div><span>Room charges ({n} night{n===1?'':'s'}, before synthetic tax)</span><strong>{money(b.rate*n)}</strong></div>
      <div><span>Advance received</span><strong>{money(t.paid)}</strong></div><div><span>Booking source</span><strong>{b.source}{b.otaChannel?' · '+b.otaChannel:''}</strong></div></div>
    {b.notes&&<p className="footnote">Notes: {b.notes}</p>}
    <p className="footnote">Demonstration document. All names, numbers and references are fictional. Synthetic tax applies; no GST compliance claim.</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to booking</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}

/** Buttons added to the booking dialog (registered as bookingActions). */
export function ResBookingActions({b}:{b:Booking}){
  const {s,act,run,open,permission}=useHcc();
  const status=resStatus(s,b),held=b.status==='Held'&&resHoldActive(s,b);
  return <>
    <RestrictionOverrideNote b={b}/>
    {held&&<><Pill tone="orange"><Hourglass size={11}/>Held · {resHoldLeftLabel(s,b)}</Pill>
      {act('reservations',()=>run({type:'CONFIRM',id:b.id,key:'confirm:'+b.id},'Hold confirmed. The reservation is now Confirmed.'),'Confirm hold','btn primary')}
      {act('reservations',()=>open({type:'move',id:b.id}),'Change dates / room','btn subtle')}
      {act('reservations',()=>open({type:'cancel',id:b.id}),'Cancel hold','btn subtle')}</>}
    {status==='Expired'&&<><Pill><Info size={11}/>Expired · {resHoldLeftLabel(s,b)} — room released</Pill>
      {permission('reservations')&&<button className="btn" onClick={()=>open({type:'new',room:b.room,id:b.arrival>=s.date?b.arrival:s.date})}>Book again</button>}</>}
    {(held||b.status==='Confirmed'||b.status==='In house')&&act('reservations',()=>open({type:'confirmation',id:b.id}),'Print confirmation','btn subtle')}
  </>;
}
