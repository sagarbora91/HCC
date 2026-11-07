// F12 MEAS · meas-study-pack · rpt.study.management-pack (catalogue 6.22, plan 7A): one PDF + Excel combining the study reports, chapter per
// workbook family, contents page, executive summary + scorecard, footer "Legacy to 30 Oct 2026 / HCC live". The study reports are composed BY ID
// through the registry (reportDef + runDef as the caller, so their own switches, rule M money removal and rule V masks apply); an id that is not
// registered yet, switched off or not visible to the caller is skipped with a note in the contents. Scheduled by `meas.study.packSchedule`.
// Server twin: 08 Production/src/Hcc.Reporting/Reports/StudyPack/ManagementPackReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow,RptSchedule} from '../types';
import {registerReport,reportDef} from '../catalogue';
import {runDef} from '../engine';
import {rptTables} from '../tables';
import {rptFyOf} from '../params';
import {measReportOn,measTarget,measOn} from '../../streams/measure';
import {chartSection} from '../charts/chart';
import {HccError} from '../../errors';

export const PACK_ID='rpt.study.management-pack';
export const PACK_KEY='meas.study.management-pack';
export const PACK_FOOTER='Legacy to 30 Oct 2026 / HCC live';
/** Chapters (workbook families, plan 7A) in pack order. Ids are the catalogue §3.10 ids; the pack itself is not listed. */
export const PACK_CHAPTERS:{no:number;title:string;ids:string[]}[]=[
  {no:1,title:'Revenue and cash',ids:['rpt.study.revenue-monthly','rpt.study.year-comparison','rpt.study.index-vs-base','rpt.study.fy-deep-dive']},
  {no:2,title:'Occupancy and operations',ids:['rpt.study.occupancy-history','rpt.study.operations-patterns','rpt.study.rooms-and-rates','rpt.study.zero-rate-complimentary']},
  {no:3,title:'Receivables',ids:['rpt.study.receivables-bad-debt']},
  {no:4,title:'Revenue management',ids:['rpt.study.revenue-kpi','rpt.study.channel-economics','rpt.study.pricing-shape']},
  {no:5,title:'Guests',ids:['rpt.study.guest-repeat-value','rpt.study.guest-origin-plan']},
  {no:6,title:'Housekeeping, F&B and halls',ids:['rpt.study.housekeeping-operations','rpt.study.fnb-halls-ancillary']},
  {no:7,title:'Finance, sales and compliance',ids:['rpt.study.finance-cost','rpt.study.corporate-accounts','rpt.study.compliance-risk']},
  {no:8,title:'Forecast and Owner dashboard',ids:['rpt.study.forecast-scenarios','rpt.study.owner-dashboard']},
];
export const PACK_STUDY_IDS=PACK_CHAPTERS.flatMap(c=>c.ids);

const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const PACK_CONTENTS_COLS:RptColumn[]=[I('no','No.'),T('chapter','Chapter'),T('report','Report id'),T('title','Report'),T('status','Status'),I('blocks','Blocks')];
export const PACK_SUMMARY_COLS:RptColumn[]=[T('fy','FY'),T('period','Period'),I('rooms','Rooms in service'),I('roomNights','Room-nights slept'),I('chargedNights','Charged nights'),
  M('roomRevenue','Room revenue (before GST)'),P('occupancy','Occupancy %'),M('adr','ADR'),M('revpar','RevPAR')];
export const PACK_SCORE_COLS:RptColumn[]=[T('kpi','KPI'),P('actualPct','Actual %'),P('targetPct','Target %'),M('actualMoney','Actual'),M('targetMoney','Target'),T('status','Status')];
export const PACK_NOTE_COLS:RptColumn[]=[T('note','Note')];

const row=(cells:RptRow['cells'],kind:RptRow['kind']='row'):RptRow=>({kind,cells});
const div1=(a:number,b:number)=>b>0?Math.round(a*1000/b)/10:0;          // percent, one decimal
const divP=(a:number,b:number)=>b>0?Math.round(a/b):0;                   // paise
const days=(a:string,b:string)=>Math.round((Date.parse(b+'T00:00:00Z')-Date.parse(a+'T00:00:00Z'))/86400000)+1;
const addDays=(d:string,n:number)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
const monthEnd=(m:string)=>new Date(Date.UTC(+m.slice(0,4),+m.slice(5,7),0)).toISOString().slice(0,10);
const MON=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

/** Executive summary on HCC live data, contract §9.2 definitions: occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room
 *  charge before GST ÷ charged nights (room lines by their date); halls excluded. Monthly rows + the FY row. */
