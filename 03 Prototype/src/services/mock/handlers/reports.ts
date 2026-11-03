// F7-R1 · report engine commands (F7-CONTRACT §2): favourites, schedules, the mock outbox tick for scheduled runs, and the print / export log.
// RPT_FAV_TOGGLE {reportId} · RPT_SCHEDULE_SAVE {id?,reportId,params,format,cadence,recipients,enabled} · RPT_SCHEDULE_DELETE {id}
// RPT_RUN_DUE {} (delivery held while the internet egress gate is OFF, F5) · RPT_LOG {reportId,params,format,rows,fileName?,provisional?}
import type {Handler} from '../commands';
import type {State} from '../../../domain/types';
import type {RptState,RptSchedule,RptRun} from '../../../domain/reports/types';
import {reportDef,canViewReport,canExportReport,runDef} from '../../../domain/reports/engine';
import {netEnqueue} from '../../../domain/streams/egress';
import {HccError} from '../../../domain/errors';

export const rptSlice=(s:State):RptState=>{const x=s.rptX??(s.rptX={favourites:{},schedules:[],runs:[],exportLog:[]});x.favourites??={};x.schedules??=[];x.runs??=[];x.exportLog??=[];return x;};
/** Roles a money report may be delivered to (catalogue §1.9). */
export const RPT_MONEY_RECIPIENT_ROLES=['Owner','Manager','Accountant'];
const FORMATS=['xlsx','pdf','csv'];
const TIME=/^([01]\d|2[0-3]):[0-5]\d$/;
const weekday=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
/** Due on business date d at clock time t: daily; weekly on `day` (0 = Sunday); monthly on `day` (1–28). Once per date. */
export function rptScheduleDue(sc:RptSchedule,d:string,t:string){
  if(!sc.enabled||(sc.lastRun&&sc.lastRun.slice(0,10)>=d)||t<sc.cadence.time)return false;
  return sc.cadence.kind==='daily'||(sc.cadence.kind==='weekly'&&weekday(d)===sc.cadence.day)||(sc.cadence.kind==='monthly'&&+d.slice(8,10)===sc.cadence.day);
}
/** Relative date tokens in schedule params → dates on the business date. */
export function rptResolveTokens(params:Record<string,string>,today:string):Record<string,string>{
  const add=(n:number)=>new Date(Date.parse(today+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
  const map:Record<string,string>={today,yesterday:add(-1),'month-start':today.slice(0,8)+'01'};
  const out={...params};
  if(params.range==='yesterday'){out.from=out.to=add(-1);}
  if(params.range==='month-to-date'){out.from=today.slice(0,8)+'01';out.to=today;}
  delete out.range;
  for(const k of Object.keys(out))if(map[out[k]])out[k]=map[out[k]];
  return out;
}

export const handlers:Record<string,Handler>={
  RPT_FAV_TOGGLE:k=>{
    const def=reportDef(String(k.cmd.reportId??''));k.assert(def,'No such report.','NOT_FOUND');
    if(!canViewReport(k.s,k.uid,def!))k.fail('FORBIDDEN','You cannot open this report.');
    const x=rptSlice(k.s);const list=x.favourites[k.uid]??[];const on=!list.includes(def!.id);
    x.favourites={...x.favourites,[k.uid]:on?[...list,def!.id]:list.filter(i=>i!==def!.id)};
    return `${def!.id} ${on?'added to':'removed from'} favourites`;
  },
  RPT_SCHEDULE_SAVE:k=>{
    const {cmd,s,uid,assert,fail}=k;
    const def=reportDef(String(cmd.reportId??''));assert(def,'No such report.','NOT_FOUND');
    if(!canExportReport(s,uid,def!))fail('FORBIDDEN','A schedule sends a file: you need to view and export this report.');
    const format=String(cmd.format??'');assert(FORMATS.includes(format)&&def!.formats.includes(format as never),'Choose a file format this report offers.');
    const c=cmd.cadence??{};assert(['daily','weekly','monthly'].includes(c.kind)&&TIME.test(String(c.time??'')),'Cadence: daily, weekly or monthly at HH:mm.');
    if(c.kind==='weekly')assert(Number.isInteger(c.day)&&c.day>=0&&c.day<=6,'Weekly: choose a day (0 = Sunday).');
    if(c.kind==='monthly')assert(Number.isInteger(c.day)&&c.day>=1&&c.day<=28,'Monthly: choose a day from 1 to 28.');
    const recipients:string[]=Array.isArray(cmd.recipients)?cmd.recipients.map(String):[];
    assert(recipients.length>=1&&recipients.length<=10,'Choose 1–10 recipients.');
    for(const r of recipients){const u=s.users.find(x=>x.id===r);assert(u&&u.enabled,`Recipient ${r} is not an active user.`);
      if(def!.moneyFlag&&!RPT_MONEY_RECIPIENT_ROLES.includes(u!.role))fail('FORBIDDEN',`A money report goes only to the Owner, Manager or Accountant (${u!.name.split(' · ')[0]} is ${u!.role}).`);}
    const params:Record<string,string>=Object.fromEntries(Object.entries(cmd.params??{}).map(([a,b])=>[a,String(b)]));
    const x=rptSlice(s);const existing=cmd.id?x.schedules.find(y=>y.id===cmd.id):undefined;
    if(cmd.id){assert(existing,'Schedule not found.','NOT_FOUND');if(existing!.owner!==uid&&k.user.role!=='Owner')fail('FORBIDDEN','Only its owner or the Owner changes a schedule.');}
    const sc:RptSchedule={id:existing?.id??k.id('RPS'),reportId:def!.id,params,format:format as RptSchedule['format'],cadence:{kind:c.kind,...(c.day!==undefined?{day:c.day}:{}),time:c.time},
      owner:existing?.owner??uid,recipients,enabled:cmd.enabled!==false,...(existing?.lastRun?{lastRun:existing.lastRun}:{})};
    x.schedules=existing?x.schedules.map(y=>y.id===sc.id?sc:y):[...x.schedules,sc];
    return `${sc.id} · ${def!.id} · ${sc.cadence.kind} ${sc.cadence.time} · ${format}`;
  },
  RPT_SCHEDULE_DELETE:k=>{
    const x=rptSlice(k.s);const sc=x.schedules.find(y=>y.id===k.cmd.id);k.assert(sc,'Schedule not found.','NOT_FOUND');
    if(sc!.owner!==k.uid&&k.user.role!=='Owner')k.fail('FORBIDDEN','Only its owner or the Owner deletes a schedule.');
    x.schedules=x.schedules.filter(y=>y.id!==sc!.id);return `${sc!.id} deleted`;
  },
  RPT_RUN_DUE:k=>{
    const {s}=k;if(!['Owner','Manager','Accountant'].includes(k.user.role))k.fail('FORBIDDEN','The report outbox tick is run by the Owner, Manager or Accountant.');
    const x=rptSlice(s);const t=s.clock.slice(11,16);let ok=0,held=0,failed=0;const runs:RptRun[]=[...x.runs];
    for(const sc of x.schedules.filter(y=>rptScheduleDue(y,s.date,t))){
      const params=rptResolveTokens(sc.params,s.date);
      const run:RptRun={id:k.id('RPR'),scheduleId:sc.id,reportId:sc.reportId,params,by:sc.owner,startedAt:s.clock,status:'ok'};
      try{
        const def=reportDef(sc.reportId);if(!def)throw new HccError('NOT_FOUND','The report no longer exists.');
        // Runs as the schedule owner's role (catalogue §1.9): it never widens what that role may see or export.
        const r=runDef(s,sc.owner,def,params,{all:true,purpose:'export'});
        run.rows=(r.sections??[{rows:r.rows}]).reduce((n,y)=>n+y.rows.length,0);run.fileRef=`rpt/${run.id}.${sc.format}`;
        const it=netEnqueue(s,'scheduled-report','email',run.id);
        if(it.status==='held'){run.status='held';held++;}else ok++;
      }catch(e){run.status='failed';run.error=e instanceof Error?e.message:String(e);failed++;}
      run.finishedAt=s.clock;runs.push(run);
      x.schedules=x.schedules.map(y=>y.id===sc.id?{...y,lastRun:s.clock}:y);
    }
    x.runs=runs.slice(-500);
    return `Scheduled reports: ${ok} sent, ${held} held, ${failed} failed`;
  },
  RPT_LOG:k=>{
    const {cmd,s,uid,assert,fail}=k;
    const def=reportDef(String(cmd.reportId??''));assert(def,'No such report.','NOT_FOUND');
    const format=String(cmd.format??'');assert(['print','xlsx','pdf','csv','zip','xml'].includes(format),'Unknown format.');
    if(format==='print'?!canViewReport(s,uid,def!):!canExportReport(s,uid,def!))fail('FORBIDDEN',format==='print'?'You cannot print this report.':'You cannot export this report.');
    const rows=Number(cmd.rows??0);assert(Number.isInteger(rows)&&rows>=0,'Row count.');
    const x=rptSlice(s);const id=k.id('RPL');
    x.exportLog=[...x.exportLog,{id,at:s.clock,by:uid,reportId:def!.id,params:Object.fromEntries(Object.entries(cmd.params??{}).map(([a,b])=>[a,String(b)])),
      format:format as never,rows,...(cmd.fileName?{fileName:String(cmd.fileName)}:{}),provisional:cmd.provisional===true}];
    return `${id} · ${def!.id} · ${format} · ${rows} rows`;
  },
};
