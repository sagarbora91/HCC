// SCR-11 Cashier shift: opening float, today's cash movements, expected-cash formula, count, difference approval.
import React,{useState} from 'react';
import {Wallet,ArrowRight,ShieldCheck,Check,X} from 'lucide-react';
import {money,shiftExpected,shiftMovements,shiftStatus,SHIFT_MANAGER_DIFF_LIMIT,closingUnlimitedRoles,isDayClosed} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel} from '../../components/ui';
import {ShiftCountForm} from './ShiftCount';
import {OpenShiftPanel} from '../../clients/sync/cashier-ui';

export function Cashier(){
  const {s,go,booking,permission}=useHcc();
  const ex=shiftExpected(s),mv=shiftMovements(s),st=shiftStatus(s),closed=isDayClosed(s);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
  const row=(label:string,v:number,sign:'+'|'−'|'')=><div><span>{sign&&<b className="f-sign">{sign}</b>}{label}</span><strong>{money(v)}</strong></div>;
  return <><PageTitle title="Cashier shift" sub={`One cash shift per business day · ${dateLabel(s.date)} · opening float carried from the last counted close`} action={<button className="btn" onClick={()=>go('closing')}>Day closing <ArrowRight size={15}/></button>}/>
    <div className="closing-banner"><div><div className="eyebrow">SHIFT STATUS</div><h2>{st==='Open'?'Shift open':st==='Submitted'?'Count submitted — awaiting approval':'Shift closed'}</h2><p>{st==='Open'?'Cash receipts are accepted.':st==='Submitted'?'Cash receipts and cash expenses are paused until the difference is decided.':closed?'Business day closed.':'Cash receipts are closed for today.'}</p></div>
      <Pill tone={st==='Open'?'green':st==='Submitted'?'orange':'neutral'}>{st}</Pill></div>
    <div className="two-col">
      <section className="card"><div className="section-title"><h2>Expected cash</h2><Wallet size={19}/></div>
        <div className="summary-list padded f-formula">
          {row('Opening float',ex.opening,'')}
          {row('Cash payments / advances',ex.payments,'+')}
          {row('Cash refundable deposits',ex.deposits,'+')}
          {row('Restaurant / room-service cash (net of voids)',ex.restaurant,'+')}
          {ex.other!==0&&row('Other cash movements',ex.other,'+')}
          {row('Cash payment refunds',ex.refunds,'−')}
          {row('Cash deposit returns',ex.returns,'−')}
          {row('Drawer-cash expenses (net of reversals)',ex.expenses,'−')}
          <div className="f-total"><span>= Expected cash in drawer</span><strong>{money(ex.expected)}</strong></div>
        </div>
        <p className="footnote padded">{money(ex.opening)} + {money(ex.payments)} + {money(ex.deposits)} + {money(ex.restaurant)} − {money(ex.refunds)} − {money(ex.returns)} − {money(ex.expenses)} = {money(ex.expected)}. Restaurant and room-service cash bills go into this drawer (one shared till); UPI, card, bank, vendor-credit amounts and room charges to a folio are excluded.</p>
      </section>
      <section className="card"><div className="section-title"><h2>{st==='Open'?'Count the drawer':st==='Submitted'?'Difference approval':'Shift result'}</h2></div>
        <div className="padded">{st==='Open'?(closed?<Empty title="Business day closed" body="Open the next business day to start a new shift."/>:<ShiftCountForm/>):st==='Submitted'?<DifferencePanel/>:<><ClosedPanel name={name}/>{process.env.HCC_BUILD==='production'&&!closed?<OpenShiftPanel/>:null}</>}</div>
      </section>
    </div>
    <section className="card"><div className="section-title"><h2>Today’s cash movements</h2><span className="muted">{mv.length} movement{mv.length===1?'':'s'} · cash only</span></div>
      <div className="table-wrap"><table><thead><tr><th>Reference</th><th>Movement</th><th>Guest / detail</th><th>Recorded by</th><th className="number">Drawer effect</th></tr></thead><tbody>
        {mv.map(m=><tr key={m.id}><td><strong>{m.id}</strong><small>{m.reference}</small></td><td>{m.label}</td>
          <td>{m.booking?(permission('billing')||permission('reservations')?<button className="text-btn" onClick={()=>booking(m.booking!)}>{m.guest} · {m.booking}</button>:<span>{m.booking}</span>):m.order?(permission('billing')?<button className="text-btn" onClick={()=>go('pos')}>{m.guest??'Outlet bill'} · {m.order}</button>:<span>{m.order}</span>):<span className="muted">{m.kind==='expense-reversal'?'Reversal':'Expense'}</span>}</td>
          <td>{name(m.actor)}</td><td className="number">{m.amount>=0?'+':'−'}{money(Math.abs(m.amount))}</td></tr>)}
      </tbody></table>{!mv.length&&<Empty title="No cash movements today" body="Cash receipts, deposits, returns, refunds, restaurant cash bills and posted cash expenses appear here."/>}</div></section></>;
}

