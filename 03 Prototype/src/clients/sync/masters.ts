// Masters area (MA-0): configurable lists of the masters framework (domain/streams/masters.ts) on the server module Masters.
//
//   server   GET /masters/floors (every signed-in user: floor names label the boards), GET /masters/floors/history (PERM-MST-001),
//            POST /commands/{SaveFloor,RetireFloor,ReactivateFloor,ReorderFloors,DeleteFloor} (PERM-MST-001; expectedVersion on edits).
//            MA-6: the same for designations, shift patterns, leave types and holidays on /hr/{designations,shift-patterns,leave-types,holidays} (any signed-in user reads;
//            holidays and the pay fields need PERM-MST-002).
//   loaders  mastersX ← rows of every master in MST_SERVER (+ their history when the user may read it). Prototype row id = String(server id).
//   commands MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE {master,…} → the master's server commands. A master that is
//            not in MST_SERVER is refused before any network call ("Not available on the server yet: <list>").
// MA-1 … MA-7: ONE entry per master in MST_SERVER (route + command names + the optional hooks of MstServerRoute), or a delegate in
// MST_DELEGATES when another area sends the commands; areas spread their tables here (RMS_SERVER, MNY_MST_SERVER, POS_MST_SERVER,
// FDM_SERVER, OPS_MST_SERVER; the MA-6 people lists are inline below).
import type {MasterRow,MSTHistory,MSTState} from '../../domain/streams/masters';
import {HccClientError} from '../errors';
import type {Command} from '../../services/mock';
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey,CommandOutcome} from './types';
import {POS_MST_SERVER} from './pos-masters'; // MA-4 POS masters (outlets, tables, menu categories, menu items)
import type {ReadRoute} from './reads';
import {serverId,protoId,protoUserId,utcToClock} from './map';
import {RMS_SERVER} from './roommasters'; // MA-1 room types, amenities, occupancy charges, rooms
import {MNY_MST_SERVER} from './money-master'; // MA-3 money masters (rows loaded by its own patch loader)
import {CAT_MST_DELEGATES} from './catalogue'; // MA-2 meal plans, extras, discount reasons (sync/catalogue.ts)
import {FDM_SERVER} from './frontdesk-master'; // MA-5 front-desk lists
import {OPS_MST_SERVER} from './ops-masters'; // MA-7: task types, zones, complaint and lost-and-found categories
import {MST_IMPORT_COMMANDS} from './masters-import'; // MA-8: MST_IMPORT (masters JSON import, Paste rows) → the server import

/** Server wire shapes (08 Production/src/Hcc.Contracts/Masters/MastersContracts.cs). */
type SrvFloor={floorId:number;code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;roomCount:number;updatedUtc:string;version:string};
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[]};
type SrvHistory={historyId:number;rowId:number;code:string|null;field:string;oldValue:string|null;newValue:string|null;changedBy:number;changedByName:string|null;changedUtc:string};

/** One server-backed master: list (+ optional history) routes, command names, id field names and the id-directory kind for versions.
 *  Masters integration (MA-1…MA-7): ONE optional hook set; a master without hooks has the Floors shape (code, name, sortOrder, active).
 *    fromServer     server list row → prototype MasterRow (default: the floor mapper)                         [MA-1, MA-4, MA-5, MA-6, MA-7]
 *    toBody         prototype row → extra Save body fields, beyond code / name / sortOrder / id (c for lookups) [MA-1, MA-3, MA-4, MA-5, MA-6, MA-7]
 *    noBase         toBody is the whole body (no code / name / sortOrder; e.g. rooms send roomNo)              [MA-1]
 *    rowId          server id of a prototype row id; undefined = not on the server yet (Save creates it,
 *                   Reorder skips it, Retire / Reactivate / Delete refuse). Default: serverId (positive ids)   [MA-1, MA-5]
 *    ownLoader      rows AND history come from the owning area's loader (or are another slice): loadMasters skips the master  [MA-1 rooms, MA-3]
 *    loadedElsewhere rows come from another area's slice / loader, history is still read here                  [MA-4]
 *    reload         slices reloaded after a command besides mastersX                                          [MA-1, MA-4]
 *    refuse         operations refused before any network call, with the reason                               [MA-1]
 *    precheckSave   Save refused before any network call when it returns a reason                             [MA-1]
 *  Masters whose commands another area sends entirely register in MST_DELEGATES instead (MA-2 catalogue). */
