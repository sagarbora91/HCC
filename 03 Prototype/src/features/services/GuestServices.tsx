// Module 18 · Guest services — desktop page (requests, complaints, lost & found, reports) and "My guest requests" (assigned staff).
import React,{useState} from 'react';
import {Plus,MessageSquareWarning,PackageSearch,Printer,Play,CheckCircle2,Info} from 'lucide-react';
import {gcatLabel,lfcLabel,gcatSlaLabel,gsvSlice,gsvSlaLabel,gsvIsOpen,gsvOverdue,gsvRecoveryStatus,gsvFollowUpDue,gsvDisposable,gsvItemAgeDays,gsvResponseTotals,gsvComplaintOpen,gsvVisibleOps,gsvCanWork,
  type GSVRequest} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip,FitNote} from '../../components/fit';
import {gsvWhen,gsvUserName,gsvStatusTone,gsvSevTone,GsvReportBody} from './modals';

export type GsvTab='Requests'|'Complaints'|'Lost & found'|'Reports';
export const gsvTabs:GsvTab[]=['Requests','Complaints','Lost & found','Reports'];
export type GsvReqFilter='Open'|'Overdue'|'Done'|'All';
export function gsvFilterRequests(s:ReturnType<typeof useHcc>['s'],f:GsvReqFilter,list=gsvSlice(s).requests){
  const xs=f==='Open'?list.filter(gsvIsOpen):f==='Overdue'?list.filter(r=>gsvOverdue(s,r)):f==='Done'?list.filter(r=>!gsvIsOpen(r)):list;
  return xs.slice().sort((a,b)=>gsvIsOpen(a)!==gsvIsOpen(b)?(gsvIsOpen(a)?-1:1):gsvIsOpen(a)?(a.dueAt<b.dueAt?-1:1):(a.createdAt<b.createdAt?1:-1));
}

// FIT (DEC-194…196, contract §6 `guest-services` = tabs): FitPage header (primary action of the open tab always visible), the four stat tiles as a
// StatStrip, the existing four sections as sub-screens (FitTabs, ids = kebab-case of the old labels), each table in a ScrollPanel with its
// filter chips in the panel head. Rows keep opening the existing dialogs. Default tab unchanged (Requests).
const GSV_TAB_IDS=['requests','complaints','lost-found','reports'] as const;
type GsvFitTab=typeof GSV_TAB_IDS[number];
const GSV_TAB_LABEL:Record<GsvFitTab,GsvTab>={requests:'Requests',complaints:'Complaints','lost-found':'Lost & found',reports:'Reports'};
export function GuestServicesPage(){
  const {s,open,permission}=useHcc();
  const [tab,setTab]=useFitTab<GsvFitTab>('guest-services',GSV_TAB_IDS,'requests');
  const x=gsvSlice(s),t=gsvResponseTotals(s);
  const counts:Record<GsvTab,number>={Requests:x.requests.filter(gsvIsOpen).length,Complaints:x.complaints.filter(gsvComplaintOpen).length,'Lost & found':x.items.filter(i=>i.status==='Stored'||i.status==='Claimed').length,Reports:0};
  const action=tab==='complaints'?<button className="btn primary" onClick={()=>open({type:'gsv-complaint-new'})}><MessageSquareWarning size={16}/> Log complaint</button>
    :tab==='lost-found'?<button className="btn primary" onClick={()=>open({type:'gsv-item-new'})}><PackageSearch size={16}/> Log found item</button>
    :tab==='reports'?<button className="btn" onClick={()=>open({type:'gsv-print'})}><Printer size={16}/> Print report</button>
    :<button className="btn primary" onClick={()=>open({type:'gsv-request-new'})}><Plus size={16}/> New request</button>;
  return <FitPage title="Guest services" sub="Requests with SLA timers, complaints with recovery, and lost & found — on the simulated hotel clock." actions={action}
      toolbar={<StatStrip money items={[
        {title:'Open requests',value:String(counts.Requests),sub:`${t.overdue} overdue`},
        {title:'Within SLA',value:t.done?t.pct+'%':'—',sub:`avg ${t.avg} min · ${t.done} done`},
        {title:'Open complaints',value:String(counts.Complaints),sub:`${x.complaints.filter(c=>gsvFollowUpDue(s,c)).length} follow-up due`},
        {title:'Lost & found held',value:String(counts['Lost & found']),sub:`${x.items.filter(i=>gsvDisposable(s,i)).length} past retention`}]}/>}
      tabs={<FitTabs<GsvFitTab> page="guest-services" value={tab} onChange={setTab}
        tabs={GSV_TAB_IDS.map(id=>({id,label:GSV_TAB_LABEL[id],count:id==='reports'?undefined:counts[GSV_TAB_LABEL[id]]}))}/>}>
    {tab==='requests'&&<RequestsTable/>}
    {tab==='complaints'&&<ComplaintsTable note={!permission('billing')}/>}
    {tab==='lost-found'&&<ItemsTable/>}
    {tab==='reports'&&<ScrollPanel label="Guest services report"><div className="padded"><GsvReportBody/></div></ScrollPanel>}
  </FitPage>;
}

