// MA-3 · Money masters in the production sync layer (src/clients/sync/money-master.ts + the MST_SERVER entries it adds to sync/masters.ts)
// against a fake server shaped like 08 Production/src/Hcc.Contracts/MoneyMasters/MoneyMastersContracts.cs. Names prefixed [MNY-SYNC].
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters','money-master'].includes(a.id))};
  const meta=(id,v)=>({updatedUtc:'2026-10-24T03:00:00.000Z',version:v+id,retiredOn:null,active:true,usageCount:0});
  const pms=[{paymentMethodId:1,code:'CASH',name:'Cash',sortOrder:10,kind:'cash',settlesTo:'TILL',needsReference:false,usableAt:['frontdesk','pos','expenses'],surchargeBps:0,ledger:'1000',...meta(1,'P')},
    {paymentMethodId:2,code:'UPI',name:'UPI',sortOrder:20,kind:'upi',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk','pos','expenses'],surchargeBps:0,ledger:'1010',...meta(2,'P')},
    {paymentMethodId:7,code:'PAYTM',name:'Paytm wallet',sortOrder:70,kind:'wallet',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk'],surchargeBps:150,ledger:null,...meta(7,'P')},
    {paymentMethodId:5,code:'VCREDIT',name:'Vendor credit',sortOrder:50,kind:'credit',settlesTo:null,needsReference:false,usableAt:['expenses'],surchargeBps:0,ledger:'2200',...meta(5,'P'),active:false,retiredOn:'2026-10-20'}];
  const bas=[{bankAccountId:1,code:'TILL',name:'Front-desk cash drawer',sortOrder:10,kind:'till',bank:null,accountLast4:null,ifsc:null,openingBalancePaise:null,defaultFor:[],...meta(1,'B')},
    {bankAccountId:2,code:'BANK-1',name:'Hotel current account',sortOrder:20,kind:'bank',bank:'Demo Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234',openingBalancePaise:2500000,defaultFor:['upi','card'],...meta(2,'B')}];
  const ecs=[{expenseCategoryId:3,code:'SUPPLIES',name:'Supplies',sortOrder:10,ledger:'5000',needsBill:true,approvalLimitPaise:100000,...meta(3,'E')},
    {expenseCategoryId:4,code:'GAS',name:'Kitchen gas',sortOrder:60,ledger:'5050',needsBill:false,approvalLimitPaise:null,...meta(4,'E')}];
  const evs=[{expenseVendorId:9,code:'V-SAI',name:'Sai Provisions',sortOrder:10,type:'supplier',gstin:'27ABCDE1234F1Z5',pan:'ABCDE1234F',tds:true,contact:null,terms:'15 days',...meta(9,'V')}];
  const hist=[{historyId:11,rowId:7,code:'PAYTM',field:'name',oldValue:'Paytm',newValue:'Paytm wallet',changedBy:1,changedByName:'owner',changedUtc:'2026-10-24T03:30:00.000Z'}];
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const names=['PaymentMethod','BankAccount','ExpenseCategory','ExpenseVendor'];
  const routes={'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([]),
    'GET /masters/payment-methods':()=>page(pms),'GET /masters/payment-methods/history':()=>page(hist),
    'GET /masters/bank-accounts':()=>page(bas),'GET /masters/bank-accounts/history':()=>page([]),
    'GET /masters/expense-categories':()=>page(ecs),'GET /masters/expense-categories/history':()=>page([]),
    'GET /masters/expense-vendors':()=>page(evs),'GET /masters/expense-vendors/history':()=>page([])};
  for(const n of names)for(const op of ['Save','Retire','Reactivate','Delete'])routes[`POST /commands/${op}${n}`]=()=>({});
  for(const n of ['ReorderPaymentMethods','ReorderBankAccounts','ReorderExpenseCategories','ReorderExpenseVendors'])routes['POST /commands/'+n]=()=>page([]);
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors,bootGets=srv.gets().map(c=>c.path);

  test('[MNY-SYNC] patch loader fills the four money lists into mastersX next to the floors (ids = server ids, wire → prototype fields)',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    // Masters integration: the other agents' masters load next to these (empty lists on this fake server), so only ours are pinned.
    assert.deepEqual(Object.keys(st.mastersX.rows).filter(k=>['bankAccounts','expenseCategories','expenseVendors','floors','paymentMethods'].includes(k)).sort(),['bankAccounts','expenseCategories','expenseVendors','floors','paymentMethods']);
    assert.ok(Object.keys(st.mastersX.rows).every(k=>k in S.MST_SERVER));
    assert.deepEqual(st.mastersX.rows.paymentMethods[2],{id:'7',code:'PAYTM',name:'Paytm wallet',sortOrder:70,active:true,kind:'wallet',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk'],surchargePct:150});
    assert.deepEqual(st.mastersX.rows.paymentMethods[3].retiredOn,'2026-10-20');
    assert.deepEqual(st.mastersX.rows.bankAccounts[1],{id:'2',code:'BANK-1',name:'Hotel current account',sortOrder:20,active:true,kind:'bank',bank:'Demo Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234',openingBalance:2500000,defaultFor:['upi','card']});
    assert.deepEqual(st.mastersX.rows.expenseCategories.map(r=>[r.id,r.ledger,r.needsBill,r.approvalLimit]),[['3','5000',true,100000],['4','5050',false,undefined]]);
    assert.deepEqual(st.mastersX.rows.expenseVendors,[{id:'9',code:'V-SAI',name:'Sai Provisions',sortOrder:10,active:true,type:'supplier',gstin:'27ABCDE1234F1Z5',pan:'ABCDE1234F',tds:true,terms:'15 days'}]);
    assert.deepEqual(st.mastersX.history.map(h=>[h.master,h.rowId,h.field,h.old,h.new]),[['paymentMethods','7','name','Paytm','Paytm wallet']]);
    assert.equal(rt.ids.version('paymentMethod','7'),'P7');assert.equal(rt.ids.version('expenseVendor','9'),'V9');
    for(const p of ['/masters/payment-methods','/masters/bank-accounts','/masters/expense-categories','/masters/expense-vendors'])assert.equal(bootGets.filter(x=>x===p).length,1,p);});
  test('[MNY-SYNC] the selectors read the server rows: pick lists, drawer rule, renamed name resolves, categories and vendors',()=>{
    assert.deepEqual(M.mnyNames(st,'frontdesk'),['Cash','UPI','Paytm wallet']);assert.deepEqual(M.mnyNames(st,'expenses'),['Cash','UPI']);
    assert.equal(M.mnyIsDrawer(st,'Cash'),true);assert.equal(M.mnyIsDrawer(st,'Paytm wallet'),false);assert.equal(M.mnyMethod(st,'Paytm').code,'PAYTM');
    assert.deepEqual(M.mnyCategoryNames(st),['Supplies','Kitchen gas']);assert.equal(M.mnyApprovalLimit(st,'Supplies',500000),100000);
    assert.equal(M.mnyVendor(st,'sai provisions').code,'V-SAI');assert.equal(M.accMapping(st)['expense:Kitchen gas'],'5050');});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const add=await go({type:'MST_MASTER_SAVE',master:'paymentMethods',row:{code:'card',name:'Card',sortOrder:30,kind:'card',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk','pos'],surchargePct:200,ledger:'1020'},key:'mny:add'});
  const edit=await go({type:'MST_MASTER_SAVE',master:'bankAccounts',row:{id:'2',code:'BANK-1',name:'Main account',sortOrder:20,kind:'bank',bank:'Demo Bank (fictional)',accountLast4:'4321',ifsc:'demo0001234',openingBalance:2500000,defaultFor:['upi']}});
  const cat=await go({type:'MST_MASTER_SAVE',master:'expenseCategories',row:{id:'4',code:'GAS',name:'Kitchen gas',sortOrder:60,ledger:'5050',needsBill:true}});
  const ven=await go({type:'MST_MASTER_SAVE',master:'expenseVendors',row:{code:'V-NEW',name:'New Traders',type:'supplier',gstin:'',pan:'abcde1234f',tds:true}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'paymentMethods',id:'7'});
  const back=await go({type:'MST_MASTER_REACTIVATE',master:'paymentMethods',id:'5'});
  const del=await go({type:'MST_MASTER_DELETE',master:'expenseVendors',id:'9'});
  const order=await go({type:'MST_MASTER_REORDER',master:'expenseCategories',ids:['4','3']});
  test('[MNY-SYNC] MST_MASTER_SAVE (new payment method) → SavePaymentMethod with the money-master fields; reload of mastersX',()=>{
    assert.ok(add.r.ok);const c=add.cmds[0];assert.equal(c.path,'/commands/SavePaymentMethod');assert.equal(c.key,'mny:add');
    assert.deepEqual(c.body,{code:'CARD',name:'Card',sortOrder:30,kind:'card',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk','pos'],surchargeBps:200,ledger:'1020'});
    assert.ok(add.gets.includes('/masters/payment-methods')&&add.gets.includes('/masters/floors'));});
  test('[MNY-SYNC] edits carry the server id and the loaded version (bank account: IFSC in capitals, opening balance in paise)',()=>{
    assert.ok(edit.r.ok);assert.deepEqual([edit.cmds[0].path,edit.cmds[0].body],['/commands/SaveBankAccount',{code:'BANK-1',name:'Main account',sortOrder:20,kind:'bank',bank:'Demo Bank (fictional)',
      accountLast4:'4321',ifsc:'DEMO0001234',openingBalancePaise:2500000,defaultFor:['upi'],bankAccountId:2,expectedVersion:'B2'}]);
    assert.deepEqual([cat.cmds[0].path,cat.cmds[0].body],['/commands/SaveExpenseCategory',{code:'GAS',name:'Kitchen gas',sortOrder:60,ledger:'5050',needsBill:true,approvalLimitPaise:null,itcBlocked:false,rcm:false,rcmBps:null,capital:false,expenseCategoryId:4,expectedVersion:'E4'}]); // ACC-2: purchase flags
    assert.deepEqual([ven.cmds[0].path,ven.cmds[0].body],['/commands/SaveExpenseVendor',{code:'V-NEW',name:'New Traders',type:'supplier',gstin:null,pan:'ABCDE1234F',tds:true,contact:null,terms:null,state:null,tdsSection:null}]);}); // ACC-2: state, TDS section
  test('[MNY-SYNC] retire / reactivate / delete / reorder → the money-master commands with their id fields',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetirePaymentMethod',{paymentMethodId:7,expectedVersion:'P7'}]);
    assert.deepEqual([back.cmds[0].path,back.cmds[0].body],['/commands/ReactivatePaymentMethod',{paymentMethodId:5,expectedVersion:'P5'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteExpenseVendor',{expenseVendorId:9,expectedVersion:'V9'}]);
    assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderExpenseCategories',{expenseCategoryIds:[4,3]}]);
    for(const x of [ret,back,del,order])assert.ok(x.r.ok&&x.gets.includes('/masters/payment-methods'));});

  srv.on('POST /commands/SavePaymentMethod',()=>reply(422,{code:'VALIDATION',message:'A cash method settles to a till.',details:{reason:'MASTER_INVALID',field:'settlesTo'}}));
  const bad=await rt.run({type:'MST_MASTER_SAVE',master:'paymentMethods',row:{code:'X',name:'X',kind:'cash',settlesTo:'BANK-1',usableAt:['frontdesk']}});
  // Reception (no PERM-MST-002): the lists load (pick lists), no history read; an empty methods list keeps the seed, an empty vendor list stays empty.
  const rec=fakeServer({...routes,'GET /masters/payment-methods':()=>page([]),'GET /masters/expense-vendors':()=>page([]),
    'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A'],['PERM-EXP-001','A']]})});
  const rrt=await startRuntime(S,rec,mine);const rs=rrt.get().state;
  test('[MNY-SYNC] server master errors keep their reason; Reception loads the lists without history; empty lists fall back safely',()=>{
    assert.deepEqual([bad.ok,bad.reason],[false,'server']);assert.equal(M.mstReasonOf(bad.error),'MASTER_INVALID');
    const gets=rec.gets().map(c=>c.path).filter(p=>p.startsWith('/masters'));
    assert.ok(!gets.some(p=>p.endsWith('/history')&&p!=='/masters/floors/history'));
    assert.ok(gets.includes('/masters/payment-methods')&&gets.includes('/masters/expense-categories'));
    assert.equal(rs.mastersX.rows.paymentMethods,undefined);assert.deepEqual(M.mnyNames(rs,'frontdesk'),['Cash','UPI','Card','Bank transfer']);
    assert.deepEqual(rs.mastersX.rows.expenseVendors,[]);assert.deepEqual(rs.mastersX.history,[]);assert.deepEqual(rrt.get().errors,{});});
  test('[MNY-SYNC] registry: the area has one patch loader on mastersX and no own mappers (the MA-0 generic commands carry the routes)',()=>{
    const area=S.AREAS.find(a=>a.id==='money-master');assert.deepEqual(area.loaders.map(l=>[l.slice,l.kind]),[['mastersX','patch']]);assert.deepEqual(area.commands,[]);
    assert.equal(S.AREAS.filter(a=>a.commands.some(c=>c.type==='MST_MASTER_SAVE')).map(a=>a.id).join(),'masters');});

  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true)];
  test('[MNY-SYNC] every desktop and phone page renders on the loaded money masters',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
