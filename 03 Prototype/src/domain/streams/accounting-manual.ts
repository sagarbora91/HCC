// ACC-2 · Manual vouchers and loan party ledgers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2, §7, §9; rulebook §1 principle 2,
// §5 B5–B7, B11, A18, §6). Exported names start with 'acc' / 'ACC' (shared mock namespace). Pure helpers; commands live in
// services/mock/handlers/accounting-manual.ts, production sync in clients/sync/accounting-manual.ts, the server twin in
// 08 Production Hcc.Application/Accounting/ManualVoucherService.cs (ManualVoucherRules: same guards, same messages, same order).
//
//   save      ACC_MANUAL_SAVE {id?, date, type, narration, reference?, lines, template?} — Owner or Accountant (PERM-ACC-010). The
//             Accountant's voucher is `pending`; the Owner's own is `approved` at once (the Owner is the checker, DEC-058 / DEC-063).
//   decide    ACC_MANUAL_DECIDE {id, approve, reason?} — Owner (PERM-ACC-011); approval re-runs every guard.
//   void      ACC_MANUAL_VOID {id, reason} — Owner: on an unlocked day the voucher becomes `void` and posts nothing; on a locked day a
//             reversing voucher (same lines swapped, `voidOf`) is created and approved, dated the business date (the original stays in
//             its locked snapshot and keeps posting there).
//   posting   an approved voucher (and a voided one that has a reversal) is the source {kind:'manual', id, sub: template ?? 'journal'} on
//             its date, numbered with the day's derived vouchers at lock (accounting.ts accLiveVouchers, ACC-2 manual section).
//   loans     the chart's "Add loan" (ACC_LOAN_ADD {code, name, lender, accountLast4, kind:'secured'|'unsecured'}): Owner-added party rows
//             `2400/<CODE>` (e.g. 2400/LN-HDFC) kept in accountingX.chart.ledgers with kind 'loan', lender, accountLast4; the command's kind
//             picks the group secured-loans / unsecured-loans (contract §2 + coordinator ruling 28 Oct).
//
// Import-cycle safety: accounting.ts imports this file (the posting section), so NOTHING at the top level here may read a value imported
// from './accounting' or './accounting-chart' — functions only.
import type {State} from '../types';
import {accSlice,accLockOf,type ACCLedger,type ACCManualLine,type ACCManualTemplate,type ACCManualVoucher,type ACCVoucher} from './accounting';
import {accChartIndex,accChartSlice,accGroupOf,accLedger} from './accounting-chart';
import {accIsControl,accIsParty,accControlOf,accPartyIdOf,accPartyLedgers,accPartySlug} from './accounting-parties';
import {accCashGuard,accLoanCashRefusal} from './accounting-guards'; // ACC-2 integration: contract §7 cash guard (replaces the lane's stub)

// ── Constants ──
export const ACC_MANUAL_TYPES=['Journal','Contra','Payment','Receipt'] as const;
export type ACCManualType=typeof ACC_MANUAL_TYPES[number];
export const ACC_MANUAL_TEMPLATES:readonly ACCManualTemplate[]=['journal','contra','capital-introduced','owner-drawing-bank','loan-received','loan-emi','bank-charges',
  'interest-income','bad-debt','statutory-remittance','gst-payment','tds-remittance'];
/** Contract §9 (prototype): enter = permission('reports') and one of these roles; approve / void = permission('admin') and the Owner. */
export const ACC_MANUAL_ROLES=['Owner','Accountant'];
export const ACC_MANUAL_MIN_LINES=2;
export const ACC_MANUAL_MAX_LINES=100;
/** Largest amount on one line: ₹1,000 crore in paise (acc.source_entry bound on the server). */
export const ACC_MANUAL_MAX_PAISE=10_000_000_000;
/** A line account: a 4-digit ledger code or a party ledger id `<control>/<party>` (contract §3). */
export const ACC_MANUAL_ACCOUNT=/^\d{4}(\/[A-Za-z0-9][A-Za-z0-9._-]{0,39})?$/;
/** Ledgers never posted by hand (contract §2): Suspense and the opening-balance difference. */
export const ACC_MANUAL_NEVER=['9999','1400'];
/** Loan party id (coordinator ruling 28 Oct: the code as given, upper-case, e.g. `LN-HDFC`; party row `2400/<code>`). */
export const ACC_LOAN_ID=/^[A-Z0-9]+(-[A-Z0-9]+)*$/;
export const ACC_LOAN_CONTROL='2400';

