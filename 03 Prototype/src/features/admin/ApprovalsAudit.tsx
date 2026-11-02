// SCR-17 Approvals & audit (BL-026): approvals inbox with derived expiry/invalidation, pending expenses, full audit log.
import React,{useState} from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {Lock} from 'lucide-react';
import {money,authorize,adminApprovalInbox,adminDecideBlock,adminAuditFilter,adminAuditDetailVisible,adminUserActive,adminExpenseBlock,ADMIN_INBOX_TABS} from '../../services/mock';
import type {AdminInboxRow} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,shortDate,clockTime} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,FitNote,SplitView} from '../../components/fit';
import {GuardedButton,StatusPill,Tabs,firstName} from './parts';

const PAGE=50;
const TABS=[{id:'approvals',label:'Approvals'},{id:'expenses',label:'Pending expenses'},{id:'audit',label:'Audit log'}] as const;
type Tab=typeof TABS[number]['id'];
const TAB_IDS=TABS.map(x=>x.id);
// FIT (DEC-194/196, contract §6 'approvals' = FitTabs): three sub-screens — the approvals inbox, pending expenses and the audit log —
// each one panel that fits (a fourth/second stacked panel would be cramped at 640 px, so the expenses table gets its own tab).
// Inbox status tabs and the audit filters sit in a side panel; "Load more" is in the audit panel footer. Every action stays.
export function ApprovalsAudit(){
  const {s,uid}=useHcc();
  const [tab,setTab]=useFitTab<Tab>('approvals',TAB_IDS,'approvals');
  const rows=adminApprovalInbox(s);
  const [status,setStatus]=useState<string>('Pending');
  const counts=Object.fromEntries(ADMIN_INBOX_TABS.map(t=>[t,rows.filter(r=>r.status===t).length]));
  const list=rows.filter(r=>r.status===status).reverse();
  const pendingExp=s.expenses.filter(e=>e.status==='Pending').length;
  const [sel,setSel]=useState<string|null>(null);
  const chosen=list.find(r=>r.a.id===sel)??null;
  const audit=useAuditFilters();
  const auditOk=authorize(s,uid,'PERM-ADM-004').decision==='allow';
  const toolbar=tab==='approvals'?<Tabs tabs={ADMIN_INBOX_TABS} value={status} onChange={setStatus} counts={counts}/>:undefined;
  return <FitPage title="Approvals & audit" sub="Every request with its binding, and every change with who, when and what." actions={<Pill tone="blue">Simulated clock {clockTime(s.clock)} IST</Pill>}
      toolbar={toolbar}
      tabs={<FitTabs page="approvals" value={tab} onChange={setTab}
        tabs={TABS.map(x=>({...x,count:x.id==='approvals'?counts.Pending:x.id==='expenses'?pendingExp:undefined,badge:(x.id==='approvals'&&counts.Pending)||(x.id==='expenses'&&pendingExp)?'warn' as const:undefined}))}/>}>
    {tab==='approvals'&&<SplitView label="Approvals" ratio="3fr 2fr" onCloseDetail={()=>setSel(null)}
      list={<ScrollPanel title="Approvals inbox" label="Approvals inbox" actions={<span className="muted">{list.length} {status.toLowerCase()}</span>} footer={<FitNote text="Requests are bound to a target and version. A changed target invalidates the request; an unanswered request expires."/>}>
        {list.length?<div className="table-wrap"><table className="adm-table apr-list"><thead><tr><th>What</th><th>Kind</th><th>Requested by</th><th>Amount</th><th>Age / expiry</th><th>Status</th></tr></thead><tbody>
          {list.map(r=><InboxListRow key={r.a.id} r={r} on={r.a.id===chosen?.a.id} pick={()=>setSel(r.a.id)}/>)}</tbody></table></div>:<Empty title={`No ${status.toLowerCase()} requests`} body={status==='Pending'?'Discounts above the threshold and policy change requests appear here.':'Nothing in this state.'}/>}
      </ScrollPanel>}
      detail={chosen?<InboxDetail key={chosen.a.id} r={chosen}/>:null}
      empty={<Empty title="Pick a request" body="Its context and the Approve / Reject buttons open here."/>}/>}
    {tab==='expenses'&&<Expenses/>}
    {tab==='audit'&&(auditOk?<div className="fit-cols"><div><AuditLog f={audit}/></div><div><ScrollPanel title="Filters" label="Audit filters"><div className="padded"><AuditFilters f={audit}/></div></ScrollPanel></div></div>:<AuditLog f={audit}/>)}
  </FitPage>;
}

