// F12-RPT-REVENUE · rpt.revenue.displacement — Displacement calculator, report shell (catalogue §3.10, switch meas.rev.displacement). The arithmetic is
// meas-forecast's pure fcDisplacement (read, not changed); this file only builds its inputs from the forecast and the yield module's suggested rate.
import type {State} from '../../types';
import type {RptColumn,RptBoundParams,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport,measOn} from '../../streams/measure';
import {addDays,nights} from '../../money';
import {HccError} from '../../errors';
import {fcDisplacement,fcForecastNight,fcInputOf,fcNightCounts,type FCDispNight} from '../../forecast';
import {yldNight,yldOcc,yldRoomTypes} from '../../streams/yield';
import {mrDisplacementRed,mrCounterRate,mrRecommendation,mrPct} from './logic';
import {T,D,I,P,M,mrRefType,mrSellable,mrRoomsOn,mrIntParam,type MrRow} from './shared';

export const DISP_COLS:RptColumn[]=[D('night','Night'),P('forecast','Forecast occupancy'),I('displaced','Rooms displaced'),M('displacedRevenue','Transient revenue displaced'),M('offerRevenue','Offer revenue'),
  M('net','Net gain or loss'),T('recommendation','Recommendation (accept / counter-rate / decline)')];
const DISP_BASIS='Business date forward (up to 92 nights, From to To inclusive). Displaced rooms = group rooms beyond the rooms still free on the forecast; displaced revenue = displaced rooms x the night suggested BAR (largest room type, or the chosen type); offer revenue = rooms x offered rate. Red when the net is a loss (the offered rate is below the counter-rate). Counter-rate = break-even rate rounded up to Rs 50; decline when it is more than 150 % of the offer.';
export const ENQUIRY=['group','long-stay','hall'];
export function dispNights(s:State,from:string,n:number,type:string):FCDispNight[]{
  const base=fcInputOf(s),counts=fcNightCounts(base.stays),real=measOn(s,'meas.rev.forecast');
  const adrType=type||mrRefType(s);
  return Array.from({length:n},(_,i)=>{
    const date=addDays(from,i),occ=yldOcc(s,date),sellAll=occ.sellable;
    let rooms=Math.max(occ.onBooks,occ.forecast);
    if(real&&sellAll)rooms=Math.max(occ.onBooks,fcForecastNight({...base,rooms:sellAll},date,counts).rooms);
    const sellable=type?mrSellable(s,date,type):sellAll;
    const forecastRooms=type?Math.max(mrRoomsOn(s,date,type),sellAll?Math.round(rooms*sellable/sellAll):0):rooms;
    return {date,sellable,forecastRooms:Math.min(forecastRooms,sellable),adrPaise:adrType?yldNight(s,adrType,date).suggested:0};
  });
}
export function dispCompute(s:State,from:string,n:number,rooms:number,ratePaise:number,type:string):{rows:MrRow[];total:MrRow;netPaise:number}{
  const nts=dispNights(s,from,n,type),r=fcDisplacement(nts,rooms,ratePaise,0);
  const rows:MrRow[]=r.rows.map((x,i)=>{
    const net=x.groupPaise-x.displacedPaise,rec=mrRecommendation(net,ratePaise,mrCounterRate(x.displacedPaise,rooms));
    return {cells:{night:x.date,forecast:mrPct(nts[i].forecastRooms,nts[i].sellable),displaced:x.displaced,displacedRevenue:x.displacedPaise,offerRevenue:x.groupPaise,net,recommendation:rec},red:mrDisplacementRed(net)};
  });
  const rec=mrRecommendation(r.netPaise,ratePaise,r.breakEvenRatePaise);
  const total:MrRow={cells:{night:`Total · ${n} night${n===1?'':'s'}`,displaced:r.rows.reduce((a,x)=>a+x.displaced,0),displacedRevenue:r.displacedPaise,offerRevenue:r.groupPaise,net:r.netPaise,
    recommendation:rec==='counter-rate'?`counter-rate (at least ${r.breakEvenRatePaise/100} a night)`:rec},red:mrDisplacementRed(r.netPaise)};
  return {rows,total,netPaise:r.netPaise};
}
export const displacement=registerReport({
  id:'rpt.revenue.displacement',title:'Displacement calculator',area:'revenue',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-FWD',
  params:[{key:'enquiry',label:'Enquiry',type:'enum',options:ENQUIRY,default:'group'},{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'to',label:'To',type:'date',dateClass:'D-FWD'},
    {key:'rooms',label:'Rooms',type:'text',default:'5'},{key:'rate',label:'Offered rate (Rs a night)',type:'text',default:'1500'},{key:'roomType',label:'Room type',type:'text'}],
  columns:DISP_COLS,dateBasis:DISP_BASIS,formats:['screen','pdf'],phone:'yes',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const from=p.from!,to=p.to!,n=nights(from,to)+1;
    if(n>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
    const rooms=mrIntParam('Rooms',p.rooms,5,1,s.rooms.length||500),rupees=mrIntParam('Offered rate',p.rate,1500,0,1000000);
    const type=String(p.roomType??'');if(type&&!yldRoomTypes(s).includes(type))throw new HccError('VALIDATION',`Room type: choose one of ${yldRoomTypes(s).join(', ')}.`);
    const r=dispCompute(s,from,n,rooms,rupees*100,type);
    const out:RptRow[]=[...r.rows.map(x=>({kind:'row' as const,cells:x.cells})),{kind:'total',cells:r.total.cells}];
    return {params:p,columns:DISP_COLS,rows:out};
  },
});
registerMeasReport('rpt.revenue.displacement','meas.rev.displacement');
