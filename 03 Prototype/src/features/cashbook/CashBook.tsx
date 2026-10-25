// W05-D · Cash book page (desktop table + phone cards, same logic). Range view lists every business day with opening, receipts, payments,
// closing; day view lists every entry with a running balance. All figures come from domain/streams/cashbook.ts (→ shiftMovements + closing records).
import React,{useMemo,useState} from 'react';
import {Lock,Unlock,Download,ChevronLeft,ArrowRight,FileSpreadsheet} from 'lucide-react';
import {money,cbDay,cbRange,cbDates,cbCsv,cbCanLock,cbCanReopen,cbSlice,addDays,CB_DIFF_LABEL} from '../../services/mock';
import type {CBDay} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {saveCsv,saveBinaryFile,saveAndReport} from '../../app/native';
import {XLSX_MIME} from '../../app/xlsx';
import {cbXlsx} from './exportXlsx';
import {cbRangeMemo} from '../../domain/memo';

const who=(users:{id:string;name:string}[],id?:string)=>users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const tone=(d:CBDay)=>d.locked?'green':d.status==='Closed'?'neutral':'blue';
export function DayCard({d,phone,onBack}:{d:CBDay;phone:boolean;onBack:()=>void}){
  const {s,uid,run,open,busy,go}=useHcc();
  const lock=cbCanLock(s,uid,d.date),canReopen=cbCanReopen(s,uid,d.date);
  const hist=cbSlice(s).history[d.date]??[];
  let bal=d.opening;const rows=d.entries.map(e=>{bal+=e.in-e.out;return {e,bal};});
  const pg=usePaged(rows,phone?PHONE_PAGE:DESKTOP_PAGE);
  // W05-7: the count difference is shown as its own line so the closing visibly carries to the next day's opening.
  const diff=d.carried-d.closing;
  return <>
    <div className="cb-daybar"><button className="btn" onClick={onBack}><ChevronLeft size={15}/> All days</button>
      <h2>{dateLabel(d.date)}</h2><Pill tone={tone(d)}>{d.locked?'Locked':d.status==='Closed'?'Closed · not locked':'Open day (provisional)'}</Pill></div>
    <div className="cb-sums">
      <div><span>Opening balance</span><strong>{money(d.opening)}</strong></div>
      <div><span>Cash received</span><strong>{money(d.receipts)}</strong></div>
      <div><span>Cash paid out</span><strong>{money(d.payments)}</strong></div>
      <div><span>Closing balance</span><strong>{money(d.closing)}</strong></div>
      {diff!==0&&<div><span>Cash over/short at count</span><strong>{money(diff)}</strong></div>}
      {d.counted!==undefined&&<div><span>Carried to next day</span><strong>{money(d.carried)}</strong></div>}</div>
    {d.expected!==undefined&&<div className={'cb-recon '+(d.reconciles?'ok':'bad')}>
      Cashier count: expected {money(d.expected)} · counted {money(d.counted!)} · {d.difference?`difference ${money(d.difference)}`:'no difference'}
      {d.banked>0&&<> · banked {money(d.banked)}</>} · carried to next day {money(d.carried)}
      {!d.reconciles&&<strong> · Book does not match the closing record</strong>}</div>}
    <section className="card"><div className="section-title"><h2>Entries</h2>
      <span className="muted">Same movements as the Cashier shift · {rows.length} line{rows.length===1?'':'s'}</span></div>
      {!rows.length?<Empty title="No cash movements" body="Nothing moved through the drawer on this day."/>:phone?
        <div className="cb-cards">{pg.shown.map(({e,bal})=><div className="cb-card" key={e.id}><div><strong>{e.label}</strong><small>{e.reference}{e.guest?` · ${e.guest}`:''}</small><small>{e.id} · {who(s.users,e.actor)}</small></div>
          <div className="cb-amt"><b className={e.in?'in':'out'}>{e.in?'+ '+money(e.in):'− '+money(e.out)}</b><small>Balance {money(bal)}</small></div></div>)}
          {diff!==0&&<div className="cb-card"><div><strong>{CB_DIFF_LABEL}</strong><small>Cashier count · not a cash movement</small></div>
            <div className="cb-amt"><b className={diff>0?'in':'out'}>{diff>0?'+ '+money(diff):'− '+money(-diff)}</b><small>Carried {money(d.carried)}</small></div></div>}</div>:
        <div className="table-wrap"><table><thead><tr><th>Entry</th><th>Reference</th><th>By</th><th className="number">Receipt</th><th className="number">Payment</th><th className="number">Balance</th></tr></thead><tbody>
          <tr className="cb-open"><td colSpan={5}>Opening balance (carried forward)</td><td className="number">{money(d.opening)}</td></tr>
          {pg.shown.map(({e,bal})=><tr key={e.id}><td><strong>{e.label}</strong><small>{e.id}{e.guest?` · ${e.guest}`:''}</small></td><td>{e.reference}</td><td>{who(s.users,e.actor)}</td>
            <td className="number">{e.in?money(e.in):''}</td><td className="number">{e.out?money(e.out):''}</td><td className="number">{money(bal)}</td></tr>)}
          <tr className="cb-close"><td colSpan={3}>Closing balance</td><td className="number">{money(d.receipts)}</td><td className="number">{money(d.payments)}</td><td className="number">{money(d.closing)}</td></tr>
          {diff!==0&&<tr><td colSpan={3}><strong>{CB_DIFF_LABEL}</strong><small>Cashier count · not a cash movement</small></td><td className="number">{diff>0?money(diff):''}</td><td className="number">{diff<0?money(-diff):''}</td><td className="number">{money(d.carried)}</td></tr>}
          {d.counted!==undefined&&<tr className="cb-open"><td colSpan={5}>Carried forward to next day</td><td className="number">{money(d.carried)}</td></tr>}
        </tbody></table></div>}
      <ShowMore p={pg}/></section>
    <section className="card"><div className="section-title"><h2>Day lock</h2></div>
      {d.locked?<p>Locked by <strong>{who(s.users,d.locked.lockedBy)}</strong> at {d.locked.lockedAt.replace('T',' ')}.</p>:<p className="muted">{d.status==='Closed'?'Not locked yet. A different person from the one who counted checks and locks the day.':'The day can be locked after it is closed.'}</p>}
      <div className="cb-actions">
        {!d.locked&&<button className="btn primary" disabled={busy||!lock.ok} onClick={()=>run({type:'CB_LOCK',date:d.date,key:'cb-lock:'+d.date+':'+hist.length},'Day locked')}><Lock size={15}/> Lock this day</button>}
        {d.locked&&<button className="btn" disabled={busy||!canReopen} onClick={()=>open({type:'cb-reopen',id:d.date})}><Unlock size={15}/> Reopen…</button>}
        {!d.locked&&!lock.ok&&<span className="f-why">{lock.why}</span>}
        {d.locked&&!canReopen&&<span className="f-why">Only the Owner can reopen, with a reason.</span>}
        <button className="btn" onClick={()=>go('closing')}>Day closing <ArrowRight size={14}/></button></div>
      {!!hist.length&&<ul className="cb-hist">{hist.map((h,i)=><li key={i}><strong>{h.action==='lock'?'Locked':'Reopened'}</strong> by {who(s.users,h.by)} · {h.at.replace('T',' ')}{h.reason?` · ${h.reason}`:''}</li>)}</ul>}
    </section></>;
}

