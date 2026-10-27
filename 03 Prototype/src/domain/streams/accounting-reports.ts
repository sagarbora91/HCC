// ACC-1 lane acc1-statements · Accounting reports over the vouchers: ledger / party statement with a running balance, the six-column
// trial balance (opening Dr/Cr · period Dr/Cr · closing Dr/Cr, control or party detail) and the day book, with a CSV builder for each.
// Contract: 02 Planning/agents/ACC1-POSTING-CONTRACT.md §6 (shapes copied exactly), §3 (party ledgers `<code>/<partyId>`, `detail`),
// §5 (the opening entry voucher). Rules: 02 Planning/HCC_Accounting_Rulebook_v1.md §7.1 (day book), §7.2 (statement), §7.3 (trial balance),
// §G2 ("opening balances pending"). Pure selectors; money = integer paise. Exported names start with 'acc' / 'ACC' (shared mock namespace).
//
// Opening (contract §6): every voucher dated before `from` — the locked snapshot for a locked date, the live derivation elsewhere
// (`accVouchersUpTo`, which also carries the opening entry once the acc1-opening lane lands it, and in a production build the server's
// window opening). An opening voucher (the hand-entered opening entry `opening-entry`, the imported `opening-import`, the server's
// balance-before-window `opening`) dated ON `from` also counts as opening: the FY's opening journal is the opening balance of a report
// that starts on the FY start (rulebook §7.2 "incl. opening journal").
import {csvMinimal} from '../csv';
import type {State} from '../types';
import {ACC_CHART,accAccount,accSlice,accVouchersUpTo,type ACCVoucher,type ACCVoucherType} from './accounting';

// ── Contract §6 shapes (copied exactly) ──
export type ACCStatementRow={date:string;no:string;type:ACCVoucherType;narration:string;counter:string;  // counter = the other side's ledger name(s)
  debit:number;credit:number;running:number};                                                        // running = Dr positive / Cr negative
export type ACCStatement={account:string;name:string;from:string;to:string;
  opening:number;debit:number;credit:number;closing:number;rows:ACCStatementRow[]};              // opening/closing signed as `running`
export type ACCTB6Row={account:string;name:string;group:string;control?:string;                          // control set on party rows (detail = true)
  openingDebit:number;openingCredit:number;debit:number;credit:number;closingDebit:number;closingCredit:number};
export type ACCTB6={from:string;to:string;detail:boolean;rows:ACCTB6Row[];
  totals:Omit<ACCTB6Row,'account'|'name'|'group'|'control'>;balanced:boolean;openingPending:boolean};
// Day book (contract §6: "every voucher in the range in `date, no` order, one row per entry"; rulebook §7.1 columns).
export type ACCDayBookRow={date:string;no:string;type:ACCVoucherType;narration:string;account:string;ledger:string;debit:number;credit:number;source:string};
export type ACCDayBook={from:string;to:string;rows:ACCDayBookRow[];vouchers:number;debit:number;credit:number;balanced:boolean};

