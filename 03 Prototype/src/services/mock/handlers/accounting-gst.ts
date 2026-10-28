// ACC-2 · GST month end, statutory remittances and the month lock — command handlers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 switch,
// §6, §9; rulebook E1–E5, D5, §G1; DEC-131, DEC-142). Command types start with 'ACC_'. Return value = audit text.
//   ACC_GST_SETOFF {month}                                   Owner / Accountant (PERM-ACC-013): posts the month's set-off journal (derived,
//                                                            {kind:'tax', sub:'gst-setoff'}) on the business date; idempotent per month — a
//                                                            second call replaces a set-off that is not locked (its day / the month).
//   ACC_GST_PAY {month, amount, method, challan, date, rcm?} Owner / Accountant: template gst-payment, Dr 1340 · Cr tender (E2); with `rcm`
//                                                            also Dr 2140 · Cr 1340 (E3).
//   ACC_STAT_REMIT {kind, month, amount, method, challan, date}  Owner / Accountant: template statutory-remittance, Dr 2310 / 2320 / 2330 ·
//                                                            Cr tender (D5); the month's EPF / ESIC / PT compliance card is marked done with
//                                                            the challan as evidence.
//   ACC_MONTH_LOCK {month}         Owner (PERM-ACC-012): every day closed and locked (closed days locked here), set-off posted, no pending
//                                  manual voucher; also locks the SCC expense month (EXP_MONTH_LOCK semantics).
//   ACC_MONTH_LOCK_REQUEST {month} Accountant: asks the Owner (kept in the audit trail; accMonthLockRequests lists open requests).
//   ACC_MONTH_UNLOCK {month, reason}  Owner: lifts the month lock (DEC-131 mirror) and unlocks the expense month; day locks stay.
//   ACC_GST_ADVANCES_SET {on}      Owner (money setting): the firm profile's "GST on advances" switch (A6, DEC-142 default off).
// Payments and remittances go through the manual-voucher API of acc2-manual (`accManualSave`). LOCAL STUB below until that lane merges:
// it stores the voucher in accountingX.manual (Owner → approved, Accountant → pending, contract §2) with the template, and nothing posts it
// here (the manual lane's section of accLiveVouchers does). The integrator replaces the stub with the real import (ACC2-GST-DONE.md).
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {rbacMoneyAdmin} from '../../../domain/streams/rbac';
import {accSlice,accMapping,accLockOf,ACC_SUSPENSE,type ACCState,type ACCManualVoucher,type ACCManualLine,type ACCManualTemplate} from '../../../domain/streams/accounting';
import {accPost} from '../../../domain/streams/accounting-parties';
import {accChartIndex} from '../../../domain/streams/accounting-chart';
import {
  ACC_MONTH,accMonthRange,accMonthOf,accGstMonth,accGstSetoffPosted,accGstSetoffId,accMonthLockOf,accMonthLocked,accMonthLockedMessage,
  accMonthLockBlocker,accMonthDaysToLock,accDayLockSnapshot,ACC_GST_LEDGER,ACC_STAT_LEDGER,ACC_STAT_LABEL,ACC_STAT_CARD_KIND,accGstOnAdvances,type ACCStatKind,
} from '../../../domain/streams/accounting-gst';
import {cmpSlice,cmpLegacyCard,cmpFindCard,cmpRecord,cmpParseDue,type CMPState} from '../../../domain/streams/compliance';
import {expSlice,expMonthSummary,expMonthLocked} from '../../../domain/streams/expenses-scc';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const slice=(k:Kit):ACCState=>{k.s.accountingX={...accSlice(k.s)};return k.s.accountingX;};
/** PERM-ACC-013 (contract §9): 'reports' + Owner or Accountant. */
const tax=(k:Kit,what:string)=>{k.req('reports');if(!['Owner','Accountant'].includes(k.user.role))k.fail('FORBIDDEN',`Access denied: only the Owner or the Accountant ${what}.`);};
/** PERM-ACC-012 (contract §9): 'admin' + Owner. */
const owner=(k:Kit,what:string)=>{k.req('admin');if(k.user.role!=='Owner')k.fail('FORBIDDEN',`Access denied: only the Owner ${what}.`);};
const monthOf=(k:Kit)=>{const m=txt(k.cmd.month);k.assert(ACC_MONTH.test(m),'Choose a month (YYYY-MM).');return m;};
const validDate=(d:string)=>DATE.test(d)&&!Number.isNaN(Date.parse(d+'T00:00:00Z'))&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;

