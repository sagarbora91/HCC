// F6-H3 · modal 'hall-event' (id = event id): the event-day screen — function sheet, advance plan and receipts, take an advance, block rooms, actual plates + extras → Close & bill,
// cancel with the slab preview, approve the refund, credit note on a printed invoice. Housekeeping / maintenance / supervisors see the function sheet without prices.
import React,{useState} from 'react';
import {Printer,Plus,Trash2,BedDouble} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {ModalFrame} from '../../components/fit';
import {Pill,Empty} from '../../components/ui';
import {money} from '../../domain/money';
import {hallSlice,hallDaysBefore,type HallEvent,type QuoteLine} from '../../domain/streams/hall';
import {hallCancelQuote,hallPaid,hallAdvancesDue} from '../../domain/streams/hall-billing';
import {grpSlice,grpRoomTypes} from '../../domain/streams/groups';
import {mnyNames,mnyMethod} from '../../domain/streams/money-master';
import {hallUi} from './access';
import {hallBeo,hallBillPreview,venueName,EVENT_TONE,EVENT_LABEL,SLOT_LABEL,dayLabel} from './logic';
import {Field,RupeeInput,toPaise} from './ui';

export function HallEventModal({id}:ModalProps){
  const {s,uid}=useHcc();const ui=hallUi(s,uid),e=hallSlice(s).events.find(o=>o.id===id);
  if(!ui.view)return <Empty title="Not available"/>;
  if(!e)return <Empty title="This event is unavailable"/>;
  return <Body key={e.id+':'+e.status} e={e}/>;
}

