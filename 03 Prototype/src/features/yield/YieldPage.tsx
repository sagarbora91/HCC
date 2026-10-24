// Module 13 · Tariff & yield — desktop page: rate calendar, rate plans + quote, rules, restrictions, history, proposals.
import React,{useState} from 'react';
import {Upload,Send,Info,Plus,Pencil,Lock} from 'lucide-react';
import {money,yldSlice,yldSummary,yldCalendar,yldWindow,yldOcc,yldRoomTypes,yldPct,yldPlanRate,yldPlanAvailable,yldNight,yldQuote,yldProposals,yldProposalStatus,YLD_DOW_NAMES} from '../../services/mock';
import type {Approval} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate,clockTime} from '../../components/ui';
import {useYldRole,yldDayLabel} from './modals';

const TABS=['Rate calendar','Rate plans','Rules','Restrictions','History'] as const;
const first=(n:string)=>n.split(' · ')[0];

export function YieldPage(){
  const {s,open}=useHcc();const {owner,propose}=useYldRole();
  const [tab,setTab]=useState<string>('Rate calendar');const sum=yldSummary(s);
  const action=owner?<button className="btn primary" onClick={()=>open({type:'yld-publish'})}><Upload size={16}/> Publish rates</button>
    :propose?<button className="btn primary" onClick={()=>open({type:'yld-publish'})}><Send size={16}/> Propose publish</button>:<Pill>View only</Pill>;
  return <><PageTitle title="Tariff & yield" sub="Suggested nightly BAR for the next 14 nights: base rate → season, weekday and occupancy rules → guardrails. Publishing writes effective-dated tariffs; existing bookings keep their agreed rates." action={action}/>
    <div className="info-note yld-note"><Info size={16}/><span><strong>Simulated:</strong> the forecast occupancy curve is demonstration data; nothing is sent to OTAs or the website. {owner?'You can change rules and publish.':propose?'You can propose a publish; the Owner approves it.':'Only the Owner changes rules or publishes.'}</span></div>
    <div className="small-stats yld-stats">
      <div><span>Avg suggested BAR · next 7 nights</span><strong>{money(sum.avgBar7)}</strong></div>
      <div><span>High-demand nights (≥ 80%)</span><strong>{sum.highDemand}</strong></div>
      <div><span>Nights awaiting publish</span><strong>{sum.pending}</strong></div>
      <div><span>Pending proposals</span><strong>{sum.proposals}</strong></div>
    </div>
    <Proposals/>
    <section className="card"><div className="yld-tabs" role="tablist">{TABS.map(t=><button key={t} role="tab" aria-selected={tab===t} className={tab===t?'active':''} onClick={()=>setTab(t)}>{t}</button>)}</div>
      {tab==='Rate calendar'&&<Calendar/>}{tab==='Rate plans'&&<Plans/>}{tab==='Rules'&&<Rules/>}{tab==='Restrictions'&&<Restrictions/>}{tab==='History'&&<History/>}
    </section></>;
}

/** Pending and recent Manager proposals (approvals kind 'yield'). */
export function Proposals({compact}:{compact?:boolean}){
  const {s,uid,run,busy}=useHcc();const {owner}=useYldRole();
  const list=yldProposals(s).slice().reverse().filter(a=>a.status==='Pending'||!compact).slice(0,5);
  if(!list.length)return null;
  return <section className="card yld-proposals"><div className="section-title"><div><h2>Tariff publish requests</h2><p>Raised by a Manager; only the Owner decides. Requests expire on the simulated clock and go stale if tariffs change.</p></div></div>
    <div className="yld-prop-list">{list.map(a=><ProposalRow key={a.id} a={a} owner={owner} uid={uid} busy={busy} run={run} s={s}/>)}</div></section>;
}
function ProposalRow({a,owner,uid,busy,run,s}:{a:Approval;owner:boolean;uid:string;busy:boolean;run:ReturnType<typeof useHcc>['run'];s:ReturnType<typeof useHcc>['s']}){
  const st=yldProposalStatus(s,a);const who=first(s.users.find(u=>u.id===a.actor)?.name??a.actor);
  const own=a.actor===uid;const live=a.status==='Pending';
  return <div className="yld-prop"><div><strong>{String(a.meta?.summary??a.id)}</strong><small>{a.id} · by {who} · {a.reason}{a.expiresAt?` · expires ${shortDate(a.expiresAt.slice(0,10))} ${clockTime(a.expiresAt)}`:''}</small></div>
    <Pill tone={st==='Executed'?'green':st==='Pending'?'blue':'orange'}>{st==='Executed'?'Approved · published':st}</Pill>
    {live&&owner&&!own&&<div className="yld-prop-actions"><button className="btn primary" disabled={busy||st!=='Pending'} onClick={()=>run({type:'YLD_DECIDE',id:a.id,approve:true,key:'yld-decide:'+a.id},'Approved and published')}>Approve & publish</button>
      <button className="btn" disabled={busy} onClick={()=>run({type:'YLD_DECIDE',id:a.id,approve:false,key:'yld-decide:'+a.id},'Request rejected')}>Reject</button></div>}
    {live&&own&&<small className="muted">Your request — the Owner decides.</small>}</div>;
}

