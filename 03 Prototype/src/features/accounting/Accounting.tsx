// Module 20 · Accounting automation — desktop page (CLOSING & REPORTS › Accounting).
import React,{useMemo,useState} from 'react';
import {Info,CheckCircle2,AlertTriangle,Download} from 'lucide-react';
import {saveTextFile} from '../../app/native';
import {useHcc} from '../../app/context';
import {money,accDates,accVouchers,accTrialBalance,accVouchersUpTo,accExceptions,accReconcile,accMapRows,accLockOf,accDayStatus,accTotals,accLockBlocker,accLedgerName,accTallyXml,TALLY_MAX_DAYS,addDays,
  ACC_CHART,ACC_SUSPENSE} from '../../services/mock';
import {PageTitle,Empty,Stat,shortDate,clockTime} from '../../components/ui';
import {AccStatusPill,AccSeverity,AccLockButton,AccExportButton,accMoney,accUserName,useAccPerms,accVoucherDebit} from './parts';

type Tab='vouchers'|'tb'|'exceptions'|'recon'|'mapping'|'chart'|'tally';
export function AccDatePicker({date,setDate,dates}:{date:string;setDate:(d:string)=>void;dates:string[]}){
  return <label className="acc-date">Business date<select value={date} onChange={e=>setDate(e.target.value)}>
    {dates.map(d=><option key={d} value={d}>{shortDate(d)} {d.slice(0,4)}</option>)}</select></label>;
}
export function AccountingPage(){
  const {s,open}=useHcc();const {canMap}=useAccPerms();
  const dates=useMemo(()=>accDates(s),[s]);
  const [sel,setDate]=useState(s.date);const date=dates.includes(sel)?sel:dates[0];
  const [tab,setTab]=useState<Tab>('vouchers');const [cum,setCum]=useState(false);
  const vs=useMemo(()=>accVouchers(s,date),[s,date]);
  const ex=useMemo(()=>accExceptions(s,date),[s,date]);
  const recon=useMemo(()=>accReconcile(s,date),[s,date]);
  const tb=useMemo(()=>accTrialBalance(cum?accVouchersUpTo(s,date):vs),[s,date,vs,cum]);
  const t=accTotals(vs),status=accDayStatus(s,date),lock=accLockOf(s,date),why=accLockBlocker(s,date);
  const errors=ex.filter(x=>x.severity==='error').length,reconOk=recon.every(c=>c.ok);
  const tabs:[Tab,string,number?][]=[['vouchers','Vouchers',vs.length],['tb','Trial balance'],['exceptions','Exceptions',ex.filter(x=>x.severity!=='info').length],['recon','Reconciliation'],['mapping','Mapping'],['chart','Chart of accounts'],['tally','Tally export']];
  const tbT=tb.reduce((a,r)=>({debit:a.debit+r.debit,credit:a.credit+r.credit}),{debit:0,credit:0});
  return <div className="acc-page">
    <PageTitle title="Accounting" sub="Balanced vouchers derived automatically from folio lines, receipts, expenses and closing records · fictional data · synthetic 10% tax"
      action={<div className="inline"><AccDatePicker date={date} setDate={setDate} dates={dates}/><AccExportButton date={date}/><AccLockButton date={date}/></div>}/>
    <div className="acc-statusbar"><AccStatusPill status={status}/>
      {lock?<span className="muted">Locked by {accUserName(s,lock.lockedBy)} · {shortDate(lock.lockedAt.slice(0,10))} {clockTime(lock.lockedAt)} (simulated) · {lock.count} vouchers frozen</span>
        :why?<span className="muted" id="acc-lock-why">{why}</span>:<span className="muted">Ready to lock.</span>}
      <span className="acc-generic"><Info size={14} aria-hidden/> Export is a generic import format · Tally/other mapping later</span></div>
    <div className="stats acc-stats">
      <Stat title="Vouchers" value={String(vs.length)} sub={`${vs.filter(v=>v.type==='Sales').length} sales · ${vs.filter(v=>v.type==='Receipt').length} receipt · ${vs.filter(v=>v.type==='Payment').length} payment · ${vs.filter(v=>v.type==='Journal').length} journal`}/>
      <Stat title="Total debits" value={money(t.debit)} sub={t.debit===t.credit?'Equals total credits — balanced':'Does not equal credits'}/>
      <Stat title="Total credits" value={money(t.credit)} sub={reconOk?`Reconciles with Reports (${recon.length} checks)`:`${recon.filter(c=>!c.ok).length} reconciliation check(s) differ`}/>
      <Stat title="Exceptions" value={String(errors)} sub={errors?'Blocking — resolve before locking':'Nothing blocks locking'}/>
    </div>
    <section className="card">
      <div className="tabs acc-tabs" role="tablist" aria-label="Accounting view">{tabs.map(([id,label,n])=><button key={id} role="tab" aria-selected={tab===id} className={tab===id?'active':''} onClick={()=>setTab(id)}>{label}{n!==undefined&&<span>{n}</span>}</button>)}</div>
      {tab==='vouchers'&&(vs.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Voucher no</th><th>Type</th><th>Narration</th><th>Source ID</th><th className="acc-num">Amount</th><th>Check</th></tr></thead>
        <tbody>{vs.map(v=><tr key={v.no}><td><button className="text-btn acc-link number" onClick={()=>open({type:'acc-voucher',id:date+'|'+v.no})}>{v.no}</button></td><td>{v.type}</td>
          <td className="acc-wrap">{v.narration}</td><td className="number"><small>{v.source.id}</small></td><td className="acc-num number"><strong>{money(accVoucherDebit(v))}</strong></td>
          <td>{v.unmapped.length?<span className="pill acc-sev-error">Suspense</span>:<span className="pill green">Balanced</span>}</td></tr>)}</tbody></table></div>
        :<Empty title="No vouchers for this date" body="Nothing was posted, received or spent on this business date."/>)}
      {tab==='tb'&&<>
        <div className="acc-subbar"><div className="filters" role="group" aria-label="Trial balance scope"><button className={!cum?'active':''} aria-pressed={!cum} onClick={()=>setCum(false)}>Day movement</button><button className={cum?'active':''} aria-pressed={cum} onClick={()=>setCum(true)}>Cumulative to {shortDate(date)}</button></div>
          {tbT.debit===tbT.credit?<span className="pill green"><CheckCircle2 size={12}/>Debits = credits</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Out of balance</span>}</div>
        {tb.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Code</th><th>Ledger</th><th>Group</th><th className="acc-num">Debit</th><th className="acc-num">Credit</th><th className="acc-num">Balance</th></tr></thead>
          <tbody>{tb.map(r=><tr key={r.account} className={r.account===ACC_SUSPENSE?'acc-suspense':''}><td className="number">{r.account}</td><td><strong>{r.name}</strong></td><td>{r.group}</td><td className="acc-num number">{accMoney(r.debit)}</td><td className="acc-num number">{accMoney(r.credit)}</td>
            <td className="acc-num number">{r.balance===0?'—':money(Math.abs(r.balance))+(r.balance>0?' Dr':' Cr')}</td></tr>)}
            <tr className="acc-total"><td/><td><strong>Total</strong></td><td/><td className="acc-num number"><strong>{money(tbT.debit)}</strong></td><td className="acc-num number"><strong>{money(tbT.credit)}</strong></td><td/></tr></tbody></table></div>
          :<Empty title="No ledger movement" body="No vouchers for this scope."/>}</>}
      {tab==='exceptions'&&(ex.length?<div className="acc-exlist">{ex.map(x=><div key={x.id} className={'acc-ex acc-ex-'+x.severity}><AccSeverity x={x}/><div><strong>{x.title}</strong><p>{x.detail}</p>
          <div className="inline">{x.voucher&&<button className="text-btn" onClick={()=>open({type:'acc-voucher',id:date+'|'+x.voucher})}>View {x.voucher}</button>}{x.key&&canMap&&<button className="text-btn" onClick={()=>open({type:'acc-map',id:x.key})}>Map now</button>}</div></div></div>)}</div>
        :<Empty title="No exceptions" body="Every voucher is balanced and mapped."/>)}
      {tab==='recon'&&<div className="table-wrap"><table className="acc-table"><thead><tr><th>Check</th><th className="acc-num">Reports / closing</th><th className="acc-num">Ledger</th><th>Result</th><th>Compared with</th></tr></thead>
        <tbody>{recon.map(c=><tr key={c.id}><td><strong>{c.label}</strong></td><td className="acc-num number">{money(c.report)}</td><td className="acc-num number">{money(c.ledger)}</td>
          <td>{c.ok?<span className="pill green"><CheckCircle2 size={12}/>Agrees</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Differs {money(c.ledger-c.report)}</span>}</td><td><small>{c.note}</small></td></tr>)}</tbody></table></div>}
      {tab==='mapping'&&<MappingTable/>}
      {tab==='tally'&&<TallyExport/>}
      {tab==='chart'&&<div className="table-wrap"><table className="acc-table"><thead><tr><th>Code</th><th>Ledger</th><th>Group</th><th>Note</th></tr></thead>
        <tbody>{ACC_CHART.map(a=><tr key={a.code}><td className="number">{a.code}</td><td><strong>{a.name}</strong></td><td>{a.group}</td><td><small>{a.note??''}</small></td></tr>)}</tbody></table></div>}
    </section>
    <p className="footnote">Vouchers are re-derived from source records until a closed day is locked; voucher numbers are provisional until then. Synthetic chart of accounts and 10% test tax — not GST, not a statutory book of account.</p>
  </div>;
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
  const {s,toast}=useHcc();
  const [to,setTo]=useState(s.date);const [from,setFrom]=useState(addDays(s.date,-6));
  const p=useMemo(()=>accTallyXml(s,from,to),[s,from,to]);
  const lines=p.xml.split(String.fromCharCode(10)),shown=lines.slice(0,60).join(String.fromCharCode(10));
  return <div className="padded exp-tally">
    <div className="exp-tally-head"><span className="pill orange" role="status">Simulated</span><span className="muted">No file is sent to Tally. Saving this XML does not mean it was imported — only Tally's own import confirms that.</span></div>
    <div className="inline exp-tally-range"><label className="acc-date">From<input type="date" value={from} max={s.date} onChange={e=>setFrom(e.target.value)}/></label>
      <label className="acc-date">To<input type="date" value={to} max={s.date} onChange={e=>setTo(e.target.value)}/></label>
      <button className="btn" disabled={!p.count} onClick={()=>{void saveTextFile(`HCC-Demo-tally-${from}_${to}.xml`,p.xml,'application/xml');toast('Simulated Tally XML saved · not imported into Tally');}}><Download size={16}/> Download XML</button></div>
    {p.error?<p className="f-why" role="alert">{p.error}</p>:<>
      <p className="muted">{p.count} voucher{p.count===1?'':'s'} over {p.dates} business date{p.dates===1?'':'s'} · debits {money(p.debit)} · credits {money(p.credit)} · {p.debit===p.credit?'balanced':'NOT balanced'} · up to {TALLY_MAX_DAYS} days</p>
      {p.count?<><pre className="exp-xml" aria-label="Tally XML preview">{shown}</pre>{lines.length>60&&<p className="muted">Preview shows the first 60 of {lines.length} lines; the download has everything.</p>}</>
        :<Empty title="No vouchers in this range" body="Nothing was posted, received or spent on these business dates."/>}</>}
  </div>;
}
