// AF2-HUB-SERVER (audit "Missed A", AUDITFIX-PLAN §2 Q6) · the production Report library runs on the hotel server, not on the device's partial
// synced slices (DEC-108). Pure mappers + loaders; the React hooks are in reportsHubHooks.ts. Production build only (no top-level calls).
//   GET /reports/catalogue                         reports the caller may view (rule V; DEC-061 money guard on the server) + canExport / money
//   GET /reports/{id}?<params>&all=true            every row in one answer (≤ 50,000; masked, money columns removed by rule M / DEC-061)
//   GET /reports/{id}/export?format=…&encoding=base64   the file the server builds (view + export permission); the server writes the export-log row
//   GET /reports/export-log                        the print / export log (Owner / Manager / Accountant: every row; others: their own)
// The server serialises its enums as numbers (Hcc.Application.Reporting.Rpt order); names are accepted too.
import type {SyncApi} from './types';
import type {RptDefinition,RptResult,RptColumn,RptRow,RptParam,RptCell,RptDateClass,RptFormat,RptPhone,RptColType,RptMask,RptExportLogEntry,RptArea,RptPriority,RptStatus,RptParamType} from '../../domain/reports/types';
import {protoId,protoUserId,utcToClock} from './map';

const DATE_CLASS:readonly RptDateClass[]=['D-DAY','D-S','D-B','D-F','D-M','D-FY','D-ASOF','D-NOW','D-FWD'];
const DATE_CLASS_NAMES=['day','short','business','financial','month','financialyear','asof','now','forward'];
const FORMAT=['screen','xlsx','pdf','csv','zip','xml','print'] as const;
const PHONE:readonly RptPhone[]=['yes','summary','no'];
const COL_TYPE:readonly RptColType[]=['text','int','money','date','pct','bool'];
const COL_TYPE_NAMES=['text','int','money','date','percent','bool'];
const MASK:readonly (RptMask|undefined)[]=[undefined,'guestName','idNumber','bankAccount'];
const MASK_NAMES=['none','guestname','idnumber','bankaccount'];
const ROW_KIND:readonly RptRow['kind'][]=['row','subtotal','total'];

/** A server enum (number = declaration order, or its name) → the prototype value; `names` = the C# names lower-cased, in order. */
function en<T>(v:unknown,values:readonly T[],names?:readonly string[]):T|undefined{
  if(v===null||v===undefined)return undefined;
  if(typeof v==='number')return values[v];
  const s=String(v).toLowerCase(),i=(names??values.map(x=>String(x).toLowerCase())).indexOf(s);
  return i>=0?values[i]:(values as readonly unknown[]).includes(v)?v as T:undefined;
}
export const dateClassOf=(v:unknown)=>en(v,DATE_CLASS,DATE_CLASS_NAMES);
export const formatOf=(v:unknown)=>en(v,FORMAT);
export const colTypeOf=(v:unknown)=>en(v,COL_TYPE,COL_TYPE_NAMES)??'text';
export const maskOf=(v:unknown)=>en(v,MASK,MASK_NAMES);
export const rowKindOf=(v:unknown)=>en(v,ROW_KIND)??'row';

export type SrvColumn={key:string;label:string;type:number|string;money:boolean;mask?:number|string|null;computed?:{expr:string;deps:string[]}|null};
export type SrvParam={key:string;label:string;type:string;dateClass?:number|string|null;options?:string[]|null;default?:string|null;required?:boolean};
export type SrvDefinition={id:string;title:string;area:string;priority:string;status:string;moneyFlag:boolean;moneyGate?:string|null;viewPerms:string[];deskPerms?:string[]|null;
  exportPerm?:string|null;dateClass?:number|string|null;params:SrvParam[];columns:SrvColumn[];dateBasis:string;formats:(number|string)[];phone:number|string;documentLayout?:boolean;
  legacyIds?:string[]|null;ownShift?:boolean};
