// DEC-120 · SCC expense features on the Expenses page (desktop) and the phone page, with HCC's approval rules kept. Shared sections
// (recurring templates, petty cash, budgets, the Udhaar register, month & tax) and dialogs (edit, void, bill photo, entry text,
// recurring template / post, Udhaar payment / write-off / reminder, month lock / unlock, Owner override). Phone: `phone` = cards and mm-form.
// SCC decisions wave (27 Oct 2026): DEC-129 budgets and templates are kept by the Owner only (others read them; posting stays with expense
// users); DEC-130 an Udhaar payment is a desk receipt (cash into the open shift's drawer); DEC-131 Owner unlock of a locked month with a
// reason; DEC-135 ITC allowed / disallowed on a bill's GST.
import React,{useState} from 'react';
import {AlertTriangle,Camera,Copy,Download,Eye,Lock,Repeat,Unlock} from 'lucide-react';
import {money,mnyCategoryNames,mnyNames,mnyIsDrawer,mnyMethod,rbacCanSubmitExpense,rbacCanDecideExpense,closingUnlimitedRoles,EXPENSE_MANAGER_LIMIT,can,cxCashBlocked,
  EXP_VOID_REASONS,EXP_MAX_PAISE,expSlice,expYm,expDescription,expGstPrefill,expGstOverMessage,expMonthLocked,expMonthLockedMessage,expFeedStale,expMonthSummary,
  expBudgetRows,expBudgetCopyPlan,expRecurringDue,expRecStatus,expRecPosted,expRecRef,expPetty,expUdOpen,expUdOut,expUdPaid,expUdOverdue,expUdBucket,expUdReminder,
  expEntryText,expLedgerCsv,expMonthCsv,expMonthLockRefusal,expCanSetMoney,EXP_OWNER_ONLY_BUDGETS,EXP_OWNER_ONLY_RECURRING,expMonthUnlockRefusal,expMonthUnlockedMessage,
  expItcRefusal,EXP_ITC_LABEL,expGstSplit,type Expense} from '../../services/mock';
import {useHcc} from '../../app/context';
import {saveCsv,saveAndReport,clearCapturedPhotos} from '../../app/native';
import {compressBillPhoto} from '../../app/slipPhoto';
import {Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {mnyExpenseSources} from '../money/masters';
import {BillPhotoProd} from '../../clients/sync/expense-ui';

export const EXP_SECTIONS=[['register','Register'],['recurring','Recurring'],['petty','Petty cash'],['budgets','Budgets'],['udhaar','Udhaar'],['month','Month & tax']] as const;
export type ExpSection=typeof EXP_SECTIONS[number][0];

/** Rupee text → paise (NaN when not a valid amount with at most 2 decimals). */
export function expPaise(v:string):number{
  const t=String(v??'').replace(/[₹,\s]/g,'');if(!t||!/^\d*(\.\d{0,2})?$/.test(t)||t==='.')return NaN;return Math.round(Number(t)*100);
}
/** Percent text → basis points (NaN when invalid; 0–100 %). */
export function expBps(v:string):number{const t=String(v??'').trim();if(!t)return 0;if(!/^\d*(\.\d{0,2})?$/.test(t))return NaN;const n=Math.round(Number(t)*100);return n<=10000?n:NaN;}
const rupees=(p:number)=>p?String(p/100):'';
const name=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??(id&&/^\d+$/.test(id)?'another user':id)??'—';
const formCls=(phone:boolean)=>phone?'form mm-form':'form';

/** The Owner's locked-month override field (SCC lockedMonthOverrideModal): '' and no field while the month is open. */
export function useOverride(date:string){
  const {s,user}=useHcc();const [reason,setReason]=useState('');
  const locked=!!date&&expMonthLocked(s,date),owner=user.role==='Owner',m=expYm(date);
  const blocked=locked&&!owner?expMonthLockedMessage(m):locked&&!reason.trim()?`Month ${m} is tax-locked: give the Owner override reason.`:'';
  const field=locked?(owner?<label>Owner override reason (tax-locked month {m})<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)}
      placeholder="required — why this late change?"/><small className="muted">Saving records an Owner override and marks the {m} tax feed stale.</small></label>
    :<p className="f-why" role="alert"><Lock size={14}/> {expMonthLockedMessage(m)}</p>):null;
  return {locked,blocked,field,override:locked?reason.trim():''};
}

/** Bill photo picker (SCC readPhoto: ≤ 4 MB, 1000 px wide, JPEG 0.7) through the slip-photo pipeline. */
export function BillPhotoPicker({value,onChange}:{value:string;onChange:(v:string)=>void}){
  const [err,setErr]=useState('');const [working,setWorking]=useState(false);
  async function pick(e:React.ChangeEvent<HTMLInputElement>){
    const f=e.target.files?.[0];e.target.value='';if(!f)return;setWorking(true);setErr('');
    try{onChange((await compressBillPhoto(f)).dataUrl);}catch(x){onChange('');setErr(x instanceof Error?x.message:'The photo could not be prepared.');}
    finally{setWorking(false);void clearCapturedPhotos();}
  }
  return <div className="exp-photo-pick"><label>Bill photo (optional)<input type="file" accept="image/*" capture="environment" onChange={e=>{void pick(e);}}/></label>
    <small className="muted">Up to 4 MB; kept 1000 px wide (JPEG).</small>
    {working&&<p className="muted" role="status">Preparing the photo…</p>}{err&&<p className="form-error" role="alert">{err}</p>}
    {value&&<div className="exp-photo-preview"><img src={value} alt="Bill photo"/><button type="button" className="text-btn" onClick={()=>onChange('')}>Remove photo</button></div>}</div>;
}

/** GST on the bill (SCC P1-17): rate (%) and amount (₹); the amount is prefilled from a GST-inclusive bill when still empty. */
export function GstFields({amount,rate,gst,onRate,onGst}:{amount:number;rate:string;gst:string;onRate:(v:string)=>void;onGst:(v:string)=>void}){
  return <div className="form-row"><label>GST rate (%)<input inputMode="decimal" value={rate} placeholder="optional" onChange={e=>onRate(e.target.value)}
      onBlur={()=>{const b=expBps(rate);if(!gst.trim()&&amount>0&&b>0)onGst(rupees(expGstPrefill(amount,b)));}}/></label>
    <label>GST on bill (₹)<input inputMode="decimal" value={gst} placeholder="optional — copy from the bill" onChange={e=>onGst(e.target.value)}/></label></div>;
}

/** DEC-135: input tax credit of the bill's GST (shown once there is GST): disallowed (the default — stays in the cost) or allowed (to the GST
 *  input-credit ledger). */
