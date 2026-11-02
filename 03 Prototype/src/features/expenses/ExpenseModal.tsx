// SCR-13 expense form: save draft or submit; funding source incl. vendor credit. DEC-120 (SCC features): description (required to submit),
// optional vendor (a new name joins the vendor list), a chosen date (the business date or the Owner-reopened day; never later), GST on the
// bill with SCC's prefill, a bill photo (SCC: ≤ 4 MB, 1000 px wide, JPEG 0.7), SCC's duplicate and "GST more than amount" confirms, and
// the Owner override reason for a tax-locked month. HCC's approval rules stay: another authorised person approves before it counts.
// DEC-135: GST on the bill carries ITC allowed / disallowed (default disallowed).
import React,{useState,useRef} from 'react';
import {AlertTriangle,Info} from 'lucide-react';
import {money,expenseDuplicates,mnyCategoryNames,mnyIsDrawer,expOpenDates,expGstOverMessage,expDuplicateMessage,expBudgetAlert,expItcRefusal,EXP_MAX_PAISE} from '../../services/mock';
import {ExpCategorySelect,ExpVendorInput,ExpCreditNote,mnyExpenseSources,mnyFirstSource} from '../money/masters';
import {useHcc} from '../../app/context';
import {dateLabel} from '../../components/ui';
import {FormSections} from '../../components/fit';
import {GstFields,ItcField,BillPhotoPicker,useOverride,expPaise,expBps} from './Scc';
import {ExpensePurchaseFields,purchaseCommand,purchaseProblem,vendorGstKind} from './purchase'; // ACC-2 purchase fields (contract §3)

/** The add-expense form state shared by the desktop dialog and the phone sheet. */
export function useExpenseForm(){
  const {s}=useHcc();
  const [f,setF]=useState(()=>({vendor:'',description:'',category:mnyCategoryNames(s)[0]??'',method:mnyFirstSource(s),amount:'',receipt:'',notes:'',date:s.date,rate:'',gst:'',photo:'',
    confirmDuplicate:false,confirmGst:false,itc:'disallowed' as 'allowed'|'disallowed',gstKind:'intra' as 'intra'|'inter',rcmBps:''}));
  const ov=useOverride(f.date);
  const paise=expPaise(f.amount),rate=expBps(f.rate),gst=f.gst.trim()?expPaise(f.gst):0;
  const dups=paise>0&&f.description.trim()?expenseDuplicates(s,f.description,paise,f.date):[];
  const dates=expOpenDates(s);
  const problem=(draft:boolean)=>!(paise>0)?'Enter an amount greater than zero.':paise>EXP_MAX_PAISE?'Amount is too large (maximum ₹10,00,00,000).'
    :!draft&&!f.description.trim()?'Description is required':Number.isNaN(rate)?'Enter a GST rate between 0 and 100 %.':Number.isNaN(gst)?'Enter the GST on the bill in rupees.'
    :gst>paise&&!f.confirmGst?expGstOverMessage(gst,paise):gst>0&&expItcRefusal(f.itc,gst,paise)?expItcRefusal(f.itc,gst,paise)
    :purchaseProblem(s,f,Number.isNaN(gst)?0:gst,Number.isNaN(rate)?0:rate)||(dups.length&&!f.confirmDuplicate?expDuplicateMessage(f.date,paise,f.description.trim()):ov.blocked);
  const command=(draft:boolean,key:string)=>({type:'EXPENSE',vendor:f.vendor,description:f.description.trim(),category:f.category,method:f.method,amount:paise,receipt:f.receipt,notes:f.notes,draft,
    ...(f.date!==s.date?{date:f.date}:{}),...(rate?{gstRateBps:rate}:{}),...(gst?{gstPaise:gst,itc:f.itc}:{}),...(f.photo?{billPhoto:f.photo}:{}),
    ...(f.confirmDuplicate?{confirmDuplicate:true}:{}),...purchaseCommand(s,f,Number.isNaN(gst)?0:gst),...(f.confirmGst?{confirmGst:true}:{}),...(ov.override?{override:ov.override}:{}),key:key+(draft?':draft':':submit')});
  const alert=paise>0?expBudgetAlert(s,f.category,f.date,paise):null;
  return {f,setF,ov,paise,gst,dups,dates,problem,command,alert};
}

