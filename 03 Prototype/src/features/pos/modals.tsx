// Module 23 · POS dialogs: open check, settle, void, KOT / bill print, day report, phone cart sheet.
import {PrintTitle,PrintNotes} from '../exports/PrintNotes';
import {printDocument} from '../../app/native';
import {bookingRef} from '../../domain/bookingRef';
import React,{useState} from 'react';
import {Printer,Wallet,Ban,ShieldCheck,BedDouble} from 'lucide-react';
import type {ModalProps} from '../../app/registry';
import {money,rbacPosCanSettle,posOrder,posOrderTotals,posOutlet,posInHouse,posSpot,posActiveLines,posLineValue,posKotLabel,posDaySummary,posOpenOrders,
  posBuyerOf,posBillSplit,posFyCode,gstHalfPct,POS_VOID_LIMIT,POS_RST_SAC,POS_B2C_NAME_ADDRESS_LIMIT,mnyNames,mnyIsDrawer,mnyMethod} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,dateLabel,clockTime} from '../../components/ui';
import {PosCheck,posUserName} from './parts';

export function PosOpenModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const out=posOutlet(s,id||'');
  const [covers,setCovers]=useState('2');const [guest,setGuest]=useState('');
  if(!out||!room)return <Empty title="This record is unavailable"/>;
  const b=out.kind==='room'?posInHouse(s).find(b=>b.room===room):undefined;
  const submit=(e:React.FormEvent)=>{e.preventDefault();run({type:'POS_ORDER_OPEN',outlet:out.id,...(out.kind==='table'?{table:room,guest:guest.trim()||undefined}:{room}),covers:Number(covers)},`Check opened · ${out.kind==='table'?'Table':'Room'} ${room}`,{close:true});};
  return <><div className="modal-heading"><div className="eyebrow">{out.name.toUpperCase()}</div><h2>Open a check · {out.kind==='table'?'Table':'Room'} {room}</h2>
    <p>{b?<>Room service for <strong>{b.guest}</strong> ({bookingRef(b)}). The bill can be posted to this folio.</>:'Start a new dine-in check. Add dishes from the menu, then send the KOT.'}</p></div>
    <form className="form pos-form" onSubmit={submit}>
      <label>Covers (guests)<input inputMode="numeric" enterKeyHint="next" value={covers} onChange={e=>setCovers(e.target.value.replace(/\D/g,'').slice(0,2))} required/></label>
      {out.kind==='table'&&<label>Guest / reference (optional)<input enterKeyHint="done" maxLength={60} value={guest} onChange={e=>setGuest(e.target.value)} placeholder="e.g. Walk-in · Mr. Patil"/></label>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!Number(covers)}>Open check</button></div>
    </form></>;
}

