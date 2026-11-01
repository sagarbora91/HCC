// ACC-2 · acc2-ui — Expenses › Vendor payments (contract §3.2 / §3.5): open bills per vendor, the allocation editor (oldest bill first by default, the rest an
// advance), ageing buckets 0–30 / 31–60 / 61–90 / 90+, payments with void (Owner), vendor returns (Manager requests, Owner approves). Desktop table / phone cards.
// Screens only: the allocation default, the ageing and the postings are the purchase lane's (EXP_VENDOR_PAY / _VOID / _RETURN).
import React,{useMemo,useState} from 'react';
import {Download,Banknote,Ban,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,mnyIsDrawer,rbacCanExportVouchers} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate} from '../../components/ui';
import {downloadCsv} from '../reports/Reports';
import {expOpenBillRows,expVendorAdvance,expVendorAllocate,expVendorAgeingRows,expVendorAgeingCsv,expVendorPaymentList,accCashVerdict,EXP_AGEING_BUCKETS,
  type ExpVendorPay,type ExpVendorPayVoid,type ExpVendorReturn} from '../accounting/acc2-views';
import {useAccPerms,accUserName} from '../accounting/parts';
import {toPaise,fromPaise,payMethods} from '../accounting/shared';
import {CashGuardNote,cashGuardToast} from '../accounting/cashguard';

