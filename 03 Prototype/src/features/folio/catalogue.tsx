// MA-2 · Catalogue UI: the charge dialog with the extras catalogue in front ("Other (describe)" stays free text, DEC-MA-4), the
// discount dialog with reasons from the master, the meal-plan picker of a booking (booking/folio action, desktop and phone) and the
// "Plans & extras" report page (catalogue vs free text, stays by plan, discounts by reason). The lists themselves are edited in
// Master configuration (MstLink "Manage list…", Owner-only money masters). Domain: domain/streams/catalogue.ts. Styles: styles/m-catalogue.css.
import React,{useState} from 'react';
import {ShieldCheck,Utensils,Download,Info} from 'lucide-react';
import {money,totals,gstPct,gstLineTax,gstForFolioAdjustment,GST_CHARGE_CATEGORIES,GST_ILLUSTRATIVE,rbacDiscountLimit,
  catExtras,catExtra,catChargePrefill,catChargeBps,catReasonsFor,catReason,catReasonLimits,catPlanOptions,catPlan,catPlanLabel,catPlanAddOn,
  catPlanGuests,catPlanQuote,catMealsLabel,catChargeSplit,catPlanReport,catDiscountReport,catReportCsv,CAT_UNITS,CAT_QTY_MAX} from '../../services/mock';
import type {Booking} from '../../services/mock';
import type {FeatureRegistration} from '../../app/registry';
import {bookingRef} from '../../domain/bookingRef';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,Pill,shortDate} from '../../components/ui';
import {MstLink} from '../masters/MasterList';
import {saveCsv} from '../../app/native';

type GstCat='food'|'laundry'|'banquet'|'other';
const OTHER='__other';
const paise=(t:string)=>Math.round(Number(t)*100);
const rupees=(p?:number)=>p===undefined?'':String(p/100);
const unitLabel=(u?:string)=>CAT_UNITS.find(x=>x.value===u)?.label??'';
const Missing=()=><Empty title="This record is unavailable" body="The booking may have been removed when a scenario was reloaded."/>;

/** Modal 'charge' {id: bookingId}: catalogue item (prefilled, editable) or "Other (describe)" free text. */
export function CatChargeModal({id}:{id?:string}){
  const {s,run,booking,modal,busy}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const items=catExtras(s,true);
  const [pick,setPick]=useState('');const [qty,setQty]=useState('1');
  const [label,setLabel]=useState('');const [amt,setAmt]=useState('');const [gst,setGst]=useState<GstCat|''>('');
  const [problem,setProblem]=useState('');
  if(!b)return <Missing/>;
  const item=pick&&pick!==OTHER?catExtra(s,pick):undefined;
  const prefill=(x:string,q:number)=>{const it=catExtra(s,x);if(!it)return;const p=catChargePrefill(s,it,q);setLabel(p.label);setAmt(rupees(p.amount));setGst(p.gst);};
  const choose=(x:string)=>{setPick(x);setProblem('');if(x===OTHER){setLabel('');setAmt('');setGst('');setQty('1');}else if(x){setQty('1');prefill(x,1);}};
  const changeQty=(t:string)=>{setQty(t);const q=Number(t);if(item&&Number.isInteger(q)&&q>=1&&q<=CAT_QTY_MAX)prefill(item.id,q);};
  const a=paise(amt),cat=(gst||'other') as GstCat,bps=catChargeBps(s,cat);
  const post=(e:React.FormEvent)=>{e.preventDefault();
    if(!pick)return setProblem('Choose an item, or “Other (describe)”.');
    if(!label.trim())return setProblem('Describe the service.');
    if(!Number.isSafeInteger(a)||a<=0)return setProblem('Enter an amount greater than zero.');
    void run(item?{type:'CAT_CHARGE',id:b.id,extraId:item.id,qty:Number(qty)||1,label:label.trim(),amount:a,gst:cat,key:modal!.key}
      :{type:'CHARGE',id:b.id,label:label.trim(),amount:a,...(gst?{gst}:{}),key:modal!.key},'Service charge posted',{after:()=>booking(b.id)});};
  return <><div className="modal-heading"><h2>Add a service charge</h2><p>{b.guest} · {bookingRef(b)}</p></div>
    <form className="form cat-form" onSubmit={post} noValidate>
      <label>Item<select value={pick} onChange={e=>choose(e.target.value)} aria-describedby="cat-item-help">
        <option value="">Choose from the catalogue…</option>
        {items.map(x=>{const p=catChargePrefill(s,x);return <option key={x.id} value={x.id}>{x.name}{p.price!==undefined?` · ${money(p.price)} ${unitLabel(x.unit)}`:''}</option>;})}
        <option value={OTHER}>Other (describe)</option></select>
        <small id="cat-item-help" className="muted">Name, price and GST class come from the catalogue and can be changed. “Other (describe)” posts free text — no approval needed.</small></label>
      {pick&&<>
        {item&&<label>Quantity<input type="number" inputMode="numeric" min={1} max={CAT_QTY_MAX} step={1} value={qty} onChange={e=>changeQty(e.target.value)}/></label>}
        <label>Service description<input required maxLength={120} value={label} placeholder="e.g. Laundry service" onChange={e=>setLabel(e.target.value)}/></label>
        <label>Amount before GST (₹)<input type="number" inputMode="decimal" min="0.01" step="0.01" required value={amt} onChange={e=>setAmt(e.target.value)}/></label>
        <label>GST category<select value={gst} onChange={e=>setGst(e.target.value as GstCat|'')}>
          {!item&&<option value="">Automatic (from the description)</option>}
          {GST_CHARGE_CATEGORIES.map(c=><option key={c.id} value={c.id}>{c.label}</option>)}</select></label>
        {a>0&&(item||gst)&&<p className="footnote">GST {gstPct(bps)} = {money(gstLineTax(a,bps))} · total {money(a+gstLineTax(a,bps))}{item?` · catalogue ${item.code}`:' · free text'}</p>}
      </>}
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="cat-manage"><MstLink spec="extras" label="Manage list… · Extras and services"/></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>Post charge</button></div>
    </form></>;
}