export function packSummary(s:State,from:string,to:string){
  const rooms=(s.rooms??[]).filter(r=>!/hall/i.test(r.type)).length;
  const months:string[]=[];for(let m=from.slice(0,7);m<=to.slice(0,7);m=addDays(monthEnd(m),1).slice(0,7))months.push(m);
  const acc=new Map(months.map(m=>[m,{slept:new Set<string>(),charged:0,rev:0}]));
  for(const b of s.bookings??[]){if(b.status==='Cancelled'||b.status==='Expired')continue;
    for(const l of b.lines??[]){if(l.kind!=='room'||l.date<from||l.date>to)continue;const a=acc.get(l.date.slice(0,7));if(!a)continue;
      a.slept.add(b.room+'|'+l.date);a.charged++;a.rev+=l.amount;}}
  const out=months.map(m=>{const a=acc.get(m)!;const f=m+'-01'<from?from:m+'-01',t=monthEnd(m)>to?to:monthEnd(m);const cap=rooms*days(f,t);
    return {month:m,label:MON[+m.slice(5,7)-1]+' '+m.slice(2,4),roomNights:a.slept.size,chargedNights:a.charged,roomRevenue:a.rev,cap,
      occupancy:div1(a.slept.size,cap),adr:divP(a.rev,a.charged),revpar:divP(a.rev,cap)};});
  const tot=out.reduce((x,m)=>({roomNights:x.roomNights+m.roomNights,chargedNights:x.chargedNights+m.chargedNights,roomRevenue:x.roomRevenue+m.roomRevenue,cap:x.cap+m.cap}),{roomNights:0,chargedNights:0,roomRevenue:0,cap:0});
  return {rooms,months:out,fy:{...tot,occupancy:div1(tot.roomNights,tot.cap),adr:divP(tot.roomRevenue,tot.chargedNights),revpar:divP(tot.roomRevenue,tot.cap)}};
}

type Sec={key:string;title?:string;columns:RptColumn[];rows:RptRow[]};
/** Composes the pack for the caller. Exported for the server parity notes and the tests. */
export function packSections(s:State,uid:string,p:{fy?:string;from?:string;to?:string}):Sec[]{
  const fy=String(p.fy??rptFyOf(s.date)),from=String(p.from??fy.slice(0,4)+'-04-01'),to=String(p.to??(+fy.slice(0,4)+1)+'-03-31');
  const contents:RptRow[]=[];const chapters:Sec[]=[];
  for(const ch of PACK_CHAPTERS)for(const id of ch.ids){
    const def=reportDef(id);let status='included',blocks=0;
    if(!measReportOn(s,id))status='switched off';
    else if(!def)status='not built yet';
    else try{
      const r=runDef(s,uid,def,{fy},{all:true});
      for(const t of rptTables(r)){chapters.push({key:`${id}:${t.key}`,title:`${ch.no}. ${def.title}${t.title?' · '+t.title:''}`,columns:t.columns,rows:t.rows});blocks++;}
    }catch(e){status=e instanceof HccError&&e.code==='FORBIDDEN'?'not visible to you':'not available: '+(e instanceof Error?e.message:String(e));}
    contents.push(row({no:ch.no,chapter:ch.title,report:id,title:def?.title??id,status,blocks}));
  }
  const sm=packSummary(s,from,to);
  const summary:RptRow[]=[...sm.months.map(m=>row({fy,period:m.label,rooms:sm.rooms,roomNights:m.roomNights,chargedNights:m.chargedNights,roomRevenue:m.roomRevenue,occupancy:m.occupancy,adr:m.adr,revpar:m.revpar})),
    row({fy,period:'FY '+fy,rooms:sm.rooms,roomNights:sm.fy.roomNights,chargedNights:sm.fy.chargedNights,roomRevenue:sm.fy.roomRevenue,occupancy:sm.fy.occupancy,adr:sm.fy.adr,revpar:sm.fy.revpar},'total')];
  const tOcc=measTarget(s,'meas.target.occupancy')??35,tAdr=measTarget(s,'meas.target.adr')??205000;
  const score:RptRow[]=[row({kpi:'Occupancy',actualPct:sm.fy.occupancy,targetPct:tOcc,actualMoney:null,targetMoney:null,status:sm.fy.occupancy>=tOcc?'On target':'Below target'}),
    row({kpi:'ADR (before GST)',actualPct:null,targetPct:null,actualMoney:sm.fy.adr,targetMoney:tAdr,status:sm.fy.adr>=tAdr?'On target':'Below target'})];
  const labels=sm.months.map(m=>m.label);
  const included=contents.filter(r=>r.cells.status==='included').length;
  const notes:RptRow[]=[row({note:`Footer: ${PACK_FOOTER}. Legacy rows carry the legacy marker; HCC live data starts 31 Oct 2026.`}),
    row({note:`${included} of ${PACK_STUDY_IDS.length} study reports included; the others are listed in the contents with the reason.`}),
    row({note:'Definitions (contract §9.2): occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room charge before GST ÷ charged nights; FY April–March; halls excluded.'})];
  return [
    {key:'contents',title:'Contents',columns:PACK_CONTENTS_COLS,rows:contents},
    {key:'summary',title:'Executive summary',columns:PACK_SUMMARY_COLS,rows:summary},
    {key:'scorecard',title:'Scorecard',columns:PACK_SCORE_COLS,rows:score},
    chartSection({id:'pack-revenue',title:'Room revenue by month',kind:'bar',labelTitle:'Month',labels,series:[{key:'roomRevenue',label:'Room revenue (before GST)',type:'money',values:sm.months.map(m=>m.roomRevenue)}]}),
    chartSection({id:'pack-occupancy',title:'Occupancy by month',kind:'line',labelTitle:'Month',labels,series:[{key:'occupancy',label:'Occupancy %',type:'pct',values:sm.months.map(m=>m.occupancy)}]}),
    ...chapters,
    {key:'notes',title:'Notes',columns:PACK_NOTE_COLS,rows:notes},
  ];
}

