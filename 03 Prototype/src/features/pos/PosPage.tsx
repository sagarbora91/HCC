// Module 23 · Restaurant POS & room service — desktop page (Orders · Kitchen · Day summary).
import React,{useState} from 'react';
import {Printer,Info} from 'lucide-react';
import {money,posSlice,posOpenOrderAt,posKitchen,posKitchenAt,posStations,posDaySummary,posOpenOrders,posOrderTotals,posSpot,posMenuFor,posOutletList,posSoldOut,posItemActive,isDayClosed,rbacPosSeesDaySales} from '../../services/mock';
import {MstLink} from '../masters/MasterList';
import {useHcc} from '../../app/context';
import {CfgHubLink} from '../admin/config/panels'; // W05-A: settings also in Master configuration
import {Empty,dateLabel} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,useFitNav,ScrollPanel,BoardColumns,StatStrip,FitNote} from '../../components/fit';
import {PosSpots,PosMenu,PosCheck,PosKotCard,posKitchenColumns,PosStatus} from './parts';
import {PosSummaryTables} from './modals';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';

export type PosView='orders'|'kitchen'|'summary';
/** FIT (DEC-194…196, contract §6 `pos`): the three views are page tabs (sub-screens orders / kitchen / summary); the order view is a board of
 *  three columns — tables, menu, check — that scroll on their own, the check's KOT / Settle bar sticky at the bottom of its column. */
const POS_VIEWS=['orders','kitchen','summary'] as const;
export function PosPage(){
  const {s,uid,open}=useHcc();const p=posSlice(s);
  const [tab,setView]=useFitTab<PosView>('pos',POS_VIEWS,'orders');
  const kitchenOpen=posKitchen(s).filter(x=>x.kot.status!=='Served').length;
  const sales=rbacPosSeesDaySales(s,uid); // DEC-079 D1: no day sales for the Restaurant role
  const view:PosView=tab==='summary'&&!sales?'orders':tab;
  const sub=`Outlet checks, KOTs and room-service charges · ${dateLabel(s.date)} · fictional menu, GST from the master`;
  if(!posOutletList(s).length)return <FitPage title="Restaurant & room service" sub="Outlet checks, KOTs and room-service charges" toolbar={<PosMasterLinks/>}><div className="card pos-fit-empty"><Empty title="No outlets configured" body={p.outlets.length?'Every outlet is retired — reactivate one or add an outlet in Master configuration.':'Load a demo scenario to get the fictional restaurant and room-service menu.'}/></div></FitPage>;
  const tabs:{id:PosView;label:string;count?:number}[]=[{id:'orders',label:'Orders',count:posOpenOrders(s).length},{id:'kitchen',label:'Kitchen (KOT)',count:kitchenOpen},
    ...(sales?[{id:'summary' as PosView,label:'Day summary'}]:[])];
  return <FitPage title="Restaurant & room service" sub={sub} layout="fill"
      actions={<>{view==='summary'&&<button className="btn" onClick={()=>open({type:'pos-report'})}><Printer size={15}/> Print summary</button>}<span className="pos-sim"><Info size={14}/>KOT printer & card terminal simulated</span></>}
      toolbar={<>{isDayClosed(s)&&<FitNote text="The business day is closed — checks are read-only until the next day is opened."/>}<PosMasterLinks/></>}
      tabs={<FitTabs page="pos" value={view} onChange={setView} tabs={tabs}/>}>
    {view==='orders'&&<PosOrders/>}{view==='kitchen'&&<PosKitchenBoard/>}{view==='summary'&&sales&&<PosSummary/>}
  </FitPage>;
}

/** MA-4: "Manage list…" links to the POS masters (outlets, tables, categories, menu) — shown to Owner / Tech admin (masters). */
export function PosMasterLinks(){
  return <p className="pos-mst-links"><MstLink spec="outlets"/> <MstLink spec="posTables"/> <MstLink spec="menuCategories"/> <MstLink spec="menuItems"/></p>;
}

function PosOrders(){
  const {s,uid,open}=useHcc();const p=posSlice(s),outlets=posOutletList(s);
  const [pick,setOutlet]=useState(outlets[0].id);const [spot,setSpot]=useState<string>();
  const outlet=outlets.some(o=>o.id===pick)?pick:outlets[0].id;
  const order=spot?posOpenOrderAt(s,outlet,spot):undefined;
  const select=(x:string)=>{setSpot(x);if(!posOpenOrderAt(s,outlet,x))open({type:'pos-open',id:outlet,room:x});};
  const recent=rbacPosSeesDaySales(s,uid)?p.orders.filter(o=>o.outlet===outlet&&o.status!=='Open'&&(o.settlement?.date===s.date||o.date===s.date)).slice(-6).reverse():[];
  const scroll=(label:string)=>({'data-fit-scroll':'',tabIndex:0,'aria-label':label}) as React.HTMLAttributes<HTMLElement>;
  return <section className="card pos-fit-card"><div className="pos-layout">
    <aside className="pos-col pos-col-spots" {...scroll('Outlet, tables and rooms')}><div className="pos-seg" role="group" aria-label="Outlet">{outlets.map(o=><button key={o.id} aria-pressed={outlet===o.id} className={outlet===o.id?'on':''} onClick={()=>{setOutlet(o.id);setSpot(undefined);}}>{o.name}</button>)}</div>
      <PosSpots outlet={outlet} selected={spot} onSelect={select}/>
      {recent.length>0&&<div className="pos-recent"><h3>Closed today</h3>{recent.map(o=><button key={o.id} className="pos-recent-row" onClick={()=>open({type:'pos-print',id:o.id,room:'bill'})}><span>{posSpot(o)} · {o.settlement?.billNo||o.id}</span><span><PosStatus st={o.status}/> {money(posOrderTotals(s,o).total)}</span></button>)}</div>}
    </aside>
    <div className="pos-col pos-col-menu" {...scroll('Menu')}><PosMenu outlet={outlet} order={order}/><p className="footnote">{posMenuFor(s,outlet).filter(m=>posSoldOut(s,m)).length} item(s) sold out today (86). Switch them in the Kitchen tab.</p></div>
    <div className="pos-col pos-col-check" {...scroll('Current check')}>{order?<PosCheck o={order}/>:<Empty title={spot?'No open check':'Select a table or room'} body={spot?'Open a check for this spot to start an order.':'Tables with a check show their running total and kitchen status.'}/>}
      {spot&&!order&&<div className="pos-row-actions pos-pad"><button className="btn primary" onClick={()=>open({type:'pos-open',id:outlet,room:spot})}>Open check</button></div>}</div>
  </div></section>;
}

