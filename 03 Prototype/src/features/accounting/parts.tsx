// Module 20 · Accounting automation — shared UI pieces (desktop + phone) and dialogs.
import React,{useState} from 'react';
import {Lock,LockOpen,Download,AlertTriangle,CheckCircle2,Info,Link2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,accVouchers,accVoucherTotals,accLedgerName,accSourceOf,accLockBlocker,accLockOf,accDaySummary,accCsv,rbacCanExportVouchers,accMapRows,accMapping,
  ACC_CHART,ACC_SUSPENSE,ACC_LOCK_ROLES,type ACCVoucher,type ACCDayStatus,type ACCException} from '../../services/mock';
import {Empty,dateLabel,shortDate,clockTime} from '../../components/ui';
import {downloadCsv} from '../reports/Reports';

export const accMoney=(n:number)=>n?money(n):'—';
export function AccStatusPill({status}:{status:ACCDayStatus}){
  return <span className={'pill acc-st-'+status.toLowerCase()}>{status==='Locked'?<Lock size={12} aria-hidden/>:null}{status==='Open'?'Open · provisional':status==='Closed'?'Closed · not locked':'Locked'}</span>;
}
export function AccSeverity({x}:{x:ACCException}){
  const Icon=x.severity==='error'?AlertTriangle:x.severity==='warning'?AlertTriangle:Info;
  return <span className={'pill acc-sev-'+x.severity}><Icon size={12} aria-hidden/>{x.severity==='error'?'Blocks lock':x.severity==='warning'?'Warning':'Info'}</span>;
}
export const accUserName=(s:ReturnType<typeof useHcc>['s'],id:string)=>s.users.find(u=>u.id===id)?.name??id;
/** Permission helpers for the page (coarse permissions + lock roles). */
export function useAccPerms(){
  const {s,uid,user,permission}=useHcc();
  return {canLock:permission('reports')&&ACC_LOCK_ROLES.includes(user.role),canUnlock:permission('reports')&&permission('admin'),canMap:permission('admin'),s,uid};
}
export function accExport(s:ReturnType<typeof useHcc>['s'],date:string,toast:(t:string)=>void,uid:string){
  if(!rbacCanExportVouchers(s,uid)){toast('Your role cannot export vouchers.');return;} // DEC-063 G13
  const vs=accVouchers(s,date);
  downloadCsv(`HCC-Demo-vouchers-${date}_${date}.csv`,accCsv(vs));
  toast(`Generic voucher CSV downloaded · ${vs.length} voucher${vs.length===1?'':'s'} · Tally/other mapping later`);
}
/** Lock / unlock action button for the selected date (desktop header and phone footer). */
export function AccLockButton({date,primary=true}:{date:string;primary?:boolean}){
  const {s,open}=useHcc();const {canLock,canUnlock}=useAccPerms();
  const locked=!!accLockOf(s,date),why=accLockBlocker(s,date);
  if(locked)return canUnlock?<button className="btn" onClick={()=>open({type:'acc-unlock',id:date})}><LockOpen size={16}/> Unlock period</button>:null;
  if(!canLock)return null;
  return <button className={'btn'+(primary?' primary':'')} disabled={!!why} title={why||undefined} aria-describedby={why?'acc-lock-why':undefined} onClick={()=>open({type:'acc-lock',id:date})}><Lock size={16}/> Lock period</button>;
}
export function AccExportButton({date,primary=false}:{date:string;primary?:boolean}){
  const {s,uid,toast}=useHcc();const n=accVouchers(s,date).length;
  if(!rbacCanExportVouchers(s,uid))return null;
  return <button className={'btn'+(primary?' primary':'')} disabled={!n} onClick={()=>accExport(s,date,toast,uid)}><Download size={16}/> Export CSV</button>;
}

