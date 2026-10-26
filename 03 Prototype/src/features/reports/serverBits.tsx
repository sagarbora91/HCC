// Production-only pieces of Reports / day pack / history (Wave 4 gap G3): figures come from the server's report endpoint, never from the
// slices loaded on this device. Reached only from `process.env.HCC_BUILD==='production'` branches (tree-shaken out of the prototype).
import React,{useState} from 'react';
import {TrendingUp,Info,Banknote} from 'lucide-react';
import {money} from '../../services/mock';
import {useServerReport,useServerHistory,useServerCollections,useMayRead,type Fetched} from '../../clients/sync/reportsHooks';
import {SERVER_DETAIL_NOTE,fyMonths,type ServerReport} from '../../clients/sync/reports';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,Stat,shortDate,dateLabel} from '../../components/ui';

import {asOfText} from './asOf';
export {useServerReport,SERVER_DETAIL_NOTE,fyMonths,asOfText};

/** Loading / "as of" / error line under the filters. */
export function ServerReportStrip({f}:{f:Fetched<ServerReport>}){
  if(f.status==='idle')return null;
  if(f.status==='loading'&&!f.data)return <p className="sync-report-note" role="status" aria-live="polite">Loading the report from the server{'…'}</p>;
  if(f.status==='error')return <p className="sync-report-note" role="alert">{f.data?`The server could not be reached. Showing the report as of ${asOfText(f.data.asOfUtc)}.`:'The report could not be loaded from the server.'}{f.error?' '+f.error:''}</p>;
  return <p className="sync-report-note" role="status">Report from the server as of {asOfText(f.data?.asOfUtc)}{f.status==='loading'?' · refreshing…':''}. {SERVER_DETAIL_NOTE}</p>;
}
export function ServerDetailNote({label}:{label?:string}){
  return <section className="card f-drill" aria-label={'Details for '+(label??'this figure')}><div className="info-note"><Info size={17}/>{label?label+': ':''}{SERVER_DETAIL_NOTE}</div></section>;
}

/**
 * DEC-079: the Cashier sees the daily collections report but not the finance reports or the books. When the user may not read the daily
 * report (GET /reports/daily, property-wide PERM-RPT-002) but may read the collections (the Cashier's own-shift cell), the Reports page is
 * the collections report; everyone else gets `children` (the full Reports page).
 */
export function ServerReportsGate({children,phone=false}:{children:React.ReactNode;phone?:boolean}){
  const daily=useMayRead('GET /reports/daily'),collections=useMayRead('GET /cashier/collections');
  return !daily&&collections?<ServerCollections phone={phone}/>:<>{children}</>;
}

/** The daily collections report of one business date (net receipts, by tender, deposits received and returned). */
export function ServerCollections({phone=false}:{phone?:boolean}){
  const {s}=useHcc();const [date,setDate]=useState(s.date);
  const valid=/^\d{4}-\d{2}-\d{2}$/.test(date)&&date<=s.date;
  const f=useServerCollections(date,valid);const v=f.data?.view;
  const tenders=v?Object.entries(v.byTenderPaise).sort(([a],[b])=>a.localeCompare(b)):[];
  return <div className={phone?'ma-page':undefined}>
    {!phone&&<PageTitle title="Daily collections" sub={'Receipts from the hotel server · '+dateLabel(date)}/>}
    <section className="card f-filters" aria-label="Collections date">
      <label>Business date<input type="date" value={date} max={s.date} onChange={e=>setDate(e.target.value)}/></label>
      {!valid&&<p className="f-why" role="alert">Choose a business date on or before today ({shortDate(s.date)}).</p>}
      {f.status==='loading'&&!v&&<p className="sync-report-note" role="status" aria-live="polite">Loading the collections from the server{'…'}</p>}
      {f.status==='error'&&<p className="sync-report-note" role="alert">{v?`The server could not be reached. Showing the collections as of ${asOfText(f.data?.asOfUtc)}.`:'The collections could not be loaded from the server.'}</p>}
    </section>
    {!v?<Empty title={valid?'Waiting for the server':'No date chosen'} body={valid?'The collections report loads from the hotel server.':'Pick a business date.'}/>:<>
      <div className="stats">
        <Stat title="Net collections" value={money(v.collectionsPaise)} sub="Payments less refunds · deposits excluded"/>
        <Stat title="Deposits received" value={money(v.depositsReceivedPaise)} sub="Advance deposits taken"/>
        <Stat title="Deposits returned" value={money(v.depositsReturnedPaise)} sub="Deposits paid back"/>
      </div>
      <section className="card"><div className="section-title"><h2><Banknote size={18} aria-hidden="true"/> By tender</h2><span className="muted">{shortDate(v.businessDate)}</span></div>
        {tenders.length?<ul className="hist-months" aria-label="Collections by tender">{tenders.map(([t,p])=><li key={t}><span className="hist-label">{t}</span><span className="hist-val"><strong>{money(p)}</strong></span></li>)}</ul>
          :<p className="muted padded">No payments or refunds on this date.</p>}</section>
      <div className="info-note"><Info size={17}/>Your role sees the day’s collections only; the other finance reports and the accounting books are for the Owner, Manager and Accountant (DEC-079).</div>
    </>}
  </div>;
}

/** History summary over the financial year, one server report per month. */
export function ServerHistorySummary({compact=false}:{compact?:boolean}){
  const {s}=useHcc();const f=useServerHistory(s.date);
  if(f.status==='idle')return null;
  if(!f.data)return <section className={'card hist-card'+(compact?' hist-compact':'')} aria-label="History summary"><p className="sync-report-note" role={f.status==='error'?'alert':'status'}>{f.status==='error'?'The history could not be loaded from the server.':'Loading history from the server…'}</p></section>;
  const {months,total}=f.data,max=Math.max(...months.map(m=>m.roomRevenue),1);
  const tiles:[string,string,string][]=[
    ['Occupancy',total.occupancy,`${total.occupied.toLocaleString('en-IN')} of ${total.sellable.toLocaleString('en-IN')} room-nights`],
    ['ADR',total.adr,'Room revenue ÷ occupied nights'],['RevPAR',total.revpar,'Room revenue ÷ sellable nights'],
    ['Room revenue',money(total.roomRevenue),'Excl. GST'],['Net revenue',money(total.netRevenue),'Excl. GST']];
  return <section className={'card hist-card'+(compact?' hist-compact':'')} aria-label="History summary">
    <div className="section-title"><h2><TrendingUp size={18} aria-hidden="true"/> History summary</h2><span className="muted">{shortDate(f.data.from)} – {shortDate(f.data.to)} · from the server as of {asOfText(f.data.asOfUtc)}{f.status==='error'?' (server unreachable)':f.status==='loading'?' · refreshing…':''}</span></div>
    <div className="hist-tiles">{tiles.map(([l,v,n])=><div key={l} className="hist-tile"><span>{l}</span><strong>{v}</strong><small>{n}</small></div>)}</div>
    <ul className="hist-months" aria-label="Room revenue by month">{months.map(m=><li key={m.month}>
      <span className="hist-label">{m.label}</span>
      <span className="hist-bar" aria-hidden="true"><i style={{width:Math.max(2,Math.round(m.roomRevenue/max*100))+'%'}}/></span>
      <span className="hist-val"><strong>{money(m.roomRevenue)}</strong><small>{m.occupancy} occupied · ADR {m.adr}</small></span></li>)}</ul>
    <p className="sync-report-note">Stays and cancellations per month are on the server PC.</p>
  </section>;
}
