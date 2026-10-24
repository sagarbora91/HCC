// History summary card (FY26 scenario): FY-to-date occupancy, ADR, RevPAR, room revenue, stays and cancellations, with a bar list by month.
// Figures come from the stored closing records (historyMonths), so they equal the Reports totals for the same dates.
import React from 'react';
import {TrendingUp} from 'lucide-react';
import {money,historyMonths} from '../../services/mock';
import {useHcc} from '../../app/context';
import {shortDate} from '../../components/ui';

export function HistorySummary({compact=false}:{compact?:boolean}){
  const {s}=useHcc();
  const h=s.historyX;
  const data=React.useMemo(()=>historyMonths(s),[s.closings,s.bookings,s.historyX]); // eslint-disable-line react-hooks/exhaustive-deps
  if(!h||!data.months.length)return null;
  const {months,total}=data,max=Math.max(...months.map(m=>m.roomRevenue),1);
  const tiles:[string,string,string][]=[
    ['Occupancy',total.occupancy,`${total.occupied.toLocaleString('en-IN')} of ${total.sellable.toLocaleString('en-IN')} room-nights`],
    ['ADR',total.adr,'Room revenue ÷ occupied nights'],
    ['RevPAR',total.revpar,'Room revenue ÷ sellable nights'],
    ['Room revenue',money(total.roomRevenue),'Excl. synthetic tax'],
    ['Stays',total.stays.toLocaleString('en-IN'),'Checked out'],
    ['Cancellations',String(total.cancellations),`${total.noShows} no-shows`],
  ];
  return <section className={'card hist-card'+(compact?' hist-compact':'')} aria-label="History summary">
    <div className="section-title"><h2><TrendingUp size={18} aria-hidden="true"/> History summary</h2><span className="muted">FY 2026-27 · {shortDate(h.from)} – {shortDate(h.to)} · fictional</span></div>
    <div className="hist-tiles">{tiles.map(([l,v,n])=><div key={l} className="hist-tile"><span>{l}</span><strong>{v}</strong><small>{n}</small></div>)}</div>
    <ul className="hist-months" aria-label="Room revenue by month">{months.map(m=><li key={m.month}>
      <span className="hist-label">{m.label}</span>
      <span className="hist-bar" aria-hidden="true"><i style={{width:Math.max(2,Math.round(m.roomRevenue/max*100))+'%'}}/></span>
      <span className="hist-val"><strong>{money(m.roomRevenue)}</strong><small>{m.occupancy} occupied · ADR {m.adr} · {m.stays} stays · {m.cancellations} cancelled</small></span></li>)}</ul>
  </section>;
}
