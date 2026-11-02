// LF-ALERTS · the desk banner (LF contract §9.1, §9.2): wake-up calls that are due and reminders inside their lead window, for the signed-in front-desk
// role. Persistent amber banner, no sound (CSP / Android policy; the legacy pop-up was visual too). Mounted by App.tsx in both shells under the top bar.
import React,{useState} from 'react';
import {BellRing,AlarmClock,Check,Clock} from 'lucide-react';
import {useHcc} from './context';
import {rmdDue,rmdDueDate} from '../services/mock';

const KEY=(date:string)=>'hcc-desk-later:'+date;
const readLater=(date:string):string[]=>{try{const v=JSON.parse(sessionStorage.getItem(KEY(date))||'[]');return Array.isArray(v)?v.map(String):[];}catch{return [];}};
const writeLater=(date:string,ids:string[])=>{try{sessionStorage.setItem(KEY(date),JSON.stringify(ids));}catch{/* storage unavailable: the banner just comes back */}};

export function DeskAlerts({phone=false}:{phone?:boolean}){
  const {s,uid,run,go,busy}=useHcc();
  const [later,setLater]=useState<string[]>(()=>readLater(s.date));
  const day=useRef0(s.date);if(day.current!==s.date){day.current=s.date;if(later.length)setLater([]);}
  const a=rmdDue(s,uid,later);
  if(!a.count)return null;
  const rows=[...a.wake.map(w=>({kind:'wake' as const,id:w.id,w})),...a.reminders.map(r=>({kind:'rmd' as const,id:r.id,r}))];
  const shown=rows.slice(0,3);
  const hide=(id:string)=>{const next=[...later,id];setLater(next);writeLater(s.date,next);};
  return <section className={'dk-alerts'+(phone?' phone':'')} role="region" aria-label="Desk alerts" aria-live="polite">
    {shown.map(x=>x.kind==='wake'?<div key={x.id} className="dk-row dk-wake">
      <AlarmClock size={18} aria-hidden="true"/><div className="dk-main"><strong>Wake-up · Room {x.w.room} · {x.w.wakeAt!.slice(11,16)} · {x.w.guest}</strong>{x.w.detail&&<small>{x.w.detail}</small>}</div>
      <div className="dk-act"><button type="button" className="btn small-btn primary" disabled={busy} onClick={()=>void run({type:'GSV_REQUEST_COMPLETE',id:x.w.id,note:'Wake-up call made'},`Wake-up call for room ${x.w.room} done.`)}><Check size={14}/> Done</button>
        <button type="button" className="btn small-btn" disabled={busy} onClick={()=>void run({type:'GSV_WAKE_SNOOZE',id:x.w.id},`Wake-up for room ${x.w.room} snoozed 5 minutes.`)}><Clock size={14}/> Snooze 5 min</button></div></div>
      :<div key={x.id} className="dk-row dk-rmd">
      <BellRing size={18} aria-hidden="true"/><div className="dk-main"><strong>Reminder · {x.r.subject}</strong><small>{rmdDueDate(x.r)<s.date?'Was due ':'Due '}{rmdDueDate(x.r)}{x.r.time?' '+x.r.time:''}{x.r.note?' · '+x.r.note:''}</small></div>
      <div className="dk-act"><button type="button" className="btn small-btn primary" disabled={busy} onClick={()=>void run({type:'RMD_DONE',id:x.r.id},'Reminder done.')}><Check size={14}/> Done</button>
        <button type="button" className="btn small-btn" onClick={()=>hide(x.r.id)}>Later</button></div></div>)}
    {rows.length>shown.length&&<div className="dk-more"><button type="button" className="text-btn" onClick={()=>go('desk-alerts')}>+{rows.length-shown.length} more · Reminders &amp; wake-ups</button></div>}
  </section>;
}
function useRef0<T>(v:T){const r=React.useRef<T>(v);return r;}
