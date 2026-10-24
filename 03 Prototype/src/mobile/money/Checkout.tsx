// M11 Checkout (phone override of 'checkout'): checklist with ticks + text, Confirm only when clear, invoice preview sheet.
// Phone override of 'invoice': phone-friendly invoice that still prints (DEC-047) at A4 and in a phone print preview.
import {printDocument} from '../../app/native';
import React from 'react';
import {Check,AlertTriangle,ArrowRight,Printer,Building2,Receipt,Hotel,Info} from 'lucide-react';
import {money,totals,folioCompanyCreditState,FOLIO_MANAGER_CREDIT_LIMIT} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate} from '../../components/ui';
import {mmCheckoutChecklist,mmCheckoutCommand} from './logic';

export function MmCheckout({id}:{id?:string}){
  const {s,run,booking,open,busy,permission}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const t=totals(b),c=mmCheckoutChecklist(s,b),cc=folioCompanyCreditState(s,b,t.balance);
  const canCheckout=permission('checkout');
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · ROOM {b.room}</div><h2>Checkout</h2><p>Review {b.guest}’s folio before releasing Room {b.room}.</p></div>
    <section className="mm-card mm-checklist" aria-label="Checkout checklist">
      {c.items.map(i=><div key={i.id} className={'mm-check '+(i.ok?'ok':'todo')}>
        <span className="mm-check-icon" aria-hidden>{i.ok?<Check size={18}/>:<AlertTriangle size={18}/>}</span>
        <div><strong>{i.label}</strong><small>{i.detail}</small></div>
        <span className="mm-check-state">{i.ok?'Done':'To do'}</span></div>)}
    </section>
    <div className="mm-card mm-sum">
      <div className="mm-row"><div className="mm-row-main"><strong>Total incl. test tax</strong></div><div className="mm-row-amt"><strong className="number">{money(t.total)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Net paid</strong></div><div className="mm-row-amt"><strong className="number">{money(t.paid)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Outstanding</strong></div><div className="mm-row-amt"><strong className="number">{money(t.balance)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Deposit to return</strong></div><div className="mm-row-amt"><strong className="number">{money(t.deposit)}</strong></div></div>
    </div>
    {b.company&&t.balance>0&&<div className="info-note mm-cc"><Building2 size={18}/><div>
      <strong>Company credit · {b.company.name}</strong>
      {cc.state==='none'&&<p>Move the {money(t.balance)} balance to the company’s receivable. A Manager approves up to {money(FOLIO_MANAGER_CREDIT_LIMIT)}, an Owner above.</p>}
      {cc.state==='pending'&&<p>Requested as {cc.approval!.id} — awaiting approval by another user.</p>}
      {cc.state==='approved'&&<p>Approved ({cc.approval!.id}) for exactly {money(cc.approval!.amount)}. Applied when you confirm.</p>}
      {cc.state==='stale'&&<p>Approval {cc.approval!.id} no longer matches the folio. Request again.</p>}
      {(cc.state==='none'||cc.state==='stale')&&canCheckout&&<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'COMPANY_CREDIT_REQUEST',id:b.id,key:`cc:${b.id}:${b.version}`},'Company credit requested — awaiting approval')}>Request company credit</button>}
    </div></div>}
    {c.clear?<div className="info-note"><Info size={17}/>All clear. Checkout marks the room dirty, creates one housekeeping task and allocates a sample invoice number once.</div>
      :<p className="mm-why" role="status">Confirm is available when every item shows Done.{!canCheckout?' Your role cannot check guests out — reception or a manager does.':''}</p>}
    {!canCheckout&&c.clear&&<p className="mm-why">Your role cannot check guests out — reception or a manager does.</p>}
    <div className="mm-sheet-links">
      {permission('payments')&&(t.balance>0||t.deposit>0)&&<button type="button" className="btn" disabled={busy} onClick={()=>open({type:'payment',id:b.id})}><Receipt size={18}/>{t.balance>0?'Settle balance':'Return deposit'}</button>}
      <button type="button" className="btn" onClick={()=>open({type:'invoice',id:b.id})}><Printer size={18}/>Invoice preview</button>
    </div>
    <div className="modal-actions"><button className="btn" onClick={()=>booking(b.id)}>Folio</button>
      <button className="btn primary mm-big" disabled={busy||!c.clear||!canCheckout} onClick={()=>run(mmCheckoutCommand(b.id),'Checkout completed. Housekeeping task created.',{after:()=>open({type:'invoice',id:b.id})})}>Confirm checkout <ArrowRight size={18}/></button></div></>;
}

export function MmInvoice({id}:{id?:string}){
  const {s,booking}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const t=totals(b),cc=b.receipts.filter(r=>r.kind==='company-credit').reduce((a,r)=>a+r.amount,0);
  return <><div className="invoice mm-invoice">
      <div className="invoice-top mm-inv-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>Hotel Control Centre · Fictional property</p></div></div>
      <div className="invoice-stamp" aria-label="Demonstration — not a tax invoice">Demonstration — not a tax invoice · fictional records only</div>
      <div className="mm-inv-meta">
        <div><small>GUEST</small><strong>{b.guest}</strong><span>Room {b.room} · {b.people} guests · {b.id}</span>{b.company&&<span>Bill to: {b.company.name}{b.company.gstin?` · GSTIN (placeholder) ${b.company.gstin}`:''}</span>}</div>
        <div><small>{b.invoiceNo?'SAMPLE INVOICE NO.':'FOLIO PREVIEW'}</small><strong>{b.invoiceNo??'Not yet issued'}</strong><span>{shortDate(b.arrival)} — {shortDate(b.departure)}</span><span>{b.invoiceNo?'Allocated once at checkout':'Number allocated once at checkout'}</span></div>
      </div>
      <table className="mm-inv-table"><thead><tr><th>Description</th><th>Date</th><th className="number">Net</th><th className="number">Tax</th></tr></thead>
        <tbody>{b.lines.map(l=><tr key={l.id}><td data-label="Description">{l.label}</td><td data-label="Date">{shortDate(l.date)}</td><td className="number" data-label="Net">{money(l.amount)}</td><td className="number" data-label="Synthetic tax">{money(Math.round(l.amount*l.taxBps/10000))}</td></tr>)}</tbody></table>
      <div className="mm-inv-sum">
        <div><span>Total including test tax</span><strong className="number">{money(t.total)}</strong></div>
        <div><span>Net receipts applied</span><strong className="number">{money(t.paid)}</strong></div>
        {cc>0&&<div><span>On company credit (receivable)</span><strong className="number">{money(cc)}</strong></div>}
        <div className="mm-inv-bal"><span>Balance</span><strong className="number">{money(t.balance)}</strong></div>
        <div><span>Refundable deposit held separately</span><strong className="number">{money(t.deposit)}</strong></div>
      </div>
      {!!b.creditNotes?.length&&<div className="mm-inv-cn"><small>SAMPLE CREDIT NOTES</small>{b.creditNotes.map(c=><p key={c.id}>{c.no} · {shortDate(c.date)} · {money(c.amount)} before tax · {c.reason}</p>)}</div>}
      <p className="footnote">Synthetic tax rules. No GST compliance claim. All names, transactions and references are fictional. Demonstration — not a tax invoice.</p>
    </div>
    <p className="mm-help no-print">Prints on A4 through the phone’s print / share dialog (wireless printer or Save as PDF).</p>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={18}/> Print / share</button></div></>;
}
