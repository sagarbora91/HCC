// Stream C · Payments & folio (owns PAYMENT, CHARGE, DISCOUNT, APPROVAL, CHECKOUT; adds REFUND_REQUEST, REFUND_EXECUTE,
// COMPANY_CREDIT_REQUEST, CORRECTION_REQUEST, CREDIT_NOTE). Replaces the built-in versions of the owned commands;
// existing error message texts are kept (tests match on them).
import type {Handler,Kit} from '../commands';
import {HccError} from '../../../domain/errors';
import {deskCheckoutLate} from './desk'; // LF-DESK
import type {Approval,Booking,Receipt} from '../../../domain/types';
import {money,addDays} from '../../../domain/money';
import {totals} from '../../../domain/selectors';
import {gstForCharge,gstCategoryOfLabel,gstForFolioAdjustment,gstExactPieces,GST_CHARGE_CATEGORIES,gstAdjustmentMixed,GST_ADJUSTMENT_MIXED_MSG} from '../../../domain/gst'; // F8-INT: H2
import {rbacCanDiscount,rbacDiscountLimit,rbacApprovalLimit,rbacCanRequestCorrection,RBAC_OWNER_DECIDES} from '../../../domain/streams/rbac';
import {cbIsLocked,cbLockedMessage} from '../../../domain/streams/cashbook';
import {mnyMethod,mnyMethodFor,mnyIsDrawer} from '../../../domain/streams/money-master';
import {mstFail} from '../../../domain/streams/masters';
import {recentEqualReceipt,folioDupMessage,FOLIO_DUP_REASON,FOLIO_MANAGER_CREDIT_LIMIT,folioExpiresAt,folioKind,folioApprovalExpired,folioRefundable,folioOutstandingRefunds,folioLineCorrectable} from '../../../domain/streams/folio';
import {fdmAllocate} from '../../../domain/streams/frontdesk-master';
import {closingReopened} from '../../../domain/streams/closing';
import {grpAccountOf,grpExposure,grpBookingExposure,grpCreditRefusal} from '../../../domain/streams/groups';

/**
 * DEC-076 (audit A03a-05): company credit needs a booking billed to a company account, and the balance must fit beside everything else
 * billed to that company (its exposure without this stay); checked at the request, the approval and the checkout. No override.
 */
function companyCreditCheck(k:Kit,x:Booking,amount:number){
  const c=grpAccountOf(k.s,x);
  if(!c)k.fail('INVALID_TRANSITION','This booking is not billed to a company account. Link the booking to the company account before requesting company credit.');
  const refusal=grpCreditRefusal(c!,grpExposure(k.s,c!)-grpBookingExposure(k.s,x),amount,'stay on company credit');
  if(refusal)k.fail('INVALID_TRANSITION',refusal);
}
// F8-SF-DOMAIN · split folio in the folio commands (SF-CONTRACT §3, §4, §5, §6, §7). A single-folio stay takes none of these paths (I1).
import {sfIsSplit,sfView,sfFolio,sfFolios,sfCmdFolio,sfCompanyFolio,sfRouteTarget,sfNewLineFolio,SF_PRIMARY} from '../../../domain/streams/folio-split';
import {sfFail} from './folio-split';
import {grpCompany,sfFolioExposure} from '../../../domain/streams/groups';
import {adminLimitFor} from '../../../domain/streams/admin';
/** The folio a command names (`cmd.folio`), else `fallback`; refused SF_FOLIO_UNKNOWN when it is not an open folio of the stay. */
function cmdFolio(k:Kit,x:Booking,fallback:string=SF_PRIMARY):string{
  const f=sfCmdFolio(x,k.cmd.folio);
  if(f===false)sfFail('NOT_FOUND','SF_FOLIO_UNKNOWN',`This stay has no open folio ${String(k.cmd.folio)}.`);
  return f||fallback;
}
const stamp=(x:Booking,folio:string)=>x.folios?.length?{folio}:{};
const metaFolio=(a:Approval)=>String((a.meta as Record<string,unknown>|undefined)?.folio??SF_PRIMARY);
/** D7 on a split stay's company folio (§7.3): `used` = the company's exposure without this folio; refusal text as grpCreditRefusal. */
function sfCompanyCreditCheck(k:Kit,x:Booking,folio:string,amount:number){
  const f=sfFolio(x,folio);
  if(f?.payer.kind!=='company')sfFail('VALIDATION','SF_COMPANY_FOLIO','Company credit goes on the company folio only.');
  const c=grpCompany(k.s,f!.payer.ref);
  if(!c)k.fail('INVALID_TRANSITION','The company account of this folio no longer exists.');
  const refusal=grpCreditRefusal(c!,Math.max(0,grpExposure(k.s,c!)-sfFolioExposure(k.s,x,folio)),amount,'split folio');
  if(refusal)k.fail('INVALID_TRANSITION',refusal);
}
// F8-SF-DOMAIN end
import {strCanApprove,strRefusal,strOverThreshold,strThresholdBps,strAgreedRate} from '../../../domain/streams/stay-rate'; // LF-RATE (+ AF2-CTL01)
import {strApply} from './stay-rate'; // LF-RATE
import {blcExecute} from './bill-correction'; // LF-BILL
import {blcCorrections} from '../../../domain/streams/bill-correction'; // LF-BILL
import {CTR_VOID_KIND,ctrVoidExecute,gcrApprove,gcrCheckout} from './counter'; // LF-COUNTER
import {cxCashBlocked} from '../../../domain/streams/cashout';
import {accCashEnforce} from '../../../domain/streams/accounting-guards'; // ACC-2 flows (contract §7, B9)
import {chnChannelOfBooking,chnOtaCreditRefusal} from '../../../domain/streams/channels'; // ACC-2 flows (contract §4, A13)

