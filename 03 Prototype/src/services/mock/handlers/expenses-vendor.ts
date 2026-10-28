// ACC-2 · lane acc2-purchase — vendor payments, advances, voids and returns (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3.2, §3.5, §9;
// rulebook C3, C4, C5). Production: clients/sync/expenses-vendor.ts (server Hcc.Application/Expenses/VendorPaymentCommands.cs).
//   EXP_VENDOR_PAY {vendor, amount, method, reference, date?, allocations?}  Owner / Manager / Accountant (PERM-EXP-004 expenses.vendor.pay).
//       Cash leaves the drawer (open shift; the acc2-flows cash guard — stubbed here until it merges); UPI / bank need the reference.
//       Allocated oldest open bill first unless `allocations` is sent; the unallocated rest is an advance. Posts Payment Dr 2200/<vendor> · Cr tender.
//   EXP_VENDOR_PAY_VOID {id, reason}  Owner: on the payment's own business day the payment simply becomes void (posts nothing); later, a
//       reversing voucher is posted on the void date (cash comes back into the drawer).
//   EXP_VENDOR_RETURN {expense, amount, gstPaise, reason}  Owner / Manager / Accountant: a goods return / vendor credit note against a posted
//       bill = a negative bill (sourceRef 'ret:<bill>') through a credit method (Dr 2200/<vendor> · Cr the category · Cr ITC taken). The
//       Manager's / Accountant's request is Pending and only the Owner approves it (EXPENSE_APPROVE); the Owner's own posts at once.
import type {Handler,Kit} from '../commands';
import type {Expense} from '../../../domain/types';
import {money} from '../../../domain/money';
import {can} from '../../../domain/selectors';
import {mnyVendor,mnyMethodFor,mnyMethod,mnyIsCredit,mnyIsDrawer} from '../../../domain/streams/money-master';
import {mstFail} from '../../../domain/streams/masters';
import {rbacIsAccountant} from '../../../domain/streams/rbac';
import {accLockOf} from '../../../domain/streams/accounting';
import {accCashEnforce,accCashNote} from '../../../domain/streams/accounting-guards'; // ACC-2 integration: the flows lane's cash guard (contract §7)
import {expSlice,expDateCheck,expMonthLocked,expMonthLockedMessage,EXP_MAX_PAISE,type EXPState,type ExpVendorPayment} from '../../../domain/streams/expenses-scc';
import {EXP_VENDOR_PAY_ROLES,expVendorAllocate,expVendorAllocationRefusal,expVendorOpenBills,expIsReturn,expReturned,expReturnMethod,expVendorName} from '../../../domain/streams/expenses-vendor';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const slice=(k:Kit):EXPState=>{const x={...expSlice(k.s)};k.s.expensesX=x;return x;};
/** Contract §9 PERM-EXP-004 (expenses.vendor.pay): the Owner, the Manager (payments) and the Accountant (back office, no 'payments' grant in
 *  the coarse prototype model — the role decides, as for the other Accountant money checks). */
function payer(k:Kit,what:string){
  const ok=EXP_VENDOR_PAY_ROLES.includes(k.user.role)&&(can(k.s,k.uid,'payments')||rbacIsAccountant(k.s,k.uid));
  if(!ok)k.fail('FORBIDDEN',`Access denied: only the Owner, the Manager or the Accountant ${what}.`);
}
const amountOk=(k:Kit,n:unknown,msg:string)=>{if(!(Number.isSafeInteger(n)&&(n as number)>0&&(n as number)<=EXP_MAX_PAISE))k.fail('VALIDATION',msg);return n as number;};

