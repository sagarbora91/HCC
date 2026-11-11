// F7-R2 · Report viewer frame: parameters panel, result table (desktop) or cards (phone), Export Excel / CSV / Print. Reads only the engine's masked RptResult.
import React,{useMemo,useState} from 'react';
import {Download,FileSpreadsheet,FileText,Printer,Star} from 'lucide-react';
import {money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,Pill,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {saveCsv,saveBinaryFile,saveAndReport,printPage} from '../../app/native';
import {XLSX_MIME,buildXlsx} from '../../app/xlsx';
import type {RptDefinition,RptColumn,RptRow,RptCell,RptParam,RptResult,RptFormat} from '../../domain/reports/types';
import {runReport,toXlsx,toCsv,toPrintModel,printHtml,canExportReport} from './hub-engine';
import {isChart,chartOf,chartSvg} from '../../domain/reports/charts/chart'; // F12 MEAS chart sections
import {defaultParams,exportFileName,favsOf,AREA_LABEL,STATUS_LABEL} from './hub-logic';
import {useServerReportRun,ServerRunNote,fetchHubFile,isOnServer,NOT_ON_SERVER,NOT_ON_SERVER_TEXT,type HubDef} from './hubServer'; // AF2-HUB-SERVER: production branches only

export function fmtCell(c:RptColumn,v:RptCell):string{
  if(v===null||v===undefined||v==='')return '';
  if(c.type==='money'&&typeof v==='number')return money(v);
  if(c.type==='pct'&&typeof v==='number')return v.toFixed(1)+'%';
  if(c.type==='date'&&typeof v==='string')return shortDate(v);
  if(c.type==='bool')return v?'Yes':'No';
  if(c.type==='int'&&typeof v==='number')return v.toLocaleString('en-IN');
  return String(v);
}
/** FX-VIEWER: paging covers the detail rows only; the closing total / subtotal rows of the whole report stay under every page (never hidden behind Show more). */
export function splitTail(rows:readonly RptRow[]):{body:RptRow[];tail:RptRow[]}{let n=rows.length;while(n>0&&rows[n-1].kind!=='row')n--;return {body:rows.slice(0,n),tail:rows.slice(n)};}
const isNum=(c:RptColumn)=>c.type==='money'||c.type==='int'||c.type==='pct';

/** One parameter control. `range` writes two keys (from / to); everything else writes its own key. */
export function ParamsPanel({params,raw,set,max}:{params:readonly RptParam[];raw:Record<string,string>;set:(k:string,v:string)=>void;max:string}){
  return <div className="rpt-params row" role="group" aria-label="Report parameters">
    {params.map(p=>{
      if(p.type==='range')return <React.Fragment key={p.key}><label>From <input type="date" value={raw.from??''} max={max} onChange={e=>set('from',e.target.value)}/></label><label>To <input type="date" value={raw.to??''} max={max} onChange={e=>set('to',e.target.value)}/></label></React.Fragment>;
      if(p.type==='date')return <label key={p.key}>{p.label} <input type="date" value={raw[p.key]??''} max={p.dateClass==='D-FWD'?undefined:max} onChange={e=>set(p.key,e.target.value)}/></label>;
      if(p.type==='month')return <label key={p.key}>{p.label} <input type="month" value={raw[p.key]??''} onChange={e=>set(p.key,e.target.value)}/></label>;
      if(p.type==='enum')return <label key={p.key}>{p.label} <select value={raw[p.key]??''} onChange={e=>set(p.key,e.target.value)}>{(p.options??[]).map(o=><option key={o} value={o}>{o}</option>)}</select></label>;
      if(p.type==='bool')return <label key={p.key} className="rpt-check"><input type="checkbox" checked={raw[p.key]==='true'} onChange={e=>set(p.key,String(e.target.checked))}/> {p.label}</label>;
      return <label key={p.key}>{p.label} <input type="text" value={raw[p.key]??''} onChange={e=>set(p.key,e.target.value)}/></label>;
    })}
  </div>;
}

export function useReportRun(def:RptDefinition){
  const {s,uid}=useHcc();
  const [raw,setRaw]=useState(()=>defaultParams(def.params,s.date));
  const set=(k:string,v:string)=>setRaw(r=>({...r,[k]:v}));
  // AF2-HUB-SERVER: in production the report runs on the hotel server (GET /reports/{id}?all=true), never on this device's synced slices.
  if(process.env.HCC_BUILD==='production'){const out=useServerReportRun(def,raw);return {raw,set,...out};} // eslint-disable-line react-hooks/rules-of-hooks
  const out=useMemo(()=>{try{return {result:runReport(s,uid,def.id,raw) as RptResult,error:''};}catch(e){return {result:null as RptResult|null,error:(e as Error).message||'The report could not be run.'};}},
    [s,uid,def.id,JSON.stringify(raw)]); // eslint-disable-line react-hooks/exhaustive-deps
  return {raw,set,...out,loading:false};
}

export function ExportBar({def,result,raw,phone}:{def:RptDefinition;result:RptResult|null;raw:Record<string,string>;phone?:boolean}){
  const {s,uid,toast,run}=useHcc();
  // AF2-HUB-SERVER: in production the export right is the server catalogue's (rule V + export), and the server builds and logs the file.
  const canExport=process.env.HCC_BUILD==='production'?(def as HubDef).canExport===true:canExportReport(s,uid,def);
  const has=(f:RptFormat)=>def.formats.includes(f);
  const code=(s.settings.hotel||'HCC').replace(/[^A-Za-z]/g,'').slice(0,4).toUpperCase()||'HCC';
  const log=(format:'print'|'xlsx'|'csv')=>{if(result)void run({type:'RPT_LOG',reportId:def.id,params:raw,format,rows:result.rows.length,provisional:result.about.provisional});};
  // Declared only in the production build (a const that merely goes unused would keep the sync layer in the prototype bundle).
  const serverFile=process.env.HCC_BUILD==='production'?(format:'xlsx'|'csv'|'pdf',what:string)=>{const fallback=exportFileName(code,def.id,raw,format);
    void saveAndReport(fetchHubFile(def.id,format,raw,fallback).then(f=>saveBinaryFile(f.fileName,f.bytes,f.mime)),toast,what+' saved · '+def.title,'the '+what.toLowerCase());}:undefined;
  const xl=()=>{if(!result)return;if(serverFile){serverFile('xlsx','Excel workbook');return;}
    const name=exportFileName(code,def.id,raw,'xlsx');
    void saveAndReport(saveBinaryFile(name,buildXlsx(toXlsx(result,s)),XLSX_MIME),toast,'Excel workbook saved · '+def.title,'the Excel workbook');log('xlsx');};
  const csv=()=>{if(!result)return;if(serverFile){serverFile('csv','CSV file');return;}
    void saveCsv(exportFileName(code,def.id,raw,'csv'),toCsv(result));toast('CSV saved · '+def.title);log('csv');};
  const pdf=()=>{if(result&&serverFile)serverFile('pdf','PDF file');}; // FX-VIEWER: the server's PDF (production only; the demo has no PDF writer)
  const print=()=>{if(!result)return;void printPage(def.title,printHtml(toPrintModel(result,s)));log('print');};
  const dis=!result;
  return <div className={'rpt-export'+(phone?' rpt-export-m':'')}>
    {canExport&&has('xlsx')&&<button className="btn" disabled={dis} onClick={xl}><FileSpreadsheet size={16}/> Export Excel</button>}
    {canExport&&has('csv')&&<button className="btn" disabled={dis} onClick={csv}><Download size={16}/> Export CSV</button>}
    {process.env.HCC_BUILD==='production'&&canExport&&has('pdf')&&<button className="btn" disabled={dis} onClick={pdf}><FileText size={16}/> Export PDF</button>}
    <button className="btn" disabled={dis} onClick={print}><Printer size={16}/> Print</button>
  </div>;
}

export function ResultTable({result}:{result:RptResult}){
  const {body,tail}=splitTail(result.rows);const p=usePaged(body,DESKTOP_PAGE);
  const cols=result.columns;
  return <><div className="table-wrap"><table className="rpt-table"><thead><tr>{cols.map(c=><th key={c.key} className={isNum(c)?'number':''}>{c.label}</th>)}</tr></thead>
    <tbody>{[...p.shown,...tail].map((r,i)=><tr key={i} className={r.kind==='row'?'':'cb-close rpt-'+r.kind}>{cols.map(c=>{const t=fmtCell(c,r.cells[c.key]);return <td key={c.key} className={isNum(c)?'number':''}>{r.kind==='row'?t:<strong>{t}</strong>}</td>;})}</tr>)}</tbody></table></div>
    <ShowMore p={p}/></>;
}
export function ResultCards({result}:{result:RptResult}){
  const {body,tail}=splitTail(result.rows);const p=usePaged(body,PHONE_PAGE);const [first,...rest]=result.columns;
  return <><ul className="rpt-cards" aria-label="Report rows">{[...p.shown,...tail].map((r:RptRow,i)=><li key={i} className={'rpt-card'+(r.kind==='row'?'':' total')}>
    <strong>{first?fmtCell(first,r.cells[first.key])||(r.kind==='total'?'Total':r.kind==='subtotal'?'Subtotal':'—'):''}</strong>
    <dl>{rest.map(c=>{const t=fmtCell(c,r.cells[c.key]);return t?<div key={c.key}><dt>{c.label}</dt><dd className={isNum(c)?'number':''}>{t}</dd></div>:null;})}</dl></li>)}</ul><ShowMore p={p}/></>;
}

/** F12 MEAS (STUDY-PACK item 1): every section of a multi-block report; a chart section (`chart.<kind>`) draws as inline SVG (text escaped by chartSvg). */
function ResultSections({result,phone}:{result:RptResult;phone?:boolean}){
  return <>{result.sections!.map((t,i)=>{const c=isChart(t)?chartOf(t):null;const sub={...result,columns:t.columns,rows:t.rows,sections:undefined};
    return <section key={t.key+i} className="rpt-section" aria-label={t.title||t.key}>{t.title&&<h3>{t.title}</h3>}
      {c?<div className="rpt-chart" dangerouslySetInnerHTML={{__html:chartSvg(c)}}/>:t.rows.length===0?<p className="muted">No rows.</p>:phone?<ResultCards result={sub}/>:<ResultTable result={sub}/>}</section>;})}</>;
}

export function ResultNotes({result,def}:{result:RptResult;def:RptDefinition}){
  const a=result.about;
  return <div className="rpt-notes muted">
    {a.provisional&&<Pill tone="orange">Provisional · day not closed</Pill>} {a.legacy&&<Pill tone="neutral">Includes legacy data</Pill>}
    {result.removed?.length?<p>Money columns are hidden for your role.</p>:null}
    <p>Basis: {a.dateBasis||def.dateBasis}. Run {a.runAt} by {a.user}.</p></div>;
}

export function ReportView({def,phone}:{def:RptDefinition;phone?:boolean}){
  const {s,uid,run}=useHcc();
  const {raw,set,result,error,loading}=useReportRun(def);
  const fav=favsOf(s,uid).includes(def.id);
  // AF2-HUB-SERVER: a prototype-only report is listed in production but never run on the device.
  const off=process.env.HCC_BUILD==='production'&&!isOnServer(def);
  return <div className="rpt-view" data-report={def.id}>
    <div className="rpt-view-head"><div><h2>{def.title}</h2><p className="muted">{AREA_LABEL[def.area]} · {off?NOT_ON_SERVER:STATUS_LABEL[def.status]??def.status}{def.moneyFlag?' · money':''}</p></div>
      {!off&&<button type="button" className={'btn'+(fav?' on':'')} aria-pressed={fav} onClick={()=>void run({type:'RPT_FAV_TOGGLE',reportId:def.id})}><Star size={16} fill={fav?'currentColor':'none'}/> {fav?'Favourite':'Add to favourites'}</button>}</div>
    {off?<Empty title={NOT_ON_SERVER} body={NOT_ON_SERVER_TEXT}/>:<>
    <ParamsPanel params={def.params} raw={raw} set={set} max={s.date}/>
    <ExportBar def={def} result={result} raw={raw} phone={phone}/>
    {process.env.HCC_BUILD==='production'&&!error&&<ServerRunNote result={result} loading={loading}/>}
    {error?<Empty title="Cannot show this report" body={error}/>:!result?null:<>
      <ResultNotes result={result} def={def}/>
      {result.sections&&result.sections.length>1?<ResultSections result={result} phone={phone}/>
        :result.rows.length===0?<Empty title="No rows" body="Nothing matches these parameters."/>:phone?<ResultCards result={result}/>:<ResultTable result={result}/>}</>}</>}
  </div>;
}
