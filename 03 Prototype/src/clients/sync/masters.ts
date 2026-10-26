// Masters area (MA-0): configurable lists of the masters framework (domain/streams/masters.ts) on the server module Masters.
//
//   server   GET /masters/floors (every signed-in user: floor names label the boards), GET /masters/floors/history (PERM-MST-001),
//            POST /commands/{SaveFloor,RetireFloor,ReactivateFloor,ReorderFloors,DeleteFloor} (PERM-MST-001; expectedVersion on edits).
//   loaders  mastersX ← rows of every master in MST_SERVER (+ their history when the user may read it). Prototype row id = String(server id).
//   commands MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE {master,…} → the master's server commands. A master that is
//            not in MST_SERVER is refused before any network call ("Not available on the server yet: <list>").
// MA-1 … MA-7: add ONE entry per master to MST_SERVER (route + command names), or map your own `<PFX>_MASTER_*` aliases in your area.
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

/** Server wire shapes (08 Production/src/Hcc.Contracts/Masters/MastersContracts.cs). */
type SrvFloor={floorId:number;code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;roomCount:number;updatedUtc:string;version:string};
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[]};
type SrvHistory={historyId:number;rowId:number;code:string|null;field:string;oldValue:string|null;newValue:string|null;changedBy:number;changedByName:string|null;changedUtc:string};

/** One server-backed master: list + history routes, command names, id field names and the id-directory kind for versions. */
export type MstServerRoute={list:ReadRoute;history?:ReadRoute;listPath:string;historyPath?:string;kind:string;idField:string;idsField:string;
  save:string;retire:string;reactivate:string;reorder:string;remove:string;
  /** MA-1+ optional hooks (defaults = the Floors shapes): server row → master row; extra save-body fields (or the whole body with noBase);
   *  server id of a prototype row id; rows not loaded into mastersX (noList, e.g. rooms = s.rooms); extra reloads; refused operations. */
  fromServer?:(x:Record<string,unknown>)=>MasterRow;body?:(row:Record<string,unknown>,c:CommandCtx)=>Record<string,unknown>;noBase?:boolean;
  serverId?:(c:CommandCtx,protoId:string)=>number;noList?:boolean;reloadAlso?:SliceKey[];
  refuse?:Partial<Record<'retire'|'reactivate'|'reorder'|'remove',string>>;precheckSave?:(row:Record<string,unknown>)=>string|undefined;
  /** Extra Save body fields from the prototype row (beyond code, name, sortOrder, id). */toServer?:(row:Record<string,unknown>)=>Record<string,unknown>;
  /** The rows are loaded by the providing area's own patch loader on mastersX (not by loadMasters). */ownLoader?:boolean;
  /** MA-4/MA-5: prototype row → extra save-body fields (without the id / version). */toBody?:(row:Record<string,unknown>,c:CommandCtx)=>Record<string,unknown>;
  /** Store-backed master whose rows another area loads (MA-4: outlets / tables / menu items live in posX): the loader skips its list. */
  loadedElsewhere?:boolean;
  /** Slices reloaded after a command besides mastersX. */reload?:SliceKey[];
  /** MA-5: server id of a prototype row id; undefined = a row the server does not hold yet (Save creates it). */rowId?:(id:string)=>number|undefined};
