// Module 15 · Maintenance — desktop page. Managers/supervisors: tabs (work orders, preventive, assets, spares, vendors & AMC, reports).
// Maintenance technicians: own work orders as big cards. Housekeepers: read-only open work orders for their rooms. Costs gated by reports/expenses.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Plus,Sparkles,Printer,Wrench,ArrowRight,Info,Lock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip,FitNote} from '../../components/fit';
import {mntSlice,mntVisibleOrders,mntCanManage,mntCanSeeCost,mntSort,mntSummary,mntPendingRepairs,mntPmDue,mntPmCompliance,mntMttr,mntAgeBuckets,
  mntDowntimeByRoom,mntByKind,mntMonthPartsCost,mntPlanState,mntAmcStatus,mntWhere,mntAsset,mntVendor,mntAssigneeLabel,mntDuration,mntAge,mntPartsCost,
  mntLowSpares,mntIsActive,mntSla,MNT_PRIORITIES,MNT_PRIORITY,money,type MNTWorkOrder,type MNTStatus} from '../../services/mock';
import {MntStatus,MntPriority,MntSla,MntWoCard,mntWhen} from './shared';

export type MntTab='orders'|'pm'|'assets'|'spares'|'vendors'|'reports';
export const MNT_TABS:{id:MntTab;label:string}[]=[{id:'orders',label:'Work orders'},{id:'pm',label:'Preventive'},{id:'assets',label:'Assets'},{id:'spares',label:'Spares'},{id:'vendors',label:'Vendors & AMC'},{id:'reports',label:'Reports'}];

export function MaintenancePage(){
  const {s,uid,user,open}=useHcc();
  const mgr=mntCanManage(s,uid);
  const [tab,setTab]=useFitTab<MntTab>('maintenance',['orders','pm','assets','spares','vendors','reports'],'orders'); // FIT: same ids as before, remembered per session
  if(user.role==='Maintenance')return <MntTechView/>;
  if(!mgr)return <MntHousekeeperView/>;
  const x=mntSlice(s);
  const counts:Partial<Record<MntTab,number>>={orders:x.orders.filter(mntIsActive).length,pm:mntPmDue(s).length,spares:mntLowSpares(s).length};
  return <FitPage title="Maintenance" sub="Assets, work orders and preventive care — so rooms stay sellable." actions={<button className="btn primary" onClick={()=>open({type:'mnt-new'})}><Plus size={16}/> New work order</button>}
      tabs={<FitTabs page="maintenance" value={tab} onChange={setTab} tabs={MNT_TABS.map(t=>({id:t.id,label:t.label,count:counts[t.id]||undefined}))}/>}>
    {tab==='orders'&&<MntOrdersTab/>}{tab==='pm'&&<MntPmTab/>}{tab==='assets'&&<MntAssetsTab/>}{tab==='spares'&&<MntSparesTab/>}{tab==='vendors'&&<MntVendorsTab/>}{tab==='reports'&&<MntReportsFit/>}
  </FitPage>;
}

function MntTechView(){
  const {s,uid,open,run,busy}=useHcc();
  const mine=mntSort(mntVisibleOrders(s,uid));const repairs=mntPendingRepairs(s).filter(t=>t.assignee===uid);
  const active=mine.filter(w=>mntIsActive(w)),done=mine.filter(w=>!mntIsActive(w)).slice(0,6);
  const main=<ScrollPanel title="Open work orders" label="Open work orders"><div className="mnt-cards">{active.map(w=><MntWoCard key={w.id} w={w} large/>)}{!active.length&&<Empty title="No open work orders" body="Nothing is assigned to you right now."/>}</div></ScrollPanel>;
  const side=<>{repairs.length>0&&<ScrollPanel title="Repair requests for you" label="Repair requests for you" footer={<FitNote text="Turn each request into a work order to track parts, SLA and downtime."/>}>
      <div className="request-list">{repairs.map(t=><div className="request" key={t.id}><div><strong>Room {t.room} · {t.note}</strong><p>Reported {t.createdAt?mntWhen(t.createdAt):''}</p></div><button className="btn" disabled={busy} onClick={()=>run({type:'MNT_WO_FROM_REPAIR',taskId:t.id},'Work order created from the repair request')}>Create work order <ArrowRight size={14}/></button></div>)}</div></ScrollPanel>}
    {done.length>0&&<ScrollPanel title="Recently finished" label="Recently finished work orders"><div className="mnt-cards">{done.map(w=><MntWoCard key={w.id} w={w}/>)}</div></ScrollPanel>}</>;
  return <FitPage title="My work orders" sub="Your assigned jobs, most urgent first. Rooms stay blocked until a supervisor verifies the work." actions={<button className="btn primary" onClick={()=>open({type:'mnt-new'})}><Wrench size={16}/> Report a fault</button>}>
    {repairs.length>0||done.length>0?<div className="fit-cols"><div>{main}</div><div>{side}</div></div>:main}
  </FitPage>;
}

