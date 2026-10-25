// SCR-14 Day closing as a step list: Preflight → Post room nights → Cash shift → Close → Closing summary → Open next day.
import React from 'react';
import {Check,ArrowUpRight,ArrowRight,Printer,Zap,AlertTriangle} from 'lucide-react';
import {money,addDays,isDayClosed,closingPreflight,closingNightTargets,closingHasNight,closingSummary,shiftStatus,shiftExpected} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {ClosingSummaryView} from './ClosingReport';
import {Corrections} from './Corrections';
import {usePaged,ShowMore} from '../../components/ShowMore';
import {DayPackButton} from '../exports/DayPack';

type StepState='done'|'current'|'blocked'|'waiting';
function Step({n,title,state,children}:{n:number;title:string;state:StepState;children:React.ReactNode}){
  const label={done:'Complete',current:'To do',blocked:'Blocked',waiting:'Waiting'}[state];
  return <section className={'card f-step '+state} aria-label={`Step ${n}: ${title} — ${label}`}>
    <div className="f-step-head"><span className={'check-circle '+(state==='done'?'done':'')}>{state==='done'?<Check size={16}/>:n}</span><h2>{title}</h2><Pill tone={state==='done'?'green':state==='blocked'?'orange':state==='current'?'blue':'neutral'}>{label}</Pill></div>
    <div className="f-step-body">{children}</div></section>;
}
export function DayClosing(){
  const {s,run,go,open,booking,busy,permission}=useHcc();
  const closed=isDayClosed(s),pf=closingPreflight(s),st=shiftStatus(s);
  const general=pf.filter(x=>x.id!=='nights'&&x.id!=='shift'),generalOpen=general.filter(x=>x.items.length);
  const targets=closingNightTargets(s),toPost=targets.filter(b=>!closingHasNight(b,s.date));
  const prog=s.closeProgress?.date===s.date?s.closeProgress:undefined;
  const blockers=pf.filter(x=>x.items.length);
  const canClose=permission('close');
  const rec=s.closings?.[s.date];
  const pastClosings=Object.keys(s.closings??{}).filter(d=>d!==s.date).sort().reverse();
  const pgClose=usePaged(pastClosings,31);
  const drill=(it:{booking?:string;page?:string})=>it.booking?booking(it.booking):it.page&&go(it.page);
  return <><PageTitle title="Day closing" sub="Finish the day with confidence: resolve exceptions, post room nights, reconcile cash, then close."
      action={<button className="btn" onClick={()=>open({type:'closing-report',id:s.date})}><Printer size={15}/> {closed?'Closing report':'Preview report'}</button>}/>
    <div className="closing-banner"><div><div className="eyebrow">CURRENT BUSINESS DATE</div><h2>{dateLabel(s.date)}</h2><p>Asia/Kolkata · simulated clock {s.clock.slice(11)}</p></div><Pill tone={closed?'neutral':'green'}>{closed?'Day closed':'Business day open'}</Pill></div>
    {!canClose&&<div className="info-note"><AlertTriangle size={16}/>You can review the preflight. Posting nights, closing and opening the next day need the close permission (Manager or Owner).</div>}
    <div className="f-steps">
      <Step n={1} title="Preflight" state={closed||!generalOpen.length?'done':'blocked'}>
        {closed?<p className="muted">Completed before close.</p>:<ul className="f-checks">{general.map(c=><li key={c.id} className={c.items.length?'bad':'ok'}>
          <div><strong>{c.items.length?'✗':'✓'} {c.title}</strong><small>{c.items.length?`${c.items.length} to resolve · ${c.hint}`:'All clear'}</small>
            {!!c.items.length&&<ul>{c.items.map((it,i)=><li key={i}><button className="text-btn" onClick={()=>drill(it)}>{it.label} <ArrowUpRight size={13}/></button></li>)}</ul>}</div>
          {c.page&&!!c.items.length&&<button className="btn small-btn" onClick={()=>go(c.page!)}>Open</button>}</li>)}</ul>}
      </Step>
      <Step n={2} title="Post room nights" state={closed||(!toPost.length)?'done':prog?.interrupted?'blocked':'current'}>
        {closed?<><p className="muted">Nights posted for {shortDate(s.date)}.</p>
          {canClose&&<div className="card-footer f-actions"><button className="btn subtle" disabled={busy} title="Demonstrates that a closed date cannot be posted again" onClick={()=>run({type:'CLOSE_RUN',key:'closerun-rerun:'+s.date+':'+s.sequence})}>Try posting again</button></div>}</>:<>
          <p>{targets.length} in-house stay{targets.length===1?'':'s'} tonight · <strong>{toPost.length}</strong> to post · {targets.length-toPost.length} already posted.</p>
          {prog?.interrupted&&<div className="info-note f-warn"><AlertTriangle size={16}/>Night audit was interrupted after {prog.posted.length} of {prog.total} stays. Resume posts only the remaining {toPost.length}; nothing is posted twice.</div>}
          <div className="table-wrap"><table><thead><tr><th>Room</th><th>Guest / booking</th><th className="number">Night</th><th>Status</th></tr></thead><tbody>
            {targets.map(b=>{const done=closingHasNight(b,s.date);return <tr key={b.id}><td><span className="room-number">{b.room}</span></td><td><button className="text-btn" onClick={()=>booking(b.id)}>{b.guest}</button><small>{b.id}</small></td><td className="number">{money(b.rate)}</td><td><Pill tone={done?'green':'orange'}>{done?'Posted':'To post'}</Pill></td></tr>;})}
          </tbody></table>{!targets.length&&<Empty title="No in-house stays tonight" body="Nothing to post."/>}</div>
          <div className="card-footer f-actions">
            <button className="btn primary" disabled={busy||!canClose||!toPost.length} onClick={()=>run({type:'CLOSE_RUN'},prog?.interrupted?'Night audit resumed — remaining nights posted once':'Room nights posted once for '+shortDate(s.date))}>{prog?.interrupted?'Resume posting':'Post room nights'} ({toPost.length})</button>
            <button className="btn subtle" disabled={busy||!canClose||toPost.length<2} title="Demo tool: stops the run part-way" onClick={()=>run({type:'CLOSE_RUN',interruptAfter:Math.min(3,toPost.length-1)},'Simulated interruption — progress saved. Resume to continue.')}><Zap size={14}/> Demo: interrupt after {Math.max(1,Math.min(3,toPost.length-1))}</button>
            {(!canClose||!toPost.length)&&<span className="f-why">{!canClose?'Needs close permission.':'All nights posted — rerunning posts nothing.'}</span>}
          </div></>}
      </Step>
      <Step n={3} title="Cash shift" state={st==='Closed'?'done':st==='Submitted'?'blocked':'current'}>
        <div className="summary-list"><div><span>Status</span><strong>{st}</strong></div><div><span>Expected cash</span><strong>{money(s.shiftClose?.expected??shiftExpected(s).expected)}</strong></div>
          {s.shiftClose&&<><div><span>Counted</span><strong>{money(s.shiftClose.counted)}</strong></div><div><span>Difference</span><strong>{money(s.shiftClose.counted-s.shiftClose.expected)}</strong></div></>}</div>
        <div className="card-footer f-actions"><button className="btn" onClick={()=>go('cashier')}>Open cashier shift <ArrowRight size={14}/></button>
          {st==='Open'&&!closed&&<button className="btn primary" disabled={!permission('payments')} onClick={()=>open({type:'cash'})}>Count & submit</button>}
          {st==='Submitted'&&<span className="f-why">Waiting for difference approval on the cashier page.</span>}
          <button className="btn" onClick={()=>go('cashbook')}>Cash book <ArrowRight size={14}/></button></div>
      </Step>
      <Step n={4} title="Close business date" state={closed?'done':blockers.length?'waiting':'current'}>
        {closed?<><p>{dateLabel(s.date)} is closed. Rerunning close or night posting is refused; totals cannot change.</p>
          {canClose&&<div className="card-footer f-actions"><button className="btn subtle" disabled={busy} title="Demonstrates that a closed date cannot be closed twice" onClick={()=>run({type:'DAY_CLOSE',key:'dayclose-rerun:'+s.date+':'+s.sequence})}>Try closing again</button></div>}</>:<>
          {blockers.length?<p className="f-why">{blockers.length} check{blockers.length===1?'':'s'} still blocking: {blockers.map(b=>b.title.toLowerCase()).join(', ')}.</p>:<p>All checks clear. Closing stores one immutable closing record for {shortDate(s.date)}.</p>}
          <div className="card-footer f-actions"><button className="btn primary" disabled={busy||!canClose||!!blockers.length} onClick={()=>run({type:'DAY_CLOSE',key:'dayclose:'+s.date},'Business day closed. Closing record saved.')}>Close {shortDate(s.date)} <ArrowRight size={15}/></button></div></>}
      </Step>
      <Step n={5} title="Closing summary" state={rec?'done':'waiting'}>
        {rec?<ClosingSummaryView rec={rec}/>:<><p className="muted">Preview — final when the day is closed.</p><ClosingSummaryView rec={closingSummary(s)}/></>}
        <div className="card-footer f-actions"><button className="btn" onClick={()=>open({type:'closing-report',id:s.date})}><Printer size={15}/> {rec?'Print closing report':'Print preview'}</button>{rec&&<DayPackButton date={s.date}/>}</div>
      </Step>
      <Step n={6} title="Open next business day" state={closed?'current':'waiting'}>
        <p>{closed?`Opens ${dateLabel(addDays(s.date,1))} at 08:00 with an opening float of ${money(s.shiftClose?.counted??0)} (counted cash).`:'Available after the day is closed.'}</p>
        <div className="card-footer f-actions"><button className="btn primary" disabled={busy||!closed||!canClose} onClick={()=>run({type:'NEXT_DAY',key:'nextday:'+s.date},'Next business day opened')}>Open next business day <ArrowRight size={16}/></button>{!closed&&<span className="f-why">Close today first.</span>}</div>
      </Step>
    </div>
    <Corrections/>
    <section className="card"><div className="section-title"><h2>Earlier closing records</h2><span className="muted">Immutable · one per date</span></div>
      {pastClosings.length?<><div className="request-list">{pgClose.shown.map(d=>{const c=s.closings![d];return <div className="request" key={d}><div><strong>{dateLabel(d)}</strong><p>Net revenue {money(c.revenue.net)} · receipts {money(c.receipts.net)} · counted cash {money(c.cash.counted)}{c.cash.difference?` · difference ${money(c.cash.difference)}`:''}</p></div><Pill tone="neutral">Closed</Pill><button className="btn small-btn" onClick={()=>open({type:'closing-report',id:d})}>View / print</button></div>;})}</div><ShowMore p={pgClose}/></>
        :<Empty title="No earlier closing records" body={s.closedDates.filter(d=>d!==s.date).length?'Dates closed before this prototype kept records (e.g. 23 Oct) have no stored summary.':'Close a business day to create its record.'}/>}
    </section></>;
}
