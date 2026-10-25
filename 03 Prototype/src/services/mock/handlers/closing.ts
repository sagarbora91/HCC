// Stream F · Cashier, closing & reports (owns NIGHT_POST, POST_NIGHTS, SHIFT_CLOSE, DAY_CLOSE, NEXT_DAY, EXPENSE, EXPENSE_APPROVE).
// Register new or replacement command handlers here. A handler with the same name as a built-in command replaces it.
// Kept built-in: POST_NIGHTS (M1 compatibility, no UI button). SHIFT_CLOSE (exact-count close by close-permission users) is re-registered here
// with the same rules so the counter is recorded (W05 review F1: the person who counted may not lock the cash book day).
import type {Handler,Kit} from '../commands';
import type {Expense} from '../../../domain/types';
import {money,addDays,OPENING_TIME} from '../../../domain/money';
import {drawer} from '../../../domain/selectors';
import {gstForFolioAdjustment} from '../../../domain/gst';
import {hkExpireBlocks} from '../../../domain/streams/housekeeping';
import {posOpenOrders} from '../../../domain/streams/pos';
import {closingNightTargets,closingHasNight,closingSummary,shiftStatus,shiftDenominationTotal,SHIFT_MANAGER_DIFF_LIMIT,EXPENSE_MANAGER_LIMIT,
  closingUnlimitedRoles,EXPENSE_METHODS} from '../../../domain/streams/closing';
import {rbacCanDecideCash,rbacCanSubmitExpense,rbacCanDecideExpense,rbacCanRequestClosedCorrection} from '../../../domain/streams/rbac';

/** Resumable room-night posting for the business date (WF-DAY-001, REQ-CLOSE-003/005). Returns [newly posted, done, total, interrupted]. */
function postNights(k:Kit,interruptAfter?:number){
  const {s,addNight}=k;const targets=closingNightTargets(s);
  const prev=s.closeProgress?.date===s.date?s.closeProgress:undefined;
  const p={date:s.date,posted:[...(prev?.posted??[])],total:targets.length,interrupted:false,runs:(prev?.runs??0)+1};
  let added=0;
  for(const x of targets){
    if(p.posted.includes(x.id))continue;
    if(closingHasNight(x,s.date)){p.posted.push(x.id);continue;} // posted earlier (e.g. by NIGHT_POST): record, never repost
    if(interruptAfter!==undefined&&added>=interruptAfter){p.interrupted=true;break;}
    addNight(x,s.date);x.version++;added++;p.posted.push(x.id);
  }
  s.closeProgress=p;
  return {added,done:targets.filter(t=>p.posted.includes(t.id)).length,total:targets.length,interrupted:p.interrupted};
}
const limited=(role:string)=>!closingUnlimitedRoles.includes(role);
/** DEC-063 G4: role-aware guard (the Accountant decides cash/expense items without the broad 'approve' grant). */
const need=(k:Kit,ok:(s:Kit['s'],uid:string)=>boolean,what:string)=>{if(!ok(k.s,k.uid))k.fail('FORBIDDEN',`Access denied: ${what}.`);};
const closeShift=(k:Kit,expected:number,counted:number,countedBy:string)=>{k.s.shiftClose={expected,counted,countedBy};k.s.shiftOpen=false;};

