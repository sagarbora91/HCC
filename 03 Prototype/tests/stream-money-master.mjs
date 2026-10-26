// MA-3 · Money masters (plan Part 2 §8 M15–M18): payment methods, bank accounts and tills, expense categories, expense vendors.
// Domain + command tests through the MA-0 generic commands (MST_MASTER_*). Names prefixed [MNY]. DEC-058: every change is Owner-only;
// DEC-MA-6: expense vendors are their own list (maintenance vendors untouched). Seeds = the old constants, so no figure moves.
import {readFileSync,readdirSync,statSync} from 'node:fs';
import {join} from 'node:path';
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const {seed,execute,drawer,metrics,loadScenario,mstRows,mstSpec,mstSpecsOfGroup,mstSearch,mstHistory,mstVisibleFields,mstCanEdit,MstError,
    mnyMethods,mnyAccounts,mnyCategories,mnyVendors,mnyNames,mnyMethod,mnyIsDrawer,mnyIsCredit,mnyCategory,mnyVendor,mnyCategoryNames,
    mnySettlement,mnyLedgerDefaults,mnyApprovalLimit,mnySettlesTo,accMapping,ACC_DEFAULT_MAP,reportPack,reportTenders,closingOutlet,posDaySummary,
    FOLIO_TENDERS,POS_TENDERS,EXPENSE_METHODS,EXPENSE_CATEGORIES,TENDERS,EXPENSE_MANAGER_LIMIT}=M;
  await writeFile('tests/.generated/mny-mm-entry.ts',"export * from '../../src/mobile/money/logic';");
  await build({entryPoints:['tests/.generated/mny-mm-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mny-mm.mjs',logLevel:'error'});
  const L=await import('./.generated/mny-mm.mjs');
  const reason=r=>e=>e instanceof MstError&&e.reason===r;
  const code=fn=>{try{fn();return 'OK';}catch(e){return e.reason??e.code??e.message;}};
  const save=(s,master,row,u='owner',key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,...(key?{key}:{})});
  const byCode=(s,master,c)=>mstRows(s,master).find(r=>r.code===c);
  const fy=(()=>{let x;return ()=>x??=loadScenario('FY26');})();
  /** A fresh restaurant check with items sent to the kitchen (stream-pos pattern). */
  const table=(s)=>{s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T3',covers:2});const id=s.posX.orders.at(-1).id;
    s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-dal',qty:1});s=execute(s,'cashier',{type:'POS_KOT_SEND',id});return [s,id];};
  /** Owner adds a till, a cash method on it and a wallet method on the bank (front desk + POS). */
  const extra=(s=seed())=>{
    s=save(s,'bankAccounts',{code:'TILL-2',name:'Bar till',kind:'till'});
    s=save(s,'paymentMethods',{code:'BARCASH',name:'Bar cash',kind:'cash',settlesTo:'TILL-2',needsReference:false,usableAt:['frontdesk','pos']});
    s=save(s,'paymentMethods',{code:'PAYTM',name:'Paytm wallet',kind:'wallet',settlesTo:'BANK-1',needsReference:true,usableAt:['frontdesk','pos'],surchargePct:150,ledger:'1010'});
    return s;};

  // ── Registry and seeds ──
  test('[MNY] four money masters registered in "Money and tax": money lists, Owner-only (settings), MA-0 Lists block + search find them',()=>{
    const ids=['paymentMethods','bankAccounts','expenseCategories','expenseVendors'];
    for(const id of ids){const sp=mstSpec(id);assert.ok(sp,id);assert.equal(sp.group,'money');assert.equal(sp.money,true);assert.equal(sp.permission,'settings');}
    assert.deepEqual(mstSpecsOfGroup('money').map(x=>x.id).filter(x=>ids.includes(x)),ids);
    assert.ok(mstSearch('vendors').some(x=>x.id==='expenseVendors'));assert.ok(mstSearch('payment methods').some(x=>x.id==='paymentMethods'));
    assert.ok(mstSearch('ifsc').some(x=>x.id==='bankAccounts'));});
  test('[MNY] payment-method seed = the union of the three old lists (+ Company credit); each place offers exactly its old list',()=>{
    const s=seed();assert.equal(s.mastersX,undefined);
    assert.deepEqual(mnyMethods(s).map(r=>[r.code,r.name,r.kind,r.settlesTo??null]),[['CASH','Cash','cash','TILL'],['UPI','UPI','upi','BANK-1'],['CARD','Card','card','BANK-1'],
      ['BANK','Bank transfer','bank','BANK-1'],['VCREDIT','Vendor credit','credit',null],['COMPANY','Company credit','credit',null]]);
    assert.deepEqual(mnyNames(s,'frontdesk'),[...FOLIO_TENDERS]);assert.deepEqual(mnyNames(s,'frontdesk'),TENDERS);
    assert.deepEqual(mnyNames(s,'pos'),[...POS_TENDERS]);assert.deepEqual(mnyNames(s,'expenses'),EXPENSE_METHODS);
    assert.deepEqual(mnyCategoryNames(s),EXPENSE_CATEGORIES);
    assert.deepEqual(mnyAccounts(s).map(a=>[a.code,a.kind]),[['TILL','till'],['BANK-1','bank']]);});
  test('[MNY] only Cash settles to the till; Vendor credit and Company credit are credit (no drawer, no bank)',()=>{
    const s=seed();
    assert.deepEqual(['Cash','UPI','Card','Bank transfer','Vendor credit','Company credit'].map(m=>[mnyIsDrawer(s,m),mnyIsCredit(s,m),mnySettlesTo(s,m)?.code??null]),
      [[true,false,'TILL'],[false,false,'BANK-1'],[false,false,'BANK-1'],[false,false,'BANK-1'],[false,true,null],[false,true,null]]);
    assert.equal(mnyIsDrawer(s,'Bitcoin'),false);assert.equal(mnyMethod(s,'cash')?.code,'CASH');assert.equal(mnyMethod(s,'BANK')?.name,'Bank transfer');});
  test('[MNY] expense vendors seeded from the free-text vendors on the expenses (BASE 2, FY26 16), codes V-…, types guessed',()=>{
    assert.deepEqual(mnyVendors(seed()).map(v=>[v.code,v.name,v.type]),[['V-FRESH-LINEN-SERVIC','Fresh Linen Services','laundry'],['V-MORNING-SUPPLIES','Morning Supplies','supplier']]);
    const f=fy(),vs=mnyVendors(f);assert.equal(vs.length,16);assert.equal(new Set(vs.map(v=>v.code)).size,16);
    assert.ok(vs.every(v=>/^[A-Z0-9-]{1,20}$/.test(v.code)));
    assert.equal(mnyVendor(f,'Marathwada Power Supply (fictional)').type,'utility');});
  test('[MNY] every historical record resolves: FY26 folio receipts, POS receipts, expense methods, categories and vendors',()=>{
    const f=fy();
    const bad=[...f.bookings.flatMap(b=>b.receipts.map(r=>r.method)),...(f.posX?.receipts??[]).map(r=>r.method),...f.expenses.map(e=>e.method)].filter(m=>!mnyMethod(f,m));
    assert.deepEqual([...new Set(bad)],[]);
    assert.deepEqual(f.expenses.filter(e=>!mnyCategory(f,e.category)).map(e=>e.category),[]);
    assert.deepEqual(f.expenses.filter(e=>!mnyVendor(f,e.vendor)).map(e=>e.vendor),[]);
    assert.equal(mstSpec('paymentMethods').inUse(f,byCode(f,'paymentMethods','COMPANY')).count,5);});
  test('[MNY] golden figures unchanged (occupancy 8, collections ₹21,500, drawer ₹10,725) and accounting defaults identical',()=>{
    const s=seed();assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(drawer(s),1072500);
    for(const [k,v] of Object.entries(mnyLedgerDefaults(s)))assert.equal(ACC_DEFAULT_MAP[k],v,k);
    assert.deepEqual(accMapping(s),{...ACC_DEFAULT_MAP});});
  test('[MNY] FY26 report pack cash split equals the old method rule (Cash / UPI+Bank / Vendor credit) over a month',()=>{
    const f=fy(),from='2026-06-01',to='2026-06-30',p=reportPack(f,from,to),ex=p.tabs.find(t=>t.id==='expenses');
    const exp=f.expenses.filter(e=>(e.status==='Posted'||e.status==='Reversed')&&e.date>=from&&e.date<=to),sum=xs=>xs.reduce((a,e)=>a+e.amount,0);
    const val=id=>ex.metrics.find(m=>m.id===id).value;
    assert.equal(val('cash'),sum(exp.filter(e=>e.method==='Cash')));assert.equal(val('noncash'),sum(exp.filter(e=>e.method==='UPI'||e.method==='Bank transfer')));
    assert.equal(val('vendor'),sum(exp.filter(e=>e.method==='Vendor credit')));
    const rec=f.bookings.flatMap(b=>b.receipts).filter(r=>r.date>=from&&r.date<=to&&r.method==='Cash'&&(r.kind==='payment'||r.kind==='deposit'));
    const pos=(f.posX?.receipts??[]).filter(r=>r.method==='Cash'&&r.kind==='sale'&&r.date>=from&&r.date<=to);
    assert.equal(val('cash-in'),sum(rec)+sum(pos));
    assert.deepEqual(reportTenders(f,from,to).slice(0,4),['Cash','UPI','Card','Bank transfer']);
    assert.deepEqual(Object.keys(closingOutlet(f,'2026-06-15').byMethod),['Cash','UPI','Card']);
    assert.deepEqual(Object.keys(posDaySummary(f,'2026-06-15').byMode),['Cash','UPI','Card','Room folio']);});

  // ── Payment methods ──
  test('[MNY] Owner adds a wallet method: offered at the front desk and POS, needs a reference, no drawer effect, ledger default mapped',()=>{
    let s=extra();const before=drawer(s);
    assert.deepEqual(mnyNames(s,'frontdesk'),['Cash','UPI','Card','Bank transfer','Bar cash','Paytm wallet']);
    assert.deepEqual(L.mmTenders(s).map(t=>t.label),['Cash','UPI','Card','Bank','Bar cash','Paytm wallet']);
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:10000,method:'Paytm wallet'}),/reference/);
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:10000,method:'PAYTM',reference:'DEMO-PAYTM-1'});
    assert.equal(s.bookings.find(b=>b.id==='HCC-1041').receipts.at(-1).method,'Paytm wallet');assert.equal(drawer(s),before);
    assert.equal(accMapping(s)['method:Paytm wallet'],'1010');
    assert.equal(L.mmPaymentProblem('payment','100',[{method:'Paytm wallet',amount:'',reference:''}],0,s).includes('reference'),true);});
  test('[MNY] a cash method on a second till counts in the drawer (settles to a till); the settles-to view groups it under that till',()=>{
    let s=extra();const before=drawer(s);
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:20000,method:'Bar cash'});
    assert.equal(drawer(s),before+20000);assert.equal(mnyIsDrawer(s,'Bar cash'),true);
    const g=mnySettlement(s,s.date,s.date);assert.deepEqual(g.map(x=>x.account),['TILL','TILL-2','BANK-1']);
    assert.deepEqual(g.find(x=>x.account==='TILL-2').lines,[{method:'Bar cash',in:20000,out:0,count:1}]);});
  test('[MNY] settles-to view (BASE): till = cash receipts + outlet cash − cash expenses; bank = UPI; foots to the records',()=>{
    const s=seed(),g=mnySettlement(s,s.date,s.date),till=g.find(x=>x.account==='TILL'),bank=g.find(x=>x.account==='BANK-1');
    const sum=xs=>xs.reduce((a,r)=>a+r.amount,0),rec=s.bookings.flatMap(b=>b.receipts).filter(r=>r.date===s.date);
    assert.equal(till.in,sum(rec.filter(r=>r.method==='Cash'))+sum(s.posX.receipts.filter(r=>r.method==='Cash'&&r.date===s.date)));
    assert.equal(till.out,sum(s.expenses.filter(e=>e.method==='Cash'&&e.date===s.date&&(e.status==='Posted'||e.status==='Reversed'))));
    assert.equal(bank.in,sum(rec.filter(r=>r.method==='UPI'))+sum(s.posX.receipts.filter(r=>r.method==='UPI'&&r.date===s.date)));
    assert.equal(till.label,'Front-desk cash drawer');});
  test('[MNY] rules: cash settles to a till, credit to nothing, others to a bank; credit is not a guest payment',()=>{
    const s=seed();
    assert.throws(()=>save(s,'paymentMethods',{code:'X1',name:'X1',kind:'cash',settlesTo:'BANK-1',usableAt:['frontdesk']}),e=>reason('MASTER_INVALID')(e)&&/till/.test(e.message));
    assert.throws(()=>save(s,'paymentMethods',{code:'X2',name:'X2',kind:'credit',settlesTo:'BANK-1',usableAt:['expenses']}),/no account/);
    assert.throws(()=>save(s,'paymentMethods',{code:'X3',name:'X3',kind:'upi',usableAt:['frontdesk']}),/bank account/);
    assert.throws(()=>save(s,'paymentMethods',{code:'X4',name:'X4',kind:'credit',usableAt:['frontdesk']}),/not a guest payment/);
    assert.throws(()=>save(s,'paymentMethods',{code:'X5',name:'X5',kind:'upi',settlesTo:'BANK-1',usableAt:['pos'],surchargePct:1500}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'paymentMethods',{code:'upi',name:'UPI 2',kind:'upi',settlesTo:'BANK-1',usableAt:['pos']}),reason('MASTER_CODE_TAKEN'));});
  test('[MNY] a used method keeps its kind, settles-to and code (old drawer and bank figures cannot move)',()=>{
    const s=seed(),cash=byCode(s,'paymentMethods','CASH');
    assert.throws(()=>save(s,'paymentMethods',{...cash,kind:'upi',settlesTo:'BANK-1'}),e=>reason('MASTER_INVALID')(e)&&/cannot change once the method is used/.test(e.message));
    assert.throws(()=>save(s,'paymentMethods',{...cash,code:'CASH2'}),reason('MASTER_IN_USE'));
    const s2=save(s,'paymentMethods',{code:'GPAY',name:'GPay',kind:'upi',settlesTo:'BANK-1',usableAt:['frontdesk']});const g=byCode(s2,'paymentMethods','GPAY');
    const s3=save(s2,'paymentMethods',{...g,kind:'wallet'});assert.equal(byCode(s3,'paymentMethods','GPAY').kind,'wallet');});
  test('[MNY] rename UPI → "UPI (GPay)": old receipts still resolve and count; new receipts store the new name; history + audit',()=>{
    let s=seed();const upi=byCode(s,'paymentMethods','UPI'),d0=drawer(s),c0=metrics(s).collections;
    s=save(s,'paymentMethods',{...upi,name:'UPI (GPay)'});
    assert.equal(s.audit[0].detail,'Master Payment methods: name UPI → UPI (GPay)');
    assert.equal(mnyMethod(s,'UPI').code,'UPI');assert.equal(drawer(s),d0);assert.equal(metrics(s).collections,c0);
    assert.deepEqual(mnyNames(s,'frontdesk'),['Cash','UPI (GPay)','Card','Bank transfer']);
    assert.equal(mstSpec('paymentMethods').inUse(s,byCode(s,'paymentMethods','UPI')).count,mstSpec('paymentMethods').inUse(seed(),upi).count);
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:5000,method:'UPI',reference:'DEMO-R'});
    assert.equal(s.bookings.find(b=>b.id==='HCC-1041').receipts.at(-1).method,'UPI (GPay)');
    assert.deepEqual(mstHistory(s,'paymentMethods',upi.id).map(h=>[h.field,h.old,h.new]),[['name','UPI','UPI (GPay)']]);});
  test('[MNY] retire Card: refused at the POS and front desk (MASTER_RETIRED), gone from pick lists, old records keep the name',()=>{
    let s=seed();const card=byCode(s,'paymentMethods','CARD');
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'paymentMethods',id:card.id});
    assert.deepEqual(mnyNames(s,'pos'),['Cash','UPI']);assert.deepEqual(mnyNames(s,'frontdesk'),['Cash','UPI','Bank transfer']);
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Card',reference:'X'}),reason('MASTER_RETIRED'));
    const [t,id]=table(s);assert.throws(()=>execute(t,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Card',reference:'X'}),reason('MASTER_RETIRED'));
    assert.equal(mnyMethod(s,'Card').active,false);});
  test('[MNY] POS settle: "Select Cash, UPI or Card." for an unknown method; a new POS wallet needs its reference',()=>{
    const [b,bid]=table(seed());assert.throws(()=>execute(b,'cashier',{type:'POS_SETTLE',id:bid,mode:'outlet',method:'Cheque',reference:'x'}),/Select Cash, UPI or Card\./);
    let [s,id]=table(extra());
    assert.throws(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Paytm wallet'}),/reference/);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'PAYTM',reference:'DEMO-PT'});
    assert.equal(s.posX.receipts.at(-1).method,'Paytm wallet');});
  test('[MNY] the last front-desk method cannot be retired; an unused method can be deleted, a used one cannot',()=>{
    let s=seed();for(const c of ['UPI','CARD','BANK'])s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'paymentMethods',id:byCode(s,'paymentMethods',c).id});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'paymentMethods',id:byCode(s,'paymentMethods','CASH').id}),reason('MASTER_IN_USE'));
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_DELETE',master:'paymentMethods',id:'pm-cash'}),reason('MASTER_IN_USE'));
    let t=save(seed(),'paymentMethods',{code:'NEFT',name:'NEFT',kind:'bank',settlesTo:'BANK-1',usableAt:['expenses']});
    t=execute(t,'owner',{type:'MST_MASTER_DELETE',master:'paymentMethods',id:byCode(t,'paymentMethods','NEFT').id});assert.equal(byCode(t,'paymentMethods','NEFT'),undefined);});
  test('[MNY] Owner only (DEC-058): Manager, Tech admin, Accountant, Reception are refused; repeat with the same key is idempotent',()=>{
    for(const u of ['manager','techadmin','accountant','reception','cashier','housekeeper'])
      assert.equal(code(()=>save(seed(),'expenseVendors',{code:'V-X',name:'X',type:'other',tds:false},u)),'FORBIDDEN',u);
    assert.equal(mstCanEdit(seed(),'owner',mstSpec('paymentMethods')).ok,true);assert.equal(mstCanEdit(seed(),'techadmin',mstSpec('bankAccounts')).ok,false);
    const a=save(seed(),'expenseVendors',{code:'V-X',name:'X traders',type:'supplier',tds:false},'owner','k-v');
    assert.deepEqual(execute(a,'owner',{type:'MST_MASTER_SAVE',master:'expenseVendors',row:{code:'V-X',name:'X traders',type:'supplier',tds:false},key:'k-v'}),a);});
  test('[MNY] money fields hidden from housekeeping / maintenance (DEC-061): surcharge, opening balance, approval limit',()=>{
    const s=seed(),keys=(u,id)=>mstVisibleFields(s,u,mstSpec(id)).map(f=>f.key);
    for(const u of ['housekeeper','maint']){assert.ok(!keys(u,'paymentMethods').includes('surchargePct'));assert.ok(!keys(u,'bankAccounts').includes('openingBalance'));
      assert.ok(!keys(u,'expenseCategories').includes('approvalLimit'));}
    assert.ok(keys('owner','bankAccounts').includes('openingBalance'));});

  // ── Bank accounts and tills ──
  test('[MNY] bank accounts: IFSC, last 4 digits, till has no bank fields, one default per kind; IFSC stored in capitals',()=>{
    const s=seed();
    assert.throws(()=>save(s,'bankAccounts',{code:'B2',name:'Second',kind:'bank',bank:'Demo',ifsc:'ABC123'}),/IFSC/);
    assert.throws(()=>save(s,'bankAccounts',{code:'B2',name:'Second',kind:'bank',bank:'Demo',accountLast4:'12345'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'bankAccounts',{code:'B2',name:'Second',kind:'bank',bank:'Demo',accountLast4:'12ab'}),/last 4 digits/);
    assert.throws(()=>save(s,'bankAccounts',{code:'T2',name:'Till 2',kind:'till',bank:'Demo'}),/till has no bank/);
    assert.throws(()=>save(s,'bankAccounts',{code:'B2',name:'Second',kind:'bank',bank:'Demo',defaultFor:['upi']}),/already the default/);
    const t=save(s,'bankAccounts',{code:'B2',name:'Second',kind:'bank',bank:'Demo',ifsc:'abcd0123456',accountLast4:'9876',openingBalance:500000});
    assert.equal(byCode(t,'bankAccounts','B2').ifsc,'ABCD0123456');assert.equal(byCode(t,'bankAccounts','B2').openingBalance,500000);});
  test('[MNY] an account that active methods settle to cannot be retired; the opening balance is setup-only once records settle through it',()=>{
    const s=seed(),bank=byCode(s,'bankAccounts','BANK-1');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'bankAccounts',id:'ba-till'}),e=>reason('MASTER_IN_USE')(e)&&/Cash/.test(e.message));
    assert.throws(()=>save(s,'bankAccounts',{...bank,openingBalance:100000}),/setup-only/);
    assert.equal(mstSpec('bankAccounts').inUse(s,bank).count,3);});

  // ── Expense categories ──
  test('[MNY] a new category with its ledger maps the accounting key (Kitchen gas → 5050) and is offered on the form',()=>{
    let s=save(seed(),'expenseCategories',{code:'GAS',name:'Kitchen gas',ledger:'5050',needsBill:false});
    assert.equal(accMapping(s)['expense:Kitchen gas'],'5050');assert.deepEqual(mnyCategoryNames(s),[...EXPENSE_CATEGORIES,'Kitchen gas']);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Gas Agency (fictional)',category:'GAS',amount:120000,method:'Cash'});
    assert.equal(s.expenses.at(-1).category,'Kitchen gas');
    assert.throws(()=>save(seed(),'expenseCategories',{code:'X',name:'X',ledger:'1000',needsBill:false}),reason('MASTER_INVALID'));});
  test('[MNY] expense category rules: unknown → VALIDATION, retired → MASTER_RETIRED, needs-bill → bill number on submit (draft allowed)',()=>{
    let s=seed();
    assert.equal(code(()=>execute(s,'reception',{type:'EXPENSE',vendor:'X',category:'Repairs',amount:100,method:'Cash'})),'VALIDATION');
    const sup=byCode(s,'expenseCategories','SUPPLIES');let r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'expenseCategories',id:sup.id});
    assert.throws(()=>execute(r,'reception',{type:'EXPENSE',vendor:'X',category:'Supplies',amount:100,method:'Cash'}),reason('MASTER_RETIRED'));
    assert.deepEqual(mnyCategoryNames(r,'Supplies'),EXPENSE_CATEGORIES);assert.ok(!mnyCategoryNames(r).includes('Supplies'));
    s=save(s,'expenseCategories',{...byCode(s,'expenseCategories','UTIL'),needsBill:true});
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',vendor:'Water Board',category:'Utilities',amount:100,method:'UPI'}),/bill/);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Water Board',category:'Utilities',amount:100,method:'UPI',draft:true});
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE_SUBMIT',id:s.expenses.at(-1).id}),/bill/);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Water Board',category:'Utilities',amount:100,method:'UPI',receipt:'WB-1'});assert.equal(s.expenses.at(-1).status,'Pending');});
  test('[MNY] category approval limit overrides the Manager limit (₹1,000 for Petty cash); the Owner still approves above it',()=>{
    let s=save(seed(),'expenseCategories',{...byCode(seed(),'expenseCategories','PETTY'),approvalLimit:100000});
    assert.equal(mnyApprovalLimit(s,'Petty cash',EXPENSE_MANAGER_LIMIT),100000);assert.equal(mnyApprovalLimit(s,'Supplies',EXPENSE_MANAGER_LIMIT),EXPENSE_MANAGER_LIMIT);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Tea & Snacks Counter',category:'Petty cash',amount:200000,method:'Cash'});const id=s.expenses.at(-1).id;
    assert.throws(()=>execute(s,'manager',{type:'EXPENSE_APPROVE',id,approve:true}),/Owner approval/);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});assert.equal(s.expenses.find(e=>e.id===id).status,'Posted');});
  test('[MNY] a new credit funding source behaves like Vendor credit: no drawer, Journal voucher, "vendor credit" report line',()=>{
    let s=save(seed(),'paymentMethods',{code:'SUPPACC',name:'Supplier account',kind:'credit',needsReference:false,usableAt:['expenses'],ledger:'2200'});
    const d=drawer(s);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Hardware Corner',category:'Maintenance',amount:30000,method:'Supplier account'});
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    assert.equal(drawer(s),d);assert.equal(accMapping(s)['funding:Supplier account'],'2200');
    const v=M.accVouchers(s,s.date).find(v=>v.source.id===s.expenses.at(-1).id);assert.equal(v.type,'Journal');
    assert.equal(reportPack(s,s.date,s.date).tabs.find(t=>t.id==='expenses').metrics.find(m=>m.id==='vendor').value,30000);});

  // ── Expense vendors ──
  test('[MNY] expense vendor: a typed name in other case is stored as the list name; free text kept; retired vendor refused',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',vendor:'morning supplies',category:'Supplies',amount:100,method:'Cash'});
    assert.equal(s.expenses.at(-1).vendor,'Morning Supplies');
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Brand New Shop',category:'Supplies',amount:100,method:'Cash'});assert.equal(s.expenses.at(-1).vendor,'Brand New Shop');
    const v=byCode(s,'expenseVendors','V-MORNING-SUPPLIES');const r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'expenseVendors',id:v.id});
    assert.throws(()=>execute(r,'reception',{type:'EXPENSE',vendor:'Morning Supplies',category:'Supplies',amount:100,method:'Cash'}),reason('MASTER_RETIRED'));});
  test('[MNY] vendor GSTIN / PAN / TDS rules; stored in capitals; PAN must match the GSTIN',()=>{
    const s=seed(),base={code:'V-ACME',name:'Acme Linen',type:'laundry'};
    assert.throws(()=>save(s,'expenseVendors',{...base,gstin:'27ABCDE1234F1Z',tds:false}),/GSTIN/);
    assert.throws(()=>save(s,'expenseVendors',{...base,pan:'ABCD1234F',tds:false}),/PAN/);
    assert.throws(()=>save(s,'expenseVendors',{...base,tds:true}),/PAN when TDS/);
    assert.throws(()=>save(s,'expenseVendors',{...base,gstin:'27ABCDE1234F1Z5',pan:'ZZZZZ9999Z',tds:false}),/does not match/);
    const t=save(s,'expenseVendors',{...base,gstin:'27abcde1234f1z5',pan:'abcde1234f',tds:true,contact:'Ravi · 98xxxxxx01',terms:'15 days'});
    const r=byCode(t,'expenseVendors','V-ACME');assert.deepEqual([r.gstin,r.pan,r.tds],['27ABCDE1234F1Z5','ABCDE1234F',true]);});
  test('[MNY] rename a vendor: old expenses still resolve and count as used; maintenance vendors untouched (DEC-MA-6)',()=>{
    const f=fy(),v=byCode(f,'expenseVendors',mnyVendor(f,'Sai Provisions').code),before=mstSpec('expenseVendors').inUse(f,v).count;
    const mnt=JSON.stringify(f.maintenanceX);
    const s=save(f,'expenseVendors',{...v,name:'Sai Provisions & Kirana'});
    assert.equal(mnyVendor(s,'Sai Provisions').name,'Sai Provisions & Kirana');assert.equal(mstSpec('expenseVendors').inUse(s,byCode(s,'expenseVendors',v.code)).count,before);
    assert.ok(before>0);assert.equal(JSON.stringify(s.maintenanceX),mnt);
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'expenseVendors',id:v.id}),reason('MASTER_IN_USE'));});

  // ── Wiring and legacy constants ──
  test('[MNY] the old hard-coded lists are only seeds: no screen or handler reads FOLIO_TENDERS, POS_TENDERS, EXPENSE_METHODS, EXPENSE_CATEGORIES, MM_TENDERS',()=>{
    const walk=d=>readdirSync(d).flatMap(n=>{const p=join(d,n);return statSync(p).isDirectory()?walk(p):/\.tsx?$/.test(n)?[p]:[];});
    const hits=[];
    for(const f of walk('src')){const t=readFileSync(f,'utf8');
      for(const c of ['FOLIO_TENDERS','POS_TENDERS','EXPENSE_METHODS','EXPENSE_CATEGORIES','MM_TENDERS','TENDERS.map','TENDERS.includes'])
        for(const line of t.split('\n'))if(line.includes(c)&&!/LEGACY|export const|MA-3|seed|\/\/|\/\*\*/.test(line))hits.push(`${f}: ${line.trim().slice(0,90)}`);}
    assert.deepEqual(hits,[]);
    for(const f of ['src/domain/streams/folio.ts','src/domain/streams/pos.ts','src/domain/streams/closing.ts'])assert.match(readFileSync(f,'utf8'),/LEGACY \(MA-3\)/);});
  test('[MNY] screens use the masters: payment dialog, POS settle, expense forms (desktop + phone), cash book settles-to view',()=>{
    const src=f=>readFileSync(f,'utf8');
    assert.match(src('src/features/folio/modals.tsx'),/mnyNames\(s,'frontdesk'\)/);assert.match(src('src/features/pos/modals.tsx'),/mnyNames\(s,'pos'\)/);
    assert.match(src('src/features/expenses/ExpenseModal.tsx'),/ExpVendorInput/);assert.match(src('src/mobile/money/Expenses.tsx'),/ExpVendorInput phone/);
    assert.match(src('src/mobile/money/Payment.tsx'),/mmTenders\(s\)/);assert.match(src('src/features/cashbook/CashBook.tsx'),/<MnySettlesTo/);
    assert.match(src('src/clients/sync/registry.ts'),/moneyMasters, \/\/ MA-3/);});
  // ── Screens (server-side render with the app context, desktop + phone) ──
  await writeFile('tests/.generated/mny-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {navItems} from '../../src/app/nav'; // same import order as App.tsx (nav before registry)
import '../../src/app/registry';
import {ExpenseModal} from '../../src/features/expenses/ExpenseModal';
import {MmExpenseSheet} from '../../src/mobile/money/Expenses';
import {PaymentModal} from '../../src/features/folio/modals';
import {MmPayment} from '../../src/mobile/money/Payment';
import {PosSettleModal} from '../../src/features/pos/modals';
import {MasterList,MstListsBlock} from '../../src/features/masters/MasterList';
import {MasterListPhone} from '../../src/mobile/masters/MasterListPhone';
import {MnySettlesTo} from '../../src/features/money/masters';
import {CashBookView} from '../../src/features/cashbook/CashBook';
function ctx(s:any,uid:string,mobile:boolean){const user=s.users.find((u:any)=>u.id===uid);const noop=()=>{};
  return {s,uid,user,m:metrics(s),busy:false,run:async()=>true,open:noop,go:noop,booking:noop,closeModal:noop,toast:noop,setDirty:noop,error:'',modal:{key:'k',type:'x'},
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
const r=(s:any,uid:string,el:any,mobile=false)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
void navItems;
export const ui={
  expense:(s:any,u:string)=>r(s,u,<ExpenseModal/>),expensePhone:(s:any,u:string)=>r(s,u,<MmExpenseSheet/>,true),
  payment:(s:any,u:string,id:string)=>r(s,u,<PaymentModal id={id}/>),paymentPhone:(s:any,u:string,id:string)=>r(s,u,<MmPayment id={id}/>,true),
  settle:(s:any,u:string,id:string)=>r(s,u,<PosSettleModal id={id}/>),
  list:(s:any,u:string,spec:string)=>r(s,u,<MasterList spec={spec}/>),listPhone:(s:any,u:string,spec:string)=>r(s,u,<MasterListPhone spec={spec}/>,true),
  block:(s:any,u:string)=>r(s,u,<MstListsBlock g="money"/>),
  settles:(s:any,u:string,f:string,t:string)=>r(s,u,<MnySettlesTo from={f} to={t}/>),cashbook:(s:any,u:string,phone=false)=>r(s,u,<CashBookView phone={phone}/>,phone)};
`);
  await build({entryPoints:['tests/.generated/mny-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mny-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __mnyReq} from 'node:module';const require=__mnyReq(import.meta.url);"}});
  const {ui}=await import('./.generated/mny-ui.mjs');
  test('[MNY-UI] expense form (desktop + phone): categories, funding sources and vendor list from the masters; Owner gets one-tap "add to list"',()=>{
    let s=save(seed(),'expenseCategories',{code:'GAS',name:'Kitchen gas',ledger:'5050',needsBill:true});
    const d=ui.expense(s,'owner'),p=ui.expensePhone(s,'manager');
    for(const h of [d,p]){assert.match(h,/<option[^>]*>Kitchen gas<\/option>/);assert.match(h,/<datalist/);assert.match(h,/value="Morning Supplies"/);assert.match(h,/Drawer cash/);}
    assert.match(d,/Vendor credit \(pay later\)/);assert.match(p,/>Bank</);assert.doesNotMatch(d+p,/undefined|NaN/);});
  test('[MNY-UI] payment dialogs and POS settle offer the master methods (new wallet shown, retired Card gone)',()=>{
    let s=extra();s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'paymentMethods',id:byCode(s,'paymentMethods','CARD').id});
    const d=ui.payment(s,'cashier','HCC-1041'),p=ui.paymentPhone(s,'cashier','HCC-1041');
    assert.match(d,/<option[^>]*>Paytm wallet<\/option>/);assert.doesNotMatch(d,/<option[^>]*>Card<\/option>/);
    assert.match(p,/Paytm wallet/);assert.doesNotMatch(p,/>Card</);
    const [t,id]=table(s);const st=ui.settle(t,'cashier',id);assert.match(st,/Paytm wallet/);assert.match(st,/Bar cash/);assert.doesNotMatch(st,/>Card</);});
  test('[MNY-UI] Master configuration › Money and tax lists: Owner edits; Manager sees "Owner changes this"; phone list renders cards',()=>{
    const s=seed();const b=ui.block(s,'owner');for(const l of ['Payment methods','Bank accounts and tills','Expense categories','Expense vendors'])assert.ok(b.includes(l),l);
    for(const id of ['paymentMethods','bankAccounts','expenseCategories','expenseVendors']){
      assert.match(ui.list(s,'owner',id),/Add /);const m=ui.list(s,'manager',id);assert.match(m,/Owner changes this/);assert.doesNotMatch(m,/Add (payment|account|expense)/);
      const ph=ui.listPhone(s,'owner',id);assert.match(ph,/m-list-card|mst-m-row/);assert.doesNotMatch(ph,/<table/);}
    assert.match(ui.list(fy(),'owner','expenseVendors'),/Stationers &amp; Co/);});
  test('[MNY-UI] cash book shows "By account · settles to" (desktop + phone) with the till and the bank account (masked number)',()=>{
    const s=seed(),d=ui.cashbook(s,'owner'),p=ui.cashbook(s,'owner',true);
    for(const h of [d,p]){assert.match(h,/By account · settles to/);assert.match(h,/Front-desk cash drawer/);assert.match(h,/Hotel current account/);assert.match(h,/XXXX 4321/);}
    assert.doesNotMatch(p,/mny-settle[^"]*"[^>]*><table/);});
}