function MntHousekeeperView(){
  const {s,uid}=useHcc();const list=mntSort(mntVisibleOrders(s,uid));
  return <FitPage title="Maintenance" sub="Open repairs on the rooms you are cleaning today."
      toolbar={<FitNote lines={2} text={<><Info size={16}/> To report a new problem, use “Report repair” on your housekeeping task card.</>}/>}>
    <ScrollPanel label="Open repairs on your rooms">{list.length?<div className="request-list">{list.map(w=><div className="request" key={w.id}><div><strong>{mntWhere(s,w)} · {w.title}</strong><p>{w.id} · {w.status}</p></div><MntStatus st={w.status}/></div>)}</div>:<Empty title="No open repairs on your rooms"/>}</ScrollPanel>
  </FitPage>;
}

function MntOrdersTab(){
  const {s,uid,open,run,busy}=useHcc();const x=mntSlice(s);
  const [status,setStatus]=useState<'Active'|'All'|MNTStatus>('Active'),[prio,setPrio]=useState('All'),[who,setWho]=useState('All'),[q,setQ]=useState('');
  const sum=mntSummary(s);const repairs=mntPendingRepairs(s);
  const people=[...new Set(x.orders.map(w=>w.vendorId?'v:'+w.vendorId:w.assignee?'u:'+w.assignee:'none'))];
  const list=mntSort(x.orders.filter(w=>(status==='All'||(status==='Active'?mntIsActive(w)||w.status==='Done':w.status===status))&&(prio==='All'||w.priority===prio)
    &&(who==='All'||(who==='none'?!w.assignee&&!w.vendorId:who==='v:'+w.vendorId||who==='u:'+w.assignee))
    &&(!q||`${w.id} ${w.title} ${mntWhere(s,w)} ${mntAsset(s,w.assetId)?.name??''}`.toLowerCase().includes(q.toLowerCase()))));
  const table=<ScrollPanel label="Work orders">
      {list.length?<div className="table-wrap"><table className="mnt-table"><thead><tr><th>Work order</th><th>Location / asset</th><th>Priority</th><th>Status</th><th>Assigned</th><th>Age</th><th>SLA</th><th/></tr></thead><tbody>
        {list.map(w=>{const room=w.room?s.rooms.find(r=>r.id===w.room):undefined;return <tr key={w.id}>
          <td><button className="mnt-rowlink" onClick={()=>open({type:'mnt-wo',id:w.id})}><strong>{w.id}</strong><small>{w.title}{w.kind==='Preventive'?' · PM':''}</small></button></td>
          <td>{mntWhere(s,w)}{room?.blocked&&<> <Lock size={11} aria-label="room blocked"/></>}<small>{mntAsset(s,w.assetId)?.name??''}</small></td>
          <td><MntPriority p={w.priority}/></td><td><MntStatus st={w.status}/></td><td>{mntAssigneeLabel(s,w)}</td>
          <td className="number">{mntIsActive(w)?mntDuration(mntAge(w,s.clock)):w.doneAt?shortDate(w.doneAt.slice(0,10)):'—'}</td><td><MntSla w={w}/></td>
          <td><button className="text-btn" onClick={()=>open({type:'mnt-wo',id:w.id})}>Open</button></td></tr>;})}</tbody></table></div>:<Empty title="No work orders match" body="Change the filters to see more."/>}
    </ScrollPanel>;
  const requests=<ScrollPanel title="Repair requests from housekeeping" label="Repair requests from housekeeping" actions={<Pill tone="orange">{repairs.length} waiting</Pill>}
      footer={<FitNote text="Reported on task cards (stream D). Convert to a work order to track SLA, parts and room downtime."/>}>
      <div className="request-list">{repairs.map(t=>{const r=s.rooms.find(r=>r.id===t.room);return <div className="request" key={t.id}><div><strong>Room {t.room} · {t.note}</strong><p>{t.id} · reported {t.createdAt?mntWhen(t.createdAt):'—'} · {s.users.find(u=>u.id===t.assignee)?.name.split(' · ')[0]}{r?.blocked?' · room blocked':''}</p></div>
        <button className="btn" disabled={busy} onClick={()=>run({type:'MNT_WO_FROM_REPAIR',taskId:t.id,priority:'P2'},'Work order created from the repair request')}>Create work order <ArrowRight size={14}/></button></div>;})}</div></ScrollPanel>;
  return <>
    <StatStrip items={[{title:'Open',value:String(sum.open)},{title:'In progress',value:String(sum.inProgress)},{title:'On hold (parts)',value:String(sum.onHold)},{title:'Awaiting check',value:String(sum.done)},
      {title:'SLA breached',value:String(sum.breached),tone:sum.breached?'danger':undefined}]}/>
    <div className="mnt-filters fit-ops-bar" role="group" aria-label="Filter work orders">
        <label>Status<select value={status} onChange={e=>setStatus(e.target.value as typeof status)}><option value="Active">Active + awaiting check</option><option value="All">All</option>{(['Open','In progress','On hold','Done','Verified','Cancelled'] as MNTStatus[]).map(v=><option key={v}>{v}</option>)}</select></label>
        <label>Priority<select value={prio} onChange={e=>setPrio(e.target.value)}><option value="All">All</option>{MNT_PRIORITIES.map(p=><option key={p} value={p}>{p} · {MNT_PRIORITY[p].label}</option>)}</select></label>
        <label>Assigned<select value={who} onChange={e=>setWho(e.target.value)}><option value="All">Everyone</option>{people.map(p=><option key={p} value={p}>{p==='none'?'Unassigned':p.startsWith('v:')?mntVendor(s,p.slice(2))?.name:s.users.find(u=>u.id===p.slice(2))?.name.split(' · ')[0]}</option>)}</select></label>
        <label className="mnt-grow">Search<input value={q} onChange={e=>setQ(e.target.value)} placeholder="WO, room, asset…"/></label>
        <span className="muted">{list.length} of {x.orders.length}</span>
    </div>
    {repairs.length>0?<div className="fit-cols"><div>{table}</div><div>{requests}</div></div>:table}
  </>;
}

