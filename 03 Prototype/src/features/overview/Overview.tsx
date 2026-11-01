import React from 'react';
import {Wallet,Brush,Sunset,ArrowRight} from 'lucide-react';
import {money,drawer,mstFloorGroups,rmsListed} from '../../services/mock';
import {useHcc} from '../../app/context';
import {BookingTable} from '../reservations/BookingTable';
import {CmpDueCard} from '../compliance/DueCard';
import {asOfText} from '../reports/asOf';
import {BackupStatusCard} from '../housekeeping/BackupStatus';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip} from '../../components/fit';
/** Room tiles carry a letter as well as a colour (WCAG 1.4.1); the legend spells the letters out. */
const ROOM_CODE:Record<string,string>={Occupied:'O',Ready:'R',Dirty:'D',Cleaning:'C',Inspection:'I',Blocked:'B'};
const OV_TABS=[{id:'arrivals',label:'Arrivals'},{id:'departures',label:'Departures'},{id:'in-house',label:'In house'}] as const;
const OV_IDS=['arrivals','departures','in-house'] as const;
type OvTab=typeof OV_IDS[number];
export function Overview(){
  const {s,user,m,act,open,go,arrivals,departures,pending,roomStatus,permission}=useHcc();
  // FIT (DEC-194/196, contract §6 'overview'): the Arrivals / Departures / In house tabs are page sub-screens (FitTabs), the four headline
  // figures sit in the toolbar StatStrip, and every other block keeps its content in a panel of three columns that each scroll on their own.
  const [tab,setTab]=useFitTab<OvTab>('overview',OV_IDS,'arrivals');
  const inHouse=s.bookings.filter(b=>b.status==='In house');
  const stayList=tab==='arrivals'?arrivals:tab==='departures'?departures:inHouse;
  const wait=!!m.live&&!m.live.ready,PD='…'; // production: the server's figures for today are still loading (or unreachable)
  const inspection=s.tasks.filter(t=>t.status==='Inspection').length;
  const counts:Record<OvTab,number>={arrivals:arrivals.length,departures:departures.length,'in-house':m.occupied};
  return <FitPage title="A little clarity for a busy day." sub={`Your hotel, at a glance · Welcome back, ${user.name.split(' · ')[0]}. Here’s what’s happening at ${s.settings.hotel}.`}
    actions={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}
    toolbar={<StatStrip items={[
      {title:'Room occupancy',value:wait?PD:m.occupancy+'%',sub:wait?'Waiting for the server…':`${m.occupied} occupied of ${m.sellable} sellable rooms`},
      {title:'Today’s arrivals',value:arrivals.length.toString().padStart(2,'0'),sub:`Expected check-ins · ${s.settings.checkin}`},
      {title:'Room ready',value:String(m.available).padStart(2,'0'),sub:'Vacant, clean and available now',tone:'ok'},
      {title:'Payment receipts',value:wait?PD:money(m.collections),sub:'Today’s net receipts · deposits excluded'}]}/>}
    tabs={<FitTabs page="overview" value={tab} onChange={setTab} tabs={OV_TABS.map(t=>({...t,count:counts[t.id]}))}/>}>
    <div className="fit-ov-cols">
      <ScrollPanel title="Front desk today" label={`Front desk today: ${OV_TABS.find(t=>t.id===tab)!.label}`} actions={act('reservations',()=>go('reservations'),'All reservations ↗','text-btn')}>
        <BookingTable list={stayList} compact/>
      </ScrollPanel>
      <ScrollPanel title="A view across your rooms" label="Rooms by floor and status" actions={act('reservations',()=>go('rooms'),'Open room board ↗','text-btn')}
        footer={<div className="legend">{['Occupied','Ready','Dirty','Cleaning','Inspection','Blocked'].map(v=><span key={v}><i className={'dot '+v.toLowerCase()}/>{v} ({ROOM_CODE[v]})</span>)}</div>}>
        <div className="floor-overview">{mstFloorGroups(s).map(({floor:f,label})=><div className="floor-line" key={f}><span>{label}</span><div>{s.rooms.filter(r=>r.floor===f&&rmsListed(r)).map(r=><button className={'mini-room '+roomStatus(r).toLowerCase()} key={r.id} aria-label={`Room ${r.id}: ${roomStatus(r)}`} onClick={()=>open({type:'room',id:r.id})}>{r.id}<span className="mr-code" aria-hidden="true">{ROOM_CODE[roomStatus(r)]}</span></button>)}</div></div>)}</div>
      </ScrollPanel>
      <ScrollPanel title="Needs your attention" label="Attention, cash and balances, compliance and backup" actions={<span className="count">{pending+inspection}</span>}>
        <div className="attention fit-ov-attention">
          <div className="attention-item"><span className="attention-icon orange"><Wallet size={19}/></span><div><strong>{pending} pending approvals</strong><p>Expenses and requested adjustments</p><button className="text-btn" onClick={()=>go('expenses')}>Review requests <ArrowRight size={14}/></button></div></div>
          <div className="attention-item"><span className="attention-icon blue"><Brush size={19}/></span><div><strong>{inspection} rooms awaiting inspection</strong><p>A final check before the next guest</p><button className="text-btn" onClick={()=>go('housekeeping')}>View housekeeping <ArrowRight size={14}/></button></div></div>
          <div className="closing-note"><Sunset size={22}/><div><strong>Keep the day in balance</strong><p>Review room charges and reconcile cash before closing.</p><button className="text-btn" onClick={()=>go('closing')}>Open day closing <ArrowRight size={14}/></button></div></div>
        </div>
        {/* SCR-01 cash cards: each drills to its source page. Overview is gated by 'reports' (Owner, Manager, Accountant). */}
        <div className="small-stats overview-cash" aria-label="Cash and balances">
          <div><span>Expected drawer cash</span><strong>{m.live?(m.live.ready?money(m.live.drawer??0):PD):money(drawer(s))}</strong>{(permission('payments')||permission('close'))&&<button className="text-btn" onClick={()=>go(permission('payments')?'cashier':'closing')}>Cash shift <ArrowRight size={13}/></button>}</div>
          <div><span>Outstanding balances</span><strong>{money(m.outstanding)}</strong>{permission('billing')&&<button className="text-btn" onClick={()=>go('folios')}>Folios <ArrowRight size={13}/></button>}</div>
          <div><span>Deposits held</span><strong>{money(m.deposits)}</strong></div>
          <div><span>Today’s posted expenses</span><strong>{wait?PD:money(m.expenses)}</strong>{permission('expenses')&&<button className="text-btn" onClick={()=>go('expenses')}>Expenses <ArrowRight size={13}/></button>}</div>
        </div>
        {m.live&&<p className="sync-report-note" role="status" aria-live="polite">{m.live.ready?`Today’s occupancy, receipts, expenses and drawer cash come from the server’s report as of ${asOfText(m.live.asOfUtc)}${m.live.error?' (the server could not be reached since)':m.live.loading?' · refreshing…':''}.`:m.live.error?'Today’s figures could not be loaded from the server.':'Loading today’s figures from the server…'} Outstanding balances and deposits held are totals of the folios loaded from the server.</p>}
        <CmpDueCard/>
        <BackupStatusCard/>
      </ScrollPanel>
    </div>
  </FitPage>;
}
