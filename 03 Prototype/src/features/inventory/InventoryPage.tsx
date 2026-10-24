// Module 16 · Linen & stock — desktop page. Quantities for every housekeeping user; values/costs only for roles with reports or expenses.
import React,{useState} from 'react';
import {PackagePlus,ArrowRightLeft,Truck,AlertTriangle,Printer,ClipboardList} from 'lucide-react';
import {money,can,INV_LOCS,INV_COUNTABLE,INV_KINDS,INVKind,invSlice,invItem,invAlerts,invClean,invTotal,invValue,invLocLabel,invPiecesOut,invPendingCounts,invCountVariance,
  invMissing,invParRequired,invParPerSet,invParCoverage,invRoomTypes,invRoomsByType,invConsumption,invFmt,addDays} from '../../services/mock';
import {PageTitle,Pill,Empty,shortDate,dateLabel} from '../../components/ui';
import {InvStatus,useInv,invUserName} from './shared';

const TABS=['Stock','Laundry','Counts','Losses','Consumption','Par & minimums','Movements'] as const;
type Tab=typeof TABS[number];
export function InvCanReceive(){const {s,uid}=useInv();return can(s,uid,'inspect')||can(s,uid,'expenses');}

export function InventoryPage(){
  const h=useInv();const {s,open,permission,money:showMoney}=h;const x=invSlice(s);
  const [tab,setTab]=useState<Tab>('Stock');
  const alerts=invAlerts(s),pend=invPendingCounts(s),canGrn=InvCanReceive();
  const soiled=x.items.reduce((a,i)=>a+(i.stock.soiled??0),0);
  const counts:Partial<Record<Tab,number>>={Laundry:x.batches.filter(b=>b.status==='Out').length,Counts:pend.length,Losses:x.losses.length};
  return <><PageTitle title="Linen & stock" sub="Linen par, laundry, amenities and cleaning supplies — every piece accounted for."
      action={<div className="inv-actions">
        {canGrn&&<button className="btn" onClick={()=>open({type:'inv-grn'})}><PackagePlus size={16}/> Receive stock</button>}
        <button className="btn primary" onClick={()=>open({type:'inv-move'})}><ArrowRightLeft size={16}/> Issue / use</button></div>}/>
    <div className="small-stats">
      <div><span>Below minimum</span><strong>{alerts.length}</strong></div>
      <div><span>Pieces at laundry</span><strong>{invPiecesOut(s)}</strong></div>
      <div><span>Soiled, not yet sent</span><strong>{soiled}</strong></div>
      <div><span>Counts to review</span><strong>{pend.length}</strong></div>
      {showMoney&&<div><span>Stock value (std cost)</span><strong>{money(x.items.reduce((a,i)=>a+invValue(i),0))}</strong></div>}
    </div>
    {alerts.length>0&&<section className="card inv-alerts"><div className="section-title"><h2><AlertTriangle size={17}/> Reorder alerts</h2><span className="muted">Clean stock (store + floor pantries) below minimum</span></div>
      <div className="request-list">{alerts.map(a=><div className="request" key={a.item.id}><div><strong>{a.item.name}</strong><p>Clean {a.clean} of minimum {a.item.min} · suggested order {a.suggest} {a.item.unit}{a.item.supplier?` · usual supplier ${a.item.supplier}`:''}{showMoney?` · approx. ${money(a.suggest*a.item.cost)}`:''}</p></div>
        <Pill tone="inv-low">Below minimum</Pill>
        {canGrn?<button className="btn small-btn" onClick={()=>open({type:'inv-grn',id:a.item.id,room:String(a.suggest)})}>Receive…</button>:<span className="muted">Tell the supervisor</span>}</div>)}</div></section>}
    <section className="card">
      <div className="tabs" role="tablist" aria-label="Linen and stock sections">{TABS.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}{counts[t]!==undefined&&<span>{counts[t]}</span>}</button>)}</div>
      {tab==='Stock'&&<StockTab/>}{tab==='Laundry'&&<LaundryTab/>}{tab==='Counts'&&<CountsTab/>}{tab==='Losses'&&<LossTab/>}
      {tab==='Consumption'&&<ConsumptionTab/>}{tab==='Par & minimums'&&<ParTab/>}{tab==='Movements'&&<MovesTab/>}
    </section>
    {!permission('housekeeping')&&<p className="muted">This page needs the housekeeping permission.</p>}</>;
}

