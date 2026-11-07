// F12-RPT-REVENUE · rpt.revenue.restriction-effect — Length-of-stay and minimum-stay controls (catalogue §3.10, switch meas.rev.restrictions).
// Nights that carry a MIN_STAY / CTA restriction (yield module), the refusal / re-shape counters meas-forecast keeps (s.measX.restrictions),
// the revenue on the night and the revenue of the same weekday on unrestricted nights of the range. Reads only.
import type {State} from '../../types';
import type {RptColumn,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {addDays,nights} from '../../money';
import {HccError} from '../../errors';
import {yldRestrictionsEnforced,yldSlice} from '../../streams/yield';
import {mrRestrictionRed,mrDow} from './logic';
import {T,D,I,M,mrRows,mrLive,mrSellable,mrRoomsOn,CAPTURE_NOTE,type MrRow} from './shared';

export const RESTR_COLS:RptColumn[]=[D('night','Night'),T('restriction','Restriction (MIN_STAY / CTA)'),I('refused','Bookings refused'),I('reshaped','Bookings re-shaped'),M('revenue','Revenue on the night'),
  M('unrestricted','Same nights unrestricted'),T('mode','Enforced or advisory')];
const RESTR_BASIS='Business date, range up to 92 days: one row per restricted night and restriction kind. Refused / re-shaped = the counters kept when a booking met the restriction; revenue = nightly rate of the bookings that night; same nights unrestricted = the average revenue of the same weekday on nights of the range with no restriction (whole paise, rounded down). Red when the night stayed under 40 % occupancy.';
export const RESTR_KINDS=['MIN_STAY','CTA'];
/** Room revenue (paise) of the bookings on a night. */
function nightRevenue(s:State,date:string):number{
  let r=0;const seen=new Set<string>();
  for(const b of s.bookings)if(b.arrival<=date&&b.departure>date&&mrLive(s,b)&&!seen.has(b.room)){seen.add(b.room);r+=b.rate;}
  return r;
}
export function restrictionCompute(s:State,from:string,to:string,kind:string):MrRow[]{
  const rs=yldSlice(s).restrictions,mode=yldRestrictionsEnforced(s)?'Enforced':'Advisory';
  const kindsOn=(d:string)=>{const k=new Set<string>();for(const r of rs)if(r.from<=d&&r.to>=d){if(r.minStay>1)k.add('MIN_STAY');if(r.cta)k.add('CTA');}return k;};
  const dates:string[]=[];for(let d=from;d<=to;d=addDays(d,1))dates.push(d);
  const rev=new Map(dates.map(d=>[d,nightRevenue(s,d)]));
  const free=new Map<number,number[]>();
  for(const d of dates)if(!kindsOn(d).size){const w=mrDow(d),l=free.get(w)??[];l.push(rev.get(d)!);free.set(w,l);}
  const out:MrRow[]=[];
  for(const d of dates){
    for(const k of [...kindsOn(d)].sort()){
      if(kind&&k!==kind)continue;
      const c=(s.measX?.restrictions??[]).find(x=>x.night===d&&x.kind===k);
      const peers=free.get(mrDow(d))??[];
      out.push({cells:{night:d,restriction:k,refused:c?.refused??0,reshaped:c?.reshaped??0,revenue:rev.get(d)!,
        unrestricted:peers.length?Math.floor(peers.reduce((a,x)=>a+x,0)/peers.length):null,mode},red:mrRestrictionRed(mrRoomsOn(s,d),mrSellable(s,d))});
    }
  }
  return out;
}
export const restrictionEffect=registerReport({
  id:'rpt.revenue.restriction-effect',title:'Length-of-stay and minimum-stay controls',area:'revenue',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'kind',label:'Restriction kind',type:'enum',options:RESTR_KINDS}],
  columns:RESTR_COLS,dateBasis:RESTR_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const from=p.from!,to=p.to!;if(nights(from,to)+1>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
    const rows=restrictionCompute(s,from,to,String(p.kind??''));
    return {params:p,columns:RESTR_COLS,rows:mrRows(rows),...(rows.length?{}:{about:{dateBasis:RESTR_BASIS+CAPTURE_NOTE('restriction counters and yld.restrictionsEnforce')}})};
  },
});
registerMeasReport('rpt.revenue.restriction-effect','meas.rev.restrictions');