export type SrvCatalogueEntry={definition:SrvDefinition;canExport:boolean;money:boolean};
export type SrvRow={kind:number|string;cells:Record<string,RptCell|boolean>;group?:string|null;drill?:string|null};
export type SrvTable={key:string;title?:string|null;columns:SrvColumn[];rows:SrvRow[]};
export type SrvResult={id:string;title:string;params:{values:Record<string,string>;from?:string|null;to?:string|null;legacy?:boolean};columns:SrvColumn[];rows:SrvRow[];
  about:{runAt:string;user:string;dateBasis:string;provisional:boolean;legacy:boolean;from?:string|null;to?:string|null};removed?:string[]|null;pageIndex?:number;pageSize?:number;totalRows?:number;sections?:SrvTable[]|null};
export type SrvExportLog={id:number;at:string;principalId:number;reportId:string;paramsJson:string;format:number|string;rows:number;fileName?:string|null;provisional:boolean};

/** A hub entry in the production build: the server's definition plus its rights for this caller. `onServer:false` = a prototype-only report
 *  (serverMirror false) shown as "Not on the server yet" and never run on the device. */
export type HubDef=RptDefinition&{canExport?:boolean;seesMoney?:boolean;onServer?:boolean};
export const NOT_ON_SERVER='Not on the server yet';
export const NOT_ON_SERVER_TEXT='This report has not been moved to the hotel server yet, so it cannot be run in this app. It will appear here when the server offers it.';

const runsOnServer=():never=>{throw new Error('This report runs on the hotel server.');};
export function columnFromServer(c:SrvColumn):RptColumn{
  const mask=maskOf(c.mask);
  return {key:c.key,label:c.label,type:colTypeOf(c.type),money:!!c.money,...(mask?{mask}:{}),...(c.computed?{computed:{expr:c.computed.expr,deps:[...(c.computed.deps??[])]}}:{})};
}
export function paramFromServer(p:SrvParam):RptParam{
  const dc=dateClassOf(p.dateClass);
  return {key:p.key,label:p.label,type:p.type as RptParamType,...(dc?{dateClass:dc}:{}),...(p.options?.length?{options:[...p.options]}:{}),
    ...(p.default!=null?{default:p.default}:{}),...(p.required?{required:true}:{})};
}
export function defFromServer(e:SrvCatalogueEntry):HubDef{
  const d=e.definition,dc=dateClassOf(d.dateClass);
  const formats=(d.formats??[]).map(formatOf).filter(f=>!!f&&f!=='print') as RptFormat[];
  return {id:d.id,title:d.title,area:d.area as RptArea,priority:d.priority as RptPriority,status:d.status as RptStatus,moneyFlag:!!d.moneyFlag,
    ...(d.moneyGate?{moneyGate:d.moneyGate}:{}),viewPerms:[...d.viewPerms],deskPerms:[...(d.deskPerms??[])],...(d.exportPerm?{exportPerm:d.exportPerm}:{}),
    ...(dc?{dateClass:dc}:{}),params:(d.params??[]).map(paramFromServer),columns:(d.columns??[]).map(columnFromServer),dateBasis:d.dateBasis,formats,
    phone:en(d.phone,PHONE)??'yes',layout:d.documentLayout?'document':'table',...(d.legacyIds?.length?{legacyIds:[...d.legacyIds]}:{}),...(d.ownShift?{ownShift:true}:{}),
    select:runsOnServer,canExport:!!e.canExport,seesMoney:!!e.money,onServer:true};
}
const cell=(v:unknown):RptCell=>v===undefined?null:typeof v==='boolean'?(v as unknown as RptCell):v as RptCell;
export function rowFromServer(r:SrvRow):RptRow{
  return {kind:rowKindOf(r.kind),cells:Object.fromEntries(Object.entries(r.cells??{}).map(([k,v])=>[k,cell(v)])),...(r.group?{group:r.group}:{}),...(r.drill?{drill:r.drill}:{})};
}
/** GET /reports/{id} → the RptResult the viewer, cards, notes and print model already read (figures exactly as the server sent them). */
export function resultFromServer(r:SrvResult):RptResult{
  const p=r.params??{values:{}};
  const out:RptResult={id:r.id,title:r.title,params:{...(p.values??{}),...(p.from?{from:p.from}:{}),...(p.to?{to:p.to}:{})},
    columns:(r.columns??[]).map(columnFromServer),rows:(r.rows??[]).map(rowFromServer),
    about:{runAt:utcToClock(r.about?.runAt).replace('T',' '),user:r.about?.user??'',dateBasis:r.about?.dateBasis??'',provisional:!!r.about?.provisional,legacy:!!r.about?.legacy,
      ...(r.about?.from?{from:r.about.from}:{}),...(r.about?.to?{to:r.about.to}:{})}};
  if(r.removed?.length)out.removed=[...r.removed];
  if(r.pageSize)out.page={index:r.pageIndex??0,size:r.pageSize,total:r.totalRows??out.rows.length};
  if(r.sections?.length)out.sections=r.sections.map(t=>({key:t.key,...(t.title?{title:t.title}:{}),columns:(t.columns??[]).map(columnFromServer),rows:(t.rows??[]).map(rowFromServer)}));
  return out;
}
const paramsOf=(j:string):Record<string,string>=>{try{const o=JSON.parse(j);return o&&typeof o==='object'?Object.fromEntries(Object.entries(o).map(([k,v])=>[k,String(v??'')])):{};}catch{return {};}};
export function exportLogFromServer(rows:SrvExportLog[]):RptExportLogEntry[]{
  return rows.map(e=>{const f=formatOf(e.format);return {id:protoId(e.id),at:utcToClock(e.at),by:protoUserId(e.principalId),reportId:e.reportId,params:paramsOf(e.paramsJson),
    format:(f==='screen'||!f?'print':f) as RptExportLogEntry['format'],rows:e.rows,...(e.fileName?{fileName:e.fileName}:{}),provisional:!!e.provisional};});
}

