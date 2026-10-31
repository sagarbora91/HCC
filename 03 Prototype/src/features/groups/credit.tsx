// DEC-076 · company credit where company billing happens (desktop + phone): the checkout panel (which company account the stay is billed to,
// its remaining credit, linking the stay to an account, the refusal before the request is sent), the account picker of the booking dialogs
// and the company-payment dialog ('grp-payment', id = company). The commands decide; these screens repeat the same figures and wording.
import React,{useState} from 'react';
import {Building2,AlertTriangle,Check,Info} from 'lucide-react';
import {grpActiveCompanies,money,totals,grpSlice,grpCompany,grpCredit,grpCreditPreview,grpCreditRefusal,folioCompanyCreditState,FOLIO_MANAGER_CREDIT_LIMIT,GRP_PAY_METHODS} from '../../services/mock';
import type {Booking,GRPCompany} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,field,submit} from '../../components/ui';
import {grpPaise} from './parts';

/** "₹4,300 used of ₹1,50,000 · ₹1,45,700 left" — the account's credit in one line. */
export function grpCreditLine(c:GRPCompany,k:{exposure:number;available:number}){return `${money(k.exposure)} used of ${money(c.creditLimit)}${c.creditLimit===0?' (no credit)':''} · ${money(k.available)} left`;}

/** Checkout panel: company credit for the folio balance (desktop CheckoutModal and phone MmCheckout). */
export function CompanyCreditPanel({b,mobile}:{b:Booking;mobile?:boolean}){
  const {s,run,busy,permission}=useHcc();
  const [pick,setPick]=useState('');
  const t=totals(b);
  if(t.balance<=0||b.status!=='In house')return null;
  const cc=folioCompanyCreditState(s,b,t.balance),p=grpCreditPreview(s,b,t.balance),accounts=grpActiveCompanies(s,b.company?.account); // ACC-2 integration: the flows lane's picker filter (retired hidden, the chosen one kept)
  const cls='info-note folio-credit'+(mobile?' mm-cc':'');
  if(!p.c){
    if(!b.company&&!(permission('reservations')&&accounts.length))return null;
    return <div className={cls}><Building2 size={mobile?18:16}/><div>
      <strong>Company credit{b.company?' · '+b.company.name:''}</strong>
      <p>{b.company?`${b.company.name} is on the invoice only — the guest pays. `:''}Company credit needs the booking billed to a company account; that account’s credit limit applies (DEC-076).</p>
      {permission('reservations')&&accounts.length>0&&<div className={'grp-link'+(mobile?' grp-link-m':'')}>
        <label>Company account<select value={pick} onChange={e=>setPick(e.target.value)}><option value="">Choose an account…</option>
          {accounts.map(c=>{const k=grpCredit(s,c);return <option key={c.id} value={c.id}>{c.name} · {money(k.available)} left</option>;})}</select></label>
        <button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy||!pick} onClick={()=>run({type:'BOOKING_COMPANY',id:b.id,account:pick,key:`bco:${b.id}:${b.version}:${pick}`},'Booking billed to the company account')}>
          <Check size={mobile?18:15}/> Bill to this account</button></div>}
    </div></div>;
  }
  const k=grpCredit(s,p.c),showRefusal=!!p.refusal&&(cc.state==='none'||cc.state==='stale'||cc.state==='approved');
  return <div className={cls}><Building2 size={mobile?18:16}/><div>
    <strong>Company credit · {p.c.name}</strong>
    <p className="grp-credit-line">Credit {grpCreditLine(p.c,k)}.</p>
    {cc.state==='none'&&!p.refusal&&<p>Move the {money(t.balance)} balance to the company’s receivable. Needs approval: a Manager up to {money(FOLIO_MANAGER_CREDIT_LIMIT)}, an Owner above.</p>}
    {cc.state==='pending'&&<p>Requested as {cc.approval!.id} for {money(cc.approval!.amount)} — awaiting approval by another user.</p>}
    {cc.state==='approved'&&<p>Approved ({cc.approval!.id}) for exactly {money(cc.approval!.amount)}. It is applied when you confirm checkout{p.refusal?' — but the credit limit is checked again then':''}.</p>}
    {cc.state==='stale'&&<p>Approval {cc.approval!.id} was for {money(cc.approval!.amount)}, but the folio has changed. Request again.</p>}
    {showRefusal&&<p className="grp-refusal" role="alert"><AlertTriangle size={mobile?18:15}/><span>{p.refusal} Or take the payment from the guest.</span></p>}
    {(cc.state==='none'||cc.state==='stale')&&(permission('checkout')
      ?<button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy||!!p.refusal} onClick={()=>run({type:'COMPANY_CREDIT_REQUEST',id:b.id,key:`cc:${b.id}:${b.version}`},'Company credit requested — awaiting approval')}>Request company credit for {money(t.balance)}</button>
      :<p className="muted">Your role cannot request company credit.</p>)}
  </div></div>;
}

