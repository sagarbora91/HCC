// F8 QW-PM · Preventive-maintenance calendar: month grid (desktop) / day list (phone) of plan occurrences with a text state label,
// AMC expiry cards, and the Manager's "Add standard CCTV / DG shift checklist" button. No money anywhere (DEC-061).
import React,{useState} from 'react';
import {ChevronLeft,ChevronRight,Sparkles,ClipboardList} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {ScrollPanel,FitNote} from '../../components/fit';
import {hovIsManager,pmcMonth,pmcCounts,pmcAmcCards,pmcMonthOf,pmcShiftMonth,pmcMissingTemplates,type PmcOccurrence,type PmcState} from '../../services/mock';

const TONE:Record<PmcState,string>={Done:'green',Overdue:'mnt-red',Due:'orange',Scheduled:'neutral'};
const AMC_TONE={Active:'green',Expiring:'orange',Expired:'mnt-red','Not started':'neutral'} as const;
const monthLabel=(m:string)=>new Date(m+'-01T00:00:00Z').toLocaleDateString('en-IN',{month:'long',year:'numeric',timeZone:'UTC'});

function useOccurrenceClick(){
  const {open,run,busy,s}=useHcc();
  return {busy,click:(o:PmcOccurrence)=>{
    if(o.workOrder)open({type:'mnt-wo',id:o.workOrder});
    else if(o.state==='Overdue'||o.state==='Due')void run({type:'MNT_PM_GENERATE',key:'pm:'+s.date+':'+s.sequence},'Preventive work orders generated');
    else open({type:'mnt-plan',id:o.planId});
  }};
}
const hint=(o:PmcOccurrence)=>o.workOrder?`Open ${o.workOrder}`:(o.state==='Overdue'||o.state==='Due')?'Generate the work order':'Open the plan';
function OccButton({o,onClick,disabled}:{o:PmcOccurrence;onClick:()=>void;disabled?:boolean}){
  return <button type="button" className="text-btn" disabled={disabled} title={`${o.title} · ${o.assetName} · ${o.where} · ${o.state}${o.paused?' (plan paused)':''} · ${hint(o)}`}
      style={{display:'block',width:'100%',textAlign:'left',padding:'2px 0',minHeight:0,whiteSpace:'normal',overflowWrap:'anywhere',height:'auto'}} onClick={onClick}>
    <Pill tone={TONE[o.state]}>{o.state}</Pill> <span>{o.title}</span></button>;
}

export function PmCalendar({phone}:{phone?:boolean}){
  const {s,uid,run,busy}=useHcc();const [month,setMonth]=useState(pmcMonthOf(s.date));
  const days=pmcMonth(s,month),cnt=pmcCounts(days),amcs=pmcAmcCards(s);const {click,busy:b2}=useOccurrenceClick();
  const missing=pmcMissingTemplates(s),mgr=hovIsManager(s,uid);
  const addTemplates=async()=>{for(const t of pmcMissingTemplates(s))if(!await run({type:'HOV_TEMPLATE_SAVE',template:t,key:'qwpm:tpl:'+t.name},`${t.name} added`))break;};
  const nav=<div className="inline" role="group" aria-label="Choose month"><button className="btn" aria-label="Previous month" onClick={()=>setMonth(pmcShiftMonth(month,-1))}><ChevronLeft size={15}/></button>
    <strong style={{minWidth:130,textAlign:'center'}}>{monthLabel(month)}</strong><button className="btn" aria-label="Next month" onClick={()=>setMonth(pmcShiftMonth(month,1))}><ChevronRight size={15}/></button>
    {month!==pmcMonthOf(s.date)&&<button className="btn" onClick={()=>setMonth(pmcMonthOf(s.date))}>This month</button>}</div>;
  const summary=<p className="muted" data-testid="pmc-summary">{cnt.Overdue} overdue · {cnt.Due} due · {cnt.Scheduled} scheduled · {cnt.Done} done</p>;
  const tplBtn=mgr&&<button className="btn" disabled={busy||!missing.length} onClick={()=>void addTemplates()} title={missing.length?'Saves the Morning, Evening and Night checklists':'Already added'}>
    <ClipboardList size={15}/> {missing.length?'Add standard CCTV / DG checklist':'CCTV / DG checklist added'}</button>;
  const grid=(()=>{
    const [y,m]=month.split('-').map(Number),lead=(new Date(Date.UTC(y,m-1,1)).getUTCDay()+6)%7,n=new Date(Date.UTC(y,m,0)).getUTCDate();
    const by=new Map(days.map(d=>[d.date,d.items])),cells:React.ReactNode[]=[];
    for(let i=0;i<lead;i++)cells.push(<div key={'l'+i}/>);
    for(let d=1;d<=n;d++){const date=`${month}-${String(d).padStart(2,'0')}`,items=by.get(date)??[];
      cells.push(<div key={date} aria-label={dateLabel(date)} style={{border:'1px solid var(--line)',borderRadius:6,padding:4,minHeight:64,minWidth:0,overflow:'hidden',background:date===s.date?'var(--surface)':undefined}}>
        <small className="muted">{d}{date===s.date?' · today':''}</small>{items.map(o=><OccButton key={o.planId+o.due} o={o} disabled={b2} onClick={()=>click(o)}/>)}</div>);}
    return <div role="group" aria-label="Preventive maintenance calendar" style={{display:'grid',gridTemplateColumns:'repeat(7,minmax(0,1fr))',gap:4}}>
      {['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(w=><strong key={w} style={{textAlign:'center'}}>{w}</strong>)}{cells}</div>;
  })();
  const list=days.length?days.map(d=><section key={d.date} className="card padded" aria-label={dateLabel(d.date)} style={{marginBottom:8}}><strong>{dateLabel(d.date)}{d.date===s.date?' · today':''}</strong>
      {d.items.map(o=><div key={o.planId+o.due} style={{marginTop:6}}><OccButton o={o} disabled={b2} onClick={()=>click(o)}/><small className="muted">{o.assetName} · {o.where}{o.register?` · register ${o.register.no}`:''}</small></div>)}</section>)
    :<Empty title="No preventive work this month" body="Plans show here on the days they fall due."/>;
  const amcCards=<div style={{display:'grid',gridTemplateColumns:phone?'1fr':'repeat(auto-fill,minmax(220px,1fr))',gap:8}}>
    {amcs.map(a=><div key={a.id} className="card padded" aria-label={`AMC ${a.vendor}`}><div className="inline"><strong>{a.vendor}</strong><Pill tone={AMC_TONE[a.status]}>{a.status}</Pill></div>
      <small className="muted">{a.scope}</small><br/><small>{shortDate(a.from)} → {shortDate(a.to)} {a.to.slice(0,4)} · {a.status==='Expired'?`ended ${-a.daysLeft} d ago`:a.status==='Not started'?'not started':`${a.daysLeft} d left`}</small></div>)}
    {!amcs.length&&<Empty title="No AMC contracts"/>}</div>;
  if(phone)return <>
    <div style={{display:'flex',flexDirection:'column',gap:8}}>{nav}{summary}{tplBtn}</div>
    {list}<h2 className="m-group">AMC CONTRACTS</h2>{amcCards}</>;
  return <>
    <ScrollPanel title="PM calendar" label="PM calendar" actions={<>{nav}{tplBtn}</>} footer={<FitNote text={`Business date ${dateLabel(s.date)}. Click an occurrence to open its work order, or to generate the due ones.`}/>}>
      {summary}{grid}<h2 className="mnt-subhead"><Sparkles size={14}/> AMC contracts</h2>{amcCards}</ScrollPanel>
  </>;
}
