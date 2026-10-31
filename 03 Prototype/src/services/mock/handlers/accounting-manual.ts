// ACC-2 · Manual vouchers and loan party ledgers — command handlers (posting contract §2, §7, §9; rules in domain/streams/accounting-manual.ts).
//   ACC_MANUAL_SAVE   {id?, date, voucherType, narration, reference?, lines, template?}   Owner or Accountant with 'reports' (PERM-ACC-010). The Owner's own
//                                                                                 voucher is approved at once; the Accountant's is pending. With id: a
//                                                                                 pending / rejected voucher is replaced (its maker or the Owner).
//   ACC_MANUAL_DECIDE {id, approve, reason?}                                       Owner with 'admin' (PERM-ACC-011); approval re-runs every guard;
//                                                                                 a rejection needs a reason.
//   ACC_MANUAL_VOID   {id, reason}                                                 Owner (PERM-ACC-011): unlocked day → void, posts nothing; locked day →
//                                                                                 a reversing voucher (lines swapped, voidOf) approved, dated the business date.
//   ACC_LOAN_ADD      {code, name, lender, accountLast4?, kind:'secured'|'unsecured', note?}   Owner (chart edit, PERM-ACC-007): party row 2400/<CODE>
//                                                                                 (e.g. 2400/LN-HDFC), group secured-loans / unsecured-loans.
// accManualSave(k, {date, type, narration, reference?, lines, template?}) = the manual-voucher API other ACC-2 handlers call (tds / gst lanes'
// remittances replace their local `accManualSave` stub with this export).
// Cash guards (contract §7) run through accManualCashCheck → the flows lane's accCashGuard (ACC-2 integration).
// The audit row of each command is written by execute() from the returned detail text. Production: clients/sync/accounting-manual.ts.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice,type ACCState,type ACCManualVoucher,type ACCManualLine,type ACCManualTemplate,type ACCChartChange} from '../../../domain/streams/accounting';
import {accChartSlice,accCanEditChart} from '../../../domain/streams/accounting-chart';
import {accManualProblems,accManualNormalise,accManualById,accManualIsBadDebt,accManualCashCheck,accManualTotals,accManualSwap,accManualDateBlocker,
  accLoanAddPlan,ACC_MANUAL_ROLES,ACC_MANUAL_MSG,type ACCManualType,type ACCLoanInput} from '../../../domain/streams/accounting-manual';