function MntPmTab(){
  const {s,run,open,busy}=useHcc();const x=mntSlice(s);const due=mntPmDue(s);const c=mntPmCompliance(s);
  const tone={Overdue:'mnt-red',Due:'orange',Scheduled:'green',Paused:'neutral'} as const;
  return <>
    <div className="mnt-pm-head card"><div className="padded mnt-pm-row">
      <div><div className="eyebrow">PM COMPLIANCE</div><strong className="mnt-big-num">{c.pct===null?'—':c.pct+'%'}</strong><small>{c.onTime} of {c.due} due preventive jobs done on time</small></div>
      <div className="mnt-pm-gen"><p>{due.length?`${due.length} occurrence(s) due by ${shortDate(s.date)} + 3 days without a work order.`:'All due preventive work orders already exist.'}</p>
        <button className="btn primary" disabled={busy||!due.length} onClick={()=>run({type:'MNT_PM_GENERATE',key:'pm:'+s.date+':'+s.sequence},'Preventive work orders generated')}><Sparkles size={15}/> Generate due work orders{due.length?` (${due.length})`:''}</button></div></div></div>
    <ScrollPanel title="PM schedule" label="PM schedule" actions={<button className="btn" onClick={()=>open({type:'mnt-plan'})}><Plus size={15}/> Add plan</button>}
        footer={<FitNote text={`Generation uses the business date (${dateLabel(s.date)}), never the device clock.`}/>}>
      <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Plan</th><th>Asset · location</th><th>Every</th><th>Next due</th><th>Last done</th><th>State</th><th/></tr></thead><tbody>
        {x.plans.map(p=>{const a=mntAsset(s,p.assetId),st=mntPlanState(s,p);return <tr key={p.id}><td><strong>{p.title}</strong><small>{p.id}</small></td><td>{a?.name??p.assetId}<small>{a?mntWhere(s,a):''}</small></td>
          <td>{p.everyDays} days</td><td>{dateLabel(p.nextDue)}</td><td>{p.lastDone?dateLabel(p.lastDone):'—'}</td><td><Pill tone={tone[st]}>{st}</Pill></td><td><button className="text-btn" onClick={()=>open({type:'mnt-plan',id:p.id})}>Edit</button></td></tr>;})}</tbody></table></div></ScrollPanel>
  </>;
}

