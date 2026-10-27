// ACC-1 · Opening balances entered by hand (contract §5, 02 Planning/agents/ACC1-POSTING-CONTRACT.md; rulebook §G2; DEC-139 / DEC-148).
// One opening entry per property, dated the start of the business financial year (1 April). The Manager or the Accountant enters it
// (it may be saved unbalanced: the gap is parked on 1400 Opening-balance difference, added here and shown), the Owner approves it, and
// the first period lock of any date on or after `asOf` is refused while the entry is a draft or 1400 ≠ 0 ("opening balances pending").
// After approval the entry is read-only until the Owner reopens it, which is possible only while no lock exists on or after `asOf`.
// Exported names start with 'acc' / 'ACC' (shared mock namespace). Money = integer paise. Pure helpers only.
import type {State} from '../types';
import {money} from '../money';
import {accSlice,accAccount,ACC_SUSPENSE,type ACCOpening,type ACCOpeningLine,type ACCVoucher,type ACCSourceKind} from './accounting';

/** The ledger that carries the difference of an unbalanced opening entry (contract §2, group `suspense`). */
export const ACC_OPENING_DIFF='1400';
/** Voucher number suffix and source of the opening journal (contract §5). */
export const ACC_OPENING_SOURCE={kind:'opening',id:'opening',sub:'opening-entry'} as const;
/** Upper bound of lines in one entry (one line per ledger and per party). */
export const ACC_OPENING_MAX_LINES=500;
/** Largest amount on one line: ₹1,000 crore in paise (same bound as acc.source_entry on the server). */
export const ACC_OPENING_MAX_PAISE=10_000_000_000;
/** An account of an opening line: a 4-digit ledger code or a party ledger id `<control code>/<party id>` (contract §3). Party ids are
 *  plain strings here; the parties lane resolves them. */
export const ACC_OPENING_ACCOUNT=/^\d{4}(\/[A-Za-z0-9][A-Za-z0-9._-]{0,35})?$/;

/** Start of the financial year (1 April) that contains `date` (YYYY-MM-DD). */
export function accFyStart(date:string):string{
  const y=Number(date.slice(0,4)),m=Number(date.slice(5,7));
  return `${m>=4?y:y-1}-04-01`;
}
/** The control (ledger) code of an account: '1110/CMP-0007' → '1110'. */
export const accOpeningCode=(account:string)=>account.split('/')[0];

/** The hand-entered opening entry, or undefined when none was saved. */
export const accOpening=(s:State):ACCOpening|undefined=>accSlice(s).opening;

/** The balancing line for a set of lines: Σ debit − Σ credit parked on 1400 on the short side (undefined when they balance). */
export function accOpeningBalancingLine(lines:ACCOpeningLine[]):ACCOpeningLine|undefined{
  const d=lines.reduce((a,l)=>a+l.debit,0),c=lines.reduce((a,l)=>a+l.credit,0);
  if(d===c)return undefined;
  return d>c?{account:ACC_OPENING_DIFF,debit:0,credit:d-c}:{account:ACC_OPENING_DIFF,debit:c-d,credit:0};
}
/** Lines as entered (the 1400 line dropped) plus the recomputed 1400 line — what a save stores. */
export function accOpeningWithBalance(lines:ACCOpeningLine[]):ACCOpeningLine[]{
  const own=lines.filter(l=>l.account!==ACC_OPENING_DIFF),bal=accOpeningBalancingLine(own);
  return bal?[...own,bal]:own;
}
/** Signed balance of 1400 in the entry (debit positive). 0 when there is no entry or no difference. */
export function accOpeningDifference(s:State):number{
  return (accOpening(s)?.lines??[]).filter(l=>l.account===ACC_OPENING_DIFF).reduce((a,l)=>a+l.debit-l.credit,0);
}
export function accOpeningTotals(o:Pick<ACCOpening,'lines'>|undefined):{debit:number;credit:number}{
  return (o?.lines??[]).reduce((a,l)=>({debit:a.debit+l.debit,credit:a.credit+l.credit}),{debit:0,credit:0});
}

/** The opening journal `JRN/<asOf yyyymmdd>/000` dated `asOf` for the entry in any status (draft shown as such), or undefined. */
export function accOpeningVoucher(s:State):ACCVoucher|undefined{
  const o=accOpening(s);if(!o||!o.lines.length)return undefined;
  return {no:`JRN/${o.asOf.split('-').join('')}/000`,date:o.asOf,type:'Journal',
    narration:`Opening balances as of ${o.asOf}`+(o.status==='approved'?'':' (draft, not approved)'),
    source:{kind:ACC_OPENING_SOURCE.kind as ACCSourceKind,id:ACC_OPENING_SOURCE.id,sub:ACC_OPENING_SOURCE.sub},
    entries:o.lines.filter(l=>l.debit||l.credit).map(l=>({account:l.account,debit:l.debit,credit:l.credit})),unmapped:[]};
}
/** The opening journal as it counts in the books: only an APPROVED entry posts (a draft never does), as a 0- or 1-element list.
 *  Production (`accountingX.server`): [] — the server carries the approved entry in its cumulative trial balance (as the opening that
 *  supersedes an imported one), which the sync layer turns into the `opening-import` voucher of `server.opening`. */
export function accOpeningPosted(s:State):ACCVoucher[]{
  if(accSlice(s).server)return [];
  const o=accOpening(s),v=o?.status==='approved'?accOpeningVoucher(s):undefined;
  return v?[v]:[];
}