/** Modal 'discount' {id: bookingId}: reason from the master (caps, approval) or "Other" with a typed reason (the DISCOUNT rules). */
export function CatDiscountModal({id}:{id?:string}){
  const {s,uid,user,run,booking,modal,busy}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const [code,setCode]=useState('');const [amt,setAmt]=useState('');const [note,setNote]=useState('');const [problem,setProblem]=useState('');
  if(!b)return <Missing/>;
  const reasons=catReasonsFor(s,user.role),r=code&&code!==OTHER?catReason(s,code):undefined;
  const lim=rbacDiscountLimit(s,uid),caps=r?catReasonLimits(r,b):undefined,a=paise(amt);
  const post=(e:React.FormEvent)=>{e.preventDefault();
    if(!code)return setProblem('Choose a reason, or “Other”.');
    if(!Number.isSafeInteger(a)||a<=0)return setProblem('Enter an amount greater than zero.');
    if(code===OTHER&&!note.trim())return setProblem('A reason is required.');
    void run(r?{type:'CAT_DISCOUNT',id:b.id,amount:a,reasonCode:r.code,...(note.trim()?{note:note.trim()}:{}),key:modal!.key}
      :{type:'DISCOUNT',id:b.id,amount:a,reason:note.trim(),key:modal!.key},'Discount recorded or submitted for approval',{after:()=>booking(b.id)});};
  return <><div className="modal-heading"><h2>Add a discount</h2><p>{b.guest} · {bookingRef(b)} · charges {money(Math.max(0,totals(b).net))}</p></div>
    <form className="form cat-form" onSubmit={post} noValidate>
      <label>Reason<select value={code} onChange={e=>{setCode(e.target.value);setProblem('');}}>
        <option value="">Choose a reason…</option>{reasons.map(x=><option key={x.code} value={x.code}>{x.name}</option>)}<option value={OTHER}>Other (type the reason)</option></select></label>
      {caps&&<p className="footnote">“{r!.name}”: at most {(r!.maxPct??10000)/100} % of the charges ({money(caps.max)}){caps.approvalAbove!==undefined?` · approval above ${(r!.approvalAbovePct??0)/100} % (${money(caps.approvalAbove)})${user.role==='Owner'?' — the Owner decides directly':''}`:''}.</p>}
      <label>Amount before GST (₹)<input type="number" inputMode="decimal" min="0.01" step="0.01" required value={amt} onChange={e=>setAmt(e.target.value)}/></label>
      <label>{code===OTHER?'Reason for discount':'Note (optional)'}<input maxLength={code===OTHER?120:100} value={note} placeholder={code===OTHER?'e.g. Service recovery':'e.g. Late towels'} onChange={e=>setNote(e.target.value)}/></label>
      <p className="footnote">GST on the discount follows the room-night rate ({gstPct(gstForFolioAdjustment(s,b,s.date).bps)}).</p>
      <div className="info-note"><ShieldCheck size={16}/>{lim===undefined?'No discount limit for the Owner — it posts at once.':<>Your limit is {money(lim)}. Above it, another authorized user must approve within 24 simulated hours.</>} Any folio change while it is pending invalidates the request.</div>
      {problem&&<p className="field-error" role="alert">{problem}</p>}
      <div className="cat-manage"><MstLink spec="discountReasons" label="Manage list… · Discount reasons"/></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy}>Submit discount</button></div>
    </form></>;
}

