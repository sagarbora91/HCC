// MA-2 · Catalogue area of the production sync layer (src/clients/sync/catalogue.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Catalogue/CatalogueContracts.cs). Names prefixed [CAT-SYNC].
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  // core + masters + the catalogue's mastersX patch (the bookings patch and folio commands need the reservations / money areas: driven directly below).
  const cat=S.AREAS.find(a=>a.id==='catalogue');
  const mine={areas:[...S.AREAS.filter(a=>['core','masters'].includes(a.id)),{...cat,loaders:cat.loaders.filter(l=>l.slice==='mastersX'),commands:[]}]};
  const meta=(id,o={})=>({sortOrder:id*10,active:true,retiredOn:null,usageCount:0,updatedUtc:'2026-10-24T03:00:00.000Z',version:'V'+id,...o});
  const plans=[{mealPlanId:1,code:'EP',name:'Room only (EP)',meals:[],gstTreatment:'separate',roomTypes:[],addOn:[{effectiveFrom:'2025-04-01',amountPaise:0}],...meta(1)},
    {mealPlanId:2,code:'CP',name:'Breakfast included (CP)',meals:['B'],gstTreatment:'bundled',roomTypes:['Deluxe'],addOn:[{effectiveFrom:'2025-04-01',amountPaise:30000}],...meta(2,{usageCount:3})}];
  const extras=[{extraItemId:11,code:'LAUNDRY',name:'Laundry',category:'laundry',gstCategory:'laundry',unit:'bag',department:'laundry',prices:[{effectiveFrom:'2025-04-01',amountPaise:50000}],...meta(11)},
    {extraItemId:12,code:'XBED',name:'Extra bed',category:'room',gstCategory:'other',unit:'night',department:null,prices:[],...meta(12,{active:false,retiredOn:'2026-10-20'})}];
  const reasons=[{discountReasonId:21,code:'SERVICE',name:'Service recovery',maxBps:2500,approvalAboveBps:1000,roles:[],...meta(21)},
    {discountReasonId:22,code:'MGMT',name:'Management courtesy',maxBps:10000,approvalAboveBps:null,roles:['OWN','MGR'],...meta(22)}];
  const hist=[{historyId:9,rowId:11,code:'LAUNDRY',field:'price',oldValue:'₹450 from 2025-04-01',newValue:'₹500 from 2025-04-01',changedBy:1,changedByName:'owner',changedUtc:'2026-10-24T03:30:00.000Z'}];
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const routes={'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([]),
    'GET /catalogue/meal-plans':()=>page(plans),'GET /catalogue/extras':()=>page(extras),'GET /catalogue/discount-reasons':()=>page(reasons),
    'GET /catalogue/meal-plans/history':()=>page([]),'GET /catalogue/extras/history':()=>page(hist),'GET /catalogue/discount-reasons/history':()=>page([])};
  for(const n of ['SaveExtraItem','RetireExtraItem','ReorderExtraItems','SaveDiscountReason','SaveMealPlan','DeleteMealPlan'])routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors;

  test('[CAT-SYNC] Loader fills mastersX with the three lists (ids = server ids, dated money, roles, retired kept), history and versions',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    assert.deepEqual(st.mastersX.rows.mealPlans[1],{id:'2',code:'CP',name:'Breakfast included (CP)',sortOrder:20,active:true,meals:['B'],addOn:[{amount:30000,effectiveFrom:'2025-04-01'}],gst:'bundled',roomTypes:['Deluxe']});
    assert.deepEqual(st.mastersX.rows.extras[1],{id:'12',code:'XBED',name:'Extra bed',sortOrder:120,active:false,retiredOn:'2026-10-20',category:'room',price:[],gst:'other',unit:'night'});
    assert.deepEqual(st.mastersX.rows.discountReasons.map(r=>[r.code,r.maxPct,r.approvalAbovePct,r.roles]),[['SERVICE',2500,1000,[]],['MGMT',10000,undefined,['Owner','Manager']]]);
    assert.deepEqual(st.mastersX.history.map(h=>[h.master,h.rowId,h.field,h.new,h.at]),[['extras','11','price','₹500 from 2025-04-01','2026-10-24T09:00']]);
    assert.equal(rt.ids.version('extraItem','11'),'V11');assert.equal(rt.ids.version('mealPlan','2'),'V2');});
  test('[CAT-SYNC] Prototype selectors read the server rows: active extras, prefill, reasons per role, plans per room type',()=>{
    assert.deepEqual(M.catExtras(st,true).map(x=>x.code),['LAUNDRY']);
    assert.deepEqual(M.catChargePrefill(st,M.catExtra(st,'11'),2),{label:'Laundry × 2',amount:100000,gst:'laundry',price:50000});
    assert.deepEqual(M.catReasonsFor(st,'Reception').map(r=>r.code),['SERVICE']);
    assert.deepEqual(M.catPlanOptions(st,'Classic').map(o=>o.value),['EP']);});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const add=await go({type:'MST_MASTER_SAVE',master:'extras',row:{code:' iron ',name:'Ironing',category:'laundry',gst:'laundry',unit:'piece',price:[{amount:10000,effectiveFrom:'2026-10-24'}]},key:'cat:add'});
  const edit=await go({type:'MST_MASTER_SAVE',master:'extras',row:{id:'11',code:'LAUNDRY',name:'Laundry',category:'laundry',gst:'laundry',unit:'bag',department:'laundry',sortOrder:110,
    price:[{amount:50000,effectiveFrom:'2025-04-01'},{amount:55000,effectiveFrom:'2026-11-01'}]}});
  const reason=await go({type:'MST_MASTER_SAVE',master:'discountReasons',row:{id:'22',code:'MGMT',name:'Management courtesy',maxPct:5000,roles:['Owner','Reception']}});
  const plan=await go({type:'MST_MASTER_SAVE',master:'mealPlans',row:{code:'HB',name:'Half board',meals:['B','D'],gst:'separate',addOn:[{amount:70000,effectiveFrom:'2026-10-24'}]}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'extras',id:'11'});
  const del=await go({type:'MST_MASTER_DELETE',master:'mealPlans',id:'1'});
  const order=await go({type:'MST_MASTER_REORDER',master:'extras',ids:['12','11']});
  test('[CAT-SYNC] MST_MASTER_SAVE of the catalogue lists → SaveExtraItem / SaveDiscountReason / SaveMealPlan with the full fields',()=>{
    assert.ok(add.r.ok);assert.deepEqual([add.cmds[0].path,add.cmds[0].key,add.cmds[0].body],['/commands/SaveExtraItem','cat:add',
      {code:'IRON',name:'Ironing',category:'laundry',gstCategory:'laundry',unit:'piece',department:null,prices:[{effectiveFrom:'2026-10-24',amountPaise:10000}]}]);
    assert.deepEqual(edit.cmds[0].body,{code:'LAUNDRY',name:'Laundry',category:'laundry',gstCategory:'laundry',unit:'bag',department:'laundry',
      prices:[{effectiveFrom:'2025-04-01',amountPaise:50000},{effectiveFrom:'2026-11-01',amountPaise:55000}],sortOrder:110,extraItemId:11,expectedVersion:'V11'});
    assert.deepEqual(reason.cmds[0].body,{code:'MGMT',name:'Management courtesy',maxBps:5000,approvalAboveBps:null,roles:['OWN','REC'],discountReasonId:22,expectedVersion:'V22'});
    assert.deepEqual(plan.cmds[0].body,{code:'HB',name:'Half board',meals:['B','D'],gstTreatment:'separate',roomTypes:[],addOn:[{effectiveFrom:'2026-10-24',amountPaise:70000}]});
    for(const x of [add,edit,reason,plan])assert.ok(x.r.ok&&x.gets.includes('/catalogue/extras'),'reloads mastersX');});
  test('[CAT-SYNC] Retire / delete / reorder → RetireExtraItem / DeleteMealPlan / ReorderExtraItems with ids and versions',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetireExtraItem',{extraItemId:11,expectedVersion:'V11'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteMealPlan',{mealPlanId:1,expectedVersion:'V1'}]);
    assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderExtraItems',{extraItemIds:[12,11]}]);
    const area=S.AREAS.find(a=>a.id==='catalogue');
    assert.deepEqual(area.commands.map(c=>c.type).sort(),['CAT_CHARGE','CAT_DISCOUNT','CAT_PLAN_SET']);
    assert.deepEqual(area.loaders.map(l=>[l.slice,l.kind]),[['mastersX','patch'],['bookings','patch']]);});

  // Read gates: Reception gets the lists without the history; Housekeeping gets empty lists (never the demo seed) and sends no catalogue read.
  const rec=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A'],['PERM-FOL-001','A'],['PERM-FOL-002','A']]})});
  const rrt=await startRuntime(S,rec,mine);
  const hk=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const hrt=await startRuntime(S,hk,mine);
  test('[CAT-SYNC] Reception loads the lists but no history; Housekeeping sends no catalogue read and sees empty lists, not the seed',()=>{
    assert.deepEqual(rec.gets().map(c=>c.path).filter(p=>p.startsWith('/catalogue')),['/catalogue/meal-plans','/catalogue/extras','/catalogue/discount-reasons']);
    assert.equal(rrt.get().state.mastersX.rows.extras.length,2);assert.deepEqual(rrt.get().errors,{});
    assert.deepEqual(hk.gets().map(c=>c.path).filter(p=>p.startsWith('/catalogue')),[]);
    assert.deepEqual(hrt.get().state.mastersX.rows.extras,[]);assert.deepEqual(M.catExtras(hrt.get().state),[]);});

  // Folio commands and the bookings patch, driven directly (bookings and folio ids come from the reservations / money areas).
  const area=S.AREAS.find(a=>a.id==='catalogue'),mapper=t=>area.commands.find(c=>c.type===t);
  const b={id:'501',guest:'Server guest',people:2,room:'105',arrival:'2026-10-24',departure:'2026-10-26',rate:200000,status:'In house',lines:[
    {id:'9001',label:'Room night · 2026-10-24',amount:200000,date:'2026-10-24',kind:'room',taxBps:500},{id:'9002',label:'Laundry',amount:50000,date:'2026-10-24',kind:'service',taxBps:1800},
    {id:'9003',label:'Meal plan CP',amount:60000,date:'2026-10-24',kind:'service',taxBps:500},{id:'9004',label:'Discount · Service recovery',amount:-10000,date:'2026-10-24',kind:'discount',taxBps:500}],receipts:[]};
  const sent=[];
  const ctx=cmd=>({cmd,state:{...st,bookings:[b]},key:'k',ids:{server:(k,id)=>k==='folio'&&id==='501'?77:undefined,version:(k,id)=>k==='folio'&&id==='501'?'FV':undefined},
    api:{command:async(name,body,opts)=>{sent.push({name,body,opts});return {};}}});
  await mapper('CAT_CHARGE').send(ctx({type:'CAT_CHARGE',id:'501',extraId:'11',qty:2}));
  await mapper('CAT_DISCOUNT').send(ctx({type:'CAT_DISCOUNT',id:'501',amount:15000,reasonCode:'service',note:' Late towels '}));
  await mapper('CAT_PLAN_SET').send(ctx({type:'CAT_PLAN_SET',id:'501',plan:'cp'}));
  await mapper('CAT_PLAN_SET').send(ctx({type:'CAT_PLAN_SET',id:'501',plan:''}));
  const links={plans:[{reservationId:501,mealPlanCode:'CP'}],charges:[{chargeId:9002,extraItemId:11,mealPlanCode:null,discountReasonCode:null},
    {chargeId:9003,extraItemId:null,mealPlanCode:'CP',discountReasonCode:null},{chargeId:9004,extraItemId:null,mealPlanCode:null,discountReasonCode:'SERVICE'}]};
  const patched=await area.loaders[1].load({current:()=>[b,{...b,id:'502',lines:[]}],mayRead:()=>true,api:{get:async p=>{assert.equal(p,'/catalogue/links');return links;}}});
  test('[CAT-SYNC] CAT_CHARGE → PostExtra, CAT_DISCOUNT → RequestReasonedDiscount (folio version), CAT_PLAN_SET → SetReservationMealPlan',()=>{
    assert.deepEqual(sent.map(x=>[x.name,x.body,x.opts?.expectedVersion]),[
      ['PostExtra',{folioId:77,extraItemId:11,quantity:2,label:null,amountPaise:null,gstCategory:null},undefined],
      ['RequestReasonedDiscount',{folioId:77,amountPaise:15000,reasonCode:'SERVICE',note:'Late towels'},'FV'],
      ['SetReservationMealPlan',{reservationId:501,mealPlanCode:'CP'},undefined],['SetReservationMealPlan',{reservationId:501,mealPlanCode:null},undefined]]);
    assert.equal(mapper('CAT_CHARGE').precheck({type:'CAT_CHARGE',id:'501'}),'Choose an item from the catalogue.');
    assert.equal(mapper('CAT_DISCOUNT').precheck({type:'CAT_DISCOUNT',id:'501',amount:100}),'Choose a discount reason.');});
  test('[CAT-SYNC] Bookings patch: plan on the booking, extraId / planCode / discountReason on the server lines; reports read them',()=>{
    const x=patched[0];assert.equal(x.plan,'CP');assert.equal(patched[1].plan,undefined);
    assert.deepEqual(x.lines.map(l=>[l.id,l.extraId,l.planCode,l.discountReason]),[['9001',undefined,undefined,undefined],['9002','11',undefined,undefined],['9003',undefined,'CP',undefined],['9004',undefined,undefined,'SERVICE']]);
    const s={...st,bookings:patched};const split=M.catChargeSplit(s,'2026-10-24','2026-10-24');
    assert.deepEqual([split.catalogue.count,split.freeText.count,split.plans.amount],[1,0,60000]);
    assert.deepEqual(M.catDiscountReport(s,'2026-10-24','2026-10-24').map(r=>[r.key,r.amount]),[['SERVICE',10000]]);});
}
