// F12-RPT-PEOPLE · helpers and pure rules shared by the five people measurement reports (MEAS-CONTRACT §1, §4, §8).
// Pay amounts never appear here: overtime and absenteeism are minutes / days. Server twin: Hcc.Reporting/Reports/MeasPeople/PeopleRules.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {RPT_V2_GRANTS} from '../mask';
import {HccError} from '../../errors';
import {hrSlice,hrDeptName} from '../../streams/staff';

/** PERM-HR-001 (hr.staff.view, server seed 0901: Owner, Manager, Accountant, Tech admin) and PERM-HR-002 (Owner, Manager) are not in the prototype's v1
 *  matrix; the grants are registered here (same pattern as the F7 lanes in mask.ts) so rptHolds answers like the server. */
RPT_V2_GRANTS['PERM-HR-001']=['Owner','Manager','Accountant','Tech admin'];
RPT_V2_GRANTS['PERM-HR-002']=['Owner','Manager'];

export const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
/** Trailing column: the exception rule of the catalogue entry drove this row red (the engine has no row flag; every renderer prints Yes / No). */
export const EXC:RptColumn={key:'exception',label:'Exception',type:'bool',money:false};

export const CAPTURE_NEEDED='Capture needed: ';
/** One decimal of a ratio num/den expressed as a percentage: integer arithmetic, half away from zero (server: same). */
export function pct1(num:number,den:number):number{return den>0?Math.round(num*1000/den)/10:0;}
/** Decimal text with `d` places from an integer count of 1/10^d units (hours = minutes / 60 is formatted by hoursText). */
export const dec=(n:number,d:number)=>n.toFixed(d);
/** Minutes as hours with one decimal ("12.5"). */
export const hoursText=(min:number)=>(Math.round(min*10/60)/10).toFixed(1);
export const monthOf=(date:string)=>date.slice(0,7);

/** Shift length in minutes from "HH:MM" start / end (an end at or before the start crosses midnight). */
export function shiftMinutes(start:string,end:string):number{
  const m=(t:string)=>{const [h,mm]=t.split(':').map(Number);return (h||0)*60+(mm||0);};
  if(!start||!end)return 0;const a=m(start),b=m(end);return b>a?b-a:b+1440-a;
}
/** Department filter value: id or name, case-insensitive; '' = all. */
export const deptMatches=(s:State,deptId:string,filter?:string)=>{
  if(!filter)return true;const f=filter.trim().toLowerCase();return deptId.toLowerCase()===f||hrDeptName(s,deptId).toLowerCase()===f;
};
export const deptList=(s:State,filter?:string)=>hrSlice(s).depts.filter(d=>deptMatches(s,d.id,filter));

/** Owner / Manager only (pulse, scorecard): the selector refuses any other role even when the permission grants wider. */
export function ownerManagerOnly(s:State,uid:string,what:string){
  const u=s.users.find(x=>x.id===uid);
  if(!u||(u.role!=='Owner'&&u.role!=='Manager'))throw new HccError('FORBIDDEN',`${what} is for the Owner and Manager only.`);
}
export const row=(cells:RptRow['cells'],kind:RptRow['kind']='row'):RptRow=>({kind,cells});
/** Nearest-rank percentile of a numeric list (p in 0..1); server twin uses the same index rule. */
export function nearestRank(values:number[],p:number):number{
  if(!values.length)return 0;const v=[...values].sort((a,b)=>a-b);return v[Math.max(0,Math.ceil(p*v.length)-1)];
}
/** Outlier rule of the agent scorecard: team of at least 4, value above zero and above the team's upper quartile (Q3). */
export function aboveUpperQuartile(team:number[],value:number):boolean{return team.length>=4&&value>0&&value>nearestRank(team,0.75);}
