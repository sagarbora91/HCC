// Stream C · Payments & folio (owns PAYMENT, CHARGE, DISCOUNT, APPROVAL, CHECKOUT; adds REFUND_REQUEST, REFUND_EXECUTE,
// COMPANY_CREDIT_REQUEST, CORRECTION_REQUEST, CREDIT_NOTE). Replaces the built-in versions of the owned commands;
// existing error message texts are kept (tests match on them).
import type {Handler,Kit} from '../commands';
import type {Approval,Booking,Receipt} from '../../../domain/types';
import {money,addDays} from '../../../domain/money';
import {totals,taxRateFor} from '../../../domain/selectors';
import {rbacCanDiscount,rbacDiscountLimit,rbacApprovalLimit,rbacCanRequestCorrection,RBAC_OWNER_DECIDES} from '../../../domain/streams/rbac';
import {FOLIO_TENDERS,FOLIO_MANAGER_CREDIT_LIMIT,folioExpiresAt,folioKind,folioApprovalExpired,folioRefundable,folioOutstandingRefunds,folioLineCorrectable,folioInvoiceNo,folioCreditNoteNo} from '../../../domain/streams/folio';

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
const pendingOf=(k:Kit,bookingId:string,kind:string)=>k.s.approvals.some(a=>a.booking===bookingId&&folioKind(a)===kind&&a.status==='Pending'&&!folioApprovalExpired(k.s,a));
const validMethod=(k:Kit,method:unknown,reference:unknown)=>{
  k.assert((FOLIO_TENDERS as readonly string[]).includes(method as string),'Select a payment method.');
  if(method==='Cash')k.assert(k.s.shiftOpen,'The cash shift is closed. Open the next business day first.','INVALID_TRANSITION');
  if(method!=='Cash')k.assert(String(reference??'').trim(),'Enter a demonstration transaction reference.');
};

const PAYMENT:Handler=k=>{
  // PERM-PAY-001/002 (coarse: payments)
  const {s,cmd,req,b,open,positive,assert,mutable,fail,id,uid}=k;
  req('payments');const x=b();open();positive(cmd.amount);
  const kind:Receipt['kind']=cmd.kind||'payment';
  if(kind==='refund')fail('INVALID_TRANSITION','Refunds are no longer posted directly. Use “Request refund” on the original receipt; after approval, execute the refund.');
  assert(['payment','deposit','deposit-return'].includes(kind),'Invalid receipt type.');
  if(['payment','deposit'].includes(kind))mutable(x);
  const tenders:{method:string;amount:number;reference:string}[]=Array.isArray(cmd.tenders)&&cmd.tenders.length
    ?cmd.tenders.map((t:any)=>({method:t?.method,amount:t?.amount,reference:String(t?.reference??'').trim()}))
    :[{method:cmd.method,amount:cmd.amount,reference:String(cmd.reference??'').trim()}];
  assert(tenders.length<=4,'Use at most four tender rows.');
  if(tenders.length>1)assert(kind!=='deposit-return','Return a deposit with a single tender.');
  for(const t of tenders){positive(t.amount);validMethod(k,t.method,t.reference);}
  const sum=tenders.reduce((a,t)=>a+t.amount,0);
  assert(sum===cmd.amount,`Tender rows total ${money(sum)} but the payment amount is ${money(cmd.amount)}. They must be equal.`);
  if(kind==='deposit-return')assert(cmd.amount<=totals(x).deposit,'Return cannot exceed the deposit held.');
  const group=tenders.length>1?id('TND'):undefined;
  for(const t of tenders){
    const r:Receipt={id:id('RCPT'),amount:t.amount,method:t.method,date:s.date,kind,reference:t.reference||'Demo cash',actor:uid};
    if(group)r.tenderGroup=group;
    x.receipts.push(r);
  }
  x.version++;
  return x.id+' · '+kind+' · '+money(cmd.amount)+(tenders.length>1?` · ${tenders.length} tenders (${tenders.map(t=>t.method).join(' + ')})`:'')+' · manual entry, not verified';
};

const CHARGE:Handler=({s,cmd,req,b,financial,positive,assert,id})=>{
  // PERM-FOL-002
  req('billing');const x=b();financial(x);positive(cmd.amount);assert(cmd.label?.trim(),'Describe the service.');
  x.lines.push({id:id('line'),label:cmd.label.trim(),amount:cmd.amount,date:s.date,kind:'service',taxBps:taxRateFor(s,s.date)});x.version++;
  return x.id+' · '+cmd.label;
};