// ── LOCAL STUB of acc2-manual's accManualSave (contract §2) — replaced at integration ──
type ManualInput={date:string;type:ACCManualVoucher['type'];narration:string;reference?:string;lines:ACCManualLine[];template:ACCManualTemplate};
function accManualSave(k:Kit,v:ManualInput):ACCManualVoucher{
  const {s,uid,user,assert}=k;
  const d=v.lines.reduce((a,l)=>a+l.debit,0),c=v.lines.reduce((a,l)=>a+l.credit,0);
  assert(d===c&&d>0,'The voucher does not balance.');
  assert(v.date<=s.date,'A voucher cannot be dated after the business date.');
  assert(!accLockOf(s,v.date),`${v.date} is locked — unlock the day first.`,'INVALID_TRANSITION');
  const x=slice(k),mv:ACCManualVoucher={id:k.id('MV'),date:v.date,type:v.type,narration:v.narration,...(v.reference?{reference:v.reference}:{}),lines:v.lines,
    template:v.template,status:user.role==='Owner'?'approved':'pending',by:uid,at:s.clock,...(user.role==='Owner'?{decidedBy:uid,decidedAt:s.clock}:{}),bookedOn:v.date};
  x.manual=[...(x.manual??[]),mv];
  return mv;
}
// ── end LOCAL STUB ──

/** Tender side of a payment: the ledger the method maps to, on the party it settles to (contract §3; e.g. 1030/<bank>). */
function tender(k:Kit,method:string):string{
  const map=accMapping(k.s),code=map['method:'+method],known=accChartIndex(k.s);
  k.assert(method&&code&&known.get(code)?.active&&code!==ACC_SUSPENSE,'Choose how it was paid (a payment method mapped to a cash or bank ledger).');
  return accPost(k.s,code,{method});
}
/** Common fields of a payment / remittance command. */
function payment(k:Kit){
  const {s,cmd,assert}=k;
  const month=monthOf(k),amount=Number(cmd.amount),method=txt(cmd.method),challan=txt(cmd.challan),date=txt(cmd.date)||s.date;
  assert(Number.isSafeInteger(amount)&&amount>0,'Enter the amount paid (whole paise, more than zero).');
  assert(challan.length>=3&&challan.length<=40,'Enter the challan / CIN reference (3–40 characters).');
  assert(validDate(date)&&date<=s.date,'The payment date must be a valid date on or before the business date.');
  assert(accMonthOf(date)>month,`The payment for ${month} is dated after the month (the challan is paid in the following month).`);
  if(accMonthLocked(s,date))k.fail('INVALID_TRANSITION',accMonthLockedMessage(accMonthOf(date)));
  return {month,amount,method,challan,date,acct:tender(k,method)};
}

/** The compliance card hook (contract §6 D5): the remittance marks the month's EPF / ESIC / PT card done with the challan as evidence. */
function markCard(k:Kit,kind:ACCStatKind,month:string,date:string,challan:string,note:string):string{
  const w=cmpLegacyCard(ACC_STAT_CARD_KIND[kind],month);if(!w)return ' · no compliance card for that month';
  const x:CMPState=cmpSlice(k.s);
  if(!x.profile)return ' · compliance card not marked (set the firm profile first)';
  const c=cmpFindCard(x,w.itemId,w.fy);if(!c)return ' · no compliance card for that month';
  const cur=cmpRecord(x,w.fy,w.itemId);
  if(cur?.na===true)return ` · compliance card ${w.itemId} is marked not applicable (left as it is)`;
  if(cur?.done)return ` · compliance card ${w.itemId} was already filed (left as it is)`;
  const dueDate=cmpParseDue(c.due,w.fy)||cur?.dueDate||null;
  k.s.complianceX=x;const b=(x.records[String(w.fy)]??={});
  b[w.itemId]={done:true,filedOn:date,notes:note,evidenceRef:challan,reviewerNote:'',...(dueDate?{dueDate}:{}),by:k.uid,at:k.s.clock};
  return ` · compliance card ${w.itemId} marked filed (evidence ${challan})`;
}

