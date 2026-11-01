// Module 20 · Accounting automation — desktop page (CLOSING & REPORTS › Accounting).
import React,{useMemo,useState} from 'react';
import {Info,AlertTriangle,CheckCircle2,Download} from 'lucide-react';
import {saveTextFile,saveAndReport} from '../../app/native';
import {useHcc} from '../../app/context';
import {money,accDates,accVouchers,accExceptions,accReconcile,accMapRows,accLockOf,accDayStatus,accTotals,accLockBlocker,accLedgerName,accTallyXml,TALLY_MAX_DAYS,addDays,
  rbacCanTally,rbacSeesBooks} from '../../services/mock';
import {Empty,shortDate,clockTime} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip,FitNote} from '../../components/fit';
import {AccStatusPill,AccSeverity,AccLockButton,AccExportButton,accUserName,useAccPerms,accVoucherDebit} from './parts';
import {accOpeningPending} from '../../services/mock';
import {TrialBalanceView,StatementView,DayBookView} from './books';
import {ChartView,PartiesView} from './chart';
import {OpeningView} from './opening';
import {useAccRange} from './shared';
import {ManualView} from './manual';
import {GstMonthView} from './gst';
import {TdsView,AssetsView} from './tds';
import {accManualList} from './acc2-views';
import {PlView,BalanceSheetView,OutstandingView,GstSummaryView,CashRatioView,ExportsView,TallyMastersPanel} from './reports3';
import {accTallyVouchers,accTallyCompany} from './acc3-views';

