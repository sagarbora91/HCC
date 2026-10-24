// Module 23 · POS dialogs: open check, settle, void, KOT / bill print, day report, phone cart sheet.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Printer,Wallet,Ban,ShieldCheck,BedDouble} from 'lucide-react';
import type {ModalProps} from '../../app/registry';
import {money,posOrder,posOrderTotals,posOutlet,posInHouse,posSpot,posActiveLines,posLineValue,posKotLabel,posDaySummary,posOpenOrders,
  POS_VOID_LIMIT,POS_TENDERS} from '../../services/mock';
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
    <p>{b?<>Room service for <strong>{b.guest}</strong> ({b.id}). The bill can be posted to this folio.</>:'Start a new dine-in check. Add dishes from the menu, then send the KOT.'}</p></div>
    <form className="form pos-form" onSubmit={submit}>
      <label>Covers (guests)<input inputMode="numeric" enterKeyHint="next" value={covers} onChange={e=>setCovers(e.target.value.replace(/\D/g,'').slice(0,2))} required/></label>
      {out.kind==='table'&&<label>Guest / reference (optional)<input enterKeyHint="done" maxLength={60} value={guest} onChange={e=>setGuest(e.target.value)} placeholder="e.g. Walk-in · Mr. Patil"/></label>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!Number(covers)}>Open check</button></div>
    </form></>;
}

export function PosSettleModal({id}:ModalProps){
  const {s,run,busy,closeModal,permission}=useHcc();const o=posOrder(s,id);
  const canOutlet=permission('payments'),canFolio=permission('billing');
  const inHouse=posInHouse(s);
  const [mode,setMode]=useState<string>(o?.booking&&canFolio?'folio':canOutlet?'Cash':'folio');
  const [ref,setRef]=useState('');const [tendered,setTendered]=useState('');const [booking,setBooking]=useState(o?.booking||inHouse[0]?.id||'');
  if(!o)return <Empty title="This record is unavailable"/>;
  const t=posOrderTotals(s,o),paid=Math.round(Number(tendered||0)*100),change=paid-t.total;
  const target=s.bookings.find(b=>b.id===booking);
  const submit=(e:React.FormEvent)=>{e.preventDefault();
    if(mode==='folio')run({type:'POS_SETTLE',id:o.id,mode:'folio',booking,key:'pos-settle:'+o.id},`Bill ${money(t.total)} posted to folio ${booking}`,{close:true});
    else run({type:'POS_SETTLE',id:o.id,mode:'outlet',method:mode,reference:ref.trim()||undefined,key:'pos-settle:'+o.id},`Bill settled · ${money(t.total)} ${mode}`,{close:true});};
  return <><div className="modal-heading"><div className="eyebrow">{posOutlet(s,o.outlet)?.name.toUpperCase()} · {o.id}</div><h2>Settle {posSpot(o)}</h2>
    <p>Net {money(t.net)} + synthetic tax {t.taxBps/100}% {money(t.tax)} = <strong>{money(t.total)}</strong>. Tax is snapshotted at settlement.</p></div>
    <form className="form pos-form" onSubmit={submit}>
      <fieldset className="pos-modes"><legend>Settle by</legend>
        {POS_TENDERS.map(m=><label key={m} className={mode===m?'on':''}><input type="radio" name="mode" value={m} checked={mode===m} disabled={!canOutlet} onChange={()=>setMode(m)}/>{m}</label>)}
        <label className={mode==='folio'?'on':''}><input type="radio" name="mode" value="folio" checked={mode==='folio'} disabled={!canFolio||!inHouse.length} onChange={()=>setMode('folio')}/><BedDouble size={15}/> Room folio</label>
      </fieldset>
      {!canOutlet&&<div className="info-note"><ShieldCheck size={16}/>Taking cash, UPI or card needs the payments permission. You can post the bill to an in-house guest’s folio.</div>}
      {mode==='Cash'&&<><label>Cash received (₹, optional — for change)<input inputMode="decimal" enterKeyHint="done" value={tendered} onChange={e=>setTendered(e.target.value.replace(/[^\d.]/g,''))} placeholder={String(t.total/100)}/></label>
        {tendered&&<p className={'pos-change'+(change<0?' short':'')}>{change<0?`Short by ${money(-change)}`:`Change to return: ${money(change)}`}</p>}
        <div className="info-note"><Wallet size={16}/>Outlet cash is kept in the outlet summary (POS day summary), separate from the front-desk cashier drawer in this prototype.</div></>}
      {(mode==='UPI'||mode==='Card')&&<label>Demonstration transaction reference<input enterKeyHint="done" required maxLength={40} value={ref} onChange={e=>setRef(e.target.value)} placeholder={mode==='UPI'?'e.g. DEMO-UPI-1234':'e.g. DEMO-CARD-5678'}/><small>Simulated — no terminal or bank is contacted.</small></label>}
      {mode==='folio'&&<label>Charge to in-house guest<select value={booking} onChange={e=>setBooking(e.target.value)}>{inHouse.map(b=><option key={b.id} value={b.id}>Room {b.room} · {b.guest} · {b.id}</option>)}</select>
        <small>One service line “{posOutlet(s,o.outlet)?.name} · bill …” of {money(t.net)} with tax snapshot {t.taxBps/100}% is added to {target?`${target.guest}’s folio`:'the folio'}.</small></label>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||(mode==='Cash'&&!!tendered&&change<0)||(mode==='folio'&&!booking)}>{mode==='folio'?'Post to room folio':`Settle ${money(t.total)}`}</button></div>
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
  const {s,closeModal}=useHcc();const o=posOrder(s,id);
  if(!o)return <Empty title="This record is unavailable"/>;
  const out=posOutlet(s,o.outlet),k=o.kots.find(k=>k.id===room);
  const actions=<div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / share</button></div>;
  if(k){const lines=o.lines.filter(l=>l.kot===k.id);
    return <><div className="pos-doc pos-kot-doc invoice"><h2>KITCHEN ORDER TICKET</h2><p className="pos-doc-big">{posKotLabel(s,o,k)}</p>
      <div className="pos-doc-meta"><span>{out?.name}</span><span>{posSpot(o)}</span><span>{dateLabel(s.date)} {clockTime(k.at)}</span><span>By {posUserName(s,k.by)}</span><span>Covers {o.covers}</span></div>
      <table><thead><tr><th>Qty</th><th>Item</th></tr></thead><tbody>{lines.map(l=><tr key={l.id}><td className="pos-doc-qty">{l.qty}</td><td>{l.name}{l.voided?' — VOID':''}</td></tr>)}</tbody></table>
      <p className="footnote">Simulated KOT printer — printed through the browser. Status: {k.status}.</p></div>{actions}</>;}
  const t=posOrderTotals(s,o),st=o.settlement;
  return <><div className="pos-doc invoice"><h2>{s.settings.hotel}</h2><p className="pos-doc-sub">{out?.name} · {st?'Bill '+st.billNo:'Pro-forma (not a bill)'}{o.status==='Void'?' · VOID':''}</p>
    <div className="pos-doc-meta"><span>{posSpot(o)}</span>{o.guest&&<span>{o.guest}</span>}<span>Check {o.id}</span><span>{dateLabel(st?.date||s.date)} {clockTime(st?.at||s.clock)}</span><span>Covers {o.covers}</span></div>
    <table><thead><tr><th>Item</th><th className="number">Qty</th><th className="number">Rate</th><th className="number">Amount</th></tr></thead>
      <tbody>{posActiveLines(o).map(l=><tr key={l.id}><td>{l.name}</td><td className="number">{l.qty}</td><td className="number">{money(l.price)}</td><td className="number">{money(posLineValue(l))}</td></tr>)}</tbody></table>
    <div className="pos-totals pos-doc-totals"><div><span>Net</span><strong>{money(t.net)}</strong></div><div><span>Synthetic tax {t.taxBps/100}%</span><strong>{money(t.tax)}</strong></div><div className="pos-grand"><span>Total</span><strong>{money(t.total)}</strong></div></div>
    {st&&<p className="pos-doc-sub">{st.mode==='folio'?`Charged to room folio ${st.booking} — payable at checkout.`:`Paid by ${st.method}${st.reference?' · '+st.reference:''}.`}</p>}
    <p className="footnote">Demonstration document · fictional data · synthetic tax, not a GST invoice.</p></div>{actions}</>;
}