function Body({e}:{e:HallEvent}){
  const {s,uid,run,busy,open,closeModal}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  const beo=hallBeo(s,e,ui.seesMoney),confirmed=e.status==='CONFIRMED',methods=mnyNames(s,'frontdesk'),needsRef=(m:string)=>!!mnyMethod(s,m)?.needsReference; // CU-PAY-B: the master decides, not the literal "Cash"
  const [adv,setAdv]=useState({amount:'',method:methods[0]??'Cash',ref:''});
  const [pax,setPax]=useState(String(e.actualPax??e.guaranteedPax??0)),[extras,setExtras]=useState<{desc:string;qty:string;rate:string}[]>([]);
  const [settle,setSettle]=useState({mode:'TENDER',method:methods[0]??'Cash',ref:'',company:grpSlice(s).companies[0]?.id??''});
  const [cancelReason,setCancelReason]=useState(''),[creditReason,setCreditReason]=useState(''),[refund,setRefund]=useState({method:methods[0]??'Cash',ref:''});
  const [blk,setBlk]=useState<Record<string,string>>({});
  const extraLines:QuoteLine[]=extras.filter(r=>r.desc.trim().length>=2&&toPaise(r.rate)&&Number(r.qty)>0).map((r,i)=>({id:'X'+(i+1),kind:'EXTRA',desc:r.desc.trim(),qty:Number(r.qty),unit:'each',ratePaise:toPaise(r.rate)!,listRatePaise:toPaise(r.rate)!,discountPaise:0,gstClass:'SERVICE'}));
  const preview=ui.seesMoney&&confirmed?hallBillPreview(s,e,Number(pax)||0,extraLines):undefined;
  const cq=ui.seesMoney&&confirmed?hallCancelQuote(s,e,s.date):undefined;
  const paid=hallPaid(e),plan=e.advance??[],due=hallAdvancesDue(plan,e.date,s.date,30);
  const blockRow=e.blockId?grpSlice(s).blocks.find(b=>b.id===e.blockId):undefined;
  const canBillToday=s.date>=e.date;
  const doClose=()=>run({type:'HALL_EVENT_CLOSE',id:e.id,actualPax:Number(pax),extras:extraLines.map(l=>({kind:l.kind,desc:l.desc,qty:l.qty,ratePaise:l.ratePaise,unit:l.unit,gstClass:l.gstClass})),
    settle:{mode:settle.mode,method:settle.method,reference:settle.ref||undefined,company:settle.company}},'Event billed',{close:false});
  return <ModalFrame heading={<><h2>Event {e.id} · {beo.title}</h2><p>{beo.dates.map(dayLabel).join(', ')} · {beo.slot}{beo.time?' · '+beo.time:''} · {beo.venues.join(' + ')} <Pill tone={EVENT_TONE[e.status]}>{e.status==='CONFIRMED'?'Confirmed':e.status==='BILLED'?'Billed':'Cancelled'}</Pill></p></>}
    actions={<><button type="button" className="btn" onClick={closeModal}>Close</button>
      <button type="button" className="btn" onClick={()=>open({type:'hall-print',id:'beo:'+e.id})}><Printer size={15}/> Function sheet</button>
      {ui.seesMoney&&e.invoice&&<button type="button" className="btn" onClick={()=>open({type:'hall-print',id:'invoice:'+e.id})}><Printer size={15}/> Invoice</button>}
      {confirmed&&ui.bill&&ui.seesMoney&&<button type="button" className="btn primary" disabled={busy||!canBillToday||!pax.length} title={canBillToday?undefined:`The bill is made on or after ${dayLabel(e.date)}`} onClick={()=>{void doClose();}}>Close and bill</button>}</>}>
    <div className="hall-qbody">
      <section className="hall-box"><h3>Function sheet</h3>
        <dl className="hall-kv"><div><dt>Customer</dt><dd>{beo.customer}{beo.phone?' · '+beo.phone:''}</dd></div><div><dt>Event</dt><dd>{beo.eventType}</dd></div><div><dt>Set-up</dt><dd>{beo.setup??'—'}</dd></div>
          <div><dt>Guaranteed plates</dt><dd>{beo.guaranteedPax}</dd></div>{e.actualPax!==undefined&&<div><dt>Actual plates</dt><dd>{e.actualPax}</dd></div>}</dl>
        {beo.menu.length>0&&<><h4>Menu</h4><ul className="hall-list">{beo.menu.map((l,i)=><li key={i}>{l.desc} · {l.qty} {l.unit}</li>)}</ul></>}
        {beo.extras.length>0&&<><h4>Extras and services</h4><ul className="hall-list">{beo.extras.map((l,i)=><li key={i}>{l.desc} · {l.qty} {l.unit}</li>)}</ul></>}
        {beo.notes.map((n,i)=><p key={i} className="muted">{n}</p>)}
        {!ui.seesMoney&&<p className="muted hall-noprice" role="note">Prices are not shown to this role.</p>}</section>
      {ui.seesMoney&&<section className="hall-box"><h3>Advances and balance</h3>
        <div className="table-wrap"><table className="table"><thead><tr><th>Instalment</th><th>Due</th><th className="num">Amount</th><th className="num">Paid</th></tr></thead><tbody>
          {plan.map(r=>{const o=due.find(d=>d.id===r.id);return <tr key={r.id}><td>{r.label}{o?.overdue&&<> <Pill tone="red">Overdue</Pill></>}</td><td>{r.due==='ON_ACCEPT'?'On acceptance':r.due==='EVENT_DAY'?'Event day':dayLabel(r.due)}</td><td className="num">{money(r.amountPaise??0)}</td><td className="num">{money(r.paidPaise)}</td></tr>;})}
          {!plan.length&&<tr><td colSpan={4} className="muted">No payment plan on this event.</td></tr>}</tbody></table></div>
        <p>Advances received: <strong>{money(paid)}</strong>{e.invoice&&<> · invoice {money(e.invoice.totalPaise)} · balance {money(e.invoice.balancePaise)}</>}</p>
        {(e.receipts??[]).length>0&&<ul className="hall-list">{(e.receipts??[]).map(r=><li key={r.id}>{dayLabel(r.date)} · {r.kind==='ADVANCE'?'Advance':r.kind==='BALANCE'?'Balance':'Refund'} {money(r.amountPaise)} · {r.company?'company '+r.company:r.udhaar?'Udhaar':r.method}
          {r.kind!=='BALANCE'&&<> <button type="button" className="btn subtle" onClick={()=>open({type:'hall-print',id:`receipt:${e.id}:${r.id}`})}><Printer size={14}/> Receipt</button></>}</li>)}</ul>}
        {confirmed&&ui.advance&&<div className="inline hall-add"><Field label="Advance received (₹)"><input className="hall-rupee" inputMode="decimal" value={adv.amount} onChange={ev=>setAdv(o=>({...o,amount:ev.target.value}))}/></Field>
          <Field label="Paid by"><select value={adv.method} onChange={ev=>setAdv(o=>({...o,method:ev.target.value}))}>{methods.map(m=><option key={m}>{m}</option>)}</select></Field>
          {needsRef(adv.method)&&<Field label="Reference"><input value={adv.ref} onChange={ev=>setAdv(o=>({...o,ref:ev.target.value}))}/></Field>}
          <button type="button" className="btn primary" disabled={busy||!toPaise(adv.amount)} onClick={()=>run({type:'HALL_ADVANCE_RECEIVE',eventId:e.id,amountPaise:toPaise(adv.amount),method:adv.method,reference:adv.ref||undefined},'Advance recorded (no GST)',{close:false}).then(ok=>ok&&setAdv(o=>({...o,amount:'',ref:''})))}>Take advance</button></div>}</section>}
      {confirmed&&ui.accept&&<section className="hall-box"><h3><BedDouble size={16}/> Rooms for the guests</h3>
        {blockRow?<p>Room block <strong>{blockRow.id}</strong> ({Object.entries(blockRow.allot).map(([t,n])=>n+' '+t).join(', ')}) · {blockRow.arrival} to {blockRow.departure} · {blockRow.status}
          {blockRow.status==='Open'&&<> <button type="button" className="btn" disabled={busy} onClick={()=>run({type:'HALL_ROOM_BLOCK',eventId:e.id,release:true},'Room block released',{close:false})}>Release rooms</button></>}</p>
          :<div className="inline">{grpRoomTypes(s).map(t=><Field key={t} label={`${t} rooms`}><input className="hall-qty" inputMode="numeric" value={blk[t]??''} onChange={ev=>setBlk(o=>({...o,[t]:ev.target.value.replace(/\D/g,'')}))}/></Field>)}
            <button type="button" className="btn" disabled={busy||!Object.values(blk).some(v=>Number(v)>0)} onClick={()=>run({type:'HALL_ROOM_BLOCK',eventId:e.id,allot:Object.fromEntries(Object.entries(blk).filter(([,v])=>Number(v)>0).map(([k,v])=>[k,Number(v)]))},'Rooms blocked on the room board',{close:false})}>Block rooms</button></div>}
        <p className="muted">Held like a group block: other bookings cannot take these rooms until the cut-off.</p></section>}
      {confirmed&&ui.bill&&ui.seesMoney&&<section className="hall-box"><h3>Event day bill</h3>
        {!canBillToday&&<p className="muted">The bill can be made from {dayLabel(e.date)}; today is {dayLabel(s.date)}.</p>}
        <div className="hall-fields"><Field label={`Actual plates (guaranteed ${e.guaranteedPax??0}; the bill takes the higher)`}><input inputMode="numeric" value={pax} onChange={ev=>setPax(ev.target.value.replace(/\D/g,''))}/></Field></div>
        {extras.map((r,i)=><div className="inline hall-add" key={i}><Field label="Extra item (hours, damage …)"><input value={r.desc} onChange={ev=>setExtras(o=>o.map((v,j)=>j===i?{...v,desc:ev.target.value}:v))}/></Field>
          <Field label="Qty"><input className="hall-qty" inputMode="numeric" value={r.qty} onChange={ev=>setExtras(o=>o.map((v,j)=>j===i?{...v,qty:ev.target.value.replace(/\D/g,'')}:v))}/></Field>
          <Field label="Rate (₹)"><input className="hall-rupee" inputMode="decimal" value={r.rate} onChange={ev=>setExtras(o=>o.map((v,j)=>j===i?{...v,rate:ev.target.value}:v))}/></Field>
          <button type="button" className="icon-btn" aria-label="Remove extra" onClick={()=>setExtras(o=>o.filter((_,j)=>j!==i))}><Trash2 size={16}/></button></div>)}
        <button type="button" className="btn" onClick={()=>setExtras(o=>[...o,{desc:'',qty:'1',rate:''}])}><Plus size={15}/> Add extra item</button>
        {preview&&<dl className="hall-totals"><div><dt>Plates billed</dt><dd>{preview.billedPax}</dd></div><div><dt>Total before GST</dt><dd>{money(preview.totals.taxablePaise)}</dd></div><div><dt>CGST + SGST</dt><dd>{money(preview.totals.taxPaise)}</dd></div>
          <div className="grand"><dt>Invoice total</dt><dd>{money(preview.totals.totalPaise)}</dd></div><div><dt>Advances applied</dt><dd>{money(preview.appliedPaise)}</dd></div><div className="grand"><dt>Balance due</dt><dd>{money(preview.balancePaise)}</dd></div>
          {preview.leftPaise>0&&<div><dt>Advance left to refund</dt><dd>{money(preview.leftPaise)}</dd></div>}</dl>}
        {preview&&preview.balancePaise>0&&<div className="inline hall-add"><Field label="Settle the balance"><select value={settle.mode} onChange={ev=>setSettle(o=>({...o,mode:ev.target.value}))}>
            <option value="TENDER">Pay now</option><option value="DUE">Leave due</option>{ui.role!=='Cashier'&&<option value="COMPANY">Company credit</option>}{(ui.role==='Owner'||ui.role==='Manager')&&<option value="UDHAAR">Udhaar</option>}</select></Field>
          {settle.mode==='TENDER'&&<Field label="Paid by"><select value={settle.method} onChange={ev=>setSettle(o=>({...o,method:ev.target.value}))}>{methods.map(m=><option key={m}>{m}</option>)}</select></Field>}
          {settle.mode==='TENDER'&&needsRef(settle.method)&&<Field label="Reference"><input value={settle.ref} onChange={ev=>setSettle(o=>({...o,ref:ev.target.value}))}/></Field>}
          {settle.mode==='COMPANY'&&<Field label="Company account"><select value={settle.company} onChange={ev=>setSettle(o=>({...o,company:ev.target.value}))}>{grpSlice(s).companies.map(c=><option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>}</div>}</section>}
      {confirmed&&ui.cancel&&<section className="hall-box"><h3>Cancel the event</h3>
        {cq&&<p className="muted">{hallDaysBefore(s.date,e.date)} days before the event: the hotel keeps {cq.keptBps/100} % of the quotation ({money(cq.keptPaise)} of the {money(cq.paidPaise)} received, with GST); {money(cq.refundPaise)} is refunded.</p>}
        <div className="inline"><Field label="Reason"><input value={cancelReason} maxLength={200} onChange={ev=>setCancelReason(ev.target.value)}/></Field>
          <button type="button" className="btn" disabled={busy||cancelReason.trim().length<3} onClick={()=>run({type:'HALL_EVENT_CANCEL',id:e.id,reason:cancelReason},'Event cancelled, hall released')}>Cancel event</button></div>
        {ui.role==='Reception'&&<p className="muted">The refund is approved by the Manager or Owner.</p>}</section>}
      {e.status==='CANCELLED'&&e.cancellation&&ui.seesMoney&&<section className="hall-box"><h3>Cancellation</h3>
        <p>{dayLabel(e.cancellation.date)} · {e.cancellation.daysBefore} days before · kept {money(e.cancellation.keptPaise)} ({e.cancellation.keptBps/100} %) · refund {money(e.cancellation.refundPaise)} ({e.cancellation.refund.toLowerCase()}) · {e.cancellation.reason}</p>
        {e.cancellation.refund==='PENDING'&&ui.refund&&<div className="inline"><Field label="Refund by"><select value={refund.method} onChange={ev=>setRefund(o=>({...o,method:ev.target.value}))}>{methods.map(m=><option key={m}>{m}</option>)}</select></Field>
          {needsRef(refund.method)&&<Field label="Reference"><input value={refund.ref} onChange={ev=>setRefund(o=>({...o,ref:ev.target.value}))}/></Field>}
          <button type="button" className="btn primary" disabled={busy||e.cancellation.by===uid} title={e.cancellation.by===uid?'Another person approves the refund':undefined} onClick={()=>run({type:'HALL_REFUND',id:e.id,method:refund.method,reference:refund.ref||undefined},'Refund paid',{close:false})}>Approve and pay refund</button></div>}</section>}
      {e.status==='BILLED'&&e.invoice&&ui.seesMoney&&<section className="hall-box"><h3>Invoice {e.invoice.no}</h3>
        <p>{dayLabel(e.invoice.date)} · total {money(e.invoice.totalPaise)} · advances applied {money(e.invoice.advanceAppliedPaise)} · balance {money(e.invoice.balancePaise)} ({e.invoice.settle.mode.toLowerCase()})</p>
        {e.creditNote?<p className="hall-warn" role="status">Credit note {e.creditNote.no} on {dayLabel(e.creditNote.date)}: {e.creditNote.reason}</p>
          :ui.creditNote?<div className="inline"><Field label="Credit note: reason (cancels the whole invoice)"><input value={creditReason} maxLength={200} onChange={ev=>setCreditReason(ev.target.value)}/></Field>
            <button type="button" className="btn" disabled={busy||creditReason.trim().length<3} onClick={()=>run({type:'HALL_CREDIT_NOTE',id:e.id,reason:creditReason},'Credit note issued',{close:false})}>Issue credit note</button></div>
            :<p className="muted">A printed invoice is not edited. The Owner issues a credit note to correct it.</p>}</section>}
      {!!venueName(x,e.venueIds[0])&&<p className="muted">{EVENT_LABEL[e.eventType??'OTHER']} · {SLOT_LABEL[e.slot]} · created {e.createdAt?.slice(0,10)}</p>}
    </div>
  </ModalFrame>;
}
