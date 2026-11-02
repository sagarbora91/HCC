// LF lane lf-money-counter · contract §6 (A7) CTR_BILL_ISSUE, CTR_BILL_VOID_REQUEST and §7 (B2) GUEST_CREDIT_ACCOUNT_SET,
// GUEST_CREDIT_REQUEST. The approval branches (`counter-credit-note`, `guest-credit`) and the CHECKOUT 'guest-credit' receipt live in the
// LF-COUNTER blocks of handlers/folio.ts and call the functions exported here.
import type {Handler,Kit} from '../commands';
import type {Approval,Booking,CTRBill,CTRLine} from '../../../domain/types';
import {money,addDays} from '../../../domain/money';
import {can,totals} from '../../../domain/selectors';
import {folioExpiresAt,folioKind,folioApprovalExpired,FOLIO_MANAGER_CREDIT_LIMIT} from '../../../domain/streams/folio';
import {fdmAllocate} from '../../../domain/streams/frontdesk-master';
import {mnyMethodFor,mnyIsDrawer} from '../../../domain/streams/money-master';
import {cxCashBlocked} from '../../../domain/streams/cashout';
import {accCashEnforce} from '../../../domain/streams/accounting-guards';
import {adminLimitFor} from '../../../domain/streams/admin';
import {RES_GSTIN} from '../../../domain/streams/reservations';
import {EXP_UD_DUE_DAYS,expUdPaid,type ExpUdReceivable} from '../../../domain/streams/expenses-scc';
import {ctrLineFrom,ctrTotals,ctrDiscountRefusal,ctrCompanyRefusal,ctrUdKind,ctrDominant,ctrState,gcrRefusal,gcrCustomerKey,CTR_SETTLEMENTS,type CTRLineInput} from '../../../domain/streams/counter';

/** Approval kind of a counter-bill void (a credit note on a counter bill; the folio `credit-note` kind is booking-bound). Owner decides. */
export const CTR_VOID_KIND='counter-credit-note';
const txt=(v:unknown)=>v===undefined||v===null?'':String(v).trim();

/** Front-desk tender rules (as folio validMethod): an active method; drawer cash needs the user's open shift; bank / UPI need a reference. */
function tender(k:Kit,method:unknown,reference:unknown):string{
  const m=mnyMethodFor(k.s,method,'frontdesk');k.assert(m,'Select a payment method.');
  if(mnyIsDrawer(k.s,m!.name)){
    k.assert(k.s.shiftOpen,k.s.shiftClose||k.s.closedDates.includes(k.s.date)?'The cash shift is closed. Open the next business day first.':'The cash shift is closed. Open a cashier shift first.','INVALID_TRANSITION');
    const own=cxCashBlocked(k.s,k.uid);if(own)k.fail('FORBIDDEN',own);
  }
  if(m!.needsReference)k.assert(txt(reference),'Enter a demonstration transaction reference.');
  return m!.name;
}
const counterRoles=(k:Kit)=>{k.req('billing');k.req('payments');
  if(k.user.role==='Restaurant')k.fail('FORBIDDEN','Access denied: the restaurant bills through POS, not counter bills.');};
const udhaars=(k:Kit)=>{const x=k.s.expensesX??(k.s.expensesX={} as NonNullable<typeof k.s.expensesX>);return (x.udhaar??=[]);};

