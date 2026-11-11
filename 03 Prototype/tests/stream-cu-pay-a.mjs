// CU-PAY-A: the POS settle precheck / request (sync/pos.ts) and the expense till (sync/accounting.ts) read the payment-method master
// (s.mastersX.rows.paymentMethods) instead of fixed lists: active accepted, inactive / unknown / not-for-the-place refused with the server's words,
// a renamed method shows (and is sent under) its new label, the till follows "settles to a till", and receipts recorded before a rename keep their
// snapshot. Money stays integer paise. Names prefixed [CU-PAY-A].
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/cu-pay-a-entry.ts',"export {pos} from '../../src/clients/sync/pos';\nexport {accounting} from '../../src/clients/sync/accounting';\nexport {HccClientError} from '../../src/clients/errors';\n");
await build({entryPoints:['tests/.generated/cu-pay-a-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/cu-pay-a.mjs',logLevel:'error'});
const X=await import('./.generated/cu-pay-a.mjs');

export async function register({test,assert,M}){
  const cmdOf=(area,type)=>area.commands.find(c=>c.type===type);
  const settle=cmdOf(X.pos,'POS_SETTLE'),expense=cmdOf(X.accounting,'EXPENSE');
  // The Owner's master: Cash renamed "Front till" (still the drawer), UPI renamed "PhonePe UPI", Card retired, Zomato added (POS only, no reference),
  // Vendor credit kept (expenses only), Bank transfer unchanged (no POS).
  const rows=[
    {id:'pm-cash',code:'CASH',name:'Front till',sortOrder:10,active:true,kind:'cash',settlesTo:M.MNY_TILL,needsReference:false,usableAt:['frontdesk','pos','expenses'],ledger:'1000'},
    {id:'pm-upi',code:'UPI',name:'PhonePe UPI',sortOrder:20,active:true,kind:'upi',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos','expenses'],ledger:'1010'},
    {id:'pm-card',code:'CARD',name:'Card',sortOrder:30,active:false,retiredOn:'2026-10-20',kind:'card',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos'],ledger:'1020'},
    {id:'pm-bank',code:'BANK',name:'Bank transfer',sortOrder:40,active:true,kind:'bank',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','expenses'],ledger:'1030'},
    {id:'pm-zomato',code:'ZOMATO',name:'Zomato',sortOrder:47,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:false,usableAt:['pos']},
    {id:'pm-vendor-credit',code:'VCREDIT',name:'Vendor credit',sortOrder:50,active:true,kind:'credit',needsReference:false,usableAt:['expenses'],ledger:'2200'},
  ];
  const history=[{id:'h1',master:'paymentMethods',rowId:'pm-cash',code:'CASH',field:'name',old:'Cash',new:'Front till',by:'owner',at:'2026-10-21T10:00'},
    {id:'h2',master:'paymentMethods',rowId:'pm-upi',code:'UPI',field:'name',old:'UPI',new:'PhonePe UPI',by:'owner',at:'2026-10-21T10:01'}];
  const withMaster=s=>({...s,shiftOpen:true,mastersX:{...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),paymentMethods:rows.map(r=>({...r,usableAt:[...r.usableAt]}))},history:[...history]}});
  const base=withMaster(M.seed());
  const pre=(cmd,s=base)=>settle.precheck({type:'POS_SETTLE',id:'1',mode:'outlet',...cmd},s,undefined);

  // ── POS settle precheck ──
  test('[CU-PAY-A] POS precheck accepts an active master method (renamed, added) and takes its reference from the row',()=>{
    assert.equal(pre({method:'Front till'}),undefined,'drawer cash needs no reference');
    assert.equal(pre({method:'PhonePe UPI',reference:'U-1'}),undefined);
    assert.equal(pre({method:'Zomato'}),undefined,'an Owner-added POS method without "needs a reference"');
    assert.equal(pre({method:'PhonePe UPI'}),'Enter the transaction reference.');
  });
  test('[CU-PAY-A] POS precheck: retired, unknown and not-for-POS methods are refused with the server words',()=>{
    assert.equal(pre({method:'Card',reference:'C-1'}),'Card is retired — choose another payment method.');
    const list='Select Front till, PhonePe UPI or Zomato.';
    assert.equal(pre({method:'Cheque',reference:'x'}),list);
    assert.equal(pre({method:'Bank transfer',reference:'x'}),list,'bank transfer is not offered at the outlet');
    assert.equal(pre({method:'Vendor credit'}),list);
    assert.equal(pre({method:''}),list);
    assert.equal(pre({mode:'x',method:'Front till'}),'Choose how the bill is settled.');
  });
  test('[CU-PAY-A] POS precheck with the seeded master is unchanged: Cash / UPI / Card, reference for UPI and Card only',()=>{
    const s=M.seed();
    assert.equal(pre({method:'Cash'},s),undefined);assert.equal(pre({method:'UPI',reference:'u'},s),undefined);
    assert.equal(pre({method:'Card'},s),'Enter the transaction reference.');
    assert.equal(pre({method:'Bank transfer',reference:'x'},s),'Select Cash, UPI or Card.');
  });
  test('[CU-PAY-A] POS precheck: an earlier name, the code or another case of a renamed method still resolves',()=>{
    for(const m of ['UPI','upi','PhonePe UPI',' phonepe upi ']) assert.equal(pre({method:m,reference:'U-1'}),undefined,m);
    for(const m of ['Cash','CASH','Front till']) assert.equal(pre({method:m}),undefined,m);
  });

  // ── POS settle request ──
  const run=async(s,cmd,{shiftOpen=true}={})=>{
    const calls=[];
    const c={cmd:{type:'POS_SETTLE',id:'77',mode:'outlet',...cmd},state:s,ids:{version:()=>'CV1'},
      api:{get:async p=>{calls.push({get:p});return {tillId:shiftOpen?3:null,code:'FD',shiftId:shiftOpen?12:null,shiftOpen};},
        command:async(name,body,opt)=>{calls.push({name,body,opt});return {};}}};
    let out,err;try{out=await settle.send(c);}catch(e){err=e;}
    return {calls,out,err,post:calls.find(x=>x.name==='SettleCheck')};
  };
  const outcomes={};
  outcomes.old=await run(base,{method:'Cash'});
  outcomes.cur=await run(base,{method:'Front till'});
  outcomes.upi=await run(base,{method:'upi',reference:' U-9 '});
  outcomes.zom=await run(base,{method:'Zomato'});
  outcomes.closed=await run(base,{method:'Front till'},{shiftOpen:false});
  test('[CU-PAY-A] POS request: a renamed method is sent under its current label; a method that settles to the till takes the till, not the literal "Cash"',()=>{
    for(const k of ['old','cur']){const o=outcomes[k];
      assert.equal(o.err,undefined,k);assert.equal(o.post.body.tender,'Front till',k);assert.equal(o.post.body.tillId,3,k+': till carried');
      assert.ok(o.calls.some(x=>x.get==='/pos/till'),k);assert.ok(o.out&&o.out.reload,k+': cash slices reload');}
    assert.equal(outcomes.upi.post.body.tender,'PhonePe UPI');assert.equal(outcomes.upi.post.body.reference,'U-9');assert.equal(outcomes.upi.post.body.tillId,undefined);
    assert.equal(outcomes.upi.out,undefined,'no drawer cash: nothing extra to reload');
    assert.equal(outcomes.zom.post.body.tender,'Zomato');assert.equal(outcomes.zom.post.body.tillId,undefined);assert.ok(!outcomes.zom.calls.some(x=>x.get==='/pos/till'));
  });
  test('[CU-PAY-A] POS request: a drawer method with the shift closed is refused before any command',()=>{
    assert.equal(outcomes.closed.err?.code??outcomes.closed.err?.wireCode,'INVALID_TRANSITION');assert.match(outcomes.closed.err.message,/cash shift is closed/);assert.equal(outcomes.closed.post,undefined);
  });

  // ── Prototype handler + snapshots (the in-memory command path) ──
  const open=s=>{s=M.execute(s,'owner',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T5',covers:2});const id=s.posX.orders.at(-1).id;
    s=M.execute(s,'owner',{type:'POS_ITEM_ADD',id,item:'m-poha',qty:2});s=M.execute(s,'owner',{type:'POS_KOT_SEND',id});return [s,id];};
  test('[CU-PAY-A] old receipts keep their snapshot after a rename; a settle under the old name is booked under the current label (paise intact)',()=>{
    let [s,id1]=open({...M.seed(),shiftOpen:true});
    s=M.execute(s,'owner',{type:'POS_SETTLE',id:id1,mode:'outlet',method:'Cash'});
    const r1=s.posX.receipts.at(-1),before=JSON.stringify(r1),set1=JSON.stringify(s.posX.orders.find(o=>o.id===id1).settlement);
    assert.equal(r1.method,'Cash');assert.ok(Number.isInteger(r1.amount));
    // the Owner renames Cash to Front till in the master (history keeps the old name)
    s={...s,mastersX:{...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),paymentMethods:withMaster(s).mastersX.rows.paymentMethods},history:[...history]}};
    assert.equal(JSON.stringify(s.posX.receipts.find(r=>r.id===r1.id)),before,'receipt untouched by the rename');
    assert.equal(JSON.stringify(s.posX.orders.find(o=>o.id===id1).settlement),set1,'settlement untouched by the rename');
    let id2;[s,id2]=open(s);
    s=M.execute(s,'owner',{type:'POS_SETTLE',id:id2,mode:'outlet',method:'Cash'});
    const r2=s.posX.receipts.at(-1);
    assert.equal(r2.method,'Front till','a settle under the earlier name is booked under the current label');
    assert.equal(r2.amount,r2.net+r2.tax);assert.equal(r2.amount,r1.amount,'same lines, same paise');
    assert.equal(s.posX.receipts.find(r=>r.id===r1.id).method,'Cash','the first receipt still says Cash');
  });
  test('[CU-PAY-A] the in-memory POS handler refuses a retired method and one not usable at the POS',()=>{
    const [s,id]=open(base);
    assert.throws(()=>M.execute(s,'owner',{type:'POS_SETTLE',id,mode:'outlet',method:'Card',reference:'c'}),/retired/);
    assert.throws(()=>M.execute(s,'owner',{type:'POS_SETTLE',id,mode:'outlet',method:'Bank transfer',reference:'c'}));
  });

  // ── Expense till ──
  const ex=(cmd,s=base)=>expense.precheck({type:'EXPENSE',amount:50000,category:'Supplies',description:'x',...cmd},s,undefined);
  test('[CU-PAY-A] expense precheck: active expense methods accepted (renamed, credit); retired, unknown and POS-only refused',()=>{
    for(const m of ['Front till','Cash','PhonePe UPI','Bank transfer','Vendor credit']) assert.equal(ex({method:m}),undefined,m);
    assert.equal(ex({method:'Zomato'}),'Choose a funding source.','POS-only');
    assert.equal(ex({method:'Card'}),'Choose a funding source.','Card is not an expense method');
    const retiredBank={...base,mastersX:{...base.mastersX,rows:{...base.mastersX.rows,paymentMethods:rows.map(r=>r.id==='pm-bank'?{...r,active:false,retiredOn:'2026-10-22'}:r)}}};
    assert.equal(ex({method:'Bank transfer'},retiredBank),'Bank transfer is retired — choose another payment method.');
    assert.equal(ex({method:'Cheque'}),'Choose a funding source.');
    assert.equal(ex({method:'Cheque'},undefined),'Choose a funding source.');
    assert.equal(ex({method:'Cash'},M.seed()),undefined,'seeded master: unchanged');
  });
  const till=async(type,s,e)=>{
    const calls=[];
    const c={cmd:{type,id:'9',approve:true,reason:'dup'},state:{...s,expenses:[e]},
      ids:{version:()=>'EV1',server:(k,x)=>k==='till'&&x==='current'?3:undefined,list:()=>[]},
      api:{command:async(name,body)=>{calls.push({name,body});return {};}}};
    const out=await cmdOf(X.accounting,type).send(c);
    return {calls,out};
  };
  const exp=m=>({id:'9',vendor:'v',category:'Supplies',amount:50000,method:m,date:'2026-10-24',status:'Pending'});
  const tills={};
  tills.renamedOld=await till('EXPENSE_APPROVE',base,exp('Cash'));
  tills.renamedNew=await till('EXPENSE_APPROVE',base,exp('Front till'));
  tills.upi=await till('EXPENSE_APPROVE',base,exp('PhonePe UPI'));
  tills.revOld=await till('EXPENSE_REVERSE',base,exp('Cash'));
  tills.revUpi=await till('EXPENSE_REVERSE',base,exp('UPI'));
  test('[CU-PAY-A] expense approve / reverse: the till follows "settles to a till" (a renamed drawer method sends it), other methods do not',()=>{
    for(const k of ['renamedOld','renamedNew']){const o=tills[k];assert.equal(o.calls[0].name,'DecideExpense');assert.equal(o.calls[0].body.tillId,3,k);assert.ok(o.out.reload,k+': cash slices reload');}
    assert.equal(tills.upi.calls[0].body.tillId,undefined);assert.equal(tills.upi.out.reload,undefined);
    assert.equal(tills.revOld.calls[0].name,'ReverseExpense');assert.equal(tills.revOld.calls[0].body.tillId,3);assert.ok(tills.revOld.out.reload);
    assert.equal(tills.revUpi.calls[0].body.tillId,undefined);assert.equal(tills.revUpi.out,undefined);
    assert.equal(tills.renamedNew.calls[0].body.expectedVersion,'EV1');
  });
  test('[CU-PAY-A] expense handler snapshot: an expense booked as Cash before a rename keeps "Cash"; a new one under the old name is stored under the current label',()=>{
    let s=M.execute({...M.seed(),shiftOpen:true},'owner',{type:'EXPENSE',description:'Test expense cu-pay-a',vendor:'Petty Stores',category:'Petty cash',method:'Cash',amount:42000,draft:false});
    const e1=s.expenses.at(-1);assert.equal(e1.method,'Cash');
    s=withMaster(s);
    s=M.execute(s,'owner',{type:'EXPENSE',description:'Test expense cu-pay-a 2',vendor:'Petty Stores',category:'Petty cash',method:'Cash',amount:42000,draft:false});
    assert.equal(s.expenses.find(e=>e.id===e1.id).method,'Cash','existing expense untouched');
    assert.equal(s.expenses.at(-1).method,'Front till');assert.equal(s.expenses.at(-1).amount,42000);
  });
}
