// Rooms area (W4-2): room status board, housekeeping tasks, stay-over generation, out-of-order blocks (the Inventory side is
// written by the same server command) and repair requests.
//
//   server   GET /rooms/board (paged <=100, assigned-scope filtered), GET /housekeeping/tasks (open tasks, paged),
//            GET /availability via the typed reservations client (rates only), POST /commands/{TransitionHousekeepingTask,BlockRoom,UnblockRoom,RequestRepair,
//            GenerateStayoverTasks}.
//   loaders  rooms <- board rows (+ nightly rates from /availability for the business date when the user may see reservations;
//            forbidden -> rate 0). tasks <- open tasks. The server has no floor column: floor = hundreds digit of the room number.
//   commands TASK -> TransitionHousekeepingTask (expectedVersion = task version from the load; the prototype command carries none),
//            BLOCK -> BlockRoom (or UnblockRoom for the prototype's "BLOCK{id} on a blocked room" toggle), UNBLOCK -> UnblockRoom
//            (expectedVersion = the active block's version), REPAIR_REQUEST -> RequestRepair, STAYOVER_GENERATE -> GenerateStayoverTasks.
import type {Room,Task} from '../../services/mock';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx} from './types';
import {serverId,protoUserId,utcToClock,isoDate} from './map';

type SrvBlock={blockId:number;from:string|null;to:string|null;reason:string;source:string;version:string};
type SrvTask={taskId:number;roomId:number;roomNo:string;kind:string;status:string;assigneeId:number|null;checklist:boolean[];note:string|null;rejectReason:string|null;
  businessDate:string;startedUtc:string|null;completedUtc:string|null;version:string};
type SrvRow={roomId:number;roomNo:string;roomType:string;hk:string;status:string;block:SrvBlock|null;openTasks:SrvTask[];roomVersion:string};
type SrvBoard={businessDate:string;page:number;pageSize:number;total:number;rows:SrvRow[]};
type SrvTaskPage={page:number;pageSize:number;total:number;rows:SrvTask[]};

const PAGE=100;
const TASK_STATUS:Record<string,Task['status']>={ToDo:'To do',InProgress:'In progress',Inspection:'Inspection',Done:'Done'};
const HK=['Ready','Dirty','Cleaning','Inspection'];

/** Floor from the room number (101 -> 1, 305 -> 3); the server has no floor column. */
export function floorOf(roomNo:string):number{const n=/^\d{3,}$/.test(roomNo)?Number(roomNo):NaN;return Number.isFinite(n)?Math.max(1,Math.floor(n/100)):1;}
const addDay=(d:string)=>new Date(Date.parse(d+'T00:00:00Z')+86_400_000).toISOString().slice(0,10);

export function taskFromServer(t:SrvTask):Task{
  return {id:String(t.taskId),room:t.roomNo,assignee:t.assigneeId?protoUserId(t.assigneeId):'',status:TASK_STATUS[t.status]??'To do',checked:[...(t.checklist??[])],
    kind:t.kind as Task['kind'],date:t.businessDate,note:t.note??undefined,rejectReason:t.rejectReason??undefined,
    startedAt:t.startedUtc?utcToClock(t.startedUtc):undefined,completedAt:t.completedUtc?utcToClock(t.completedUtc):undefined} as Task;
}
export function roomFromServer(r:SrvRow,rate:number):Room{
  const b=r.block;
  return {id:r.roomNo,floor:floorOf(r.roomNo),type:r.roomType,rate,hk:(HK.includes(r.hk)?r.hk:'Dirty') as Room['hk'],blocked:!!b,
    blockReason:b?.reason,blockFrom:b?.from??undefined,blockTo:b?.to??undefined} as Room;
}

async function allPages<T>(c:LoaderCtx,path:string):Promise<{first:{businessDate?:string};rows:T[]}>{
  let first:{businessDate?:string}={},rows:T[]=[];
  for(let page=1;page<=50;page++){
    const p=await c.api.get<{rows:T[];total:number;businessDate?:string}>(path,{page,pageSize:PAGE});
    if(page===1)first=p;
    rows=rows.concat(p.rows);if(!p.rows.length||rows.length>=p.total)break;
  }
  return {first,rows};
}

async function rates(c:LoaderCtx,businessDate:string|undefined):Promise<Map<string,number>>{
  const out=new Map<string,number>();
  if(!businessDate||!c.can('reservations'))return out;
  try{
    const a=await c.api.clients.reservations.getAvailability({arrival:isoDate(businessDate),departure:addDay(businessDate),adults:1});
    for(const r of a.rooms)out.set(r.roomId,r.nightlyRatePaise);
  }catch(e){if(!(e instanceof HccClientError)||e.wireCode==='UNAVAILABLE'||e.wireCode==='UNAUTHENTICATED')throw e;}
  return out;
}

