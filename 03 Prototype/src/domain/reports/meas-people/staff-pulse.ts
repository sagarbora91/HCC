// F12-RPT-PEOPLE · rpt.people.staff-pulse — anonymous staff pulse by department and quarter (catalogue §3.10, switch meas.people.pulse). Owner and Manager only.
// Department only, never a staff id; a department-quarter with fewer than 5 answers is suppressed. Server twin: StaffPulseReport.cs.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {hrDeptName} from '../../streams/staff';
import type {MeasPulse} from '../../streams/measure-types';
import {T,I,EXC,ownerManagerOnly,row,CAPTURE_NEEDED} from './shared';

export const PULSE_ID='rpt.people.staff-pulse';
export const PULSE_COLS=[T('quarter','Quarter'),T('dept','Department'),I('responses','Responses'),T('workload','Workload'),T('respect','Respect'),T('tools','Tools'),T('payFairness','Pay fairness'),
  T('recommend','Would recommend'),I('enps','eNPS'),EXC];
export const PULSE_CAPTURE='hr.pulse (meas-cap-people)';
/** Fewer answers than this in a department-quarter and the figures are not shown. */
export const PULSE_MIN_ANSWERS=5;

/** eNPS = promoters (9-10) minus detractors (0-6), as whole percent of the answers (half away from zero). */
export function enps(recommend:number[]):number{
  const n=recommend.length;if(!n)return 0;
  const pro=recommend.filter(v=>v>=9).length,det=recommend.filter(v=>v<=6).length;
  const d=(pro-det)*100;return d<0?-Math.round(-d/n):Math.round(d/n);
}
const avg=(v:number[])=>(Math.round(v.reduce((a,x)=>a+x,0)*10/v.length)/10).toFixed(1);

export function pulseRows(s:State,fy:string,quarter?:string):{rows:RptRow[];suppressed:number}{
  const list=(s.measX?.pulse??[]).filter(p=>p.quarter.startsWith(fy+'-')&&(!quarter||p.quarter.endsWith('-'+quarter)));
  const groups=new Map<string,MeasPulse[]>();
  for(const p of list){const k=p.quarter+'|'+p.dept;(groups.get(k)??groups.set(k,[]).get(k)!).push(p);}
  const rows:RptRow[]=[];let suppressed=0;
  for(const [k,v] of [...groups].sort((a,b)=>a[0].localeCompare(b[0]))){
    if(v.length<PULSE_MIN_ANSWERS){suppressed++;continue;}
    const e=enps(v.map(x=>x.recommend));
    rows.push(row({quarter:v[0].quarter,dept:hrDeptName(s,k.split('|')[1]),responses:v.length,workload:avg(v.map(x=>x.workload)),respect:avg(v.map(x=>x.respect)),tools:avg(v.map(x=>x.tools)),
      payFairness:avg(v.map(x=>x.payFairness)),recommend:avg(v.map(x=>x.recommend)),enps:e,exception:e<0?1:0}));
  }
  return {rows,suppressed};
}

const PULSE_BASIS='Anonymous pulse answers (four 1-5 scores and a 0-10 recommend score) by financial-year quarter and department; no staff, user or name is stored or shown. A department with fewer than 5 answers in a quarter is suppressed. Scores are averages; eNPS = promoters (9-10) minus detractors (0-6) in whole percent. Red: eNPS below 0.';
export const staffPulse=registerReport({
  id:PULSE_ID,title:'Staff pulse (optional)',area:'people',priority:'P3',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HR-002'],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'},{key:'quarter',label:'Quarter',type:'enum',options:['Q1','Q2','Q3','Q4']}],
  columns:PULSE_COLS,dateBasis:PULSE_BASIS,formats:['screen','pdf'],phone:'yes',
  select:(s,uid,p)=>{
    ownerManagerOnly(s,uid,'The staff pulse');
    const {rows,suppressed}=pulseRows(s,p.fy as string,typeof p.quarter==='string'?p.quarter:undefined);
    const none=!(s.measX?.pulse??[]).length;
    const note=none?` ${CAPTURE_NEEDED}${PULSE_CAPTURE}. No pulse answer is captured yet.`:suppressed?` ${suppressed} department-quarter${suppressed===1?'':'s'} suppressed (fewer than ${PULSE_MIN_ANSWERS} answers).`:'';
    return {params:p,columns:PULSE_COLS,rows,...(note?{about:{dateBasis:PULSE_BASIS+note}}:{})};
  },
});