const targetText=(s:ReturnType<typeof useHcc>['s'],r:AdminInboxRow)=>{const a=r.a;const booking=s.bookings.find(b=>b.id===a.booking);
  return r.kind==='policy'?String(a.meta?.summary||r.target):booking?`${booking.guest} · ${bookingRef(booking)}`:r.target||'—';};
/** Time left to expiry on the simulated clock (the request has no created-at, so this is its age-like figure). */
const expiryText=(clock:string,exp?:string)=>{if(!exp)return 'No expiry';const m=Math.round((Date.parse(exp)-Date.parse(clock))/60000);
  if(Number.isNaN(m))return shortDate(exp.slice(0,10))+' '+clockTime(exp);if(m<=0)return 'Expired';return m<60?`in ${m} min`:m<2880?`in ${Math.floor(m/60)} h`:`in ${Math.floor(m/1440)} d`;};
function InboxListRow({r,on,pick}:{r:AdminInboxRow;on:boolean;pick:()=>void}){
  const {s}=useHcc();const a=r.a;
  return <tr className={on?'fol-row-on':undefined}><td className="adm-wrap"><button className="guest-link" aria-current={on?'true':undefined} onClick={pick}><span><strong>{targetText(s,r)}</strong><small>{a.id} · {a.reason}</small></span></button></td>
    <td>{r.kind}</td><td>{r.requester}</td><td className="number">{a.amount?money(a.amount):'—'}</td><td>{expiryText(s.clock,a.expiresAt)}</td><td><StatusPill status={r.status}/></td></tr>;
}
function InboxDetail({r}:{r:AdminInboxRow}){
  const {s,uid,run,busy}=useHcc();const a=r.a;
  const block=adminDecideBlock(s,uid,r);
  const decide=(approve:boolean)=>run({type:r.command,id:a.id,approve,key:'approval:'+a.id},approve?'Request approved':'Request rejected');
  const live=a.status==='Pending';
  const approveBlock=block||(r.status==='Expired'?'Expired — reject it and ask for a new request.':r.status==='Invalidated'?(r.derived||'Target changed')+' — reject it and request again.':undefined);
  return <div className="apr-detail"><div className="apr-detail-body">
    <h2>{targetText(s,r)}</h2>
    <dl className="apr-dl"><dt>Request</dt><dd>{a.id} · v{a.version}</dd><dt>Kind</dt><dd>{r.kind}</dd><dt>Amount</dt><dd>{a.amount?money(a.amount):'—'}</dd><dt>Requested by</dt><dd>{r.requester}</dd>
      <dt>Reason</dt><dd className="adm-wrap">{a.reason}{r.kind==='policy'&&<small>{String(a.meta?.before||'')} → {String(a.meta?.after||'')}</small>}</dd>
      <dt>Expires</dt><dd>{a.expiresAt?<>{shortDate(a.expiresAt.slice(0,10))} {clockTime(a.expiresAt)} IST</>:'No expiry'}</dd>
      <dt>Status</dt><dd><StatusPill status={r.status}/>{r.derived&&<small className="adm-wrap">{r.derived}</small>}</dd>
      {a.decidedAt&&<><dt>Decided</dt><dd>{shortDate(a.decidedAt.slice(0,10))} {clockTime(a.decidedAt)}{a.approver?' by '+firstName(s.users.find(u=>u.id===a.approver)?.name||a.approver):''}</dd></>}</dl></div>
    <div className="apr-decide">{live?<div className="adm-decide"><GuardedButton block={approveBlock} busy={busy} onClick={()=>decide(true)}>Approve</GuardedButton>{!block?<button className="text-btn" disabled={busy} onClick={()=>decide(false)}>Reject</button>:null}</div>:<span className="muted">Closed</span>}</div></div>;
}

