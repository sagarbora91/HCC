// F6-H3 · modal 'hall-enquiry' (id = enquiry id, none = new): add / follow up an enquiry, check the hall on the calendar, soft hold (Manager+), mark lost, start the quotation.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {ModalFrame} from '../../components/fit';
import {Pill,Empty} from '../../components/ui';
import {hallSlice,HALL_EVENT_TYPES,HALL_SLOTS,type HallEventType,type HallSlot,type Enquiry} from '../../domain/streams/hall';
import {addDays} from '../../domain/money';
import {hallUi} from './access';
import {hallAvailability,EVENT_LABEL,SLOT_LABEL,SOURCE_LABEL,ENQ_TONE,dayLabel} from './logic';
import {Field} from './ui';

export function HallEnquiryModal({id}:ModalProps){
  const {s,uid}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  const q=id?x.enquiries.find(e=>e.id===id):undefined;
  if(!ui.enquiry)return <Empty title="Not available" body="Enquiries are kept by the Owner, Manager and Reception."/>;
  if(id&&!q)return <Empty title="This enquiry is unavailable"/>;
  return <Form key={q?.id??'new'} q={q} />;
}
function Form({q}:{q?:Enquiry}){
  const {s,uid,run,busy,open,closeModal}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  const closed=q&&(q.status==='WON'||q.status==='LOST');
  const [f,setF]=useState({name:q?.name??'',phone:q?.phone??'',eventType:(q?.eventType??'WEDDING') as HallEventType,date:q?.dates[0]??addDays(s.date,30),date2:q?.dates[1]??'',
    slot:(q?.slot??'EVENING') as HallSlot,pax:String(q?.pax??100),venueId:q?.venueId??'',source:(q?.source??'PHONE') as Enquiry['source'],followUp:q?.followUp??addDays(s.date,2),note:q?.note??''});
  const [lost,setLost]=useState({on:false,reasonId:x.lostReasons[0]?.id??'',note:''}),[hold,setHold]=useState({days:'3',venueId:q?.venueId||x.venues.find(v=>v.active)?.id||''});
  const set=(k:string,v:string)=>setF(o=>({...o,[k]:v}));
  const dates=[f.date,f.date2].filter(Boolean),venue=f.venueId||hold.venueId;
  const av=venue&&dates.length?hallAvailability(s,venue,dates,f.slot,Number(f.pax)||undefined):undefined;
  const payload={name:f.name,phone:f.phone,eventType:f.eventType,dates,slot:f.slot,pax:Number(f.pax),venueId:f.venueId||undefined,source:f.source,followUp:f.followUp,note:f.note};
  const save=()=>q?run({type:'HALL_ENQUIRY_UPDATE',id:q.id,followUp:f.followUp,note:f.note,pax:Number(f.pax),venueId:f.venueId,dates,slot:f.slot},`Enquiry ${q.id} saved`,{close:true})
    :run({type:'HALL_ENQUIRY_ADD',...payload},'Enquiry added',{close:true});
  const reason=x.lostReasons.find(r=>r.id===lost.reasonId);
  return <ModalFrame heading={<><h2>{q?`Enquiry ${q.id}`:'New hall enquiry'}</h2>{q&&<p>{q.name} · <Pill tone={ENQ_TONE[q.status]}>{q.status}</Pill>{q.softHold&&<> · <Pill tone="orange">Hold until {q.softHold.until.slice(0,10)}</Pill></>}</p>}</>}
    actions={<>
      <button type="button" className="btn" onClick={closeModal}>Close</button>
      {q&&!closed&&ui.quote&&<button type="button" className="btn" disabled={busy} onClick={()=>{const has=x.quotations.find(o=>o.enquiryId===q.id);open({type:'hall-quote',id:has?has.id:'new:'+q.id});}}>{x.quotations.some(o=>o.enquiryId===q.id)?'Open quotation':'Create quotation'}</button>}
      {q&&q.status==='LOST'&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'HALL_ENQUIRY_UPDATE',id:q.id,status:'OPEN'},'Enquiry reopened',{close:true})}>Reopen</button>}
      {!closed&&<button type="submit" form="hall-enq-form" className="btn primary" disabled={busy||!f.name.trim()||!f.phone.trim()}>{q?'Save':'Add enquiry'}</button>}</>}>
    <form id="hall-enq-form" className="form hall-form" onSubmit={e=>{e.preventDefault();void save();}}>
      <div className="hall-fields">
        <Field label="Customer name"><input value={f.name} disabled={!!q} maxLength={80} onChange={e=>set('name',e.target.value)} autoComplete="off"/></Field>
        <Field label="Phone"><input value={f.phone} disabled={!!q} inputMode="tel" maxLength={14} onChange={e=>set('phone',e.target.value)} autoComplete="off"/></Field>
        <Field label="Event"><select value={f.eventType} disabled={!!q} onChange={e=>set('eventType',e.target.value)}>{HALL_EVENT_TYPES.map(t=><option key={t} value={t}>{EVENT_LABEL[t]}</option>)}</select></Field>
        <Field label="Source"><select value={f.source} disabled={!!q} onChange={e=>set('source',e.target.value)}>{Object.entries(SOURCE_LABEL).map(([k,l])=><option key={k} value={k}>{l}</option>)}</select></Field>
        <Field label="Event date"><input type="date" value={f.date} min={s.date} disabled={!!closed} onChange={e=>set('date',e.target.value)}/></Field>
        <Field label="Second day (if any)"><input type="date" value={f.date2} min={f.date} disabled={!!closed} onChange={e=>set('date2',e.target.value)}/></Field>
        <Field label="Part of the day"><select value={f.slot} disabled={!!closed} onChange={e=>set('slot',e.target.value)}>{HALL_SLOTS.map(t=><option key={t} value={t}>{SLOT_LABEL[t]}</option>)}</select></Field>
        <Field label="Expected guests"><input inputMode="numeric" value={f.pax} disabled={!!closed} onChange={e=>set('pax',e.target.value.replace(/\D/g,''))}/></Field>
        <Field label="Hall (if known)"><select value={f.venueId} disabled={!!closed} onChange={e=>set('venueId',e.target.value)}><option value="">Not decided</option>{x.venues.filter(v=>v.active).map(v=><option key={v.id} value={v.id}>{v.name}</option>)}</select></Field>
        <Field label="Follow up on"><input type="date" value={f.followUp} min={s.date} disabled={!!closed} onChange={e=>set('followUp',e.target.value)}/></Field>
        <Field label="Note" wide><input value={f.note} maxLength={500} disabled={!!closed} onChange={e=>set('note',e.target.value)}/></Field>
      </div>
      {av&&<p className={'hall-avail'+(av.free?' ok':' bad')} role="status">{av.free?`${x.venues.find(v=>v.id===venue)?.name} is free on ${dates.map(dayLabel).join(', ')} (${SLOT_LABEL[f.slot]}).`
        :`Clash: ${av.clashes.map(c=>`${c.hall} on ${dayLabel(c.date)} is held by ${c.customer} (${c.eventId})`).join('; ')}.`}
        {av.soft.length>0&&` Also on enquiry hold: ${av.soft.map(o=>o.name).join(', ')}.`}{av.overCapacity&&` ${f.pax} guests is above the ${av.capacity} the hall seats.`}</p>}
    </form>
    {q&&!closed&&<section className="hall-box"><h3>Hold the hall</h3>
      {ui.softHold?<div className="inline"><Field label="Hall"><select value={hold.venueId} onChange={e=>setHold(o=>({...o,venueId:e.target.value}))}>{x.venues.filter(v=>v.active).map(v=><option key={v.id} value={v.id}>{v.name}</option>)}</select></Field>
        <Field label="Days (1–3)"><select value={hold.days} onChange={e=>setHold(o=>({...o,days:e.target.value}))}>{['1','2','3'].map(d=><option key={d}>{d}</option>)}</select></Field>
        <button type="button" className="btn" disabled={busy||!hold.venueId} onClick={()=>run({type:'HALL_ENQUIRY_SOFT_HOLD',id:q.id,venueId:hold.venueId,days:Number(hold.days)},'Hall held for the enquiry',{close:false})}>Hold</button>
        {q.softHold&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'HALL_ENQUIRY_SOFT_HOLD',id:q.id,release:true},'Hold released',{close:false})}>Release hold</button>}</div>
        :<p className="muted">A soft hold is placed by the Manager or Owner (up to 3 days). It warns other staff but does not block the hall.</p>}</section>}
    {q&&!closed&&<section className="hall-box"><h3>Mark lost</h3>
      {!lost.on?<button type="button" className="btn" onClick={()=>setLost(o=>({...o,on:true}))}>Customer is not going ahead</button>
        :<div className="inline"><Field label="Reason"><select value={lost.reasonId} onChange={e=>setLost(o=>({...o,reasonId:e.target.value}))}>{x.lostReasons.map(r=><option key={r.id} value={r.id}>{r.label}</option>)}</select></Field>
          <Field label={reason?.needsNote?'Note (needed)':'Note'}><input value={lost.note} maxLength={300} onChange={e=>setLost(o=>({...o,note:e.target.value}))}/></Field>
          <button type="button" className="btn" disabled={busy||(reason?.needsNote&&lost.note.trim().length<3)} onClick={()=>run({type:'HALL_ENQUIRY_LOST',id:q.id,reasonId:lost.reasonId,note:lost.note},'Enquiry marked lost',{close:true})}>Mark lost</button></div>}</section>}
    {q&&closed&&<p className="muted">{q.status==='LOST'?`Lost: ${x.lostReasons.find(r=>r.id===q.lostReasonId)?.label??''}${q.lostNote?' · '+q.lostNote:''}`:'Won: the quotation was accepted.'}</p>}
  </ModalFrame>;

}