async function loadRooms(c:LoaderCtx):Promise<Room[]>{
  const {first,rows}=await allPages<SrvRow>(c,'/rooms/board');
  const rate=await rates(c,first.businessDate);
  const out=rows.map(r=>{
    c.ids.learn('room',r.roomNo,r.roomId);
    c.ids.setVersion('room',r.roomNo,r.roomVersion);
    if(r.block)c.ids.setVersion('roomBlock',r.roomNo,r.block.version);
    return roomFromServer(r,rate.get(r.roomNo)??0);
  });
  return out.sort((a,b)=>a.id<b.id?-1:a.id>b.id?1:0);
}
async function loadTasks(c:LoaderCtx):Promise<Task[]>{
  const {rows}=await allPages<SrvTask>(c,'/housekeeping/tasks');
  for(const t of rows)c.ids.setVersion('task',String(t.taskId),t.version);
  return rows.map(taskFromServer).sort((a,b)=>Number(a.id)-Number(b.id));
}

const roomIdOf=(c:{ids:LoaderCtx['ids']},room:unknown)=>{
  const id=c.ids.server('room',String(room));
  if(id===undefined)throw new HccClientError('VALIDATION',`Room ${room} is not on the server board. Refresh and try again.`);
  return id;
};
const ISO=/^\d{4}-\d{2}-\d{2}$/;

export const rooms:SyncArea={
  id:'rooms',
  loaders:[
    {slice:'rooms',requires:'housekeeping',load:loadRooms},
    {slice:'tasks',requires:'housekeeping',load:loadTasks},
  ],
  commands:[
    {type:'TASK',reload:['tasks','rooms'],
      precheck(cmd){
        if(!['assign','start','check','complete','inspect','reject'].includes(String(cmd.action)))return 'Choose a task action.';
        if(cmd.action==='assign'&&!cmd.assignee)return 'Select a user to assign.';
        if(cmd.action==='reject'&&String(cmd.reason??'').trim().length<3)return 'Enter a reason for returning the room for cleaning.';
        return undefined;
      },
      async send(c){
        const cmd=c.cmd,id=String(cmd.id);
        const body:Record<string,unknown>={taskId:serverId(id,'Task'),action:cmd.action};
        if(cmd.action==='assign')body.assigneeId=serverId(cmd.assignee,'User');
        if(cmd.action==='check')body.index=cmd.index;
        if(cmd.action==='reject')body.reason=String(cmd.reason).trim();
        const r=await c.api.command<{detail?:string}>('TransitionHousekeepingTask',body,{expectedVersion:c.ids.version('task',id)});
        return {result:r};
      }},
    {type:'BLOCK',reload:['rooms','tasks'],
      precheck(cmd,s){
        const reason=String(cmd.reason??'').trim();
        if(!reason&&!cmd.from&&!cmd.to)return s.rooms.find(r=>r.id===cmd.id)?.blocked?undefined:'Enter a reason for the block.';
        if(reason.length<3)return 'Enter a reason for the block.';
        if((cmd.from&&!ISO.test(cmd.from))||(cmd.to&&!ISO.test(cmd.to)))return 'Enter valid block dates.';
        if(cmd.from&&cmd.from<s.date)return 'A block cannot start before the current business date.';
        if(cmd.to&&cmd.to<=(cmd.from||s.date))return 'The block must end after it starts.';
        return undefined;
      },
      async send(c){
        const cmd=c.cmd,rid=roomIdOf(c,cmd.id),reason=String(cmd.reason??'').trim();
        if(!reason&&!cmd.from&&!cmd.to){ // prototype toggle: BLOCK{id} on a blocked room unblocks
          await c.api.command('UnblockRoom',{roomId:rid},{expectedVersion:c.ids.version('roomBlock',String(cmd.id))});return;
        }
        const body:Record<string,unknown>={roomId:rid,reason};
        if(cmd.from)body.from=cmd.from;
        if(cmd.to)body.to=cmd.to;
        await c.api.command('BlockRoom',body);
      }},
    {type:'UNBLOCK',reload:['rooms','tasks'],
      precheck:(cmd,s)=>s.rooms.find(r=>r.id===cmd.id)?.blocked?undefined:'This room is not blocked.',
      async send(c){
        await c.api.command('UnblockRoom',{roomId:roomIdOf(c,c.cmd.id)},{expectedVersion:c.ids.version('roomBlock',String(c.cmd.id))});
      }},
    {type:'REPAIR_REQUEST',reload:['tasks','rooms'],
      precheck:cmd=>String(cmd.reason??'').trim().length>=3?undefined:'Describe the repair needed.',
      async send(c){await c.api.command('RequestRepair',{roomId:roomIdOf(c,c.cmd.room),reason:String(c.cmd.reason).trim()});}},
    {type:'STAYOVER_GENERATE',reload:['tasks'],
      async send(c){const r=await c.api.command<{created:number;alreadyExisted:number}>('GenerateStayoverTasks',{});return {result:r};}},
  ],
  unmapped:{
    BACKUP_SIMULATE:'demonstration only (audit-only simulated backup); real backups are run by the server deployment, not from the app',
    EDIT_BLOCK:'no EditRoomBlock command on the server (IRoomBlockEditPort is internal to group-block changes); the prototype has no edit-block screen',
  },
};
