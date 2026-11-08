// M11 Checkout (phone override of 'checkout'): checklist with ticks + text, Confirm only when clear, invoice preview sheet.
// Phone override of 'invoice': phone-friendly invoice that still prints (DEC-047) at A4 and in a phone print preview.
import {printDocument} from '../../app/native';
import {bookingRef} from '../../domain/bookingRef';
import React from 'react';
import {Check,AlertTriangle,ArrowRight,Printer,Receipt,Hotel,Info} from 'lucide-react';
import {money,totals,gstLineSplit,gstSplitLines,gstHalfPct,plural} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate} from '../../components/ui';
import {mmCheckoutChecklist,mmCheckoutCommand} from './logic';
import {IssueInvoiceAction} from '../../features/folio/modals';
import {CompanyCreditPanel} from '../../features/groups/credit';
import {ApplyDepositPanel,useOtaCredit} from '../../features/folio/deposit'; // ACC-2
// F8-SF-UI
import {sfIsSplit,sfView} from '../../services/mock';
import {SfSwitcher,SfCheckoutSheet,SfInvoiceList} from '../../features/folio/Split';
import {sfSettleSheet,sfOpen,sfFolioOfModal,sfCurrent} from '../../features/folio/SplitLogic';
// F8-SF-UI end

export function MmCheckout({id}:{id?:string}){
  const {s,run,booking,open,busy,permission}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const ota=useOtaCredit(b,true);
  if(!b)return <Empty title="This record is unavailable"/>;
  const split=sfIsSplit(b); // F8-SF-UI: a split stay settles every folio on one sheet
  const sh=split?sfSettleSheet(s,b):undefined;
  const t=totals(b),c=split?{items:[{id:'nights',label:'All folios settled',ok:sh!.rows.every(r=>r.status==='settled'||r.status==='company-credit'||r.status==='guest-credit'),detail:`${sh!.rows.length} folios · outstanding ${money(sh!.balance)}`},{id:'deposit',label:'Deposit returned',ok:sh!.deposit===0,detail:sh!.deposit===0?'No deposit held':'Deposit still held — return it first'},{id:'blockers',label:'Nothing else blocking',ok:sh!.clear,detail:sh!.blockers[0]??'Clear'}],clear:sh!.clear,blockers:sh!.blockers}:mmCheckoutChecklist(s,b);
  const canCheckout=permission('checkout');
  return <><div className="modal-heading"><div className="eyebrow">{bookingRef(b)} · ROOM {b.room}</div><h2>Checkout</h2><p>Review {b.guest}’s folio before releasing Room {b.room}.</p></div>
    <section className="mm-card mm-checklist" aria-label="Checkout checklist">
      {c.items.map(i=><div key={i.id} className={'mm-check '+(i.ok?'ok':'todo')}>
        <span className="mm-check-icon" aria-hidden>{i.ok?<Check size={18}/>:<AlertTriangle size={18}/>}</span>
        <div><strong>{i.label}</strong><small>{i.detail}</small></div>
        <span className="mm-check-state">{i.ok?'Done':'To do'}</span></div>)}
    </section>
    <div className="mm-card mm-sum">
      <div className="mm-row"><div className="mm-row-main"><strong>Total incl. GST</strong></div><div className="mm-row-amt"><strong className="number">{money(t.total)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Net paid</strong></div><div className="mm-row-amt"><strong className="number">{money(t.paid)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Outstanding</strong></div><div className="mm-row-amt"><strong className="number">{money(t.balance)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Deposit to return</strong></div><div className="mm-row-amt"><strong className="number">{money(t.deposit)}</strong></div></div>
    </div>
    {split?<><SfCheckoutSheet b={b} mobile/><ApplyDepositPanel b={sfView(b,'A')} mobile/></>:<><ApplyDepositPanel b={b} mobile/>
    {ota.node}
    <CompanyCreditPanel b={b} mobile/></>}{/* F8-SF-UI */}
    {c.clear?<div className="info-note"><Info size={17}/>All clear. Checkout marks the room dirty, creates one housekeeping task and allocates a sample invoice number once.</div>
      :<p className="mm-why" role="status">Confirm is available when every item shows Done.{!canCheckout?' Your role cannot check guests out — reception or a manager does.':''}</p>}
    {!canCheckout&&c.clear&&<p className="mm-why">Your role cannot check guests out — reception or a manager does.</p>}
    <div className="mm-sheet-links">
      {permission('payments')&&(split?t.deposit>0:t.balance>0||t.deposit>0)&&<button type="button" className="btn" disabled={busy} onClick={()=>open(sfOpen(b,'payment','A'))}><Receipt size={18}/>{t.balance>0&&!split?'Settle balance':'Return deposit'}</button>}
      <button type="button" className="btn" onClick={()=>open(sfOpen(b,'invoice','A'))}><Printer size={18}/>Invoice preview</button>
    </div>
    <div className="modal-actions"><button className="btn" onClick={()=>booking(b.id)}>Folio</button>
      <button className="btn primary mm-big" disabled={busy||!c.clear||!canCheckout} onClick={()=>run({...mmCheckoutCommand(b.id),...(ota.on&&!split?{otaCredit:true,key:'checkout:'+b.id+':ota'}:{})},'Checkout completed. Housekeeping task created.',{after:()=>open(sfOpen(b,'invoice','A'))})}>Confirm checkout <ArrowRight size={18}/></button></div></>;
}

export function MmInvoice({id}:{id?:string}){
  const {s,booking,modal}=useHcc();
  const [chosen,setChosen]=React.useState<string|undefined>(undefined); // F8-SF-UI: one document per folio
  const b0=s.bookings.find(x=>x.id===id);
  if(!b0)return <Empty title="This record is unavailable"/>;
  const fol=sfCurrent(b0,chosen??sfFolioOfModal(b0,modal?.room)),b=sfIsSplit(b0)?sfView(b0,fol):b0; // F8-SF-UI
  const t=totals(b),cc=b.receipts.filter(r=>r.kind==='company-credit').reduce((a,r)=>a+r.amount,0);
  return <><SfSwitcher b={b0} value={fol} onChange={setChosen} mobile/><SfInvoiceList b={b0}/>{/* F8-SF-UI */}<div className="invoice mm-invoice">
      <div className="invoice-top mm-inv-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>{process.env.HCC_BUILD==='production'?'Hotel Control Centre':'Hotel Control Centre · Fictional property'}</p></div></div>
      {process.env.HCC_BUILD==='production'?<div className="invoice-stamp">Folio summary — the tax invoice is issued and kept on the hotel server</div>
        :<div className="invoice-stamp" aria-label="Demonstration — not a tax invoice">Demonstration — not a tax invoice · fictional records only</div>}
      <div className="mm-inv-meta">
        <div><small>GUEST</small><strong>{b.guest}</strong><span>Room {b.room} · {plural(b.people,'guest')} · {bookingRef(b)}</span>{b.company&&<span>Bill to: {b.company.name}{b.company.gstin?` · ${process.env.HCC_BUILD==='production'?'GSTIN':'GSTIN (placeholder)'} ${b.company.gstin}`:''}</span>}</div>
        <div><small>{b.invoiceNo?(process.env.HCC_BUILD==='production'?'TAX INVOICE NO.':'SAMPLE INVOICE NO.'):'FOLIO PREVIEW'}</small><strong>{b.invoiceNo??'Not yet issued'}</strong><span>{shortDate(b.arrival)} — {shortDate(b.departure)}</span><span>{process.env.HCC_BUILD==='production'?(b.invoiceNo?'Issued by the hotel server':'Issued after checkout'):b.invoiceNo?'Allocated once at checkout':'Number allocated once at checkout'}</span><IssueInvoiceAction b={b} className="btn small-btn no-print"/></div>
      </div>
      <table className="mm-inv-table"><thead><tr><th>Description</th><th>Date</th><th className="number">Net</th><th className="number">CGST</th><th className="number">SGST</th></tr></thead>
        <tbody>{b.lines.map(l=><tr key={l.id}><td data-label="Description">{l.label}</td><td data-label="Date">{shortDate(l.date)}</td><td className="number" data-label="Net">{money(l.amount)}</td>{(()=>{const g=gstLineSplit(l);return <><td className="number" data-label="CGST"><span>{money(g.cgst)} <small>@{gstHalfPct(l.taxBps)}</small></span></td><td className="number" data-label="SGST"><span>{money(g.sgst)} <small>@{gstHalfPct(l.taxBps)}</small></span></td></>;})()}</tr>)}</tbody></table>
      <div className="mm-inv-sum">
        <div><span>Net charges</span><strong className="number">{money(t.net)}</strong></div>
        <div><span>CGST</span><strong className="number">{money(gstSplitLines(b.lines).cgst)}</strong></div>
        <div><span>SGST</span><strong className="number">{money(gstSplitLines(b.lines).sgst)}</strong></div>
        <div><span>Total including GST</span><strong className="number">{money(t.total)}</strong></div>
        <div><span>Net receipts applied</span><strong className="number">{money(t.paid)}</strong></div>
        {cc>0&&<div><span>On company credit (receivable)</span><strong className="number">{money(cc)}</strong></div>}
        <div className="mm-inv-bal"><span>Balance</span><strong className="number">{money(t.balance)}</strong></div>
        <div><span>Refundable deposit held separately</span><strong className="number">{money(t.deposit)}</strong></div>
      </div>
      {!!b.creditNotes?.length&&<div className="mm-inv-cn"><small>{process.env.HCC_BUILD==='production'?'CREDIT NOTES':'SAMPLE CREDIT NOTES'}</small>{b.creditNotes.map(c=><p key={c.id}>{c.no} · {shortDate(c.date)} · {money(c.amount)} before tax · {c.reason}</p>)}</div>}
      <p className="footnote">{process.env.HCC_BUILD==='production'?'GST from the dated GST master (CGST + SGST). The issued tax invoice is the hotel server’s document.':'GST from the dated GST master (CGST + SGST, illustrative — CA to confirm). All names, transactions and references are fictional. Demonstration — not a tax invoice.'}</p>
    </div>
    <p className="mm-help no-print">Prints on A4 through the phone’s print / share dialog (wireless printer or Save as PDF).</p>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={18}/> Print / share</button></div></>;
}
