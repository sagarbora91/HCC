// Module 18 · Guest services — desktop page (requests, complaints, lost & found, reports) and "My guest requests" (assigned staff).
import React,{useState} from 'react';
import {Plus,MessageSquareWarning,PackageSearch,Printer,Play,CheckCircle2,Info} from 'lucide-react';
import {gsvSlice,gsvSlaLabel,gsvIsOpen,gsvOverdue,gsvRecoveryStatus,gsvFollowUpDue,gsvDisposable,gsvItemAgeDays,gsvResponseTotals,gsvComplaintOpen,gsvVisibleOps,gsvCanWork,
  type GSVRequest} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty} from '../../components/ui';
import {gsvWhen,gsvUserName,gsvStatusTone,gsvSevTone,GsvReportBody} from './modals';

export type GsvTab='Requests'|'Complaints'|'Lost & found'|'Reports';
export const gsvTabs:GsvTab[]=['Requests','Complaints','Lost & found','Reports'];
export type GsvReqFilter='Open'|'Overdue'|'Done'|'All';
export function gsvFilterRequests(s:ReturnType<typeof useHcc>['s'],f:GsvReqFilter,list=gsvSlice(s).requests){
  const xs=f==='Open'?list.filter(gsvIsOpen):f==='Overdue'?list.filter(r=>gsvOverdue(s,r)):f==='Done'?list.filter(r=>!gsvIsOpen(r)):list;
  return xs.slice().sort((a,b)=>gsvIsOpen(a)!==gsvIsOpen(b)?(gsvIsOpen(a)?-1:1):gsvIsOpen(a)?(a.dueAt<b.dueAt?-1:1):(a.createdAt<b.createdAt?1:-1));
}

export function GuestServicesPage(){
  const {s,open,permission}=useHcc();
  const [tab,setTab]=useState<GsvTab>('Requests');
  const x=gsvSlice(s),t=gsvResponseTotals(s);
  const counts:Record<GsvTab,number>={Requests:x.requests.filter(gsvIsOpen).length,Complaints:x.complaints.filter(gsvComplaintOpen).length,'Lost & found':x.items.filter(i=>i.status==='Stored'||i.status==='Claimed').length,Reports:0};
  const action=tab==='Complaints'?<button className="btn primary" onClick={()=>open({type:'gsv-complaint-new'})}><MessageSquareWarning size={16}/> Log complaint</button>
    :tab==='Lost & found'?<button className="btn primary" onClick={()=>open({type:'gsv-item-new'})}><PackageSearch size={16}/> Log found item</button>
    :tab==='Reports'?<button className="btn" onClick={()=>open({type:'gsv-print'})}><Printer size={16}/> Print report</button>
    :<button className="btn primary" onClick={()=>open({type:'gsv-request-new'})}><Plus size={16}/> New request</button>;
  return <>
    <PageTitle title="Guest services" sub="Requests with SLA timers, complaints with recovery, and lost & found — on the simulated hotel clock." action={action}/>
    <section className="stats gsv-stats">
      <div className="stat"><div className="stat-label">Open requests</div><strong className="money-stat">{counts.Requests}</strong><small>{t.overdue} overdue</small></div>
      <div className="stat"><div className="stat-label">Within SLA</div><strong className="money-stat">{t.done?t.pct+'%':'—'}</strong><small>avg {t.avg} min · {t.done} done</small></div>
      <div className="stat"><div className="stat-label">Open complaints</div><strong className="money-stat">{counts.Complaints}</strong><small>{x.complaints.filter(c=>gsvFollowUpDue(s,c)).length} follow-up due</small></div>
      <div className="stat"><div className="stat-label">Lost & found held</div><strong className="money-stat">{counts['Lost & found']}</strong><small>{x.items.filter(i=>gsvDisposable(s,i)).length} past retention</small></div>
    </section>
    <section className="card">
      <div className="tabs" role="tablist" aria-label="Guest services">{gsvTabs.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}{t!=='Reports'&&<span>{counts[t]}</span>}</button>)}</div>
      {tab==='Requests'&&<RequestsTable/>}
      {tab==='Complaints'&&<ComplaintsTable/>}
      {tab==='Lost & found'&&<ItemsTable/>}
      {tab==='Reports'&&<div className="padded"><GsvReportBody/></div>}
    </section>
    {!permission('billing')&&tab==='Complaints'&&<p className="footnote">Recovery discounts are applied by billing staff through the normal folio discount and approval.</p>}
  </>;
}