export const handlers:Record<string,Handler>={
  EXP_VENDOR_PAY:k=>{
    const {s,cmd,fail,assert,id,uid,open}=k;payer(k,'records vendor payments');open();
    const v=mnyVendor(s,txt(cmd.vendor));if(!v)fail('VALIDATION','Choose a vendor from the list (Master configuration › Expense vendors).');
    if(!v!.active)mstFail('MASTER_RETIRED',`Vendor ${v!.name} is retired — choose an active vendor.`,'vendor');
    const amount=amountOk(k,cmd.amount,'Enter the amount paid (more than zero, at most ₹10,00,00,000).');
    const pm=mnyMethodFor(s,cmd.method,'expenses');if(!pm)fail('VALIDATION','Choose how the vendor was paid.');
    if(mnyIsCredit(s,pm!.name))fail('VALIDATION','A vendor payment moves money: choose cash, UPI or a bank transfer — not a credit method.');
    const reference=txt(cmd.reference);assert(reference.length<=120,'Keep the reference to 120 characters.');
    const cash=mnyIsDrawer(s,pm!.name);
    if(!cash)assert(reference,`Enter the ${pm!.name} reference (UTR / UPI reference) of the payment.`);
    const date=cmd.date===undefined||cmd.date===null||cmd.date===''?s.date:String(cmd.date);
    const w=expDateCheck(s,date,pm!.name);if(w)fail(w.code,w.message);
    assert(!accLockOf(s,date),`The books of ${date} are locked — unlock the day first.`,'DAY_CLOSED');
    if(cash)assert(s.shiftOpen,'Open cash shift required: a cash vendor payment leaves the drawer.','INVALID_TRANSITION');
    // ACC-2 integration (contract §7 B10): cash above ₹10,000 to one vendor in a day posts with a warning and the record is marked "deduction lost".
    const cg=cash?accCashEnforce(s,{kind:'payment',party:'2200/'+v!.code,amount,date,method:pm!.name,name:v!.name}):undefined;
    let allocations:{expense:string;amount:number}[];
    if(cmd.allocations!==undefined&&cmd.allocations!==null){const why=expVendorAllocationRefusal(s,v!.code,amount,cmd.allocations);if(why)fail('VALIDATION',why);
      allocations=(cmd.allocations as {expense:string;amount:number}[]).map(a=>({expense:txt(a.expense),amount:a.amount}));}
    else allocations=expVendorAllocate(s,v!.code,amount);
    const allocated=allocations.reduce((a,x)=>a+x.amount,0),advance=amount-allocated;
    const p:ExpVendorPayment={id:id('VP'),vendor:v!.code,date,amount,method:pm!.name,reference,by:uid,at:s.clock,allocations,...(cg?.deductionLost?{cashDeductionLost:true}:{})};
    const x=slice(k);x.vendorPayments=[...(x.vendorPayments??[]),p];
    return `${p.id} · ${v!.name} paid ${money(amount)} by ${pm!.name}${reference?' · '+reference:''}`
      +(allocations.length?` · against ${allocations.map(a=>`${a.expense} ${money(a.amount)}`).join(', ')}`:'')
      +(advance>0?` · advance ${money(advance)}`:'')+(date!==s.date?` · dated ${date}`:'')+(cg?accCashNote(cg):'');
  },
  EXP_VENDOR_PAY_VOID:k=>{
    const {s,cmd,fail,assert,uid,user,open}=k;
    if(user.role!=='Owner')fail('FORBIDDEN','Access denied: only the Owner voids a vendor payment.');open();
    const x=slice(k),p=(x.vendorPayments??[]).find(v=>v.id===cmd.id);if(!p)fail('NOT_FOUND','Vendor payment not found.');
    assert(!p!.void,'This vendor payment is already void.','INVALID_TRANSITION');
    const reason=txt(cmd.reason);assert(reason.length>=3,'A reason is required to void a vendor payment.');assert(reason.length<=200,'Keep the reason to 200 characters.');
    const sameDay=p!.date===s.date,w=expDateCheck(s,s.date,p!.method);if(w)fail(w.code,w.message);
    if(mnyIsDrawer(s,p!.method))assert(s.shiftOpen,'Open cash shift required: the cash of a voided payment comes back into the drawer.','INVALID_TRANSITION');
    x.vendorPayments=x.vendorPayments!.map(v=>v.id===p!.id?{...v,void:true,voidReason:reason,voidBy:uid,voidAt:s.date}:v);
    return `${p!.id} · ${expVendorName(s,p!.vendor)} ${money(p!.amount)} void · ${reason} · `+(sameDay?'same day: nothing posts':`reversing voucher on ${s.date}`);
  },
  EXP_VENDOR_RETURN:k=>{
    const {s,cmd,fail,assert,id,uid,user,open}=k;payer(k,'records vendor returns');open();
    const o=s.expenses.find(e=>e.id===txt(cmd.expense));if(!o)fail('NOT_FOUND','Bill not found.');
    assert(o!.status==='Posted'&&!o!.reversalOf&&!expIsReturn(o!)&&o!.amount>0,'Only a posted bill (not reversed) can be returned.','INVALID_TRANSITION');
    assert(txt(o!.vendor),'This bill names no vendor — a return needs the vendor.');
    const amount=amountOk(k,cmd.amount,'Enter the amount returned (more than zero).');
    const g=cmd.gstPaise===undefined||cmd.gstPaise===null||cmd.gstPaise===''?0:cmd.gstPaise;
    assert(Number.isSafeInteger(g)&&g>=0,'Enter the GST on the credit note in rupees (0 or more).');
    const done=expReturned(s,o!.id);
    if(amount>o!.amount-done.amount)fail('VALIDATION',`At most ${money(o!.amount-done.amount)} of this bill is left to return.`);
    if(g>(o!.gstPaise??0)-done.gst)fail('VALIDATION',`At most ${money(Math.max(0,(o!.gstPaise??0)-done.gst))} of the bill's GST is left to reverse.`);
    assert(g<=amount,'The GST cannot be more than the amount returned.');
    const reason=txt(cmd.reason);assert(reason.length>=3,'A reason is required for a return.');assert(reason.length<=200,'Keep the reason to 200 characters.');
    const method=expReturnMethod(s,o!);if(!method)fail('VALIDATION','No credit method is set up for expenses (Master configuration › Payment methods, kind Credit).');
    const w=expDateCheck(s,s.date,method);if(w)fail(w.code,w.message);
    if(expMonthLocked(s,s.date))fail('FORBIDDEN',expMonthLockedMessage(s.date.slice(0,7)));
    const owner=user.role==='Owner';
    const tds=o!.tdsPaise?Math.round(o!.tdsPaise*amount/o!.amount):0;
    const r:Expense={id:id('EXP'),vendor:o!.vendor,category:o!.category,amount:-amount,method:mnyMethod(s,method!)?.name??method!,date:s.date,status:owner?'Posted':'Pending',actor:uid,createdAt:s.clock,
      description:`Return to ${o!.vendor} · ${reason}`,reason,sourceRef:'ret:'+o!.id,
      ...(g?{gstPaise:-g,...(o!.gstRateBps?{gstRateBps:o!.gstRateBps}:{}),itc:o!.itc==='allowed'?'allowed' as const:'disallowed' as const}:{}),
      ...(o!.rcm?{rcm:true,rcmBps:o!.rcmBps??0,...(o!.itc?{itc:o!.itc}:{})}:{}),
      ...(o!.gstKind?{gstKind:o!.gstKind}:{}),...(o!.capital?{capital:true}:{}),...(tds?{tdsPaise:-tds,...(o!.tdsSection?{tdsSection:o!.tdsSection}:{})}:{}),
      ...(owner?{approver:uid,decidedAt:s.clock}:{})};
    s.expenses.push(r);
    const open2=expVendorOpenBills(s,o!.vendor).find(b=>b.expense===o!.id)?.open??0;
    return `${r.id} · return against ${o!.id} · ${o!.vendor} ${money(amount)}${g?` (GST ${money(g)} reversed${o!.itc==='allowed'?' — ITC taken reversed':''})`:''} · `
      +(owner?`posted (vendor credit note) · bill open ${money(open2)}`:'awaiting the Owner’s approval');
  },
};