/** Guard messages — the server (ManualVoucherRules) uses the same text, in the same order. */
export const ACC_MANUAL_MSG={
  type:'Choose the voucher type: Journal, Contra, Payment or Receipt.',
  template:'Choose a voucher template from the list.',
  narration:'Enter the narration (3–400 characters).',
  reference:'Keep the reference to 60 characters.',
  date:'Enter the voucher date (YYYY-MM-DD).',
  future:'The voucher date cannot be after the business date.',
  locked:(d:string)=>`${d} is locked — unlock the day first (the Owner reopens a locked day with a reason, DEC-077).`,
  lines:`Enter ${ACC_MANUAL_MIN_LINES} to ${ACC_MANUAL_MAX_LINES} lines.`,
  lineAccount:(n:number)=>`Line ${n}: choose a ledger code or a party ledger (code/party).`,
  lineAmount:(n:number)=>`Line ${n}: enter either a debit or a credit, in whole paise more than zero.`,
  lineLarge:(n:number)=>`Line ${n}: the amount is too large.`,
  lineNever:(n:number,code:string)=>`Line ${n}: ${code} is never posted by hand (9999 Suspense, 1400 Opening-balance difference).`,
  lineControl:(n:number,code:string)=>`Line ${n}: ${code} is a control ledger — post to one of its parties (${code}/<party>).`,
  lineUnknown:(n:number,account:string)=>`Line ${n}: ${account} is not an active ledger of the chart of accounts or a party ledger.`,
  balance:'The voucher does not balance: total debits must equal total credits.',
  contra:'A Contra voucher moves money only between cash and bank ledgers (Cash-in-Hand, Bank Accounts).',
  payment:'A Payment voucher credits cash or bank and debits no cash or bank ledger (money between cash and bank is a Contra).',
  receipt:'A Receipt voucher debits cash or bank and credits no cash or bank ledger (money between cash and bank is a Contra).',
  badDebt:'Only the Owner writes off a bad debt (rulebook A18).',
  reason:'Enter a reason (3–200 characters).',
} as const;

// ── Reading ──
export const accManualList=(s:State):ACCManualVoucher[]=>accSlice(s).manual??[];
export const accManualById=(s:State,id:string)=>accManualList(s).find(v=>v.id===id);
/** The reversing voucher of `id` (created when it was voided on a locked day), if any. */
export const accManualReversalOf=(s:State,id:string)=>accManualList(s).find(v=>v.voidOf===id);
/** Pending vouchers (the Owner's approval queue), oldest first. */
export const accManualPending=(s:State)=>accManualList(s).filter(v=>v.status==='pending');
/** Does the voucher count in the books? Approved, or voided on a locked day (its reversal undoes it on the business date). */
export const accManualPosts=(s:State,v:ACCManualVoucher)=>v.status==='approved'||(v.status==='void'&&!!accManualReversalOf(s,v.id));
/** The voucher's accounting source (contract §2). */
export const accManualSource=(v:ACCManualVoucher)=>({kind:'manual' as const,id:v.id,sub:v.template??'journal'});
export const accManualTotals=(lines:ACCManualLine[])=>lines.reduce((a,l)=>({debit:a.debit+(l.debit||0),credit:a.credit+(l.credit||0)}),{debit:0,credit:0});
/** Dates on which a manual voucher posts (accDates adds them). */
export const accManualDates=(s:State)=>[...new Set(accManualList(s).filter(v=>accManualPosts(s,v)).map(v=>v.date))];
/** The vouchers (without numbers) the manual vouchers post on `date`, in entry order — accLiveVouchers numbers them with the day's others. */
export function accManualVouchersOn(s:State,date:string):Omit<ACCVoucher,'no'>[]{
  return accManualList(s).filter(v=>v.date===date&&accManualPosts(s,v)).map(v=>({date,type:v.type,
    narration:v.narration+(v.reference?' · '+v.reference:'')+' · '+(v.voidOf?'reversal of '+v.voidOf:'manual '+v.id),
    source:accManualSource(v),entries:v.lines.filter(l=>l.debit||l.credit).map(l=>({account:l.account,debit:l.debit,credit:l.credit})),unmapped:[]}));
}
/** Lines of a reversing voucher: the same lines with debit and credit swapped. */
export const accManualSwap=(lines:ACCManualLine[]):ACCManualLine[]=>lines.map(l=>({account:l.account,debit:l.credit,credit:l.debit,...(l.note?{note:l.note}:{})}));

