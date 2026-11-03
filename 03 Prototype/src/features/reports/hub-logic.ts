// F7-R2 · Reports hub — pure logic (no React): role filter, money guard, search, grouping, parameter defaults, schedule text, phone fit.
// The engine (R1) decides view / export rights per definition (rule V); the hub adds the DEC-061 guard on top so a mis-seeded grant can never
// show a money report to housekeeping / maintenance, and keeps everything testable without a browser.
import type {State,Role} from '../../domain/types';
import type {RptDefinition,RptArea,RptParam,RptSchedule,RptRun,RptExportLogEntry,RptState} from '../../domain/reports/types';

/** DEC-061: these roles never see hotel money, whatever the grants say. */
export const NO_MONEY_ROLES:readonly Role[]=['Housekeeper','Maintenance','Supervisor'];
/** Catalogue §1.9: money reports are delivered only to these roles. */
export const MONEY_RECIPIENT_ROLES:readonly Role[]=['Owner','Manager','Accountant'];
export const AREA_LABEL:Record<RptArea,string>={guests:'Guests',rooms:'Rooms',money:'Money & cash',tax:'Tax & GST',acc:'Accounting',people:'People',stock:'Stock',rest:'Restaurant',admin:'Admin & audit'};
export const AREA_ORDER:readonly RptArea[]=['guests','rooms','money','tax','acc','people','stock','rest','admin'];
export const STATUS_LABEL:Record<string,string>={built:'Built',partial:'Partly built','ACC-3':'Accounting',planned:'Planned'};

export const roleOf=(s:State,uid:string):Role|undefined=>s.users.find(u=>u.id===uid&&u.enabled)?.role;
export const roleSeesMoney=(role?:Role)=>!!role&&!NO_MONEY_ROLES.includes(role);

/** The definitions this user may open: engine view right, and money reports only for roles that may see money. */
export function hubDefs(s:State,uid:string,defs:readonly RptDefinition[],canView:(d:RptDefinition)=>boolean):RptDefinition[]{
  const role=roleOf(s,uid);if(!role)return [];   // unknown or disabled user: nothing
  const money=roleSeesMoney(role);
  return defs.filter(d=>canView(d)&&(money||!d.moneyFlag));
}
export type HubFilter={query:string;area:RptArea|'all';favOnly:boolean;favs:readonly string[];phone?:boolean};
export function filterHub(defs:readonly RptDefinition[],f:HubFilter):RptDefinition[]{
  const q=f.query.trim().toLowerCase();
  return defs.filter(d=>{
    if(f.area!=='all'&&d.area!==f.area)return false;
    if(f.favOnly&&!f.favs.includes(d.id))return false;
    if(f.phone&&d.phone==='no')return false;
    if(!q)return true;
    return [d.title,d.id,AREA_LABEL[d.area],...(d.legacyIds??[])].join(' ').toLowerCase().includes(q);
  });
}
export function groupByArea(defs:readonly RptDefinition[]):{area:RptArea;label:string;defs:RptDefinition[]}[]{
  return AREA_ORDER.map(a=>({area:a,label:AREA_LABEL[a],defs:defs.filter(d=>d.area===a).sort((x,y)=>x.priority.localeCompare(y.priority)||x.title.localeCompare(y.title))})).filter(g=>g.defs.length);
}
export const favsOf=(s:State,uid:string):string[]=>s.rptX?.favourites?.[uid]??[];
export const rptState=(s:State):RptState=>s.rptX??{favourites:{},schedules:[],runs:[],exportLog:[]};

/** Default raw parameter values from the definition and the business date (never the wall clock). */
export function defaultParams(params:readonly RptParam[],businessDate:string):Record<string,string>{
  const out:Record<string,string>={};
  for(const p of params){
    if(p.type==='range'){out.from=p.default??businessDate.slice(0,8)+'01';out.to=businessDate;}
    else if(p.type==='date')out[p.key]=p.default??businessDate;
    else if(p.type==='month')out[p.key]=p.default??businessDate.slice(0,7);
    else if(p.type==='bool')out[p.key]=p.default??'false';
    else if(p.default!==undefined)out[p.key]=p.default;
    else if(p.type==='enum'&&p.options?.length)out[p.key]=p.options[0];
    else out[p.key]='';
  }
  return out;
}
export const paramsKey=(raw:Record<string,string>)=>Object.keys(raw).sort().map(k=>k+'='+raw[k]).join('&');
/** File name <code>-<report id>-<from>-<to>.<ext> (architecture §4.2). */
export function exportFileName(code:string,id:string,raw:Record<string,string>,ext:string):string{
  const parts=[code||'HCC',id,raw.from||raw.date||raw.month||'',raw.to||''].filter(Boolean);
  return parts.join('-').replace(/[^A-Za-z0-9._-]+/g,'-')+'.'+ext;
}
export function cadenceText(c:RptSchedule['cadence']):string{
  const DOW=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
  if(c.kind==='daily')return `Every day at ${c.time}`;
  if(c.kind==='weekly')return `Every ${DOW[(c.day??1)%7]} at ${c.time}`;
  return `Day ${c.day??1} of each month at ${c.time}`;
}
export const runTone=(r:RptRun)=>r.status==='ok'?'green':r.status==='held'?'orange':'red';
export const runText=(r:RptRun)=>r.status==='ok'?'Delivered':r.status==='held'?'Held (internet delivery is off)':'Failed';

/** Users a schedule for this report may be sent to: money reports only to Owner / Manager / Accountant (§1.9). */
export function recipientChoices(s:State,def:RptDefinition):{id:string;name:string;role:Role}[]{
  return s.users.filter(u=>u.enabled&&(!def.moneyFlag||MONEY_RECIPIENT_ROLES.includes(u.role))&&(def.moneyFlag?roleSeesMoney(u.role):true)).map(u=>({id:u.id,name:u.name,role:u.role}));
}
/** Schedules the user may see: their own; the Owner sees all. */
export function visibleSchedules(s:State,uid:string):RptSchedule[]{
  const all=rptState(s).schedules;return roleOf(s,uid)==='Owner'?all:all.filter(x=>x.owner===uid);
}
export function visibleRuns(s:State,uid:string):RptRun[]{
  const st=rptState(s),mine=new Set(visibleSchedules(s,uid).map(x=>x.id));
  return st.runs.filter(r=>roleOf(s,uid)==='Owner'||r.by===uid||(r.scheduleId&&mine.has(r.scheduleId))).slice().sort((a,b)=>b.startedAt.localeCompare(a.startedAt));
}
export function visibleExportLog(s:State,uid:string):RptExportLogEntry[]{
  const all=rptState(s).exportLog;const role=roleOf(s,uid);
  return (role==='Owner'||role==='Manager'||role==='Accountant'?all:all.filter(e=>e.by===uid)).slice().sort((a,b)=>b.at.localeCompare(a.at));
}
/** Hub tabs (sub-screens of page `report-hub`). */
export const HUB_TABS=[{id:'reports',label:'Reports'},{id:'scheduled',label:'Scheduled runs'},{id:'log',label:'Export log'}] as const;
export type HubTab=typeof HUB_TABS[number]['id'];