export function ItcField({gst,amount,value,onChange}:{gst:number;amount:number;value:'allowed'|'disallowed';onChange:(v:'allowed'|'disallowed')=>void}){
  if(!(gst>0))return null;const why=expItcRefusal(value,gst,amount);
  return <><label>Input tax credit (ITC) on this GST<select value={value} onChange={e=>onChange(e.target.value==='allowed'?'allowed':'disallowed')}>
      <option value="disallowed">ITC disallowed — GST stays in the expense cost</option><option value="allowed">ITC allowed — GST to the GST input-credit ledger</option></select>
    <small className="muted">Nothing is claimed unless marked allowed. Your CA decides which bills qualify.</small></label>
    {why&&<p className="f-why" role="alert">{why}</p>}</>;
}

/** Banners on top of the Expenses page: recurring nudge (SCC recurringNudge) and the business month's lock / stale state. */
export function ExpBanners({onSection}:{onSection:(s:ExpSection)=>void}){
  const {s}=useHcc();const due=expRecurringDue(s),m=expYm(s.date),f=expSlice(s).months?.[m];
  return <>{due.length>0&&<div className="info-note f-warn exp-banner" role="status"><Repeat size={16}/><span><strong>{due.length}</strong> recurring expense{due.length===1?'':'s'} not yet posted this month: {due.slice(0,4).map(t=>t.name).join(', ')}{due.length>4?'…':''}{' '}
      <button type="button" className="text-btn" onClick={()=>onSection('recurring')}>Post now</button></span></div>}
    {f&&f.unlockedAt&&<div className="info-note exp-banner f-warn" role="status"><Unlock size={16}/><span>{expMonthUnlockedMessage(f)}</span></div>}
    {f&&!f.unlockedAt&&<div className={'info-note exp-banner'+(expFeedStale(s,m)?' f-warn':'')} role="status"><Lock size={16}/><span>Month {m} is tax-locked (feed generated {f.generatedAt.replace('T',' ')}). Entries dated in it need the Owner and a reason.
      {expFeedStale(s,m)?' The ledger changed after the feed was generated — regenerate it before sharing with your CA.':''}</span></div>}</>;
}