export function PosSettleModal({id}:ModalProps){
  const {s,uid,run,busy,closeModal,permission}=useHcc();const o=posOrder(s,id);
  // DEC-079 D1: the Restaurant role never settles (the server refuses it too); the desk settles.
  const settle=rbacPosCanSettle(s,uid),canOutlet=settle&&permission('payments'),canFolio=settle&&permission('billing');
  const inHouse=posInHouse(s);
  const tenders=mnyNames(s,'pos'); // MA-3: payment methods usable at the POS (seed Cash, UPI, Card)
  const [mode,setMode]=useState<string>(o?.booking&&canFolio?'folio':canOutlet?(tenders.find(m=>mnyIsDrawer(s,m))??tenders[0]??'folio'):'folio');
  const cashMode=mode!=='folio'&&mnyIsDrawer(s,mode),refMode=mode!=='folio'&&!!mnyMethod(s,mode)?.needsReference;
  const [ref,setRef]=useState('');const [tendered,setTendered]=useState('');const [booking,setBooking]=useState(o?.booking||inHouse[0]?.id||'');
  const [gstin,setGstin]=useState('');const [buyerName,setBuyerName]=useState('');const [buyerAddress,setBuyerAddress]=useState('');
  if(!o)return <Empty title="This record is unavailable"/>;
  if(!settle)return <Empty title="Reception or the Cashier settles this bill" body="Your role takes orders and sends them to the kitchen."/>;
  const t=posOrderTotals(s,o),paid=Math.round(Number(tendered||0)*100),change=paid-t.total;
  const target=s.bookings.find(b=>b.id===booking);
  // DEC-072: an outlet bill is issued as a GST tax invoice (RST series) — B2C to the guest, or B2B with a checked buyer GSTIN.
  const buyer=posBuyerOf({buyerGstin:gstin,buyerName,buyerAddress},t.total),buyerError='error' in buyer?buyer.error:undefined;
  const large=t.total>=POS_B2C_NAME_ADDRESS_LIMIT;
  const buyerCmd={...(gstin.trim()?{buyerGstin:gstin.trim()}:{}),...(buyerName.trim()?{buyerName:buyerName.trim()}:{}),...(buyerAddress.trim()?{buyerAddress:buyerAddress.trim()}:{})};
  const submit=(e:React.FormEvent)=>{e.preventDefault();
    if(mode==='folio')run({type:'POS_SETTLE',id:o.id,mode:'folio',booking,key:'pos-settle:'+o.id},`Bill ${money(t.total)} posted to folio ${booking}`,{close:true});
    else run({type:'POS_SETTLE',id:o.id,mode:'outlet',method:mode,reference:ref.trim()||undefined,...buyerCmd,key:'pos-settle:'+o.id},`Bill settled · ${money(t.total)} ${mode}`,{close:true});};
  return <><div className="modal-heading"><div className="eyebrow">{posOutlet(s,o.outlet)?.name.toUpperCase()} · {o.id}</div><h2>Settle {posSpot(o)}</h2>
    <p>Net {money(t.net)} + GST {t.taxBps/100}% {money(t.tax)} = <strong>{money(t.total)}</strong>. Tax is snapshotted at settlement.</p></div>
    <form className="form pos-form" onSubmit={submit}>
      <fieldset className="pos-modes"><legend>Settle by</legend>
        {tenders.map(m=><label key={m} className={mode===m?'on':''}><input type="radio" name="mode" value={m} checked={mode===m} disabled={!canOutlet} onChange={()=>setMode(m)}/>{m}</label>)}
        <label className={mode==='folio'?'on':''}><input type="radio" name="mode" value="folio" checked={mode==='folio'} disabled={!canFolio||!inHouse.length} onChange={()=>setMode('folio')}/><BedDouble size={15}/> Room folio</label>
      </fieldset>
      {!canOutlet&&<div className="info-note"><ShieldCheck size={16}/>Taking cash, UPI or card needs the payments permission. You can post the bill to an in-house guest’s folio.</div>}
      {cashMode&&<><label>Cash received (₹, optional — for change)<input inputMode="decimal" enterKeyHint="done" value={tendered} onChange={e=>setTendered(e.target.value.replace(/[^\d.]/g,''))} placeholder={String(t.total/100)}/></label>
        {tendered&&<p className={'pos-change'+(change<0?' short':'')}>{change<0?`Short by ${money(-change)}`:`Change to return: ${money(change)}`}</p>}
        <div className="info-note"><Wallet size={16}/>Cash sales count once in the shared front-desk drawer and appear in the POS day summary. UPI/card payments and charges to a room folio do not add drawer cash.</div></>}
      {refMode&&<label>Demonstration transaction reference<input enterKeyHint="done" required maxLength={40} value={ref} onChange={e=>setRef(e.target.value)} placeholder={mode==='UPI'?'e.g. DEMO-UPI-1234':'e.g. DEMO-CARD-5678'}/><small>Simulated — no terminal or bank is contacted.</small></label>}
      {mode==='folio'&&<label>Charge to in-house guest<select value={booking} onChange={e=>setBooking(e.target.value)}>{inHouse.map(b=><option key={b.id} value={b.id}>Room {b.room} · {b.guest} · {b.id}</option>)}</select>
        <small>One service line “{posOutlet(s,o.outlet)?.name} · bill …” of {money(t.net)} with tax snapshot {t.taxBps/100}% is added to {target?`${target.guest}’s folio`:'the folio'}. It is on the room’s tax invoice after checkout (no restaurant invoice).</small></label>}
      {mode!=='folio'&&<fieldset className="pos-buyer"><legend>Tax invoice · {large?'₹50,000 or more: buyer name and address needed':'buyer details (optional)'}</legend>
        <label>Buyer GSTIN for a B2B invoice<input value={gstin} maxLength={15} autoCapitalize="characters" spellCheck={false} enterKeyHint="next" placeholder="Leave empty for a walk-in (B2C)"
          onChange={e=>setGstin(e.target.value.toUpperCase().replace(/[^0-9A-Z]/g,'').slice(0,15))}/></label>
        <label>{gstin?'Buyer legal name (required)':'Buyer name'}<input value={buyerName} maxLength={120} enterKeyHint="next" placeholder={o.guest||'e.g. Demo Industries Pvt Ltd (fictional)'} onChange={e=>setBuyerName(e.target.value)}/></label>
        <label>Buyer address{large&&!gstin?' (required)':''}<input value={buyerAddress} maxLength={200} enterKeyHint="done" onChange={e=>setBuyerAddress(e.target.value)}/></label>
        {buyerError&&(gstin||large)&&<p className="pos-change short" role="alert">{buyerError}</p>}
        <small>Settling issues a GST tax invoice in the restaurant series RST/{posFyCode(s.date)}/… — SAC {POS_RST_SAC}, CGST + SGST at {gstHalfPct(t.taxBps)} each. B2C to the guest, or B2B when a GSTIN is given.</small>
      </fieldset>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||(cashMode&&!!tendered&&change<0)||(mode==='folio'&&!booking)||(mode!=='folio'&&!!buyerError)}>{mode==='folio'?'Post to room folio':`Settle ${money(t.total)}`}</button></div>
    </form></>;
}

