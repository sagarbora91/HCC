// M17 + M22 phone: approvals inbox cards, pending expenses, audit log with a filter sheet. Same selectors/commands as desktop SCR-17.
import React,{useState} from 'react';
import {Check,X,SlidersHorizontal,Lock} from 'lucide-react';
import {money,authorize,adminApprovalInbox,adminAuditFilter,adminAuditDetailVisible,ADMIN_INBOX_TABS} from '../../services/mock';
import type {AdminInboxRow} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter} from '../../app/mobile/components';
import {Empty,shortDate,clockTime} from '../../components/ui';
import {maApprovalActions,maExpenseBlock,maAuditFilterCount} from './logic';
import {useMaUi,maSetUi} from './store';
import {MaTag,maTone,MaIntro,MaH,MaGuard,MaRows} from './parts';

type Section='requests'|'expenses'|'audit';
const PAGE=30;
export function MaApprovals(){
  const {s,user}=useHcc();
  const rows=adminApprovalInbox(s);
  const pendingExp=s.expenses.filter(e=>e.status==='Pending').length;
  const [sec,setSec]=useState<Section>(()=>user.role==='Tech admin'?'audit':!rows.some(r=>r.status==='Pending')&&pendingExp?'expenses':'requests');
  return <div className="ma-page">
    <ChipFilter<Section> label="Approvals and audit" value={sec} onChange={setSec} options={[
      {id:'requests',label:'Requests',count:rows.filter(r=>r.status==='Pending').length},{id:'expenses',label:'Expenses',count:pendingExp},{id:'audit',label:'Audit log'}]}/>
    {sec==='requests'&&<Requests rows={rows}/>}{sec==='expenses'&&<ExpensesList/>}{sec==='audit'&&<AuditList/>}
  </div>;
}

function Requests({rows}:{rows:AdminInboxRow[]}){
  const {s}=useHcc();
  const [tab,setTab]=useState<string>('Pending');
  const list=rows.filter(r=>r.status===tab).reverse();
  return <>
    <MaIntro>Requests are bound to a target and version. A changed target invalidates the request; an unanswered one expires (simulated clock {clockTime(s.clock)} IST).</MaIntro>
    <ChipFilter label="Request status" value={tab} onChange={setTab} options={ADMIN_INBOX_TABS.map(t=>({id:t,label:t,count:rows.filter(r=>r.status===t).length}))}/>
    {list.length?list.map(r=><RequestCard key={r.a.id} r={r}/>):<Empty title={`No ${tab.toLowerCase()} requests`} body={tab==='Pending'?'Discounts above the threshold, refunds, credit and policy requests appear here.':'Nothing in this state.'}/>}
  </>;
}
function RequestCard({r}:{r:AdminInboxRow}){
  const {s,uid,run,open,busy}=useHcc();const a=r.a;
  const act=maApprovalActions(s,uid,r);
  const booking=s.bookings.find(b=>b.id===a.booking);
  const target=r.kind==='policy'?String(a.meta?.summary||r.target):booking?`${booking.guest} · ${booking.id}`:r.target||'—';
  return <article className="ma-card" aria-label={`Request ${a.id}`}>
    <div className="ma-card-top"><div><strong className="ma-cap">{r.kind}</strong><span>{target}</span></div>{a.amount?<strong className="ma-amt number">{money(a.amount)}</strong>:null}</div>
    <MaRows rows={[['Requested by',r.requester+(act.own?' (you)':'')],['Reason',<>{a.reason||'—'}{r.kind==='policy'&&<small className="ma-sub">{String(a.meta?.before||'')} → {String(a.meta?.after||'')}</small>}</>],
      ['Expires',a.expiresAt?`${shortDate(a.expiresAt.slice(0,10))} · ${clockTime(a.expiresAt)} IST`:'No expiry'],['Status',<><MaTag tone={maTone(r.status)}>{r.status}</MaTag>{r.derived&&<small className="ma-sub">{r.derived}</small>}</>],
      ...(a.decidedAt?[['Decided',`${shortDate(a.decidedAt.slice(0,10))} ${clockTime(a.decidedAt)}${a.approver?' · '+(s.users.find(u=>u.id===a.approver)?.name.split(' · ')[0]||a.approver):''}`] as [string,string]]:[])]}/>
    {act.live&&<div className="ma-actions">
      <MaGuard className="btn primary" block={act.approveBlock} busy={busy} onClick={()=>run({type:r.command,id:a.id,approve:true,key:'approval:'+a.id},'Request approved')}><Check size={18}/>Approve</MaGuard>
      {!act.rejectBlock&&<button type="button" className="btn" disabled={busy} onClick={()=>open({type:'ma-reject',id:a.id})}><X size={18}/>Reject…</button>}
    </div>}
  </article>;
}