type View='bills'|'ageing'|'payments';
export function ExpVendors({phone=false}:{phone?:boolean}){
  const {s,uid,open,toast,user}=useHcc();const {canVendorPay}=useAccPerms();
  const ageing=useMemo(()=>expVendorAgeingRows(s),[s]);const pays=expVendorPaymentList(s);
  const [view,setView]=useState<View>('bills');
  const [vendor,setVendor]=useState(ageing[0]?.vendor??'');const v=ageing.some(a=>a.vendor===vendor)?vendor:ageing[0]?.vendor??'';
  const bills=v?expOpenBillRows(s,v):[],adv=v?expVendorAdvance(s,v):0,open$=bills.reduce((a,b)=>a+b.open,0);
  const tot=ageing.reduce((a,r)=>({b0:a.b0+r.b0,b31:a.b31+r.b31,b61:a.b61+r.b61,b90:a.b90+r.b90,total:a.total+r.total,advance:a.advance+r.advance}),{b0:0,b31:0,b61:0,b90:0,total:0,advance:0});
  const exportOk=rbacCanExportVouchers(s,uid);
  const csvBtn=exportOk&&<button type="button" className="btn" disabled={!ageing.length} onClick={()=>{downloadCsv('HCC-Demo-vendor-ageing.csv',expVendorAgeingCsv(ageing));toast('CSV downloaded');}}><Download size={16}/> Ageing CSV</button>;
  const kpis=<div className={phone?'mm-kpis':'small-stats'}>{phone?<><KpiCard label="Owed to vendors" value={money(tot.total)} sub="open bills"/><KpiCard label="Advances paid" value={money(tot.advance)} sub="vendors owe us"/></>
    :<><div><span>Owed to vendors</span><strong>{money(tot.total)}</strong></div><div><span>Over 60 days</span><strong>{money(tot.b61+tot.b90)}</strong></div><div><span>Advances paid</span><strong>{money(tot.advance)}</strong></div></>}</div>;
  const views=<ChipFilter<View> label="Vendor payments view" value={view} onChange={setView} options={[{id:'bills',label:'Open bills'},{id:'ageing',label:'Ageing'},{id:'payments',label:'Payments',count:pays.length}]}/>;
  const picker=<label className="exp-vend-pick">Vendor<select value={v} onChange={e=>setVendor(e.target.value)}>{ageing.map(a=><option key={a.vendor} value={a.vendor}>{a.vendor} · {money(a.total)}{a.advance?` · advance ${money(a.advance)}`:''}</option>)}</select></label>;
  const billsView=!ageing.length?<Empty title="No open vendor bills" body="Bills recorded with the funding “Vendor credit” appear here until they are paid."/>:<>
    {picker}
    <div className="summary-list"><div><span>Open bills of {v}</span><strong>{money(open$)}</strong></div>{adv>0&&<div><span>Advance already paid (carried to the next bill)</span><strong>{money(adv)}</strong></div>}</div>
    {phone?bills.map(b=><ListCard key={b.expense} title={`${b.bill} · ${money(b.open)} open`} sub={`${shortDate(b.date)} · ${b.ageDays} days old`} meta={`Bill ${money(b.amount)} · paid ${money(b.paid)} · ${b.expense}`}/>)
      :<div className="table-wrap"><table><thead><tr><th>Bill</th><th>Date</th><th className="number">Age (days)</th><th className="number">Bill (net of TDS)</th><th className="number">Paid</th><th className="number">Open</th></tr></thead><tbody>
        {bills.map(b=><tr key={b.expense}><td><strong>{b.bill}</strong><small>{b.expense}</small></td><td>{dateLabel(b.date)}</td><td className="number">{b.ageDays}</td><td className="number">{money(b.amount)}</td><td className="number">{money(b.paid)}</td><td className="number"><strong>{money(b.open)}</strong></td></tr>)}</tbody></table></div>}
    {canVendorPay?<div className="exp-item-actions"><button type="button" className="btn primary" onClick={()=>open({type:'exp-vendor-pay',id:v})}><Banknote size={16}/> Pay {v}…</button></div>
      :<p className="muted">Vendor payments are made by the Owner, Manager or Accountant.</p>}</>;
  const ageingView=!ageing.length?<Empty title="Nothing owed" body="No open vendor bills and no advances."/>:<>
    {phone?ageing.map(r=><ListCard key={r.vendor} title={r.vendor} sub={EXP_AGEING_BUCKETS.map((b,i)=>`${b}: ${money([r.b0,r.b31,r.b61,r.b90][i])}`).join(' · ')} meta={r.advance?`Advance paid ${money(r.advance)}`:undefined} right={<strong className="number">{money(r.total)}</strong>}/>)
      :<div className="table-wrap"><table><thead><tr><th>Vendor</th>{EXP_AGEING_BUCKETS.map(b=><th key={b} className="number">{b} days</th>)}<th className="number">Open total</th><th className="number">Advance</th></tr></thead><tbody>
        {ageing.map(r=><tr key={r.vendor}><td>{r.vendor}</td><td className="number">{money(r.b0)}</td><td className="number">{money(r.b31)}</td><td className="number">{money(r.b61)}</td><td className="number">{money(r.b90)}</td><td className="number"><strong>{money(r.total)}</strong></td><td className="number">{r.advance?money(r.advance):'—'}</td></tr>)}
        <tr className="acc-total"><td><strong>Total</strong></td><td className="number">{money(tot.b0)}</td><td className="number">{money(tot.b31)}</td><td className="number">{money(tot.b61)}</td><td className="number">{money(tot.b90)}</td><td className="number"><strong>{money(tot.total)}</strong></td><td className="number">{money(tot.advance)}</td></tr></tbody></table></div>}
    <p className="muted">Age is counted from the bill date over the bills still open after the payments allocated to them.</p>{csvBtn}</>;
  const paymentsView=!pays.length?<Empty title="No vendor payments yet" body="Payments made from here appear with the bills they settled."/>:phone?pays.slice(0,50).map(p=><React.Fragment key={p.id}>
      <ListCard title={`${p.vendor} · ${money(p.amount)}`} sub={`${shortDate(p.date)} · ${p.method} · ${p.reference||'no reference'}${p.void?' · void':''}`}
        meta={p.allocations.length?`Settled ${p.allocations.map(a=>a.expense).join(', ')}`:'Advance'} right={p.void?<span className="pill neutral">Void</span>:<span className="pill green">Paid</span>} onClick={()=>open({type:'exp-vendor-void',id:p.id})}/>
      {!p.void&&['Owner','Manager','Accountant'].includes(user.role)&&<button type="button" className="btn full" onClick={()=>open({type:'exp-vendor-realloc',id:p.id})}>Re-allocate {p.id}…</button>}</React.Fragment>)
    :<div className="table-wrap"><table><thead><tr><th>Payment</th><th>Vendor</th><th>Method / reference</th><th className="number">Amount</th><th>Settles</th><th>Status</th><th/></tr></thead><tbody>
      {pays.slice(0,100).map(p=>{const used=p.allocations.reduce((a,x)=>a+x.amount,0);return <tr key={p.id}><td><strong>{p.id}</strong><small>{dateLabel(p.date)} · {accUserName(s,p.by)}</small></td><td>{p.vendor}</td><td>{p.method}<small>{p.reference||'—'}</small></td>
        <td className="number">{money(p.amount)}</td><td>{p.allocations.map(a=><small key={a.expense}>{a.expense} · {money(a.amount)}</small>)}{p.amount>used&&<small>Advance {money(p.amount-used)}</small>}</td>
        <td>{p.void?<span className="pill neutral">Void</span>:<span className="pill green">Paid</span>}{p.void&&p.voidReason&&<small>{p.voidReason}</small>}</td>
        <td>{!p.void&&<button type="button" className="text-btn danger-text" onClick={()=>open({type:'exp-vendor-void',id:p.id})}>Void…</button>}{!p.void&&['Owner','Manager','Accountant'].includes(user.role)&&<button type="button" className="text-btn" onClick={()=>open({type:'exp-vendor-realloc',id:p.id})}>Re-allocate…</button>}</td></tr>;})}</tbody></table></div>;
  return <section className={phone?'mm-vendors':'card exp-vendors'}>
    <div className="section-title"><h2>Vendor payments</h2><span className="muted">Pay open bills, oldest first</span></div>
    <div className={phone?undefined:'padded'}>{kpis}{views}
      {view==='bills'&&billsView}{view==='ageing'&&ageingView}{view==='payments'&&paymentsView}</div></section>;
}

