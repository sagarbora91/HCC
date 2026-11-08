// F8-RPT-P2-C · rpt.admin.rate-sheet — rate sheet, publish log and rate-change history (partial -> built on the engine). Adapter over the yield module (yld*): no new figures.
//   rate sheet     : room type x plan x night, the plan rate the yield engine suggests (yldPlanRate) where the plan is sold that night.
//   publish log    : one row per publish whose nights overlap the range (what window, which room types, who, why).
//   change history : every night rate a publish changed (before -> after, closing rows marked) and every manual night override (after only), with when / who / why.
// The tariff-card print and its CSV stay on the F4 page (rate-sheet.tsx); this report is the audit of rate changes (SF-CONTRACT 12).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {yldSlice,yldRoomTypes,yldPlanAvailable,yldPlanRate,yldNight,yldDates} from '../../streams/yield';
import {nights} from '../../money';
import {HccError} from '../../errors';
import {col,rowOf,userName} from '../money/common';
import {RPT_V2_GRANTS} from '../mask';
import {stamp} from './shared';

const G='PERM-YLD-001';
/** PERM-YLD-001 (yield.view): Owner, Manager - the server grant (seed 3700); the prototype's v1 matrix has no row for it (same pattern as F7-R5 / R6 in mask.ts). */
RPT_V2_GRANTS[G]=['Owner','Manager'];
export const RATE_SHEET_COLS:RptColumn[]=[col('roomType','Room type','text'),col('plan','Plan','text'),col('rate','Rate','money',true),col('night','Night','date'),col('type','Type','text'),
  col('before','Before','money',true),col('after','After','money',true),col('when','When','text'),col('who','Who','text'),col('reason','Reason','text')];
const by=(...keys:string[])=>RATE_SHEET_COLS.filter(c=>keys.includes(c.key));
export const RATE_VIEWS=['rate sheet','publish log','change history'];
export const rateSheet=registerReport({
  id:'rpt.admin.rate-sheet',title:'Rate sheet, publish log and change history',area:'admin',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,viewPerms:[G],dateClass:'D-FWD',legacyIds:['C7.RoomTariffCard'],
  params:[{key:'view',label:'View',type:'enum',options:RATE_VIEWS,default:'rate sheet'},{key:'from',label:'From night',type:'date',dateClass:'D-FWD'},{key:'to',label:'To night',type:'date',dateClass:'D-FWD'}],
  columns:RATE_SHEET_COLS,
  dateBasis:'Nights in the range. Rate sheet: the rate each active plan sells at on that night (yield engine, rounded to the guardrail unit). Publish log: publishes whose nights overlap the range. Change history: the rate each publish moved (before and after) and each manual night override, oldest first.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    if(nights(p.from!,p.to!)+1>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
    if(nights(s.date,p.to!)>90)throw new HccError('VALIDATION','The report can look 90 days ahead at most.');
    const y=yldSlice(s),view=String(p.view??'rate sheet'),types=yldRoomTypes(s).sort();
    if(view==='publish log'){
      const cols=by('roomType','night','type','when','who','reason');
      const list=y.publishes.filter(x=>x.from<=p.to!&&x.to>=p.from!).sort((a,b)=>b.at.localeCompare(a.at)||b.id.localeCompare(a.id));
      return {params:p,columns:cols,rows:list.map(x=>rowOf('row',{roomType:x.types.join(', '),night:x.from,type:`${x.rows.length} night rates to ${x.to}`,when:stamp(x.at),who:userName(s,x.actor)||x.actor,reason:x.reason||null}))};
    }
    if(view==='change history'){
      const cols=by('roomType','night','type','before','after','when','who','reason');
      const list:{night:string;at:string;id:number;cells:RptRow['cells']}[]=[];let n=0;
      for(const x of y.publishes)for(const r of x.rows){if(r.date<p.from!||r.date>p.to!)continue;
        list.push({night:r.date,at:x.at,id:n++,cells:{roomType:r.type,night:r.date,type:r.closing?'Published (closing row)':'Published',before:r.before,after:r.after,when:stamp(x.at),who:userName(s,x.actor)||x.actor,reason:x.reason||null}});}
      for(const o of y.overrides){if(o.date<p.from!||o.date>p.to!)continue;
        list.push({night:o.date,at:o.at,id:n++,cells:{roomType:o.type,night:o.date,type:'Override',before:null,after:o.rate,when:stamp(o.at),who:userName(s,o.actor)||o.actor,reason:o.reason||null}});}
      list.sort((a,b)=>a.night<b.night?-1:a.night>b.night?1:a.at<b.at?-1:a.at>b.at?1:a.id-b.id);
      return {params:p,columns:cols,rows:list.map(x=>rowOf('row',x.cells))};
    }
    const cols=by('roomType','plan','night','rate'),rows:RptRow[]=[];
    for(const t of types)for(const pl of y.plans.filter(x=>x.active))for(const d of yldDates(p.from!,nights(p.from!,p.to!)+1)){
      if(!yldPlanAvailable(s,pl,d).ok)continue;
      rows.push(rowOf('row',{roomType:t,plan:pl.name,night:d,rate:yldPlanRate(s,pl,t,d,yldNight(s,t,d).suggested)},t));}
    return {params:p,columns:cols,rows};
  },
});