// ── Ledger names, groups and order (chart-as-a-master aware; party ledgers `<code>/<partyId>`) ──
/** Control code of a ledger id: `1110/CMP-0007` → `1110`; a plain code is its own control. */
export const accRptControl=(account:string)=>{const i=account.indexOf('/');return i<0?account:account.slice(0,i);};
/** Party id of a party ledger id (`1110/CMP-0007` → `CMP-0007`); undefined for a plain code. */
export const accRptParty=(account:string)=>{const i=account.indexOf('/');return i<0?undefined:account.slice(i+1);};
const ownLedger=(s:State,code:string)=>accSlice(s).chart?.ledgers.find(l=>l.code===code);
const codeName=(s:State,code:string)=>ownLedger(s,code)?.name??accAccount(code)?.name??accSlice(s).server?.accounts?.[code]?.name??code;
/** Party display name. INTEGRATION (acc1-parties): replace the fallback with `accPartyName(s, account)` from accounting-parties.ts. */
const partyName=(_s:State,account:string)=>accRptParty(account)??account;
/** Ledger display name: the chart's name for a code (Owner override / Owner-added ledger first), `<control name> · <party name>` for a party ledger. */
export function accRptLedgerName(s:State,account:string):string{
  const p=accRptParty(account);return p===undefined?codeName(s,account):`${codeName(s,accRptControl(account))} · ${partyName(s,account)}`;
}
/** Group of a ledger (a party ledger takes its control's group): the chart's group id when the chart master carries one, else the legacy group. */
export function accRptLedgerGroup(s:State,account:string):string{
  const c=accRptControl(account);return ownLedger(s,c)?.group??accAccount(c)?.group??accSlice(s).server?.accounts?.[c]?.group??'Suspense';
}
const chartOrder=(code:string)=>{const i=ACC_CHART.findIndex(a=>a.code===code);return i<0?ACC_CHART.length:i;};
const byChart=(a:string,b:string)=>chartOrder(a)-chartOrder(b)||(a<b?-1:a>b?1:0);
const byDateNo=(a:ACCVoucher,b:ACCVoucher)=>a.date<b.date?-1:a.date>b.date?1:a.no<b.no?-1:a.no>b.no?1:0;

// ── Opening / period split ──
const OPENING_SUBS=new Set(['opening-entry','opening-import','opening']);
/** True for an opening voucher (hand-entered opening entry, imported opening, server balance-before-window). */
export const accRptIsOpening=(v:ACCVoucher)=>OPENING_SUBS.has(v.source.sub)||(v.source.kind as string)==='opening';
/** Vouchers before the range (the opening; an opening voucher dated on `from` included) and in it, each in `date, no` order. */
export function accRptVouchers(s:State,from:string,to:string):{opening:ACCVoucher[];period:ACCVoucher[]}{
  const opening:ACCVoucher[]=[],period:ACCVoucher[]=[];
  const end=to<from?from:to;
  for(const v of accVouchersUpTo(s,end)){
    if(v.date<from||(v.date===from&&accRptIsOpening(v)))opening.push(v);
    else if(v.date<=to)period.push(v);
  }
  return {opening:opening.sort(byDateNo),period:period.sort(byDateNo)};
}
/** Does a voucher entry belong to the statement of `account`? A control (or any plain code) takes its party ledgers too; a party ledger only itself. */
const matches=(account:string,entry:string)=>entry===account||(!account.includes('/')&&entry.startsWith(account+'/'));

// ── Dr / Cr display (never negative on screen) ──
/** A signed balance (Dr positive / Cr negative) as an amount and its side; zero has no side. */
export const accDrCr=(n:number):{amount:number;side:'Dr'|'Cr'|''}=>({amount:Math.abs(n),side:n>0?'Dr':n<0?'Cr':''});
const rupees=(p:number)=>(p/100).toFixed(2);
/** `1234.50 Dr` / `80.00 Cr` / `0.00` — the CSV / print form of a signed balance. */
export const accDrCrText=(n:number)=>{const x=accDrCr(n);return x.side?`${rupees(x.amount)} ${x.side}`:rupees(0);};

// ── 7.2 Ledger / party statement ──
/** Statement of a ledger code, a control (its parties included) or a party ledger `<code>/<partyId>` for [from, to]. */
export function accStatement(s:State,account:string,from:string,to:string):ACCStatement{
  const {opening:before,period}=accRptVouchers(s,from,to);
  let opening=0;
  for(const v of before)for(const e of v.entries)if(matches(account,e.account))opening+=e.debit-e.credit;
  const rows:ACCStatementRow[]=[];let running=opening,debit=0,credit=0;
  for(const v of period){
    const mine=v.entries.filter(e=>matches(account,e.account));if(!mine.length)continue;
    const d=mine.reduce((a,e)=>a+e.debit,0),c=mine.reduce((a,e)=>a+e.credit,0);if(!d&&!c)continue;
    const others=v.entries.filter(e=>!matches(account,e.account));
    const counter=[...new Set((others.length?others:mine).map(e=>accRptLedgerName(s,e.account)))].join(', ');
    running+=d-c;debit+=d;credit+=c;
    rows.push({date:v.date,no:v.no,type:v.type,narration:v.narration,counter,debit:d,credit:c,running});
  }
  return {account,name:accRptLedgerName(s,account),from,to,opening,debit,credit,closing:opening+debit-credit,rows};
}