export function PosReportModal(){
  const {s,closeModal}=useHcc();const d=posDaySummary(s);const open=posOpenOrders(s);
  return <><div className="pos-doc invoice"><h2>Outlet day summary</h2><p className="pos-doc-sub">{s.settings.hotel} · business date {dateLabel(d.date)} · printed {clockTime(s.clock)}</p>
    <PosSummaryTables d={d}/>
    {open.length>0&&<><h3>Open checks</h3><table><tbody>{open.map(o=><tr key={o.id}><td>{posSpot(o)} · {o.id}</td><td className="number">{money(posOrderTotals(s,o).total)}</td></tr>)}</tbody></table></>}
    <p className="footnote">Outlet cash is listed separately from the front-desk cashier drawer in this prototype. Synthetic tax; fictional data.</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / share</button></div></>;
}
export function PosSummaryTables({d}:{d:ReturnType<typeof posDaySummary>}){
  return <div className="pos-sum-grid">
    <table><caption>By outlet</caption><thead><tr><th>Outlet</th><th className="number">Bills</th><th className="number">Net</th><th className="number">Tax</th><th className="number">Total</th></tr></thead>
      <tbody>{d.byOutlet.map(x=><tr key={x.id}><td>{x.name}</td><td className="number">{x.checks}</td><td className="number">{money(x.net)}</td><td className="number">{money(x.tax)}</td><td className="number">{money(x.gross)}</td></tr>)}
      <tr className="pos-total-row"><td>All outlets</td><td className="number">{d.checks}</td><td className="number">{money(d.net)}</td><td className="number">{money(d.tax)}</td><td className="number">{money(d.gross)}</td></tr></tbody></table>
    <table><caption>By settlement</caption><tbody>{Object.entries(d.byMode).map(([k,v])=><tr key={k}><td>{k}</td><td className="number">{money(v)}</td></tr>)}
      <tr className="pos-total-row"><td>Outlet cash (separate from drawer)</td><td className="number">{money(d.outletCash)}</td></tr>
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