export function PosVoidModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const o=posOrder(s,id);const [reason,setReason]=useState('');
  if(!o)return <Empty title="This record is unavailable"/>;
  const bill=room==='bill',l=o.lines.find(l=>l.id===room);
  if(!bill&&!l)return <Empty title="This item is unavailable"/>;
  const value=bill?(o.settlement&&o.status==='Settled'?o.settlement.total:posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0)):posLineValue(l!);
  const needs=value>POS_VOID_LIMIT;
  const submit=(e:React.FormEvent)=>{e.preventDefault();
    run(bill?{type:'POS_VOID_BILL',id:o.id,reason:reason.trim()}:{type:'POS_VOID_ITEM',id:o.id,line:l!.id,reason:reason.trim()},needs?'Void request sent for approval':'Voided',{close:true});};
  return <><div className="modal-heading"><div className="eyebrow">{posSpot(o)} · {o.id}</div><h2>{bill?'Void the whole bill':'Void '+l!.qty+'× '+l!.name}</h2>
    <p>Value {money(value)}{bill&&o.status==='Settled'?` (bill ${o.settlement!.billNo}; the outlet receipt is reversed)`:''}. Voids are kept on the check with your name and reason.</p></div>
    <form className="form pos-form" onSubmit={submit}>
      {needs&&<div className="info-note"><ShieldCheck size={16}/>Above {money(POS_VOID_LIMIT)} — this becomes an approval request. A different user with approval rights (Manager / Owner) must approve it; the check is locked until then.</div>}
      <label>Reason<textarea required minLength={3} maxLength={160} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Guest changed order · dish returned"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn primary" disabled={busy||reason.trim().length<3}><Ban size={15}/> {needs?'Request void approval':'Void now'}</button></div>
    </form></>;
}

