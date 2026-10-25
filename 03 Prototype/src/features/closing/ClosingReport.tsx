// Closing summary (REQ-CLOSE-004) and its printable, watermarked report.
import {printDocument} from '../../app/native';
import React from 'react';
import {Printer,Hotel} from 'lucide-react';
import {ClosingRecord,money,closingSummary,isDayClosed} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,dateLabel} from '../../components/ui';

export function ClosingSummaryView({rec}:{rec:ClosingRecord}){
  const {s}=useHcc();const name=(id?:string)=>s.users.find(u=>u.id===id)?.name??id??'—';
  const L=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong>{v}</strong></div>;
  const diff=rec.cash.difference;
  return <div className="f-summary">
    <div><h4>Revenue (excl. tax)</h4><div className="summary-list">{L('Room',money(rec.revenue.room))}{L('Service',money(rec.revenue.service))}{L('Discounts',money(rec.revenue.discounts))}{L('Adjustments',money(rec.revenue.adjustments))}{L('Net revenue',money(rec.revenue.net))}{L('GST (CGST + SGST)',money(rec.revenue.tax))}</div></div>
    <div><h4>Receipts by tender</h4><div className="summary-list">{Object.entries(rec.receipts.byTender).map(([t,v])=>L(t,money(v)))}{L('Net receipts',money(rec.receipts.net))}{rec.receipts.companyCredit!==0&&L('Company credit',money(rec.receipts.companyCredit))}</div></div>
    {rec.outlet&&<div><h4>Restaurant & room service</h4><div className="summary-list">{Object.entries(rec.outlet.byMethod).map(([t,v])=>L(t,money(v)))}{L('Outlet receipts (net of voids)',money(rec.outlet.net))}{L('Charged to room folios',money(rec.outlet.roomFolio))}</div>
      <p className="footnote">Cash bills are counted in the front-desk drawer. Room charges are folio service lines, collected at checkout.</p></div>}
    <div><h4>Deposits</h4><div className="summary-list">{L('Received',money(rec.deposits.received))}{L('Returned',money(rec.deposits.returned))}{L('Held at close',money(rec.deposits.heldAtClose))}</div></div>
    <div><h4>Expenses</h4><div className="summary-list">{L('Drawer cash',money(rec.expenses.cash))}{L('UPI / bank',money(rec.expenses.nonCash))}{L('Vendor credit',money(rec.expenses.vendorCredit))}{L('Total posted',money(rec.expenses.total))}</div></div>
    <div><h4>Occupancy</h4><div className="summary-list">{L('Sellable rooms',rec.occupancy.sellable)}{L('Occupied rooms',rec.occupancy.occupied)}{L('Occupancy',rec.occupancy.occupancy)}{L('ADR',rec.occupancy.adr)}{L('RevPAR',rec.occupancy.revpar)}</div></div>
    <div><h4>Cash</h4><div className="summary-list">{L('Opening float',money(rec.cash.opening))}{rec.cash.restaurant!==undefined&&L('Incl. restaurant cash',money(rec.cash.restaurant))}{L('Expected',money(rec.cash.expected))}{L('Counted',money(rec.cash.counted))}{L('Difference',diff===0?'None':(diff>0?'Over ':'Short ')+money(Math.abs(diff)))}{rec.cash.approvedBy&&L('Accepted by',name(rec.cash.approvedBy))}</div></div>
    <div className="f-span"><h4>Exceptions</h4>{rec.exceptions.length?<ul className="f-list">{rec.exceptions.map(e=><li key={e}>{e}</li>)}</ul>:<p className="muted">None recorded.</p>}
      <p className="footnote">Night audit: {rec.nightAudit.stays} in-house stay{rec.nightAudit.stays===1?'':'s'} posted{rec.nightAudit.resumed?' · resumed after an interruption':''}. {rec.closedBy?`Closed by ${name(rec.closedBy)} at ${rec.closedAt.slice(11)} (simulated clock).`:'Preview — figures become final when the day is closed.'}</p></div>
  </div>;
}
/** Wide modal 'closing-report'; id = business date. Uses the stored record, or a live preview for the open day. */
export function ClosingReportModal({id}:{id?:string}){
  const {s,closeModal}=useHcc();const date=id||s.date;
  const rec=s.closings?.[date]??(date===s.date&&!isDayClosed(s)?closingSummary(s):undefined);
  return <><div className="f-print-doc invoice">
    <div className="invoice-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>Closing report · {dateLabel(date)} · scenario {s.scenario}</p></div></div>
    <div className="invoice-stamp">DEMONSTRATION — FICTIONAL DATA · NOT A TAX DOCUMENT</div>
    {!rec?<Empty title="No closing record" body="This date was closed before closing records were kept in this prototype."/>:<>
      {!s.closings?.[date]&&<p className="f-why">Preview only — this business day is still open.</p>}
      <ClosingSummaryView rec={rec}/></>}
    <p className="footnote">All figures derived from fictional source records. GST from the dated GST master (illustrative — CA to confirm).</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" disabled={!rec} onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}