const DEPARTED_REFUND_MSG='A settled departed stay requires a credit note before a refund. Issue an approved credit note first.';
const STALE_MSG='The folio changed after this request. Reject it and request again.';

/** New approval request bound to the folio version, expiring 24 simulated hours from now. */
function request(k:Kit,x:Booking,fields:Pick<Approval,'amount'|'reason'|'kind'>&Partial<Approval>){
  const a:Approval={id:k.id('APR'),booking:x.id,actor:k.uid,version:x.version,status:'Pending',expiresAt:folioExpiresAt(k.s.clock),meta:{command:'APPROVAL'},...fields};
  // Drop undefined fields so state survives a JSON round trip unchanged (persistence tests use deepEqual).
  const clean=(o:Record<string,unknown>)=>{for(const key of Object.keys(o))if(o[key]===undefined)delete o[key];};
  clean(a as unknown as Record<string,unknown>);if(a.meta)clean(a.meta);
  k.s.approvals.push(a);return a;
}
/** A pending, unexpired request of `kind` on the stay. F8D-LOW (money review L3): on a split stay with `folio`, only a request on that folio
 *  (meta.folio, absent = A) blocks: each folio keeps its own one-pending-request rule (the server keeps approvals per folio). The approval is
 *  still bound to the stay's version, so deciding one folio's request makes another folio's open request stale (re-request), as any other
 *  posting on the stay does today. CHECKOUT still refuses while any folio has a pending request (SF-CONTRACT §4). */
const pendingOf=(k:Kit,x:Booking,kind:string,folio?:string)=>k.s.approvals.some(a=>a.booking===x.id&&folioKind(a)===kind&&a.status==='Pending'&&!folioApprovalExpired(k.s,a)
  &&(folio===undefined||!sfIsSplit(x)||metaFolio(a)===folio));
/** MA-3: the method must be an active payment method usable at the front desk (money masters; seed = the old FOLIO_TENDERS).
 *  A refund may go back through the original receipt's method even if it was retired since (`original`). Returns the stored name. */
const validMethod=(k:Kit,method:unknown,reference:unknown,original?:string):string=>{
  const row=typeof method==='string'?mnyMethod(k.s,method):undefined;
  if(row&&!row.active&&(row.usableAt??[]).includes('frontdesk')&&!(original&&mnyMethod(k.s,original)?.id===row.id))mstFail('MASTER_RETIRED',`${row.name} is retired — choose an active payment method.`);
  const m=mnyMethodFor(k.s,method,'frontdesk')??(original&&row&&mnyMethod(k.s,original)?.id===row.id?row:undefined);
  k.assert(m,'Select a payment method.');
  if(mnyIsDrawer(k.s,m!.name)){
    // DEC-078 (Wave B1): after the next day opens no shift is open until the cashier opens their own; one counted shift per demo day.
    k.assert(k.s.shiftOpen,k.s.shiftClose||k.s.closedDates.includes(k.s.date)?'The cash shift is closed. Open the next business day first.':'The cash shift is closed. Open a cashier shift first.','INVALID_TRANSITION');
    const own=cxCashBlocked(k.s,k.uid);if(own)k.fail('FORBIDDEN',own); // own shift (PERM-PAY-001 scope S for Reception / Cashier)
  }
  if(m!.needsReference)k.assert(String(reference??'').trim(),'Enter a demonstration transaction reference.');
  return m!.name;
};

// AF-PAY08 (AF-DEC-1): the server's duplicate-receipt rule on s.clock; `confirmDuplicate` bypasses it. Runs after execute()'s idempotent same-key check.
function dupGuard(k:Kit,x:Booking,kind:string,rows:{method:string;amount:number}[]){
  if(k.cmd.confirmDuplicate===true)return;
  const hit=recentEqualReceipt(x,rows,k.s.clock,kind);if(!hit)return;
  const at=(hit.receipt as Receipt&{at?:string}).at!;
  throw Object.assign(new HccError('VALIDATION',folioDupMessage(hit.receipt.method,kind,money(hit.receipt.amount),at,hit.minutes,hit.receipt.id)),{details:{reason:FOLIO_DUP_REASON,receiptId:hit.receipt.id}});
}
// AF-PAY08 end

