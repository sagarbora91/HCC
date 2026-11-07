// F12-RPT-DAILY · rpt.guests.regrets-denials (catalogue 1.2, meas.daily.regrets). Source: the turn-away log s.measX.turnaway (meas-cap-desk).
// Exception: more than 3 price regrets on a night (arrival night) whose occupancy is under 60 %. Server twin: Reports/MeasDaily/RegretsDenialsReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {mdTurnaways,TURNAWAY_REASONS} from '../../streams/turnaway';
import {rangeGuard} from '../guests/shared';
import {T,I,M,D,EXC,occOn,inRange,CAPTURE_NOTE} from './shared';

export const REGRETS_COLS:RptColumn[]=[D('date','Date'),T('reason','Reason'),T('channel','Channel'),I('leadDays','Lead days'),T('roomType','Room type'),I('nightsLost','Nights lost'),
  I('roomsLost','Rooms lost'),M('quotedRate','Quoted rate'),T('convertedLater','Converted later'),EXC()];
export const REGRETS_KEY='meas.daily.regrets';
export const REGRETS_PRICE_LIMIT=3;
export const REGRETS_OCC_LIMIT_PCT=60;
const reasonLabel=(id:string)=>TURNAWAY_REASONS.find(r=>r.id===id)?.label??id;

export function regretsRows(s:State,p:{from?:string;to?:string;reason?:string;channel?:string;roomType?:string}):RptRow[]{
  const all=mdTurnaways(s);
  // price regrets per arrival night over the whole log (the rule judges the night, not the filter)
  const priceByNight=new Map<string,number>();
  for(const t of all)if(t.reason==='price')priceByNight.set(t.arrival,(priceByNight.get(t.arrival)??0)+1);
  const redNight=(n:string)=>{
    if((priceByNight.get(n)??0)<=REGRETS_PRICE_LIMIT)return false;
    const {occ,sell}=occOn(s,n);return sell>0&&occ*100<REGRETS_OCC_LIMIT_PCT*sell;
  };
  return all.filter(t=>inRange(t.at.slice(0,10),p.from,p.to)&&(!p.reason||p.reason==='all'||t.reason===p.reason)&&(!p.channel||t.channel.toLowerCase()===p.channel.toLowerCase())
      &&(!p.roomType||t.roomType.toLowerCase()===p.roomType.toLowerCase()))
    .sort((a,b)=>a.at.localeCompare(b.at)||a.id.localeCompare(b.id))
    .map(t=>({kind:'row' as const,cells:{date:t.at.slice(0,10),reason:reasonLabel(t.reason),channel:t.channel,leadDays:t.leadDays,roomType:t.roomType,nightsLost:t.nights,roomsLost:t.rooms,
      quotedRate:t.quotedRate??null,convertedLater:t.convertedBooking?'Yes':'No',
      exception:t.reason==='price'&&redNight(t.arrival)?`More than ${REGRETS_PRICE_LIMIT} price regrets on ${t.arrival}, a night under ${REGRETS_OCC_LIMIT_PCT} % occupancy`:''}}));
}
registerMeasReport('rpt.guests.regrets-denials',REGRETS_KEY);
export const regretsDenials=registerReport({
  id:'rpt.guests.regrets-denials',title:'Regrets and denials',area:'guests',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},
    {key:'reason',label:'Reason',type:'enum',options:['all',...TURNAWAY_REASONS.map(r=>r.id)],default:'all'},{key:'channel',label:'Channel',type:'text'},{key:'roomType',label:'Room type',type:'text'}],
  columns:REGRETS_COLS,
  dateBasis:'Turn-aways by the business date they were logged (range up to 92 days). Lead days = days from the log date to the wanted arrival. Quoted rate is money (hidden without the money gate). Exception: more than 3 price regrets for one arrival night whose occupancy is under 60 %.'+CAPTURE_NOTE('res.turnaway log (meas-cap-desk)'),
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{rangeGuard(p.from,p.to,92,s.date);return {params:p,columns:REGRETS_COLS,rows:regretsRows(s,p)};},
});
