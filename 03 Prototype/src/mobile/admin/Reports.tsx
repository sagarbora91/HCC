// M19 phone Reports: tab chips, date-range sheet, KPI cards → drill-down sheet (row total = figure), by-date cards, CSV and print kept.
import React,{useState} from 'react';
import {CalendarRange,Download,FileSpreadsheet,Printer,BookOpen,Info} from 'lucide-react';
import {money,reportCsv,reportRangeLimit,rbacCanExportReports} from '../../services/mock';
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import {HistorySummary} from '../../features/reports/HistorySummary';
import type {ReportTab} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter,KpiCard,ListCard} from '../../app/mobile/components';
import {Empty,shortDate} from '../../components/ui';
import {downloadCsv} from '../../features/reports/Reports';
import {reportXlsx} from '../../features/reports/exportXlsx';
import {XLSX_MIME} from '../../app/xlsx';
import {saveBinaryFile} from '../../app/native';
import {MA_REPORT_CHIPS,MaReportTabId,maRangeCheck,maMaskReports,maLimit} from './logic';
import {useMaPack} from './serverPack';
import {ServerReportStrip,ServerHistorySummary,SERVER_DETAIL_NOTE} from '../../features/reports/serverBits';
import {useMaUi,maRange} from './store';
import {MaRows} from './parts';

export function MaReports(){
  const {s,uid,open,toast}=useHcc();const u=useMaUi();const {from,to}=maRange(u,s.date);
  const [tab,setTab]=useState<MaReportTabId>('daily');
  const chk=maRangeCheck(from,to,s.date,maLimit(s));
  const {pack,srv}=useMaPack(s,uid,from,to);const t=pack?.tabs.find(x=>x.id===tab);
  const label=MA_REPORT_CHIPS.find(c=>c.id===tab)!.label;
  const exportTab=()=>{if(!pack)return;downloadCsv(`HCC-Demo-${tab}-${from}_${to}.csv`,reportCsv(s,pack,tab));toast('Demo CSV downloaded · '+label);};
  const exportExcel=()=>{if(!pack)return;void saveBinaryFile(`HCC-Demo-reports-${from}_${to}.xlsx`,reportXlsx({hotel:s.settings.hotel,scenario:s.scenario,clock:s.clock},pack),XLSX_MIME);toast('Demo Excel workbook saved · all report tabs');};
  return <div className="ma-page">
    {process.env.HCC_BUILD==='production'?<ServerHistorySummary compact/>:<HistorySummary compact/>}
    <button type="button" className="ma-range" onClick={()=>open({type:'ma-range'})} aria-label="Change report date range">
      <CalendarRange size={20}/><span><small>Business dates</small><strong>{from===to?shortDate(from):`${shortDate(from)} – ${shortDate(to)}`}</strong></span><em>Change</em></button>
    <ChipFilter<MaReportTabId> label="Report" value={tab} onChange={setTab} options={MA_REPORT_CHIPS.map(c=>({id:c.id,label:c.label}))}/>
    {!chk.ok&&<p className="ma-why" role="alert">{chk.error}</p>}
    {process.env.HCC_BUILD==='production'&&chk.ok&&<ServerReportStrip f={srv}/>}
    {maMaskReports(s,uid)&&<p className="ma-intro">Restricted scope: guest names are masked.</p>}
    {!pack||!t?<Empty title={process.env.HCC_BUILD==='production'&&chk.ok&&srv.status!=='error'?'Loading the report…':'No report for this range'} body={process.env.HCC_BUILD==='production'&&chk.ok?(srv.status==='error'?'The server did not answer. Try Refresh.':'Waiting for the server.'):'Adjust the date range.'}/>:<>
      <div className="ma-kpis">{t.metrics.map(m=><KpiCard key={m.id} label={m.label} value={m.unit==='money'?money(m.value):m.value} sub={process.env.HCC_BUILD==='production'?'Server figure · details on the server PC':`${m.rows.length} source row${m.rows.length===1?'':'s'} · view ›`} onClick={()=>open({type:'ma-drill',id:m.id,room:tab})}/>)}
        {t.ratios.map(r=><KpiCard key={r.id} label={r.label} value={r.display} sub={r.formula}/>)}</div>
      {t.table&&<ByDate t={t}/>}
      <div className="ma-note"><Info size={18}/><span>Charges follow service date, receipts follow receipt date, expenses follow business date. GST from the dated GST master (illustrative — CA to confirm).</span></div>
    </>}
    <ListCard title={<span className="m-lc-title"><BookOpen size={20}/>Report dictionary</span>} sub="Definitions and zero handling" onClick={()=>open({type:'ma-dictionary'})}/>
    <div className="ma-footer-actions"><button type="button" className="btn" disabled={!pack} onClick={()=>open({type:'report-print',id:tab,room:from+'|'+to})}><Printer size={18}/>Print</button>
      {rbacCanExportReports(s,uid)&&<><button type="button" className="btn" disabled={!pack} onClick={exportExcel}><FileSpreadsheet size={18}/>Export Excel</button>
      <button type="button" className="btn primary" disabled={!pack} onClick={exportTab}><Download size={18}/>Export CSV</button></>}</div>
  </div>;
}
function ByDate({t}:{t:ReportTab}){
  const tb=t.table!,p=usePaged(tb.rows,PHONE_PAGE);
  const fmt=(v:string|number,i:number)=>v===''?'—':typeof v==='number'?(tb.units[i]==='money'?money(v):String(v)):v;
  return <section aria-label="By business date"><h2 className="ma-h2">By business date</h2>
    {p.shown.map(r=><div className="ma-card" key={String(r[0])}><strong>{shortDate(String(r[0]))}</strong>
      <MaRows rows={tb.columns.slice(1).map((c,i)=>[c,<span className={tb.units[i+1]!=='text'?'number':''}>{fmt(r[i+1],i+1)}</span>])}/></div>)}<ShowMore p={p}/></section>;
}
