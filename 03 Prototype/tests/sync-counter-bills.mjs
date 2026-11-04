// AF-CTR-UI (BIL-01, UX-01, HIS-01 client): CTR_BILL_ISSUE sends the till of the open shift for a drawer method, refuses a closed shift before any request,
// keeps the reference for UPI; counterBillLookup sends GET /counter-bills?invoiceNo=&from=&to=. Names prefixed [AF-CTR].
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/af-ctr-entry.ts',"export {lfmoney,counterBillLookup} from '../../src/clients/sync/lfmoney';\n");
await build({entryPoints:['tests/.generated/af-ctr-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/af-ctr.mjs',logLevel:'error'});
const X=await import('./.generated/af-ctr.mjs');

export async function register({test,assert}){
  const S=await loadSync();
  const shift=(over={})=>({shiftId:41,tillId:7,businessDate:'2026-10-24',status:'Open',openingFloatPaise:1072500,expectedPaise:null,countedPaise:null,approvalId:null,version:'SHFv1',...over});
  const desk=(open)=>({businessDate:'2026-10-24',tills:[{till:{tillId:7,code:'FRONT',name:'Front desk',version:'T1'},
    active:open?{shift:shift(),submittedBy:null,differenceReason:null,closedBy:null,approval:null}:null,expected:open?{expectedPaise:1222500}:null,lastClosed:null}]});
  const meta=(id,v)=>({updatedUtc:'2026-10-24T03:00:00.000Z',version:v+id,retiredOn:null,active:true,usageCount:0});
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const issued={billId:5,invoiceNo:'INV/2627/0005',version:'CB1'};
  const mk=(open,extra={})=>fakeServer({'GET /cashier/desk':()=>desk(open),'POST /commands/IssueCounterBill':()=>issued,...extra});
  const party={name:'Walk-in (fictional)'};
  const lines=[{label:'Laundry',qty:1,unit:10000,gstCategory:'laundry'}];
  const issue=(rt,settlement)=>rt.run({type:'CTR_BILL_ISSUE',party,lines,settlement,key:'ctr-'+Math.random().toString(36).slice(2)});

  // 1 cash with an open shift
  const s1=mk(true);const r1=await startRuntime(S,s1);const o1=await issue(r1,{mode:'paid',method:'Cash',reference:''});
  const c1=s1.commands('IssueCounterBill');
  // 2 shift closed
  const s2=mk(false);const r2=await startRuntime(S,s2);const o2=await issue(r2,{mode:'paid',method:'Cash',reference:''});
  // the mapper itself (without the precheck, as on a stale phone) throws INVALID_TRANSITION when no till of an open shift is known
  const mapper=X.lfmoney.commands.find(c=>c.type==='CTR_BILL_ISSUE');
  const direct=await mapper.send({cmd:{party,lines,settlement:{mode:'paid',method:'Cash'}},state:r2.get().state,ids:{server:()=>undefined},
    api:{command:async()=>{throw new Error('no request expected');}}}).then(()=>null,e=>e);
  // 3 UPI
  const s3=mk(true);const r3=await startRuntime(S,s3);const o3=await issue(r3,{mode:'paid',method:'UPI',reference:'UPI-REF-1'});
  const o3b=await issue(r3,{mode:'paid',method:'UPI',reference:''});
  // 4 a drawer method from the master that is not named "Cash"
  const pms=[{paymentMethodId:1,code:'FDESK',name:'Front till',sortOrder:10,kind:'cash',settlesTo:'TILL',needsReference:false,usableAt:['frontdesk'],surchargeBps:0,ledger:'1000',...meta(1,'P')},
    {paymentMethodId:2,code:'UPI',name:'UPI',sortOrder:20,kind:'upi',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk'],surchargeBps:0,ledger:'1010',...meta(2,'P')}];
  const bas=[{bankAccountId:1,code:'TILL',name:'Front-desk cash drawer',sortOrder:10,kind:'till',bank:null,accountLast4:null,ifsc:null,openingBalancePaise:null,defaultFor:[],...meta(1,'B')},
    {bankAccountId:2,code:'BANK-1',name:'Hotel current account',sortOrder:20,kind:'bank',bank:'Demo Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234',openingBalancePaise:2500000,defaultFor:['upi'],...meta(2,'B')}];
  const s4=mk(true,{'GET /masters/payment-methods':()=>page(pms),'GET /masters/bank-accounts':()=>page(bas)});
  const r4=await startRuntime(S,s4);const o4=await issue(r4,{mode:'paid',method:'Front till',reference:''});
  // 5 lookup
  const row=(id,no)=>({billId:id,invoiceNo:no,businessDate:'2026-09-01',issuedUtc:'2026-09-01T05:30:00.000Z',issuedBy:3,partyName:'Old customer (fictional)',partyPhone:null,partyGstin:null,partyAddress:null,
    guestId:null,companyId:null,discountPaise:0,discountReason:null,netPaise:10000,taxPaise:500,cgstPaise:250,sgstPaise:250,totalPaise:10500,remark:null,settlementMode:'paid',method:'UPI',reference:'R9',
    udhaarId:null,status:'Issued',creditNoteNo:null,creditNoteReason:null,voidApprovalId:null,
    lines:[{seq:1,label:'Laundry',extraId:null,menuItemId:null,qty:1,unitPaise:10000,amountPaise:10000,gstCategory:'laundry',taxBps:500,sac:'999711'}]});
  const seen=[];const api={get:async(path,query)=>{seen.push({path,query});return [row(3,'INV/2627/0003')];}};
  const found=await X.counterBillLookup(api,{invoiceNo:' INV/2627/0003 ',from:'',to:'2026-09-30'});

  test('[AF-CTR] CTR_BILL_ISSUE cash sends tillId 7 of the open shift and the method name',()=>{
    assert.equal(o1.ok,true,JSON.stringify(o1));assert.equal(c1.length,1);
    assert.equal(c1[0].body.settlement.tillId,7);assert.equal(c1[0].body.settlement.method,'Cash');assert.equal(c1[0].body.settlement.mode,'paid');
  });
  test('[AF-CTR] cash with the shift closed is refused before any request (0 commands sent, INVALID_TRANSITION text)',()=>{
    assert.equal(o2.ok,false);assert.equal(o2.reason,'precheck');
    assert.match(o2.text,/cash shift is closed/i);assert.equal(s2.commands('IssueCounterBill').length,0);
    assert.ok(direct,'the mapper refuses when no till is known');assert.equal(direct.code,'INVALID_TRANSITION');assert.match(direct.message,/cash shift is closed/i);
  });
  test('[AF-CTR] UPI sends no tillId and keeps the reference',()=>{
    assert.equal(o3.ok,true,JSON.stringify(o3));const c=s3.commands('IssueCounterBill');assert.equal(c.length,1);
    assert.equal(c[0].body.settlement.tillId,undefined);assert.equal(c[0].body.settlement.reference,'UPI-REF-1');assert.equal(c[0].body.settlement.method,'UPI');
    assert.equal(o3b.ok,false);assert.equal(o3b.reason,'precheck');assert.match(o3b.text,/Enter the payment reference.$/); // a UPI bill without a reference never leaves the device
  });
  test('[AF-CTR] a drawer method from the master that is not named "Cash" still sends tillId',()=>{
    assert.equal(o4.ok,true,JSON.stringify(o4));const c=s4.commands('IssueCounterBill');assert.equal(c.length,1);
    assert.equal(c[0].body.settlement.method,'Front till');assert.equal(c[0].body.settlement.tillId,7);
  });
  test('[AF-CTR] counterBillLookup sends GET /counter-bills?invoiceNo=… and maps rows',()=>{
    assert.equal(seen.length,1);assert.equal(seen[0].path,'/counter-bills');
    assert.deepEqual(seen[0].query,{invoiceNo:'INV/2627/0003',to:'2026-09-30'});
    assert.equal(found.length,1);assert.equal(found[0].no,'INV/2627/0003');assert.equal(found[0].id,'3');assert.equal(found[0].lines.length,1);assert.equal(found[0].total,10500);
  });
}
