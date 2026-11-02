// LF-ALERTS · page 'desk-alerts' — Front Desk › Reminders & wake-ups (LF contract §9.2). FIT split page with tabs Today · Upcoming · Done · Wake-ups.
import React,{useState} from 'react';
import {Plus,Check,Clock,Pencil,X,AlarmClock,BellRing} from 'lucide-react';
import {useHcc} from '../../app/context';
import {rmdList,rmdWakeOpen,rmdWakeDone,rmdDueDate,rmdShowFrom,rmdRoleOf,rmdSlice,type RMDBucket,type RMDReminder,type GSVRequest} from '../../services/mock';
import {FitPage,FitTabs,ScrollPanel,SplitView,FitNote,useFitTab,useWideSplit} from '../../components/fit';
import {Pill,Empty,shortDate} from '../../components/ui';

const TABS=['today','upcoming','done','wake-ups'] as const;
type Tab=typeof TABS[number];

export function ReminderDetail({r,onChanged}:{r:RMDReminder;onChanged?:()=>void}){
  const {s,uid,run,open,busy}=useHcc();
  const [reason,setReason]=useState('');const [askDismiss,setAskDismiss]=useState(false);
  const boss=['Owner','Manager'].includes(rmdRoleOf(s,uid)??'');
  const canEdit=r.status==='open'&&(boss||r.by===uid);
  const by=s.users.find(u=>u.id===r.by)?.name.split(' · ')[0]??r.by;
  return <div className="dk-detail"><h2>{r.subject}</h2>
    <div className="inline"><Pill tone={r.status==='open'?(rmdDueDate(r)<s.date?'orange':'blue'):r.status==='done'?'green':'neutral'}>{r.status==='open'?(rmdDueDate(r)<s.date?'Overdue':'Open'):r.status==='done'?'Done':'Dismissed'}</Pill>
      {r.repeat!=='none'&&<Pill>Repeats {r.repeat}</Pill>}</div>
    <div className="dk-kv"><span>Date</span><strong>{shortDate(rmdDueDate(r))}{r.time?' · '+r.time:''}</strong>
      <span>Shows from</span><strong>{shortDate(rmdShowFrom(r))} ({r.leadDays} day{r.leadDays===1?'':'s'} before)</strong>
      <span>For</span><strong>{r.role==='all'?'Everyone at the desk':r.role}</strong><span>Set by</span><strong>{by} · {r.at.slice(0,10)}</strong>
      {r.note&&<><span>Note</span><strong>{r.note}</strong></>}{r.doneAt&&<><span>Finished</span><strong>{r.doneAt.replace('T',' ')}</strong></>}</div>
    {r.status==='open'&&<div className="inline">
      <button type="button" className="btn primary" disabled={busy} onClick={()=>void run({type:'RMD_DONE',id:r.id},'Reminder done.',{after:onChanged})}><Check size={15}/> Done</button>
      {canEdit&&<button type="button" className="btn" onClick={()=>open({type:'rmd-edit',id:r.id})}><Pencil size={15}/> Edit</button>}
      <button type="button" className="btn" onClick={()=>setAskDismiss(v=>!v)}><X size={15}/> Dismiss…</button></div>}
    {askDismiss&&<div className="form"><label>Reason (required)<input value={reason} maxLength={200} onChange={e=>setReason(e.target.value)} placeholder="Why is this not needed?"/></label>
      <div className="inline"><button type="button" className="btn danger" disabled={busy||reason.trim().length<3} onClick={()=>void run({type:'RMD_DISMISS',id:r.id,reason:reason.trim()},'Reminder dismissed.',{after:()=>{setAskDismiss(false);onChanged?.();}})}>Dismiss reminder</button></div></div>}
  </div>;
}
export function WakeDetail({w}:{w:GSVRequest}){
  const {s,run,busy}=useHcc();const due=!!w.wakeAt&&w.wakeAt<=s.clock;
  return <div className="dk-detail"><h2>Wake-up · Room {w.room}</h2>
    <div className="inline"><Pill tone={w.status==='Done'?'green':due?'orange':'blue'}>{w.status==='Done'?'Done':due?'Due now':'Scheduled'}</Pill></div>
    <div className="dk-kv"><span>Guest</span><strong>{w.guest}</strong><span>Wake-up time</span><strong>{w.wakeAt?.replace('T',' ')}</strong><span>Booking</span><strong>{w.booking}</strong>
      {w.detail&&<><span>Detail</span><strong>{w.detail}</strong></>}{w.doneAt&&<><span>Done at</span><strong>{w.doneAt.replace('T',' ')}</strong></>}</div>
    {w.status!=='Done'&&<div className="inline"><button type="button" className="btn primary" disabled={busy} onClick={()=>void run({type:'GSV_REQUEST_COMPLETE',id:w.id,note:'Wake-up call made'},`Wake-up call for room ${w.room} done.`)}><Check size={15}/> Done</button>
      <button type="button" className="btn" disabled={busy} onClick={()=>void run({type:'GSV_WAKE_SNOOZE',id:w.id},'Snoozed 5 minutes.')}><Clock size={15}/> Snooze 5 min</button></div>}
  </div>;
}