const PAYMENT:Handler=k=>{
  // PERM-PAY-001/002 (coarse: payments)
  const {s,cmd,req,b,open,positive,assert,mutable,fail,id,uid}=k;
  req('payments');const x=b();open();positive(cmd.amount);
  const kind:Receipt['kind']=cmd.kind||'payment';
  // LF-BILL · PAYMENT on a departed folio for exactly the correction balance (§4)
  const lfCorr=x.status==='Departed'?blcCorrections(s,x.id).filter(c=>c.status==='Issued').at(-1):undefined; // otherwise the existing rules answer
  if(lfCorr&&lfCorr.newInvoiceNo===x.invoiceNo){
    const corr=lfCorr,bal=totals(x).balance;
    assert(bal>0,'There is no balance due on this departed stay.','INVALID_TRANSITION');
    assert(kind==='payment','Only a payment can be received on a departed stay.','INVALID_TRANSITION');
    assert(cmd.amount===bal,`Receive exactly the balance due after the bill correction: ${money(bal)}.`);
    assert(!Array.isArray(cmd.tenders)||cmd.tenders.length<=1,'Receive the correction balance with a single tender.');
    const method=validMethod(k,cmd.method,cmd.reference);
    dupGuard(k,x,'payment',[{method,amount:cmd.amount}]); // AF-PAY08
    if(mnyIsDrawer(s,method))accCashEnforce(s,{kind:'receipt',party:'booking:'+x.id,scope:'booking:'+x.id,amount:cmd.amount});
    x.receipts.push(Object.assign({id:id('RCPT'),amount:cmd.amount,method,date:s.date,kind:'payment' as const,reference:'Bill correction '+corr!.id+(String(cmd.reference??'').trim()?' · '+String(cmd.reference).trim():''),actor:uid},{at:s.clock})); // AF-PAY08: at = s.clock
    x.version++;
    return `${x.id} · payment ${money(cmd.amount)} · balance after bill correction ${corr!.id} settled`;
  }
  // LF-BILL end
  if(kind==='refund')fail('INVALID_TRANSITION','Refunds are no longer posted directly. Use “Request refund” on the original receipt; after approval, execute the refund.');
  assert(['payment','deposit','deposit-return'].includes(kind),'Invalid receipt type.');
  if(['payment','deposit'].includes(kind))mutable(x);
  const tenders:{method:string;amount:number;reference:string}[]=Array.isArray(cmd.tenders)&&cmd.tenders.length
    ?cmd.tenders.map((t:any)=>({method:t?.method,amount:t?.amount,reference:String(t?.reference??'').trim()}))
    :[{method:cmd.method,amount:cmd.amount,reference:String(cmd.reference??'').trim()}];
  assert(tenders.length<=4,'Use at most four tender rows.');
  if(tenders.length>1)assert(kind!=='deposit-return','Return a deposit with a single tender.');
  for(const t of tenders){positive(t.amount);t.method=validMethod(k,t.method,t.reference);}
  const sum=tenders.reduce((a,t)=>a+t.amount,0);
  assert(sum===cmd.amount,`Tender rows total ${money(sum)} but the payment amount is ${money(cmd.amount)}. They must be equal.`);
  if(kind==='deposit-return'){const ld=x.receipts.find(r=>r.kind==='deposit'&&cbIsLocked(s,r.date));assert(!ld,cbLockedMessage(ld?.date??'','returning a deposit taken on that day'),'DAY_CLOSED');} // DEC-065
  if(kind==='deposit-return')assert(cmd.amount<=totals(x).deposit,'Return cannot exceed the deposit held.');
  // ACC-2 flows (contract §7, rulebook B9): cash from the guest at or above ₹2,00,000 on a day or for the booking is refused (CASH_LIMIT).
  if(kind!=='deposit-return'){const cash=tenders.filter(t=>mnyIsDrawer(s,t.method)).reduce((a,t)=>a+t.amount,0);if(cash)accCashEnforce(s,{kind:'receipt',party:'booking:'+x.id,scope:'booking:'+x.id,amount:cash});}
  // AF-PAY08: after the hard cash cap (ACC-2 B9, a refusal of its own) so a cap test keeps its own message; before anything is written
  // F8-SF-DOMAIN · PAYMENT {folio?}: receipt table (SF-CONTRACT §3) and the duplicate guard per folio
  const fol=cmdFolio(k,x);
  if(sfIsSplit(x)){
    if(fol!==SF_PRIMARY&&(kind==='deposit'||kind==='deposit-return'))sfFail('VALIDATION','SF_DEPOSIT_FOLIO','Deposits are held on folio A only.');
    if(sfFolio(x,fol)?.payer.kind==='company'&&tenders.some(t=>mnyIsDrawer(s,t.method)))sfFail('VALIDATION','SF_COMPANY_FOLIO','A company folio is not paid in cash (DEC-076): use bank transfer, UPI or card.');
  }
  dupGuard(k,sfView(x,fol),kind,tenders);
  // F8-SF-DOMAIN end
  const group=tenders.length>1?id('TND'):undefined;
  for(const t of tenders){
    const r:Receipt={id:id('RCPT'),amount:t.amount,method:t.method,date:s.date,kind,reference:t.reference||'Demo cash',actor:uid};
    if(group)r.tenderGroup=group;
    if(x.folios?.length)r.folio=fol; // F8-SF-DOMAIN
    x.receipts.push(Object.assign(r,{at:s.clock})); // AF-PAY08: at = s.clock (the duplicate guard)
  }
  x.version++;
  return x.id+' · '+kind+' · '+money(cmd.amount)+(tenders.length>1?` · ${tenders.length} tenders (${tenders.map(t=>t.method).join(' + ')})`:'')+' · manual entry, not verified';
};

const CHARGE:Handler=k=>{const {s,cmd,req,b,financial,positive,assert,id}=k; // F8-SF-DOMAIN: kit kept for cmdFolio
  // PERM-FOL-002
  req('billing');const x=b();financial(x);positive(cmd.amount);assert(cmd.label?.trim(),'Describe the service.');
  // DEC-069: GST by supply — an explicit category (food / laundry / banquet / other) or one read from the description.
  assert(cmd.gst===undefined||GST_CHARGE_CATEGORIES.some(c=>c.id===cmd.gst),'Choose a GST category: food, laundry, banquet or other.');
  // DEC-077: `date` may name the business day the Owner reopened (a missed charge posts on it at that day's GST rate); any other earlier
  // date stays closed (adjustment-only), its cash-book lock (DEC-065) still applies, and the date must fall within the stay.
  let date=s.date;
  if(cmd.date!==undefined&&cmd.date!==null&&cmd.date!==s.date){
    date=String(cmd.date);assert(/^\d{4}-\d{2}-\d{2}$/.test(date),'Choose a valid service date.');
    assert(date<s.date,'A charge cannot be posted ahead of the business date.');
    assert(closingReopened(s)?.date===date,`Business day ${date} is closed. Corrections post in the open day.`,'DAY_CLOSED');
    assert(!cbIsLocked(s,date),cbLockedMessage(date,'a charge on that day'),'DAY_CLOSED');
    assert(x.arrival<=date&&date<=x.departure,'The service date is outside the stay.','INVALID_TRANSITION');
  }
  const fol=sfIsSplit(x)?cmdFolio(k,x,sfRouteTarget(x,'extras')):undefined; // F8-SF-DOMAIN: CHARGE {folio?} else route(extras); D7 in the routing hook
  x.lines.push({id:id('line'),label:cmd.label.trim(),amount:cmd.amount,date,kind:'service',taxBps:gstForCharge(s,date,cmd.gst??gstCategoryOfLabel(cmd.label)).bps,...(fol?{folio:fol}:{})});x.version++;
  return x.id+' · '+cmd.label+(date!==s.date?` · on reopened day ${date}`:'');
};

