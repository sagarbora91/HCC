// F6-H3 · Hall booking prints (A4 HTML, spec §18): quotation, function sheet (BEO), tax invoice, advance receipt. Each renders a pure model from logic.ts, the same
// figures the screen shows (printed totals = screen totals). The BEO takes no prices for the kitchen / housekeeping.
import React from 'react';
import {money} from '../../domain/money';
import {gstPct} from '../../domain/gst';
import {dayLabel,SLOT_LABEL,EVENT_LABEL,SETUP_LABEL,type QuoteDocModel,type BeoModel,type InvoiceDocModel,hallReceiptDoc} from './logic';

const Head=({hotel,title,no,date}:{hotel:string;title:string;no?:string;date?:string})=><header className="hall-doc-head"><div><strong>{hotel}</strong><span>Banquet &amp; events</span></div>
  <div className="hall-doc-title"><h2>{title}</h2>{no&&<span>{no}</span>}{date&&<span>{dayLabel(date)}</span>}</div></header>;

export function QuotationDoc({m}:{m:QuoteDocModel}){
  return <article className="print-doc hall-doc" aria-label={`Quotation ${m.number}`}>
    <Head hotel={m.hotel} title="Quotation" no={`${m.number} · v${m.version}`} date={m.date}/>
    <div className="hall-doc-grid"><div><small>Prepared for</small><strong>{m.customer.name}</strong>{m.customer.company&&<span>{m.customer.company}</span>}{m.customer.phone&&<span>{m.customer.phone}</span>}</div>
      <div><small>Event</small><strong>{EVENT_LABEL[m.header.eventType]}</strong><span>{m.header.dates.map(dayLabel).join(', ')} · {SLOT_LABEL[m.header.slot]}</span><span>{m.venue} · {SETUP_LABEL[m.header.setup]}</span></div>
      <div><small>Guests</small><strong>{m.header.pax} expected</strong><span>{m.header.guaranteedPax} guaranteed plates</span><span>Valid until {dayLabel(m.validUntil)}</span></div></div>
    <table className="table hall-doc-table"><thead><tr><th>Description</th><th className="num">Qty</th><th className="num">Rate</th><th className="num">Taxable</th><th className="num">GST</th><th className="num">Amount</th></tr></thead>
      <tbody>{m.lines.map((l,i)=><tr key={i}><td>{l.desc}</td><td className="num">{l.qty} {l.unit}</td><td className="num">{money(l.ratePaise)}</td><td className="num">{money(l.taxablePaise)}</td>
        <td className="num">{gstPct(l.taxBps)}</td><td className="num">{money(l.taxablePaise+l.cgstPaise+l.sgstPaise)}</td></tr>)}</tbody>
      <tfoot><tr><td colSpan={5}>Total before GST</td><td className="num">{money(m.totals.taxablePaise)}</td></tr>
        <tr><td colSpan={5}>CGST</td><td className="num">{money(m.totals.cgstPaise)}</td></tr><tr><td colSpan={5}>SGST</td><td className="num">{money(m.totals.sgstPaise)}</td></tr>
        <tr className="hall-doc-total"><td colSpan={5}>Grand total</td><td className="num">{money(m.totals.totalPaise)}</td></tr></tfoot></table>
    <h3>Payment plan</h3>
    <table className="table hall-doc-table"><thead><tr><th>Instalment</th><th>Due</th><th className="num">Amount</th></tr></thead>
      <tbody>{m.advance.map(r=><tr key={r.id}><td>{r.label}{r.pctBps?` (${r.pctBps/100} %)`:''}</td><td>{r.due==='ON_ACCEPT'?'On acceptance':r.due==='EVENT_DAY'?'On the event day':dayLabel(r.due)}</td><td className="num">{money(r.amountPaise??0)}</td></tr>)}</tbody></table>
    <p className="muted">Advances carry no GST; GST is charged on the tax invoice after the event.</p>
    {m.tnc&&<section className="hall-doc-tnc"><h3>Terms and conditions</h3>{m.tnc.hasPlaceholder&&<p className="hall-warn" role="alert">These terms still contain text the Owner has to replace.</p>}
      <ol>{m.tnc.clauses.map(c=><li key={c.n}><strong>{c.heading}.</strong> {c.text}</li>)}</ol></section>}
    <div className="hall-doc-sign"><div>For {m.hotel}</div><div>Accepted by (name, signature, date)</div></div>
  </article>;
}

