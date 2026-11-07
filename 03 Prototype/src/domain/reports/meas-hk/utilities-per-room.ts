// F12 MEAS · rpt.rooms.utilities-per-room — Energy, water and LPG per occupied room-night (catalogue §3.10; switch meas.hk.utilities, module Maintenance).
// Data: monotonic meter readings (s.measX.readings; use = difference between consecutive readings, counted on the day of the later reading),
// occupied room-nights from stays, Posted "Utilities" expenses for the unit cost. Money: Unit cost and Cost per occupied night are money (the
// catalogue says "cost behind MNT-004"), so the report carries moneyFlag with gate PERM-RPT-002; Maintenance sees quantities only (DEC-061).
// Red row: per-occupied-night electricity more than 15 % above the average of the previous 3 months.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {addDays} from '../../money';
import {invOccupied} from '../../streams/inventory';
import {measMeters,measConsumption} from '../../streams/maintenance';
import {T,M,mkRow,captureNote,rangeGuard,eachDay,roomsInService,dec2,addMonths} from './shared';

export const UTIL_OVER_PCT=15;          // more than this many percent above the 3-month average is red
export const UTIL_LPG_KG_PER_CYLINDER=19; // a commercial cylinder; an LPG meter already in kg is used as it is
export const UTIL_COLS=[T('day','Day'),T('electricity','Electricity kWh'),T('water','Water kL'),T('lpg','LPG kg'),T('diesel','Diesel litres'),T('dgHours','DG hours'),
  T('perOccupied','Per occupied room-night'),T('perAvailable','Per available room'),M('unitCost','Unit cost'),M('costPerNight','Cost per occupied night'),T('lastYear','Last year same month')];

type Kind='electricity'|'water'|'lpg'|'diesel'|'dg-hours';
/** Use per day per meter kind (LPG converted to kg), optionally for one meter id. */
export function utilityUse(s:State,meterId?:string):Record<Kind,Map<string,number>>{
  const out={electricity:new Map(),water:new Map(),lpg:new Map(),diesel:new Map(),'dg-hours':new Map()} as Record<Kind,Map<string,number>>;
  for(const m of measMeters(s)){
    if(meterId&&m.id!==meterId)continue;
    const k=m.kind==='lpg'&&m.unit!=='kg'?UTIL_LPG_KG_PER_CYLINDER:1;
    for(const c of measConsumption(s,m.id))out[m.kind].set(c.date,(out[m.kind].get(c.date)??0)+c.used*k);
  }
  return out;
}
const sumIn=(m:Map<string,number>,pre:string)=>[...m].filter(([d])=>d.startsWith(pre)).reduce((a,[,v])=>a+v,0);
const occIn=(s:State,month:string)=>{const from=month+'-01';return eachDay(from,addDays(addMonths(from,1),-1)).reduce((a,d)=>a+invOccupied(s,d),0);};
/** Average electricity per occupied room-night over the 3 months before `month` (months with both readings and occupancy only); null when none. */
export function utilityBaseline(s:State,month:string,meterId?:string):number|null{
  const el=utilityUse(s,meterId).electricity,vals:number[]=[];
  for(let i=1;i<=3;i++){const m=addMonths(month+'-01',-i).slice(0,7),kwh=sumIn(el,m),rn=occIn(s,m);if(kwh>0&&rn>0)vals.push(kwh/rn);}
  return vals.length?vals.reduce((a,b)=>a+b,0)/vals.length:null;
}
/** The red test: per-occupied-night strictly more than 15 % above the baseline. */
export const utilityRed=(perNight:number|null,baseline:number|null)=>perNight!==null&&baseline!==null&&perNight*100>baseline*(100+UTIL_OVER_PCT)+1e-9;

const BASIS='Meter use by the day the reading was taken (use = difference from the previous reading; LPG cylinders × 19 kg). Per occupied room-night = electricity kWh ÷ occupied room-nights of that day; per available room = kWh ÷ rooms in service. Unit cost = the month\'s Posted Utilities expenses ÷ kWh; cost per occupied night = those expenses ÷ occupied room-nights (month row). Red: more than 15 % above the average per occupied night of the previous 3 months.';

export const utilitiesPerRoom=registerReport({
  id:'rpt.rooms.utilities-per-room',title:'Energy, water and LPG per occupied room-night',area:'rooms',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],deskPerms:['PERM-MNT-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'meter',label:'Meter',type:'text'}],
  columns:UTIL_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date);
    if(!(s.measX?.readings??[]).length)return {params:p,columns:UTIL_COLS,rows:[],about:{dateBasis:captureNote(BASIS,'meter readings (meas-cap-hk)')}};
    const month=p.month as string,use=utilityUse(s,p.meter as string|undefined),base=utilityBaseline(s,month,p.meter as string|undefined);
    const avail=roomsInService(s)||1;
    const rows:RptRow[]=[];
    for(const d of eachDay(p.from!,p.to!)){
      const get=(k:Kind)=>use[k].get(d);
      if(!(['electricity','water','lpg','diesel','dg-hours'] as Kind[]).some(k=>get(k)!==undefined))continue;
      const el=get('electricity'),rn=invOccupied(s,d),per=el!==undefined&&rn>0?el/rn:null;
      rows.push(mkRow({day:d,electricity:dec2(el),water:dec2(get('water')),lpg:dec2(get('lpg')),diesel:dec2(get('diesel')),dgHours:dec2(get('dg-hours')),
        perOccupied:dec2(per),perAvailable:el===undefined?null:dec2(el/avail),unitCost:null,costPerNight:null,lastYear:null},utilityRed(per,base)));
    }
    const kwh=sumIn(use.electricity,month),rn=occIn(s,month);
    const spend=s.expenses.filter(e=>e.category==='Utilities'&&e.status==='Posted'&&e.date.startsWith(month)).reduce((a,e)=>a+e.amount,0);
    const ly=sumIn(use.electricity,addMonths(month+'-01',-12).slice(0,7));
    const per=kwh>0&&rn>0?kwh/rn:null;
    rows.push({kind:'total',...(utilityRed(per,base)?{group:'red'}:{}),cells:{day:'Month',electricity:dec2(kwh),water:dec2(sumIn(use.water,month)),lpg:dec2(sumIn(use.lpg,month)),diesel:dec2(sumIn(use.diesel,month)),
      dgHours:dec2(sumIn(use['dg-hours'],month)),perOccupied:dec2(per),perAvailable:kwh>0?dec2(kwh/avail):null,
      unitCost:kwh>0&&spend>0?Math.round(spend/kwh):null,costPerNight:rn>0&&spend>0?Math.round(spend/rn):null,lastYear:ly>0?dec2(ly):null}});
    return {params:p,columns:UTIL_COLS,rows};
  },
});