/** True until an approved opening entry with 1400 = 0 exists (DEC-148: "opening balances pending" on the trial balance). */
export function accOpeningPending(s:State):boolean{
  const o=accOpening(s);
  return !o||o.status!=='approved'||accOpeningDifference(s)!==0;
}

/** Why a period lock of `date` is refused because of the opening entry ('' = it is not). Contract §5: the lock of any date on or after
 *  `asOf` is refused while the entry is a draft or 1400 ≠ 0. No entry saved yet → '' (ledgers run from zero, DEC-148; see the ACC-1
 *  opening DONE file, open item 1). */
export function accOpeningLockBlocker(s:State,date:string):string{
  const o=accOpening(s);if(!o||date<o.asOf)return '';
  if(o.status!=='approved')return `Opening balances pending: the opening entry as of ${o.asOf} is a draft — the Owner approves it before the first period lock.`;
  const diff=accOpeningDifference(s);
  if(diff!==0)return `Opening balances pending: ${money(Math.abs(diff))} ${diff>0?'Dr':'Cr'} sits in ${ACC_OPENING_DIFF} Opening-balance difference — the Owner reopens the opening entry and corrects it before the first period lock.`;
  return '';
}
/** Dates locked on or after `asOf` (reopening the approved entry is refused while any exists). */
export function accOpeningLocksFrom(s:State,asOf:string):string[]{
  return Object.keys(accSlice(s).locks??{}).filter(d=>d>=asOf).sort();
}

export type ACCOpeningProblem={severity:'error'|'warning';line?:number;account?:string;message:string};
/** Problems of a set of lines as entered (line numbers 1-based). Errors refuse the save; warnings are shown and never block. */
export function accOpeningLineProblems(lines:unknown):ACCOpeningProblem[]{
  const out:ACCOpeningProblem[]=[];
  if(!Array.isArray(lines)){out.push({severity:'error',message:'Enter the opening lines.'});return out;}
  if(lines.length>ACC_OPENING_MAX_LINES)out.push({severity:'error',message:`Enter at most ${ACC_OPENING_MAX_LINES} lines.`});
  const seen=new Set<string>();
  lines.forEach((raw,i)=>{
    const n=i+1,l=(raw??{}) as Partial<ACCOpeningLine>,account=typeof l.account==='string'?l.account.trim():'';
    if(account===ACC_OPENING_DIFF)return; // recomputed on every save
    if(!ACC_OPENING_ACCOUNT.test(account)){out.push({severity:'error',line:n,account,message:`Line ${n}: choose a ledger code or a party ledger (code/party).`});return;}
    if(accOpeningCode(account)===ACC_SUSPENSE)out.push({severity:'error',line:n,account,message:`Line ${n}: ${ACC_SUSPENSE} Suspense cannot carry an opening balance.`});
    if(accOpeningCode(account)===ACC_OPENING_DIFF)out.push({severity:'error',line:n,account,message:`Line ${n}: ${ACC_OPENING_DIFF} is filled automatically with the difference.`});
    const d=l.debit??0,c=l.credit??0;
    if(!Number.isSafeInteger(d)||!Number.isSafeInteger(c)||d<0||c<0)out.push({severity:'error',line:n,account,message:`Line ${n}: amounts are whole paise, zero or more.`});
    else if((d>0)===(c>0))out.push({severity:'error',line:n,account,message:`Line ${n}: enter either a debit or a credit.`});
    else if(d>ACC_OPENING_MAX_PAISE||c>ACC_OPENING_MAX_PAISE)out.push({severity:'error',line:n,account,message:`Line ${n}: the amount is too large.`});
    if(seen.has(account))out.push({severity:'error',line:n,account,message:`Line ${n}: ${account} is entered twice — one line per ledger and per party.`});
    seen.add(account);
    if(!accAccount(accOpeningCode(account)))out.push({severity:'warning',line:n,account,message:`Line ${n}: ${accOpeningCode(account)} is not in the chart of accounts.`});
  });
  if(!lines.some(l=>(l as ACCOpeningLine)?.account!==ACC_OPENING_DIFF))out.push({severity:'error',message:'Enter at least one opening line.'});
  return out;
}
/** Problems of the saved entry: the pending state and the line warnings (for the screen and the trial balance banner). */
export function accOpeningProblems(s:State):ACCOpeningProblem[]{
  const o=accOpening(s);
  if(!o)return [{severity:'warning',message:'Opening balances pending: no opening entry has been entered yet.'}];
  const out=accOpeningLineProblems(o.lines).filter(p=>p.severity==='warning');
  const diff=accOpeningDifference(s);
  if(diff!==0)out.unshift({severity:'warning',account:ACC_OPENING_DIFF,message:`${money(Math.abs(diff))} ${diff>0?'Dr':'Cr'} sits in ${ACC_OPENING_DIFF} Opening-balance difference: the first period lock waits until it is zero.`});
  if(o.status!=='approved')out.unshift({severity:'warning',message:'Opening balances pending: the Owner has not approved the opening entry.'});
  const t=accOpeningTotals(o);
  if(t.debit!==t.credit)out.unshift({severity:'error',message:'The opening entry does not balance.'});
  return out;
}
/** Lines as stored from raw command input (trimmed accounts, numbers; the 1400 line recomputed). Call after accOpeningLineProblems. */
export function accOpeningNormalise(lines:ACCOpeningLine[]):ACCOpeningLine[]{
  return accOpeningWithBalance(lines.map(l=>({account:String(l.account).trim(),debit:l.debit??0,credit:l.credit??0})).filter(l=>l.account!==ACC_OPENING_DIFF));
}
