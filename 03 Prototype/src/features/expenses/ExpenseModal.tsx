// SCR-13 expense form: save draft or submit; funding source incl. vendor credit; receipt placeholder; duplicate warning (not blocking).
import React,{useState,useRef} from 'react';
import {AlertTriangle,Info} from 'lucide-react';
import {money,expenseDuplicates,EXPENSE_CATEGORIES} from '../../services/mock';
import {useHcc} from '../../app/context';
import {dateLabel} from '../../components/ui';
const sources:[string,string][]=[['Cash','Drawer cash'],['UPI','UPI'],['Bank transfer','Bank transfer'],['Vendor credit','Vendor credit (pay later)']];
export function ExpenseModal(){
  const {s,run,closeModal,modal,busy}=useHcc();
  const [f,setF]=useState({vendor:'',category:EXPENSE_CATEGORIES[0],method:'Cash',amount:'',receipt:'',notes:''});
  const form=useRef<HTMLFormElement>(null);
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement|HTMLTextAreaElement>)=>setF({...f,[k]:e.target.value});
  const paise=Math.round(Number(f.amount)*100);
  const dups=paise>0?expenseDuplicates(s,f.vendor,paise,s.date):[];
  function save(draft:boolean){
    if(!form.current?.reportValidity())return;
    run({type:'EXPENSE',vendor:f.vendor,category:f.category,method:f.method,amount:paise,receipt:f.receipt,notes:f.notes,draft,key:modal!.key+(draft?':draft':':submit')},
      draft?'Draft saved — submit it when ready':'Expense submitted for approval',{close:true});
  }
  return <><div className="modal-heading"><h2>Record an expense</h2><p>Dated business date {dateLabel(s.date)}. Another authorised person approves before it is posted; drawer cash is reduced once, on posting.</p></div>
    <form ref={form} className="form" onSubmit={e=>{e.preventDefault();save(false);}}>
      <label>Vendor<input required maxLength={80} value={f.vendor} onChange={set('vendor')} placeholder="Fictional vendor name"/></label>
      <div className="form-row"><label>Category<select value={f.category} onChange={set('category')}>{EXPENSE_CATEGORIES.map(x=><option key={x}>{x}</option>)}</select></label>
        <label>Funding source<select value={f.method} onChange={set('method')}>{sources.map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label></div>
      <label>Amount (₹)<input required type="number" min="0.01" max="1000000" step="0.01" value={f.amount} onChange={set('amount')}/></label>
      <label>Receipt (placeholder — file name only, nothing is uploaded)<input maxLength={120} value={f.receipt} onChange={set('receipt')} placeholder="e.g. aqua-water-0026.jpg"/></label>
      <label>Notes<textarea maxLength={300} value={f.notes} onChange={set('notes')} placeholder="Optional"/></label>
      {f.method==='Vendor credit'&&<div className="info-note"><Info size={16}/>Vendor credit records the expense without any cash drawer effect.</div>}
      {!!dups.length&&<div className="info-note f-warn" role="alert"><AlertTriangle size={16}/>Possible duplicate: {dups.map(d=>`${d.id} (${d.status})`).join(', ')} has the same vendor, amount ({money(paise)}) and date. You can still continue.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button type="button" className="btn subtle" disabled={busy} onClick={()=>save(true)}>Save draft</button>
        <button className="btn primary" disabled={busy}>Submit for approval</button></div></form></>;
}
/** Modal 'expense-reason': id = expense ID, room = 'reject' | 'reverse'. */
export function ExpenseReasonModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,modal,busy}=useHcc();const [reason,setReason]=useState('');
  const e=s.expenses.find(x=>x.id===id);const reverse=room==='reverse';
  if(!e)return <p className="padded">This expense is unavailable.</p>;
  return <><div className="modal-heading"><h2>{reverse?'Reverse a posted expense':'Reject an expense'}</h2><p>{e.id} · {e.vendor} · {money(e.amount)} · {e.method==='Cash'?'drawer cash':e.method}</p></div>
    <form className="form" onSubmit={ev=>{ev.preventDefault();if(!reason.trim())return;
      run(reverse?{type:'EXPENSE_REVERSE',id:e.id,reason:reason.trim(),key:modal!.key}:{type:'EXPENSE_APPROVE',id:e.id,approve:false,reason:reason.trim(),key:modal!.key},reverse?'Expense reversed — a negative record was posted today':'Expense rejected',{close:true});}}>
      <label>Reason (required)<textarea required maxLength={200} value={reason} onChange={ev=>setReason(ev.target.value)}/></label>
      {reverse&&<div className="info-note"><Info size={16}/>The original stays on its date, marked Reversed. A new posted record of −{money(e.amount)} is dated {dateLabel(s.date)}{e.method==='Cash'?`, returning ${money(e.amount)} to expected drawer cash`:''}. Reports count each exactly once.</div>}
      {!reason.trim()&&<p className="f-why">Enter a reason to continue.</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!reason.trim()}>{reverse?'Reverse expense':'Reject expense'}</button></div></form></>;
}
