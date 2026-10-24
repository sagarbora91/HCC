// Module 13 · Tariff & yield — phone page: KPI cards, room-type chips, 7-day strip, explained night card, proposals, rules, history.
import React,{useState} from 'react';
import {money,yldSlice,yldSummary,yldDates,yldNight,yldOcc,yldRoomTypes,yldPct,yldPlanRate,yldPlanAvailable,addDays,YLD_DOW_NAMES} from '../../services/mock';
import {useHcc} from '../../app/context';
import {KpiCard,ChipFilter,ListCard,StickyFooter} from '../../app/mobile/components';
import {Pill,shortDate,clockTime} from '../../components/ui';
import {useYldRole} from './modals';
import {Proposals} from './YieldPage';

export function YieldMobile(){
  const {s,open}=useHcc();const {owner,propose}=useYldRole();
  const types=yldRoomTypes(s),sum=yldSummary(s),y=yldSlice(s);
  const [type,setType]=useState(types[0]);const [week,setWeek]=useState<'0'|'7'>('0');
  const days=yldDates(addDays(s.date,Number(week)),7);
  const [day,setDay]=useState(s.date);const sel=days.includes(day)?day:days[0];
  const n=yldNight(s,type,sel);
  const edit=(id:string,room?:string)=>owner?()=>open({type:'yld-edit',id,room}):undefined;
  return <div className="yld-m">
    <p className="yld-m-sim">Simulated forecast · rates for new bookings only</p>
    <div className="yld-m-kpis"><KpiCard label="Avg BAR · 7 nights" value={money(sum.avgBar7)}/><KpiCard label="Awaiting publish" value={sum.pending} sub={`${sum.highDemand} high-demand nights`}/></div>
    <ChipFilter label="Room type" options={types.map(t=>({id:t,label:t}))} value={type} onChange={setType}/>
    <ChipFilter label="Week" options={[{id:'0' as const,label:'Next 7 nights'},{id:'7' as const,label:'Following 7'}]} value={week} onChange={setWeek}/>
    <div className="yld-strip" role="group" aria-label="Choose a night">{days.map(d=>{const o=yldOcc(s,d),x=yldNight(s,type,d),dw=new Date(d+'T12:00:00Z').getUTCDay();
      return <button key={d} type="button" aria-pressed={d===sel} className={(d===sel?'on ':'')+(x.suggested!==x.live?'changed':'')} onClick={()=>setDay(d)} aria-label={`${YLD_DOW_NAMES[dw]} ${shortDate(d)}, forecast ${o.pct}%, suggested ${money(x.suggested)}`}>
        <span>{YLD_DOW_NAMES[dw]}</span><strong>{d.slice(8)}</strong><em className={o.pct>=80?'high':o.pct<40?'low':''}>{o.pct}%</em></button>;})}</div>
    <section className="yld-m-card" aria-live="polite">
      <div className="yld-m-head"><div><span>{type} · {YLD_DOW_NAMES[new Date(sel+'T12:00:00Z').getUTCDay()]} {shortDate(sel)}</span><strong>{money(n.suggested)}</strong></div>
        <Pill tone={n.suggested===n.live?'green':'orange'}>{n.suggested===n.live?'Live':'Live '+money(n.live)}</Pill></div>
      <ol className="yld-m-steps">
        <li><span>Base {n.baseSource==='tariff'?'(scheduled tariff)':'(rack rate)'}</span><b>{money(n.base)}</b></li>
        {n.steps.map(x=><li key={x.label}><span>{x.label}</span><b className={x.bps>0?'yld-up':'yld-down'}>{yldPct(x.bps)}</b></li>)}
        {n.clamped&&<li><span>Guardrail {n.clamped}</span><b>{yldPct(n.appliedBps)}</b></li>}
        {n.override&&<li><span>Override · {n.override.reason}</span><b>{money(n.override.rate)}</b></li>}
        <li className="yld-total"><span>Suggested BAR</span><b>{money(n.suggested)}</b></li>
      </ol>
      <p className="yld-m-occ">Forecast {n.occ.pct}% · {n.occ.onBooks} on the books · {n.occ.sellable} sellable</p>
      <div className="yld-m-plans">{y.plans.map(p=>{const a=yldPlanAvailable(s,p,sel,n.occ.pct);return <span key={p.id} className={'yld-m-chip'+(a.ok?'':' off')}>{p.code} <b>{a.ok?money(yldPlanRate(s,p,type,sel,n.suggested)):'closed'}</b></span>;})}</div>
      {n.restrictions.map(r=><p key={r.id} className="yld-m-restr">{r.minStay>1?`Minimum stay ${r.minStay} nights`:''}{r.minStay>1&&r.cta?' · ':''}{r.cta?'Closed to arrival':''}{r.note?` — ${r.note}`:''}</p>)}
      <button type="button" className="btn yld-m-more" onClick={()=>open({type:'yld-night',id:sel,room:type})}>{owner?'Explain / override':'Full explanation'}</button>
    </section>
    <Proposals compact/>
    <h2 className="m-group">SEASONS</h2>
    {y.seasons.map(x=><ListCard key={x.id} title={x.name} sub={`${shortDate(x.from)} – ${shortDate(x.to)} · ${x.types.length?x.types.join(', '):'All types'}`} right={<b className={x.adjBps>0?'yld-up':'yld-down'}>{yldPct(x.adjBps)}</b>} onClick={edit('season',x.id)}/>)}
    {owner&&<ListCard title="Add season" onClick={edit('season')}/>}
    <h2 className="m-group">RULES</h2>
    <ListCard title="Day of week" sub={y.dow.map((v,i)=>v?`${YLD_DOW_NAMES[i]} ${yldPct(v)}`:'').filter(Boolean).join(' · ')||'No weekday rules'} onClick={edit('dow')}/>
    {y.occ.slice().sort((a,b)=>b.occ-a.occ).map(r=><ListCard key={r.id} title={`Occupancy ${r.op==='>='?'≥':'<'} ${r.occ}%`} right={<b className={r.adjBps>0?'yld-up':'yld-down'}>{yldPct(r.adjBps)}</b>} onClick={edit('occ',r.id)}/>)}
    <ListCard title="Guardrails" sub={`Floor ${yldPct(y.guard.floorBps)} · ceiling ${yldPct(y.guard.ceilBps)} · round ${money(y.guard.roundTo)}`} onClick={edit('guard')}/>
    <h2 className="m-group">RESTRICTIONS</h2>
    {y.restrictions.map(r=><ListCard key={r.id} title={`${r.type==='*'?'All types':r.type} · ${shortDate(r.from)}${r.to!==r.from?' – '+shortDate(r.to):''}`} sub={[r.minStay>1?`Min ${r.minStay} nights`:'',r.cta?'Closed to arrival':''].filter(Boolean).join(' · ')} meta={r.note} onClick={edit('restriction',r.id)}/>)}
    {owner&&<ListCard title="Add restriction" onClick={edit('restriction')}/>}
    <h2 className="m-group">RATE PLANS</h2>
    {y.plans.map(p=><ListCard key={p.id} title={`${p.code} · ${p.name}`} sub={p.id==='BAR'?'Reference rate':`BAR ${yldPct(p.adjBps)}${p.dayUse?` · ${p.dayUse.from}–${p.dayUse.to}`:` · min ${p.minNights}`}`} right={<Pill tone={p.active?'green':'neutral'}>{p.active?'Active':'Off'}</Pill>} onClick={edit('plan',p.id)}/>)}
    <h2 className="m-group">HISTORY</h2>
    {y.history.slice(0,6).map(h=><ListCard key={h.id} title={h.area} sub={`${h.before} → ${h.after}`} meta={`${h.actor.split(' · ')[0]} · ${shortDate(h.at.slice(0,10))} ${clockTime(h.at)}${h.reason?' · '+h.reason:''}`}/>)}
    {(owner||propose)&&<StickyFooter><button type="button" className="btn primary" onClick={()=>open({type:'yld-publish'})}>{owner?`Publish rates (${sum.pending})`:'Propose publish'}</button></StickyFooter>}
  </div>;
}
