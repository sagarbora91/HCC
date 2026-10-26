// Stream C · Folio dialogs: booking/folio (SCR-08), payment with mixed tenders (SCR-09), charge/discount, checkout with
// company credit (SCR-10), sample invoice, refund request, correction request and credit note.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Receipt,Printer,Info,ShieldCheck,Check,ArrowRight,Hotel,Plus,Trash2,RotateCcw,Building2,AlertTriangle} from 'lucide-react';
import {resStatus,Approval,money,nights,totals,folioNights,folioRefundable,folioRefunded,folioLineReversed,folioLineCorrectable,folioApprovalsFor,folioApprovalStatus,folioKind,folioKindLabel,folioReceiptLabel,folioCompanyCreditState,folioCheckoutBlockers,FOLIO_TENDERS,FOLIO_MANAGER_CREDIT_LIMIT,plural,rbacCanViewFolios,rbacCanDiscount,rbacCanRequestCorrection,rbacDiscountLimit,rbacApprovalLimit,RBAC_OWNER_DECIDES} from '../../services/mock';
import {useHcc} from '../../app/context';
import {cbIsLocked,cbLockedMessage,lineTax,gstLineSplit,gstSplitLines,gstPct,gstHalfPct,gstForFolioAdjustment,GST_CHARGE_CATEGORIES} from '../../services/mock';
import {Pill,Empty,field,amount,submit,shortDate} from '../../components/ui';
import {bookingActionSlots} from '../../app/registry';
const useBooking=(id:string)=>{const c=useHcc();return {...c,b:c.s.bookings.find(b=>b.id===id)};};
/** Production only (W4 D1): issue the tax invoice for a departed stay that has none — the step after checkout was refused (e.g. no hotel
 *  GSTIN yet) or the checkout user could not issue invoices. Maps to IssueInvoice (sync/money.ts INVOICE_ISSUE) with a stable key. */
export function IssueInvoiceAction({b,className='btn primary'}:{b:{id:string;status:string;invoiceNo?:string};className?:string}){
  const {run,busy,permission}=useHcc();
  if(process.env.HCC_BUILD!=='production'||b.status!=='Departed'||b.invoiceNo||!permission('checkout'))return null;
  return <button className={className} disabled={busy} onClick={()=>run({type:'INVOICE_ISSUE',id:b.id,key:'invoice:'+b.id},'Tax invoice issued')}><Receipt size={16}/> Issue invoice</button>;
}
const Missing=()=><Empty title="This record is unavailable" body="The booking may have been removed when a scenario was reloaded."/>;
const userName=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const statusTone=(st:string)=>st==='Pending'?'orange':st==='Approved'||st==='Executed'?'green':'neutral';