export const handlers:Record<string,Handler>={
  ACC_GST_SETOFF:k=>{
    const {s}=k;tax(k,'posts the GST set-off');
    const month=monthOf(k),{to}=accMonthRange(month);
    k.assert(to<s.date,`The month ${month} is not over: the set-off is posted after its last day (${to}).`,'INVALID_TRANSITION');
    if(accMonthLockOf(s,month))k.fail('INVALID_TRANSITION',accMonthLockedMessage(month));
    const cur=accGstSetoffPosted(s,month),id=accGstSetoffId(month,s.date),g=accGstMonth(s,month);
    const text=`GST set-off ${month} · output ${money(g.setoff.output.cgst)} CGST + ${money(g.setoff.output.sgst)} SGST + ${money(g.setoff.output.igst)} IGST · ITC used ${money(g.setoff.output.cgst+g.setoff.output.sgst+g.setoff.output.igst-g.setoff.payable)} · cash ledger ${money(g.setoff.payable)} · challan ${money(g.setoff.challan)} · posted ${s.date}`;
    if(cur?.id===id)return text+' · unchanged (already posted today)';
    if(cur&&accLockOf(s,cur.date))k.fail('INVALID_TRANSITION',`The set-off for ${month} posted on ${cur.date}, which is locked — the Owner unlocks that day first.`);
    const x=slice(k);x.gstSetoffs={...(x.gstSetoffs??{}),[month]:id};
    return text+(cur?` · replaces the set-off of ${cur.date}`:'');
  },
  ACC_GST_PAY:k=>{
    tax(k,'records a GST payment');
    const p=payment(k),rcm=k.cmd.rcm==null?0:Number(k.cmd.rcm);
    k.assert(Number.isSafeInteger(rcm)&&rcm>=0&&rcm<=p.amount,'The reverse-charge part is whole paise, zero or more, at most the amount paid.');
    const L=ACC_GST_LEDGER,lines:ACCManualLine[]=[{account:L.cash,debit:p.amount,credit:0,note:'Challan deposit (E2)'},{account:p.acct,debit:0,credit:p.amount},
      ...(rcm?[{account:L.rcm,debit:rcm,credit:0,note:'RCM paid in cash (E3)'},{account:L.cash,debit:0,credit:rcm}]:[])];
    const v=accManualSave(k,{date:p.date,type:'Payment',narration:`GST paid for ${p.month} · challan ${p.challan}${rcm?' · RCM '+money(rcm):''}`,reference:p.challan,lines,template:'gst-payment'});
    return `GST payment ${p.month} · ${money(p.amount)} · ${p.method} · challan ${p.challan}${rcm?` · RCM ${money(rcm)}`:''} · manual voucher ${v.id} (${v.status})`;
  },
  ACC_STAT_REMIT:k=>{
    tax(k,'records a statutory remittance');
    const kind=txt(k.cmd.kind) as ACCStatKind;k.assert(kind in ACC_STAT_LEDGER,'Choose PF, ESIC or professional tax.');
    const p=payment(k);
    const v=accManualSave(k,{date:p.date,type:'Payment',narration:`${ACC_STAT_LABEL[kind]} remitted for ${p.month} · challan ${p.challan}`,reference:p.challan,
      lines:[{account:ACC_STAT_LEDGER[kind],debit:p.amount,credit:0},{account:p.acct,debit:0,credit:p.amount}],template:'statutory-remittance'});
    const card=markCard(k,kind,p.month,p.date,p.challan,`${ACC_STAT_LABEL[kind]} ${money(p.amount)} remitted · ${p.method} · manual voucher ${v.id}`);
    return `${ACC_STAT_LABEL[kind]} remittance ${p.month} · ${money(p.amount)} · ${p.method} · challan ${p.challan} · manual voucher ${v.id} (${v.status})${card}`;
  },
  ACC_MONTH_LOCK:k=>{
    const {s,uid,id}=k;owner(k,'locks an accounting month (an Accountant requests it)');
    const month=monthOf(k),why=accMonthLockBlocker(s,month);
    if(why)k.fail(why.startsWith('Choose')?'VALIDATION':'INVALID_TRANSITION',why);
    const x=slice(k),days=accMonthDaysToLock(s,month);
    for(const d of days){const snap=accDayLockSnapshot(k.s,d);
      if(snap.debit!==snap.credit)k.fail('VALIDATION',`${d}: debits and credits do not agree.`);
      x.locks={...(x.locks??{}),[d]:{date:d,lockedAt:s.clock,lockedBy:uid,vouchers:snap.vouchers,debit:snap.debit,credit:snap.credit,count:snap.vouchers.length}};
      x.lockHistory=[...(x.lockHistory??[]),{id:id('ACCL'),at:s.clock,actor:uid,date:d,action:'lock',reason:`month lock ${month}`}];}
    const setoff=accGstSetoffPosted(s,month)!.id,prev=x.monthLocks?.[month];
    x.monthLocks={...(x.monthLocks??{}),[month]:{lockedAt:s.clock,lockedBy:uid,setoff,...(prev?.unlocks?.length?{unlocks:prev.unlocks}:{})}};
    // The SCC expense month lock (EXP_MONTH_LOCK semantics: the feed regenerated, an earlier unlock kept in its history).
    const e={...expSlice(s)},sum=expMonthSummary(s,month),ef=e.months?.[month];
    e.months={...(e.months??{}),[month]:{month,...sum,generatedAt:s.clock,by:uid,...(ef?.unlocks?.length?{unlocks:ef.unlocks}:{})}};
    k.s.expensesX=e as typeof k.s.expensesX;
    return `Month ${month} locked · ${days.length} day(s) locked now · set-off ${setoff} · expense month locked (expense ${money(sum.expense)})`;
  },
  ACC_MONTH_LOCK_REQUEST:k=>{
    const {s}=k;k.req('reports');if(k.user.role!=='Accountant')k.fail('FORBIDDEN','Access denied: the Accountant requests a month lock; the Owner locks it.');
    const month=monthOf(k),why=accMonthLockBlocker(s,month);
    if(why)k.fail(why.startsWith('Choose')?'VALIDATION':'INVALID_TRANSITION',why);
    return `Month lock requested · ${month} · for the Owner to lock`;
  },
  ACC_MONTH_UNLOCK:k=>{
    const {s,uid}=k;owner(k,'unlocks an accounting month (DEC-131)');
    const month=monthOf(k),reason=txt(k.cmd.reason);
    k.assert(accMonthLockOf(s,month),`Month ${month} is not locked.`,'INVALID_TRANSITION');
    k.assert(reason.length>=3,'A reason is required to unlock a month.');k.assert(reason.length<=200,'Keep the reason to 200 characters.');
    const x=slice(k),r=x.monthLocks![month];
    x.monthLocks={...x.monthLocks,[month]:{...r,unlocks:[...(r.unlocks??[]),{at:s.clock,by:uid,reason}]}};
    let exp='';
    if(expMonthLocked(s,`${month}-01`)){const e={...expSlice(s)},f=e.months![month];
      e.months={...e.months,[month]:{...f,stale:true,staleAt:s.clock,unlockedAt:s.clock,unlockedBy:uid,unlockReason:reason,unlocks:[...(f.unlocks??[]),{at:s.clock,by:uid,reason}]}};
      k.s.expensesX=e as typeof k.s.expensesX;exp=' · expense month unlocked';}
    return `Month ${month} unlocked · ${reason}${exp} · day locks stay (unlock a day with its reason to correct it)`;
  },
  ACC_GST_ADVANCES_SET:k=>{
    const {s,uid}=k;if(!rbacMoneyAdmin(s,uid)||k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: only the Owner changes the GST on advances switch (money setting, DEC-058).');
    const on=k.cmd.on===true;
    const x=cmpSlice(s);k.assert(x.profile,'Set the compliance firm profile first (Owner: Compliance calendar › Firm profile).','INVALID_TRANSITION');
    k.assert(accGstOnAdvances(s)!==on,`GST on advances is already ${on?'on':'off'}.`,'INVALID_TRANSITION');
    k.s.complianceX={...x,profile:{...x.profile!,gstOnAdvances:on,updatedBy:uid,updatedAt:s.clock}};
    return `GST on booking advances switched ${on?'ON — deposits received post Dr 1350 · Cr 2110 · Cr 2120 at the booked room\'s rate (A6)':'OFF — no GST on advances (DEC-142)'} · locked days keep their snapshot`;
  },
};
