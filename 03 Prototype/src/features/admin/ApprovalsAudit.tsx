// SCR-17 Approvals & audit (BL-026): approvals inbox with derived expiry/invalidation, pending expenses, full audit log.
import React,{useState} from 'react';
import {Lock} from 'lucide-react';
import {money,authorize,adminApprovalInbox,adminDecideBlock,adminAuditFilter,adminAuditDetailVisible,adminUserActive,adminExpenseBlock,ADMIN_INBOX_TABS} from '../../services/mock';
import type {AdminInboxRow} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate,clockTime} from '../../components/ui';
import {GuardedButton,StatusPill,Tabs,firstName} from './parts';

const PAGE=50;
export function ApprovalsAudit(){
  const {s}=useHcc();
  const rows=adminApprovalInbox(s);
  const [tab,setTab]=useState<string>('Pending');
  const counts=Object.fromEntries(ADMIN_INBOX_TABS.map(t=>[t,rows.filter(r=>r.status===t).length]));
  const list=rows.filter(r=>r.status===tab).reverse();
  return <><PageTitle title="Approvals & audit" sub="Every request with its binding, and every change with who, when and what." action={<Pill tone="blue">Simulated clock {clockTime(s.clock)} IST</Pill>}/>
    <section className="card"><div className="section-title"><div><h2>Approvals inbox</h2><p>Requests are bound to a target and version. A changed target invalidates the request; an unanswered request expires.</p></div></div>
      <Tabs tabs={ADMIN_INBOX_TABS} value={tab} onChange={setTab} counts={counts}/>
      {list.length?<div className="table-wrap"><table className="adm-table"><thead><tr><th>Request</th><th>Kind · target</th><th>Amount</th><th>Requested by</th><th>Reason</th><th>Version</th><th>Expires</th><th>Status</th><th>Decision</th></tr></thead><tbody>
        {list.map(r=><InboxRow key={r.a.id} r={r}/>)}</tbody></table></div>:<Empty title={`No ${tab.toLowerCase()} requests`} body={tab==='Pending'?'Discounts above the threshold and policy change requests appear here.':'Nothing in this state.'}/>}
    </section>
    <Expenses/>
    <AuditLog/></>;
}

function InboxRow({r}:{r:AdminInboxRow}){
  const {s,uid,run,busy}=useHcc();const a=r.a;
  const block=adminDecideBlock(s,uid,r);
  const booking=s.bookings.find(b=>b.id===a.booking);
  const decide=(approve:boolean)=>run({type:r.command,id:a.id,approve,key:'approval:'+a.id},approve?'Request approved':'Request rejected');
  const live=a.status==='Pending';
  const approveBlock=block||(r.status==='Expired'?'Expired — reject it and ask for a new request.':r.status==='Invalidated'?(r.derived||'Target changed')+' — reject it and request again.':undefined);
  return <tr><td><strong>{a.id}</strong>{a.decidedAt&&<small>decided {shortDate(a.decidedAt.slice(0,10))} {clockTime(a.decidedAt)}{a.approver?' by '+firstName(s.users.find(u=>u.id===a.approver)?.name||a.approver):''}</small>}</td>
    <td className="adm-wrap">{r.kind}<small>{r.kind==='policy'?String(a.meta?.summary||r.target):booking?`${booking.guest} · ${booking.id}`:r.target||'—'}</small></td>
    <td className="number">{a.amount?money(a.amount):'—'}</td><td>{r.requester}</td><td className="adm-wrap">{a.reason}{r.kind==='policy'&&<small>{String(a.meta?.before||'')} → {String(a.meta?.after||'')}</small>}</td>
    <td>v{a.version}</td><td>{a.expiresAt?<>{shortDate(a.expiresAt.slice(0,10))}<small>{clockTime(a.expiresAt)} IST</small></>:<span className="muted">No expiry</span>}</td>
    <td><StatusPill status={r.status}/>{r.derived&&<small className="adm-wrap">{r.derived}</small>}</td>
    <td className="adm-wrap">{live?<div className="adm-decide"><GuardedButton block={approveBlock} busy={busy} onClick={()=>decide(true)}>Approve</GuardedButton>{!block?<button className="text-btn" disabled={busy} onClick={()=>decide(false)}>Reject</button>:null}</div>:<span className="muted">Closed</span>}</td></tr>;
}

