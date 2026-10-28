// M16 Expenses (phone page 'expenses'): section chips (DEC-120: Register, Recurring, Petty cash, Budgets, Udhaar, Month & tax), status chips,
// expense cards with actions, Add expense sheet (override of 'expense') with the SCC fields (description, date, GST, bill photo, confirms).
// Reuses EXPENSE / EXPENSE_SUBMIT / EXPENSE_APPROVE / EXPENSE_REVERSE and the DEC-120 EXP_* commands; HCC's approval rules stay.
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import React,{useState,useRef} from 'react';
import {Plus,Paperclip} from 'lucide-react';
import {money,expenseCounts,EXPENSE_MANAGER_LIMIT,closingUnlimitedRoles,Expense,rbacCanSubmitExpense,rbacCanDecideExpense,rbacSeesBooks,cbIsLocked,cbLockedMessage,mnyIsDrawer,mnyApprovalLimit,
  mnyShortLabel,expLedgerFilter,expDescription,expMonthLocked,expYm,EXP_LEDGER_CAP} from '../../services/mock';
import {ExpCategorySelect,ExpVendorInput,ExpCreditNote,mnyExpenseSources} from '../../features/money/masters';
import {useHcc} from '../../app/context';
import {ChipFilter,KpiCard,Fab} from '../../app/mobile/components';
import {Pill,Empty,shortDate,dateLabel} from '../../components/ui';
import {expSectionsFor,type ExpSection,ExpBanners,ExpRecurring,ExpPetty,ExpBudgets,ExpUdhaar,ExpMonth,ExpRowExtras,ExpRowActions,useExpCsv} from '../../features/expenses/Scc';
import {useExpenseForm,ExpenseSccFields} from '../../features/expenses/ExpenseModal';
import {ExpVendors} from '../../features/expenses/vendors';
import {vendorGstKind} from '../../features/expenses/purchase';

const TABS=['All','Draft','Pending','Posted','Rejected','Reversed','Void'] as const;
const tone=(st:string)=>st==='Posted'?'green':st==='Pending'?'orange':st==='Draft'?'blue':'neutral';
/** Funding label on the cards (MA-3: from the payment-methods master; seed labels as before: Drawer cash, UPI, Bank, Vendor credit). */
const fundLabel=(s:Parameters<typeof mnyIsDrawer>[0],m:string)=>mnyShortLabel(s,m,'expenses');