const planOpen=(b:Booking)=>['Held','Confirmed','In house'].includes(b.status);
/** Booking / folio dialog action (desktop and phone): the booking's meal plan, with a change button while the booking is open. */
export function CatPlanAction({b}:{b:Booking}){
  const {s,open,permission}=useHcc();
  const may=planOpen(b)&&permission(b.status==='In house'?'billing':'reservations');
  if(!may&&!b.plan)return null;
  const label=`Meal plan: ${catPlanLabel(s,b.plan)}`;
  return may?<button type="button" className="btn cat-plan-btn" onClick={()=>open({type:'cat-plan',id:b.id})}><Utensils size={16}/> {label}</button>
    :<span className="muted cat-plan-btn"><Utensils size={14}/> {label}</span>;
}
/** Modal 'cat-plan' {id: bookingId}: choose the booking's meal plan (applies from the next room night posted). */
export function CatPlanModal({id}:{id?:string}){
  const {s,run,booking,modal,busy}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const [code,setCode]=useState(b?.plan??'');
  if(!b)return <Missing/>;
  const room=s.rooms.find(r=>r.id===b.room),opts=catPlanOptions(s,room?.type,b.plan),p=code?catPlan(s,code):undefined;
  const from=b.status==='In house'&&s.date>b.arrival?s.date:b.arrival,guests=catPlanGuests(b);
  const quote=p?catPlanQuote(s,p.code,guests,from,b.departure):0;
  return <><div className="modal-heading"><div className="eyebrow">{bookingRef(b)} · {b.guest}</div><h2>Meal plan</h2><p>Room {b.room}{room?` · ${room.type}`:''} · {guests} guest{guests===1?'':'s'}</p></div>
    <form className="form cat-form" onSubmit={e=>{e.preventDefault();void run({type:'CAT_PLAN_SET',id:b.id,plan:code,key:modal!.key},'Meal plan saved',{after:()=>booking(b.id)});}}>
      <fieldset className="cat-plans"><legend>Plan</legend>
        <label className="cat-plan-opt"><input type="radio" name="plan" checked={code===''} onChange={()=>setCode('')}/><span><strong>No plan</strong><small>Room only, no plan line</small></span></label>
        {opts.map(o=>{const x=catPlan(s,o.value)!;return <label key={o.value} className="cat-plan-opt"><input type="radio" name="plan" checked={code===o.value} onChange={()=>setCode(o.value)}/>
          <span><strong>{o.label}</strong><small>{catMealsLabel(x)} · {money(catPlanAddOn(x,s.date))} per guest per night · GST {x.gst==='bundled'?'bundled with the room':'separate restaurant line'}</small></span></label>;})}
      </fieldset>
      {p&&quote>0&&<p className="footnote">Plan add-on from {shortDate(from)} to {shortDate(b.departure)}: {money(quote)} before GST, posted night by night with the room.</p>}
      {b.status==='In house'&&<div className="info-note"><Info size={16}/>Nights already posted keep their lines; the new plan applies from the next room night posted.</div>}
      <p className="footnote">GST treatment of meal plans: {GST_ILLUSTRATIVE}.</p>
      <div className="cat-manage"><MstLink spec="mealPlans" label="Manage list… · Meal plans"/></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back</button><button className="btn primary" disabled={busy||code===(b.plan??'')}>Save plan</button></div>
    </form></>;
}

