// MA-1 · Rooms masters on the production sync layer (sync/roommasters.ts RMS_SERVER + area 'roommasters', sync/rooms.ts master fields)
// against a fake server shaped like the .NET DTOs (08 Production/src/Hcc.Contracts/RoomMasters/RoomMastersContracts.cs). Names [RMS-SYNC].
import {loadSync,loadPages,fakeServer,startRuntime,reply} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const row=(roomId,roomNo,over={})=>({roomId,roomNo,roomType:'DLX',hk:'Ready',status:'Ready',block:null,current:null,next:null,openTasks:[],roomVersion:'RV'+roomId,...over});
  const board=[row(7,'105'),row(8,'106',{block:{blockId:61,from:'2026-10-25',to:'2026-10-27',reason:'Out of order · Leak',source:'Inventory',version:'BV61'}}),row(9,'1201',{roomType:'STE'}),row(10,'207')];
  const master=(roomId,roomNo,floorId,floorCode,over={})=>({roomId,roomNo,floorId,floorCode,roomTypeId:2,roomTypeCode:'DLX',features:[],smoking:false,connectingRoomNo:null,extension:null,notes:null,active:true,retiredOn:null,version:'MV'+roomId,...over});
  const rooms=[master(7,'105',1,'F1',{features:['WIFI'],smoking:true,extension:'2105',notes:'Garden side'}),master(8,'106',1,'F1'),master(9,'1201',3,'F12',{roomTypeId:3,roomTypeCode:'STE'}),
    master(10,'207',2,'F2',{active:false,retiredOn:'2026-10-20'})];
  const floors=[{floorId:1,code:'F1',name:'Ground',sortOrder:10,active:true,retiredOn:null,roomCount:2,updatedUtc:'2026-10-24T03:00:00.000Z',version:'FV1'},
    {floorId:2,code:'F2',name:'First',sortOrder:20,active:true,retiredOn:null,roomCount:1,updatedUtc:'2026-10-24T03:00:00.000Z',version:'FV2'},
    {floorId:3,code:'F12',name:'Sky',sortOrder:30,active:true,retiredOn:null,roomCount:1,updatedUtc:'2026-10-24T03:00:00.000Z',version:'FV3'}];
  const type=(id,code,name,over={})=>({roomTypeId:id,code,name,maxAdults:2,maxChildren:1,extraBedAllowed:true,bedType:'King',sizeSqft:240,amenities:['WIFI'],description:'Spacious',sortOrder:id*10,active:true,
    retiredOn:null,roomCount:2,rackRates:[{amountPaise:300000,effectiveFrom:'2000-01-01'},{amountPaise:320000,effectiveFrom:'2026-11-01'}],updatedUtc:'2026-10-24T03:00:00.000Z',version:'TV'+id,...over});
  const types=[type(2,'DLX','Deluxe'),type(3,'STE','Suite',{rackRates:[{amountPaise:450000,effectiveFrom:'2000-01-01'}]})];
  const amen=[{amenityId:4,code:'WIFI',name:'Wi-Fi',icon:'wifi',showOnWebsite:true,sortOrder:10,active:true,retiredOn:null,version:'AV4'}];
  const occ=[{chargeId:5,code:'X-DLX',name:'Deluxe extras',roomTypeId:2,roomTypeCode:'DLX',childAgeFrom:6,childAgeTo:12,extraAdult:[{amountPaise:80000,effectiveFrom:'2025-04-01'}],
    extraChild:[],extraBed:[{amountPaise:100000,effectiveFrom:'2025-04-01'}],sortOrder:10,active:true,retiredOn:null,version:'OV5'}];
  const cmds=['SaveRoomType','RetireRoomType','ReactivateRoomType','ReorderRoomTypes','DeleteRoomType','SaveAmenity','SaveOccupancyCharge','SaveRoom','RetireRoom','ReactivateRoom','DeleteRoom','AddRooms','RenumberRoom','BlockRoom','UnblockRoom'];
  const routes={'GET /rooms/board':()=>({businessDate:'2026-10-24',page:1,pageSize:100,total:board.length,rows:board,counts:{},assignedOnly:false}),
    'GET /housekeeping/tasks':()=>({page:1,pageSize:100,total:0,rows:[],assignedOnly:false}),'GET /availability':()=>({businessDate:'2026-10-24',rooms:[],types:[]}),
    'GET /masters/rooms':()=>({items:rooms}),'GET /masters/floors':()=>page(floors),'GET /masters/floors/history':()=>page([]),
    'GET /masters/room-types':()=>page(types),'GET /masters/room-types/history':()=>page([]),'GET /masters/amenities':()=>page(amen),'GET /masters/amenities/history':()=>page([]),
    'GET /masters/occupancy-charges':()=>page(occ),'GET /masters/occupancy-charges/history':()=>page([])};
  for(const n of cmds)routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>['core','masters','rooms','roommasters'].includes(a.id))});
  const st=rt.get().state,boot=rt.get().errors;

  test('[RMS-SYNC] room types / amenities / occupancy charges load into mastersX (key = code, rack rate + charges as dated money)',()=>{
    assert.deepEqual(boot,{});assert.deepEqual(srv.unrouted,[]);
    const t=st.mastersX.rows.roomTypes;assert.deepEqual(t.map(r=>[r.id,r.code,r.key,r.name]),[['2','DLX','DLX','Deluxe'],['3','STE','STE','Suite']]);
    assert.deepEqual(t[0].rackRate,[{amount:300000,effectiveFrom:'2000-01-01'},{amount:320000,effectiveFrom:'2026-11-01'}]);
    assert.equal(M.rmsTypeName(st,'DLX'),'Deluxe');assert.equal(M.rmsRackOn(st,'DLX','2026-11-02'),320000);
    assert.deepEqual(st.mastersX.rows.amenities.map(a=>[a.id,a.code,a.icon,a.showOnWebsite]),[['4','WIFI','wifi',true]]);
    const o=st.mastersX.rows.occupancyCharges[0];assert.deepEqual([o.id,o.roomType,o.extraAdult,o.extraChild,o.childAgeTo],['5','DLX',[{amount:80000,effectiveFrom:'2025-04-01'}],undefined,12]);
    assert.equal(rt.ids.version('roomTypeRow','2'),'TV2');assert.equal(rt.ids.version('occupancyCharge','5'),'OV5');});
  test('[RMS-SYNC] rooms read the floor from floor_id (F12 → 12, not the hundreds digit), master fields, retired status, out-of-order blocks',()=>{
    const r=id=>st.rooms.find(x=>x.id===id);
    assert.deepEqual(st.rooms.map(x=>[x.id,x.floor]),[['105',1],['106',1],['1201',12],['207',2]]);
    assert.deepEqual([r('105').smoking,r('105').extension,r('105').notes,r('105').features],[true,'2105','Garden side',['WIFI']]);
    assert.deepEqual([r('207').status,r('207').retiredOn],['retired','2026-10-20']);assert.ok(!M.rmsBoardRooms(st).some(x=>x.id==='207'));
    assert.deepEqual([r('106').status,r('106').ooo,r('106').blocked],['out-of-order',{from:'2026-10-25',to:'2026-10-27',reason:'Leak'},false]);
    assert.equal(M.available(st,'106','2026-10-25','2026-10-26'),false);assert.equal(M.mstFloorLabel(st,r('1201')),'Sky');
    assert.equal(rt.ids.version('room','105'),'MV7');
    assert.deepEqual(M.mstRows(st,'rooms').map(x=>[x.code,x.floor,x.type,x.active]),[['105','F1','DLX',true],['106','F1','DLX',true],['207','F2','DLX',false],['1201','F12','STE',true]]);});

  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);return {r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)};};
  const saveType=await go({type:'MST_MASTER_SAVE',master:'roomTypes',row:{id:'2',code:'DLX',name:'Deluxe King',maxAdults:2,maxChildren:1,extraBedAllowed:true,bedType:'King',sizeSqft:240,amenities:['WIFI'],description:' Spacious ',sortOrder:20,
    rackRate:[{amount:300000,effectiveFrom:'2000-01-01'},{amount:320000,effectiveFrom:'2026-11-01'}]},key:'rt:1'});
  const newType=await go({type:'MST_MASTER_SAVE',master:'roomTypes',row:{code:'exec',name:'Executive',maxAdults:2}});
  const retType=await go({type:'MST_MASTER_RETIRE',master:'roomTypes',id:'3'});
  const saveAm=await go({type:'MST_MASTER_SAVE',master:'amenities',row:{id:'4',code:'WIFI',name:'Wi-Fi 6',icon:'wifi',showOnWebsite:false,sortOrder:10}});
  const saveOcc=await go({type:'MST_MASTER_SAVE',master:'occupancyCharges',row:{id:'5',code:'X-DLX',name:'Deluxe extras',roomType:'DLX',childAgeFrom:5,childAgeTo:12,extraAdult:[{amount:80000,effectiveFrom:'2025-04-01'}],sortOrder:10}});
  test('[RMS-SYNC] room types: SaveRoomType body (rack rates in paise, version), new type without id, RetireRoomType; reload adds rooms',()=>{
    assert.ok(saveType.r.ok,saveType.r.text);const c=saveType.cmds[0];assert.equal(c.path,'/commands/SaveRoomType');assert.equal(c.key,'rt:1');
    assert.deepEqual(c.body,{code:'DLX',name:'Deluxe King',sortOrder:20,maxAdults:2,maxChildren:1,extraBedAllowed:true,bedType:'King',sizeSqft:240,amenities:['WIFI'],description:'Spacious',
      rackRates:[{amountPaise:300000,effectiveFrom:'2000-01-01'},{amountPaise:320000,effectiveFrom:'2026-11-01'}],roomTypeId:2,expectedVersion:'TV2'});
    assert.ok(saveType.gets.includes('/masters/room-types')&&saveType.gets.includes('/rooms/board'),'reloads mastersX and rooms');
    assert.equal(newType.cmds[0].body.roomTypeId,undefined);assert.equal(newType.cmds[0].body.code,'EXEC');
    assert.deepEqual([retType.cmds[0].path,retType.cmds[0].body],['/commands/RetireRoomType',{roomTypeId:3,expectedVersion:'TV3'}]);});
  test('[RMS-SYNC] amenities → SaveAmenity {icon, showOnWebsite}; occupancy charges → SaveOccupancyCharge with roomTypeId and paise lists',()=>{
    assert.deepEqual(saveAm.cmds[0].body,{code:'WIFI',name:'Wi-Fi 6',sortOrder:10,icon:'wifi',showOnWebsite:false,amenityId:4,expectedVersion:'AV4'});
    assert.deepEqual(saveOcc.cmds[0].body,{code:'X-DLX',name:'Deluxe extras',sortOrder:10,roomTypeId:2,childAgeFrom:5,childAgeTo:12,extraAdult:[{amountPaise:80000,effectiveFrom:'2025-04-01'}],
      extraChild:[],extraBed:[],chargeId:5,expectedVersion:'OV5'});});

  const saveRoom=await go({type:'MST_MASTER_SAVE',master:'rooms',row:{id:'105',code:'105',name:'Room 105',floor:'F2',type:'STE',features:['WIFI'],smoking:false,connecting:' 106 ',extension:'',notes:'Garden',sortOrder:10}});
  const newRoom=await go({type:'MST_MASTER_SAVE',master:'rooms',row:{code:'109',floor:'F1',type:'DLX'}});
  const retRoom=await go({type:'MST_MASTER_RETIRE',master:'rooms',id:'105'});
  const delRoom=await go({type:'MST_MASTER_DELETE',master:'rooms',id:'1201'});
  const ordRoom=await go({type:'MST_MASTER_REORDER',master:'rooms',ids:['106','105']});
  test('[RMS-SYNC] rooms: SaveRoom by room number → roomId, floorId / roomTypeId from the lists, room version; Add only via range add; no reorder',()=>{
    assert.ok(saveRoom.r.ok,saveRoom.r.text);
    assert.deepEqual(saveRoom.cmds[0].body,{roomNo:'105',floorId:2,roomTypeId:3,features:['WIFI'],smoking:false,connectingRoomNo:'106',extension:null,notes:'Garden',roomId:7,expectedVersion:'MV7'});
    assert.ok(saveRoom.gets.includes('/rooms/board'));
    assert.deepEqual([newRoom.r.ok,newRoom.r.reason,newRoom.cmds.length],[false,'precheck',0]);assert.match(newRoom.r.text,/Add rooms/);
    assert.deepEqual([retRoom.cmds[0].path,retRoom.cmds[0].body],['/commands/RetireRoom',{roomId:7,expectedVersion:'MV7'}]);
    assert.deepEqual([delRoom.cmds[0].path,delRoom.cmds[0].body],['/commands/DeleteRoom',{roomId:9,expectedVersion:'MV9'}]);
    assert.deepEqual([ordRoom.r.ok,ordRoom.r.reason,ordRoom.cmds.length],[false,'precheck',0]);});

  const addR=await go({type:'RMS_ROOMS_ADD',rooms:'110–112',floor:'F1',roomType:'DLX',key:'add:1'});
  const renum=await go({type:'RMS_ROOM_RENUMBER',id:'105',to:'115'});
  const ooo=await go({type:'RMS_ROOM_OOO',id:'105',from:'2026-10-26',to:'2026-10-28',reason:'Painting'});
  const clr=await go({type:'RMS_ROOM_OOO_CLEAR',id:'106'});
  const badOoo=await go({type:'RMS_ROOM_OOO',id:'105',from:'2026-10-20',reason:'Painting'});
  test('[RMS-SYNC] tools: AddRooms {roomNos, floorId, roomTypeId}, RenumberRoom (room version), out of order = BlockRoom "Out of order · …" / UnblockRoom',()=>{
    assert.deepEqual([addR.cmds[0].path,addR.cmds[0].body,addR.cmds[0].key],['/commands/AddRooms',{roomNos:['110','111','112'],floorId:1,roomTypeId:2},'add:1']);
    assert.deepEqual([renum.cmds[0].path,renum.cmds[0].body],['/commands/RenumberRoom',{roomId:7,roomNo:'115',expectedVersion:'MV7'}]);
    assert.deepEqual([ooo.cmds[0].path,ooo.cmds[0].body],['/commands/BlockRoom',{roomId:7,reason:'Out of order · Painting',from:'2026-10-26',to:'2026-10-28'}]);
    assert.deepEqual(clr.cmds[0].path,'/commands/UnblockRoom');assert.equal(clr.cmds[0].body.roomId,8);
    assert.deepEqual([badOoo.r.ok,badOoo.r.reason],[false,'precheck']);
    const area=S.AREAS.find(a=>a.id==='roommasters');assert.deepEqual(area.commands.map(c=>c.type),['RMS_ROOMS_ADD','RMS_ROOM_RENUMBER','RMS_ROOM_OOO','RMS_ROOM_OOO_CLEAR']);});

  srv.on('POST /commands/SaveRoom',()=>reply(409,{code:'INVALID_TRANSITION',message:'Room 105 has bookings from today on.',details:{reason:'MASTER_LOCKED_BY_BOOKINGS',field:'roomTypeId'}}));
  const locked=await rt.run({type:'MST_MASTER_SAVE',master:'rooms',row:{id:'105',code:'105',floor:'F1',type:'STE'}});
  const old=fakeServer({...routes,'GET /masters/rooms':()=>reply(404,{code:'NOT_FOUND',message:'No route'})});
  const ort=await startRuntime(S,old,{areas:S.AREAS.filter(a=>['core','rooms'].includes(a.id))});
  const P=await loadPages();const pages=[P.renderPages(st,'1',false),P.renderPages(st,'1',true)];
  test('[RMS-SYNC] server master errors keep their reason; an older server without /masters/rooms loads rooms by number; pages render',()=>{
    assert.deepEqual([locked.ok,locked.reason],[false,'server']);assert.equal(M.mstReasonOf(locked.error),'MASTER_LOCKED_BY_BOOKINGS');
    assert.deepEqual(ort.get().errors,{});assert.deepEqual(ort.get().state.rooms.map(r=>[r.id,r.floor]),[['105',1],['106',1],['1201',12],['207',2]]);
    assert.deepEqual(pages.map(x=>x.errors),[{},{}]);assert.ok(pages.every(x=>x.rendered>5));});
}