// ── 7.3 Six-column trial balance ──
/** Opening balances pending (rulebook §G2): no approved opening entry, or its balancing line on 1400 Opening-balance difference ≠ 0. */
export function accRptOpeningPending(s:State):boolean{
  const o=accSlice(s).opening;if(!o||o.status!=='approved')return true;
  return o.lines.filter(l=>l.account!=='1400').reduce((a,l)=>a+l.debit-l.credit,0)!==0||o.lines.filter(l=>l.account==='1400').reduce((a,l)=>a+l.debit-l.credit,0)!==0;
}
type Sums={od:number;oc:number;d:number;c:number};
const zero=():Sums=>({od:0,oc:0,d:0,c:0});
const net=(dr:number,cr:number)=>({debit:Math.max(dr-cr,0),credit:Math.max(cr-dr,0)});
/** Six-column trial balance for [from, to]. `detail = false`: parties summed into their control (one row per ledger code);
 *  `detail = true`: a row per party ledger (`control` set) right after its control's own row (shown only when something posted to the
 *  control itself, e.g. a `no-party` entry). Opening and closing are netted per row (Dr or Cr, never both); period columns are gross.
 *  Rows with nothing in any column are left out. `balanced` = Σ Dr = Σ Cr in all three column pairs. */
export function accTrialBalance6(s:State,from:string,to:string,detail:boolean):ACCTB6{
  const {opening,period}=accRptVouchers(s,from,to);
  const m=new Map<string,Sums>();
  const add=(account:string,k:'o'|'p',dr:number,cr:number)=>{const id=detail?account:accRptControl(account);const x=m.get(id)??zero();
    if(k==='o'){x.od+=dr;x.oc+=cr;}else{x.d+=dr;x.c+=cr;}m.set(id,x);};
  for(const v of opening)for(const e of v.entries)add(e.account,'o',e.debit,e.credit);
  for(const v of period)for(const e of v.entries)add(e.account,'p',e.debit,e.credit);
  const ids=[...m.keys()].sort((a,b)=>{const ca=accRptControl(a),cb=accRptControl(b);if(ca!==cb)return byChart(ca,cb);
    const pa=accRptParty(a),pb=accRptParty(b);if(pa===undefined)return -1;if(pb===undefined)return 1;
    const na=partyName(s,a).toLowerCase(),nb=partyName(s,b).toLowerCase();return na<nb?-1:na>nb?1:a<b?-1:1;});
  const rows:ACCTB6Row[]=[];
  for(const id of ids){
    const x=m.get(id)!;const o=net(x.od,x.oc),c=net(x.od+x.d,x.oc+x.c);
    if(!o.debit&&!o.credit&&!x.d&&!x.c&&!c.debit&&!c.credit)continue;
    const party=accRptParty(id)!==undefined;
    rows.push({account:id,name:accRptLedgerName(s,id),group:accRptLedgerGroup(s,id),...(party?{control:accRptControl(id)}:{}),
      openingDebit:o.debit,openingCredit:o.credit,debit:x.d,credit:x.c,closingDebit:c.debit,closingCredit:c.credit});
  }
  const totals=rows.reduce((a,r)=>({openingDebit:a.openingDebit+r.openingDebit,openingCredit:a.openingCredit+r.openingCredit,debit:a.debit+r.debit,credit:a.credit+r.credit,
    closingDebit:a.closingDebit+r.closingDebit,closingCredit:a.closingCredit+r.closingCredit}),{openingDebit:0,openingCredit:0,debit:0,credit:0,closingDebit:0,closingCredit:0});
  const balanced=totals.openingDebit===totals.openingCredit&&totals.debit===totals.credit&&totals.closingDebit===totals.closingCredit;
  return {from,to,detail,rows,totals,balanced,openingPending:accRptOpeningPending(s)};
}

