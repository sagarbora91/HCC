// ACC-3 lane acc3-exports · CSV / Excel / bulk-zip builders over the accounting report shapes (contract §5; rulebook 7.2; ETP-reuse spots 19 / 22).
// ONE row model per report (a `Cell[][]` table with a title block) is rendered twice: as an Excel sheet through src/app/xlsx.ts (title row bold,
// money as numbers `#,##0.00`, dates as dates) and as formula-guarded CSV (csvMinimal = the shared csvSafe guard). The ACC-1 statements keep their
// own existing CSV builders (accStatementCsv, accTrialBalance6Csv, accDayBookCsv); the sheets here carry the same figures.
// The §2–§3 report shapes are the real ones of accounting-pl.ts / accounting-ageing.ts / accounting-gst-summary.ts (stubs removed at integration).
// Pure builders: nothing here reads the clock or State beyond the statements and the party list.
import {csvMinimal} from '../csv';
import type {State} from '../types';
import {buildXlsx,XLSX_MIME,type XCell,type XSheet} from '../../app/xlsx';
import {zipFiles} from '../../app/zip';
import {accDrCr,accStatement,type ACCDayBook,type ACCStatement,type ACCTB6} from './accounting-reports';
import {accPartyLedgers} from './accounting-parties';
import type {ACCBalanceSheet,ACCCashRatio,ACCPl,ACCPlGroup} from './accounting-pl';
import type {ACCAgeing} from './accounting-ageing';
import type {ACCGstSummary} from './accounting-gst-summary';

/** A table cell: text, a plain number, a date (ISO), money in paise (`#,##0.00`), an integer, a fraction (percent) or bold text. */
export type ExCell=XCell;
export type ExTable={/** Sheet name (Excel rules applied by buildXlsx). */name:string;/** Title block + header + body, top to bottom. */rows:ExCell[][];/** Rows kept visible (title block + header). */freeze:number;widths?:number[]};
const NAMES={pl:'profit-and-loss',bs:'balance-sheet',cash:'cash-ratio',ageing:'outstanding',gst:'gst-summary',tb:'trial-balance',book:'day-book',stmt:'statement'};

/** `<property code>-<report>-<from>-<to>.<ext>` (contract §5); letters, digits, dot, dash only. A one-day range gives one date. */
export function accExportName(prop:string,report:string,from:string,to:string,ext:string):string{
  const clean=(x:string)=>String(x).replace(/[^A-Za-z0-9.]+/g,'-').replace(/^-+|-+$/g,'');
  return [clean(prop)||'HCC',clean(report),clean(from),from===to?'':clean(to)].filter(Boolean).join('-')+'.'+ext.replace(/^\./,'');
}
/** The property code that prefixes export file names. The prototype carries no separate code, so `settings.propertyCode` when present, else `HCC`. */
export const accExportProperty=(s:State):string=>String((s.settings as unknown as {propertyCode?:string}).propertyCode||'HCC');

const money=(p:number):ExCell=>({amt:p});
const date=(d:string):ExCell=>({date:d});
/** The title block: row 0 = bold "Title · from to to · PROVISIONAL", then property / range rows (dates are date cells), a note row per note, a blank row. */
function block(prop:string,title:string,range:{from:string;to:string}|{asOf:string}|{label:string},provisional:boolean,notes:string[]=[]):ExCell[][]{
  const span='from' in range?`${range.from} to ${range.to}`:'asOf' in range?`as of ${range.asOf}`:range.label;
  const rows:ExCell[][]=[[{b:`${title} · ${span}${provisional?' · PROVISIONAL':''}`}],['Property',prop]];
  if('from' in range)rows.push(['From',date(range.from)],['To',date(range.to)]);else if('asOf' in range)rows.push(['As of',date(range.asOf)]);else rows.push(['Period',range.label]);
  if(provisional)rows.push(['Status','Provisional — the range includes days that are not locked, or opening balances are pending.']);
  for(const n of notes)rows.push(['Note',n]);
  rows.push([]);return rows;
}
const head=(...h:string[]):ExCell[]=>h.map(x=>({b:x}));
const table=(name:string,top:ExCell[][],header:ExCell[],body:ExCell[][],widths?:number[]):ExTable=>({name,rows:[...top,header,...body],freeze:top.length+1,widths});

