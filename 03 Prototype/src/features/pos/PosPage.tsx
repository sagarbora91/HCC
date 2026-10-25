// Module 23 · Restaurant POS & room service — desktop page (Orders · Kitchen · Day summary).
import React,{useState} from 'react';
import {Printer,ChefHat,ClipboardList,BarChart3,Info} from 'lucide-react';
import {money,posSlice,posOpenOrderAt,posKitchen,posDaySummary,posOpenOrders,posOrderTotals,posSpot,posMenuFor,isDayClosed} from '../../services/mock';
import {useHcc} from '../../app/context';
import {CfgHubLink} from '../admin/config/panels'; // W05-A: settings also in Master configuration
import {PageTitle,Empty,Stat,dateLabel} from '../../components/ui';
import {PosSpots,PosMenu,PosCheck,PosKotCard,posKitchenColumns,PosStatus} from './parts';
import {PosSummaryTables} from './modals';

export type PosView='orders'|'kitchen'|'summary';
export function PosPage(){
  const {s}=useHcc();const p=posSlice(s);
  const [view,setView]=useState<PosView>('orders');
  const kitchenOpen=posKitchen(s).filter(x=>x.kot.status!=='Served').length;
  if(!p.outlets.length)return <><PageTitle title="Restaurant & room service" sub="Outlet checks, KOTs and room-service charges"/><Empty title="No outlets configured" body="Load a demo scenario to get the fictional restaurant and room-service menu."/></>;
  const tabs:[PosView,string,React.ReactNode,number?][]=[['orders','Orders',<ClipboardList size={15}/>,posOpenOrders(s).length],['kitchen','Kitchen (KOT)',<ChefHat size={15}/>,kitchenOpen],['summary','Day summary',<BarChart3 size={15}/>]];
  return <><PageTitle title="Restaurant & room service" sub={`Outlet checks, KOTs and room-service charges · ${dateLabel(s.date)} · fictional menu, GST from the master`}
      action={<span className="pos-sim"><Info size={14}/>KOT printer & card terminal simulated</span>}/>
    {isDayClosed(s)&&<div className="info-note"><Info size={16}/>The business day is closed — checks are read-only until the next day is opened.</div>}
    <section className="card pos-card"><div className="tabs" role="tablist">{tabs.map(([id,label,icon,n])=><button key={id} role="tab" aria-selected={view===id} className={view===id?'active':''} onClick={()=>setView(id)}>{icon}{label}{n!==undefined&&<span>{n}</span>}</button>)}</div>
      {view==='orders'&&<PosOrders/>}{view==='kitchen'&&<PosKitchenBoard/>}{view==='summary'&&<PosSummary/>}
    </section></>;
}

function PosOrders(){
  const {s,open}=useHcc();const p=posSlice(s);
  const [outlet,setOutlet]=useState(p.outlets[0].id);const [spot,setSpot]=useState<string>();
  const order=spot?posOpenOrderAt(s,outlet,spot):undefined;
  const select=(x:string)=>{setSpot(x);if(!posOpenOrderAt(s,outlet,x))open({type:'pos-open',id:outlet,room:x});};
  const recent=p.orders.filter(o=>o.outlet===outlet&&o.status!=='Open'&&(o.settlement?.date===s.date||o.date===s.date)).slice(-6).reverse();
  return <div className="pos-layout">
    <aside className="pos-col"><div className="pos-seg" role="group" aria-label="Outlet">{p.outlets.map(o=><button key={o.id} aria-pressed={outlet===o.id} className={outlet===o.id?'on':''} onClick={()=>{setOutlet(o.id);setSpot(undefined);}}>{o.name}</button>)}</div>
      <PosSpots outlet={outlet} selected={spot} onSelect={select}/>
      {recent.length>0&&<div className="pos-recent"><h3>Closed today</h3>{recent.map(o=><button key={o.id} className="pos-recent-row" onClick={()=>open({type:'pos-print',id:o.id,room:'bill'})}><span>{posSpot(o)} · {o.settlement?.billNo||o.id}</span><span><PosStatus st={o.status}/> {money(posOrderTotals(s,o).total)}</span></button>)}</div>}
    </aside>
    <div className="pos-col pos-col-menu"><PosMenu outlet={outlet} order={order}/><p className="footnote">{posMenuFor(s,outlet).filter(m=>!m.available).length} item(s) marked unavailable (86). Toggle availability in the Kitchen tab.</p></div>
    <div className="pos-col pos-col-check">{order?<PosCheck o={order}/>:<Empty title={spot?'No open check':'Select a table or room'} body={spot?'Open a check for this spot to start an order.':'Tables with a check show their running total and kitchen status.'}/>}
      {spot&&!order&&<div className="pos-row-actions pos-pad"><button className="btn primary" onClick={()=>open({type:'pos-open',id:outlet,room:spot})}>Open check</button></div>}</div>
  </div>;
}

