// ACC-3 · lane acc3-carry — the ACC-2 carry-overs C1…C11 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7, §9; rulebook B9, B11, F2, F4,
// A6, E5, E6; DEC-136, DEC-142, DEC-172, DEC-175). One test (or more) per item. Names prefixed [ACC3-CARRY].
export function register({test,M,assert}){
  const T='[ACC3-CARRY] ';
  const {seed,execute}=M;
  const D=seed().date;
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  function withMasters({cats=[],vens=[]}={}){
    const s=structuredClone(seed());
    const c=[...M.mnyCategories(s),...cats.map((x,i)=>({id:'ec-c'+i,sortOrder:300+i,active:true,needsBill:false,...x}))];
    const v=[...M.mnyVendors(s),...vens.map((x,i)=>({id:'ev-c'+i,sortOrder:300+i,active:true,type:'supplier',tds:false,...x}))];
    s.mastersX={...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),expenseCategories:c,expenseVendors:v}};
    return s;
  }
  const CATS=[{code:'FURN',name:'Furniture',ledger:'1510',capital:true},{code:'BANQ',name:'Banquet supplies',ledger:'5000'},
    {code:'RENTP',name:'Equipment hire',ledger:'5000'},{code:'RENTB',name:'Premises rent',ledger:'5000'}];
  const VENS=[{code:'V-KAVERI',name:'Kaveri Kitchen Equipment (fictional)',state:'KA',gstin:'29ABCDE1234F1Z5'},
    {code:'V-DECCAN',name:'Deccan Agro Traders (fictional)',state:'MH',gstin:'24ABCDE1234F1Z5'},
    {code:'V-GEN',name:'Genset Hire (fictional)',state:'MH',tds:true,pan:'ABCDE1234F',tdsSection:'rent:plant'},
    {code:'V-LAND',name:'Landlord Estates (fictional)',state:'MH',tds:true,pan:'ABCDF1234F',tdsSection:'rent'}];
  const base=()=>withMasters({cats:CATS,vens:VENS});
  function bill(s,x){s=execute(s,'reception',{type:'EXPENSE',description:x.description??'Bill',vendor:x.vendor,category:x.category,amount:x.amount,method:x.method??'Vendor credit',...x.extra});
    const id=s.expenses.at(-1).id;s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});return [s,id];}
  const chairs=()=>bill(base(),{vendor:'Kaveri Kitchen Equipment (fictional)',description:'Dining chairs × 20',category:'Furniture',amount:236000,extra:{gstRateBps:1800,gstPaise:36000,itc:'allowed'}});

  // ── C1 put to use / disposal ──
  test(T+'C1 ACC_ASSET_PUT_TO_USE: Owner / Accountant only, not before the purchase or after the business date; F2 factor full ≥ 180 days, else half',()=>{
    let [s]=chairs();const a=M.accAssets(s)[0];
    assert.throws(()=>execute(s,'manager',{type:'ACC_ASSET_PUT_TO_USE',asset:a.no,date:D}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'owner',{type:'ACC_ASSET_PUT_TO_USE',asset:a.no,date:'2026-01-01'}),/before the purchase/);
    assert.throws(()=>execute(s,'owner',{type:'ACC_ASSET_PUT_TO_USE',asset:a.no,date:'2099-01-01'}),/after the business date/);
    s=execute(s,'accountant',{type:'ACC_ASSET_PUT_TO_USE',asset:a.no,date:D});
    const b=M.accAssets(s)[0];assert.equal(b.putToUse,D);assert.equal(M.accAssetRegister(s)[0].status,'In use');
    assert.equal(M.accFyEnd(D),'2027-03-31');
    assert.equal(M.accAssetDepreciationFactor(b,'2027-03-31'),0.5,'24 Oct → 31 Mar is fewer than 180 days');
    assert.equal(M.accAssetDepreciationFactor({putToUse:'2026-09-30'},'2027-03-31'),1,'30 Sep → 31 Mar = 183 days');
    assert.equal(M.accAssetDepreciationFactor({putToUse:'2026-10-03'},'2027-03-31'),1,'3 Oct → 31 Mar = 180 days');
    assert.equal(M.accAssetDepreciationFactor({putToUse:'2026-10-04'},'2027-03-31'),0.5);
    assert.equal(M.accAssetDepreciationFactor({putToUse:'2025-06-01'},'2027-03-31'),1,'an earlier year: full');
    assert.equal(M.accAssetDepreciationFactor({},'2027-03-31'),0,'not yet put to use');
  });
  test(T+'C1 ACC_ASSET_DISPOSE (rulebook F4): a sale is a Receipt Dr tender · Cr block at the sale value; empty block flagged "capital gain / loss — CA"; scrap posts nothing',()=>{
    let [s]=chairs();const a=M.accAssets(s)[0];
    assert.equal(M.accAssetWdvTotal(s),200000);assert.deepEqual(M.accAssetWdv(s).map(x=>[x.ledger,x.wdv,x.count]),[['1510',200000,1]]);
    assert.throws(()=>execute(s,'manager',{type:'ACC_ASSET_DISPOSE',asset:a.no,proceeds:50000,method:'Bank transfer'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'owner',{type:'ACC_ASSET_DISPOSE',asset:a.no,proceeds:-1}),/sale value/);
    const t=execute(s,'owner',{type:'ACC_ASSET_DISPOSE',asset:a.no,proceeds:50000,method:'Bank transfer',reason:'Sold to staff'});
    const d=M.accAssets(t)[0].disposed;assert.equal(d.amount,50000);assert.equal(d.method,'Bank transfer');assert.match(d.flag,/no asset left.*CA/);
    const v=M.accLiveVouchers(t,D).find(x=>x.source.kind==='manual'&&x.source.id===d.voucher);assert.ok(v,'the receipt voucher posts');
    assert.equal(v.type,'Receipt');assert.deepEqual(ent(v).map(x=>[x[0].split('/')[0],x[1],x[2]]),[['1030',50000,0],['1510',0,50000]]);
    assert.equal(M.accAssetWdvTotal(t),150000,'the block keeps cost less the sale value until the CA closes it');
    assert.equal(M.accAssetRegister(t)[0].status,'Disposed');
    assert.throws(()=>execute(t,'owner',{type:'ACC_ASSET_DISPOSE',asset:a.no,proceeds:0}),/already disposed/);
    // Accountant's sale waits for the Owner (manual voucher pending); a scrap posts nothing.
    const p=execute(s,'accountant',{type:'ACC_ASSET_DISPOSE',asset:a.id,proceeds:50000,method:'Bank transfer'});
    assert.equal(p.accountingX.manual.at(-1).status,'pending');
    const z=execute(s,'owner',{type:'ACC_ASSET_DISPOSE',asset:a.no,proceeds:0,reason:'Broken'});
    assert.deepEqual([M.accAssets(z)[0].disposed.amount,M.accAssets(z)[0].disposed.voucher],[0,'']);
    assert.equal((z.accountingX.manual??[]).length,(s.accountingX.manual??[]).length,'no voucher for a scrap');
  });

  // ── C2 a return against a capital purchase reduces the register cost ──
  test(T+'C2 a posted return against a capital purchase reduces the asset cost (net of the ITC it reverses); a pending one does not',()=>{
    let [s,id]=chairs();
    s=execute(s,'manager',{type:'EXP_VENDOR_RETURN',expense:id,amount:59000,gstPaise:9000,reason:'Five chairs damaged'});
    assert.equal(M.accAssets(s)[0].cost,200000,'pending: unchanged');
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    const a=M.accAssets(s)[0];assert.equal(a.cost,150000);assert.equal(a.returned,50000);
    assert.equal(s.accountingX.assets[0].cost,200000,'the stored row keeps the purchase cost (derived, never double counted)');
    assert.deepEqual(M.accAssetBlocks(s),[{ledger:'1510',name:'Furniture, fittings & electrical fittings',cost:150000,count:1}]);
    assert.equal(M.accAssetWdvTotal(s),150000,'the books agree with the register');
  });

  // ── C3 re-allocate a vendor payment ──
  test(T+'C3 EXP_VENDOR_PAY_REALLOCATE: Owner / Manager / Accountant; the payment\'s own allocation counts as open; ageing follows; history kept',()=>{
    let s=base(),a,b;
    [s,a]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:300000});
    [s,b]=bill(s,{vendor:'Deccan Agro Traders (fictional)',category:'Banquet supplies',amount:200000});
    s=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:250000,method:'Bank transfer',reference:'UTR-1'});
    const p=s.expensesX.vendorPayments.at(-1);assert.deepEqual(p.allocations,[{expense:a,amount:250000}]);
    assert.throws(()=>execute(s,'reception',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[]}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[{expense:b,amount:250000}]}),/only 2000\.00 open/);
    assert.throws(()=>execute(s,'manager',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[{expense:a,amount:150000},{expense:b,amount:150000}]}),/more than the payment/);
    const t=execute(s,'accountant',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[{expense:b,amount:200000},{expense:a,amount:50000}]});
    const q=t.expensesX.vendorPayments.at(-1);assert.deepEqual(q.allocations,[{expense:b,amount:200000},{expense:a,amount:50000}]);
    assert.equal(q.reallocations.length,1);assert.deepEqual(q.reallocations[0].before,[{expense:a,amount:250000}]);
    assert.deepEqual(M.expVendorOpenBills(t,'V-DECCAN').map(x=>[x.expense,x.open]),[[a,250000]]);
    assert.equal(M.expVendorBalance(t,'V-DECCAN'),M.expVendorBalance(s,'V-DECCAN'),'the party ledger does not move');
    assert.ok(t.audit.some(a=>/re-allocated · was .* now /.test(a.detail)));
    assert.throws(()=>execute(t,'owner',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:q.allocations}),/unchanged/);
    // a shorter list leaves an advance
    const u=execute(s,'owner',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[{expense:a,amount:100000}]});
    assert.equal(M.expVendorAdvance(u,'V-DECCAN'),150000);
    // a void payment cannot be re-allocated
    const v=execute(s,'owner',{type:'EXP_VENDOR_PAY_VOID',id:p.id,reason:'Bounced'});
    assert.throws(()=>execute(v,'owner',{type:'EXP_VENDOR_PAY_REALLOCATE',payment:p.id,allocations:[{expense:a,amount:1}]}),/void/);
  });

  // ── C4 vendor GST state vs GSTIN ──
  test(T+'C4 the GSTIN\'s first two digits against the vendor\'s state: a warning only (DEC-136)',()=>{
    assert.equal(M.expVendorGstinWarning({name:'K',gstin:'29ABCDE1234F1Z5',state:'KA'}),'');
    assert.equal(M.expVendorGstinWarning({name:'M',gstin:'27ABCDE1234F1Z5'}),'','empty state = MH = 27');
    assert.match(M.expVendorGstinWarning({name:'D',gstin:'24ABCDE1234F1Z5',state:'MH'}),/starts 24, but its GST state is MH \(27\)/);
    assert.equal(M.expVendorGstinWarning({name:'X',state:'MH'}),'','no GSTIN: nothing to compare');
    const s=base();
    assert.match(M.expPurchaseEntry(s,{category:'Banquet supplies',vendor:'Deccan Agro Traders (fictional)'}).warning,/DEC-136/);
    assert.equal(M.expPurchaseEntry(s,{category:'Banquet supplies',vendor:'Kaveri Kitchen Equipment (fictional)'}).warning,undefined);
    const t=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:'V-DECCAN',amount:1000,method:'Bank transfer',reference:'UTR-2'});
    assert.ok(t.audit.some(a=>/GSTIN .* starts 24/.test(a.detail)),'the payment goes through with the warning');
  });

  // ── C5 Udhaar B9 per customer per day ──
  test(T+'C5 B9 Udhaar: cash from one customer is totalled per day across all their credits (name + mobile = one party)',()=>{
    const s=structuredClone(seed());const d=s.date;
    const u=(id,amt)=>({id,name:'Ravi Kumar (fictional)',mobile:'9800000001',amount:amt,date:d,settlements:[{id:id+'-P',amount:15000000,date:d,mode:'Cash',desk:true}]});
    s.expensesX={...(s.expensesX??{}),udhaar:[u('UD-A',15000000),u('UD-B',15000000)]};
    const party=M.accCashUdhaarParty({name:'Ravi Kumar (fictional)',mobile:'9800000001'});
    const g=M.accCashGuard(s,{kind:'receipt',party,amount:100,date:d,scope:'udhaar:UD-C'});
    assert.equal(g.refused,true,'₹1,50,000 + ₹1,50,000 already today from the same customer');assert.equal(g.total,30000100);
    const other=M.accCashGuard(s,{kind:'receipt',party:M.accCashUdhaarParty({name:'Asha (fictional)',mobile:'9800000002'}),amount:100,date:d});
    assert.equal(other.refused,false);
  });

  // ── C6 TDS remittance per sub-section, C7 Form 168 figures ──
  test(T+'C6 TDS remittance per sub-section: 194-I(a) plant and 194-I(b) premises are held, remitted and filed apart',()=>{
    assert.deepEqual(['rent','rent:plant','professional','professional:technical','contractor'].map(M.tdsOldOf),['194-I(b)','194-I(a)','194J(b)','194J(a)','194C']);
    const s=base();
    // Two bills carrying TDS (stamped fields, as a posted bill keeps them).
    const mk=(id,vendor,section,tds)=>({id,vendor,category:'Banquet supplies',amount:10000000,method:'Vendor credit',date:s.date,status:'Posted',actor:'owner',createdAt:s.clock,tdsPaise:tds,tdsSection:section});
    s.expenses.push(mk('EXP-T1','Genset Hire (fictional)','rent:plant',200000),mk('EXP-T2','Landlord Estates (fictional)','rent',1000000));
    const m=s.date.slice(0,7);
    assert.equal(M.tdsPayableOutstanding(s,'rent:plant',m),200000);assert.equal(M.tdsPayableOutstanding(s,'rent',m),1000000);
    assert.throws(()=>execute(s,'owner',{type:'ACC_TDS_REMIT',month:m,section:'rent:plant',amount:300000,method:'Bank transfer',challan:'CH-001'}),/194-I\(a\).*\(₹2,000/);
    let t=execute(s,'owner',{type:'ACC_TDS_REMIT',month:m,section:'rent:plant',amount:200000,method:'Bank transfer',challan:'CH-001'});
    assert.ok(t.audit.some(a=>/Rent of plant.*old 194-I\(a\)/.test(a.detail)));
    const r=M.tdsRemittances(t).at(-1);assert.deepEqual([r.section,r.code,r.amount],['rent','rent:plant',200000]);
    assert.equal(M.tdsPayableOutstanding(t,'rent:plant',m),0);assert.equal(M.tdsPayableOutstanding(t,'rent',m),1000000,'194-I(b) untouched');
    const rows=M.tdsBills(t);assert.deepEqual(rows.map(b=>[b.expense,b.remitted]),[['EXP-T1',200000],['EXP-T2',0]]);
    assert.match(M.tdsDeducteeReport(t,m).rows.find(x=>x.section==='rent:plant').sectionLabel,/old 194-I\(a\)/);
  });
  test(T+'C7 Form 168 figures are stored on the TDS remittance and feed the receivable reconciliation',()=>{
    const s=base();
    s.expenses.push({id:'EXP-T3',vendor:'Landlord Estates (fictional)',category:'Banquet supplies',amount:10000000,method:'Vendor credit',date:s.date,status:'Posted',actor:'owner',createdAt:s.clock,tdsPaise:1000000,tdsSection:'rent'});
    const co=(s.groupsX?.companies??[])[0];assert.ok(co,'a company in the seed');
    const m=s.date.slice(0,7);
    assert.throws(()=>execute(s,'owner',{type:'ACC_TDS_REMIT',month:m,section:'rent',amount:1000000,method:'Bank transfer',challan:'CH-002',form168:{'NO-SUCH':1}}),/not a company/);
    let t=execute(s,'owner',{type:'ACC_TDS_REMIT',month:m,section:'rent',amount:1000000,method:'Bank transfer',challan:'CH-002',form168:{[co.id]:123400}});
    const r=M.tdsRemittances(t).at(-1);assert.deepEqual(r.form168,{[co.id]:123400});
    assert.deepEqual(M.tdsForm168Of(t),{[co.id]:123400});
    const c=M.tdsReceivableReport(t).companies.find(x=>x.company===co.id);assert.equal(c.form168,123400);
    t=execute(t,'accountant',{type:'ACC_TDS_FORM168',remittance:r.id,form168:{[co.id]:5000}});
    assert.deepEqual(M.tdsForm168Of(t),{[co.id]:5000});
    assert.throws(()=>execute(t,'manager',{type:'ACC_TDS_FORM168',remittance:r.id,form168:{}}),e=>e.code==='FORBIDDEN');
  });

  // ── C8 rulebook B11 ──
  test(T+'C8 B11: a loan is received / repaid only through a bank — a cash leg on 2400 is refused; bank passes',()=>{
    assert.match(M.accLoanCashRefusal([{account:'2400/LN-X',cash:false},{account:'1000/DESK',cash:true}]),/B11/);
    assert.equal(M.accLoanCashRefusal([{account:'2400/LN-X',cash:false},{account:'1030/B',cash:false}]),'');
    assert.equal(M.accLoanCashRefusal([{account:'5000',cash:false},{account:'1000/DESK',cash:true}]),'');
    let s=execute(seed(),'owner',{type:'ACC_LOAN_ADD',code:'LN-HDFC',name:'HDFC term loan',lender:'HDFC Bank (fictional)',kind:'secured'});
    const loan=M.accLoans(s).find(l=>l.code.endsWith('LN-HDFC')).code;
    const cash=M.accPartyLedgers(s).find(p=>p.id.startsWith('1000/')).id,bank=M.accPartyLedgers(s).find(p=>p.id.startsWith('1030/')).id;
    const p=M.accManualProblems(s,{date:s.date,type:'Receipt',narration:'Loan received',lines:[{account:cash,debit:5000000,credit:0},{account:loan,debit:0,credit:5000000}]});
    assert.ok(p.some(x=>/B11/.test(x)),JSON.stringify(p));
    assert.throws(()=>execute(s,'owner',{type:'ACC_MANUAL_SAVE',date:s.date,voucherType:'Receipt',narration:'Loan received',lines:[{account:cash,debit:5000000,credit:0},{account:loan,debit:0,credit:5000000}]}),/B11/);
    const ok=M.accManualProblems(s,{date:s.date,type:'Receipt',narration:'Loan received',lines:[{account:bank,debit:5000000,credit:0},{account:loan,debit:0,credit:5000000}]});
    assert.deepEqual(ok,[]);
  });

  // ── C10 dated GST-on-advances switch ──
  test(T+'C10 ACC_GST_ADVANCES_SET {on, from}: advances dated before the switch date carry no GST; a later one does',()=>{
    let s=execute(seed(),'owner',{type:'ACC_GST_ADVANCES_SET',on:true,from:'2099-01-01'});
    assert.equal(M.cmpSlice(s).profile.gstOnAdvancesFrom,'2099-01-01');
    const b=s.bookings.find(x=>x.status==='In-house')??s.bookings[0];
    s=execute(s,'cashier',{type:'PAYMENT',id:b.id,amount:1050000,method:'Cash',kind:'deposit'});
    assert.equal(M.accAdvanceGstVouchers(s,s.date).length,0,'dated before the switch');
    s=execute(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:true,from:s.date});
    assert.equal(M.accAdvanceGstVouchers(s,s.date).filter(v=>v.source.sub==='advance-gst').length,1,'on or after the date');
    assert.throws(()=>execute(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:true,from:s.date}),/already on/);
    assert.throws(()=>execute(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:true,from:'01-04-2026'}),/YYYY-MM-DD/);
    const off=execute(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:false});assert.equal(M.cmpSlice(off).profile.gstOnAdvancesFrom,undefined);
    // the default from = the business date; a profile save keeps the date
    const d=execute(seed(),'owner',{type:'ACC_GST_ADVANCES_SET',on:true});assert.equal(M.cmpSlice(d).profile.gstOnAdvancesFrom,d.date);
    const p=M.cmpSlice(d).profile,{gstOnAdvancesFrom:_x,updatedBy:_u,updatedAt:_a,...prof}=p;
    const e=execute(d,'owner',{type:'CMP_PROFILE_SET',...prof});assert.equal(M.cmpSlice(e).profile.gstOnAdvancesFrom,d.date);
  });

  // ── C11 dated TDS rates into configX.changes (production hub display) ──
  test(T+'C11 the server acc.tds_rate versions become Master configuration changes that tdsValueOn reads',()=>{
    const rows=M.tdsRatesToChanges([{rateId:7,key:'tds.rent.bpsOther',effectiveFrom:'2026-04-01',value:1200},{rateId:3,key:'tds.rent.bpsOther',effectiveFrom:'2025-04-01',value:1100}]);
    assert.deepEqual(rows.map(r=>[r.id,r.key,r.after,r.effectiveFrom,r.status]),[['TDS-R3','tds.rent.bpsOther',1100,'2025-04-01','applied'],['TDS-R7','tds.rent.bpsOther',1200,'2026-04-01','applied']]);
    const s=structuredClone(seed());s.configX={...(s.configX??{}),changes:[...(s.configX?.changes??[]),...rows]};
    assert.equal(M.tdsValueOn(s,'tds.rent.bpsOther','2026-05-01'),1200);assert.equal(M.tdsValueOn(s,'tds.rent.bpsOther','2025-05-01'),1100);
  });
}
