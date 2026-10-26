// Day pack on the production build (Wave 4 gap G3): every figure is the server's report for the date (same calculator as the closing
// snapshot), not a client sum over loaded slices. Receipt and expense lines are not in the aggregate report: a note says so.
import React from 'react';
import {Printer,Hotel,X} from 'lucide-react';
import {money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {printDocument} from '../../app/native';
import {dateLabel,shortDate} from '../../components/ui';
import {useServerReport,asOfText,SERVER_DETAIL_NOTE} from '../reports/serverBits';
import {closingFromDailyView} from '../../clients/sync/reports';

export function ServerDayPackSheet({date,onClose}:{date:string;onClose:()=>void}){
  const {s}=useHcc();const f=useServerReport(date,date);
  const view=f.data?.view,rec=view?closingFromDailyView(view,date):undefined;
  const status=view?.dayStatus.find(d=>d.date===date)?.status;
  const L=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong>{v}</strong></div>;
  const diff=rec?.cash.difference??0;
  return <div className="modal-overlay" onMouseDown={e=>{if(e.target===e.currentTarget)onClose();}}>
    <div className="modal wide" role="dialog" aria-modal="true" aria-label="Day pack">
      <button autoFocus className="modal-close icon-btn no-print" aria-label="Close day pack" onClick={onClose}><X size={21}/></button>
      <div className="f-print-doc invoice exp-pack">
        <div className="invoice-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>Day pack · {dateLabel(date)}</p></div></div>
        <div className="invoice-stamp">FIGURES FROM THE HOTEL SERVER · NOT A TAX DOCUMENT</div>
        {f.status!=='error'&&!rec&&<p className="sync-report-note" role="status">Loading the day pack from the server…</p>}
        {f.status==='error'&&<p className="sync-report-note" role="alert">{rec?`The server could not be reached. Showing the figures as of ${asOfText(f.data?.asOfUtc)}.`:'The day pack could not be loaded from the server.'}</p>}
        {rec&&<>
          <p className="sync-report-note" role="status">As of {asOfText(f.data?.asOfUtc)} · {status==='Closed'?'this business day is closed':'Preview only — this business day is not closed yet.'}</p>
          <h3>1 · Summary</h3>
          <div className="summary-list">{L('Net revenue (excl. tax)',money(rec.revenue.net))}{L('GST (CGST + SGST)',money(rec.revenue.tax))}{L('Net receipts',money(rec.receipts.net))}{L('Expenses posted',money(rec.expenses.total))}{L('Occupancy',rec.occupancy.occupancy)}{L('ADR',rec.occupancy.adr)}{L('RevPAR',rec.occupancy.revpar)}</div>
          <h3>2 · Cash</h3>
          <div className="summary-list">{L('Opening float',money(rec.cash.opening))}{L('Expected',money(rec.cash.expected))}{L('Counted',money(rec.cash.counted))}{L('Difference',diff===0?'None':(diff>0?'Over ':'Short ')+money(Math.abs(diff)))}</div>
          <h3>3 · Receipts</h3>
          <div className="summary-list">{Object.entries(rec.receipts.byTender).map(([t,v])=>L(t,money(v)))}{L('Deposits received',money(rec.deposits.received))}{L('Deposits returned',money(rec.deposits.returned))}</div>
          <h3>4 · Expenses</h3>
          <div className="summary-list">{L('Drawer-cash expenses',money(rec.expenses.cash))}{L('Posted expenses (net of reversals)',money(rec.expenses.total))}</div>
          <p className="muted">Receipt and expense lines: {SERVER_DETAIL_NOTE}</p>
          <p className="footnote">Figures for {shortDate(date)} from the server report as of {asOfText(f.data?.asOfUtc)}. GST from the dated GST master (illustrative — CA to confirm).</p></>}
      </div>
      <div className="modal-actions no-print"><button className="btn" onClick={onClose}>Close</button><button className="btn primary" disabled={!rec} onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save A4 PDF</button></div>
    </div></div>;
}
