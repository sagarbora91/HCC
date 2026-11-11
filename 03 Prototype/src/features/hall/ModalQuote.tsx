// F6-H3 · modal 'hall-quote' (id = quotation id, or 'new:<enquiry id>' before the first save): the quotation editor (header, lines, totals, advance plan, T&C preview, version
// strip, status pill) with Save / Send / Revise / Accept / Mark lost / Print. Editing lines is desktop-only: the phone shows the summary with Send / Accept (spec §17).
// Commands are H2's QUO_CREATE / QUO_EDIT / QUO_REVISE / QUO_SEND / QUO_LOST / QUO_CANCEL / QUO_PLACEHOLDER_OVERRIDE and QUO_DISCOUNT_APPROVE and H1's QUO_ACCEPT (payloads reconciled with handlers/hall-quote.ts by F6-int).
import React,{useState} from 'react';
import {Printer,Send,FilePlus2,CheckCircle2,XCircle,Trash2,Plus} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {ModalFrame} from '../../components/fit';
import {Pill,Empty} from '../../components/ui';
import {money} from '../../domain/money';
import {gstPct} from '../../domain/gst';
import {hallSlice,hallLatest,HALL_EVENT_TYPES,HALL_SLOTS,type HallSetup,type HallEventType,type HallSlot,type Quotation,type QuoteVersion} from '../../domain/streams/hall';
import {hallPrice,hallLineTaxable} from '../../domain/streams/hall-billing';
import {mnyNames,mnyMethod} from '../../domain/streams/money-master';
import {hallUi} from './access';
import {hallDraftFromEnquiry,hallDraftFromVersion,hallSyncLines,hallAddMenu,hallAddExtra,hallSetRate,hallSetQty,hallRemoveLine,hallDraftPlan,hallDiscountState,hallTncFor,hallAvailability,
  EVENT_LABEL,SLOT_LABEL,SETUP_LABEL,QUOTE_TONE,dayLabel,type QuoteDraft} from './logic';
import {Field,RupeeInput,toPaise} from './ui';

export function HallQuoteModal({id}:ModalProps){
  const {s,uid}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  if(!ui.view||!ui.seesMoney)return <Empty title="Not available" body="Quotations are shown to the Owner, Manager, Reception and Accountant."/>;
  const enqId=id?.startsWith('new:')?id.slice(4):undefined;
  const q=enqId?x.quotations.find(o=>o.enquiryId===enqId):x.quotations.find(o=>o.id===id);
  const enq=x.enquiries.find(e=>e.id===(enqId??q?.enquiryId));
  if(!q&&!enq)return <Empty title="This quotation is unavailable"/>;
  return <Editor key={(q?.id??'new')+':'+(q?.currentVersion??0)} q={q} enqId={enq?.id??''}/>;
}

