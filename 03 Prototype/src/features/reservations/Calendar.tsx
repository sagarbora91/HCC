// Stream A · Calendar timeline (SCR-03, REQ-RACK-004/005): rooms grouped by type × 14 nights.
import React,{useState} from 'react';
import {ChevronLeft,ChevronRight} from 'lucide-react';
import {addDays,resCalendar,RES_CAPACITY} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate,dateLabel} from '../../components/ui';
import {FitPage,ScrollPanel,FitNote} from '../../components/fit';

const DAYS=14,DATE=/^\d{4}-\d{2}-\d{2}$/;
const weekday=(d:string)=>new Date(d+'T12:00:00').toLocaleDateString('en-IN',{weekday:'short'});
const tone=(status:string)=>status==='Held'?'held':status==='Confirmed'?'confirmed':status==='In house'?'inhouse':status==='Departed'?'departed':'block';

export function Calendar(){
  const {s,act,open,booking,permission}=useHcc();
  const [start,setStart]=useState(s.date),[type,setType]=useState('All');
  const cal=resCalendar(s,start,DAYS,type),canBook=permission('reservations');
  const rows=cal.groups.reduce((n,g)=>n+g.rows.length,0);
  const cols={'--res-days':DAYS} as React.CSSProperties;
  // FIT (DEC-194, contract §6 'calendar' = fill): header + one toolbar row; ONE panel scrolls both ways (room column and date row sticky);
  // the range line, legend and the note sit in the panel footer.
  const legend=<div className="res-legend" aria-label="Legend">{[['held','Held (time left)'],['confirmed','Confirmed'],['inhouse','In house'],['departed','Departed'],['block','Blocked']].map(([k,l])=><span key={k}><i className={'res-swatch '+k}/>{l}</span>)}</div>;
  return <FitPage title="Calendar" sub="Rooms by type across two weeks. Select an empty night to book, or a bar to open the booking."
      actions={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}
      toolbar={<>
        <div className="res-cal-nav" role="group" aria-label="Date navigation">
          <button className="btn" onClick={()=>setStart(addDays(start,-7))}><ChevronLeft size={15}/> Previous week</button>
          <button className="btn" disabled={start===s.date} onClick={()=>setStart(s.date)} title="Jump to the business date">Today</button>
          <button className="btn" onClick={()=>setStart(addDays(start,7))}>Next week <ChevronRight size={15}/></button>
        </div>
        <div className="res-cal-filters">
          <label>From<input type="date" value={start} onChange={e=>{if(DATE.test(e.target.value))setStart(e.target.value);}}/></label>
          <label>Room type<select value={type} onChange={e=>setType(e.target.value)}><option value="All">All types</option>{Object.keys(RES_CAPACITY).map(t=><option key={t}>{t}</option>)}</select></label>
        </div></>}>
    <ScrollPanel label="Room calendar, scrolls in both directions" title={<>{dateLabel(start)} — {dateLabel(addDays(cal.end,-1))}</>} actions={<span className="muted">{DAYS} nights · business date {shortDate(s.date)}</span>}
      footer={<div className="fit-cal-foot">{legend}<FitNote text="Bars cover nights from arrival up to (not including) departure. To move a confirmed reservation or change its dates, open its bar and choose “Move / extend” — the whole new interval is revalidated and a conflict keeps the original. Expired holds are not shown because they no longer reserve the room."/></div>}>
      {!rows?<Empty title="No rooms to show" body="No rooms match this room type."/>:
      <div className="res-cal-scroll">
        <div className="res-cal" style={cols} role="grid" aria-rowcount={rows+1}>
          <div className="res-cal-row res-cal-head" role="row"><div className="res-cal-room" role="columnheader">Room</div>
            {cal.dates.map(d=><div key={d} role="columnheader" className={'res-cal-day'+(d===s.date?' today':'')+(d<s.date?' past':'')}><small>{weekday(d)}</small><strong>{d.slice(8)}</strong><small>{shortDate(d).split(' ')[1]}</small></div>)}</div>
          {cal.groups.map(g=><React.Fragment key={g.type}>
            <div className="res-cal-group" role="row"><span role="rowheader">{g.type} · sleeps {g.capacity} · {g.rows.length} room{g.rows.length===1?'':'s'}</span></div>
            {g.rows.map(r=><div className="res-cal-row" role="row" key={r.room.id}>
              <div className="res-cal-room" role="rowheader"><strong>{r.room.id}</strong><small>{r.room.type}</small></div>
              {r.cells.map((c,i)=>c.kind==='free'&&!c.past&&canBook
                ?<button key={c.date} role="gridcell" className="res-cal-cell free" style={{gridColumn:i+2}} aria-label={`Book room ${r.room.id} from ${shortDate(c.date)}`} title={`Book room ${r.room.id} from ${shortDate(c.date)}`} onClick={()=>open({type:'new',room:r.room.id,id:c.date})}><span aria-hidden>+</span></button>
                :<div key={c.date} role="gridcell" className={'res-cal-cell '+c.kind+(c.past?' past':'')} style={{gridColumn:i+2}} aria-label={c.kind==='free'?(c.past?'Past night':'Free'):undefined}/>)}
              {r.bars.map((bar,i)=>{const cls='res-bar '+tone(bar.status)+(bar.clipStart?' clip-start':'')+(bar.clipEnd?' clip-end':'');
                const label=(bar.clipStart?'continues from earlier · ':'')+bar.label+(bar.clipEnd?' · continues later':'');
                return bar.kind==='stay'
                  ?<button key={'b'+i} className={cls} style={{gridColumn:`${bar.start+2} / span ${bar.span}`}} title={label} aria-label={label} onClick={()=>booking(bar.booking!)}><strong>{bar.guest}</strong><small>{bar.sub}</small></button>
                  :<button key={'k'+i} className={cls} style={{gridColumn:`${bar.start+2} / span ${bar.span}`}} title={label} aria-label={label} onClick={()=>open({type:'room',id:r.room.id})}><strong>{bar.label}</strong></button>;})}
            </div>)}
          </React.Fragment>)}
        </div>
      </div>}
    </ScrollPanel>
  </FitPage>;
}