function FilterChips<T extends string>({label,value,onChange,options}:{label:string;value:T;onChange:(v:T)=>void;options:{id:T;text:string}[]}){
  return <div className="filters" role="group" aria-label={label}>{options.map(o=><button key={o.id} className={o.id===value?'active':''} aria-pressed={o.id===value} onClick={()=>onChange(o.id)}>{o.text}</button>)}</div>;
}

function RequestsTable(){
  const {s,open}=useHcc();
  const [f,setF]=useState<GsvReqFilter>('Open');
  const list=gsvFilterRequests(s,f);
  return <ScrollPanel label="Guest requests" actions={<FilterChips label="Request filter" value={f} onChange={setF} options={(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(x=>({id:x,text:`${x} (${gsvFilterRequests(s,x).length})`}))}/>}>
    {!list.length?<Empty title="No requests here" body="Log a guest request with “New request”."/>:<div className="table-wrap"><table><thead><tr><th>Room · guest</th><th>Request</th><th>Department · assignee</th><th>Logged</th><th>Status</th><th>SLA</th><th>Action</th></tr></thead>
    <tbody>{list.map(r=>{const sla=gsvSlaLabel(s,r);return <tr key={r.id}>
      <td><strong>Room {r.room}</strong><br/>{r.guest}</td><td>{r.type}{r.priority==='Urgent'&&<> <Pill tone="gsv-red">Urgent</Pill></>}<br/><small>{r.detail||r.id}</small></td>
      <td>{r.dept}<br/><small>{gsvUserName(s,r.assignee)}</small></td><td>{gsvWhen(r.createdAt)}</td>
      <td><Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></td><td><Pill tone={sla.tone}>{sla.label}</Pill></td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-request',id:r.id})}>{gsvIsOpen(r)?'Manage':'View'}</button></td></tr>;})}</tbody></table></div>}</ScrollPanel>;
}
function ComplaintsTable({note}:{note:boolean}){
  const {s,open}=useHcc();
  const [f,setF]=useState<'Open'|'Follow-up due'|'Closed'|'All'>('Open');
  const all=gsvSlice(s).complaints;
  const pick=(x:typeof f)=>x==='Open'?all.filter(gsvComplaintOpen):x==='Follow-up due'?all.filter(c=>gsvFollowUpDue(s,c)):x==='Closed'?all.filter(c=>c.status==='Closed'):all;
  const list=pick(f).slice().sort((a,b)=>a.createdAt<b.createdAt?1:-1);
  return <ScrollPanel label="Complaints" actions={<FilterChips label="Complaint filter" value={f} onChange={setF} options={(['Open','Follow-up due','Closed','All'] as const).map(x=>({id:x,text:`${x} (${pick(x).length})`}))}/>}
      footer={note?<FitNote text="Recovery discounts are applied by billing staff through the normal folio discount and approval."/>:undefined}>
    {!list.length?<Empty title="No complaints here"/>:<div className="table-wrap"><table><thead><tr><th>Guest</th><th>Category</th><th>Severity</th><th>Owner</th><th>Status</th><th>Recovery</th><th>Follow-up</th><th>Action</th></tr></thead>
    <tbody>{list.map(c=>{const rec=gsvRecoveryStatus(s,c);return <tr key={c.id}>
      <td><strong>{c.guest}</strong><br/><small>{c.id}{c.room?' · Room '+c.room:''}</small></td><td>{gcatLabel(s,c)}{(()=>{const l=gcatSlaLabel(s,c);return l&&(c.status==='Open'||c.status==='Investigating')?<><br/><Pill tone={l.tone}>{l.label}</Pill></>:null;})()}</td><td><Pill tone={gsvSevTone(c.severity)}>{c.severity}</Pill></td>
      <td>{gsvUserName(s,c.owner)}</td><td><Pill tone={gsvStatusTone(c.status)}>{c.status}</Pill></td>
      <td>{rec==='None'?(c.recoveryAction?'Action recorded':'—'):'Discount · '+rec}</td>
      <td>{c.followUp?(c.followUp.satisfied?'Satisfied':'Not satisfied'):c.followUpOn?(gsvFollowUpDue(s,c)?<Pill tone="orange">Due</Pill>:c.followUpOn):'—'}</td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-complaint',id:c.id})}>Open</button></td></tr>;})}</tbody></table></div>}</ScrollPanel>;
}
function ItemsTable(){
  const {s,open}=useHcc();
  const [f,setF]=useState<'Held'|'Past retention'|'Closed'|'All'>('Held');
  const all=gsvSlice(s).items;
  const pick=(x:typeof f)=>x==='Held'?all.filter(i=>i.status==='Stored'||i.status==='Claimed'):x==='Past retention'?all.filter(i=>gsvDisposable(s,i)):x==='Closed'?all.filter(i=>i.status==='Handed over'||i.status==='Disposed'):all;
  const list=pick(f).slice().sort((a,b)=>a.foundAt<b.foundAt?1:-1);
  return <ScrollPanel label="Lost and found items" actions={<FilterChips label="Item filter" value={f} onChange={setF} options={(['Held','Past retention','Closed','All'] as const).map(x=>({id:x,text:`${x} (${pick(x).length})`}))}/>}>
    {!list.length?<Empty title="No items here"/>:<div className="table-wrap"><table><thead><tr><th>Item</th><th>Found</th><th>Found by</th><th>Storage</th><th>Age</th><th>Status</th><th>Action</th></tr></thead>
    <tbody>{list.map(i=><tr key={i.id}>
      <td><strong>{i.description}</strong><br/><small>{i.id} · {lfcLabel(s,i)}{i.valuable?' · valuable':''}</small></td><td>{gsvWhen(i.foundAt)}<br/><small>{i.foundWhere}</small></td>
      <td>{gsvUserName(s,i.foundBy)}</td><td>{i.storage}</td><td>{gsvItemAgeDays(s,i)} d</td>
      <td><Pill tone={gsvStatusTone(i.status)}>{i.status}</Pill>{gsvDisposable(s,i)&&<> <Pill tone="orange">Retention over</Pill></>}</td>
      <td><button className="text-btn" onClick={()=>open({type:'gsv-item',id:i.id})}>Open</button></td></tr>)}</tbody></table></div>}</ScrollPanel>;
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
  // FIT (contract §6 `gsv-my-requests` = fill): header with the action, one filter row, the request cards in one ScrollPanel; the info note is a FitNote.
  return <FitPage title="My guest requests" sub="Guest requests assigned to you or your team. Add a short note for the guest when done." actions={<button className="btn" onClick={()=>open({type:'gsv-item-new'})}><PackageSearch size={16}/> Log found item</button>}
      toolbar={<FilterChips label="Request filter" value={f} onChange={setF} options={(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(x=>({id:x,text:`${x} (${gsvFilterRequests(s,x,mine).length})`}))}/>}>
    <ScrollPanel label="My guest requests" footer={<FitNote text={<><Info size={16}/> Guest messages are simulated — nothing is sent. No prices or folio amounts are shown here.</>}/>}>
    {!list.length?<Empty title="No guest requests" body={permission('reservations')?'Nothing in this view.':'Nothing is assigned to you right now.'}/>:
    <div className="gsv-cards">{list.map(r=>{const sla=gsvSlaLabel(s,r);return <article key={r.id} className="card gsv-card" aria-label={`Room ${r.room} · ${r.type} · ${r.status}`}>
      <div className="gsv-card-top"><strong>Room {r.room}</strong><Pill tone={sla.tone}>{sla.label}</Pill></div>
      <h3>{r.type}{r.priority==='Urgent'&&<> <Pill tone="gsv-red">Urgent</Pill></>}</h3>
      <p>{r.detail||'—'} · {r.guest}</p>
      <p className="gsv-meta">{r.dept} · {gsvUserName(s,r.assignee)} · <Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></p>
      {r.note&&<p className="gsv-meta">Guest note: “{r.note}”</p>}
      <GsvWorkButtons r={r}/></article>;})}</div>}
    </ScrollPanel>
  </FitPage>;
}