/** One folio approval with its decision/execution controls (used in the folio dialog and on the Folios page). */
export function FolioApprovalRow({a,showGuest=false}:{a:Approval;showGuest?:boolean}){
  const {s,uid,user,permission,run,busy,open}=useHcc();
  const kind=folioKind(a),status=folioApprovalStatus(s,a),b=s.bookings.find(x=>x.id===a.booking);
  const own=a.actor===uid,canDecide=status==='Pending'&&permission('approve');
  // DEC-063: corrections / credit notes → Owner only (G3); a Manager approves discounts up to the Manager limit (G1).
  const aprLimit=kind==='discount'?rbacApprovalLimit(s,uid):undefined;
  const ownerOnly=RBAC_OWNER_DECIDES.includes(kind)&&user.role!=='Owner';
  const overLimit=kind==='company-credit'&&a.amount>FOLIO_MANAGER_CREDIT_LIMIT&&user.role!=='Owner'||aprLimit!==undefined&&a.amount>aprLimit||ownerOnly;
  const target=kind==='refund'?` · receipt ${a.target}`:kind==='correction'?` · line ${b?.lines.find(l=>l.id===a.target)?.label??a.target}`:kind==='credit-note'&&a.target?` · invoice ${a.target}`:'';
  const okMsg:Record<string,string>={discount:'Discount approved and posted',refund:'Refund approved — a cashier can now execute it','company-credit':'Company credit approved — it is applied at checkout',correction:'Correction approved — reversal line posted','credit-note':'Credit note approved and issued'};
  return <div className="request folio-request">
    <div><strong>{folioKindLabel[kind]??kind} · {money(a.amount)}{showGuest&&b?` · ${b.guest}`:''}</strong>
      <p>{a.id}{showGuest&&b?` · ${b.id}`:''}{target} · {a.reason} · requested by {userName(s,a.actor)}{a.approver?` · decided by ${userName(s,a.approver)}`:''}{a.expiresAt&&status==='Pending'?` · expires ${shortDate(a.expiresAt.slice(0,10))} ${a.expiresAt.slice(11)}`:''}</p>
      {status==='Pending'&&own&&<p className="muted">Your own request — another authorised user must decide it.</p>}
      {canDecide&&!own&&overLimit&&<p className="muted">{ownerOnly?'Corrections and credit notes are approved by an Owner.':aprLimit!==undefined?`Above ${money(aprLimit)} — only an Owner can approve this discount.`:`Above ${money(FOLIO_MANAGER_CREDIT_LIMIT)} — only an Owner can approve this company credit.`}</p>}
    </div>
    <Pill tone={statusTone(status)}>{status}</Pill>
    {canDecide&&!own&&<div className="inline">
      <button className="btn small-btn" disabled={busy||overLimit} title={overLimit?'Owner approval required':undefined} onClick={()=>run({type:a.meta?.command as string||'APPROVAL',id:a.id,approve:true,key:'approval:'+a.id},okMsg[kind]??'Request approved')}>Approve</button>
      <button className="text-btn" disabled={busy} onClick={()=>run({type:a.meta?.command as string||'APPROVAL',id:a.id,approve:false,key:'approval-no:'+a.id},'Request rejected')}>Reject</button></div>}
    {status==='Expired'&&a.status==='Pending'&&permission('approve')&&!own&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'APPROVAL',id:a.id,approve:false,key:'approval-no:'+a.id},'Expired request dismissed')}>Dismiss</button>}
    {kind==='refund'&&status==='Approved'&&(permission('payments')
      ?<button className="btn small-btn primary" disabled={busy} onClick={()=>open({type:'refund-execute',id:a.id})}>Execute refund</button>
      :<span className="muted">A cashier executes it</span>)}
  </div>;
}

