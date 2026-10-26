// MA-0 · Masters area of the production sync layer (src/clients/sync/masters.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Masters/MastersContracts.cs). Names prefixed [MST-SYNC].
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters'].includes(a.id))};
  const floor=(id,code,name,sortOrder,o={})=>({floorId:id,code,name,sortOrder,active:true,retiredOn:null,roomCount:8,updatedUtc:'2026-10-24T03:00:00.000Z',version:'V'+id,...o});
  const floors=[floor(1,'F1','Ground',10),floor(2,'F2','Floor 2',20),floor(3,'F3','Top floor',30,{active:false,retiredOn:'2026-10-20'})];
  const hist=[{historyId:7,rowId:1,code:'F1',field:'name',oldValue:'Floor 1',newValue:'Ground',changedBy:1,changedByName:'owner',changedUtc:'2026-10-24T03:30:00.000Z'},
    {historyId:5,rowId:3,code:'F3',field:'active',oldValue:'active',newValue:'retired',changedBy:4,changedByName:'techadmin',changedUtc:'2026-10-20T05:00:00.000Z'}];
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const names=['SaveFloor','RetireFloor','ReactivateFloor','ReorderFloors','DeleteFloor'];
  const routes={'GET /masters/floors':()=>page(floors),'GET /masters/floors/history':()=>page(hist)};
  // MA-1 (old → new): the masters loader also reads the room-types / amenities / occupancy-charges lists — empty here (tests/sync-roommasters.mjs covers them).
  for(const x of ['room-types','amenities','occupancy-charges'])for(const h of ['','/history'])routes[`GET /masters/${x}${h}`]=()=>page([]);
  routes['GET /pos/menu-categories']=()=>page([]); // MA-4: the masters loader also reads the POS menu categories (sync/pos-masters.ts)
  for(const n of names)routes['POST /commands/'+n]=()=>(n==='SaveFloor'?floor(4,'F4','Annexe',40,{roomCount:0}):n==='ReorderFloors'?page(floors):{});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors,bootGets=srv.gets().map(c=>c.path);

  test('[MST-SYNC] Loader fills mastersX.rows.floors (ids = server ids, retired kept) and the change history (IST clock, user ids); versions learned',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    assert.deepEqual(st.mastersX.rows.floors,[{id:'1',code:'F1',name:'Ground',sortOrder:10,active:true},{id:'2',code:'F2',name:'Floor 2',sortOrder:20,active:true},
      {id:'3',code:'F3',name:'Top floor',sortOrder:30,active:false,retiredOn:'2026-10-20'}]);
    assert.deepEqual(st.mastersX.history.map(h=>[h.id,h.rowId,h.field,h.old,h.new,h.by,h.at]),[['7','1','name','Floor 1','Ground','1','2026-10-24T09:00'],['5','3','active','active','retired','4','2026-10-20T10:30']]);
    assert.equal(rt.ids.version('floor','2'),'V2');assert.ok(bootGets.includes('/masters/floors')&&bootGets.includes('/masters/floors/history'));
    assert.deepEqual(srv.gets('/masters/floors')[0].query,{page:'1',pageSize:'200'});});
  test('[MST-SYNC] The prototype selectors read the server rows: names, retired label kept, active pick list',()=>{
    assert.equal(M.mstLabel(st,'floors','F1'),'Ground');assert.equal(M.mstLabel(st,'floors','F3'),'Top floor');
    assert.deepEqual(M.mstOptions(st,'floors').map(o=>o.value),['F1','F2']);assert.equal(M.mstFloorLabel(st,1),'Ground');assert.equal(M.mstFloorLabel(st,5),'Floor 5');});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const add=await go({type:'MST_MASTER_SAVE',master:'floors',row:{code:' f4 ',name:' Annexe ',sortOrder:40},key:'mst:add'});
  const edit=await go({type:'MST_MASTER_SAVE',master:'floors',row:{id:'2',code:'F2',name:'First floor',sortOrder:20}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'floors',id:'2'});
  const back=await go({type:'MST_MASTER_REACTIVATE',master:'floors',id:'3'});
  const del=await go({type:'MST_MASTER_DELETE',master:'floors',id:'1'});
  const order=await go({type:'MST_MASTER_REORDER',master:'floors',ids:['3','1','2']});
  const other=await go({type:'MST_MASTER_SAVE',master:'__noServer',row:{code:'DLX',name:'Deluxe'}}); // MA-1: roomTypes now has a route (old → new)
  test('[MST-SYNC] MST_MASTER_SAVE (new) → SaveFloor {code,name,sortOrder}, the dialog key, reload of mastersX',()=>{
    assert.ok(add.r.ok);const c=add.cmds[0];assert.equal(c.path,'/commands/SaveFloor');assert.equal(c.key,'mst:add');
    assert.deepEqual(c.body,{code:'F4',name:'Annexe',sortOrder:40});assert.ok(add.gets.includes('/masters/floors'));});
  test('[MST-SYNC] MST_MASTER_SAVE (edit) → SaveFloor with floorId and the loaded version as expectedVersion',()=>{
    assert.ok(edit.r.ok);assert.deepEqual(edit.cmds[0].body,{code:'F2',name:'First floor',sortOrder:20,floorId:2,expectedVersion:'V2'});assert.ok(edit.cmds[0].key);});
  test('[MST-SYNC] Retire / reactivate / delete → RetireFloor / ReactivateFloor / DeleteFloor {floorId, expectedVersion}',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetireFloor',{floorId:2,expectedVersion:'V2'}]);
    assert.deepEqual([back.cmds[0].path,back.cmds[0].body],['/commands/ReactivateFloor',{floorId:3,expectedVersion:'V3'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteFloor',{floorId:1,expectedVersion:'V1'}]);
    for(const x of [ret,back,del])assert.ok(x.r.ok&&x.gets.includes('/masters/floors'));});
  test('[MST-SYNC] Reorder → ReorderFloors {floorIds} (numbers, new order)',()=>{
    assert.ok(order.r.ok);assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderFloors',{floorIds:[3,1,2]}]);});
  test('[MST-SYNC] A master without a server route is refused before any network call; the area maps the five MST commands',()=>{
    assert.deepEqual([other.r.ok,other.r.reason,other.cmds.length],[false,'precheck',0]);assert.match(other.r.text,/Not available on the server yet: the __noServer list/);
    const area=S.AREAS.find(a=>a.id==='masters');assert.deepEqual(area.loaders.map(l=>l.slice),['mastersX']);
    assert.deepEqual(area.commands.map(c=>c.type).sort(),['MST_MASTER_DELETE','MST_MASTER_REACTIVATE','MST_MASTER_REORDER','MST_MASTER_RETIRE','MST_MASTER_SAVE']);});

  // Server master errors keep their reason (details.reason) for the screens.
  srv.on('POST /commands/SaveFloor',()=>reply(422,{code:'VALIDATION',message:'Code F1 is already used in Floors.',details:{reason:'MASTER_CODE_TAKEN',field:'code'}}));
  const taken=await rt.run({type:'MST_MASTER_SAVE',master:'floors',row:{code:'F1',name:'Again'}});
  // A role without PERM-MST-001 (Reception) loads the floors but never asks for the history (D5 read gate).
  const rec=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A']]})});
  const rrt=await startRuntime(S,rec,mine);
  test('[MST-SYNC] MASTER_CODE_TAKEN from the server surfaces with its reason; Reception loads floors without the history read',()=>{
    assert.deepEqual([taken.ok,taken.reason],[false,'server']);assert.equal(M.mstReasonOf(taken.error),'MASTER_CODE_TAKEN');assert.match(taken.text,/already used/);
    assert.deepEqual(rec.gets().map(c=>c.path).filter(p=>p.startsWith('/masters')&&p.endsWith('/history')),[]);assert.ok(rec.gets().some(c=>c.path==='/masters/floors'));
    assert.equal(rrt.get().state.mastersX.rows.floors.length,3);assert.deepEqual(rrt.get().state.mastersX.history,[]);
    assert.deepEqual(rrt.get().errors,{});});

  // Every page renders on the server state and on a prototype state with renamed / retired floors (boards, hub Lists block, phone).
  let proto=M.execute(M.seed(),'owner',{type:'MST_MASTER_SAVE',master:'floors',row:{id:'flr-1',code:'F1',name:'Ground'}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-3'});
  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(proto,'owner',false),P.renderPages(proto,'owner',true),P.renderPages(proto,'techadmin',false),P.renderPages(proto,'housekeeper',true)];
  test('[MST-SYNC] Every desktop and phone page renders on the loaded server data and on renamed / retired floors',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
