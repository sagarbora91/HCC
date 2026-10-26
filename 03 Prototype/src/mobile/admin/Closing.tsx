// M18 phone Day closing: vertical stepper Preflight → Nights → Cash → Close → Summary → Next day. Every blocker is a tappable row
// that goes to where it is fixed. Same commands (CLOSE_RUN, DAY_CLOSE, NEXT_DAY) and selectors as desktop SCR-14. Print kept (DEC-047).
import {usePaged,ShowMore} from '../../components/ShowMore';
import React,{useState,useEffect} from 'react';
import {BookOpen,Check,Printer,ArrowRight,AlertTriangle,Zap,RotateCcw,ChevronDown} from 'lucide-react';
import {money,addDays,closingSummary,shiftExpected} from '../../services/mock';
import {useHcc} from '../../app/context';
import {navItems} from '../../app/nav';
import {ListCard,StickyFooter} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate} from '../../components/ui';
import {DayPackButton} from '../../features/exports/DayPack';
import {ClosingSummaryView} from '../../features/closing/ClosingReport';
import {maClosingModel,MaStep,MaStepId,MaBlocker} from './logic';
import {CashLockNote} from '../../features/cashbook/LockNote';
import {MaTag,MaRows} from './parts';

const STATE_TEXT={done:'Complete',current:'To do',blocked:'Blocked',waiting:'Waiting'} as const;
const STATE_TONE={done:'ok',current:'info',blocked:'warn',waiting:'neutral'} as const;
export function MaClosing(){
  const {s,run,go,open,booking,busy,permission}=useHcc();
  const md=maClosingModel(s);const canClose=permission('close');
  const [expanded,setExpanded]=useState<MaStepId>(md.steps[md.focus].id);
  const focusId=md.steps[md.focus].id;
  useEffect(()=>{setExpanded(focusId);},[focusId,s.date]);
  const toStep=(id:MaStepId)=>{setExpanded(id);window.setTimeout(()=>document.getElementById('ma-step-'+id)?.scrollIntoView({block:'start',behavior:'smooth'}),50);};
  const drill=(b:MaBlocker)=>b.booking?booking(b.booking):b.page?go(b.page):b.step&&toStep(b.step);
  const pastClosings=Object.keys(s.closings??{}).filter(d=>d!==s.date).sort().reverse();
  const pgClose=usePaged(pastClosings,31);
  const postNights=()=>run({type:'CLOSE_RUN'},md.resume?'Night audit resumed — remaining nights posted once':'Room nights posted once for '+shortDate(s.date));
  const closeDay=()=>run({type:'DAY_CLOSE',key:'dayclose:'+s.date},'Business day closed. Closing record saved.');
  const nextDay=()=>run({type:'NEXT_DAY',key:'nextday:'+s.date},'Next business day opened');
  // One primary action for the page — the focused step's.
  const primary=(():{label:string;fn:()=>void;why?:string}|null=>{
    const st=md.steps[md.focus];
    if(st.id==='preflight'){const b=st.blockers[0];return b?{label:`Resolve preflight (${st.blockers.length} left)`,fn:()=>drill(b)}:null;}
    if(st.id==='nights')return {label:`${md.resume?'Resume posting':'Post room nights'} (${md.toPost})`,fn:postNights,why:canClose?undefined:'Needs close permission (Manager or Owner).'};
    if(st.id==='cash')return md.shift==='Open'?{label:'Count & submit cash',fn:()=>open({type:'cash'}),why:permission('payments')?undefined:'Needs the payments permission.'}:{label:'Open cashier shift',fn:()=>go('cashier')};
    if(st.id==='close')return md.blocking?{label:'See what is blocking',fn:()=>toStep('close')}:{label:`Close ${shortDate(s.date)}`,fn:closeDay,why:canClose?undefined:'Needs close permission (Manager or Owner).'};
    if(st.id==='next'||st.id==='summary')return {label:'Open next business day',fn:nextDay,why:!md.closed?'Close today first.':canClose?undefined:'Needs close permission (Manager or Owner).'};
    return null;
  })();
  const body=(st:MaStep)=>{
    switch(st.id){
      case 'preflight':return st.blockers.length?<Blockers list={st.blockers} onTap={drill}/>:<p className="ma-ok"><Check size={18}/>{st.summary}</p>;
      case 'nights':return <>
        {md.resume&&<div className="ma-warn" role="status"><AlertTriangle size={18}/><span>Night audit was interrupted after <b>{md.resume.posted} of {md.resume.total}</b> stays. Resume posts only the remaining {md.resume.remaining}; nothing is posted twice.</span></div>}
        <MaRows rows={[['In-house tonight',md.targets],['To post',md.toPost],['Already posted',md.targets-md.toPost]]}/>
        {st.blockers.length>0&&<Blockers list={st.blockers} onTap={drill}/>}
        {process.env.HCC_BUILD==='production'?null:!md.closed&&md.toPost>=2&&canClose&&<button type="button" className="btn ma-subtle" disabled={busy} onClick={()=>run({type:'CLOSE_RUN',interruptAfter:Math.min(3,md.toPost-1)},'Simulated interruption — progress saved. Resume to continue.')}><Zap size={16}/>Demo: interrupt after {Math.max(1,Math.min(3,md.toPost-1))}</button>}
        {md.closed&&canClose&&<button type="button" className="btn ma-subtle" disabled={busy} onClick={()=>run({type:'CLOSE_RUN',key:'closerun-rerun:'+s.date+':'+s.sequence})}><RotateCcw size={16}/>Try posting again (refused)</button>}
      </>;
      case 'cash':return <>
        <MaRows rows={[['Status',md.shift],['Expected cash',money(s.shiftClose?.expected??shiftExpected(s).expected)],...(s.shiftClose?[['Counted',money(s.shiftClose.counted)],['Difference',money(s.shiftClose.counted-s.shiftClose.expected)]] as [string,string][]:[])]}/>
        {st.blockers.length>0&&<Blockers list={st.blockers} onTap={drill}/>}
      </>;
      case 'close':return md.closed?<><p>{dateLabel(s.date)} is closed. Rerunning close or night posting is refused; totals cannot change.</p>
          {canClose&&<button type="button" className="btn ma-subtle" disabled={busy} onClick={()=>run({type:'DAY_CLOSE',key:'dayclose-rerun:'+s.date+':'+s.sequence})}><RotateCcw size={16}/>Try closing again (refused)</button>}</>
        :st.blockers.length?<Blockers list={st.blockers} onTap={drill}/>:<p>All checks clear. Closing stores one immutable closing record for {shortDate(s.date)}.</p>;
      case 'summary':return <><div className="ma-summary">{md.record?<ClosingSummaryView rec={md.record}/>:<><p className="ma-intro">Preview — final when the day is closed.</p><ClosingSummaryView rec={closingSummary(s)}/></>}</div>
        <button type="button" className="btn" onClick={()=>open({type:'closing-report',id:s.date})}><Printer size={18}/>{md.record?'Print closing report':'Print preview'}</button>
        {md.record&&<DayPackButton date={s.date}/>}
        {(permission('reports')||permission('payments'))&&<button type="button" className="btn" onClick={()=>go('cashbook')}><BookOpen size={18}/>Cash book</button>}</>;
      case 'next':return <p>{md.closed?`Opens ${dateLabel(addDays(s.date,1))} at 08:00 with an opening float of ${money(s.shiftClose?.counted??0)} (counted cash).`:'Available after the day is closed.'}</p>;
    }
  };
  return <div className="ma-page ma-closing">
    <div className="ma-closing-head"><div><small>Business date</small><strong>{dateLabel(s.date)}</strong></div><MaTag tone={md.closed?'neutral':'ok'}>{md.closed?'Day closed':'Day open'}</MaTag>
      <button type="button" className="btn ma-print" onClick={()=>open({type:'closing-report',id:s.date})} aria-label={md.closed?'Print closing report':'Print preview of closing report'}><Printer size={18}/>Report</button></div>
    {md.resume&&<button type="button" className="ma-resume" onClick={()=>toStep('nights')}><AlertTriangle size={20}/><span><strong>Closing was interrupted — resume here</strong><small>{md.resume.posted} of {md.resume.total} nights posted · {md.resume.remaining} remaining · nothing posts twice</small></span><ArrowRight size={18}/></button>}
    {!canClose&&<div className="ma-note"><AlertTriangle size={18}/><span>You can review the preflight. Posting nights, closing and opening the next day need the close permission (Manager or Owner).</span></div>}
    <ol className="ma-vsteps" aria-label={`Day closing, step ${md.focus+1} of 6`}>
      {md.steps.map((st,i)=>{const isOpen=expanded===st.id;return <li key={st.id} id={'ma-step-'+st.id} className={st.state+(i===md.focus?' focus':'')} aria-current={i===md.focus?'step':undefined}>
        <button type="button" className="ma-vstep-head" aria-expanded={isOpen} onClick={()=>setExpanded(isOpen?('' as MaStepId):st.id)}>
          <span className="ma-dot" aria-hidden>{st.state==='done'?<Check size={16}/>:i+1}</span>
          <span className="ma-vstep-text"><strong>{st.label}</strong><small>{st.summary}</small></span>
          <MaTag tone={STATE_TONE[st.state]}>{st.id==='nights'&&md.resume?'Interrupted':STATE_TEXT[st.state]}</MaTag><ChevronDown size={18} className="ma-chev" aria-hidden/></button>
        {isOpen&&<div className="ma-vstep-body">{body(st)}</div>}
      </li>;})}
    </ol>
    <CashLockNote/>
    <h2 className="ma-h2">Earlier closing records</h2>
    {pastClosings.length?pgClose.shown.map(d=>{const c=s.closings![d];return <ListCard key={d} title={dateLabel(d)} sub={`Net revenue ${money(c.revenue.net)} · receipts ${money(c.receipts.net)}`} meta={`Counted cash ${money(c.cash.counted)}${c.cash.difference?' · difference '+money(c.cash.difference):''} · tap to view / print`} onClick={()=>open({type:'closing-report',id:d})}/>;})
      :<Empty title="No earlier closing records" body="Close a business day to create its record."/>}
    <ShowMore p={pgClose}/>
    {primary&&<StickyFooter><div className="ma-primary"><button type="button" className="btn primary" disabled={busy||!!primary.why} onClick={primary.fn}>{primary.label}<ArrowRight size={18}/></button>{primary.why&&<small className="ma-why">{primary.why}</small>}</div></StickyFooter>}
  </div>;
}
function Blockers({list,onTap}:{list:MaBlocker[];onTap:(b:MaBlocker)=>void}){
  return <div className="ma-blockers" role="list">{list.map((b,i)=><div role="listitem" key={i}><ListCard tone="ma-blocker" title={b.label} sub={b.hint} meta={b.booking?'Open booking':b.page?'Go to '+(navItems.find(n=>n.id===b.page)?.label||b.page):b.step?'Go to step':undefined} onClick={(b.booking||b.page||b.step)?()=>onTap(b):undefined}/></div>)}</div>;
}