function MntAssetsTab(){
  const {s,open}=useHcc();const x=mntSlice(s);const [kind,setKind]=useState('All');
  const kinds=[...new Set(x.assets.map(a=>a.kind))];const list=x.assets.filter(a=>kind==='All'||a.kind===kind);
  return <ScrollPanel title="Asset register" label="Asset register" footer={<FitNote text={`${x.assets.length} assets in rooms and common areas (fictional makes and models).`}/>}
      actions={<><select aria-label="Asset type" value={kind} onChange={e=>setKind(e.target.value)}><option value="All">All types</option>{kinds.map(k=><option key={k}>{k}</option>)}</select><button className="btn" onClick={()=>open({type:'mnt-asset'})}><Plus size={15}/> Add asset</button></>}>
    <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Asset</th><th>Type</th><th>Location</th><th>Make / model</th><th>Installed</th><th>Vendor · AMC</th><th>Open WOs</th><th>Status</th></tr></thead><tbody>
      {list.map(a=>{const v=mntVendor(s,a.vendorId),amc=x.amcs.find(m=>m.vendorId===a.vendorId),openWo=x.orders.filter(w=>w.assetId===a.id&&mntIsActive(w)).length;
        return <tr key={a.id}><td><button className="mnt-rowlink" onClick={()=>open({type:'mnt-asset',id:a.id})}><strong>{a.name}</strong><small>{a.id}</small></button></td><td>{a.kind}</td><td>{mntWhere(s,a)}</td><td>{a.make}<small>{a.model}</small></td>
          <td>{a.installedOn?shortDate(a.installedOn)+' '+a.installedOn.slice(0,4):'—'}</td><td>{v?.name??'—'}<small>{amc?`AMC ${mntAmcStatus(amc,s.date).toLowerCase()} · to ${shortDate(amc.to)}`:'No AMC'}</small></td>
          <td className="number">{openWo||'—'}</td><td><Pill tone={a.status==='Working'?'green':a.status==='Faulty'?'orange':'neutral'}>{a.status}</Pill></td></tr>;})}</tbody></table></div></ScrollPanel>;
}

