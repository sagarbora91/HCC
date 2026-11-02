// ACC-4 · F5-U1 — Accounting › Year end / Bank reconciliation / Clearing: role gates and pure display helpers (no React).
// The figures and commands are the F5-A1 lane's (domain/streams/accounting-year.ts, accounting-bank.ts; handlers of the same names).
import type {State} from '../../services/mock';
import {accYearProvisional,ACC_PROVISIONAL_TEXT,accBankSlice,accBankLayouts,accPartyLedgers,accYearSlice,accFyOfDate,ACC_YEAR_FIRST,accFyNext,type AccBankLineRef,type BankStatementRow} from '../../services/mock';

/** Contract §4. PERM-ACC-014 year preview / carry-forward draft = Owner, Accountant · PERM-ACC-015 lock / reopen / relock = Owner ·
 *  PERM-ACC-016 bank import / match / settlement = Owner, Accountant · PERM-ACC-017 layout master = Owner · clearing view also for the Manager (read). */
export const acc4Gates=(role:string|undefined)=>({
  year:role==='Owner'||role==='Accountant',
  yearLock:role==='Owner',
  bank:role==='Owner'||role==='Accountant',
  layout:role==='Owner',
  clearing:role==='Owner'||role==='Accountant'||role==='Manager',
});
export type Acc4Gates=ReturnType<typeof acc4Gates>;

/** Financial years to offer: the first HCC year up to the year of the business date (and the next one once a year has ended). */
export function acc4Years(s:State):string[]{
  const out:string[]=[];let fy=ACC_YEAR_FIRST;const last=accFyOfDate(s.date);
  while(fy<=last&&out.length<30){out.push(fy);fy=accFyNext(fy);}
  return out;
}
/** Years reopened (exports carry the provisional mark until relocked). */
export const acc4Provisional=(s:State)=>accYearSlice(s).years.filter(y=>y.provisionalExports).map(y=>y.fy);
export const acc4DraftOf=(s:State,fy:string)=>accYearSlice(s).carryForwards.find(c=>c.fromFy===fy&&c.status==='draft');

/** Bank ledgers to choose from: layouts, imports and whatever 1030/<bank> ledgers the books already hold. */
export function acc4BankLedgers(s:State,extra:string[]=[]):string[]{
  const x=accBankSlice(s);
  const own=accPartyLedgers(s).filter(p=>p.control==='1030'&&p.active).map(p=>p.id);
  return [...new Set([...own,...accBankLayouts(s).map(l=>l.bankLedger),...x.imports.map(i=>i.bankLedger),...extra])].filter(l=>/^1030\/./.test(l)).sort();
}
/** Same key as the match store (voucher id + line). */
export const acc4LineKey=(r:AccBankLineRef)=>r.voucherId+'#'+r.line;
/** Statement row amount from the bank's view: credit = money in. */
export const acc4RowAmount=(r:Pick<BankStatementRow,'debit'|'credit'>)=>r.credit-r.debit;
export const ACC4_BAND_LABEL={ok:'Within 3 days',amber:'Over 3 days',red:'Over 7 days'} as const;
export const ACC4_BAND_PILL={ok:'green',amber:'orange',red:'acc-sev-error'} as const;
export const ACC4_CLEARING_NAME:Record<string,string>={'1010':'UPI clearing','1020':'Card clearing'};

/** Contract §1.2: while a year is reopened every export of it (and the next) carries "PROVISIONAL — year reopened". Adds a Status row under the title row of
 *  each table and the mark to the title; tables are returned unchanged when no date of the report is in a reopened year. */
export function acc4Stamp<T extends {rows:unknown[][];freeze?:number}>(s:State,dates:string[],tables:T[]):T[]{
  if(!dates.some(d=>/^\d{4}-\d{2}/.test(d)&&accYearProvisional(s,d.length===7?d+'-01':d.slice(0,10))))return tables;
  return tables.map(t=>{
    const rows=t.rows.map(r=>r.slice());const first=rows[0]?.[0];
    if(first&&typeof first==='object'&&'b' in (first as object))rows[0][0]={b:`${(first as {b:string}).b} · ${ACC_PROVISIONAL_TEXT}`};
    rows.splice(1,0,['Status',ACC_PROVISIONAL_TEXT]);
    return {...t,rows,...(typeof t.freeze==='number'?{freeze:t.freeze+1}:{})};
  });
}