const DISCOUNT:Handler=k=>{
  // PERM-FOL-003 (+ PERM-APR-001 above the limit). DEC-063 G1: Owner, Manager, Reception only (never the Cashier);
  // own limit per role (Reception ≤ hotel threshold ₹250, Manager ≤ ₹1,000, Owner none).
  const {s,cmd,req,b,financial,positive,assert,id,uid,fail}=k;
  req('billing');if(!rbacCanDiscount(s,uid))fail('FORBIDDEN','Access denied: discounts are given by Reception, a Manager or the Owner — not at the cash counter (DEC-063).');
  const x=b();financial(x);positive(cmd.amount);assert(cmd.reason?.trim(),'A reason is required.');
  const fol=sfIsSplit(x)?cmdFolio(k,x,sfRouteTarget(x,'room')):SF_PRIMARY,xv=sfView(x,fol); // F8-SF-DOMAIN: cap = that folio's net
  assert(cmd.amount<=totals(xv).net,'Discount cannot exceed existing net charges.');
  assert(!gstAdjustmentMixed(xv),GST_ADJUSTMENT_MIXED_MSG); // F8-INT (H2)
  const limit=rbacDiscountLimit(s,uid);
  if(limit!==undefined&&cmd.amount>limit){
    assert(!pendingOf(k,x,'discount',fol),'A discount request is already pending.','INVALID_TRANSITION');
    request(k,x,{amount:cmd.amount,reason:cmd.reason,kind:undefined,...(sfIsSplit(x)?{meta:{command:'APPROVAL',folio:fol}}:{})}); // F8-SF-DOMAIN: meta.folio
    return x.id+' · approval requested';
  }
  x.lines.push({id:id('discount'),label:'Discount · '+cmd.reason,amount:-cmd.amount,date:s.date,kind:'discount',taxBps:gstForFolioAdjustment(s,xv,s.date).bps,...stamp(x,fol)});x.version++; // F8-SF-DOMAIN
  return x.id+' · discount posted';
};

const REFUND_REQUEST:Handler=k=>{
  // PERM-PAY-003 refund.request (coarse: payments)
  const {s,cmd,req,b,open,positive,assert}=k;
  req('payments');const x=b();open();positive(cmd.amount);
  assert(String(cmd.reason??'').trim(),'Enter a reason for the refund.');
  const r=x.receipts.find(r=>r.id===cmd.receiptId);
  assert(r,'Select the original receipt to refund.','NOT_FOUND');
  assert(r!.kind==='payment','Only a payment / advance receipt can be refunded. Deposits are returned with “Return deposit”.','INVALID_TRANSITION');
  assert(!cbIsLocked(s,r!.date),cbLockedMessage(r!.date,'a refund against receipt '+r!.id),'DAY_CLOSED'); // DEC-065
  const remaining=folioRefundable(s,x,r!.id);
  assert(cmd.amount<=remaining,`Refund cannot exceed the refundable remainder of ${r!.id} (${money(remaining)}).`);
  if(x.status==='Departed')assert(cmd.amount<=Math.max(0,-totals(sfView(x,r!.folio??SF_PRIMARY)).balance)-folioOutstandingRefunds(s,x.id,'',x,r!.folio??SF_PRIMARY),DEPARTED_REFUND_MSG,'INVALID_TRANSITION'); // F8-SF-DOMAIN: that folio's balance; F8D-LOW (L2): that folio's outstanding refunds
  const a=request(k,x,{amount:cmd.amount,reason:String(cmd.reason).trim(),kind:'refund',target:r!.id});
  return `${x.id} · refund of ${money(cmd.amount)} against ${r!.id} requested · ${a.id}`;
};

const REFUND_EXECUTE:Handler=k=>{
  // PERM-PAY-004 refund.execute (approved only; coarse: payments)
  const {s,cmd,req,open,assert,fail,id,uid}=k;
  req('payments');open();
  const a=s.approvals.find(a=>a.id===cmd.approvalId&&a.kind==='refund');
  assert(a,'Refund request not found.','NOT_FOUND');
  if(a!.status==='Executed')fail('INVALID_TRANSITION','This refund has already been executed.');
  assert(a!.status==='Approved','Only an approved refund request can be executed.','INVALID_TRANSITION');
  assert(!folioApprovalExpired(s,a!),'This approval has expired. Request the refund again.','INVALID_TRANSITION');
  assert(a!.actor!==uid,'The person who requested a refund cannot also execute it (DEC-031). Switch to another cashier or manager.','FORBIDDEN');
  const x=s.bookings.find(b=>b.id===a!.booking);assert(x,'Booking not found.','NOT_FOUND');
  const r=x!.receipts.find(r=>r.id===a!.target);assert(r,'The original receipt no longer exists.','NOT_FOUND');
  assert(!cbIsLocked(s,r!.date),cbLockedMessage(r!.date,'executing a refund against receipt '+r!.id),'DAY_CLOSED'); // DEC-065
  assert(a!.amount<=folioRefundable(s,x!,r!.id,a!.id),'Refund cannot exceed the refundable remainder of this receipt.','INVALID_TRANSITION');
  if(x!.status==='Departed')assert(a!.amount<=Math.max(0,-totals(sfView(x!,r!.folio??SF_PRIMARY)).balance)-folioOutstandingRefunds(s,x!.id,a!.id,x!,r!.folio??SF_PRIMARY),DEPARTED_REFUND_MSG,'INVALID_TRANSITION'); // F8-SF-DOMAIN: that folio's balance; F8D-LOW (L2): that folio's outstanding refunds
  const method=validMethod(k,cmd.method||r!.method,cmd.reference??('Refund of '+r!.id),r!.method);
  const rid=id('RCPT');
  x!.receipts.push({id:rid,amount:a!.amount,method,date:s.date,kind:'refund',reference:String(cmd.reference??'').trim()||('Refund of '+r!.id),actor:uid,refundOf:r!.id,approvalId:a!.id});
  a!.status='Executed';a!.meta={...a!.meta,executedBy:uid,executedAt:s.clock,receipt:rid};
  x!.version++;
  return `${x!.id} · refund ${money(a!.amount)} executed against ${r!.id} (${a!.id})`;
};

