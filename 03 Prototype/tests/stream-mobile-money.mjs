// MB-06 money (phone): command builders used by the phone screens (src/mobile/money/logic.ts), replayed through M.execute.
// AC-M06: cashier settles ₹3,400 split (₹2,000 cash + ₹1,400 UPI), returns the deposit and closes the shift — S0 oracle figures.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/mm-entry.ts',"export * from '../../src/mobile/money/logic';");
  await build({entryPoints:['tests/.generated/mm-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mm.mjs',logLevel:'error'});
  const L=await import('./.generated/mm.mjs');
  const {loadScenario,execute,drawer,totals}=M;
  const code=fn=>{try{fn();return 'OK';}catch(e){return e.code||e.message;}};
  const one=[{method:'Cash',amount:'',reference:''}];
  const split=[{method:'Cash',amount:'2,000',reference:''},{method:'UPI',amount:'1400',reference:'DEMO-UPI-0002'}];

  /** S0 days 24–25 (same steps as the stream F oracle), leaving business date 26 Oct with Room 105 at ₹3,400 due, ₹500 deposit. */
  function s0To26(){
    let s=loadScenario('S0');const X=(u,c)=>{s=execute(s,u,c);};
    X('reception',{type:'BOOK',guest:'Rahul Verma (fictional)',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2,source:'Phone',key:'w-book'});
    const id=s.bookings.at(-1).id;
    X('reception',{type:'PAYMENT',id,amount:100000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0001',key:'w-adv'});
    X('reception',{type:'REGISTER',id});X('reception',{type:'CHECKIN',id});
    X('cashier',L.mmPaymentCommand(id,'deposit','500',one,'mm-dep'));
    X('manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});X('manager',{type:'CLOSE_RUN'});
    X('cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});
    X('reception',{type:'CHARGE',id,amount:50000,label:'Laundry service'});
    X('reception',{type:'DISCOUNT',id,amount:50000,reason:'Service delay goodwill'});
    X('manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});X('manager',{type:'CLOSE_RUN'});
    X('cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});
    return {s,id};
  }

  test('[MM] AC-M06: phone split ₹2,000 cash + ₹1,400 UPI, deposit return, checkout, expense, shift close — S0 figures',()=>{
    let {s,id}=s0To26();const X=(u,c)=>{s=execute(s,u,c);};const bk=()=>s.bookings.find(b=>b.id===id);
    assert.equal(s.date,'2026-10-26');assert.equal(drawer(s),1070000);
    assert.equal(totals(bk()).balance,340000);assert.equal(totals(bk()).deposit,50000);
    assert.equal(L.mmBalanceHero(bk()).state,'due');
    assert.equal(L.mmCheckoutChecklist(s,bk()).clear,false);
    // M10: amount prefilled from the balance (₹3,400), "+ Split" → Cash ₹2,000 + UPI ₹1,400
    const total=L.mmRupeeText(totals(bk()).balance);assert.equal(total,'3400');
    assert.equal(L.mmPaymentProblem('payment',total,split,totals(bk()).deposit),'');
    const pay=L.mmPaymentCommand(id,'payment',total,split,'payment:mm-1');
    assert.deepEqual(pay,{type:'PAYMENT',id,amount:340000,kind:'payment',tenders:[{method:'Cash',amount:200000,reference:''},{method:'UPI',amount:140000,reference:'DEMO-UPI-0002'}],key:'payment:mm-1'});
    X('cashier',pay);
    const again=execute(s,'cashier',pay);assert.deepEqual(again,s,'retrying the same request never duplicates');
    const rs=bk().receipts.filter(r=>r.date==='2026-10-26');
    assert.equal(rs.length,2);assert.equal(rs[0].tenderGroup,rs[1].tenderGroup);
    assert.equal(totals(bk()).balance,0);assert.equal(drawer(s),1270000);
    // Return the deposit (single tender, prefilled with the deposit held)
    assert.notEqual(L.mmPaymentProblem('deposit-return','500',split,50000),'');
    X('cashier',L.mmPaymentCommand(id,'deposit-return',L.mmRupeeText(totals(bk()).deposit),one,'payment:mm-2'));
    assert.deepEqual(totals(bk()),{net:400000,tax:40000,total:440000,paid:440000,deposit:0,balance:0});
    assert.equal(drawer(s),1220000);
    assert.equal(L.mmBalanceHero(bk()).state,'settled');
    // M11: checklist all ticked → reception confirms checkout (cashier lacks checkout)
    const c=L.mmCheckoutChecklist(s,bk());assert.equal(c.clear,true);assert.ok(c.items.every(i=>i.ok));
    assert.equal(code(()=>execute(s,'cashier',L.mmCheckoutCommand(id))),'FORBIDDEN');
    X('reception',L.mmCheckoutCommand(id));assert.equal(bk().status,'Departed');assert.ok(bk().invoiceNo);
    assert.deepEqual(execute(s,'reception',L.mmCheckoutCommand(id)),s);
    // M16: Aqua Water Cans ₹300 cash; manager approves → drawer ₹11,900
    X('cashier',L.mmExpenseCommand({vendor:'Aqua Water Cans',category:'Supplies',method:'Cash',amount:'300',receipt:'aqua-water-0026.jpg',notes:''},false,'expense:mm'));
    const e=s.expenses.at(-1);assert.equal(e.status,'Pending');assert.equal(e.amount,30000);assert.equal(e.receipt,'aqua-water-0026.jpg');
    X('manager',{type:'EXPENSE_APPROVE',id:e.id,approve:true,key:'expense:'+e.id});assert.equal(drawer(s),1190000);
    // M15: denomination count 23×₹500 + 2×₹200 = ₹11,900 → exact → shift closes on the phone
    const den=L.mmDenominations({500:23,200:2},'');assert.equal(L.mmCountTotal(den),1190000);
    X('cashier',L.mmShiftSubmitCommand(L.mmCountTotal(den),'',den,'mm-count:1'));
    assert.equal(s.shiftOpen,false);assert.deepEqual(s.shiftClose,{expected:1190000,counted:1190000});assert.equal(M.shiftStatus(s),'Closed');
    X('manager',{type:'CLOSE_RUN'});X('manager',{type:'DAY_CLOSE'});assert.ok(s.closedDates.includes('2026-10-26'));
  });

  test('[MM] Keypad amounts parse to paise; invalid text rejected',()=>{
    assert.equal(L.mmPaise('3400'),340000);assert.equal(L.mmPaise('₹ 2,000'),200000);assert.equal(L.mmPaise('12.5'),1250);
    for(const bad of ['','abc','1.234','-5','.'])assert.ok(Number.isNaN(L.mmPaise(bad)),bad);
    assert.equal(L.mmRupeeText(0),'');assert.equal(L.mmRupeeText(1250),'12.5');
  });

  test('[MM] Payment checks match the command: totals, references, deposit-return limits; single tender = desktop shape',()=>{
    assert.match(L.mmPaymentProblem('payment','3400',[{method:'Cash',amount:'2000',reference:''},{method:'UPI',amount:'1000',reference:'X'}],0),/must be equal/);
    assert.match(L.mmPaymentProblem('payment','100',[{method:'UPI',amount:'',reference:' '}],0),/reference/);
    assert.match(L.mmPaymentProblem('deposit-return','600',one,50000),/exceed the deposit/);
    assert.match(L.mmPaymentProblem('payment','0',one,0),/greater than zero/);
    assert.deepEqual(L.mmPaymentCommand('HCC-1041','payment','100',[{method:'UPI',amount:'',reference:' DEMO-1 '}]),{type:'PAYMENT',id:'HCC-1041',amount:10000,kind:'payment',method:'UPI',reference:'DEMO-1'});
    // the command enforces the same rule the screen shows
    let {s,id}=s0To26();
    assert.equal(code(()=>execute(s,'cashier',L.mmPaymentCommand(id,'deposit-return','600',one))),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',L.mmPaymentCommand(id,'payment','100',one))),'FORBIDDEN');
  });

  test('[MM] Lost response: payment saved once; retry with the same sheet key reports it, never duplicates',()=>{
    const {s,id}=s0To26();const cmd=L.mmPaymentCommand(id,'payment','3400',split,'payment:lost');
    const r=M.executeWithFault(s,'cashier',cmd,'lost');assert.equal(r.error.code,'RESULT_UNKNOWN');
    assert.ok(r.state.processed.includes('payment:lost'),'the phone checks processed keys to show "Saved"');
    const retry=execute(r.state,'cashier',cmd);assert.deepEqual(retry,r.state);
    assert.equal(retry.bookings.find(b=>b.id===id).receipts.filter(x=>x.date==='2026-10-26').length,2);
  });

  test('[MM] Count difference → reason → approval request; no self-approval; manager accepts within limit',()=>{
    let {s}=s0To26();const exp=drawer(s);
    const den=L.mmDenominations({500:21},'50');assert.equal(L.mmCountTotal(den),exp-15000);
    assert.equal(L.mmDiffKind(L.mmCountTotal(den)-exp),'short');
    assert.equal(code(()=>execute(s,'cashier',L.mmShiftSubmitCommand(L.mmCountTotal(den),'',den))),'VALIDATION');
    s=execute(s,'cashier',L.mmShiftSubmitCommand(L.mmCountTotal(den),'₹100 note given as change twice',den,'mm-count:2'));
    const a=s.approvals.at(-1);assert.equal(a.kind,'cash-difference');assert.equal(a.amount,15000);assert.equal(M.shiftStatus(s),'Submitted');
    assert.equal(code(()=>execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'cashier',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true})),'FORBIDDEN');
    s=execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true,key:'shiftdiff:'+a.id});
    assert.equal(M.shiftStatus(s),'Closed');assert.equal(s.shiftClose.counted,exp-15000);
    assert.deepEqual(L.mmDenominations({500:-2,100:1.7},'abc'),{500:0,200:0,100:1,50:0,20:0,10:0,coins:0});
  });

  test('[MM] Checkout checklist names each blocker (nights, balance, deposit, pending request)',()=>{
    let {s,id}=s0To26();
    s=execute(s,'reception',{type:'DISCOUNT',id,amount:60000,reason:'Pending goodwill'});
    const c=L.mmCheckoutChecklist(s,s.bookings.find(b=>b.id===id));
    const ok=Object.fromEntries(c.items.map(i=>[i.id,i.ok]));
    assert.deepEqual(ok,{nights:true,balance:false,deposit:false,requests:false});
    assert.equal(c.clear,false);assert.ok(c.blockers.length>=3);
    const inhouse=loadScenario('S0').bookings.find(b=>b.status==='In house');
    const c2=L.mmCheckoutChecklist(loadScenario('S0'),inhouse);assert.equal(c2.items.find(i=>i.id==='nights').ok,false);
  });

  test('[MM] Expense sheet: draft vs submit keys, duplicate detected for the inline warning',()=>{
    let s=loadScenario('S0');const f={vendor:'Shree Ganesh Supplies (fictional)',category:'Supplies',method:'UPI',amount:'450',receipt:'',notes:''};
    const d=L.mmExpenseCommand(f,true,'expense:k');assert.equal(d.key,'expense:k:draft');assert.equal(d.draft,true);
    s=execute(s,'cashier',L.mmExpenseCommand(f,false,'expense:k'));
    assert.equal(M.expenseDuplicates(s,' shree ganesh supplies (fictional) ',L.mmPaise(f.amount),s.date).length,1);
    s=execute(s,'cashier',d);assert.equal(s.expenses.at(-1).status,'Draft');
    assert.equal(code(()=>execute(s,'housekeeper',L.mmExpenseCommand(f,false))),'FORBIDDEN');
  });
}
