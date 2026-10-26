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
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey} from './types';
import {POS_MST_SERVER} from './pos-masters'; // MA-4 POS masters (outlets, tables, menu categories, menu items)
import type {ReadRoute} from './reads';
import {serverId,protoId,protoUserId,utcToClock} from './map';

/** Server wire shapes (08 Production/src/Hcc.Contracts/Masters/MastersContracts.cs). */
type SrvFloor={floorId:number;code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;roomCount:number;updatedUtc:string;version:string};
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[]};
type SrvHistory={historyId:number;rowId:number;code:string|null;field:string;oldValue:string|null;newValue:string|null;changedBy:number;changedByName:string|null;changedUtc:string};

/** One server-backed master: list + history routes, command names, id field names and the id-directory kind for versions. */
export type MstServerRoute={list:ReadRoute;history?:ReadRoute;listPath:string;historyPath?:string;kind:string;idField:string;idsField:string;
  save:string;retire:string;reactivate:string;reorder:string;remove:string;
  /** MA-1…MA-7 hooks (defaults = floors): server row → MasterRow; prototype row → save body (without the id / version). */
  fromServer?:(x:Record<string,unknown>)=>MasterRow;toBody?:(c:CommandCtx,row:Record<string,unknown>)=>Record<string,unknown>;
  /** Store-backed master whose rows another area loads (MA-4: outlets / tables / menu items live in posX): the loader skips its list. */
  loadedElsewhere?:boolean;
  /** Slices reloaded after a command besides mastersX. */reload?:SliceKey[]};
export const MST_SERVER:Record<string,MstServerRoute>={
  floors:{list:'GET /masters/floors',history:'GET /masters/floors/history',listPath:'/masters/floors',historyPath:'/masters/floors/history',kind:'floor',idField:'floorId',idsField:'floorIds',
    save:'SaveFloor',retire:'RetireFloor',reactivate:'ReactivateFloor',reorder:'ReorderFloors',remove:'DeleteFloor'},
  ...POS_MST_SERVER, // MA-4
};
const PAGE=200;

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
    if(!r.loadedElsewhere){
      if(!c.mayRead(r.list))continue;
      const rows=await pages<Record<string,unknown>>(c,r.listPath);
      for(const f of rows)c.ids.setVersion(r.kind,protoId(f[r.idField] as number),String(f.version));
      out.rows[master]=rows.map(x=>r.fromServer?r.fromServer(x):floorFromServer(x as unknown as SrvFloor));
    }
    if(r.history&&r.historyPath&&c.mayRead(r.history))out.history.push(...(await pages<SrvHistory>(c,r.historyPath)).map(h=>historyFromServer(master,h)));
  }
  out.history.sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:Number(b.id)-Number(a.id));
  return out;
}

const routeOf=(master:unknown)=>MST_SERVER[String(master)];
function precheck(cmd:Command):string|undefined{
  return routeOf(cmd.master)?undefined:`Not available on the server yet: the ${String(cmd.master??'unknown')} list.`;
}
const version=(c:CommandCtx,r:MstServerRoute,id:string)=>c.ids.version(r.kind,id);
function idOf(c:CommandCtx,r:MstServerRoute,id:unknown){
  if(id===undefined||id===null||id==='')throw new HccClientError('VALIDATION','Choose a row.');
  return serverId(String(id),r.kind[0].toUpperCase()+r.kind.slice(1));
}
/** Body of SaveFloor from the prototype row (only the fields the server knows; no money fields on floors). */
function saveBody(c:CommandCtx,r:MstServerRoute,row:Record<string,unknown>):{body:Record<string,unknown>;expectedVersion?:string}{
  const body:Record<string,unknown>={code:String(row.code??'').trim().toUpperCase(),name:String(row.name??'').trim()};
  if(row.sortOrder!==undefined&&row.sortOrder!==null&&row.sortOrder!=='')body.sortOrder=Number(row.sortOrder);
  if(r.toBody){Object.assign(body,r.toBody(c,row));}
  if(row.id){body[r.idField]=idOf(c,r,row.id);return {body,expectedVersion:version(c,r,String(row.id))};}
  return {body};
}
async function status(c:CommandCtx,name:'retire'|'reactivate'|'remove'){
  const r=routeOf(c.cmd.master)!,id=String(c.cmd.id);
  await c.api.command(r[name],{[r.idField]:idOf(c,r,id)},{expectedVersion:version(c,r,id)});
  return r.reload?{reload:r.reload}:undefined;
}

export const masters:SyncArea={
  id:'masters',
  loaders:[{slice:'mastersX',reads:['GET /masters/floors'],load:loadMasters}],
  commands:[
    {type:'MST_MASTER_SAVE',reload:['mastersX'],precheck(cmd){return precheck(cmd)??(cmd.row&&typeof cmd.row==='object'?undefined:'Fill in the row.');},
      async send(c){const r=routeOf(c.cmd.master)!;const {body,expectedVersion}=saveBody(c,r,c.cmd.row as Record<string,unknown>);
        const res=await c.api.command<SrvFloor>(r.save,body,{expectedVersion});return {result:res,...(r.reload?{reload:r.reload}:{})};}},
    {type:'MST_MASTER_RETIRE',reload:['mastersX'],precheck,send:c=>status(c,'retire')},
    {type:'MST_MASTER_REACTIVATE',reload:['mastersX'],precheck,send:c=>status(c,'reactivate')},
    {type:'MST_MASTER_DELETE',reload:['mastersX'],precheck,send:c=>status(c,'remove')},
    {type:'MST_MASTER_REORDER',reload:['mastersX'],
      precheck(cmd){const p=precheck(cmd);if(p)return p;return Array.isArray(cmd.ids)&&cmd.ids.length?undefined:'Send the new order.';},
      async send(c){const r=routeOf(c.cmd.master)!;await c.api.command(r.reorder,{[r.idsField]:(c.cmd.ids as unknown[]).map(id=>idOf(c,r,id))});return r.reload?{reload:r.reload}:undefined;}},
  ],
};
