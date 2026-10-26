// MA-5 · Front-desk lists through the production sync layer (src/clients/sync/frontdesk-master.ts, entries in masters.ts MST_SERVER)
// against a fake server shaped like 08 Production/src/Hcc.Contracts/FrontDeskMasters/FrontDeskMastersContracts.cs. Names [FDM-SYNC].
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters'].includes(a.id))};
  const V=(id,o)=>({rowId:id,sortOrder:id>0?id*10:-id*10,active:true,retiredOn:null,usage:0,updatedUtc:'2026-10-24T03:00:00.000Z',version:id>0?'V'+id:'',...o});
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  // Sources and ID types are stored; numbering and policies are still the server defaults (negative ids, no version).
  const sources=[V(1,{code:'WALKIN',name:'Walk-in',segment:'DIRECT',channel:null,commissionBps:null}),V(2,{code:'OTA-BOOKNEST',name:'OTA · BookNest',segment:'OTA',channel:'BookNest',commissionBps:1800}),
    V(3,{code:'PHONE',name:'Phone',segment:'DIRECT',channel:null,commissionBps:0,active:false,retiredOn:'2026-10-20'})];
  const idTypes=[V(4,{code:'AADHAAR',name:'Aadhaar',accepted:true,needsPhoto:false,foreignerOnly:false}),V(5,{code:'VISA',name:'Passport + visa',accepted:true,needsPhoto:true,foreignerOnly:true})];
  const series=[V(-1,{code:'INV',name:'Tax invoice',document:'INV',prefix:'INV',startNo:1,padding:7,reset:'FY',activeFrom:'2000-04-01',issued:12,nextNumber:'INV/2627/0000013'}),
    V(-2,{code:'CRN',name:'Credit note',document:'CRN',prefix:'CN',startNo:1,padding:8,reset:'FY',activeFrom:'2000-04-01',issued:0,nextNumber:'CN/2627/00000001'})];
  const policies=[V(-1,{code:'DEFAULT',name:'Default',freeCancelHours:0,cancelChargeBps:0,noShowChargeBps:0,depositBps:0,sources:[]})];
  const hist=[{historyId:9,rowId:2,code:'OTA-BOOKNEST',field:'commissionBps',oldValue:'—',newValue:'18 %',changedBy:1,changedByName:'owner',changedUtc:'2026-10-24T03:30:00.000Z'}];
  const lists={'booking-sources':sources,'id-types':idTypes,'number-series':series,'policies':policies};
  const routes={'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([])};
  for(const [p,items] of Object.entries(lists)){routes['GET /masters/'+p]=()=>page(items);routes['GET /masters/'+p+'/history']=()=>page(p==='booking-sources'?hist:[]);}
  const names=['SaveBookingSource','RetireBookingSource','ReactivateBookingSource','ReorderBookingSources','DeleteBookingSource','SaveIdType','RetireIdType','SaveNumberSeries',
    'RetireNumberSeries','ReorderNumberSeries','SaveBookingPolicy','DeleteBookingPolicy'];
  for(const n of names)routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors,bootGets=srv.gets().map(c=>c.path);

  test('[FDM-SYNC] Loader fills the four lists (ids = server ids, defaults negative, retired kept, server counts) and their history',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    for(const p of Object.keys(lists))assert.ok(bootGets.includes('/masters/'+p)&&bootGets.includes('/masters/'+p+'/history'),p);
    const r=st.mastersX.rows;
    assert.deepEqual(r.bookingSources.map(x=>[x.id,x.code,x.segment,x.channel,x.commissionBps,x.active]),[['1','WALKIN','DIRECT',undefined,undefined,true],['2','OTA-BOOKNEST','OTA','BookNest',1800,true],['3','PHONE','DIRECT',undefined,0,false]]);
    assert.deepEqual(r.idTypes[1],{id:'5',code:'VISA',name:'Passport + visa',sortOrder:50,active:true,accepted:true,needsPhoto:true,foreignerOnly:true});
    assert.deepEqual(r.numberSeries[0],{id:'-1',code:'INV',name:'Tax invoice',sortOrder:10,active:true,document:'INV',prefix:'INV',start:1,padding:7,reset:'FY',activeFrom:'2000-04-01',issued:12,nextNumber:'INV/2627/0000013'});
    assert.deepEqual(r.policies[0].sources,[]);assert.equal(rt.ids.version('bookingSource','2'),'V2');
    assert.ok(st.mastersX.history.some(h=>h.master==='bookingSources'&&h.field==='commissionBps'&&h.rowId==='2'));});
  test('[FDM-SYNC] The prototype selectors read the server rows: source choices, ID types, next number and issued count from the server, Default policy',()=>{
    assert.ok(M.fdmSourceChoices(st).includes('Walk-in'));assert.ok(!M.fdmSourceChoices(st).includes('Phone'));
    assert.deepEqual(M.fdmIdTypeChoices(st,'Indian'),['Aadhaar']);assert.equal(M.fdmPreview(st,'INV'),'INV/2627/0000013');
    assert.equal(M.fdmIssued(st,st.mastersX.rows.numberSeries[0]).count,12);assert.equal(M.fdmPolicyFor(st,{source:'Walk-in'}).code,'DEFAULT');});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const add=await go({type:'MST_MASTER_SAVE',master:'bookingSources',row:{code:' agent-tg ',name:'Travel Guru',segment:'AGENT',commissionBps:1000,sortOrder:40},key:'fdm:add'});
  const edit=await go({type:'MST_MASTER_SAVE',master:'bookingSources',row:{id:'2',code:'OTA-BOOKNEST',name:'OTA · BookNest',segment:'OTA',channel:'BookNest',commissionBps:2000,sortOrder:20}});
  const auto=await go({type:'MST_MASTER_SAVE',master:'bookingSources',row:{id:'bsrc-auto-corp-co-1',code:'CORP-CO-1',name:'Deccan Pharma',segment:'CORPORATE',channel:'Deccan Pharma',sortOrder:1010}});
  const virt=await go({type:'MST_MASTER_SAVE',master:'numberSeries',row:{id:'-1',code:'INV',name:'Tax invoice (hotel)',document:'INV',prefix:'inv',start:1,padding:7,reset:'FY',activeFrom:'2000-04-01',sortOrder:10}});
  const pol=await go({type:'MST_MASTER_SAVE',master:'policies',row:{code:'OTA-STD',name:'OTA standard',freeCancelHours:48,cancelChargeBps:10000,noShowChargeBps:10000,depositBps:2500,sources:['OTA-BOOKNEST']}});
  const idt=await go({type:'MST_MASTER_SAVE',master:'idTypes',row:{id:'4',code:'AADHAAR',name:'Aadhaar',accepted:false,needsPhoto:true,foreignerOnly:false,sortOrder:40}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'bookingSources',id:'1'});
  const retV=await go({type:'MST_MASTER_RETIRE',master:'numberSeries',id:'-2'});
  const del=await go({type:'MST_MASTER_DELETE',master:'policies',id:'-1'});
  const order=await go({type:'MST_MASTER_REORDER',master:'bookingSources',ids:['3','bsrc-auto-ota-stayhub','1','2']});
  test('[FDM-SYNC] Save maps every field: new source (no rowId), edit with rowId + version, a client-registered auto row is created',()=>{
    assert.ok(add.r.ok&&edit.r.ok&&auto.r.ok);
    assert.deepEqual([add.cmds[0].path,add.cmds[0].body,add.cmds[0].key],['/commands/SaveBookingSource',{code:'AGENT-TG',name:'Travel Guru',sortOrder:40,segment:'AGENT',channel:null,commissionBps:1000},'fdm:add']);
    assert.deepEqual(edit.cmds[0].body,{code:'OTA-BOOKNEST',name:'OTA · BookNest',sortOrder:20,segment:'OTA',channel:'BookNest',commissionBps:2000,rowId:2,expectedVersion:'V2'});
    assert.deepEqual(auto.cmds[0].body,{code:'CORP-CO-1',name:'Deccan Pharma',sortOrder:1010,segment:'CORPORATE',channel:'Deccan Pharma'});
    assert.ok(add.gets.includes('/masters/booking-sources'));});
  test('[FDM-SYNC] Numbering, policies and ID types: server field names; a default row (negative id) is sent as its rowId with an empty version',()=>{
    assert.deepEqual([virt.cmds[0].path,virt.cmds[0].body],['/commands/SaveNumberSeries',{code:'INV',name:'Tax invoice (hotel)',sortOrder:10,document:'INV',prefix:'INV',startNo:1,padding:7,reset:'FY',activeFrom:'2000-04-01',rowId:-1,expectedVersion:''}]);
    assert.deepEqual([pol.cmds[0].path,pol.cmds[0].body],['/commands/SaveBookingPolicy',{code:'OTA-STD',name:'OTA standard',freeCancelHours:48,cancelChargeBps:10000,noShowChargeBps:10000,depositBps:2500,sources:['OTA-BOOKNEST']}]);
    assert.deepEqual(idt.cmds[0].body,{code:'AADHAAR',name:'Aadhaar',sortOrder:40,accepted:false,needsPhoto:true,foreignerOnly:false,rowId:4,expectedVersion:'V4'});});
  test('[FDM-SYNC] Retire / delete send rowId (+ version); reorder sends the server rows only (client auto rows left out)',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetireBookingSource',{rowId:1,expectedVersion:'V1'}]);
    assert.deepEqual([retV.cmds[0].path,retV.cmds[0].body],['/commands/RetireNumberSeries',{rowId:-2,expectedVersion:''}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteBookingPolicy',{rowId:-1,expectedVersion:''}]);
    assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderBookingSources',{rowIds:[3,1,2]}]);
    assert.ok([ret,retV,del,order].every(x=>x.r.ok));});

  // Server master errors keep their reason; Reception loads the lists without any history read (D5 read gate).
  srv.on('POST /commands/SaveNumberSeries',()=>reply(422,{code:'VALIDATION',message:'INV has issued 12 numbers — numbering cannot change.',details:{reason:'MASTER_INVALID',field:'prefix'}}));
  const locked=await rt.run({type:'MST_MASTER_SAVE',master:'numberSeries',row:{id:'-1',code:'INV',name:'Tax invoice',document:'INV',prefix:'HCC',start:1,padding:7,reset:'FY',activeFrom:'2000-04-01'}});
  const rec=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A']]})});
  const rrt=await startRuntime(S,rec,mine);
  test('[FDM-SYNC] A locked series surfaces the server reason; Reception reads the four lists but never their history',()=>{
    assert.deepEqual([locked.ok,locked.reason],[false,'server']);assert.equal(M.mstReasonOf(locked.error),'MASTER_INVALID');assert.match(locked.text,/issued 12/);
    assert.deepEqual(rec.gets().map(c=>c.path).filter(p=>p.startsWith('/masters/')&&p!=='/masters/floors').sort(),['/masters/booking-sources','/masters/id-types','/masters/number-series','/masters/policies']);
    assert.equal(rrt.get().state.mastersX.rows.idTypes.length,2);assert.deepEqual(rrt.get().errors,{});});

  // Every page renders on the server data and on a prototype state with changed lists.
  let proto=M.execute(M.seed(),'owner',{type:'MST_MASTER_SAVE',master:'policies',row:{code:'OTA-STD',name:'OTA standard',freeCancelHours:48,cancelChargeBps:10000,noShowChargeBps:0,depositBps:2500,sources:['OTA']}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_RETIRE',master:'bookingSources',id:'bsrc-whatsapp'});
  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(proto,'owner',false),P.renderPages(proto,'reception',true),P.renderPages(proto,'housekeeper',true)];
  test('[FDM-SYNC] Every desktop and phone page renders on the server lists and on changed prototype lists',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