/** Raw parameter values as query arguments (blank values are left out, as the server's binder treats them as absent). */
const query=(raw:Record<string,string>)=>Object.fromEntries(Object.entries(raw).filter(([,v])=>v!==undefined&&v!==null&&v!==''));
const path=(id:string)=>'/reports/'+encodeURIComponent(id);

export async function loadHubCatalogue(api:SyncApi):Promise<HubDef[]>{return (await api.get<SrvCatalogueEntry[]>('/reports/catalogue')).map(defFromServer);}
export async function runHubReport(api:SyncApi,id:string,raw:Record<string,string>):Promise<RptResult>{
  return resultFromServer(await api.get<SrvResult>(path(id),{...query(raw),all:true}));
}
export async function loadHubExportLog(api:SyncApi):Promise<RptExportLogEntry[]>{return exportLogFromServer(await api.get<SrvExportLog[]>('/reports/export-log'));}

export const FILE_MIME:Record<'xlsx'|'csv'|'pdf',string>={xlsx:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',csv:'text/csv',pdf:'application/pdf'};
export type HubFile={fileName:string;bytes:Uint8Array;mime:string};
export function base64Bytes(b64:string):Uint8Array{
  const bin=atob(b64.replace(/\s+/g,''));const out=new Uint8Array(bin.length);
  for(let i=0;i<bin.length;i++)out[i]=bin.charCodeAt(i);
  return out;
}
/** The file the server builds (export permission and the export-log row are the server's). `fallbackName` when no Content-Disposition arrived. */
export async function exportHubFile(api:SyncApi,id:string,format:'xlsx'|'csv'|'pdf',raw:Record<string,string>,fallbackName:string):Promise<HubFile>{
  const f=await api.transport.getText(path(id)+'/export',{...query(raw),format,encoding:'base64'});
  return {fileName:f.fileName||fallbackName,bytes:base64Bytes(f.text),mime:FILE_MIME[format]};
}

/** The hub list in production: the server's catalogue, then the prototype-only reports the role could open, marked not on the server. */
export function mergeHubDefs(server:readonly HubDef[],protoOnly:readonly RptDefinition[]):HubDef[]{
  const ids=new Set(server.map(d=>d.id));
  return [...server,...protoOnly.filter(d=>d.serverMirror===false&&!ids.has(d.id)).map(d=>({...d,select:runsOnServer,canExport:false,onServer:false}))];
}
