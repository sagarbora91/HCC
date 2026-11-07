// F12-RPT-PEOPLE · rpt.people.attrition — attrition and tenure (catalogue §3.10, switch meas.people.attrition). Data: HR staff master + exit reasons (measX.exit).
// Server twin: AttritionReport.cs.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {measTarget} from '../../streams/measure';
import {hrSlice} from '../../streams/staff';
import {addDays} from '../../money';
import {T,I,P,EXC,pct1,deptList,row,CAPTURE_NEEDED} from './shared';

export const ATTRITION_ID='rpt.people.attrition';
export const ATTRITION_COLS=[T('dept','Department'),I('joiners','Joiners'),I('leavers','Leavers'),T('exitReasons','Exit reasons'),T('tenure','Tenure at exit'),P('attritionPct','12-month attrition %'),
  I('vacancyDays','Vacancy days'),EXC];
export const ATTRITION_CAPTURE='exit reason (staff.exitReason list, meas-cap-people)';

/** Annual attrition above the limit: leavers over the trailing 12 months / average headcount (opening + closing) / 2. Integer arithmetic. */
export const attritionRed=(leavers12:number,headOpen:number,headClose:number,limitPct:number)=>leavers12>0&&leavers12*100*2>limitPct*(headOpen+headClose);

export function attritionExitDate(s:State,st:{id:string;exitedOn?:string}):string|undefined{
  return s.measX?.exit?.[st.id]?.exitDate??st.exitedOn;
}
const monthsBetween=(a:string,b:string)=>Math.max(0,Math.round((Date.parse(b+'T00:00:00Z')-Date.parse(a+'T00:00:00Z'))/86400000/30.4375*10)/10);

export function attritionRows(s:State,from:string,to:string,deptFilter?:string):RptRow[]{
  const x=hrSlice(s),limit=measTarget(s,'meas.target.attrition')!,w0=addDays(to,-364);
  const headAt=(dept:string,d:string)=>x.staff.filter(z=>z.dept===dept&&z.joined<=d&&(!attritionExitDate(s,z)||attritionExitDate(s,z)!>d)).length;
  const rows:RptRow[]=[];
  for(const d of deptList(s,deptFilter)){
    const staff=x.staff.filter(z=>z.dept===d.id);
    const joined=staff.filter(z=>z.joined>=from&&z.joined<=to);
    const left=staff.filter(z=>{const e=attritionExitDate(s,z);return !!e&&e>=from&&e<=to;});
    const left12=staff.filter(z=>{const e=attritionExitDate(s,z);return !!e&&e>=w0&&e<=to;});
    if(!staff.length)continue;
    const reasons=new Map<string,number>();
    for(const z of left){const r=s.measX?.exit?.[z.id]?.reason??'Not recorded';reasons.set(r,(reasons.get(r)??0)+1);}
    const tenure=left.length?(left.reduce((a,z)=>a+monthsBetween(z.joined,attritionExitDate(s,z)!),0)/left.length).toFixed(1)+' months':'';
    // vacancy: from each leaver's exit to the next joiner of the same department (or the end of the period)
    const vac=left.reduce((a,z)=>{const e=attritionExitDate(s,z)!;const next=staff.filter(j=>j.joined>e).map(j=>j.joined).sort()[0];
      const end=next&&next<=to?next:to;return a+Math.max(0,Math.round((Date.parse(end+'T00:00:00Z')-Date.parse(e+'T00:00:00Z'))/86400000));},0);
    const open=headAt(d.id,w0),close=headAt(d.id,to);
    rows.push(row({dept:d.name,joiners:joined.length,leavers:left.length,exitReasons:[...reasons].sort((a,b)=>b[1]-a[1]||a[0].localeCompare(b[0])).map(([r,n])=>`${r} x${n}`).join('; '),tenure,
      attritionPct:pct1(left12.length*2,open+close),vacancyDays:vac,exception:attritionRed(left12.length,open,close,limit)?1:0}));
  }
  return rows;
}

const ATTRITION_BASIS='Joiners and leavers by joining / exit date inside the financial year (exit date = the recorded exit reason\'s date, else the staff record\'s). Tenure = months from joining to exit, averaged. 12-month attrition % = leavers in the 12 months ending at the report end / average of opening and closing headcount. Vacancy days = days from each leaver\'s exit to the next joiner of the department (or the report end). Red: attrition above the annual limit (target 30 %).';
export const attrition=registerReport({
  id:ATTRITION_ID,title:'Attrition and tenure',area:'people',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HR-001'],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'},{key:'dept',label:'Department',type:'text'}],
  columns:ATTRITION_COLS,
  dateBasis:ATTRITION_BASIS,
  formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s,_uid,p)=>{
    const rows=attritionRows(s,p.from!,p.to!,typeof p.dept==='string'?p.dept:undefined);
    const noReasons=!Object.keys(s.measX?.exit??{}).length;
    return {params:p,columns:ATTRITION_COLS,rows,...(noReasons?{about:{dateBasis:`${ATTRITION_BASIS} ${CAPTURE_NEEDED}${ATTRITION_CAPTURE}. No exit reason is recorded yet; leavers show as "Not recorded".`}}:{})};
  },
});