function StockTab(){
  const {s,open,money:showMoney}=useInv();const x=invSlice(s);const [kind,setKind]=useState<INVKind|'all'>('all');
  const list=x.items.filter(i=>kind==='all'||i.kind===kind);
  return <div className="padded">
    <div className="inv-toolbar"><div className="inv-seg" role="group" aria-label="Item type">{[{id:'all',label:'All'},...INV_KINDS].map(k=><button key={k.id} type="button" aria-pressed={kind===k.id} className={kind===k.id?'on':''} onClick={()=>setKind(k.id as INVKind|'all')}>{k.label}</button>)}</div>
      <button className="btn" onClick={()=>open({type:'inv-print',id:'stock'})}><Printer size={15}/> Print stock sheet</button></div>
    <div className="table-wrap"><table className="inv-table"><thead><tr><th>Item</th>{INV_LOCS.map(l=><th key={l.id} className="number" title={l.label}>{l.short}</th>)}<th className="number">Clean / min</th><th>Status</th>{showMoney&&<th className="number">Value</th>}<th></th></tr></thead>
      <tbody>{list.map(i=><tr key={i.id}><td><strong>{i.name}</strong><small>{i.id} · {i.unit}{showMoney?` · ${money(i.cost)} each`:''}</small></td>
        {INV_LOCS.map(l=><td key={l.id} className="number">{i.kind!=='linen'&&!INV_COUNTABLE.includes(l.id)?<span className="muted">—</span>:i.stock[l.id]??0}</td>)}
        <td className="number"><strong>{invClean(i)}</strong> / {i.min}</td><td><InvStatus it={i}/></td>{showMoney&&<td className="number">{money(invValue(i))}</td>}
        <td><div className="inline"><button className="text-btn" onClick={()=>open({type:'inv-move',id:i.id,room:i.kind==='linen'?'issue':'use'})}>Move</button><button className="text-btn" onClick={()=>open({type:'inv-loss',id:i.id})}>Loss…</button></div></td></tr>)}</tbody></table></div>
    <p className="muted">Store = Main store · F1–F3 = floor pantries · Rooms = linen dressed in rooms · Soiled = waiting for the laundry · Laundry = with the vendor.</p>
  </div>;
}

function LaundryTab(){
  const {s,open,money:showMoney}=useInv();const x=invSlice(s);const list=x.batches.slice().reverse();
  const soiled=x.items.filter(i=>(i.stock.soiled??0)>0);
  return <div className="padded">
    <div className="inv-toolbar"><p className="muted">Vendor: <strong>{x.vendor?.name??'Fresh Linen Services'}</strong> (fictional) · pickup and delivery are <Pill tone="blue">Simulated</Pill> · soiled waiting: {soiled.map(i=>`${i.name} ${i.stock.soiled}`).join(', ')||'none'}</p>
      <button className="btn primary" disabled={!soiled.length} onClick={()=>open({type:'inv-laundry-send'})}><Truck size={16}/> Send soiled linen</button></div>
    {!list.length?<Empty title="No laundry batches"/>:<div className="table-wrap"><table className="inv-table"><thead><tr><th>Batch</th><th>Sent</th><th className="number">Pieces</th><th className="number">Clean back</th><th className="number">Damaged</th><th className="number">Missing</th>{showMoney&&<th className="number">Charge</th>}<th>Status</th><th></th></tr></thead>
      <tbody>{list.map(b=>{const sum=(f:(l:typeof b.lines[number])=>number)=>b.lines.reduce((a,l)=>a+f(l),0);return <tr key={b.id}>
        <td><strong>{b.id}</strong><small>{b.lines.map(l=>`${invItem(s,l.item)?.name} ${l.sent}`).join(' · ')}</small>{b.note&&<small>{b.note}</small>}</td>
        <td>{shortDate(b.sent)} {b.sentAt.slice(11)}<small>by {invUserName(s,b.actor)}</small></td><td className="number">{sum(l=>l.sent)}</td>
        <td className="number">{b.status==='Out'?'—':sum(l=>l.received||0)}</td><td className="number">{b.status==='Out'?'—':sum(l=>l.damaged||0)}</td><td className="number">{b.status==='Out'?'—':sum(invMissing)}</td>
        {showMoney&&<td className="number">{b.charge===undefined?'—':money(b.charge)}</td>}
        <td><Pill tone={b.status==='Out'?'blue':b.status==='Returned'?'green':'orange'}>{b.status==='Out'?'At laundry':b.status}</Pill></td>
        <td>{b.status==='Out'?<button className="btn small-btn" onClick={()=>open({type:'inv-laundry-receive',id:b.id})}>Receive…</button>:<button className="text-btn" onClick={()=>open({type:'inv-laundry-receive',id:b.id})}>Details</button>}</td></tr>;})}</tbody></table></div>}
  </div>;
}

