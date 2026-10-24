// Module 13 · Tariff & yield dialogs (render as full-screen sheets on phones).
// 'yld-night' (id = date, room = room type) · 'yld-publish' · 'yld-edit' (id = season|occ|restriction|plan|dow|guard, room = record ID; empty = new).
import React,{useState} from 'react';
import {Check,Send,Trash2} from 'lucide-react';
import {money,addDays,yldSlice,yldNight,yldPct,yldPlanPublish,yldPlanRate,yldPlanAvailable,yldRoomTypes,yldWindow,YLD_DOW_NAMES,YLD_WINDOW,YLD_ROUND_OPTIONS} from '../../services/mock';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {Pill,shortDate} from '../../components/ui';

const rupees=(v:string)=>Math.round(Number(v)*100);
const pctToBps=(v:string)=>Math.round(Number(v)*100);
export const yldDayLabel=(d:string)=>YLD_DOW_NAMES[new Date(d+'T12:00:00Z').getUTCDay()]+' '+shortDate(d);

/** Role helper shared by pages and dialogs. */
export function useYldRole(){const {permission}=useHcc();const owner=permission('admin')&&permission('reports');return {owner,propose:!owner&&permission('approve'),view:permission('reports')};}

export function YldNightModal({id,room}:ModalProps){
  const {s,run,modal,closeModal,busy}=useHcc();const {owner}=useYldRole();
  const date=id||s.date,type=room||yldRoomTypes(s)[0];const n=yldNight(s,type,date);
  const [rate,setRate]=useState(String((n.override?.rate??n.computed)/100));const [reason,setReason]=useState('');
  const editable=date>=s.date;
  return <><div className="modal-heading"><div className="eyebrow">{type.toUpperCase()} · {yldDayLabel(date).toUpperCase()}</div><h2>Why {money(n.suggested)}?</h2><p>Base rate, then each rule, then guardrails and rounding. Live tariff for new bookings: <strong>{money(n.live)}</strong>{n.live===n.suggested?' (already published)':' (publish to apply)'}.</p></div>
    <div className="summary-list yld-steps">
      <div><span>Base · {n.baseSource==='tariff'?`scheduled tariff from ${shortDate(n.baseFrom!)}`:'room-type rack rate'}</span><strong>{money(n.base)}</strong></div>
      {n.steps.map(x=><div key={x.label}><span>{x.label}<small>{x.detail}</small></span><strong className={x.bps>0?'yld-up':'yld-down'}>{yldPct(x.bps)}</strong></div>)}
      {!n.steps.length&&<div><span>No season, weekday or occupancy rule applies</span><strong>±0%</strong></div>}
      {n.clamped&&<div><span>Guardrail: {n.clamped} ({yldPct(n.rawBps)} → {yldPct(n.appliedBps)})</span><strong>{yldPct(n.appliedBps)}</strong></div>}
      <div><span>Computed (rounded to {money(yldSlice(s).guard.roundTo)})</span><strong>{money(n.computed)}</strong></div>
      {n.override&&<div><span>Manual override · {n.override.reason}</span><strong>{money(n.override.rate)}</strong></div>}
      <div className="yld-total"><span>Suggested BAR</span><strong>{money(n.suggested)}</strong></div>
    </div>
    <p className="footnote">Forecast occupancy {n.occ.pct}% · {n.occ.onBooks} on the books, {n.occ.forecast} forecast (simulated), {n.occ.sellable} sellable rooms.</p>
    <h3 className="yld-h3">Rate plans this night</h3>
    <div className="yld-plan-chips">{yldSlice(s).plans.map(p=>{const a=yldPlanAvailable(s,p,date,n.occ.pct);return <div key={p.id} className={'yld-plan-chip'+(a.ok?'':' off')}><strong>{p.code}</strong><span>{money(yldPlanRate(s,p,type,date,n.suggested))}</span><small>{a.ok?(p.dayUse?`${p.dayUse.from}–${p.dayUse.to}`:p.minNights>1?`min ${p.minNights} nights`:'open'):a.reason}</small></div>;})}</div>
    {n.restrictions.length>0&&<><h3 className="yld-h3">Restrictions for arrivals</h3><ul className="yld-list">{n.restrictions.map(r=><li key={r.id}>{r.minStay>1&&<Pill tone="orange">Min stay {r.minStay}</Pill>} {r.cta&&<Pill tone="orange">Closed to arrival</Pill>} {r.note}</li>)}</ul></>}
    {owner&&editable?<form className="form yld-override" onSubmit={e=>{e.preventDefault();run({type:'YLD_OVERRIDE',roomType:type,date,rate:rupees(rate),reason,key:modal!.key},'Override saved — publish to make it live',{close:true});}}>
      <h3 className="yld-h3">Manual override (Owner)</h3>
      <div className="form-row"><label>Nightly rate (₹)<input type="number" inputMode="decimal" enterKeyHint="next" min="100" step="1" required value={rate} onChange={e=>setRate(e.target.value)}/></label>
        <label>Reason<input required minLength={3} maxLength={80} enterKeyHint="done" value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Corporate block"/></label></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
        {n.override&&<button type="button" className="btn" disabled={busy||reason.trim().length<3} title="Enter a reason to clear" onClick={()=>run({type:'YLD_OVERRIDE',roomType:type,date,rate:null,reason,key:modal!.key+':clear'},'Override cleared',{close:true})}>Clear override</button>}
        <button className="btn primary" disabled={busy}>Save override <Check size={16}/></button></div>
    </form>:<div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div>}
  </>;
}

