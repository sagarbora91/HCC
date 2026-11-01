// SCR-13 Expenses: status tabs, draft/submit/approve/reject/reverse; plus the approval requests list. DEC-120 adds SCC's features in
// sections — Register (SCC ledger filters: mode, month, search; 200 shown; edit / void / bill photo / entry text; ledger CSV), Recurring,
// Petty cash, Budgets, Udhaar and Month & tax — while HCC's approval rules stay (counted once approved; Manager ≤ ₹5,000 or the category's
// limit; never your own nor one you changed; the Cashier never reverses or voids).
import React,{useId,useState} from 'react';
import {Paperclip,Download} from 'lucide-react';
import {money,expenseCounts,approvalKindLabel,EXPENSE_MANAGER_LIMIT,closingUnlimitedRoles,Expense,rbacCanSubmitExpense,rbacCanDecideExpense,rbacCanDecideCash,cbIsLocked,cbLockedMessage,
  mnyIsDrawer,mnyApprovalLimit,mnyVendor,mnyNames,expLedgerFilter,expDescription,expMonthLocked,expGstPaid,expYm,EXP_LEDGER_CAP} from '../../services/mock';
import {MstLink} from '../masters/MasterList';
import {useHcc} from '../../app/context';
import {Pill,Empty,shortDate} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip,FitNote} from '../../components/fit';
import {ExpVendors} from './vendors';
import {seesVendorBooks} from '../accounting/shared';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {expSectionsFor,type ExpSection,ExpBanners,ExpRecurring,ExpPetty,ExpBudgets,ExpUdhaar,ExpMonth,ExpRowExtras,ExpRowActions,useExpCsv} from './Scc';
const SECTION_IDS=['register','recurring','petty','budgets','udhaar','vendors','month'] as const;
const tabs=['All','Draft','Pending','Posted','Rejected','Reversed','Void'] as const;
const tone=(st:string)=>st==='Posted'?'green':st==='Pending'?'orange':st==='Draft'?'blue':'neutral';
export function Expenses(){
  const {s,uid,user,open,run,permission,busy,go,booking}=useHcc();
  // DEC-063 G4/G5: the Accountant submits own expenses and decides/reverses them; the Cashier never reverses.
  const canSubmit=rbacCanSubmitExpense(s,uid),canDecide=rbacCanDecideExpense(s,uid);
  // FIT (DEC-194…196): the page's sub-screens are the sections (register … month & tax); the status tabs inside the register keep their ids.
  const books=seesVendorBooks(s,uid);const [asked,setSection]=useFitTab<ExpSection>('expenses',SECTION_IDS,'register');
  const section:ExpSection=asked==='vendors'&&!books?'register':asked;const regId=useId();
  const [tab,setTab]=useState<typeof tabs[number]>('All');
  const [mode,setMode]=useState('');const [month,setMonth]=useState('');const [q,setQ]=useState('');
  const all=expLedgerFilter(s,{status:tab,mode,month,q}),list=all.slice(0,EXP_LEDGER_CAP);
  const pg=usePaged(list,DESKTOP_PAGE);
  const today=s.expenses.filter(e=>expenseCounts(e)&&e.date===s.date);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??(id&&process.env.HCC_BUILD==='production'&&/^\d+$/.test(id)?'another user':id)??'—';
  const csv=useExpCsv();
  function actions(e:Expense){
    if(e.status==='Draft'){if(e.actor!==uid||!canSubmit)return <span className="muted">Draft · {name(e.actor)}</span>;
      const locked=expMonthLocked(s,e.date);
      return <button className="btn small-btn" disabled={busy} onClick={()=>locked?open({type:'exp-override',id:e.id,room:'submit'}):run({type:'EXPENSE_SUBMIT',id:e.id,key:'expsubmit:'+e.id},'Draft submitted for approval')}>Submit</button>;}
    if(e.status==='Pending'){
      if(!canDecide)return <span className="muted">Awaiting approval</span>;
      if(e.actor===uid)return <span className="f-why">Your request — another approver needed</span>;
      if(e.editors?.includes(uid))return <span className="f-why">You changed it — another approver decides</span>;
      const lim=mnyApprovalLimit(s,e.category,EXPENSE_MANAGER_LIMIT),over=!closingUnlimitedRoles.includes(user.role)&&e.amount>lim,noShift=mnyIsDrawer(s,e.method)&&!s.shiftOpen;
      const locked=expMonthLocked(s,e.date);
      return <div className="inline"><button className="btn small-btn" disabled={busy||over||noShift||locked&&user.role!=='Owner'}
          onClick={()=>locked?open({type:'exp-override',id:e.id,room:'approve'}):run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve & post</button>
        <button className="text-btn" disabled={busy} onClick={()=>open({type:'expense-reason',id:e.id,room:'reject'})}>Reject…</button>
        {(over||noShift||locked&&user.role!=='Owner')&&<small className="f-why">{over?`Above ${money(lim)} — Owner approves`:noShift?'Cash shift not open':`Month ${expYm(e.date)} is tax-locked — the Owner decides`}</small>}</div>;
    }
    if(e.status==='Posted'&&!e.reversalOf&&canDecide){const noShift=mnyIsDrawer(s,e.method)&&!s.shiftOpen,locked=cbIsLocked(s,e.date); // DEC-065
      return <div className="inline"><button className="text-btn" disabled={busy||noShift||locked} title={locked?cbLockedMessage(e.date,'reversing this expense'):undefined} onClick={()=>open({type:'expense-reason',id:e.id,room:'reverse'})}>Reverse…</button>{locked?<small className="f-why">{cbLockedMessage(e.date,'reversing this expense')}</small>:noShift&&<small className="f-why">Cash shift not open</small>}</div>;}
    return null;
  }
  const approvals=s.approvals.slice().reverse();
  // The Accountant decides cash differences here (no cashier page); the counter never decides their own (SOD-004).
  const cashChecker=!permission('approve')&&rbacCanDecideCash(s,uid);
  const flat=(label:string,node:React.ReactNode)=><ScrollPanel label={label}>{node}</ScrollPanel>;
  const register=<div className="fit-cols exp-fit-cols"><div>
      <section className="card fit-panel exp-register" aria-labelledby={regId}>
        <div className="exp-subbar"><div className="exp-reg-top"><div className="section-title fit-panel-head exp-reg-title"><h2 id={regId}>Expense register</h2></div>
          <div className="tabs" role="tablist" aria-label="Expense status">{tabs.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}<span>{t==='All'?s.expenses.length:s.expenses.filter(e=>e.status===t).length}</span></button>)}</div></div>
          <div className="form-row exp-filters"><label>Mode<select value={mode} onChange={e=>setMode(e.target.value)}><option value="">All</option>{mnyNames(s,'expenses').map(m=><option key={m}>{m}</option>)}</select></label>
            <label>Month<input type="month" value={month} onChange={e=>setMonth(e.target.value)}/></label>
            <label>Search<input value={q} onChange={e=>setQ(e.target.value)} placeholder="description / vendor / category"/></label></div></div>
        <div className="fit-panel-body" data-fit-scroll="" tabIndex={0} aria-label="Expense register rows"><div className="table-wrap"><table><thead><tr><th>Description / reference</th><th>Category</th><th>Funding</th><th className="number">Amount</th><th>Status</th><th>Action</th></tr></thead><tbody>
          {pg.shown.map(e=><tr key={e.id}><td><strong>{expDescription(e)}</strong>{e.vendor&&!mnyVendor(s,e.vendor)&&<small>Not in the vendor list</small>}<ExpRowExtras e={e}/>
              <small>{e.id} · {shortDate(e.date)} · by {name(e.actor)}{e.reversalOf?` · reverses ${e.reversalOf}`:''}{e.reversedBy?` · reversed by ${e.reversedBy}`:''}</small>
              {e.receipt&&<small><Paperclip size={11}/> {e.receipt}</small>}{e.notes&&<small>{e.notes}</small>}{e.reason&&<small>Reason: {e.reason}</small>}</td>
              <td>{e.category}</td><td>{mnyIsDrawer(s,e.method)?'Drawer cash':e.method}</td><td className="number">{money(e.amount)}</td><td><Pill tone={tone(e.status)}>{e.reversalOf?'Posted · reversal':e.status}</Pill></td>
              <td>{actions(e)}<ExpRowActions e={e}/></td></tr>)}
        </tbody></table>{!list.length&&<Empty title={tab==='All'?'No expenses recorded':`No ${tab.toLowerCase()} expenses`} body="Submitted expenses appear here for approval."/>}</div></div>
        <div className="fit-panel-foot"><ShowMore p={pg}/>{all.length>EXP_LEDGER_CAP&&<FitNote text={`Showing the ${EXP_LEDGER_CAP} most recent of ${all.length} entries. Use the filters above (status / mode / month / search) to narrow.`}/>}</div></section></div>
    <div><ScrollPanel title="Approval requests" label="Approval requests" actions={<span className="muted">Requests cannot be self-approved</span>}>
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
        </div>;})}</div>}</ScrollPanel></div></div>;
  const body=section==='register'?register:section==='recurring'?flat('Recurring expenses',<ExpRecurring/>):section==='petty'?flat('Petty cash',<ExpPetty/>):section==='budgets'?flat('Monthly budgets',<ExpBudgets/>):section==='udhaar'?flat('Udhaar credit register',<ExpUdhaar/>):section==='vendors'?flat('Vendor payments',<ExpVendors/>):flat('Month and tax',<ExpMonth/>);
  const stats=[{title:'Posted today (net)',value:money(today.reduce((a,e)=>a+e.amount,0))},{title:'Drawer cash today',value:money(today.filter(e=>mnyIsDrawer(s,e.method)).reduce((a,e)=>a+e.amount,0))},
    {title:'Pending approval',value:String(s.expenses.filter(e=>e.status==='Pending').length)},{title:'Drafts',value:String(s.expenses.filter(e=>e.status==='Draft').length)},{title:'GST paid this month',value:money(expGstPaid(s,expYm(s.date)))}];
  return <FitPage title="Expenses & approvals" sub="Every expense accounted for. Every adjustment traceable." layout="fill"
      actions={<>{canSubmit&&<button className="btn primary" disabled={busy} onClick={()=>open({type:'expense'})}>+ Add expense</button>}
        {section==='register'&&<><Pill>Bill photos up to 4 MB</Pill>{(permission('admin')||permission('masters'))&&<><MstLink spec="expenseCategories" label="Categories…"/><MstLink spec="expenseVendors" label="Vendors…"/><MstLink spec="paymentMethods" label="Funding sources…"/></>}
          {canDecide&&<button type="button" className="btn small-btn" onClick={()=>csv('ledger')}><Download size={15}/> Ledger CSV</button>}</>}</>}
      toolbar={<StatStrip items={stats}/>}
      tabs={<FitTabs page="expenses" value={section} onChange={setSection} tabs={expSectionsFor(books).map(([id,label])=>({id,label}))}/>}>
    <ExpBanners onSection={setSection} fit/>
    {body}
  </FitPage>;
}