function DifferencePanel(){
  const {s,uid,user,run,busy,permission}=useHcc();const [reason,setReason]=useState('');
  const sh=s.shift!,a=s.approvals.find(a=>a.id===sh.approval),diff=sh.counted-sh.expected;
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name??id;
  const overLimit=!closingUnlimitedRoles.includes(user.role)&&Math.abs(diff)>SHIFT_MANAGER_DIFF_LIMIT;
  const own=sh.submittedBy===uid,canDecide=permission('approve')&&!own;
  return <div>
    <div className="summary-list"><div><span>Expected</span><strong>{money(sh.expected)}</strong></div><div><span>Counted</span><strong>{money(sh.counted)}</strong></div>
      <div><span>Difference</span><strong>{diff>0?'Over ':'Short '}{money(Math.abs(diff))}</strong></div><div><span>Reason</span><strong>{sh.reason}</strong></div>
      <div><span>Submitted by</span><strong>{name(sh.submittedBy)}</strong></div><div><span>Request</span><strong>{a?.id} · {a?.status}</strong></div></div>
    {own&&<div className="info-note"><ShieldCheck size={16}/>You submitted this count. Another authorised user must decide (no self-approval).</div>}
    {!own&&!permission('approve')&&<div className="info-note"><ShieldCheck size={16}/>A manager, accountant or owner decides this difference. Switch user in the demo toolbar.</div>}
    {canDecide&&<><label className="form f-mt">Reason / note for rejection (optional for approval)<textarea maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Recount the ₹100 notes"/></label>
      {overLimit&&<p className="f-why">Above the manager limit of {money(SHIFT_MANAGER_DIFF_LIMIT)} — the Owner must approve. You may still reject for a recount.</p>}
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={()=>run({type:'SHIFT_DIFFERENCE_DECIDE',id:a!.id,approve:false,reason:reason.trim()||undefined,key:'shiftdiff:'+a!.id},'Difference rejected — shift reopened for recount')}><X size={15}/> Reject · recount</button>
        <button className="btn primary" disabled={busy||overLimit} onClick={()=>run({type:'SHIFT_DIFFERENCE_DECIDE',id:a!.id,approve:true,key:'shiftdiff:'+a!.id},'Difference accepted — shift closed')}><Check size={15}/> Accept difference & close</button></div></>}
  </div>;
}
function ClosedPanel({name}:{name:(id?:string)=>string}){
  const {s,go}=useHcc();const c=s.shiftClose;
  if(!c)return <Empty title="Shift closed" body="No count is recorded for this date."/>;
  const diff=c.counted-c.expected;
  return <div><div className="summary-list"><div><span>Expected</span><strong>{money(c.expected)}</strong></div><div><span>Counted</span><strong>{money(c.counted)}</strong></div>
    <div><span>Difference recorded</span><strong>{diff===0?'None · exact':(diff>0?'Over ':'Short ')+money(Math.abs(diff))}</strong></div>
    {s.shift?.decidedBy&&<div><span>Accepted by</span><strong>{name(s.shift.decidedBy)}</strong></div>}
    <div><span>Next opening float</span><strong>{money(c.counted)}</strong></div></div>
    <div className="modal-actions"><button className="btn" onClick={()=>go('closing')}>Continue to day closing <ArrowRight size={15}/></button></div></div>;
}