// ── ACC-1 statements ──
/** Ledger / party statement: same rows as accStatementCsv; Balance is shown without sign with a Dr / Cr column. */
export function accStatementTable(prop:string,st:ACCStatement):ExTable{
  const bal=(n:number):ExCell[]=>{const x=accDrCr(n);return [money(x.amount),x.side];};
  return table(st.name,block(prop,'Ledger statement · '+st.name,{from:st.from,to:st.to},false,['Ledger '+st.account]),
    head('Date','Voucher No','Voucher Type','Particulars','Narration','Debit','Credit','Balance','Dr/Cr'),
    [[date(st.from),'','','Opening balance','','','',...bal(st.opening)],
     ...st.rows.map(r=>[date(r.date),r.no,r.type,r.counter,r.narration,r.debit?money(r.debit):'',r.credit?money(r.credit):'',...bal(r.running)]),
     ['','','','Total for the period','',money(st.debit),money(st.credit),'',''],
     [date(st.to),'','','Closing balance','','','',...bal(st.closing)]],[12,20,14,34,40,14,14,16,6]);
}
export function accTrialBalanceTable(prop:string,tb:ACCTB6):ExTable{
  const m=(p:number):ExCell=>p?money(p):'',t=tb.totals;
  return table('Trial balance',block(prop,tb.detail?'Trial balance (party detail)':'Trial balance',{from:tb.from,to:tb.to},tb.openingPending,tb.balanced?[]:['Does not balance']),
    head('Ledger','Name','Group','Control','Opening Debit','Opening Credit','Debit','Credit','Closing Debit','Closing Credit'),
    [...tb.rows.map(r=>[r.account,r.name,r.group,r.control??'',m(r.openingDebit),m(r.openingCredit),m(r.debit),m(r.credit),m(r.closingDebit),m(r.closingCredit)]),
     [{b:'Total'},'','','',money(t.openingDebit),money(t.openingCredit),money(t.debit),money(t.credit),money(t.closingDebit),money(t.closingCredit)]],[10,36,22,10,16,16,16,16,16,16]);
}
export function accDayBookTable(prop:string,db:ACCDayBook):ExTable{
  const m=(p:number):ExCell=>p?money(p):'';
  return table('Day book',block(prop,'Day book',{from:db.from,to:db.to},false),
    head('Date','Voucher No','Voucher Type','Ledger code','Ledger','Debit','Credit','Narration','Source ID'),
    [...db.rows.map(r=>[date(r.date),r.no,r.type,r.account,r.ledger,m(r.debit),m(r.credit),r.narration,r.source]),
     [{b:'Total'},'','','','',money(db.debit),money(db.credit),`${db.vouchers} voucher(s)`,'']],[12,20,14,12,34,14,14,40,22]);
}

