// SCR-11 Cashier shift: opening float, today's cash movements, expected-cash formula, count, difference approval.
import React,{useState} from 'react';
import {bookingRefOf} from '../../domain/bookingRef';
import {Wallet,ArrowRight,ShieldCheck,Check,X} from 'lucide-react';
import {money,shiftExpected,shiftMovements,shiftStatus,SHIFT_MANAGER_DIFF_LIMIT,closingUnlimitedRoles,isDayClosed,cxSlice,cxPending,udGuestOutstanding} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel} from '../../components/ui';
import {FitPage,ScrollPanel,StepFlow,useFitTab} from '../../components/fit';
import {ShiftCountForm} from './ShiftCount';
import {OpenShiftForm,CashOutPanel,CashOutActions} from './CashOut';
import {CreditGuests} from '../lfmoney/GuestMoney'; // LF-UI B2

/** Detail column of a drawer movement without a booking or outlet bill (Wave B1: banking, transfers, drawings). */
const MOVE_KIND:Record<string,string>={'expense-reversal':'Reversal',expense:'Expense',banking:'Bank deposit','transfer-out':'Till transfer','transfer-in':'Till transfer','owner-drawing':'Owner drawing','staff-advance':'Salary advance'}; // ACC-2 integration: the parity lane's cash advance movement

/** Step ids = SUB_SCREENS 'cashier' (kebab-case of the labels). */
export const CASHIER_STEPS=['expected-cash','count-and-result','cash-movements','cash-out','credit-guests'] as const;
type CashierStep=typeof CASHIER_STEPS[number];