function Expenses(){
  const {s,uid,run,busy,permission}=useHcc();
  const list=s.expenses.filter(e=>e.status==='Pending');
  const u=s.users.find(x=>x.id===uid);
  return <ScrollPanel title="Pending expenses" label="Pending expenses" actions={<Pill>{list.length} pending</Pill>} footer={<FitNote text="Submitters cannot approve their own expense."/>}>
    {list.length?<div className="table-wrap"><table className="adm-table"><thead><tr><th>Expense</th><th>Category · method</th><th>Amount</th><th>Submitted by</th><th>Decision</th></tr></thead><tbody>
      {list.map(e=>{const block=adminExpenseBlock(s,uid,e.actor);
        return <tr key={e.id}><td><strong>{e.description??e.vendor}</strong><small>{e.description&&e.vendor?e.vendor+' · ':''}{e.id} · {shortDate(e.date)}</small></td><td>{e.category}<small>{e.method}</small></td><td className="number">{money(e.amount)}</td><td>{firstName(s.users.find(x=>x.id===e.actor)?.name||e.actor)}</td>
          <td className="adm-wrap"><div className="adm-decide"><GuardedButton block={block} busy={busy} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve</GuardedButton>{!block&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:false,key:'expense:'+e.id},'Expense rejected')}>Reject</button>}</div></td></tr>;})}
    </tbody></table></div>:<Empty title="No pending expenses"/>}</ScrollPanel>;
}

type AuditF=ReturnType<typeof useAuditFilters>;
function useAuditFilters(){
  const [actor,setActor]=useState(''),[action,setAction]=useState(''),[text,setText]=useState(''),[from,setFrom]=useState(''),[to,setTo]=useState('');
  const [limit,setLimit]=useState(PAGE);
  const reset=(f:(v:string)=>void)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>{f(e.target.value);setLimit(PAGE);};
  return {actor,action,text,from,to,limit,setLimit,reset,setActor,setAction,setText,setFrom,setTo};
}
function AuditFilters({f}:{f:AuditF}){
  const {s}=useHcc();
  const actors=[...new Set(s.audit.map(a=>a.actor))].sort(),actions=[...new Set(s.audit.map(a=>a.action))].sort();
  return <div className="adm-filters form-row">
      <label>Actor<select value={f.actor} onChange={f.reset(f.setActor)}><option value="">All actors</option>{actors.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Action<select value={f.action} onChange={f.reset(f.setAction)}><option value="">All actions</option>{actions.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Contains text<input value={f.text} onChange={f.reset(f.setText)} placeholder="booking, room, setting…"/></label>
      <label>From<input type="date" value={f.from} onChange={f.reset(f.setFrom)}/></label><label>To<input type="date" value={f.to} onChange={f.reset(f.setTo)}/></label>
    </div>;
}
function AuditLog({f}:{f:AuditF}){
  const {s,uid}=useHcc();
  const d=authorize(s,uid,'PERM-ADM-004');
  const {actor,action,text,from,to,limit,setLimit}=f;
  if(d.decision!=='allow')return <ScrollPanel title="Audit log" label="Audit log" actions={<Lock size={18}/>}><p className="adm-lock padded">{d.reason} Audit view is limited to Owner, Manager, Accountant and Tech admin (view).</p></ScrollPanel>;
  const detail=adminAuditDetailVisible(s,uid);
  const list=adminAuditFilter(s,{actor,action,text,from,to});
  return <ScrollPanel title="Audit log" label="Audit log"
      actions={<span className="muted">{s.audit.length} events</span>}
      footer={<><span className="muted">Showing {Math.min(limit,list.length)} of {list.length}</span>{list.length>limit&&<button className="btn small-btn" onClick={()=>setLimit(limit+PAGE)}>Load {Math.min(PAGE,list.length-limit)} more</button>}
        <FitNote text={`Simulated clock · browser-local history${detail?'':' · before/after details restricted for your role (action and time only)'}`}/></>}>
    {list.length?<div className="audit-list">{list.slice(0,limit).map(a=><div key={a.id}><span className="audit-dot"/><div><strong>{a.action}</strong><p>{detail?a.detail:'Details restricted'}</p><small>{a.actor} · {shortDate(a.at.slice(0,10))} {clockTime(a.at)} IST · {a.id}</small></div></div>)}</div>:<Empty title="No matching events" body="Clear a filter to see more."/>}
  </ScrollPanel>;
}
