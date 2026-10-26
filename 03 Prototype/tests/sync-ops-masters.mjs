// MA-7 · Operations masters of the production sync layer (src/clients/sync/ops-masters.ts + masters.ts) against a fake server shaped like
// the .NET DTOs (08 Production/src/Hcc.Contracts/OpsMasters/OpsMastersContracts.cs). Names prefixed [OPSM-SYNC].
import {readFileSync} from 'node:fs';
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters'].includes(a.id))};
  const src=f=>readFileSync(f,'utf8');
  const row=(id,code,name,sortOrder,o={})=>({id,code,name,sortOrder,active:true,retiredOn:null,used:0,updatedUtc:'2026-10-24T03:00:00.000Z',version:'V'+id,...o});
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const types=[row(1,'DEP','Departure clean',10,{appliesTo:['checkout','move'],standardMinutes:40,needsInspection:true,checklist:['Linen & bathroom','Surfaces & amenities','Final room check'],used:3}),
    row(2,'STAY','Stayover service',20,{appliesTo:['stayover'],standardMinutes:20,needsInspection:false,checklist:['Towels','Bin']}),
    row(3,'DEEP','Deep clean',30,{active:false,retiredOn:'2026-10-20',appliesTo:['deep'],standardMinutes:90,needsInspection:true,checklist:['All']})];
  const zones=[row(1,'Z1','Ground section',10,{floors:['F1'],rooms:['201'],defaultAttendantId:6}),row(2,'Z2','Second',20,{floors:['F2'],rooms:[],defaultAttendantId:null})];
  const complaints=[row(1,'NOISE','Noise',10,{department:'Front desk',slaHours:2,used:1}),row(2,'PARK','Parking',20,{department:'Front desk',slaHours:6,active:false,retiredOn:'2026-10-21'})];
  const found=[row(1,'JEWEL','Jewellery',10,{department:'Front desk',slaHours:4,valuable:true}),row(2,'CLOTH','Clothing',20,{department:'Housekeeping',slaHours:24,valuable:false})];
  const hist=(id,rowId,code,field,o,n)=>({historyId:id,rowId,code,field,oldValue:o,newValue:n,changedBy:4,changedByName:'techadmin',changedUtc:'2026-10-24T03:30:00.000Z'});
  const routes={'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([]),
    'GET /housekeeping/task-types':()=>page(types),'GET /housekeeping/task-types/history':()=>page([hist(9,1,'DEP','standardMinutes','35','40')]),
    'GET /housekeeping/zones':()=>page(zones),'GET /housekeeping/zones/history':()=>page([]),
    'GET /guest-services/complaint-categories':()=>page(complaints),'GET /guest-services/complaint-categories/history':()=>page([]),
    'GET /guest-services/lost-found-categories':()=>page(found),'GET /guest-services/lost-found-categories/history':()=>page([])};
  const ops=['HkTaskType','HkZone','ComplaintCategory','LostFoundCategory'];
  for(const n of ops)for(const v of ['Save','Retire','Reactivate','Delete'])routes['POST /commands/'+v+n]=()=>row(7,'NEW','New',90);
  for(const n of ['HkTaskTypes','HkZones','ComplaintCategories','LostFoundCategories'])routes['POST /commands/Reorder'+n]=()=>({count:2});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors;

  test('[OPSM-SYNC] Loader fills the four lists from the server rows (ids = server ids, retired kept, lists as the prototype row shapes) and the history; versions learned',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    assert.deepEqual(st.mastersX.rows.hkTaskTypes[0],{id:'1',code:'DEP',name:'Departure clean',sortOrder:10,active:true,appliesTo:['checkout','move'],standardMinutes:40,needsInspection:true,
      checklist:'Linen & bathroom\nSurfaces & amenities\nFinal room check'});
    assert.deepEqual([st.mastersX.rows.hkTaskTypes[2].active,st.mastersX.rows.hkTaskTypes[2].retiredOn],[false,'2026-10-20']);
    assert.deepEqual(st.mastersX.rows.hkZones,[{id:'1',code:'Z1',name:'Ground section',sortOrder:10,active:true,floors:['F1'],rooms:['201'],defaultAttendant:'6'},
      {id:'2',code:'Z2',name:'Second',sortOrder:20,active:true,floors:['F2'],rooms:[]}]);
    assert.deepEqual(st.mastersX.rows.complaintCategories[0],{id:'1',code:'NOISE',name:'Noise',sortOrder:10,active:true,department:'Front desk',slaHours:2});
    assert.deepEqual(st.mastersX.rows.lostFoundCategories.map(r=>[r.code,r.valuable,r.department]),[['JEWEL',true,'Front desk'],['CLOTH',false,'Housekeeping']]);
    assert.deepEqual(st.mastersX.history.map(h=>[h.master,h.rowId,h.field,h.old,h.new,h.by]),[['hkTaskTypes','1','standardMinutes','35','40','4']]);
    assert.equal(rt.ids.version('hkTaskType','2'),'V2');assert.equal(rt.ids.version('hkZone','1'),'V1');assert.equal(rt.ids.version('complaintCategory','1'),'V1');assert.equal(rt.ids.version('lostFoundCategory','2'),'V2');
    assert.deepEqual(srv.gets('/housekeeping/zones')[0].query,{page:'1',pageSize:'200'});});
  test('[OPSM-SYNC] The prototype selectors read the server rows: checklist, type by kind, zone of a room, attendant, categories and valuable flag',()=>{
    assert.deepEqual(M.hkChecklistOf(st,{kind:'stayover'}),['Towels','Bin']);assert.equal(M.hkStdMinutes(st,{kind:'checkout'}),40);assert.equal(M.hkNeedsInspection(st,{kind:'checkout'}),true);
    assert.equal(M.hkTypeFor(st,'deep').active,false);
    const withRooms={...st,rooms:M.seed().rooms}; // this run loads only the masters area, the rooms come from the fixture
    assert.equal(M.hkzOf(withRooms,'201')?.code,'Z1');assert.equal(M.hkzOf(withRooms,'101')?.code,'Z1');assert.equal(M.hkzOf(withRooms,'301'),undefined);assert.equal(M.hkzOf(withRooms,'202')?.code,'Z2');assert.equal(M.hkzAttendant({...withRooms,users:M.seed().users},'201'),undefined); // attendant 6 is not a housekeeper of this state
    assert.equal(M.mstLabel(st,'complaintCategories','PARK'),'Parking');assert.deepEqual(M.mstOptions(st,'complaintCategories').map(o=>o.value),['NOISE']);
    assert.equal(M.gsvItemValuable(st,'JEWEL'),true);assert.equal(M.gsvItemValuable(st,'Clothing'),false);});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const addType=await go({type:'MST_MASTER_SAVE',master:'hkTaskTypes',row:{code:' fast ',name:' Fast clean ',sortOrder:50,appliesTo:['inspection'],standardMinutes:'8',needsInspection:false,checklist:'One\n\n Two \n'},key:'ops:t1'});
  const editType=await go({type:'MST_MASTER_SAVE',master:'hkTaskTypes',row:{id:'2',code:'STAY',name:'Stayover service',sortOrder:20,appliesTo:['stayover'],standardMinutes:25,needsInspection:false,checklist:'Towels\nBin'}});
  const zone=await go({type:'MST_MASTER_SAVE',master:'hkZones',row:{code:'VIP',name:'VIP',floors:[],rooms:['101','102'],defaultAttendant:'6'}});
  const zoneEdit=await go({type:'MST_MASTER_SAVE',master:'hkZones',row:{id:'2',code:'Z2',name:'Second',floors:['F2'],rooms:[]}});
  const comp=await go({type:'MST_MASTER_SAVE',master:'complaintCategories',row:{id:'1',code:'NOISE',name:'Noise',department:'Front desk',slaHours:3}});
  const lf=await go({type:'MST_MASTER_SAVE',master:'lostFoundCategories',row:{code:'ART',name:'Artwork',department:'Front desk',slaHours:6,valuable:true}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'hkZones',id:'1'});
  const back=await go({type:'MST_MASTER_REACTIVATE',master:'complaintCategories',id:'2'});
  const del=await go({type:'MST_MASTER_DELETE',master:'lostFoundCategories',id:'2'});
  const order=await go({type:'MST_MASTER_REORDER',master:'hkTaskTypes',ids:['3','1','2']});
  test('[OPSM-SYNC] MST_MASTER_SAVE (new task type) → SaveHkTaskType {code,name,sortOrder,appliesTo,standardMinutes,needsInspection,checklist[]}, the dialog key, reload of mastersX',()=>{
    assert.ok(addType.r.ok);const c=addType.cmds[0];assert.equal(c.path,'/commands/SaveHkTaskType');assert.equal(c.key,'ops:t1');
    assert.deepEqual(c.body,{code:'FAST',name:'Fast clean',sortOrder:50,appliesTo:['inspection'],standardMinutes:8,needsInspection:false,checklist:['One','Two']});assert.ok(addType.gets.includes('/housekeeping/task-types'));});
  test('[OPSM-SYNC] Edit → the row id as "id" and the loaded version as expectedVersion',()=>{
    assert.ok(editType.r.ok);assert.deepEqual(editType.cmds[0].body,{code:'STAY',name:'Stayover service',sortOrder:20,appliesTo:['stayover'],standardMinutes:25,needsInspection:false,checklist:['Towels','Bin'],id:2,expectedVersion:'V2'});});
  test('[OPSM-SYNC] Zones → SaveHkZone {floors,rooms,defaultAttendantId} (no attendant = absent); complaint → SaveComplaintCategory; found item → SaveLostFoundCategory {valuable}',()=>{
    assert.deepEqual([zone.r.ok,zone.cmds[0].path,zone.cmds[0].body],[true,'/commands/SaveHkZone',{code:'VIP',name:'VIP',floors:[],rooms:['101','102'],defaultAttendantId:6}]);
    assert.deepEqual(zoneEdit.cmds[0].body,{code:'Z2',name:'Second',floors:['F2'],rooms:[],id:2,expectedVersion:'V2'});
    assert.deepEqual([comp.cmds[0].path,comp.cmds[0].body],['/commands/SaveComplaintCategory',{code:'NOISE',name:'Noise',department:'Front desk',slaHours:3,id:1,expectedVersion:'V1'}]);
    assert.deepEqual([lf.cmds[0].path,lf.cmds[0].body],['/commands/SaveLostFoundCategory',{code:'ART',name:'Artwork',department:'Front desk',slaHours:6,valuable:true}]);
    for(const x of [addType,editType,zone,zoneEdit,comp,lf])assert.ok(x.r.ok&&x.gets.length>0);});
  test('[OPSM-SYNC] Retire / reactivate / delete / reorder → Retire…/Reactivate…/Delete… {id,expectedVersion} and Reorder… {ids as numbers}',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetireHkZone',{id:1,expectedVersion:'V1'}]);
    assert.deepEqual([back.cmds[0].path,back.cmds[0].body],['/commands/ReactivateComplaintCategory',{id:2,expectedVersion:'V2'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteLostFoundCategory',{id:2,expectedVersion:'V2'}]);
    assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderHkTaskTypes',{ids:[3,1,2]}]);
    for(const x of [ret,back,del,order])assert.ok(x.r.ok);});
  test('[OPSM-SYNC] The floors route still works and an unmapped master is refused before any network call',async()=>{
    const f=await go({type:'MST_MASTER_RETIRE',master:'floors',id:'1'});assert.equal(f.cmds[0]?.path,'/commands/RetireFloor');
    // Masters integration: roomTypes is mapped by MA-1 now, so an unknown list stands in for "unmapped" (old: roomTypes).
    const u=await go({type:'MST_MASTER_SAVE',master:'notAMaster',row:{code:'DLX',name:'Deluxe'}});assert.deepEqual([u.r.ok,u.cmds.length],[false,0]);});

  srv.on('POST /commands/SaveHkTaskType',()=>reply(422,{code:'VALIDATION',message:'Departure clean is already handled by “Departure clean” — each kind belongs to one active type.',details:{reason:'MASTER_INVALID',field:'appliesTo'}}));
  const bad=await rt.run({type:'MST_MASTER_SAVE',master:'hkTaskTypes',row:{code:'X',name:'X',appliesTo:['checkout'],standardMinutes:30,checklist:'A'}});
  const rec=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-003','A']]})});
  const rrt=await startRuntime(S,rec,mine);
  test('[OPSM-SYNC] Master errors keep their reason and field; a housekeeper loads the lists (checklist, categories) without any history read',()=>{
    assert.deepEqual([bad.ok,bad.reason],[false,'server']);assert.equal(M.mstReasonOf(bad.error),'MASTER_INVALID');assert.match(bad.text,/already handled by/);
    const paths=rec.gets().map(c=>c.path);
    for(const p of ['/housekeeping/task-types','/housekeeping/zones','/guest-services/complaint-categories','/guest-services/lost-found-categories'])assert.ok(paths.includes(p),p);
    assert.equal(paths.filter(p=>p.endsWith('/history')).length,0);assert.equal(rrt.get().state.mastersX.rows.hkTaskTypes.length,3);assert.deepEqual(rrt.get().state.mastersX.history,[]);assert.deepEqual(rrt.get().errors,{});});

  test('[OPSM-SYNC] production mapping present: ops-masters.ts routes merged into MST_SERVER, reads listed, guest-services keeps the category code',()=>{
    const a=src('src/clients/sync/masters.ts'),o=src('src/clients/sync/ops-masters.ts'),r=src('src/clients/sync/reads.ts'),g=src('src/clients/sync/services/guest.ts');
    assert.match(a,/import \{OPS_MST_SERVER\} from '\.\/ops-masters';/);assert.match(a,/\.\.\.OPS_MST_SERVER,/);
    for(const n of ['SaveHkTaskType','RetireHkZone','ReactivateComplaintCategory','DeleteLostFoundCategory','ReorderHkTaskTypes','ReorderHkZones','ReorderComplaintCategories','ReorderLostFoundCategories'])assert.ok(o.includes(`'${n}'`),n);
    for(const p of ['/housekeeping/task-types','/housekeeping/zones','/guest-services/complaint-categories','/guest-services/lost-found-categories']){assert.ok(r.includes(`'GET ${p}':always`),p);assert.ok(r.includes(`'GET ${p}/history':one(wide('PERM-MST-001'))`),p+'/history');}
    assert.match(g,/categoryCode:c\.categoryCode\?\?undefined/);assert.match(g,/categoryCode:i\.categoryCode\?\?undefined/);});

  // Every page renders on the server state (task cards with checklist / timing / zone, guest services with categories).
  let proto=M.execute(M.seed(),'owner',{type:'MST_MASTER_SAVE',master:'hkZones',row:{code:'VIP',name:'VIP',rooms:['101'],defaultAttendant:'housekeeper2'}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_SAVE',master:'complaintCategories',row:{code:'PARK',name:'Parking',department:'Front desk',slaHours:6}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_RETIRE',master:'lostFoundCategories',id:'lfc-cloth'});
  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(proto,'owner',false),P.renderPages(proto,'owner',true),P.renderPages(proto,'techadmin',false),P.renderPages(proto,'housekeeper',true),P.renderPages(proto,'supervisor',true)];
  test('[OPSM-SYNC] Every desktop and phone page renders on the loaded server data and on a prototype state with new zones / categories (owner, tech admin, housekeeper, supervisor)',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