function MntSparesTab(){
  const {s,uid,open}=useHcc();const x=mntSlice(s);const cost=mntCanSeeCost(s,uid);
  return <ScrollPanel title="Spares" label="Spares" actions={mntLowSpares(s).length>0?<Pill tone="orange">{mntLowSpares(s).length} at or below reorder level</Pill>:undefined}
      footer={<FitNote text={'Small stock of common parts. Parts booked on work orders reduce stock.'+(cost?'':' Costs are visible to roles with reports or expenses access.')}/>}>
    <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Spare</th><th>In stock</th><th>Reorder at</th>{cost&&<th>Unit cost</th>}{cost&&<th>Stock value</th>}<th>Status</th><th/></tr></thead><tbody>
      {x.spares.map(p=><tr key={p.id}><td><strong>{p.name}</strong><small>{p.id}</small></td><td className="number">{p.stock} {p.unit}</td><td className="number">{p.reorderAt}</td>
        {cost&&<td className="number">{money(p.cost)}</td>}{cost&&<td className="number">{money(p.cost*p.stock)}</td>}
        <td>{p.stock===0?<Pill tone="mnt-red">Out of stock</Pill>:p.stock<=p.reorderAt?<Pill tone="orange">Reorder</Pill>:<Pill tone="green">OK</Pill>}</td>
        <td><button className="text-btn" onClick={()=>open({type:'mnt-receive',id:p.id})}>Receive</button></td></tr>)}</tbody></table></div></ScrollPanel>;
}

function MntVendorsTab(){
  const {s,uid,open,permission}=useHcc();const x=mntSlice(s);const cost=mntCanSeeCost(s,uid);
  const tone={Active:'green',Expiring:'orange',Expired:'mnt-red','Not started':'neutral'} as const;
  return <div className="fit-cols fit-cols-even">
    <div><ScrollPanel title="Vendors" label="Vendors" actions={<button className="btn" onClick={()=>open({type:'mnt-vendor'})}><Plus size={15}/> Add vendor</button>}
        footer={<FitNote text="Fictional service companies. Contact is simulated — nothing is sent."/>}>
      <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Vendor</th><th>Trade</th><th>Phone</th><th>Assets</th><th/></tr></thead><tbody>
        {x.vendors.map(v=><tr key={v.id}><td><strong>{v.name}</strong><small>{v.id}</small></td><td>{v.trade}</td><td>{v.phone}</td><td className="number">{x.assets.filter(a=>a.vendorId===v.id).length}</td><td><button className="text-btn" onClick={()=>open({type:'mnt-vendor',id:v.id})}>Edit</button></td></tr>)}</tbody></table></div></ScrollPanel></div>
    <div><ScrollPanel title="AMC contracts" label="AMC contracts" actions={permission('expenses')?<button className="btn" onClick={()=>open({type:'mnt-amc'})}><Plus size={15}/> Add AMC</button>:undefined}
        footer={<FitNote text="Expiring = ends within 30 days of the business date."/>}>
      <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Contract</th><th>Period</th><th>Visits</th>{cost&&<th>Value</th>}<th>Status</th>{permission('expenses')&&<th/>}</tr></thead><tbody>
        {x.amcs.map(a=>{const st=mntAmcStatus(a,s.date);return <tr key={a.id}><td><strong>{mntVendor(s,a.vendorId)?.name}</strong><small>{a.scope}</small></td><td>{shortDate(a.from)} {a.from.slice(0,4)} → {shortDate(a.to)} {a.to.slice(0,4)}</td><td className="number">{a.visits}/yr</td>
          {cost&&<td className="number">{money(a.value)}</td>}<td><Pill tone={tone[st]}>{st}</Pill></td>{permission('expenses')&&<td><button className="text-btn" onClick={()=>open({type:'mnt-amc',id:a.id})}>Edit</button></td>}</tr>;})}</tbody></table></div></ScrollPanel></div>
  </div>;
}

