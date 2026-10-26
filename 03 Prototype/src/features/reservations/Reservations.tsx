import React,{useMemo,useState} from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {useHcc} from '../../app/context';
import {resStatus} from '../../services/mock';
import {PageTitle,SearchBox} from '../../components/ui';
import {BookingTable} from './BookingTable';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
const STATUSES=['All','Held','Expired','Confirmed','In house','Departed','Cancelled','No show'];
/** Reservations list; with folio=true it is the "Guests & folios" view of the same records. Status uses the derived hold state (Held / Expired). */
export function Reservations({folio=false}:{folio?:boolean}){
  const {s,act,open,search,setSearch,filter,setFilter}=useHcc();
  const [source,setSource]=useState('All'),[date,setDate]=useState('');
  const sources=useMemo(()=>[...new Set(s.bookings.map(b=>b.source))].sort(),[s.bookings]);
  const q=search.toLowerCase();
  // Filtered list only recomputes when the bookings, the clock (hold expiry) or a filter changes; only the visible page is rendered (usePaged).
  const list=useMemo(()=>s.bookings.filter(b=>(filter==='All'||resStatus(s,b)===filter)&&(source==='All'||b.source===source)&&(!date||b.arrival<=date&&b.departure>date)
    &&`${b.guest} ${bookingRef(b)} ${b.room} ${b.phone||''} ${b.company?.name||''}`.toLowerCase().includes(q)),[s.bookings,s.clock,filter,source,date,q]); // eslint-disable-line react-hooks/exhaustive-deps
  const pg=usePaged(list,DESKTOP_PAGE);
  return <><PageTitle title={folio?'Guests & folios':'Reservations'} sub={folio?'Charges, receipts and every guest’s balance, connected.':'From the first enquiry to a warm welcome.'} action={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}/>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Search guest, phone, room or booking…"/><div className="filters">{STATUSES.map(x=><button key={x} className={filter===x?'active':''} aria-pressed={filter===x} onClick={()=>setFilter(x)}>{x}</button>)}</div></div>
    <div className="res-list-filters"><label>Source<select value={source} onChange={e=>setSource(e.target.value)}><option value="All">All sources</option>{sources.map(x=><option key={x}>{x}</option>)}</select></label>
      <label>Staying on<input type="date" value={date} onChange={e=>setDate(e.target.value)}/></label>{date&&<button className="text-btn" onClick={()=>setDate('')}>Clear date</button>}
      <span className="muted">{list.length} of {s.bookings.length} bookings</span></div>
    <section className="card"><BookingTable list={pg.shown}/><ShowMore p={pg}/></section>
    <p className="footnote">All names and records are fictional. Tax figures use the dated GST master (illustrative — CA to confirm).</p></>;
}
