// F7-R1 · report engine (F7-CONTRACT §2, RPT-ARCHITECTURE §1): bind → permission → select (once) → money removal → masks → computed → page.
// The three renderers (screen, Excel, PDF / print) take the same result: figures identical by construction (golden harness tests/rpt-golden).
import type {State} from '../types';
import type {RptDefinition,RptResult,RptRow,RptColumn} from './types';
import type {XSheet,XCell} from '../../app/xlsx';
import {HccError} from '../errors';
import {csvMinimal} from '../csv';
import {reportDef,allReports} from './catalogue';
import {bindParams,paramsText} from './params';
import {canViewReport,canExportReport,holdsMoneyGate,removeMoney,applyMasks} from './mask';
import {computeColumns} from './computed';
import {cellText} from './print';
import {rptTables} from './tables';
import './register';
export {registerReport,reportDef,allReports,catalogueRecord} from './catalogue';
export {bindParams,paramsText,rptFyOf,RPT_LEGACY_BEFORE} from './params';
export {canViewReport,canExportReport,holdsMoneyGate,maskValue,rptHolds,RPT_NO_MONEY_ROLES} from './mask';
export {computeColumns,evaluateComputed,registerComputedEngine,rptDivRound,moneyColumns} from './computed';
export {toPrintModel,printHtml,printText,cellText} from './print';
export {rptTables} from './tables';

/** Screen page sizes (desktop / phone) and the file limit (RPT-ARCHITECTURE §3.1). */
export const RPT_PAGE={desktop:100,phone:50,fileMax:50000};
/** Error for a file over the row limit. ErrorCode has no RPT_TOO_LARGE yet (integration item), so it is VALIDATION with `rptCode`. */
export class RptTooLarge extends HccError{readonly rptCode='RPT_TOO_LARGE';constructor(n:number){super('VALIDATION',`RPT_TOO_LARGE: ${n.toLocaleString('en-IN')} rows is more than ${RPT_PAGE.fileMax.toLocaleString('en-IN')}. Narrow the range.`);}}

const userName=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.name.split(' · ')[0]??uid;
/** Catalogue for the caller: definitions they may view, with whether they may export and whether money columns will show. */
export function reportCatalogue(s:State,uid:string){
  return allReports().filter(d=>canViewReport(s,uid,d)).map(d=>({id:d.id,title:d.title,area:d.area,priority:d.priority,status:d.status,
    formats:d.formats,phone:d.phone,params:d.params,dateClass:d.dateClass,canExport:canExportReport(s,uid,d),money:holdsMoneyGate(s,uid,d)}));
}
export type RunOpts={/** Screen page (0-based); omitted with `all` for files. */page?:number;pageSize?:number;/** Files: every row (≤ 50,000). */all?:boolean;/** Export / print check (rule V). */purpose?:'screen'|'print'|'export'};
/** Runs one report for the caller. Throws NOT_FOUND / FORBIDDEN / VALIDATION (incl. RPT_TOO_LARGE). */
export function runReport(s:State,uid:string,id:string,raw:Record<string,unknown>={},o:RunOpts={}):RptResult{
  const def=reportDef(id);if(!def)throw new HccError('NOT_FOUND',`No report ${id}.`);
  return runDef(s,uid,def,raw,o);
}
export function runDef(s:State,uid:string,def:RptDefinition,raw:Record<string,unknown>={},o:RunOpts={}):RptResult{
  if(!canViewReport(s,uid,def))throw new HccError('FORBIDDEN',`You cannot open ${def.title}.`);
  if(o.purpose==='export'&&!canExportReport(s,uid,def))throw new HccError('FORBIDDEN',`Exporting ${def.title} needs ${def.exportPerm??'PERM-RPT-003'}.`);
  const bound=bindParams(def,raw,s);const {legacy,...p}=bound;
  const sel=def.select(s,uid,p);
  const money=holdsMoneyGate(s,uid,def);
  const removed:string[]=[];
  const sections=(sel.sections?.length?sel.sections:[{key:'main',columns:sel.columns,rows:sel.rows}]).map(t=>{
    let columns:RptColumn[]=t.columns,rows:RptRow[]=computeColumns(t.columns,t.rows);
    if(!money){const r=removeMoney(columns,rows);columns=r.columns;rows=r.rows;removed.push(...r.removed);}
    rows=applyMasks(s,uid,columns,rows);
    return {...t,columns,rows};
  }).filter(t=>t.columns.length);
  if(!sections.length)throw new HccError('FORBIDDEN',`${def.title} shows only money, and you do not hold ${def.moneyGate}.`);
  const total=sections.reduce((n,t)=>n+t.rows.length,0);
  if(o.all&&total>RPT_PAGE.fileMax)throw new RptTooLarge(total);
  let page:RptResult['page'];
  if(!o.all&&o.page!==undefined&&sections.length===1){
    const size=o.pageSize??RPT_PAGE.desktop,idx=Math.max(0,o.page);sections[0]={...sections[0],rows:sections[0].rows.slice(idx*size,idx*size+size)};page={index:idx,size,total};
  }
  const provisional=!!p.to&&p.to>=s.date&&!s.closedDates.includes(s.date)||(sel.about?.provisional??false);
  const multi=sections.length>1||!!sel.sections?.length;
  return {id:def.id,title:def.title,params:p,columns:sections[0].columns,rows:sections[0].rows,...(multi?{sections}:{}),
    about:{runAt:s.clock,user:userName(s,uid),dateBasis:def.dateBasis,legacy:legacy||(sel.about?.legacy??false),...(p.from?{from:p.from}:{}),...(p.to?{to:p.to}:{}),...sel.about,...{provisional}},
    ...(removed.length?{removed:[...new Set(removed)]}:{}),...(page?{page}:{})};
}