function Editor({q,enqId}:{q?:Quotation;enqId:string}){
  const {s,uid,run,busy,open,closeModal,display}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  const enq=x.enquiries.find(e=>e.id===enqId)!;
  const [shown,setShown]=useState(q?.currentVersion??0);
  const v:QuoteVersion|undefined=q?.versions.find(o=>o.v===shown);
  const latest=q?hallLatest(q):undefined;
  const editable=!!ui.quote&&!display.isMobile&&(!q||(shown===q.currentVersion&&latest?.status==='DRAFT'));
  const [d,setD]=useState<QuoteDraft>(()=>q&&latest?hallDraftFromVersion(q,latest):hallDraftFromEnquiry(s,enq));
  const view:QuoteDraft=q&&v&&!editable?hallDraftFromVersion(q,v):d;
  const priced=hallPrice(s,view.lines,s.date),t=priced.totals,plan=hallDraftPlan(s,view),disc=hallDiscountState(s,uid,view.lines);
  const tnc=q&&v?v.tnc:hallTncFor(s,view.header.eventType,s.date,view.tncTemplateId);
  const av=view.header.venueId?hallAvailability(s,view.header.venueId,view.header.dates,view.header.slot,view.header.pax,view.header.setup):undefined;
  const venue=x.venues.find(o=>o.id===view.header.venueId);
  const [extra,setExtra]=useState({desc:'',qty:'1',rate:''}),[menuId,setMenuId]=useState(''),[panel,setPanel]=useState<''|'accept'|'lost'|'override'>('');
  const [acc,setAcc]=useState({amount:'',method:mnyNames(s,'frontdesk')[0]??'Cash',ref:'',reason:'',capReason:''}),[lost,setLost]=useState({reasonId:x.lostReasons[0]?.id??'',note:''}),[ovr,setOvr]=useState('');
  const setH=(patch:Partial<QuoteDraft['header']>)=>setD(o=>hallSyncLines(s,{...o,header:{...o.header,...patch}}));
  const boss=ui.role==='Owner'||ui.role==='Manager';
  const payload=()=>({header:d.header,lines:d.lines,advance:boss&&d.advance.length?d.advance.map(r=>({label:r.label,due:r.due,...(r.pctBps!==undefined?{pctBps:r.pctBps}:r.amountPaise!==undefined?{amountPaise:r.amountPaise}:{})})):undefined});
  const save=()=>q?run({type:'QUO_EDIT',id:q.id,...payload()},'Quotation saved',{close:false}):run({type:'QUO_CREATE',enquiryId:enqId,...payload()},'Quotation created',{close:false});
  const send=async()=>{if(editable&&!(await save()))return;if(q)await run({type:'QUO_SEND',id:q.id},`Quotation ${q.number} sent`,{close:false});};
  const mut=x.menus.filter(m=>m.active),status=v?.status??'DRAFT';
  const sent=status==='SENT'||status==='EXPIRED';
  const canAccept=!!q&&ui.accept&&shown===q.currentVersion&&sent;
  return <ModalFrame heading={<><h2>{q?`Quotation ${q.number}`:'New quotation'}{q&&<> · v{shown}</>}</h2>
      <p>{enq.name} · {enq.phone} · enquiry {enq.id} <Pill tone={QUOTE_TONE[status]}>{status}</Pill>{status==='SENT'&&v&&<> · valid until {dayLabel(v.validUntil)}</>}</p></>}
    actions={<>
      <button type="button" className="btn" onClick={closeModal}>Close</button>
      {q&&<button type="button" className="btn" onClick={()=>open({type:'hall-print',id:'quote:'+q.id+':'+shown})}><Printer size={15}/> Print</button>}
      {q&&ui.quote&&shown===q.currentVersion&&(status==='SENT'||status==='EXPIRED')&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'QUO_REVISE',id:q.id},'New version created',{close:false})}><FilePlus2 size={15}/> Revise</button>}
      {q&&ui.quote&&sent&&shown===q.currentVersion&&<button type="button" className="btn" onClick={()=>setPanel(panel==='lost'?'':'lost')}><XCircle size={15}/> Mark lost</button>}
      {canAccept&&<button type="button" className="btn primary" onClick={()=>setPanel(panel==='accept'?'':'accept')}><CheckCircle2 size={15}/> Accept</button>}
      {ui.quote&&(editable||(!!q&&status==='DRAFT'))&&<button type="button" className="btn" disabled={busy||!d.header.venueId||!d.lines.length} onClick={()=>{void save();}}>Save draft</button>}
      {ui.quote&&status==='DRAFT'&&(display.isMobile?!!q:true)&&<button type="button" className="btn primary" disabled={busy||(!q&&(!d.header.venueId||!d.lines.length))||(!!tnc?.hasPlaceholder&&!v?.placeholderOverride)} onClick={()=>{void send();}}><Send size={15}/> Send</button>}</>}>
    <div className="hall-qbody">
      {q&&q.versions.length>1&&<div className="hall-versions" role="group" aria-label="Versions">{q.versions.map(o=><button key={o.v} type="button" className={'trc-chip'+(o.v===shown?' on':'')} aria-pressed={o.v===shown} onClick={()=>setShown(o.v)}>
        <strong>v{o.v}</strong><Pill tone={QUOTE_TONE[o.status]}>{o.status}</Pill></button>)}</div>}
      <section className="hall-box"><h3>Event</h3><div className="hall-fields">
        <Field label="Event"><select disabled={!editable} value={view.header.eventType} onChange={e=>setH({eventType:e.target.value as HallEventType})}>{HALL_EVENT_TYPES.map(t=><option key={t} value={t}>{EVENT_LABEL[t]}</option>)}</select></Field>
        <Field label="Hall"><select disabled={!editable} value={view.header.venueId} onChange={e=>setH({venueId:e.target.value})}><option value="">Choose…</option>{x.venues.filter(o=>o.active).map(o=><option key={o.id} value={o.id}>{o.name}</option>)}</select></Field>
        <Field label="Date"><input type="date" disabled={!editable} min={s.date} value={view.header.dates[0]??''} onChange={e=>setH({dates:[e.target.value,...view.header.dates.slice(1)].filter(Boolean)})}/></Field>
        <Field label="Second day"><input type="date" disabled={!editable} min={view.header.dates[0]} value={view.header.dates[1]??''} onChange={e=>setH({dates:[view.header.dates[0],e.target.value].filter(Boolean)})}/></Field>
        <Field label="Part of the day"><select disabled={!editable} value={view.header.slot} onChange={e=>setH({slot:e.target.value as HallSlot})}>{HALL_SLOTS.map(o=><option key={o} value={o}>{SLOT_LABEL[o]}</option>)}</select></Field>
        <Field label="Set-up"><select disabled={!editable} value={view.header.setup} onChange={e=>setH({setup:e.target.value as HallSetup})}>{Object.entries(SETUP_LABEL).map(([k,l])=><option key={k} value={k}>{l}</option>)}</select></Field>
        <Field label="Expected guests"><input disabled={!editable} inputMode="numeric" value={view.header.pax} onChange={e=>setH({pax:Number(e.target.value.replace(/\D/g,''))||0})}/></Field>
        <Field label="Guaranteed plates"><input disabled={!editable} inputMode="numeric" value={view.header.guaranteedPax} onChange={e=>setH({guaranteedPax:Number(e.target.value.replace(/\D/g,''))||0})}/></Field>
        {editable&&<Field label="Valid until"><input type="date" min={s.date} value={d.validUntil} onChange={e=>setD(o=>({...o,validUntil:e.target.value}))}/></Field>}
      </div>
      {av&&<p className={'hall-avail'+(av.free?' ok':' bad')} role="status">{av.free?`${venue?.name} is free on ${view.header.dates.map(dayLabel).join(', ')} (${SLOT_LABEL[view.header.slot]}).`:`Clash: ${av.clashes.map(c=>`${c.hall} on ${dayLabel(c.date)} is held by ${c.customer} (${c.eventId})`).join('; ')}. The quotation can be saved, but it cannot be accepted until this is cleared.`}
        {av.soft.length>0&&` Enquiry hold: ${av.soft.map(o=>o.name).join(', ')}.`}{av.overCapacity&&` ${view.header.pax} guests is above the ${av.capacity} this set-up seats; a Manager can override on acceptance.`}</p>}</section>
      <section className="hall-box"><h3>Lines</h3>
        <div className="table-wrap"><table className="table hall-lines"><thead><tr><th>Description</th><th className="num">Qty</th><th className="num">Rate</th><th className="num">List</th><th className="num">GST</th><th className="num">Amount</th>{editable&&<th><span className="sr-only">Remove</span></th>}</tr></thead>
          <tbody>{priced.lines.map(l=><tr key={l.id}><td>{l.desc}</td>
            <td className="num">{editable&&l.unit!=='plate'?<input className="hall-qty" inputMode="numeric" aria-label={`Quantity of ${l.desc}`} value={l.qty} onChange={e=>setD(o=>hallSetQty(o,l.id,Number(e.target.value.replace(/\D/g,''))||1))}/>:`${l.qty} ${l.unit}`}</td>
            <td className="num">{editable?<RupeeInput label={`Rate of ${l.desc}`} value={l.ratePaise} onChange={p=>setD(o=>hallSetRate(o,l.id,p))}/>:money(l.ratePaise)}</td>
            <td className="num muted">{money(l.listRatePaise)}</td><td className="num">{gstPct(l.taxBps)}</td><td className="num">{money(hallLineTaxable(l)+l.cgstPaise+l.sgstPaise)}</td>
            {editable&&<td>{l.kind==='EXTRA'||l.kind==='MENU'?<button type="button" className="icon-btn" aria-label={`Remove ${l.desc}`} onClick={()=>setD(o=>hallRemoveLine(o,l.id))}><Trash2 size={16}/></button>:null}</td>}</tr>)}
            {!priced.lines.length&&<tr><td colSpan={7} className="muted">Choose a hall to start; add the menu and extras below.</td></tr>}</tbody></table></div>
        {editable&&<div className="inline hall-add">
          <Field label="Menu per plate"><select value={menuId} onChange={e=>setMenuId(e.target.value)}><option value="">Choose a package…</option>{mut.map(m=><option key={m.id} value={m.id}>{m.name} · {money(m.perPlatePaise)}</option>)}</select></Field>
          <button type="button" className="btn" disabled={!menuId} onClick={()=>{setD(o=>hallAddMenu(s,o,menuId));setMenuId('');}}><Plus size={15}/> Add menu</button>
          <Field label="Extra (decor, sound …)"><input value={extra.desc} maxLength={80} onChange={e=>setExtra(o=>({...o,desc:e.target.value}))}/></Field>
          <Field label="Qty"><input className="hall-qty" inputMode="numeric" value={extra.qty} onChange={e=>setExtra(o=>({...o,qty:e.target.value.replace(/\D/g,'')}))}/></Field>
          <Field label="Rate (₹)"><input className="hall-rupee" inputMode="decimal" value={extra.rate} onChange={e=>setExtra(o=>({...o,rate:e.target.value}))}/></Field>
          <button type="button" className="btn" disabled={extra.desc.trim().length<2||!toPaise(extra.rate)} onClick={()=>{setD(o=>hallAddExtra(o,extra.desc.trim(),Number(extra.qty)||1,toPaise(extra.rate)!));setExtra({desc:'',qty:'1',rate:''});}}><Plus size={15}/> Add extra</button></div>}
        <dl className="hall-totals"><div><dt>Total before GST</dt><dd>{money(t.taxablePaise)}</dd></div><div><dt>CGST</dt><dd>{money(t.cgstPaise)}</dd></div><div><dt>SGST</dt><dd>{money(t.sgstPaise)}</dd></div>
          <div className="grand"><dt>Grand total</dt><dd>{money(t.totalPaise)}</dd></div>
          <div><dt>Discount</dt><dd>{(disc.bps/100).toFixed(2)} % <small>{disc.limitBps===null?'(any, Owner)':`(limit ${disc.limitBps/100} % for your role)`}</small></dd></div></dl>
        {editable&&!disc.within&&<p className="hall-warn" role="alert">This discount is above your limit. Lower it, or ask the Owner to approve it.</p>}
        {q&&latest?.status==='DRAFT'&&shown===q.currentVersion&&ui.role==='Owner'&&latest.createdBy!==uid&&!latest.approvalId&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'QUO_DISCOUNT_APPROVE',id:q.id,version:q.currentVersion},'Discount approved',{close:false})}>Approve discount</button>}</section>
      <section className="hall-box"><h3>Payment plan</h3><div className="table-wrap"><table className="table"><thead><tr><th>Instalment</th><th>Due</th><th className="num">Amount</th></tr></thead>
        <tbody>{plan.map(r=><tr key={r.id}><td>{r.label}{r.pctBps?` (${r.pctBps/100} %)`:''}</td><td>{r.due==='ON_ACCEPT'?'On acceptance':r.due==='EVENT_DAY'?'On the event day':dayLabel(r.due)}</td><td className="num">{money(r.amountPaise??0)}</td></tr>)}</tbody></table></div>
        <p className="muted">Advances carry no GST. The balance row takes any rounding difference.</p></section>
      <section className="hall-box"><h3>Terms and conditions {tnc&&<small className="muted">· {tnc.templateId} v{tnc.v}</small>}</h3>
        {tnc?.hasPlaceholder&&<p className="hall-warn" role="alert">These terms still hold text marked “OWNER TO REPLACE”. A quotation cannot be sent until the Owner has replaced it{ui.role==='Owner'?' (or overrides below)':''}.</p>}
        {tnc?<ol className="hall-tnc">{tnc.clauses.map(c=><li key={c.n}><strong>{c.heading}.</strong> {c.text}</li>)}</ol>:<p className="muted">No terms template for this event type yet.</p>}
        {ui.tnc&&q&&status==='DRAFT'&&tnc?.hasPlaceholder&&!v?.placeholderOverride&&(panel==='override'
          ?<div className="inline"><Field label="Reason for sending with placeholder text"><input value={ovr} onChange={e=>setOvr(e.target.value)} maxLength={200}/></Field>
            <button type="button" className="btn" disabled={busy||ovr.trim().length<3} onClick={()=>run({type:'QUO_PLACEHOLDER_OVERRIDE',id:q.id,reason:ovr},'Override recorded',{close:false})}>Record override</button></div>
          :<button type="button" className="btn" onClick={()=>setPanel('override')}>Override (Owner)</button>)}
        {v?.placeholderOverride&&<p className="muted">Placeholder override by {v.placeholderOverride.by}: {v.placeholderOverride.reason}</p>}</section>
      {panel==='accept'&&q&&<section className="hall-box"><h3>Accept quotation</h3><p className="muted">This creates the event and holds the hall. A clash is refused.</p><div className="hall-fields">
        <Field label="First advance received (₹, optional)"><input className="hall-rupee" inputMode="decimal" value={acc.amount} onChange={e=>setAcc(o=>({...o,amount:e.target.value}))}/></Field>
        <Field label="Paid by"><select value={acc.method} onChange={e=>setAcc(o=>({...o,method:e.target.value}))}>{mnyNames(s,'frontdesk').map(m=><option key={m}>{m}</option>)}</select></Field>
        {!!mnyMethod(s,acc.method)?.needsReference&&<Field label="Reference"><input value={acc.ref} onChange={e=>setAcc(o=>({...o,ref:e.target.value}))}/></Field>}
        {boss&&<Field label="Manager reason (expired quotation)"><input value={acc.reason} onChange={e=>setAcc(o=>({...o,reason:e.target.value}))}/></Field>}
        {boss&&<Field label="Manager reason (above capacity)"><input value={acc.capReason} onChange={e=>setAcc(o=>({...o,capReason:e.target.value}))}/></Field>}</div>
        <button type="button" className="btn primary" disabled={busy||(!!acc.amount&&!toPaise(acc.amount))} onClick={()=>{const a=toPaise(acc.amount);
          void run({type:'QUO_ACCEPT',id:q.id,version:q.currentVersion,reason:acc.reason||undefined,capacityReason:acc.capReason||undefined,
            ...(a?{advance:{amountPaise:a,method:acc.method,reference:acc.ref||undefined}}:{})},'Quotation accepted: the event is on the Events tab',{close:true});}}><CheckCircle2 size={15}/> Accept and create event</button></section>}
      {panel==='lost'&&q&&<section className="hall-box"><h3>Mark lost</h3><div className="inline"><Field label="Reason"><select value={lost.reasonId} onChange={e=>setLost(o=>({...o,reasonId:e.target.value}))}>{x.lostReasons.map(r=><option key={r.id} value={r.id}>{r.label}</option>)}</select></Field>
        <Field label="Note"><input value={lost.note} onChange={e=>setLost(o=>({...o,note:e.target.value}))} maxLength={300}/></Field>
        <button type="button" className="btn" disabled={busy} onClick={()=>run({type:'QUO_LOST',id:q.id,reasonId:lost.reasonId,note:lost.note},'Quotation marked lost',{close:true})}>Mark lost</button></div></section>}
    </div>
  </ModalFrame>;
}