// ── §2 P&L, balance sheet, cash ratio ──
export function accPlTables(prop:string,pl:ACCPl):ExTable[]{
  const body:ExCell[][]=[];
  const section=(title:string,lines:{code:string;name:string;paise:number}[],total:number,totalLabel:string)=>{
    body.push([{b:title},'','']);for(const l of lines)body.push([l.code,l.name,money(l.paise)]);body.push(['',{b:totalLabel},money(total)]);body.push([]);};
  section('Revenue',pl.revenue.lines,pl.revenue.total,'Total revenue');
  section('Other income',pl.otherIncome.lines,pl.otherIncome.total,'Total other income');
  const ex=pl.expenses;
  for(const g of [ex.consumption,ex.employee,ex.finance,ex.depreciation,ex.other] as ACCPlGroup[])section('Expenses · '+g.name,g.lines,g.total,'Total '+g.name.toLowerCase());
  body.push(['',{b:'Total expenses'},money(ex.total)]);body.push(['',{b:'Profit before tax'},money(pl.profitBeforeTax)]);
  const top=block(prop,'Profit & loss',{from:pl.from,to:pl.to},pl.provisional,['No tax line (proprietor).']);
  return [table('Profit and loss',top,head('Code','Ledger','Amount'),body,[10,44,18]),
    table('By Tally group',block(prop,'Profit & loss by Tally group',{from:pl.from,to:pl.to},pl.provisional),head('Group','Name','Amount'),pl.byTallyGroup.map(g=>[g.group,g.name,money(g.paise)]),[24,44,18])];
}
export function accBalanceSheetTable(prop:string,bs:ACCBalanceSheet):ExTable{
  const body:ExCell[][]=[];
  const side=(title:string,blocks:ACCBalanceSheet['assets']['current'][],total:number)=>{
    body.push([{b:title},'','','']);
    for(const b of blocks){body.push(['',{b:b.name},'','']);for(const l of b.lines)body.push(['',l.code,l.name,money(l.paise)]);body.push(['','','Total '+b.name,money(b.total)]);}
    body.push([{b:'Total '+title.toLowerCase()},'','',money(total)]);body.push([]);};
  side('Liabilities',[bs.liabilities.ownersFunds,bs.liabilities.nonCurrent,bs.liabilities.current],bs.liabilities.total);
  side('Assets',[bs.assets.nonCurrent,bs.assets.current],bs.assets.total);
  body.push(['Balanced',bs.balanced?'Yes':'No','','']);body.push(['Suspense (9999)','','',money(bs.suspense)]);body.push(['Opening difference (1400)','','',money(bs.openingDifference)]);
  return table('Balance sheet',block(prop,'Balance sheet',{asOf:bs.asOf},bs.provisional,['HCC view — not the statutory statement.']),head('Side','Block / code','Ledger','Amount'),body,[24,22,40,18]);
}
export function accCashRatioTable(prop:string,c:ACCCashRatio):ExTable{
  return table('Cash ratio',block(prop,'Cash ratio',{label:'FY '+c.fy},false,c.flagged?['FLAGGED: a ratio is above 5 %.']:[]),head('Measure','Amount','Ratio'),
    [['Cash receipts',money(c.cashReceipts),{pct:c.receiptsPct/10000}],['Total receipts',money(c.totalReceipts),''],['Cash payments',money(c.cashPayments),{pct:c.paymentsPct/10000}],['Total payments',money(c.totalPayments),''],['Flagged',c.flagged?'Yes':'No','']],[24,18,12]);
}