// ── Loans (contract §2 "Add loan") ──
export type ACCLoanLedger=ACCLedger&{kind:'loan';lender:string;accountLast4?:string};
export const accIsLoanRow=(l:ACCLedger):l is ACCLoanLedger=>(l as Partial<ACCLoanLedger>).kind==='loan';
/** Owner-added loan party rows (`2400/LN-<code>`), as stored in accountingX.chart.ledgers. */
export const accLoans=(s:State):ACCLoanLedger[]=>accChartSlice(s).ledgers.filter(accIsLoanRow);
export type ACCLoanInput={code?:unknown;name?:unknown;lender?:unknown;accountLast4?:unknown;kind?:unknown;note?:unknown};
/** Plan of ACC_LOAN_ADD: the new row or the refusal message. */
export function accLoanAddPlan(s:State,input:ACCLoanInput):{ok:true;row:ACCLoanLedger}|{ok:false;message:string;field?:string}{
  const t=(v:unknown)=>v==null?'':String(v).trim();
  const name=t(input.name),lender=t(input.lender),last4=t(input.accountLast4),note=t(input.note);
  if(name.length<3||name.length>60)return {ok:false,message:'Enter the loan name (3–60 characters).',field:'name'};
  if(lender.length<2||lender.length>60)return {ok:false,message:'Enter the lender (2–60 characters).',field:'lender'};
  if(last4&&!/^\d{4}$/.test(last4))return {ok:false,message:'The loan account number ends in 4 digits (or leave it empty).',field:'accountLast4'};
  if(note.length>200)return {ok:false,message:'Keep the note to 200 characters.',field:'note'};
  const kind=t(input.kind)||'secured';
  if(kind!=='secured'&&kind!=='unsecured')return {ok:false,message:'Choose the loan kind: secured or unsecured.',field:'kind'};
  // The code as given, upper-case (e.g. LN-HDFC); without one, LN-<lender>.
  const code=(t(input.code)?accPartySlug(t(input.code)):'LN-'+accPartySlug(lender)).slice(0,36).replace(/-+$/,'');
  if(!ACC_LOAN_ID.test(code))return {ok:false,message:'The loan code is letters and digits (e.g. LN-HDFC).',field:'code'};
  const id=`${ACC_LOAN_CONTROL}/${code}`,chart=accChartIndex(s);
  if(chart.has(id))return {ok:false,message:`Loan ${id} already exists — choose another code.`,field:'code'};
  for(const l of chart.values())if(l.name.toLowerCase()===name.toLowerCase()||(l.tallyName||'').toLowerCase()===name.toLowerCase())
    return {ok:false,message:`${l.code} is already called “${l.name}”.`,field:'name'};
  const row:ACCLoanLedger={code:id,name,group:kind==='unsecured'?'unsecured-loans':'secured-loans',system:false,gst:'none',active:true,kind:'loan',lender,...(last4?{accountLast4:last4}:{}),...(note?{note}:{})};
  return {ok:true,row};
}

