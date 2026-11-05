// F12 MEAS · meas-forecast (MEAS-CONTRACT §2, plan §4 items 2.2 / 2.3 / 2.4). Pure functions only; exported names start with 'fc' / 'FC'
// (shared mock namespace). Server twin: 08 Production/src/Hcc.Domain/Yield/MeasForecast.cs (same names in PascalCase, same arithmetic).
//
// 2.2 Real forecast curve for one night (rooms sold):
//   baseline %  = same night one and two years back, aligned on the weekday (date − 364 / − 728 days) plus the same weekday ±1 and ±2 weeks
//                 (five samples a year), year 1 weighted 2 and year 2 weighted 1; nights without history are skipped. No history at all →
//                 'trailing': the same weekday over the last 8 weeks before the as-of date. Nothing → 0 ('none').
//   pickup      = when the night is in the future and last year is known: on the books now + (last year's final − last year's on the books at the
//                 same lead time), never below on the books now. Blended 50 / 50 with the baseline rooms.
//   event uplift= sum of impactPct of the MEAS events covering the night (clamped −50 … +100), applied to the blended rooms.
//   result      = max(on the books, round(...)), at most the sellable rooms; pct = round(result ÷ sellable × 100).
// Occupancy definition (contract §9.2): distinct rooms slept on the night ÷ rooms in service. Integer arithmetic; percentages are Math.round.
// 2.4 Displacement (fcDisplacement): per night the group takes `rooms`; the rooms it pushes out = max(0, rooms − (sellable − forecast rooms));
//   displaced revenue = displaced × transient ADR (paise); group revenue = rooms × group rate (+ ancillary per room-night); net = group −
//   displaced; accept when net ≥ 0, else counter at the break-even rate = ceil((displaced revenue − ancillary) ÷ group room-nights) rounded up
//   to ₹50 (5000 paise). All money integer paise.
import type {State,Booking} from './types';
import type {AnStay} from './analytics/types';
import type {MeasEvent} from './streams/measure-types';
import {addDays,nights as nightsBetween} from './money';

/** One stay as the forecast sees it (prototype bookings, legacy `lgc` stays and server reservations map to this). */
export type FCStay={room:string;from:string;to:string;bookedOn?:string;cancelledOn?:string;noShow?:boolean};
export type FCInput={stays:FCStay[];rooms:number;asOf:string;events?:MeasEvent[];
  /** First date the history covers (nights before it count as unknown, not as empty). Default: the earliest stay's arrival. */
  historyFrom?:string};
export type FCNight={date:string;rooms:number;pct:number;onBooks:number;baselinePct:number;baselineSource:'last-years'|'trailing'|'none';
  pickup:number|null;paceFactorBps:number|null;eventPct:number;sellable:number;explain:string};

/** Default lead when a booking carries no booking date: 14 days before arrival (documented in DONE). */
export const FC_DEFAULT_LEAD=14;
export const FC_DAY=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();

/** Booking date of a prototype booking (Booking has no createdAt; SCHEMA stays 15): its earliest receipt date on or before arrival (the
 *  history generator dates advances by the lead-time buckets), else arrival − 14 days. `_s` kept for a later createdAt side table. */
export function fcBookedOn(_s:State,b:Booking):string{
  const r=(b.receipts||[]).map(x=>x.date).filter(x=>!!x&&x<=b.arrival).sort()[0];
  return r??addDays(b.arrival,-FC_DEFAULT_LEAD);
}
/** Prototype bookings → forecast stays. Cancelled bookings carry a cancellation date (arrival − 1 when unknown); no-shows are kept with the flag. */
export function fcStaysFromState(s:State):FCStay[]{
  return s.bookings.filter(b=>b.status!=='Expired'&&b.arrival<b.departure).map(b=>({room:b.room,from:b.arrival,to:b.departure,bookedOn:fcBookedOn(s,b),
    cancelledOn:b.status==='Cancelled'?addDays(b.arrival,-1):undefined,noShow:b.status==='No show'||undefined}));
}
/** Legacy / analytics stays (`lgc` via AnInput) → forecast stays (booked on = arrival − 14 when the legacy row has no booking date). */
export const fcStaysFromAn=(xs:AnStay[]):FCStay[]=>xs.filter(x=>x.status!=='cancelled'&&x.from<x.to).map(x=>({room:x.room,from:x.from,to:x.to}));

