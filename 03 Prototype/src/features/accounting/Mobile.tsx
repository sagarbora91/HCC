// Module 20 · Accounting automation — phone page: date chips, day summary cards, exceptions, reconciliation, voucher cards.
import React,{useMemo,useState} from 'react';
import {CheckCircle2,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,accDates,accVouchers,accExceptions,accReconcile,accLockOf,accDayStatus,accTotals,accLockBlocker,accMapRows,rbacCanTally} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {Empty,shortDate,clockTime} from '../../components/ui';
import {TallyExport} from './Accounting';
import {AccStatusPill,AccSeverity,AccLockButton,AccExportButton,accUserName,useAccPerms,accVoucherDebit} from './parts';

type Filter='all'|'Sales'|'Receipt'|'Payment'|'Journal';
export function AccountingMobile(){
  const {s,uid,open}=useHcc();const {canLock,canMap}=useAccPerms();
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
  const [view,setView]=useState<'ledger'|'tally'>('ledger'); // W05-3: Tally export (Simulated) reachable on the phone
  const head=<><div className="page-title"><div><h1>Accounting</h1><p>Vouchers derived from folios, receipts, expenses and closing · fictional data</p></div></div>
    {tally&&<ChipFilter<'ledger'|'tally'> label="Accounting view" value={view} onChange={setView} options={[{id:'ledger',label:'Ledger'},{id:'tally',label:'Tally export (Simulated)'}]}/>}</>;
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
      onClick={x.key&&canMap?()=>open({type:'acc-map',id:x.key}):x.voucher?()=>open({type:'acc-voucher',id:date+'|'+x.voucher}):undefined}/>)
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
    <StickyFooter>{lockable?<><AccExportButton date={date}/><AccLockButton date={date}/></>:<><AccExportButton date={date} primary/>{lock&&<AccLockButton date={date} primary={false}/>}</>}</StickyFooter>
  </div>;
}