function Expenses(){
  const {s,uid,run,busy,permission}=useHcc();
  const list=s.expenses.filter(e=>e.status==='Pending');
  const u=s.users.find(x=>x.id===uid);
  return <section className="card"><div className="section-title"><div><h2>Pending expenses</h2><p>Submitters cannot approve their own expense.</p></div><Pill>{list.length} pending</Pill></div>
    {list.length?<div className="table-wrap"><table className="adm-table"><thead><tr><th>Expense</th><th>Category · method</th><th>Amount</th><th>Submitted by</th><th>Decision</th></tr></thead><tbody>
      {list.map(e=>{const block=adminExpenseBlock(s,uid,e.actor);
        return <tr key={e.id}><td><strong>{e.vendor}</strong><small>{e.id} · {shortDate(e.date)}</small></td><td>{e.category}<small>{e.method}</small></td><td className="number">{money(e.amount)}</td><td>{firstName(s.users.find(x=>x.id===e.actor)?.name||e.actor)}</td>
          <td className="adm-wrap"><div className="adm-decide"><GuardedButton block={block} busy={busy} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve</GuardedButton>{!block&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:false,key:'expense:'+e.id},'Expense rejected')}>Reject</button>}</div></td></tr>;})}
    </tbody></table></div>:<Empty title="No pending expenses"/>}</section>;
}

function AuditLog(){
  const {s,uid}=useHcc();
  const d=authorize(s,uid,'PERM-ADM-004');
  const [actor,setActor]=useState(''),[action,setAction]=useState(''),[text,setText]=useState(''),[from,setFrom]=useState(''),[to,setTo]=useState('');
  const [limit,setLimit]=useState(PAGE);
  if(d.decision!=='allow')return <section className="card"><div className="section-title"><h2>Audit log</h2><Lock size={18}/></div><p className="adm-lock padded">{d.reason} Audit view is limited to Owner, Manager, Accountant and Tech admin (view).</p></section>;
  const detail=adminAuditDetailVisible(s,uid);
  const list=adminAuditFilter(s,{actor,action,text,from,to});
  const actors=[...new Set(s.audit.map(a=>a.actor))].sort(),actions=[...new Set(s.audit.map(a=>a.action))].sort();
  const reset=(f:(v:string)=>void)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>{f(e.target.value);setLimit(PAGE);};
  return <section className="card"><div className="section-title"><div><h2>Audit log</h2><p>{s.audit.length} events · simulated clock · browser-local history{detail?'':' · before/after details restricted for your role (action and time only)'}</p></div></div>
    <div className="padded adm-filters form-row">
      <label>Actor<select value={actor} onChange={reset(setActor)}><option value="">All actors</option>{actors.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Action<select value={action} onChange={reset(setAction)}><option value="">All actions</option>{actions.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Contains text<input value={text} onChange={reset(setText)} placeholder="booking, room, setting…"/></label>
      <label>From<input type="date" value={from} onChange={reset(setFrom)}/></label><label>To<input type="date" value={to} onChange={reset(setTo)}/></label>
    </div>
    {list.length?<div className="audit-list">{list.slice(0,limit).map(a=><div key={a.id}><span className="audit-dot"/><div><strong>{a.action}</strong><p>{detail?a.detail:'Details restricted'}</p><small>{a.actor} · {shortDate(a.at.slice(0,10))} {clockTime(a.at)} IST · {a.id}</small></div></div>)}</div>:<Empty title="No matching events" body="Clear a filter to see more."/>}
    <div className="padded adm-row-head"><span className="muted">Showing {Math.min(limit,list.length)} of {list.length}</span>{list.length>limit&&<button className="btn small-btn" onClick={()=>setLimit(limit+PAGE)}>Load {Math.min(PAGE,list.length-limit)} more</button>}</div>
  </section>;
}
