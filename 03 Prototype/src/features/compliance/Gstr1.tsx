// DEC-072 (audit A03a-01) · GSTR-1 working summary — opened from the Compliance calendar (Owner and Accountant, like the calendar).
// Desktop dialog with tables; phone sheet with cards (no sideways scroll at 360 px). Prototype: built from the demo's issued documents
// (domain/gstr1.ts: folio sample invoices, restaurant RST invoices, credit notes); production: the hotel server's summary (GET /invoices/gstr1).
import React,{useMemo,useState} from 'react';
import {Printer,ShieldAlert} from 'lucide-react';
import {useHcc} from '../../app/context';
import {printDocument} from '../../app/native';
import {money,gstPct,gstr1Summary,gstr1Months,cmpHasAccess,GSTR1_SERIES_LABEL} from '../../services/mock';
import type {Gstr1Summary} from '../../services/mock';
import {Empty,dateLabel} from '../../components/ui';
import {loadServerGstr1} from '../../clients/sync/gstr1';
import {useServerFetch,type Fetched} from '../../clients/sync/reportsHooks';

const NO_FETCH:Fetched<Gstr1Summary>={status:'idle'};
const PLACE_NOTE='Place of supply: the hotel’s state, Maharashtra (27) — intra-state, CGST + SGST. B2CS is net of its credit notes; the RST invoice of a voided restaurant bill counts as cancelled and its credit note is listed.';

function useGstr1(){
  const {s,uid}=useHcc();
  const months=useMemo(()=>gstr1Months(s.date),[s.date]);
  const [month,setMonth]=useState(months[0]!.id);
  const m=months.find(x=>x.id===month)??months[0]!;
  // Production: the server's summary for the period (refetched on Refresh / after a command, like the server reports).
  const srv=process.env.HCC_BUILD==='production'?useServerFetch(`gstr1|${m.from}|${m.to}`,api=>loadServerGstr1(api,m.from,m.to)):NO_FETCH; // eslint-disable-line react-hooks/rules-of-hooks
  const local=useMemo(()=>process.env.HCC_BUILD==='production'?undefined:gstr1Summary(s,m.from,m.to),[s.bookings,s.posX,m.from,m.to]); // eslint-disable-line react-hooks/exhaustive-deps
  return {months,month,setMonth,m,srv,data:process.env.HCC_BUILD==='production'?srv.data:local,access:cmpHasAccess(s,uid)};
}
type G=ReturnType<typeof useGstr1>;

/** Production only: loading / unreachable lines (the prototype computes the summary on the device). */
function ServerStatus({g}:{g:G}){
  if(process.env.HCC_BUILD!=='production')return null;
  const f=g.srv;
  if(f.status==='loading'&&!f.data)return <p className="sync-report-note" role="status" aria-live="polite">Loading the GSTR-1 summary from the server{'…'}</p>;
  if(f.status==='error')return <p className="sync-report-note" role="alert">{f.data?'The server could not be reached. Showing the last summary loaded.':'The summary could not be loaded from the server.'}{f.error?' '+f.error:''}</p>;
  return null;
}
function Period({g}:{g:G}){
  return <label className="gstr1-period">Period<select value={g.month} onChange={e=>g.setMonth(e.target.value)}>{g.months.map(x=><option key={x.id} value={x.id}>{x.label}</option>)}</select></label>;
}
const net=(d:{count:number;cancelled:number})=>d.count-d.cancelled;