// ── Guards ──
/** True when the ledger of `account` (a code or a party id) sits in Cash-in-Hand or Bank Accounts (or a sub-group below them). */
export function accManualIsCashBank(s:State,account:string):boolean{
  const l=accLedger(s,account);if(!l)return false;
  let g=accGroupOf(s,l.group);
  for(let i=0;g&&i<32;i++){if(g.id==='cash'||g.id==='bank')return true;g=g.parent?accGroupOf(s,g.parent):undefined;}
  return false;
}
/** ACC-3 C8: true when the ledger of `account` sits in Cash-in-Hand (or a sub-group of it). */
export function accManualIsCash(s:State,account:string):boolean{
  const l=accLedger(s,account);if(!l)return false;
  let g=accGroupOf(s,l.group);
  for(let i=0;g&&i<32;i++){if(g.id==='cash')return true;g=g.parent?accGroupOf(s,g.parent):undefined;}
  return false;
}
/** Party ledger ids that exist (contract §3 parties derived from the masters, plus the Owner's loan rows). */
function partyIds(s:State):Set<string>{
  const out=new Set(accPartyLedgers(s).map(p=>p.id));
  for(const l of accLoans(s))if(l.active)out.add(l.code);
  return out;
}
export type ACCManualInput={date?:unknown;type?:unknown;narration?:unknown;reference?:unknown;lines?:unknown;template?:unknown};
/** Why the voucher date cannot take a posting now ('' = it can): after the business date, or a locked day. */
export function accManualDateBlocker(s:State,date:string):string{
  if(date>s.date)return ACC_MANUAL_MSG.future;
  if(accLockOf(s,date))return ACC_MANUAL_MSG.locked(date);
  return '';
}
const realDate=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d)&&!Number.isNaN(Date.parse(d+'T00:00:00Z'))&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
/**
 * Every guard of contract §2 in order (the first one is the refusal): type, template, narration, reference, date (≤ business date, not a
 * locked day), 2–100 lines, each line (account shape, one side in whole paise, never 9999 / 1400, never a control without its party,
 * an active ledger of the effective chart or an existing party ledger), balanced, Contra only between cash / bank, Payment / Receipt with
 * exactly one cash-or-bank side. The bad-debt rule (Owner only) and the cash guards are the handler's (they need the user / the day).
 */
export function accManualProblems(s:State,x:ACCManualInput):string[]{
  const out:string[]=[],M=ACC_MANUAL_MSG;
  const type=String(x.type??'');if(!(ACC_MANUAL_TYPES as readonly string[]).includes(type))out.push(M.type);
  if(x.template!=null&&x.template!==''&&!ACC_MANUAL_TEMPLATES.includes(x.template as ACCManualTemplate))out.push(M.template);
  const narration=String(x.narration??'').trim();if(narration.length<3||narration.length>400)out.push(M.narration);
  if(String(x.reference??'').trim().length>60)out.push(M.reference);
  const date=String(x.date??'');
  if(!realDate(date))out.push(M.date);else{const b=accManualDateBlocker(s,date);if(b)out.push(b);}
  const lines=Array.isArray(x.lines)?x.lines as Partial<ACCManualLine>[]:[];
  if(lines.length<ACC_MANUAL_MIN_LINES||lines.length>ACC_MANUAL_MAX_LINES){out.push(M.lines);return out;}
  const chart=accChartIndex(s),parties=partyIds(s);let lineOk=true;
  lines.forEach((l,i)=>{
    const n=i+1,account=typeof l?.account==='string'?l.account.trim():'',d=l?.debit??0,c=l?.credit??0;
    if(!ACC_MANUAL_ACCOUNT.test(account)){out.push(M.lineAccount(n));lineOk=false;return;}
    if(!Number.isSafeInteger(d)||!Number.isSafeInteger(c)||d<0||c<0||(d>0)===(c>0)){out.push(M.lineAmount(n));lineOk=false;}
    else if(d>ACC_MANUAL_MAX_PAISE||c>ACC_MANUAL_MAX_PAISE){out.push(M.lineLarge(n));lineOk=false;}
    const code=accControlOf(account);
    if(ACC_MANUAL_NEVER.includes(code)){out.push(M.lineNever(n,code));lineOk=false;return;}
    if(!accIsParty(account)){
      if(accIsControl(account)){out.push(M.lineControl(n,account));lineOk=false;return;}
      if(!chart.get(account)?.active){out.push(M.lineUnknown(n,account));lineOk=false;}
      return;
    }
    // A party ledger: its control must be an active control ledger of the chart and the party must exist.
    if(!accIsControl(code)||!chart.get(code)?.active||!parties.has(account)){out.push(M.lineUnknown(n,account));lineOk=false;}
  });
  if(!lineOk)return out;
  const ls=lines as ACCManualLine[],t=accManualTotals(ls);
  if(t.debit!==t.credit||t.debit<=0){out.push(M.balance);return out;}
  const cb=ls.map(l=>accManualIsCashBank(s,l.account.trim()));
  if(type==='Contra'&&cb.some(v=>!v))out.push(M.contra);
  if(type==='Payment'&&(!ls.some((l,i)=>cb[i]&&l.credit>0)||ls.some((l,i)=>cb[i]&&l.debit>0)))out.push(M.payment);
  if(type==='Receipt'&&(!ls.some((l,i)=>cb[i]&&l.debit>0)||ls.some((l,i)=>cb[i]&&l.credit>0)))out.push(M.receipt);
  const b11=accLoanCashRefusal(ls.map(l=>({account:l.account.trim(),cash:accManualIsCash(s,l.account.trim())})));if(b11)out.push(b11); // ACC-3 C8 (rulebook B11)
  return out;
}
/** Lines as stored from raw command input (trimmed accounts, numbers, optional note ≤ 120). Call after accManualProblems. */
export const accManualNormalise=(lines:ACCManualLine[]):ACCManualLine[]=>lines.map(l=>{const note=l.note==null?'':String(l.note).trim().slice(0,120);
  return {account:String(l.account).trim(),debit:l.debit||0,credit:l.credit||0,...(note?{note}:{})};});
