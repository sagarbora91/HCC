// AF-SYNC-OPS (OPS-03 + interim EX-01) · Report hub favourites, schedules, run history and the device-side export log.
//   server  GET /reports/favourites · POST /reports/favourites/{id} (toggle) · GET/POST /reports/schedules · PUT/DELETE /reports/schedules/{sid}
//           GET /reports/runs · POST /reports/{id}/print-log        (Hcc.Api ReportEndpoints, Hcc.Reporting ReportService)
// These are plain routes, not /commands/: no Idempotency-Key, and they are never retried (a favourite toggle must not flip twice). The
// slice rptX has no other owner in the sync layer, so this area owns it; the export log (exportLog) is write-only from this device.
// RPT_LOG is the interim EX-01 closure: a print / CSV / Excel made on the device is logged on the server with its format. RPT_RUN_DUE is
// the server worker's job and stays unmapped.
import {HccClientError} from '../errors';
import {serverId,protoId,protoUserId,utcToClock} from './map';
import type {RptState,RptSchedule,RptRun} from '../../domain/reports/types';
import type {SyncArea,CommandCtx} from './types';

const text=(v:unknown)=>String(v??'').trim();
const SCHEDULE_PERM='PERM-RPT-004';
const FORMATS=['screen','xlsx','pdf','csv','zip','xml','print'];

export type SrvSchedule={id:number;reportId:string;params:Record<string,string>|null;format:string|number;cadence:string;day:number|null;time:string;ownerPrincipalId:number;
  recipients:number[];enabled:boolean;lastRun:string|null};
export type SrvRun={id:number;scheduleId:number|null;reportId:string;paramsJson:string;principalId:number;startedAt:string;finishedAt:string|null;status:string;rows:number|null;fileRef:string|null;error:string|null};

const formatOf=(f:string|number):RptSchedule['format']=>(typeof f==='number'?FORMATS[f]??'xlsx':String(f).toLowerCase()) as RptSchedule['format'];
export function schedulesFromServer(rows:SrvSchedule[]):RptSchedule[]{
  return rows.map(r=>({id:protoId(r.id),reportId:r.reportId,params:{...(r.params??{})},format:formatOf(r.format),
    cadence:{kind:r.cadence as RptSchedule['cadence']['kind'],...(r.day!=null?{day:r.day}:{}),time:String(r.time).slice(0,5)},
    owner:protoUserId(r.ownerPrincipalId),recipients:(r.recipients??[]).map(protoUserId),enabled:r.enabled,...(r.lastRun?{lastRun:utcToClock(r.lastRun)}:{})}));
}
const paramsOf=(j:string):Record<string,string>=>{try{const o=JSON.parse(j);return o&&typeof o==='object'?Object.fromEntries(Object.entries(o).map(([k,v])=>[k,String(v??'')])):{};}catch{return {};}};
export function runsFromServer(rows:SrvRun[]):RptRun[]{
  return rows.map(r=>({id:protoId(r.id),...(r.scheduleId!=null?{scheduleId:protoId(r.scheduleId)}:{}),reportId:r.reportId,params:paramsOf(r.paramsJson),by:protoUserId(r.principalId),
    startedAt:utcToClock(r.startedAt),...(r.finishedAt?{finishedAt:utcToClock(r.finishedAt)}:{}),status:r.status as RptRun['status'],
    ...(r.rows!=null?{rows:r.rows}:{}),...(r.fileRef?{fileRef:r.fileRef}:{}),...(r.error?{error:r.error}:{})}));
}

const reportId=(c:CommandCtx)=>{const id=text(c.cmd.reportId);if(!id)throw new HccClientError('VALIDATION','Choose a report.');return encodeURIComponent(id);};
const strMap=(v:unknown):Record<string,string>=>Object.fromEntries(Object.entries((v&&typeof v==='object'?v:{}) as Record<string,unknown>).map(([k,x])=>[k,String(x??'')]));

export const reportsHub:SyncArea={
  id:'reportsHub',
  loaders:[{slice:'rptX',async load(c){
    const out:RptState={favourites:{},schedules:[],runs:[],exportLog:[]};
    if(c.can('reports'))out.favourites={[c.me.uid]:await c.api.get<string[]>('/reports/favourites')};
    if(c.me.permIds.includes(SCHEDULE_PERM)){
      out.schedules=schedulesFromServer(await c.api.get<SrvSchedule[]>('/reports/schedules'));
      out.runs=runsFromServer(await c.api.get<SrvRun[]>('/reports/runs'));
    }
    return out;
  }}],
  commands:[
    {type:'RPT_FAV_TOGGLE',reload:['rptX'],async send(c){const res=await c.api.transport.post<{favourite:boolean}>('/reports/favourites/'+reportId(c));return {result:res};}},
    {type:'RPT_SCHEDULE_SAVE',reload:['rptX'],async send(c){const k=c.cmd;const cad=(k.cadence&&typeof k.cadence==='object'?k.cadence:{}) as {kind?:string;day?:number;time?:string};
      const body={reportId:text(k.reportId),params:strMap(k.params),format:text(k.format),cadence:text(cad.kind),day:cad.kind==='daily'||cad.day===undefined?undefined:Number(cad.day),time:text(cad.time),
        recipients:(Array.isArray(k.recipients)?k.recipients:[]).map(r=>serverId(r as string,'User')),enabled:k.enabled!==false};
      const res=text(k.id)?await c.api.transport.put<{id:number}>('/reports/schedules/'+serverId(k.id as string,'Schedule'),body)
        :await c.api.transport.post<{id:number}>('/reports/schedules',body);
      return {result:res};}},
    {type:'RPT_SCHEDULE_DELETE',reload:['rptX'],async send(c){await c.api.transport.delete('/reports/schedules/'+serverId(c.cmd.id as string,'Schedule'));}},
    {type:'RPT_LOG',reload:[],async send(c){const k=c.cmd;
      const res=await c.api.transport.post<{id:number}>(`/reports/${reportId(c)}/print-log`,{params:strMap(k.params),rows:Number(k.rows??0),provisional:k.provisional===true,format:text(k.format)||undefined});
      return {result:res.id};}},
  ],
  unmapped:{RPT_RUN_DUE:'Scheduled reports are run by the hotel server on their own schedule; there is no "run now" from this device.'},
};
