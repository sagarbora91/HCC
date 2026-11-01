// ACC-3 · acc3-ui — dialogs for the carry-over commands (contract §7): asset put-to-use and disposal (C1), vendor payment re-allocation (C3),
// Form 168 figures on a TDS remittance (C7) and the dated GST-on-advances switch (C10). Screens only; the commands, refusals and postings are the carry lane's.
import React,{useState} from 'react';
import {Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,tdsRemittances,tdsForm168Refusal,expVendorOpenBills,expVendorReallocationRefusal,accGstOnAdvances,accGstOnAdvancesFrom,cmpSlice,accAssets} from '../../services/mock';
import {Empty,dateLabel} from '../../components/ui';
import {toPaise,fromPaise,payMethods} from './shared';

const ASSET_ROLES=['Owner','Accountant'];
const TAX_ROLES=['Owner','Accountant'];
const REALLOC_ROLES=['Owner','Manager','Accountant'];

/** 'acc-asset-put' · id = asset id: the date the asset was first used (depreciation counts from it, rulebook F2). */
export function AssetPutModal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const a=accAssets(s).find(x=>x.id===id);const [date,setDate]=useState(a?.putToUse??s.date);
  if(!a)return <Empty title="Asset not found"/>;
  const can=ASSET_ROLES.includes(user.role),bad=!date||date<a.date||date>s.date;
  return <form className="form" onSubmit={e=>{e.preventDefault();if(can&&!bad)void run({type:'ACC_ASSET_PUT_TO_USE',asset:a.id,date,key:`accput:${a.id}:${date}`},'Put to use recorded',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">FIXED ASSET</p><h2>Put to use · {a.no}</h2><p>{a.name} · bought {dateLabel(a.date)}. Depreciation counts from the date it was first used: 180 days or more in the year gives the full rate, fewer gives half.</p></div>
    <label>Date first used<input type="date" value={date} min={a.date} max={s.date} onChange={e=>setDate(e.target.value)}/></label>
    {bad&&<p className="f-why" role="alert">Choose a date between the purchase ({dateLabel(a.date)}) and today.</p>}
    {!can&&<p className="f-why">Only the Owner or the Accountant changes the fixed-asset register.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!can||bad}>Save date</button></div></form>;
}

/** 'acc-asset-dispose' · id = asset id: sale or scrap (date, sale value, how it was received). */
export function AssetDisposeModal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const a=accAssets(s).find(x=>x.id===id);const methods=payMethods(s).filter(m=>!m[2]||true);
  const [date,setDate]=useState(s.date);const [proceeds,setProceeds]=useState('');const [method,setMethod]=useState(methods[0]?.[0]??'');const [reason,setReason]=useState('');
  if(!a)return <Empty title="Asset not found"/>;
  const p=toPaise(proceeds),can=ASSET_ROLES.includes(user.role),bad=!date||date<a.date||date>s.date||(p>0&&!method)||!!a.disposed;
  return <form className="form" onSubmit={e=>{e.preventDefault();if(can&&!bad)void run({type:'ACC_ASSET_DISPOSE',asset:a.id,date,proceeds:p,...(p>0?{method}:{}),...(reason.trim()?{reason:reason.trim()}:{}),key:`accdis:${a.id}`},'Asset disposal recorded',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">FIXED ASSET</p><h2>Dispose · {a.no}</h2><p>{a.name}. A sale posts Dr the account you received it in · Cr the asset block. Gain or loss on a block is for the CA (rulebook F4); HCC raises a flag when the block goes negative or empty.</p></div>
    <div className="form-row"><label>Date<input type="date" value={date} min={a.date} max={s.date} onChange={e=>setDate(e.target.value)}/></label>
      <label>Sale value (₹, 0 for scrap)<input inputMode="decimal" value={proceeds} onChange={e=>setProceeds(e.target.value)} placeholder={fromPaise(0)||'0'}/></label></div>
    {p>0&&<label>Received in<select value={method} onChange={e=>setMethod(e.target.value)}>{methods.map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>}
    <label>Reason (optional)<input value={reason} maxLength={200} onChange={e=>setReason(e.target.value)}/></label>
    {a.disposed&&<p className="f-why" role="alert">Already disposed on {dateLabel(a.disposed.date)}.</p>}
    {!can&&<p className="f-why">Only the Owner or the Accountant changes the fixed-asset register.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn danger" disabled={busy||!can||bad}>Record disposal</button></div></form>;
}

/** 'exp-vendor-realloc' · id = vendor payment id: change which open bills the payment settles (the voucher does not change). */
export function VendorReallocModal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const p=(s.expensesX?.vendorPayments??[]).find(x=>x.id===id);
  const mine=new Map<string,number>();for(const x of p?.allocations??[])mine.set(x.expense,(mine.get(x.expense)??0)+x.amount);
  const [edit,setEdit]=useState<Record<string,string>>(()=>Object.fromEntries([...mine].map(([k,v])=>[k,fromPaise(v)])));
  if(!p)return <Empty title="Payment not found"/>;
  const bills=expVendorOpenBills(s,p.vendor,s.date,true).map(b=>({...b,cap:b.open+(mine.get(b.expense)??0)})).filter(b=>b.cap>0);
  const alloc=bills.map(b=>({expense:b.expense,amount:toPaise(edit[b.expense]??'')})).filter(a=>a.amount>0),used=alloc.reduce((a,x)=>a+x.amount,0);
  const problem=p.void?'This payment is void.':expVendorReallocationRefusal(s,p,alloc)||(JSON.stringify(alloc)===JSON.stringify(p.allocations)?'Nothing changed.':'');
  const can=REALLOC_ROLES.includes(user.role);
  return <form className="form" onSubmit={e=>{e.preventDefault();if(can&&!problem)void run({type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:alloc,key:`expvre:${p.id}:${used}`},'Payment re-allocated',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">VENDOR PAYMENT</p><h2>Re-allocate {p.id}</h2><p>{p.vendor} · {money(p.amount)} · {dateLabel(p.date)}. The voucher stays as it is; only the bills it settles change, so the ageing follows. What is not allocated stays an advance.</p></div>
    <fieldset className="exp-alloc"><legend>Settles these bills</legend>
      {bills.map(b=><label key={b.expense} className="exp-alloc-row"><span><strong>{b.description||b.expense}</strong><small>{dateLabel(b.date)} · can take up to {money(b.cap)}</small></span>
        <input inputMode="decimal" aria-label={`Allocate to ${b.expense}`} value={edit[b.expense]??''} onChange={e=>setEdit({...edit,[b.expense]:e.target.value})}/></label>)}
      {!bills.length&&<p className="muted">The vendor has no open bills: the whole payment is an advance.</p>}
      <p className="muted" role="status">Allocated {money(used)} of {money(p.amount)}{p.amount>used?` · advance ${money(p.amount-used)}`:''}</p></fieldset>
    {problem&&<p className="f-why" role="alert">{problem}</p>}
    {!can&&<p className="f-why">Re-allocation is for the Owner, Manager or Accountant.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!can||!!problem}>Save allocation</button></div></form>;
}

/** 'acc-tds-form168' · id = remittance voucher id: the company-wise TDS figures from Form 168. */
export function TdsForm168Modal({id}:{id?:string}){
  const {s,run,closeModal,busy,user}=useHcc();const r=tdsRemittances(s).find(x=>x.id===id);const companies=(s.groupsX?.companies??[]) as {id:string;name:string}[];
  const [edit,setEdit]=useState<Record<string,string>>(()=>Object.fromEntries(Object.entries(r?.form168??{}).map(([k,v])=>[k,fromPaise(v as number)])));
  if(!r)return <Empty title="Remittance not found"/>;
  const form168=Object.fromEntries(companies.map(c=>[c.id,toPaise(edit[c.id]??'')]).filter(([,v])=>(v as number)>0));
  const problem=tdsForm168Refusal(s,form168),can=TAX_ROLES.includes(user.role);
  return <form className="form" onSubmit={e=>{e.preventDefault();if(can&&!problem)void run({type:'ACC_TDS_FORM168',remittance:r.id,form168,key:`acc168:${r.id}`},'Form 168 figures saved',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">TDS REMITTANCE</p><h2>Form 168 figures · {r.id}</h2><p>{r.code} · {r.month} · {money(r.amount)} · challan {r.challan}. Enter, for each company that deducted TDS from us, the amount on its Form 168. Saving replaces the earlier figures.</p></div>
    {companies.length?<fieldset className="exp-alloc"><legend>TDS deducted by companies</legend>
      {companies.map(c=><label key={c.id} className="exp-alloc-row"><span><strong>{c.name}</strong><small>{c.id}</small></span>
        <input inputMode="decimal" aria-label={`Form 168 amount for ${c.name}`} value={edit[c.id]??''} onChange={e=>setEdit({...edit,[c.id]:e.target.value})}/></label>)}</fieldset>
      :<div className="info-note"><Info size={16}/>No companies in the groups master yet.</div>}
    {problem&&<p className="f-why" role="alert">{problem}</p>}
    {!can&&<p className="f-why">Form 168 figures are entered by the Owner or the Accountant.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!can||!!problem}>Save figures</button></div></form>;
}

/** 'acc-gst-advances' (Owner): the GST-on-advances switch with the date it applies from. */
export function GstAdvancesModal(){
  const {s,run,closeModal,busy,user}=useHcc();const on0=accGstOnAdvances(s),prof=cmpSlice(s).profile;
  const [on,setOn]=useState(on0);const [from,setFrom]=useState(accGstOnAdvancesFrom(s)||s.date);
  const owner=user.role==='Owner',bad=on&&!/^\d{4}-\d{2}-\d{2}$/.test(from);
  return <form className="form" onSubmit={e=>{e.preventDefault();if(owner&&prof&&!bad)void run({type:'ACC_GST_ADVANCES_SET',on,...(on?{from}:{}),key:`accgstadv:${on}:${from}`},'GST on advances updated',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">GST</p><h2>GST on booking advances</h2><p>Off: GST is booked when the stay is billed. On: an advance received on or after the date below carries GST, reversed when the deposit is applied or refunded. Days already locked keep their figures.</p></div>
    <label className="cmp-check"><input type="checkbox" checked={on} onChange={e=>setOn(e.target.checked)}/>Charge GST on booking advances</label>
    {on&&<label>Applies to advances dated on or after<input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label>}
    {!prof&&<p className="f-why">Set the compliance firm profile first (Compliance calendar › Firm profile).</p>}
    {!owner&&<p className="f-why">Only the Owner changes this switch.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!owner||!prof||bad}>Save</button></div></form>;
}