export const handlers:Record<string,Handler>={
  // PERM-CLS-002 close.nightPost
  NIGHT_POST:k=>{k.req('close');k.open();postNights(k);return 'Idempotent room-night posting for '+k.s.date;},
  // PERM-CLS-002 · night audit step with simulated interruption (S5, AC-017)
  CLOSE_RUN:k=>{
    const {cmd,req,open,assert,s}=k;req('close');open();
    const n=cmd.interruptAfter;
    assert(n===undefined||Number.isInteger(n)&&n>=0,'Interruption point must be a whole number of stays.');
    const r=postNights(k,n);
    return r.interrupted?`Night audit ${s.date} interrupted (simulated) · ${r.done} of ${r.total} stays posted`:`Night audit ${s.date} · ${r.added} new night(s) posted · ${r.done} of ${r.total} stays complete`;
  },
  // PERM-CLS-002 exact-count close (same rules as the built-in command) · records who counted
  SHIFT_CLOSE:k=>{
    const {s,cmd,req,open,assert,uid}=k;req('close');open();assert(s.shiftOpen,'Shift already closed.','INVALID_TRANSITION');
    assert(Number.isSafeInteger(cmd.counted)&&cmd.counted>=0,'Enter a valid physical cash count.');
    const expected=drawer(s);assert(cmd.counted===expected,'Resolve the cash difference before closing this demo shift.');
    closeShift(k,expected,cmd.counted,uid);return 'Cash reconciled · '+money(expected);
  },
  // PERM-SHF-001 shift.count / submit (cashier or reception: coarse 'payments')
  SHIFT_SUBMIT:k=>{
    const {s,cmd,req,open,assert,fail,uid,id}=k;req('payments');open();
    const st=shiftStatus(s);
    if(st==='Submitted')fail('INVALID_TRANSITION','A cash count is already awaiting difference approval.');
    assert(s.shiftOpen,'Shift already closed.','INVALID_TRANSITION');
    assert(Number.isSafeInteger(cmd.counted)&&cmd.counted>=0&&cmd.counted<=100000000,'Enter a valid physical cash count.');
    let denominations:Record<string,number>|undefined;
    if(cmd.denominations){
      const d:Record<string,number>={...cmd.denominations};denominations=d;
      assert(Object.values(d).every(v=>Number.isFinite(v)&&v>=0),'Denomination counts cannot be negative.');
      assert(shiftDenominationTotal(d)===cmd.counted,'The denomination grid does not add up to the counted total.');
    }
    const expected=drawer(s),diff=cmd.counted-expected;
    if(diff===0){
      closeShift(k,expected,cmd.counted,uid);
      s.shift={date:s.date,status:'Closed',expected,counted:cmd.counted,submittedBy:uid,denominations};
      return 'Cash counted and reconciled exactly · '+money(expected);
    }
    assert(cmd.reason?.trim(),'A reason is required when the count differs from the expected cash.');
    const aid=id('APR');
    s.approvals.push({id:aid,booking:'',amount:Math.abs(diff),reason:cmd.reason.trim(),status:'Pending',actor:uid,version:s.sequence,
      kind:'cash-difference',target:s.date,meta:{command:'SHIFT_DIFFERENCE_DECIDE',expected,counted:cmd.counted}});
    s.shift={date:s.date,status:'Submitted',expected,counted:cmd.counted,reason:cmd.reason.trim(),submittedBy:uid,approval:aid,denominations};
    s.shiftOpen=false; // cash receipts and cash expense postings are blocked while the difference is pending
    return `Cash count submitted · expected ${money(expected)}, counted ${money(cmd.counted)} (${diff>0?'over':'short'} ${money(Math.abs(diff))}) · ${aid} pending`;
  },
  // PERM-SHF-002 shift.difference.approve · PERM-SOD-001/004 (approver ≠ submitter)
  SHIFT_DIFFERENCE_DECIDE:k=>{
    const {s,cmd,open,assert,fail,uid,user}=k;need(k,rbacCanDecideCash,'approve permission or the Accountant role is required');open();
    const a=s.approvals.find(a=>a.id===cmd.id&&a.kind==='cash-difference');
    assert(a,'Cash difference request not found.','NOT_FOUND');
    assert(a!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
    const sh=s.shift;assert(sh&&sh.approval===a!.id&&sh.date===s.date&&sh.status==='Submitted','This count does not belong to the current shift.','INVALID_TRANSITION');
    const expected=Number(a!.meta!.expected),counted=Number(a!.meta!.counted);
    a!.approver=uid;a!.decidedAt=s.clock;
    if(cmd.approve){
      if(limited(user.role)&&Math.abs(counted-expected)>SHIFT_MANAGER_DIFF_LIMIT)fail('FORBIDDEN',`Differences above ${money(SHIFT_MANAGER_DIFF_LIMIT)} need Owner approval.`);
      assert(drawer(s)===expected,'Cash movements changed after this count. Reject it and recount.','STALE_VERSION');
      a!.status='Approved';closeShift(k,expected,counted,sh!.submittedBy);sh!.status='Closed';sh!.decidedBy=uid;
      return `${a!.id} · difference ${money(counted-expected)} accepted · shift closed`;
    }
    a!.status='Rejected';if(cmd.reason?.trim())a!.meta={...a!.meta,rejectReason:cmd.reason.trim()};
    s.shift=undefined;s.shiftOpen=true;
    return `${a!.id} · difference rejected · shift reopened for recount`;
  },
  // PERM-EXP-001 expense.submit. draft:true saves a Draft (own); otherwise submits for approval.
  EXPENSE:k=>{
    const {s,cmd,open,assert,positive,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();positive(cmd.amount);
    assert(cmd.vendor?.trim(),'Enter a vendor.');
    assert(cmd.category?.trim(),'Choose a category.');
    assert(EXPENSE_METHODS.includes(cmd.method),'Choose a funding source.');
    const e:Expense={id:id('EXP'),vendor:cmd.vendor.trim(),category:cmd.category,amount:cmd.amount,method:cmd.method,date:s.date,status:cmd.draft?'Draft':'Pending',actor:uid};
    if(cmd.notes?.trim())e.notes=String(cmd.notes).trim().slice(0,300);
    if(cmd.receipt?.trim())e.receipt=String(cmd.receipt).trim().slice(0,120);
    s.expenses.push(e);
    return (cmd.draft?'Expense draft saved · ':'Expense submitted · ')+e.vendor;
  },
  EXPENSE_SUBMIT:k=>{
    const {s,cmd,open,assert,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const e=s.expenses.find(e=>e.id===cmd.id);assert(e,'Expense not found.','NOT_FOUND');
    assert(e!.status==='Draft','Only a draft can be submitted.','INVALID_TRANSITION');
    assert(e!.actor===uid,'You can submit only your own drafts.','FORBIDDEN');
    e!.status='Pending';e!.date=s.date;return e!.id+' · submitted for approval';
  },
  // PERM-EXP-002 expense.approve / reject (manager ≤ ₹5,000) + PERM-EXP-003 post
  EXPENSE_APPROVE:k=>{
    const {s,cmd,open,assert,fail,uid,user}=k;need(k,rbacCanDecideExpense,'approve permission or the Accountant role is required');open();
    const e=s.expenses.find(e=>e.id===cmd.id);
    assert(e&&e.status==='Pending','Expense is no longer pending.','INVALID_TRANSITION');
    assert(e!.actor!==uid,'You cannot approve your own expense.','FORBIDDEN');
    if(cmd.approve&&limited(user.role)&&e!.amount>EXPENSE_MANAGER_LIMIT)fail('FORBIDDEN',`Expenses above ${money(EXPENSE_MANAGER_LIMIT)} need Owner approval.`);
    if(cmd.approve&&e!.method==='Cash')assert(s.shiftOpen,'Open cash shift required.','INVALID_TRANSITION');
    e!.status=cmd.approve?'Posted':'Rejected';e!.date=s.date;e!.approver=uid;e!.decidedAt=s.clock;
    if(!cmd.approve&&cmd.reason?.trim())e!.reason=String(cmd.reason).trim();
    return e!.id+' · '+e!.status;
  },
  // PERM-EXP-003 expense.reverse: Posted → Reversed + negative Posted record on today's business date
  EXPENSE_REVERSE:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanDecideExpense,'approve permission or the Accountant role is required (not the Cashier, DEC-063)');open();
    const e=s.expenses.find(e=>e.id===cmd.id);assert(e,'Expense not found.','NOT_FOUND');
    assert(e!.status==='Posted'&&!e!.reversalOf,'Only a posted expense can be reversed.','INVALID_TRANSITION');
    assert(cmd.reason?.trim(),'A reason is required to reverse an expense.');
    if(e!.method==='Cash')assert(s.shiftOpen,'Open cash shift required.','INVALID_TRANSITION');
    const rid=id('EXP');
    s.expenses.push({id:rid,vendor:e!.vendor,category:e!.category,amount:-e!.amount,method:e!.method,date:s.date,status:'Posted',actor:uid,reversalOf:e!.id,reason:cmd.reason.trim(),approver:uid,decidedAt:s.clock});
    e!.status='Reversed';e!.reversedBy=rid;e!.reason=cmd.reason.trim();
    return `${e!.id} reversed by ${rid} · ${money(e!.amount)}`;
  },
  // PERM-CLS-002 close.closeDay
  DAY_CLOSE:k=>{
    const {s,req,open,assert,uid}=k;req('close');open();
    assert(!s.bookings.some(b=>b.status==='Confirmed'&&b.arrival<=s.date),'Resolve pending arrivals: check in or cancel after refunding advances.','INVALID_TRANSITION');
    assert(!s.bookings.some(b=>b.status==='In house'&&b.departure<=s.date),'Resolve overdue departures.','INVALID_TRANSITION');
    assert(!s.bookings.some(b=>{const h=(b as {holdUntil?:string}).holdUntil;return b.status==='Held'&&!!h&&h<s.clock;}),'Release expired holds before closing.','INVALID_TRANSITION');
    assert(!s.bookings.some(b=>b.status==='In house'&&!closingHasNight(b,s.date)),'Post room nights before closing.','INVALID_TRANSITION');
    assert(!s.approvals.some(a=>a.status==='Pending'&&a.kind!=='policy')&&!s.expenses.some(e=>e.status==='Pending'),'Resolve pending approvals and expenses.','INVALID_TRANSITION');
    const openChecks=posOpenOrders(s);
    assert(!openChecks.length,`Settle, charge to the room or void ${openChecks.length} open restaurant / room-service check${openChecks.length===1?'':'s'} first (${openChecks.map(o=>o.id).join(', ')}).`,'INVALID_TRANSITION');
    assert(!s.shiftOpen,'Close and reconcile the cash shift first.','INVALID_TRANSITION');
    s.closedDates.push(s.date);
    // One immutable closing record per date; nothing writes to it after this point.
    s.closings={...(s.closings??{}),[s.date]:closingSummary(s,uid)};
    return 'Business day closed · '+s.date;
  },
  // PERM-CLS-002 close.openNextDay · opening float = counted cash at close (DEC-035)
  NEXT_DAY:k=>{
    const {s,req,assert}=k;req('close');assert(s.closedDates.includes(s.date),'Close the current business day first.','INVALID_TRANSITION');
    s.openingCash=s.shiftClose?.counted??drawer(s);s.date=addDays(s.date,1);s.shiftOpen=true;s.shiftClose=undefined;s.shift=undefined;s.closeProgress=undefined;
    s.clock=s.date+'T'+OPENING_TIME;const released=hkExpireBlocks(s);
    return 'Opened '+s.date+' · opening float '+money(s.openingCash)+(released.length?' · blocks ended: '+released.join(', '):'');
  },
  // PERM-CLS-003 close.correction.request (DEC-028: adjustment in the open day, referencing the closed date)
  CORRECTION_ADJUST:k=>{
    const {s,cmd,open,assert,b,id,uid}=k;need(k,rbacCanRequestClosedCorrection,'close permission or the Accountant role is required');open();
    const x=b();
    assert(['In house','Confirmed','Departed'].includes(x.status),'Corrections apply to active or departed stays only.','INVALID_TRANSITION');
    assert(s.closedDates.includes(cmd.date),'Choose a closed business date to correct.');
    assert(Number.isSafeInteger(cmd.amount)&&cmd.amount!==0&&Math.abs(cmd.amount)<=100000000,'Enter a non-zero correction amount (maximum ₹10,00,000).');
    assert(cmd.reason?.trim(),'A reason is required.');
    assert(!s.approvals.some(a=>a.kind==='closed-date-correction'&&a.status==='Pending'&&a.booking===x.id),'A correction for this folio is already pending.','INVALID_TRANSITION');
    const aid=id('APR');
    s.approvals.push({id:aid,booking:x.id,amount:Math.abs(cmd.amount),reason:cmd.reason.trim(),status:'Pending',actor:uid,version:x.version,kind:'closed-date-correction',target:cmd.date,
      meta:{command:'CORRECTION_ADJUST_DECIDE',date:cmd.date,amount:cmd.amount}});
    return `${aid} · correction ${money(cmd.amount)} on ${x.id} for closed date ${cmd.date} · Owner approval required`;
  },
  // PERM-CLS-004 close.correction.approve (Owner only)
  CORRECTION_ADJUST_DECIDE:k=>{
    const {s,cmd,req,open,assert,id,uid,user}=k;req('approve');open();
    const a=s.approvals.find(a=>a.id===cmd.id&&a.kind==='closed-date-correction');
    assert(a,'Correction request not found.','NOT_FOUND');
    assert(a!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
    assert(user.role==='Owner','Closed-date corrections are approved by an Owner.','FORBIDDEN');
    a!.approver=uid;a!.decidedAt=s.clock;
    if(cmd.approve){
      const x=s.bookings.find(b=>b.id===a!.booking);assert(x,'Booking not found.','NOT_FOUND');
      const date=String(a!.meta!.date),amount=Number(a!.meta!.amount);
      x!.lines.push({id:id('adj'),label:`Correction for closed ${date} · ${a!.reason}`,amount,date:s.date,kind:'adjustment',taxBps:gstForFolioAdjustment(s,x!,date).bps,correctionOf:date});
      x!.version++;a!.status='Approved';
      return `${a!.id} · correction ${money(amount)} posted on ${s.date} for ${date}`;
    }
    a!.status='Rejected';if(cmd.reason?.trim())a!.meta={...a!.meta,rejectReason:cmd.reason.trim()};
    return a!.id+' · correction rejected';
  },
};