function Calendar(){
  const {s,open}=useHcc();const cal=yldCalendar(s),days=yldWindow(s);
  return <div className="padded"><p className="muted yld-lead">Each cell shows the <strong>suggested</strong> BAR; a second line shows the live tariff when it differs. Click a cell for the step-by-step explanation{useYldRole().owner?' or to override':''}.</p>
    <div className="table-wrap yld-cal-wrap"><table className="yld-cal"><thead><tr><th scope="col">Room type</th>{days.map(d=>{const dw=new Date(d+'T12:00:00Z').getUTCDay();return <th key={d} scope="col" className={dw===5||dw===6?'yld-wkend':''}>{YLD_DOW_NAMES[dw]}<small>{shortDate(d)}</small></th>;})}</tr></thead><tbody>
      <tr className="yld-occ-row"><th scope="row">Forecast occupancy<small>simulated</small></th>{days.map(d=>{const o=yldOcc(s,d);return <td key={d}><span className={'yld-occ '+(o.pct>=80?'high':o.pct<40?'low':'mid')}>{o.pct}%</span></td>;})}</tr>
      {cal.map(r=><tr key={r.type}><th scope="row">{r.type}<small>base {money(r.nights[0].base)}</small></th>{r.nights.map(n=><td key={n.date}>
        <button className={'yld-cell'+(n.suggested!==n.live?' changed':'')+(n.override?' override':'')} onClick={()=>open({type:'yld-night',id:n.date,room:r.type})} aria-label={`${r.type} ${yldDayLabel(n.date)}: suggested ${money(n.suggested)}, live ${money(n.live)}`}>
          <strong className={n.suggested>n.base?'yld-up':n.suggested<n.base?'yld-down':''}>{money(n.suggested)}</strong>
          {n.suggested!==n.live&&<small>live {money(n.live)}</small>}
          {(n.restrictions.length>0||n.override)&&<span className="yld-badges">{n.override&&<em>OVR</em>}{n.restrictions.some(x=>x.minStay>1)&&<em>MS{Math.max(...n.restrictions.map(x=>x.minStay))}</em>}{n.restrictions.some(x=>x.cta)&&<em>CTA</em>}</span>}
        </button></td>)}</tr>)}
    </tbody></table></div>
    <p className="footnote">Legend: maroon = above base, green = below base · “live” = tariff new bookings get today · MS = minimum stay · CTA = closed to arrival · OVR = manual override.</p></div>;
}

