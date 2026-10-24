// Module 16 · Linen & stock — phone page: KPIs, stock cards by location, quick count (NumberStepper), laundry cards, alerts.
import React,{useState} from 'react';
import {ArrowRightLeft,Truck,Printer} from 'lucide-react';
import {INV_LOCS,INV_COUNTABLE,INVLoc,invSlice,invItem,invAlerts,invClean,invLocLabel,invPiecesOut,invPendingCounts,invCountVariance,invItemsAt,money} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {Empty,Pill,shortDate} from '../../components/ui';
import {InvStatus,useInv,invUserName} from './shared';
import {InvCountForm} from './modals';
import {InvCanReceive} from './InventoryPage';

type Sec='stock'|'count'|'laundry'|'alerts';
export function InventoryMobile(){
  const {s,open,money:showMoney}=useInv();const x=invSlice(s);
  const [sec,setSec]=useState<Sec>('stock');const [loc,setLoc]=useState<INVLoc>('store');const [cloc,setCloc]=useState<INVLoc>('floor1');const [round,setRound]=useState(0);
  const alerts=invAlerts(s),pend=invPendingCounts(s),open_=x.batches.filter(b=>b.status==='Out');
  const canGrn=InvCanReceive();
  return <div className="inv-m">
    <div className="inv-m-kpis">
      <KpiCard label="Below minimum" value={alerts.length} onClick={()=>setSec('alerts')}/>
      <KpiCard label="At laundry" value={invPiecesOut(s)} sub={`${open_.length} batch${open_.length===1?'':'es'}`} onClick={()=>setSec('laundry')}/>
      <KpiCard label="Counts to review" value={pend.length} onClick={()=>setSec('count')}/>
    </div>
    <ChipFilter<Sec> label="Section" value={sec} onChange={setSec} options={[{id:'stock',label:'Stock'},{id:'count',label:'Count'},{id:'laundry',label:'Laundry',count:open_.length},{id:'alerts',label:'Alerts',count:alerts.length}]}/>
    {sec==='stock'&&<>
      <label className="inv-m-select"><span>Location</span><select value={loc} onChange={e=>setLoc(e.target.value as INVLoc)}>{INV_LOCS.map(l=><option key={l.id} value={l.id}>{l.label}</option>)}</select></label>
      {invItemsAt(s,loc).map(i=><ListCard key={i.id} title={i.name} sub={`Clean ${invClean(i)} · min ${i.min}${showMoney?` · ${money(i.cost)} each`:''}`}
        right={<span className="inv-m-qty"><strong>{i.stock[loc]??0}</strong><small>{i.unit}</small></span>} meta={<InvStatus it={i}/>}
        onClick={()=>open({type:'inv-move',id:i.id,room:i.kind==='linen'?(loc==='store'?'issue':'change'):(loc==='store'?'issue':'use')})}/>)}
      <div className="inv-m-links"><button className="btn full" onClick={()=>open({type:'inv-loss',room:loc})}>Report shortage / damage</button>
        <button className="btn full" onClick={()=>open({type:'inv-print',id:'stock'})}><Printer size={16}/> Print stock sheet</button></div>
      <Fab label="Issue / use" icon={ArrowRightLeft} onClick={()=>open({type:'inv-move'})}/></>}
    {sec==='count'&&<>
      {pend.map(c=><ListCard key={c.id} title={`${c.id} · ${invLocLabel(c.loc)}`} sub={`By ${invUserName(s,c.actor)} · ${invCountVariance(c).length} variance line(s)`} right={<Pill tone="orange">Review</Pill>} onClick={()=>open({type:'inv-count-review',id:c.id})}/>)}
      <ChipFilter<INVLoc> label="Count location" value={cloc} onChange={setCloc} options={INV_COUNTABLE.map(l=>({id:l,label:invLocLabel(l).replace(' pantry','').replace('Main store','Store')}))}/>
      <InvCountForm key={cloc+round} loc={cloc} after={()=>setRound(round+1)}/></>}
    {sec==='laundry'&&<>
      {!x.batches.length&&<Empty title="No laundry batches"/>}
      {x.batches.slice().reverse().slice(0,12).map(b=><ListCard key={b.id} title={`${b.id} · ${b.lines.reduce((a,l)=>a+l.sent,0)} pieces`} sub={`Sent ${shortDate(b.sent)} · ${b.vendor}`}
        meta={b.lines.map(l=>`${invItem(s,l.item)?.name} ${l.sent}`).join(' · ')} right={<Pill tone={b.status==='Out'?'blue':b.status==='Returned'?'green':'orange'}>{b.status==='Out'?'At laundry':b.status}</Pill>}
        onClick={()=>open({type:'inv-laundry-receive',id:b.id})}/>)}
      <p className="muted inv-m-note">Pickup and delivery are simulated. Tap a batch at the laundry to receive it.</p>
      <Fab label="Send soiled" icon={Truck} onClick={()=>open({type:'inv-laundry-send'})}/></>}
    {sec==='alerts'&&<>
      {!alerts.length?<Empty title="Nothing below minimum"/>:alerts.map(a=><ListCard key={a.item.id} title={a.item.name} sub={`Clean ${a.clean} of minimum ${a.item.min} · order ${a.suggest} ${a.item.unit}`}
        meta={a.item.supplier?`Supplier: ${a.item.supplier}`:'Linen — order from the textile supplier'} right={<Pill tone="inv-low">Low</Pill>}
        onClick={canGrn?()=>open({type:'inv-grn',id:a.item.id,room:String(a.suggest)}):undefined}/>)}
      {!canGrn&&alerts.length>0&&<p className="muted inv-m-note">Receiving stock is done by a supervisor or manager.</p>}</>}
  </div>;
}