export function PosKitchenBoard(){
  const {s}=useHcc();const stations=posStations(s);const [station,setStation]=useState('');const all=posKitchenAt(s,station);
  return <>
    {stations.length>0&&<div className="pos-cats pos-fit-stations" role="group" aria-label="Kitchen station">{['',...stations].map(x=><button type="button" key={x||'all'} aria-pressed={station===x} className={station===x?'on':''} onClick={()=>setStation(x)}>{x||'All stations'}</button>)}</div>}
    <BoardColumns minColumn={200} columns={posKitchenColumns.map(c=>{const xs=all.filter(x=>x.kot.status===c);return {id:c,title:c,count:xs.length,
      body:<PosKotColumn key={c+station} xs={xs}/>};})}/>
    <PosAvailability/>
  </>;
}

/** One kitchen column: the cards a page at a time (the Served column holds every KOT of the day and history), "Show more" at its foot. */
function PosKotColumn({xs}:{xs:ReturnType<typeof posKitchenAt>}){
  const pg=usePaged(xs,DESKTOP_PAGE);
  return <>{pg.shown.map(x=><PosKotCard key={x.kot.id} o={x.order} kotId={x.kot.id}/>)}{!xs.length&&<p className="muted">No KOTs.</p>}<ShowMore p={pg}/></>;
}

/** Menu availability ("86" list). */
export function PosAvailability(){
  const {s,run,busy,permission}=useHcc();const p=posSlice(s);const {desktop}=useFitNav();
  if(!permission('billing'))return null;
  // MA-4: the "sold out today" switch of the menu master (lapses at the next business date); retired items are not listed.
  const menu=p.menu.filter(posItemActive);
  return <details className="pos-86"><summary>Sold out today (86 list) · {menu.filter(m=>posSoldOut(s,m)).length} off</summary><div className="pos-86-grid" {...(desktop?{'data-fit-scroll':'',tabIndex:0,'aria-label':'Sold out today switches'} as React.HTMLAttributes<HTMLElement>:{})}>{menu.map(m=>{const sold=posSoldOut(s,m);return <label key={m.id}><input type="checkbox" checked={!sold} disabled={busy} onChange={()=>run({type:'POS_MENU_TOGGLE',item:m.id},sold?`${m.name} available again`:`${m.name} sold out today`)}/>{m.name}</label>;})}</div><CfgHubLink what="Menu availability switches" group="pos"/> <MstLink spec="menuItems"/></details>;
}

export function PosSummary(){
  const {s}=useHcc();const d=posDaySummary(s);
  return <>
    <StatStrip money compact collapseOnTablet items={[{title:'Outlet sales (incl. tax)',value:money(d.gross),sub:`${d.checks} bill${d.checks===1?'':'s'} · net ${money(d.net)}`},
      {title:'Outlet cash',value:money(d.outletCash),sub:'Counted in the front-desk drawer'},
      {title:'Posted to room folios',value:money(d.byMode['Room folio']||0),sub:'Collected at checkout'},
      {title:'Open checks',value:String(d.open.count),sub:`${money(d.open.value)} running · ${d.pendingVoids} void request${d.pendingVoids===1?'':'s'} pending`}]}/>
    <ScrollPanel label="Day summary tables" footer={<FitNote lines={2} text={`Settled bills by settlement date; open checks as of ${s.clock.slice(11,16)} (simulated clock).`}/>}>
      <PosSummaryTables d={d}/>
      {d.top.length>0&&<><h3 className="pos-h3">Top items today</h3><ol className="pos-top">{d.top.map(t=><li key={t.name}><span>{t.name}</span><b>{t.qty}</b><span>{money(t.value)}</span></li>)}</ol></>}
      <div className="info-note pos-sum-note"><Info size={16}/>One shared till: cash bills (net of same-day voids) are counted in the front-desk drawer on the cashier shift. UPI and card are non-cash; room-service charged to a room is a folio charge, collected at checkout. Open checks block day close.</div>
    </ScrollPanel>
  </>;
}
