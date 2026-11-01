// SCR-14 Day closing as a step list: Preflight → Post room nights → Cash shift → Close → Closing summary → Open next day.
// FIT (DEC-194…196, contract §6 'closing' = steps): a StepFlow over seven steps (the six above + "Reopen & corrections"); one step body at a
// time, each in a ScrollPanel whose footer holds the step's buttons. The committing action ("Close <date>" while the day is open, "Open next
// business day" once it is closed) sits in the page header, always visible, with exactly the enabling rule it had in step 4 / step 6.
import React from 'react';
import {ArrowUpRight,ArrowRight,Printer,Zap,AlertTriangle} from 'lucide-react';
import {money,addDays,isDayClosed,closingPreflight,closingNightTargets,closingHasNight,closingSummary,shiftStatus,shiftExpected,closingReopened} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {FitPage,StepFlow,ScrollPanel,FitNote,useFitTab} from '../../components/fit';
import {ClosingSummaryView} from './ClosingReport';
import {Corrections} from './Corrections';
import {ReopenDay} from './ReopenDay';
import {usePaged,ShowMore} from '../../components/ShowMore';
import {DayPackButton} from '../exports/DayPack';

type StepState='done'|'current'|'blocked'|'waiting';
/** Step ids = SUB_SCREENS 'closing' (kebab-case of the labels). */
export const CLOSING_STEPS=['preflight','post-room-nights','cash-shift','close-business-date','closing-summary','open-next-business-day','reopen-and-corrections'] as const;
type StepId=typeof CLOSING_STEPS[number];
const STEP_LABEL:Record<StepId,string>={'preflight':'Preflight','post-room-nights':'Post room nights','cash-shift':'Cash shift','close-business-date':'Close business date',
  'closing-summary':'Closing summary','open-next-business-day':'Open next business day','reopen-and-corrections':'Reopen & corrections'};
const STATE_LABEL={done:'Complete',current:'To do',blocked:'Blocked',waiting:'Waiting'} as const;
const statePill=(state:StepState)=><Pill tone={state==='done'?'green':state==='blocked'?'orange':state==='current'?'blue':'neutral'}>{STATE_LABEL[state]}</Pill>;

