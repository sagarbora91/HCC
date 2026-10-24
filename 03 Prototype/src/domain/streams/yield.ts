// Module 13 · Advanced tariff & yield — pure helpers, selectors and the state slice type. Exported names MUST start with 'yld' / 'YLD' (shared mock namespace).
// Spec: 02 Planning/modules/HCC_Module_13_Tariff_Yield_Spec.md. Money = integer paise. Percentages are basis points (100 bps = 1 %).
import type {State,Room,Approval} from '../types';
import {addDays,nights as nightsBetween,money} from '../money';
import {rateFor,blockedDuring} from '../selectors';

export type YLDPlan={id:string;code:string;name:string;
  /** Adjustment on the night's suggested BAR, bps (BAR itself = 0). */
  adjBps:number;
  /** Minimum nights for the plan (0 = day-use, no overnight). */
  minNights:number;
  /** Nights of week the plan may be sold for (0 = Sunday … 6 = Saturday). Undefined = every night. */
  nights?:number[];
  /** Day-use plan: hours and the forecast occupancy at which it closes. */
  dayUse?:{from:string;to:string;closeAtOcc:number};
  active:boolean;note:string};
export type YLDSeason={id:string;name:string;from:string;to:string;adjBps:number;
  /** Room types the season applies to; empty = all types. */
  types:string[];
  /** Higher wins when seasons overlap. */
  priority:number};
export type YLDOccRule={id:string;op:'>='|'<';occ:number;adjBps:number};
export type YLDRestriction={id:string;
  /** Room type or '*' for all types. */
  type:string;
  /** Arrival dates covered (inclusive). */
  from:string;to:string;minStay:number;cta:boolean;note:string};
export type YLDOverride={type:string;date:string;rate:number;reason:string;actor:string;at:string};
export type YLDHistory={id:string;at:string;actor:string;area:string;before:string;after:string;reason:string};
export type YLDPublishRow={type:string;date:string;before:number;after:number;closing?:boolean};
export type YLDPublish={id:string;at:string;actor:string;from:string;to:string;types:string[];rows:YLDPublishRow[];reason:string;approval?:string};
export type YLDGuard={floorBps:number;ceilBps:number;
  /** Rounding unit in paise (5000 = ₹50). */
  roundTo:number};
/** State slice for this module, stored at s.yieldX (optional; absent in old saves → fixture defaults without a forecast). */
export type YLDState={plans:YLDPlan[];seasons:YLDSeason[];
  /** Day-of-week adjustment, bps, index 0 = Sunday. */
  dow:number[];
  occ:YLDOccRule[];guard:YLDGuard;restrictions:YLDRestriction[];overrides:YLDOverride[];
  /** SIMULATED forecast of rooms sold per night (fixture data). */
  forecast:Record<string,number>;
  /** Tariff rows written by this module: key `${type}|${effectiveFrom}` → rate. Excluded from the base rate. */
  owned:Record<string,number>;
  history:YLDHistory[];publishes:YLDPublish[]};

export const YLD_WINDOW=14;
export const YLD_DOW_NAMES=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
export const YLD_ROUND_OPTIONS=[100,1000,5000,10000];
export const YLD_APPROVAL_KIND='yield';
export function yldDefaultPlans():YLDPlan[]{return [
  {id:'BAR',code:'BAR',name:'Best available rate',adjBps:0,minNights:1,active:true,note:'Published to the tariff schedule; all other plans derive from it.'},
  {id:'CORP',code:'CORP',name:'Corporate',adjBps:-1200,minNights:1,active:true,note:'Contracted fictional companies; GSTIN on the booking.'},
  {id:'WKND',code:'WKND',name:'Weekend stay',adjBps:-500,minNights:2,nights:[5,6,0],active:true,note:'Fri, Sat and Sun nights only; minimum 2 nights.'},
  {id:'LONG',code:'LONG7',name:'Long-stay 7+ nights',adjBps:-1500,minNights:7,active:true,note:'Applies to every night of a 7+ night stay.'},
  {id:'DAY',code:'DAYUSE',name:'Day-use (10:00–17:00)',adjBps:-4500,minNights:0,dayUse:{from:'10:00',to:'17:00',closeAtOcc:90},active:true,note:'No overnight; room must be cleaned before evening arrivals. Closed on very high demand.'},
];}
export function yldEmpty():YLDState{return {plans:yldDefaultPlans(),seasons:[],dow:[0,0,0,0,0,0,0],occ:[],guard:{floorBps:-2000,ceilBps:4000,roundTo:5000},restrictions:[],overrides:[],forecast:{},owned:{},history:[],publishes:[]};}
export const yldSlice=(s:State):YLDState=>s.yieldX??yldEmpty();

