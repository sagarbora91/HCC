// ACC-3 · acc3-ui — the report selectors and export builders the report tabs (reports3.tsx) use. At integration the stubs were removed: the
// selectors are the report lanes' real ones (acc3-pl accounting-pl.ts, acc3-ageing accounting-ageing.ts / accounting-gst-summary.ts, acc3-tally
// accounting-tally.ts) and the files are built by acc3-exports' accounting-exports.ts. No accounting rule lives here; money = integer paise.
import {accPl,accBalanceSheet,accCashRatio,accAgeing,accGstSummary,accTallyMasters as accTallyMastersFy,accTallyVouchers,accExportName,accExportProperty,accPlTables,accBalanceSheetTable,accCashRatioTable,accAgeingTables,accGstSummaryTable,accTablesCsv,accTablesXlsx,type ExTable,type State} from '../../services/mock';
import {cfgValueOn} from '../../services/mock';
import {acc4Stamp} from './acc4-views';
import {XLSX_MIME} from '../../app/xlsx';
import {zipFiles} from '../../app/zip';

export {accPl,accBalanceSheet,accCashRatio,accAgeing,accGstSummary,accTallyVouchers};
export type {ACCPl,ACCPlLine,ACCPlGroup,ACCBsBlock,ACCBalanceSheet,ACCCashRatio} from '../../services/mock';
export type {ACCAgeing,ACCAgeingRow,ACCAgeingBill} from '../../services/mock';
export type {ACCGstSummary} from '../../services/mock';
export type {TallyMastersResult,TallyVouchersResult} from '../../services/mock';

/** The UI passes the FY start date (`2026-04-01`); the Tally selector takes `2026` / `2026-27`. */
export const accTallyMasters=(s:State,fyStart:string,company?:string)=>accTallyMastersFy(s,fyStart.slice(0,4),company);
/** Tally company name: Master configuration `brand.legalName` (Owner) when set, else the property name (server rule, TallyQueries). */
export const accTallyCompany=(s:State):string|undefined=>{const v=cfgValueOn(s,'brand.legalName');const t=typeof v==='string'?v.trim():'';return t||undefined;};

export type Acc3Report='pl'|'bs'|'ageing-r'|'ageing-p'|'gst'|'cash';
export type Acc3Args={from:string;to:string;asOf:string;month:string;fy:string};
export const ACC3_REPORTS:[Acc3Report,string][]=[['pl','Profit and loss'],['bs','Balance sheet'],['ageing-r','Outstanding receivable'],['ageing-p','Outstanding payable'],['gst','GST summary'],['cash','Cash ratio']];
const SLUG:Record<Acc3Report,string>={pl:'pl','bs':'balance-sheet','ageing-r':'ageing-receivable','ageing-p':'ageing-payable',gst:'gst-summary',cash:'cash-ratio'};

/** The export tables of one report (acc3-exports' builders over the real selectors). */
export function accReportTables(s:State,kind:Acc3Report,a:Acc3Args):{tables:ExTable[];from:string;to:string}{
  const r=accReportTablesRaw(s,kind,a);return {...r,tables:acc4Stamp(s,[r.from,r.to],r.tables)}; // F5-U1: PROVISIONAL — year reopened
}
function accReportTablesRaw(s:State,kind:Acc3Report,a:Acc3Args):{tables:ExTable[];from:string;to:string}{
  const prop=accExportProperty(s);
  if(kind==='pl')return {tables:accPlTables(prop,accPl(s,a.from,a.to)),from:a.from,to:a.to};
  if(kind==='bs')return {tables:[accBalanceSheetTable(prop,accBalanceSheet(s,a.asOf))],from:a.asOf,to:a.asOf};
  if(kind==='ageing-r'||kind==='ageing-p')return {tables:accAgeingTables(prop,accAgeing(s,a.asOf,kind==='ageing-r'?'receivable':'payable')),from:a.asOf,to:a.asOf};
  if(kind==='gst')return {tables:[accGstSummaryTable(prop,accGstSummary(s,a.month))],from:a.month,to:a.month};
  const c=accCashRatio(s,a.fy);return {tables:[accCashRatioTable(prop,c)],from:c.fy,to:c.fy};
}
export type Acc3File={name:string;bytes:Uint8Array;mime:string};
/** One report as CSV or Excel, named `<property>-<report>-<from>-<to>.<ext>`. */
export function accReportFile(s:State,kind:Acc3Report,a:Acc3Args,format:'csv'|'xlsx'):Acc3File{
  const r=accReportTables(s,kind,a),name=accExportName(accExportProperty(s),SLUG[kind],r.from,r.to,format);
  return format==='xlsx'?{name,bytes:accTablesXlsx(r.tables),mime:XLSX_MIME}:{name,bytes:new TextEncoder().encode(accTablesCsv(r.tables)),mime:'text/csv'};
}
export const accReportCsvText=(s:State,kind:Acc3Report,a:Acc3Args)=>{const r=accReportTables(s,kind,a);return {name:accExportName(accExportProperty(s),SLUG[kind],r.from,r.to,'csv'),text:accTablesCsv(r.tables)};};
/** Every report of the list in one store-only zip (one file per report, CSV or Excel). */
export function accReportsZip(s:State,a:Acc3Args,format:'csv'|'xlsx'='csv'):Acc3File{
  const files=ACC3_REPORTS.map(([k])=>accReportFile(s,k,a,format));
  return {name:accExportName(accExportProperty(s),'reports',a.from,a.to,'zip'),bytes:zipFiles(files.map(f=>({name:f.name,data:f.bytes}))),mime:'application/zip'};
}