export function mntReportData(s:ReturnType<typeof useHcc>['s']){
  return {buckets:mntAgeBuckets(s),mttr:mntMttr(s),pm:mntPmCompliance(s),down:mntDowntimeByRoom(s),kinds:mntByKind(s),parts:mntMonthPartsCost(s),sum:mntSummary(s)};
}
function MntReportsTab(){
  const {s,uid}=useHcc();const r=mntReportData(s);const cost=mntCanSeeCost(s,uid);const max=Math.max(1,...r.buckets.map(b=>b.count));
  const x=mntSlice(s);const breached=x.orders.filter(w=>mntSla(w,s.clock)==='Breached');
  return <div className="mnt-report">
    <div className="mnt-print-head"><strong>{s.settings.hotel} · Maintenance report</strong><span>Business date {dateLabel(s.date)} · simulated clock {s.clock.slice(11)} · fictional data</span></div>
    <div className="section-title compact mnt-noprint"><div/><button className="btn" onClick={()=>{void printDocument();}}><Printer size={15}/> Print report</button></div>
    <div className="stats">
      <div className="stat"><div className="stat-label">Mean time to repair</div><strong className="money-stat">{r.mttr===null?'—':mntDuration(r.mttr)}</strong><small>Corrective work orders, reported → done</small></div>
      <div className="stat"><div className="stat-label">PM compliance</div><strong>{r.pm.pct===null?'—':r.pm.pct}<span>%</span></strong><small>{r.pm.onTime} of {r.pm.due} due jobs done on time</small></div>
      <div className="stat"><div className="stat-label">SLA breaches</div><strong>{r.sum.breached}</strong><small>Open past due, or done late</small></div>
      {cost?<div className="stat"><div className="stat-label">Parts cost this month</div><strong className="money-stat">{money(r.parts)}</strong><small>Work orders completed in {s.date.slice(0,7)}</small></div>
        :<div className="stat"><div className="stat-label">Open work orders</div><strong>{r.sum.open+r.sum.inProgress+r.sum.onHold}</strong><small>{r.sum.done} awaiting verification</small></div>}
    </div>
    <div className="two-col mnt-two">
      <div className="card"><div className="section-title"><div><h2>Open work orders by age</h2><p>Open, in progress and on hold.</p></div></div>
        <div className="bar-chart">{r.buckets.map(b=><div className="bar-row mnt-bar" key={b.label}><div><span>{b.label}</span><strong>{b.count}</strong></div><div className="track"><span style={{width:`${b.count*100/max}%`}}/></div></div>)}</div></div>
      <div className="card"><div className="section-title"><div><h2>Room downtime</h2><p>From a maintenance block until verification or unblock.</p></div></div>
        {r.down.length?<div className="table-wrap"><table className="mnt-table"><thead><tr><th>Room</th><th>Downtime</th><th>Work orders</th><th>Now</th></tr></thead><tbody>
          {r.down.map(d=><tr key={d.room}><td><strong>Room {d.room}</strong></td><td className="number">{mntDuration(d.minutes)}</td><td className="number">{d.orders}</td><td>{d.running?<Pill tone="orange">Blocked</Pill>:<Pill tone="green">Back in use</Pill>}</td></tr>)}</tbody></table></div>:<Empty title="No room downtime recorded"/>}</div>
    </div>
    <div className="two-col mnt-two">
      <div className="card"><div className="section-title"><div><h2>Work orders by asset type</h2></div></div>
        <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Type</th><th>Open</th><th>Total</th></tr></thead><tbody>{r.kinds.map(k=><tr key={k.kind}><td>{k.kind}</td><td className="number">{k.open}</td><td className="number">{k.total}</td></tr>)}</tbody></table></div></div>
      <div className="card"><div className="section-title"><div><h2>SLA breaches</h2></div></div>
        {breached.length?<div className="table-wrap"><table className="mnt-table"><thead><tr><th>WO</th><th>Priority</th><th>Status</th>{cost&&<th>Parts</th>}</tr></thead><tbody>{breached.map(w=><tr key={w.id}><td><strong>{w.id}</strong><small>{w.title} · {mntWhere(s,w)}</small></td><td>{w.priority}</td><td>{w.status}</td>{cost&&<td className="number">{money(mntPartsCost(w))}</td>}</tr>)}</tbody></table></div>:<Empty title="No SLA breaches"/>}</div>
    </div>
  </div>;
}

