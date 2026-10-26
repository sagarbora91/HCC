// Module 23 · Restaurant POS & room service — phone page: spot list → menu grid with big tap targets → cart sheet.
import React,{useState} from 'react';
import {ArrowLeft,ShoppingCart,Printer} from 'lucide-react';
import {money,posSlice,posOpenOrderAt,posOrderTotals,posDaySummary,posKitchen,posKitchenAt,posStations,posOpenOrders,posUnsent,posOutletList,rbacPosSeesDaySales} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter,KpiCard,StickyFooter} from '../../app/mobile/components';
import {Empty} from '../../components/ui';
import {PosSpots,PosMenu,PosKotCard,PosStatus} from './parts';
import {PosSummaryTables} from './modals';
import {PosAvailability,PosMasterLinks} from './PosPage';

type View='orders'|'kitchen'|'summary';
export function PosMobile(){
  const {s,uid}=useHcc();const p=posSlice(s);
  const [view,setView]=useState<View>('orders');
  if(!posOutletList(s).length)return <><Empty title="No outlets configured" body={p.outlets.length?'Every outlet is retired — reactivate one in Master configuration.':'Load a demo scenario to get the fictional menu.'}/><PosMasterLinks/></>;
  const kitchenOpen=posKitchen(s).filter(x=>x.kot.status!=='Served').length;
  const sales=rbacPosSeesDaySales(s,uid); // DEC-079 D1: the Restaurant role has orders and kitchen, no day sales
  return <div className="pos-m">
    <ChipFilter<View> label="POS view" value={view} onChange={setView} options={[{id:'orders',label:'Orders',count:posOpenOrders(s).length},{id:'kitchen',label:'Kitchen',count:kitchenOpen},
      ...(sales?[{id:'summary' as View,label:'Summary'}]:[])]}/>
    {view==='orders'&&<MobileOrders/>}{view==='kitchen'&&<MobileKitchen/>}{view==='summary'&&sales&&<MobileSummary/>}
    {view!=='summary'&&<PosMasterLinks/>}
  </div>;
}

function MobileOrders(){
  const {s,open}=useHcc();const outlets=posOutletList(s);
  const [pick,setOutlet]=useState(outlets[0].id);const [spot,setSpot]=useState<string>();
  const outlet=outlets.some(o=>o.id===pick)?pick:outlets[0].id,kind=outlets.find(o=>o.id===outlet)?.kind;
  const order=spot?posOpenOrderAt(s,outlet,spot):undefined;
  if(!spot)return <>
    <ChipFilter label="Outlet" value={outlet} onChange={v=>setOutlet(v)} options={outlets.map(o=>({id:o.id,label:o.name,count:posOpenOrders(s).filter(x=>x.outlet===o.id).length}))}/>
    <PosSpots outlet={outlet} onSelect={x=>{setSpot(x);if(!posOpenOrderAt(s,outlet,x))open({type:'pos-open',id:outlet,room:x});}}/>
  </>;
  const t=order&&posOrderTotals(s,order),unsent=order?posUnsent(order).reduce((a,l)=>a+l.qty,0):0;
  return <>
    <div className="pos-m-head"><button type="button" className="btn" onClick={()=>setSpot(undefined)}><ArrowLeft size={18}/> All {kind==='room'?'rooms':'tables'}</button>
      <div><strong>{kind==='room'?'Room':'Table'} {spot}</strong>{order?<PosStatus st={order.status}/>:<span className="muted">No open check</span>}</div></div>
    {!order&&<div className="pos-row-actions"><button className="btn primary full" onClick={()=>open({type:'pos-open',id:outlet,room:spot})}>Open check</button></div>}
    <PosMenu outlet={outlet} order={order}/>
    {order&&<StickyFooter><button type="button" className="btn primary full pos-cart-btn" onClick={()=>open({type:'pos-cart',id:order.id})}>
      <ShoppingCart size={18}/> Cart · {t!.items} item{t!.items===1?'':'s'} · {money(t!.total)}{unsent?` · ${unsent} to send`:''}</button></StickyFooter>}
  </>;
}

function MobileKitchen(){
  const {s}=useHcc();const stations=posStations(s);const [station,setStation]=useState('');const all=posKitchenAt(s,station);const [st,setSt]=useState<'Preparing'|'Ready'|'Served'|'all'>('Preparing');
  const xs=all.filter(x=>st==='all'||x.kot.status===st);
  return <>
    {stations.length>0&&<ChipFilter label="Kitchen station" value={station} onChange={setStation} options={['',...stations].map(x=>({id:x||'all',label:x||'All stations',count:posKitchenAt(s,x).filter(k=>k.kot.status!=='Served').length})).map(o=>({...o,id:o.id==='all'?'':o.id}))}/>}
    <ChipFilter label="Kitchen status" value={st} onChange={setSt} options={(['Preparing','Ready','Served'] as const).map(c=>({id:c,label:c,count:all.filter(x=>x.kot.status===c).length}))}/>
    {xs.map(x=><PosKotCard key={x.kot.id} o={x.order} kotId={x.kot.id}/>)}
    {!xs.length&&<Empty title={`No ${st.toLowerCase()} KOTs`}/>}
    <PosAvailability/>
  </>;
}

function MobileSummary(){
  const {s,open}=useHcc();const d=posDaySummary(s);
  return <>
    <div className="pos-m-kpis">
      <KpiCard label="Outlet sales" value={money(d.gross)} sub={`${d.checks} bills · net ${money(d.net)}`}/>
      <KpiCard label="Outlet cash" value={money(d.outletCash)} sub="In front-desk drawer"/>
      <KpiCard label="To room folios" value={money(d.byMode['Room folio']||0)}/>
      <KpiCard label="Open checks" value={d.open.count} sub={money(d.open.value)}/>
    </div>
    <div className="card pos-m-card"><PosSummaryTables d={d}/></div>
    <StickyFooter><button className="btn primary full" onClick={()=>open({type:'pos-report'})}><Printer size={18}/> Print day summary</button></StickyFooter>
  </>;
}
