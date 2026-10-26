// Day pack: one A4 printable pack (summary, cash, receipts, expenses) for a business date, printed through printDocument (ETP ReportPackageService.cs idea: one pack per day).
import React,{useState} from 'react';
import {bookingRefOf} from '../../domain/bookingRef';
import {Printer,Hotel,X} from 'lucide-react';
import {money,reportPack,closingSummary,isDayClosed,rbacCanDayPack} from '../../services/mock';
import {useHcc} from '../../app/context';
import {printDocument} from '../../app/native';
import {dateLabel,shortDate} from '../../components/ui';

import {ServerDayPackSheet} from './ServerDayPack';
/** Production: figures from the server's report; prototype: derived from the loaded records. */
export function DayPackSheet(p:{date:string;onClose:()=>void}){return process.env.HCC_BUILD==='production'?<ServerDayPackSheet {...p}/>:<LocalDayPackSheet {...p}/>;}
function LocalDayPackSheet({date,onClose}:{date:string;onClose:()=>void}){
  const {s}=useHcc();
  const rec=s.closings?.[date]??(date===s.date&&!isDayClosed(s)?closingSummary(s):undefined);
  const pack=reportPack(s,date,date);
  const rows=(tab:string,id:string)=>pack.tabs.find(t=>t.id===tab)?.metrics.find(m=>m.id===id)?.rows??[];
  const receipts=rows('receipts','net'),expenses=rows('expenses','total');
  const L=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong>{v}</strong></div>;
  const diff=rec?.cash.difference??0;
  return <div className="modal-overlay" onMouseDown={e=>{if(e.target===e.currentTarget)onClose();}}>
    <div className="modal wide" role="dialog" aria-modal="true" aria-label="Day pack">
      <button autoFocus className="modal-close icon-btn no-print" aria-label="Close day pack" onClick={onClose}><X size={21}/></button>
      <div className="f-print-doc invoice exp-pack">
        <div className="invoice-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>Day pack · {dateLabel(date)} · scenario {s.scenario}</p></div></div>
        <div className="invoice-stamp">DEMONSTRATION — FICTIONAL DATA · NOT A TAX DOCUMENT</div>
        {!s.closings?.[date]&&<p className="f-why">Preview only — this business day is not closed yet.</p>}
        <h3>1 · Summary</h3>
        {rec?<div className="summary-list">{L('Net revenue (excl. tax)',money(rec.revenue.net))}{L('GST (CGST + SGST)',money(rec.revenue.tax))}{L('Net receipts',money(rec.receipts.net))}{L('Expenses posted',money(rec.expenses.total))}{L('Occupancy',rec.occupancy.occupancy)}{L('ADR',rec.occupancy.adr)}{L('RevPAR',rec.occupancy.revpar)}</div>:<p className="muted">No closing record for this date.</p>}
        <h3>2 · Cash</h3>
        {rec&&<div className="summary-list">{L('Opening float',money(rec.cash.opening))}{L('Expected',money(rec.cash.expected))}{L('Counted',money(rec.cash.counted))}{L('Difference',diff===0?'None':(diff>0?'Over ':'Short ')+money(Math.abs(diff)))}</div>}
        <h3>3 · Receipts ({receipts.length})</h3>
        {rec&&<div className="summary-list">{Object.entries(rec.receipts.byTender).map(([t,v])=>L(t,money(v)))}</div>}
        <div className="table-wrap"><table><thead><tr><th>Ref</th><th>Booking / guest</th><th>Detail</th><th>Method</th><th className="number">Amount</th></tr></thead><tbody>
          {receipts.map(r=><tr key={r.ref}><td><small>{r.ref}</small></td><td>{r.booking?`${bookingRefOf(s.bookings,r.booking)} · ${r.guest??''}`:'—'}</td><td>{r.detail}</td><td>{r.method??'—'}</td><td className="number">{money(r.amount)}</td></tr>)}
          {!receipts.length&&<tr><td colSpan={5}>No receipts on this date.</td></tr>}</tbody></table></div>
        <h3>4 · Expenses ({expenses.length})</h3>
        <div className="table-wrap"><table><thead><tr><th>Ref</th><th>Detail</th><th>Method</th><th className="number">Amount</th></tr></thead><tbody>
          {expenses.map(r=><tr key={r.ref}><td><small>{r.ref}</small></td><td>{r.detail}</td><td>{r.method??'—'}</td><td className="number">{money(r.amount)}</td></tr>)}
          {!expenses.length&&<tr><td colSpan={4}>No expenses on this date.</td></tr>}</tbody></table></div>
        <p className="footnote">Generated at simulated clock {s.clock.replace('T',' ')} for {shortDate(date)}. Every figure is derived from source records. GST from the dated GST master (illustrative — CA to confirm).</p>
      </div>
      <div className="modal-actions no-print"><button className="btn" onClick={onClose}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save A4 PDF</button></div>
    </div></div>;
}
/** Day closing button: opens the pack for `date`. */
export function DayPackButton({date,label}:{date:string;label?:string}){
  const [on,setOn]=useState(false);const {s,uid}=useHcc();
  if(!rbacCanDayPack(s,uid))return null; // DEC-063 G4: Owner, Manager and Accountant (read-only closing view)
  return <><button className="btn" onClick={()=>setOn(true)}><Printer size={15}/> {label??'Day pack (A4 PDF)'}</button>{on&&<DayPackSheet date={date} onClose={()=>setOn(false)}/>}</>;
}
