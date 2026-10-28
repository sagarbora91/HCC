// ACC-2 · acc2-ui — Accounting › GST month (contract §6): the set-off working by head in s.49 order, the GST payment and statutory remittance forms,
// the month lock with its blocker list, unlock with a reason. Screens only: the working, the postings and the lock are the gst lane's.
import React,{useMemo,useState} from 'react';
import {Lock,LockOpen,CheckCircle2,AlertTriangle,Banknote} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter} from '../../app/mobile/components';
import {Empty,dateLabel} from '../../components/ui';
import {accGstMonth,accGstHeads,accMonthLockBlocker,accMonthLockOf,accManualList,type ACCGstHead,
  type AccGstSetoff,type AccGstPay,type AccStatRemit,type AccMonthLockCmd,type AccMonthLockRequest,type AccMonthUnlockCmd} from './_contract-stubs-2';
import {useAccPerms,accUserName} from './parts';
import {monthChoices,monthLabel,toPaise,fromPaise,bankMethods} from './shared';
import {TEMPLATE_LABEL} from './manual-templates';

const HEAD_LABEL:Record<ACCGstHead,string>={igst:'IGST',cgst:'CGST',sgst:'SGST'};
const HEAD_LEDGER:Record<ACCGstHead,[string,string]>={igst:['2130','1330'],cgst:['2110','1310'],sgst:['2120','1320']};
export const blockersOf=(b:string|string[]|undefined)=>([] as string[]).concat(b||[]).filter(Boolean);
const sum3=(r:Record<ACCGstHead,number>)=>r.igst+r.cgst+r.sgst;

