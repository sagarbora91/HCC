// F12-RPT-PEOPLE · rpt.people.training — training hours and certifications (catalogue §3.10, switch meas.people.training). Data: measX.training (hr.training).
// Server twin: TrainingReport.cs.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {hrSlice,hrMeasQuarter} from '../../streams/staff';
import type {MeasTraining} from '../../streams/measure-types';
import {T,I,D,EXC,deptMatches,row,CAPTURE_NEEDED} from './shared';

export const TRAINING_ID='rpt.people.training';
export const TRAINING_COLS=[T('staff','Staff'),T('course','Course'),D('date','Date'),T('hours','Hours'),T('certificateNo','Certificate no'),D('validTo','Valid to'),I('daysToExpiry','Days to expiry'),
  T('quarterHours','Hours per head this quarter'),EXC];
export const TRAINING_CAPTURE='hr.training (meas-cap-people)';

/** Mandatory certificates: [match text (lower case), label, department id restriction]. FSSAI handler is for the Kitchen only. */
export const MANDATORY_COURSES:{match:string;label:string;dept?:string}[]=[
  {match:'fire',label:'Fire safety'},{match:'first aid',label:'First aid'},{match:'posh',label:'POSH'},{match:'fssai',label:'FSSAI handler',dept:'KIT'}];
export const mandatoryKey=(course:string)=>MANDATORY_COURSES.find(m=>course.toLowerCase().includes(m.match));
const daysBetween=(a:string,b:string)=>Math.round((Date.parse(b+'T00:00:00Z')-Date.parse(a+'T00:00:00Z'))/86400000);

/** True when a mandatory certificate is expired on `asOf`: valid-to before the date and no later record of the same course for the same person that is still valid. */
export function certificateExpired(list:MeasTraining[],t:MeasTraining,asOf:string):boolean{
  if(!t.validTo||t.validTo>=asOf)return false;
  return !list.some(o=>o.staff===t.staff&&o.id!==t.id&&mandatoryKey(o.course)?.match===mandatoryKey(t.course)?.match&&(!o.validTo||o.validTo>=asOf)&&o.date>=t.date);
}

export function trainingRows(s:State,asOf:string,deptFilter?:string):RptRow[]{
  const x=hrSlice(s),list=(s.measX?.training??[]).filter(t=>t.date<=asOf),q=hrMeasQuarter(asOf);
  if(!list.length)return [];
  const staffOf=(id:string)=>x.staff.find(z=>z.id===id);
  const inDept=(id:string)=>{const st=staffOf(id);return !!st&&deptMatches(s,st.dept,deptFilter);};
  const qHours=new Map<string,number>();
  for(const t of list)if(hrMeasQuarter(t.date)===q)qHours.set(t.staff,(qHours.get(t.staff)??0)+t.hours*100);
  const rows:RptRow[]=[];
  for(const t of [...list].sort((a,b)=>a.date.localeCompare(b.date)||a.id.localeCompare(b.id))){
    if(!inDept(t.staff))continue;
    const mand=!!t.mandatory||!!mandatoryKey(t.course);
    rows.push(row({staff:staffOf(t.staff)?.name??t.staff,course:t.course,date:t.date,hours:t.hours.toFixed(1),certificateNo:t.certificateNo??'',validTo:t.validTo??null,
      daysToExpiry:t.validTo?daysBetween(asOf,t.validTo):null,quarterHours:((qHours.get(t.staff)??0)/100).toFixed(1),exception:mand&&certificateExpired(list,t,asOf)}));
  }
  // missing: an active person with no record at all of a mandatory course that applies to their department
  for(const st of x.staff.filter(z=>z.active&&!z.exitedOn&&z.joined<=asOf&&deptMatches(s,z.dept,deptFilter))){
    for(const m of MANDATORY_COURSES){
      if(m.dept&&m.dept!==st.dept)continue;
      if(list.some(t=>t.staff===st.id&&t.course.toLowerCase().includes(m.match)))continue;
      rows.push(row({staff:st.name,course:m.label+' (missing)',date:null,hours:'0.0',certificateNo:'',validTo:null,daysToExpiry:null,quarterHours:((qHours.get(st.id)??0)/100).toFixed(1),exception:true}));
    }
  }
  return rows;
}

const TRAINING_BASIS='Training records up to the as-of date. Days to expiry = valid-to minus the as-of date (negative = expired). Hours per head this quarter = that person\'s training hours in the financial-year quarter of the as-of date. Mandatory certificates: fire safety, first aid, POSH, and FSSAI handler for the Kitchen. Red: a mandatory certificate expired (and not renewed by a later valid record) or missing altogether (listed as "(missing)").';
export const training=registerReport({
  id:TRAINING_ID,title:'Training hours and certifications',area:'people',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HR-001'],dateClass:'D-ASOF',
  params:[{key:'asOf',label:'As of',type:'date',dateClass:'D-ASOF'},{key:'dept',label:'Department',type:'text'}],
  columns:TRAINING_COLS,dateBasis:TRAINING_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const rows=trainingRows(s,p.asOf as string,typeof p.dept==='string'?p.dept:undefined);
    const none=!(s.measX?.training??[]).length;
    return {params:p,columns:TRAINING_COLS,rows,...(none?{about:{dateBasis:`${TRAINING_BASIS} ${CAPTURE_NEEDED}${TRAINING_CAPTURE}. No training record is captured yet.`}}:{})};
  },
});