export function BeoDoc({m}:{m:BeoModel}){
  return <article className="print-doc hall-doc" aria-label={`Function sheet ${m.eventId}`}>
    <Head hotel={m.hotel} title="Function sheet" no={m.eventId} date={m.date}/>
    <div className="hall-doc-grid"><div><small>Event</small><strong>{m.title}</strong><span>{m.dates.map(dayLabel).join(', ')} · {m.slot}{m.time?' · '+m.time:''}</span></div>
      <div><small>Hall</small><strong>{m.venues.join(' + ')}</strong>{m.setup&&<span>Set-up: {m.setup}</span>}</div>
      <div><small>Guests</small><strong>{m.guaranteedPax} guaranteed</strong>{m.actualPax!==undefined&&<span>{m.actualPax} actual</span>}<span>{m.customer}{m.phone?' · '+m.phone:''}</span></div></div>
    <h3>Menu</h3>
    {m.menu.length?<table className="table hall-doc-table"><tbody>{m.menu.map((l,i)=><tr key={i}><td>{l.desc}</td><td className="num">{l.qty} {l.unit}</td></tr>)}</tbody></table>:<p className="muted">No menu on this booking.</p>}
    {m.extras.length>0&&<><h3>Extras and services</h3><table className="table hall-doc-table"><tbody>{m.extras.map((l,i)=><tr key={i}><td>{l.desc}</td><td className="num">{l.qty} {l.unit}</td></tr>)}</tbody></table></>}
    {m.notes.length>0&&<><h3>Notes</h3><ul>{m.notes.map((n,i)=><li key={i}>{n}</li>)}</ul></>}
    {m.prices&&<><h3>Account</h3><table className="table hall-doc-table"><tfoot><tr><td>Total with GST</td><td className="num">{money(m.prices.totals.totalPaise)}</td></tr>
      <tr><td>Advances received</td><td className="num">{money(m.prices.paid)}</td></tr><tr className="hall-doc-total"><td>Balance</td><td className="num">{money(m.prices.balance)}</td></tr></tfoot></table></>}
    {!m.prices&&<p className="muted">No prices on this sheet.</p>}
  </article>;
}

export function InvoiceDoc({m}:{m:InvoiceDocModel}){
  return <article className="print-doc hall-doc" aria-label={`Tax invoice ${m.no}`}>
    <Head hotel={m.hotel} title="Tax invoice" no={m.no} date={m.date}/>
    <div className="hall-doc-grid"><div><small>Billed to</small><strong>{m.customer.name}</strong>{m.customer.company&&<span>{m.customer.company}</span>}{m.customer.phone&&<span>{m.customer.phone}</span>}</div>
      <div><small>Event</small><strong>{m.event}</strong><span>{m.billedPax} plates billed</span></div><div><small>Settled</small><strong>{m.settle}</strong></div></div>
    <table className="table hall-doc-table"><thead><tr><th>Description</th><th className="num">Qty</th><th className="num">Rate</th><th className="num">Taxable</th><th className="num">CGST</th><th className="num">SGST</th><th className="num">Amount</th></tr></thead>
      <tbody>{m.lines.map((l,i)=><tr key={i}><td>{l.desc}</td><td className="num">{l.qty} {l.unit}</td><td className="num">{money(l.ratePaise)}</td><td className="num">{money(l.taxablePaise)}</td>
        <td className="num">{money(l.cgstPaise)} <small>({gstPct(l.taxBps/2)})</small></td><td className="num">{money(l.sgstPaise)} <small>({gstPct(l.taxBps/2)})</small></td><td className="num">{money(l.taxablePaise+l.cgstPaise+l.sgstPaise)}</td></tr>)}</tbody>
      <tfoot><tr><td colSpan={6}>Total before GST</td><td className="num">{money(m.taxablePaise)}</td></tr><tr><td colSpan={6}>CGST</td><td className="num">{money(m.cgstPaise)}</td></tr>
        <tr><td colSpan={6}>SGST</td><td className="num">{money(m.sgstPaise)}</td></tr><tr className="hall-doc-total"><td colSpan={6}>Invoice total</td><td className="num">{money(m.totalPaise)}</td></tr>
        <tr><td colSpan={6}>Advances received</td><td className="num">{money(m.advanceAppliedPaise)}</td></tr><tr><td colSpan={6}>Balance</td><td className="num">{money(m.balancePaise)}</td></tr></tfoot></table>
    {m.credit&&<p className="hall-warn" role="status">Credit note {m.credit.no} dated {dayLabel(m.credit.date)} cancels this invoice: {m.credit.reason}</p>}
    <p className="muted">Computer-generated invoice. Corrections are made by credit note.</p>
  </article>;
}

export function ReceiptDoc({m}:{m:ReturnType<typeof hallReceiptDoc>}){
  return <article className="print-doc hall-doc" aria-label={`Receipt ${m.id}`}>
    <Head hotel={m.hotel} title={m.kind==='REFUND'?'Refund voucher':'Advance receipt'} no={m.id} date={m.date}/>
    <div className="hall-doc-grid"><div><small>{m.kind==='REFUND'?'Paid to':'Received from'}</small><strong>{m.customer}</strong></div><div><small>Event</small><strong>{m.event}</strong></div>
      <div><small>Paid by</small><strong>{m.method}</strong>{m.reference&&<span>{m.reference}</span>}</div></div>
    <p className="hall-doc-big">{money(m.amountPaise)}</p>
    <p className="muted">{m.kind==='REFUND'?'Refund of advance money.':'Advance towards the event. No GST on an advance; GST is charged on the tax invoice.'}</p>
  </article>;
}
