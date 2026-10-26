// M10 Payment (phone override of 'payment'): keypad amount, tender chips, "+ Split" rows, confirm screen, lost-response recovery.
// Sends the same PAYMENT command as the desktop PaymentModal (built by mmPaymentCommand).
import React,{useState} from 'react';
import {Plus,Trash2,ArrowRight,Check,AlertTriangle,RefreshCw,Info} from 'lucide-react';
import {money,totals} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty} from '../../components/ui';
import {useCashBlock} from '../../clients/sync/cashier-ui';
import {MM_TENDERS,MM_PAY_KINDS,MmTenderRow,mmPaise,mmRupeeText,mmEffectiveRows,mmPaymentProblem,mmPaymentCommand} from './logic';

function TenderChips({value,onChange,label,only,noCash}:{value:string;onChange:(v:string)=>void;label:string;only?:string[];noCash?:boolean}){
  return <div className="mm-chips" role="radiogroup" aria-label={label}>{MM_TENDERS.filter(t=>!only||only.includes(t.id)).map(t=>
    <button key={t.id} type="button" role="radio" aria-checked={value===t.id} className={value===t.id?'on':''} disabled={noCash&&t.id==='Cash'} onClick={()=>onChange(t.id)}>{t.label}</button>)}</div>;
}