/**
 * "Bill to company account" for the new-booking dialogs (desktop and phone). `value` = company id ('' = the guest pays). Shows the account's
 * remaining credit and, for the estimated stay value (`stay`: agreed nights + GST), the refusal the booking would get.
 */
export function CompanyAccountPicker({value,onChange,stay,mobile}:{value:string;onChange:(id:string)=>void;stay:number;mobile?:boolean}){
  const {s}=useHcc();const accounts=grpActiveCompanies(s); // ACC-2 integration
  if(!accounts.length)return null;
  const c=grpCompany(s,value),k=c?grpCredit(s,c):undefined,refusal=c&&k&&stay>0?grpCreditRefusal(c,k.exposure,stay,'booking'):undefined;
  return <div className={'grp-acct'+(mobile?' grp-acct-m':'')}>
    <label>Bill to company account (optional)<select value={value} onChange={e=>onChange(e.target.value)}><option value="">No — the guest pays (company below is invoice details only)</option>
      {accounts.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
    {c&&k&&<small className={refusal?'grp-refusal':''} role={refusal?'alert':undefined}>{refusal?<><AlertTriangle size={14}/> {refusal}</>:<>Credit {grpCreditLine(c,k)}{stay>0?` · this stay about ${money(stay)} incl. GST`:''}. The account’s name and GSTIN go on the invoice.</>}</small>}
  </div>;
}

/** Record a payment received from a company against its company-credit receivable (id = company). Desktop and phone. */
export function CompanyPaymentModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy,permission}=useHcc();const c=grpCompany(s,id);
  if(!c)return <Empty title="This company is unavailable"/>;
  const k=grpCredit(s,c);
  if(!permission('payments'))return <Empty title="Not permitted" body="Recording a company payment needs the payments permission."/>;
  return <><div className="modal-heading"><div className="eyebrow">COMPANY PAYMENT · {c.id}</div><h2>{c.name}</h2>
      <p>Money received from the company against what it owes on company credit. It frees credit under the limit.</p></div>
    <div className="summary-list grp-summary"><div><span>Owed on company credit</span><strong>{money(k.receivable)}</strong></div><div><span>Credit</span><strong>{grpCreditLine(c,k)}</strong></div></div>
    {k.receivable<=0?<><div className="info-note"><Info size={16}/>{c.name} owes nothing on company credit.</div><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>:
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'GRP_COMPANY_PAYMENT',company:c.id,amount:grpPaise(f.get('amount'))??0,...(grpPaise(f.get('tds'))?{tds:grpPaise(f.get('tds'))}:{}),method:field(f,'method'),reference:field(f,'reference'),
        invoice:field(f,'invoice'),key:modal!.key},'Company payment recorded',{close:true}))}>
      <label>Amount received (₹)<input name="amount" required inputMode="decimal" defaultValue={String(k.receivable/100)} enterKeyHint="next"/></label>
 <label>TDS deducted by the company (₹, optional)<input name="tds" inputMode="decimal" placeholder="0" enterKeyHint="next"/><small>The company paid net of TDS and holds the certificate (Form 16A). The receivable is cleared by the amount received plus the TDS; the TDS waits in TDS receivable until it shows in Form 26AS.</small></label>
      <label>Method<select name="method" defaultValue={GRP_PAY_METHODS[0]}>{GRP_PAY_METHODS.map(m=><option key={m}>{m}</option>)}</select><small>No cash on company credit; record a cheque as a bank transfer.</small></label>
      <label>Transaction reference<input name="reference" required maxLength={120} placeholder="e.g. NEFT / UTR number (fictional)" enterKeyHint="next"/></label>
      <label>Invoice it pays (optional)<input name="invoice" maxLength={16} placeholder="e.g. DEMO/2627/0001" autoCapitalize="characters" enterKeyHint="send"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Record payment <Check size={16}/></button></div>
    </form>}</>;
}