export function DayClosing(){
  const {s,user,run,go,open,booking,busy,permission}=useHcc();
  const closed=isDayClosed(s),pf=closingPreflight(s),st=shiftStatus(s);
  const general=pf.filter(x=>x.id!=='nights'&&x.id!=='shift'),generalOpen=general.filter(x=>x.items.length);
  const targets=closingNightTargets(s),toPost=targets.filter(b=>!closingHasNight(b,s.date));
  const prog=s.closeProgress?.date===s.date?s.closeProgress:undefined;
  const blockers=pf.filter(x=>x.items.length);
  const canClose=permission('close');
  const rec=s.closings?.[s.date];
  const reopened=closingReopened(s);
  const corrWaiting=s.approvals.filter(a=>a.kind==='closed-date-correction'&&a.status==='Pending').length;
  const pastClosings=Object.keys(s.closings??{}).filter(d=>d!==s.date).sort().reverse();
  const pgClose=usePaged(pastClosings,31);
  const drill=(it:{booking?:string;page?:string})=>it.booking?booking(it.booking):it.page&&go(it.page);
  // Step states: exactly the states the old step cards showed (step 7 is new: blocked while a reopened day waits to be closed again).
  const state:Record<StepId,StepState>={
    'preflight':closed||!generalOpen.length?'done':'blocked',
    'post-room-nights':closed||(!toPost.length)?'done':prog?.interrupted?'blocked':'current',
    'cash-shift':st==='Closed'?'done':st==='Submitted'?'blocked':'current',
    'close-business-date':closed?'done':blockers.length?'waiting':'current',
    'closing-summary':rec?'done':'waiting',
    'open-next-business-day':closed?'current':'waiting',
    'reopen-and-corrections':reopened?'blocked':corrWaiting?'current':'waiting',
  };
  const note:Partial<Record<StepId,string>>={
    'preflight':!closed&&generalOpen.length?`${generalOpen.reduce((n,c)=>n+c.items.length,0)} to resolve`:undefined,
    'post-room-nights':!closed&&toPost.length?`${toPost.length} to post`:undefined,
    'close-business-date':!closed&&blockers.length?`${blockers.length} blocking`:undefined,
    'reopen-and-corrections':reopened?`${shortDate(reopened.date)} reopened`:corrWaiting?`${corrWaiting} waiting`:undefined,
  };
  // Default step: a reopened day first (today cannot close until it is closed again), else the first step not done.
  const first:StepId=reopened?'reopen-and-corrections':CLOSING_STEPS.find(id=>state[id]!=='done')??'closing-summary';
  const [step,setStep]=useFitTab<StepId>('closing',CLOSING_STEPS,first);

  // Header: the committing action first (enabled exactly as before), then the report.
  const closeBtn=<button className="btn primary" disabled={busy||!canClose||!!blockers.length} onClick={()=>run({type:'DAY_CLOSE',key:'dayclose:'+s.date},'Business day closed. Closing record saved.')}>Close {shortDate(s.date)} <ArrowRight size={15}/></button>;
  const nextBtn=<button className="btn primary" disabled={busy||!closed||!canClose} onClick={()=>run({type:'NEXT_DAY',key:'nextday:'+s.date},'Next business day opened')}>Open next business day <ArrowRight size={16}/></button>;
  const reportBtn=<button className="btn" onClick={()=>open({type:'closing-report',id:s.date})}><Printer size={15}/> {closed?'Closing report':'Preview report'}</button>;

  const panel=(id:StepId,body:React.ReactNode,footer?:React.ReactNode)=><ScrollPanel title={`${CLOSING_STEPS.indexOf(id)+1}. ${STEP_LABEL[id]}`} label={`Step ${CLOSING_STEPS.indexOf(id)+1}: ${STEP_LABEL[id]}`}
    actions={statePill(state[id])} footer={footer}><div className="f-step-body f-fit-step">{body}</div></ScrollPanel>;
  const actionsBar=(children:React.ReactNode)=><div className="card-footer f-actions">{children}</div>;

  let body:React.ReactNode;
  if(step==='preflight')body=panel(step,closed?<p className="muted">Completed before close.</p>:<ul className="f-checks">{general.map(c=><li key={c.id} className={c.items.length?'bad':'ok'}>
      <div><strong>{c.items.length?'✗':'✓'} {c.title}</strong><small>{c.items.length?`${c.items.length} to resolve · ${c.hint}`:'All clear'}</small>
        {!!c.items.length&&<ul>{c.items.map((it,i)=><li key={i}><button className="text-btn" onClick={()=>drill(it)}>{it.label} <ArrowUpRight size={13}/></button></li>)}</ul>}</div>
      {c.page&&!!c.items.length&&<button className="btn small-btn" onClick={()=>go(c.page!)}>Open</button>}</li>)}</ul>);
  else if(step==='post-room-nights')body=closed?panel(step,<p className="muted">Nights posted for {shortDate(s.date)}.</p>,
      canClose?actionsBar(<button className="btn subtle" disabled={busy} title="Demonstrates that a closed date cannot be posted again" onClick={()=>run({type:'CLOSE_RUN',key:'closerun-rerun:'+s.date+':'+s.sequence})}>Try posting again</button>):undefined)
    :panel(step,<>
      <p>{targets.length} in-house stay{targets.length===1?'':'s'} tonight · <strong>{toPost.length}</strong> to post · {targets.length-toPost.length} already posted.</p>
      {prog?.interrupted&&<div className="info-note f-warn"><AlertTriangle size={16}/>Night audit was interrupted after {prog.posted.length} of {prog.total} stays. Resume posts only the remaining {toPost.length}; nothing is posted twice.</div>}
      <div className="table-wrap"><table><thead><tr><th>Room</th><th>Guest / booking</th><th className="number">Night</th><th>Status</th></tr></thead><tbody>
        {targets.map(b=>{const done=closingHasNight(b,s.date);return <tr key={b.id}><td><span className="room-number">{b.room}</span></td><td><button className="text-btn" onClick={()=>booking(b.id)}>{b.guest}</button><small>{b.id}</small></td><td className="number">{money(b.rate)}</td><td><Pill tone={done?'green':'orange'}>{done?'Posted':'To post'}</Pill></td></tr>;})}
      </tbody></table>{!targets.length&&<Empty title="No in-house stays tonight" body="Nothing to post."/>}</div></>,
      actionsBar(<>
        <button className="btn primary" disabled={busy||!canClose||!toPost.length} onClick={()=>run({type:'CLOSE_RUN'},prog?.interrupted?'Night audit resumed — remaining nights posted once':'Room nights posted once for '+shortDate(s.date))}>{prog?.interrupted?'Resume posting':'Post room nights'} ({toPost.length})</button>
        {process.env.HCC_BUILD==='production'?null:<button className="btn subtle" disabled={busy||!canClose||toPost.length<2} title="Demo tool: stops the run part-way" onClick={()=>run({type:'CLOSE_RUN',interruptAfter:Math.min(3,toPost.length-1)},'Simulated interruption — progress saved. Resume to continue.')}><Zap size={14}/> Demo: interrupt after {Math.max(1,Math.min(3,toPost.length-1))}</button>}
        {(!canClose||!toPost.length)&&<span className="f-why">{!canClose?'Needs close permission.':'All nights posted — rerunning posts nothing.'}</span>}</>));
  else if(step==='cash-shift')body=panel(step,<div className="summary-list"><div><span>Status</span><strong>{st}</strong></div><div><span>Expected cash</span><strong>{money(s.shiftClose?.expected??shiftExpected(s).expected)}</strong></div>
      {s.shiftClose&&<><div><span>Counted</span><strong>{money(s.shiftClose.counted)}</strong></div><div><span>Difference</span><strong>{money(s.shiftClose.counted-s.shiftClose.expected)}</strong></div></>}</div>,
    actionsBar(<><button className="btn" onClick={()=>go('cashier')}>Open cashier shift <ArrowRight size={14}/></button>
      {st==='Open'&&!closed&&<button className="btn primary" disabled={!permission('payments')} onClick={()=>open({type:'cash'})}>Count & submit</button>}
      {st==='Submitted'&&<span className="f-why">Waiting for difference approval on the cashier page.</span>}
      <button className="btn" onClick={()=>go('cashbook')}>Cash book <ArrowRight size={14}/></button></>));
  else if(step==='close-business-date')body=closed?panel(step,<p>{dateLabel(s.date)} is closed. Rerunning close or night posting is refused; totals cannot change.</p>,
      canClose?actionsBar(<button className="btn subtle" disabled={busy} title="Demonstrates that a closed date cannot be closed twice" onClick={()=>run({type:'DAY_CLOSE',key:'dayclose-rerun:'+s.date+':'+s.sequence})}>Try closing again</button>):undefined)
    :panel(step,<>{blockers.length?<p className="f-why">{blockers.length} check{blockers.length===1?'':'s'} still blocking: {blockers.map(b=>b.title.toLowerCase()).join(', ')}.</p>:<p>All checks clear. Closing stores one immutable closing record for {shortDate(s.date)}.</p>}
      <p className="muted">Close {shortDate(s.date)} is the first button in the page header; it stays there on every step.</p></>);
  else if(step==='closing-summary')body=<div className="fit-cols f-fit-cols">
      <div>{panel(step,rec?<ClosingSummaryView rec={rec}/>:<><p className="muted">Preview — final when the day is closed.</p><ClosingSummaryView rec={closingSummary(s)}/></>,
        actionsBar(<><button className="btn" onClick={()=>open({type:'closing-report',id:s.date})}><Printer size={15}/> {rec?'Print closing report':'Print preview'}</button>{rec&&<DayPackButton date={s.date}/>}</>))}</div>
      <div><ScrollPanel title="Earlier closing records" label="Earlier closing records" actions={<span className="muted">Immutable · one per date</span>} footer={pastClosings.length?<ShowMore p={pgClose}/>:undefined}>
        {pastClosings.length?<div className="request-list">{pgClose.shown.map(d=>{const c=s.closings![d];return <div className="request" key={d}><div><strong>{dateLabel(d)}</strong><p>Net revenue {money(c.revenue.net)} · receipts {money(c.receipts.net)} · counted cash {money(c.cash.counted)}{c.cash.difference?` · difference ${money(c.cash.difference)}`:''}{c.replaced?.length?` · closed again (${c.replaced.length} earlier record${c.replaced.length===1?'':'s'} kept)`:''}</p></div><Pill tone={c.reopens?.some(r=>!r.reclosedAt)?'orange':'neutral'}>{c.reopens?.some(r=>!r.reclosedAt)?'Reopened':c.replaced?.length?'Closed again':'Closed'}</Pill><button className="btn small-btn" onClick={()=>open({type:'closing-report',id:d})}>View / print</button></div>;})}</div>
          :<Empty title="No earlier closing records" body={s.closedDates.filter(d=>d!==s.date).length?'Dates closed before this prototype kept records (e.g. 23 Oct) have no stored summary.':'Close a business day to create its record.'}/>}
      </ScrollPanel></div></div>;
  else if(step==='open-next-business-day')body=panel(step,<><p>{closed?`Opens ${dateLabel(addDays(s.date,1))} at 08:00 with an opening float of ${money(s.shiftClose?.counted??0)} (counted cash).`:'Available after the day is closed.'}</p>
      {closed?<p className="muted">Open next business day is the first button in the page header.</p>:<span className="f-why">Close today first.</span>}</>);
  // ReopenDay renders the Owner's reopen form or the reopened day's panel (everyone), else nothing: then Corrections takes the whole body.
  else body=reopened||user.role==='Owner'?<div className="fit-cols f-fit-even"><div><ReopenDay/></div><div><Corrections/></div></div>:<Corrections/>;

  return <FitPage title="Day closing" sub="Finish the day with confidence: resolve exceptions, post room nights, reconcile cash, then close." layout="steps"
      actions={<>{closed?nextBtn:closeBtn}{reportBtn}</>}
      toolbar={<><div className="closing-banner f-fit-banner"><div><div className="eyebrow">CURRENT BUSINESS DATE</div><h2>{dateLabel(s.date)}</h2><p>Asia/Kolkata · simulated clock {s.clock.slice(11)}</p></div><Pill tone={closed?'neutral':'green'}>{closed?'Day closed':'Business day open'}</Pill></div>
        {!canClose&&<FitNote text="Read-only check. You can review the preflight, the cash shift and the closing summary, and request a closed-date correction. Posting nights, closing and opening the next day need the close permission (Manager or Owner)."/>}</>}>
    <StepFlow value={step} onChange={id=>setStep(id as StepId)} steps={CLOSING_STEPS.map(id=>({id,label:STEP_LABEL[id],status:state[id],note:note[id]}))}>{body}</StepFlow>
  </FitPage>;
}