export function BookingModal({id}:{id:string}){
  const {b,s,uid,permission,act,open,run,busy}=useBooking(id);
  if(!b)return <Missing/>;
  if(!rbacCanViewFolios(s,uid))return <Empty title="Access denied" body="Your role cannot open guest folios."/>;
  const readOnly=!permission(['billing','reservations']); // DEC-063 G4: the Accountant checks folios read-only
  const canCorrect=rbacCanRequestCorrection(s,uid);
  const t=totals(b),active=['Confirmed','In house'].includes(b.status),n=folioNights(b);
  const approvals=folioApprovalsFor(s,b.id);
  const closed=(d:string)=>s.closedDates.includes(d);
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · ROOM {b.room}{b.invoiceNo?` · INVOICE ${b.invoiceNo}`:''}</div><h2>{b.guest}</h2>
      <div className="inline">{(()=>{const st=resStatus(s,b);return <Pill tone={st==='In house'?'green':st==='Confirmed'?'blue':st==='Held'?'orange':'neutral'}>{st}</Pill>;})()}<span>{shortDate(b.arrival)} — {shortDate(b.departure)} · {plural(nights(b.arrival,b.departure),'night')} · {plural(b.people,'guest')}</span>
        <span className={'folio-nights '+(n.posted<n.agreed?'short':'')} title="Room nights are posted by the nightly audit">Nights posted {n.posted} of {n.agreed} agreed</span></div>
      {b.company&&<p className="folio-company"><Building2 size={14}/>{b.company.name}{b.company.gstin?` · ${process.env.HCC_BUILD==='production'?'GSTIN':'GSTIN (placeholder)'} ${b.company.gstin}`:''}</p>}</div>
    <div className="booking-actions">
      {b.status==='Confirmed'&&act('reservations',()=>open({type:'cancel',id:b.id}),'Cancel / no-show','btn subtle')}
      {b.status==='In house'&&act('checkout',()=>open({type:'checkout',id:b.id}),'Checkout','btn primary')}
      {active&&act('reservations',()=>open({type:b.status==='In house'?'stay-move':'move',id:b.id}),b.status==='In house'?'Move / extend stay':'Change dates / room','btn subtle')}
      {b.status==='Departed'&&canCorrect&&<button className="btn subtle" disabled={busy} onClick={()=>open({type:'credit-note',id:b.id})}>Request credit note</button>}
      <IssueInvoiceAction b={b}/>
      {bookingActionSlots.map((Slot,i)=><Slot key={i} b={b}/>)}
    </div>
    {readOnly&&<div className="info-note"><Info size={16}/>Read-only check (Accountant). You can request a correction or credit note; the Owner approves it.</div>}
    <div className="folio-totals"><div><span>Charges + tax</span><strong>{money(t.total)}</strong></div><div><span>Net paid / advance</span><strong>{money(t.paid)}</strong></div><div><span>Deposit held</span><strong>{money(t.deposit)}</strong></div><div className={t.balance>0?'balance-due':'balance-clear'}><span>{t.balance<0?'Credit balance':'Balance due'}</span><strong>{money(t.balance)}</strong></div></div>
    <div className="section-title compact"><h3>Guest folio</h3><div className="inline">{active&&act('billing',()=>open({type:'charge',id:b.id}),'Add charge','text-btn')}{active&&rbacCanDiscount(s,uid)&&<button className="text-btn" disabled={busy} onClick={()=>open({type:'discount',id:b.id})}>Discount</button>}{!['Held','Expired','Cancelled','No show'].includes(resStatus(s,b))&&act('payments',()=>open({type:'payment',id:b.id}),'Record payment','btn small-btn')}</div></div>
    <div className="table-wrap folio-table"><table><thead><tr><th>Description</th><th>Service date</th><th>Net amount</th><th>GST</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{b.lines.map(l=>{const reversed=folioLineReversed(b,l.id);
      return <tr key={l.id} className={reversed||l.reversalOf?'folio-line-muted':''}><td><strong>{l.label}</strong><small>{l.kind}{l.reversalOf?' · reverses '+l.reversalOf:''}{reversed?' · reversed':''}{closed(l.date)?' · closed date (read-only)':''}</small></td><td>{shortDate(l.date)}</td><td className="number">{money(l.amount)}</td><td className="number">{money(lineTax(l))}<small>{gstPct(l.taxBps)}</small></td>
        <td>{reversed?<Pill>Reversed</Pill>:l.reversalOf?<Pill tone="blue">Reversal</Pill>:active&&canCorrect&&folioLineCorrectable(s,b,l)?<button className="text-btn" disabled={busy} onClick={()=>open({type:'correction',id:b.id,room:l.id})}>Correct</button>:null}</td></tr>;})}</tbody></table>{!b.lines.length&&<Empty title="No charges yet" body="Room nights are posted by the nightly audit. Add a service charge if needed."/>}</div>
    <div className="section-title compact"><h3>Receipts & deposits</h3><span className="muted">Manual demonstration records</span></div>
    <div className="receipt-list">{b.receipts.map(r=>{const refundable=r.kind==='payment'?folioRefundable(s,b,r.id):0,refunded=r.kind==='payment'?folioRefunded(b,r.id):0;
      return <div key={r.id}><span className="receipt-icon"><Receipt size={16}/></span><div><strong>{folioReceiptLabel[r.kind]??r.kind} · {r.method}</strong><small>{r.id} · {shortDate(r.date)} · {r.reference}{r.refundOf?' · refund of '+r.refundOf:''}{r.tenderGroup?' · mixed tender '+r.tenderGroup:''}{refunded?' · refunded '+money(refunded):''} · by {userName(s,r.actor)}</small></div>
        <strong className="number">{['refund','deposit-return'].includes(r.kind)?'−':''}{money(r.amount)}</strong>
        {refundable>0&&permission('payments')&&(()=>{const locked=cbIsLocked(s,r.date),why=cbLockedMessage(r.date,'a refund against receipt '+r.id);return <><button className="text-btn" disabled={busy||locked} title={locked?why:undefined} onClick={()=>open({type:'refund',id:b.id,room:r.id})}><RotateCcw size={13}/> Request refund</button>{locked&&<small className="f-why">{why}</small>}</>;})()} {/* DEC-065 */}</div>;})}
      {!b.receipts.length&&<p className="muted padded">No receipts recorded.</p>}</div>
    {approvals.length>0&&<><div className="section-title compact"><h3>Folio requests</h3><span className="muted">No one approves their own request</span></div><div className="request-list folio-requests">{approvals.map(a=><FolioApprovalRow key={a.id} a={a}/>)}</div></>}
    <div className="modal-actions"><span className="footnote">GST per line at its snapshot rate (illustrative — CA to confirm). No real guest documents stored.</span><button className="btn" disabled={busy} onClick={()=>open({type:'invoice',id:b.id})}><Printer size={16}/>{b.invoiceNo?(process.env.HCC_BUILD==='production'?'Invoice ':'Sample invoice ')+b.invoiceNo:'Folio preview'}</button></div></>;
}
type Row={method:string;amount:string;reference:string};
const paise=(v:string)=>Math.round(Number(v||0)*100);
/** SCR-09 payment dialog: one or more tender rows summing to the amount, then a confirmation summary before posting. */
export function PaymentModal({id}:{id:string}){
  const {b,run,booking,modal,busy}=useBooking(id);
  const t=b?totals(b):undefined;
  const [kind,setKind]=useState('payment');
  const [total,setTotal]=useState(t&&t.balance>0?String(t.balance/100):'');
  const [rows,setRows]=useState<Row[]>([{method:'Cash',amount:'',reference:''}]);
  const [step,setStep]=useState<'form'|'confirm'>('form');
  const [problem,setProblem]=useState('');
  if(!b||!t)return <Missing/>;
  const single=rows.length===1;
  const effective=rows.map(r=>({...r,amount:single?total:r.amount}));
  const sum=effective.reduce((a,r)=>a+paise(r.amount),0),target=paise(total);
  const setRow=(i:number,p:Partial<Row>)=>setRows(rows.map((r,j)=>j===i?{...r,...p}:r));
  const check=()=>{
    if(!(target>0))return 'Enter an amount greater than zero.';
    if(effective.some(r=>!(paise(r.amount)>0)))return 'Every tender row needs an amount greater than zero.';
    if(sum!==target)return `Tender rows total ${money(sum)} but the amount is ${money(target)}. They must be equal.`;
    if(effective.some(r=>r.method!=='Cash'&&!r.reference.trim()))return 'Enter a demonstration transaction reference for each non-cash tender.';
    if(kind==='deposit-return'&&target>t.deposit)return 'Return cannot exceed the deposit held.';
    return '';
  };
  const next=(e:React.FormEvent)=>{e.preventDefault();const p=check();setProblem(p);if(!p)setStep('confirm');};
  const post=()=>{
    const cmd=single?{type:'PAYMENT',id:b.id,amount:target,kind,method:effective[0].method,reference:effective[0].reference.trim(),key:modal!.key}
      :{type:'PAYMENT',id:b.id,amount:target,kind,tenders:effective.map(r=>({method:r.method,amount:paise(r.amount),reference:r.reference.trim()})),key:modal!.key};
    run(cmd,single?'Receipt recorded once':`${rows.length} receipts recorded once`,{after:()=>booking(b.id)});
  };
  const kindLabel:Record<string,string>={payment:'Payment / advance against folio',deposit:'Receive refundable deposit','deposit-return':'Return refundable deposit'};
  if(step==='confirm')return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Confirm before posting</h2><p>{kindLabel[kind]} · {money(target)}</p></div>
    <div className="summary-list">{effective.map((r,i)=><div key={i}><span>{r.method}{r.reference.trim()?' · '+r.reference.trim():''}</span><strong>{money(paise(r.amount))}</strong></div>)}
      <div><span>Total</span><strong>{money(target)}</strong></div>
      {kind==='payment'&&<div><span>Balance after posting</span><strong>{money(t.balance-target)}</strong></div>}</div>
    <div className="info-note folio-unverified"><AlertTriangle size={16}/><span><strong>Manual entry — bank/UPI transaction not verified</strong>. This is a fictional demonstration receipt; no money moves. Retrying after an unclear result reuses the same request and never creates a second receipt.</span></div>
    <div className="modal-actions"><button type="button" className="btn" disabled={busy} onClick={()=>setStep('form')}>Edit</button><button className="btn primary" disabled={busy} onClick={post}>{busy?'Posting…':'Post '+(single?'receipt':rows.length+' receipts')} <Check size={16}/></button></div></>;
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Record a payment</h2><p>Balance {money(t.balance)} · Deposit held {money(t.deposit)}</p></div>
    <form className="form" onSubmit={next} noValidate>
      <label>Entry type<select name="kind" value={kind} onChange={e=>{setKind(e.target.value);if(e.target.value==='deposit-return')setRows(rows.slice(0,1));}}>{Object.entries(kindLabel).map(([k,v])=><option key={k} value={k}>{v}</option>)}</select></label>
      <label>Amount (₹)<input type="number" name="amount" min="0.01" step="0.01" value={total} onChange={e=>setTotal(e.target.value)} required/></label>
      <fieldset className="folio-tenders"><legend>Tender{single?'':'s'}</legend>
        {rows.map((r,i)=><div className="form-row folio-tender" key={i}>
          <label>Method<select value={r.method} onChange={e=>setRow(i,{method:e.target.value})}>{FOLIO_TENDERS.map(x=><option key={x}>{x}</option>)}</select></label>
          {!single&&<label>Amount (₹)<input type="number" min="0.01" step="0.01" value={r.amount} onChange={e=>setRow(i,{amount:e.target.value})} aria-label={`Tender ${i+1} amount`}/></label>}
          <label>Reference{r.method==='Cash'?' (optional)':''}<input value={r.reference} maxLength={100} placeholder={r.method==='Cash'?'Demo cash':'Demo transaction reference'} onChange={e=>setRow(i,{reference:e.target.value})}/></label>
          {!single&&<button type="button" className="icon-btn" aria-label={`Remove tender ${i+1}`} onClick={()=>setRows(rows.filter((_,j)=>j!==i))}><Trash2 size={16}/></button>}
        </div>)}
        <div className="inline">{kind!=='deposit-return'&&rows.length<4&&<button type="button" className="text-btn" onClick={()=>setRows(single?[{...rows[0],amount:total},{method:'UPI',amount:'',reference:''}]:[...rows,{method:'UPI',amount:'',reference:''}])}><Plus size={14}/> Split across another tender</button>}
          {!single&&<span className={'muted '+(sum!==target?'folio-mismatch':'')}>Allocated {money(sum)} of {money(target||0)}{sum!==target?` · ${money(target-sum)} left`:' ✓'}</span>}</div>
      </fieldset>
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="info-note"><Info size={16}/>Refunds are requested from the original receipt on the folio and need approval before a cashier executes them.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" disabled={busy}>Review <ArrowRight size={16}/></button></div></form></>;
}
export function ChargeModal({id,discount}:{id:string;discount:boolean}){
  const {b,s,uid,run,booking,modal,busy}=useBooking(id);
  if(!b)return <Missing/>;
  const lim=rbacDiscountLimit(s,uid); // DEC-063 G1: limit of the signed-in role (undefined = Owner, no limit)
  return <><div className="modal-heading"><h2>{discount?'Add a discount':'Add a service charge'}</h2><p>{b.guest} · {b.id}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run(discount?{type:'DISCOUNT',id:b.id,amount:amount(f),reason:field(f,'description'),key:modal!.key}:{type:'CHARGE',id:b.id,amount:amount(f),label:field(f,'description'),...(field(f,'gst')?{gst:field(f,'gst')}:{}),key:modal!.key},discount?'Discount recorded or submitted for approval':'Service charge posted',{after:()=>booking(b.id)}))}>
      <label>{discount?'Reason for discount':'Service description'}<input required name="description" maxLength={120} placeholder={discount?'e.g. Service recovery':'e.g. Laundry service'}/></label>
      <label>Amount before GST (₹)<input name="amount" type="number" min="0.01" step="0.01" required/></label>
      {!discount&&<label>GST category<select name="gst" defaultValue=""><option value="">Automatic (from the description)</option>{GST_CHARGE_CATEGORIES.map(c=><option key={c.id} value={c.id}>{c.label}</option>)}</select></label>}
      {discount&&<p className="footnote">GST on the discount follows the room-night rate ({gstPct(gstForFolioAdjustment(s,b,s.date).bps)}).</p>}
      {discount&&<div className="info-note"><ShieldCheck size={16}/>{lim===undefined?'No discount limit for the Owner — it posts at once.':<>Your limit is {money(lim)}. Above it, another authorized user must approve within 24 simulated hours.</>} Any folio change while it is pending invalidates the request.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>{discount?'Submit discount':'Post charge'}</button></div></form></>;
}
export function CheckoutModal({id}:{id:string}){
  const {b,s,run,booking,open,busy,permission}=useBooking(id);
  if(!b)return <Missing/>;
  const t=totals(b),n=folioNights(b),blockers=folioCheckoutBlockers(s,b,t),cc=folioCompanyCreditState(s,b,t.balance);
  const pending=folioApprovalsFor(s,b.id).filter(a=>folioApprovalStatus(s,a)==='Pending');
  return <><div className="modal-heading"><h2>Ready to say goodbye?</h2><p>Review {b.guest}’s folio before releasing Room {b.room}.</p></div>
    <div className="summary-list"><div><span>Room nights posted / agreed</span><strong>{n.posted} of {n.agreed}</strong></div><div><span>Total charges including GST</span><strong>{money(t.total)}</strong></div><div><span>Net payments</span><strong>{money(t.paid)}</strong></div><div><span>Outstanding balance</span><strong>{money(t.balance)}</strong></div><div><span>Deposit to return</span><strong>{money(t.deposit)}</strong></div><div><span>Pending approvals</span><strong>{pending.length?pending.map(a=>a.id).join(', '):'None'}</strong></div></div>
    {b.company&&t.balance>0&&<div className="info-note folio-credit"><Building2 size={16}/><div>
      <strong>Company credit · {b.company.name}</strong>
      {cc.state==='none'&&<p>Move the {money(t.balance)} balance to the company’s receivable. Needs approval: a Manager up to {money(FOLIO_MANAGER_CREDIT_LIMIT)}, an Owner above.</p>}
      {cc.state==='pending'&&<p>Requested as {cc.approval!.id} for {money(cc.approval!.amount)} — awaiting approval by another user.</p>}
      {cc.state==='approved'&&<p>Approved ({cc.approval!.id}) for exactly {money(cc.approval!.amount)}. It is applied when you confirm checkout.</p>}
      {cc.state==='stale'&&<p>Approval {cc.approval!.id} was for {money(cc.approval!.amount)}, but the folio has changed. Request again.</p>}
      {(cc.state==='none'||cc.state==='stale')&&(permission('checkout')
        ?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'COMPANY_CREDIT_REQUEST',id:b.id,key:`cc:${b.id}:${b.version}`},'Company credit requested — awaiting approval')}>Request company credit for {money(t.balance)}</button>
        :<p className="muted">Your role cannot request company credit.</p>)}
    </div></div>}
    {blockers.length>0?<div className="info-note folio-blockers" role="status"><AlertTriangle size={17}/><div><strong>Checkout is blocked until:</strong><ul>{blockers.map(x=><li key={x}>{x}</li>)}</ul></div></div>
      :<div className="info-note"><Info size={17}/>Checkout preserves all stay charges, marks the room dirty, creates exactly one housekeeping task and {process.env.HCC_BUILD==='production'?'then issues the tax invoice':'allocates a sample invoice number once'}. Repeating checkout returns the same result.</div>}
    <div className="modal-actions"><button className="btn" onClick={()=>booking(b.id)}>Review folio</button>
      {permission('payments')&&(t.balance>0||t.deposit>0)&&<button className="btn subtle" disabled={busy} onClick={()=>open({type:'payment',id:b.id})}>Settle / return deposit</button>}
      <button className="btn primary" disabled={busy} onClick={()=>run({type:'CHECKOUT',id:b.id,key:'checkout:'+b.id},'Checkout completed. Housekeeping task created.',{after:()=>booking(b.id)})}>Confirm checkout <ArrowRight size={16}/></button></div></>;
}
export function InvoiceModal({id}:{id:string}){
  const {b,s,booking}=useBooking(id);
  if(!b)return <Missing/>;
  const t=totals(b);
  return <><div className="invoice"><div className="invoice-top"><Hotel size={34}/><div><h2>{s.settings.hotel}</h2><p>{process.env.HCC_BUILD==='production'?'Hotel Control Centre':'Hotel Control Centre · Fictional property'}</p></div></div>
    {process.env.HCC_BUILD==='production'?<div className="invoice-stamp">Folio summary — the tax invoice is issued and kept on the hotel server</div>
      :<div className="invoice-stamp" aria-label="Demonstration — not a tax invoice">Demonstration — not a tax invoice · fictional records only</div>}
    <div className="invoice-meta"><div><small>GUEST</small><h3>{b.guest}</h3><p>Room {b.room} · {plural(b.people,'guest')}</p>{b.company&&<p>Bill to: {b.company.name}{b.company.gstin?<><br/>GSTIN (placeholder): {b.company.gstin}</>:null}</p>}</div>
      <div><small>{b.invoiceNo?(process.env.HCC_BUILD==='production'?'TAX INVOICE NO.':'SAMPLE INVOICE NO.'):'FOLIO PREVIEW'}</small><h3>{b.invoiceNo??'Not yet issued'}</h3><p>{process.env.HCC_BUILD==='production'?(b.invoiceNo?'Issued by the hotel server':'Issued after checkout'):b.invoiceNo?'Allocated once at checkout':'A number is allocated once at checkout'}</p><IssueInvoiceAction b={b} className="btn small-btn no-print"/><p>{shortDate(b.arrival)} — {shortDate(b.departure)} · {b.id}</p></div></div>
    <table><thead><tr><th>Description</th><th>Date</th><th>Net</th><th>GST rate</th><th>CGST</th><th>SGST</th></tr></thead><tbody>{b.lines.map(l=>{const g=gstLineSplit(l);return <tr key={l.id}><td>{l.label}</td><td>{shortDate(l.date)}</td><td>{money(l.amount)}</td><td>{gstPct(l.taxBps)}</td><td>{money(g.cgst)} <small>@{gstHalfPct(l.taxBps)}</small></td><td>{money(g.sgst)} <small>@{gstHalfPct(l.taxBps)}</small></td></tr>;})}</tbody></table>
    <div className="summary-list invoice-summary"><div><span>Net charges</span><strong>{money(t.net)}</strong></div><div><span>CGST</span><strong>{money(gstSplitLines(b.lines).cgst)}</strong></div><div><span>SGST</span><strong>{money(gstSplitLines(b.lines).sgst)}</strong></div><div><span>Total including GST</span><strong>{money(t.total)}</strong></div><div><span>Net receipts applied</span><strong>{money(t.paid)}</strong></div>
      {b.receipts.some(r=>r.kind==='company-credit')&&<div><span>On company credit (receivable)</span><strong>{money(b.receipts.filter(r=>r.kind==='company-credit').reduce((a,r)=>a+r.amount,0))}</strong></div>}
      <div><span>Balance</span><strong>{money(t.balance)}</strong></div><div><span>Refundable deposit held separately</span><strong>{money(t.deposit)}</strong></div></div>
    {!!b.creditNotes?.length&&<div className="folio-cn-list"><small>{process.env.HCC_BUILD==='production'?'CREDIT NOTES':'SAMPLE CREDIT NOTES'}</small>{b.creditNotes.map(c=><p key={c.id}>{c.no} · {shortDate(c.date)} · {money(c.amount)} before tax · {c.reason}</p>)}</div>}
    <p className="footnote">{process.env.HCC_BUILD==='production'?'GST from the dated GST master, intra-state (CGST + SGST), per line, half away from zero. The issued tax invoice is the hotel server’s document.':'GST from the dated GST master, intra-state (CGST + SGST), per line, half away from zero — illustrative, CA to confirm. All names, transactions and references are fictional. Demonstration — not a tax invoice.'}</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}

