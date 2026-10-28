// Module 20 · Accounting automation — phone page: date chips, day summary cards, exceptions, reconciliation, voucher cards.
import React,{useMemo,useState} from 'react';
import {CheckCircle2,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,accDates,accVouchers,accExceptions,accReconcile,accLockOf,accDayStatus,accTotals,accLockBlocker,accMapRows,rbacCanTally,rbacCanExportVouchers,rbacSeesBooks} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {Empty,shortDate,clockTime} from '../../components/ui';
import {accOpeningPending} from '../../services/mock';
import {TrialBalanceView,StatementView,DayBookView} from './books';
import {ChartView,PartiesView} from './chart';
import {OpeningView} from './opening';
import {useAccRange} from './shared';
import {ManualView} from './manual';
import {GstMonthView} from './gst';
import {TdsView,AssetsView} from './tds';
import {accManualList} from './_contract-stubs-2';
import {TallyExport} from './Accounting';
import {AccStatusPill,AccSeverity,AccLockButton,AccExportButton,accUserName,useAccPerms,accVoucherDebit} from './parts';

type View='ledger'|'manual'|'gst'|'tds'|'assets'|'tb'|'statements'|'daybook'|'chart'|'parties'|'opening'|'tally';
type Filter='all'|'Sales'|'Receipt'|'Payment'|'Journal';
export function AccountingMobile(){
  const {s,uid,open,go,permission}=useHcc();const {canLock,canMap}=useAccPerms();
  const dates=useMemo(()=>accDates(s).slice(0,7),[s]);
  const [sel,setDate]=useState(s.date);const date=dates.includes(sel)?sel:dates[0];
  const [f,setF]=useState<Filter>('all');const [showAll,setShowAll]=useState(false);
  const vs=useMemo(()=>accVouchers(s,date),[s,date]);
  const ex=useMemo(()=>accExceptions(s,date).filter(x=>x.severity!=='info'),[s,date]);
  const recon=useMemo(()=>accReconcile(s,date),[s,date]);
  const t=accTotals(vs),status=accDayStatus(s,date),lock=accLockOf(s,date),why=accLockBlocker(s,date);
  const shown=vs.filter(v=>f==='all'||v.type===f),list=showAll?shown:shown.slice(0,20);
  const bad=recon.filter(c=>!c.ok),unmapped=accMapRows(s).filter(r=>!r.mapped&&r.uses>0);
  const lockable=canLock&&!lock&&!why;
  const tally=rbacCanTally(s,uid); // DEC-063 G13: Owner + Accountant only
  const [view,setView]=useState<View>('ledger'); // W05-3: Tally export (Simulated) reachable on the phone; ACC-1: books, chart, parties, opening balances
  const [range,setRange]=useAccRange(s.date);const [stmtAcc,setStmtAcc]=useState('');
  const goStatement=(a:string)=>{setStmtAcc(a);setView('statements');};
  const head=<><div className="page-title"><div><h1>Accounting</h1><p>Vouchers derived from folios, receipts, expenses and closing · fictional data</p></div></div>
    <ChipFilter<View> label="Accounting view" value={view} onChange={setView} options={[{id:'ledger',label:'Day'},{id:'manual',label:accManualList(s).some(v=>v.status==='pending')?'Manual (pending)':'Manual vouchers'},{id:'gst',label:'GST month'},{id:'tds',label:'TDS'},{id:'assets',label:'Assets'},{id:'tb',label:'Trial balance'},{id:'statements',label:'Statements'},{id:'daybook',label:'Day book'},{id:'chart',label:'Chart'},{id:'parties',label:'Parties'},{id:'opening',label:accOpeningPending(s)?'Opening (pending)':'Opening'},...(tally?[{id:'tally' as View,label:'Tally export (Simulated)'}]:[])]}/></>;
  if(!rbacSeesBooks(s,uid)||!permission('reports'))return <div className="acc-m"><Empty title="Not available for your role" body="The books are for the Owner, Manager and Accountant."/></div>;
  if(view==='manual')return <div className="acc-m">{head}<ManualView phone/></div>;
  if(view==='gst')return <div className="acc-m">{head}<GstMonthView phone/></div>;
  if(view==='tds')return <div className="acc-m">{head}<TdsView phone/></div>;
  if(view==='assets')return <div className="acc-m">{head}<AssetsView phone/></div>;
  if(view==='tb')return <div className="acc-m">{head}<TrialBalanceView range={range} setRange={setRange} phone onStatement={goStatement}/></div>;
  if(view==='statements')return <div className="acc-m">{head}<StatementView range={range} setRange={setRange} account={stmtAcc} setAccount={setStmtAcc} phone/></div>;
  if(view==='daybook')return <div className="acc-m">{head}<DayBookView range={range} setRange={setRange} phone/></div>;
  if(view==='chart')return <div className="acc-m">{head}<ChartView phone/></div>;
  if(view==='parties')return <div className="acc-m">{head}<PartiesView phone onStatement={goStatement}/></div>;
  if(view==='opening')return <div className="acc-m">{head}<OpeningView phone/></div>;
  if(view==='tally'&&tally)return <div className="acc-m">{head}<div className="acc-m-tally"><TallyExport/></div></div>;
  return <div className="acc-m">
    {head}
    <ChipFilter label="Business date" value={date} onChange={setDate} options={dates.map(d=>({id:d,label:shortDate(d)+(accLockOf(s,d)?' · locked':d===s.date?' · today':'')}))}/>
    <div className="acc-m-status"><AccStatusPill status={status}/>
      <small>{lock?`Locked by ${accUserName(s,lock.lockedBy)} · ${clockTime(lock.lockedAt)}`:why||'Ready to lock'}</small></div>
    <div className="acc-m-kpis">
      <KpiCard label="Vouchers" value={vs.length} sub={`${vs.filter(v=>v.type==='Receipt').length} receipts · ${vs.filter(v=>v.type==='Sales').length} sales`}/>
      <KpiCard label="Exceptions" value={ex.filter(x=>x.severity==='error').length} sub={ex.length?'Tap below to resolve':'None'}/>
      <KpiCard label="Debits" value={money(t.debit)} sub={t.debit===t.credit?'= credits · balanced':'≠ credits'}/>
      <KpiCard label="Credits" value={money(t.credit)} sub={bad.length?`${bad.length} check(s) differ`:'Reconciles with Reports'}/>
    </div>
    <h2 className="m-group">EXCEPTIONS</h2>
    {ex.length?ex.map(x=><ListCard key={x.id} tone={'acc-m-ex-'+x.severity} title={x.title} sub={x.detail} meta={<AccSeverity x={x}/>}
      onClick={x.kind==='stuck'&&permission('admin')?()=>go('syshealth'):x.key&&canMap?()=>open({type:'acc-map',id:x.key}):x.voucher?()=>open({type:'acc-voucher',id:date+'|'+x.voucher}):undefined}/>)
      :<ListCard title="No exceptions" sub="Every voucher is balanced and mapped." right={<CheckCircle2 size={20} className="acc-ok-icon" aria-hidden/>}/>}
    {unmapped.length>0&&!canMap&&<p className="muted acc-m-note">Ask the Owner to map: {unmapped.map(r=>r.value).join(', ')}.</p>}
    <h2 className="m-group">RECONCILIATION</h2>
    <ListCard title={bad.length?`${bad.length} of ${recon.length} checks differ`:`All ${recon.length} checks agree`}
      sub={bad.length?bad.map(c=>`${c.label}: ledger ${money(c.ledger)} vs report ${money(c.report)}`).join(' · '):`Net receipts ${money(recon.find(c=>c.id==='collections')?.ledger??0)} · revenue, tax, deposits, expenses, cash`}
      right={bad.length?<AlertTriangle size={20} className="acc-warn-icon" aria-hidden/>:<CheckCircle2 size={20} className="acc-ok-icon" aria-hidden/>}/>
    <h2 className="m-group">VOUCHERS</h2>
    <ChipFilter label="Voucher type" value={f} onChange={setF} options={[{id:'all',label:'All',count:vs.length},...(['Sales','Receipt','Payment','Journal'] as const).map(k=>({id:k,label:k,count:vs.filter(v=>v.type===k).length}))]}/>
    {list.length?list.map(v=><ListCard key={v.no} title={v.no} sub={v.narration} meta={v.source.id}
      right={<><strong className="number">{money(accVoucherDebit(v))}</strong>{v.unmapped.length>0&&<><br/><span className="pill acc-sev-error">Suspense</span></>}</>}
      onClick={()=>open({type:'acc-voucher',id:date+'|'+v.no})}/>):<Empty title="No vouchers" body="Nothing was posted, received or spent on this date."/>}
    {shown.length>list.length&&<button className="btn full" onClick={()=>setShowAll(true)}>Show all {shown.length} vouchers</button>}
    <p className="footnote">Export: generic import format (date, voucher no, ledger, debit, credit, narration, source ID) · Tally/other mapping later.</p>
    {(lockable||lock||rbacCanExportVouchers(s,uid))&&<StickyFooter>{lockable?<><AccExportButton date={date}/><AccLockButton date={date}/></>:<><AccExportButton date={date} primary/>{lock&&<AccLockButton date={date} primary={false}/>}</>}</StickyFooter>}
  </div>;
}