export function YldPublishModal(){
  const {s,run,modal,closeModal,busy}=useHcc();const {owner,propose}=useYldRole();
  const types=yldRoomTypes(s),win=yldWindow(s),last=win[win.length-1];
  const closed=s.closedDates.includes(s.date),first=closed?addDays(s.date,1):s.date;
  const [from,setFrom]=useState(first),[to,setTo]=useState(last),[pick,setPick]=useState<string[]>(types),[reason,setReason]=useState('');
  const valid=from>=first&&to>=from&&to<=last&&pick.length>0;
  const plan=valid?yldPlanPublish(s,from,to,pick):{rows:[],tariffs:[]};
  const rows=plan.rows.filter(r=>r.before!==r.after),closing=plan.rows.filter(r=>r.before===r.after);
  const toggle=(t:string)=>setPick(p=>p.includes(t)?p.filter(x=>x!==t):[...p,t]);
  const cmd={from,to,types:pick,reason,key:modal!.key};
  return <><div className="modal-heading"><div className="eyebrow">TARIFF & YIELD</div><h2>{owner?'Publish suggested rates':'Propose a tariff publish'}</h2>
      <p>{owner?'Writes effective-dated BAR tariffs that new bookings use.':'Sends the suggested rates to the Owner for approval.'} Existing bookings and posted room lines keep their agreed rates.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:owner?'YLD_PUBLISH':'YLD_PROPOSE',...cmd},owner?'Tariffs published':'Request sent to the Owner',{close:true});}}>
      <div className="form-row"><label>First night<input type="date" min={first} max={last} required value={from} onChange={e=>setFrom(e.target.value)}/></label>
        <label>Last night<input type="date" min={from} max={last} required value={to} onChange={e=>setTo(e.target.value)}/></label></div>
      <fieldset className="yld-types"><legend>Room types</legend>{types.map(t=><label key={t} className="yld-check"><input type="checkbox" checked={pick.includes(t)} onChange={()=>toggle(t)}/>{t}</label>)}</fieldset>
      <label>Reason (kept in history){!owner&&' — shown to the Owner'}<input maxLength={120} enterKeyHint="done" value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Diwali demand"/></label>
      {!valid&&<p className="yld-err" role="alert">Choose nights between {shortDate(first)} and {shortDate(last)} ({YLD_WINDOW}-night window) and at least one room type.</p>}
      {valid&&<div className="yld-preview"><strong>{rows.length} nightly rate{rows.length===1?'':'s'} will change{closing.length?` · ${closing.length} anchor row${closing.length===1?'':'s'} keep unchanged nights (and the nights after ${shortDate(to)}) at their current rate`:''}</strong>
        {rows.length>0?<div className="table-wrap yld-preview-table"><table><thead><tr><th>Night</th><th>Type</th><th className="number">Live now</th><th className="number">New</th></tr></thead><tbody>
          {rows.map(r=><tr key={r.type+r.date}><td>{yldDayLabel(r.date)}</td><td>{r.type}</td><td className="number">{money(r.before)}</td><td className="number"><strong className={r.after>r.before?'yld-up':'yld-down'}>{money(r.after)}</strong></td></tr>)}
        </tbody></table></div>:<p className="muted">Live tariffs already match the suggested rates.</p>}</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||!valid||!rows.length||(!owner&&!propose)}>{owner?<>Publish {rows.length} rate{rows.length===1?'':'s'} <Check size={16}/></>:<>Send for approval <Send size={16}/></>}</button></div>
    </form></>;
}