/** True when the voucher is a bad-debt write-off (template, or a debit on 5300 Bad debts written off): Owner only (rulebook A18). */
export const accManualIsBadDebt=(x:{template?:unknown;lines?:unknown})=>x.template==='bad-debt'||(Array.isArray(x.lines)&&(x.lines as Partial<ACCManualLine>[]).some(l=>String(l?.account??'').trim()==='5300'&&(l?.debit??0)>0));

// ── Cash guards (contract §7; B9 / B10) ──
/** Result of the cash guard for a manual voucher: `refused` = the s.186 receipt limit (CASH_LIMIT); `warning` = the payment limit (deduction lost). */
export type ACCManualCashResult={refused?:string;warning?:string;deductionLost?:boolean};
/** ACC-2 integration: the flows lane's guard (accounting-guards.ts, contract §7) for a manual Payment / Receipt (the stub is gone). */
function accManualCashGuard(s:State,x:{kind:'receipt'|'payment';party:string;amount:number;date:string;exclude?:string}):ACCManualCashResult{
  const g=accCashGuard(s,{kind:x.kind,party:x.party,amount:x.amount,date:x.date,...(x.exclude?{exclude:x.exclude}:{})});
  return g.refused?{refused:g.message}:g.deductionLost?{warning:g.warning,deductionLost:true}:{};
}
/** The cash guard of a Payment / Receipt that moves cash (1000 / 1005) with a party on the other side: {kind, party, amount} or undefined. */
export function accManualCashCheck(s:State,x:{type:string;date:string;lines:ACCManualLine[];exclude?:string}):ACCManualCashResult{
  if(x.type!=='Payment'&&x.type!=='Receipt')return {};
  const cash=x.lines.filter(l=>['1000','1005'].includes(accControlOf(l.account)));
  // (a manual Payment / Receipt keeps the cash guard; the voucher being re-saved / approved is excluded from the day's total)
  if(!cash.length)return {};
  const party=x.lines.find(l=>!['1000','1005'].includes(accControlOf(l.account))&&accIsParty(l.account));
  if(!party)return {};
  const amount=cash.reduce((a,l)=>a+(x.type==='Payment'?l.credit:l.debit),0);
  return accManualCashGuard(s,{kind:x.type==='Payment'?'payment':'receipt',party:party.account,amount,date:x.date,...(x.exclude?{exclude:x.exclude}:{})});
}

