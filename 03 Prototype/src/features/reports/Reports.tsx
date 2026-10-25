// SCR-15 Reports: tabs, business-date range, drill-down from every total to its source rows, CSV per tab, print with watermark.
import {printDocument,saveCsv,saveBinaryFile} from '../../app/native';
import {XLSX_MIME} from '../../app/xlsx';
import {reportXlsx} from './exportXlsx';
import React,{useMemo,useState} from 'react';
import {Download,Printer,Info,X,Hotel,FileSpreadsheet} from 'lucide-react';
import {money,reportMonthRange,reportPack,reportCsv,reportDrillCsv,reportDictionary,ReportPack,ReportMetric,ReportTab,reportRangeLimit,reportRangePresets,historyMonths} from '../../services/mock';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {HistorySummary} from './HistorySummary';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,dateLabel,shortDate} from '../../components/ui';

/** CSV export: a download in the browser, Documents/HCC + share sheet in the Android app. */
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
  const {s,permission,toast,open}=useHcc();
  const [tab,setTab]=useState('daily');const [from,setFrom]=useState(s.date);const [to,setTo]=useState(s.date);const [drill,setDrill]=useState<string|null>(null);
  const valid=/^\d{4}-\d{2}-\d{2}$/.test(from)&&/^\d{4}-\d{2}-\d{2}$/.test(to)&&from<=to&&to<=s.date;
  const span=valid?Math.round((Date.parse(to)-Date.parse(from))/86400000)+1:0;
  const limit=reportRangeLimit(s),tooLong=span>limit;
  const mask=!permission('billing')&&!permission('reservations');
  const pack=useMemo(()=>valid&&!tooLong?scoped(reportPack(s,from,to),mask):null,[s,from,to,valid,tooLong,mask]);
  const t=pack?.tabs.find(x=>x.id===tab);
  const presets=reportRangePresets(s),months=s.historyX?historyMonths(s).months:[];
  const m=t?.metrics.find(x=>x.id===drill);
  const setRange=(a:string,b:string)=>{setFrom(a);setTo(b);setDrill(null);};
  function exportTab(){if(!pack)return;downloadCsv(`HCC-Demo-${tab}-${from}_${to}.csv`,reportCsv(s,pack,tab));toast('Demo CSV downloaded · '+(TABS.find(x=>x[0]===tab)?.[1]));}
  function exportExcel(){if(!pack)return;void saveBinaryFile(`HCC-Demo-reports-${from}_${to}.xlsx`,reportXlsx({hotel:s.settings.hotel,scenario:s.scenario,clock:s.clock},pack),XLSX_MIME);toast('Demo Excel workbook saved · all report tabs');}
  return <><PageTitle title="Reports" sub={'Every figure derived from source records · fictional data · '+dateLabel(s.date)} action={<div className="inline">
      <button className="btn" disabled={!pack} onClick={exportTab}><Download size={16}/> Export CSV</button>
      <button className="btn" disabled={!pack} onClick={exportExcel}><FileSpreadsheet size={16}/> Export Excel</button>
      <button className="btn" disabled={!pack} onClick={()=>open({type:'report-print',id:tab,room:from+'|'+to})}><Printer size={16}/> Print</button></div>}/>
    <HistorySummary/>
    <section className="card f-filters" aria-label="Report filters">
      <label>From (business date)<input type="date" value={from} max={s.date} onChange={e=>{setFrom(e.target.value);setDrill(null);}}/></label>
      <label>To<input type="date" value={to} max={s.date} onChange={e=>{setTo(e.target.value);setDrill(null);}}/></label>
      <div className="inline f-quick">{presets.map(p=><button key={p.id} className="btn small-btn" onClick={()=>setRange(p.from,p.to)}>{p.label}</button>)}
        {months.length>0&&<label className="f-month">Month<select value="" onChange={e=>{const m=e.target.value;if(m){const r=reportMonthRange(s,m);setRange(r.from,r.to);}}}><option value="">Choose…</option>{months.map(x=><option key={x.month} value={x.month}>{x.label}</option>)}</select></label>}</div>
      {!valid&&<p className="f-why" role="alert">Choose a valid range: “from” on or before “to”, and not after today’s business date ({shortDate(s.date)}).</p>}
      {tooLong&&<p className="f-why" role="alert">Choose at most {limit} days in this demo.</p>}
      {mask&&<p className="muted">Restricted scope: guest names are masked.</p>}
    </section>
    <div className="tabs f-report-tabs" role="tablist" aria-label="Report">{TABS.map(([id,label])=><button key={id} role="tab" aria-selected={tab===id} className={tab===id?'active':''} onClick={()=>{setTab(id);setDrill(null);}}>{label}</button>)}</div>
    {tab==='dictionary'?<Dictionary/>:!pack||!t?<Empty title="No report for this range" body="Adjust the date range."/>:<>
      <div className="f-metrics">{t.metrics.map(x=><button key={x.id} className={'f-metric '+(drill===x.id?'on':'')} aria-expanded={drill===x.id} onClick={()=>setDrill(drill===x.id?null:x.id)}>
        <span>{x.label}</span><strong>{x.unit==='money'?money(x.value):x.value}</strong><small>{x.rows.length} source row{x.rows.length===1?'':'s'} · view ›</small>{x.note&&<small>{x.note}</small>}</button>)}
        {t.ratios.map(r=><div key={r.id} className="f-metric ratio"><span>{r.label}</span><strong>{r.display}</strong><small>{r.formula}</small></div>)}</div>
      {m&&<Drill key={m.id+pack.from+pack.to} pack={pack} m={m} mask={mask} onClose={()=>setDrill(null)}/>}
      {t.table&&<section className="card"><div className="section-title"><h2>By business date</h2><span className="muted">{shortDate(from)} – {shortDate(to)}</span></div><TableView t={t}/></section>}
      <div className="info-note"><Info size={17}/>Charges follow service date, receipts follow receipt date, expenses follow business date. Daily cash need not equal daily revenue. Synthetic 10% test tax.</div>
    </>}</>;
}
function Drill({pack,m,mask,onClose}:{pack:ReportPack;m:ReportMetric;mask:boolean;onClose:()=>void}){
  const {s,booking,toast}=useHcc();const p=usePaged(m.rows,DESKTOP_PAGE);
  return <section className="card f-drill" aria-label={'Source rows for '+m.label}><div className="section-title"><h2>{m.label} · source rows</h2>
    <div className="inline"><button className="btn small-btn" onClick={()=>{downloadCsv(`HCC-Demo-rows-${m.id}-${pack.from}_${pack.to}.csv`,reportDrillCsv(s,pack,m));toast('Source rows downloaded');}}><Download size={14}/> Rows CSV</button>
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
  const pack=scoped(reportPack(s,from,to),!permission('billing')&&!permission('reservations'));
  const t=pack.tabs.find(x=>x.id===id);
  return <><div className="f-print-doc invoice">
    <div className="invoice-top"><Hotel size={30}/><div><h2>{s.settings.hotel}</h2><p>{TABS.find(x=>x[0]===id)?.[1]??'Report'} · {dateLabel(from)} – {dateLabel(to)} · scenario {s.scenario}</p></div></div>
    <div className="invoice-stamp">DEMONSTRATION — FICTIONAL DATA · NOT A TAX DOCUMENT</div>
    {!t?<div className="table-wrap"><table><thead><tr><th>Metric</th><th>Definition</th><th>Zero handling</th></tr></thead><tbody>{reportDictionary.map(([a,b,c])=><tr key={a}><td>{a}</td><td>{b}</td><td>{c}</td></tr>)}</tbody></table></div>:<>
      <div className="summary-list">{t.metrics.map(m=><div key={m.id}><span>{m.label}</span><strong>{m.unit==='money'?money(m.value):m.value}</strong></div>)}{t.ratios.map(r=><div key={r.id}><span>{r.label} ({r.formula})</span><strong>{r.display}</strong></div>)}</div>
      {t.table&&<TableView t={t} all/>}</>}
    <p className="footnote">Generated at simulated clock {s.clock.replace('T',' ')}. Every total is the sum of its source records. Synthetic tax; no GST compliance claim.</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}