const COMPANY_CREDIT_REQUEST:Handler=k=>{
  // PERM-STY-003 / company credit request (coarse: checkout)
  const {s,cmd,req,b,open,assert}=k;
  req('checkout');open();const x=b();
  assert(x.status==='In house','This stay is not in house.','INVALID_TRANSITION');
  // F8-SF-DOMAIN · COMPANY_CREDIT_REQUEST {folio?} on a split stay: the company folio's balance, D7 on that folio (SF-CONTRACT §4, §7)
  if(sfIsSplit(x)){
    const cf=sfCompanyFolio(x),fol=cmdFolio(k,x,cf?.id??SF_PRIMARY);
    if(!cf||fol!==cf.id)sfFail('VALIDATION','SF_COMPANY_FOLIO','Company credit goes on the company folio only. Open a company folio (Split bill) first.');
    const bal=totals(sfView(x,fol)).balance;
    assert(bal>0,'There is no outstanding balance to place on company credit.','INVALID_TRANSITION');
    assert(!pendingOf(k,x,'company-credit',fol),'A company-credit request is already pending.','INVALID_TRANSITION');
    sfCompanyCreditCheck(k,x,fol,bal);
    const a=request(k,x,{amount:bal,reason:String(cmd.reason??'').trim()||('Company credit · '+cf!.payer.name),kind:'company-credit',meta:{command:'APPROVAL',company:cf!.payer.name,gstin:cf!.payer.gstin,folio:fol}});
    return `${x.id} · folio ${fol} · company credit ${money(bal)} requested for ${cf!.payer.name} · ${a.id}`;
  }
  // F8-SF-DOMAIN end
  const account=grpAccountOf(s,x),company=x.company?.name?.trim()||account?.name;
  assert(company,'Add a company to this booking before requesting company credit.');
  const bal=totals(x).balance;
  assert(bal>0,'There is no outstanding balance to place on company credit.','INVALID_TRANSITION');
  assert(!pendingOf(k,x,'company-credit'),'A company-credit request is already pending.','INVALID_TRANSITION');
  companyCreditCheck(k,x,bal);
  const a=request(k,x,{amount:bal,reason:String(cmd.reason??'').trim()||('Company credit · '+company),kind:'company-credit',meta:{command:'APPROVAL',company:company!,gstin:x.company?.gstin??account?.gstin}});
  return `${x.id} · company credit ${money(bal)} requested for ${company} · ${a.id}`;
};

const CORRECTION_REQUEST:Handler=k=>{
  // PERM-FOL-005 folio.correction: DEC-063 G3 — Reception, Cashier, Accountant, Manager (and Owner) request; Owner approves.
  const {s,cmd,b,financial,assert,fail,uid}=k;
  if(!rbacCanRequestCorrection(s,uid))fail('FORBIDDEN','Access denied: billing permission or the Accountant role is required.');const x=b();financial(x);
  assert(String(cmd.reason??'').trim(),'A reason is required.');
  const l=x.lines.find(l=>l.id===cmd.lineId);assert(l,'Select the posted line to correct.','NOT_FOUND');
  assert(!l!.reversalOf,'A reversal line cannot itself be reversed.','INVALID_TRANSITION');
  assert(folioLineCorrectable(s,x,l!),'This line is already reversed or has a correction pending.','INVALID_TRANSITION');
  const a=request(k,x,{amount:Math.abs(l!.amount),reason:String(cmd.reason).trim(),kind:'correction',target:l!.id});
  return `${x.id} · correction of “${l!.label}” requested · ${a.id}`;
};

const CREDIT_NOTE:Handler=k=>{
  // PERM-FOL-005 credit note: DEC-063 G3 — as CORRECTION_REQUEST.
  const {s,cmd,b,open,positive,assert,fail,uid}=k;
  if(!rbacCanRequestCorrection(s,uid))fail('FORBIDDEN','Access denied: billing permission or the Accountant role is required.');open();const x=b();positive(cmd.amount);
  assert(x.status==='Departed','A credit note applies only to a departed (invoiced) stay. Use a correction while the guest is in house.','INVALID_TRANSITION');
  assert(String(cmd.reason??'').trim(),'A reason is required.');
  // F8-SF-DOMAIN · CREDIT_NOTE {folio?}: cap = that folio's net, target = that folio's invoice (SF-CONTRACT §6)
  const fol=sfIsSplit(x)?cmdFolio(k,x):SF_PRIMARY,xv=sfView(x,fol);
  assert(cmd.amount<=totals(xv).net,'Credit note cannot exceed the net charges on this folio.');
  assert(!gstAdjustmentMixed(xv),GST_ADJUSTMENT_MIXED_MSG); // F8-INT (H2)
  assert(!pendingOf(k,x,'credit-note',fol),'A credit-note request is already pending.','INVALID_TRANSITION');
  if(sfIsSplit(x))assert(xv.invoiceNo,`Folio ${fol} has no tax invoice (a ₹0 folio is not invoiced): nothing to credit.`,'INVALID_TRANSITION');
  const a=request(k,x,{amount:cmd.amount,reason:String(cmd.reason).trim(),kind:'credit-note',target:xv.invoiceNo,...(sfIsSplit(x)?{meta:{command:'APPROVAL',folio:fol}}:{})});
  // F8-SF-DOMAIN end
  return `${x.id} · credit note ${money(cmd.amount)} requested · ${a.id}`;
};