// ── §3 outstanding with ageing, GST summary ──
export function accAgeingTables(prop:string,a:ACCAgeing):ExTable[]{
  const t=a.totals,side=a.side==='receivable'?'Receivables':'Payables';
  return [table('Outstanding',block(prop,side+' outstanding with ageing',{asOf:a.asOf},false),head('Party','Name','Control','Open','0-30 days','31-60 days','61-90 days','90+ days'),
      [...a.rows.map(r=>[r.party,r.name,r.control,money(r.open),money(r.b0_30),money(r.b31_60),money(r.b61_90),money(r.b90)]),
       [{b:'Total'},'','',money(t.open),money(t.b0_30),money(t.b31_60),money(t.b61_90),money(t.b90)]],[18,36,10,16,16,16,16,16]),
    table('Bills',block(prop,side+' open bills',{asOf:a.asOf},false),head('Party','Name','Bill ref','Bill date','Due','Amount','Paid','Open','Days'),
      a.rows.flatMap(r=>r.bills.map(b=>[r.party,r.name,b.ref,date(b.date),date(b.due),money(b.amount),money(b.paid),money(b.open),{int:b.days}] as ExCell[])),[18,36,24,12,12,16,16,16,8])];
}
const hs=(h:{igst:number;cgst:number;sgst:number})=>h.igst+h.cgst+h.sgst;
export function accGstSummaryTable(prop:string,g:ACCGstSummary):ExTable{
  const o=g.output,i=g.itc;
  return table('GST summary',block(prop,'GST summary',{label:g.month},false,g.ties.differences.slice()),head('Item','Amount'),
    [[{b:'Output tax'},''],['5 % slab',money(o.rate5)],['18 % slab',money(o.rate18)],['B2B',money(o.b2b)],['B2C small',money(o.b2cs)],['Advances received (11A)',money(o.advances11A)],['Advances adjusted (11B)',money(o.advances11B)],['Credit notes',money(o.creditNotes)],
     [{b:'Input tax credit'},''],['CGST',money(i.cgst)],['SGST',money(i.sgst)],['IGST',money(i.igst)],
     [{b:'Other'},''],['Reverse charge',money(g.rcm)],['Set-off · output tax',money(hs(g.setoff.output))],['Set-off · credit used',money(hs(g.setoff.credit)-hs(g.setoff.creditLeft))],['Set-off · payable in cash',money(g.setoff.payable)],['Set-off · challan',money(g.setoff.challan)],['Credit carried forward',money(hs(g.setoff.creditLeft))],['Paid in cash (challan)',money(g.cashPaid)],['TCS credit',money(g.tcsCredit)],['Cash ledger balance (1340)',money(g.cashLedgerBalance)],
     [{b:'Ties'},''],['Agrees with GSTR-1 summary',g.ties.gstr1?'Yes':'No'],['Agrees with GST month (GSTR-3B)',g.ties.gstr3b?'Yes':'No']],[34,18]);
}

// ── Renderers ──
/** The Excel sheet for a table: no header flag (the title block and header are bold cells), frozen above the body. */
export const accTableSheet=(t:ExTable):XSheet=>({name:t.name,rows:t.rows,header:false,freeze:t.freeze,widths:t.widths});
export const accTablesXlsx=(tables:ExTable[]):Uint8Array=>buildXlsx(tables.map(accTableSheet));
const cellText=(c:ExCell):string=>{
  if(c===null||c===undefined)return '';
  if(typeof c==='string')return c;if(typeof c==='number')return String(c);
  if('paise' in c)return (c.paise/100).toFixed(2);if('amt' in c)return (c.amt/100).toFixed(2);if('date' in c)return c.date;
  if('int' in c)return String(c.int);if('pct' in c)return (c.pct*100).toFixed(1)+'%';return c.b;
};
/** CSV (CRLF) of a table: every text cell passes the formula guard; numbers stay numbers. The same rows as the Excel sheet. */
export const accTableCsv=(t:ExTable):string=>t.rows.map(r=>r.map(c=>csvMinimal(cellText(c))).join(',')).join('\r\n');
/** One CSV for a multi-table report: tables one after another, a blank line and the sheet name between them. */
export const accTablesCsv=(ts:ExTable[]):string=>ts.map((t,i)=>(i?`\r\n${csvMinimal(t.name)}\r\n`:'')+accTableCsv(t)).join('\r\n');