// ───────────── formatting ─────────────
export const yldPct=(bps:number)=>(bps>0?'+':bps<0?'−':'±')+Math.abs(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+'%';
export const yldDow=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
export const yldDates=(from:string,n:number)=>Array.from({length:n},(_,i)=>addDays(from,i));
export const yldWindow=(s:State,n=YLD_WINDOW)=>yldDates(s.date,n);
export const yldRoomTypes=(s:State)=>[...new Set(s.rooms.map(r=>r.type))];
const roomOf=(s:State,type:string):Room|undefined=>s.rooms.find(r=>r.type===type);
export const yldRack=(s:State,type:string)=>roomOf(s,type)?.rate??0;
const key=(type:string,date:string)=>type+'|'+date;
const latest=<T extends {effectiveFrom:string}>(xs:T[])=>xs.slice().sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:a.effectiveFrom>b.effectiveFrom?1:0).slice(-1)[0];

/** Whether a tariff row was written by this module (and not since overwritten by a manual schedule). */
export function yldIsOwned(s:State,t:{type:string;rate:number;effectiveFrom:string}){return yldSlice(s).owned[key(t.type,t.effectiveFrom)]===t.rate;}
/** Base rate: latest manual (non-yield) tariff with effectiveFrom ≤ date, else the room-type rack rate. */
export function yldBase(s:State,type:string,date:string):{rate:number;source:'tariff'|'rack';from?:string}{
  const t=latest((s.tariffs||[]).filter(t=>t.type===type&&t.effectiveFrom<=date&&!yldIsOwned(s,t)));
  return t?{rate:t.rate,source:'tariff',from:t.effectiveFrom}:{rate:yldRack(s,type),source:'rack'};
}
/** Live tariff today used by new bookings (rateFor). */
export function yldLive(s:State,type:string,date:string){const r=roomOf(s,type);return r?rateFor(s,r,date):0;}

export type YLDOcc={onBooks:number;forecast:number;sellable:number;pct:number;simulated:boolean};
/** Forecast occupancy for a night: max(on the books, simulated forecast) ÷ sellable rooms. */
export function yldOcc(s:State,date:string):YLDOcc{
  const next=addDays(date,1);
  const sellable=s.rooms.filter(r=>!blockedDuring(r,date,next)).length;
  const onBooks=s.bookings.filter(b=>(b.status==='Confirmed'||b.status==='In house'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil)))&&b.arrival<=date&&b.departure>date).length;
  const forecast=yldSlice(s).forecast[date]??0;
  const sold=Math.max(onBooks,forecast);
  return {onBooks,forecast,sellable,pct:sellable?Math.min(100,Math.round(sold/sellable*100)):0,simulated:forecast>onBooks};
}
export function yldSeasonFor(s:State,type:string,date:string){
  return yldSlice(s).seasons.filter(x=>x.from<=date&&x.to>=date&&(!x.types.length||x.types.includes(type)))
    .reduce<YLDSeason|undefined>((best,x)=>!best||x.priority>=best.priority?x:best,undefined);
}
export function yldOccRuleFor(s:State,pct:number):YLDOccRule[]{
  const rules=yldSlice(s).occ;
  const up=rules.filter(r=>r.op==='>='&&pct>=r.occ).sort((a,b)=>b.occ-a.occ)[0];
  const down=rules.filter(r=>r.op==='<'&&pct<r.occ).sort((a,b)=>a.occ-b.occ)[0];
  return [up,down].filter(Boolean) as YLDOccRule[];
}
export const yldRound=(paise:number,unit:number)=>Math.max(unit,Math.round(paise/unit)*unit);
export function yldRestrictionsFor(s:State,type:string,date:string){return yldSlice(s).restrictions.filter(r=>(r.type==='*'||r.type===type)&&r.from<=date&&r.to>=date);}
export function yldOverrideFor(s:State,type:string,date:string){return yldSlice(s).overrides.find(o=>o.type===type&&o.date===date);}

export type YLDStep={label:string;bps:number;detail:string};
export type YLDNight={type:string;date:string;base:number;baseSource:'tariff'|'rack';baseFrom?:string;steps:YLDStep[];rawBps:number;appliedBps:number;clamped:'floor'|'ceiling'|null;
  computed:number;override?:YLDOverride;suggested:number;live:number;occ:YLDOcc;restrictions:YLDRestriction[];explain:string};
