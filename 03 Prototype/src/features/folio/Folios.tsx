// Stream C · "Guests & folios" page (SCR-08 list): balances, posted vs agreed nights, invoices and folio requests.
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import React from 'react';
import {ArrowUpRight} from 'lucide-react';
import {money,totals,folioNights,folioApprovalStatus,folioKind,FOLIO_APPROVAL_KINDS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,SearchBox,Pill,Empty,shortDate} from '../../components/ui';
import {FolioApprovalRow} from './modals';
const FILTERS=['All','In house','Due out','Balance due','Departed','Confirmed'];
export function Folios(){
  const {s,act,open,booking,search,setSearch,filter,setFilter}=useHcc();
  const q=search.toLowerCase();
  const list=s.bookings.filter(b=>{
    const t=totals(b);
    const ok=filter==='All'||(filter==='Due out'?b.status==='In house'&&b.departure<=s.date:filter==='Balance due'?t.balance>0:b.status===filter);
    return ok&&`${b.guest} ${b.id} ${b.room} ${b.company?.name??''} ${b.invoiceNo??''}`.toLowerCase().includes(q);
  });
  const pg=usePaged(list,DESKTOP_PAGE);
  const requests=s.approvals.filter(a=>FOLIO_APPROVAL_KINDS.includes(folioKind(a))).filter(a=>{const st=folioApprovalStatus(s,a);return st==='Pending'||(st==='Approved'&&a.kind==='refund')||st==='Expired'&&a.status==='Pending';}).slice().reverse();
  const outstanding=s.bookings.reduce((a,b)=>a+Math.max(0,totals(b).balance),0);
  return <><PageTitle title="Guests & folios" sub="Charges, receipts and every guest’s balance, connected." action={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}/>
    <div className="small-stats"><div><span>Outstanding balances</span><strong>{money(outstanding)}</strong></div><div><span>In house</span><strong>{s.bookings.filter(b=>b.status==='In house').length}</strong></div><div><span>Folio requests pending</span><strong>{requests.filter(a=>folioApprovalStatus(s,a)==='Pending').length}</strong></div><div><span>Approved refunds to execute</span><strong>{requests.filter(a=>a.kind==='refund'&&a.status==='Approved').length}</strong></div></div>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Search guest, room, company or invoice…"/><div className="filters">{FILTERS.map(x=><button key={x} className={filter===x?'active':''} aria-pressed={filter===x} onClick={()=>setFilter(x)}>{x}</button>)}</div></div>
    <section className="card"><div className="table-wrap"><table><thead><tr><th>Guest / booking</th><th>Room</th><th>Stay</th><th>Nights posted</th><th>Status</th><th>Balance</th><th>Invoice</th><th></th></tr></thead><tbody>
      {pg.shown.map(b=>{const t=totals(b),n=folioNights(b);return <tr key={b.id}>
        <td><button className="guest-link" onClick={()=>booking(b.id)}><span className="avatar">{b.guest.split(' ').map(x=>x[0]).slice(0,2).join('')}</span><span><strong>{b.guest}</strong><small>{b.id}{b.company?' · '+b.company.name:' · '+b.source}</small></span></button></td>
        <td><span className="room-number">{b.room}</span></td>
        <td>{shortDate(b.arrival)} — {shortDate(b.departure)}{b.status==='In house'&&b.departure<=s.date&&<small>Due out today</small>}</td>
        <td>{n.posted} of {n.agreed}</td>
        <td><Pill tone={b.status==='In house'?'green':b.status==='Confirmed'?'blue':'neutral'}>{b.status}</Pill></td>
        <td className="number">{money(t.balance)}{t.balance>0?<small>due</small>:t.balance<0?<small>credit</small>:null}</td>
        <td>{b.invoiceNo??<span className="muted">—</span>}</td>
        <td><button className="icon-btn" title={'Open folio for '+b.guest} aria-label={'Open folio for '+b.guest} onClick={()=>booking(b.id)}><ArrowUpRight size={18}/></button></td></tr>;})}
    </tbody></table>{!list.length&&<Empty title="No folios match" body="Try another filter or search."/>}</div><ShowMore p={pg}/></section>
    <section className="card"><div className="section-title"><h2>Folio requests</h2><span className="muted">Refunds, company credit, corrections, credit notes and discounts · no self-approval</span></div>
      {!requests.length?<Empty title="Nothing waiting" body="Requests that need a decision or an approved refund awaiting execution appear here."/>:<div className="request-list">{requests.map(a=><FolioApprovalRow key={a.id} a={a} showGuest/>)}</div>}</section>
    <p className="footnote">All names and records are fictional. Tax figures use the dated GST master (illustrative — CA to confirm). Room nights are posted by the nightly audit.</p></>;
}
