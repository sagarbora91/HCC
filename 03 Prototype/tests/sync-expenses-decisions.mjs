// SCC decisions wave · expenses in the production client (sync/accounting.ts), owner 27 Oct 2026: DEC-130 an Udhaar cash payment carries the
// till with the open shift and reloads the cash desk (a desk receipt into the drawer), other modes their reference; DEC-131 EXP_MONTH_UNLOCK →
// UnlockExpenseMonth (Owner + reason) and the registers' unlock fields; DEC-135 EXPENSE / EXP_EDIT carry the ITC flag and ExpenseView.itc maps
// back (a GST bill without it = disallowed). Names prefixed [EXP-DEC sync].
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  const S=await loadSync();
  const D='2026-10-24';
  const exp=(id,over={})=>({expenseId:id,ref:'EXP-'+id,vendor:'',category:'Supplies',amountPaise:59000,method:'UPI',businessDate:D,status:'Pending',submittedBy:3,notes:null,receipt:null,
    reason:null,decidedBy:null,decidedUtc:null,reversalOf:null,reversedBy:null,version:'V'+id,...over});
  const list=[exp(301,{description:'Bulbs',gstRateBps:1800,gstPaise:9000,itc:'allowed',status:'Posted',decidedBy:1,decidedUtc:'2026-10-24T05:00:00Z'}),
    exp(302,{description:'Soap',gstRateBps:1800,gstPaise:1800}),exp(303,{description:'Tea',amountPaise:5000})];
  const registers={businessDate:D,budgets:[],recurring:[],petty:[],
    udhaar:[{receivableId:21,name:'Ramesh (fictional)',mobile:null,amountPaise:250000,creditDate:'2026-09-01',dueDate:'2026-10-01',note:null,createdBy:4,createdUtc:'2026-09-01T04:00:00Z',
      void:false,voidReason:null,voidedBy:null,voidedDate:null,settlements:[
        {settlementId:31,amountPaise:5000,date:'2026-09-15',mode:'Cash',recordedBy:4,recordedUtc:'2026-09-15T04:00:00Z'},
        {settlementId:32,amountPaise:100000,date:D,mode:'Cash',recordedBy:4,recordedUtc:'2026-10-24T04:00:00Z',desk:true,reference:null},
        {settlementId:33,amountPaise:20000,date:D,mode:'UPI',recordedBy:4,recordedUtc:'2026-10-24T04:10:00Z',desk:true,reference:'UPI-77'}]}],
    months:[{month:'2026-09',expensePaise:180000,count:1,gstPaidPaise:18000,byCategory:{Laundry:180000},generatedBy:1,generatedUtc:'2026-10-01T04:00:00Z',stale:true,staleUtc:'2026-10-02T04:00:00Z',
      gstItcPaise:9000,unlocked:true,unlockedBy:1,unlockedUtc:'2026-10-02T04:00:00Z',unlockReason:'CA found a missed bill',unlocks:[{by:1,atUtc:'2026-10-02T04:00:00Z',reason:'CA found a missed bill'}]},
      {month:'2026-08',expensePaise:1000,count:1,gstPaidPaise:0,byCategory:{Supplies:1000},generatedBy:1,generatedUtc:'2026-09-01T04:00:00Z',stale:false,staleUtc:null}]};
  const shift=open=>({businessDate:D,tills:[{till:{tillId:1,code:'FRONT',name:'Front desk',version:'T1'},active:open?{shift:{shiftId:9,tillId:1,businessDate:D,
    status:'Open',openingFloatPaise:500000,expectedPaise:null,countedPaise:null,approvalId:null,version:'S9'},submittedBy:null,differenceReason:null,closedBy:null,approval:null}:null,expected:null,lastClosed:null}]});
  const ok={message:'ok',id:null,budgetAlert:null};
  const srv=fakeServer({
    'GET /expenses':({query})=>{const all=list.filter(e=>query.status?e.status===query.status:e.businessDate>=query.from&&e.businessDate<=query.to);return {items:all,page:1,pageSize:100,total:all.length,ownOnly:false};},
    'GET /expenses/registers':()=>registers,
    'GET /cashier/desk':()=>shift(true),
    'POST /commands/SubmitExpense':({body})=>({expense:exp(304,{description:body.description}),duplicateOf:[],cashMovementId:null,budgetAlert:null}),
    'POST /commands/EditExpense':()=>({expense:exp(302),duplicateOf:[],cashMovementId:null,budgetAlert:null}),
    'POST /commands/PayUdhaar':()=>({...ok,id:34}),'POST /commands/UnlockExpenseMonth':()=>ok,
  });
  const rt=await startRuntime(S,srv);const s=rt.get().state;
  const res={};const seq=async(name,cmd)=>{srv.reset();res[name]={r:await rt.run(cmd),calls:srv.calls.map(c=>`${c.method} ${c.path}`),body:srv.calls.find(c=>c.method==='POST')?.body};};
  await seq('add',{type:'EXPENSE',description:'Bulbs',vendor:'',category:'Maintenance',method:'UPI',amount:59000,draft:false,gstRateBps:1800,gstPaise:9000,itc:'allowed',key:'m-1:submit'});
  await seq('edit',{type:'EXP_EDIT',id:'302',amount:59000,category:'Supplies',method:'UPI',description:'Soap',gstRateBps:1800,gstPaise:1800,itc:'disallowed',reason:'CA: not eligible',key:'e-1'});
  await seq('payCash',{type:'EXP_UD_PAY',id:'21',amount:100000,mode:'Cash',key:'ud-1'});
  await seq('payUpiNoRef',{type:'EXP_UD_PAY',id:'21',amount:1000,mode:'UPI'});
  await seq('payUpi',{type:'EXP_UD_PAY',id:'21',amount:1000,mode:'UPI',reference:'UPI-88'});
  await seq('unlock',{type:'EXP_MONTH_UNLOCK',month:'2026-08',reason:'CA found a missed bill',key:'mu-1'});
  await seq('unlockBad',{type:'EXP_MONTH_UNLOCK',month:'2026-08',reason:'x'});
  srv.on('GET /cashier/desk',()=>shift(false));await rt.refresh();
  await seq('payNoShift',{type:'EXP_UD_PAY',id:'21',amount:1000,mode:'Cash'});

  test('[EXP-DEC sync] DEC-135 · ExpenseView.itc → itc (a GST bill without the flag = disallowed; none without GST); EXPENSE / EXP_EDIT send itc',()=>{
    const e=id=>s.expenses.find(x=>x.id===id);
    assert.deepEqual([e('301').itc,e('302').itc,'itc' in e('303')],['allowed','disallowed',false]);
    assert.ok(res.add.r.ok,res.add.r.text);assert.equal(res.add.body.itc,'allowed');assert.equal(res.add.body.gstPaise,9000);
    assert.ok(res.edit.r.ok,res.edit.r.text);assert.deepEqual([res.edit.body.itc,res.edit.body.reason],['disallowed','CA: not eligible']);});
  test('[EXP-DEC sync] DEC-130 · registers: desk settlements map with their reference; only they reach the drawer and the books (a pre-DEC-130 payment never does)',()=>{
    const p=s.expensesX.udhaar[0].settlements;
    assert.deepEqual(p.map(x=>[x.id,x.desk??false,x.reference??'']),[['31',false,''],['32',true,''],['33',true,'UPI-77']]);
    assert.equal(M.expUdDeskCash(s,D),100000,'cash desk receipts of the day');assert.equal(M.expUdDeskReceipts(s,'2026-09-15').length,0);});
  test('[EXP-DEC sync] DEC-130 · EXP_UD_PAY in cash sends the till with the open shift and reloads the cash desk; UPI needs its reference; no open shift → refused on the device',()=>{
    assert.ok(res.payCash.r.ok,res.payCash.r.text);
    assert.deepEqual(res.payCash.body,{receivableId:21,amountPaise:100000,mode:'Cash',tillId:1});
    assert.ok(res.payCash.calls.includes('GET /cashier/desk'),'cash into the drawer: the cash desk reloads');
    assert.deepEqual([res.payUpiNoRef.r.reason,res.payUpiNoRef.calls.length],['precheck',0]);assert.match(res.payUpiNoRef.r.text,/Enter the UPI transaction reference/);
    assert.deepEqual(res.payUpi.body,{receivableId:21,amountPaise:1000,mode:'UPI',reference:'UPI-88'});assert.ok(!res.payUpi.calls.includes('GET /cashier/desk'));
    assert.equal(res.payNoShift.r.reason,'precheck');assert.match(res.payNoShift.r.text,/Open cash shift required/);});
  test('[EXP-DEC sync] DEC-131 · EXP_MONTH_UNLOCK → UnlockExpenseMonth {month, reason}; the registers carry the unlock (not locked, stale, history, ITC part of GST paid)',()=>{
    assert.ok(res.unlock.r.ok,res.unlock.r.text);assert.deepEqual(res.unlock.body,{month:'2026-08',reason:'CA found a missed bill'});
    assert.ok(res.unlock.calls.includes('GET /expenses/registers'));
    assert.deepEqual([res.unlockBad.r.reason,res.unlockBad.calls.length],['precheck',0]);
    const f=s.expensesX.months['2026-09'];
    assert.deepEqual([f.unlockedAt,f.unlockedBy,f.unlockReason,f.unlocks,f.gstItc],['2026-10-02T09:30','1','CA found a missed bill',[{at:'2026-10-02T09:30',by:'1',reason:'CA found a missed bill'}],9000]);
    assert.deepEqual([M.expMonthLocked(s,'2026-09-15'),M.expFeedStale(s,'2026-09'),M.expMonthLocked(s,'2026-08-15')],[false,true,true]);});
}