export type MstServerRoute={list:ReadRoute;history?:ReadRoute;listPath:string;historyPath?:string;kind:string;idField:string;idsField:string;
  save:string;retire:string;reactivate:string;reorder:string;remove:string;
  fromServer?:(x:Record<string,unknown>)=>MasterRow;
  toBody?:(row:Record<string,unknown>,c:CommandCtx)=>Record<string,unknown>;noBase?:boolean;
  rowId?:(id:string,c:CommandCtx)=>number|undefined;
  ownLoader?:boolean;loadedElsewhere?:boolean;reload?:SliceKey[];
  refuse?:Partial<Record<'retire'|'reactivate'|'reorder'|'remove',string>>;precheckSave?:(row:Record<string,unknown>)=>string|undefined};
export const MST_SERVER:Record<string,MstServerRoute>={
  floors:{list:'GET /masters/floors',history:'GET /masters/floors/history',listPath:'/masters/floors',historyPath:'/masters/floors/history',kind:'floor',idField:'floorId',idsField:'floorIds',
    save:'SaveFloor',retire:'RetireFloor',reactivate:'ReactivateFloor',reorder:'ReorderFloors',remove:'DeleteFloor'},
  ...RMS_SERVER,
  ...MNY_MST_SERVER, // MA-3: paymentMethods, bankAccounts, expenseCategories, expenseVendors
  ...POS_MST_SERVER, // MA-4
  ...FDM_SERVER, // MA-5: bookingSources, idTypes, numberSeries, policies
  // MA-6 people lists (server module PeopleLists: hr.designation, hr.shift_pattern, hr.leave_type, hr.holiday).
  designations:{list:'GET /hr/designations',history:'GET /hr/designations/history',listPath:'/hr/designations',historyPath:'/hr/designations/history',kind:'designation',idField:'designationId',idsField:'designationIds',
    save:'SaveDesignation',retire:'RetireDesignation',reactivate:'ReactivateDesignation',reorder:'ReorderDesignations',remove:'DeleteDesignation',
    fromServer:r=>({...listBase(r,'designationId'),...optional(r,['dept','grade','defaultPayRule'])}),
    // The pay rule is hidden from roles without PERM-MST-002 / PERM-HR-005: absent = unchanged on the server, "" = cleared.
    toBody:r=>({dept:r.dept??null,grade:r.grade??null,...(r.defaultPayRule!==undefined?{defaultPayRule:r.defaultPayRule}:{})})},
  shiftPatterns:{list:'GET /hr/shift-patterns',history:'GET /hr/shift-patterns/history',listPath:'/hr/shift-patterns',historyPath:'/hr/shift-patterns/history',kind:'shiftPattern',idField:'shiftPatternId',idsField:'shiftPatternIds',
    save:'SaveShiftPattern',retire:'RetireShiftPattern',reactivate:'ReactivateShiftPattern',reorder:'ReorderShiftPatterns',remove:'DeleteShiftPattern',
    fromServer:r=>({...listBase(r,'shiftPatternId'),start:r.start,end:r.end,breakMinutes:r.breakMinutes,night:!!r.night,weeklyOffRule:r.weeklyOffRule}),
    toBody:r=>({start:r.start,end:r.end,breakMinutes:r.breakMinutes??0,night:!!r.night,weeklyOffRule:r.weeklyOffRule??'fixed'})},
  leaveTypes:{list:'GET /hr/leave-types',history:'GET /hr/leave-types/history',listPath:'/hr/leave-types',historyPath:'/hr/leave-types/history',kind:'leaveType',idField:'leaveTypeId',idsField:'leaveTypeIds',
    save:'SaveLeaveType',retire:'RetireLeaveType',reactivate:'ReactivateLeaveType',reorder:'ReorderLeaveTypes',remove:'DeleteLeaveType',
    // SCC leave port (DEC-116): SCC's fixed categories — paid flag and a (decimal) quota only; no carry-forward or encashment.
    fromServer:r=>({...listBase(r,'leaveTypeId'),paid:!!r.paid,...optional(r,['yearlyQuota'])}),
    toBody:r=>({paid:r.paid!==false,yearlyQuota:r.yearlyQuota??null})},
  holidays:{list:'GET /hr/holidays',history:'GET /hr/holidays/history',listPath:'/hr/holidays',historyPath:'/hr/holidays/history',kind:'holiday',idField:'holidayId',idsField:'holidayIds',
    save:'SaveHoliday',retire:'RetireHoliday',reactivate:'ReactivateHoliday',reorder:'ReorderHolidays',remove:'DeleteHoliday',
    fromServer:r=>({...listBase(r,'holidayId'),kind:r.kind}),
    toBody:r=>({kind:r.kind})},
  ...OPS_MST_SERVER, // MA-7
};
/** Framework columns of any server list row → prototype MasterRow base (id = String(server id)). */
const listBase=(r:Record<string,unknown>,idField:string):MasterRow=>({id:protoId(r[idField] as number),code:String(r.code),name:String(r.name),sortOrder:Number(r.sortOrder),active:!!r.active,
  ...(r.retiredOn?{retiredOn:String(r.retiredOn)}:{})});
