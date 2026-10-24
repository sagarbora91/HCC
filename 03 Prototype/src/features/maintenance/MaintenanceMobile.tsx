// Module 15 · Maintenance — phone page. Technician: big cards with Start / Hold / Complete and a "Report fault" FAB.
// Supervisor/manager: section chips, KPI cards and list cards (tables become cards); FAB "New work order".
import React,{useState} from 'react';
import {Plus,Wrench,Sparkles,Printer,Lock,ArrowRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {mntSlice,mntVisibleOrders,mntCanManage,mntCanSeeCost,mntSort,mntSummary,mntPendingRepairs,mntPmDue,mntPmCompliance,mntPlanState,mntAmcStatus,
  mntWhere,mntAsset,mntVendor,mntAssigneeLabel,mntDuration,mntAge,mntLowSpares,mntIsActive,money} from '../../services/mock';
import {MntWoCard,MntStatus,MntPriority,MntSla,mntWhen} from './shared';
import {MNT_TABS,mntReportData,type MntTab} from './Maintenance';

export function MaintenanceMobile(){
  const {s,uid,user}=useHcc();
  if(user.role==='Maintenance')return <MntTechMobile/>;
  if(!mntCanManage(s,uid))return <MntHkMobile/>;
  return <MntManagerMobile/>;
}

function MntTechMobile(){
  const {s,uid,open,run,busy}=useHcc();
  const mine=mntSort(mntVisibleOrders(s,uid));const repairs=mntPendingRepairs(s).filter(t=>t.assignee===uid);
  const [f,setF]=useState<'active'|'hold'|'done'>('active');
  const groups={active:mine.filter(w=>w.status==='Open'||w.status==='In progress'),hold:mine.filter(w=>w.status==='On hold'),done:mine.filter(w=>!mntIsActive(w))};
  return <div className="mnt-m">
    <h1 className="mnt-m-title">My work orders</h1>
    <ChipFilter label="Work order status" value={f} onChange={setF} options={[{id:'active',label:'To do',count:groups.active.length},{id:'hold',label:'On hold',count:groups.hold.length},{id:'done',label:'Finished',count:groups.done.length}]}/>
    {f==='active'&&repairs.map(t=><div className="mnt-card mnt-card-large" key={t.id}><span className="mnt-card-id">Repair request · {t.id}</span><h3>Room {t.room} · {t.note}</h3><p className="mnt-meta">Reported {t.createdAt?mntWhen(t.createdAt):''}</p>
      <button className="btn full mnt-big" disabled={busy} onClick={()=>run({type:'MNT_WO_FROM_REPAIR',taskId:t.id},'Work order created')}>Create work order <ArrowRight size={16}/></button></div>)}
    {groups[f].map(w=><MntWoCard key={w.id} w={w} large/>)}
    {!groups[f].length&&!(f==='active'&&repairs.length)&&<Empty title={f==='done'?'Nothing finished yet':'Nothing here'} body="Work orders assigned to you appear here."/>}
    <Fab label="Report fault" icon={Wrench} onClick={()=>open({type:'mnt-new'})}/>
  </div>;
}

function MntHkMobile(){
  const {s,uid}=useHcc();const list=mntSort(mntVisibleOrders(s,uid));
  return <div className="mnt-m"><h1 className="mnt-m-title">Maintenance</h1><p className="mnt-m-sub">Open repairs on your rooms. Report new problems from your task card.</p>
    {list.map(w=><ListCard key={w.id} title={`${mntWhere(s,w)} · ${w.title}`} sub={w.id} right={<MntStatus st={w.status}/>}/>)}
    {!list.length&&<Empty title="No open repairs on your rooms"/>}</div>;
}

function MntManagerMobile(){
  const {s,uid,open,run,busy}=useHcc();const x=mntSlice(s);const cost=mntCanSeeCost(s,uid);
  const [tab,setTab]=useState<MntTab>('orders');const [st,setSt]=useState<'active'|'check'|'closed'>('active');
  const sum=mntSummary(s),repairs=mntPendingRepairs(s),due=mntPmDue(s);
  const lists={active:x.orders.filter(mntIsActive),check:x.orders.filter(w=>w.status==='Done'),closed:x.orders.filter(w=>w.status==='Verified'||w.status==='Cancelled')};
  return <div className="mnt-m">
    <h1 className="mnt-m-title">Maintenance</h1>
    <ChipFilter label="Maintenance section" value={tab} onChange={setTab} options={MNT_TABS.map(t=>({id:t.id,label:t.label,count:t.id==='orders'?lists.active.length:t.id==='pm'?due.length||undefined:t.id==='spares'?mntLowSpares(s).length||undefined:undefined}))}/>
    {tab==='orders'&&<>
      <div className="mnt-kpis"><KpiCard label="Open" value={sum.open} sub={`${sum.inProgress} in progress`} onClick={()=>setSt('active')}/><KpiCard label="On hold" value={sum.onHold} sub="waiting for parts" onClick={()=>setSt('active')}/>
        <KpiCard label="Awaiting check" value={sum.done} sub="verify the work" onClick={()=>setSt('check')}/><KpiCard label="SLA breached" value={sum.breached} sub="open or done late"/></div>
      {repairs.length>0&&<><h2 className="m-group">REPAIR REQUESTS</h2>{repairs.map(t=><ListCard key={t.id} title={`Room ${t.room} · ${t.note}`} sub={`${t.id} · tap to create a work order`} right={<Pill tone="orange">New</Pill>} onClick={()=>{if(!busy)run({type:'MNT_WO_FROM_REPAIR',taskId:t.id,priority:'P2'},'Work order created from the repair request');}}/>)}</>}
      <ChipFilter label="Work order list" value={st} onChange={setSt} options={[{id:'active',label:'Active',count:lists.active.length},{id:'check',label:'Awaiting check',count:lists.check.length},{id:'closed',label:'Closed',count:lists.closed.length}]}/>
      {mntSort(lists[st]).map(w=>{const room=w.room?s.rooms.find(r=>r.id===w.room):undefined;return <ListCard key={w.id} onClick={()=>open({type:'mnt-wo',id:w.id})}
        title={<>{w.title}</>} sub={<>{mntWhere(s,w)}{room?.blocked&&<> · <Lock size={12} aria-label="room blocked"/> blocked</>} · {mntAssigneeLabel(s,w)}</>}
        meta={<span className="mnt-tags"><MntPriority p={w.priority}/><MntStatus st={w.status}/><MntSla w={w}/></span>}
        right={<small className="number">{mntIsActive(w)?mntDuration(mntAge(w,s.clock)):w.id}</small>}/>;})}
      {!lists[st].length&&<Empty title="No work orders here"/>}
      <Fab label="New work order" icon={Plus} onClick={()=>open({type:'mnt-new'})}/>
    </>}
    {tab==='pm'&&(()=>{const c=mntPmCompliance(s);return <>
      <div className="mnt-kpis"><KpiCard label="PM compliance" value={c.pct===null?'—':c.pct+'%'} sub={`${c.onTime} of ${c.due} on time`}/><KpiCard label="Due to generate" value={due.length} sub={`by ${shortDate(s.date)} + 3 days`}/></div>
      <button className="btn primary full mnt-big" disabled={busy||!due.length} onClick={()=>run({type:'MNT_PM_GENERATE',key:'pm:'+s.date+':'+s.sequence},'Preventive work orders generated')}><Sparkles size={17}/> Generate due work orders</button>
      <h2 className="m-group">PM SCHEDULE</h2>
      {x.plans.map(p=>{const a=mntAsset(s,p.assetId),ps=mntPlanState(s,p);return <ListCard key={p.id} onClick={()=>open({type:'mnt-plan',id:p.id})} title={p.title} sub={`${a?.name??p.assetId} · every ${p.everyDays} d`} meta={`Next due ${shortDate(p.nextDue)}${p.lastDone?' · last '+shortDate(p.lastDone):''}`} right={<Pill tone={ps==='Overdue'?'mnt-red':ps==='Due'?'orange':ps==='Paused'?'neutral':'green'}>{ps}</Pill>}/>;})}
      <Fab label="Add plan" icon={Plus} onClick={()=>open({type:'mnt-plan'})}/></>;})()}
    {tab==='assets'&&<>{x.assets.map(a=><ListCard key={a.id} onClick={()=>open({type:'mnt-asset',id:a.id})} title={a.name} sub={`${a.kind} · ${mntWhere(s,a)}`} meta={`${a.make} ${a.model}${a.vendorId?' · '+mntVendor(s,a.vendorId)?.name:''}`} right={<Pill tone={a.status==='Working'?'green':a.status==='Faulty'?'orange':'neutral'}>{a.status}</Pill>}/>)}
      <Fab label="Add asset" icon={Plus} onClick={()=>open({type:'mnt-asset'})}/></>}
    {tab==='spares'&&<>{x.spares.map(p=><ListCard key={p.id} onClick={()=>open({type:'mnt-receive',id:p.id})} title={p.name} sub={`${p.stock} ${p.unit} in stock · reorder at ${p.reorderAt}`} meta={cost?`${money(p.cost)} each · tap to receive`:'Tap to receive stock'}
      right={p.stock===0?<Pill tone="mnt-red">Out</Pill>:p.stock<=p.reorderAt?<Pill tone="orange">Reorder</Pill>:<Pill tone="green">OK</Pill>}/>)}</>}
    {tab==='vendors'&&<>
      <h2 className="m-group">AMC CONTRACTS</h2>
      {x.amcs.map(a=>{const st2=mntAmcStatus(a,s.date);return <ListCard key={a.id} onClick={cost?()=>open({type:'mnt-amc',id:a.id}):undefined} title={mntVendor(s,a.vendorId)?.name??a.vendorId} sub={a.scope} meta={`${shortDate(a.from)} ${a.from.slice(0,4)} → ${shortDate(a.to)} ${a.to.slice(0,4)} · ${a.visits} visits/yr${cost?' · '+money(a.value):''}`} right={<Pill tone={st2==='Active'?'green':st2==='Expiring'?'orange':st2==='Expired'?'mnt-red':'neutral'}>{st2}</Pill>}/>;})}
      <h2 className="m-group">VENDORS</h2>
      {x.vendors.map(v=><ListCard key={v.id} onClick={()=>open({type:'mnt-vendor',id:v.id})} title={v.name} sub={v.trade} meta={v.phone}/>)}
      <Fab label="Add vendor" icon={Plus} onClick={()=>open({type:'mnt-vendor'})}/></>}
    {tab==='reports'&&(()=>{const r=mntReportData(s);return <>
      <div className="mnt-kpis"><KpiCard label="MTTR" value={r.mttr===null?'—':mntDuration(r.mttr)} sub="reported → done"/><KpiCard label="PM compliance" value={r.pm.pct===null?'—':r.pm.pct+'%'} sub={`${r.pm.onTime} of ${r.pm.due}`}/>
        <KpiCard label="SLA breaches" value={r.sum.breached}/>{cost?<KpiCard label="Parts this month" value={money(r.parts)}/>:<KpiCard label="Awaiting check" value={r.sum.done}/>}</div>
      <h2 className="m-group">OPEN WORK ORDERS BY AGE</h2>
      {r.buckets.map(b=><ListCard key={b.label} title={b.label} right={<strong className="number">{b.count}</strong>}/>)}
      <h2 className="m-group">ROOM DOWNTIME</h2>
      {r.down.map(d=><ListCard key={d.room} title={`Room ${d.room}`} sub={`${d.orders} work order(s)`} meta={d.running?'Still blocked':'Back in use'} right={<strong className="number">{mntDuration(d.minutes)}</strong>}/>)}
      {!r.down.length&&<Empty title="No room downtime recorded"/>}
      <h2 className="m-group">BY ASSET TYPE</h2>
      {r.kinds.map(k=><ListCard key={k.kind} title={k.kind} sub={`${k.open} open`} right={<strong className="number">{k.total}</strong>}/>)}
      <button className="btn full mnt-big" onClick={()=>open({type:'mnt-print'})}><Printer size={17}/> Print report…</button></>;})()}
  </div>;
}
