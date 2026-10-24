// M01 Home / Overview (phone): KPI cards 2-up, "Needs attention", arrivals / departures / in-house segmented list.
import React from 'react';
import {Plus,AlertTriangle,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,totals} from '../../services/mock';
import {KpiCard,ListCard,ChipFilter,Fab} from '../../app/mobile/components';
import {shortDate} from '../../components/ui';
import {mfAttention,mfInitials} from './logic';
import {MfStatus,useMfPages,useMfTop} from './shared';

export function MfHome(){
  useMfTop();
  const {s,m,user,go,open,booking,tab,setTab,setFilter,arrivals,departures,permission}=useHcc();
  const pages=useMfPages();const money_=permission('billing')||permission('reports');
  const goto=(page:string,filter?:string)=>{if(!pages.includes(page))return undefined;return ()=>{go(page);if(filter)setFilter(filter);};};
  const seg=['Arrivals','Departures','In house'].includes(tab)?tab:'Arrivals';
  const list=seg==='Arrivals'?arrivals:seg==='Departures'?departures:s.bookings.filter(b=>b.status==='In house');
  const att=mfAttention(s,permission,pages);
  return <div className="mf-page">
    <p className="mf-hello">Hello, {user.name.split(' · ')[0]} · {s.settings.hotel}</p>
    <div className="mf-kpis">
      <KpiCard label="Occupancy" value={m.occupancy+'%'} sub={`${m.occupied} of ${m.sellable} rooms`} onClick={goto('rooms','Occupied')}/>
      <KpiCard label="Arrivals today" value={arrivals.length} sub={`From ${s.settings.checkin}`} onClick={goto('arrivals')}/>
      <KpiCard label="Ready rooms" value={m.available} sub="Vacant, clean, available" onClick={goto('rooms','Ready')}/>
      {money_?<KpiCard label="Receipts today" value={money(m.collections)} sub="Net · deposits excluded" onClick={goto(pages.includes('cashier')?'cashier':'reports')}/>
        :<KpiCard label="Departures due" value={departures.length} sub="Check out today" onClick={goto('inhouse')}/>}
    </div>
    <h2 className="m-group">NEEDS ATTENTION</h2>
    {att.length?att.map(a=><ListCard key={a.id} tone={'mf-att mf-att-'+a.tone} title={<span className="mf-att-title">{a.tone==='orange'?<AlertTriangle size={18}/>:<Info size={18}/>}{a.title}</span>} sub={a.detail} onClick={goto(a.page,a.filter)}/>)
      :<p className="mf-empty">Nothing needs your attention right now.</p>}
    <h2 className="m-group">FRONT DESK TODAY</h2>
    <ChipFilter label="Front desk list" value={seg} onChange={v=>setTab(v)} options={[{id:'Arrivals',label:'Arrivals',count:arrivals.length},{id:'Departures',label:'Departures',count:departures.length},{id:'In house',label:'In house',count:m.occupied}]}/>
    {list.length?list.slice(0,8).map(b=><ListCard key={b.id} title={<span className="mf-lc-guest"><span className="mf-initials" aria-hidden>{mfInitials(b.guest)}</span>{b.guest}</span>}
        sub={`Room ${b.room} · ${shortDate(b.arrival)} – ${shortDate(b.departure)}`} meta={b.id}
        right={<>{<MfStatus status={b.status}/>}{money_&&<small className="mf-bal">Bal {money(totals(b).balance)}</small>}</>} onClick={()=>booking(b.id)}/>)
      :<p className="mf-empty">No {seg.toLowerCase()} to show.</p>}
    {list.length>8&&pages.includes(seg==='Arrivals'?'arrivals':'inhouse')&&<button className="btn full mf-more" onClick={()=>go(seg==='Arrivals'?'arrivals':'inhouse')}>See all {list.length}</button>}
    {permission('reservations')&&<Fab label="New booking" icon={Plus} onClick={()=>open({type:'new'})}/>}
  </div>;
}
