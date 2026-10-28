// ACC-2 · lane acc2-parity (contract §8, rulebook D1–D4, §8.2 T13a–c): the payroll sources of the prototype's books, mirroring the server's
// SourceRules.AdvanceGiven / PayRun / PayRunPaid / PayRunReversal / Fnf / FnfPaid and AccountingEventMapper (same source keys, same mapping keys,
// same sides). Pure: State → keyed drafts; accLiveVouchers (accounting.ts, its ACC-2 parity section) resolves the keys through the mapping and
// the parties. Money = integer paise. Exported names start with 'acc' (shared mock namespace).
import type {State} from '../types';
import {hrSlice,hrRunBook,hrOn,hrFnfSettled,hrFnfAdvanceRecovered,hrMonthLabel,type HRRunBook,type HRFnf} from './staff';

/** One keyed side of a payroll source (server SourceEntry): `staff` names the 1200 party (payroll:advances, payroll:fnf-recoverable), `method`
 *  the funding's settles-to account (funding:*). */
export type ACCPayrollLine={key:string;debit:number;credit:number;staff?:string;method?:string};
export type ACCPayrollDraft={type:'Journal'|'Payment';narration:string;source:{kind:'payroll';id:string;sub:string};lines:ACCPayrollLine[]};

/** HCC pays advances, net pay and F&F by bank transfer (server PayrollService.AdvanceFunding; the bank file is simulated). */
export const ACC_PAYROLL_FUNDING='Bank transfer';
/** Mapping keys of the payroll sources and their default ledgers (server ChartOfAccounts.DefaultMapping). */
export const ACC_PAYROLL_MAP:Record<string,string>={
  'payroll:salaries':'5100','payroll:employer-pf':'5110','payroll:employer-esic':'5110',
  'payroll:net-payable':'2300','payroll:pf-payable':'2310','payroll:esic-payable':'2320',
  'payroll:pt-payable':'2330','payroll:other-deductions':'2340','payroll:advances':'1200',
  // DEC-074 (illustrative — CA to confirm): F&F additions, deductions and an amount the leaver owes (party = the leaver, as the server).
  'payroll:fnf-additions':'5100','payroll:fnf-deductions':'2340','payroll:fnf-recoverable':'1200',
};

const cr=(key:string,amount:number,x:Partial<ACCPayrollLine>={}):ACCPayrollLine=>amount>=0?{key,debit:0,credit:amount,...x}:{key,debit:-amount,credit:0,...x};
const dr=(key:string,amount:number,x:Partial<ACCPayrollLine>={}):ACCPayrollLine=>cr(key,-amount,x);
const lockSuffix=(seq:number)=>seq<=1?'':'/lock'+seq;

/** Server SourceRules.PayRun: Dr salaries · Dr employer PF · Dr employer ESIC · Cr net pay · Cr PF (employee + employer) · Cr ESIC (employee +
 *  employer) · Cr PT · Cr staff advances PER STAFF (1200/<staff>; ACC-2) · Cr other deductions. */
export function accPayRunLines(b:HRRunBook):ACCPayrollLine[]{
  return [dr('payroll:salaries',b.gross),dr('payroll:employer-pf',b.pfEr),dr('payroll:employer-esic',b.esicEr),
    cr('payroll:net-payable',b.net),cr('payroll:pf-payable',b.pf+b.pfEr),cr('payroll:esic-payable',b.esic+b.esicEr),cr('payroll:pt-payable',b.pt),
    ...b.recoveries.map(([staff,p])=>cr('payroll:advances',p,{staff})),
    // a recovery the lines do not name (never with SCC lines; kept so the voucher balances on any figures) stays on the control
    ...(b.advance!==b.recoveries.reduce((a,[,p])=>a+p,0)?[cr('payroll:advances',b.advance-b.recoveries.reduce((a,[,p])=>a+p,0))]:[]),
    cr('payroll:other-deductions',b.other)];
}
/** Server SourceRules.Fnf figures from a stored settlement (DEC-074 split: exit-month line, additions, deductions, the advance recovered). */
export function accFnfLines(f:HRFnf):ACCPayrollLine[]{
  const l=f.salaryIncluded?f.line:undefined,add=f.adjustments.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0),ded=f.adjustments.filter(a=>a.kind==='ded').reduce((n,a)=>n+a.amount,0);
  const n=(v:number|undefined)=>Number.isFinite(v)?v as number:0,staff=f.staffId;
  return [dr('payroll:salaries',n(l?.gross)),dr('payroll:fnf-additions',add),dr('payroll:employer-pf',n(l?.pfEr)),dr('payroll:employer-esic',n(l?.esicEr)),
    cr('payroll:pf-payable',n(l?.pf)+n(l?.pfEr)),cr('payroll:esic-payable',n(l?.esic)+n(l?.esicEr)),cr('payroll:pt-payable',n(l?.pt)),
    cr('payroll:advances',hrFnfAdvanceRecovered(f),{staff}),cr('payroll:fnf-deductions',ded),
    f.net>=0?cr('payroll:net-payable',f.net):dr('payroll:fnf-recoverable',-f.net,{staff})];
}
const paid=(id:string,sub:'payment'|'fnf-payment',narration:string,amount:number):ACCPayrollDraft=>({type:'Payment',narration,source:{kind:'payroll',id,sub},
  lines:[dr('payroll:net-payable',amount),cr('funding:'+ACC_PAYROLL_FUNDING,amount,{method:ACC_PAYROLL_FUNDING})]});

