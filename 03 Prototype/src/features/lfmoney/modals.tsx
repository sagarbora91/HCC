// LF-UI · money dialogs: 'stay-rate' (rate / guest change, A1), 'bill-correct' (Correct this bill, A2), 'agreed-rate' (B1) and 'credit-account' (B2).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {money,strRefusal,strCanDirect,STR_SCOPES,STR_SCOPE_LABEL,strTariff,strAgreedRate,strJudgedText,blcRefusal,blcPreview,blcLive,blcNightOf,type STRScope,type BLCChange,type Booking} from '../../services/mock';
import {Empty} from '../../components/ui';
import type {ModalProps} from '../../app/registry';

const paise=(v:string)=>Math.round(Number(v)*100);
const rupees=(p:number)=>String(p/100);

export function StayRateModal({id}:ModalProps){
  const {s,uid,run,busy,closeModal,modal}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const [f,setF]=useState({rate:b?rupees(b.rate):'',people:String(b?.people??1),children:String(b?.children??0),scope:'today-next' as STRScope,reason:''});
  if(!b)return <Empty title="Booking not found"/>;
  const set=(p:Partial<typeof f>)=>setF(x=>({...x,...p}));
  const input={rate:paise(f.rate),people:Number(f.people),children:Number(f.children),scope:f.scope,reason:f.reason};
  const refusal=strRefusal(s,b,input);const direct=!refusal&&strCanDirect(s,uid,b,input);
  return <><div className="modal-heading"><h2>Rate / guest change · room {b.room}</h2><p>Current rate {money(b.rate)} per night for {b.people} guest{b.people===1?'':'s'}. Tariff today {money(strTariff(s,b))}. Agreed rate {money(strAgreedRate(s,b))}.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'STAY_RATE_CHANGE',booking:b.id,...input,key:modal!.key},direct?'Rate changed.':'Rate change sent for approval.',{close:true});}}>
      <div className="form-row"><label>New rate per night (₹)<input type="number" min={0} step="0.01" value={f.rate} onChange={e=>set({rate:e.target.value})}/></label>
        <label>Guests<input type="number" min={1} max={4} value={f.people} onChange={e=>set({people:e.target.value})}/></label>
        <label>Children<input type="number" min={0} max={3} value={f.children} onChange={e=>set({children:e.target.value})}/></label></div>
      <label>Applies to<select value={f.scope} onChange={e=>set({scope:e.target.value as STRScope})}>{STR_SCOPES.map(x=><option key={x} value={x}>{STR_SCOPE_LABEL[x]}</option>)}</select></label>
      <label>Reason (required)<input value={f.reason} maxLength={200} onChange={e=>set({reason:e.target.value})}/></label>
      {refusal?<p className="muted" role="status">{refusal}</p>:<p className="muted" role="status">{direct?'You can apply this directly.':'This needs an approval from the Owner or a Manager within their limit.'}
        {input.rate!==b.rate&&Number.isFinite(input.rate)?` (${strJudgedText(s,b,input.rate)})`:''}</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!refusal}>{direct?'Apply change':'Request approval'}</button></div>
    </form></>;
}