/** Writable manual list on the cloned state (creates the slice for old saves). */
function manual(k:Kit):ACCManualVoucher[]{const x:ACCState={...accSlice(k.s)};x.manual=[...(x.manual??[])];k.s.accountingX=x;return x.manual;}
const enter=(k:Kit)=>{k.req('reports');if(!ACC_MANUAL_ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: only the Owner or the Accountant enters manual vouchers (PERM-ACC-010).');};
const owner=(k:Kit,what:string)=>{k.req('admin');if(k.user.role!=='Owner')k.fail('FORBIDDEN',`Access denied: only the Owner ${what} manual vouchers (PERM-ACC-011).`);};
const reasonOf=(k:Kit)=>{const r=String(k.cmd.reason??'').trim();k.assert(r.length>=3&&r.length<=200,ACC_MANUAL_MSG.reason);return r;};
const refuse=(k:Kit,problems:string[])=>{if(problems.length)k.fail(/unlock the day first/.test(problems[0])?'INVALID_TRANSITION':'VALIDATION',problems[0]);};
const total=(lines:ACCManualLine[])=>money(accManualTotals(lines).debit);

/** The input of the manual-voucher API (contract §2 shape: `type` is the voucher type). */
export type ACCManualSaveInput={id?:string;date:string;type:string;narration:string;reference?:string;lines:ACCManualLine[];template?:ACCManualTemplate};
/**
 * The manual-voucher API for other ACC-2 handlers (ACC_TDS_REMIT, ACC_GST_PAY, ACC_STAT_REMIT — contract §5, §6): runs every guard of contract
 * §2 (and the cash guard), then stores the voucher — approved at once when the acting user is the Owner, pending for the Accountant. The caller
 * checks its own permission first (this checks only the bad-debt rule). Returns the stored voucher and the audit text.
 */
export function accManualSave(k:Kit,x:ACCManualSaveInput):{voucher:ACCManualVoucher;detail:string}{
  const {s,uid,user}=k;
  if(accManualIsBadDebt(x)&&user.role!=='Owner')k.fail('FORBIDDEN',ACC_MANUAL_MSG.badDebt);
  const id=x.id==null||x.id===''?'':String(x.id);
  const old=id?accManualById(s,id):undefined;
  if(id){
    k.assert(old,'This manual voucher was not found.','NOT_FOUND');
    k.assert(old!.status==='pending'||old!.status==='rejected','Only a pending or rejected voucher can be changed — void an approved one instead.','INVALID_TRANSITION');
    if(old!.by!==uid&&user.role!=='Owner')k.fail('FORBIDDEN','Access denied: only the voucher’s maker or the Owner changes it.');
  }
  refuse(k,accManualProblems(s,x));
  const lines=accManualNormalise(x.lines),type=String(x.type) as ACCManualType,date=String(x.date);
  const cash=accManualCashCheck(s,{type,date,lines,...(id?{exclude:id}:{})});
  if(cash.refused)k.fail('VALIDATION',cash.refused);
  const approved=user.role==='Owner',reference=String(x.reference??'').trim(),template=x.template?String(x.template) as ACCManualTemplate:undefined;
  const v:ACCManualVoucher={id:id||k.id('MV'),date,type,narration:String(x.narration).trim(),...(reference?{reference}:{}),lines,...(template?{template}:{}),
    status:approved?'approved':'pending',by:uid,at:s.clock,...(approved?{decidedBy:uid,decidedAt:s.clock,bookedOn:date}:{}),...(cash.deductionLost?{cashDeductionLost:true}:{})};
  const list=manual(k),i=list.findIndex(x=>x.id===v.id);if(i<0)list.push(v);else list[i]=v;
  return {voucher:v,detail:`Manual ${type} ${v.id} dated ${date} ${id?'changed':'saved'} · ${total(lines)} · ${approved?'approved (Owner)':'pending the Owner’s approval'}${cash.warning?' · '+cash.warning:''}`};
}

export const handlers:Record<string,Handler>={
  // Contract §2 names the voucher type `type`; a prototype command's `type` is the command name, so the payload carries `voucherType`.
  ACC_MANUAL_SAVE:k=>{enter(k);const c=k.cmd;
    return accManualSave(k,{id:c.id,date:c.date,type:c.voucherType,narration:c.narration,reference:c.reference,lines:c.lines,template:c.template}).detail;},
  ACC_MANUAL_DECIDE:k=>{
    owner(k,'approves or rejects');const {s,cmd,uid}=k;
    const v=accManualById(s,String(cmd.id??''));k.assert(v,'This manual voucher was not found.','NOT_FOUND');
    k.assert(v!.status==='pending','This manual voucher is no longer pending.','INVALID_TRANSITION');
    const list=manual(k),i=list.findIndex(x=>x.id===v!.id);
    if(cmd.approve===true){
      refuse(k,accManualProblems(s,v!));
      const cash=accManualCashCheck(s,{type:v!.type,date:v!.date,lines:v!.lines,exclude:v!.id}); // ACC-2 integration: the cash guard counts once approved
      if(cash.refused)k.fail('VALIDATION',cash.refused);
      list[i]={...v!,status:'approved',decidedBy:uid,decidedAt:s.clock,bookedOn:v!.date,...(cash.deductionLost?{cashDeductionLost:true}:{})};
      return `Manual ${v!.type} ${v!.id} dated ${v!.date} approved · ${total(v!.lines)}`;
    }
    const reason=reasonOf(k);
    list[i]={...v!,status:'rejected',decidedBy:uid,decidedAt:s.clock,reason};
    return `Manual ${v!.type} ${v!.id} rejected · ${reason}`;
  },
  ACC_MANUAL_VOID:k=>{
    owner(k,'voids');const {s,cmd,uid}=k;
    const v=accManualById(s,String(cmd.id??''));k.assert(v,'This manual voucher was not found.','NOT_FOUND');
    k.assert(v!.status==='approved','Only an approved manual voucher can be voided.','INVALID_TRANSITION');
    k.assert(!v!.voidOf,'A reversing voucher cannot be voided — enter a new voucher instead.','INVALID_TRANSITION');
    const reason=reasonOf(k);
    const list=manual(k),i=list.findIndex(x=>x.id===v!.id);
    if(!accSlice(s).locks?.[v!.date]){
      list[i]={...v!,status:'void',reason,voidedBy:uid};
      return `Manual ${v!.type} ${v!.id} dated ${v!.date} voided (unlocked day: posts nothing) · ${reason}`;
    }
    // Locked day: the original stays in its snapshot; a reversing voucher dated the business date undoes it (DEC-077 / rulebook G5).
    const blocker=accManualDateBlocker(s,s.date);if(blocker)k.fail('INVALID_TRANSITION',blocker);
    const rid=k.id('MV');
    list[i]={...v!,status:'void',reason,voidedBy:uid};
    list.push({id:rid,date:s.date,type:v!.type,narration:`Reversal of ${v!.id} · ${v!.narration}`.slice(0,400),...(v!.reference?{reference:v!.reference}:{}),
      lines:accManualSwap(v!.lines),...(v!.template?{template:v!.template}:{}),status:'approved',by:uid,at:s.clock,decidedBy:uid,decidedAt:s.clock,reason,voidOf:v!.id,bookedOn:s.date});
    return `Manual ${v!.type} ${v!.id} dated ${v!.date} voided · ${v!.date} is locked: reversing voucher ${rid} dated ${s.date} · ${reason}`;
  },
  ACC_LOAN_ADD:k=>{
    if(!accCanEditChart(k.s,k.uid))k.fail('FORBIDDEN','Access denied: only the Owner adds a loan to the chart of accounts (PERM-ACC-007).');
    const p=accLoanAddPlan(k.s,k.cmd as ACCLoanInput);if(!p.ok)k.fail(/already/.test(p.message)?'INVALID_TRANSITION':'VALIDATION',p.message);
    const row=(p as Extract<typeof p,{ok:true}>).row;
    const x:ACCState={...accSlice(k.s)},c=accChartSlice(k.s);
    const change:ACCChartChange={id:k.id('ACCC'),at:k.s.clock,actor:k.uid,kind:'ledger',code:row.code,field:'created',before:'',
      after:`${row.code} ${row.name} · loan from ${row.lender}${row.accountLast4?' · a/c …'+row.accountLast4:''}`};
    x.chart={ledgers:[...c.ledgers,row].sort((a,b)=>a.code<b.code?-1:1),groups:[...c.groups],history:[...c.history,change]};k.s.accountingX=x;
    return `Loan ${row.code} ${row.name} added under 2400 (${row.group==='unsecured-loans'?'unsecured':'secured'}) · lender ${row.lender}`;
  },
};
