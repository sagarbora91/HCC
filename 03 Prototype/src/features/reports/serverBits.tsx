// Production-only pieces of Reports / day pack / history (Wave 4 gap G3): figures come from the server's report endpoint, never from the
// slices loaded on this device. Reached only from `process.env.HCC_BUILD==='production'` branches (tree-shaken out of the prototype).
import React from 'react';
import {TrendingUp,Info} from 'lucide-react';
import {money} from '../../services/mock';
import {useServerReport,useServerHistory,type Fetched} from '../../clients/sync/reportsHooks';
import {SERVER_DETAIL_NOTE,fyMonths,type ServerReport} from '../../clients/sync/reports';
import {useHcc} from '../../app/context';
import {shortDate} from '../../components/ui';

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
