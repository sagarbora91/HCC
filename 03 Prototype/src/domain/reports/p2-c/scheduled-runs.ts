// F8-RPT-P2-C · rpt.admin.scheduled-runs — runs started by a schedule (not by hand), with recipients, result and error, newest first. Adapter over s.rptX.
// Money reports are delivered only to Owner / Manager / Accountant (catalogue 1.9); this report only lists the runs, it holds no figures.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {reportDef} from '../catalogue';
import {col,rowOf,rangeParams,userName} from '../money/common';
import {cap92,stamp} from './shared';

export const SCHED_RUN_COLS:RptColumn[]=[col('schedule','Schedule','text'),col('report','Report','text'),col('runAt','Run at','text'),col('recipients','Recipients','text'),col('result','Result','text'),col('error','Error','text')];
export const scheduledRuns=registerReport({
  id:'rpt.admin.scheduled-runs',title:'Scheduled runs',area:'admin',priority:'P2',status:'built',moneyFlag:false,viewPerms:['PERM-ADM-004'],dateClass:'D-B',
  params:rangeParams('D-B'),columns:SCHED_RUN_COLS,
  dateBasis:'Runs started by a schedule, by the date and time the run started, newest first. Result: ok, failed or held.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    cap92(p);
    const X=s.rptX,sch=new Map((X?.schedules??[]).map(x=>[x.id,x]));
    const runs=(X?.runs??[]).filter(r=>r.scheduleId&&r.startedAt.slice(0,10)>=p.from!&&r.startedAt.slice(0,10)<=p.to!).sort((a,b)=>b.startedAt.localeCompare(a.startedAt)||b.id.localeCompare(a.id));
    const rows:RptRow[]=runs.map(r=>{
      const sc=sch.get(r.scheduleId!),title=reportDef(r.reportId)?.title??r.reportId;
      return rowOf('row',{schedule:sc?`${title} · ${sc.cadence.kind} ${sc.cadence.time} · ${userName(s,sc.owner)}`:r.scheduleId!,report:title,runAt:stamp(r.startedAt),
        recipients:sc?sc.recipients.map(u=>userName(s,u)).join(', ')||null:null,result:r.status,error:r.error||null});});
    return {params:p,columns:SCHED_RUN_COLS,rows};
  },
});