/** Full explanation for one room type and night: base → adjustments → guardrail → rounding → (override) → suggested. */
export function yldNight(s:State,type:string,date:string):YLDNight{
  const y=yldSlice(s),b=yldBase(s,type,date),occ=yldOcc(s,date),steps:YLDStep[]=[];
  const season=yldSeasonFor(s,type,date);
  if(season&&season.adjBps)steps.push({label:'Season · '+season.name,bps:season.adjBps,detail:`${season.from} to ${season.to}${season.types.length?' · '+season.types.join(', '):''}`});
  const dw=yldDow(date),dowBps=y.dow[dw]??0;
  if(dowBps)steps.push({label:'Day of week · '+YLD_DOW_NAMES[dw],bps:dowBps,detail:'Weekday rule'});
  for(const r of yldOccRuleFor(s,occ.pct))if(r.adjBps)steps.push({label:`Forecast occupancy ${occ.pct}% ${r.op==='>='?'≥':'<'} ${r.occ}%`,bps:r.adjBps,detail:`${Math.max(occ.onBooks,occ.forecast)} of ${occ.sellable} sellable rooms${occ.simulated?' (simulated forecast)':' on the books'}`});
  const rawBps=steps.reduce((a,x)=>a+x.bps,0);
  const appliedBps=Math.max(y.guard.floorBps,Math.min(y.guard.ceilBps,rawBps));
  const clamped=appliedBps!==rawBps?(rawBps<appliedBps?'floor':'ceiling'):null;
  const computed=yldRound(Math.round(b.rate*(10000+appliedBps)/10000),y.guard.roundTo);
  const override=yldOverrideFor(s,type,date);
  const suggested=override?override.rate:computed;
  const parts=[`Base ${money(b.rate)}`,...steps.map(x=>`${x.label} ${yldPct(x.bps)}`)];
  if(clamped)parts.push(`capped at ${clamped} ${yldPct(appliedBps)}`);
  parts.push(`rounded → ${money(computed)}`);
  if(override)parts.push(`manual override ${money(override.rate)} (${override.reason})`);
  return {type,date,base:b.rate,baseSource:b.source,baseFrom:b.from,steps,rawBps,appliedBps,clamped,computed,override,suggested,live:yldLive(s,type,date),occ,restrictions:yldRestrictionsFor(s,type,date),explain:parts.join(' · ')};
}
/** Calendar grid: rows = room types, cells = nights. */
export function yldCalendar(s:State,from=s.date,n=YLD_WINDOW){return yldRoomTypes(s).map(type=>({type,nights:yldDates(from,n).map(d=>yldNight(s,type,d))}));}

// ───────────── rate plans & quotes ─────────────
export function yldPlanAvailable(s:State,p:YLDPlan,date:string,occPct=yldOcc(s,date).pct):{ok:boolean;reason?:string}{
  if(!p.active)return {ok:false,reason:'Plan inactive'};
  if(p.nights&&!p.nights.includes(yldDow(date)))return {ok:false,reason:'Not sold on '+YLD_DOW_NAMES[yldDow(date)]};
  if(p.dayUse&&occPct>=p.dayUse.closeAtOcc)return {ok:false,reason:`Closed: forecast ${occPct}% ≥ ${p.dayUse.closeAtOcc}%`};
  return {ok:true};
}
export function yldPlanRate(s:State,p:YLDPlan,type:string,date:string,suggested=yldNight(s,type,date).suggested){
  return yldRound(Math.round(suggested*(10000+p.adjBps)/10000),yldSlice(s).guard.roundTo);
}
export type YLDQuote={plan:YLDPlan;type:string;arrival:string;nights:number;rows:{date:string;rate:number;bar:number}[];total:number;issues:string[]};
/** Quote a stay (or a day-use visit when the plan is day-use) and list every restriction it breaks. BOOK, CHANGE_DATES and every import path enforce the house restrictions via yldStayViolations (A2.1); plan rules stay advisory. */
export function yldQuote(s:State,planId:string,type:string,arrival:string,n:number):YLDQuote{
  const plan=yldSlice(s).plans.find(p=>p.id===planId)??yldSlice(s).plans[0];
  const issues:string[]=[];
  const count=plan.dayUse?1:Math.max(1,n);
  if(arrival<s.date)issues.push('Arrival is before the business date.');
  const rows=yldDates(arrival,count).map(date=>{const nt=yldNight(s,type,date);const a=yldPlanAvailable(s,plan,date,nt.occ.pct);
    if(!a.ok)issues.push(`${date}: ${a.reason}`);return {date,rate:yldPlanRate(s,plan,type,date,nt.suggested),bar:nt.suggested};});
  if(!plan.dayUse){
    if(count<plan.minNights)issues.push(`${plan.name} needs at least ${plan.minNights} nights.`);
    for(const r of yldRestrictionsFor(s,type,arrival)){
      if(r.cta)issues.push(`Closed to arrival on ${arrival}${r.note?' ('+r.note+')':''}.`);
      if(count<r.minStay)issues.push(`Minimum stay ${r.minStay} nights for arrivals ${r.from} to ${r.to}.`);
    }
  }
  return {plan,type,arrival,nights:plan.dayUse?0:count,rows,total:rows.reduce((a,r)=>a+r.rate,0),issues};
}
/** Check a proposed stay against restrictions (for reservations; advisory). */
export function yldCheckStay(s:State,type:string,arrival:string,departure:string){return yldQuote(s,'BAR',type,arrival,nightsBetween(arrival,departure)).issues;}
export type YLDViolation={code:'MIN_STAY'|'CTA';restriction:string;message:string};
/** House stay restrictions (minimum stay, closed to arrival) that a stay breaks. Enforced by BOOK and CHANGE_DATES for every
 *  source and rate plan, including group pickup and channel/web imports (A2.1). Rate-plan rules (e.g. WKND minimum nights)
 *  stay quote-level only. In-house MOVE (extend/shorten) is not checked: the arrival already happened. */
