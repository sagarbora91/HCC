// F7-R2 · Report viewer frame: parameters panel, result table (desktop) or cards (phone), Export Excel / CSV / Print. Reads only the engine's masked RptResult.
import React,{useMemo,useState} from 'react';
import {Download,FileSpreadsheet,Printer,Star} from 'lucide-react';
import {money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,Pill,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {saveCsv,saveBinaryFile,saveAndReport,printPage} from '../../app/native';
import {XLSX_MIME,buildXlsx} from '../../app/xlsx';
import type {RptDefinition,RptColumn,RptRow,RptCell,RptParam,RptResult,RptFormat} from '../../domain/reports/types';
import {runReport,toXlsx,toCsv,toPrintModel,printHtml,canExportReport} from './hub-engine';
import {defaultParams,exportFileName,favsOf,AREA_LABEL,STATUS_LABEL} from './hub-logic';

export function fmtCell(c:RptColumn,v:RptCell):string{
  if(v===null||v===undefined||v==='')return '';
  if(c.type==='money'&&typeof v==='number')return money(v);
  if(c.type==='pct'&&typeof v==='number')return v.toFixed(1)+'%';
  if(c.type==='date'&&typeof v==='string')return shortDate(v);
  if(c.type==='bool')return v?'Yes':'No';
  if(c.type==='int'&&typeof v==='number')return v.toLocaleString('en-IN');
  return String(v);
}
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
  const out=useMemo(()=>{try{return {result:runReport(s,uid,def.id,raw) as RptResult,error:''};}catch(e){return {result:null as RptResult|null,error:(e as Error).message||'The report could not be run.'};}},
    [s,uid,def.id,JSON.stringify(raw)]); // eslint-disable-line react-hooks/exhaustive-deps
  return {raw,set,...out};
}

export function ExportBar({def,result,raw,phone}:{def:RptDefinition;result:RptResult|null;raw:Record<string,string>;phone?:boolean}){
  const {s,uid,toast,run}=useHcc();
  const canExport=canExportReport(s,uid,def);
  const has=(f:RptFormat)=>def.formats.includes(f);
  const code=(s.settings.hotel||'HCC').replace(/[^A-Za-z]/g,'').slice(0,4).toUpperCase()||'HCC';
  const log=(format:'print'|'xlsx'|'csv')=>{if(result)void run({type:'RPT_LOG',reportId:def.id,params:raw,format,rows:result.rows.length,provisional:result.about.provisional});};
  const xl=()=>{if(!result)return;const name=exportFileName(code,def.id,raw,'xlsx');
    void saveAndReport(saveBinaryFile(name,buildXlsx(toXlsx(result,s)),XLSX_MIME),toast,'Excel workbook saved · '+def.title,'the Excel workbook');log('xlsx');};
  const csv=()=>{if(!result)return;void saveCsv(exportFileName(code,def.id,raw,'csv'),toCsv(result));toast('CSV saved · '+def.title);log('csv');};
  const print=()=>{if(!result)return;void printPage(def.title,printHtml(toPrintModel(result,s)));log('print');};
  const dis=!result;
  return <div className={'rpt-export'+(phone?' rpt-export-m':'')}>
    {canExport&&has('xlsx')&&<button className="btn" disabled={dis} onClick={xl}><FileSpreadsheet size={16}/> Export Excel</button>}
    {canExport&&has('csv')&&<button className="btn" disabled={dis} onClick={csv}><Download size={16}/> Export CSV</button>}
    <button className="btn" disabled={dis} onClick={print}><Printer size={16}/> Print</button>
  </div>;
}

function ResultTable({result}:{result:RptResult}){
  const p=usePaged(result.rows,DESKTOP_PAGE);
  const cols=result.columns;
  return <><div className="table-wrap"><table className="rpt-table"><thead><tr>{cols.map(c=><th key={c.key} className={isNum(c)?'number':''}>{c.label}</th>)}</tr></thead>
    <tbody>{p.shown.map((r,i)=><tr key={i} className={r.kind==='row'?'':'cb-close rpt-'+r.kind}>{cols.map(c=>{const t=fmtCell(c,r.cells[c.key]);return <td key={c.key} className={isNum(c)?'number':''}>{r.kind==='row'?t:<strong>{t}</strong>}</td>;})}</tr>)}</tbody></table></div>
    <ShowMore p={p}/></>;
}
export function ResultCards({result}:{result:RptResult}){
  const p=usePaged(result.rows,PHONE_PAGE);const [first,...rest]=result.columns;
  return <><ul className="rpt-cards" aria-label="Report rows">{p.shown.map((r:RptRow,i)=><li key={i} className={'rpt-card'+(r.kind==='row'?'':' total')}>
    <strong>{first?fmtCell(first,r.cells[first.key])||(r.kind==='total'?'Total':r.kind==='subtotal'?'Subtotal':'—'):''}</strong>
    <dl>{rest.map(c=>{const t=fmtCell(c,r.cells[c.key]);return t?<div key={c.key}><dt>{c.label}</dt><dd className={isNum(c)?'number':''}>{t}</dd></div>:null;})}</dl></li>)}</ul><ShowMore p={p}/></>;
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
  const {raw,set,result,error}=useReportRun(def);
  const fav=favsOf(s,uid).includes(def.id);
  return <div className="rpt-view" data-report={def.id}>
    <div className="rpt-view-head"><div><h2>{def.title}</h2><p className="muted">{AREA_LABEL[def.area]} · {STATUS_LABEL[def.status]??def.status}{def.moneyFlag?' · money':''}</p></div>
      <button type="button" className={'btn'+(fav?' on':'')} aria-pressed={fav} onClick={()=>void run({type:'RPT_FAV_TOGGLE',reportId:def.id})}><Star size={16} fill={fav?'currentColor':'none'}/> {fav?'Favourite':'Add to favourites'}</button></div>
    <ParamsPanel params={def.params} raw={raw} set={set} max={s.date}/>
    <ExportBar def={def} result={result} raw={raw} phone={phone}/>
    {error?<Empty title="Cannot show this report" body={error}/>:!result?null:<>
      <ResultNotes result={result} def={def}/>
      {result.rows.length===0?<Empty title="No rows" body="Nothing matches these parameters."/>:phone?<ResultCards result={result}/>:<ResultTable result={result}/>}</>}
  </div>;
}
