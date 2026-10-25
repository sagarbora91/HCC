// M16 Expenses (phone page 'expenses'): status chips, expense cards with actions, Add expense sheet (override of 'expense')
// with an inline duplicate warning. Reuses EXPENSE / EXPENSE_SUBMIT / EXPENSE_APPROVE / EXPENSE_REVERSE.
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import React,{useState,useRef} from 'react';
import {Plus,Paperclip,AlertTriangle,Info} from 'lucide-react';
import {money,expenseDuplicates,expenseCounts,EXPENSE_MANAGER_LIMIT,EXPENSE_CATEGORIES,closingUnlimitedRoles,Expense,rbacCanSubmitExpense,rbacCanDecideExpense} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter,KpiCard,Fab} from '../../app/mobile/components';
import {Pill,Empty,shortDate,dateLabel} from '../../components/ui';
import {mmExpenseCommand,mmPaise} from './logic';

const TABS=['All','Draft','Pending','Posted','Rejected','Reversed'] as const;
const tone=(st:string)=>st==='Posted'?'green':st==='Pending'?'orange':st==='Draft'?'blue':'neutral';
const FUNDING:[string,string][]=[['Cash','Drawer cash'],['UPI','UPI'],['Bank transfer','Bank'],['Vendor credit','Vendor credit']];
const fundLabel=(m:string)=>FUNDING.find(f=>f[0]===m)?.[1]??m;

export function MmExpenses(){
  const {s,uid,user,open,run,permission,busy}=useHcc();
  const [tab,setTab]=useState<typeof TABS[number]>('All');
  const list=s.expenses.filter(e=>tab==='All'||e.status===tab).slice().reverse();
  const pg=usePaged(list,PHONE_PAGE);
  const today=s.expenses.filter(e=>expenseCounts(e)&&e.date===s.date);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
  function actions(e:Expense){
    if(e.status==='Draft')return e.actor===uid&&rbacCanSubmitExpense(s,uid)?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'EXPENSE_SUBMIT',id:e.id,key:'expsubmit:'+e.id},'Draft submitted for approval')}>Submit for approval</button>:<span className="mm-why">Draft · {name(e.actor)}</span>;
    if(e.status==='Pending'){
      if(!rbacCanDecideExpense(s,uid))return <span className="mm-why">Awaiting approval by a manager, accountant or owner</span>;
      if(e.actor===uid)return <span className="mm-why">Your request — another approver needed</span>;
      const over=!closingUnlimitedRoles.includes(user.role)&&e.amount>EXPENSE_MANAGER_LIMIT,noShift=e.method==='Cash'&&!s.shiftOpen;
      return <><div className="mm-two"><button className="btn" disabled={busy} onClick={()=>open({type:'expense-reason',id:e.id,room:'reject'})}>Reject…</button>
        <button className="btn primary" disabled={busy||over||noShift} onClick={()=>run({type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id},'Expense approved and posted')}>Approve & post</button></div>
        {(over||noShift)&&<span className="mm-why">{over?`Above ${money(EXPENSE_MANAGER_LIMIT)} — Owner approves`:'Cash shift not open'}</span>}</>;
    }
    if(e.status==='Posted'&&!e.reversalOf&&rbacCanDecideExpense(s,uid)){const noShift=e.method==='Cash'&&!s.shiftOpen;
      return <><button className="btn small-btn" disabled={busy||noShift} onClick={()=>open({type:'expense-reason',id:e.id,room:'reverse'})}>Reverse…</button>{noShift&&<span className="mm-why">Cash shift not open</span>}</>;}
    return null;
  }
  return <div className="mm-page mm-has-fab">
    <div className="mm-kpis"><KpiCard label="Posted today" value={money(today.reduce((a,e)=>a+e.amount,0))} sub={`Drawer cash ${money(today.filter(e=>e.method==='Cash').reduce((a,e)=>a+e.amount,0))}`}/>
      <KpiCard label="Pending" value={s.expenses.filter(e=>e.status==='Pending').length} sub="Awaiting approval" onClick={()=>setTab('Pending')}/></div>
    <ChipFilter label="Expense status" value={tab} onChange={setTab} options={TABS.map(t=>({id:t,label:t,count:t==='All'?s.expenses.length:s.expenses.filter(e=>e.status===t).length}))}/>
    {pg.shown.map(e=>{const dup=!e.reversalOf&&e.status!=='Rejected'&&expenseDuplicates(s,e.vendor,e.amount,e.date,e.id).length>0;const act=actions(e);
      return <article key={e.id} className="mm-card mm-exp">
        <div className="mm-row"><div className="mm-row-main"><strong>{e.vendor}</strong><small>{e.category} · {fundLabel(e.method)} · {shortDate(e.date)}</small><small>{e.id} · by {name(e.actor)}{e.reversalOf?` · reverses ${e.reversalOf}`:''}{e.reversedBy?` · reversed by ${e.reversedBy}`:''}</small></div>
          <div className="mm-row-amt"><strong className="number">{money(e.amount)}</strong><Pill tone={tone(e.status)}>{e.reversalOf?'Reversal':e.status}</Pill></div></div>
        {(e.receipt||e.notes||e.reason||dup)&&<div className="mm-exp-extra">{e.receipt&&<small><Paperclip size={13}/> {e.receipt}</small>}{e.notes&&<small>{e.notes}</small>}{e.reason&&<small>Reason: {e.reason}</small>}{dup&&<Pill tone="orange">Possible duplicate</Pill>}</div>}
        {act&&<div className="mm-exp-actions">{act}</div>}
      </article>;})}
    <ShowMore p={pg}/>
    {!list.length&&<Empty title={tab==='All'?'No expenses recorded':`No ${tab.toLowerCase()} expenses`} body="Submitted expenses appear here for approval."/>}
    {rbacCanSubmitExpense(s,uid)&&<Fab label="Add expense" icon={Plus} onClick={()=>open({type:'expense'})}/>}
  </div>;
}

