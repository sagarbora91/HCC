// Wave 0.5 · W05-C Dashboard "Due this week" card (Owner and Accountant only; renders nothing for others).
import React from 'react';
import {CalendarClock,ArrowRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpHasAccess,cmpDueThisWeek,cmpSlice} from '../../services/mock';
import {dateLabel} from '../../components/ui';
import {CmpStatusPill} from './shared';

export function CmpDueCard(){
  const {s,uid,go}=useHcc();
  if(!cmpHasAccess(s,uid))return null;
  const due=cmpDueThisWeek(s);
  return <section className="card cmp-due-card" aria-label="Compliance due this week">
    <div className="section-title"><div><h2>Due this week</h2><p>Tax and statutory filings · within {cmpSlice(s).settings.dueSoonDays} days, plus anything overdue</p></div>
      <button className="text-btn" onClick={()=>go('compliance')}>Compliance calendar <ArrowRight size={14}/></button></div>
    {!due.length?<p className="cmp-due-empty"><CalendarClock size={16}/> Nothing is overdue or due this week.</p>:
    <ul className="cmp-due-list">{due.slice(0,5).map(i=><li key={i.key}><div><strong>{i.obligation.name}</strong><small>{i.periodLabel} · due {dateLabel(i.due)}</small></div><CmpStatusPill item={i}/></li>)}
      {due.length>5&&<li className="cmp-due-more">+ {due.length-5} more in the calendar</li>}</ul>}
  </section>;}