// ── Guided templates (contract §2; the UI lane fills the form, these build the lines) ──
export type ACCManualTemplateInput={amount?:number;bank?:string;cash?:string;loan?:string;principal?:number;interest?:number;party?:string;
  kind?:'pf'|'esic'|'pt';rcm?:boolean;from?:string;to?:string};
export type ACCManualTemplateResult={type:ACCManualType;lines:ACCManualLine[];narration:string};
const ln=(account:string,debit:number,credit:number):ACCManualLine=>({account,debit,credit});
/** The lines (and default type / narration) of a guided template. `bank` = a 1030 party id (`1030/<code>`) or a cash ledger id for
 *  capital-introduced; `loan` = a 2400 party id; `party` = the bad-debt party (`1110/…` or `1120/…`). Amounts in paise. */
export function accManualTemplateLines(t:ACCManualTemplate,x:ACCManualTemplateInput):ACCManualTemplateResult{
  const a=x.amount??0,bank=x.bank??'',loan=x.loan??'';
  switch(t){
    case 'capital-introduced':return {type:'Receipt',lines:[ln(bank||x.cash||'',a,0),ln('3100',0,a)],narration:'Capital introduced by the Owner'};
    case 'owner-drawing-bank':return {type:'Payment',lines:[ln('3000',a,0),ln(bank,0,a)],narration:'Owner drawing from the bank'};
    case 'loan-received':return {type:'Receipt',lines:[ln(bank,a,0),ln(loan,0,a)],narration:'Loan received'};
    case 'loan-emi':{const p=x.principal??0,i=x.interest??0;return {type:'Payment',lines:[ln(loan,p,0),ln('5090',i,0),ln(bank,0,p+i)].filter(l=>l.debit||l.credit),narration:'Loan EMI'};}
    case 'bank-charges':return {type:'Payment',lines:[ln('5080',a,0),ln(bank,0,a)],narration:'Bank charges'};
    case 'interest-income':return {type:'Receipt',lines:[ln(bank,a,0),ln('4960',0,a)],narration:'Interest credited by the bank'};
    case 'bad-debt':return {type:'Journal',lines:[ln('5300',a,0),ln(x.party??'',0,a)],narration:'Bad debt written off'};
    case 'statutory-remittance':{const code=x.kind==='esic'?'2320':x.kind==='pt'?'2330':'2310';
      return {type:'Payment',lines:[ln(code,a,0),ln(bank,0,a)],narration:`${(x.kind??'pf').toUpperCase()} remitted`};}
    case 'gst-payment':return x.rcm?{type:'Journal',lines:[ln('2140',a,0),ln('1340',0,a)],narration:'Reverse-charge GST paid from the electronic cash ledger'}
      :{type:'Payment',lines:[ln('1340',a,0),ln(bank,0,a)],narration:'GST deposited (challan)'};
    case 'tds-remittance':return {type:'Payment',lines:[ln('2150',a,0),ln(bank,0,a)],narration:'TDS remitted'};
    case 'contra':return {type:'Contra',lines:[ln(x.to??'',a,0),ln(x.from??'',0,a)],narration:'Contra'};
    default:return {type:'Journal',lines:[],narration:''};
  }
}
/** Display label of a template. */
export const ACC_MANUAL_TEMPLATE_LABEL:Record<ACCManualTemplate,string>={journal:'Journal',contra:'Contra (cash ↔ bank)','capital-introduced':'Capital introduced',
  'owner-drawing-bank':'Owner drawing from the bank','loan-received':'Loan received','loan-emi':'Loan EMI','bank-charges':'Bank charges','interest-income':'Interest income',
  'bad-debt':'Bad debt written off','statutory-remittance':'PF / ESIC / PT remittance','gst-payment':'GST payment','tds-remittance':'TDS remittance'};
/** Party id of a loan ledger id ('' when not a loan row). */
export const accLoanPartyId=(id:string)=>accControlOf(id)===ACC_LOAN_CONTROL?accPartyIdOf(id):'';
