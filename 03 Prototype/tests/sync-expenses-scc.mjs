// DEC-120 · SCC expense features in the production client (sync/accounting.ts): GET /expenses/registers → s.expensesX (the prototype
// selectors then give the same budgets, recurring status, petty balance, Udhaar ageing and month feed as the demo), the ExpenseView extras
// (description, GST, source, void, edit log → editLog / editors, bill photo flag) and every EXP_* command sent as the server contract
// (Hcc.Contracts.Expenses) with key + loaded version. Names prefixed [EXP-SCC sync].
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  const S=await loadSync();
  const D='2026-10-24';
  const exp=(id,over={})=>({expenseId:id,ref:'EXP-'+id,vendor:'Aqua',category:'Supplies',amountPaise:30000,method:'UPI',businessDate:D,status:'Pending',submittedBy:3,notes:null,receipt:null,
    reason:null,decidedBy:null,decidedUtc:null,reversalOf:null,reversedBy:null,version:'V'+id,...over});
  const list=[
    exp(201,{description:'Water cans',gstRateBps:1800,gstPaise:4576,hasBillPhoto:true,createdUtc:'2026-10-24T03:00:00Z',
      edits:[{editId:1,by:2,atUtc:'2026-10-24T04:00:00Z',reason:'Bill says ₹300',old:{amount:25000,description:''},new:{amount:30000,description:'Water cans'}}]}),
    exp(202,{status:'Void',description:'Duplicate',voidReason:'Duplicate entry — entered twice',voidedBy:5,voidedUtc:'2026-10-24T05:00:00Z'}),
    exp(203,{status:'Posted',method:'Cash',source:'petty',description:'Petty: tea',amountPaise:40000,decidedBy:1,decidedUtc:'2026-10-24T05:30:00Z'}),
    exp(204,{status:'Draft',source:'recurring',sourceRef:'rec:7:2026-10',description:'Shop rent — 2026-10',amountPaise:2400000,method:'Bank transfer'}),
  ];
  const registers={businessDate:D,
    budgets:[{month:'2026-10',category:'Supplies',amountPaise:100000},{month:'2026-09',category:'Laundry',amountPaise:50000}],
    recurring:[{templateId:7,name:'Shop rent',amountPaise:2500000,dueDay:31,category:'Utilities',method:'Bank transfer',vendor:'Landlord',active:true,createdBy:2,createdUtc:'2026-10-01T04:00:00Z'}],
    petty:[{entryId:11,amountPaise:500000,businessDate:'2026-10-20',status:'Approved',note:'Float',recordedBy:4,recordedUtc:'2026-10-20T04:00:00Z',decidedBy:1,decidedUtc:'2026-10-20T05:00:00Z',reason:null},
      {entryId:12,amountPaise:100000,businessDate:D,status:'Pending',note:null,recordedBy:4,recordedUtc:'2026-10-24T04:00:00Z',decidedBy:null,decidedUtc:null,reason:null}],
    udhaar:[{receivableId:21,name:'Ramesh (fictional)',mobile:'9876543210',amountPaise:250000,creditDate:'2026-08-01',dueDate:'2026-08-31',note:'Banquet snacks',createdBy:4,createdUtc:'2026-08-01T04:00:00Z',
      void:false,voidReason:null,voidedBy:null,voidedDate:null,settlements:[{settlementId:31,amountPaise:100000,date:'2026-09-01',mode:'Cash',recordedBy:4,recordedUtc:'2026-09-01T04:00:00Z'}]}],
    months:[{month:'2026-09',expensePaise:180000,count:1,gstPaidPaise:0,byCategory:{Laundry:180000},generatedBy:1,generatedUtc:'2026-10-01T04:00:00Z',stale:true,staleUtc:'2026-10-02T04:00:00Z'}]};
  const ok={message:'ok',id:null,budgetAlert:null};
  let saved=null;
  const srv=fakeServer({
    'GET /expenses':({query})=>{const all=list.filter(e=>query.status?e.status===query.status:e.businessDate>=query.from&&e.businessDate<=query.to);return {items:all,page:1,pageSize:100,total:all.length,ownOnly:false};},
    'GET /expenses/registers':()=>registers,
    'POST /commands/SubmitExpense':({body})=>({expense:exp(205,{description:body.description}),duplicateOf:[],cashMovementId:null,budgetAlert:'Supplies would be 95% of Oct budget once approved'}),
    'POST /commands/EditExpense':()=>({expense:exp(201),duplicateOf:[],cashMovementId:null,budgetAlert:null}),
    'POST /commands/VoidExpense':()=>({expense:exp(201,{status:'Void'}),duplicateOf:[],cashMovementId:null}),
    'POST /commands/DecideExpense':()=>({expense:exp(201,{status:'Posted'}),duplicateOf:[],cashMovementId:null,budgetAlert:'Supplies now 100% of Oct budget'}),
    'POST /commands/SetExpenseBudget':()=>ok,'POST /commands/CopyExpenseBudgets':()=>ok,'POST /commands/SaveRecurringExpense':()=>({...ok,id:8}),
    'POST /commands/ToggleRecurringExpense':()=>ok,'POST /commands/DeleteRecurringExpense':()=>ok,'POST /commands/PostRecurringExpense':()=>({...ok,id:206}),
    'POST /commands/RequestPettyFloat':()=>({...ok,id:13}),'POST /commands/DecidePettyFloat':()=>ok,'POST /commands/RecordPettyDisbursement':()=>({...ok,id:207}),
    'POST /commands/AddUdhaar':()=>({...ok,id:22}),'POST /commands/PayUdhaar':()=>ok,'POST /commands/VoidUdhaar':()=>ok,'POST /commands/LockExpenseMonth':()=>ok,
    'GET /expenses/export/ledger':()=>({fileName:'ledger_2026-10-24.csv',csv:'Date,Firm\n',rows:0}),
    'GET /expenses/export/month':({query})=>({fileName:`expense_month_${query.month}.csv`,csv:'Date,Firm\n',rows:0}),
  });
  const rt=await startRuntime(S,srv);const s=rt.get().state;
  const res={};const seq=async(name,cmd)=>{srv.reset();res[name]={r:await rt.run(cmd),calls:srv.calls.map(c=>`${c.method} ${c.path}`),body:srv.calls.find(c=>c.method==='POST')?.body,key:srv.calls.find(c=>c.method==='POST')?.key};};
  await seq('add',{type:'EXPENSE',description:'Soap',vendor:'',category:'Supplies',method:'UPI',amount:95000,receipt:'',notes:'',draft:false,date:D,gstRateBps:1800,gstPaise:14492,
    billPhoto:'data:image/jpeg;base64,/9j/AA==',confirmDuplicate:true,confirmGst:true,override:'Bill found late',key:'m-1:submit'});
  await seq('addBad',{type:'EXPENSE',vendor:'X',category:'Supplies',method:'UPI',amount:100});
  await seq('edit',{type:'EXP_EDIT',id:'201',amount:30000,category:'Supplies',method:'UPI',description:'Water cans',gstRateBps:1800,gstPaise:4576,receipt:'AQ-1',reason:'Bill says ₹300',key:'e-1'});
  await seq('void',{type:'EXP_VOID',id:'201',reason:'Duplicate entry',note:'entered twice',key:'v-1'});
  await seq('approve',{type:'EXPENSE_APPROVE',id:'201',approve:true,override:'Owner override',key:'a-1'});
  await seq('budget',{type:'EXP_BUDGET_SET',category:'Supplies',amount:120000});
  await seq('copy',{type:'EXP_BUDGET_COPY'});
  await seq('rec',{type:'EXP_REC_SAVE',name:'Shop rent',amount:2500000,dueDay:31,category:'Utilities',method:'Bank transfer',vendor:'Landlord',confirmDuplicate:true});
  await seq('recEdit',{type:'EXP_REC_SAVE',id:'7',name:'Shop rent',amount:2600000,dueDay:30,category:'Utilities',method:'Bank transfer',vendor:''});
  await seq('toggle',{type:'EXP_REC_TOGGLE',id:'7'});await seq('del',{type:'EXP_REC_DELETE',id:'7'});
  await seq('post',{type:'EXP_REC_POST',id:'7',amount:2400000,again:true});
  await seq('float',{type:'EXP_PETTY_FLOAT',amount:100000,note:'Top-up'});await seq('decide',{type:'EXP_PETTY_FLOAT_DECIDE',id:'12',approve:false,reason:'Not needed'});
  await seq('out',{type:'EXP_PETTY_OUT',amount:5000,category:'Petty cash',for:'Tea'});
  await seq('ud',{type:'EXP_UD_ADD',name:'Mohan',mobile:'98765 43210',amount:50000,dueDate:'2026-11-20',note:''});
  await seq('pay',{type:'EXP_UD_PAY',id:'21',amount:150000,mode:'UPI',reference:'UPI-REF-1'});await seq('wo',{type:'EXP_UD_VOID',id:'21',reason:'Written off'});
  await seq('lock',{type:'EXP_MONTH_LOCK',month:'2026-10'});
  await seq('csvLedger',{type:'EXP_CSV',kind:'ledger'});await seq('csvMonth',{type:'EXP_CSV',kind:'month',month:'2026-09'});

  test('[EXP-SCC sync] GET /expenses/registers → expensesX: the prototype selectors give the same budgets, recurring status, petty balance, Udhaar ageing and feed state',()=>{
    const x=s.expensesX;
    assert.deepEqual(x.budgets,{'2026-10':{Supplies:100000},'2026-09':{Laundry:50000}});
    assert.deepEqual(x.recurring.map(t=>[t.id,t.name,t.dueDay,t.active,t.createdBy]),[['7','Shop rent',31,true,'2']]);
    assert.deepEqual(M.expRecStatus(s,x.recurring[0]),{kind:'posted',count:1,day:31},'the draft rec:7:2026-10 counts as posted');
    assert.deepEqual([M.expPetty(s).float,M.expPetty(s).disbursed,M.expPetty(s).pendingTopUps],[500000,40000,100000]);
    const u=M.expUdOpen(s);assert.deepEqual([u.total,u.buckets['60+'],u.open[0].mobile,M.expUdOverdue(u.open[0],D)],[150000,150000,'9876543210',true]);
    assert.equal(M.expMonthLocked(s,'2026-09-15'),true);assert.equal(M.expFeedStale(s,'2026-09'),true);
  });
  test('[EXP-SCC sync] ExpenseView extras: description, GST, source, void, bill-photo flag, edit log → editLog + editors (who may then not approve)',()=>{
    const e=s.expenses.find(x=>x.id==='201');
    assert.deepEqual([e.description,e.gstRateBps,e.gstPaise,e.hasBillPhoto,e.createdAt],['Water cans',1800,4576,true,'2026-10-24T08:30']);
    assert.deepEqual(e.editLog,[{at:'2026-10-24T09:30',by:'2',old:{amount:25000,description:''},new:{amount:30000,description:'Water cans'},reason:'Bill says ₹300'}]);
    assert.deepEqual(e.editors,['2']);
    const v=s.expenses.find(x=>x.id==='202');assert.deepEqual([v.status,v.voidReason,v.voidedBy],['Void','Duplicate entry — entered twice','5']);
    assert.deepEqual([s.expenses.find(x=>x.id==='203').source,s.expenses.find(x=>x.id==='204').sourceRef],['petty','rec:7:2026-10']);
  });
  test('[EXP-SCC sync] EXPENSE sends description, date, GST, bill photo (base64 + type), confirms and the Owner override; the budget alert comes back as a warning',()=>{
    assert.ok(res.add.r.ok,res.add.r.text);assert.equal(res.add.key,'m-1:submit');
    assert.deepEqual(res.add.body,{vendor:null,category:'Supplies',amountPaise:95000,method:'UPI',draft:false,description:'Soap',expenseDate:D,gstRateBps:1800,gstPaise:14492,
      billPhotoBase64:'/9j/AA==',billPhotoContentType:'image/jpeg',confirmDuplicate:true,confirmGst:true,overrideReason:'Bill found late'});
    assert.equal(res.add.r.warning,'Supplies would be 95% of Oct budget once approved');
    assert.ok(res.add.calls.includes('GET /expenses/registers'),'the registers reload (budgets)');
    assert.deepEqual([res.addBad.r.reason,res.addBad.calls.length],['precheck',0]);assert.match(res.addBad.r.text,/Description is required/);
  });
  test('[EXP-SCC sync] EXP_EDIT / EXP_VOID / approve with override → EditExpense, VoidExpense, DecideExpense with the loaded version',()=>{
    assert.deepEqual(res.edit.body,{expenseId:201,expectedVersion:'V201',amountPaise:30000,category:'Supplies',method:'UPI',description:'Water cans',reason:'Bill says ₹300',gstRateBps:1800,gstPaise:4576,receipt:'AQ-1'});
    assert.deepEqual(res.void.body,{expenseId:201,expectedVersion:'V201',reason:'Duplicate entry',note:'entered twice'});
    assert.deepEqual(res.approve.body,{expenseId:201,expectedVersion:'V201',approve:true,overrideReason:'Owner override'});
    assert.equal(res.approve.r.warning,'Supplies now 100% of Oct budget');
  });
  test('[EXP-SCC sync] budgets, recurring, petty cash, Udhaar, month lock → the register commands; CSV = the server file saved on this device',()=>{
    assert.deepEqual([res.budget.body,res.copy.body],[{category:'Supplies',amountPaise:120000},{}]);
    assert.deepEqual(res.rec.body,{templateId:null,name:'Shop rent',amountPaise:2500000,dueDay:31,category:'Utilities',method:'Bank transfer',vendor:'Landlord',confirmDuplicate:true});
    assert.deepEqual(res.rec.r.result,{id:'8'});
    assert.deepEqual(res.recEdit.body,{templateId:7,name:'Shop rent',amountPaise:2600000,dueDay:30,category:'Utilities',method:'Bank transfer',vendor:null});
    assert.deepEqual([res.toggle.body,res.del.body],[{templateId:7},{templateId:7}]);
    assert.deepEqual(res.post.body,{templateId:7,amountPaise:2400000,again:true});
    assert.deepEqual([res.float.body,res.decide.body],[{amountPaise:100000,note:'Top-up'},{entryId:12,approve:false,reason:'Not needed'}]);
    assert.deepEqual(res.out.body,{amountPaise:5000,category:'Petty cash',for:'Tea'});
    assert.deepEqual(res.ud.body,{name:'Mohan',amountPaise:50000,mobile:'98765 43210',dueDate:'2026-11-20'});
    assert.deepEqual([res.pay.body,res.wo.body],[{receivableId:21,amountPaise:150000,mode:'UPI',reference:'UPI-REF-1'},{receivableId:21,reason:'Written off'}]);
    assert.deepEqual(res.lock.body,{month:'2026-10'});
    assert.ok(res.csvLedger.r.ok&&res.csvLedger.calls.includes('GET /expenses/export/ledger'),res.csvLedger.r.text);
    assert.ok(res.csvMonth.calls.includes('GET /expenses/export/month'));
    assert.equal(res.csvMonth.r.result.rows,0);
    assert.ok(['saved','downloaded','shared','failed'].includes(res.csvMonth.r.result.saved)||typeof res.csvMonth.r.result.saved==='string');
    void saved;
  });
}