const APPROVAL:Handler=k=>{
  // PERM-APR-001 approval.decide
  const {s,cmd,req,open,assert,fail,uid,user,financial,id}=k;
  req('approve');open();const a=s.approvals.find(a=>a.id===cmd.id);
  assert(a&&a.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
  assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
  if(folioApprovalExpired(s,a!)){
    // Rejecting/dismissing an expired request records it as Expired; approving it fails.
    if(!cmd.approve){a!.status='Expired';a!.approver=uid;a!.decidedAt=s.clock;return a!.id+' · Expired';}
    fail('INVALID_TRANSITION','This request expired (24 simulated hours). Dismiss it and request again.');
  }
  const kind=folioKind(a!);
  // DEC-063 G3: corrections and credit notes are approved by an Owner. G1: a Manager approves discounts only up to the Manager limit.
  if(cmd.approve&&RBAC_OWNER_DECIDES.includes(kind))assert(user.role==='Owner','Corrections and credit notes are approved by an Owner (DEC-063).','FORBIDDEN');
  if(cmd.approve&&kind==='discount'){const lim=rbacApprovalLimit(s,uid);if(lim!==undefined)assert(a!.amount<=lim,`Discounts above ${money(lim)} need an Owner’s approval.`,'FORBIDDEN');}
  const x=s.bookings.find(b=>b.id===a!.booking);
  const stale=()=>assert(x!.version===a!.version,STALE_MSG,'STALE_VERSION');
  let extra='';
  if(cmd.approve){
    if(kind==='discount'){
      financial(x!);stale();
      const fol=sfIsSplit(x!)?metaFolio(a!):SF_PRIMARY,xv=sfView(x!,fol); // F8-SF-DOMAIN: the folio named at the request
      assert(totals(xv).net>=a!.amount,'Discount exceeds the current charges.');
      assert(!gstAdjustmentMixed(xv),GST_ADJUSTMENT_MIXED_MSG); // F8-INT (H2)
      x!.lines.push({id:id('discount'),label:'Approved discount · '+a!.reason,amount:-a!.amount,date:s.date,kind:'discount',taxBps:gstForFolioAdjustment(s,xv,s.date).bps,...stamp(x!,fol)});x!.version++;
    }else if(kind==='refund'){
      assert(x,'Booking not found.','NOT_FOUND');stale();
      const r=x!.receipts.find(r=>r.id===a!.target);assert(r,'The original receipt no longer exists.','NOT_FOUND');
      assert(a!.amount<=folioRefundable(s,x!,r!.id,a!.id),'Refund cannot exceed the refundable remainder of this receipt.');
      extra=' · awaiting execution';
    }else if(kind==='company-credit'){
      financial(x!);stale();
      if(sfIsSplit(x!)){
        // F8-SF-DOMAIN · per company folio: Manager ≤ ₹10,000 (Administration-panel override read as the counter-bill path does), Owner above; D7 again
        const fol=metaFolio(a!),lim=adminLimitFor(s,'Manager','PERM-PAY-005')??FOLIO_MANAGER_CREDIT_LIMIT;
        assert(totals(sfView(x!,fol)).balance===a!.amount,STALE_MSG,'STALE_VERSION');
        if(user.role!=='Owner')assert(a!.amount<=lim,`Company credit above ${money(lim)} per company folio needs an Owner’s approval.`,'FORBIDDEN');
        sfCompanyCreditCheck(k,x!,fol,a!.amount);
        // F8-SF-DOMAIN end
      }else{
      assert(totals(x!).balance===a!.amount,STALE_MSG,'STALE_VERSION');
      if(user.role!=='Owner')assert(a!.amount<=FOLIO_MANAGER_CREDIT_LIMIT,`Company credit above ${money(FOLIO_MANAGER_CREDIT_LIMIT)} per stay needs an Owner’s approval.`,'FORBIDDEN');
      companyCreditCheck(k,x!,a!.amount);
      }
      extra=' · apply at checkout';
    }else if(kind==='correction'){
      financial(x!);stale();
      const l=x!.lines.find(l=>l.id===a!.target);assert(l,'The original line no longer exists.','NOT_FOUND');
      assert(!x!.lines.some(r=>r.reversalOf===l!.id),'This line is already reversed.','INVALID_TRANSITION');
      const lid=id('reversal');
      // DEC-077: the reversal posts on the original line's date while that day is reopened by the Owner, else on the business date.
      const on=l!.date<s.date&&closingReopened(s)?.date===l!.date?l!.date:s.date;
      x!.lines.push({id:lid,label:'Reversal · '+l!.label+' · '+a!.reason,amount:-l!.amount,date:on,kind:'adjustment',taxBps:l!.taxBps,reversalOf:l!.id,approvalId:a!.id});
      x!.version++;a!.status='Executed';extra=' · reversal '+lid+' posted'+(on!==s.date?' on reopened day '+on:'');
    }else if(kind==='credit-note'){
      assert(x&&x.status==='Departed','A credit note applies only to a departed stay.','INVALID_TRANSITION');stale();
      const fol=sfIsSplit(x!)?metaFolio(a!):SF_PRIMARY,xv=sfView(x!,fol); // F8-SF-DOMAIN: the credited folio (SF-CONTRACT §6); identity on a single-folio stay
      assert(a!.amount<=totals(xv).net,'Credit note cannot exceed the net charges on this folio.');
      assert(!gstAdjustmentMixed(xv),GST_ADJUSTMENT_MIXED_MSG); // F8-INT (H2)
      const no=fdmAllocate(s,'CRN'); // MA-5: numbering series (seeded CRN series continues s.creditNoteSeqC)
      const lid=id('credit'),bps=gstForFolioAdjustment(s,xv,s.date).bps,t=totals(xv);
      // A03a-04: crediting the folio's whole remaining net, all at the credit's rate, credits exactly the tax still on it (never a paisa
      // more after an earlier credit whose tax was exactly half a paisa): pieces that each follow the per-line rule.
      const whole=a!.amount===t.net&&xv.lines.every(l=>l.taxBps===bps);
      const pieces=(whole&&gstExactPieces(-a!.amount,bps,-t.tax))||[-a!.amount];
      pieces.forEach((amount,i)=>x!.lines.push({id:i?id('credit'):lid,label:`Credit note ${no} · ${a!.reason}`,amount,date:s.date,kind:'adjustment',taxBps:bps,approvalId:a!.id,...stamp(x!,fol)}));
      (x!.creditNotes??=[]).push({id:id('CN'),no,amount:a!.amount,reason:a!.reason,date:s.date,approvalId:a!.id,lineId:lid,...(sfIsSplit(x!)?{folio:fol}:{})});
      x!.version++;a!.status='Executed';a!.meta={...a!.meta,creditNote:no};extra=' · '+no+' issued';
    }
    // LF lanes: your approval kind as a separate `if(kind==='<kind>'){…}` statement inside your own block (folioKind must know the kind).
    // LF-RATE · APPROVAL branch
    if(kind==='rate-change'){
      assert(x&&x.status==='In house','A rate change applies to an in-house stay only.','INVALID_TRANSITION');stale();
      const m=a!.meta as Record<string,any>;
      assert(strCanApprove(s,uid,x!,Number(m.rate)),Number(m.rate)!==x!.rate&&strOverThreshold(s,x!,Number(m.rate))
        ?`This rate is more than ${strThresholdBps(s)/100} % below the agreed rate ${money(strAgreedRate(s,x!))} (CTL-01). An Owner must approve it.`
        :'This rate is below what a Manager may allow (PERM-RATE-001). An Owner must approve it.','FORBIDDEN');
      const refusal=strRefusal(s,x,{rate:m.rate,people:m.people,children:m.children,scope:m.scope,reason:a!.reason});if(refusal)fail('INVALID_TRANSITION',refusal);
      const ch=strApply(k,x!,{rate:Number(m.rate),people:Number(m.people),children:Number(m.children),scope:m.scope,reason:a!.reason},a!);
      a!.status='Executed';a!.meta={...a!.meta,change:ch.id};extra=` · rate ${money(ch.oldRate)} → ${money(ch.newRate)} applied`+(ch.reposted.length?` · ${ch.reposted.length} night(s) re-posted`:'');
    }
    // LF-RATE end
    // LF-BILL · APPROVAL branch
    if(kind==='bill-correction'){
      assert(x,'Booking not found.','NOT_FOUND');stale();
      extra=blcExecute(k,x!,a!);
    }
    // LF-BILL end
    // LF-COUNTER · APPROVAL branch
    if(kind===CTR_VOID_KIND)extra=ctrVoidExecute(k,a!);
    if(kind==='guest-credit'){assert(x&&x.status==='In house','This stay is not in house.','INVALID_TRANSITION');financial(x!);stale();extra=gcrApprove(k,x!,a!);}
    // LF-COUNTER end
    // Other kinds (owned elsewhere, defaulting to meta.command 'APPROVAL') are simply decided here; their owner executes.
  }
  if(a!.status==='Pending')a!.status=cmd.approve?'Approved':'Rejected';
  else if(!cmd.approve)a!.status='Rejected';
  if(!cmd.approve&&String(cmd.reason??'').trim())a!.meta={...a!.meta,rejectReason:String(cmd.reason).trim().slice(0,200)} as any;
  a!.approver=uid;a!.decidedAt=s.clock;
  return a!.id+' · '+(a!.status==='Executed'?'Approved':a!.status)+extra;
};

const CHECKOUT:Handler=k=>{
  // PERM-STY-003
  const {s,cmd,req,open,b,assert,id,uid}=k;
  req('checkout');open();const x=b();assert(x.status==='In house','This stay is not in house.','INVALID_TRANSITION');
  for(let d=x.arrival;d<x.departure;d=addDays(d,1))assert(x.lines.some(l=>l.kind==='room'&&l.date===d),'Post all agreed stay charges before checkout.','INVALID_TRANSITION');
  // LF-DESK · CHECKOUT late check-out (half-day / full-night / waived, §8.4)
  const lateNote=deskCheckoutLate(k,x);
  // LF-DESK end
  let detail=lateNote;
  // F8-SF-DOMAIN · CHECKOUT of a split stay: settle every folio (one sheet), then one tax invoice per non-zero folio in letter order (SF-CONTRACT §4, §5)
  if(sfIsSplit(x)){
    for(const l of x.lines)if(!l.folio)l.folio=sfNewLineFolio(x,l); // the late check-out line just posted (routed as extras)
    for(const f of sfFolios(x)){
      const bal=totals(sfView(x,f.id)).balance;
      if(bal>0&&f.payer.kind==='company'){
        const credit=s.approvals.find(a=>a.booking===x.id&&a.kind==='company-credit'&&a.status==='Approved'&&!folioApprovalExpired(s,a)&&a.amount===bal&&a.version===x.version&&metaFolio(a)===f.id);
        if(credit){
          sfCompanyCreditCheck(k,x,f.id,bal);
          x.receipts.push({id:id('RCPT'),amount:bal,method:'Company credit',date:s.date,kind:'company-credit',reference:`${f.payer.name} · ${credit.id}`,actor:uid,approvalId:credit.id,folio:f.id});
          credit.status='Executed';credit.meta={...credit.meta,executedBy:uid,executedAt:s.clock};
          detail+=` · folio ${f.id} ${money(bal)} to company credit (${credit.id})`;
        }
      }else if(bal>0&&f.id===SF_PRIMARY&&cmd.otaCredit){
        const refusal=chnOtaCreditRefusal(s,x);assert(!refusal,refusal,'INVALID_TRANSITION');
        const ch=chnChannelOfBooking(s,x)!;
        x.receipts.push({id:id('RCPT'),amount:bal,method:'OTA credit',date:s.date,kind:'ota-credit',reference:`${ch.name} · paid by the OTA`,actor:uid,folio:SF_PRIMARY});
        detail+=` · folio A ${money(bal)} to ${ch.name} (OTA receivable)`;
      }
      detail+=gcrCheckout(k,x,f.id);
      assert(totals(sfView(x,f.id)).balance===0,`Settle folio ${f.id} (${f.payer.name}) exactly before checkout. Refund any excess receipt.`,'INVALID_TRANSITION');
    }
    assert(totals(x).deposit===0,'Return the refundable deposit before checkout.','INVALID_TRANSITION');
    const pending=s.approvals.filter(a=>a.booking===x.id&&a.status==='Pending'&&!folioApprovalExpired(s,a));
    assert(!pending.some(a=>folioKind(a)==='discount'),'Resolve the pending discount request before checkout.','INVALID_TRANSITION');
    assert(!pending.length,'Resolve the pending approval request on this folio before checkout.','INVALID_TRANSITION');
    x.status='Departed';x.version++;s.rooms.find(r=>r.id===x.room)!.hk='Dirty';
    const issued:string[]=[];
    for(const f of x.folios!.filter(f=>!f.removedAt).sort((p,q)=>p.id<q.id?-1:1)){
      if(f.invoiceNo||totals(sfView(x,f.id)).total===0)continue; // SFQ-8: a ₹0 folio gets no tax invoice
      f.invoiceNo=fdmAllocate(s,'INV');issued.push(`${f.id} ${f.invoiceNo}`);
    }
    if(!x.invoiceNo){const first=sfFolios(x).find(f=>f.invoiceNo);if(first)x.invoiceNo=first.invoiceNo;}
    if(!s.tasks.some(t=>t.booking===x.id))s.tasks.push({id:id('task'),room:x.room,booking:x.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
    return x.id+' · departed; room dirty; housekeeping task created; invoices '+(issued.join(', ')||'none (₹0 folios)')+detail;
  }
  // F8-SF-DOMAIN end
  const bal=totals(x).balance;
  if(bal>0){
    // Approved company credit for exactly this balance and folio version settles the folio to a receivable (AC-008).
    const credit=s.approvals.find(a=>a.booking===x.id&&a.kind==='company-credit'&&a.status==='Approved'&&!folioApprovalExpired(s,a)&&a.amount===bal&&a.version===x.version);
    if(credit){
      companyCreditCheck(k,x,bal);
      x.receipts.push({id:id('RCPT'),amount:bal,method:'Company credit',date:s.date,kind:'company-credit',reference:`${x.company?.name??'Company'} · ${credit.id}`,actor:uid,approvalId:credit.id});
      credit.status='Executed';credit.meta={...credit.meta,executedBy:uid,executedAt:s.clock};
      detail=` · ${money(bal)} to company credit (${credit.id})`;
    }else if(cmd.otaCredit){
      // ACC-2 flows (contract §4, rulebook A13, DEC-150): an OTA-paid stay — the balance moves to the channel's receivable (receipt kind
      // 'ota-credit', Journal Dr 1115/<channel> · Cr 1100); only when the booking's source is a connected channel. Settled by CHN_PAYOUT.
      const refusal=chnOtaCreditRefusal(s,x);assert(!refusal,refusal,'INVALID_TRANSITION');
      const ch=chnChannelOfBooking(s,x)!;
      x.receipts.push({id:id('RCPT'),amount:bal,method:'OTA credit',date:s.date,kind:'ota-credit',reference:`${ch.name} · paid by the OTA`,actor:uid});
      detail=` · ${money(bal)} to ${ch.name} (OTA receivable)`;
    }
  }
  // LF-COUNTER · CHECKOUT 'guest-credit' receipt + Udhaar receivable (§7)
  detail+=gcrCheckout(k,x);
  // LF-COUNTER end
  assert(totals(x).balance===0,'Settle the folio exactly before checkout. Refund any excess receipt.','INVALID_TRANSITION');
  assert(totals(x).deposit===0,'Return the refundable deposit before checkout.','INVALID_TRANSITION');
  const pending=s.approvals.filter(a=>a.booking===x.id&&a.status==='Pending'&&!folioApprovalExpired(s,a));
  assert(!pending.some(a=>folioKind(a)==='discount'),'Resolve the pending discount request before checkout.','INVALID_TRANSITION');
  assert(!pending.length,'Resolve the pending approval request on this folio before checkout.','INVALID_TRANSITION');
  x.status='Departed';x.version++;s.rooms.find(r=>r.id===x.room)!.hk='Dirty';
  // Sample invoice number allocated once, at checkout (Financial Rulebook §5).
  if(!x.invoiceNo)x.invoiceNo=fdmAllocate(s,'INV'); // MA-5: numbering series (seeded INV series continues s.invoiceSeqC)
  if(!s.tasks.some(t=>t.booking===x.id))s.tasks.push({id:id('task'),room:x.room,booking:x.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
  void cmd;
  return x.id+' · departed; room dirty; housekeeping task created; sample invoice '+x.invoiceNo+detail;
};

export const handlers:Record<string,Handler>={PAYMENT,CHARGE,DISCOUNT,APPROVAL,CHECKOUT,REFUND_REQUEST,REFUND_EXECUTE,COMPANY_CREDIT_REQUEST,CORRECTION_REQUEST,CREDIT_NOTE};