export function GstMonthView({phone}:{phone?:boolean}){
  const {s,open,run,busy,toast}=useHcc();const {canTax,canMonthLock,canMonthRequest}=useAccPerms();
  const months=useMemo(()=>monthChoices(s.date),[s.date]);const [month,setMonth]=useState(months[0]);
  const g=useMemo(()=>accGstMonth(s,month),[s,month]);const lock=accMonthLockOf(s,month),blockers=blockersOf(accMonthLockBlocker(s,month));
  const remits=accManualList(s).filter(v=>v.date.startsWith(month)&&(v.template==='gst-payment'||v.template==='statutory-remittance')&&v.status!=='void'&&v.status!=='rejected');
  const stepsTo=(h:ACCGstHead)=>g.setoff.filter(x=>x.against===h);
  const used=(h:ACCGstHead)=>g.setoff.filter(x=>x.credit===h).reduce((a,x)=>a+x.amount,0);
  const picker=phone?<ChipFilter<string> label="GST month" value={month} onChange={setMonth} options={months.slice(0,6).map(m=>({id:m,label:monthLabel(m)}))}/>
    :<label className="acc-date">Month<select value={month} onChange={e=>setMonth(e.target.value)}>{months.map(m=><option key={m} value={m}>{monthLabel(m)}</option>)}</select></label>;
  const status=lock?<span className="pill acc-st-locked"><Lock size={12} aria-hidden/>Month locked</span>:<span className="pill acc-st-open">Open</span>;
  const actions=canTax?<div className="acc-gst-actions">
    <button type="button" className="btn" disabled={busy||!!lock} onClick={()=>void run({type:'ACC_GST_SETOFF',month,key:'accgst-so:'+month} satisfies AccGstSetoff,g.posted?'Set-off re-posted':'Set-off posted')}>{g.posted?'Re-post set-off':'Post set-off'}</button>
    <button type="button" className="btn primary" onClick={()=>open({type:'acc-gst-pay',id:month})}><Banknote size={16}/> Pay GST…</button>
    <button type="button" className="btn" onClick={()=>open({type:'acc-gst-pay',id:month,room:'rcm'})}>Pay reverse-charge GST…</button>
    <button type="button" className="btn" onClick={()=>open({type:'acc-stat-remit',id:month})}>PF / ESIC / PT remittance…</button></div>
    :<p className="muted">GST entries are made by the Owner or the Accountant.</p>;
  const lockBox=<section className="acc-lock-box" aria-label="Month lock">
    <h3 className="acc-h3">Month lock {status}</h3>
    {lock?<p className="muted">Locked by {accUserName(s,lock.lockedBy)} · {dateLabel(lock.lockedAt.slice(0,10))}{lock.setoff?` · set-off ${lock.setoff}`:''}. Every day of the month is locked and the expense month is tax-locked.</p>
      :blockers.length?<><p className="muted">The month cannot be locked yet:</p><ul className="acc-blockers" id="acc-month-why">{blockers.map(b=><li key={b}><AlertTriangle size={14} aria-hidden/> {b}</li>)}</ul></>
      :<p className="muted"><CheckCircle2 size={14} aria-hidden/> Nothing blocks the lock: every day is closed, the set-off is posted and no voucher is pending.</p>}
    {!!lock?.unlocks?.length&&<p className="muted">Unlocked {lock.unlocks.length}×: {lock.unlocks.map(u=>`${u.at.replace('T',' ')} by ${accUserName(s,u.by)} — ${u.reason}`).join(' · ')}</p>}
    <div className="inline">
      {!lock&&canMonthLock&&<button type="button" className="btn primary" disabled={busy||blockers.length>0} aria-describedby={blockers.length?'acc-month-why':undefined} onClick={()=>open({type:'acc-month-lock',id:month})}><Lock size={16}/> Lock month</button>}
      {!lock&&!canMonthLock&&canMonthRequest&&<button type="button" className="btn" disabled={busy||blockers.length>0} onClick={()=>void run({type:'ACC_MONTH_LOCK_REQUEST',month,key:'accml-req:'+month} satisfies AccMonthLockRequest,'Lock requested — the Owner locks the month',{after:()=>toast('Request sent to the Owner')})}>Request month lock</button>}
      {lock&&canMonthLock&&<button type="button" className="btn" onClick={()=>open({type:'acc-month-unlock',id:month})}><LockOpen size={16}/> Unlock month…</button>}
      {!canMonthLock&&!canMonthRequest&&<span className="muted">The Owner locks and unlocks the month.</span>}</div></section>;
  const cards=<div className="acc-m-kpis"><KpiCard label="Output GST" value={money(sum3(g.output))} sub="IGST + CGST + SGST"/><KpiCard label="Input credit" value={money(sum3(g.input))} sub="Claimed ITC"/>
    <KpiCard label="Payable in cash" value={money(g.payableTotal)} sub={g.posted?`Set-off ${g.posted}`:'Set-off not posted'}/><KpiCard label="Reverse charge" value={money(g.rcm)} sub="Paid by GST payment only"/></div>;
  const remitList=remits.length>0&&<><h3 className="acc-h3">Payments and remittances this month</h3>
    {phone?remits.map(v=><ListCard key={v.id} title={`${v.id} · ${TEMPLATE_LABEL[v.template??'journal']}`} sub={v.narration} meta={`${dateLabel(v.date)}${v.reference?' · '+v.reference:''}`} right={<strong className="number">{money(v.lines.reduce((a,l)=>a+l.debit,0))}</strong>} onClick={()=>open({type:'acc-manual',id:v.id})}/>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Voucher</th><th>Date</th><th>Entry</th><th>Reference</th><th className="acc-num">Amount</th></tr></thead><tbody>
        {remits.map(v=><tr key={v.id}><td><button type="button" className="text-btn acc-link number" onClick={()=>open({type:'acc-manual',id:v.id})}>{v.id}</button></td><td>{dateLabel(v.date)}</td><td>{TEMPLATE_LABEL[v.template??'journal']}</td><td>{v.reference??'—'}</td><td className="acc-num number">{money(v.lines.reduce((a,l)=>a+l.debit,0))}</td></tr>)}</tbody></table></div>}</>;
  if(phone)return <>
    {picker}{status}{cards}
    <h3 className="m-group">SET-OFF BY HEAD (SECTION 49 ORDER)</h3>
    {accGstHeads.map(h=><ListCard key={h} title={`${HEAD_LABEL[h]} · ${HEAD_LEDGER[h][0]}`} sub={`Output ${money(g.output[h])} · input credit ${money(g.input[h])}`}
      meta={g.setoff.filter(x=>x.against===h||x.credit===h).map(x=>`${HEAD_LABEL[x.credit]} credit ${money(x.amount)} against ${HEAD_LABEL[x.against]}`).join(' · ')||'No set-off'}
      right={<><strong className="number">{money(g.payable[h])}</strong><br/><small>to pay</small></>}/>)}
    <ListCard title="GST electronic cash ledger (1340)" sub={`TCS credit and challans paid: ${money(g.cashLedger)} Dr`}/>
    {actions}{lockBox}{remitList}</>;
  return <div className="padded acc-gst">
    <div className="inline acc-gst-head">{picker}{status}</div>
    <p className="muted">Output tax of the month less input credit, set off in the order of section 49: IGST credit against IGST, then CGST, then SGST; CGST credit against CGST, then IGST; SGST credit against SGST, then IGST; never CGST against SGST. Illustrative — CA to confirm.</p>
    <div className="table-wrap"><table className="acc-table acc-gst-table"><thead><tr><th>Head</th><th className="acc-num">Output tax</th><th className="acc-num">Input credit</th><th>Set off against this head</th><th className="acc-num">Credit used elsewhere</th><th className="acc-num">To pay in cash</th></tr></thead><tbody>
      {accGstHeads.map(h=><tr key={h}><td><strong>{HEAD_LABEL[h]}</strong><small>{HEAD_LEDGER[h][0]} output · {HEAD_LEDGER[h][1]} input</small></td><td className="acc-num number">{money(g.output[h])}</td><td className="acc-num number">{money(g.input[h])}</td>
        <td>{stepsTo(h).length?stepsTo(h).map(x=><small key={x.credit} className="acc-sub">{HEAD_LABEL[x.credit]} credit {money(x.amount)}</small>):'—'}</td>
        <td className="acc-num number">{money(used(h))}</td><td className="acc-num number"><strong>{money(g.payable[h])}</strong></td></tr>)}
      <tr className="acc-total"><td><strong>Total</strong></td><td className="acc-num number">{money(sum3(g.output))}</td><td className="acc-num number">{money(sum3(g.input))}</td><td/><td className="acc-num number">{money(accGstHeads.reduce((a,h)=>a+used(h),0))}</td><td className="acc-num number"><strong>{money(g.payableTotal)}</strong></td></tr></tbody></table></div>
    <div className="summary-list acc-gst-sum"><div><span>Reverse-charge GST payable (2140)</span><strong>{money(g.rcm)}</strong></div><div><span>TCS credit and challans in the cash ledger (1340)</span><strong>{money(g.cashLedger)} Dr</strong></div>
      <div><span>Credit carried forward</span><strong>{money(sum3(g.carried))}</strong></div><div><span>Set-off voucher</span><strong>{g.posted??'Not posted yet'}</strong></div></div>
    {actions}{lockBox}{remitList}</div>;
}

// ── dialogs ──
function useRemitFields(defaultAmount:number){
  const {s}=useHcc();const methods=bankMethods(s);
  const [amount,setAmount]=useState(fromPaise(defaultAmount));const [method,setMethod]=useState(methods[0]?.[0]??'');const [challan,setChallan]=useState('');const [date,setDate]=useState(s.date);
  const fields=<><label>Amount (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
    <label>Paid from<select value={method} onChange={e=>setMethod(e.target.value)}>{methods.map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
    <label>Challan / reference number<input value={challan} maxLength={40} onChange={e=>setChallan(e.target.value)} placeholder="e.g. CPIN (fictional)"/></label>
    <label>Date paid<input type="date" value={date} max={s.date} onChange={e=>e.target.value&&setDate(e.target.value)}/></label></>;
  return {fields,paise:toPaise(amount),method,challan:challan.trim(),date,ok:toPaise(amount)>0&&!!method&&challan.trim().length>=3};
}
/** 'acc-gst-pay' · id = YYYY-MM, room = 'rcm' (pay the reverse-charge GST from 2140 instead of the set-off balance). Dr 1340 · Cr bank. */
export function GstPayModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canTax}=useAccPerms();const month=id||s.date.slice(0,7),rcm=room==='rcm',g=accGstMonth(s,month);
  const f=useRemitFields(rcm?Math.max(g.rcm,0):g.payableTotal);
  return <form className="form" onSubmit={e=>{e.preventDefault();if(!f.ok)return;void run({type:'ACC_GST_PAY',month,amount:f.paise,method:f.method,challan:f.challan,date:f.date,...(rcm?{rcm:true as const}:{}),key:`accgst-pay:${month}:${rcm?'rcm':'tax'}:${f.challan}`} satisfies AccGstPay,'GST payment recorded',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">GST · {monthLabel(month).toUpperCase()}</p><h2>{rcm?'Pay reverse-charge GST':'Pay GST'}</h2>
      <p>{rcm?`Reverse-charge GST is paid in cash, never by set-off: Dr GST payable under reverse charge (2140) · Cr bank. Payable now ${money(Math.max(g.rcm,0))}.`:`Challan deposit into the electronic cash ledger: Dr 1340 · Cr bank. The set-off leaves ${money(g.payableTotal)} to pay (IGST ${money(g.payable.igst)}, CGST ${money(g.payable.cgst)}, SGST ${money(g.payable.sgst)}).`}</p></div>
    {f.fields}
    {!canTax&&<p className="f-why">GST payments are entered by the Owner or the Accountant.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!canTax||!f.ok}>Record payment</button></div></form>;
}
const STAT:{id:'pf'|'esic'|'pt';label:string;ledger:string}[]=[{id:'pf',label:'Provident fund (PF)',ledger:'2310'},{id:'esic',label:'ESIC',ledger:'2320'},{id:'pt',label:'Professional tax',ledger:'2330'}];
/** 'acc-stat-remit' · id = YYYY-MM: PF / ESIC / PT remittance (Dr 2310 / 2320 / 2330 · Cr bank); the matching compliance card is marked done with the challan. */
export function StatRemitModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canTax}=useAccPerms();const month=id||s.date.slice(0,7);const [kind,setKind]=useState<'pf'|'esic'|'pt'>('pf');
  const f=useRemitFields(0);const st=STAT.find(x=>x.id===kind)!;
  return <form className="form" onSubmit={e=>{e.preventDefault();if(!f.ok)return;void run({type:'ACC_STAT_REMIT',kind,month,amount:f.paise,method:f.method,challan:f.challan,date:f.date,key:`accstat:${kind}:${month}:${f.challan}`} satisfies AccStatRemit,`${st.label} remittance recorded`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">STATUTORY REMITTANCE · {monthLabel(month).toUpperCase()}</p><h2>Remit {st.label}</h2>
      <p>Dr {st.label} payable ({st.ledger}) · Cr bank. The matching compliance card is marked done with the challan as evidence.</p></div>
    <label>Which dues<select value={kind} onChange={e=>setKind(e.target.value as typeof kind)}>{STAT.map(x=><option key={x.id} value={x.id}>{x.label}</option>)}</select></label>
    {f.fields}
    {!canTax&&<p className="f-why">Statutory remittances are entered by the Owner or the Accountant.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!canTax||!f.ok}>Record remittance</button></div></form>;
}
/** 'acc-month-lock' · id = YYYY-MM (Owner): confirm, with the blocker list. */
export function MonthLockModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canMonthLock}=useAccPerms();const month=id||s.date.slice(0,7),blockers=blockersOf(accMonthLockBlocker(s,month));
  return <><div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Lock {monthLabel(month)}?</h2>
      <p>Every day of the month is locked (closed days are locked in this step), the expense month is tax-locked, and nothing can be posted into it until you unlock it with a reason.</p></div>
    {blockers.length>0&&<ul className="acc-blockers f-why" role="alert">{blockers.map(b=><li key={b}>{b}</li>)}</ul>}
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||!canMonthLock||blockers.length>0} onClick={()=>void run({type:'ACC_MONTH_LOCK',month,key:'accml:'+month} satisfies AccMonthLockCmd,`${monthLabel(month)} locked`,{close:true})}><Lock size={16}/> Lock month</button></div></>;
}
/** 'acc-month-unlock' · id = YYYY-MM (Owner, reason required — DEC-131 style). */
export function MonthUnlockModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canMonthLock}=useAccPerms();const month=id||s.date.slice(0,7),lock=accMonthLockOf(s,month);const [reason,setReason]=useState('');
  if(!lock)return <Empty title="This month is not locked"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_MONTH_UNLOCK',month,reason:reason.trim(),key:`accmu:${month}:${reason.trim()}`} satisfies AccMonthUnlockCmd,`${monthLabel(month)} unlocked`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Unlock {monthLabel(month)}</h2><p>Locked by {accUserName(s,lock.lockedBy)} on {dateLabel(lock.lockedAt.slice(0,10))}. The reason is kept with the month.</p></div>
    <label>Reason (required)<textarea rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Correct a challan date"/></label>
    {!canMonthLock&&<p className="f-why">Only the Owner can unlock a month.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!canMonthLock||reason.trim().length<3}><LockOpen size={16}/> Unlock month</button></div></form>;
}