export const managementPack=registerReport({
  id:PACK_ID,title:'Management study pack (PDF + Excel)',area:'study',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'}],
  columns:PACK_CONTENTS_COLS,
  dateBasis:`Study reports on their own bases (bill month, night date); executive summary on room lines by night date · ${PACK_FOOTER}`,
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,uid,p)=>{const sections=packSections(s,uid,p);return {params:p,columns:sections[0].columns,rows:sections[0].rows,sections,about:{legacy:String(p.fy??'')<'2026-27'}};},
});

// ── Schedule (meas.study.packSchedule: off / monthly / quarterly; M-10: monthly on the 1st to Owner + Manager, Manager's copy run as the Manager) ──
export type PackSchedule='off'|'monthly'|'quarterly';
export const PACK_SCHEDULE_IDS={owner:'RPS-STUDY-PACK-OWN',manager:'RPS-STUDY-PACK-MGR'} as const;
export function packScheduleSetting(s:State):PackSchedule{const v=s.configX?.values?.['meas.study.packSchedule'];return v==='off'||v==='quarterly'?v:'monthly';}
/** True on the dates the pack is due: monthly = every 1st; quarterly = 1 Apr / 1 Jul / 1 Oct / 1 Jan; off = never. */
export function packScheduleDue(setting:PackSchedule,date:string):boolean{
  if(setting==='off'||date.slice(8,10)!=='01')return false;
  return setting==='monthly'||[1,4,7,10].includes(+date.slice(5,7));
}
/** The F7 schedules the setting implies on business date `s.date`: one per recipient role (Owner, Manager), monthly on day 1 at 06:00, PDF, for the
 *  FY of the month just closed; `enabled` = the setting is due this month and the pack item is on. Other schedules are returned unchanged.
 *  The F7 tick (RPT_RUN_DUE) then runs them; call this before the tick (integration: one line in handlers/reports.ts, see DONE). */
export function packScheduleSync(s:State):RptSchedule[]{
  const list=(s.rptX?.schedules??[]).filter(x=>x.id!==PACK_SCHEDULE_IDS.owner&&x.id!==PACK_SCHEDULE_IDS.manager);
  const setting=packScheduleSetting(s);const monthStart=s.date.slice(0,8)+'01';
  const on=packScheduleDue(setting,monthStart)&&measOn(s,PACK_KEY);
  const fy=rptFyOf(addDays(monthStart,-1));const prev=(id:string)=>s.rptX?.schedules.find(x=>x.id===id);
  const mk=(id:string,user:string|undefined):RptSchedule[]=>user?[{id,reportId:PACK_ID,params:{fy},format:'pdf',cadence:{kind:'monthly',day:1,time:'06:00'},owner:user,recipients:[user],enabled:on,...(prev(id)?.lastRun?{lastRun:prev(id)!.lastRun}:{})}]:[];
  const first=(role:string)=>s.users.find(u=>u.role===role&&u.enabled)?.id;
  return [...list,...mk(PACK_SCHEDULE_IDS.owner,first('Owner')),...mk(PACK_SCHEDULE_IDS.manager,first('Manager'))];
}