export function CmpGstr1Modal(){
  const g=useGstr1();const {closeModal}=useHcc();
  if(!g.access)return <Empty title="Owner and Accountant only" body="The GSTR-1 working summary is visible to the Owner and the Accountant."/>;
  const d=g.data,igst=!!d&&d.rates.some(r=>r.igst!==0);
  return <><div className="modal-heading"><div className="eyebrow">GST · WORKING AID FOR THE CA</div><h2>GSTR-1 working summary</h2>
    <p>Outward supplies from the issued documents — room and stay tax invoices (INV), restaurant tax invoices (RST) and credit notes. Not a return file.</p></div>
    <div className="gstr1-bar no-print"><Period g={g}/><span className="muted">{dateLabel(g.m.from)} – {dateLabel(g.m.to)}</span></div>
    <ServerStatus g={g}/>
    {d&&<div className="gstr1-doc">
      <h3>Rate-wise outward supplies</h3>
      {d.rates.length?<div className="table-wrap"><table className="gstr1-table"><thead><tr><th>Section</th><th className="number">Rate</th><th className="number">Taxable value</th>
        <th className="number">CGST</th><th className="number">SGST</th>{igst&&<th className="number">IGST</th>}<th className="number">Documents</th></tr></thead>
        <tbody>{d.rates.map(r=><tr key={r.section+':'+r.bps}><td>{r.section}</td><td className="number">{gstPct(r.bps)}</td><td className="number">{money(r.taxable)}</td>
          <td className="number">{money(r.cgst)}</td><td className="number">{money(r.sgst)}</td>{igst&&<td className="number">{money(r.igst)}</td>}<td className="number">{r.documents}</td></tr>)}</tbody></table></div>
        :<p className="muted">No documents were issued in this period.</p>}
      {d.sac.length>0&&<><h3>SAC summary (table 12)</h3><div className="table-wrap"><table className="gstr1-table"><thead><tr><th>SAC</th><th className="number">Rate</th><th className="number">Taxable value</th>
        <th className="number">CGST</th><th className="number">SGST</th></tr></thead>
        <tbody>{d.sac.map(r=><tr key={r.sac+':'+r.bps}><td>{r.sac}</td><td className="number">{gstPct(r.bps)}</td><td className="number">{money(r.taxable)}</td><td className="number">{money(r.cgst)}</td><td className="number">{money(r.sgst)}</td></tr>)}</tbody></table></div></>}
      {d.documents.length>0&&<><h3>Documents issued (table 13)</h3><div className="table-wrap"><table className="gstr1-table"><thead><tr><th>Series</th><th>From</th><th>To</th>
        <th className="number">Total</th><th className="number">Cancelled</th><th className="number">Net issued</th></tr></thead>
        <tbody>{d.documents.map(x=><tr key={x.docType}><td>{GSTR1_SERIES_LABEL[x.docType]??x.docType}</td><td>{x.first}</td><td>{x.last}</td><td className="number">{x.count}</td>
          <td className="number">{x.cancelled}</td><td className="number">{net(x)}</td></tr>)}</tbody></table></div></>}
      <div className="info-note"><ShieldAlert size={16}/><span>{d.note} {PLACE_NOTE}</span></div>
    </div>}
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" disabled={!d} onClick={()=>{void printDocument();}}><Printer size={16}/> Print / share</button></div></>;
}

/** Phone sheet: the same three tables as cards. */
export function CmpGstr1Mobile(){
  const g=useGstr1();const {closeModal}=useHcc();
  if(!g.access)return <Empty title="Owner and Accountant only"/>;
  const d=g.data;
  const row=(label:string,value:React.ReactNode)=><div className="gstr1-m-row"><span>{label}</span><strong>{value}</strong></div>;
  return <div className="gstr1-m">
    <div className="modal-heading"><h2>GSTR-1 working summary</h2><p>From the issued documents: room (INV) and restaurant (RST) tax invoices and credit notes.</p></div>
    <Period g={g}/>
    <ServerStatus g={g}/>
    {d&&<>
      <h3>Rate-wise supplies</h3>
      {!d.rates.length&&<p className="muted">No documents were issued in this period.</p>}
      {d.rates.map(r=><div key={r.section+':'+r.bps} className="card gstr1-m-card"><div className="gstr1-m-top"><strong>{r.section} · {gstPct(r.bps)}</strong><span>{r.documents} document{r.documents===1?'':'s'}</span></div>
        {row('Taxable value',money(r.taxable))}{row('CGST',money(r.cgst))}{row('SGST',money(r.sgst))}{r.igst!==0&&row('IGST',money(r.igst))}</div>)}
      {d.sac.length>0&&<h3>SAC summary</h3>}
      {d.sac.map(r=><div key={r.sac+':'+r.bps} className="card gstr1-m-card"><div className="gstr1-m-top"><strong>SAC {r.sac} · {gstPct(r.bps)}</strong></div>
        {row('Taxable value',money(r.taxable))}{row('CGST + SGST',money(r.cgst+r.sgst))}</div>)}
      {d.documents.length>0&&<h3>Documents issued</h3>}
      {d.documents.map(x=><div key={x.docType} className="card gstr1-m-card"><div className="gstr1-m-top"><strong>{GSTR1_SERIES_LABEL[x.docType]??x.docType}</strong></div>
        <span className="gstr1-m-range">{x.first} – {x.last}</span>{row('Total',x.count)}{row('Cancelled',x.cancelled)}{row('Net issued',net(x))}</div>)}
      <p className="gstr1-m-note">{d.note} {PLACE_NOTE}</p>
    </>}
    <button className="btn full" onClick={closeModal}>Close</button>
  </div>;
}
