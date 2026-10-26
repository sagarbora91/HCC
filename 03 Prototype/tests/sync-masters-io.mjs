// MA-8 · MST_IMPORT (masters JSON import and "Paste rows") in the production sync layer (src/clients/sync/masters-import.ts) against a fake server shaped like the
// import DTOs (08 Production/src/Hcc.Contracts/Import/ImportContracts.cs): UploadImportBatch with the template CSV, then CommitImportBatch. Names prefixed [MIO-SYNC].
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters'].includes(a.id))};
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const routes={'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([])};
  for(const x of ['room-types','amenities','occupancy-charges','booking-sources','id-types','number-series','policies'])routes['GET /masters/'+x]=routes['GET /masters/'+x+'/history']=()=>page([]);
  routes['GET /pos/menu-categories']=()=>page([]);
  for(const p of ['/housekeeping/task-types','/housekeeping/zones','/guest-services/complaint-categories','/guest-services/lost-found-categories','/hr/designations','/hr/shift-patterns','/hr/leave-types','/hr/holidays']){routes['GET '+p]=()=>page([]);routes['GET '+p+'/history']=()=>page([]);}
  const report=(over={})=>({batchId:11,master:'floors',fileName:'floors.csv',status:'Uploaded',rows:1,errorCount:0,errors:[],warnings:[],counts:{create:1,update:0,unchanged:0,remove:0},plan:[],commitAllowed:true,...over});
  let uploads=[];
  routes['POST /commands/UploadImportBatch']=r=>{uploads.push(r.body);return report({master:r.body.master,counts:{create:r.body.csvText.split('\r\n').length-2,update:0,unchanged:0,remove:0}});};
  routes['POST /commands/CommitImportBatch']=r=>({batchId:r.body.batchId,committed:true,counts:{create:1,update:0,unchanged:0,remove:0}});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const go=async cmd=>{srv.reset();uploads=[];const r=await rt.run(cmd);return {r,posts:srv.calls.filter(c=>c.method==='POST')};};
  const area=S.AREAS.find(a=>a.id==='masters');

  test('[MIO-SYNC] the masters area maps MST_IMPORT (one mapper per type) next to the five MST commands',()=>{
    assert.deepEqual(area.commands.map(c=>c.type).sort(),['MST_IMPORT','MST_MASTER_DELETE','MST_MASTER_REACTIVATE','MST_MASTER_REORDER','MST_MASTER_RETIRE','MST_MASTER_SAVE']);
    assert.equal(S.AREAS.flatMap(a=>a.commands).filter(c=>c.type==='MST_IMPORT').length,1);});
  const pasted=await go({type:'MST_IMPORT',sets:[{master:'floors',source:'csv',rows:[{code:'F4',name:'Annexe',sortOrder:40},{code:'F5',name:'Roof'}]}],key:'mio:paste'});
  test('[MIO-SYNC] Paste rows → UploadImportBatch {master:"floors", fileName, csvText with the template headers, only the columns given} then CommitImportBatch {batchId}; mastersX reloaded',()=>{
    assert.ok(pasted.r.ok,pasted.r.text);assert.deepEqual(pasted.posts.map(p=>p.path),['/commands/UploadImportBatch','/commands/CommitImportBatch']);
    assert.deepEqual(uploads[0],{master:'floors',fileName:'floors.csv',csvText:'code,name,sortOrder\r\nF4,Annexe,40\r\nF5,Roof,\r\n'});
    assert.deepEqual(pasted.posts[1].body,{batchId:11});assert.ok(pasted.posts[0].key&&pasted.posts[1].key&&pasted.posts[0].key!==pasted.posts[1].key);
    assert.ok(srv.gets('/masters/floors').length>0);});
  const money=await go({type:'MST_IMPORT',key:'mio:money',sets:[{master:'extras',rows:[{code:'SPA',name:'Spa',category:'misc',price:[{amount:120050,effectiveFrom:'2026-10-24'},{amount:130000,effectiveFrom:'2027-01-01'}],gst:'other',unit:'each',department:'frontdesk'}]},
    {master:'hkZones',rows:[{code:'Z9',name:'Zone',floors:['F1','F2'],rooms:['301']}]}]});
  test('[MIO-SYNC] cells follow the server file: rupees from the amount in force, ";" lists, yes/no; one upload + commit per list',()=>{
    assert.ok(money.r.ok,money.r.text);assert.deepEqual(uploads.map(u=>u.master),['extras','hk-zones']);
    assert.equal(uploads[0].csvText,'code,name,category,price,gst,unit,department\r\nSPA,Spa,misc,1200.50,other,each,frontdesk\r\n');
    assert.equal(uploads[1].csvText,'code,name,floors,rooms\r\nZ9,Zone,F1;F2,301\r\n');});
  const outlets=await go({type:'MST_IMPORT',key:'mio:outlets',sets:[{master:'posTables',rows:[{code:'REST-T9',name:'Restaurant T9',outlet:'REST',tableNo:'T9',seats:4}]},{master:'outlets',rows:[{code:'REST',name:'Restaurant',type:'restaurant'}]}]});
  test('[MIO-SYNC] an outlet row carries the table names of the same file in the server "tables" column; outlets go before their tables',()=>{
    assert.ok(outlets.r.ok,outlets.r.text);assert.deepEqual(uploads.map(u=>u.master),['pos-outlets','pos-tables']);
    assert.equal(uploads[0].csvText,'code,name,type,tables\r\nREST,Restaurant,restaurant,T9\r\n');});
  srv.on('POST /commands/UploadImportBatch',r=>report({master:r.body.master,errorCount:2,commitAllowed:false,errors:[{row:2,column:'maxPct',message:'The maximum discount is a percentage from 0.01 to 100.'},{row:3,column:'code',message:'Duplicate of row 2.'}]}));
  const refused=await go({type:'MST_IMPORT',key:'mio:refused',sets:[{master:'discountReasons',rows:[{code:'X',name:'X',maxPct:0}]}]});
  test('[MIO-SYNC] a refused file is not committed and the server row problems are shown',()=>{
    assert.equal(refused.r.ok,false);assert.deepEqual(refused.posts.map(p=>p.path),['/commands/UploadImportBatch']);assert.match(refused.r.text,/2 row problems — nothing was imported from this list/);assert.match(refused.r.text,/row 2 · maxPct: The maximum discount/);});
  srv.on('POST /commands/UploadImportBatch',r=>report({master:r.body.master,commitAllowed:false,counts:{create:0,update:0,unchanged:1,remove:0}}));
  const same=await go({type:'MST_IMPORT',key:'mio:same',sets:[{master:'floors',rows:[{code:'F1',name:'Floor 1'}]}]});
  const pre=[];for(const master of ['roomTypes','rooms','__nope'])pre.push([master,await go({type:'MST_IMPORT',key:'mio:'+master,sets:[{master,rows:[{code:'A',name:'B'}]}]})]);
  const empty=await go({type:'MST_IMPORT',key:'mio:empty',sets:[]});
  test('[MIO-SYNC] a file with nothing to change commits nothing; room types and rooms (own import) and unknown lists are refused before any network call',()=>{
    assert.ok(same.r.ok);assert.deepEqual(same.posts.map(p=>p.path),['/commands/UploadImportBatch']);
    for(const [master,x] of pre)assert.deepEqual([x.r.ok,x.r.reason,x.posts.length],[false,'precheck',0],master);
    assert.match(empty.r.text,/Nothing to import/);});
}