/** FIT: the Reports tab on the desktop layout — the same figures as MntReportsTab (which stays for the print dialog), in one row of stats and four panels. */
function MntReportsFit(){
  const {s,uid}=useHcc();const r=mntReportData(s);const cost=mntCanSeeCost(s,uid);const max=Math.max(1,...r.buckets.map(b=>b.count));
  const x=mntSlice(s);const breached=x.orders.filter(w=>mntSla(w,s.clock)==='Breached');
  return <div className="mnt-report fit-stack">
    <div className="mnt-print-head"><strong>{s.settings.hotel} · Maintenance report</strong><span>Business date {dateLabel(s.date)} · simulated clock {s.clock.slice(11)} · fictional data</span></div>
    <div className="fit-ops-bar"><StatStrip items={[
      {title:'Mean time to repair',value:r.mttr===null?'—':mntDuration(r.mttr),sub:'Corrective work orders, reported → done'},
      {title:'PM compliance',value:r.pm.pct===null?'—':r.pm.pct+'%',sub:`${r.pm.onTime} of ${r.pm.due} due jobs done on time`},
      {title:'SLA breaches',value:String(r.sum.breached),sub:'Open past due, or done late'},
      cost?{title:'Parts cost this month',value:money(r.parts),sub:`Work orders completed in ${s.date.slice(0,7)}`}
        :{title:'Open work orders',value:String(r.sum.open+r.sum.inProgress+r.sum.onHold),sub:`${r.sum.done} awaiting verification`}]}/>
      <button className="btn mnt-noprint" onClick={()=>{void printDocument();}}><Printer size={15}/> Print report</button></div>
    <div className="fit-ops-grid">
      <ScrollPanel title="Open work orders by age" label="Open work orders by age" footer={<FitNote text="Open, in progress and on hold."/>}>
        <div className="bar-chart">{r.buckets.map(b=><div className="bar-row mnt-bar" key={b.label}><div><span>{b.label}</span><strong>{b.count}</strong></div><div className="track"><span style={{width:`${b.count*100/max}%`}}/></div></div>)}</div></ScrollPanel>
      <ScrollPanel title="Room downtime" label="Room downtime" footer={<FitNote text="From a maintenance block until verification or unblock."/>}>
        {r.down.length?<div className="table-wrap"><table className="mnt-table"><thead><tr><th>Room</th><th>Downtime</th><th>Work orders</th><th>Now</th></tr></thead><tbody>
          {r.down.map(d=><tr key={d.room}><td><strong>Room {d.room}</strong></td><td className="number">{mntDuration(d.minutes)}</td><td className="number">{d.orders}</td><td>{d.running?<Pill tone="orange">Blocked</Pill>:<Pill tone="green">Back in use</Pill>}</td></tr>)}</tbody></table></div>:<Empty title="No room downtime recorded"/>}</ScrollPanel>
      <ScrollPanel title="Work orders by asset type" label="Work orders by asset type">
        <div className="table-wrap"><table className="mnt-table"><thead><tr><th>Type</th><th>Open</th><th>Total</th></tr></thead><tbody>{r.kinds.map(k=><tr key={k.kind}><td>{k.kind}</td><td className="number">{k.open}</td><td className="number">{k.total}</td></tr>)}</tbody></table></div></ScrollPanel>
      <ScrollPanel title="SLA breaches" label="SLA breaches">
        {breached.length?<div className="table-wrap"><table className="mnt-table"><thead><tr><th>WO</th><th>Priority</th><th>Status</th>{cost&&<th>Parts</th>}</tr></thead><tbody>{breached.map(w=><tr key={w.id}><td><strong>{w.id}</strong><small>{w.title} · {mntWhere(s,w)}</small></td><td>{w.priority}</td><td>{w.status}</td>{cost&&<td className="number">{money(mntPartsCost(w))}</td>}</tr>)}</tbody></table></div>:<Empty title="No SLA breaches"/>}</ScrollPanel>
    </div>
  </div>;
}

export {MntReportsFit,MntReportsTab,MntPmTab,MntAssetsTab,MntSparesTab,MntVendorsTab};
export type {MNTWorkOrder};
