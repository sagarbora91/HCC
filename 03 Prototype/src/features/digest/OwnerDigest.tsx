// F4-T2 · Owner digest: the "Yesterday" card (Dashboard) and the printable page 'owner-digest'. Owner only (PERM-DGS-001).
import React,{useMemo,useState} from 'react';
import {Printer,ArrowRight} from 'lucide-react';
import {money,dgsCard,dgsCanView,dgsClosedDates,dgsYesterday,dgsSlice,DGS_REASON} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,Pill} from '../../components/ui';
import {printDocument} from '../../app/native';
import {FitPage,ScrollPanel} from '../../components/fit';

export function YesterdayCard(){
  const {s,uid,go}=useHcc();
  const d=dgsYesterday(s);
  const c=useMemo(()=>dgsCanView(s,uid)&&s.closings?.[d]?dgsCard(s,d):null,[s.closings,s.bookings,s.posX,s.counterX,s.approvals,uid,d]); // eslint-disable-line react-hooks/exhaustive-deps
  if(!c)return null;
  return <div className="attention-item"><span className="attention-icon blue"><ArrowRight size={19}/></span><div><strong>Yesterday · {c.occupancy.pct} occupied, {money(c.revenueTotal.net)} revenue</strong>
    <p>{money(c.collected)} collected · {c.pendingApprovals.reduce((t,x)=>t+x.count,0)} approvals waiting</p><button className="text-btn" onClick={()=>go('owner-digest')}>Open the digest <ArrowRight size={14}/></button></div></div>;
}
export function OwnerDigest(){
  const {s,uid}=useHcc();
  const dates=dgsClosedDates(s),[date,setDate]=useState(()=>dates.includes(dgsYesterday(s))?dgsYesterday(s):dates[0]??'');
  if(!dgsCanView(s,uid))return <FitPage title="Owner digest" sub="Owner only"><Empty title="Not permitted" body="The owner digest is for the Owner."/></FitPage>;
  const c=date&&s.closings?.[date]?dgsCard(s,date):null;
  const drafts=dgsSlice(s).drafts.filter(x=>x.date===date);
  const row=(k:string,v:React.ReactNode)=><tr key={k}><td>{k}</td><td className="number">{v}</td></tr>;
  return <FitPage title="Owner digest" sub="One page for a closed business day" actions={<button className="btn" onClick={()=>void printDocument()}><Printer size={15}/> Print</button>}
    toolbar={<label>Business date <select value={date} onChange={e=>setDate(e.target.value)}>{dates.map(d=><option key={d}>{d}</option>)}</select></label>}>
    {!c?<Empty title="No closed day yet" body="The digest is written when a business day is closed."/>:<>
      <ScrollPanel title={`Digest for ${c.date}`} label="Digest figures"><div className="table-wrap"><table><tbody>
        {row('Occupancy',`${c.occupancy.pct} · ${c.occupancy.sold} of ${c.occupancy.sellable} rooms`)}{row('ADR / RevPAR',`${c.occupancy.adr} / ${c.occupancy.revpar}`)}
        {c.revenue.map(x=>row('Revenue · '+x.label,`${money(x.net)} net · ${money(x.gross)} incl. GST`))}{row('Revenue total',`${money(c.revenueTotal.net)} net · ${money(c.revenueTotal.gross)} incl. GST`)}
        {c.collections.map(x=>row('Collected · '+x.tender,money(x.amount)))}{row('Collected total',money(c.collected))}
        {row('Advances received / adjusted / held',`${money(c.advances.received)} / ${money(c.advances.adjusted)} / ${c.advances.held===undefined?'—':money(c.advances.held)}`)}
        {row('Expenses',`${money(c.expenses.posted)} · ${c.expenses.approved} posted, ${c.expenses.pending} pending`)}{row('Cash in drawer at close',c.cashAtClose===undefined?'—':money(c.cashAtClose))}
        {row('Pending approvals',c.pendingApprovals.length?c.pendingApprovals.map(x=>`${x.kind} ${x.count}`).join(', '):'None')}
        {row('Reorder alerts',c.reorder)}{row('Complaints / lost & found over SLA',c.overdue.complaints+c.overdue.lostFound)}
        {row('Feedback',c.feedback.count?`${c.feedback.count} · ${c.feedback.average.toFixed(1)} stars`:'None')}
        {row('Tomorrow',`${c.tomorrow.arrivals} arrivals${c.tomorrow.rooms.length?' (rooms '+c.tomorrow.rooms.join(', ')+')':''} · ${c.tomorrow.events} events`)}
      </tbody></table></div></ScrollPanel>
      <ScrollPanel title="Outbox drafts" label="Held drafts" actions={<Pill tone="neutral">held</Pill>}><div className="padded">
        {drafts.length?drafts.map(d=><p key={d.id}><strong>{d.channel}{d.countsOnly?' · counts only':''}</strong> · {d.status} · {d.reason}</p>):<p className="muted">No drafts for this date.</p>}
        <p className="muted">{DGS_REASON}. Nothing is sent from here.</p></div></ScrollPanel></>}
  </FitPage>;
}