export type AccTab='vouchers'|'manual'|'gst'|'tds'|'assets'|'tb'|'statements'|'daybook'|'chart'|'parties'|'opening'|'exceptions'|'recon'|'mapping'|'tally'|'pl'|'bs'|'ageing'|'gstsum'|'cashratio'|'exports';
type Tab=AccTab;
/** FIT (DEC-196): the page's sections are sub-screens with their existing ids, in tab-bar order (src/app/subscreens.ts lists the same). */
export const ACC_TAB_IDS=['vouchers','manual','gst','tds','assets','tb','statements','pl','bs','ageing','gstsum','cashratio','exports','daybook','chart','parties','opening','exceptions','recon','mapping','tally'] as const;
const ACC_FOOTNOTE='Vouchers are re-derived from source records until a closed day is locked; voucher numbers are provisional until then. Synthetic chart of accounts; GST split CGST + SGST from the GST master (illustrative — CA to confirm) — not a statutory book of account.';
export function AccDatePicker({date,setDate,dates}:{date:string;setDate:(d:string)=>void;dates:string[]}){
  return <label className="acc-date">Business date<select value={date} onChange={e=>setDate(e.target.value)}>
    {dates.map(d=><option key={d} value={d}>{shortDate(d)} {d.slice(0,4)}</option>)}</select></label>;
}
export function AccountingPage(){
  const {s,uid,open,go,permission}=useHcc();const {canMap}=useAccPerms();
  const tally=rbacCanTally(s,uid); // DEC-063 G13: Tally export tab = Owner + Accountant (Manager/Cashier keep Accounting without it)
  const dates=useMemo(()=>accDates(s),[s]);
  const [sel,setDate]=useState(s.date);const date=dates.includes(sel)?sel:dates[0];
  const [picked,setTab]=useFitTab<Tab>('accounting',ACC_TAB_IDS,'vouchers');
  const tab:Tab=picked==='tally'&&!tally?'vouchers':picked; // a role without the Tally export never lands on it (remembered or asked)
  const [range,setRange]=useAccRange(s.date);const [stmtAcc,setStmtAcc]=useState('');const [asOf,setAsOf]=useState(s.date);
  const goStatement=(a:string)=>{setStmtAcc(a);setTab('statements');};
  const vs=useMemo(()=>accVouchers(s,date),[s,date]);
  const ex=useMemo(()=>accExceptions(s,date),[s,date]);
  const recon=useMemo(()=>accReconcile(s,date),[s,date]);
  const t=accTotals(vs),status=accDayStatus(s,date),lock=accLockOf(s,date),why=accLockBlocker(s,date);
  const errors=ex.filter(x=>x.severity==='error').length,reconOk=recon.every(c=>c.ok);
  const manualPending=accManualList(s).filter(v=>v.status==='pending').length;
  const tabs:[Tab,string,(number|string)?][]=[['vouchers','Vouchers',vs.length],['manual','Manual vouchers',manualPending||undefined],['gst','GST month'],['tds','TDS'],['assets','Assets'],['tb','Trial balance'],['statements','Statements'],['pl','P&L'],['bs','Balance sheet'],['ageing','Outstanding'],['gstsum','GST summary'],['cashratio','Cash ratio'],['exports','Exports'],['daybook','Day book'],['chart','Chart of accounts'],['parties','Parties'],['opening','Opening balances',accOpeningPending(s)?'pending':undefined],
    ['exceptions','Exceptions',ex.filter(x=>x.severity!=='info').length],['recon','Reconciliation'],['mapping','Mapping'],...(tally?[['tally','Tally export'] as [Tab,string]]:[])];
  if(!rbacSeesBooks(s,uid)||!permission('reports'))return <FitPage title="Accounting" sub="The books">
    <ScrollPanel label="Accounting"><Empty title="Not available for your role" body="The books are for the Owner, Manager and Accountant. The Cashier’s collections are in Reports."/></ScrollPanel></FitPage>;
  // FIT (contract §6 'accounting'): header = Lock (the committing action, first) + Export; toolbar = business date + day status + StatStrip;
  // the sections are FitTabs with their existing ids; each section's existing content scrolls in one ScrollPanel, the footnotes sit in its footer.
  const label=tabs.find(x=>x[0]===tab)?.[1]??'Accounting';
  return <FitPage title="Accounting" sub="Balanced vouchers derived automatically from folio lines, receipts, expenses and closing records · fictional data · GST (CGST + SGST)"
      actions={<><AccLockButton date={date}/><AccExportButton date={date}/></>}
      toolbar={<div className="acc-fit-toolbar">
        <div className="acc-statusbar"><AccDatePicker date={date} setDate={setDate} dates={dates}/><AccStatusPill status={status}/>
          {lock?<span className="muted">Locked by {accUserName(s,lock.lockedBy)} · {shortDate(lock.lockedAt.slice(0,10))} {clockTime(lock.lockedAt)} (simulated) · {lock.count} vouchers frozen</span>
            :why?<span className="muted" id="acc-lock-why">{why}</span>:<span className="muted">Ready to lock.</span>}</div>
        <StatStrip items={[
          {title:'Vouchers',value:String(vs.length),sub:`${vs.filter(v=>v.type==='Sales').length} sales · ${vs.filter(v=>v.type==='Receipt').length} receipt · ${vs.filter(v=>v.type==='Payment').length} payment · ${vs.filter(v=>v.type==='Journal').length} journal`},
          {title:'Total debits',value:money(t.debit),sub:t.debit===t.credit?'Equals total credits — balanced':'Does not equal credits'},
          {title:'Total credits',value:money(t.credit),sub:reconOk?`Reconciles with Reports (${recon.length} checks)`:`${recon.filter(c=>!c.ok).length} reconciliation check(s) differ`},
          {title:'Exceptions',value:String(errors),sub:errors?'Blocking — resolve before locking':'Nothing blocks locking'}]}/></div>}
      tabs={<FitTabs<Tab> page="accounting" value={tab} onChange={setTab} tabs={tabs.map(([id,l,n])=>({id,label:l,count:n as number|undefined /* 'pending' on Opening balances shows as text, as before */,badge:id==='opening'&&n?'warn':undefined}))}/>}>
    <ScrollPanel label={label+' — accounting'} footer={<FitNote text={<><Info size={14} aria-hidden/> Export is a generic import format · Tally/other mapping later. {ACC_FOOTNOTE}</>}/>}>
      {tab==='vouchers'&&(vs.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Voucher no</th><th>Type</th><th>Narration</th><th>Source ID</th><th className="acc-num">Amount</th><th>Check</th></tr></thead>
        <tbody>{vs.map(v=><tr key={v.no}><td><button className="text-btn acc-link number" onClick={()=>open({type:'acc-voucher',id:date+'|'+v.no})}>{v.no}</button></td><td>{v.type}</td>
          <td className="acc-wrap">{v.narration}</td><td className="number"><small>{v.source.id}</small></td><td className="acc-num number"><strong>{money(accVoucherDebit(v))}</strong></td>
          <td>{v.unmapped.length?<span className="pill acc-sev-error">Suspense</span>:<span className="pill green">Balanced</span>}</td></tr>)}</tbody></table></div>
        :<Empty title="No vouchers for this date" body="Nothing was posted, received or spent on this business date."/>)}
      {tab==='manual'&&<ManualView/>}
      {tab==='gst'&&<GstMonthView/>}
      {tab==='tds'&&<TdsView/>}
      {tab==='assets'&&<AssetsView/>}
      {tab==='tb'&&<TrialBalanceView range={range} setRange={setRange} onStatement={goStatement}/>}
      {tab==='statements'&&<StatementView range={range} setRange={setRange} account={stmtAcc} setAccount={setStmtAcc}/>}
      {tab==='pl'&&<PlView range={range} setRange={setRange}/>}
      {tab==='bs'&&<BalanceSheetView asOf={asOf} setAsOf={setAsOf}/>}
      {tab==='ageing'&&<OutstandingView/>}
      {tab==='gstsum'&&<GstSummaryView/>}
      {tab==='cashratio'&&<CashRatioView/>}
      {tab==='exports'&&<ExportsView/>}
      {tab==='daybook'&&<DayBookView range={range} setRange={setRange}/>}
      {tab==='chart'&&<ChartView/>}
      {tab==='parties'&&<PartiesView onStatement={goStatement}/>}
      {tab==='opening'&&<OpeningView/>}
      {tab==='exceptions'&&(ex.length?<div className="acc-exlist">{ex.map(x=><div key={x.id} className={'acc-ex acc-ex-'+x.severity}><AccSeverity x={x}/><div><strong>{x.title}</strong><p>{x.detail}</p>
          <div className="inline">{x.voucher&&<button className="text-btn" onClick={()=>open({type:'acc-voucher',id:date+'|'+x.voucher})}>View {x.voucher}</button>}{x.key&&canMap&&<button className="text-btn" onClick={()=>open({type:'acc-map',id:x.key})}>Map now</button>}{x.kind==='stuck'&&permission('admin')&&<button className="text-btn" onClick={()=>go('syshealth')}>Open System health</button>}</div></div></div>)}</div>
        :<Empty title="No exceptions" body="Every voucher is balanced and mapped."/>)}
      {tab==='recon'&&<div className="table-wrap"><table className="acc-table"><thead><tr><th>Check</th><th className="acc-num">Reports / closing</th><th className="acc-num">Ledger</th><th>Result</th><th>Compared with</th></tr></thead>
        <tbody>{recon.map(c=><tr key={c.id}><td><strong>{c.label}</strong></td><td className="acc-num number">{money(c.report)}</td><td className="acc-num number">{money(c.ledger)}</td>
          <td>{c.ok?<span className="pill green"><CheckCircle2 size={12}/>Agrees</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Differs {money(c.ledger-c.report)}</span>}</td><td><small>{c.note}</small></td></tr>)}</tbody></table></div>}
      {tab==='mapping'&&<MappingTable/>}
      {tab==='tally'&&tally&&<TallyExport/>}
    </ScrollPanel>
  </FitPage>;
}
function MappingTable(){
  const {s,open}=useHcc();const {canMap}=useAccPerms();const rows=accMapRows(s);const hist=[...(s.accountingX?.mapHistory??[])].reverse().slice(0,8);
  return <>
    <div className="info-note acc-note"><Info size={16}/>Each source classification posts to one ledger. Changes apply to unlocked dates only; locked periods keep their snapshot. {canMap?'':'Only an administrator (Owner) can change mappings.'}</div>
    <div className="table-wrap"><table className="acc-table"><thead><tr><th>Source</th><th>Classification</th><th>Ledger</th><th>Default</th><th className="acc-num">Records</th><th>Status</th><th/></tr></thead>
      <tbody>{rows.map(r=><tr key={r.key}><td>{r.group}</td><td><strong>{r.value}</strong></td><td>{r.mapped?`${r.account} · ${accLedgerName(r.account)}`:'—'}</td><td><small>{r.defaultAccount?`${r.defaultAccount} · ${accLedgerName(r.defaultAccount)}`:'none'}</small></td>
        <td className="acc-num number">{r.uses}</td><td>{!r.mapped?<span className="pill acc-sev-error">Unmapped</span>:r.changed?<span className="pill blue">Changed</span>:<span className="pill green">Default</span>}</td>
        <td>{canMap&&<button className="btn small-btn" onClick={()=>open({type:'acc-map',id:r.key})}>Change</button>}</td></tr>)}</tbody></table></div>
    {hist.length>0&&<div className="padded"><h3 className="acc-h3">Recent mapping changes</h3><ul className="acc-hist">{hist.map(h=><li key={h.id}><strong>{h.key}</strong>: {h.before||'none'} → {h.after||'none'} · {accUserName(s,h.actor)} · {clockTime(h.at)}</li>)}</ul></div>}
  </>;
}