/** KOT (room = KOT id) or bill (room = 'bill') print document. */
export function PosPrintModal({id,room}:ModalProps){
  const {s,closeModal,run,busy,permission}=useHcc();const o=posOrder(s,id);
  if(!o)return <Empty title="This record is unavailable"/>;
  const out=posOutlet(s,o.outlet),k=o.kots.find(k=>k.id===room);
  const actions=<div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / share</button></div>;
  if(k){const lines=o.lines.filter(l=>l.kot===k.id);
    return <><div className="pos-doc pos-kot-doc invoice"><h2>KITCHEN ORDER TICKET</h2><p className="pos-doc-big">{posKotLabel(s,o,k)}</p>
      <div className="pos-doc-meta"><span>{out?.name}</span><span>{posSpot(o)}</span><span>{dateLabel(s.date)} {clockTime(k.at)}</span><span>By {posUserName(s,k.by)}</span><span>Covers {o.covers}</span></div>
      <table><thead><tr><th>Qty</th><th>Item</th></tr></thead><tbody>{lines.map(l=><tr key={l.id}><td className="pos-doc-qty">{l.qty}</td><td>{l.name}{l.voided?' — VOID':''}</td></tr>)}</tbody></table>
      <p className="footnote">Simulated KOT printer — printed through the browser. Status: {k.status}.</p></div>{actions}</>;}
  const t=posOrderTotals(s,o),st=o.settlement,prod=process.env.HCC_BUILD==='production';
  // DEC-072: an outlet bill prints as its GST tax invoice (RST number, hotel GSTIN, SAC, CGST / SGST) — the folio invoice's particulars.
  const inv=st?.invoiceNo,split=st?posBillSplit(st):undefined,state=st?.hotelGstin?.slice(0,2);
  const canIssue=!!st&&st.mode==='outlet'&&o.status==='Settled'&&!inv&&permission('payments');
  // The items lines are line values (price × qty); the bill is taxed once on its net, so the tax lines below carry the bill's own GST.
  return <><div className="pos-doc invoice"><h2>{s.settings.hotel}</h2>
    {inv&&<p className="pos-doc-sub pos-doc-title"><strong>{st!.supply==='B2B'?'TAX INVOICE · B2B':'TAX INVOICE'}</strong> No. <strong>{inv}</strong></p>}
    <p className="pos-doc-sub">{out?.name} · {st?'Bill '+st.billNo:'Pro-forma (not a bill)'}{o.status==='Void'?' · VOID':''}</p>
    <div className="pos-doc-meta"><span>{posSpot(o)}</span>{o.guest&&<span>{o.guest}</span>}<span>Check {o.id}</span><span>{dateLabel(st?.date||s.date)} {clockTime(st?.at||s.clock)}</span><span>Covers {o.covers}</span></div>
    {inv&&<div className="pos-doc-meta pos-doc-gst">
      <span>Hotel GSTIN {st!.hotelGstin??(prod?'—':'not set (demonstration)')}</span>
      <span>Place of supply {state&&state!=='27'?'state '+state:'Maharashtra (27)'} · intra-state</span>
      <span>SAC {st!.sac??POS_RST_SAC} · restaurant service</span>
      {st!.buyer?.name&&<span>Buyer {st!.buyer.name}</span>}{st!.buyer?.gstin&&<span>Buyer GSTIN {st!.buyer.gstin}</span>}{st!.buyer?.address&&<span>{st!.buyer.address}</span>}</div>}
    <table><thead><tr><th>Item</th><th className="number">Qty</th><th className="number">Rate</th><th className="number">Amount</th></tr></thead>
      <tbody>{posActiveLines(o).map(l=><tr key={l.id}><td>{l.name}</td><td className="number">{l.qty}</td><td className="number">{money(l.price)}</td><td className="number">{money(posLineValue(l))}</td></tr>)}</tbody></table>
    <div className="pos-totals pos-doc-totals"><div><span>{inv?'Taxable value':'Net'}</span><strong>{money(t.net)}</strong></div>
      {inv&&split?<><div><span>CGST @{gstHalfPct(t.taxBps)}</span><strong>{money(split.cgst)}</strong></div><div><span>SGST @{gstHalfPct(t.taxBps)}</span><strong>{money(split.sgst)}</strong></div></>
        :<div><span>GST {t.taxBps/100}%</span><strong>{money(t.tax)}</strong></div>}
      <div className="pos-grand"><span>Total</span><strong>{money(t.total)}</strong></div></div>
    {st&&<p className="pos-doc-sub">{st.mode==='folio'?`Charged to room folio ${st.booking} — payable at checkout; on the room’s tax invoice.`:`Paid by ${st.method}${st.reference?' · '+st.reference:''}.`}</p>}
    {st?.creditNoteNo&&<p className="pos-doc-sub pos-doc-cn"><strong>Cancelled:</strong> reversed in full by credit note {st.creditNoteNo}{st.creditNoteDate?' of '+dateLabel(st.creditNoteDate):''}.</p>}
    {canIssue&&<div className="pos-doc-issue no-print"><p className="pos-doc-sub">No tax invoice number on this bill yet{prod?' — the hotel server issues it once the hotel GSTIN is recorded (Master configuration › Property and branding).':' (settled before the restaurant series).'}</p>
      <button className="btn small-btn" disabled={busy} onClick={()=>run({type:'POS_INVOICE_ISSUE',id:o.id,key:'pos-invoice:'+o.id},'Tax invoice issued')}>Issue tax invoice</button></div>}
    <PrintNotes doc={inv?'pos-invoice':'receipt'}/>
    <p className="footnote">{inv?(prod?'Tax invoice issued and kept by the hotel server. GST from the dated GST master, intra-state (CGST + SGST) — CA to confirm.'
      :'Demonstration — sample tax invoice of fictional records. GST from the dated GST master, intra-state (CGST + SGST), half away from zero — illustrative, CA to confirm.')
      :prod?'Not a tax invoice.':'Demonstration document · fictional data · GST illustrative — not a tax invoice.'}</p></div>{actions}</>;
}