export function MmExpenses(){
  const {s,uid,user,open,run,busy}=useHcc();
  const [section,setSection]=useState<ExpSection>('register');
  const [tab,setTab]=useState<typeof TABS[number]>('All');const [q,setQ]=useState('');
  const list=expLedgerFilter(s,{status:tab,q}).slice(0,EXP_LEDGER_CAP);
  const pg=usePaged(list,PHONE_PAGE);
  const today=s.expenses.filter(e=>expenseCounts(e)&&e.date===s.date);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??(id&&process.env.HCC_BUILD==='production'&&/^\d+$/.test(id)?'another user':id)??'—';
  const csv=useExpCsv();
  function actions(e:Expense){
    if(e.status==='Draft')return e.actor===uid&&rbacCanSubmitExpense(s,uid)?<button className="btn small-btn" disabled={busy}
      onClick={()=>expMonthLocked(s,e.date)?open({type:'exp-override',id:e.id,room:'submit'}):run({type:'EXPENSE_SUBMIT',id:e.id,key:'expsubmit:'+e.id},'Draft submitted for approval')}>Submit for approval</button>:<span className="mm-why">Draft · {name(e.actor)}</span>;
    if(e.status==='Pending'){
      if(!rbacCanDecideExpense(s,uid))return <span className="mm-why">Awaiting approval by a manager, accountant or owner</span>;
      if(e.actor===uid)return <span className="mm-why">Your request — another approver needed</span>;
      if(e.editors?.includes(uid))return <span className="mm-why">You changed it — another approver decides</span>;
      const lim=mnyApprovalLimit(s,e.category,EXPENSE_MANAGER_LIMIT),over=!closingUnlimitedRoles.includes(user.role)&&e.amount>lim,noShift=mnyIsDrawer(s,e.method)&&!s.shiftOpen;
      const locked=expMonthLocked(s,e.date),lockBlock=locked&&user.role!=='Owner';
      return <><div className="mm-two"><button className="btn" disabled={busy} onClick={()=>open({type:'expense-reason',id:e.id,room:'reject'})}>Reject…</button>
        <button className="btn primary" disabled={busy||over||noShift||lockBlock} onClick={()=>locked?open({type:'exp-override',id:e.id,room:'approve'}):run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve & post</button></div>
        {(over||noShift||lockBlock)&&<span className="mm-why">{over?`Above ${money(lim)} — Owner approves`:noShift?'Cash shift not open':`Month ${expYm(e.date)} is tax-locked — the Owner decides`}</span>}</>;
    }
    if(e.status==='Posted'&&!e.reversalOf&&rbacCanDecideExpense(s,uid)){const noShift=mnyIsDrawer(s,e.method)&&!s.shiftOpen,locked=cbIsLocked(s,e.date); // DEC-065
      return <><button className="btn small-btn" disabled={busy||noShift||locked} title={locked?cbLockedMessage(e.date,'reversing this expense'):undefined} onClick={()=>open({type:'expense-reason',id:e.id,room:'reverse'})}>Reverse…</button>{locked?<span className="mm-why">{cbLockedMessage(e.date,'reversing this expense')}</span>:noShift&&<span className="mm-why">Cash shift not open</span>}</>;}
    return null;
  }
  return <div className="mm-page mm-has-fab">
    <ChipFilter label="Expense section" value={section} onChange={setSection} options={expSectionsFor(rbacSeesBooks(s,uid)).map(([id,label])=>({id,label}))}/>
    <ExpBanners onSection={setSection}/>
    {section==='register'?<>
      <div className="mm-kpis"><KpiCard label="Posted today" value={money(today.reduce((a,e)=>a+e.amount,0))} sub={`Drawer cash ${money(today.filter(e=>mnyIsDrawer(s,e.method)).reduce((a,e)=>a+e.amount,0))}`}/>
        <KpiCard label="Pending" value={s.expenses.filter(e=>e.status==='Pending').length} sub="Awaiting approval" onClick={()=>setTab('Pending')}/></div>
      <ChipFilter label="Expense status" value={tab} onChange={setTab} options={TABS.map(t=>({id:t,label:t,count:t==='All'?s.expenses.length:s.expenses.filter(e=>e.status===t).length}))}/>
      <label className="mm-search">Search<input value={q} onChange={e=>setQ(e.target.value)} placeholder="description / vendor / category"/></label>
      {rbacCanDecideExpense(s,uid)&&<button type="button" className="btn" onClick={()=>csv('ledger')}>Ledger CSV</button>}
      {pg.shown.map(e=>{const act=actions(e);
        return <article key={e.id} className="mm-card mm-exp">
          <div className="mm-row"><div className="mm-row-main"><strong>{expDescription(e)}</strong><small>{e.category} · {fundLabel(s,e.method)} · {shortDate(e.date)}</small><small>{e.id} · by {name(e.actor)}{e.reversalOf?` · reverses ${e.reversalOf}`:''}{e.reversedBy?` · reversed by ${e.reversedBy}`:''}</small></div>
            <div className="mm-row-amt"><strong className="number">{money(e.amount)}</strong><Pill tone={tone(e.status)}>{e.reversalOf?'Reversal':e.status}</Pill></div></div>
          <div className="mm-exp-extra"><ExpRowExtras e={e}/>{e.receipt&&<small><Paperclip size={13}/> {e.receipt}</small>}{e.notes&&<small>{e.notes}</small>}{e.reason&&<small>Reason: {e.reason}</small>}</div>
          <div className="mm-exp-actions">{act}<ExpRowActions e={e}/></div>
        </article>;})}
      <ShowMore p={pg}/>
      {!list.length&&<Empty title={tab==='All'?'No expenses recorded':`No ${tab.toLowerCase()} expenses`} body="Submitted expenses appear here for approval."/>}
    </>:section==='recurring'?<ExpRecurring phone/>:section==='petty'?<ExpPetty phone/>:section==='budgets'?<ExpBudgets phone/>:section==='udhaar'?<ExpUdhaar phone/>:section==='vendors'&&rbacSeesBooks(s,uid)?<ExpVendors phone/>:<ExpMonth phone/>}
    {rbacCanSubmitExpense(s,uid)&&<Fab label="Add expense" icon={Plus} onClick={()=>open({type:'expense'})}/>}
  </div>;
}

/** Phone override of 'expense': vendor, category, amount, funding chips, description, date, GST, bill photo, bill number; SCC confirms inline. */
export function MmExpenseSheet(){
  const {s,run,closeModal,modal,busy}=useHcc();
  const x=useExpenseForm(),{f,setF}=x;
  const [problem,setProblem]=useState('');
  const form=useRef<HTMLFormElement>(null);
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement|HTMLTextAreaElement>)=>setF({...f,[k]:e.target.value});
  function save(draft:boolean){
    const p=x.problem(draft);setProblem(p);if(p)return;
    run(x.command(draft,modal!.key),draft?'Draft saved — submit it when ready':'Expense submitted for approval',{close:true});
  }
  return <><div className="modal-heading"><h2>Add expense</h2><p>Business date {dateLabel(s.date)}. Another authorised person approves before it is posted; drawer cash is reduced once, on posting.</p></div>
    <form ref={form} className="form mm-form" onSubmit={e=>{e.preventDefault();save(false);}} noValidate>
      <ExpVendorInput phone value={f.vendor} onChange={v=>setF({...f,vendor:v,gstKind:vendorGstKind(s,v)})}/>
      <ExpCategorySelect value={f.category} onChange={v=>setF({...f,category:v})}/>
      <label className="mm-amount">Amount (₹)<input required inputMode="decimal" enterKeyHint="next" value={f.amount} onChange={set('amount')} placeholder="0"/></label>
      <div className="mm-field"><span className="mm-label" id="mm-fund">Funding source</span>
        <div className="mm-chips" role="radiogroup" aria-labelledby="mm-fund">{mnyExpenseSources(s,true).map(([v,l])=><button key={v} type="button" role="radio" aria-checked={f.method===v} className={f.method===v?'on':''} onClick={()=>setF({...f,method:v})}>{l}</button>)}</div></div>
      <ExpCreditNote method={f.method}/>
      <ExpenseSccFields x={x}/>
      <label>Bill / receipt number<input maxLength={120} enterKeyHint="next" value={f.receipt} onChange={set('receipt')} placeholder="e.g. AQ-0026"/></label>
      <label>Notes<textarea maxLength={300} value={f.notes} onChange={set('notes')} placeholder="Optional"/></label>
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="modal-actions"><button type="button" className="btn" disabled={busy} onClick={()=>save(true)}>Save draft</button>
        <button className="btn primary" disabled={busy}>Submit</button></div></form></>;
}