// ── Dialogs ──
const parseRef=(id?:string)=>{const [date,no]=(id??'').split('|');return {date,no};};
/** 'acc-voucher' · id = "<date>|<voucher no>" */
export function AccVoucherModal({id}:{id?:string}){
  const {s,closeModal,booking,display}=useHcc();const {date,no}=parseRef(id);
  const v=accVouchers(s,date).find(x=>x.no===no);
  if(!v)return <Empty title="Voucher not found" body="It may have changed since the list was opened (vouchers are re-derived until the day is locked)."/>;
  const t=accVoucherTotals(v),src=accSourceOf(s,v),locked=!!accLockOf(s,date);
  return <><div className="modal-heading"><p className="eyebrow">{v.type.toUpperCase()} VOUCHER · {locked?'LOCKED SNAPSHOT':'PROVISIONAL'}</p><h2 className="number">{v.no}</h2><p>{dateLabel(v.date)} · {v.narration}</p></div>
    {display.isMobile?<div className="acc-m-entries">{v.entries.map((e,i)=><div key={i}><span><strong>{accLedgerName(e.account)}</strong><small>{e.account}{e.account===ACC_SUSPENSE?' · unmapped':''}</small></span>
        <b className="number">{e.debit?'Dr '+money(e.debit):'Cr '+money(e.credit)}</b></div>)}
      <div className="acc-total"><span><strong>Total</strong></span><b className="number">Dr {money(t.debit)} · Cr {money(t.credit)}</b></div></div>
    :<div className="table-wrap acc-entries"><table><thead><tr><th>Ledger</th><th className="acc-num">Debit</th><th className="acc-num">Credit</th></tr></thead>
      <tbody>{v.entries.map((e,i)=><tr key={i}><td><strong>{accLedgerName(e.account)}</strong><small>{e.account}{e.account===ACC_SUSPENSE?' · unmapped':''}</small></td><td className="acc-num number">{accMoney(e.debit)}</td><td className="acc-num number">{accMoney(e.credit)}</td></tr>)}
        <tr className="acc-total"><td><strong>Total</strong></td><td className="acc-num number"><strong>{money(t.debit)}</strong></td><td className="acc-num number"><strong>{money(t.credit)}</strong></td></tr></tbody></table></div>}
    <p className="acc-balance-line">{t.debit===t.credit?<span className="pill green"><CheckCircle2 size={12}/>Balanced</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Unbalanced</span>}
      {v.unmapped.length>0&&<span className="pill acc-sev-error">Unmapped: {v.unmapped.join(', ')}</span>}</p>
    <h3 className="acc-h3">Source record</h3>
    <div className="summary-list acc-source">
      <div><span>Source ID</span><strong className="number">{v.source.id}</strong></div>
      <div><span>Kind</span><strong>{v.source.kind} · {v.source.sub}</strong></div>
      {src.line&&<div><span>Folio line</span><strong>{src.line.label} · {money(src.line.amount)} · tax {src.line.taxBps/100}%</strong></div>}
      {src.receipt&&<div><span>Receipt</span><strong>{src.receipt.method} · {money(src.receipt.amount)} · {src.receipt.reference}</strong></div>}
      {src.expense&&<div><span>Expense</span><strong>{src.expense.vendor} · {src.expense.category} · {src.expense.status}</strong></div>}
      {src.posReceipt&&<div><span>Outlet receipt</span><strong>{src.posReceipt.order} · {src.posReceipt.method} · {money(src.posReceipt.amount)} · {src.posReceipt.reference}</strong></div>}
      {v.source.kind==='closing'&&<div><span>Closing record</span><strong>Cash difference accepted for {shortDate(date)}</strong></div>}
      {(src.line||src.receipt)===undefined&&v.source.kind!=='closing'&&!src.expense&&!src.posReceipt&&<div><span>Status</span><strong>Source record no longer present (snapshot kept)</strong></div>}
    </div>
    <div className="modal-actions">{src.booking&&<button className="btn" onClick={()=>booking(src.booking!.id)}><Link2 size={16}/> Open folio {src.booking.id}</button>}<button className="btn primary" onClick={closeModal}>Done</button></div></>;
}
/** 'acc-lock' · id = business date */
export function AccLockModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const date=id??s.date;const sum=accDaySummary(s,date),why=accLockBlocker(s,date);
  const failing=sum.checks.filter(c=>!c.ok);
  return <><div className="modal-heading"><p className="eyebrow">ACCOUNTING PERIOD</p><h2>Lock {dateLabel(date)}?</h2>
    <p>The {sum.count} derived voucher{sum.count===1?'':'s'} for this closed business day are frozen as a snapshot. Later mapping changes will not alter them; only the owner can unlock (with a reason).</p></div>
    <div className="summary-list"><div><span>Vouchers</span><strong>{sum.count}</strong></div><div><span>Total debits</span><strong className="number">{money(sum.debit)}</strong></div>
      <div><span>Total credits</span><strong className="number">{money(sum.credit)}</strong></div><div><span>Reconciliation</span><strong>{failing.length?`${failing.length} check(s) differ`:`All ${sum.checks.length} checks agree`}</strong></div></div>
    {why&&<p className="f-why" role="alert" id="acc-lock-why">{why}</p>}
    {!why&&failing.length>0&&<div className="info-note"><AlertTriangle size={16}/>Some figures differ from Reports: {failing.map(c=>c.label).join(', ')}. You can still lock; review the Reconciliation tab first.</div>}
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={!!why} onClick={()=>run({type:'ACC_LOCK',date},`Accounting period ${shortDate(date)} locked`,{close:true})}><Lock size={16}/> Lock period</button></div></>;
}
/** 'acc-unlock' · id = business date (owner) */
export function AccUnlockModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const date=id??s.date;const lock=accLockOf(s,date);const [reason,setReason]=useState('');
  if(!lock)return <Empty title="This date is not locked"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'ACC_UNLOCK',date,reason:reason.trim()},`Accounting period ${shortDate(date)} unlocked`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Unlock {dateLabel(date)}</h2><p>Locked by {accUserName(s,lock.lockedBy)} at {clockTime(lock.lockedAt)} on {shortDate(lock.lockedAt.slice(0,10))}. Vouchers return to live derivation with the current mapping.</p></div>
    <label>Reason (required)<textarea name="reason" rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Reclassify laundry expense"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" type="submit" disabled={reason.trim().length<3}><LockOpen size={16}/> Unlock</button></div></form>;
}
/** 'acc-map' · id = mapping key (admin) */
export function AccMapModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const {canMap}=useAccPerms();
  const row=accMapRows(s).find(r=>r.key===id);const [account,setAccount]=useState(row?.account&&row.account!==ACC_SUSPENSE?row.account:'');
  if(!row)return <Empty title="Classification not found"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'ACC_MAP_SET',source:row.key,account},'Mapping updated · locked periods unchanged',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">{row.group.toUpperCase()}</p><h2>Map “{row.value}”</h2>
      <p>Currently {row.mapped?<strong>{row.account} {accLedgerName(row.account)}</strong>:<strong>unmapped (posts to Suspense)</strong>}{row.defaultAccount?` · default ${row.defaultAccount} ${accLedgerName(row.defaultAccount)}`:''}. Used by {row.uses} source record{row.uses===1?'':'s'}. Changes apply to unlocked dates only.</p></div>
    <label>Ledger<select name="account" value={account} disabled={!canMap} onChange={e=>setAccount(e.target.value)}>
      <option value="">Choose a ledger…</option>
      {ACC_CHART.filter(a=>a.code!==ACC_SUSPENSE).map(a=><option key={a.code} value={a.code}>{a.code} · {a.name} ({a.group})</option>)}</select></label>
    {!canMap&&<p className="f-why">Only an administrator (Owner) can change mappings.</p>}
    <div className="modal-actions">
      {row.changed&&canMap&&<button type="button" className="btn" onClick={()=>run({type:'ACC_MAP_RESET',source:row.key},'Mapping reset to default',{close:true})}>Reset to default</button>}
      <button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" type="submit" disabled={!canMap||!account||account===accMapping(s)[row.key]}>Save mapping</button></div></form>;
}
export function accVoucherDebit(v:ACCVoucher){return accVoucherTotals(v).debit;}
