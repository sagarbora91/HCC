// ACC-2 · lane acc2-purchase: purchases as Purchase vouchers, IGST to 1330, the ITC guard (DEC-143), reverse charge (DEC-145, C8), capital
// purchases and the asset register (C10), vendor payments / advances / voids (C3, C4), returns (C5), ageing by allocations, the category /
// vendor master fields, TDS lines (the acc2-tds helper is stubbed to 0 here; lines are tested from a record that carries tdsPaise).
// Contract: 02 Planning/agents/ACC2-POSTING-CONTRACT.md §3, §9. Names prefixed [ACC2-PURCHASE].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const D=seed().date;
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  const balanced=vs=>{const t=M.accTotals(vs);return t.debit===t.credit&&vs.every(M.accBalanced);};
  const vs=(s,d=s.date)=>M.accLiveVouchers(s,d);
  /** The seed with extra expense categories / vendors stored in the money masters (rows = seed + extras). */
  function withMasters({cats=[],vens=[]}={}){
    const s=structuredClone(seed());
    const c=[...M.mnyCategories(s),...cats.map((x,i)=>({id:'ec-x'+i,sortOrder:200+i,active:true,needsBill:false,...x}))];
    const v=[...M.mnyVendors(s),...vens.map((x,i)=>({id:'ev-x'+i,sortOrder:200+i,active:true,type:'supplier',tds:false,...x}))];
    s.mastersX={...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),expenseCategories:c,expenseVendors:v}};
    return s;
  }
  const CATS=[{code:'SECURITY',name:'Security services',ledger:'5050',rcm:true,rcmBps:1800},{code:'FOOD',name:'Staff food',ledger:'5120',itcBlocked:true},
    {code:'FURN',name:'Furniture',ledger:'1510',capital:true},{code:'BANQ',name:'Banquet supplies',ledger:'5000'},{code:'CAR',name:'Vehicles',ledger:'1540',capital:true,itcBlocked:true}];
  const VENS=[{code:'V-KAVERI',name:'Kaveri Kitchen Equipment (fictional)',state:'KA'},{code:'V-SHIELD',name:'Shield Security Agency (fictional)',state:'MH'},
    {code:'V-DECCAN',name:'Deccan Agro Traders (fictional)',state:'MH'}];
  const base=()=>withMasters({cats:CATS,vens:VENS});
  /** Reception submits a bill, the Owner approves it; returns [state, expense id]. */
  function bill(s,x){s=execute(s,'reception',{type:'EXPENSE',description:x.description??'Bill',vendor:x.vendor,category:x.category,amount:x.amount,method:x.method??'Vendor credit',...x.extra});
    const id=s.expenses.at(-1).id;s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});return [s,id];}
  const vOf=(s,id)=>vs(s).find(v=>v.source.id===id);

  test('[ACC2-PURCHASE] a bill on vendor credit is a Purchase voucher (Dr category · Cr 2200/<vendor>); a bill paid at once stays a Payment',()=>{
    let [s,id]=bill(base(),{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:1200000});
    const v=vOf(s,id);assert.equal(v.type,'Purchase');assert.match(v.no,/^PUR\/\d{8}\/001$/);
    assert.deepEqual(ent(v),[['5000',1200000,0],['2200/V-DECCAN',0,1200000]]);
    let id2;[s,id2]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:50000,method:'UPI'});
    assert.equal(vOf(s,id2).type,'Payment');assert.ok(balanced(vs(s)));
    assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    assert.ok(M.accDayBook(s,D,D).rows.some(r=>r.type==='Purchase'),'the day book carries the Purchase voucher');
  });

  test('[ACC2-PURCHASE] ITC allowed: an intra-state bill splits 1310 / 1320; an inter-state vendor (state ≠ MH) puts the whole GST on 1330 (IGST)',()=>{
    let [s,id]=bill(base(),{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:118000,extra:{gstRateBps:1800,gstPaise:18000,itc:'allowed'}});
    assert.deepEqual(ent(vOf(s,id)),[['5000',100000,0],['1310',9000,0],['1320',9000,0],['2200/V-DECCAN',0,118000]]);
    let id2;[s,id2]=bill(s,{vendor:'Kaveri Kitchen Equipment (fictional)',category:'Banquet supplies',amount:59000,extra:{gstRateBps:1800,gstPaise:9000,itc:'allowed'}});
    const e=s.expenses.find(x=>x.id===id2);assert.equal(e.gstKind,'inter','from the vendor state KA');
    assert.deepEqual(ent(vOf(s,id2)),[['5000',50000,0],['1330',9000,0],['2200/V-KAVERI',0,59000]]);
    assert.match(vOf(s,id2).narration,/IGST/);
    // The screen may say intra explicitly for that vendor (bill raised from a Maharashtra branch): the record keeps it.
    let id3;[s,id3]=bill(s,{vendor:'Kaveri Kitchen Equipment (fictional)',category:'Banquet supplies',amount:11800,extra:{gstRateBps:1800,gstPaise:1800,itc:'allowed',gstKind:'intra'}});
    assert.deepEqual(ent(vOf(s,id3)).map(x=>x[0]),['5000','1310','1320','2200/V-KAVERI']);
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'x',category:'Banquet supplies',amount:100,method:'UPI',gstKind:'foreign'}),/intra-state .* inter-state/);
    // A reversal mirrors the IGST line.
    s=execute(s,'owner',{type:'EXPENSE_REVERSE',id:id2,reason:'Wrong vendor'});
    const r=s.expenses.at(-1);assert.equal(r.gstKind,'inter');assert.deepEqual(ent(vOf(s,r.id)),[['5000',0,50000],['1330',0,9000],['2200/V-KAVERI',59000,0]]);
  });

  test('[ACC2-PURCHASE] DEC-143 guard: ITC cannot be allowed on a blocked category (entry, edit and approval); disallowed books the GST in the cost',()=>{
    const s=base();
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'Staff lunch',category:'Staff food',amount:10500,method:'UPI',gstRateBps:500,gstPaise:500,itc:'allowed'}),
      e=>e.code==='VALIDATION'&&/blocked \(CGST s\.17\(5\)\).*DEC-143/.test(e.message));
    let t=execute(s,'reception',{type:'EXPENSE',description:'Staff lunch',category:'Staff food',amount:10500,method:'UPI',gstRateBps:500,gstPaise:500});
    const id=t.expenses.at(-1).id;
    assert.throws(()=>execute(t,'reception',{type:'EXP_EDIT',id,reason:'claim',amount:10500,category:'Staff food',method:'UPI',description:'Staff lunch',gstRateBps:500,gstPaise:500,itc:'allowed'}),/DEC-143/);
    // A category blocked after entry: the approval refuses the ITC that was asked for.
    let u=execute(s,'reception',{type:'EXPENSE',description:'Banquet linen',category:'Banquet supplies',amount:11800,method:'UPI',reference:'x',gstRateBps:1800,gstPaise:1800,itc:'allowed'});
    const uid=u.expenses.at(-1).id;u.mastersX.rows.expenseCategories=u.mastersX.rows.expenseCategories.map(c=>c.code==='BANQ'?{...c,itcBlocked:true}:c);
    assert.throws(()=>execute(u,'owner',{type:'EXPENSE_APPROVE',id:uid,approve:true}),/DEC-143/);
    t=execute(t,'owner',{type:'EXPENSE_APPROVE',id,approve:true});assert.deepEqual(ent(vOf(t,id)),[['5120',10500,0],['1010',0,10500]]);
    // A capital vehicle is blocked whatever the flag (rulebook §4.5).
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'Van',category:'Vehicles',amount:118000,method:'Vendor credit',vendor:'Deccan Agro Traders (fictional)',gstRateBps:1800,gstPaise:18000,itc:'allowed'}),/DEC-143/);
  });

  test('[ACC2-PURCHASE] reverse charge (C8, DEC-145): no vendor GST on the bill; a second source tax/rcm Dr category · Cr 2140 at the rate; ITC allowed → 1310 / 1320',()=>{
    const s=base();
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'Guards Oct',vendor:'Shield Security Agency (fictional)',category:'Security services',amount:4000000,method:'Vendor credit',gstRateBps:1800,gstPaise:610169}),
      e=>e.code==='VALIDATION'&&/reverse-charge bill carries no vendor GST/.test(e.message));
    let [t,id]=bill(s,{vendor:'Shield Security Agency (fictional)',description:'Guards Oct',category:'Security services',amount:4000000});
    const e=t.expenses.find(x=>x.id===id);assert.equal(e.rcm,true);assert.equal(e.rcmBps,1800);
    assert.deepEqual(ent(vOf(t,id)),[['5050',4000000,0],['2200/V-SHIELD',0,4000000]]);
    const r=vs(t).find(v=>v.source.id==='rcm:'+id);
    assert.equal(r.type,'Journal');assert.deepEqual(r.source,{kind:'tax',id:'rcm:'+id,sub:'rcm'});assert.deepEqual(ent(r),[['5050',720000,0],['2140',0,720000]]);
    // ITC allowed on an RCM bill (not blocked) and an edited rate: the RCM GST goes to the input-credit ledgers.
    let [u,id2]=bill(s,{vendor:'Shield Security Agency (fictional)',description:'Guards Nov',category:'Security services',amount:100000,extra:{itc:'allowed',rcmBps:500}});
    assert.deepEqual(ent(vs(u).find(v=>v.source.id==='rcm:'+id2)),[['1310',2500,0],['1320',2500,0],['2140',0,5000]]);
    // The reversal reverses the RCM source too; the day stays balanced and reconciled.
    t=execute(t,'owner',{type:'EXPENSE_REVERSE',id,reason:'Duplicate bill'});const rid=t.expenses.at(-1).id;
    assert.deepEqual(ent(vs(t).find(v=>v.source.id==='rcm:'+rid)),[['5050',0,720000],['2140',720000,0]]);
    assert.ok(balanced(vs(t)));assert.ok(M.accReconcile(t,D).every(c=>c.ok));
    assert.equal(M.accSourceOf(t,r).expense.id,id,'trace to source');
  });

  test('[ACC2-PURCHASE] capital purchase (C10): Dr the fixed-asset block, a register row FA-<yyyy>-<nnn> at cost less ITC, CSV; a reversal marks it disposed at 0',()=>{
    let [s,id]=bill(base(),{vendor:'Kaveri Kitchen Equipment (fictional)',description:'Dining chairs × 20',category:'Furniture',amount:236000,extra:{gstRateBps:1800,gstPaise:36000,itc:'allowed'}});
    const e=s.expenses.find(x=>x.id===id);assert.equal(e.capital,true);assert.equal(e.assetNo,'FA-2026-001');
    assert.deepEqual(ent(vOf(s,id)),[['1510',200000,0],['1330',36000,0],['2200/V-KAVERI',0,236000]]);
    const a=M.accAssets(s);assert.equal(a.length,1);
    assert.deepEqual({no:a[0].no,ledger:a[0].ledger,name:a[0].name,cost:a[0].cost,itc:a[0].itc,date:a[0].date,expense:a[0].expense},{no:'FA-2026-001',ledger:'1510',name:'Dining chairs × 20',cost:200000,itc:36000,date:D,expense:id});
    assert.equal(M.accAssetNextNo(s,D),'FA-2026-002');assert.equal(M.accAssetNextNo(s,'2027-04-02'),'FA-2027-001');
    const csv=M.accAssetsCsv(s).split('\r\n');assert.equal(csv[0],M.ACC_ASSET_CSV_HEADER.join(','));assert.match(csv[1],/^FA-2026-001,"?Furniture, fittings & electrical fittings"?,1510,Dining chairs × 20,2000\.00,360\.00,2026-10-24,,/);
    assert.deepEqual(M.accAssetBlocks(s),[{ledger:'1510',name:'Furniture, fittings & electrical fittings',cost:200000,count:1}]);
    s=execute(s,'owner',{type:'EXPENSE_REVERSE',id,reason:'Wrong category'});
    assert.deepEqual(M.accAssets(s)[0].disposed,{date:D,amount:0,voucher:s.expenses.at(-1).id});assert.equal(M.accAssetRegister(s)[0].status,'Disposed');
    assert.deepEqual(M.accAssetBlocks(s),[]);
  });

  test('[ACC2-PURCHASE] category master (Owner-only): capital ⇔ a fixed-asset block, RCM needs its rate, vehicles / buildings are ITC-blocked; vendor state and TDS section',()=>{
    const spec=M.mstSpec('expenseCategories'),cols=M.mstCsvColumns(spec);
    for(const f of ['itcBlocked','rcm','rcmBps','capital'])assert.ok(cols.includes(f),f);
    const s=base(),rows=M.mnyCategories(s),v=r=>spec.validate(s,r,rows);
    assert.match(v({code:'X',name:'X',ledger:'5000',capital:true}),/fixed-asset block/);
    assert.match(v({code:'X',name:'X',ledger:'1520'}),/capital category only/);
    assert.match(v({code:'X',name:'X',ledger:'5050',rcm:true}),/reverse-charge GST rate/);
    const car={code:'X',name:'X',ledger:'1540',capital:true};assert.equal(v(car),undefined);assert.equal(car.itcBlocked,true);
    assert.ok(spec.fields.find(f=>f.key==='ledger').options.some(o=>o.value==='1520'),'fixed-asset blocks offered');
    assert.equal(spec.money,true,'Owner-only (DEC-058)');
    const vs2=M.mstSpec('expenseVendors'),vv=r=>vs2.validate(s,r,M.mnyVendors(s));
    for(const f of ['state','tdsSection'])assert.ok(M.mstCsvColumns(vs2).includes(f),f);
    const ka={code:'V',name:'V',type:'supplier',tds:false,state:' ka '};assert.equal(vv(ka),undefined);assert.equal(ka.state,'KA');
    assert.match(vv({code:'V',name:'V',type:'supplier',tds:false,state:'Karnataka'}),/two letters/);
    assert.match(vv({code:'V',name:'V',type:'supplier',tds:false,tdsSection:'rent'}),/TDS applies/);
    assert.equal(vv({code:'V',name:'V',type:'supplier',tds:true,pan:'ABCDE1234F',tdsSection:'contractor'}),undefined);
    assert.equal(M.expGstKindOf(s,'Kaveri Kitchen Equipment (fictional)'),'inter');assert.equal(M.expGstKindOf(s,'Morning Supplies'),'intra');
  });

  test('[ACC2-PURCHASE] vendor payment (C3 / C4): oldest open bill first, the rest an advance; Payment Dr 2200/<vendor> · Cr the settles-to bank; ageing by allocations',()=>{
    let s=base(),a,b;
    [s,a]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:3000000});
    [s,b]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:1200000});
    // Make the first bill 45 days old (history), so ageing has two buckets.
    s.expenses.find(e=>e.id===a).date='2026-09-09';
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100000,method:'Bank transfer'}),/reference/);
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100000,method:'Vendor credit',reference:'x'}),/not a credit method/);
    s=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'Deccan Agro Traders (fictional)',amount:3500000,method:'Bank transfer',reference:'NEFT-778'});
    const p=s.expensesX.vendorPayments.at(-1);
    assert.deepEqual(p.allocations,[{expense:a,amount:3000000},{expense:b,amount:500000}]);assert.equal(p.vendor,'V-DECCAN');
    const v=vs(s).find(v=>v.source.id==='vendor-payment:'+p.id);
    assert.equal(v.type,'Payment');assert.deepEqual(v.source,{kind:'vendor',id:'vendor-payment:'+p.id,sub:'vendor-payment'});
    assert.deepEqual(ent(v),[['2200/V-DECCAN',3500000,0],['1030/BANK-1',0,3500000]]);
    let ag=M.expVendorAgeing(s,D,'V-DECCAN');assert.equal(ag.length,1);
    assert.deepEqual({open:ag[0].open,advance:ag[0].advance,net:ag[0].net,b:ag[0].buckets},{open:700000,advance:0,net:700000,b:{'0–30':700000,'31–60':0,'61–90':0,'90+':0}});
    // An advance: more than is open.
    s=execute(s,'accountant',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:1000000,method:'UPI',reference:'UPI-55'});
    const q=s.expensesX.vendorPayments.at(-1);assert.deepEqual(q.allocations,[{expense:b,amount:700000}]);
    ag=M.expVendorAgeing(s,D,'V-DECCAN')[0];assert.deepEqual([ag.open,ag.advance,ag.net],[0,300000,-300000]);
    assert.equal(M.accPartyBalance(s,'2200/V-DECCAN'),300000,'the party ledger is Dr by the advance');
    assert.equal(-M.expVendorBalance(s,'V-DECCAN'),M.accPartyBalance(s,'2200/V-DECCAN'),'ageing net = the party ledger');
    // Explicit allocations are checked.
    let [t,c]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:200000});
    assert.throws(()=>execute(t,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100000,method:'UPI',reference:'u',allocations:[{expense:c,amount:150000}]}),/more than the payment/);
    assert.throws(()=>execute(t,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:500000,method:'UPI',reference:'u',allocations:[{expense:a,amount:1}]}),/not an open bill/);
    t=execute(t,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100000,method:'UPI',reference:'u',allocations:[{expense:c,amount:100000}]});
    assert.equal(M.expVendorOpenBills(t,'V-DECCAN')[0].open,100000);
    assert.ok(balanced(vs(t)));
  });

  test('[ACC2-PURCHASE] ageing buckets 0–30 / 31–60 / 61–90 / 90+ by bill date; a vendor with nothing open drops out',()=>{
    let s=base();const ids=[];
    for(const [amt,date] of [[100,'2026-10-20'],[200,'2026-09-10'],[300,'2026-08-10'],[400,'2026-05-01']]){let id;[s,id]=bill(s,{vendor:'Kaveri Kitchen Equipment (fictional)',category:'Banquet supplies',amount:amt*100});ids.push(id);s.expenses.find(e=>e.id===id).date=date;}
    const r=M.expVendorAgeing(s,D,'Kaveri Kitchen Equipment (fictional)')[0];
    assert.deepEqual(r.buckets,{'0–30':10000,'31–60':20000,'61–90':30000,'90+':40000});assert.equal(r.bills[0].expense,ids[3],'oldest first');
    assert.equal(M.expAgeBucket(30),'0–30');assert.equal(M.expAgeBucket(31),'31–60');assert.equal(M.expAgeBucket(90),'61–90');assert.equal(M.expAgeBucket(91),'90+');
    assert.ok(!M.expVendorAgeing(s).some(x=>x.vendor==='V-SHIELD'));
  });

  test('[ACC2-PURCHASE] cash vendor payment leaves the drawer; a same-day void posts nothing and gives the cash back; a later void posts the reversal on the void date',()=>{
    let [s]=bill(base(),{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:300000});
    const before=M.drawer(s);
    s=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:120000,method:'Cash'});
    const p=s.expensesX.vendorPayments.at(-1);assert.equal(M.drawer(s),before-120000);
    assert.deepEqual(ent(vs(s).find(v=>v.source.id==='vendor-payment:'+p.id)),[['2200/V-DECCAN',120000,0],['1000/TILL',0,120000]]);
    assert.ok(M.accReconcile(s,D).find(c=>c.id==='cash').ok,'cash in hand movement ties');
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_PAY_VOID',id:p.id,reason:'Wrong vendor'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'owner',{type:'EXP_VENDOR_PAY_VOID',id:p.id,reason:'x'}),/reason is required/);
    const t=execute(s,'owner',{type:'EXP_VENDOR_PAY_VOID',id:p.id,reason:'Wrong vendor'});
    assert.equal(M.drawer(t),before);assert.ok(!vs(t).some(v=>v.source.kind==='vendor'),'nothing posts');
    assert.equal(M.expVendorOpenBills(t,'V-DECCAN')[0].open,300000,'the bill is open again');
    // A bank payment recorded on an earlier day (history), voided today: a reversing voucher dated today.
    let u=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:50000,method:'Bank transfer',reference:'NEFT-9'});
    const q=u.expensesX.vendorPayments.at(-1);q.date='2026-10-20';
    u=execute(u,'owner',{type:'EXP_VENDOR_PAY_VOID',id:q.id,reason:'Bounced'});
    const w=vs(u).find(v=>v.source.id==='vendor-payment:'+q.id+'/void');
    assert.deepEqual(ent(w),[['2200/V-DECCAN',0,50000],['1030/BANK-1',50000,0]]);assert.equal(w.source.sub,'vendor-payment-void');
    assert.ok(vs(u,'2026-10-20').some(v=>v.source.id==='vendor-payment:'+q.id),'the original stays on its own day');
    assert.throws(()=>execute(u,'owner',{type:'EXP_VENDOR_PAY_VOID',id:q.id,reason:'again'}),/already void/);
  });

  test('[ACC2-PURCHASE] return / vendor credit note (C5): the Manager requests, only the Owner approves; a negative Purchase reverses the ITC taken; the bill’s open amount drops',()=>{
    let [s,id]=bill(base(),{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:118000,extra:{gstRateBps:1800,gstPaise:18000,itc:'allowed'}});
    assert.throws(()=>execute(s,'reception',{type:'EXP_VENDOR_RETURN',expense:id,amount:59000,gstPaise:9000,reason:'Damaged'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_RETURN',expense:id,amount:200000,gstPaise:0,reason:'Damaged'}),/left to return/);
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_RETURN',expense:id,amount:59000,gstPaise:19000,reason:'Damaged'}),/GST is left to reverse/);
    s=execute(s,'manager',{type:'EXP_VENDOR_RETURN',expense:id,amount:59000,gstPaise:9000,reason:'Damaged crates'});
    const r=s.expenses.at(-1);assert.equal(r.status,'Pending');assert.equal(r.amount,-59000);assert.equal(r.sourceRef,'ret:'+id);
    assert.throws(()=>execute(s,'accountant',{type:'EXPENSE_APPROVE',id:r.id,approve:true}),/Only the Owner approves a vendor return/);
    assert.throws(()=>execute(s,'manager',{type:'EXP_EDIT',id:r.id,reason:'x',amount:1,category:'Banquet supplies',method:'Vendor credit',description:'x'}),/vendor return changes only/);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:r.id,approve:true});
    const v=vOf(s,r.id);assert.equal(v.type,'Purchase');assert.equal(v.source.sub,'return');
    assert.deepEqual(ent(v),[['5000',0,50000],['1310',0,4500],['1320',0,4500],['2200/V-DECCAN',59000,0]]);
    assert.equal(M.expVendorOpenBills(s,'V-DECCAN')[0].open,59000);assert.deepEqual(M.expReturned(s,id),{amount:59000,gst:9000});
    assert.throws(()=>execute(s,'owner',{type:'EXPENSE_REVERSE',id:r.id,reason:'oops'}),/vendor return cannot be reversed/);
    // The Owner's own return posts at once; a cash-paid bill's return leaves a vendor credit (advance).
    let [t,c]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:40000,method:'UPI'});
    t=execute(t,'owner',{type:'EXP_VENDOR_RETURN',expense:c,amount:10000,reason:'Short supply'});
    const r2=t.expenses.at(-1);assert.equal(r2.status,'Posted');assert.equal(r2.method,'Vendor credit');
    assert.deepEqual(ent(vOf(t,r2.id)),[['5000',0,10000],['2200/V-DECCAN',10000,0]]);
    assert.equal(M.expVendorAgeing(t,D,'V-DECCAN')[0].advance,10000);
    assert.ok(balanced(vs(t)));assert.ok(M.accReconcile(t,D).every(c=>c.ok),JSON.stringify(M.accReconcile(t,D).filter(c=>!c.ok)));
  });

  test('[ACC2-PURCHASE] TDS lines (C9): a bill carrying tdsPaise credits 2150 and the vendor net; the reconciliation counts funding + TDS (tdsForBill stubbed to 0 in this lane)',()=>{
    let [s,id]=bill(base(),{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:100000});
    assert.equal(s.expenses.find(e=>e.id===id).tdsPaise,undefined,'stub: no TDS held');
    const e=s.expenses.find(e=>e.id===id);e.tdsPaise=2000;e.tdsSection='contractor';
    const v=vOf(s,id);assert.deepEqual(ent(v),[['5000',100000,0],['2150',0,2000],['2200/V-DECCAN',0,98000]]);assert.match(v.narration,/TDS contractor/);
    assert.ok(M.accReconcile(s,D).find(c=>c.id==='expenses').ok);
    assert.equal(M.expVendorOpenBills(s,'V-DECCAN')[0].open,98000,'the vendor is owed the bill net of TDS');
  });

  test('[ACC2-PURCHASE] permissions (contract §9 PERM-EXP-004): vendor pay / return = Owner, Manager, Accountant; void = Owner; mapping keys and the chart',()=>{
    const s=base();
    for(const u of ['reception','cashier','housekeeper'])assert.throws(()=>execute(s,u,{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100,method:'UPI',reference:'x'}),e=>e.code==='FORBIDDEN',u);
    for(const u of ['manager','accountant'])assert.throws(()=>execute(s,u,{type:'EXP_VENDOR_PAY_VOID',id:'VP-X',reason:'xxx'}),e=>e.code==='FORBIDDEN',u);
    assert.deepEqual(['tax:gst-input-igst','tax:rcm-payable','tax:tds-payable','vendor:payable'].map(k=>M.accMapping(s)[k]),['1330','2140','2150','2200']);
    assert.ok(M.ACC_KEY_PATTERN.test('vendor:payable'));assert.equal(M.accKeyGroup('vendor:payable'),'Vendor payments (payables)');
    const t=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:100,method:'UPI',reference:'x'});
    assert.ok(M.accMapRows(t).find(r=>r.key==='vendor:payable').uses>0);assert.ok(M.accDates(t).includes(D));
  });
}