/** The DEC-120 fields of the add form (both layouts): description, date, GST, photo, confirms, override. */
type ExpForm=ReturnType<typeof useExpenseForm>;
export function ExpenseSccFields({x}:{x:ExpForm}){return <><ExpDescField x={x}/><ExpTaxFields x={x}/><ExpPhotoFields x={x}/></>;}
/** FIT-2: the DEC-120 fields in three parts (the desktop dialog shows one part per section; the phone sheet still shows all three in a row). */
export function ExpDescField({x}:{x:ExpForm}){const {f,setF}=x;
  return <label>Description (what is this for?)<input maxLength={300} enterKeyHint="next" value={f.description} onChange={e=>setF({...f,description:e.target.value,confirmDuplicate:false})}/></label>;}
export function ExpTaxFields({x}:{x:ExpForm}){
  const {s}=useHcc();const {f,setF,paise,gst,dups,dates}=x;
  return <>
    {dates.length>1?<label>Expense date<select value={f.date} onChange={e=>setF({...f,date:e.target.value})}>{dates.map(d=><option key={d} value={d}>{dateLabel(d)}{d===s.date?' (business date)':' (reopened day)'}</option>)}</select></label>
      :<p className="muted">Dated {dateLabel(f.date)} (the business date). A closed day takes entries only while the Owner has it reopened.</p>}
    <GstFields amount={paise} rate={f.rate} gst={f.gst} onRate={v=>setF({...f,rate:v})} onGst={v=>setF({...f,gst:v,confirmGst:false})}/>
    {gst>paise&&paise>0&&<label className="checkbox-line"><input type="checkbox" checked={f.confirmGst} onChange={e=>setF({...f,confirmGst:e.target.checked})}/> {expGstOverMessage(gst,paise)}</label>}
    <ItcField gst={Number.isNaN(gst)?0:gst} amount={paise} value={f.itc} onChange={v=>setF({...f,itc:v})}/>
    <ExpensePurchaseFields f={f} setF={setF} paise={Number.isNaN(paise)?0:paise} gst={gst}/>
    {!!dups.length&&<div className="info-note f-warn" role="alert"><AlertTriangle size={16}/><span>{expDuplicateMessage(f.date,paise,f.description.trim())} ({dups.map(d=>`${d.id} ${d.status}`).join(', ')})
      <label className="checkbox-line"><input type="checkbox" checked={f.confirmDuplicate} onChange={e=>setF({...f,confirmDuplicate:e.target.checked})}/> Save anyway</label></span></div>}
  </>;
}
export function ExpPhotoFields({x}:{x:ExpForm}){const {f,setF}=x;
  return <>
    <BillPhotoPicker value={f.photo} onChange={v=>setF({...f,photo:v})}/>
    {x.ov.field}
    {x.alert&&<p className={x.alert.style==='err'?'f-why':'muted'} role="status">{x.alert.msg}</p>}
  </>;
}

