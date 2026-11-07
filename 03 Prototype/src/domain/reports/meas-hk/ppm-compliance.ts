// F12 MEAS · rpt.rooms.ppm-compliance — Preventive maintenance compliance (catalogue §3.10; switch meas.hk.ppm, module Maintenance). Money flag n.
// Data: maintenance plans, work orders and AMC contracts (module 15). Red row (per asset kind): a preventive task overdue more than 7 days;
// P1 corrective MTTR above 24 hours; a third failure of one asset within 90 days.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {nights} from '../../money';
import {mntSlice,mntMinutes,MNT_ASSET_KINDS,MNT_PRIORITIES,type MNTWorkOrder} from '../../streams/maintenance';
import {T,I,mkRow,rangeGuard,r1,mean} from './shared';

export const PPM_OVERDUE_DAYS=7;   // overdue by more than this many days is red
export const PPM_P1_MTTR_HOURS=24; // P1 MTTR above this is red
export const PPM_REPEAT_FAILURES=3; // this many failures of one asset ...
export const PPM_REPEAT_WINDOW_DAYS=90; // ... within this many days is red
export const PPM_COLS=[T('kind','Asset kind'),I('plansDue','Plans due'),I('doneOnTime','Done on time'),I('overdue','Overdue'),T('ratio','Preventive-to-corrective ratio'),T('mttr','MTTR by priority'),I('repeatFailures','Repeat failures in 90 days'),I('amcVisits','AMC visits due')];

/** Assets with PPM_REPEAT_FAILURES or more corrective work orders created inside any PPM_REPEAT_WINDOW_DAYS span. */
export function ppmRepeatAssets(orders:MNTWorkOrder[]):string[]{
  const by=new Map<string,string[]>();
  for(const w of orders)if(w.kind==='Corrective'&&w.assetId&&w.status!=='Cancelled'){const l=by.get(w.assetId)??[];l.push(w.createdAt.slice(0,10));by.set(w.assetId,l);}
  const out:string[]=[];
  for(const [a,l] of by){l.sort();for(let i=0;i+PPM_REPEAT_FAILURES-1<l.length;i++)if(nights(l[i],l[i+PPM_REPEAT_FAILURES-1])<=PPM_REPEAT_WINDOW_DAYS){out.push(a);break;}}
  return out;
}
const BASIS='Preventive work orders by their due date in the range (up to 366 days, from the FY start by default); a work order is done on time when finished on or before its due date, overdue when not done and past due at the To date. Ratio = preventive : corrective work orders created in the range. MTTR = created to done of corrective work orders finished in the range, by priority. Repeat failures = assets with 3 corrective work orders inside 90 days. AMC visits = visits of contracts running in the range for the vendors of that kind of asset. Red: a preventive task overdue more than 7 days, P1 MTTR above 24 hours, or a third failure of one asset in 90 days.';

export const ppmCompliance=registerReport({
  id:'rpt.rooms.ppm-compliance',title:'Preventive maintenance compliance',area:'rooms',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],deskPerms:['PERM-MNT-001'],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'},{key:'assetKind',label:'Asset kind',type:'enum',options:[...MNT_ASSET_KINDS]}],
  columns:PPM_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s:State,_uid,p)=>{
    rangeGuard(p.from,p.to,366,s.date);
    const x=mntSlice(s),kindOf=(id?:string)=>x.assets.find(a=>a.id===id)?.kind??'Other';
    const inRange=(d:string)=>d>=p.from!&&d<=p.to!;
    const rows:RptRow[]=[];
    for(const kind of [...MNT_ASSET_KINDS]){
      if(p.assetKind&&p.assetKind!==kind)continue;
      const mine=x.orders.filter(w=>kindOf(w.assetId)===kind&&w.status!=='Cancelled');
      const pm=mine.filter(w=>w.kind==='Preventive'&&w.dueDate&&inRange(w.dueDate));
      const onTime=pm.filter(w=>w.doneAt&&w.doneAt.slice(0,10)<=w.dueDate!).length;
      const late=pm.filter(w=>!w.doneAt&&nights(w.dueDate!,p.to!)>0);
      const overdueRed=late.some(w=>nights(w.dueDate!,p.to!)>PPM_OVERDUE_DAYS);
      const created=mine.filter(w=>inRange(w.createdAt.slice(0,10)));
      const nPm=created.filter(w=>w.kind==='Preventive').length,nCor=created.filter(w=>w.kind==='Corrective').length;
      const mttrBy=MNT_PRIORITIES.map(pr=>{
        const m=mean(mine.filter(w=>w.kind==='Corrective'&&w.priority===pr&&w.doneAt&&inRange(w.doneAt.slice(0,10))).map(w=>mntMinutes(w.createdAt,w.doneAt!)));
        return {pr,hours:m===null?null:m/60};
      });
      const p1=mttrBy.find(m=>m.pr==='P1')!.hours;
      const repeat=ppmRepeatAssets(mine.filter(w=>w.createdAt.slice(0,10)<=p.to!)).length;
      const vendors=new Set(x.assets.filter(a=>a.kind===kind&&a.vendorId).map(a=>a.vendorId));
      const amc=x.amcs.filter(a=>vendors.has(a.vendorId)&&a.from<=p.to!&&a.to>=p.from!).reduce((t,a)=>t+a.visits,0);
      if(!pm.length&&!nCor&&!repeat&&!amc)continue;
      rows.push(mkRow({kind,plansDue:pm.length,doneOnTime:onTime,overdue:late.length,ratio:`${nPm} : ${nCor}`,
        mttr:mttrBy.filter(m=>m.hours!==null).map(m=>`${m.pr} ${r1(m.hours!)} h`).join('; '),repeatFailures:repeat,amcVisits:amc},
        overdueRed||(p1!==null&&p1>PPM_P1_MTTR_HOURS)||repeat>0));
    }
    return {params:p,columns:PPM_COLS,rows};
  },
});