export function yldStayViolations(s:State,type:string,arrival:string,departure:string):YLDViolation[]{
  const n=nightsBetween(arrival,departure),out:YLDViolation[]=[];
  for(const r of yldRestrictionsFor(s,type,arrival)){
    if(r.cta)out.push({code:'CTA',restriction:r.id,message:`${type} is closed to arrival on ${arrival}${r.note?' ('+r.note+')':''}.`});
    if(n<r.minStay)out.push({code:'MIN_STAY',restriction:r.id,message:`Minimum stay ${r.minStay} nights for arrivals ${r.from} to ${r.to}${r.note?' ('+r.note+')':''}.`});
  }
  return out;
}

// ───────────── publishing ─────────────
export type YLDTarget=(type:string,date:string)=>number;
/** Plan a publish without changing state: rows written (only where the live rate differs) plus a closing row on to+1
 *  that keeps the rate after the window unchanged. Returns the new tariff list. */
export function yldPlanPublish(s:State,from:string,to:string,types:string[],target?:YLDTarget){
  const list=[...(s.tariffs||[])];const rows:YLDPublishRow[]=[];
  const rateIn=(xs:typeof list,type:string,d:string)=>latest(xs.filter(t=>t.type===type&&t.effectiveFrom<=d))?.rate??yldRack(s,type);
  const put=(type:string,d:string,rate:number)=>{const i=list.findIndex(t=>t.type===type&&t.effectiveFrom===d);if(i>=0)list.splice(i,1);list.push({type,rate,effectiveFrom:d});};
  const orig=s.tariffs||[];
  for(const type of types){
    const after=addDays(to,1),keep=rateIn(orig,type,after);
    for(let d=from;d<=to;d=addDays(d,1)){
      const t=target?target(type,d):yldNight(s,type,d).suggested;
      if(rateIn(list,type,d)!==t){rows.push({type,date:d,before:rateIn(orig,type,d),after:t});put(type,d,t);}
    }
    if(rateIn(list,type,after)!==keep){rows.push({type,date:after,before:keep,after:keep,closing:true});put(type,after,keep);}
  }
  return {rows,tariffs:list};
}
/** Nights in the window whose suggested rate differs from the live tariff. */
export function yldPendingNights(s:State,n=YLD_WINDOW){return yldCalendar(s,s.date,n).flatMap(r=>r.nights).filter(x=>x.suggested!==x.live);}
/** Signature of the tariff rows for given types — a proposal is stale when it changes. */
export function yldTariffSig(s:State,types:string[]){return JSON.stringify((s.tariffs||[]).filter(t=>types.includes(t.type)).map(t=>[t.type,t.effectiveFrom,t.rate]).sort());}
export const yldProposals=(s:State)=>s.approvals.filter(a=>a.kind===YLD_APPROVAL_KIND);
export const yldProposalExpired=(s:State,a:Approval)=>!!a.expiresAt&&s.clock>a.expiresAt;
export function yldProposalStatus(s:State,a:Approval):string{
  if(a.status!=='Pending')return a.status;
  if(yldProposalExpired(s,a))return 'Expired';
  if(yldTariffSig(s,(a.meta?.types as string[])||[])!==a.meta?.sig)return 'Stale';
  return 'Pending';
}
export const yldPendingProposals=(s:State)=>yldProposals(s).filter(a=>a.status==='Pending'&&!yldProposalExpired(s,a));
/** Summary figures for the page header. */
export function yldSummary(s:State){
  const cal=yldCalendar(s,s.date,7),all=cal.flatMap(r=>r.nights);
  const avg=all.length?Math.round(all.reduce((a,x)=>a+x.suggested,0)/all.length/100)*100:0;
  const high=yldWindow(s).filter(d=>yldOcc(s,d).pct>=80).length;
  return {avgBar7:avg,highDemand:high,pending:yldPendingNights(s).length,proposals:yldPendingProposals(s).length};
}
