// SCR-15 Reports: tabs, business-date range, drill-down from every total to its source rows, CSV per tab, print with watermark.
import {printDocument,saveCsv,saveBinaryFile,saveAndReport} from '../../app/native';
import {XLSX_MIME} from '../../app/xlsx';
import {reportXlsx} from './exportXlsx';
import React,{useMemo,useState} from 'react';
import {Download,Printer,Info,X,Hotel,FileSpreadsheet} from 'lucide-react';
import {money,reportMonthRange,reportPack,reportCsv,reportDrillCsv,reportDictionary,ReportPack,ReportMetric,ReportTab,reportRangeLimit,reportRangePresets,historyMonths,rbacCanExportReports} from '../../services/mock';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {HistorySummary} from './HistorySummary';
import {reportPackMemo} from '../../domain/memo';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,dateLabel,shortDate} from '../../components/ui';
import {useServerReport,ServerReportStrip,ServerDetailNote,ServerHistorySummary,fyMonths,SERVER_DETAIL_NOTE} from './serverBits';
import {asOfText,NO_FETCH} from './asOf';
import {REPORT_MAX_DAYS} from '../../clients/sync/reports';

/** CSV export: a download in the browser, app-cache exports/ + share sheet in the Android app. */
export function downloadCsv(name:string,csv:string){void saveCsv(name,csv);}
const maskName=(n?:string)=>n?n.split(' ').map(w=>w[0]+'•••').join(' '):n;
/** Restricted scope (no folio access): guest names masked on screen, print and export alike. */
function scoped(pack:ReportPack,mask:boolean):ReportPack{
  if(!mask)return pack;
  return {...pack,tabs:pack.tabs.map(t=>({...t,metrics:t.metrics.map(m=>({...m,rows:m.rows.map(r=>({...r,guest:maskName(r.guest)}))}))}))};
}
const fmt=(v:string|number,u:string)=>typeof v==='number'?(u==='money'?money(v):String(v)):v;
const TABS=[['daily','Daily operations'],['revenue','Revenue & tax'],['receipts','Receipts & deposits'],['expenses','Expenses & cash'],['occupancy','Occupancy'],['dictionary','Report dictionary']];

