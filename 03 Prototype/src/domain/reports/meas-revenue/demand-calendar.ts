// F12-RPT-REVENUE · rpt.revenue.demand-calendar — Demand calendar (catalogue §3.10, switch meas.rev.demand-calendar). Reads the forecast curve
// (fcForecastNight, meas.rev.forecast), the yield module (BAR, suggested rate, offers) and the events master; changes nothing.
import type {State} from '../../types';
import type {RptColumn,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {addDays} from '../../money';
import {fcForecastNight,fcInputOf,fcNightCounts,type FCInput} from '../../forecast';
import {yldNight,yldOcc,yldPlanAvailable,yldSlice} from '../../streams/yield';
import {measOn} from '../../streams/measure';
import {mrDemandRed,mrWeekday,mrPct} from './logic';
import {T,D,I,P,B,M,mrRows,mrRefType,mrSellable,mrIntParam,mrNightsGuard,CAPTURE_NOTE,type MrRow} from './shared';

export const DEMAND_COLS:RptColumn[]=[D('date','Date'),T('weekday','Weekday'),I('onBooks','On the books'),P('forecast','Forecast'),P('lastYear','Same night last year'),T('event','Event'),
  M('bar','BAR'),M('suggested','Suggested rate'),B('offerOpen','Offer open')];

/** One row's inputs → cells + red flag (pure; the boundary tests call it directly). */
export function demandRow(x:{date:string;onBooks:number;forecastPct:number;lastYearPct:number|null;event:string;bar:number;suggested:number;base:number;offerOpen:boolean}):MrRow{
  const unchanged=x.bar<=x.base;
  return {cells:{date:x.date,weekday:mrWeekday(x.date),onBooks:x.onBooks,forecast:x.forecastPct,lastYear:x.lastYearPct,event:x.event,bar:x.bar,suggested:x.suggested,offerOpen:x.offerOpen?1:0},
    red:mrDemandRed(x.forecastPct,unchanged,x.offerOpen)};
}
export function demandCompute(s:State,from:string,nights:number):MrRow[]{
  const type=mrRefType(s),base:FCInput=fcInputOf(s),counts=fcNightCounts(base.stays);
  const real=measOn(s,'meas.rev.forecast');
  const histFrom=base.stays.reduce<string>((a,x)=>!a||x.from<a?x.from:a,'');
  const events=s.measX?.events??[];
  const plans=yldSlice(s).plans.filter(p=>p.code!=='BAR'&&p.adjBps<0);
  const out:MrRow[]=[];
  for(let i=0;i<nights;i++){
    const date=addDays(from,i),occ=yldOcc(s,date),sellable=occ.sellable;
    let pct=occ.pct;
    if(real&&sellable){const f=fcForecastNight({...base,rooms:sellable},date,counts);pct=Math.min(100,mrPct(Math.max(occ.onBooks,f.rooms),sellable));}
    const ly=addDays(date,-364),lyPct=histFrom&&ly>=histFrom&&ly<s.date?mrPct(counts.get(ly)??0,mrSellable(s,ly)||s.rooms.length):null;
    const n=type?yldNight(s,type,date):undefined;
    out.push(demandRow({date,onBooks:occ.onBooks,forecastPct:pct,lastYearPct:lyPct,event:events.filter(e=>e.from<=date&&e.to>=date).map(e=>e.name).join(' · '),
      bar:n?.live??0,suggested:n?.suggested??0,base:n?.base??0,offerOpen:plans.some(p=>yldPlanAvailable(s,p,date,pct).ok)}));
  }
  return out;
}
const DEMAND_BASIS='Business date forward: forecast = the real forecast curve (last two years by weekday, pickup, events) when switched on, else rooms on the books. BAR = live tariff of the largest room type; red when forecast is 85 % or more with the BAR not above its base, or 25 % or less with no discount offer open.';
export const demandCalendar=registerReport({
  id:'rpt.revenue.demand-calendar',title:'Demand calendar',area:'revenue',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-FWD',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'nights',label:'Nights (max 92)',type:'text',default:'90'}],
  columns:DEMAND_COLS,
  dateBasis:DEMAND_BASIS,
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const n=mrIntParam('Nights',p.nights,90,1,92);mrNightsGuard(n,92);
    const from=p.from!;const rows=demandCompute(s,from,n);
    return {params:p,columns:DEMAND_COLS,rows:mrRows(rows),about:{from,to:addDays(from,n-1),...(s.measX?.events?.length?{}:{dateBasis:DEMAND_BASIS+CAPTURE_NOTE('events master (yld.event)')})}};
  },
});
registerMeasReport('rpt.revenue.demand-calendar','meas.rev.demand-calendar');