/** Refund request (open with {type:'refund', id: bookingId, room: receiptId}). */
export function RefundRequestModal({id,room}:{id?:string;room?:string}){
  const {b,s,run,booking,modal,busy}=useBooking(id??'');
  const r=b?.receipts.find(r=>r.id===room);
  if(!b||!r)return <Missing/>;
  const max=folioRefundable(s,b,r.id),departedCredit=b.status==='Departed'?Math.max(0,-totals(b).balance):undefined;
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Request a refund</h2><p>Against {r.id} · {r.method} · {money(r.amount)} on {shortDate(r.date)}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'REFUND_REQUEST',id:b.id,receiptId:r.id,amount:amount(f),reason:field(f,'reason'),key:modal!.key},'Refund requested — awaiting approval by another user',{after:()=>booking(b.id)}))}>
      <label>Refund amount (₹)<input name="amount" type="number" min="0.01" step="0.01" max={max/100} defaultValue={(()=>{const bal=totals(b).balance;return bal<0?Math.min(max,-bal)/100:'';})()} required/></label>
      <label>Reason<input name="reason" required maxLength={120} placeholder="e.g. Overpayment at check-in"/></label>
      <div className="info-note"><ShieldCheck size={16}/><span>Refundable remainder of this receipt: <strong>{money(max)}</strong>.{departedCredit!==undefined?` Departed stay: refunds are limited to the credit balance (${money(departedCredit)}); issue an approved credit note first if needed.`:''} A Manager or Owner other than you approves it; a cashier then executes it. No money moves until execution.</span></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy||max<=0}>{max<=0?'Nothing left to refund':'Submit refund request'}</button></div></form></>;
}
/** Execute an approved refund (open with {type:'refund-execute', id: approvalId}). */
export function RefundExecuteModal({id}:{id?:string}){
  const {s,run,booking,modal,busy}=useHcc();
  const a=s.approvals.find(a=>a.id===id&&a.kind==='refund'),b=a&&s.bookings.find(b=>b.id===a.booking),r=b?.receipts.find(r=>r.id===a!.target);
  const [method,setMethod]=useState(r?.method??'Cash');
  if(!a||!b||!r)return <Missing/>;
  return <><div className="modal-heading"><div className="eyebrow">{a.id} · {b.id} · {b.guest}</div><h2>Execute approved refund</h2><p>{money(a.amount)} against {r.id} ({r.method}) · approved by {userName(s,a.approver)}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'REFUND_EXECUTE',approvalId:a.id,method,reference:field(f,'reference'),key:modal!.key},'Refund executed and linked to the original receipt',{after:()=>booking(b.id)}))}>
      <label>Refund via<select value={method} onChange={e=>setMethod(e.target.value)}>{FOLIO_TENDERS.map(x=><option key={x}>{x}</option>)}</select></label>
      <label>Reference{method==='Cash'?' (optional)':''}<input name="reference" maxLength={100} required={method!=='Cash'} placeholder={'Refund of '+r.id}/></label>
      <div className="info-note folio-unverified"><AlertTriangle size={16}/><span><strong>Manual entry — bank/UPI transaction not verified</strong>. Cash refunds reduce the expected drawer.</span></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" disabled={busy}>Execute refund {money(a.amount)}</button></div></form></>;
}
/** Correction request for a posted line (open with {type:'correction', id: bookingId, room: lineId}). */
export function CorrectionModal({id,room}:{id?:string;room?:string}){
  const {b,run,booking,modal,busy}=useBooking(id??'');
  const l=b?.lines.find(l=>l.id===room);
  if(!b||!l)return <Missing/>;
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}</div><h2>Request a correction</h2><p>{l.label} · {shortDate(l.date)} · {money(l.amount)}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'CORRECTION_REQUEST',id:b.id,lineId:l.id,reason:field(f,'reason'),key:modal!.key},'Correction requested — awaiting approval',{after:()=>booking(b.id)}))}>
      <label>Reason<input name="reason" required maxLength={120} placeholder="e.g. Posted to the wrong folio"/></label>
      <div className="info-note"><Info size={16}/>Posted lines are never edited. On approval, a reversal line of {money(-l.amount)} is posted on today’s business date with the original tax snapshot, linked to this line. Post the correct charge separately if needed.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>Submit correction request</button></div></form></>;
}
/** Sample credit note for a departed stay (open with {type:'credit-note', id: bookingId}). */
export function CreditNoteModal({id}:{id?:string}){
  const {b,s,run,booking,modal,busy}=useBooking(id??'');
  if(!b)return <Missing/>;
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · {b.guest}{b.invoiceNo?' · '+b.invoiceNo:''}</div><h2>Request a sample credit note</h2><p>Net charges {money(totals(b).net)}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'CREDIT_NOTE',id:b.id,amount:amount(f),reason:field(f,'reason'),key:modal!.key},'Credit note requested — awaiting approval',{after:()=>booking(b.id)}))}>
      <label>Credit amount before GST (₹)<input name="amount" type="number" min="0.01" step="0.01" required/></label>
      <label>Reason<input name="reason" required maxLength={120} placeholder="e.g. Service recovery after departure"/></label>
      <div className="info-note"><ShieldCheck size={16}/>Needs approval by another user. On approval a sample number from DEMO-CN/2026-27/ is issued with GST at the stay’s room-night rate ({gstPct(gstForFolioAdjustment(s,b,s.date).bps)}, CGST + SGST); a refund against the resulting credit can then be requested.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>Submit credit note request</button></div></form></>;
}