/** Distinct rooms slept per night (cancelled and no-show stays excluded). */
export function fcNightCounts(stays:FCStay[]):Map<string,number>{
  const per=new Map<string,Set<string>>();
  for(const x of stays){if(x.cancelledOn||x.noShow)continue;
    for(let d=x.from;d<x.to;d=addDays(d,1)){let set=per.get(d);if(!set)per.set(d,set=new Set());set.add(x.room);}}
  return new Map([...per].map(([d,set])=>[d,set.size]));
}
/** Rooms on the books for `night` as known on `asOf` (booked on or before, not cancelled by then; no-shows count until the night). */
export function fcOnBooks(stays:FCStay[],night:string,asOf:string):number{
  const set=new Set<string>();
  for(const x of stays){if(x.from>night||x.to<=night)continue;
    if((x.bookedOn??addDays(x.from,-FC_DEFAULT_LEAD))>asOf)continue;
    if(x.cancelledOn&&x.cancelledOn<=asOf)continue;
    if(x.noShow&&night<asOf)continue;
    set.add(x.room);}
  return set.size;
}
const clamp=(v:number,lo:number,hi:number)=>Math.max(lo,Math.min(hi,v));
/** Sum of event impact on a night, percent, clamped −50 … +100. */
export const fcEventPct=(events:MeasEvent[]|undefined,date:string)=>clamp((events||[]).filter(e=>e.from<=date&&e.to>=date).reduce((a,e)=>a+(e.impactPct||0),0),-50,100);

type Ctx={counts:Map<string,number>;from:string;asOf:string;rooms:number};
const known=(c:Ctx,d:string)=>d>=c.from&&d<c.asOf;
const pctOf=(c:Ctx,d:string)=>c.rooms?(c.counts.get(d)??0)*100/c.rooms:0;
function baseline(c:Ctx,date:string):{pct:number;source:FCNight['baselineSource']}{
  let wsum=0,acc=0;
  for(const [k,w] of [[1,2],[2,1]] as const){
    const xs=[-14,-7,0,7,14].map(o=>addDays(date,-364*k+o)).filter(d=>known(c,d));
    if(xs.length){acc+=w*xs.reduce((a,d)=>a+pctOf(c,d),0)/xs.length;wsum+=w;}
  }
  if(wsum)return {pct:acc/wsum,source:'last-years'};
  const dow=FC_DAY(date),tr:string[]=[];
  for(let i=1;i<=56;i++){const d=addDays(c.asOf,-i);if(FC_DAY(d)===dow&&known(c,d))tr.push(d);}
  if(tr.length)return {pct:tr.reduce((a,d)=>a+pctOf(c,d),0)/tr.length,source:'trailing'};
  return {pct:0,source:'none'};
}
/** Real forecast for one night (2.2). `counts` may be passed in to avoid recounting in a loop. */
export function fcForecastNight(input:FCInput,date:string,counts=fcNightCounts(input.stays)):FCNight{
  const rooms=Math.max(0,input.rooms);
  const from=input.historyFrom??input.stays.reduce((m,x)=>x.from<m?x.from:m,input.asOf);
  const c:Ctx={counts,from,asOf:input.asOf,rooms};
  const onBooks=Math.min(rooms,fcOnBooks(input.stays,date,input.asOf));
  const b=baseline(c,date),baseRooms=b.pct*rooms/100;
  const lead=nightsBetween(input.asOf,date);
  let pickup:number|null=null,pace:number|null=null,blend=baseRooms;
  const ly=addDays(date,-364),lyAsOf=addDays(input.asOf,-364);
  if(lead>0&&known(c,ly)){
    const lyFinal=counts.get(ly)??0,lyOn=fcOnBooks(input.stays,ly,lyAsOf);
    pickup=Math.max(0,lyFinal-lyOn);pace=Math.round((onBooks+1)*10000/(lyOn+1));
    blend=(onBooks+pickup+baseRooms)/2;
  }else if(lead<=0){blend=Math.max(baseRooms,onBooks);}
  const ev=fcEventPct(input.events,date);
  const sold=clamp(Math.max(onBooks,Math.round(blend*(100+ev)/100)),0,rooms);
  const pct=rooms?Math.round(sold*100/rooms):0;
  const parts=[`${onBooks} on the books`,`baseline ${Math.round(b.pct)}% (${b.source==='last-years'?'same weekday, last two years':b.source==='trailing'?'same weekday, last 8 weeks':'no history'})`];
  if(pickup!==null)parts.push(`expected pickup ${pickup} (last year's pace)`);
  if(ev)parts.push(`events ${ev>0?'+':''}${ev}%`);
  parts.push(`→ ${sold} of ${rooms} (${pct}%)`);
  return {date,rooms:sold,pct,onBooks,baselinePct:Math.round(b.pct),baselineSource:b.source,pickup,paceFactorBps:pace,eventPct:ev,sellable:rooms,explain:parts.join(' · ')};
}
/** Forecast curve for n nights from `from`. */
export function fcCurve(input:FCInput,from:string,n:number):FCNight[]{
  const counts=fcNightCounts(input.stays);
  return Array.from({length:n},(_,i)=>fcForecastNight(input,addDays(from,i),counts));
}
/** Prototype input: bookings + optional legacy stays, rooms in the master, events from s.measX. */
export function fcInputOf(s:State,legacy:FCStay[]=[]):FCInput{
  return {stays:[...legacy,...fcStaysFromState(s)],rooms:s.rooms.length,asOf:s.date,events:s.measX?.events};
}