export function Reports(){
  const {s,uid,permission,toast,open}=useHcc();
  const canExport=rbacCanExportReports(s,uid); // DEC-063 G13: RPT-003 export = Owner, Manager, Accountant (not the Cashier)
  const [tab,setTab]=useState('daily');const [from,setFrom]=useState(s.date);const [to,setTo]=useState(s.date);const [drill,setDrill]=useState<string|null>(null);
  const valid=/^\d{4}-\d{2}-\d{2}$/.test(from)&&/^\d{4}-\d{2}-\d{2}$/.test(to)&&from<=to&&to<=s.date;
  const span=valid?Math.round((Date.parse(to)-Date.parse(from))/86400000)+1:0;
  const limit=process.env.HCC_BUILD==='production'?REPORT_MAX_DAYS:reportRangeLimit(s),tooLong=span>limit;
  const mask=!permission('billing')&&!permission('reservations');
  // Keyed on the records the pack reads (not on all of `s`), so unrelated state changes do not recompute a long range.
  // Production: the server's report for the range (a constant branch, so the hook order never changes); prototype: computed from the slices.
  const srv=process.env.HCC_BUILD==='production'?useServerReport(from,to,valid&&!tooLong):NO_FETCH; // eslint-disable-line react-hooks/rules-of-hooks
  const raw=useMemo(()=>process.env.HCC_BUILD==='production'?(srv.data?.pack??null):valid&&!tooLong?reportPackMemo(s,from,to):null,[s.bookings,s.expenses,s.rooms,s.posX,s.closings,s.closedDates,s.date,s.openingCash,s.shiftClose,from,to,valid,tooLong,srv.data]); // eslint-disable-line react-hooks/exhaustive-deps
  const pack=useMemo(()=>raw?scoped(raw,mask):null,[raw,mask]);
  const t=pack?.tabs.find(x=>x.id===tab);
  const presets=useMemo(()=>reportRangePresets(s),[s.date,s.historyX]),months=useMemo(()=>process.env.HCC_BUILD==='production'?fyMonths(s.date):s.historyX?historyMonths(s).months:[],[s.closings,s.bookings,s.historyX,s.date]); // eslint-disable-line react-hooks/exhaustive-deps
  const m=t?.metrics.find(x=>x.id===drill);
  const setRange=(a:string,b:string)=>{setFrom(a);setTo(b);setDrill(null);};
  function exportTab(){if(!pack)return;downloadCsv(`HCC-Demo-${tab}-${from}_${to}.csv`,reportCsv(s,pack,tab));toast('Demo CSV downloaded · '+(TABS.find(x=>x[0]===tab)?.[1]));}
  function exportExcel(){if(!pack)return;
    let bytes:Uint8Array;try{bytes=reportXlsx({hotel:s.settings.hotel,scenario:s.scenario,clock:s.clock},pack);}catch{toast('Could not build the Excel workbook for this range. Try a shorter range (for example one month).',true);return;}
    void saveAndReport(saveBinaryFile(`HCC-Demo-reports-${from}_${to}.xlsx`,bytes,XLSX_MIME),toast,'Demo Excel workbook saved · all report tabs','the Excel workbook');}
  return <><PageTitle title="Reports" sub={process.env.HCC_BUILD==='production'?'Figures from the hotel server · '+dateLabel(s.date):'Every figure derived from source records · fictional data · '+dateLabel(s.date)} action={<div className="inline">
      {canExport&&<><button className="btn" disabled={!pack} onClick={exportTab}><Download size={16}/> Export CSV</button>
      <button className="btn" disabled={!pack} onClick={exportExcel}><FileSpreadsheet size={16}/> Export Excel</button></>}
      <button className="btn" disabled={!pack} onClick={()=>open({type:'report-print',id:tab,room:from+'|'+to})}><Printer size={16}/> Print</button></div>}/>
    {process.env.HCC_BUILD==='production'?<ServerHistorySummary/>:<HistorySummary/>}
    <section className="card f-filters" aria-label="Report filters">
      <label>From (business date)<input type="date" value={from} max={s.date} onChange={e=>{setFrom(e.target.value);setDrill(null);}}/></label>
      <label>To<input type="date" value={to} max={s.date} onChange={e=>{setTo(e.target.value);setDrill(null);}}/></label>
      <div className="inline f-quick">{presets.map(p=><button key={p.id} className="btn small-btn" onClick={()=>setRange(p.from,p.to)}>{p.label}</button>)}
        {months.length>0&&<label className="f-month">Month<select value="" onChange={e=>{const m=e.target.value;if(m){const r=reportMonthRange(s,m);setRange(r.from,r.to);}}}><option value="">Choose…</option>{months.map(x=><option key={x.month} value={x.month}>{x.label}</option>)}</select></label>}</div>
      {!valid&&<p className="f-why" role="alert">Choose a valid range: “from” on or before “to”, and not after today’s business date ({shortDate(s.date)}).</p>}
      {tooLong&&<p className="f-why" role="alert">Choose at most {limit} days{process.env.HCC_BUILD==='production'?' (the server report limit)':' in this demo'}.</p>}
      {process.env.HCC_BUILD==='production'&&valid&&!tooLong&&<ServerReportStrip f={srv}/>}
      {mask&&<p className="muted">Restricted scope: guest names are masked.</p>}
    </section>
    <div className="tabs f-report-tabs" role="tablist" aria-label="Report">{TABS.map(([id,label])=><button key={id} role="tab" aria-selected={tab===id} className={tab===id?'active':''} onClick={()=>{setTab(id);setDrill(null);}}>{label}</button>)}</div>
    {tab==='dictionary'?<Dictionary/>:!pack||!t?<Empty title={process.env.HCC_BUILD==='production'&&valid&&!tooLong&&srv.status!=='error'?'Loading the report…':'No report for this range'} body={process.env.HCC_BUILD==='production'&&valid&&!tooLong?(srv.status==='error'?'The server did not answer. Try Refresh.':'Waiting for the server.'):'Adjust the date range.'}/>:<>
      <div className="f-metrics">{t.metrics.map(x=><button key={x.id} className={'f-metric '+(drill===x.id?'on':'')} aria-expanded={drill===x.id} onClick={()=>setDrill(drill===x.id?null:x.id)}>
        <span>{x.label}</span><strong>{x.unit==='money'?money(x.value):x.value}</strong><small>{process.env.HCC_BUILD==='production'?'Server figure · details on the server PC':<>{x.rows.length} source row{x.rows.length===1?'':'s'} · view ›</>}</small>{x.note&&<small>{x.note}</small>}</button>)}
        {t.ratios.map(r=><div key={r.id} className="f-metric ratio"><span>{r.label}</span><strong>{r.display}</strong><small>{r.formula}</small></div>)}</div>
      {m&&(process.env.HCC_BUILD==='production'?<ServerDetailNote label={m.label}/>:<Drill key={m.id+pack.from+pack.to} pack={pack} m={m} mask={mask} onClose={()=>setDrill(null)}/>)}
      {t.table&&<section className="card"><div className="section-title"><h2>By business date</h2><span className="muted">{shortDate(from)} – {shortDate(to)}</span></div><TableView t={t}/></section>}
      <div className="info-note"><Info size={17}/>Charges follow service date, receipts follow receipt date, expenses follow business date. Daily cash need not equal daily revenue. GST from the dated GST master (illustrative — CA to confirm).</div>
    </>}</>;
}
function Drill({pack,m,mask,onClose}:{pack:ReportPack;m:ReportMetric;mask:boolean;onClose:()=>void}){
  const {s,uid,booking,toast}=useHcc();const p=usePaged(m.rows,DESKTOP_PAGE);
  return <section className="card f-drill" aria-label={'Source rows for '+m.label}><div className="section-title"><h2>{m.label} · source rows</h2>
    <div className="inline">{rbacCanExportReports(s,uid)&&<button className="btn small-btn" onClick={()=>{downloadCsv(`HCC-Demo-rows-${m.id}-${pack.from}_${pack.to}.csv`,reportDrillCsv(s,pack,m));toast('Source rows downloaded');}}><Download size={14}/> Rows CSV</button>}
      <button className="icon-btn" aria-label="Close source rows" onClick={onClose}><X size={17}/></button></div></div>
    <div className="table-wrap"><table><thead><tr><th>Date</th><th>Reference</th><th>Booking / guest</th><th>Detail</th><th>Method</th><th className="number">{m.unit==='money'?'Amount':'Count'}</th></tr></thead><tbody>
      {p.shown.map(r=><tr key={r.ref}><td>{shortDate(r.date)}</td><td><small>{r.ref}</small></td>
        <td>{r.booking?(mask?<span>{r.booking} · {r.guest}</span>:<button className="text-btn" onClick={()=>booking(r.booking!)}>{r.booking} · {r.guest}</button>):r.room?`Room ${r.room}`:'—'}</td>
        <td>{r.detail}</td><td>{r.method??'—'}</td><td className="number">{m.unit==='money'?money(r.amount):r.amount}</td></tr>)}
    </tbody><tfoot><tr><td colSpan={5}><strong>Total ({m.rows.length} rows) — equals the figure above</strong></td><td className="number"><strong>{m.unit==='money'?money(m.value):m.value}</strong></td></tr></tfoot></table>
    {!m.rows.length&&<Empty title="No source records" body="Nothing matches this figure in the selected range."/>}</div><ShowMore p={p}/></section>;
}
function TableView({t,all=false}:{t:ReportTab;all?:boolean}){
  const tb=t.table!,p=usePaged(tb.rows,DESKTOP_PAGE),rows=all?tb.rows:p.shown;
  return <><div className="table-wrap"><table><thead><tr>{tb.columns.map((c,i)=><th key={c} className={tb.units[i]!=='text'?'number':''}>{c}</th>)}</tr></thead><tbody>
    {rows.map(r=><tr key={String(r[0])}>{r.map((v,i)=><td key={i} className={tb.units[i]!=='text'?'number':''}>{i===0?shortDate(String(v)):v===''?'—':fmt(v,tb.units[i])}</td>)}</tr>)}</tbody></table></div>{!all&&<ShowMore p={p}/>}</>;
}
function Dictionary(){
  return <section className="card"><div className="section-title"><h2>Report dictionary</h2><span className="muted">Financial Rulebook §6</span></div>
    <div className="table-wrap"><table><thead><tr><th>Metric</th><th>Definition</th><th>Zero handling</th></tr></thead><tbody>{reportDictionary.map(([a,b,c])=><tr key={a}><td><strong>{a}</strong></td><td>{b}</td><td>{c}</td></tr>)}</tbody></table></div></section>;
}
/** Wide modal 'report-print': id = tab ID, room = 'from|to'. */
export function ReportPrintModal({id,room}:{id?:string;room?:string}){
  const {s,closeModal,permission}=useHcc();
  const [from,to]=(room||s.date+'|'+s.date).split('|');
  const mask=!permission('billing')&&!permission('reservations');
  const srv=process.env.HCC_BUILD==='production'?useServerReport(from,to):NO_FETCH; // eslint-disable-line react-hooks/rules-of-hooks
  const pack0=useMemo(()=>process.env.HCC_BUILD==='production'?(srv.data?.pack??null):scoped(reportPackMemo(s,from,to),mask),[s.bookings,s.expenses,s.rooms,s.posX,s.closings,s.closedDates,s.date,s.openingCash,s.shiftClose,from,to,mask,srv.data]); // eslint-disable-line react-hooks/exhaustive-deps
  const pack=pack0?scoped(pack0,mask):null;
  if(!pack)return <><div className="f-print-doc invoice"><p role="status">{srv.status==='error'?'The report could not be loaded from the server.':'Loading the report from the server…'}</p></div><div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const t=pack.tabs.find(x=>x.id===id);
  return <><div className="f-print-doc invoice">
    <div className="invoice-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>{TABS.find(x=>x[0]===id)?.[1]??'Report'} · {dateLabel(from)} – {dateLabel(to)} · scenario {s.scenario}</p></div></div>
    <div className="invoice-stamp">{process.env.HCC_BUILD==='production'?'FIGURES FROM THE HOTEL SERVER · NOT A TAX DOCUMENT':'DEMONSTRATION — FICTIONAL DATA · NOT A TAX DOCUMENT'}</div>
    {!t?<div className="table-wrap"><table><thead><tr><th>Metric</th><th>Definition</th><th>Zero handling</th></tr></thead><tbody>{reportDictionary.map(([a,b,c])=><tr key={a}><td>{a}</td><td>{b}</td><td>{c}</td></tr>)}</tbody></table></div>:<>
      <div className="summary-list">{t.metrics.map(m=><div key={m.id}><span>{m.label}</span><strong>{m.unit==='money'?money(m.value):m.value}</strong></div>)}{t.ratios.map(r=><div key={r.id}><span>{r.label} ({r.formula})</span><strong>{r.display}</strong></div>)}</div>
      {t.table&&<TableView t={t} all/>}</>}
    <p className="footnote">{process.env.HCC_BUILD==='production'?<>Figures from the server report as of {asOfText(srv.data?.asOfUtc)}. {SERVER_DETAIL_NOTE} GST illustrative — CA to confirm.</>:<>Generated at simulated clock {s.clock.replace('T',' ')}. Every total is the sum of its source records. GST illustrative — CA to confirm.</>}</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}