/** Every payroll source booked on `date` (keys as the server: advance:{id}, payroll:{run}[/lock{n}], payroll-paid:{run}[/lock{n}],
 *  payroll-unlock:{run}/lock{n}, fnf:{id}, fnf-paid:{id}). */
export function accPayrollSources(s:State,date:string):ACCPayrollDraft[]{
  const x=hrSlice(s),out:ACCPayrollDraft[]=[];
  // D3 · salary advance given (Payment): Dr 1200/<staff> · Cr funding
  for(const a of x.advances)if(a.date===date&&a.amount)out.push({type:'Payment',narration:`Salary advance given · ${ACC_PAYROLL_FUNDING} · advance ${a.id} · staff ${a.staffId}`,
    source:{kind:'payroll',id:'advance:'+a.id,sub:'advance'},lines:[dr('payroll:advances',a.amount,{staff:a.staffId}),cr('funding:'+ACC_PAYROLL_FUNDING,a.amount,{method:ACC_PAYROLL_FUNDING})]});
  // D1 / D2 · pay run posted at lock, net pay paid by the first bank file of that lock, the exact reversal at an Owner unlock
  for(const run of x.runs){
    const locks:{seq:number;on?:string;book?:HRRunBook;unlockedOn?:string;files:{on?:string;at:string;net:number}[]}[]=(run.unlocks??[]).map((u,i)=>({seq:i+1,on:u.lockedOn,book:u.book,unlockedOn:u.on,files:u.bankFiles??[]}));
    if(run.status==='Locked')locks.push({seq:locks.length+1,on:hrOn(run.lockedOn,run.lockedAt),book:hrRunBook(run),files:run.bankFiles??[]});
    let issued=0;
    for(const l of locks){
      const sfx=lockSuffix(l.seq),first=l.files[0];
      if(l.book&&l.on===date)out.push({type:'Journal',narration:`Pay run ${run.month} · run ${run.id}${l.seq>1?' · lock '+l.seq:''}`,source:{kind:'payroll',id:'payroll:'+run.id+sfx,sub:'payrun'},lines:accPayRunLines(l.book)});
      if(first&&hrOn(first.on,first.at)===date&&first.net)out.push(paid('payroll-paid:'+run.id+sfx,'payment',`Net pay paid · ${ACC_PAYROLL_FUNDING} · Bank file ${issued+1} · ${hrMonthLabel(run.month)} · run ${run.id}`,first.net));
      if(l.book&&l.unlockedOn===date){
        const flag=l.files.length?' · BANK FILE ALREADY ISSUED: net-pay payment voucher kept (not reversed); do not pay twice on re-lock':'';
        out.push({type:'Journal',narration:`Reversal of pay run ${run.month} · run ${run.id} · lock ${l.seq} unlocked · reverses payroll:${run.id}${sfx}${flag}`,
          source:{kind:'payroll',id:`payroll-unlock:${run.id}/lock${l.seq}`,sub:'payrun-reversal'},lines:accPayRunLines(l.book).map(e=>({...e,debit:e.credit,credit:e.debit}))});
      }
      issued+=l.files.length;
    }
  }
  // D4 · full and final on confirm (Journal) and its net pay (Payment) the same day
  for(const f of x.fnf??[]){
    if(!hrFnfSettled(f)||hrOn(f.confirmedOn,f.confirmedAt??f.at)!==date)continue;
    const left=Math.max(0,f.advanceOutstanding-hrFnfAdvanceRecovered(f));
    out.push({type:'Journal',narration:`Full and final ${f.month} · staff ${f.staffId} · settlement ${f.id}${f.net<0?` · recoverable from the leaver ₹${(-f.net/100).toFixed(2)}`:''}${left>0?` · advance ₹${(left/100).toFixed(2)} left open`:''}`,
      source:{kind:'payroll',id:'fnf:'+f.id,sub:'fnf'},lines:accFnfLines(f)});
    if(f.net>0)out.push(paid('fnf-paid:'+f.id,'fnf-payment',`Full and final net pay paid · ${ACC_PAYROLL_FUNDING} · settlement ${f.id}`,f.net));
  }
  return out;
}
/** Business dates with a payroll source (accDates). */
export function accPayrollDates(s:State):string[]{
  const x=hrSlice(s),d=new Set<string>();
  x.advances.forEach(a=>d.add(a.date));
  for(const run of x.runs){
    for(const u of run.unlocks??[]){if(u.lockedOn)d.add(u.lockedOn);if(u.on)d.add(u.on);const f=u.bankFiles?.[0];if(f)d.add(hrOn(f.on,f.at)!);}
    if(run.status==='Locked'){const on=hrOn(run.lockedOn,run.lockedAt);if(on)d.add(on);const f=run.bankFiles?.[0];if(f)d.add(hrOn(f.on,f.at)!);}
  }
  for(const f of x.fnf??[])if(hrFnfSettled(f)){const on=hrOn(f.confirmedOn,f.confirmedAt??f.at);if(on)d.add(on);}
  return [...d];
}
/** Mapping keys the payroll records use, one per source record side (accMapRows "uses"). */
export function accPayrollKeyUses(s:State):string[]{
  const keys:string[]=[];
  for(const date of accPayrollDates(s))for(const v of accPayrollSources(s,date))for(const l of v.lines)if(l.debit||l.credit)keys.push(l.key);
  return keys;
}