function ExpensesList(){
  const {s,uid,run,open,busy}=useHcc();
  const list=s.expenses.filter(e=>e.status==='Pending');
  return <>
    <MaIntro>Submitters cannot approve their own expense. Rejecting asks for a reason.</MaIntro>
    {list.length?list.map(e=>{const block=maExpenseBlock(s,uid,e);return <article className="ma-card" key={e.id} aria-label={`Expense ${e.id}`}>
      <div className="ma-card-top"><div><strong>{e.vendor}</strong><span>{e.id} · {shortDate(e.date)}</span></div><strong className="ma-amt number">{money(e.amount)}</strong></div>
      <MaRows rows={[['Category',e.category],['Method',e.method],['Submitted by',s.users.find(x=>x.id===e.actor)?.name.split(' · ')[0]||e.actor]]}/>
      <div className="ma-actions"><MaGuard className="btn primary" block={block} busy={busy} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}><Check size={18}/>Approve</MaGuard>
        {!block&&<button type="button" className="btn" disabled={busy} onClick={()=>open({type:'expense-reason',id:e.id,room:'reject'})}><X size={18}/>Reject…</button>}</div>
    </article>;}):<Empty title="No pending expenses"/>}
  </>;
}

function AuditList(){
  const {s,uid,open}=useHcc();const {audit:f}=useMaUi();
  const [limit,setLimit]=useState(PAGE);
  const d=authorize(s,uid,'PERM-ADM-004');
  if(d.decision!=='allow')return <div className="ma-card ma-locked"><Lock size={18}/><p>{d.reason} Audit view is limited to Owner, Manager, Accountant and Tech admin (view).</p></div>;
  const detail=adminAuditDetailVisible(s,uid);
  const list=adminAuditFilter(s,f);const n=maAuditFilterCount(f);
  return <>
    <MaH aside={<button type="button" className="btn ma-filter-btn" onClick={()=>open({type:'ma-audit-filter'})}><SlidersHorizontal size={18}/>Filter{n?` (${n})`:''}</button>}>Audit log</MaH>
    <MaIntro>{list.length} of {s.audit.length} events · simulated clock{detail?'':' · details restricted for your role (action and time only)'}</MaIntro>
    {n>0&&<button type="button" className="btn ma-clear" onClick={()=>{maSetUi({audit:{}});setLimit(PAGE);}}>Clear {n} filter{n>1?'s':''}</button>}
    {list.length?<ol className="ma-audit">{list.slice(0,limit).map(a=><li key={a.id}><strong>{a.action}</strong><p>{detail?a.detail:'Details restricted'}</p><small>{a.actor} · {shortDate(a.at.slice(0,10))} {clockTime(a.at)} IST · {a.id}</small></li>)}</ol>
      :<Empty title="No matching events" body="Clear a filter to see more."/>}
    {list.length>limit&&<button type="button" className="btn ma-more" onClick={()=>setLimit(limit+PAGE)}>Load {Math.min(PAGE,list.length-limit)} more</button>}
  </>;
}