function Plans(){
  const {s,open}=useHcc();const {owner}=useYldRole();const y=yldSlice(s),types=yldRoomTypes(s),days=yldWindow(s);
  const [day,setDay]=useState(s.date);
  return <div className="padded">
    <div className="table-wrap"><table><thead><tr><th>Plan</th><th>Rule</th><th>Nights sold</th><th>Notes</th><th>Status</th>{owner&&<th/>}</tr></thead><tbody>
      {y.plans.map(p=><tr key={p.id}><td><strong>{p.code}</strong><small>{p.name}</small></td><td>{p.id==='BAR'?'Suggested rate':`BAR ${yldPct(p.adjBps)}`}<small>{p.dayUse?`Day-use ${p.dayUse.from}–${p.dayUse.to} · closes at ${p.dayUse.closeAtOcc}%`:`min ${p.minNights} night${p.minNights===1?'':'s'}`}</small></td>
        <td>{p.nights?p.nights.map(n=>YLD_DOW_NAMES[n]).join(', '):'Every night'}</td><td className="yld-wrap">{p.note}</td><td><Pill tone={p.active?'green':'neutral'}>{p.active?'Active':'Inactive'}</Pill></td>
        {owner&&<td><button className="text-btn" onClick={()=>open({type:'yld-edit',id:'plan',room:p.id})}><Pencil size={14}/> Edit</button></td>}</tr>)}
    </tbody></table></div>
    <div className="section-title yld-sub"><div><h2>Rate sheet</h2><p>Plan rates for one night, derived from that night’s suggested BAR.</p></div>
      <label className="yld-inline">Night<select value={day} onChange={e=>setDay(e.target.value)}>{days.map(d=><option key={d} value={d}>{yldDayLabel(d)}</option>)}</select></label></div>
    <div className="table-wrap"><table><thead><tr><th>Room type</th>{y.plans.map(p=><th key={p.id} className="number">{p.code}</th>)}</tr></thead><tbody>
      {types.map(t=>{const n=yldNight(s,t,day);return <tr key={t}><td><strong>{t}</strong></td>{y.plans.map(p=>{const a=yldPlanAvailable(s,p,day,n.occ.pct);return <td key={p.id} className="number">{a.ok?money(yldPlanRate(s,p,t,day,n.suggested)):<span className="muted" title={a.reason}>Closed</span>}</td>;})}</tr>;})}
    </tbody></table></div>
    <QuoteTool/></div>;
}
function QuoteTool(){
  const {s}=useHcc();const y=yldSlice(s),types=yldRoomTypes(s);
  const [plan,setPlan]=useState('BAR'),[type,setType]=useState(types[0]),[arr,setArr]=useState(s.date),[n,setN]=useState('1');
  const q=yldQuote(s,plan,type,arr,Math.max(1,Math.min(30,Number(n)||1)));
  return <div className="yld-quote"><div className="section-title yld-sub"><div><h2>Quote & check a stay</h2><p>Nightly rates and every restriction the stay breaks (advisory — reservations does not enforce these yet).</p></div></div>
    <div className="form yld-quote-form"><div className="yld-quote-grid">
      <label>Plan<select value={plan} onChange={e=>setPlan(e.target.value)}>{y.plans.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      <label>Room type<select value={type} onChange={e=>setType(e.target.value)}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
      <label>Arrival<input type="date" min={s.date} value={arr} onChange={e=>setArr(e.target.value)}/></label>
      {!q.plan.dayUse&&<label>Nights<input type="number" inputMode="numeric" min="1" max="30" value={n} onChange={e=>setN(e.target.value)}/></label>}</div></div>
    <div className="summary-list">{q.rows.map(r=><div key={r.date}><span>{yldDayLabel(r.date)}{q.plan.dayUse?` · day-use ${q.plan.dayUse.from}–${q.plan.dayUse.to}`:''}<small>BAR {money(r.bar)}</small></span><strong>{money(r.rate)}</strong></div>)}
      <div className="yld-total"><span>Total before synthetic tax</span><strong>{money(q.total)}</strong></div></div>
    {q.issues.length?<ul className="yld-issues" role="alert">{q.issues.map(i=><li key={i}>{i}</li>)}</ul>:<p className="yld-ok">No restriction applies — this stay can be sold.</p>}</div>;
}

function Rules(){
  const {s,open}=useHcc();const {owner}=useYldRole();const y=yldSlice(s);
  const edit=(id:string,room?:string)=>open({type:'yld-edit',id,room});
  return <div className="padded">
    <div className="section-title yld-sub"><div><h2>Seasons</h2><p>Date ranges; the highest priority applies when they overlap.</p></div>{owner&&<button className="btn" onClick={()=>edit('season')}><Plus size={15}/> Add season</button>}</div>
    {y.seasons.length?<div className="table-wrap"><table><thead><tr><th>Season</th><th>Dates</th><th>Room types</th><th className="number">Adjustment</th><th>Priority</th>{owner&&<th/>}</tr></thead><tbody>
      {y.seasons.map(x=><tr key={x.id}><td><strong>{x.name}</strong></td><td>{shortDate(x.from)} – {shortDate(x.to)}</td><td>{x.types.length?x.types.join(', '):'All'}</td><td className="number">{yldPct(x.adjBps)}</td><td>{x.priority}</td>{owner&&<td><button className="text-btn" onClick={()=>edit('season',x.id)}><Pencil size={14}/> Edit</button></td>}</tr>)}
    </tbody></table></div>:<Empty title="No seasons"/>}
    <div className="section-title yld-sub"><div><h2>Day of week</h2><p>Added to every night of that weekday.</p></div>{owner&&<button className="btn" onClick={()=>edit('dow')}><Pencil size={15}/> Edit</button>}</div>
    <div className="yld-dow">{YLD_DOW_NAMES.map((d,i)=><div key={d}><span>{d}</span><strong className={y.dow[i]>0?'yld-up':y.dow[i]<0?'yld-down':''}>{yldPct(y.dow[i])}</strong></div>)}</div>
    <div className="section-title yld-sub"><div><h2>Occupancy tiers</h2><p>Forecast occupancy (simulated) for the night.</p></div>{owner&&<button className="btn" onClick={()=>edit('occ')}><Plus size={15}/> Add tier</button>}</div>
    {y.occ.length?<div className="table-wrap"><table><thead><tr><th>When forecast occupancy is</th><th className="number">Adjustment</th>{owner&&<th/>}</tr></thead><tbody>
      {y.occ.slice().sort((a,b)=>b.occ-a.occ).map(r=><tr key={r.id}><td>{r.op==='>='?'at or above':'below'} {r.occ}%</td><td className="number">{yldPct(r.adjBps)}</td>{owner&&<td><button className="text-btn" onClick={()=>edit('occ',r.id)}><Pencil size={14}/> Edit</button></td>}</tr>)}
    </tbody></table></div>:<Empty title="No occupancy tiers"/>}
    <div className="section-title yld-sub"><div><h2>Guardrails</h2><p>Total adjustment kept within limits, then rounded.</p></div>{owner&&<button className="btn" onClick={()=>edit('guard')}><Pencil size={15}/> Edit</button>}</div>
    <div className="summary-list"><div><span>Floor</span><strong>{yldPct(y.guard.floorBps)}</strong></div><div><span>Ceiling</span><strong>{yldPct(y.guard.ceilBps)}</strong></div><div><span>Rounding</span><strong>{money(y.guard.roundTo)}</strong></div></div>
    {!owner&&<p className="muted yld-lock"><Lock size={14}/> Only the Owner changes rules.</p>}
  </div>;
}

function Restrictions(){
  const {s,open}=useHcc();const {owner}=useYldRole();const y=yldSlice(s);
  return <div className="padded"><div className="section-title yld-sub"><div><h2>Stay restrictions</h2><p>Minimum stay and closed-to-arrival by arrival date. Shown in the calendar and checked by the quote tool.</p></div>{owner&&<button className="btn" onClick={()=>open({type:'yld-edit',id:'restriction'})}><Plus size={15}/> Add restriction</button>}</div>
    {y.restrictions.length?<div className="table-wrap"><table><thead><tr><th>Room type</th><th>Arrivals</th><th>Minimum stay</th><th>Closed to arrival</th><th>Note</th>{owner&&<th/>}</tr></thead><tbody>
      {y.restrictions.map(r=><tr key={r.id}><td><strong>{r.type==='*'?'All types':r.type}</strong></td><td>{shortDate(r.from)}{r.to!==r.from?' – '+shortDate(r.to):''}</td><td>{r.minStay>1?`${r.minStay} nights`:'—'}</td><td>{r.cta?<Pill tone="orange">Closed</Pill>:'—'}</td><td className="yld-wrap">{r.note||'—'}</td>
        {owner&&<td><button className="text-btn" onClick={()=>open({type:'yld-edit',id:'restriction',room:r.id})}><Pencil size={14}/> Edit</button></td>}</tr>)}
    </tbody></table></div>:<Empty title="No restrictions" body="Add a minimum stay or close arrivals for peak dates."/>}</div>;
}

function History(){
  const {s}=useHcc();const y=yldSlice(s);
  return <div className="padded">
    <div className="section-title yld-sub"><div><h2>Publish log</h2><p>Every tariff row written, with the live rate it replaced.</p></div></div>
    {y.publishes.length?y.publishes.map(p=><details key={p.id} className="yld-pub"><summary><strong>{p.id}</strong> · {shortDate(p.from)} – {shortDate(p.to)} · {p.types.join(', ')} · {p.rows.filter(r=>r.before!==r.after).length} rates changed · {first(p.actor)} {clockTime(p.at)}{p.approval?` · approved request ${p.approval}`:''}</summary>
      <div className="table-wrap"><table><thead><tr><th>Night</th><th>Type</th><th className="number">Before</th><th className="number">After</th></tr></thead><tbody>
        {p.rows.map(r=><tr key={r.type+r.date}><td>{yldDayLabel(r.date)}{r.closing?<small>closing row — keeps the rate after the window</small>:r.before===r.after?<small>anchor row — keeps this night unchanged</small>:null}</td><td>{r.type}</td><td className="number">{money(r.before)}</td><td className="number">{money(r.after)}</td></tr>)}
      </tbody></table></div></details>):<Empty title="Nothing published yet" body="Publish suggested rates to write effective-dated tariffs."/>}
    <div className="section-title yld-sub"><div><h2>Change history</h2><p>Rules, overrides, proposals and publishes — who, when (simulated clock), before → after.</p></div></div>
    <div className="table-wrap"><table><thead><tr><th>When</th><th>Who</th><th>Area</th><th>Before</th><th>After</th><th>Reason</th></tr></thead><tbody>
      {y.history.map(h=><tr key={h.id}><td>{shortDate(h.at.slice(0,10))}<small>{clockTime(h.at)}</small></td><td>{first(h.actor)}</td><td>{h.area}</td><td className="yld-wrap">{h.before}</td><td className="yld-wrap">{h.after}</td><td className="yld-wrap">{h.reason||'—'}</td></tr>)}
    </tbody></table></div></div>;
}