/** Phone override of 'expense': vendor, category, amount, funding chips, receipt name; duplicate warning inline. */
export function MmExpenseSheet(){
  const {s,run,closeModal,modal,busy}=useHcc();
  const [f,setF]=useState({vendor:'',category:EXPENSE_CATEGORIES[0],method:'Cash',amount:'',receipt:'',notes:''});
  const [problem,setProblem]=useState('');
  const form=useRef<HTMLFormElement>(null);
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement|HTMLTextAreaElement>)=>setF({...f,[k]:e.target.value});
  const paise=mmPaise(f.amount);
  const dups=paise>0?expenseDuplicates(s,f.vendor,paise,s.date):[];
  function save(draft:boolean){
    const p=!f.vendor.trim()?'Enter a vendor.':!(paise>0)?'Enter an amount greater than zero.':paise>100000000?'Amount is too large.':'';
    setProblem(p);if(p)return;
    run(mmExpenseCommand(f,draft,modal!.key),draft?'Draft saved — submit it when ready':'Expense submitted for approval',{close:true});
  }
  return <><div className="modal-heading"><h2>Add expense</h2><p>Dated {dateLabel(s.date)}. Another authorised person approves before it is posted; drawer cash is reduced once, on posting.</p></div>
    <form ref={form} className="form mm-form" onSubmit={e=>{e.preventDefault();save(false);}} noValidate>
      <label>Vendor<input required maxLength={80} enterKeyHint="next" autoComplete="off" value={f.vendor} onChange={set('vendor')} placeholder="Fictional vendor name"/></label>
      <label>Category<select value={f.category} onChange={set('category')}>{EXPENSE_CATEGORIES.map(x=><option key={x}>{x}</option>)}</select></label>
      <label className="mm-amount">Amount (₹)<input required inputMode="decimal" enterKeyHint="next" value={f.amount} onChange={set('amount')} placeholder="0"/></label>
      {!!dups.length&&<div className="info-note mm-dup" role="alert"><AlertTriangle size={18}/><span><strong>Possible duplicate:</strong> {dups.map(d=>`${d.id} (${d.status})`).join(', ')} has the same vendor, amount ({money(paise)}) and date. You can still continue.</span></div>}
      <div className="mm-field"><span className="mm-label" id="mm-fund">Funding source</span>
        <div className="mm-chips" role="radiogroup" aria-labelledby="mm-fund">{FUNDING.map(([v,l])=><button key={v} type="button" role="radio" aria-checked={f.method===v} className={f.method===v?'on':''} onClick={()=>setF({...f,method:v})}>{l}</button>)}</div></div>
      {f.method==='Vendor credit'&&<div className="info-note"><Info size={16}/>Vendor credit records the expense without any cash drawer effect.</div>}
      <label>Receipt name (placeholder — nothing is uploaded)<input maxLength={120} enterKeyHint="next" value={f.receipt} onChange={set('receipt')} placeholder="e.g. aqua-water-0026.jpg"/></label>
      <label>Notes<textarea maxLength={300} value={f.notes} onChange={set('notes')} placeholder="Optional"/></label>
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="modal-actions"><button type="button" className="btn" disabled={busy} onClick={()=>save(true)}>Save draft</button>
        <button className="btn primary" disabled={busy}>Submit</button></div></form></>;
}