export function MmPayment({id}:{id?:string}){
  const {s,run,booking,modal,busy,error}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const t=b?totals(b):undefined;
  const [kind,setKind]=useState<string>('payment');
  const [total,setTotal]=useState(t&&t.balance>0?mmRupeeText(t.balance):'');
  const cashBlock=useCashBlock(); // APK12-3: production, no own cashier shift → cash tender disabled with a hint
  const [rows,setRows]=useState<MmTenderRow[]>([{method:cashBlock?'UPI':'Cash',amount:'',reference:''}]);
  const [step,setStep]=useState<'form'|'confirm'>('form');
  const [problem,setProblem]=useState('');
  const [failed,setFailed]=useState(false);
  if(!b||!t)return <Empty title="This record is unavailable"/>;
  const single=rows.length===1,eff=mmEffectiveRows(total,rows),target=mmPaise(total);
  const sum=eff.reduce((a,r)=>a+(mmPaise(r.amount)||0),0),left=(target||0)-sum;
  const setRow=(i:number,p:Partial<MmTenderRow>)=>setRows(rows.map((r,j)=>j===i?{...r,...p}:r));
  const chooseKind=(k:string)=>{setKind(k);setProblem('');
    if(k==='deposit-return'){setRows([{...rows[0],method:rows[0].method,amount:''}]);setTotal(mmRupeeText(t.deposit));}
    else if(k==='payment')setTotal(t.balance>0?mmRupeeText(t.balance):'');else setTotal('');};
  const addSplit=()=>{const first=single?[{...rows[0],amount:total}]:rows;const used=first.reduce((a,r)=>a+(mmPaise(r.amount)||0),0);
    setRows([...first,{method:first.some(r=>r.method==='UPI')?'Card':'UPI',amount:mmRupeeText((target||0)-used),reference:''}]);};
  const review=(e:React.FormEvent)=>{e.preventDefault();const p=mmPaymentProblem(kind,total,rows,t.deposit);setProblem(p);if(!p)setStep('confirm');};
  const key=modal!.key;
  const saved=s.processed.includes(key); // lost-response recovery: the receipt exists even though the answer was lost
  const post=async()=>{const ok=await run(mmPaymentCommand(b.id,kind,total,rows,key),single?'Receipt recorded once':`${rows.length} receipts recorded once`,{after:()=>booking(b.id)});setFailed(!ok);};
  const kindLong=MM_PAY_KINDS.find(k=>k.id===kind)!.long;

  if(step==='confirm')return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Confirm before posting</h2><p>{kindLong}</p></div>
    <div className="mm-confirm-amt"><span>Total</span><strong className="number">{money(target)}</strong></div>
    <div className="mm-card">{eff.map((r,i)=><div className="mm-row" key={i}><div className="mm-row-main"><strong>{r.method}</strong><small>{r.reference.trim()||'Demo cash'}</small></div><div className="mm-row-amt"><strong className="number">{money(mmPaise(r.amount))}</strong></div></div>)}
      {kind==='payment'&&<div className="mm-row mm-row-total"><div className="mm-row-main"><strong>Balance after posting</strong></div><div className="mm-row-amt"><strong className="number">{money(t.balance-target)}</strong></div></div>}
      {kind!=='payment'&&<div className="mm-row mm-row-total"><div className="mm-row-main"><strong>Deposit held after posting</strong></div><div className="mm-row-amt"><strong className="number">{money(t.deposit+(kind==='deposit'?target:-target))}</strong></div></div>}</div>
    <div className="info-note mm-unverified"><AlertTriangle size={18}/><span><strong>Not verified.</strong> Manual entry — the bank/UPI transaction is not checked. Fictional demonstration receipt; no money moves.</span></div>
    {failed&&(saved?<div className="mm-recover ok" role="status"><Check size={18}/><span><strong>Saved, but the confirmation was lost.</strong> The receipt is on the folio. Tap “Check again” — it reuses the same request and never creates a second receipt.</span></div>
      :<div className="mm-recover" role="status"><RefreshCw size={18}/><span><strong>Not posted.</strong> {error||'The service did not accept it.'} You can safely try again — the same request is reused.</span></div>)}
    <div className="modal-actions"><button type="button" className="btn" disabled={busy} onClick={()=>{setStep('form');setFailed(false);}}>Edit</button>
      <button className="btn primary mm-big" disabled={busy} onClick={post}>{busy?'Posting…':failed?(saved?'Check again':'Try again'):'Confirm '+money(target)} <Check size={18}/></button></div></>;

  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Record a payment</h2><p>Balance {money(t.balance)} · Deposit held {money(t.deposit)}</p></div>
    <form className="form mm-form" onSubmit={review} noValidate>
      <div className="mm-field"><span className="mm-label" id="mm-kind">Entry type</span>
        <div className="mm-chips" role="radiogroup" aria-labelledby="mm-kind">{MM_PAY_KINDS.map(k=><button key={k.id} type="button" role="radio" aria-checked={kind===k.id} className={kind===k.id?'on':''} disabled={k.id==='deposit-return'&&t.deposit<=0} onClick={()=>chooseKind(k.id)}>{k.label}</button>)}</div></div>
      <label className="mm-amount">Amount (₹)<input inputMode="decimal" enterKeyHint="done" autoComplete="off" name="amount" value={total} onChange={e=>setTotal(e.target.value)} placeholder="0" aria-describedby="mm-amount-help"/></label>
      <small id="mm-amount-help" className="mm-help">{kind==='payment'?`Balance due ${money(Math.max(0,t.balance))}`:kind==='deposit-return'?`Deposit held ${money(t.deposit)}`:'Refundable deposit — kept separate from the balance'}</small>
      <fieldset className="mm-tenders"><legend>{single?'Tender':'Split tenders'}</legend>
        {rows.map((r,i)=><div className="mm-tender" key={i}>
          {!single&&<div className="mm-tender-head"><strong>Tender {i+1}</strong><button type="button" className="m-icon mm-del" aria-label={`Remove tender ${i+1}`} onClick={()=>setRows(rows.filter((_,j)=>j!==i))}><Trash2 size={18}/></button></div>}
          <TenderChips label={`Tender ${i+1} method`} value={r.method} noCash={!!cashBlock} onChange={m=>setRow(i,{method:m})}/>
          {!single&&<label>Amount (₹)<input inputMode="decimal" enterKeyHint="next" value={r.amount} onChange={e=>setRow(i,{amount:e.target.value})} aria-label={`Tender ${i+1} amount`}/></label>}
          {r.method!=='Cash'?<label>Reference<input enterKeyHint="next" maxLength={100} value={r.reference} placeholder="Demo transaction reference, e.g. DEMO-UPI-0002" onChange={e=>setRow(i,{reference:e.target.value})} aria-label={`Tender ${i+1} reference`}/></label>
            :<small className="mm-help">Cash goes into the drawer. Reference optional.</small>}
        </div>)}
        {kind!=='deposit-return'&&rows.length<4&&<button type="button" className="btn mm-split" onClick={addSplit}><Plus size={18}/> Split</button>}
        {!single&&<p className={'mm-alloc '+(left!==0?'bad':'ok')} role="status">Allocated {money(sum)} of {money(target||0)}{left!==0?` · ${money(Math.abs(left))} ${left>0?'left':'too much'}`:' · matches'}</p>}
      </fieldset>
      {cashBlock&&<div className="info-note" role="status"><Info size={16}/>{cashBlock}</div>}
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="info-note"><Info size={16}/>Refunds are requested from the original receipt (Receipts tab) and need approval first.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" disabled={busy}>Review <ArrowRight size={16}/></button></div>
    </form></>;
}