// ── pay vendor ──
/** 'exp-vendor-pay' · id = vendor name. Allocation editor: oldest open bill first by default; Σ allocations ≤ amount; the rest is an advance. */
export function VendorPayModal({id}:{id?:string}){
  const {s,run,closeModal,busy,modal,toast}=useHcc();const {canVendorPay}=useAccPerms();const vendor=id??'';
  const bills=expOpenBillRows(s,vendor),adv=expVendorAdvance(s,vendor),methods=payMethods(s);
  const totalOpen=bills.reduce((a,b)=>a+b.open,0);
  const [amount,setAmount]=useState(fromPaise(totalOpen));const [method,setMethod]=useState(methods.find(m=>!m[2])?.[0]??methods[0]?.[0]??'');
  const [reference,setReference]=useState('');const [date,setDate]=useState(s.date);
  const [edit,setEdit]=useState<Record<string,string>|null>(null); // null = follow the oldest-first default
  const paise=toPaise(amount),drawer=!!method&&mnyIsDrawer(s,method);
  const def=expVendorAllocate(s,vendor,paise),alloc=edit?bills.map(b=>({expense:b.expense,amount:toPaise(edit[b.expense]??'')})).filter(a=>a.amount>0):def;
  const used=alloc.reduce((a,x)=>a+x.amount,0),over=bills.find(b=>(alloc.find(a=>a.expense===b.expense)?.amount??0)>b.open);
  const problem=!canVendorPay?'Vendor payments are made by the Owner, Manager or Accountant.':paise<=0?'Enter an amount greater than zero.':used>paise?`Allocations ${money(used)} are more than the payment ${money(paise)}.`
    :over?`${over.bill}: allocating more than the ${money(over.open)} still open.`:!drawer&&reference.trim().length<3?'A bank or UPI payment needs a reference (UTR / transaction number).':drawer&&!s.shiftOpen?'Drawer cash needs an open cash shift.':'';
  const guard=drawer&&paise>0?accCashVerdict(s,{kind:'payment',party:vendor,amount:paise,date}):undefined;
  const reset=()=>setEdit(null);
  return <form className="form" onSubmit={e=>{e.preventDefault();if(problem)return;void run({type:'EXP_VENDOR_PAY',vendor,amount:paise,method,reference:reference.trim(),...(date!==s.date?{date}:{}),allocations:alloc,key:modal?.key} satisfies ExpVendorPay,
      'Vendor payment recorded',{close:true,after:()=>{const w=cashGuardToast(guard);if(w)toast(w);}});}}>
    <div className="modal-heading"><p className="eyebrow">VENDOR PAYMENT</p><h2>Pay {vendor}</h2><p>Open bills {money(totalOpen)}{adv?` · advance already paid ${money(adv)}`:''}. Dr vendor payable · Cr the money you pay from.</p></div>
    <label>Amount (₹)<input inputMode="decimal" value={amount} onChange={e=>{setAmount(e.target.value);setEdit(null);}}/></label>
    <div className="form-row"><label>Paid from<select value={method} onChange={e=>setMethod(e.target.value)}>{methods.map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
      <label>Date<input type="date" value={date} max={s.date} onChange={e=>e.target.value&&setDate(e.target.value)}/></label></div>
    <label>Reference {drawer?'(optional for drawer cash)':'(UTR / transaction number)'}<input value={reference} maxLength={60} onChange={e=>setReference(e.target.value)}/></label>
    <fieldset className="exp-alloc"><legend>Settles these bills {edit?'(edited)':'(oldest first)'}</legend>
      {bills.map(b=>{const a=alloc.find(x=>x.expense===b.expense)?.amount??0;return <label key={b.expense} className="exp-alloc-row"><span><strong>{b.bill}</strong><small>{shortDate(b.date)} · {b.ageDays} days · open {money(b.open)}</small></span>
        <input inputMode="decimal" aria-label={`Allocate to ${b.bill}`} value={edit?(edit[b.expense]??''):fromPaise(a)} onChange={e=>setEdit({...(edit??Object.fromEntries(def.map(d=>[d.expense,fromPaise(d.amount)]))),[b.expense]:e.target.value})}/></label>;})}
      {!bills.length&&<p className="muted">No open bills — the whole payment is an advance.</p>}
      <p className="muted" role="status">Allocated {money(used)} · {paise>used?`advance ${money(paise-used)} (the vendor owes us until the next bill)`:'no advance'}</p>
      {edit&&<button type="button" className="text-btn" onClick={reset}>Back to oldest first</button>}</fieldset>
    {guard&&<CashGuardNote verdict={guard}/>}
    {problem&&paise>0&&<p className="f-why" role="alert">{problem}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!problem}><Banknote size={16}/> Record payment</button></div></form>;
}
/** 'exp-vendor-void' · id = payment id (Owner, reason). */
export function VendorVoidModal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const p=(s.expensesX?.vendorPayments??[]).find(x=>x.id===id);const [reason,setReason]=useState('');
  if(!p)return <Empty title="Payment not found"/>;
  const owner=user.role==='Owner';
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'EXP_VENDOR_PAY_VOID',id:p.id,reason:reason.trim(),key:'expvpv:'+p.id} satisfies ExpVendorPayVoid,'Payment voided',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">{p.void?'VOID PAYMENT':'OWNER ACTION'}</p><h2>{p.void?'Voided':'Void'} {p.id}</h2><p>{p.vendor} · {money(p.amount)} · {p.method} · {dateLabel(p.date)}{p.reference?` · ${p.reference}`:''}</p></div>
    {p.void?<div className="info-note"><Info size={16}/>Voided by {accUserName(s,p.voidBy??'')}: {p.voidReason}. Its bills are open again.</div>:<>
      <div className="info-note"><Info size={16}/>The bills it settled become open again. On a locked day a reversing entry is posted today; otherwise the payment is cancelled.</div>
      <label>Reason (required)<textarea rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
      {!owner&&<p className="f-why">Only the Owner can void a vendor payment.</p>}</>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>{p.void?'Close':'Back'}</button>{!p.void&&<button className="btn danger" disabled={busy||!owner||reason.trim().length<3}><Ban size={16}/> Void payment</button>}</div></form>;
}