const CTR_BILL_ISSUE:Handler=k=>{
  // PERM-CTR-001 counter.bill.issue (Reception, Cashier, Manager, Owner)
  const {s,cmd,open,assert,fail,id,uid}=k;counterRoles(k);open();
  const p=cmd.party??{};const name=txt(p.name);assert(name.length>=2,'Enter the customer’s name.');assert(name.length<=80,'Keep the name to 80 characters.');
  const gstin=txt(p.gstin).toUpperCase();assert(!gstin||RES_GSTIN.test(gstin),'Enter the GSTIN in the 15-character format, e.g. 27AAAAA0000A1Z5 (fictional).');
  const phone=txt(p.phone);
  const inputs:CTRLineInput[]=Array.isArray(cmd.lines)?cmd.lines:[];
  assert(inputs.length>=1,'Add at least one item.');assert(inputs.length<=30,'Use at most 30 lines on one counter bill.');
  const lines:CTRLine[]=[];
  for(const [i,inp] of inputs.entries()){const l=ctrLineFrom(s,inp??{},s.date,`L${i+1}`);if('error' in l)fail('VALIDATION',`Line ${i+1}: ${l.error}`);lines.push(l as CTRLine);}
  const disc=cmd.discount?.amount?Number(cmd.discount.amount):0;
  if(disc){assert(Number.isInteger(disc)&&disc>0,'Enter the discount in whole paise.');assert(txt(cmd.discount.reason),'A discount reason is required.');
    const net=lines.reduce((a,l)=>a+l.amount,0);assert(disc<=net,'The discount cannot exceed the bill.');
    const r=ctrDiscountRefusal(s,uid,disc);if(r)fail('FORBIDDEN',r);}
  const t=ctrTotals(lines,disc);
  const mode=txt(cmd.settlement?.mode) as CTRBill['settlement']['mode'];
  assert(CTR_SETTLEMENTS.includes(mode as never),'Choose how the bill is settled: paid now, on Udhaar credit, or company credit.');
  const bid=id('CTR');
  const settlement:CTRBill['settlement']={mode};
  const guestId=txt(p.guestId)||undefined,guest=guestId?(s.guests??[]).find(g=>g.id===guestId):undefined;
  assert(!guestId||guest,'Guest profile not found.','NOT_FOUND');
  if(mode==='paid'){
    const method=tender(k,cmd.settlement.method,cmd.settlement.reference);
    if(mnyIsDrawer(s,method))accCashEnforce(s,{kind:'receipt',party:'counter:'+bid,scope:'counter:'+bid,amount:t.total} as never);
    Object.assign(settlement,{method,receiptId:id('RCPT'),...(txt(cmd.settlement.reference)?{reference:txt(cmd.settlement.reference)}:{})});
  }else if(mode==='udhaar'){
    assert(phone.replace(/\D/g,'').length>=10||guest,'A counter bill on Udhaar credit needs the customer’s name and phone.');
    if(guest?.creditAccount){const r=gcrRefusal(s,guest,t.total);if(r)fail('INVALID_TRANSITION',r.replace('this stay on guest credit','this counter bill on credit'));}
  }else{
    const companyId=txt(cmd.settlement.companyId??p.companyId);const r=ctrCompanyRefusal(s,companyId,t.total);if(r)fail('INVALID_TRANSITION',r);
    settlement.companyId=companyId;
  }
  const no=fdmAllocate(s,'INV'); // DEC-197 series HPE/2627/…
  const party:CTRBill['party']={name,...(phone?{phone}:{}),...(gstin?{gstin}:{}),...(txt(p.address)?{address:txt(p.address).slice(0,200)}:{}),...(guestId?{guestId}:{}),...(settlement.companyId?{companyId:settlement.companyId}:{})};
  const bill:CTRBill={id:bid,no,date:s.date,at:s.clock,by:uid,party,lines,...(disc?{discount:{amount:disc,reason:txt(cmd.discount.reason).slice(0,200)}}:{}),
    net:t.net,tax:t.tax,cgst:t.cgst,sgst:t.sgst,total:t.total,...(txt(cmd.remark)?{remark:txt(cmd.remark).slice(0,200)}:{}),settlement,status:'Issued'};
  if(mode==='udhaar'){
    const mobile=guest?.creditAccount?.customerKey&&/^\d{10}$/.test(guest.creditAccount.customerKey)?guest.creditAccount.customerKey:phone.replace(/\D/g,'').slice(-10);
    const r:ExpUdReceivable={id:id('UD'),name,mobile,amount:t.total,date:s.date,dueDate:addDays(s.date,EXP_UD_DUE_DAYS),note:`Counter bill ${no}`,settlements:[],by:uid,at:s.clock,
      kind:ctrUdKind(bill),gstBps:ctrDominant(lines)?.taxBps??0,counterBill:bid,...(guestId?{guestId}:{})};
    udhaars(k).push(r);settlement.udhaarId=r.id;
  }
  (s.counterX??(s.counterX={bills:[]})).bills.push(bill);
  return `${bid} · counter bill ${no} · ${name} · ${money(t.total)} · ${mode}${settlement.method?' · '+settlement.method:''}`;
};