/** Only the keys the server sent with a value (null = hidden or empty). */
const optional=(r:Record<string,unknown>,keys:string[])=>Object.fromEntries(keys.filter(k=>r[k]!==null&&r[k]!==undefined).map(k=>[k,r[k]]));
const PAGE=200;
/** MA-2 …: a master whose rows another area loads (a kind:'patch' loader on mastersX) and whose server commands that area sends —
 *  for masters with fields beyond code / name / sort order (dated money, selects). The MST_MASTER_* mappers below hand them over. */
export type MstDelegate={save(c:CommandCtx):Promise<CommandOutcome|void>;status(c:CommandCtx,op:'retire'|'reactivate'|'remove'):Promise<void>;reorder(c:CommandCtx):Promise<void>};
export const MST_DELEGATES:Record<string,MstDelegate>={...CAT_MST_DELEGATES};

export function floorFromServer(f:SrvFloor):MasterRow{
  const r:MasterRow={id:protoId(f.floorId),code:f.code,name:f.name,sortOrder:f.sortOrder,active:f.active};
  if(f.retiredOn)r.retiredOn=f.retiredOn;
  return r;
}
export function historyFromServer(master:string,h:SrvHistory):MSTHistory{
  return {id:protoId(h.historyId),master,rowId:protoId(h.rowId),code:h.code??'',field:h.field,old:h.oldValue??'—',new:h.newValue??'—',by:protoUserId(h.changedBy),at:utcToClock(h.changedUtc)};
}
async function pages<T>(c:LoaderCtx,path:string):Promise<T[]>{
  let rows:T[]=[];
  for(let page=1;page<=50;page++){const p=await c.api.get<SrvPage<T>>(path,{page,pageSize:PAGE});rows=rows.concat(p.items);if(!p.items.length||rows.length>=p.total)break;}
  return rows;
}
async function loadMasters(c:LoaderCtx):Promise<MSTState>{
  const out:MSTState={rows:{},history:[]};
  for(const [master,r] of Object.entries(MST_SERVER)){
    if(r.ownLoader)continue;
    if(!r.loadedElsewhere){
      if(!c.mayRead(r.list))continue;
      const rows=await pages<Record<string,unknown>>(c,r.listPath);
      for(const f of rows)c.ids.setVersion(r.kind,protoId(f[r.idField] as number),String(f.version??''));
      out.rows[master]=rows.map(x=>r.fromServer?r.fromServer(x):floorFromServer(x as unknown as SrvFloor));
    }
    if(r.history&&r.historyPath&&c.mayRead(r.history))out.history.push(...(await pages<SrvHistory>(c,r.historyPath)).map(h=>historyFromServer(master,h)));
  }
  out.history.sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:Number(b.id)-Number(a.id));
  return out;
}