export function ExpenseModal(){
  const {run,closeModal,modal,busy,s}=useHcc();
  const x=useExpenseForm(),{f,setF}=x;
  const form=useRef<HTMLFormElement>(null);
  const [why,setWhy]=useState('');
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement|HTMLTextAreaElement>)=>setF({...f,[k]:e.target.value});
  function save(draft:boolean){
    const p=x.problem(draft);setWhy(p);if(p)return;
    run(x.command(draft,modal!.key),draft?'Draft saved — submit it when ready':'Expense submitted for approval',{close:true});
  }
  // FIT-2 (§12): the form is three sections that each fit; the rail beside them shows each section's current values (read-only, from the form state).
  const [sec,setSec]=useState<'what'|'tax'|'bill'>('what');
  const gstShown=Number.isNaN(x.gst)?0:x.gst;
  const sections=[
    {id:'what',label:'What was bought',summary:`${f.vendor.trim()||'No vendor'} · ${f.category} · ${mnyExpenseSources(s).find(([v])=>v===f.method)?.[1]??f.method} · ${x.paise>0?money(x.paise):'amount not entered'}${f.description.trim()?' · '+f.description.trim():' · no description'}`},
    {id:'tax',label:'Date and tax',summary:`${dateLabel(f.date)} · ${gstShown>0?`GST ${money(gstShown)} · ITC ${f.itc}`:'no GST on the bill'}${f.receipt.trim()?' · bill '+f.receipt.trim():''}${x.ov.locked?' · tax-locked month':''}${x.dups.length?` · ${x.dups.length} possible duplicate${x.dups.length===1?'':'s'}`:''}`},
    {id:'bill',label:'Photo and notes',summary:`${f.photo?'Bill photo attached':'No bill photo'}${f.notes.trim()?' · '+f.notes.trim():' · no notes'}`},
  ];
  return <><div className="modal-heading"><h2>Record an expense</h2><p>Business date {dateLabel(s.date)}. Another authorised person approves before it is posted; drawer cash is reduced once, on posting.</p></div>
    <form ref={form} className="form exp-add-task" onSubmit={e=>{e.preventDefault();save(false);}}>
      <FormSections sections={sections} value={sec} onChange={id=>setSec(id as typeof sec)}
        actions={<>{why&&<p className="f-why" role="alert">{why}</p>}<button type="button" className="btn" onClick={closeModal}>Cancel</button>
          <button type="button" className="btn subtle" disabled={busy} onClick={()=>save(true)}>Save draft</button>
          <button className="btn primary" disabled={busy}>Submit for approval</button></>}>
        {sec==='what'&&<>
          <ExpVendorInput value={f.vendor} onChange={v=>setF({...f,vendor:v,gstKind:vendorGstKind(s,v)})}/>
          <div className="form-row"><ExpCategorySelect value={f.category} onChange={v=>setF({...f,category:v})}/>
            <label>Funding source<select value={f.method} onChange={set('method')}>{mnyExpenseSources(s).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label></div>
          <label>Amount (₹)<input inputMode="decimal" value={f.amount} onChange={set('amount')}/></label>
          <ExpDescField x={x}/>
          <ExpCreditNote method={f.method}/>
          {mnyIsDrawer(s,f.method)&&f.date!==s.date&&<p className="f-why">Drawer cash leaves today’s drawer: date a cash expense on the business date, or choose another funding source.</p>}</>}
        {sec==='tax'&&<>
          <ExpTaxFields x={x}/>
          <label>Bill / receipt number<input maxLength={120} value={f.receipt} onChange={set('receipt')} placeholder="e.g. AQ-0026"/></label></>}
        {sec==='bill'&&<>
          <ExpPhotoFields x={x}/>
          <label>Notes<textarea maxLength={300} value={f.notes} onChange={set('notes')} placeholder="Optional"/></label></>}
      </FormSections></form></>;
}
/** Modal 'expense-reason': id = expense ID, room = 'reject' | 'reverse'. */
export function ExpenseReasonModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,modal,busy}=useHcc();const [reason,setReason]=useState('');
  const e=s.expenses.find(x=>x.id===id);const reverse=room==='reverse';
  const ov=useOverride(reverse?s.date:'');
  if(!e)return <p className="padded">This expense is unavailable.</p>;
  return <><div className="modal-heading"><h2>{reverse?'Reverse a posted expense':'Reject an expense'}</h2><p>{e.id} · {e.description??e.vendor} · {money(e.amount)} · {mnyIsDrawer(s,e.method)?'drawer cash':e.method}</p></div>
    <form className="form" onSubmit={ev=>{ev.preventDefault();if(!reason.trim()||ov.blocked)return;
      run(reverse?{type:'EXPENSE_REVERSE',id:e.id,reason:reason.trim(),...(ov.override?{override:ov.override}:{}),key:modal!.key}:{type:'EXPENSE_APPROVE',id:e.id,approve:false,reason:reason.trim(),key:modal!.key},reverse?'Expense reversed — a negative record was posted today':'Expense rejected',{close:true});}}>
      <label>Reason (required)<textarea required maxLength={200} value={reason} onChange={ev=>setReason(ev.target.value)}/></label>
      {reverse&&<div className="info-note"><Info size={16}/>The original stays on its date, marked Reversed. A new posted record of −{money(e.amount)} is dated {dateLabel(s.date)}{mnyIsDrawer(s,e.method)?`, returning ${money(e.amount)} to expected drawer cash`:''}. Reports count each exactly once.</div>}
      {ov.field}
      {!reason.trim()&&<p className="f-why">Enter a reason to continue.</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!reason.trim()||!!ov.blocked}>{reverse?'Reverse expense':'Reject expense'}</button></div></form></>;
}
