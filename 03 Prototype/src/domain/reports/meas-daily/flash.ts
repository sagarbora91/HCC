// F12-RPT-DAILY · rpt.guests.manager-flash (catalogue 1.1, meas.daily.flash, core). One row per KPI; money / percent / count KPIs fill the matching
// column group. Exception: any KPI that has a target (occupancy, ADR, RevPAR) more than 10 % below it. Server twin: Reports/MeasDaily/ManagerFlashReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow,RptCell} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport,measTarget} from '../../streams/measure';
import {reportStaysOn,reportSellableOn} from '../../streams/closing';
import {rmsOooDuring} from '../../streams/roomoffline';
import {T,M,I,P,EXC,pct1,divRound,occOn,addDays} from './shared';

export const FLASH_COLS:RptColumn[]=[T('kpi','KPI'),P('pctActual','Actual %'),P('pctTarget','Target %'),P('pctLast','Last year %'),P('pctVar','Variance %'),
  M('amtActual','Actual'),M('amtTarget','Target'),M('amtLast','Last year'),M('amtVar','Variance'),
  I('cntActual','Actual (count)'),I('cntTarget','Target (count)'),I('cntLast','Last year (count)'),I('cntVar','Variance (count)'),EXC()];
export const FLASH_KEY='meas.daily.flash';
/** Red when the actual is more than 10 % below the target (exactly 10 % below is not red). */
export const flashRed=(actual:number,target:number|undefined|null)=>target!==undefined&&target!==null&&target>0&&actual*100<target*90;

export type FlashKpi={kpi:string;unit:'pct'|'amt'|'cnt';actual:number|null;target:number|null;last:number|null};
function dayFigures(s:State,d:string){
  const stays=reportStaysOn(s,d),sell=reportSellableOn(s,d).length;
  const roomRev=s.bookings.reduce((a,b)=>a+b.lines.filter(l=>l.kind==='room'&&l.date===d).reduce((x,l)=>x+l.amount,0),0);
  const occupied=stays.length;
  return {occupied,sell,roomRev,occ:sell?pct1(occupied,sell):null,adr:occupied?divRound(roomRev,occupied):null,revpar:sell&&occupied?divRound(roomRev,sell):null};
}
/** All KPIs of one business date with their targets and last-year figures (pure; the digest and the night audit reuse it). */
export function flashKpis(s:State,date:string):FlashKpi[]{
  const ly0=addDays(date,-365);
  const f=dayFigures(s,date),ly=dayFigures(s,ly0);
  const tOcc=measTarget(s,'meas.target.occupancy'),tAdr=measTarget(s,'meas.target.adr');
  const tRev=tOcc!==undefined&&tAdr!==undefined?Math.round(tOcc*tAdr/100):null;
  const live=(b:{status:string})=>b.status==='Confirmed'||b.status==='In house'||b.status==='Departed';
  const arrivals=(d:string)=>s.bookings.filter(b=>live(b)&&b.arrival===d).length;
  const departures=(d:string)=>s.bookings.filter(b=>(b.status==='Departed'||b.status==='In house')&&b.departure===d).length;
  const noShows=(d:string)=>s.bookings.filter(b=>b.status==='No show'&&b.arrival===d).length;
  const comps=(d:string)=>reportStaysOn(s,d).filter(b=>(b as unknown as {complimentary?:unknown}).complimentary).length; // pending DEC-266
  const ooo=(d:string)=>s.rooms.filter(r=>rmsOooDuring(r,d,addDays(d,1))).length;
  const out:FlashKpi[]=[
    {kpi:'Occupancy %',unit:'pct',actual:f.occ,target:tOcc??null,last:ly.occ},
    {kpi:'ADR',unit:'amt',actual:f.adr,target:tAdr??null,last:ly.adr},
    {kpi:'RevPAR',unit:'amt',actual:f.revpar,target:tRev,last:ly.revpar},
    {kpi:'Arrivals',unit:'cnt',actual:arrivals(date),target:null,last:arrivals(ly0)},
    {kpi:'Departures',unit:'cnt',actual:departures(date),target:null,last:departures(ly0)},
    {kpi:'In-house',unit:'cnt',actual:f.occupied,target:null,last:ly.occupied},
    {kpi:'No-shows',unit:'cnt',actual:noShows(date),target:null,last:noShows(ly0)},
    {kpi:'Comps (pending DEC-266)',unit:'cnt',actual:comps(date),target:null,last:comps(ly0)},
    {kpi:'OOO',unit:'cnt',actual:ooo(date),target:null,last:ooo(ly0)},
  ];
  for(const k of [7,14,30])out.push({kpi:`Rooms on books +${k}`,unit:'cnt',actual:occOn(s,addDays(date,k)).occ,target:null,last:null});
  return out;
}
export function flashRows(s:State,date:string,compare:string):RptRow[]{
  return flashKpis(s,date).map(k=>{
    const px=k.unit; // column group prefix: pct / amt / cnt
    const base=compare==='last year'?k.last:k.target;
    const cells:Record<string,RptCell>={kpi:k.kpi,exception:k.actual!==null&&flashRed(k.actual,k.target)?`${k.kpi} more than 10 % below target`:''};
    cells[px+'Actual']=k.actual;cells[px+'Target']=k.target;cells[px+'Last']=k.last;
    cells[px+'Var']=k.actual!==null&&base!==null?Math.round((k.actual-base)*10)/10:null;
    return {kind:'row',cells};
  });
}
registerMeasReport('rpt.guests.manager-flash',FLASH_KEY);
export const managerFlash=registerReport({
  id:'rpt.guests.manager-flash',title:"Manager's flash",area:'guests',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-DAY',
  params:[{key:'from',label:'Date',type:'date',dateClass:'D-DAY'},{key:'compare',label:'Compare with',type:'enum',options:['budget','last year'],default:'budget'}],
  columns:FLASH_COLS,
  dateBasis:'One business date. Occupancy = rooms slept / rooms in service; ADR = room charge before GST / rooms slept; RevPAR = room charge / rooms in service. Targets are meas.target.* (budget); last year = the date 365 days earlier. Exception: a KPI with a target more than 10 % below it. Comps are pending DEC-266.',
  formats:['screen','pdf'],phone:'yes',
  select:(s,_uid,p)=>({params:p,columns:FLASH_COLS,rows:flashRows(s,p.from!,String(p.compare??'budget'))}),
});
