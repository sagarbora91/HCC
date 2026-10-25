// SCR-13 Expenses: status tabs, draft/submit/approve/reject/reverse, duplicate warning; plus the approval requests list.
import React,{useState} from 'react';
import {Paperclip} from 'lucide-react';
import {money,expenseDuplicates,expenseCounts,approvalKindLabel,EXPENSE_MANAGER_LIMIT,closingUnlimitedRoles,Expense,rbacCanSubmitExpense,rbacCanDecideExpense,rbacCanDecideCash,cbIsLocked,cbLockedMessage} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
const tabs=['All','Draft','Pending','Posted','Rejected','Reversed'] as const;
const tone=(st:string)=>st==='Posted'?'green':st==='Pending'?'orange':st==='Draft'?'blue':'neutral';
export function Expenses(){
  const {s,uid,user,open,run,permission,busy,go,booking}=useHcc();
  // DEC-063 G4/G5: the Accountant submits own expenses and decides/reverses them; the Cashier never reverses.
  const canSubmit=rbacCanSubmitExpense(s,uid),canDecide=rbacCanDecideExpense(s,uid);
  const [tab,setTab]=useState<typeof tabs[number]>('All');
  const list=s.expenses.filter(e=>tab==='All'||e.status===tab).slice().reverse();
  const pg=usePaged(list,DESKTOP_PAGE);
  const today=s.expenses.filter(e=>expenseCounts(e)&&e.date===s.date);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
  function actions(e:Expense){
    if(e.status==='Draft')return e.actor===uid&&canSubmit?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'EXPENSE_SUBMIT',id:e.id,key:'expsubmit:'+e.id},'Draft submitted for approval')}>Submit</button>:<span className="muted">Draft · {name(e.actor)}</span>;
    if(e.status==='Pending'){
      if(!canDecide)return <span className="muted">Awaiting approval</span>;
      if(e.actor===uid)return <span className="f-why">Your request — another approver needed</span>;
      const over=!closingUnlimitedRoles.includes(user.role)&&e.amount>EXPENSE_MANAGER_LIMIT,noShift=e.method==='Cash'&&!s.shiftOpen;
      return <div className="inline"><button className="btn small-btn" disabled={busy||over||noShift} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve & post</button>
        <button className="text-btn" disabled={busy} onClick={()=>open({type:'expense-reason',id:e.id,room:'reject'})}>Reject…</button>
        {(over||noShift)&&<small className="f-why">{over?`Above ${money(EXPENSE_MANAGER_LIMIT)} — Owner approves`:'Cash shift not open'}</small>}</div>;
    }
    if(e.status==='Posted'&&!e.reversalOf&&canDecide){const noShift=e.method==='Cash'&&!s.shiftOpen,locked=cbIsLocked(s,e.date); // DEC-065
      return <div className="inline"><button className="text-btn" disabled={busy||noShift||locked} title={locked?cbLockedMessage(e.date,'reversing this expense'):undefined} onClick={()=>open({type:'expense-reason',id:e.id,room:'reverse'})}>Reverse…</button>{locked?<small className="f-why">{cbLockedMessage(e.date,'reversing this expense')}</small>:noShift&&<small className="f-why">Cash shift not open</small>}</div>;}
    return <span className="muted">—</span>;
  }
  const approvals=s.approvals.slice().reverse();
  // The Accountant decides cash differences here (no cashier page); the counter never decides their own (SOD-004).
  const cashChecker=!permission('approve')&&rbacCanDecideCash(s,uid);
  return <><PageTitle title="Expenses & approvals" sub="Every expense accounted for. Every adjustment traceable." action={canSubmit?<button className="btn primary" disabled={busy} onClick={()=>open({type:'expense'})}>+ Add expense</button>:null}/>
    <div className="small-stats"><div><span>Posted today (net)</span><strong>{money(today.reduce((a,e)=>a+e.amount,0))}</strong></div><div><span>Drawer cash today</span><strong>{money(today.filter(e=>e.method==='Cash').reduce((a,e)=>a+e.amount,0))}</strong></div><div><span>Pending approval</span><strong>{s.expenses.filter(e=>e.status==='Pending').length}</strong></div><div><span>Drafts</span><strong>{s.expenses.filter(e=>e.status==='Draft').length}</strong></div></div>
    <section className="card"><div className="section-title"><h2>Expense register</h2><Pill>Receipt placeholders · no uploads</Pill></div>
      <div className="tabs" role="tablist" aria-label="Expense status">{tabs.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}<span>{t==='All'?s.expenses.length:s.expenses.filter(e=>e.status===t).length}</span></button>)}</div>
      <div className="table-wrap"><table><thead><tr><th>Vendor / reference</th><th>Category</th><th>Funding</th><th className="number">Amount</th><th>Status</th><th>Action</th></tr></thead><tbody>
        {pg.shown.map(e=>{const dup=!e.reversalOf&&e.status!=='Rejected'&&expenseDuplicates(s,e.vendor,e.amount,e.date,e.id).length>0;
          return <tr key={e.id}><td><strong>{e.vendor}</strong><small>{e.id} · {shortDate(e.date)} · by {name(e.actor)}{e.reversalOf?` · reverses ${e.reversalOf}`:''}{e.reversedBy?` · reversed by ${e.reversedBy}`:''}</small>
            {e.receipt&&<small><Paperclip size={11}/> {e.receipt}</small>}{e.notes&&<small>{e.notes}</small>}{e.reason&&<small>Reason: {e.reason}</small>}{dup&&<Pill tone="orange">Possible duplicate</Pill>}</td>
            <td>{e.category}</td><td>{e.method==='Cash'?'Drawer cash':e.method}</td><td className="number">{money(e.amount)}</td><td><Pill tone={tone(e.status)}>{e.reversalOf?'Posted · reversal':e.status}</Pill></td><td>{actions(e)}</td></tr>;})}
      </tbody></table>{!list.length&&<Empty title={tab==='All'?'No expenses recorded':`No ${tab.toLowerCase()} expenses`} body="Submitted expenses appear here for approval."/>}</div><ShowMore p={pg}/></section>
    <section className="card"><div className="section-title"><h2>Approval requests</h2><span className="muted">Requests cannot be self-approved</span></div>
      {!approvals.length?<Empty title="No approval requests" body="Discounts above the limit, cash differences and closed-date corrections appear here."/>:<div className="request-list">{approvals.map(a=>{
        const cmd=(a.meta?.command as string|undefined)??'APPROVAL',g=s.bookings.find(b=>b.id===a.booking);
        const ownerOnly=['closed-date-correction','correction','credit-note'].includes(a.kind??'')&&user.role!=='Owner';
        return <div className="request" key={a.id}>
          <div><strong>{approvalKindLabel(a.kind)} · {money(a.amount)}{g?` · ${g.guest}`:a.target?` · ${a.target}`:''}</strong><p>{a.id} · {a.reason} · requested by {s.users.find(u=>u.id===a.actor)?.name}</p></div>
          <Pill tone={a.status==='Pending'?'orange':a.status==='Approved'?'green':'neutral'}>{a.status}</Pill>
          {a.status==='Pending'&&(permission('approve')||cashChecker&&a.kind==='cash-difference')&&(a.actor===uid?<span className="f-why">Your request</span>:ownerOnly?<span className="f-why">Owner approves</span>:<>
            {a.kind==='cash-difference'&&permission('payments')?<button className="btn" onClick={()=>go('cashier')}>Review on cashier page</button>:<>
              <button className="btn" disabled={busy} onClick={()=>run({type:cmd,id:a.id,approve:true,key:'approval:'+a.id},'Request approved')}>Approve</button>
              <button className="text-btn" disabled={busy} onClick={()=>run({type:cmd,id:a.id,approve:false,key:'approval:'+a.id},'Request rejected')}>Reject</button></>}</>)}
          {g&&<button className="text-btn" onClick={()=>booking(g.id)}>Folio</button>}
        </div>;})}</div>}</section></>;
}