// ── Excel (app/xlsx.ts buildXlsx) ──
/** One Excel cell from a typed value: money as ₹ numbers, dates as dates, percent as 0.0 %. Never re-totalled. */
export function xlsxCell(c:RptColumn,v:RptRow['cells'][string]|undefined):XCell{
  if(v===null||v===undefined)return null;
  if(typeof v==='string')return c.type==='date'&&/^\d{4}-\d{2}-\d{2}$/.test(v)?{date:v}:v;
  if(c.type==='money')return {paise:v};if(c.type==='pct')return {pct:v/100};if(c.type==='int')return {int:v};
  if(c.type==='bool')return v?'Yes':'No';return v;
}
/** Sheets: one per table + About (parameters, date basis, run time, user, provisional). */
export function toXlsx(r:RptResult,property:string):XSheet[]{
  const sheets:XSheet[]=rptTables(r).map(t=>({name:t.title??r.title,widths:t.columns.map(c=>c.type==='text'?26:16),
    rows:[t.columns.map(c=>c.label),...t.rows.map(row=>t.columns.map(c=>xlsxCell(c,row.cells[c.key])))]}));
  const about:XCell[][]=[['Item','Value'],['Report',`${r.title} · ${r.id}`],['Property',property],
    ...Object.entries(paramsText(r.params)).map(([k,v])=>[k,v] as XCell[]),['Date basis',r.about.dateBasis],['Run at (IST, business clock)',r.about.runAt],['Run by',r.about.user],
    ['Provisional',r.about.provisional?'Yes — the day is not closed':'No'],...(r.about.legacy?[['Source','Legacy data (old Lodge software)'] as XCell[]]:[]),
    ...(r.removed?.length?[['Money columns removed',r.removed.join(', ')] as XCell[]]:[])];
  return [...sheets,{name:'About',widths:[28,70],rows:about}];
}
/** `<property code>-<report id>-<from>-<to>.<ext>` */
export const reportFileName=(code:string,r:RptResult,ext:string)=>[code,r.id,r.params.from??r.params.month??r.params.fy??r.params.asOf,r.params.to].filter(Boolean).join('-').replace(/[^A-Za-z0-9.-]/g,'_')+'.'+ext;

// ── CSV (formula cells neutralised by csv.ts) ──
export function toCsv(r:RptResult):string{
  const out:string[]=[];
  for(const t of rptTables(r)){
    if(t.title)out.push(csvMinimal(t.title));
    out.push(t.columns.map(c=>csvMinimal(c.label)).join(','));
    for(const row of t.rows)out.push(t.columns.map(c=>{const v=row.cells[c.key];return csvMinimal(typeof v==='number'&&c.type==='money'?(v/100).toFixed(2):cellText(c,v));}).join(','));
    out.push('');
  }
  return out.join('\r\n');
}