// ── Per-report entry points: bytes + file name ──
export type ExFile={name:string;bytes:Uint8Array;mime:string};
export type ExText={name:string;text:string;mime:string};
const xl=(prop:string,report:string,from:string,to:string,ts:ExTable[]):ExFile=>({name:accExportName(prop,report,from,to,'xlsx'),bytes:accTablesXlsx(ts),mime:XLSX_MIME});
const cs=(prop:string,report:string,from:string,to:string,ts:ExTable[]):ExText=>({name:accExportName(prop,report,from,to,'csv'),text:accTablesCsv(ts),mime:'text/csv'});
export const accStatementXlsx=(prop:string,st:ACCStatement)=>xl(prop,NAMES.stmt+'-'+st.account.replace('/','-'),st.from,st.to,[accStatementTable(prop,st)]);
export const accTrialBalanceXlsx=(prop:string,tb:ACCTB6)=>xl(prop,NAMES.tb,tb.from,tb.to,[accTrialBalanceTable(prop,tb)]);
export const accDayBookXlsx=(prop:string,db:ACCDayBook)=>xl(prop,NAMES.book,db.from,db.to,[accDayBookTable(prop,db)]);
export const accPlXlsx=(prop:string,pl:ACCPl)=>xl(prop,NAMES.pl,pl.from,pl.to,accPlTables(prop,pl));
export const accPlCsv=(prop:string,pl:ACCPl)=>cs(prop,NAMES.pl,pl.from,pl.to,accPlTables(prop,pl));
export const accBalanceSheetXlsx=(prop:string,bs:ACCBalanceSheet)=>xl(prop,NAMES.bs,bs.asOf,bs.asOf,[accBalanceSheetTable(prop,bs)]);
export const accBalanceSheetCsv=(prop:string,bs:ACCBalanceSheet)=>cs(prop,NAMES.bs,bs.asOf,bs.asOf,[accBalanceSheetTable(prop,bs)]);
export const accCashRatioXlsx=(prop:string,c:ACCCashRatio)=>xl(prop,NAMES.cash,c.fy,c.fy,[accCashRatioTable(prop,c)]);
export const accCashRatioCsv=(prop:string,c:ACCCashRatio)=>cs(prop,NAMES.cash,c.fy,c.fy,[accCashRatioTable(prop,c)]);
export const accAgeingXlsx=(prop:string,a:ACCAgeing)=>xl(prop,NAMES.ageing+'-'+a.side,a.asOf,a.asOf,accAgeingTables(prop,a));
export const accAgeingCsv=(prop:string,a:ACCAgeing)=>cs(prop,NAMES.ageing+'-'+a.side,a.asOf,a.asOf,accAgeingTables(prop,a));
export const accGstSummaryXlsx=(prop:string,g:ACCGstSummary)=>xl(prop,NAMES.gst,g.month,g.month,[accGstSummaryTable(prop,g)]);
export const accGstSummaryCsv=(prop:string,g:ACCGstSummary)=>cs(prop,NAMES.gst,g.month,g.month,[accGstSummaryTable(prop,g)]);

// ── Bulk: one statement per party, zipped ──
export type ACCPartiesFilter='all'|'receivable'|'payable';
export const ACC_RECEIVABLE_CONTROLS=['1100','1110','1115','1120'],ACC_PAYABLE_CONTROLS=['2200'];
/** Party ledgers a bulk export covers: receivable = parties under 1100 / 1110 / 1115 / 1120, payable = 2200, all = every party ledger. */
export const accBulkParties=(s:State,which:ACCPartiesFilter)=>accPartyLedgers(s).filter(p=>which==='all'||(which==='receivable'?ACC_RECEIVABLE_CONTROLS:ACC_PAYABLE_CONTROLS).indexOf(p.control)>=0);
/** `GET /accounting/export/statements.zip` on the prototype: one CSV (or xlsx) per party that has an opening balance or movement in the range, a stored zip. */
export function accStatementsZip(s:State,o:{from:string;to:string;parties?:ACCPartiesFilter;format?:'csv'|'xlsx';prop?:string}):{name:string;bytes:Uint8Array;mime:string;files:string[]}{
  const prop=o.prop??accExportProperty(s),fmt=o.format??'csv',which=o.parties??'all';
  const entries:{name:string;data:string|Uint8Array}[]=[];
  for(const p of accBulkParties(s,which)){
    const st=accStatement(s,p.id,o.from,o.to);if(!st.opening&&!st.rows.length)continue;
    const slug=p.name.replace(/[^A-Za-z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,40);
    const t=accStatementTable(prop,st);
    entries.push({name:accExportName(prop,NAMES.stmt+'-'+p.id.replace('/','-')+(slug?'-'+slug:''),o.from,o.to,fmt),data:fmt==='xlsx'?accTablesXlsx([t]):accTableCsv(t)});
  }
  const bytes=zipFiles(entries);
  return {name:accExportName(prop,'statements-'+which,o.from,o.to,'zip'),bytes,mime:'application/zip',files:entries.map(e=>e.name)};
}
