// W4-2 rooms area (sync/rooms.ts): board + housekeeping tasks loaders, TASK / BLOCK / UNBLOCK / REPAIR_REQUEST / STAYOVER_GENERATE mappers.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const task=(id,over={})=>({taskId:id,roomId:7,roomNo:'105',kind:'checkout',status:'ToDo',assigneeId:12,checklist:[false,false,false],note:null,rejectReason:null,
  businessDate:'2026-10-24',startedUtc:null,completedUtc:null,version:'V'+id,...over});
const row=(roomId,roomNo,over={})=>({roomId,roomNo,roomType:'Deluxe',hk:'Dirty',status:'Dirty',block:null,current:null,next:null,openTasks:[],roomVersion:'RV'+roomId,...over});
const blockRow=row(9,'207',{hk:'Ready',status:'Blocked',block:{blockId:55,from:'2026-10-24',to:'2026-10-27',reason:'AC repair',source:'manual',version:'BV55'}});
const boardRows=[row(7,'105'),blockRow,row(11,'305',{hk:'Inspection',status:'Inspection',roomType:'Suite'})];
const avail={businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-25',nights:1,party:1,types:[],
  rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000},
         {roomId:9,roomNo:'207',roomTypeId:2,roomType:'Deluxe',capacity:3,available:false,reasons:['Blocked'],heldForBlock:null,nightlyRatePaise:200000},
         {roomId:11,roomNo:'305',roomTypeId:3,roomType:'Suite',capacity:4,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:450000}]};