const CTR_BILL_VOID_REQUEST:Handler=k=>{
  // PERM-CTR-002 counter.bill.void.request (same roles); the Owner approves (DEC-063 G3) → credit note for the full bill.
  const {s,cmd,open,assert,uid}=k;counterRoles(k);open();
  const b=ctrState(s).bills.find(x=>x.id===cmd.id);assert(b,'Counter bill not found.','NOT_FOUND');
  assert(b!.status==='Issued','This counter bill is already credited.','INVALID_TRANSITION');
  const reason=txt(cmd.reason);assert(reason,'A reason is required.');
  assert(!s.approvals.some(a=>a.target===b!.id&&folioKind(a)===CTR_VOID_KIND&&a.status==='Pending'&&!folioApprovalExpired(s,a)),'A credit note request is already pending on this counter bill.','INVALID_TRANSITION');
  if(b!.settlement.udhaarId){const r=(s.expensesX?.udhaar??[]).find(x=>x.id===b!.settlement.udhaarId);assert(!r||!expUdPaid(r),'Payments were received against this Udhaar credit. Reverse them first.','INVALID_TRANSITION');}
  const a:Approval={id:k.id('APR'),booking:b!.id,actor:uid,version:1,status:'Pending',expiresAt:folioExpiresAt(s.clock),amount:b!.total,reason:reason.slice(0,200),kind:CTR_VOID_KIND,target:b!.id,meta:{command:'APPROVAL',counterBill:b!.no}};
  s.approvals.push(a);
  return `${b!.id} · credit note for counter bill ${b!.no} (${money(b!.total)}) requested · ${a.id}`;
};
/** Approved void (Owner): a CRN credit note for the full bill; the cash goes back (drawer / tender on the credit date) or the receivable is reversed. */
export function ctrVoidExecute(k:Kit,a:Approval):string{
  const {s,assert,uid}=k;
  const b=ctrState(s).bills.find(x=>x.id===a.target);assert(b&&b.status==='Issued','This counter bill is already credited.','INVALID_TRANSITION');
  if(b!.settlement.mode==='paid'&&mnyIsDrawer(s,b!.settlement.method??'')){
    assert(s.shiftOpen,'The cash shift is closed. Open a cashier shift first.','INVALID_TRANSITION');
  }
  if(b!.settlement.udhaarId){const r=(s.expensesX?.udhaar??[]).find(x=>x.id===b!.settlement.udhaarId);
    if(r){assert(!expUdPaid(r),'Payments were received against this Udhaar credit. Reverse them first.','INVALID_TRANSITION');
      Object.assign(r,{void:true,voidReason:'Counter bill credited · '+a.reason,voidAt:s.date,voidBy:uid});}}
  const crn=fdmAllocate(s,'CRN');
  Object.assign(b!,{status:'Credited',creditNoteNo:crn,creditNoteReason:a.reason,voidApprovalId:a.id});
  a.status='Executed';a.meta={...a.meta,creditNote:crn};
  return ` · ${crn} credits counter bill ${b!.no}`+(b!.settlement.mode==='paid'?` · ${money(b!.total)} returned (${b!.settlement.method})`:b!.settlement.mode==='udhaar'?' · Udhaar credit reversed':' · company receivable reversed');
}