const routeOf=(master:unknown)=>MST_SERVER[String(master)];
const delegateOf=(master:unknown)=>Object.prototype.hasOwnProperty.call(MST_DELEGATES,String(master))?MST_DELEGATES[String(master)]:undefined;
function precheck(cmd:Command):string|undefined{
  if(delegateOf(cmd.master))return undefined;
  const r=routeOf(cmd.master);if(!r)return `Not available on the server yet: the ${String(cmd.master??'unknown')} list.`;
  const op=({MST_MASTER_RETIRE:'retire',MST_MASTER_REACTIVATE:'reactivate',MST_MASTER_REORDER:'reorder',MST_MASTER_DELETE:'remove'} as const)[cmd.type as 'MST_MASTER_RETIRE'];
  return op?r.refuse?.[op]:cmd.type==='MST_MASTER_SAVE'&&cmd.row&&typeof cmd.row==='object'?r.precheckSave?.(cmd.row as Record<string,unknown>):undefined;
}
const version=(c:CommandCtx,r:MstServerRoute,id:string)=>c.ids.version(r.kind,id);
function idOf(c:CommandCtx,r:MstServerRoute,id:unknown){
  if(id===undefined||id===null||id==='')throw new HccClientError('VALIDATION','Choose a row.');
  if(r.rowId){const n=r.rowId(String(id),c);if(n===undefined)throw new HccClientError('VALIDATION','This row is not on the server yet. Refresh and try again.');return n;}
  return serverId(String(id),r.kind[0].toUpperCase()+r.kind.slice(1));
}
/** Save body from the prototype row: code / name / sortOrder (unless noBase) + the master's toBody fields + id and version on edits. */
function saveBody(c:CommandCtx,r:MstServerRoute,row:Record<string,unknown>):{body:Record<string,unknown>;expectedVersion?:string}{
  const body:Record<string,unknown>=r.noBase?{}:{code:String(row.code??'').trim().toUpperCase(),name:String(row.name??'').trim()};
  if(!r.noBase&&row.sortOrder!==undefined&&row.sortOrder!==null&&row.sortOrder!=='')body.sortOrder=Number(row.sortOrder);
  if(r.toBody)Object.assign(body,r.toBody(row,c));
  if(row.id&&(!r.rowId||r.rowId(String(row.id),c)!==undefined)){body[r.idField]=idOf(c,r,row.id);return {body,expectedVersion:version(c,r,String(row.id))};}
  return {body};
}
async function status(c:CommandCtx,name:'retire'|'reactivate'|'remove'){
  const d=delegateOf(c.cmd.master);if(d)return d.status(c,name);
  const r=routeOf(c.cmd.master)!,id=String(c.cmd.id);
  await c.api.command(r[name],{[r.idField]:idOf(c,r,id)},{expectedVersion:version(c,r,id)});
  return r.reload?.length?{reload:r.reload}:undefined;
}

export const masters:SyncArea={
  id:'masters',
  loaders:[{slice:'mastersX',reads:['GET /masters/floors','GET /housekeeping/task-types','GET /housekeeping/zones','GET /guest-services/complaint-categories','GET /guest-services/lost-found-categories'],load:loadMasters}],
  commands:[
    {type:'MST_MASTER_SAVE',reload:['mastersX'],precheck(cmd){return precheck(cmd)??(cmd.row&&typeof cmd.row==='object'?undefined:'Fill in the row.');},
      async send(c){const d=delegateOf(c.cmd.master);if(d)return d.save(c);const r=routeOf(c.cmd.master)!;const {body,expectedVersion}=saveBody(c,r,c.cmd.row as Record<string,unknown>);
        const res=await c.api.command<SrvFloor>(r.save,body,{expectedVersion});return {result:res,...(r.reload?.length?{reload:r.reload}:{})};}},
    {type:'MST_MASTER_RETIRE',reload:['mastersX'],precheck,send:c=>status(c,'retire')},
    {type:'MST_MASTER_REACTIVATE',reload:['mastersX'],precheck,send:c=>status(c,'reactivate')},
    {type:'MST_MASTER_DELETE',reload:['mastersX'],precheck,send:c=>status(c,'remove')},
    {type:'MST_MASTER_REORDER',reload:['mastersX'],
      precheck(cmd){const p=precheck(cmd);if(p)return p;return Array.isArray(cmd.ids)&&cmd.ids.length?undefined:'Send the new order.';},
      async send(c){const d=delegateOf(c.cmd.master);if(d)return d.reorder(c);const r=routeOf(c.cmd.master)!;
        await c.api.command(r.reorder,{[r.idsField]:(c.cmd.ids as unknown[]).filter(id=>!r.rowId||r.rowId(String(id),c)!==undefined).map(id=>idOf(c,r,id))});
        return r.reload?.length?{reload:r.reload}:undefined;}},
    ...MST_IMPORT_COMMANDS, // MA-8
  ],
};