export function BillCorrectModal({id}:ModalProps){
  const {s,run,busy,closeModal,modal}=useHcc();
  const b=s.bookings.find(x=>x.id===id) as Booking|undefined;
  const [step,setStep]=useState<1|2|3>(1);
  const [rates,setRates]=useState<Record<string,string>>({});const [removes,setRemoves]=useState<Set<string>>(new Set());
  const [add,setAdd]=useState({label:'',amount:'',gstCategory:'other'});const [reason,setReason]=useState('');
  if(!b)return <Empty title="Booking not found"/>;
  const refusal=blcRefusal(s,b);
  if(refusal)return <><div className="modal-heading"><h2>Correct this bill</h2></div><p role="alert">{refusal}</p><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const live=blcLive(b);const nights=live.filter(l=>l.kind==='room'&&l.amount>0);
  const changes:BLCChange[]=[
    ...nights.filter(l=>rates[l.id]!==undefined&&rates[l.id]!==''&&paise(rates[l.id])!==l.amount).map(l=>({kind:'room-rate' as const,night:blcNightOf(l),oldRate:l.amount,newRate:paise(rates[l.id])})),
    ...[...removes].map(lineId=>({kind:'remove-line' as const,lineId})),
    ...(add.label.trim()&&add.amount?[{kind:'add-line' as const,label:add.label.trim(),amount:paise(add.amount),gstCategory:add.gstCategory,date:s.date}]:[])];
  const pv=blcPreview(s,b,changes);const bad='error' in pv?pv.error:'';
  return <><div className="modal-heading"><h2>Correct this bill · {b.invoiceNo}</h2><p>Step {step} of 3 · {step===1?'What changes':step===2?'See the new total':'Reason and request'}. The Owner approves; a credit note and a new invoice follow.</p></div>
    {step===1&&<div className="form">
      <h3 className="gs-sub">Room rent per night</h3>
      {nights.map(l=><label key={l.id}>{blcNightOf(l)} (billed {money(l.amount)})<input type="number" min={0} step="0.01" value={rates[l.id]??''} placeholder={rupees(l.amount)} onChange={e=>setRates(r=>({...r,[l.id]:e.target.value}))}/></label>)}
      <h3 className="gs-sub">Remove a line</h3>
      {live.filter(l=>l.kind!=='room'&&l.amount>0).map(l=><label key={l.id} className="inline"><input type="checkbox" checked={removes.has(l.id)} onChange={e=>setRemoves(r=>{const n=new Set(r);e.target.checked?n.add(l.id):n.delete(l.id);return n;})}/> {l.label} · {money(l.amount)}</label>)}
      <h3 className="gs-sub">Add a line</h3>
      <div className="form-row"><label>Description<input value={add.label} maxLength={80} onChange={e=>setAdd(a=>({...a,label:e.target.value}))}/></label>
        <label>Amount (₹)<input type="number" min={0} step="0.01" value={add.amount} onChange={e=>setAdd(a=>({...a,amount:e.target.value}))}/></label>
        <label>GST category<select value={add.gstCategory} onChange={e=>setAdd(a=>({...a,gstCategory:e.target.value}))}>{['food','laundry','banquet','other'].map(c=><option key={c}>{c}</option>)}</select></label></div>
    </div>}
    {step===2&&<div className="summary-list">{bad?<p role="alert">{bad}</p>:<>
      <div><span>Old total</span><strong>{money((pv as {oldTotal:number}).oldTotal)}</strong></div>
      <div><span>New total</span><strong>{money((pv as {newTotal:number}).newTotal)}</strong></div>
      <div><span>Difference</span><strong>{(pv as {delta:number}).delta>=0?'+':'−'}{money(Math.abs((pv as {delta:number}).delta))}</strong></div></>}</div>}
    {step===3&&<form className="form" onSubmit={e=>{e.preventDefault();void run({type:'BILL_CORRECTION_REQUEST',booking:b.id,changes,reason,key:modal!.key},'Correction sent for approval.',{close:true});}}>
      <label>Reason (required)<input value={reason} maxLength={200} onChange={e=>setReason(e.target.value)}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>setStep(2)}>Back</button><button className="btn primary" disabled={busy||!reason.trim()||!!bad}>Request correction</button></div></form>}
    {step<3&&<div className="modal-actions"><button type="button" className="btn" onClick={step===1?closeModal:()=>setStep(1)}>{step===1?'Cancel':'Back'}</button>
      <button type="button" className="btn primary" disabled={!!bad} onClick={()=>setStep(step===1?2:3)}>Next</button></div>}
  </>;
}

export function AgreedRateModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();
  const g=(s.guests??[]).find(x=>x.id===id);
  const types=[...new Set(s.rooms.map(r=>r.type))];
  const [f,setF]=useState({roomType:types[0]??'',rate:'',validTo:'',note:''});
  if(!g)return <Empty title="Guest not found"/>;
  const set=(p:Partial<typeof f>)=>setF(x=>({...x,...p}));
  return <><div className="modal-heading"><h2>Agreed rate · {g.name}</h2><p>New bookings for this guest default to the agreed rate for the room type (Owner only).</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'GUEST_RATE_SET',guestId:g.id,roomType:f.roomType,rate:paise(f.rate),...(f.validTo?{validTo:f.validTo}:{}),note:f.note},'Agreed rate saved.',{close:true});}}>
      <div className="form-row"><label>Room type<select value={f.roomType} onChange={e=>set({roomType:e.target.value})}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
        <label>Rate per night (₹)<input type="number" min={1} step="0.01" required value={f.rate} onChange={e=>set({rate:e.target.value})}/></label>
        <label>Valid to (optional)<input type="date" value={f.validTo} onChange={e=>set({validTo:e.target.value})}/></label></div>
      <label>Note<input value={f.note} maxLength={200} onChange={e=>set({note:e.target.value})}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!f.rate}>Save agreed rate</button></div>
    </form></>;
}

export function CreditAccountModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();
  const g=(s.guests??[]).find(x=>x.id===id);
  const [f,setF]=useState({limit:g?.creditAccount?rupees(g.creditAccount.limit):'0',active:g?.creditAccount?.active??true,note:g?.creditAccount?.note??''});
  if(!g)return <Empty title="Guest not found"/>;
  return <><div className="modal-heading"><h2>Credit account · {g.name}</h2><p>A credit limit lets this guest settle a folio or counter bill on Udhaar credit (Owner only).</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:paise(f.limit),active:f.active,note:f.note},'Credit account saved.',{close:true});}}>
      <div className="form-row"><label>Credit limit (₹)<input type="number" min={0} step="0.01" value={f.limit} onChange={e=>setF(x=>({...x,limit:e.target.value}))}/></label>
        <label className="inline"><input type="checkbox" checked={f.active} onChange={e=>setF(x=>({...x,active:e.target.checked}))}/> Active</label></div>
      <label>Note<input value={f.note} maxLength={200} onChange={e=>setF(x=>({...x,note:e.target.value}))}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save credit account</button></div>
    </form></>;
}