// ── B2 ──
const GUEST_CREDIT_ACCOUNT_SET:Handler=k=>{
  // PERM-GST-011 guest.creditAccount (Owner)
  const {s,cmd,assert,uid}=k;k.req('admin');if(k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: a guest’s credit account is set by the Owner (DEC-058).');
  const g=(s.guests??[]).find(x=>x.id===cmd.guestId);assert(g,'Guest profile not found.','NOT_FOUND');
  assert(Number.isInteger(cmd.limit)&&cmd.limit>=0,'Enter the credit limit in whole paise (0 or more).');
  const note=txt(cmd.note);assert(note.length<=200,'Keep the note to 200 characters.');
  g!.creditAccount={limit:cmd.limit,active:cmd.active!==false,customerKey:g!.creditAccount?.customerKey??gcrCustomerKey(g!),setBy:uid,at:s.clock,...(note?{note}:{})};
  g!.version=(g!.version||1)+1;
  return `${g!.id} · credit account ${g!.creditAccount.active?'active':'inactive'} · limit ${money(cmd.limit)} · party 1120/UD-${g!.creditAccount.customerKey}`;
};
const guestOf=(k:Kit,x:Booking)=>x.guestId?(k.s.guests??[]).find(g=>g.id===x.guestId):undefined;
const GUEST_CREDIT_REQUEST:Handler=k=>{
  // as COMPANY_CREDIT_REQUEST (checkout permission)
  const {s,cmd,req,open,assert,fail,uid}=k;req('checkout');open();
  const x=s.bookings.find(b=>b.id===cmd.booking||b.id===cmd.id);assert(x,'Booking not found.','NOT_FOUND');
  assert(x!.status==='In house','This stay is not in house.','INVALID_TRANSITION');
  const bal=totals(x!).balance;assert(bal>0,'There is no outstanding balance to place on guest credit.','INVALID_TRANSITION');
  assert(!s.approvals.some(a=>a.booking===x!.id&&folioKind(a)==='guest-credit'&&a.status==='Pending'&&!folioApprovalExpired(s,a)),'A guest-credit request is already pending.','INVALID_TRANSITION');
  const g=guestOf(k,x!);const r=gcrRefusal(s,g,bal);if(r)fail('INVALID_TRANSITION',r);
  const a:Approval={id:k.id('APR'),booking:x!.id,actor:uid,version:x!.version,status:'Pending',expiresAt:folioExpiresAt(s.clock),amount:bal,
    reason:txt(cmd.reason)||('Guest credit · '+g!.name),kind:'guest-credit',meta:{command:'APPROVAL',guestId:g!.id,customerKey:g!.creditAccount!.customerKey}};
  s.approvals.push(a);
  return `${x!.id} · guest credit ${money(bal)} requested for ${g!.name} · ${a.id}`;
};
/** APPROVAL branch for `guest-credit`: Manager within PERM-PAY-005 (₹10,000), Owner beyond; the limit re-checked. */
export function gcrApprove(k:Kit,x:Booking,a:Approval):string{
  const {s,assert,fail,user}=k;
  assert(totals(x).balance===a.amount,'The folio changed after this request. Reject it and request again.','STALE_VERSION');
  const lim=adminLimitFor(s,'Manager','PERM-PAY-005')??FOLIO_MANAGER_CREDIT_LIMIT;
  if(user.role!=='Owner')assert(user.role==='Manager'&&a.amount<=lim,`Guest credit above ${money(lim)} per stay needs an Owner’s approval.`,'FORBIDDEN');
  const r=gcrRefusal(s,guestOf(k,x),a.amount);if(r)fail('INVALID_TRANSITION',r);
  return ' · apply at checkout';
}
/** CHECKOUT: an approved guest credit for exactly the balance and folio version → receipt 'guest-credit' + Udhaar receivable (kind room). */
export function gcrCheckout(k:Kit,x:Booking):string{
  const {s,id,uid}=k;const bal=totals(x).balance;if(bal<=0)return '';
  const a=s.approvals.find(a=>a.booking===x.id&&a.kind==='guest-credit'&&a.status==='Approved'&&!folioApprovalExpired(s,a)&&a.amount===bal&&a.version===x.version);
  if(!a)return '';
  const g=guestOf(k,x);const r=gcrRefusal(s,g,bal);if(r)k.fail('INVALID_TRANSITION',r);
  const key=g!.creditAccount!.customerKey,room=x.lines.find(l=>l.kind==='room');
  x.receipts.push({id:id('RCPT'),amount:bal,method:'Guest credit',date:s.date,kind:'guest-credit',reference:`${g!.name} · ${a.id}`,actor:uid,approvalId:a.id});
  const ud:ExpUdReceivable={id:id('UD'),name:g!.name,mobile:/^\d{10}$/.test(key)?key:'',amount:bal,date:s.date,dueDate:addDays(s.date,EXP_UD_DUE_DAYS),note:`Stay ${x.id} · room ${x.room}`,
    settlements:[],by:uid,at:s.clock,kind:'room',gstBps:room?.taxBps??0,guestId:g!.id,booking:x.id};
  udhaars(k).push(ud);
  a.status='Executed';a.meta={...a.meta,executedBy:uid,executedAt:s.clock,udhaar:ud.id};
  void can;
  return ` · ${money(bal)} to guest credit ${g!.name} (${ud.id})`;
}

export const handlers:Record<string,Handler>={CTR_BILL_ISSUE,CTR_BILL_VOID_REQUEST,GUEST_CREDIT_ACCOUNT_SET,GUEST_CREDIT_REQUEST};