/** Accounting › Tally export: Tally-style XML preview from the existing vouchers. Always Simulated — saving the XML is not an import into Tally. */
export function TallyExport(){
  const {s,toast,run,busy}=useHcc();
  const [to,setTo]=useState(s.date);const [from,setFrom]=useState(addDays(s.date,-6));
  const p=useMemo(()=>accTallyXml(s,from,to),[s,from,to]);
  // ACC-3 integration: the file is acc3-tally's vouchers export (bill allocations, explicit vendor allocations first); accTallyXml keeps the
  // DEC-077 re-export / pending / legacy notes and the ACC_TALLY_EXPORT log of the same dates.
  const tv=useMemo(()=>accTallyVouchers(s,from,to,accTallyCompany(s)),[s,from,to]);
  const lines=(tv.error?p.xml:tv.xml).split(String.fromCharCode(10)),shown=lines.slice(0,60).join(String.fromCharCode(10));
  return <div className="padded exp-tally">
    <TallyMastersPanel/>
    <div className="exp-tally-head"><span className="pill orange" role="status">Simulated</span><span className="muted">No file is sent to Tally. Saving this XML does not mean it was imported — only Tally's own import confirms that.</span></div>
    <div className="inline exp-tally-range"><label className="acc-date">From<input type="date" value={from} max={s.date} onChange={e=>setFrom(e.target.value)}/></label>
      <label className="acc-date">To<input type="date" value={to} max={s.date} onChange={e=>setTo(e.target.value)}/></label>
      {/* DEC-077: the download is recorded (ACC_TALLY_EXPORT) so a later reopen of an exported day flags it for re-export. Production: the
          server builds, records and returns the file (GET /accounting/export/tally) and the mapper saves it. */}
      <button className="btn" disabled={busy||!p.count||!!tv.error} onClick={()=>{void run({type:'ACC_TALLY_EXPORT',from,to},'Simulated Tally XML saved · not imported into Tally',
        {after:()=>{if(process.env.HCC_BUILD!=='production')void saveAndReport(saveTextFile(tv.fileName,tv.xml,'application/xml'),toast,'Simulated Tally XML saved · not imported into Tally','the Tally XML');}});}}><Download size={16}/> Download XML</button></div>
    {/* A03b-05: every voucher carries a fixed REMOTEID, so a re-import replaces instead of duplicating; provisional while events are pending. */}
    <p className="muted">Every voucher carries a fixed voucher ID (REMOTEID): importing a day again replaces its vouchers in Tally instead of adding them twice — set “Overwrite vouchers when a voucher with same GUID exists” to Yes in Tally’s import.{process.env.HCC_BUILD==='production'?' The downloaded file is built by the hotel server with the hotel’s own voucher IDs.':''}</p>
    {p.reexport.length>0&&<div className="info-note f-warn" role="status"><AlertTriangle size={16}/><span>Re-export: {p.reexport.join(', ')} changed after {p.reexport.length===1?'it was':'they were'} exported and {p.reexport.length===1?'is':'are'} carried again in this file. The vouchers keep their IDs, so importing it replaces them in Tally and adds the new ones — nothing needs deleting first.{p.reopened.length?` ${p.reopened.join(', ')} is still reopened and may change again.`:''}</span></div>}
    {p.pending>0&&p.lagging.length>0&&<div className="info-note f-warn" role="status"><AlertTriangle size={16}/><span>Provisional: {p.pending} accounting event{p.pending===1?' is':'s are'} not yet in the vouchers, so {p.lagging.join(', ')} may be incomplete. {p.lagging.length===1?'That day is':'Those days are'} carried again by the first export after the events are processed; locked days are not affected.</span></div>}
    {p.legacy.length>0&&<div className="info-note f-warn" role="status"><AlertTriangle size={16}/><span>Exported before voucher IDs: {p.legacy.join(', ')}. If that earlier file was imported, delete {p.legacy.length===1?'that day’s':'those days’'} HCC vouchers from it in Tally before importing, or Tally will hold them twice.</span></div>}
    {p.error?<p className="f-why" role="alert">{p.error}</p>:<>
      <p className="muted">{p.count} voucher{p.count===1?'':'s'} over {p.dates} business date{p.dates===1?'':'s'} · debits {money(p.debit)} · credits {money(p.credit)} · {p.debit===p.credit?'balanced':'NOT balanced'} · up to {TALLY_MAX_DAYS} days</p>
      {p.count?<><pre className="exp-xml" aria-label="Tally XML preview">{shown}</pre>{lines.length>60&&<p className="muted">Preview shows the first 60 of {lines.length} lines; the download has everything.</p>}</>
        :<Empty title="No vouchers in this range" body="Nothing was posted, received or spent on these business dates."/>}</>}
  </div>;
}
