// F12-RPT-DAILY · rpt.rooms.inventory-exceptions (catalogue 1.8, meas.daily.inventory, core). Rooms out of the sellable inventory in the range
// (master out-of-order window, captured offline reason) plus complimentary stays. DEC-266: no complimentary stay type exists in code on main, so a stay
// counts only when it carries a `complimentary` marker ({approver?,reason?}); until then that kind stays empty.
// Exception: complimentary without an approver; OOO more than 7 days (exactly 7 is not red) without a work order. Server twin: InventoryExceptionsReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {nights} from '../../money';
import {guestNameOf,rangeGuard,userLabel} from '../guests/shared';
import {T,D,EXC,inRange} from './shared';

export const INVENTORY_COLS:RptColumn[]=[T('room','Room'),T('kind','Kind'),D('from','From'),D('to','To / expected return'),T('beneficiary','Beneficiary'),T('approver','Approver'),T('reason','Reason'),T('workOrder','Work order'),EXC()];
export const INVENTORY_KEY='meas.daily.inventory';
export const OOO_DAYS_LIMIT=7;
const KIND:Record<string,string>={OOO:'OOO',OOS:'OOS','house-use':'house use',renovation:'OOO'};

export function inventoryRows(s:State,from:string,to:string):RptRow[]{
  const out:{room:string;kind:string;from:string;to:string;ben:string;approver:string;reason:string;wo:string;why:string}[]=[];
  for(const r of s.rooms){
    const off=s.measX?.offline?.[r.id];
    if(!r.ooo&&!off)continue;
    const f=r.ooo?.from??'',t=r.ooo?.to??off?.expectedReturn??'';
    if(r.ooo&&!(f<=to&&(r.ooo.to??'9999-12-31')>=from))continue;
    const kind=off?KIND[off.reason]??'OOO':'OOO',wo=off?.workOrder??'';
    const days=f?nights(f,t||s.date):0;
    out.push({room:r.id,kind,from:f,to:t,ben:'',approver:'',reason:r.ooo?.reason??off?.note??'',wo,why:kind==='OOO'&&days>OOO_DAYS_LIMIT&&!wo?`Out of order more than ${OOO_DAYS_LIMIT} days without a work order`:''});
  }
  for(const b of s.bookings){
    const c=(b as unknown as {complimentary?:{approver?:string;reason?:string}|true}).complimentary;
    if(!c||!(b.status==='In house'||b.status==='Departed')||!(b.arrival<=to&&b.departure>from))continue;
    const info=c===true?{}:c;
    out.push({room:b.room,kind:'complimentary',from:b.arrival,to:b.departure,ben:guestNameOf(s,b),approver:userLabel(s,info.approver),reason:info.reason??'',wo:'',why:info.approver?'':'Complimentary without an approver'});
  }
  return out.filter(x=>!x.from||inRange(x.from,undefined,to)).sort((a,b)=>a.room.localeCompare(b.room)||a.kind.localeCompare(b.kind)||a.from.localeCompare(b.from))
    .map(x=>({kind:'row' as const,cells:{room:x.room,kind:x.kind,from:x.from||null,to:x.to||null,beneficiary:x.ben,approver:x.approver,reason:x.reason,workOrder:x.wo,exception:x.why}}));
}
registerMeasReport('rpt.rooms.inventory-exceptions',INVENTORY_KEY);
export const inventoryExceptions=registerReport({
  id:'rpt.rooms.inventory-exceptions',title:'Complimentary, house-use and out-of-order',area:'rooms',priority:'P1',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-S',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-S'},{key:'to',label:'To',type:'date',dateClass:'D-S'}],
  columns:INVENTORY_COLS,
  dateBasis:'Rooms out of order / out of service / for house use whose window touches the range (default today; up to 31 days), and complimentary stays (pending DEC-266). Exception: complimentary without an approver; out of order more than 7 days without a work order.'+' Capture needed: roomoffline reason and expected return (meas-cap-hk); with nothing captured the report shows no rows.',
  formats:['screen','pdf'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date);
    return {params:p,columns:INVENTORY_COLS,rows:inventoryRows(s,p.from!,p.to!)};
  },
});