export const MST_SERVER:Record<string,MstServerRoute>={
  floors:{list:'GET /masters/floors',history:'GET /masters/floors/history',listPath:'/masters/floors',historyPath:'/masters/floors/history',kind:'floor',idField:'floorId',idsField:'floorIds',
    save:'SaveFloor',retire:'RetireFloor',reactivate:'ReactivateFloor',reorder:'ReorderFloors',remove:'DeleteFloor'},
  ...RMS_SERVER,
  ...MNY_MST_SERVER, // MA-3: paymentMethods, bankAccounts, expenseCategories, expenseVendors
  ...POS_MST_SERVER, // MA-4
  ...FDM_SERVER, // MA-5: bookingSources, idTypes, numberSeries, policies
};
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
    if(r.noList||r.ownLoader)continue;
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
  if(r.serverId)return r.serverId(c,String(id));
  if(r.rowId){const n=r.rowId(String(id));if(n===undefined)throw new HccClientError('VALIDATION','This row is not on the server yet. Refresh and try again.');return n;}
  return serverId(String(id),r.kind[0].toUpperCase()+r.kind.slice(1));
}
/** Body of SaveFloor from the prototype row (only the fields the server knows; no money fields on floors). */
function saveBody(c:CommandCtx,r:MstServerRoute,row:Record<string,unknown>):{body:Record<string,unknown>;expectedVersion?:string}{
  const body:Record<string,unknown>=r.noBase?{}:{code:String(row.code??'').trim().toUpperCase(),name:String(row.name??'').trim()};
  if(!r.noBase&&row.sortOrder!==undefined&&row.sortOrder!==null&&row.sortOrder!=='')body.sortOrder=Number(row.sortOrder);
  if(r.body)Object.assign(body,r.body(row,c));
  if(r.toServer)Object.assign(body,r.toServer(row));
  if(r.toBody)Object.assign(body,r.toBody(row,c));
  if(row.id&&(!r.rowId||r.rowId(String(row.id))!==undefined)){body[r.idField]=idOf(c,r,row.id);return {body,expectedVersion:version(c,r,String(row.id))};}
  return {body};
}
async function status(c:CommandCtx,name:'retire'|'reactivate'|'remove'){
  const d=delegateOf(c.cmd.master);if(d)return d.status(c,name);
  const r=routeOf(c.cmd.master)!,id=String(c.cmd.id);
  await c.api.command(r[name],{[r.idField]:idOf(c,r,id)},{expectedVersion:version(c,r,id)});
  const reload=[...(r.reloadAlso??[]),...(r.reload??[])];
  return reload.length?{reload}:undefined;
}

export const masters:SyncArea={
  id:'masters',
  loaders:[{slice:'mastersX',reads:['GET /masters/floors'],load:loadMasters}],
  commands:[
    {type:'MST_MASTER_SAVE',reload:['mastersX'],precheck(cmd){return precheck(cmd)??(cmd.row&&typeof cmd.row==='object'?undefined:'Fill in the row.');},
      async send(c){const d=delegateOf(c.cmd.master);if(d)return d.save(c);const r=routeOf(c.cmd.master)!;const {body,expectedVersion}=saveBody(c,r,c.cmd.row as Record<string,unknown>);
        const res=await c.api.command<SrvFloor>(r.save,body,{expectedVersion});const reload=[...(r.reloadAlso??[]),...(r.reload??[])];return {result:res,...(reload.length?{reload}:{})};}},
    {type:'MST_MASTER_RETIRE',reload:['mastersX'],precheck,send:c=>status(c,'retire')},
    {type:'MST_MASTER_REACTIVATE',reload:['mastersX'],precheck,send:c=>status(c,'reactivate')},
    {type:'MST_MASTER_DELETE',reload:['mastersX'],precheck,send:c=>status(c,'remove')},
    {type:'MST_MASTER_REORDER',reload:['mastersX'],
      precheck(cmd){const p=precheck(cmd);if(p)return p;return Array.isArray(cmd.ids)&&cmd.ids.length?undefined:'Send the new order.';},
      async send(c){const d=delegateOf(c.cmd.master);if(d)return d.reorder(c);const r=routeOf(c.cmd.master)!;
        await c.api.command(r.reorder,{[r.idsField]:(c.cmd.ids as unknown[]).filter(id=>!r.rowId||r.rowId(String(id))!==undefined).map(id=>idOf(c,r,id))});
        const reload=[...(r.reloadAlso??[]),...(r.reload??[])];return reload.length?{reload}:undefined;}},
  ],
};