export type FCBacktest={from:string;to:string;leadDays:number;nights:number;forecastPct:number;actualPct:number;diffPts:number;maeRooms:number};
/** Back-test: forecast every night of [from, to] as of `leadDays` before it, compare with the night's actual rooms (percentages to 0.1). */
export function fcBacktest(stays:FCStay[],rooms:number,from:string,to:string,leadDays=7,historyFrom?:string):FCBacktest{
  const counts=fcNightCounts(stays);let f=0,a=0,n=0,err=0;
  for(let d=from;d<=to;d=addDays(d,1)){
    const x=fcForecastNight({stays,rooms,asOf:addDays(d,-leadDays),historyFrom},d,counts);
    const act=counts.get(d)??0;f+=x.rooms;a+=act;err+=Math.abs(x.rooms-act);n++;
  }
  const r1=(v:number)=>Math.round(v*10)/10;
  const fp=n&&rooms?f*100/(n*rooms):0,ap=n&&rooms?a*100/(n*rooms):0;
  return {from,to,leadDays,nights:n,forecastPct:r1(fp),actualPct:r1(ap),diffPts:r1(fp-ap),maeRooms:n?r1(err/n):0};
}

// ── 2.3 OTB snapshots ──
export type FCOtbRow={night:string;rooms:number;revenuePaise:number};
/** On the books for each night from `takenOn` for `days` nights, as known on `takenOn` (revenue = the booking's nightly rate, paise). */
export function fcOtbRows(s:State,takenOn:string,days=120):FCOtbRow[]{
  const stays=s.bookings.filter(b=>b.status!=='Expired'&&b.arrival<b.departure).map(b=>({b,booked:fcBookedOn(s,b),cancel:b.status==='Cancelled'?addDays(b.arrival,-1):undefined}));
  const out:FCOtbRow[]=[];
  for(let i=0;i<days;i++){const night=addDays(takenOn,i);const rooms=new Set<string>();let rev=0;
    for(const x of stays){if(x.b.arrival>night||x.b.departure<=night||x.booked>takenOn||(x.cancel&&x.cancel<=takenOn))continue;
      if(!rooms.has(x.b.room)){rooms.add(x.b.room);rev+=x.b.rate;}}
    if(rooms.size)out.push({night,rooms:rooms.size,revenuePaise:rev});}
  return out;
}
/** Mondays on or before `to`, back to `from` (the weekly snapshot days; Monday 06:00 run). */
export function fcMondays(from:string,to:string):string[]{
  let d=to;while(FC_DAY(d)!==1)d=addDays(d,-1);const out:string[]=[];
  for(;d>=from;d=addDays(d,-7))out.unshift(d);return out;
}

// ── 2.4 Displacement ──
export type FCDispNight={date:string;sellable:number;forecastRooms:number;adrPaise:number};
export type FCDisplacement={rows:{date:string;free:number;displaced:number;displacedPaise:number;groupPaise:number}[];
  groupRoomNights:number;groupPaise:number;displacedPaise:number;netPaise:number;decision:'accept'|'counter';breakEvenRatePaise:number;explain:string};
export function fcDisplacement(nights:FCDispNight[],rooms:number,groupRatePaise:number,ancillaryPerRoomNightPaise=0):FCDisplacement{
  if(!Number.isInteger(rooms)||rooms<0||!Number.isInteger(groupRatePaise)||groupRatePaise<0||!Number.isInteger(ancillaryPerRoomNightPaise))throw new Error('Displacement inputs must be whole numbers (paise).');
  const rows=nights.map(n=>{const free=Math.max(0,n.sellable-n.forecastRooms),displaced=Math.max(0,rooms-free);
    return {date:n.date,free,displaced,displacedPaise:displaced*n.adrPaise,groupPaise:rooms*(groupRatePaise+ancillaryPerRoomNightPaise)};});
  const groupRoomNights=rooms*nights.length;
  const groupPaise=rows.reduce((a,r)=>a+r.groupPaise,0),displacedPaise=rows.reduce((a,r)=>a+r.displacedPaise,0),netPaise=groupPaise-displacedPaise;
  const need=Math.max(0,displacedPaise-ancillaryPerRoomNightPaise*groupRoomNights);
  const breakEven=groupRoomNights?Math.ceil(Math.ceil(need/groupRoomNights)/5000)*5000:0;
  const decision=netPaise>=0?'accept':'counter';
  const disp=rows.reduce((a,r)=>a+r.displaced,0);
  return {rows,groupRoomNights,groupPaise,displacedPaise,netPaise,decision,breakEvenRatePaise:breakEven,
    explain:`${groupRoomNights} group room-nights; ${disp} transient room-nights displaced; net ${netPaise>=0?'gain':'loss'} ${Math.abs(netPaise)} paise${decision==='counter'?`; counter at ≥ ${breakEven} paise a night`:''}`};
}
