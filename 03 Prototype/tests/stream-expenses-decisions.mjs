// SCC decisions wave · expenses (owner, 27 Oct 2026; Decision Register DEC-129, DEC-130, DEC-131, DEC-135 — binding). Names prefixed [EXP-DEC].
//   DEC-129  budgets and recurring templates are Owner-only (money settings, DEC-058); posting a due template stays with expense users.
//   DEC-130  an Udhaar payment taken in cash is a desk receipt into the open shift's drawer (drawer total, day close, cash book); other modes
//            go to their own ledgers; in the books it settles the receivable (Dr tender · Cr Udhaar receivables), never income twice.
//   DEC-131  the Owner unlocks a locked expense month with a reason; writes need no override after it; figures stale until locked again.
//   DEC-135  GST on an expense bill carries ITC allowed / disallowed (default disallowed): allowed GST goes to the GST input-credit ledger.
import {tillClear} from './_till.mjs';

const code=c=>e=>e?.code===c;
const says=(c,re)=>e=>e?.code===c&&re.test(e.message);

export function register({test,M,assert}){
  const {execute,seed,drawer}=M;
  const D=seed().date;
  const net=(vs,acct)=>vs.reduce((a,v)=>a+v.entries.filter(e=>e.account===acct).reduce((b,e)=>b+e.debit-e.credit,0),0);
  const balanced=vs=>{const t=M.accTotals(vs);return t.debit===t.credit&&vs.every(M.accBalanced);};
  /** The seed day closed by the Owner: arrivals checked in, EXP-102 approved, nights posted, drawer counted by the cashier. */
  const closeDay=s=>{for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=tillClear(M,s);return execute(s,'owner',{type:'DAY_CLOSE'});};
  const credit=(s,amount=250000,name='Ramesh (fictional)')=>{s=execute(s,'cashier',{type:'EXP_UD_ADD',name,amount});return [s,M.expSlice(s).udhaar.at(-1).id];};

  // ── DEC-129 · budgets and recurring templates: Owner only ──
  test('[EXP-DEC] DEC-129 · budgets and recurring templates are set / copied / saved / switched / deleted by the Owner only; others read them and still post',()=>{
    let s={...seed(),expensesX:{budgets:{'2026-09':{Supplies:200000,Laundry:90000}}}};
    for(const u of ['manager','accountant','cashier','reception','techadmin']){
      assert.throws(()=>execute(s,u,{type:'EXP_BUDGET_SET',category:'Supplies',amount:100000}),says('FORBIDDEN',/Only the Owner sets and copies the monthly budgets/),u);
      assert.throws(()=>execute(s,u,{type:'EXP_BUDGET_COPY'}),code('FORBIDDEN'),u);
      assert.throws(()=>execute(s,u,{type:'EXP_REC_SAVE',name:'Rent',amount:100,dueDay:1,category:'Utilities',method:'Bank transfer'}),says('FORBIDDEN',/Only the Owner keeps the recurring templates/),u);
      assert.equal(M.expCanSetMoney(s,u),false,u);}
    assert.equal(M.expCanSetMoney(s,'owner'),true);
    s=execute(s,'owner',{type:'EXP_BUDGET_SET',category:'Supplies',amount:100000});s=execute(s,'owner',{type:'EXP_BUDGET_COPY'});
    assert.throws(()=>execute(s,'manager',{type:'EXP_BUDGET_COPY'}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'EXP_REC_SAVE',name:'Shop rent',amount:2500000,dueDay:1,category:'Utilities',method:'Bank transfer'});
    const t=M.expSlice(s).recurring[0];
    for(const u of ['manager','accountant'])for(const type of ['EXP_REC_TOGGLE','EXP_REC_DELETE'])assert.throws(()=>execute(s,u,{type,id:t.id}),code('FORBIDDEN'),u+' '+type);
    assert.throws(()=>execute(s,'manager',{type:'EXP_REC_SAVE',id:t.id,name:'Shop rent',amount:1,dueDay:1,category:'Utilities',method:'Bank transfer'}),code('FORBIDDEN'));
    // Everyone who could see them still does (the same selectors), and posting a due template stays with expense users — a draft for approval.
    assert.equal(M.expBudgetRows(s,'2026-10',M.mnyCategoryNames(s)).find(r=>r.category==='Supplies').budget,100000);
    for(const u of ['cashier','manager','accountant']){const x=execute(s,u,{type:'EXP_REC_POST',id:t.id,amount:2400000});assert.deepEqual([x.expenses.at(-1).status,x.expenses.at(-1).actor],['Draft',u],u);}
    s=execute(s,'owner',{type:'EXP_REC_TOGGLE',id:t.id});s=execute(s,'owner',{type:'EXP_REC_DELETE',id:t.id});assert.equal(M.expSlice(s).recurring.length,0);});

  // ── DEC-130 · Udhaar cash goes into the drawer ──
  test('[EXP-DEC] DEC-130 · an Udhaar payment in cash is a desk receipt: drawer, expected cash (payments part), shift movements and the cash book include it',()=>{
    let [s,id]=credit(seed());const before=drawer(s),ex0=M.shiftExpected(s);
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:100000,mode:'Cash'});
    assert.match(s.audit[0].detail,/paid ₹1,000 · cash into the drawer \(desk receipt\)/);
    const p=M.expSlice(s).udhaar[0].settlements[0];assert.deepEqual([p.desk,p.mode,p.date],[true,'Cash',D]);
    assert.equal(drawer(s),before+100000);
    const ex=M.shiftExpected(s);assert.equal(ex.payments,ex0.payments+100000,'counted with the receipts');assert.equal(ex.expected,drawer(s));assert.equal(ex.consistent,true);
    const mv=M.shiftMovements(s).find(m=>m.id===p.id);assert.deepEqual([mv.kind,mv.label,mv.amount],['payment','Udhaar received · Ramesh (fictional)',100000]);
    const cb=M.cbDay(s,D);assert.ok(cb.entries.some(e=>e.id===p.id&&e.group==='receipt'&&e.in===100000),'cash book line');assert.equal(cb.reconciles,true);
    // UPI / card / bank: their own ledger, not the drawer — with the transaction reference like any other receipt.
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:50000,mode:'UPI'}),says('VALIDATION',/Enter the UPI transaction reference/));
    const u=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:50000,mode:'UPI',reference:'UPI-DEMO-7'});
    assert.equal(drawer(u),drawer(s));assert.deepEqual(M.expSlice(u).udhaar[0].settlements[1].reference,'UPI-DEMO-7');
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:100,mode:'Vendor credit'}),says('VALIDATION',/Choose how it was paid/));
    // The ₹0.50 overpay tolerance stays.
    const t=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:150050,mode:'Cash'});assert.equal(drawer(t),drawer(s)+150050,'₹0.50 over is taken as typed — and is in the drawer');
    const c=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:150051,mode:'Cash'});assert.equal(drawer(c),drawer(s)+150000,'capped to the outstanding');});
  test('[EXP-DEC] DEC-130 · cash needs the open shift and a desk user (the Accountant records UPI only); a closed day refuses; a settlement saved before stays out of the drawer',()=>{
    let [s,id]=credit(seed());
    assert.throws(()=>execute({...s,shiftOpen:false},'cashier',{type:'EXP_UD_PAY',id,amount:100,mode:'Cash'}),says('INVALID_TRANSITION',/Open cash shift required: Udhaar cash goes into the open shift’s drawer/));
    assert.throws(()=>execute(s,'accountant',{type:'EXP_UD_PAY',id,amount:100,mode:'Cash'}),says('FORBIDDEN',/Udhaar cash goes into the drawer/));
    assert.equal(execute(s,'accountant',{type:'EXP_UD_PAY',id,amount:100,mode:'Bank transfer',reference:'NEFT-1'}).expensesX.udhaar[0].settlements.length,1);
    // DEC-078: Reception / Cashier take cash only on their own open shift.
    const other={...s,cashOutX:{...M.cxSlice(s),opener:{by:'reception',date:s.date}}};
    assert.throws(()=>execute(other,'cashier',{type:'EXP_UD_PAY',id,amount:100,mode:'Cash'}),says('FORBIDDEN',/your own open cashier shift/));
    const closed=closeDay(s);
    assert.throws(()=>execute(closed,'owner',{type:'EXP_UD_PAY',id,amount:100,mode:'Cash'}),code('DAY_CLOSED'));
    // An old save (no desk flag): the payment it recorded never reached the drawer or the books, and still does not.
    const old={...s,expensesX:{...s.expensesX,udhaar:s.expensesX.udhaar.map(r=>({...r,settlements:[{id:'UDP-OLD',amount:5000,date:s.date,mode:'Cash',by:'cashier',at:s.clock}]}))}};
    assert.equal(drawer(old),drawer(s));assert.equal(M.accLiveVouchers(old,s.date).some(v=>v.source.sub==='udhaar-payment'),false);});
  test('[EXP-DEC] DEC-130 · the day close counts the Udhaar cash: expected = drawer, the closing record and the cash book agree',()=>{
    let [s,id]=credit(seed());s=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:120000,mode:'Cash'});
    const closed=closeDay(s),rec=closed.closings[D];
    assert.deepEqual([rec.cash.expected,rec.cash.counted,rec.cash.difference],[M.shiftExpected(s).expected,M.shiftExpected(s).expected,0]);
    const cb=M.cbDay(closed,D);assert.ok(cb.entries.some(e=>e.label==='Udhaar received · Ramesh (fictional)'&&e.in===120000));assert.equal(cb.reconciles,true);
    // A closed day cannot be changed: no further Udhaar payment is dated on it.
    assert.throws(()=>execute(closed,'cashier',{type:'EXP_UD_PAY',id,amount:100,mode:'UPI',reference:'X'}),code('DAY_CLOSED'));});
  test('[EXP-DEC] DEC-130 · accounting: the receipt settles Udhaar receivables (Dr cash / bank · Cr 1120), never income; the cash check and the trial balance hold',()=>{
    let [s,id]=credit(seed());const base=M.accLiveVouchers(s,D);
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:100000,mode:'Cash'});s=execute(s,'cashier',{type:'EXP_UD_PAY',id,amount:50000,mode:'UPI',reference:'UPI-9'});
    const vs=M.accLiveVouchers(s,D),ud=vs.filter(v=>v.source.sub==='udhaar-payment');
    assert.equal(vs.length,base.length+2);
    assert.deepEqual(ud.map(v=>[v.type,v.entries.map(e=>[e.account,e.debit,e.credit])]),[['Receipt',[['1000/TILL',100000,0],['1120/UD-RAMESH-FICTIONAL',0,100000]]],['Receipt',[['1010',50000,0],['1120/UD-RAMESH-FICTIONAL',0,50000]]]]); // ACC-1: party ledgers 1000/<till>, 1120/<customer>
    assert.match(ud[0].narration,/^Udhaar received · Ramesh \(fictional\) · Cash · payment /);
    assert.equal(M.accLedgerName('1120'),'Udhaar receivables (customer credit)');
    for(const code of ['4000','4100'])assert.equal(net(vs,code),net(base,code),'no income from the receipt: '+code);
    assert.ok(balanced(vs));
    const checks=M.accReconcile(s,D);assert.ok(checks.every(c=>c.ok),JSON.stringify(checks.filter(c=>!c.ok)));
    assert.ok(M.accMapRows(s).some(r=>r.key==='receipt:udhaar'&&r.account==='1120'&&r.uses===3)); // ACC-1: two payments + the credit raised (A17)
    const x=M.accTallyXml(s,D,D);assert.match(x.xml,/<LEDGERNAME>Udhaar receivables \(customer credit\)<\/LEDGERNAME><ISDEEMEDPOSITIVE>No<\/ISDEEMEDPOSITIVE><AMOUNT>1000\.00<\/AMOUNT>/);
    assert.equal(x.debit,x.credit);});

  // ── DEC-131 · Owner unlock of an expense month ──
  test('[EXP-DEC] DEC-131 · the Owner unlocks a locked month with a reason: writes need no override, figures stale, lock again regenerates (history kept)',()=>{
    let s=execute(seed(),'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'});
    assert.throws(()=>execute(seed(),'owner',{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:'CA found a missed bill'}),says('INVALID_TRANSITION',/is not locked/));
    for(const u of ['manager','accountant','cashier'])assert.throws(()=>execute(s,u,{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:'CA found a missed bill'}),says('FORBIDDEN',/Only the Owner can unlock/),u);
    assert.throws(()=>execute(s,'owner',{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:' '}),says('VALIDATION',/A reason is required to unlock a month/));
    const add={type:'EXPENSE',description:'Late bill',category:'Supplies',amount:500,method:'UPI'},add2={...add,description:'Second late bill'};
    assert.throws(()=>execute(s,'reception',add),code('FORBIDDEN'),'locked: Owner override needed');
    s=execute(s,'owner',{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:'CA found a missed bill'});
    assert.match(s.audit[0].detail,/2026-10 unlocked · CA found a missed bill/);
    const f=M.expSlice(s).months['2026-10'];
    assert.deepEqual([M.expMonthLocked(s,'2026-10-15'),M.expFeedStale(s,'2026-10'),f.unlockedBy,f.unlockReason,f.unlocks.length],[false,true,'owner','CA found a missed bill',1]);
    assert.throws(()=>execute(s,'owner',{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:'again'}),says('INVALID_TRANSITION',/already unlocked/));
    s=execute(s,'reception',add);assert.equal(s.expenses.at(-1).status,'Pending','no override needed after the unlock');
    assert.doesNotMatch(s.audit[0].detail,/override/);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'});
    const g=M.expSlice(s).months['2026-10'];
    assert.deepEqual([M.expMonthLocked(s,'2026-10-15'),M.expFeedStale(s,'2026-10'),g.unlockedAt,g.unlocks.length,g.expense],[true,false,undefined,1,180500]);
    assert.throws(()=>execute(s,'reception',add2),code('FORBIDDEN'),'locked again');
    s=execute(s,'owner',{type:'EXP_MONTH_UNLOCK',month:'2026-10',reason:'Second late bill'});assert.equal(M.expSlice(s).months['2026-10'].unlocks.length,2);});

  // ── DEC-135 · ITC allowed / disallowed ──
  test('[EXP-DEC] DEC-135 · GST on the bill carries ITC disallowed by default or allowed; none without GST; never allowed above the amount',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'EXPENSE',description:'Cleaning liquid',category:'Supplies',amount:118000,method:'UPI',gstRateBps:1800,gstPaise:18000});
    assert.equal(s.expenses.at(-1).itc,'disallowed','the default: nothing is claimed unless marked');
    s=execute(s,'reception',{type:'EXPENSE',description:'Bulbs',category:'Maintenance',amount:59000,method:'UPI',gstRateBps:1800,gstPaise:9000,itc:'allowed'});
    assert.equal(s.expenses.at(-1).itc,'allowed');
    s=execute(s,'reception',{type:'EXPENSE',description:'Tea',category:'Supplies',amount:5000,method:'UPI',itc:'allowed'});
    assert.equal('itc' in s.expenses.at(-1),false,'no GST → no ITC flag');
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'X',category:'Supplies',amount:5000,method:'UPI',gstPaise:100,itc:'maybe'}),says('VALIDATION',/Choose ITC allowed or ITC disallowed/));
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'Y',category:'Supplies',amount:5000,method:'UPI',gstPaise:6000,confirmGst:true,itc:'allowed'}),says('VALIDATION',/ITC cannot be allowed on more GST than the bill amount/));
    assert.equal(execute(s,'reception',{type:'EXPENSE',description:'Y',category:'Supplies',amount:5000,method:'UPI',gstPaise:6000,confirmGst:true}).expenses.at(-1).itc,'disallowed');});
  test('[EXP-DEC] DEC-135 · posting books ITC allowed GST to the GST input-credit ledger (Dr category net · Dr 1300 · Cr funding), disallowed as before; a reversal mirrors it',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'EXPENSE',description:'Bulbs',category:'Maintenance',amount:59000,method:'UPI',gstRateBps:1800,gstPaise:9000,itc:'allowed'});const a=s.expenses.at(-1).id;
    s=execute(s,'reception',{type:'EXPENSE',description:'Soap',category:'Supplies',amount:118000,method:'Cash',gstRateBps:1800,gstPaise:18000});const d=s.expenses.at(-1).id;
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:a,approve:true});s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:d,approve:true});
    let vs=M.accLiveVouchers(s,D);const va=vs.find(v=>v.source.id===a),vd=vs.find(v=>v.source.id===d);
    assert.deepEqual(va.entries.map(e=>[e.account,e.debit,e.credit]),[['5020',50000,0],['1300',9000,0],['1010',0,59000]]);
    assert.deepEqual(vd.entries.map(e=>[e.account,e.debit,e.credit]),[['5000',118000,0],['1000/TILL',0,118000]],'disallowed: the whole bill to the category');
    assert.equal(M.accLedgerName('1300'),'GST input credit');
    s=execute(s,'owner',{type:'EXPENSE_REVERSE',id:a,reason:'Wrong vendor'});
    const r=s.expenses.find(e=>e.reversalOf===a);assert.deepEqual([r.amount,r.gstPaise,r.itc],[-59000,-9000,'allowed']);
    vs=M.accLiveVouchers(s,D);
    assert.deepEqual(vs.find(v=>v.source.id===r.id).entries.map(e=>[e.account,e.debit,e.credit]),[['5020',0,50000],['1300',0,9000],['1010',59000,0]]);
    assert.equal(net(vs,'1300'),0,'the reversal nets the input credit to zero');
    assert.ok(balanced(vs));
    const checks=M.accReconcile(s,D);assert.ok(checks.find(c=>c.id==='expenses').ok&&checks.every(c=>c.ok),JSON.stringify(checks.filter(c=>!c.ok)));
    assert.ok(M.accTrialBalance(M.accVouchersUpTo(s,D)).reduce((a,r)=>a+r.balance,0)===0,'trial balance balanced');});
  test('[EXP-DEC] DEC-135 · the mapping screen and the Tally XML carry the GST input-credit ledger; the Owner can map it elsewhere',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',description:'Bulbs',category:'Maintenance',amount:59000,method:'UPI',gstRateBps:1800,gstPaise:9000,itc:'allowed'});
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    const row=M.accMapRows(s).find(r=>r.key==='tax:gst-input');assert.deepEqual([row.group,row.value,row.account,row.defaultAccount,row.uses],['GST input credit (ITC)','gst-input','1300','1300',1]);
    const x=M.accTallyXml(s,D,D);assert.match(x.xml,/<LEDGERNAME>GST input credit<\/LEDGERNAME><ISDEEMEDPOSITIVE>Yes<\/ISDEEMEDPOSITIVE><AMOUNT>-90\.00<\/AMOUNT>/);assert.equal(x.debit,x.credit);
    assert.throws(()=>execute(s,'manager',{type:'ACC_MAP_SET',source:'tax:gst-input',account:'5020'}),code('FORBIDDEN'));
    const m=execute(s,'owner',{type:'ACC_MAP_SET',source:'tax:gst-input',account:'5020'});
    assert.deepEqual(M.accLiveVouchers(m,D).find(v=>v.source.kind==='expense'&&v.entries.length===3).entries.map(e=>e.account),['5020','5020','1010']);});
  test('[EXP-DEC] DEC-135 · edit changes the flag while the expense can be edited (logged); budgets count the full bill; GST paid shows allowed and disallowed; CSVs carry ITC',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'EXPENSE',description:'Linen',category:'Laundry',amount:118000,method:'UPI',gstRateBps:1800,gstPaise:18000});const id=s.expenses.at(-1).id;
    s=execute(s,'reception',{type:'EXP_EDIT',id,amount:118000,category:'Laundry',method:'UPI',description:'Linen',gstRateBps:1800,gstPaise:18000,itc:'allowed',reason:'CA: ITC eligible'});
    const e=s.expenses.find(x=>x.id===id);assert.equal(e.itc,'allowed');assert.deepEqual([e.editLog[0].old.itc,e.editLog[0].new.itc],['disallowed','allowed']);
    s=execute(s,'reception',{type:'EXP_EDIT',id,amount:118000,category:'Laundry',method:'UPI',description:'Linen washing',gstRateBps:1800,gstPaise:18000,reason:'Wording'});
    assert.equal(s.expenses.find(x=>x.id===id).itc,'allowed','kept when the edit does not send it');
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});
    assert.throws(()=>execute(s,'owner',{type:'EXP_EDIT',id,amount:118000,category:'Laundry',method:'UPI',description:'Linen',itc:'disallowed',reason:'x'}),code('INVALID_TRANSITION'));
    assert.equal(M.expCategorySpend(s,'Laundry','2026-10'),180000+118000,'budgets keep the full bill');
    s=execute(s,'reception',{type:'EXPENSE',description:'Soap',category:'Supplies',amount:59000,method:'UPI',gstRateBps:1800,gstPaise:9000});s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    assert.deepEqual(M.expGstSplit(s,'2026-10'),{total:27000,allowed:18000,disallowed:9000});
    assert.deepEqual([M.expMonthSummary(s,'2026-10').gstPaid,M.expMonthSummary(s,'2026-10').gstItc],[27000,18000]);
    s=execute(s,'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'});assert.equal(M.expSlice(s).months['2026-10'].gstItc,18000);
    const L=M.expLedgerCsv(s).csv.split('\n'),Mo=M.expMonthCsv(s,'2026-10').csv.split('\n');
    assert.match(L[0],/,Ref,Status,ITC$/);assert.match(Mo[0],/,Ref,Status,ITC$/);
    assert.ok(Mo.some(l=>l.endsWith(`,${id},Posted,allowed`)));assert.ok(Mo.some(l=>l.endsWith(',Posted,disallowed')));assert.ok(L.some(l=>l.endsWith(',EXP-102,Pending,')),'no GST → empty ITC cell');});
  test('[EXP-DEC] DEC-135 · restore migration: an expense saved with GST before the flag is ITC disallowed (shape-detected, idempotent, SCHEMA unchanged)',()=>{
    const s=seed();const old={...s,expenses:[...s.expenses,{id:'EXP-OLD',vendor:'',category:'Supplies',amount:11800,method:'UPI',date:s.date,status:'Posted',actor:'reception',description:'Old bill',gstPaise:1800,gstRateBps:1800,approver:'owner'}]};
    const r=M.restoreState(JSON.stringify(old));assert.equal(r.state.expenses.find(e=>e.id==='EXP-OLD').itc,'disallowed');assert.equal(r.state.schema,M.SCHEMA);assert.equal(M.SCHEMA,11);
    assert.equal(r.state.expenses.find(e=>e.id==='EXP-102').itc,undefined,'no GST, no flag');
    assert.deepEqual(M.restoreState(JSON.stringify(r.state)).state,r.state,'idempotent');
    assert.equal(M.expMigrateItc(r.state),false);});
}