export function DeskAlertsPage(){
  const {s,uid,open}=useHcc();
  const [tab,setTab]=useFitTab<Tab>('desk-alerts',TABS,'today');
  const wide=useWideSplit();const [sel,setSel]=useState('');
  const lists={today:rmdList(s,uid,'today'),upcoming:rmdList(s,uid,'upcoming'),done:rmdList(s,uid,'done')} as Record<RMDBucket,RMDReminder[]>;
  const wakes=tab==='wake-ups'?[...rmdWakeOpen(s),...rmdWakeDone(s)]:[];
  const items:{id:string;title:string;sub:string;tone?:'orange'|'blue'|'green'|'neutral';tag?:string}[]=tab==='wake-ups'
    ?wakes.map(w=>({id:w.id,title:`Room ${w.room} · ${w.wakeAt?.slice(11,16)}`,sub:`${w.guest} · ${w.wakeAt?.slice(0,10)}`,tone:w.status==='Done'?'green':w.wakeAt!<=s.clock?'orange':'blue',tag:w.status==='Done'?'Done':w.wakeAt!<=s.clock?'Due':'Scheduled'}))
    :lists[tab as RMDBucket].map(r=>({id:r.id,title:r.subject,sub:`${shortDate(rmdDueDate(r))}${r.time?' '+r.time:''} · ${r.role==='all'?'everyone':r.role}${r.repeat!=='none'?' · repeats '+r.repeat:''}`,
      tone:r.status!=='open'?'neutral':rmdDueDate(r)<s.date?'orange':'blue',tag:r.status!=='open'?(r.status==='done'?'Done':'Dismissed'):rmdDueDate(r)<s.date?'Overdue':undefined}));
  const pick=items.find(x=>x.id===sel)??(wide?items[0]:undefined);
  const detailItem=pick?(tab==='wake-ups'?<WakeDetail w={wakes.find(w=>w.id===pick.id)!}/>:<ReminderDetail r={rmdSlice(s).reminders.find(r=>r.id===pick.id)!} onChanged={()=>setSel('')}/>):null;
  const counts={today:lists.today.length,upcoming:lists.upcoming.length,done:lists.done.length,'wake-ups':rmdWakeOpen(s).length};
  const tabs=[{id:'today' as Tab,label:'Today',count:counts.today||undefined,badge:counts.today?'warn' as const:undefined},{id:'upcoming' as Tab,label:'Upcoming',count:counts.upcoming||undefined},{id:'done' as Tab,label:'Done'},{id:'wake-ups' as Tab,label:'Wake-ups',count:counts['wake-ups']||undefined}];
  return <FitPage title="Reminders & wake-ups" sub={`Desk reminders (alert days) and guests' wake-up calls · business date ${shortDate(s.date)}`} layout="split"
      actions={<button type="button" className="btn primary" onClick={()=>open({type:'rmd-edit'})}><Plus size={16}/> New reminder</button>}
      tabs={<FitTabs<Tab> page="desk-alerts" tabs={tabs} value={tab} onChange={t=>{setTab(t);setSel('');}}/>}>
    <SplitView label="Reminders and wake-ups" ratio="2fr 3fr" onCloseDetail={()=>setSel('')}
      list={<ScrollPanel title={tabs.find(t=>t.id===tab)!.label} label="Reminder list" actions={<span className="muted">{items.length}</span>}
          footer={<FitNote text="A reminder shows on the desk banner from its alert days before the date until it is done. Wake-up calls come from guest requests and alert at their time."/>}>
        {items.length?<ul className="dk-list">{items.map(x=><li key={x.id}><button type="button" className={'dk-item'+(pick?.id===x.id?' active':'')} aria-pressed={pick?.id===x.id} onClick={()=>setSel(x.id)}>
          {tab==='wake-ups'?<AlarmClock size={18} aria-hidden="true"/>:<BellRing size={18} aria-hidden="true"/>}<span className="dk-main"><strong>{x.title}</strong><small>{x.sub}</small></span>{x.tag&&<Pill tone={x.tone}>{x.tag}</Pill>}</button></li>)}</ul>
          :<Empty title={tab==='wake-ups'?'No wake-up calls':'Nothing here'} body={tab==='today'?'No reminder is inside its alert days right now.':tab==='wake-ups'?'Wake-up calls logged in Guest services appear here.':'Nothing to show.'}/>}
      </ScrollPanel>}
      detail={detailItem} empty={<Empty title="Pick an item" body="Its details and actions open here."/>}/>
  </FitPage>;
}