// ── 7.1 Day book ──
/** Every voucher dated in [from, to] (an opening voucher dated on `from` belongs to the opening, not the day book), `date, no` order, one row per entry. */
export function accDayBook(s:State,from:string,to:string):ACCDayBook{
  const {period}=accRptVouchers(s,from,to);const rows:ACCDayBookRow[]=[];let debit=0,credit=0;
  for(const v of period)for(const e of v.entries){
    rows.push({date:v.date,no:v.no,type:v.type,narration:v.narration,account:e.account,ledger:accRptLedgerName(s,e.account),debit:e.debit,credit:e.credit,source:v.source.id});
    debit+=e.debit;credit+=e.credit;
  }
  return {from,to,rows,vouchers:period.length,debit,credit,balanced:debit===credit};
}

// ── CSV (CRLF, rupees with 2 decimals, formula-guarded cells) ──
const csv=(rows:(string|number)[][])=>rows.map(r=>r.map(c=>csvMinimal(c)).join(',')).join('\r\n');
const amt=(p:number)=>p?rupees(p):'';
export const ACC_STATEMENT_CSV_HEADER=['Date','Voucher No','Voucher Type','Particulars','Narration','Debit','Credit','Balance'];
/** Statement CSV: title lines, then the opening line, one line per voucher with its running balance (`Dr` / `Cr`), the totals and the closing line. */
export function accStatementCsv(st:ACCStatement):string{
  return csv([['Ledger statement',st.name],['Ledger',st.account],['From',st.from],['To',st.to],[],ACC_STATEMENT_CSV_HEADER,
    [st.from,'','','Opening balance','','','',accDrCrText(st.opening)],
    ...st.rows.map(r=>[r.date,r.no,r.type,r.counter,r.narration,amt(r.debit),amt(r.credit),accDrCrText(r.running)]),
    ['','','','Total for the period','',rupees(st.debit),rupees(st.credit),''],
    [st.to,'','','Closing balance','','','',accDrCrText(st.closing)]]);
}
export const ACC_TB6_CSV_HEADER=['Ledger','Name','Group','Control','Opening Debit','Opening Credit','Debit','Credit','Closing Debit','Closing Credit'];
/** Six-column trial balance CSV: title lines (range, summary / detail, opening-pending and balance flags), one line per row, the totals line. */
export function accTrialBalance6Csv(tb:ACCTB6):string{
  const t=tb.totals;
  return csv([['Trial balance',tb.detail?'Party detail':'Summary'],['From',tb.from],['To',tb.to],
    ...(tb.openingPending?[['Opening balances pending']]:[]),...(tb.balanced?[]:[['Does not balance']]),[],ACC_TB6_CSV_HEADER,
    ...tb.rows.map(r=>[r.account,r.name,r.group,r.control??'',amt(r.openingDebit),amt(r.openingCredit),amt(r.debit),amt(r.credit),amt(r.closingDebit),amt(r.closingCredit)]),
    ['Total','','','',rupees(t.openingDebit),rupees(t.openingCredit),rupees(t.debit),rupees(t.credit),rupees(t.closingDebit),rupees(t.closingCredit)]]);
}
export const ACC_DAYBOOK_CSV_HEADER=['Date','Voucher No','Voucher Type','Ledger code','Ledger','Debit','Credit','Narration','Source ID'];
/** Day book CSV: one line per entry in `date, no` order, then the totals line. */
export function accDayBookCsv(db:ACCDayBook):string{
  return csv([ACC_DAYBOOK_CSV_HEADER,...db.rows.map(r=>[r.date,r.no,r.type,r.account,r.ledger,amt(r.debit),amt(r.credit),r.narration,r.source]),
    ['Total','','','','',rupees(db.debit),rupees(db.credit),`${db.vouchers} voucher(s)`,'']]);
}
