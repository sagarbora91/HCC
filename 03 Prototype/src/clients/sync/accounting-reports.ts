// ACC-2 · lane acc2-parity (contract §8 "report readers beyond the 62-day window"; ACC1-DONE §7 item 7): production readers of the
// ACC-1 reports. The statement, the six-column trial balance and the day book are read from the server for ANY range
//   GET /accounting/statement?account&from&to        (StatementView)
//   GET /accounting/trial-balance?from&to&detail     (TrialBalance6View)
//   GET /accounting/day-book?from&to                 (DayBookView)
// (08 Production/src/Hcc.Contracts/Accounting/AccountingReportContracts.cs). No device derivation: the accountingX slice holds only the
// last 62 business dates, so the prototype selectors (accStatement / accTrialBalance6 / accDayBook over the slice) would miss everything
// older; the server builds the opening from its month balances (acc.ledger_month_balance) + the days after them. Same shapes as the
// prototype selectors (contract §6), so the screens render either. Production build only (no top-level calls); React hooks live in
// ./accounting-reports-hooks.ts. Permission: PERM-ACC-001, the same check as GET /accounting/trial-balance (sync/reads.ts).
import type {SyncApi} from './types';
import type {ACCStatement,ACCStatementRow,ACCTB6,ACCTB6Row,ACCDayBook,ACCDayBookRow} from '../../domain/streams/accounting-reports';
import type {ACCVoucherType} from '../../domain/streams/accounting';
import {paise,isoDate} from './map';
import {HccClientError} from '../errors';

export type SrvStatementRow={date:string;no:string;type:string;narration:string;counter:string;debitPaise:number;creditPaise:number;runningPaise:number};
export type SrvStatement={account:string;name:string;from:string;to:string;openingPaise:number;debitPaise:number;creditPaise:number;closingPaise:number;rows:SrvStatementRow[]};
export type SrvTb6Row={account:string;name:string;group:string;control?:string|null;openingDebitPaise:number;openingCreditPaise:number;debitPaise:number;creditPaise:number;
  closingDebitPaise:number;closingCreditPaise:number};
export type SrvTb6={from:string;to:string;detail:boolean;rows:SrvTb6Row[];
  totals:Omit<SrvTb6Row,'account'|'name'|'group'|'control'>;balanced:boolean;openingPending:boolean};
export type SrvDayBookRow={date:string;no:string;type:string;narration:string;account:string;ledger:string;debitPaise:number;creditPaise:number;source:string};
export type SrvDayBook={from:string;to:string;rows:SrvDayBookRow[];vouchers:number;debitPaise:number;creditPaise:number;balanced:boolean};

const TYPES:ACCVoucherType[]=['Sales','Receipt','Payment','Journal','Purchase','Contra'];
const vtype=(t:unknown):ACCVoucherType=>{if(!TYPES.includes(t as ACCVoucherType))throw new HccClientError('PROTOCOL',`Unknown voucher type ${String(t)}.`,{field:'type'});return t as ACCVoucherType;};
const txt=(v:unknown)=>typeof v==='string'?v:v===null||v===undefined?'':String(v);
const signed=(v:unknown,f:string)=>paise(v,f);

/** Validates a report range before any request (the server answers any range; from ≤ to). */
export function accReportRange(from:string,to:string){
  if(!/^\d{4}-\d{2}-\d{2}$/.test(from)||!/^\d{4}-\d{2}-\d{2}$/.test(to))throw new HccClientError('VALIDATION','Choose the dates of the report.');
  if(from>to)throw new HccClientError('VALIDATION','Choose a valid range: "from" on or before "to".');
}

export function statementFromServer(v:SrvStatement):ACCStatement{
  const rows:ACCStatementRow[]=(v.rows??[]).map((r,i)=>({date:isoDate(r.date,`rows[${i}].date`),no:txt(r.no),type:vtype(r.type),narration:txt(r.narration),counter:txt(r.counter),
    debit:paise(r.debitPaise,`rows[${i}].debitPaise`),credit:paise(r.creditPaise,`rows[${i}].creditPaise`),running:signed(r.runningPaise,`rows[${i}].runningPaise`)}));
  return {account:txt(v.account),name:txt(v.name),from:isoDate(v.from,'from'),to:isoDate(v.to,'to'),opening:signed(v.openingPaise,'openingPaise'),
    debit:paise(v.debitPaise,'debitPaise'),credit:paise(v.creditPaise,'creditPaise'),closing:signed(v.closingPaise,'closingPaise'),rows};
}
const tbCols=(r:Omit<SrvTb6Row,'account'|'name'|'group'|'control'>,at:string)=>({openingDebit:paise(r.openingDebitPaise,at+'openingDebitPaise'),openingCredit:paise(r.openingCreditPaise,at+'openingCreditPaise'),
  debit:paise(r.debitPaise,at+'debitPaise'),credit:paise(r.creditPaise,at+'creditPaise'),closingDebit:paise(r.closingDebitPaise,at+'closingDebitPaise'),closingCredit:paise(r.closingCreditPaise,at+'closingCreditPaise')});
export function tb6FromServer(v:SrvTb6):ACCTB6{
  const rows:ACCTB6Row[]=(v.rows??[]).map((r,i)=>({account:txt(r.account),name:txt(r.name),group:txt(r.group),...(r.control?{control:r.control}:{}),...tbCols(r,`rows[${i}].`)}));
  return {from:isoDate(v.from,'from'),to:isoDate(v.to,'to'),detail:!!v.detail,rows,totals:tbCols(v.totals,'totals.'),balanced:!!v.balanced,openingPending:!!v.openingPending};
}
export function dayBookFromServer(v:SrvDayBook):ACCDayBook{
  const rows:ACCDayBookRow[]=(v.rows??[]).map((r,i)=>({date:isoDate(r.date,`rows[${i}].date`),no:txt(r.no),type:vtype(r.type),narration:txt(r.narration),account:txt(r.account),
    ledger:txt(r.ledger),debit:paise(r.debitPaise,`rows[${i}].debitPaise`),credit:paise(r.creditPaise,`rows[${i}].creditPaise`),source:txt(r.source)}));
  return {from:isoDate(v.from,'from'),to:isoDate(v.to,'to'),rows,vouchers:Number(v.vouchers)||0,debit:paise(v.debitPaise,'debitPaise'),credit:paise(v.creditPaise,'creditPaise'),balanced:!!v.balanced};
}

/** A ledger / party statement for any range (contract §6). */
export async function loadAccStatement(api:SyncApi,account:string,from:string,to:string):Promise<ACCStatement>{
  const a=String(account??'').trim();if(!a)throw new HccClientError('VALIDATION','Choose a ledger or a party.');accReportRange(from,to);
  return statementFromServer(await api.get<SrvStatement>('/accounting/statement',{account:a,from,to}));
}
/** The six-column trial balance for any range (`detail` = a row per party). */
export async function loadAccTrialBalance6(api:SyncApi,from:string,to:string,detail:boolean):Promise<ACCTB6>{
  accReportRange(from,to);return tb6FromServer(await api.get<SrvTb6>('/accounting/trial-balance',{from,to,detail}));
}
/** The day book for any range. */
export async function loadAccDayBook(api:SyncApi,from:string,to:string):Promise<ACCDayBook>{
  accReportRange(from,to);return dayBookFromServer(await api.get<SrvDayBook>('/accounting/day-book',{from,to}));
}
