// W4-9 maintenance + linen & stock: the loaders fill s.maintenanceX / s.inventoryX from server DTOs (built here from the prototype
// BASE fixture, so the figures are the prototype's own) and every mapped command sends the right route / body / key / expectedVersion.
import {loadSync,loadPages,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

const ST={'On hold':'OnHold','In progress':'InProgress'};
// fixture clock "YYYY-MM-DDTHH:mm" (hotel local, IST) → the UTC instant the server would send
const iso=c=>c?new Date(Date.parse(c+':00Z')-330*60000).toISOString():null;

function dtos(M){
  const s=M.seed(),x=s.maintenanceX,v=s.inventoryX;
  const aId=Object.fromEntries(x.assets.map((a,i)=>[a.id,i+1])),vId=Object.fromEntries(x.vendors.map((a,i)=>[a.id,i+1])),spId=Object.fromEntries(x.spares.map((a,i)=>[a.id,i+1]));
  const uId={maint:5,supervisor:4,housekeeper:6,owner:1,manager:2};const user=u=>uId[u]??9;
  const woNum=w=>Number(w.id.slice(3));
  const roomIds=Object.fromEntries(s.rooms.map((r,i)=>[r.id,100+i]));
  const orders=x.orders.map(w=>({workOrderId:woNum(w),no:w.id,kind:w.kind,title:w.title,priority:w.priority,status:ST[w.status]??w.status,roomId:w.room?roomIds[w.room]:null,roomNo:w.room??null,
    assetId:w.assetId?aId[w.assetId]:null,area:w.area??null,assigneeId:w.assignee?user(w.assignee):null,vendorId:w.vendorId?vId[w.vendorId]:null,createdUtc:iso(w.createdAt),createdBy:user(w.createdBy),
    startedUtc:iso(w.startedAt),doneUtc:iso(w.doneAt),verifiedUtc:iso(w.verifiedAt),holdReason:w.holdReason??null,awaitingSpareId:w.awaitingSpare?spId[w.awaitingSpare]:null,note:w.note??null,
    returnReason:w.returnReason??null,cancelReason:w.cancelReason??null,repairTaskId:null,planId:null,dueDate:w.dueDate??null,downtimeFromUtc:iso(w.downtimeFrom),downtimeToUtc:iso(w.downtimeTo),
    parts:w.parts.map(p=>({spareId:spId[p.spareId],spareName:'x',qty:p.qty,unitCostPaise:p.unitCost})),version:'v'+woNum(w),doneBy:w.doneBy?user(w.doneBy):null,verifiedBy:w.verifiedBy?user(w.verifiedBy):null}));
  const registers={assets:x.assets.map(a=>({assetId:aId[a.id],name:a.name,kind:a.kind,roomId:a.room?roomIds[a.room]:null,roomNo:a.room??null,area:a.area??null,make:a.make,model:a.model,installedOn:a.installedOn||null,
      vendorId:a.vendorId?vId[a.vendorId]:null,status:a.status,version:'a'+aId[a.id]})),
    vendors:x.vendors.map(a=>({vendorId:vId[a.id],name:a.name,trade:a.trade,phone:a.phone,version:'vn'+vId[a.id]})),
    spares:x.spares.map(a=>({spareId:spId[a.id],name:a.name,unit:a.unit,stock:a.stock,reorderAt:a.reorderAt,costPaise:a.cost,version:'s'+spId[a.id]})),
    plans:x.plans.map((p,i)=>({planId:i+1,assetId:aId[p.assetId],title:p.title,everyDays:p.everyDays,nextDue:p.nextDue,active:p.active,lastDone:p.lastDone??null,version:'p'+(i+1)}))};
  const amcs={amcs:x.amcs.map((a,i)=>({amcId:i+1,vendorId:vId[a.vendorId],scope:a.scope,from:a.from,to:a.to,visits:a.visits,valuePaise:a.value,version:'m'+(i+1)}))};
  const LOCS=['store','floor1','floor2','floor3','rooms','soiled','laundry'];
  const items=v.items.map(i=>({code:i.id,name:i.name,kind:i.kind,unit:i.unit,minQty:i.min,reorderQty:i.reorderQty,stdX100:i.stdX100??null,par:i.par??{},levels:LOCS.map(l=>({loc:l,qty:i.stock[l]??0})),
    supplier:i.supplier??null,unitCostPaise:i.cost,laundryRatePaise:i.laundryRate??null,version:'i-'+i.id}));
  const batches=v.batches.map((b,i)=>({batchId:i+1,vendor:b.vendor,sentDate:b.sent,status:b.status,returnedDate:b.returned??null,note:b.note??null,chargePaise:b.charge??null,version:'b'+(i+1),
    lines:b.lines.map(l=>({itemCode:l.item,sent:l.sent,received:l.received??null,damaged:l.damaged??null}))}));
  return {orders,registers,amcs,items,batches,roomIds};
}
const page=(rows,q)=>{const p=Number(q.page||1),n=Number(q.pageSize||50);return {page:p,pageSize:n,total:rows.length,rows:rows.slice((p-1)*n,p*n)};};

export async function register({test,assert,M}){
  const S=await loadSync();const D=dtos(M);const seed=M.seed();
  const history=[{at:'2026-10-22T13:40:00.000Z',by:4,action:'Created',note:null}];
  const srv=fakeServer({
    'GET /maintenance/registers':()=>D.registers,'GET /maintenance/amcs':()=>D.amcs,
    'GET /maintenance/work-orders':({query})=>page(D.orders,query),
    'GET /maintenance/work-orders/:id':({params})=>({order:D.orders.find(o=>o.workOrderId===Number(params.id)),history}),
    'GET /stock/items':({query})=>({...page(D.items,query),parSets:3}),'GET /stock/laundry':({query})=>page(D.batches,query),
    'GET /stock/counts':({query})=>page([{countId:7,businessDate:'2026-10-24',loc:'store',status:'Submitted',actorId:6,reviewerId:null,reason:null,note:null,version:'c7',lines:[{itemCode:'LIN-BS',system:40,counted:38}]}],query),
    'GET /stock/losses':({query})=>page([{lossId:3,businessDate:'2026-10-24',itemCode:'LIN-BS',loc:'store',qty:2,reason:'Damaged',note:null,actorId:6,valuePaise:130000,ref:null}],query),
    'GET /stock/moves':({query})=>page([{moveId:9,businessDate:'2026-10-24',atUtc:'2026-10-24T04:30:00.000Z',kind:'grn',itemCode:'AMN-SP',qty:50,from:'Morning Supplies',to:'store',ref:'GRN-4',reason:null,actorId:4},
      {moveId:8,businessDate:'2026-10-23',atUtc:'2026-10-23T04:30:00.000Z',kind:'use',itemCode:'AMN-SP',qty:6,from:'floor1',to:'used',ref:null,reason:null,actorId:6}],query),
    'GET /stock/settings':()=>({parSets:3,vendorName:'Fresh Linen Services',turnaroundDays:1}),
    'POST /commands/:n':()=>({ok:true}),
  });
  const rt=await startRuntime(S,srv);const st=rt.get().state;const mx=st.maintenanceX,ix=st.inventoryX;
  const detailGets=srv.calls.filter(c=>/^\/maintenance\/work-orders\/\d+$/.test(c.path)).length;
  srv.reset();await rt.refresh();const refetched=srv.calls.filter(c=>/^\/maintenance\/work-orders\/\d+$/.test(c.path)).length;

  test('[W4-9] Maintenance slice = the server registers + work orders in the prototype shape (same figures as the BASE fixture)',()=>{
    const p=seed.maintenanceX;assert.ok(p.orders.length>=4,'fixture has work orders');
    assert.deepEqual([mx.assets.length,mx.vendors.length,mx.amcs.length,mx.spares.length,mx.plans.length,mx.orders.length],[p.assets.length,p.vendors.length,p.amcs.length,p.spares.length,p.plans.length,p.orders.length]);
    const sig=w=>[w.id,w.status,w.priority,w.title,w.room,w.kind,w.createdAt,w.startedAt,w.doneAt];
    assert.deepEqual(mx.orders.map(sig),p.orders.map(sig).sort((a,b)=>a[0]<b[0]?-1:1),'ids, statuses, clock times (UTC → hotel local) match');
    assert.deepEqual(M.mntLowSpares({...seed,maintenanceX:mx}).map(x=>x.name),M.mntLowSpares(seed).map(x=>x.name));
    assert.deepEqual(mx.amcs.map(a=>a.value),p.amcs.map(a=>a.value));
    assert.equal(mx.orders[0].history[0].action,'Created');
  });
  test('[W4-9] Work-order history is read once per version (detail GET only for new / changed orders)',()=>{assert.equal(detailGets,mx.orders.length);assert.equal(refetched,0);});
  test('[W4-9] Stock slice reproduces the prototype figures: items, stock by location, alerts, laundry batches, par coverage',()=>{
    const p=seed.inventoryX,full={...seed,inventoryX:ix};
    assert.equal(ix.items.length,p.items.length);
    for(const it of p.items){const l=ix.items.find(x=>x.id===it.id);assert.deepEqual([l.name,l.kind,l.min,l.reorderQty,l.cost],[it.name,it.kind,it.min,it.reorderQty,it.cost],it.id);
      for(const k of Object.keys(it.stock))assert.equal(l.stock[k]??0,it.stock[k]??0,it.id+' '+k);}
    assert.deepEqual(M.invAlerts(full).map(a=>[a.item.id,a.clean,a.suggest]),M.invAlerts(seed).map(a=>[a.item.id,a.clean,a.suggest]));
    assert.equal(M.invPiecesOut(full),M.invPiecesOut(seed));assert.equal(ix.batches.length,p.batches.length);
    assert.equal(M.invParCoverage(full,ix.items[0]),M.invParCoverage(seed,p.items[0]));
    assert.deepEqual([ix.parSets,ix.vendor.name],[3,'Fresh Linen Services']);
    assert.deepEqual(ix.counts.map(c=>[c.id,c.status,c.loc]),[['CNT-7','Submitted','store']]);assert.equal(ix.losses[0].id,'LOSS-3');
    assert.deepEqual(ix.moves.map(m=>m.id),['IMV-8','IMV-9'],'oldest first like the prototype');
    assert.deepEqual(ix.grns.map(g=>[g.id,g.supplier,g.lines[0].item,g.lines[0].qty]),[['GRN-4','Morning Supplies','AMN-SP',50]]);
  });

  // ── commands ──
  const run=async c=>{srv.reset();const r=await rt.run(c);return {r,calls:srv.calls.map(x=>`${x.method} ${x.path}`),c0:srv.calls.find(x=>x.method==='POST')};};
  const cases=[
    ['MNT_WO_START',{type:'MNT_WO_START',id:'WO-0901',key:'k-start'},'StartWorkOrder',{workOrderId:901,expectedVersion:'v901'},'k-start'],
    ['MNT_WO_HOLD',{type:'MNT_WO_HOLD',id:'WO-0901',reason:'Waiting for part',spareId:'3',key:'k-hold'},'HoldWorkOrder',{workOrderId:901,reason:'Waiting for part',spareId:3,expectedVersion:'v901'},'k-hold'],
    ['MNT_WO_PART',{type:'MNT_WO_PART',id:'WO-0901',spareId:'1',qty:2,key:'k-part'},'AddWorkOrderPart',{workOrderId:901,spareId:1,qty:2,expectedVersion:'v901'},'k-part'],
    ['MNT_WO_COMPLETE',{type:'MNT_WO_COMPLETE',id:'WO-0901',note:'Capacitor replaced',key:'k-done'},'CompleteWorkOrder',{workOrderId:901,note:'Capacitor replaced',expectedVersion:'v901'},'k-done'],
    ['MNT_WO_VERIFY',{type:'MNT_WO_VERIFY',id:'WO-0901',approve:true,unblock:true,key:'k-ver'},'VerifyWorkOrder',{workOrderId:901,approve:true,unblock:true,expectedVersion:'v901'},'k-ver'],
    ['MNT_WO_VERIFY return',{type:'MNT_WO_VERIFY',id:'WO-0901',approve:false,reason:'Still warm',key:'k-ret'},'VerifyWorkOrder',{workOrderId:901,approve:false,reason:'Still warm',unblock:false,expectedVersion:'v901'},'k-ret'],
    ['MNT_WO_CANCEL',{type:'MNT_WO_CANCEL',id:'WO-0901',reason:'Duplicate',key:'k-can'},'CancelWorkOrder',{workOrderId:901,reason:'Duplicate',expectedVersion:'v901'},'k-can'],
    ['MNT_WO_BLOCK',{type:'MNT_WO_BLOCK',id:'WO-0901',to:'2026-10-30',key:'k-blk'},'BlockWorkOrderRoom',{workOrderId:901,to:'2026-10-30',expectedVersion:'v901'},'k-blk'],
    ['MNT_WO_ASSIGN tech',{type:'MNT_WO_ASSIGN',id:'WO-0901',assignee:'5',key:'k-as'},'AssignWorkOrder',{workOrderId:901,technicianId:5,expectedVersion:'v901'},'k-as'],
    ['MNT_WO_ASSIGN vendor',{type:'MNT_WO_ASSIGN',id:'WO-0901',vendorId:'2',key:'k-asv'},'AssignWorkOrder',{workOrderId:901,vendorId:2,expectedVersion:'v901'},'k-asv'],
    ['MNT_WO_CREATE',{type:'MNT_WO_CREATE',title:'Tap leaking',priority:'P3',room:'205',notes:'bathroom',assignee:'5',key:'k-new'},'CreateWorkOrder',{title:'Tap leaking',priority:'P3',roomId:D.roomIds['205'],notes:'bathroom',assigneeId:5},'k-new'],
    ['MNT_WO_FROM_REPAIR',{type:'MNT_WO_FROM_REPAIR',taskId:'31',priority:'P2',key:'k-rep'},'ConvertRepairToWorkOrder',{taskId:31,priority:'P2'},'k-rep'],
    ['MNT_PM_GENERATE',{type:'MNT_PM_GENERATE',key:'pm:1'},'GeneratePmWorkOrders',{},'pm:1'],
    ['MNT_PLAN_SAVE edit',{type:'MNT_PLAN_SAVE',id:'2',assetId:'3',title:'Pump run test',everyDays:7,nextDue:'2026-10-30',active:true,key:'k-pl'},'SavePmPlan',{planId:2,assetId:3,title:'Pump run test',everyDays:7,nextDue:'2026-10-30',active:true,expectedVersion:'p2'},'k-pl'],
    ['MNT_ASSET_SAVE new',{type:'MNT_ASSET_SAVE',name:'Pantry fridge',kind:'Other',area:'Kitchen',make:'',model:'',vendorId:'1',key:'k-as1'},'SaveMaintenanceAsset',{name:'Pantry fridge',kind:'Other',area:'Kitchen',vendorId:1,status:'Working'},'k-as1'],
    ['MNT_VENDOR_SAVE edit',{type:'MNT_VENDOR_SAVE',id:'1',name:'Sheetal Cooling',trade:'AC',phone:'+91 90000 11201',key:'k-v'},'SaveMaintenanceVendor',{vendorId:1,name:'Sheetal Cooling',trade:'AC',phone:'+91 90000 11201',expectedVersion:'vn1'},'k-v'],
    ['MNT_AMC_SAVE edit',{type:'MNT_AMC_SAVE',id:'1',vendorId:'2',scope:'Lift service',from:'2026-04-01',to:'2027-03-31',visits:12,value:5000000,key:'k-amc'},'SaveAmc',{amcId:1,vendorId:2,scope:'Lift service',from:'2026-04-01',to:'2027-03-31',visits:12,valuePaise:5000000,expectedVersion:'m1'},'k-amc'],
    ['MNT_SPARE_RECEIVE',{type:'MNT_SPARE_RECEIVE',spareId:'3',qty:5,key:'k-sp'},'ReceiveSpare',{spareId:3,qty:5,expectedVersion:'s3'},'k-sp'],
    ['INV_GRN',{type:'INV_GRN',supplier:'Morning Supplies',ref:'MS/1',lines:[{item:'AMN-SH',qty:100,cost:1300},{item:'AMN-SP',qty:20}],key:'k-grn'},'StockReceiveGrn',{supplier:'Morning Supplies',ref:'MS/1',lines:[{itemCode:'AMN-SH',qty:100,unitCostPaise:1300},{itemCode:'AMN-SP',qty:20}]},'k-grn'],
    ['INV_ISSUE',{type:'INV_ISSUE',item:'LIN-BS',qty:4,from:'store',to:'floor2',key:'k-is'},'StockIssue',{itemCode:'LIN-BS',qty:4,from:'store',to:'floor2'},'k-is'],
    ['INV_USE',{type:'INV_USE',item:'AMN-SP',qty:3,floor:'floor1',room:'101',key:'k-use'},'StockUse',{itemCode:'AMN-SP',qty:3,floor:'floor1',room:'101'},'k-use'],
    ['INV_ROOM_CHANGE',{type:'INV_ROOM_CHANGE',item:'LIN-BS',qty:2,floor:'floor1',key:'k-rc'},'StockRoomChange',{itemCode:'LIN-BS',qty:2,floor:'floor1'},'k-rc'],
    ['INV_LAUNDRY_SEND',{type:'INV_LAUNDRY_SEND',lines:[{item:'LIN-BS',qty:5}],note:'',key:'k-ls'},'StockLaundrySend',{lines:[{itemCode:'LIN-BS',qty:5}]},'k-ls'],
    ['INV_LOSS',{type:'INV_LOSS',item:'LIN-BS',loc:'store',qty:1,reason:'Damaged',note:'',key:'k-loss'},'StockRecordLoss',{itemCode:'LIN-BS',qty:1,loc:'store',reason:'Damaged'},'k-loss'],
    ['INV_COUNT_SUBMIT',{type:'INV_COUNT_SUBMIT',loc:'store',counts:{'LIN-BS':40},key:'k-cs'},'StockCountSubmit',{loc:'store',counts:{'LIN-BS':40}},'k-cs'],
    ['INV_COUNT_POST',{type:'INV_COUNT_POST',id:'CNT-7',approve:true,reason:'Recount agreed',key:'k-cp'},'StockCountDecide',{countId:7,approve:true,reason:'Recount agreed',expectedVersion:'c7'},'k-cp'],
    ['INV_ITEM_SET',{type:'INV_ITEM_SET',id:'LIN-BS',min:50,reorderQty:40,stdX100:150,par:{Classic:2,Deluxe:2,Suite:2},key:'k-set'},'StockItemSet',{code:'LIN-BS',minQty:50,reorderQty:40,stdX100:150,par:{Classic:2,Deluxe:2,Suite:2},expectedVersion:'i-LIN-BS'},'k-set'],
  ];
  const got=[];for(const c of cases)got.push([c,await run(c[1])]);
  const out=ix.batches.find(b=>b.status==='Out');
  const recv=await run({type:'INV_LAUNDRY_RECEIVE',id:out.id,lines:out.lines.map(l=>({item:l.item,received:l.sent-1,damaged:1})),note:'one torn',key:'k-lr'});
  test('[W4-9] Every mapped command sends its server command with the right body, Idempotency-Key and expectedVersion',()=>{
    for(const [c,g] of got){assert.ok(g.r.ok,c[0]+': '+g.r.text);assert.equal(g.c0.path,'/commands/'+c[2],c[0]);assert.deepEqual(g.c0.body,c[3],c[0]);assert.equal(g.c0.key,c[4],c[0]);}
    assert.ok(recv.r.ok,recv.r.text);assert.equal(recv.c0.path,'/commands/StockLaundryReceive');
    assert.equal(recv.c0.body.batchId,Number(out.id.slice(3)));assert.match(recv.c0.body.expectedVersion,/^b\d+$/);assert.equal(recv.c0.body.lines[0].damaged,1);assert.equal(recv.c0.key,'k-lr');
  });
  test('[W4-9] After a command the owning slice is reloaded (maintenance registers + work orders; stock items + moves)',()=>{
    const one=got.find(g=>g[0][0]==='MNT_WO_START')[1].calls,inv=got.find(g=>g[0][0]==='INV_ISSUE')[1].calls;
    assert.ok(one.includes('GET /maintenance/registers')&&one.includes('GET /maintenance/work-orders'),one.join());
    assert.ok(inv.includes('GET /stock/items')&&inv.includes('GET /stock/moves'),inv.join());
    assert.ok(!inv.includes('GET /maintenance/registers'));
  });

  // ── prechecks ──
  const refused=[];for(const c of [{type:'MNT_WO_CREATE',title:'x',priority:'P2',room:'205'},{type:'MNT_WO_CREATE',title:'Valid title',priority:'P2'},{type:'MNT_WO_HOLD',id:'WO-0901',reason:''},
    {type:'INV_LOSS',item:'LIN-BS',loc:'store',qty:1,reason:'Other',note:''},{type:'MNT_AMC_SAVE',vendorId:'1',scope:'ok scope',from:'2026-05-01',to:'2026-04-01',visits:1,value:100},
    {type:'MNT_WO_START',id:'WO-9999'}]){srv.reset();const r=await rt.run(c);refused.push([r.ok,r.reason,srv.calls.filter(x=>x.method==='POST').length]);}
  test('[W4-9] Prechecks refuse bad input and unknown records without a command request',()=>{
    assert.deepEqual(refused.slice(0,5),Array(5).fill([false,'precheck',0]));
    assert.deepEqual([refused[5][0],refused[5][2]],[false,0],'unknown work order is refused before any request');
  });
  test('[W4-9] Every prototype MNT_/INV_ command is mapped (nothing left in unmapped); registry validates',()=>{
    const a=S.AREAS.find(x=>x.id==='maintenance');assert.deepEqual(Object.keys(a.unmapped??{}),[]);
    assert.deepEqual(a.commands.map(c=>c.type).sort(),[...new Set(cases.map(c=>c[1].type)),'INV_LAUNDRY_RECEIVE'].sort());
    assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });

  // ── masking (DEC-063 G12): a Maintenance technician gets no money, no vendors / plans / AMCs ──
  const tech=fakeServer({
    'GET /me/capabilities':()=>capabilities({userId:5,roles:['MNT'],grants:[['PERM-HK-001','A']]}),
    'GET /maintenance/registers':()=>({assets:D.registers.assets,vendors:[],plans:[],spares:D.registers.spares.map(s=>({...s,costPaise:null}))}),
    'GET /maintenance/work-orders':({query})=>page(D.orders.filter(o=>o.assigneeId===5).map(o=>({...o,parts:o.parts.map(p=>({...p,unitCostPaise:null}))})),query),
    'GET /maintenance/work-orders/:id':()=>({history:[]}),
    'GET /stock/items':({query})=>({...page(D.items.map(i=>({...i,unitCostPaise:null,laundryRatePaise:null})),query),parSets:3}),
    'GET /stock/laundry':({query})=>page([],query),'GET /stock/counts':({query})=>page([],query),'GET /stock/losses':({query})=>page([],query),'GET /stock/moves':({query})=>page([],query),
    'GET /stock/settings':()=>({parSets:3,vendorName:null,turnaroundDays:1}),
  });
  const trt=await startRuntime(S,tech);const ts=trt.get().state;
  test('[W4-9] Technician: own work orders only, no part prices / spare costs / stock costs; vendors, plans and AMCs stay empty and are never requested',()=>{
    assert.ok(ts.maintenanceX.orders.length>0&&ts.maintenanceX.orders.every(w=>w.assignee==='5'));
    assert.ok(ts.maintenanceX.orders.every(w=>w.parts.every(p=>p.unitCost===undefined)),'no unit cost on parts');
    assert.ok(ts.maintenanceX.spares.every(s=>s.cost===0));assert.deepEqual([ts.maintenanceX.vendors,ts.maintenanceX.plans,ts.maintenanceX.amcs],[[],[],[]]);
    assert.equal(tech.gets('/maintenance/amcs').length,0);
    assert.ok(ts.inventoryX.items.every(i=>i.cost===0&&i.laundryRate===undefined));assert.equal(ts.inventoryX.vendor,undefined);
  });

  // ── pages render on server data (desktop + phone) ──
  const P=await loadPages();const shown=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true),P.renderPages(ts,'5',true)];
  test('[W4-9] Every desktop and phone page renders on the loaded maintenance and stock data',()=>{assert.deepEqual(shown.map(x=>x.errors),[{},{},{}]);});

  const rec=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A']]})});
  const rrt=await startRuntime(S,rec);
  test('[W4-9] A user without housekeeping access never requests maintenance or stock',()=>{
    assert.equal(rec.calls.filter(c=>c.path.startsWith('/maintenance')||c.path.startsWith('/stock')).length,0);assert.deepEqual(rrt.get().state.maintenanceX.orders,[]);
  });
}
