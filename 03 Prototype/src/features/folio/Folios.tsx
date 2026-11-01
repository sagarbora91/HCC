// Stream C · "Guests & folios" page (SCR-08 list): balances, posted vs agreed nights, invoices and folio requests.
// FIT (DEC-194…196, contract §6 'folios' = split): sub-screen tabs "Folios" (the folio list left, the chosen folio right — the same folio
// record the booking dialog shows, with every one of its actions) and "Folio requests" (the requests list, which would not fit under the list).
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {bookingRef} from '../../domain/bookingRef';
import React,{useMemo,useState} from 'react';
import {ArrowUpRight} from 'lucide-react';
import {money,totals,folioNights,folioApprovalStatus,folioKind,FOLIO_APPROVAL_KINDS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {SearchBox,Pill,Empty,shortDate} from '../../components/ui';
import {FitPage,ScrollPanel,SplitView,FitTabs,useFitTab,FitNote} from '../../components/fit';
import {FolioApprovalRow,BookingModal} from './modals';
const FILTERS=['All','In house','Due out','Balance due','Departed','Confirmed'];
const FOLIO_TABS=['folios','folio-requests'] as const;
type FolioTab=typeof FOLIO_TABS[number];
export function Folios(){
  const {s,act,open,booking,search,setSearch,filter,setFilter}=useHcc();
  const [tab,setTab]=useFitTab<FolioTab>('folios',FOLIO_TABS,'folios');
  const [sel,setSel]=useState<string|null>(null);
  const q=search.toLowerCase();
  const list=useMemo(()=>s.bookings.filter(b=>{
    const t=totals(b);
    const ok=filter==='All'||(filter==='Due out'?b.status==='In house'&&b.departure<=s.date:filter==='Balance due'?t.balance>0:b.status===filter);
    return ok&&`${b.guest} ${bookingRef(b)} ${b.room} ${b.company?.name??''} ${b.invoiceNo??''}`.toLowerCase().includes(q);
  }),[s.bookings,s.date,filter,q]);
  const pg=usePaged(list,DESKTOP_PAGE);
  const requests=s.approvals.filter(a=>FOLIO_APPROVAL_KINDS.includes(folioKind(a))).filter(a=>{const st=folioApprovalStatus(s,a);return st==='Pending'||(st==='Approved'&&a.kind==='refund')||st==='Expired'&&a.status==='Pending';}).slice().reverse();
  const outstanding=useMemo(()=>s.bookings.reduce((a,b)=>a+Math.max(0,totals(b).balance),0),[s.bookings]);
  const pendingCount=requests.filter(a=>folioApprovalStatus(s,a)==='Pending').length;
  const open1=sel&&s.bookings.some(b=>b.id===sel)?sel:null;
  // The four figures keep their small-stats markup and font sizes (DEC-195), laid out as one strip (fit-stats).
  const stats=<div className="small-stats fit-stats fol-stats" role="list" aria-label="Folio totals"><div role="listitem"><span>Outstanding balances</span><strong>{money(outstanding)}</strong></div><div role="listitem"><span>In house</span><strong>{s.bookings.filter(b=>b.status==='In house').length}</strong></div><div role="listitem"><span>Folio requests pending</span><strong>{pendingCount}</strong></div><div role="listitem"><span>Approved refunds to execute</span><strong>{requests.filter(a=>a.kind==='refund'&&a.status==='Approved').length}</strong></div></div>;
  const NOTE='All names and records are fictional. Tax figures use the dated GST master (illustrative — CA to confirm). Room nights are posted by the nightly audit.';
  const folioList=<ScrollPanel label="Folio list" footer={<>{pg.more&&<ShowMore p={pg}/>}<FitNote text={NOTE}/></>}>
    <div className="table-wrap"><table><thead><tr><th>Guest / booking</th><th>Room</th><th>Stay</th><th>Nights posted</th><th>Status</th><th>Balance</th><th>Invoice</th><th></th></tr></thead><tbody>
      {pg.shown.map(b=>{const t=totals(b),n=folioNights(b);return <tr key={b.id} className={b.id===open1?'fol-row-on':undefined}>
        <td><button className="guest-link" aria-current={b.id===open1?'true':undefined} onClick={()=>setSel(b.id)}><span className="avatar">{b.guest.split(' ').map(x=>x[0]).slice(0,2).join('')}</span><span><strong>{b.guest}</strong><small>{bookingRef(b)}{b.company?' · '+b.company.name:' · '+b.source}</small></span></button></td>
        <td><span className="room-number">{b.room}</span></td>
        <td>{shortDate(b.arrival)} — {shortDate(b.departure)}{b.status==='In house'&&b.departure<=s.date&&<small>Due out today</small>}</td>
        <td>{n.posted} of {n.agreed}</td>
        <td><Pill tone={b.status==='In house'?'green':b.status==='Confirmed'?'blue':'neutral'}>{b.status}</Pill></td>
        <td className="number">{money(t.balance)}{t.balance>0?<small>due</small>:t.balance<0?<small>credit</small>:null}</td>
        <td>{b.invoiceNo??<span className="muted">—</span>}</td>
        <td><button className="icon-btn" title={'Open folio for '+b.guest} aria-label={'Open folio for '+b.guest} onClick={()=>booking(b.id)}><ArrowUpRight size={18}/></button></td></tr>;})}
    </tbody></table>{!list.length&&<Empty title="No folios match" body="Try another filter or search."/>}</div></ScrollPanel>;
  const requestsPanel=<ScrollPanel title="Folio requests" label="Folio requests" actions={<span className="muted">Refunds, company credit, corrections, credit notes and discounts · no self-approval</span>}>
    {!requests.length?<Empty title="Nothing waiting" body="Requests that need a decision or an approved refund awaiting execution appear here."/>:<div className="request-list">{requests.map(a=><FolioApprovalRow key={a.id} a={a} showGuest/>)}</div>}</ScrollPanel>;
  return <FitPage title="Guests & folios" sub="Charges, receipts and every guest’s balance, connected." layout={tab==='folios'?'split':'fill'}
      actions={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}
      toolbar={tab==='folios'?<><SearchBox value={search} onChange={setSearch} placeholder="Search guest, room, company or invoice…"/><div className="filters">{FILTERS.map(x=><button key={x} className={filter===x?'active':''} aria-pressed={filter===x} onClick={()=>setFilter(x)}>{x}</button>)}</div>{stats}</>:stats}
      tabs={<FitTabs page="folios" value={tab} onChange={setTab} tabs={[{id:'folios',label:'Folios',count:list.length},{id:'folio-requests',label:'Folio requests',count:requests.length,badge:pendingCount?'warn':undefined}]}/>}>
    {tab==='folios'?<SplitView label="Folios" ratio="1fr 1fr" onCloseDetail={()=>setSel(null)} list={folioList}
        detail={open1?<div className="fol-record"><BookingModal id={open1}/></div>:null}
        empty={<Empty title="Pick a folio" body="Its charges, receipts, requests and actions open here. The arrow button opens the same folio as a dialog."/>}/>
      :requestsPanel}
  </FitPage>;
}
