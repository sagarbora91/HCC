// Dashboard "Due this week" card (Owner and Accountant only; renders nothing for others). HCC extra kept next to SCC's 30-day Action Center
// (owner question): the Action Center rows that are overdue or due within settings.dueSoonDays.
import React from 'react';
import {CalendarClock,ArrowRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpHasAccess,cmpDueThisWeek,cmpSlice,cmpRenderFYText,cmpStripHtml,cmpWhen} from '../../services/mock';
import {cmpUiSet} from './shared';

export function CmpDueCard(){
  const {s,uid,go}=useHcc();
  if(!cmpHasAccess(s,uid))return null;
  const x=cmpSlice(s),due=cmpDueThisWeek(s);
  return <section className="card cmp-due-card" aria-label="Compliance due this week">
    <div className="section-title"><div><h2>Due this week</h2><p>Tax and statutory filings · within {x.settings.dueSoonDays} days, plus anything overdue</p></div>
      <button className="text-btn" onClick={()=>go('compliance')}>Compliance calendar <ArrowRight size={14}/></button></div>
    {!due.length?<p className="cmp-due-empty"><CalendarClock size={16}/> Nothing is overdue or due this week.</p>:
    <ul className="cmp-due-list">{due.slice(0,5).map(r=><li key={r.itemId}><div><strong>{cmpStripHtml(cmpRenderFYText(r.name,x.fyStart))}</strong><small>{r.month} · due {r.due}</small></div>
      <button type="button" className={'cmp-days '+(r.days<0?'cmp-days-overdue':'cmp-days-urgent-days')} onClick={()=>{cmpUiSet({view:'calendar',month:r.month,focus:r.itemId});go('compliance');}}>{r.days<0?'⚠ ':''}{cmpWhen(r.days)}</button></li>)}
      {due.length>5&&<li className="cmp-due-more">+ {due.length-5} more in the calendar</li>}</ul>}
  </section>;}
