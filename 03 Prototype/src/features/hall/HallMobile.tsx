// F6-H3 · Hall booking on the phone (spec §17): enquiry list + add sheet, quotation summary with Send / Accept (lines are edited on the desktop), calendar day list,
// event-day card. 48 px targets, cards not tables. No prices for roles that may not see them.
import React,{useState} from 'react';
import {Plus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {Pill,Empty} from '../../components/ui';
import {money} from '../../domain/money';
import {hallSlice} from '../../domain/streams/hall';
import {hallUi,hallTabsFor} from './access';
import {hallBoard,hallQuoteRows,hallEventRows,hallDayGrid,hallAlerts,venueName,SLOT_LABEL,SLOT_STATE_LABEL,SLOT_STATE_TONE,EVENT_LABEL,QUOTE_TONE,EVENT_TONE,ENQ_TONE,dayLabel} from './logic';

type MTab='enquiries'|'quotations'|'calendar'|'events';
export function HallMobile(){
  const {s,uid,open}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s),tabs=hallTabsFor(s,uid).filter((t):t is MTab=>t==='enquiries'||t==='quotations'||t==='calendar'||t==='events');
  const [tab,setTab]=useState<MTab>(tabs[0]??'calendar'),[day,setDay]=useState(s.date),[evf,setEvf]=useState<'upcoming'|'billed'|'all'>('upcoming');
  if(!ui.view||!tabs.length)return <Empty title="Not available" body={ui.readOnlyNote}/>;
  const al=hallAlerts(s),b=hallBoard(s);
  const labels:Record<MTab,string>={enquiries:'Enquiries',quotations:'Quotes',calendar:'Calendar',events:'Events'};
  return <div className="hall-m">
    {(ui.enquiry||ui.seesMoney)&&<div className="m-kpis"><KpiCard label="Follow-ups today" value={al.followUps} sub={al.overdue?`${al.overdue} overdue`:undefined}/><KpiCard label="Events ahead" value={x.events.filter(e=>e.status==='CONFIRMED').length}/></div>}
    <ChipFilter label="Hall booking" options={tabs.map(t=>({id:t,label:labels[t]}))} value={tab} onChange={setTab}/>
    {tab==='enquiries'&&<>{b.columns.filter(c=>c.status==='OPEN'||c.status==='QUOTED').flatMap(c=>c.items).map(q=><ListCard key={q.id} title={q.name} sub={`${EVENT_LABEL[q.eventType]} · ${q.pax} guests · ${q.dates.map(dayLabel).join(', ')} ${SLOT_LABEL[q.slot]}`}
      meta={`Follow up ${dayLabel(q.followUp)}${q.venueId?' · '+venueName(x,q.venueId):''}`} right={<Pill tone={ENQ_TONE[q.status]}>{q.status}</Pill>} tone={b.isDue(q)?'warn':''} onClick={()=>open({type:'hall-enquiry',id:q.id})}/>)}
      {!b.columns[0].items.length&&!b.columns[1].items.length&&<Empty title="No open enquiries"/>}
      {ui.enquiry&&<Fab label="New enquiry" icon={Plus} onClick={()=>open({type:'hall-enquiry'})}/>}</>}
    {tab==='quotations'&&(hallQuoteRows(s).map(r=><ListCard key={r.q.id} title={`${r.q.number} · ${r.customer}`} sub={`${EVENT_LABEL[r.v.header.eventType]} · ${dayLabel(r.date)} · ${r.venue}`}
      meta={`v${r.v.v} · valid until ${dayLabel(r.v.validUntil)}`} right={<><Pill tone={QUOTE_TONE[r.v.status]}>{r.v.status}</Pill>{ui.seesMoney&&<strong>{money(r.totalPaise)}</strong>}</>} onClick={()=>open({type:'hall-quote',id:r.q.id})}/>))}
    {tab==='quotations'&&!x.quotations.length&&<Empty title="No quotations yet"/>}
    {tab==='calendar'&&<><label className="hall-m-date">Day<input type="date" value={day} onChange={e=>setDay(e.target.value||s.date)}/></label>
      {hallDayGrid(s,day).map(r=><section key={r.venue.id} className="hall-m-hall"><h3>{r.venue.name}</h3>{r.slots.map(c=><div key={c.slot} className="hall-m-slot"><span>{SLOT_LABEL[c.slot]}</span><Pill tone={SLOT_STATE_TONE[c.state]}>{SLOT_STATE_LABEL[c.state]}</Pill>
        {c.state!=='FREE'&&<button type="button" className="link" onClick={()=>open(c.ref?.startsWith('HEV')?{type:'hall-event',id:c.ref}:{type:'hall-enquiry',id:c.ref})}>{c.label}</button>}</div>)}</section>)}</>}
    {tab==='events'&&<><ChipFilter label="Show" options={[{id:'upcoming',label:'Upcoming'},{id:'billed',label:'Billed'},{id:'all',label:'All'}]} value={evf} onChange={setEvf}/>
      {hallEventRows(s,evf).map(r=><ListCard key={r.e.id} title={`${r.e.id} · ${r.customer}`} sub={`${dayLabel(r.e.date)} ${SLOT_LABEL[r.e.slot]} · ${r.venue}`} meta={`${r.pax} plates`}
        right={<><Pill tone={EVENT_TONE[r.e.status]}>{r.e.status==='CONFIRMED'?'Confirmed':r.e.status==='BILLED'?'Billed':'Cancelled'}</Pill>{ui.seesMoney&&<strong>{money(r.balancePaise)}</strong>}</>} onClick={()=>open({type:'hall-event',id:r.e.id})}/>)}
      {!hallEventRows(s,evf).length&&<Empty title="No events here"/>}</>}
  </div>;
}