export function PosReportModal(){
  const {s,closeModal}=useHcc();const d=posDaySummary(s);const open=posOpenOrders(s);
  return <><div className="pos-doc invoice"><h2>Outlet day summary</h2><p className="pos-doc-sub">{s.settings.hotel} · business date {dateLabel(d.date)} · printed {clockTime(s.clock)}</p>
    <PosSummaryTables d={d}/>
    {open.length>0&&<><h3>Open checks</h3><table><tbody>{open.map(o=><tr key={o.id}><td>{posSpot(o)} · {o.id}</td><td className="number">{money(posOrderTotals(s,o).total)}</td></tr>)}</tbody></table></>}
    <p className="footnote">Outlet cash is counted in the front-desk cashier drawer (one shared till). GST illustrative; fictional data.</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / share</button></div></>;
}
export function PosSummaryTables({d}:{d:ReturnType<typeof posDaySummary>}){
  return <div className="pos-sum-grid">
    <table><caption>By outlet</caption><thead><tr><th>Outlet</th><th className="number">Bills</th><th className="number">Net</th><th className="number">Tax</th><th className="number">Total</th></tr></thead>
      <tbody>{d.byOutlet.map(x=><tr key={x.id}><td>{x.name}</td><td className="number">{x.checks}</td><td className="number">{money(x.net)}</td><td className="number">{money(x.tax)}</td><td className="number">{money(x.gross)}</td></tr>)}
      <tr className="pos-total-row"><td>All outlets</td><td className="number">{d.checks}</td><td className="number">{money(d.net)}</td><td className="number">{money(d.tax)}</td><td className="number">{money(d.gross)}</td></tr></tbody></table>
    <table><caption>By settlement</caption><tbody>{Object.entries(d.byMode).map(([k,v])=><tr key={k}><td>{k}</td><td className="number">{money(v)}</td></tr>)}
      <tr className="pos-total-row"><td>Outlet cash (counted in the front-desk drawer)</td><td className="number">{money(d.outletCash)}</td></tr>
      <tr><td>Voids ({d.voids.count} item{d.voids.count===1?'':'s'})</td><td className="number">{money(d.voids.value)}</td></tr>
      <tr><td>Open checks ({d.open.count})</td><td className="number">{money(d.open.value)}</td></tr></tbody></table>
  </div>;
}

/** Phone cart sheet. */
export function PosCartSheet({id}:ModalProps){
  const {s}=useHcc();const o=posOrder(s,id);
  if(!o)return <Empty title="This check is unavailable"/>;
  return <PosCheck o={o} compact/>;
}