export async function register({test,assert,M}){
  const S=await loadSync();
  const tasks=[task(21),task(22,{roomId:11,roomNo:'305',status:'Inspection',checklist:[true,true,true],startedUtc:'2026-10-24T03:30:00Z',assigneeId:null}),
    task(23,{roomId:9,roomNo:'207',kind:'repair',status:'InProgress',checklist:[],note:'AC repair'})];
  const srv=fakeServer({
    'GET /rooms/board':()=>({businessDate:'2026-10-24',page:1,pageSize:100,total:3,rows:boardRows,counts:{},assignedOnly:false}),
    'GET /housekeeping/tasks':()=>({page:1,pageSize:100,total:3,rows:tasks,assignedOnly:false}),
    'GET /availability':()=>avail,
    'POST /commands/TransitionHousekeepingTask':({body})=>({task:task(body.taskId),roomHk:'Cleaning',detail:body.action}),
    'POST /commands/BlockRoom':({body})=>({blockId:60,roomId:body.roomId,roomNo:'105',from:'2026-10-24',to:null,version:'BV60'}),
    'POST /commands/UnblockRoom':({body})=>({blockId:55,roomId:body.roomId,roomNo:'207'}),
    'POST /commands/RequestRepair':({body})=>({taskId:90,roomId:body.roomId,roomNo:'105'}),
    'POST /commands/GenerateStayoverTasks':()=>({businessDate:'2026-10-24',created:2,alreadyExisted:0,occupiedRooms:2}),
  });
  const rt=await startRuntime(S,srv);const st=rt.get().state;
  const bootPaths=srv.calls.map(c=>c.path);
  test('[W4-2] Loaders fill rooms (prototype shape: floor from room no., rate from /availability, block dates) and tasks (status/kind/assignee mapped)',()=>{
    assert.deepEqual(st.rooms.map(r=>[r.id,r.floor,r.type,r.rate,r.hk,r.blocked]),
      [['105',1,'Deluxe',200000,'Dirty',false],['207',2,'Deluxe',200000,'Ready',true],['305',3,'Suite',450000,'Inspection',false]]);
    const b=st.rooms[1];assert.deepEqual([b.blockReason,b.blockFrom,b.blockTo],['AC repair','2026-10-24','2026-10-27']);
    const proto=M.seed().rooms[0];assert.deepEqual(Object.keys(st.rooms[0]).filter(k=>k in proto).sort(),['blocked','floor','hk','id','rate','type']);
    assert.deepEqual(st.tasks.map(t=>[t.id,t.room,t.assignee,t.status,t.kind,t.checked.length]),
      [['21','105','12','To do','checkout',3],['22','305','','Inspection','checkout',3],['23','207','12','In progress','repair',0]]);
    assert.equal(st.tasks[1].startedAt,'2026-10-24T09:00');
    assert.ok(bootPaths.includes('/availability')&&bootPaths.includes('/rooms/board'));
    assert.equal(rt.ids.server('room','207'),9);assert.equal(rt.ids.version('task','21'),'V21');assert.equal(rt.ids.version('roomBlock','207'),'BV55');
  });

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,calls:srv.calls.map(c=>`${c.method} ${c.path}`),cmds:srv.calls.filter(c=>c.method==='POST')});return seq.at(-1);};
  const a1=await go({type:'TASK',id:'21',action:'start',key:'task:21:1'});
  const a2=await go({type:'TASK',id:'21',action:'assign',assignee:'14'});
  const a3=await go({type:'TASK',id:'21',action:'check',index:1});
  const a4=await go({type:'TASK',id:'22',action:'reject',reason:' Dusty bathroom '});
  const a5=await go({type:'TASK',id:'22',action:'reject',reason:'x'});
  const a6=await go({type:'BLOCK',id:'105',reason:'Leak',from:'2026-10-24',to:'2026-10-26',key:'blk:1'});
  const a7=await go({type:'BLOCK',id:'207'});
  const a8=await go({type:'UNBLOCK',id:'207'});
  const a9=await go({type:'UNBLOCK',id:'105'});
  const a10=await go({type:'REPAIR_REQUEST',room:'105',reason:'Broken tap'});
  const a11=await go({type:'STAYOVER_GENERATE'});
  const a12=await go({type:'BACKUP_SIMULATE'});
  const a13=await go({type:'BLOCK',id:'105',reason:'Leak',from:'2026-10-01'});
  test('[W4-2] TASK maps to TransitionHousekeepingTask with taskId/action and the task version as expectedVersion; reloads tasks + rooms',()=>{
    assert.ok(a1.r.ok);const c=a1.cmds[0];
    assert.equal(c.path,'/commands/TransitionHousekeepingTask');assert.equal(c.key,'task:21:1');
    assert.deepEqual(c.body,{taskId:21,action:'start',expectedVersion:'V21'});
    assert.deepEqual(a1.calls.slice(1).sort(),['GET /availability','GET /housekeeping/tasks','GET /rooms/board']);
    assert.deepEqual(a2.cmds[0].body,{taskId:21,action:'assign',assigneeId:14,expectedVersion:'V21'});
    assert.deepEqual(a3.cmds[0].body,{taskId:21,action:'check',index:1,expectedVersion:'V21'});
    assert.deepEqual(a4.cmds[0].body,{taskId:22,action:'reject',reason:'Dusty bathroom',expectedVersion:'V22'});
    assert.deepEqual([a5.r.ok,a5.r.reason,a5.calls.length],[false,'precheck',0]);
  });
  test('[W4-2] BLOCK/UNBLOCK/REPAIR_REQUEST/STAYOVER_GENERATE map to BlockRoom, UnblockRoom (block version), RequestRepair, GenerateStayoverTasks',()=>{
    assert.deepEqual([a6.cmds[0].path,a6.cmds[0].key,a6.cmds[0].body],['/commands/BlockRoom','blk:1',{roomId:7,reason:'Leak',from:'2026-10-24',to:'2026-10-26'}]);
    assert.deepEqual([a7.cmds[0].path,a7.cmds[0].body],['/commands/UnblockRoom',{roomId:9,expectedVersion:'BV55'}],'BLOCK{id} on a blocked room = prototype unblock toggle');
    assert.deepEqual([a8.cmds[0].path,a8.cmds[0].body],['/commands/UnblockRoom',{roomId:9,expectedVersion:'BV55'}]);
    assert.deepEqual([a9.r.ok,a9.r.reason,a9.calls.length],[false,'precheck',0],'unblocking a room that is not blocked is refused locally');
    assert.deepEqual([a10.cmds[0].path,a10.cmds[0].body],['/commands/RequestRepair',{roomId:7,reason:'Broken tap'}]);
    assert.ok(a10.calls.includes('GET /housekeeping/tasks'));
    assert.deepEqual([a11.cmds[0].path,a11.cmds[0].body,a11.r.result],['/commands/GenerateStayoverTasks',{},{businessDate:'2026-10-24',created:2,alreadyExisted:0,occupiedRooms:2}]);
    assert.deepEqual([a12.r.ok,a12.r.reason],[false,'unmapped']);assert.match(a12.r.text,/BACKUP_SIMULATE/);
    assert.deepEqual([a13.r.ok,a13.r.reason],[false,'precheck']);
  });

  srv.on('POST /commands/TransitionHousekeepingTask',()=>reply(409,{code:'STALE_VERSION',message:'This task changed.'}));
  const stale=await rt.run({type:'TASK',id:'21',action:'start'});
  const hk=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]}),
    'GET /rooms/board':()=>({businessDate:'2026-10-24',page:1,pageSize:100,total:1,rows:[row(7,'105')],counts:{},assignedOnly:true}),
    'GET /housekeeping/tasks':()=>({page:1,pageSize:100,total:1,rows:[task(21)],assignedOnly:true})});
  const hrt=await startRuntime(S,hk);
  const P=await loadPages();const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(hrt.get().state,'1',false)];
  test('[W4-2] Stale task version surfaces as a server error; housekeeper loads assigned data without reading rates; pages render on server data',()=>{
    assert.deepEqual([stale.ok,stale.reason],[false,'server']);assert.match(stale.text,/changed/i);
    assert.equal(hk.calls.filter(c=>c.path==='/availability').length,0);assert.equal(hrt.get().state.rooms[0].rate,0);
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{}]);
  });
  test('[W4-2] Unmapped list documents the commands that stay unavailable',()=>{
    const a=S.AREAS.find(x=>x.id==='rooms');assert.deepEqual(Object.keys(a.unmapped).sort(),['BACKUP_SIMULATE','EDIT_BLOCK']);
    assert.deepEqual(a.commands.map(c=>c.type).sort(),['BLOCK','REPAIR_REQUEST','STAYOVER_GENERATE','TASK','UNBLOCK']);
  });
}
