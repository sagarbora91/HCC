import React from 'react';
import {BedDouble,LogOut,CheckCircle2,IndianRupee,Wallet,Brush,Sunset,ArrowRight} from 'lucide-react';
import {money,drawer} from '../../services/mock';
import {useHcc} from '../../app/context';
import {BookingTable} from '../reservations/BookingTable';
import {CmpDueCard} from '../compliance/DueCard';
import {asOfText} from '../reports/asOf';
import {BackupStatusCard} from '../housekeeping/BackupStatus';
/** Room tiles carry a letter as well as a colour (WCAG 1.4.1); the legend spells the letters out. */
const ROOM_CODE:Record<string,string>={Occupied:'O',Ready:'R',Dirty:'D',Cleaning:'C',Inspection:'I',Blocked:'B'};
export function Overview(){
  const {s,user,m,act,open,go,tab,setTab,arrivals,departures,pending,roomStatus,permission}=useHcc();
  const stayList=tab==='Arrivals'?arrivals:tab==='Departures'?departures:s.bookings.filter(b=>b.status==='In house');
  const wait=!!m.live&&!m.live.ready,PD='…'; // production: the server's figures for today are still loading (or unreachable)
  const inspection=s.tasks.filter(t=>t.status==='Inspection').length;
  return <>
  <div className="welcome"><div><div className="eyebrow">YOUR HOTEL, AT A GLANCE</div><h1>A little clarity for a busy day.</h1><p>Welcome back, {user.name.split(' · ')[0]}. Here’s what’s happening at {s.settings.hotel}.</p></div>{act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}</div>
  <div className="stats">
    <div className="stat"><div className="stat-label">Room occupancy <BedDouble size={18}/></div><strong>{wait?PD:m.occupancy}<span>%</span></strong><div className="progress"><span style={{width:(wait?0:m.occupancy)+'%'}}/></div><small>{wait?'Waiting for the server…':`${m.occupied} occupied of ${m.sellable} sellable rooms`}</small></div>
    <div className="stat"><div className="stat-label">Today’s arrivals <LogOut size={18}/></div><strong>{arrivals.length.toString().padStart(2,'0')}</strong><small><span className="status-dot blue"/>Expected check-ins · {s.settings.checkin}</small></div>
    <div className="stat"><div className="stat-label">Room ready <CheckCircle2 size={18}/></div><strong>{String(m.available).padStart(2,'0')}</strong><small><span className="status-dot green"/>Vacant, clean and available now</small></div>
    <div className="stat"><div className="stat-label">Payment receipts <IndianRupee size={18}/></div><strong className="money-stat">{wait?PD:money(m.collections)}</strong><small>Today’s net receipts · deposits excluded</small></div>
  </div>
  {m.live&&<p className="sync-report-note" role="status" aria-live="polite">{m.live.ready?`Today’s occupancy, receipts, expenses and drawer cash come from the server’s report as of ${asOfText(m.live.asOfUtc)}${m.live.error?' (the server could not be reached since)':m.live.loading?' · refreshing…':''}.`:m.live.error?'Today’s figures could not be loaded from the server.':'Loading today’s figures from the server…'} Outstanding balances and deposits held are totals of the folios loaded from the server.</p>}
  {/* SCR-01 cash cards: each drills to its source page. Overview is gated by 'reports' (Owner, Manager, Accountant). */}
  <div className="small-stats overview-cash" aria-label="Cash and balances">
    <div><span>Expected drawer cash</span><strong>{m.live?(m.live.ready?money(m.live.drawer??0):PD):money(drawer(s))}</strong>{(permission('payments')||permission('close'))&&<button className="text-btn" onClick={()=>go(permission('payments')?'cashier':'closing')}>Cash shift <ArrowRight size={13}/></button>}</div>
    <div><span>Outstanding balances</span><strong>{money(m.outstanding)}</strong>{permission('billing')&&<button className="text-btn" onClick={()=>go('folios')}>Folios <ArrowRight size={13}/></button>}</div>
    <div><span>Deposits held</span><strong>{money(m.deposits)}</strong></div>
    <div><span>Today’s posted expenses</span><strong>{wait?PD:money(m.expenses)}</strong>{permission('expenses')&&<button className="text-btn" onClick={()=>go('expenses')}>Expenses <ArrowRight size={13}/></button>}</div>
  </div>
  <div className="dashboard-grid">
    <section className="card"><div className="section-title"><div><h2>Front desk today</h2><p>A smooth arrival starts here.</p></div>{act('reservations',()=>go('reservations'),'All reservations ↗','text-btn')}</div>
      <div className="tabs">{['Arrivals','Departures','In house'].map(t=><button key={t} onClick={()=>setTab(t)} className={t===tab?'active':''}>{t}<span>{t==='Arrivals'?arrivals.length:t==='Departures'?departures.length:m.occupied}</span></button>)}</div>
      <BookingTable list={stayList.slice(0,5)} compact/></section>
    <section className="card attention"><div className="section-title"><h2>Needs your attention</h2><span className="count">{pending+inspection}</span></div>
      <div className="attention-item"><span className="attention-icon orange"><Wallet size={19}/></span><div><strong>{pending} pending approvals</strong><p>Expenses and requested adjustments</p><button className="text-btn" onClick={()=>go('expenses')}>Review requests <ArrowRight size={14}/></button></div></div>
      <div className="attention-item"><span className="attention-icon blue"><Brush size={19}/></span><div><strong>{inspection} rooms awaiting inspection</strong><p>A final check before the next guest</p><button className="text-btn" onClick={()=>go('housekeeping')}>View housekeeping <ArrowRight size={14}/></button></div></div>
      <div className="closing-note"><Sunset size={22}/><div><strong>Keep the day in balance</strong><p>Review room charges and reconcile cash before closing.</p><button className="text-btn" onClick={()=>go('closing')}>Open day closing <ArrowRight size={14}/></button></div></div>
    </section>
  </div>
  <CmpDueCard/>
  <BackupStatusCard/>
  <section className="card"><div className="section-title"><div><h2>A view across your rooms</h2><p>Occupancy and cleaning status, in one place.</p></div>{act('reservations',()=>go('rooms'),'Open room board ↗','text-btn')}</div>
    <div className="floor-overview">{[1,2,3].map(f=><div className="floor-line" key={f}><span>Floor {f.toString().padStart(2,'0')}</span><div>{s.rooms.filter(r=>r.floor===f).map(r=><button className={'mini-room '+roomStatus(r).toLowerCase()} key={r.id} aria-label={`Room ${r.id}: ${roomStatus(r)}`} onClick={()=>open({type:'room',id:r.id})}>{r.id}<span className="mr-code" aria-hidden="true">{ROOM_CODE[roomStatus(r)]}</span></button>)}</div></div>)}</div>
    <div className="legend">{['Occupied','Ready','Dirty','Cleaning','Inspection','Blocked'].map(v=><span key={v}><i className={'dot '+v.toLowerCase()}/>{v} ({ROOM_CODE[v]})</span>)}</div>
  </section>
  </>;
}