function CountsTab(){
  const {s,uid,open,permission}=useInv();const x=invSlice(s);const list=x.counts.slice().reverse();
  return <div className="padded">
    <div className="inv-toolbar"><span className="muted">Start a count:</span><div className="inline">{INV_COUNTABLE.map(l=><button key={l} className="btn" onClick={()=>open({type:'inv-count',room:l})}><ClipboardList size={15}/> {invLocLabel(l)}</button>)}</div></div>
    {!list.length?<Empty title="No counts yet"/>:<div className="request-list">{list.map(c=>{const v=invCountVariance(c);return <div className="request" key={c.id}>
      <div><strong>{c.id} · {invLocLabel(c.loc)}</strong><p>{dateLabel(c.date)} {c.at.slice(11)} · counted by {invUserName(s,c.actor)} · {v.length?`${v.length} variance line(s): ${v.slice(0,3).map(l=>`${invItem(s,l.item)?.name} ${l.counted-l.system>0?'+':''}${l.counted-l.system}`).join(', ')}${v.length>3?'…':''}`:'no variance'}{c.reviewer?` · reviewed by ${invUserName(s,c.reviewer)}`:''}</p></div>
      <Pill tone={c.status==='Submitted'?'orange':c.status==='Posted'?'green':'neutral'}>{c.status==='Submitted'?'Awaiting review':c.status}</Pill>
      <button className={c.status==='Submitted'&&permission('inspect')&&c.actor!==uid?'btn small-btn':'text-btn'} onClick={()=>open({type:'inv-count-review',id:c.id})}>{c.status==='Submitted'&&permission('inspect')&&c.actor!==uid?'Review…':'Details'}</button></div>;})}</div>}
  </div>;
}

function LossTab(){
  const {s,open,money:showMoney}=useInv();const list=invSlice(s).losses.slice().reverse();
  return <div className="padded">
    <div className="inv-toolbar"><p className="muted">Shortages, damage and losses with their reason. Laundry damage/missing and count shortages are recorded automatically.</p>
      <button className="btn" onClick={()=>open({type:'inv-loss'})}><AlertTriangle size={15}/> Report loss</button></div>
    {!list.length?<Empty title="No losses recorded"/>:<div className="table-wrap"><table className="inv-table"><thead><tr><th>Date</th><th>Item</th><th>Location</th><th className="number">Qty</th><th>Reason</th>{showMoney&&<th className="number">Value</th>}<th>By</th></tr></thead>
      <tbody>{list.map(l=><tr key={l.id}><td>{shortDate(l.date)}<small>{l.id}</small></td><td>{invItem(s,l.item)?.name}</td><td>{invLocLabel(l.loc)}</td><td className="number">{l.qty}</td>
        <td>{l.reason}{l.note&&<small>{l.note}</small>}{l.ref&&<small>Ref {l.ref}</small>}</td>{showMoney&&<td className="number">{money(l.value)}</td>}<td>{invUserName(s,l.actor)}</td></tr>)}</tbody></table></div>}
    {showMoney&&list.length>0&&<p>Total written off: <strong>{money(list.reduce((a,l)=>a+l.value,0))}</strong></p>}
  </div>;
}

export function InvRange({from,to,setFrom,setTo}:{from:string;to:string;setFrom:(v:string)=>void;setTo:(v:string)=>void}){const {s}=useInv();
  return <div className="inv-range"><label>From<input type="date" value={from} max={to} onChange={e=>e.target.value&&setFrom(e.target.value)}/></label><label>To<input type="date" value={to} min={from} max={s.date} onChange={e=>e.target.value&&setTo(e.target.value)}/></label></div>;}