/** View a bill photo: the demo keeps it on the record; production fetches it on demand. */
export function ExpPhotoModal({id}:{id?:string}){
  const {s,closeModal}=useHcc();const e=s.expenses.find(x=>x.id===id);
  return <><div className="modal-heading"><h2>Bill photo</h2><p>{e?`${e.id} · ${expDescription(e)} · ${money(e.amount)}`:''}</p></div>
    {!e?<Empty title="Expense not found"/>:process.env.HCC_BUILD==='production'?<BillPhotoProd id={e.id}/>
      :e.billPhoto?<img className="exp-photo-full" src={e.billPhoto} alt={`Bill photo of ${e.id}`}/>:<p className="muted">No photo stored.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
}

/** Text to copy: one entry (SCC shareEntry) or an Udhaar reminder (SCC remindReceivable, with the hotel's name). */
export function ExpTextModal({id,room}:{id?:string;room?:string}){
  const {s,closeModal,toast}=useHcc();
  const ud=room==='remind'?(expSlice(s).udhaar??[]).find(r=>r.id===id):undefined,e=room!=='remind'?s.expenses.find(x=>x.id===id):undefined;
  const text=ud?expUdReminder(s.settings.hotel,ud):e?expEntryText(e):'';
  const copy=async()=>{try{await navigator.clipboard.writeText(text);toast('Copied — paste it into WhatsApp or SMS');}catch{toast('Copy failed — select the text and copy it',true);}};
  return <><div className="modal-heading"><h2>{ud?'Payment reminder':'Entry summary'}</h2><p>{ud?`${ud.name} · outstanding ${money(expUdOut(ud))}`:'Copy and send it'}</p></div>
    {text?<textarea className="exp-text" readOnly rows={6} value={text}/>:<Empty title="Not found"/>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button><button type="button" className="btn primary" disabled={!text} onClick={()=>{void copy();}}><Copy size={16}/> Copy text</button></div></>;
}

/** SCC editEntry: amount, category, funding, GST, description, bill number, bill photo; reason required; change log. */
export function ExpEditModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const e=s.expenses.find(x=>x.id===id);
  const [f,setF]=useState(()=>({amount:e?rupees(e.amount):'',category:e?.category??'',method:e?.method??'',description:e?.description??'',rate:e?.gstRateBps?String(e.gstRateBps/100):'',
    gst:e?.gstPaise?rupees(e.gstPaise):'',receipt:e?.receipt??'',photo:'',reason:'',confirmGst:false,itc:(e?.itc==='allowed'?'allowed':'disallowed') as 'allowed'|'disallowed'}));
  const ov=useOverride(e?.date??'');
  if(!e)return <><Empty title="Expense not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const amount=expPaise(f.amount),rate=expBps(f.rate),gst=f.gst.trim()?expPaise(f.gst):0;
  const why=!(amount>0&&amount<=EXP_MAX_PAISE)?'Enter an amount greater than 0':Number.isNaN(rate)?'Enter a GST rate between 0 and 100 %.':Number.isNaN(gst)?'Enter the GST on the bill in rupees.'
    :e.status==='Pending'&&!f.description.trim()?'Description is required':!f.reason.trim()?'Reason required':gst>amount&&!f.confirmGst?expGstOverMessage(gst,amount)
    :gst>0&&expItcRefusal(f.itc,gst,amount)||ov.blocked;
  const save=()=>void run({type:'EXP_EDIT',id:e.id,amount,category:f.category,method:f.method,description:f.description.trim(),gstRateBps:rate||0,gstPaise:gst||0,receipt:f.receipt,
    ...(gst>0?{itc:f.itc}:{}),
    ...(f.photo?{billPhoto:f.photo}:{}),reason:f.reason.trim(),...(f.confirmGst?{confirmGst:true}:{}),key:'exp-edit:'+modal!.key},'Expense updated — the change is logged',{close:true});
  return <form className={formCls(display.isMobile)} onSubmit={ev=>{ev.preventDefault();if(!why)save();}}>
    <div className="modal-heading"><h2>Change {e.id}</h2><p>{e.status} · dated {dateLabel(e.date)} · every change is logged with its reason. An approved expense changes only by a reversal and a new entry.</p></div>
    <label>Amount (₹)<input inputMode="decimal" value={f.amount} onChange={x=>setF({...f,amount:x.target.value})}/></label>
    <div className="form-row"><label>Category<select value={f.category} onChange={x=>setF({...f,category:x.target.value})}>{mnyCategoryNames(s,e.category).map(c=><option key={c}>{c}</option>)}</select></label>
      <label>Funding source<select value={f.method} onChange={x=>setF({...f,method:x.target.value})}>{mnyExpenseSources(s).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label></div>
    <label>Description{e.status==='Pending'?'':' (required to submit)'}<input maxLength={300} value={f.description} onChange={x=>setF({...f,description:x.target.value})}/></label>
    <GstFields amount={amount} rate={f.rate} gst={f.gst} onRate={v=>setF({...f,rate:v})} onGst={v=>setF({...f,gst:v})}/>
    <ItcField gst={Number.isNaN(gst)?0:gst} amount={amount} value={f.itc} onChange={v=>setF({...f,itc:v})}/>
    <label>Bill / receipt number<input maxLength={120} value={f.receipt} onChange={x=>setF({...f,receipt:x.target.value})}/></label>
    {(e.billPhoto||e.hasBillPhoto)&&<small className="muted">A photo is already attached — choosing a new one replaces it.</small>}
    <BillPhotoPicker value={f.photo} onChange={v=>setF({...f,photo:v})}/>
    <label>Reason for change (required)<input maxLength={200} value={f.reason} onChange={x=>setF({...f,reason:x.target.value})}/></label>
    {gst>amount&&amount>0&&<label className="checkbox-line"><input type="checkbox" checked={f.confirmGst} onChange={x=>setF({...f,confirmGst:x.target.checked})}/> {expGstOverMessage(gst,amount)}</label>}
    {ov.locked&&<p className="muted">The reason above is recorded as the Owner override.</p>}
    {why&&<p className="f-why" aria-live="polite">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!why}>Save change</button></div>
  </form>;
}

/** SCC voidEntry: one of six reasons + note; a tax-locked month needs the Owner and a typed note. */
export function ExpVoidModal({id}:{id?:string}){
  const {s,user,run,busy,closeModal,modal,display}=useHcc();const e=s.expenses.find(x=>x.id===id);
  const [reason,setReason]=useState<string>(EXP_VOID_REASONS[0]);const [note,setNote]=useState('');
  if(!e)return <><Empty title="Expense not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const locked=expMonthLocked(s,e.date),why=locked&&user.role!=='Owner'?expMonthLockedMessage(expYm(e.date)):locked&&!note.trim()?'Month is tax-locked — add a specific note for the override':'';
  return <form className={formCls(display.isMobile)} onSubmit={ev=>{ev.preventDefault();if(why)return;
    void run({type:'EXP_VOID',id:e.id,reason,note:note.trim()||undefined,key:'exp-void:'+modal!.key},'Entry voided (kept for audit)',{close:true});}}>
    <div className="modal-heading"><h2>Void {e.id}</h2><p>{expDescription(e)} · {money(e.amount)} · the record stays for audit and leaves every total.</p></div>
    <label>Reason<select value={reason} onChange={x=>setReason(x.target.value)}>{EXP_VOID_REASONS.map(r=><option key={r}>{r}</option>)}</select></label>
    <label>Note{locked?' (required — tax-locked month)':' (optional detail)'}<input maxLength={200} value={note} onChange={x=>setNote(x.target.value)}/></label>
    {why&&<p className="f-why">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!!why}>Void entry</button></div>
  </form>;
}

/** Owner override for a decision or a submission dated in a tax-locked month (room = approve | reverse | submit). */
export function ExpOverrideModal({id,room}:{id?:string;room?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const e=s.expenses.find(x=>x.id===id);
  const date=room==='reverse'?s.date:e?.date??'',ov=useOverride(date);const [reason,setReason]=useState('');
  if(!e)return <><Empty title="Expense not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const why=ov.blocked||(room==='reverse'&&!reason.trim()?'A reason is required to reverse an expense.':'');
  const cmd=room==='reverse'?{type:'EXPENSE_REVERSE',id:e.id,reason:reason.trim()}:room==='submit'?{type:'EXPENSE_SUBMIT',id:e.id}:{type:'EXPENSE_APPROVE',id:e.id,approve:true};
  return <form className={formCls(display.isMobile)} onSubmit={ev=>{ev.preventDefault();if(why)return;
    void run({...cmd,override:ov.override,key:'exp-override:'+modal!.key},room==='reverse'?'Expense reversed':room==='submit'?'Draft submitted for approval':'Expense approved and posted',{close:true});}}>
    <div className="modal-heading"><h2>Tax-locked month — Owner override</h2><p>{e.id} · {expDescription(e)} · {money(e.amount)}</p></div>
    {room==='reverse'&&<label>Reason for the reversal (required)<input maxLength={200} value={reason} onChange={x=>setReason(x.target.value)}/></label>}
    {ov.field}
    {why&&<p className="f-why">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn primary" disabled={busy||!!why}>Confirm</button></div>
  </form>;
}

// ── Recurring templates (SCC P1-16) ──
export function ExpRecModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const t=(expSlice(s).recurring??[]).find(x=>x.id===id);
  const [f,setF]=useState(()=>({name:t?.name??'',amount:t?rupees(t.amount):'',dueDay:String(t?.dueDay??1),category:t?.category??(mnyCategoryNames(s).includes('Utilities')?'Utilities':mnyCategoryNames(s)[0]??''),
    method:t?.method??mnyNames(s,'expenses')[0]??'',vendor:t?.vendor??'',again:false}));
  const amount=expPaise(f.amount),day=Math.floor(Number(f.dueDay));
  const dup=!t&&(expSlice(s).recurring??[]).some(x=>x.name.toLowerCase()===f.name.trim().toLowerCase());
  const why=!f.name.trim()?'Template name is required':!(amount>0)?'Enter a typical amount greater than 0':!(day>=1&&day<=31)?'Due day must be between 1 and 31'
    :dup&&!f.again?`A template named "${f.name.trim()}" already exists. Add another?`:'';
  return <form className={formCls(display.isMobile)} onSubmit={ev=>{ev.preventDefault();if(why)return;
    void run({type:'EXP_REC_SAVE',...(t?{id:t.id}:{}),name:f.name.trim(),amount,dueDay:day,category:f.category,method:f.method,vendor:f.vendor.trim(),...(f.again?{confirmDuplicate:true}:{}),
      key:'exp-rec:'+modal!.key},t?'Template updated':'Recurring template added',{close:true});}}>
    <div className="modal-heading"><h2>{t?`Edit template — ${t.name}`:'Recurring expense template'}</h2><p>Rent, electricity, insurance — posting raises this month’s draft (amount editable), which goes for approval. Salaries stay in Payroll.</p></div>
    <label>Name<input maxLength={60} value={f.name} onChange={x=>setF({...f,name:x.target.value})} placeholder="e.g. Shop rent"/></label>
    <div className="form-row"><label>Typical amount (₹)<input inputMode="decimal" value={f.amount} onChange={x=>setF({...f,amount:x.target.value})}/></label>
      <label>Due day of month (1–31)<input inputMode="numeric" value={f.dueDay} onChange={x=>setF({...f,dueDay:x.target.value})}/></label></div>
    <div className="form-row"><label>Category<select value={f.category} onChange={x=>setF({...f,category:x.target.value})}>{mnyCategoryNames(s,f.category).map(c=><option key={c}>{c}</option>)}</select></label>
      <label>Payment mode<select value={f.method} onChange={x=>setF({...f,method:x.target.value})}>{mnyExpenseSources(s).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label></div>
    <label>Vendor / payee (optional)<input maxLength={60} value={f.vendor} onChange={x=>setF({...f,vendor:x.target.value})}/></label>
    {dup&&<label className="checkbox-line"><input type="checkbox" checked={f.again} onChange={x=>setF({...f,again:x.target.checked})}/> A template with this name exists — add another</label>}
    {why&&<p className="f-why">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!why}>{t?'Save template':'Add template'}</button></div>
  </form>;
}
export function ExpRecPostModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const t=(expSlice(s).recurring??[]).find(x=>x.id===id);
  const [amount,setAmount]=useState(t?rupees(t.amount):'');const [again,setAgain]=useState(false);const ov=useOverride(s.date);
  if(!t)return <><Empty title="Template not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const m=expYm(s.date),ref=expRecRef(t.id,m),already=s.expenses.filter(e=>e.sourceRef===ref&&!e.reversalOf&&!['Void','Rejected','Reversed'].includes(e.status));
  const p=expPaise(amount),why=!(p>0)?'Enter an amount greater than 0':already.length&&!again?'Already posted this month — tick to post another entry.':ov.blocked;
  return <form className={formCls(display.isMobile)} onSubmit={ev=>{ev.preventDefault();if(why)return;
    void run({type:'EXP_REC_POST',id:t.id,amount:p,...(again?{again:true}:{}),...(ov.override?{override:ov.override}:{}),key:'exp-rec-post:'+modal!.key},
      `Draft for ${t.name} saved — submit it for approval`,{close:true});}}>
    <div className="modal-heading"><h2>Post recurring — {t.name}</h2><p>{t.category} · {t.method}{t.vendor?' · '+t.vendor:''} · raises a draft dated {dateLabel(s.date)}, which you submit for approval.</p></div>
    {already.length>0&&<div className="info-note f-warn"><AlertTriangle size={16}/><span>Already posted for {m} ({already.length} entr{already.length===1?'y':'ies'} · {money(already.reduce((a,e)=>a+e.amount,0))}). Confirming will post ANOTHER entry for this month.</span></div>}
    <label>Amount for this month (₹)<input inputMode="decimal" value={amount} onChange={x=>setAmount(x.target.value)}/></label>
    {already.length>0&&<label className="checkbox-line"><input type="checkbox" checked={again} onChange={x=>setAgain(x.target.checked)}/> Post another entry for {m}</label>}
    {ov.field}{why&&<p className="f-why">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!why}>Raise draft</button></div>
  </form>;
}
export function ExpRecurring({phone=false}:{phone?:boolean}){
  const {s,uid,open,run,busy}=useHcc();const list=expSlice(s).recurring??[],manage=expCanSetMoney(s,uid),post=rbacCanSubmitExpense(s,uid); // DEC-129
  const pill=(k:string,n:number,day:number)=>k==='inactive'?<Pill>inactive</Pill>:k==='posted'?<Pill tone="green">posted{n>1?` ×${n}`:''}</Pill>:k==='due'?<Pill tone="orange">due — not posted</Pill>:<span className="muted">upcoming · day {day}</span>;
  return <section className={phone?'mm-card':'card'} aria-label="Recurring expenses">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Recurring expenses</h2>{manage&&<button type="button" className="btn small-btn" onClick={()=>open({type:'exp-rec'})}>+ Template</button>}</div>
    <p className="muted padded">One tap raises this month’s draft (amount editable) — it goes for approval like any expense. A template is due on its day, or on the month’s last day when the month is shorter.</p>
    {!list.length?<Empty title="No recurring templates yet" body="Add rent, electricity, insurance once — then post each month with one tap."/>
      :<div className={phone?'exp-cards':'exp-list padded'}>{list.map(t=>{const st=expRecStatus(s,t);
        return <article key={t.id} className="exp-item"><div><strong>{t.name}</strong><small>{t.category} · {t.method}{t.vendor?' · '+t.vendor:''} · typical {money(t.amount)} · due day {t.dueDay}</small></div>
          <div className="exp-item-actions">{pill(st.kind,st.count,st.day)}
            {t.active&&post&&<button type="button" className="btn small-btn" disabled={busy} onClick={()=>open({type:'exp-rec-post',id:t.id})}>{st.count?'Post again':'Post now'}</button>}
            {manage&&<><button type="button" className="text-btn" onClick={()=>open({type:'exp-rec',id:t.id})}>Edit</button>
              <button type="button" className="text-btn" disabled={busy} onClick={()=>void run({type:'EXP_REC_TOGGLE',id:t.id,key:`exp-rec-toggle:${t.id}:${s.sequence}`},t.active?`${t.name} deactivated`:`${t.name} activated`)}>{t.active?'Deactivate':'Activate'}</button>
              <button type="button" className="text-btn danger-text" disabled={busy} onClick={()=>{if(globalThis.confirm?.(`Delete template "${t.name}"? Ledger entries already posted from it are kept.`)!==false)
                void run({type:'EXP_REC_DELETE',id:t.id,key:`exp-rec-del:${t.id}`},'Template deleted');}}>Delete</button></>}
          </div></article>;})}</div>}
    {!manage&&<p className="muted padded">{EXP_OWNER_ONLY_RECURRING}</p>}
  </section>;
}

// ── Petty cash (SCC renderPetty through HCC's approval and cash rules) ──
export function ExpPetty({phone=false}:{phone?:boolean}){
  const {s,uid,user,run,busy,modal}=useHcc();const p=expPetty(s),entries=(expSlice(s).petty??[]).slice().reverse();
  const canDecide=rbacCanDecideExpense(s,uid),canEnter=rbacCanSubmitExpense(s,uid);
  const [top,setTop]=useState('');const [note,setNote]=useState('');const [out,setOut]=useState({amount:'',category:mnyCategoryNames(s).includes('Petty cash')?'Petty cash':mnyCategoryNames(s)[0]??'',what:'',receipt:''});
  const ov=useOverride(s.date);const k=modal?.key??'page';
  const topP=expPaise(top),outP=expPaise(out.amount),cash=mnyNames(s,'expenses').find(n=>mnyIsDrawer(s,n));
  const limit=(a:number)=>!closingUnlimitedRoles.includes(user.role)&&a>EXPENSE_MANAGER_LIMIT;
  return <section className={phone?'mm-card':'card'} aria-label="Petty cash">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Petty cash float</h2><Pill tone={p.status==='Healthy'?'green':'orange'}>{p.status}</Pill></div>
    <div className="small-stats exp-kpis"><div><span>Float set</span><strong>{money(p.float)}</strong></div><div><span>Disbursed (approved)</span><strong>{money(p.disbursed)}</strong></div>
      <div><span>Balance</span><strong>{money(p.balance)}</strong></div><div><span>Awaiting approval</span><strong>{money(p.pending)}</strong></div></div>
    <p className="muted padded">A top-up raises the float once a second person approves it (the Manager up to {money(EXPENSE_MANAGER_LIMIT)}). A disbursement is a drawer-cash expense that goes for approval and leaves the drawer when approved. “Replenish soon” below 15 % of the float.</p>
    {canEnter&&<div className={phone?'mm-form':'form-row padded exp-forms'}>
      <form className="form" onSubmit={e=>{e.preventDefault();if(!(topP>0))return;void run({type:'EXP_PETTY_FLOAT',amount:topP,note:note.trim()||undefined,key:'exp-petty-float:'+k+':'+s.sequence},'Top-up recorded — a second person approves it');setTop('');setNote('');}}>
        <h3>Set / top-up float</h3><label>Amount (₹)<input inputMode="decimal" value={top} onChange={e=>setTop(e.target.value)} placeholder="e.g. 5000"/></label>
        <label>Note (optional)<input maxLength={200} value={note} onChange={e=>setNote(e.target.value)}/></label>
        <button className="btn" disabled={busy||!(topP>0)}>Request top-up</button></form>
      <form className="form" onSubmit={e=>{e.preventDefault();if(!(outP>0)||ov.blocked||!cash)return;
        void run({type:'EXP_PETTY_OUT',amount:outP,category:out.category,for:out.what.trim(),receipt:out.receipt.trim()||undefined,...(ov.override?{override:ov.override}:{}),key:'exp-petty-out:'+k+':'+s.sequence},
          'Petty disbursement recorded — it leaves the drawer once approved');setOut({...out,amount:'',what:'',receipt:''});}}>
        <h3>Quick petty disbursement</h3><label>Amount (₹)<input inputMode="decimal" value={out.amount} onChange={e=>setOut({...out,amount:e.target.value})}/></label>
        <label>Category<select value={out.category} onChange={e=>setOut({...out,category:e.target.value})}>{mnyCategoryNames(s).map(c=><option key={c}>{c}</option>)}</select></label>
        <label>For<input maxLength={120} value={out.what} onChange={e=>setOut({...out,what:e.target.value})} placeholder="tea / auto / sundry"/></label>
        <label>Bill / receipt number (if the category needs one)<input maxLength={120} value={out.receipt} onChange={e=>setOut({...out,receipt:e.target.value})}/></label>
        {ov.field}{!cash&&<p className="f-why">No drawer-cash funding source is set up for expenses.</p>}
        <button className="btn" disabled={busy||!(outP>0)||!!ov.blocked||!cash}>Record disbursement</button></form></div>}
    <h3 className="padded">Top-ups</h3>
    {!entries.length?<p className="muted padded">No top-ups yet.</p>:<div className={phone?'exp-cards':'exp-list padded'}>{entries.map(e=>
      <article key={e.id} className="exp-item"><div><strong>{money(e.amount)}</strong><small>{e.id} · {dateLabel(e.date)} · by {name(s,e.by)}{e.note?' · '+e.note:''}{e.decidedBy?` · ${e.status.toLowerCase()} by ${name(s,e.decidedBy)}`:''}{e.reason?' · '+e.reason:''}</small></div>
        <div className="exp-item-actions"><Pill tone={e.status==='Approved'?'green':e.status==='Pending'?'orange':'neutral'}>{e.status}</Pill>
          {e.status==='Pending'&&canDecide&&(e.by===uid?<span className="f-why">Your request — another approver needed</span>:<>
            <button type="button" className="btn small-btn" disabled={busy||limit(e.amount)} title={limit(e.amount)?`Above ${money(EXPENSE_MANAGER_LIMIT)} — Owner approves`:undefined}
              onClick={()=>void run({type:'EXP_PETTY_FLOAT_DECIDE',id:e.id,approve:true,key:'exp-petty-decide:'+e.id},'Top-up approved — float raised')}>Approve</button>
            <button type="button" className="text-btn" disabled={busy} onClick={()=>void run({type:'EXP_PETTY_FLOAT_DECIDE',id:e.id,approve:false,key:'exp-petty-decide:'+e.id},'Top-up rejected')}>Reject</button></>)}</div></article>)}</div>}
  </section>;
}

// ── Budgets (SCC renderBudget / setBudget / copyBudgets) ──
export function ExpBudgets({phone=false}:{phone?:boolean}){
  const {s,uid,run,busy}=useHcc();const m=expYm(s.date),rows=expBudgetRows(s,m,mnyCategoryNames(s)),edit=expCanSetMoney(s,uid); // DEC-129
  const [draft,setDraft]=useState<Record<string,string>>({});const plan=expBudgetCopyPlan(s,m);
  const set=(c:string)=>{const v=draft[c];if(v===undefined)return;const p=v.trim()?expPaise(v):0;if(Number.isNaN(p))return;
    void run({type:'EXP_BUDGET_SET',category:c,amount:p,key:`exp-budget:${m}:${c}:${s.sequence}`},`${c} budget for ${m} = ${money(p)}`);const d={...draft};delete d[c];setDraft(d);};
  const tone=(st:string)=>st==='over'?'red':st==='near'?'orange':st==='ok'?'green':'neutral';
  const label=(r:typeof rows[number])=>r.status==='none'?'no budget':r.status==='over'?`over ${money(r.actual-r.budget)}`:r.status==='near'?'near limit':'ok';
  return <section className={phone?'mm-card':'card'} aria-label="Monthly budgets">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Monthly budgets — {m}</h2>
      {edit&&<button type="button" className="btn small-btn" disabled={busy||!!plan.message} title={plan.message} onClick={()=>{if(globalThis.confirm?.(`Copy ${plan.heads.length} budget head${plan.heads.length>1?'s':''} from ${plan.prev} into ${m}? Heads that already have a value are left unchanged.`)!==false)
        void run({type:'EXP_BUDGET_COPY',key:`exp-budget-copy:${m}:${s.sequence}`},`Copied ${plan.heads.length} budget head${plan.heads.length>1?'s':''} from ${plan.prev}`);}}><Download size={15}/> Copy last month’s budgets</button>}</div>
    <p className="muted padded">A cap per category for the business month; actuals are the approved expenses. “Near limit” from 85 %, “over” above 100 %; an entry warns at 90 % and 100 % of the budget.</p>
    {phone?<div className="exp-cards">{rows.map(r=><article key={r.category} className="exp-item"><div><strong>{r.category}</strong><small>Actual {money(r.actual)}{r.budget?` · ${r.pct}% of ${money(r.budget)}`:''}</small>
        {edit&&<label className="exp-inline">Budget (₹)<input inputMode="decimal" value={draft[r.category]??rupees(r.budget)} onChange={e=>setDraft({...draft,[r.category]:e.target.value})} onBlur={()=>set(r.category)}/></label>}</div>
        <Pill tone={tone(r.status)}>{label(r)}</Pill></article>)}</div>
      :<div className="table-wrap"><table><thead><tr><th>Category</th><th className="number">Budget</th><th className="number">Actual</th><th className="number">Used</th><th>Status</th></tr></thead><tbody>
        {rows.map(r=><tr key={r.category}><td>{r.category}</td>
          <td className="number">{edit?<input className="exp-budget-input" aria-label={`${r.category} budget`} inputMode="decimal" value={draft[r.category]??rupees(r.budget)}
            onChange={e=>setDraft({...draft,[r.category]:e.target.value})} onBlur={()=>set(r.category)} onKeyDown={e=>{if(e.key==='Enter')set(r.category);}}/>:money(r.budget)}</td>
          <td className="number">{money(r.actual)}</td><td className="number">{r.budget?`${r.pct}%`:'—'}<div className="exp-gauge" aria-hidden><span className={r.pct>100?'over':''} style={{width:Math.min(r.pct,100)+'%'}}/></div></td>
          <td><Pill tone={tone(r.status)}>{label(r)}</Pill></td></tr>)}</tbody></table></div>}
    {!edit&&<p className="muted padded">{EXP_OWNER_ONLY_BUDGETS}</p>}
  </section>;
}

// ── Udhaar credit register (SCC Wave 3) ──
/** DEC-130: why this user cannot take an Udhaar payment in `mode` now ('' = can): cash follows the drawer rules of every desk receipt. */
export function expUdCashBlock(s:ReturnType<typeof useHcc>['s'],uid:string,mode:string):string{
  if(!mnyIsDrawer(s,mode))return '';
  if(!can(s,uid,'payments'))return 'Udhaar cash goes into the drawer: the cashier (or a desk user with an open shift) takes it — or record it by UPI / card / bank.';
  if(!s.shiftOpen)return 'Open cash shift required: Udhaar cash goes into the open shift’s drawer. Open a cashier shift first, or take it by UPI / card / bank.';
  return cxCashBlocked(s,uid)??'';
}
export function ExpUdPayModal({id}:{id?:string}){
  const {s,uid,run,busy,closeModal,modal,display}=useHcc();const r=(expSlice(s).udhaar??[]).find(x=>x.id===id);
  const [amount,setAmount]=useState(r?rupees(expUdOut(r)):'');const [mode,setMode]=useState(mnyNames(s,'frontdesk')[0]??'Cash');const [ref,setRef]=useState('');
  if(!r)return <><Empty title="Credit not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const p=expPaise(amount),out=expUdOut(r),cash=mnyIsDrawer(s,mode),needsRef=!cash&&!!mnyMethod(s,mode)?.needsReference;
  const why=expUdCashBlock(s,uid,mode)||(needsRef&&!ref.trim()?`Enter the ${mode} transaction reference.`:'');
  return <form className={formCls(display.isMobile)} onSubmit={e=>{e.preventDefault();if(!(p>0)||why)return;
    void run({type:'EXP_UD_PAY',id:r.id,amount:p,mode,...(ref.trim()?{reference:ref.trim()}:{}),key:'exp-ud-pay:'+modal!.key},
      (p>out+50?'More than outstanding — capped · ':'Payment recorded · ')+(cash?'cash added to the drawer':`${mode} desk receipt`),{close:true});}}>
    <div className="modal-heading"><h2>Record payment — {r.name}</h2><p>Outstanding {money(out)}. Taken as a desk receipt: cash goes into the open shift’s drawer, UPI / card / bank to their own ledgers. In the books it settles this credit — it is not counted as income again.</p></div>
    <label>Amount received (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
    <label>Mode<select value={mode} onChange={e=>setMode(e.target.value)}>{mnyNames(s,'frontdesk').map(m=><option key={m}>{m}</option>)}</select></label>
    {cash&&!why&&<p className="info-note exp-ud-drawer" role="status">This cash goes into your drawer: it is in the expected cash you count at shift close, the day close and the cash book.</p>}
    {!cash&&<label>Transaction reference{needsRef?' (required)':' (optional)'}<input maxLength={120} value={ref} onChange={e=>setRef(e.target.value)} placeholder="e.g. UPI ref no."/></label>}
    {p>out+50&&<p className="f-why">More than outstanding — it will be capped to {money(out)}.</p>}
    {why&&<p className="f-why" role="alert">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!(p>0)||!!why}>Record payment</button></div>
  </form>;
}
export function ExpUdVoidModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const r=(expSlice(s).udhaar??[]).find(x=>x.id===id);const [reason,setReason]=useState('');
  if(!r)return <><Empty title="Credit not found"/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  return <form className={formCls(display.isMobile)} onSubmit={e=>{e.preventDefault();void run({type:'EXP_UD_VOID',id:r.id,reason:reason.trim()||undefined,key:'exp-ud-void:'+modal!.key},'Removed from outstanding',{close:true});}}>
    <div className="modal-heading"><h2>Remove / write off — {r.name}</h2><p>Outstanding {money(expUdOut(r))}. This hides the credit from the outstanding list (kept for audit).</p></div>
    <label>Reason<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. written off, entered in error"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy}>Write off</button></div>
  </form>;
}
export function ExpUdhaar({phone=false}:{phone?:boolean}){
  const {s,uid,open,run,busy,modal}=useHcc();const {open:list,total,buckets}=expUdOpen(s),enter=rbacCanSubmitExpense(s,uid),writeOff=rbacCanDecideExpense(s,uid);
  const [f,setF]=useState({name:'',mobile:'',amount:'',due:'',note:''});const p=expPaise(f.amount);const k=modal?.key??'page';
  return <section className={phone?'mm-card':'card'} aria-label="Udhaar credit register">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Udhaar / credit</h2><span className="muted">Outstanding {money(total)}</span></div>
    <p className="muted padded">Goods or services given on trust. Recording a credit adds nothing to the drawer or to income; a payment is taken as a desk receipt — cash into the open shift’s drawer, other modes to their ledgers — and settles the credit in the books.</p>
    <div className="exp-buckets padded"><Pill>0–30 days · {money(buckets['0–30'])}</Pill><Pill tone="orange">31–60 · {money(buckets['31–60'])}</Pill><Pill tone={buckets['60+']>0?'red':'neutral'}>60+ · {money(buckets['60+'])}</Pill></div>
    {enter&&<form className={phone?'mm-form':'form padded exp-ud-form'} onSubmit={e=>{e.preventDefault();if(!f.name.trim()||!(p>0))return;
      void run({type:'EXP_UD_ADD',name:f.name.trim(),mobile:f.mobile,amount:p,dueDate:f.due||undefined,note:f.note.trim(),key:'exp-ud-add:'+k+':'+s.sequence},'Credit recorded');setF({name:'',mobile:'',amount:'',due:'',note:''});}}>
      <h3>Record a credit</h3>
      <div className="form-row"><label>Customer name<input maxLength={80} value={f.name} onChange={e=>setF({...f,name:e.target.value})}/></label>
        <label>Mobile<input type="tel" inputMode="numeric" maxLength={14} value={f.mobile} onChange={e=>setF({...f,mobile:e.target.value})} placeholder="10-digit"/></label></div>
      <div className="form-row"><label>Amount (₹)<input inputMode="decimal" value={f.amount} onChange={e=>setF({...f,amount:e.target.value})}/></label>
        <label>Due date (default +30 days)<input type="date" value={f.due} onChange={e=>setF({...f,due:e.target.value})}/></label></div>
      <label>Note<input maxLength={200} value={f.note} onChange={e=>setF({...f,note:e.target.value})}/></label>
      <button className="btn" disabled={busy||!f.name.trim()||!(p>0)}>Add credit</button></form>}
    <h3 className="padded">Who owes you · oldest first · {list.length} open</h3>
    {!list.length?<Empty title="No outstanding credit" body="Everyone has paid up."/>:<div className={phone?'exp-cards':'exp-list padded'}>{list.map(r=>
      <article key={r.id} className="exp-item"><div><strong>{r.name}</strong><small>{r.mobile?r.mobile+' · ':''}since {shortDate(r.date)} ({expUdBucket(r,s.date)}){r.dueDate?` · due ${shortDate(r.dueDate)}`:''}{r.note?' · '+r.note:''}</small>
        <small>Amount {money(r.amount)} · paid {money(expUdPaid(r))} · <strong>outstanding {money(expUdOut(r))}</strong></small></div>
        <div className="exp-item-actions">{expUdOverdue(r,s.date)&&<Pill tone="red">overdue</Pill>}
          {enter&&<button type="button" className="btn small-btn" disabled={busy} onClick={()=>open({type:'exp-ud-pay',id:r.id})}>Pay</button>}
          <button type="button" className="text-btn" onClick={()=>open({type:'exp-text',id:r.id,room:'remind'})}>Remind</button>
          {writeOff&&<button type="button" className="text-btn danger-text" disabled={busy} onClick={()=>open({type:'exp-ud-void',id:r.id})}>Write off</button>}</div></article>)}</div>}
  </section>;
}

// ── Month & tax (SCC renderClose / genTaxFeed / exportMonthCSV) ──
export function ExpMonthLockModal({room}:{room?:string}){
  const {s,run,busy,closeModal,modal,display}=useHcc();const m=room??expYm(s.date),why=expMonthLockRefusal(s,m),sum=expMonthSummary(s,m),f=expSlice(s).months?.[m];
  return <form className={formCls(display.isMobile)} onSubmit={e=>{e.preventDefault();if(why)return;void run({type:'EXP_MONTH_LOCK',month:m,key:'exp-month-lock:'+modal!.key},'Tax feed generated & month locked',{close:true});}}>
    <div className="modal-heading"><h2>{f?'Regenerate':'Generate & lock'} the {m} feed</h2><p>Expense {money(sum.expense)} · {sum.count} entries · GST paid on purchases {money(sum.gstPaid)}.</p></div>
    <p className="muted">After this, any entry dated in {m} needs the Owner and a reason, and marks the feed stale. The Owner can unlock the month with a reason (DEC-131) and lock it again after the changes.</p>
    {why&&<p className="f-why">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!why}><Lock size={16}/> {f?'Regenerate feed':'Generate & lock'}</button></div>
  </form>;
}
/** DEC-131: the Owner unlocks a tax-locked month with a reason (audited); the figures turn stale until the month is locked again. */
export function ExpMonthUnlockModal({room}:{room?:string}){
  const {s,user,run,busy,closeModal,modal,display}=useHcc();const m=room??expYm(s.date);const [reason,setReason]=useState('');
  const refusal=user.role!=='Owner'?{message:'Only the Owner can unlock a tax-locked month.'}:expMonthUnlockRefusal(s,m,reason),why=refusal?.message??'';
  return <form className={formCls(display.isMobile)} onSubmit={e=>{e.preventDefault();if(why)return;
    void run({type:'EXP_MONTH_UNLOCK',month:m,reason:reason.trim(),key:'exp-month-unlock:'+modal!.key},`Month ${m} unlocked — lock it again after the changes`,{close:true});}}>
    <div className="modal-heading"><h2>Unlock {m}</h2><p>Entries dated in {m} will no longer need the Owner override. The month’s figures are marked stale; lock it again (regenerating the feed) when the changes are done.</p></div>
    <label>Reason (required, kept in the audit trail)<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. CA found a missed bill"/></label>
    {why&&<p className="f-why" aria-live="polite">{why}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn danger" disabled={busy||!!why}><Unlock size={16}/> Unlock month</button></div>
  </form>;
}
/** CSV export (SCC ledger / month CSV): recorded (EXP_CSV); the demo saves the file built here, production saves the server's file. */
export function useExpCsv(){
  const {s,run,toast}=useHcc();
  return (kind:'ledger'|'month',month?:string)=>{
    const f=kind==='ledger'?expLedgerCsv(s):expMonthCsv(s,month??expYm(s.date));
    void run({type:'EXP_CSV',kind,...(kind==='month'?{month:month??expYm(s.date)}:{})},`${f.name} saved`,
      {after:()=>{if(process.env.HCC_BUILD!=='production')void saveAndReport(saveCsv(f.name,f.csv),toast,`${f.name} saved`,'the CSV');}});
  };
}
export function ExpMonth({phone=false}:{phone?:boolean}){
  const {s,uid,user,open}=useHcc();const [m,setM]=useState(expYm(s.date));const sum=expMonthSummary(s,m),f=expSlice(s).months?.[m],stale=expFeedStale(s,m),csv=useExpCsv();
  const exportOk=rbacCanDecideExpense(s,uid),split=expGstSplit(s,m);
  return <section className={phone?'mm-card':'card'} aria-label="Month and tax">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Month close &amp; tax feed</h2>
      <label className="exp-inline">Month<input type="month" value={m} max={expYm(s.date)} onChange={e=>setM(e.target.value||expYm(s.date))}/></label></div>
    <div className="small-stats exp-kpis"><div><span>Expense (approved)</span><strong>{money(sum.expense)}</strong></div><div><span>Entries</span><strong>{sum.count}</strong></div>
      <div><span>GST paid on purchases</span><strong>{money(sum.gstPaid)}</strong></div>
      <div><span>ITC allowed</span><strong>{money(split.allowed)}</strong></div><div><span>ITC disallowed (in the cost)</span><strong>{money(split.disallowed)}</strong></div></div>
    <p className="muted padded">GST paid on purchases is from the bills; the part marked ITC allowed goes to the GST input-credit ledger in Accounting, the rest stays in the expense cost — your CA decides what can be claimed. Income and its GST come from folios and the restaurant (Accounting), not from this register.</p>
    {!!Object.keys(sum.byCategory).length&&(phone?<div className="exp-cards">{Object.keys(sum.byCategory).sort().map(c=><article key={c} className="exp-item"><span>− {c}</span><strong>{money(sum.byCategory[c])}</strong></article>)}</div>
      :<div className="table-wrap"><table><thead><tr><th>Head</th><th className="number">Amount</th></tr></thead><tbody>{Object.keys(sum.byCategory).sort().map(c=><tr key={c}><td>− {c}</td><td className="number">{money(sum.byCategory[c])}</td></tr>)}</tbody></table></div>)}
    {f&&<div className={'info-note padded'+(stale?' f-warn':'')}>{f.unlockedAt?<Unlock size={16}/>:<Lock size={16}/>}<span>Feed generated {f.generatedAt.replace('T',' ')} by {name(s,f.by)} — expense {money(f.expense)}, GST paid {money(f.gstPaid)}{f.gstItc!==undefined?` (ITC allowed ${money(f.gstItc)})`:''}.
      {f.unlockedAt?' '+expMonthUnlockedMessage(f):stale?` Ledger changed after the tax feed was generated — regenerate the feed for ${m} before sharing with your CA.`:''}</span></div>}
    {!!f?.unlocks?.length&&<p className="muted padded">Unlocked {f.unlocks.length}×: {f.unlocks.map(u=>`${u.at.replace('T',' ')} by ${name(s,u.by)} — ${u.reason}`).join(' · ')}</p>}
    <div className="padded exp-item-actions">{user.role==='Owner'?<><button type="button" className="btn" onClick={()=>open({type:'exp-month-lock',room:m})}><Lock size={16}/> {f&&!f.unlockedAt?'Regenerate tax feed':`${f?'Lock again':'Generate & lock tax feed'} for ${m}`}</button>
        {f&&!f.unlockedAt&&<button type="button" className="btn" onClick={()=>open({type:'exp-month-unlock',room:m})}><Unlock size={16}/> Unlock month…</button>}</>
      :<span className="muted">Only the Owner can lock or unlock the month / generate the tax feed.</span>}
      {exportOk&&<button type="button" className="btn" onClick={()=>csv('month',m)}><Download size={16}/> Month CSV</button>}</div>
  </section>;
}

/** Register row extras (SCC ledger table): description, GST, photo, source, void, edit log. */
export function ExpRowExtras({e}:{e:Expense}){
  const {open}=useHcc();
  return <>{e.description&&e.vendor&&<small>{e.vendor}</small>}
    {(e.gstPaise||e.gstRateBps)&&<small>GST {e.gstRateBps?e.gstRateBps/100+' % · ':''}{money(e.gstPaise??0)}{e.gstPaise?' · '+EXP_ITC_LABEL[e.itc==='allowed'?'allowed':'disallowed']:''}</small>}
    {e.source&&<small><Pill>{e.source==='petty'?'petty cash':'recurring'}</Pill></small>}
    {e.status==='Void'&&<small>Void · {e.voidReason}</small>}
    {(e.billPhoto||e.hasBillPhoto)&&<button type="button" className="text-btn" onClick={()=>open({type:'exp-photo',id:e.id})}><Eye size={13}/> Bill photo</button>}
    {!!e.editLog?.length&&<small>Changed {e.editLog.length}× · last: {e.editLog.at(-1)!.reason}</small>}</>;
}
/** Register actions added by DEC-120: edit (draft / pending), void (draft / pending), copy summary. */
export function ExpRowActions({e}:{e:Expense}){
  const {s,uid,open,busy}=useHcc();
  if(e.reversalOf||!['Draft','Pending'].includes(e.status))return <button type="button" className="text-btn" onClick={()=>open({type:'exp-text',id:e.id})}><Copy size={13}/> Text</button>;
  const own=e.actor===uid,decider=rbacCanDecideExpense(s,uid);
  const canEdit=e.status==='Draft'?own:own&&rbacCanSubmitExpense(s,uid)||decider,canVoid=e.status==='Draft'?own:decider;
  return <span className="inline">{canEdit&&<button type="button" className="text-btn" disabled={busy} onClick={()=>open({type:'exp-edit',id:e.id})}>Edit…</button>}
    {canVoid&&<button type="button" className="text-btn danger-text" disabled={busy} onClick={()=>open({type:'exp-void',id:e.id})}>Void…</button>}
    <button type="button" className="text-btn" onClick={()=>open({type:'exp-text',id:e.id})}><Copy size={13}/> Text</button></span>;
}
/** Camera button label used by the add forms. */
export const ExpCameraIcon=Camera;