function RequestsTable(){
  const {s,open}=useHcc();
  const [f,setF]=useState<GsvReqFilter>('Open');
  const list=gsvFilterRequests(s,f);
  return <><div className="toolbar gsv-toolbar"><div className="filters">{(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(x=><button key={x} className={x===f?'active':''} aria-pressed={x===f} onClick={()=>setF(x)}>{x} ({gsvFilterRequests(s,x).length})</button>)}</div></div>
    {!list.length?<Empty title="No requests here" body="Log a guest request with “New request”."/>:<div className="table-wrap"><table><thead><tr><th>Room · guest</th><th>Request</th><th>Department · assignee</th><th>Logged</th><th>Status</th><th>SLA</th><th>Action</th></tr></thead>
    <tbody>{list.map(r=>{const sla=gsvSlaLabel(s,r);return <tr key={r.id}>
      <td><strong>Room {r.room}</strong><br/>{r.guest}</td><td>{r.type}{r.priority==='Urgent'&&<> <Pill tone="gsv-red">Urgent</Pill></>}<br/><small>{r.detail||r.id}</small></td>
      <td>{r.dept}<br/><small>{gsvUserName(s,r.assignee)}</small></td><td>{gsvWhen(r.createdAt)}</td>
      <td><Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></td><td><Pill tone={sla.tone}>{sla.label}</Pill></td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-request',id:r.id})}>{gsvIsOpen(r)?'Manage':'View'}</button></td></tr>;})}</tbody></table></div>}</>;
}
function ComplaintsTable(){
  const {s,open}=useHcc();
  const [f,setF]=useState<'Open'|'Follow-up due'|'Closed'|'All'>('Open');
  const all=gsvSlice(s).complaints;
  const pick=(x:typeof f)=>x==='Open'?all.filter(gsvComplaintOpen):x==='Follow-up due'?all.filter(c=>gsvFollowUpDue(s,c)):x==='Closed'?all.filter(c=>c.status==='Closed'):all;
  const list=pick(f).slice().sort((a,b)=>a.createdAt<b.createdAt?1:-1);
  return <><div className="toolbar gsv-toolbar"><div className="filters">{(['Open','Follow-up due','Closed','All'] as const).map(x=><button key={x} className={x===f?'active':''} aria-pressed={x===f} onClick={()=>setF(x)}>{x} ({pick(x).length})</button>)}</div></div>
    {!list.length?<Empty title="No complaints here"/>:<div className="table-wrap"><table><thead><tr><th>Guest</th><th>Category</th><th>Severity</th><th>Owner</th><th>Status</th><th>Recovery</th><th>Follow-up</th><th>Action</th></tr></thead>
    <tbody>{list.map(c=>{const rec=gsvRecoveryStatus(s,c);return <tr key={c.id}>
      <td><strong>{c.guest}</strong><br/><small>{c.id}{c.room?' · Room '+c.room:''}</small></td><td>{c.category}</td><td><Pill tone={gsvSevTone(c.severity)}>{c.severity}</Pill></td>
      <td>{gsvUserName(s,c.owner)}</td><td><Pill tone={gsvStatusTone(c.status)}>{c.status}</Pill></td>
      <td>{rec==='None'?(c.recoveryAction?'Action recorded':'—'):'Discount · '+rec}</td>
      <td>{c.followUp?(c.followUp.satisfied?'Satisfied':'Not satisfied'):c.followUpOn?(gsvFollowUpDue(s,c)?<Pill tone="orange">Due</Pill>:c.followUpOn):'—'}</td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-complaint',id:c.id})}>Open</button></td></tr>;})}</tbody></table></div>}</>;
}
function ItemsTable(){
  const {s,open}=useHcc();
  const [f,setF]=useState<'Held'|'Past retention'|'Closed'|'All'>('Held');
  const all=gsvSlice(s).items;
  const pick=(x:typeof f)=>x==='Held'?all.filter(i=>i.status==='Stored'||i.status==='Claimed'):x==='Past retention'?all.filter(i=>gsvDisposable(s,i)):x==='Closed'?all.filter(i=>i.status==='Handed over'||i.status==='Disposed'):all;
  const list=pick(f).slice().sort((a,b)=>a.foundAt<b.foundAt?1:-1);
  return <><div className="toolbar gsv-toolbar"><div className="filters">{(['Held','Past retention','Closed','All'] as const).map(x=><button key={x} className={x===f?'active':''} aria-pressed={x===f} onClick={()=>setF(x)}>{x} ({pick(x).length})</button>)}</div></div>
    {!list.length?<Empty title="No items here"/>:<div className="table-wrap"><table><thead><tr><th>Item</th><th>Found</th><th>Found by</th><th>Storage</th><th>Age</th><th>Status</th><th>Action</th></tr></thead>
    <tbody>{list.map(i=><tr key={i.id}>
      <td><strong>{i.description}</strong><br/><small>{i.id} · {i.category}{i.valuable?' · valuable':''}</small></td><td>{gsvWhen(i.foundAt)}<br/><small>{i.foundWhere}</small></td>
      <td>{gsvUserName(s,i.foundBy)}</td><td>{i.storage}</td><td>{gsvItemAgeDays(s,i)} d</td>
      <td><Pill tone={gsvStatusTone(i.status)}>{i.status}</Pill>{gsvDisposable(s,i)&&<> <Pill tone="orange">Retention over</Pill></>}</td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-item',id:i.id})}>Open</button></td></tr>)}</tbody></table></div>}</>;
}

/** Shared action buttons for an assigned request (desktop card + phone card). */
export function GsvWorkButtons({r,big}:{r:GSVRequest;big?:boolean}){
  const {s,uid,run,busy,open}=useHcc();
  if(!gsvIsOpen(r)||!gsvCanWork(s,uid,r))return null;
  const cls='btn'+(big?' gsv-big':'');
  return <div className="gsv-work">{(r.status==='Open'||r.status==='Assigned')&&<button className={cls} disabled={busy} onClick={()=>run({type:'GSV_REQUEST_START',id:r.id},'Request started')}><Play size={16}/> Start</button>}
    <button className={cls+' primary'} disabled={busy} onClick={()=>open({type:'gsv-request',id:r.id})}><CheckCircle2 size={16}/> Complete…</button></div>;
}
export function GsvMyRequestsPage(){
  const {s,uid,open,permission}=useHcc();
  const [f,setF]=useState<GsvReqFilter>('Open');
  const mine=gsvVisibleOps(s,uid),list=gsvFilterRequests(s,f,mine);
  return <>
    <PageTitle title="My guest requests" sub="Guest requests assigned to you or your team. Add a short note for the guest when done." action={<button className="btn" onClick={()=>open({type:'gsv-item-new'})}><PackageSearch size={16}/> Log found item</button>}/>
    <div className="info-note"><Info size={16}/>Guest messages are simulated — nothing is sent. No prices or folio amounts are shown here.</div>
    <div className="toolbar"><div className="filters">{(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(x=><button key={x} className={x===f?'active':''} aria-pressed={x===f} onClick={()=>setF(x)}>{x} ({gsvFilterRequests(s,x,mine).length})</button>)}</div></div>
    {!list.length?<Empty title="No guest requests" body={permission('reservations')?'Nothing in this view.':'Nothing is assigned to you right now.'}/>:
    <div className="gsv-cards">{list.map(r=>{const sla=gsvSlaLabel(s,r);return <article key={r.id} className="card gsv-card" aria-label={`Room ${r.room} · ${r.type} · ${r.status}`}>
      <div className="gsv-card-top"><strong>Room {r.room}</strong><Pill tone={sla.tone}>{sla.label}</Pill></div>
      <h3>{r.type}{r.priority==='Urgent'&&<> <Pill tone="gsv-red">Urgent</Pill></>}</h3>
      <p>{r.detail||'—'} · {r.guest}</p>
      <p className="gsv-meta">{r.dept} · {gsvUserName(s,r.assignee)} · <Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></p>
      {r.note&&<p className="gsv-meta">Guest note: “{r.note}”</p>}
      <GsvWorkButtons r={r}/></article>;})}</div>}
  </>;
}