/** Page 'plans-extras' (Closing & reports): catalogue vs free-text charges, stays by meal plan, discounts by reason, CSV. */
export function PlansExtrasPage(){
  const {s,toast,display}=useHcc();
  const [from,setFrom]=useState(s.date.slice(0,8)+'01');const [to,setTo]=useState(s.date);
  const ok=/^\d{4}-\d{2}-\d{2}$/.test(from)&&/^\d{4}-\d{2}-\d{2}$/.test(to)&&from<=to;
  const split=ok?catChargeSplit(s,from,to):undefined,plans=ok?catPlanReport(s,from,to):[],disc=ok?catDiscountReport(s,from,to):[];
  const phone=display.isMobile;
  const table=(head:string[],rows:(string|number)[][],money_:boolean[])=>phone
    ?<div className="cat-cards">{rows.map((r,i)=><div key={i} className="cat-card"><strong>{r[0]}</strong>{r.slice(1).map((v,j)=><span key={j}><small>{head[j+1]}</small> {money_[j+1]?money(Number(v)):v}</span>)}</div>)}</div>
    :<div className="table-wrap"><table><thead><tr>{head.map((h,i)=><th key={h} className={i?'number':''}>{h}</th>)}</tr></thead>
      <tbody>{rows.map((r,i)=><tr key={i}>{r.map((v,j)=><td key={j} className={j?'number':''}>{money_[j]?money(Number(v)):v}</td>)}</tr>)}</tbody></table></div>;
  return <div className="cat-page">
    <PageTitle title="Plans & extras" sub="Catalogue vs free-text charges, stays by meal plan, discounts by reason (amounts before GST)"
      action={<button className="btn" disabled={!ok} onClick={()=>{void saveCsv(`HCC-plans-extras-${from}_${to}.csv`,catReportCsv(s,from,to));toast('CSV saved · plans & extras');}}><Download size={16}/> CSV</button>}/>
    <section className="card cat-filters" aria-label="Report dates">
      <label>From<input type="date" value={from} max={s.date} onChange={e=>setFrom(e.target.value)}/></label>
      <label>To<input type="date" value={to} max={s.date} onChange={e=>setTo(e.target.value)}/></label>
      {!ok&&<p className="field-error" role="alert">Choose a valid range (from on or before to).</p>}
    </section>
    {split&&<>
      <section className="card"><div className="section-title"><h2>Service charges · catalogue vs free text</h2><span className="muted">{shortDate(from)} – {shortDate(to)}</span></div>
        <div className="cat-sums"><div><span>From the catalogue</span><strong>{money(split.catalogue.amount)}</strong><small>{split.catalogue.count} lines</small></div>
          <div><span>Free text (“Other”)</span><strong>{money(split.freeText.amount)}</strong><small>{split.freeText.count} lines</small></div>
          <div><span>Meal plan lines</span><strong>{money(split.plans.amount)}</strong><small>{split.plans.count} lines</small></div></div>
        {split.catalogue.rows.length>0&&<><h3>Catalogue items</h3>{table(['Item','Lines','Amount'],split.catalogue.rows.map(r=>[r.label,r.count,r.amount]),[false,false,true])}</>}
        {split.freeText.rows.length>0&&<><h3>Free-text charges <Pill tone="orange">review</Pill></h3>{table(['Description','Lines','Amount'],split.freeText.rows.map(r=>[r.label,r.count,r.amount]),[false,false,true])}</>}
        {!split.catalogue.rows.length&&!split.freeText.rows.length&&<Empty title="No service charges in this range"/>}
        <div className="cat-manage"><MstLink spec="extras" label="Manage list… · Extras and services"/></div></section>
      <section className="card"><div className="section-title"><h2>Stays by meal plan</h2></div>
        {plans.length?table(['Plan','Bookings','Room nights','Room revenue','Plan revenue'],plans.map(r=>[r.label,r.bookings,r.roomNights,r.roomRevenue,r.planRevenue]),[false,false,false,true,true])
          :<Empty title="No room nights in this range"/>}
        <p className="footnote">GST treatment of meal plans: {GST_ILLUSTRATIVE}.</p>
        <div className="cat-manage"><MstLink spec="mealPlans" label="Manage list… · Meal plans"/></div></section>
      <section className="card"><div className="section-title"><h2>Discounts by reason</h2></div>
        {disc.length?table(['Reason','Discounts','Amount'],disc.map(r=>[r.label,r.count,r.amount]),[false,false,true]):<Empty title="No discounts in this range"/>}
        <div className="cat-manage"><MstLink spec="discountReasons" label="Manage list… · Discount reasons"/></div></section>
    </>}
  </div>;
}

export const registration:FeatureRegistration={
  pages:{'plans-extras':PlansExtrasPage},
  nav:[{group:'CLOSING & REPORTS',item:{id:'plans-extras',label:'Plans & extras',icon:Utensils,p:'reports'},after:'reports'}],
  modals:{'charge':CatChargeModal,'discount':CatDiscountModal,'cat-plan':CatPlanModal},
  bookingActions:CatPlanAction,
};
