// M15 Cashier shift (phone page 'cashier'): expected-cash hero, movements list, count sheet with denomination steppers,
// difference → reason → approval request. Reuses the closing stream commands SHIFT_SUBMIT / SHIFT_DIFFERENCE_DECIDE.
import React,{useState} from 'react';
import {Calculator,Check,X,ShieldCheck,ArrowRight,Send,Info,ArrowLeft} from 'lucide-react';
import {money,shiftExpected,shiftMovements,shiftStatus,isDayClosed,SHIFT_DENOMINATIONS,SHIFT_MANAGER_DIFF_LIMIT,closingUnlimitedRoles} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ListCard,NumberStepper,StickyFooter,Stepper} from '../../app/mobile/components';
import {Pill,Empty,dateLabel} from '../../components/ui';
import {mmDenominations,mmCountTotal,mmShiftSubmitCommand,mmPaise,mmDiffKind} from './logic';

const diffText=(d:number)=>mmDiffKind(d)==='exact'?'None · exact':(d>0?'Over ':'Short ')+money(Math.abs(d));

export function MmCashier(){
  const {s,uid,user,open,go,run,busy,permission,booking}=useHcc();
  const ex=shiftExpected(s),mv=shiftMovements(s),st=shiftStatus(s),closed=isDayClosed(s);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
  const [reason,setReason]=useState('');
  const sh=s.shift,a=sh?.approval?s.approvals.find(x=>x.id===sh.approval):undefined;
  const cashIn=ex.payments+ex.deposits+ex.other,cashOut=ex.refunds+ex.returns+ex.expenses;
  return <div className="mm-page">
    <section className="mm-hero mm-hero-cash" aria-label="Expected cash">
      <div className="mm-hero-main"><span>Expected cash in drawer</span><strong className="number">{money(ex.expected)}</strong></div>
      <div className="mm-hero-status"><Pill tone={st==='Open'?'green':st==='Submitted'?'orange':'neutral'}>{st==='Open'?'Shift open':st==='Submitted'?'Awaiting approval':'Shift closed'}</Pill><span>{dateLabel(s.date)}</span></div>
      <div className="mm-hero-sub"><div><span>Opening float</span><strong className="number">{money(ex.opening)}</strong></div><div><span>Cash in</span><strong className="number">+{money(cashIn)}</strong></div><div><span>Cash out</span><strong className="number">−{money(cashOut)}</strong></div></div>
      <p className="mm-formula">{money(ex.opening)} + {money(cashIn)} − {money(cashOut)} = {money(ex.expected)}. UPI, card, bank and vendor credit are excluded.</p>
    </section>

    {st==='Submitted'&&sh&&<section className="mm-card mm-diffcard" aria-label="Difference approval">
      <h2 className="mm-h">Difference approval</h2>
      <div className="mm-row"><div className="mm-row-main"><strong>Expected</strong></div><div className="mm-row-amt"><strong className="number">{money(sh.expected)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Counted</strong></div><div className="mm-row-amt"><strong className="number">{money(sh.counted)}</strong></div></div>
      <div className="mm-row"><div className="mm-row-main"><strong>Difference</strong></div><div className="mm-row-amt"><strong className="number mm-bad">{diffText(sh.counted-sh.expected)}</strong></div></div>
      <p className="mm-meta">Reason: {sh.reason} · by {name(sh.submittedBy)} · {a?.id} {a?.status}</p>
      {sh.submittedBy===uid?<div className="info-note"><ShieldCheck size={16}/>You submitted this count. Another authorised user decides (no self-approval). Cash receipts are paused until then.</div>
        :!permission('approve')?<div className="info-note"><ShieldCheck size={16}/>A manager, accountant or owner decides this difference.</div>
        :(()=>{const over=!closingUnlimitedRoles.includes(user.role)&&Math.abs(sh.counted-sh.expected)>SHIFT_MANAGER_DIFF_LIMIT;
          return <div className="form mm-form"><label>Note (required to reject for recount)<textarea maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Recount the ₹100 notes"/></label>
            {over&&<p className="mm-why">Above {money(SHIFT_MANAGER_DIFF_LIMIT)} — the Owner must approve. You may still reject for a recount.</p>}
            <div className="mm-two"><button className="btn" disabled={busy||!a} onClick={()=>run({type:'SHIFT_DIFFERENCE_DECIDE',id:a!.id,approve:false,reason:reason.trim()||undefined,key:'shiftdiff:'+a!.id},'Difference rejected — shift reopened for recount')}><X size={16}/> Reject · recount</button>
              <button className="btn primary" disabled={busy||over||!a} onClick={()=>run({type:'SHIFT_DIFFERENCE_DECIDE',id:a!.id,approve:true,key:'shiftdiff:'+a!.id},'Difference accepted — shift closed')}><Check size={16}/> Accept & close</button></div></div>;})()}
    </section>}

    {st==='Closed'&&<section className="mm-card" aria-label="Shift result">
      <h2 className="mm-h">Shift result</h2>
      {s.shiftClose?<>
        <div className="mm-row"><div className="mm-row-main"><strong>Counted</strong></div><div className="mm-row-amt"><strong className="number">{money(s.shiftClose.counted)}</strong></div></div>
        <div className="mm-row"><div className="mm-row-main"><strong>Difference</strong></div><div className="mm-row-amt"><strong className="number">{diffText(s.shiftClose.counted-s.shiftClose.expected)}</strong></div></div>
        <div className="mm-row"><div className="mm-row-main"><strong>Next opening float</strong></div><div className="mm-row-amt"><strong className="number">{money(s.shiftClose.counted)}</strong></div></div></>
        :<p className="mm-meta">{closed?'Business day closed.':'No count recorded for this date.'}</p>}
      {permission('close')&&<button className="btn full" onClick={()=>go('closing')}>Day closing <ArrowRight size={16}/></button>}
    </section>}

    <h2 className="m-group">TODAY’S CASH MOVEMENTS · {mv.length}</h2>
    {mv.map(m=><ListCard key={m.id} title={m.label} sub={`${m.id} · ${m.reference}`} meta={`${m.guest?m.guest+' · ':''}by ${name(m.actor)}`}
      onClick={m.booking&&(permission('billing')||permission('reservations'))?()=>booking(m.booking!):undefined}
      right={<strong className={'number '+(m.amount<0?'mm-out':'mm-in')}>{m.amount>=0?'+':'−'}{money(Math.abs(m.amount))}</strong>}/>)}
    {!mv.length&&<Empty title="No cash movements today" body="Cash receipts, deposits, returns, refunds and posted cash expenses appear here."/>}

    {st==='Open'&&!closed&&permission('payments')&&<StickyFooter><button className="btn primary" onClick={()=>open({type:'mm-count'})}><Calculator size={18}/> Count drawer</button></StickyFooter>}
  </div>;
}

/** 'mm-count' sheet: denomination steppers (or a total), live difference, reason step when it differs, then SHIFT_SUBMIT. */
export function MmCountSheet(){
  const {s,run,closeModal,modal,busy,permission}=useHcc();
  const [mode,setMode]=useState<'notes'|'total'>('notes');
  const [notes,setNotes]=useState<Record<number,number>>({});
  const [coins,setCoins]=useState('');
  const [total,setTotal]=useState('');
  const [step,setStep]=useState<0|1>(0);
  const [reason,setReason]=useState('');
  const ex=shiftExpected(s),st=shiftStatus(s);
  if(!permission('payments'))return <Empty title="Not available" body="Counting the drawer needs the payments permission (cashier or reception)."/>;
  if(st!=='Open')return <><div className="modal-heading"><h2>Count the drawer</h2></div><Empty title={st==='Submitted'?'A count is awaiting approval':'The shift is already closed'}/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const den=mmDenominations(notes,coins);
  const coinsBad=coins.trim()!==''&&!Number.isFinite(mmPaise(coins));
  const counted=mode==='notes'?mmCountTotal(den):mmPaise(total);
  const entered=mode==='notes'?(Object.values(notes).some(v=>v>0)||(coins.trim()!==''&&!coinsBad)):Number.isFinite(counted);
  const diff=entered?counted-ex.expected:0;
  const submit=()=>run(mmShiftSubmitCommand(counted,reason,mode==='notes'?den:undefined,modal!.key),
    diff===0?'Cash matches exactly — shift closed':'Count submitted. The difference now needs approval by another user.',{close:true});
  if(step===1)return <><div className="modal-heading"><Stepper steps={['Count','Reason']} current={1}/><h2>Reason for the difference</h2><p>Another authorised user approves it. Cash receipts are paused until it is decided.</p></div>
    <div className="mm-diffbar bad" role="status"><div><span>Expected</span><strong className="number">{money(ex.expected)}</strong></div><div><span>Counted</span><strong className="number">{money(counted)}</strong></div><div><span>Difference</span><strong className="number">{diffText(diff)}</strong></div></div>
    <form className="form mm-form" onSubmit={e=>{e.preventDefault();if(reason.trim())submit();}}>
      <label>Reason (required)<textarea required maxLength={200} enterKeyHint="send" value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. ₹100 note given as change twice"/></label>
      <div className="info-note"><Info size={16}/>A manager can accept up to {money(SHIFT_MANAGER_DIFF_LIMIT)}; above that, the Owner. Rejecting reopens the shift for a recount.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>setStep(0)}><ArrowLeft size={16}/> Recount</button>
        <button className="btn primary" disabled={busy||!reason.trim()}><Send size={16}/> Request approval</button></div></form></>;
  return <><div className="modal-heading"><Stepper steps={['Count','Reason']} current={0}/><h2>Count the drawer</h2><p>Expected cash {money(ex.expected)} · {dateLabel(s.date)}</p></div>
    <div className="mm-seg" role="group" aria-label="Count method">
      <button type="button" aria-pressed={mode==='notes'} className={mode==='notes'?'on':''} onClick={()=>setMode('notes')}>Denominations</button>
      <button type="button" aria-pressed={mode==='total'} className={mode==='total'?'on':''} onClick={()=>setMode('total')}>Total</button></div>
    <form className="form mm-form" aria-label="Cash count" onSubmit={e=>{e.preventDefault();if(!entered||coinsBad)return;if(diff===0)submit();else setStep(1);}}>
      {mode==='notes'?<div className="mm-den">{SHIFT_DENOMINATIONS.map(n=><div key={n} className="mm-den-row"><NumberStepper label={`₹${n} notes`} value={notes[n]||0} onChange={v=>setNotes({...notes,[n]:v})}/><small className="number">{money((notes[n]||0)*n*100)}</small></div>)}
          <label>Coins (₹ total)<input inputMode="decimal" enterKeyHint="done" value={coins} onChange={e=>setCoins(e.target.value)} placeholder="0"/></label>
          {coinsBad&&<p className="field-error">Enter coins as a rupee amount.</p>}</div>
        :<label className="mm-amount">Physical cash counted (₹)<input inputMode="decimal" enterKeyHint="done" value={total} onChange={e=>setTotal(e.target.value)} placeholder="0"/></label>}
      <div className={'mm-diffbar '+(!entered?'':diff===0?'ok':'bad')} role="status" aria-live="polite">
        <div><span>Expected</span><strong className="number">{money(ex.expected)}</strong></div>
        <div><span>Counted</span><strong className="number">{entered?money(counted):'—'}</strong></div>
        <div><span>Difference</span><strong className="number">{entered?diffText(diff):'—'}</strong></div></div>
      {!entered&&<p className="mm-why">Enter the notes counted to see the difference.</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||!entered||coinsBad}>{!entered||diff===0?<><Send size={16}/> Submit & close shift</>:<>Next: reason <ArrowRight size={16}/></>}</button></div>
    </form></>;
}