/** CSV + Excel buttons for a date range (W05-8: Excel through the W05-E xlsx helper; a failed save shows an error, F11). */
function Exports({from,to}:{from:string;to:string}){
  const {s,toast}=useHcc();const tag=from===to?from:`${from}_${to}`;
  return <span className="inline">
    <button className="btn" onClick={()=>void saveAndReport(saveCsv(`cash-book-${tag}.csv`,cbCsv(s,from,to)),toast,'Cash book CSV saved','the cash book CSV')}><Download size={15}/> CSV</button>
    <button className="btn" onClick={()=>void saveAndReport(saveBinaryFile(`HCC-Demo-cash-book-${tag}.xlsx`,cbXlsx(s,from,to),XLSX_MIME),toast,'Cash book Excel workbook saved','the cash book Excel workbook')}><FileSpreadsheet size={15}/> Excel</button></span>;
}
export function CashBookView({phone=false}:{phone?:boolean}){
  const {s}=useHcc();
  const dates=cbDates(s);
  const [from,setFrom]=useState(()=>{const d=addDays(s.date,-13);return dates.includes(d)?d:dates[Math.max(0,dates.length-14)];});
  const [to,setTo]=useState(s.date);
  const [day,setDay]=useState<string|null>(null);
  // Range totals and day rows are computed once per range and reused until the underlying records change.
  const r=cbRangeMemo(s,from,to),list=useMemo(()=>r.days.slice().reverse(),[r]);
  const dayView=useMemo(()=>day&&dates.includes(day)?cbDay(s,day):null,[day,r,s.cashbookX]); // eslint-disable-line react-hooks/exhaustive-deps
  const pg=usePaged(list,phone?PHONE_PAGE:DESKTOP_PAGE);
  const cls='cb-page'+(phone?' mm-page':'');
  if(dayView)return <div className={cls}>
    {!phone&&<PageTitle title="Cash book" sub="Daily cash in and out with a running balance" action={<Exports from={dayView.date} to={dayView.date}/>}/>}
    <DayCard d={dayView} phone={phone} onBack={()=>setDay(null)}/></div>;
  return <div className={cls}>
    {!phone&&<PageTitle title="Cash book" sub="Opening balance, every cash receipt and payment, closing balance — carried forward day to day" action={<Exports from={from} to={to}/>}/>}
    <div className="cb-range"><label>From<input type="date" value={from} min={dates[0]} max={to} onChange={e=>e.target.value&&setFrom(e.target.value)}/></label>
      <label>To<input type="date" value={to} min={from} max={s.date} onChange={e=>e.target.value&&setTo(e.target.value)}/></label>
      {phone&&<Exports from={from} to={to}/>}</div>
    <div className="cb-sums"><div><span>Opening</span><strong>{money(r.opening)}</strong></div><div><span>Received</span><strong>{money(r.receipts)}</strong></div>
      <div><span>Paid out (incl. banking)</span><strong>{money(r.payments)}</strong></div>
      <div><span>Cash over/short (counted − expected)</span><strong>{money(r.differences)}</strong></div>
      {r.gaps!==0&&<div><span>Opening differs from previous carry</span><strong>{money(r.gaps)}</strong></div>}
      <div><span>Closing (carried forward)</span><strong>{money(r.closing)}</strong></div></div>
    <section className="card">{!list.length?<Empty title="No days in this range" body="Choose dates that include a closed or the current business day."/>:phone?
      <div className="cb-cards">{pg.shown.map(d=><button type="button" className="cb-card cb-tap" key={d.date} onClick={()=>setDay(d.date)}>
        <div><strong>{dateLabel(d.date)}</strong><small>Opening {money(d.opening)} · in {money(d.receipts)} · out {money(d.payments)}</small>
          <Pill tone={tone(d)}>{d.locked?'Locked':d.status==='Closed'?'Closed':'Open'}</Pill></div>
        <div className="cb-amt"><b>{money(d.closing)}</b><small>Closing</small></div></button>)}</div>:
      <div className="table-wrap"><table><thead><tr><th>Business date</th><th>Status</th><th className="number">Opening</th><th className="number">Receipts</th><th className="number">Payments</th><th className="number">Closing (book)</th><th className="number">Counted</th><th className="number">Over/short</th><th/></tr></thead><tbody>
        {pg.shown.map(d=><tr key={d.date}><td><strong>{shortDate(d.date)}</strong><small>{d.date}</small></td>
          <td><Pill tone={tone(d)}>{d.locked?'Locked':d.status==='Closed'?'Closed':'Open'}</Pill></td>
          <td className="number">{money(d.opening)}</td><td className="number">{money(d.receipts)}</td><td className="number">{money(d.payments)}</td><td className="number">{money(d.closing)}</td>
          <td className="number">{d.counted===undefined?'—':money(d.counted)}</td><td className="number">{d.difference?money(d.difference):'—'}</td><td><button className="btn small-btn" aria-label={`Open day ${dateLabel(d.date)}`} onClick={()=>setDay(d.date)}>Open day</button></td></tr>)}
      </tbody></table></div>}
      <ShowMore p={pg}/></section></div>;
}
export const CashBook=()=><CashBookView/>;
export const CashBookMobile=()=><CashBookView phone/>;

export function CbReopenModal({id}:{id?:string}){
  const {s,run,busy,closeModal}=useHcc();const [reason,setReason]=useState('');
  const d=id?cbSlice(s).locks[id]:undefined;
  if(!id||!d)return <Empty title="This day is not locked"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CB_REOPEN',date:id,reason,key:'cb-reopen:'+id+':'+(cbSlice(s).history[id]?.length??0)},'Day reopened',{close:true});}}>
    <div className="modal-heading"><h2>Reopen cash book day</h2><p>{dateLabel(id)} · locked by {who(s.users,d.lockedBy)}</p></div>
    <label>Reason (required, kept in the audit trail)<textarea required minLength={3} maxLength={200} rows={3} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Banking slip entered on the wrong day"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<3}>Reopen</button></div></form>;
}
