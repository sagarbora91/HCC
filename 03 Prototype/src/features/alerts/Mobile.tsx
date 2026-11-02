// LF-ALERTS · phone version of 'desk-alerts': cards, 48 px targets, no sideways scroll (LF contract §9.2).
import React,{useState} from 'react';
import {Plus,Check,Clock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {rmdList,rmdWakeOpen,rmdDueDate,type RMDBucket} from '../../services/mock';
import {Pill,Empty,shortDate} from '../../components/ui';
import {ReminderDetail} from './DeskAlertsPage';

type Tab='today'|'upcoming'|'done'|'wake-ups';
export function MfDeskAlerts(){
  const {s,uid,open,run,busy}=useHcc();
  const [tab,setTab]=useState<Tab>('today');const [openId,setOpenId]=useState('');
  const tabs:[Tab,string][]=[['today','Today'],['upcoming','Upcoming'],['done','Done'],['wake-ups','Wake-ups']];
  const list=tab==='wake-ups'?[]:rmdList(s,uid,tab as RMDBucket);
  const wakes=tab==='wake-ups'?rmdWakeOpen(s):[];
  return <div className="mf-page">
    <div className="mf-tabs" role="tablist" aria-label="Reminders and wake-ups">{tabs.map(([k,l])=><button key={k} role="tab" aria-selected={tab===k} className={tab===k?'active':''} onClick={()=>{setTab(k);setOpenId('');}}>{l}</button>)}</div>
    <div className="mf-body-actions"><button type="button" className="btn primary mf-wide" onClick={()=>open({type:'rmd-edit'})}><Plus size={16}/> New reminder</button></div>
    <div className="dk-cards">
      {list.map(r=><div key={r.id} className="dk-card"><button type="button" className="text-btn" style={{minHeight:48}} onClick={()=>setOpenId(openId===r.id?'':r.id)}><strong>{r.subject}</strong>
        <small> · {shortDate(rmdDueDate(r))}{r.time?' '+r.time:''}</small></button>{r.status==='open'&&rmdDueDate(r)<s.date&&<Pill tone="orange">Overdue</Pill>}
        {openId===r.id&&<ReminderDetail r={r} onChanged={()=>setOpenId('')}/>}</div>)}
      {wakes.map(w=><div key={w.id} className="dk-card"><strong>Room {w.room} · {w.wakeAt?.slice(11,16)}</strong><small> · {w.guest} · {w.wakeAt?.slice(0,10)}</small>
        <div className="inline" style={{marginTop:8}}><button type="button" className="btn primary" style={{minHeight:48}} disabled={busy} onClick={()=>void run({type:'GSV_REQUEST_COMPLETE',id:w.id,note:'Wake-up call made'},'Wake-up call done.')}><Check size={15}/> Done</button>
          <button type="button" className="btn" style={{minHeight:48}} disabled={busy} onClick={()=>void run({type:'GSV_WAKE_SNOOZE',id:w.id},'Snoozed 5 minutes.')}><Clock size={15}/> Snooze 5 min</button></div></div>)}
      {!list.length&&!wakes.length&&<Empty title="Nothing here" body={tab==='wake-ups'?'No wake-up calls are waiting.':'No reminders in this list.'}/>}
    </div></div>;
}
