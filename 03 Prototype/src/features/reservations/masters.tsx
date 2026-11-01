// MA-5 · Front-desk lists on the screens (domain/streams/frontdesk-master.ts): policy line on the confirmation and the cancel dialog,
// ID-type note on the registration form, and "Bookings by source" on Reports › Occupancy. The lists themselves are edited in Master
// configuration › Front-desk policies › Lists (MA-0 MasterList); module screens only link there (MstLink).
import React,{useMemo} from 'react';
import {Info,Camera} from 'lucide-react';
import {money,fdmPolicyFor,fdmPolicyText,fdmCancelCharge,fdmNoShowCharge,fdmAdvanceDue,fdmSourceReport,fdmIdType,fdmIdCheck,fdmIdTypeProblem,fdmSegmentLabel,
  FDM_SOURCES,FDM_POLICIES,type Booking} from '../../services/mock';
import {useHcc} from '../../app/context';
import {MstLink} from '../masters/MasterList';

/** Policy of a booking in one line (confirmation, booking detail). */
export function FdmPolicyLine({b}:{b:Booking}){
  const {s}=useHcc();const p=fdmPolicyFor(s,b);const adv=fdmAdvanceDue(s,b);
  return <p className="footnote fdm-policy">Booking policy · {fdmPolicyText(p)}{adv?` Advance due: ${money(adv)}.`:''}</p>;
}

/** Cancel dialog note: what the policy would charge now (shown, never posted automatically). */
export function FdmCancelNote({b}:{b:Booking}){
  const {s}=useHcc();const c=fdmCancelCharge(s,b),ns=fdmNoShowCharge(s,b);
  return <div className="info-note fdm-cancel" role="note"><Info size={16}/><span>
    {c.policy?<>Policy <strong>{c.policy.name}</strong>: {c.amount?<>cancelling now would cost <strong>{money(c.amount)}</strong> (inside the {c.policy.freeCancelHours} h window).</>:'cancelling now is free.'}
      {ns?<> No-show charge {money(ns)}.</>:null} Charges are not posted automatically — post one from the folio if it is collected.</>:'No booking policy applies.'}
    {' '}Cancelling releases the room immediately.</span>
    <MstLink spec={FDM_POLICIES} label="Manage policies…"/></div>;
}

/** Registration form note for the chosen ID type: number check, photo needed, refused types. */
export function FdmIdTypeNote({type,nationality}:{type?:string;nationality?:string}){
  const {s}=useHcc();const r=fdmIdType(s,type);
  if(!type)return null;
  const why=fdmIdTypeProblem(s,type,nationality);
  return <small className={why?'gs-field-error':'muted fdm-id-note'}>{why||<>Check: {fdmIdCheck(type)}{r?.needsPhoto?<> · <Camera size={12} aria-hidden="true"/> photo of the document suggested (optional)</>:null}</>}</small>;
}

/** Reports › Occupancy: bookings by source and market segment for arrivals in the range (prototype figures from the bookings). */
export function FdmSourceReport({from,to}:{from:string;to:string}){
  const {s}=useHcc();
  const r=useMemo(()=>fdmSourceReport(s,from,to),[s.bookings,s.mastersX,s.channelsX,s.groupsX,from,to]); // eslint-disable-line react-hooks/exhaustive-deps
  return <section className="card fdm-report" aria-label="Bookings by source">
    <div className="section-title"><h2>Bookings by source</h2><span className="muted">arrivals {from} – {to} · <MstLink spec={FDM_SOURCES} label="Manage sources…"/></span></div>
    {!r.rows.length?<p className="muted">No arrivals in this range.</p>:<>
      <ul className="fdm-segs">{r.segments.map(g=><li key={g.segment}><strong>{g.label}</strong><span>{g.bookings} booking{g.bookings===1?'':'s'} · {g.nights} nights · {money(g.roomRevenue)}{g.commission?` · commission ${money(g.commission)}`:''}</span></li>)}</ul>
      <div className="table-wrap"><table className="table fdm-table"><thead><tr><th>Source</th><th>Segment</th><th className="num">Bookings</th><th className="num">Cancelled / no-show</th><th className="num">Nights</th><th className="num">Room charges</th><th className="num">Commission</th></tr></thead>
        <tbody>{r.rows.map(x=><tr key={x.code||'other'}><td>{x.name}{x.retired?' (retired)':''}</td><td>{fdmSegmentLabel(x.segment)}</td><td className="num">{x.bookings}</td><td className="num">{x.cancelled}</td><td className="num">{x.nights}</td>
          <td className="num">{money(x.roomRevenue)}</td><td className="num">{x.commission?money(x.commission):'—'}</td></tr>)}</tbody>
        <tfoot><tr><th>Total</th><th/><th className="num">{r.total.bookings}</th><th/><th className="num">{r.total.nights}</th><th className="num">{money(r.total.roomRevenue)}</th><th className="num">{r.total.commission?money(r.total.commission):'—'}</th></tr></tfoot></table></div>
      <p className="muted">Room charges = agreed rate × nights before GST; commission = the source’s commission % (OTA rows without one use the channel’s rate). Cancelled, no-show and expired bookings are counted apart.</p></>}
  </section>;
}