const DISCOUNT:Handler=k=>{
  // PERM-FOL-003 (+ PERM-APR-001 above the limit). DEC-063 G1: Owner, Manager, Reception only (never the Cashier);
  // own limit per role (Reception ≤ hotel threshold ₹250, Manager ≤ ₹1,000, Owner none).
  const {s,cmd,req,b,financial,positive,assert,id,uid,fail}=k;
  req('billing');if(!rbacCanDiscount(s,uid))fail('FORBIDDEN','Access denied: discounts are given by Reception, a Manager or the Owner — not at the cash counter (DEC-063).');
  const x=b();financial(x);positive(cmd.amount);assert(cmd.reason?.trim(),'A reason is required.');
  assert(cmd.amount<=totals(x).net,'Discount cannot exceed existing net charges.');
  const limit=rbacDiscountLimit(s,uid);
  if(limit!==undefined&&cmd.amount>limit){
    assert(!pendingOf(k,x.id,'discount'),'A discount request is already pending.','INVALID_TRANSITION');
    request(k,x,{amount:cmd.amount,reason:cmd.reason,kind:undefined});
    return x.id+' · approval requested';
  }
  x.lines.push({id:id('discount'),label:'Discount · '+cmd.reason,amount:-cmd.amount,date:s.date,kind:'discount',taxBps:taxRateFor(s,s.date)});x.version++;
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
  const remaining=folioRefundable(s,x,r!.id);
  assert(cmd.amount<=remaining,`Refund cannot exceed the refundable remainder of ${r!.id} (${money(remaining)}).`);
  if(x.status==='Departed')assert(cmd.amount<=Math.max(0,-totals(x).balance)-folioOutstandingRefunds(s,x.id),DEPARTED_REFUND_MSG,'INVALID_TRANSITION');
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
  assert(a!.amount<=folioRefundable(s,x!,r!.id,a!.id),'Refund cannot exceed the refundable remainder of this receipt.','INVALID_TRANSITION');
  if(x!.status==='Departed')assert(a!.amount<=Math.max(0,-totals(x!).balance)-folioOutstandingRefunds(s,x!.id,a!.id),DEPARTED_REFUND_MSG,'INVALID_TRANSITION');
  const method=cmd.method||r!.method;
  validMethod(k,method,cmd.reference??('Refund of '+r!.id));
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
  assert(x.company?.name?.trim(),'Add a company to this booking before requesting company credit.');
  const bal=totals(x).balance;
  assert(bal>0,'There is no outstanding balance to place on company credit.','INVALID_TRANSITION');
  assert(!pendingOf(k,x.id,'company-credit'),'A company-credit request is already pending.','INVALID_TRANSITION');
  const a=request(k,x,{amount:bal,reason:String(cmd.reason??'').trim()||('Company credit · '+x.company!.name),kind:'company-credit',meta:{command:'APPROVAL',company:x.company!.name,gstin:x.company!.gstin}});
  return `${x.id} · company credit ${money(bal)} requested for ${x.company!.name} · ${a.id}`;
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
  assert(cmd.amount<=totals(x).net,'Credit note cannot exceed the net charges on this folio.');
  assert(!pendingOf(k,x.id,'credit-note'),'A credit-note request is already pending.','INVALID_TRANSITION');
  const a=request(k,x,{amount:cmd.amount,reason:String(cmd.reason).trim(),kind:'credit-note',target:x.invoiceNo});
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
      assert(totals(x!).net>=a!.amount,'Discount exceeds the current charges.');
      x!.lines.push({id:id('discount'),label:'Approved discount · '+a!.reason,amount:-a!.amount,date:s.date,kind:'discount',taxBps:taxRateFor(s,s.date)});x!.version++;
    }else if(kind==='refund'){
      assert(x,'Booking not found.','NOT_FOUND');stale();
      const r=x!.receipts.find(r=>r.id===a!.target);assert(r,'The original receipt no longer exists.','NOT_FOUND');
      assert(a!.amount<=folioRefundable(s,x!,r!.id,a!.id),'Refund cannot exceed the refundable remainder of this receipt.');
      extra=' · awaiting execution';
    }else if(kind==='company-credit'){
      financial(x!);stale();
      assert(totals(x!).balance===a!.amount,STALE_MSG,'STALE_VERSION');
      if(user.role!=='Owner')assert(a!.amount<=FOLIO_MANAGER_CREDIT_LIMIT,`Company credit above ${money(FOLIO_MANAGER_CREDIT_LIMIT)} per stay needs an Owner’s approval.`,'FORBIDDEN');
      extra=' · apply at checkout';
    }else if(kind==='correction'){
      financial(x!);stale();
      const l=x!.lines.find(l=>l.id===a!.target);assert(l,'The original line no longer exists.','NOT_FOUND');
      assert(!x!.lines.some(r=>r.reversalOf===l!.id),'This line is already reversed.','INVALID_TRANSITION');
      const lid=id('reversal');
      x!.lines.push({id:lid,label:'Reversal · '+l!.label+' · '+a!.reason,amount:-l!.amount,date:s.date,kind:'adjustment',taxBps:l!.taxBps,reversalOf:l!.id,approvalId:a!.id});
      x!.version++;a!.status='Executed';extra=' · reversal '+lid+' posted';
    }else if(kind==='credit-note'){
      assert(x&&x.status==='Departed','A credit note applies only to a departed stay.','INVALID_TRANSITION');stale();
      assert(a!.amount<=totals(x!).net,'Credit note cannot exceed the net charges on this folio.');
      s.creditNoteSeqC=(s.creditNoteSeqC??0)+1;const no=folioCreditNoteNo(s.creditNoteSeqC);
      const lid=id('credit');
      x!.lines.push({id:lid,label:`Credit note ${no} · ${a!.reason}`,amount:-a!.amount,date:s.date,kind:'adjustment',taxBps:taxRateFor(s,s.date),approvalId:a!.id});
      (x!.creditNotes??=[]).push({id:id('CN'),no,amount:a!.amount,reason:a!.reason,date:s.date,approvalId:a!.id,lineId:lid});
      x!.version++;a!.status='Executed';a!.meta={...a!.meta,creditNote:no};extra=' · '+no+' issued';
    }
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
  let detail='';
  const bal=totals(x).balance;
  if(bal>0){
    // Approved company credit for exactly this balance and folio version settles the folio to a receivable (AC-008).
    const credit=s.approvals.find(a=>a.booking===x.id&&a.kind==='company-credit'&&a.status==='Approved'&&!folioApprovalExpired(s,a)&&a.amount===bal&&a.version===x.version);
    if(credit){
      x.receipts.push({id:id('RCPT'),amount:bal,method:'Company credit',date:s.date,kind:'company-credit',reference:`${x.company?.name??'Company'} · ${credit.id}`,actor:uid,approvalId:credit.id});
      credit.status='Executed';credit.meta={...credit.meta,executedBy:uid,executedAt:s.clock};
      detail=` · ${money(bal)} to company credit (${credit.id})`;
    }
  }
  assert(totals(x).balance===0,'Settle the folio exactly before checkout. Refund any excess receipt.','INVALID_TRANSITION');
  assert(totals(x).deposit===0,'Return the refundable deposit before checkout.','INVALID_TRANSITION');
  const pending=s.approvals.filter(a=>a.booking===x.id&&a.status==='Pending'&&!folioApprovalExpired(s,a));
  assert(!pending.some(a=>folioKind(a)==='discount'),'Resolve the pending discount request before checkout.','INVALID_TRANSITION');
  assert(!pending.length,'Resolve the pending approval request on this folio before checkout.','INVALID_TRANSITION');
  x.status='Departed';x.version++;s.rooms.find(r=>r.id===x.room)!.hk='Dirty';
  // Sample invoice number allocated once, at checkout (Financial Rulebook §5).
  if(!x.invoiceNo){s.invoiceSeqC=(s.invoiceSeqC??0)+1;x.invoiceNo=folioInvoiceNo(s.invoiceSeqC);}
  if(!s.tasks.some(t=>t.booking===x.id))s.tasks.push({id:id('task'),room:x.room,booking:x.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
  void cmd;
  return x.id+' · departed; room dirty; housekeeping task created; sample invoice '+x.invoiceNo+detail;
};

export const handlers:Record<string,Handler>={PAYMENT,CHARGE,DISCOUNT,APPROVAL,CHECKOUT,REFUND_REQUEST,REFUND_EXECUTE,COMPANY_CREDIT_REQUEST,CORRECTION_REQUEST,CREDIT_NOTE};