/** 'exp-return' · id = expense id: a vendor return / credit note (Manager requests, Owner approves — like a credit note). */
export function VendorReturnModal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const e=s.expenses.find(x=>x.id===id);const [amount,setAmount]=useState('');const [gst,setGst]=useState('');const [reason,setReason]=useState('');
  if(!e)return <Empty title="Expense not found"/>;
  const paise=toPaise(amount),g=toPaise(gst),ok=paise>0&&paise<=e.amount&&g<=(e.gstPaise??0)&&reason.trim().length>=3;
  return <form className="form" onSubmit={ev=>{ev.preventDefault();if(ok)void run({type:'EXP_VENDOR_RETURN',expense:e.id,amount:paise,gstPaise:g,reason:reason.trim(),key:`expret:${e.id}:${paise}`} satisfies ExpVendorReturn,
      user.role==='Owner'?'Return recorded':'Return requested — the Owner approves',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">VENDOR RETURN · {e.id}</p><h2>Return to {e.vendor}</h2><p>{e.description??e.category} · bill {money(e.amount)}{e.gstPaise?` incl. GST ${money(e.gstPaise)}`:''}. A negative purchase with the same keys is posted and the input credit taken on it is reversed.</p></div>
    <label>Amount returned (₹, as on the credit note)<input inputMode="decimal" value={amount} onChange={x=>setAmount(x.target.value)}/></label>
    <label>GST in that amount (₹)<input inputMode="decimal" value={gst} placeholder="0" onChange={x=>setGst(x.target.value)}/></label>
    <label>Reason (required)<textarea rows={2} required minLength={3} value={reason} onChange={x=>setReason(x.target.value)} enterKeyHint="done"/></label>
    {paise>e.amount&&<p className="f-why" role="alert">The return cannot be more than the bill.</p>}
    {user.role!=='Owner'&&<div className="info-note"><Info size={16}/>This is a request: the Owner approves it before anything posts.</div>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ok}>{user.role==='Owner'?'Record return':'Request return'}</button></div></form>;
}