function ConsumptionTab(){
  const {s,open}=useInv();const [from,setFrom]=useState(addDays(s.date,-7));const [to,setTo]=useState(addDays(s.date,-1));
  const c=invConsumption(s,from,to);
  return <div className="padded">
    <div className="inv-toolbar"><InvRange from={from} to={to} setFrom={setFrom} setTo={setTo}/><span><strong>{c.rn}</strong> occupied room-nights</span>
      <button className="btn" onClick={()=>open({type:'inv-print',id:'consumption',room:from+'|'+to})}><Printer size={15}/> Print</button></div>
    {!c.rn?<Empty title="No occupied room-nights in this range"/>:<div className="table-wrap"><table className="inv-table"><thead><tr><th>Item</th><th className="number">Used</th><th className="number">Per room-night</th><th className="number">Standard</th><th className="number">Variance</th><th></th></tr></thead>
      <tbody>{c.rows.map(r=><tr key={r.item.id}><td><strong>{r.item.name}</strong><small>{r.item.kind==='linen'?'Pieces changed in rooms':'Used from floor pantries'}</small></td><td className="number">{r.used}</td><td className="number">{invFmt(r.perRN)}</td><td className="number">{invFmt(r.std)}</td>
        <td className="number">{r.variance===null?'—':(r.variance>0?'+':'')+r.variance+'%'}</td><td>{r.flag?<Pill tone="orange">Check</Pill>:r.variance!==null?<Pill tone="green">Within ±15%</Pill>:null}</td></tr>)}</tbody></table></div>}
    <p className="muted">Occupied room-nights come from stays in house or departed; dates before go-live use fictional history. Variance beyond ±15% of standard is flagged.</p>
  </div>;
}

function ParTab(){
  const {s,open,permission}=useInv();const x=invSlice(s);const types=invRoomTypes(s),rt=invRoomsByType(s);
  return <div className="padded">
    <p className="muted">Rooms: {types.map(t=>`${t} ${rt[t]}`).join(' · ')} · par sets in circulation: <strong>{x.parSets??3}</strong> (in room, at laundry, on shelf).</p>
    <div className="table-wrap"><table className="inv-table"><thead><tr><th>Linen</th>{types.map(t=><th key={t} className="number">Par {t}</th>)}<th className="number">One set</th><th className="number">Required</th><th className="number">Owned</th><th>Coverage</th><th></th></tr></thead>
      <tbody>{x.items.filter(i=>i.kind==='linen').map(i=>{const req=invParRequired(s,i),cov=invParCoverage(s,i),own=invTotal(i);return <tr key={i.id}><td><strong>{i.name}</strong></td>
        {types.map(t=><td key={t} className="number">{i.par?.[t]??0}</td>)}<td className="number">{invParPerSet(s,i)}</td><td className="number">{req}</td><td className="number">{own}</td>
        <td><Pill tone={cov>=100?'green':cov>=90?'orange':'inv-low'}>{cov}%{own<req?` · short ${req-own}`:''}</Pill></td>
        <td>{permission('inspect')&&<button className="text-btn" onClick={()=>open({type:'inv-item',id:i.id})}>Edit</button>}</td></tr>;})}</tbody></table></div>
    <h3 className="inv-h3">Minimums & standards</h3>
    <div className="table-wrap"><table className="inv-table"><thead><tr><th>Item</th><th className="number">Minimum</th><th className="number">Reorder qty</th><th className="number">Std / room-night</th><th>Supplier</th><th></th></tr></thead>
      <tbody>{x.items.map(i=><tr key={i.id}><td>{i.name}</td><td className="number">{i.min}</td><td className="number">{i.reorderQty}</td><td className="number">{invFmt((i.stdX100??0)/100)}</td><td>{i.kind==='linen'?'Laundry: '+(x.vendor?.name??'—'):i.supplier??'—'}</td>
        <td>{permission('inspect')&&<button className="text-btn" onClick={()=>open({type:'inv-item',id:i.id})}>Edit</button>}</td></tr>)}</tbody></table></div>
  </div>;
}

const moveLabel:Record<string,string>={grn:'Received (GRN)',issue:'Transfer',use:'Used','room-change':'Linen change','laundry-out':'To laundry','laundry-in':'From laundry',loss:'Written off',count:'Count adjustment'};
function MovesTab(){
  const {s}=useInv();const list=invSlice(s).moves.slice(-80).reverse();
  return <div className="padded">{!list.length?<Empty title="No movements"/>:<div className="table-wrap"><table className="inv-table"><thead><tr><th>When</th><th>Type</th><th>Item</th><th className="number">Qty</th><th>From → to</th><th>Ref / reason</th><th>By</th></tr></thead>
    <tbody>{list.map(m=><tr key={m.id}><td>{shortDate(m.date)} {m.at.slice(11)}</td><td>{moveLabel[m.kind]??m.kind}</td><td>{invItem(s,m.item)?.name}</td><td className="number">{m.qty>0&&m.kind==='count'?'+':''}{m.qty}</td>
      <td>{m.from?invLocLabel(m.from):'—'} → {m.to?(m.to==='used'?'used':invLocLabel(m.to)):'—'}</td><td>{[m.ref,m.reason].filter(Boolean).join(' · ')||'—'}</td><td>{invUserName(s,m.actor)}</td></tr>)}</tbody></table></div>}
    <p className="muted">Latest 80 movements.</p></div>;
}