export function PosKitchenBoard(){
  const {s}=useHcc();const all=posKitchen(s);
  return <div className="pos-pad">
    <div className="pos-board">{posKitchenColumns.map(c=>{const xs=all.filter(x=>x.kot.status===c);return <div key={c} className="pos-board-col"><h3>{c} <span>{xs.length}</span></h3>
      {xs.map(x=><PosKotCard key={x.kot.id} o={x.order} kotId={x.kot.id}/>)}{!xs.length&&<p className="muted">No KOTs.</p>}</div>;})}</div>
    <PosAvailability/>
  </div>;
}

/** Menu availability ("86" list). */
export function PosAvailability(){
  const {s,run,busy,permission}=useHcc();const p=posSlice(s);
  if(!permission('billing'))return null;
  return <details className="pos-86"><summary>Menu availability (86 list) · {p.menu.filter(m=>!m.available).length} off</summary><div className="pos-86-grid">{p.menu.map(m=><label key={m.id}><input type="checkbox" checked={m.available} disabled={busy} onChange={()=>run({type:'POS_MENU_TOGGLE',item:m.id},m.available?`${m.name} marked unavailable`:`${m.name} available again`)}/>{m.name}</label>)}</div><CfgHubLink what="Menu availability switches" group="pos"/></details>;
}

export function PosSummary(){
  const {s,open}=useHcc();const d=posDaySummary(s);
  return <div className="pos-pad">
    <div className="pos-sum-head"><p>Settled bills by settlement date; open checks as of {s.clock.slice(11,16)} (simulated clock).</p><button className="btn" onClick={()=>open({type:'pos-report'})}><Printer size={15}/> Print summary</button></div>
    <div className="stats pos-stats">
      <Stat title="Outlet sales (incl. tax)" value={money(d.gross)} sub={`${d.checks} bill${d.checks===1?'':'s'} · net ${money(d.net)}`}/>
      <Stat title="Outlet cash" value={money(d.outletCash)} sub="Counted in the front-desk drawer"/>
      <Stat title="Posted to room folios" value={money(d.byMode['Room folio']||0)} sub="Collected at checkout"/>
      <Stat title="Open checks" value={String(d.open.count)} sub={`${money(d.open.value)} running · ${d.pendingVoids} void request${d.pendingVoids===1?'':'s'} pending`}/>
    </div>
    <PosSummaryTables d={d}/>
    {d.top.length>0&&<><h3 className="pos-h3">Top items today</h3><ol className="pos-top">{d.top.map(t=><li key={t.name}><span>{t.name}</span><b>{t.qty}</b><span>{money(t.value)}</span></li>)}</ol></>}
    <div className="info-note"><Info size={16}/>One shared till: cash bills (net of same-day voids) are counted in the front-desk drawer on the cashier shift. UPI and card are non-cash; room-service charged to a room is a folio charge, collected at checkout. Open checks block day close.</div>
  </div>;
}