export function YldEditModal({id,room}:ModalProps){
  const kind=id||'season';
  return kind==='season'?<SeasonForm rid={room}/>:kind==='occ'?<OccForm rid={room}/>:kind==='restriction'?<RestrictionForm rid={room}/>:kind==='plan'?<PlanForm rid={room}/>:kind==='dow'?<DowForm/>:<GuardForm/>;
}
function Actions({label,remove}:{label:string;remove?:()=>void}){
  const {closeModal,busy}=useHcc();
  return <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>{remove&&<button type="button" className="btn" disabled={busy} onClick={remove}><Trash2 size={16}/> Remove</button>}<button className="btn primary" disabled={busy}>{label} <Check size={16}/></button></div>;
}
const Heading=({title,sub}:{title:string;sub:string})=><div className="modal-heading"><div className="eyebrow">TARIFF & YIELD · OWNER</div><h2>{title}</h2><p>{sub}</p></div>;

function SeasonForm({rid}:{rid?:string}){
  const {s,run,modal}=useHcc();const x=yldSlice(s).seasons.find(x=>x.id===rid);const types=yldRoomTypes(s);
  const [name,setName]=useState(x?.name??''),[from,setFrom]=useState(x?.from??s.date),[to,setTo]=useState(x?.to??addDays(s.date,6)),[pct,setPct]=useState(String((x?.adjBps??1000)/100)),[pick,setPick]=useState<string[]>(x?.types??[]),[prio,setPrio]=useState(String(x?.priority??1)),[reason,setReason]=useState('');
  const save=(e:React.FormEvent)=>{e.preventDefault();run({type:'YLD_SEASON_SAVE',id:x?.id,name,from,to,adjBps:pctToBps(pct),types:pick,priority:Number(prio),reason,key:modal!.key},'Season saved',{close:true});};
  return <><Heading title={x?'Edit season':'Add season'} sub="A date range that raises or lowers the base rate. When seasons overlap, the higher priority wins."/>
    <form className="form" onSubmit={save}>
      <label>Season name<input required minLength={2} maxLength={60} value={name} onChange={e=>setName(e.target.value)}/></label>
      <div className="form-row"><label>From<input type="date" required value={from} onChange={e=>setFrom(e.target.value)}/></label><label>To<input type="date" required min={from} value={to} onChange={e=>setTo(e.target.value)}/></label></div>
      <div className="form-row"><label>Adjustment (%)<input type="number" inputMode="decimal" step="0.5" min="-50" max="100" required value={pct} onChange={e=>setPct(e.target.value)}/></label><label>Priority<input type="number" inputMode="numeric" min="1" max="9" required value={prio} onChange={e=>setPrio(e.target.value)}/></label></div>
      <fieldset className="yld-types"><legend>Room types (none ticked = all)</legend>{types.map(t=><label key={t} className="yld-check"><input type="checkbox" checked={pick.includes(t)} onChange={()=>setPick(p=>p.includes(t)?p.filter(v=>v!==t):[...p,t])}/>{t}</label>)}</fieldset>
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save season" remove={x?()=>run({type:'YLD_SEASON_SAVE',id:x.id,remove:true,reason,key:modal!.key+':rm'},'Season removed',{close:true}):undefined}/>
    </form></>;
}
function OccForm({rid}:{rid?:string}){
  const {s,run,modal}=useHcc();const x=yldSlice(s).occ.find(x=>x.id===rid);
  const [op,setOp]=useState<string>(x?.op??'>='),[occ,setOcc]=useState(String(x?.occ??85)),[pct,setPct]=useState(String((x?.adjBps??500)/100)),[reason,setReason]=useState('');
  return <><Heading title={x?'Edit occupancy rule':'Add occupancy rule'} sub="Uses forecast occupancy for the night. Only the highest “at or above” tier met and the lowest “below” tier met apply."/>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'YLD_OCC_SAVE',id:x?.id,op,occ:Number(occ),adjBps:pctToBps(pct),reason,key:modal!.key},'Occupancy rule saved',{close:true});}}>
      <div className="form-row"><label>When forecast occupancy is<select value={op} onChange={e=>setOp(e.target.value)}><option value=">=">at or above</option><option value="<">below</option></select></label>
        <label>Threshold (%)<input type="number" inputMode="numeric" min="1" max="100" required value={occ} onChange={e=>setOcc(e.target.value)}/></label></div>
      <label>Adjustment (%) — positive for “at or above”, negative for “below”<input type="number" inputMode="decimal" step="0.5" min="-50" max="50" required value={pct} onChange={e=>setPct(e.target.value)}/></label>
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save rule" remove={x?()=>run({type:'YLD_OCC_SAVE',id:x.id,remove:true,reason,key:modal!.key+':rm'},'Rule removed',{close:true}):undefined}/>
    </form></>;
}
function RestrictionForm({rid}:{rid?:string}){
  const {s,run,modal}=useHcc();const x=yldSlice(s).restrictions.find(x=>x.id===rid);
  const [type,setType]=useState(x?.type??'*'),[from,setFrom]=useState(x?.from??s.date),[to,setTo]=useState(x?.to??s.date),[min,setMin]=useState(String(x?.minStay??2)),[cta,setCta]=useState(!!x?.cta),[note,setNote]=useState(x?.note??''),[reason,setReason]=useState('');
  return <><Heading title={x?'Edit restriction':'Add restriction'} sub="Applies to arrivals on the chosen dates. Advisory in this prototype: shown in quotes and the rate calendar."/>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'YLD_RESTRICTION_SAVE',id:x?.id,roomType:type,from,to,minStay:Number(min),cta,note,reason,key:modal!.key},'Restriction saved',{close:true});}}>
      <label>Room type<select value={type} onChange={e=>setType(e.target.value)}><option value="*">All room types</option>{yldRoomTypes(s).map(t=><option key={t}>{t}</option>)}</select></label>
      <div className="form-row"><label>First arrival date<input type="date" required value={from} onChange={e=>setFrom(e.target.value)}/></label><label>Last arrival date<input type="date" required min={from} value={to} onChange={e=>setTo(e.target.value)}/></label></div>
      <div className="form-row"><label>Minimum stay (nights)<input type="number" inputMode="numeric" min="1" max="14" required value={min} onChange={e=>setMin(e.target.value)}/></label>
        <label className="yld-check yld-check-row"><input type="checkbox" checked={cta} onChange={e=>setCta(e.target.checked)}/>Closed to arrival</label></div>
      <label>Note<input maxLength={80} value={note} onChange={e=>setNote(e.target.value)}/></label>
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save restriction" remove={x?()=>run({type:'YLD_RESTRICTION_SAVE',id:x.id,remove:true,reason,key:modal!.key+':rm'},'Restriction removed',{close:true}):undefined}/>
    </form></>;
}
function PlanForm({rid}:{rid?:string}){
  const {s,run,modal}=useHcc();const p=yldSlice(s).plans.find(p=>p.id===rid)??yldSlice(s).plans[0];
  const [pct,setPct]=useState(String(p.adjBps/100)),[min,setMin]=useState(String(p.minNights)),[active,setActive]=useState(p.active),[close,setClose]=useState(String(p.dayUse?.closeAtOcc??90)),[reason,setReason]=useState('');
  const bar=p.id==='BAR';
  return <><Heading title={'Rate plan · '+p.name} sub={p.note}/>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'YLD_PLAN_SAVE',id:p.id,adjBps:pctToBps(pct),minNights:Number(min),active,closeAtOcc:p.dayUse?Number(close):undefined,reason,key:modal!.key},'Rate plan saved',{close:true});}}>
      <div className="form-row"><label>Adjustment on BAR (%)<input type="number" inputMode="decimal" step="0.5" min="-90" max="50" required disabled={bar} value={pct} onChange={e=>setPct(e.target.value)}/></label>
        <label>Minimum nights<input type="number" inputMode="numeric" min={p.dayUse?0:1} max={p.dayUse?0:30} required disabled={!!p.dayUse} value={min} onChange={e=>setMin(e.target.value)}/></label></div>
      {p.dayUse&&<label>Close day-use when forecast occupancy reaches (%)<input type="number" inputMode="numeric" min="50" max="101" required value={close} onChange={e=>setClose(e.target.value)}/></label>}
      <label className="yld-check yld-check-row"><input type="checkbox" checked={active} disabled={bar} onChange={e=>setActive(e.target.checked)}/>Plan is active</label>
      {bar&&<p className="muted">BAR is the reference rate: it is fixed at 0% and always active. Change seasons and rules instead.</p>}
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save plan"/>
    </form></>;
}
function DowForm(){
  const {s,run,modal}=useHcc();const [v,setV]=useState(yldSlice(s).dow.map(x=>String(x/100)));const [reason,setReason]=useState('');
  return <><Heading title="Day-of-week rules" sub="Percentage added to the base rate for each night of the week."/>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'YLD_DOW_SET',dow:v.map(pctToBps),reason,key:modal!.key},'Day-of-week rules saved',{close:true});}}>
      <div className="yld-dow-grid">{YLD_DOW_NAMES.map((d,i)=><label key={d}>{d} (%)<input type="number" inputMode="decimal" step="0.5" min="-30" max="50" required value={v[i]} onChange={e=>setV(x=>x.map((y,j)=>j===i?e.target.value:y))}/></label>)}</div>
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save rules"/>
    </form></>;
}
function GuardForm(){
  const {s,run,modal}=useHcc();const g=yldSlice(s).guard;
  const [floor,setFloor]=useState(String(g.floorBps/100)),[ceil,setCeil]=useState(String(g.ceilBps/100)),[round,setRound]=useState(String(g.roundTo)),[reason,setReason]=useState('');
  return <><Heading title="Guardrails" sub="The total of all adjustments is kept between the floor and the ceiling, then rounded."/>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'YLD_GUARD_SET',floorBps:pctToBps(floor),ceilBps:pctToBps(ceil),roundTo:Number(round),reason,key:modal!.key},'Guardrails saved',{close:true});}}>
      <div className="form-row"><label>Floor (%)<input type="number" inputMode="decimal" step="0.5" min="-50" max="0" required value={floor} onChange={e=>setFloor(e.target.value)}/></label><label>Ceiling (%)<input type="number" inputMode="decimal" step="0.5" min="0" max="100" required value={ceil} onChange={e=>setCeil(e.target.value)}/></label></div>
      <label>Round to<select value={round} onChange={e=>setRound(e.target.value)}>{YLD_ROUND_OPTIONS.map(r=><option key={r} value={r}>{money(r)}</option>)}</select></label>
      <label>Reason (history)<input maxLength={120} value={reason} onChange={e=>setReason(e.target.value)}/></label>
      <Actions label="Save guardrails"/>
    </form></>;
}