export function Cashier(){
  const {s,go,booking,permission}=useHcc();
  const ex=shiftExpected(s),mv=shiftMovements(s),st=shiftStatus(s),closed=isDayClosed(s);
  const name=(id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
  const row=(label:string,v:number,sign:'+'|'−'|'')=><div><span>{sign&&<b className="f-sign">{sign}</b>}{label}</span><strong>{money(v)}</strong></div>;
  const opener=cxSlice(s).opener,mine=opener&&opener.date===s.date?opener:undefined; // DEC-078: whose shift this is
  // FIT (DEC-194, contract §6 'cashier' = split): left, the shift summary — status banner + expected-cash formula — and the count /
  // difference / result card (its committing buttons stay in a sticky action bar); right, today's cash movements and the cash leaving the drawer.
  // The cash-out buttons and "Day closing" sit in the page header (§11.9: one row, the rest in More). Same content, same handlers.
  const banner=<div className="closing-banner"><div><div className="eyebrow">SHIFT STATUS</div><h2>{st==='Open'?'Shift open':st==='Submitted'?'Count submitted — awaiting approval':'Shift closed'}</h2><p>{st==='Open'?'Cash receipts are accepted.':st==='Submitted'?'Cash receipts and cash expenses are paused until the difference is decided.':closed?'Business day closed.':'Cash receipts are closed for today.'}</p>
      {st==='Open'&&mine&&<p>Opened by <strong>{name(mine.by)}</strong>{mine.handover!==undefined?<> · handover count {money(mine.handover)}{mine.difference?<> · <strong>{mine.difference>0?'over':'short'} {money(Math.abs(mine.difference))}</strong>{mine.reason?` (${mine.reason})`:''}</>:' · matches the carried float'}</>:null}</p>}</div>
      <Pill tone={st==='Open'?'green':st==='Submitted'?'orange':'neutral'}>{st}</Pill></div>;
  // FIT-2 (contract §12, owner rule 2 Nov 2026): the page is a task, so it is four sub-screens that each fit - expected cash, the count / result,
  // today's movements, cash leaving the drawer - and a fixed rail beside them shows every step's result (read-only, from the state above).
  const net=mv.reduce((a,m)=>a+m.amount,0),cx=cxSlice(s),pend=cxPending(s).length;
  const outToday=cx.records.filter(r=>r.status==='Pending'||r.date===s.date||r.decidedDate===s.date).length;
  const sh=s.shift?.date===s.date?s.shift:undefined,subDiff=st==='Submitted'&&sh?sh.counted-sh.expected:0,closeDiff=s.shiftClose?s.shiftClose.counted-s.shiftClose.expected:0;
  const diffText=(d:number)=>d===0?'exact':(d>0?'over ':'short ')+money(Math.abs(d));
  const needOpen=st==='Closed'&&!closed&&!s.shiftClose;
  const first:CashierStep=st==='Closed'&&!needOpen?'expected-cash':'count-and-result';
  const [step,setStep]=useFitTab<CashierStep>('cashier',CASHIER_STEPS,first);
  const steps=[
    {id:'expected-cash',label:'Expected cash',status:'done' as const,summary:`${st} · expected ${money(ex.expected)} · float ${money(ex.opening)}`},
    {id:'count-and-result',label:'Count & result',status:st==='Open'||needOpen?'current' as const:st==='Submitted'?'blocked' as const:'done' as const,
      summary:st==='Open'?`Not counted yet · expected ${money(ex.expected)}`:st==='Submitted'?`Counted ${money(sh?.counted??0)} · ${diffText(subDiff)} · awaiting approval`
        :s.shiftClose?`Counted ${money(s.shiftClose.counted)} · ${diffText(closeDiff)}`:'Shift closed · no count recorded'},
    {id:'cash-movements',label:'Cash movements',status:'done' as const,summary:`${mv.length} movement${mv.length===1?'':'s'} · net ${net<0?'−':'+'}${money(Math.abs(net))}`},
    {id:'cash-out',label:'Cash out',status:pend?'current' as const:'done' as const,summary:pend?`${pend} awaiting a second person`:outToday?`${outToday} record${outToday===1?'':'s'} today`:'Nothing banked or drawn today'},
    {id:'credit-guests',label:'Credit guests',status:'done' as const,summary:`${udGuestOutstanding(s).filter(r=>r.open>0).length} guest(s) owing · ${money(udGuestOutstanding(s).reduce((a,r)=>a+r.open,0))}`},
  ];
  let body:React.ReactNode;
  if(step==='expected-cash')body=<ScrollPanel title="Expected cash" label="Shift status and expected cash" actions={<Wallet size={19}/>}>
          <div className="padded cashier-status">{banner}</div>
          <div className="summary-list padded f-formula">
            {row('Opening float',ex.opening,'')}
            {row('Cash payments / advances',ex.payments,'+')}
            {row('Cash refundable deposits',ex.deposits,'+')}
            {row('Restaurant / room-service cash (net of voids)',ex.restaurant,'+')}
            {ex.other!==0&&row('Other cash movements',ex.other,'+')}
            {ex.transfers!==0&&row('Bank deposits, till transfers and Owner drawings (net)',Math.abs(ex.transfers),ex.transfers<0?'−':'+')}
            {row('Cash payment refunds',ex.refunds,'−')}
            {row('Cash deposit returns',ex.returns,'−')}
            {row('Drawer-cash expenses (net of reversals)',ex.expenses,'−')}
            <div className="f-total"><span>= Expected cash in drawer</span><strong>{money(ex.expected)}</strong></div>
          </div>
          <p className="footnote padded">{money(ex.opening)} + {money(ex.payments)} + {money(ex.deposits)} + {money(ex.restaurant)} − {money(ex.refunds)} − {money(ex.returns)} − {money(ex.expenses)}{ex.transfers!==0?` ${ex.transfers<0?'−':'+'} ${money(Math.abs(ex.transfers))}`:''} = {money(ex.expected)}. Restaurant and room-service cash bills go into this drawer (one shared till); UPI, card, bank, vendor-credit amounts and room charges to a folio are excluded.</p>
        </ScrollPanel>;
  else if(step==='count-and-result')body=<ScrollPanel title={st==='Open'?'Count the drawer':st==='Submitted'?'Difference approval':'Shift result'} label="Cash count">
          <div className="padded cashier-shift">{st==='Open'?(closed?<Empty title="Business day closed" body="Open the next business day to start a new shift."/>:<ShiftCountForm/>):st==='Submitted'?<DifferencePanel/>:<><ClosedPanel name={name}/>{!closed&&(process.env.HCC_BUILD==='production'||!s.shiftClose)?<OpenShiftForm/>:null}</>}</div>
        </ScrollPanel>;
  else if(step==='cash-movements')body=<ScrollPanel title="Today’s cash movements" label="Today’s cash movements" actions={<span className="muted">{mv.length} movement{mv.length===1?'':'s'} · cash only</span>}>
          <div className="table-wrap"><table><thead><tr><th>Reference</th><th>Movement</th><th>Guest / detail</th><th>Recorded by</th><th className="number">Drawer effect</th></tr></thead><tbody>
            {mv.map(m=><tr key={m.id}><td><strong>{m.id}</strong><small>{m.reference}</small></td><td>{m.label}</td>
              <td>{m.booking?(permission('billing')||permission('reservations')?<button className="text-btn" onClick={()=>booking(m.booking!)}>{m.guest} · {bookingRefOf(s.bookings,m.booking)}</button>:<span>{bookingRefOf(s.bookings,m.booking)}</span>):m.order?(permission('billing')?<button className="text-btn" onClick={()=>go('pos')}>{m.guest??'Outlet bill'} · {m.order}</button>:<span>{m.order}</span>):<span className="muted">{MOVE_KIND[m.kind]??'Expense'}</span>}</td>
              <td>{name(m.actor)}</td><td className="number">{m.amount>=0?'+':'−'}{money(Math.abs(m.amount))}</td></tr>)}
          </tbody></table>{!mv.length&&<Empty title="No cash movements today" body="Cash receipts, deposits, returns, refunds, restaurant cash bills, posted cash expenses, bank deposits, till transfers and Owner drawings appear here."/>}</div>
        </ScrollPanel>;
  else if(step==='credit-guests')body=<ScrollPanel title="Credit guests" label="Credit guests"><CreditGuests/></ScrollPanel>;
  else body=<CashOutPanel fit/>;
  return <FitPage title="Cashier shift" sub={`One cash shift per business day · ${dateLabel(s.date)} · opening float carried from the last counted close`} layout="steps"
      actions={<><CashOutActions/><button className="btn" onClick={()=>go('closing')}>Day closing <ArrowRight size={15}/></button></>}>
    <StepFlow value={step} onChange={id=>setStep(id as CashierStep)} steps={steps}>{body}</StepFlow>
  </FitPage>;
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
