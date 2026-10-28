// ACC-2 · lane acc2-tds: the dated TDS-rate table (TDS_SEED / tdsValueOn, Master configuration rows), TDS on a vendor bill with the period
// threshold (tdsForBill), the firm-profile switches (ACC_TDS_SETTINGS), the remittance (ACC_TDS_REMIT → manual template tds-remittance),
// the deductee report + CSV, the company payment net of TDS (A9: Dr 1360) and the Form 168 reconciliation. Contract:
// 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 (A9), §5, §9. Rulebook §5 A9 / C9 / E5, §8.2 T7a / T7b. Names prefixed [ACC2-TDS].
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const {seed,execute}=M;
  const D=seed().date; // 2026-10-24, FY 2026-27
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  const fails=f=>{try{f();}catch(e){return e;}assert.fail('expected a refusal');};
  const P=r=>Math.round(r*100);
  /** A TDS vendor (the purchase lane adds tdsSection to the master form; here it is set on the stored row). */
  const withVendor=(s,code,name,section)=>{
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'expenseVendors',row:{code,name,type:'other',tds:true,pan:'AAAPS1234C'}});
    s=structuredClone(s);s.mastersX.rows.expenseVendors.find(r=>r.code===code).tdsSection=section;return s;};
  /** A posted bill as the purchase lane stores it (tdsPaise = what tdsForBill returned at posting). */
  let n=0;const bill=(s,vendor,date,amount,gstPaise=0,tdsPaise=0)=>{s=structuredClone(s);
    s.expenses.push({id:'EXP-T'+(++n),date,vendor,category:'Maintenance',description:'Works (fictional)',amount,method:'Vendor credit',status:'Posted',actor:'owner',
      ...(gstPaise?{gstPaise,gstRateBps:1800}:{}),...(tdsPaise?{tdsPaise,tdsSection:M.mnyVendor(s,vendor)?.tdsSection}:{})});return s;};

  test('[ACC2-TDS] seed: dated rows rent / contractor / professional / commission with thresholds, individual and other rates, s.393 text and source, every row "CA to confirm"',()=>{
    assert.deepEqual(M.TDS_SECTIONS.map(d=>d.id),['rent','contractor','professional','commission']);
    for(const d of M.TDS_SECTIONS){assert.equal(d.note,'CA to confirm');assert.match(d.section,/^s\.393/);assert.match(d.source,/LAW S-13/);
      for(const f of ['threshold','bpsIndividual','bpsOther'])assert.ok(M.TDS_KEYS.includes(`tds.${d.id}.${f}`),d.id+f);}
    for(const k of M.TDS_KEYS){assert.match(k,/^tds\.[a-z]+\.[A-Za-z]+$/);assert.equal(M.tdsSeedDef(k).versions[0].from,'2026-04-01');}
    const s=seed(),v=k=>M.tdsValueOn(s,k,D);
    assert.deepEqual([v('tds.rent.threshold'),v('tds.rent.bpsOther'),v('tds.rent.bpsPlant')],[P(50000),1000,200]);
    assert.deepEqual([v('tds.contractor.threshold'),v('tds.contractor.thresholdYear'),v('tds.contractor.bpsIndividual'),v('tds.contractor.bpsOther')],[P(30000),P(100000),100,200]);
    assert.deepEqual([v('tds.professional.threshold'),v('tds.professional.bpsOther'),v('tds.professional.bpsTechnical')],[P(50000),1000,200]);
    assert.deepEqual([v('tds.commission.threshold'),v('tds.commission.bpsOther')],[P(20000),200]);
    assert.equal(M.tdsValueOn(s,'tds.rent.bpsOther','2025-06-01'),1000,'the first version covers earlier dates');
    assert.deepEqual(['rent','rent:plant','contractor','professional:technical','commission','rent:x','tour'].map(c=>M.tdsRateBps(s,c,D,'other')),[1000,200,200,200,200,0,0]);
    assert.equal(M.tdsRateBps(s,'contractor',D,'individual'),100);
    const t=M.tdsTable(s,D);assert.deepEqual(t.map(r=>r.code),['rent','rent:plant','contractor','professional','professional:technical','commission']);
    assert.equal(t.find(r=>r.code==='contractor').threshold,'₹30,000 a bill or ₹1,00,000 a financial year');
    assert.deepEqual(M.TDS_SECTION_OPTIONS.map(o=>o.id),t.map(r=>r.code));
    assert.deepEqual([M.ACC_DEFAULT_MAP['tax:tds-receivable'],M.ACC_DEFAULT_MAP['tax:tds-payable']],['1360','2150']);
  });

  test('[ACC2-TDS] Owner edits a TDS rate in Master configuration › Money and tax (dated, illustrative); Tech admin and the Manager are refused',()=>{
    let s=seed();const d=M.cfgDef(s,'tds.contractor.bpsOther');
    assert.deepEqual([d.group,d.money,d.dated,d.effectiveMode,d.illustrative,d.editable],['money',true,true,'date',true,true]);
    assert.match(d.help,/Illustrative — CA to confirm for a hotel/);assert.equal(d.default,200);
    for(const k of M.TDS_KEYS)assert.ok(M.cfgDef(s,k),k);
    for(const u of ['techadmin','manager','accountant'])assert.equal(fails(()=>execute(s,u,{type:'SETTING_SET',setting:'tds.contractor.bpsOther',value:150,effectiveFrom:'2026-10-25'})).code,'FORBIDDEN',u);
    s=execute(s,'owner',{type:'SETTING_SET',setting:'tds.contractor.bpsOther',value:150,effectiveFrom:'2026-10-25'});
    assert.deepEqual([M.tdsValueOn(s,'tds.contractor.bpsOther',D),M.tdsValueOn(s,'tds.contractor.bpsOther','2026-10-25')],[200,150]);
    assert.equal(fails(()=>execute(s,'owner',{type:'SETTING_SET',setting:'tds.contractor.bpsOther',value:4000,effectiveFrom:'2026-10-25'})).code,'VALIDATION');
  });

  test('[ACC2-TDS] the two firm-profile switches: ACC_TDS_SETTINGS is the Owner\'s; defaults on (DEC-146) and the "other" column',()=>{
    let s=seed();assert.deepEqual(M.tdsSettings(s),{hotelDeductsTds:true,tdsDeductorType:'other'});
    for(const u of ['manager','accountant','techadmin','reception'])assert.equal(fails(()=>execute(s,u,{type:'ACC_TDS_SETTINGS',hotelDeductsTds:false})).code,'FORBIDDEN',u);
    assert.equal(fails(()=>execute(s,'owner',{type:'ACC_TDS_SETTINGS'})).code,'VALIDATION');
    assert.equal(fails(()=>execute(s,'owner',{type:'ACC_TDS_SETTINGS',tdsDeductorType:'company'})).code,'VALIDATION');
    const name=M.cmpSlice(s).profile.name;
    s=execute(s,'owner',{type:'ACC_TDS_SETTINGS',hotelDeductsTds:false,tdsDeductorType:'individual'});
    assert.deepEqual(M.tdsSettings(s),{hotelDeductsTds:false,tdsDeductorType:'individual'});
    assert.equal(M.cmpSlice(s).profile.name,name,'the rest of the profile is kept');
    assert.match(s.audit[0].detail,/hotel deducts TDS on → off · rate column other → individual/);
  });

  test('[ACC2-TDS] tdsForBill: taxable = amount − GST (Circular 23/2017); contractor single-bill threshold; net credit = bill − TDS; nothing without the switches',()=>{
    let s=withVendor(seed(),'V-SHIV-CIVIL','Shiv Civil Works (fictional)','contractor');
    // ₹59,000 bill with ₹9,000 GST → taxable ₹50,000 > ₹30,000 single → 2 % (other) = ₹1,000; the vendor is credited ₹58,000.
    const r=M.tdsForBill(s,{vendor:'Shiv Civil Works (fictional)',amount:P(59000),gstPaise:P(9000),date:D});
    assert.deepEqual([r.paise,r.section,r.taxable,r.base,r.bps],[P(1000),'contractor',P(50000),P(50000),200]);
    assert.equal(P(59000)-r.paise,P(58000),'net credit to 2200/<vendor>');assert.match(r.reason,/single bill ₹50,000 above ₹30,000/);
    // Below the single threshold and the year: nothing (the reason says why).
    const small=M.tdsForBill(s,{vendor:'V-SHIV-CIVIL',amount:P(20000),date:D});assert.equal(small.paise,0);assert.match(small.reason,/within/);
    // Individual column (1 %), rounding half away from zero on paise.
    const ind=execute(s,'owner',{type:'ACC_TDS_SETTINGS',tdsDeductorType:'individual'});
    assert.equal(M.tdsForBill(ind,{vendor:'V-SHIV-CIVIL',amount:3000050,date:D}).paise,30001);
    // Switch off / vendor not flagged / no section → 0.
    assert.equal(M.tdsForBill(execute(s,'owner',{type:'ACC_TDS_SETTINGS',hotelDeductsTds:false}),{vendor:'V-SHIV-CIVIL',amount:P(59000),date:D}).paise,0);
    assert.equal(M.tdsForBill(s,{vendor:'Morning Supplies',amount:P(59000),date:D}).paise,0);
    assert.equal(M.tdsForBill(withVendor(seed(),'V-X','X Works (fictional)',''),{vendor:'V-X',amount:P(59000),date:D}).paise,0);
  });

  test('[ACC2-TDS] threshold per FY: the bill that takes a vendor over the year threshold carries the TDS on the earlier untaxed bills too; later bills only their own',()=>{
    let s=withVendor(seed(),'V-SHIV-CIVIL','Shiv Civil Works (fictional)','contractor');
    // FY 2026-27: ₹25,000 (May), ₹28,000 (Jul), ₹29,000 (Sep) — each under ₹30,000, total ₹82,000; a bill dated 2026-03-31 (FY 2025-26) never counts.
    s=bill(s,'Shiv Civil Works (fictional)','2026-03-31',P(90000));
    for(const [d,a] of [['2026-05-10',25000],['2026-07-10',28000],['2026-09-10',29000]])s=bill(s,'Shiv Civil Works (fictional)',d,P(a));
    // Oct ₹20,000 → ₹1,02,000 > ₹1,00,000: 2 % of (₹20,000 + ₹82,000) = ₹2,040.
    const cross=M.tdsForBill(s,{vendor:'V-SHIV-CIVIL',amount:P(20000),date:D});
    assert.deepEqual([cross.paise,cross.base],[P(2040),P(102000)]);assert.match(cross.reason,/in FY 2026-27 above ₹1,00,000 \(earlier bills without TDS ₹82,000 included\)/);
    // Once the year is over the threshold (that bill posted with its TDS), the next bill carries only its own 2 %.
    s=bill(s,'Shiv Civil Works (fictional)',D,P(20000),0,cross.paise);
    assert.deepEqual([M.tdsForBill(s,{vendor:'V-SHIV-CIVIL',amount:P(5000),date:D}).paise],[P(100)]);
    // `exclude` = the bill being posted is not its own earlier bill.
    const id=s.expenses.at(-1).id;assert.equal(M.tdsForBill(s,{vendor:'V-SHIV-CIVIL',amount:P(20000),date:D,exclude:id}).paise,P(2040));
    // Rent: per calendar month (₹50,000); professional: per FY (₹50,000).
    let r=withVendor(seed(),'V-LANDLORD','Godown Owner (fictional)','rent');
    assert.equal(M.tdsForBill(r,{vendor:'V-LANDLORD',amount:P(50000),date:D}).paise,0,'not above ₹50,000');
    r=bill(r,'Godown Owner (fictional)','2026-10-02',P(30000));
    assert.equal(M.tdsForBill(r,{vendor:'V-LANDLORD',amount:P(30000),date:D}).paise,P(6000),'10 % of ₹60,000 in October');
    assert.equal(M.tdsForBill(r,{vendor:'V-LANDLORD',amount:P(30000),date:'2026-11-02'}).paise,0,'November starts again');
    const pl=withVendor(seed(),'V-GEN','Genset Hire (fictional)','rent:plant');
    assert.equal(M.tdsForBill(pl,{vendor:'V-GEN',amount:P(60000),date:D}).paise,P(1200),'2 % plant');
    let pr=withVendor(seed(),'V-CA','Audit Partners (fictional)','professional');
    pr=bill(pr,'Audit Partners (fictional)','2026-06-01',P(40000));
    assert.equal(M.tdsForBill(pr,{vendor:'V-CA',amount:P(11800),gstPaise:P(1800),date:D}).paise,0,'₹40,000 + ₹10,000 = ₹50,000 is not above ₹50,000');
    assert.equal(M.tdsForBill(pr,{vendor:'V-CA',amount:P(10001),date:D}).paise,500010,'₹50,001 crosses: 10 % of the whole ₹50,001');
  });

  test('[ACC2-TDS] ACC_TDS_REMIT: manual template tds-remittance Dr 2150 · Cr 1030/<bank>; Owner approved, Accountant pending; guards; deductee report with remitted and CSV',()=>{
    let s=withVendor(seed(),'V-SHIV-CIVIL','Shiv Civil Works (fictional)','contractor');
    s=withVendor(s,'V-LANDLORD','Godown Owner (fictional)','rent');
    s=bill(s,'Shiv Civil Works (fictional)','2026-10-05',P(59000),P(9000),P(1000));
    s=bill(s,'Shiv Civil Works (fictional)','2026-09-05',P(40000),0,P(800));
    s=bill(s,'Godown Owner (fictional)','2026-10-01',P(60000),0,P(6000));
    for(const u of ['manager','reception','cashier','techadmin'])assert.equal(fails(()=>execute(s,u,{type:'ACC_TDS_REMIT',month:'2026-10',section:'contractor',amount:P(1000),method:'Bank transfer',challan:'CIN-1'})).code,'FORBIDDEN',u);
    const bad=c=>fails(()=>execute(s,'owner',{type:'ACC_TDS_REMIT',month:'2026-10',section:'contractor',amount:P(1000),method:'Bank transfer',challan:'CIN-0001',...c}));
    assert.match(bad({amount:P(1001)}).message,/cannot exceed the TDS held back for payments to contractors in 2026-10 and not yet remitted \(₹1,000\)/);
    assert.match(bad({method:'Cash'}).message,/bank account/);assert.equal(bad({section:'tour'}).code,'VALIDATION');assert.equal(bad({month:'2026-11'}).code,'VALIDATION');
    assert.equal(bad({challan:'x'}).code,'VALIDATION');assert.equal(bad({month:'2026-08'}).code,'INVALID_TRANSITION');assert.equal(bad({date:'2026-09-30'}).code,'VALIDATION');
    // Accountant → pending (the Owner is the checker); Owner → approved at once.
    s=execute(s,'accountant',{type:'ACC_TDS_REMIT',month:'2026-10',section:'contractor',amount:P(1000),method:'Bank transfer',challan:'CIN-0001'});
    let v=s.accountingX.manual.at(-1);
    assert.deepEqual([v.template,v.type,v.status,v.date,v.reference],['tds-remittance','Payment','pending',D,'CIN-0001']);
    assert.deepEqual(v.lines.map(l=>[l.account,l.debit,l.credit]),[['2150',P(1000),0],['1030/BANK-1',0,P(1000)]]);
    assert.equal(M.tdsPayableOutstanding(s,'contractor','2026-10'),0,'a pending entry already counts against the outstanding');
    s=execute(s,'owner',{type:'ACC_TDS_REMIT',month:'2026-10',section:'rent',amount:P(6000),method:'Bank transfer',challan:'CIN-0002',date:'2026-10-07'});
    v=s.accountingX.manual.at(-1);assert.deepEqual([v.status,v.date],['approved','2026-10-07']);
    assert.deepEqual(M.tdsRemittances(s).map(r=>[r.section,r.month,r.amount,r.challan,r.status]),[['rent','2026-10',P(6000),'CIN-0002','approved'],['contractor','2026-10',P(1000),'CIN-0001','pending']]);
    // Deductee report (month): remitted counts approved entries only.
    const m=M.tdsDeducteeReport(s,'2026-10');
    assert.deepEqual(m.rows.map(r=>[r.vendor,r.pan,r.section,r.bills,r.taxable,r.tds,r.remitted,r.pending]),
      [['Godown Owner (fictional)','AAAPS1234C','rent',1,P(60000),P(6000),P(6000),0],['Shiv Civil Works (fictional)','AAAPS1234C','contractor',1,P(50000),P(1000),0,P(1000)]]);
    assert.deepEqual(m.totals,{taxable:P(110000),tds:P(7000),remitted:P(6000),pending:P(1000)});assert.equal(m.period.kind,'month');
    // FY: September's bill joins.
    const fy=M.tdsDeducteeReport(s,'2026-27'),sh=fy.rows.find(r=>r.section==='contractor');
    assert.deepEqual([fy.period.label,fy.period.from,sh.bills,sh.taxable,sh.tds],['FY 2026-27','2026-04-01',2,P(90000),P(1800)]);
    assert.deepEqual(M.tdsDeducteeReport(s,{month:'2026-09'}).rows.map(r=>r.tds),[P(800)]);
    const csv=M.tdsDeducteeCsv(m).split('\r\n');
    assert.equal(csv[0],'Vendor,Vendor code,PAN,Section,Bills,Taxable (₹),TDS (₹),Remitted (₹),Pending (₹)');
    assert.equal(csv.at(-1),'Total,,,,2,110000.00,7000.00,6000.00,1000.00');
    assert.match(csv[1],/^Godown Owner \(fictional\),V-LANDLORD,AAAPS1234C,Rent of premises · s\.393\(1\) Sl\. 2\(ii\) \(old 194-I\),1,60000\.00,6000\.00,6000\.00,0\.00$/);
  });

  test('[ACC2-TDS] A9 rulebook figure: Deccan Agro ₹60,000 taxable → ₹6,000 TDS (T7b); ₹40,000 below the ₹50,000 month → none (T7a)',()=>{
    const s=seed();
    assert.deepEqual(M.tdsCompanyExpected(s,{taxable:P(60000),date:'2026-04-30'}),{paise:P(6000),bps:1000,threshold:P(50000)});
    assert.equal(M.tdsCompanyExpected(s,{taxable:P(40000),date:'2026-04-10'}).paise,0);
    assert.equal(M.tdsCompanyExpected(s,{taxable:P(40000),date:'2026-04-10',monthTaxable:P(100000)}).paise,P(4000));
    // T7b: bill ₹63,000 (₹60,000 + 5 % GST) paid ₹57,000 by bank with ₹6,000 TDS.
    assert.equal(M.tdsTaxable(P(63000),P(3000)),P(60000));
  });

  test('[ACC2-TDS] GRP_COMPANY_PAYMENT tds: Receipt Dr 1030/<bank> net · Dr 1360 TDS · Cr 1110/<company> gross; the receivable falls by both; guards',()=>{
    let s=seed();const c=M.grpSlice(s).companies.find(x=>x.id==='CO-2'),owed=M.grpReceivable(s,c);assert.equal(owed,P(88000));
    const pay=x=>({type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:P(57000),tds:P(6000),method:'Bank transfer',reference:'NEFT-DEMO-0430',...x});
    assert.match(fails(()=>execute(s,'owner',pay({tds:P(20000)}))).message,/more than 20 % of the ₹77,000 settled/);
    assert.equal(fails(()=>execute(s,'owner',pay({tds:-1}))).code,'VALIDATION');assert.equal(fails(()=>execute(s,'owner',pay({tds:1.5}))).code,'VALIDATION');
    assert.match(fails(()=>execute(s,'owner',pay({amount:P(83000)}))).message,/Payment ₹83,000 \+ TDS ₹6,000 cannot exceed what Godavari Agro Inputs \(fictional\) owes on company credit \(₹88,000\)/);
    s=execute(s,'cashier',pay());
    const p=M.grpSlice(s).payments.at(-1);assert.deepEqual([p.amount,p.tds],[P(57000),P(6000)]);
    assert.equal(M.grpReceivable(s,M.grpSlice(s).companies.find(x=>x.id==='CO-2')),P(25000));
    const v=M.accLiveVouchers(s,D).find(v=>v.source.id==='company-payment:'+p.id);
    assert.deepEqual([v.type,v.source.sub],['Receipt','company-payment']);
    assert.deepEqual(ent(v),[['1030/BANK-1',P(57000),0],['1360',P(6000),0],['1110/CO-2',0,P(63000)]]);
    assert.match(v.narration,/· TDS ₹6000\.00 deducted$/);assert.ok(M.accBalanced(v));
    assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    assert.equal(M.accMapRows(s).find(r=>r.key==='tax:tds-receivable').uses,1);
    assert.match(s.audit[0].detail,/TDS ₹6,000 deducted \(receivable 1360\) · receivable ₹88,000 → ₹25,000/);
    // Without tds the voucher is unchanged (ACC-1 shape).
    const t=execute(seed(),'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:P(1000),method:'UPI',reference:'UPI-1'}),q=M.grpSlice(t).payments.at(-1);
    assert.deepEqual(ent(M.accLiveVouchers(t,D).find(v=>v.source.id==='company-payment:'+q.id)),[['1010',P(1000),0],['1110/CO-2',0,P(1000)]]);
    assert.equal(q.tds,undefined);
  });

  test('[ACC2-TDS] Form 168 reconciliation: 1360 entries by company and month, the ledger beside them, the CA\'s Form 168 figure and the difference; CSV',()=>{
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:P(57000),tds:P(6000),method:'Bank transfer',reference:'NEFT-1'});
    s=execute(s,'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-1',amount:P(18000),tds:P(2000),method:'Bank transfer',reference:'NEFT-2'});
    s=execute(s,'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-1',amount:P(1000),method:'UPI',reference:'UPI-3'});
    const r=M.tdsReceivableReport(s,'2026-27',{'CO-2':P(6000),'CO-1':P(1500)});
    assert.deepEqual(r.rows.map(x=>[x.name,x.month,x.payments,x.received,x.tds,x.settled]),
      [['Deccan Pharma Distributors (fictional)','2026-10',1,P(18000),P(2000),P(20000)],['Godavari Agro Inputs (fictional)','2026-10',1,P(57000),P(6000),P(63000)]]);
    assert.deepEqual([r.total,r.ledger,r.ledgerMatches],[P(8000),P(8000),true]);
    assert.deepEqual(r.companies.map(c=>[c.company,c.tds,c.form168,c.difference]),[['CO-1',P(2000),P(1500),-P(500)],['CO-2',P(6000),P(6000),0]]);
    assert.equal(M.tdsReceivableReport(s,'2025-26').rows.length,0);
    const csv=M.tdsReceivableCsv(r).split('\r\n');
    assert.equal(csv[0],'Company,Month,Payments,Received (₹),TDS deducted (₹),Settled (₹)');assert.equal(csv.at(-1),'Total,,2,75000.00,8000.00,83000.00');
  });

  test('[ACC2-TDS] wiring: handlers in the ACC2 block, the production sync area maps both commands, the barrel exports the helpers',()=>{
    for(const n of ['tdsForBill','tdsValueOn','tdsDeducteeReport','tdsDeducteeCsv','tdsReceivableReport','tdsSettings','TDS_SEED'])assert.ok(M[n],n);
    const src=readFileSync(new URL('../src/clients/sync/accounting-tds.ts',import.meta.url),'utf8');
    assert.match(src,/type:'ACC_TDS_SETTINGS'[\s\S]*'SetTdsSettings'/);assert.match(src,/type:'ACC_TDS_REMIT'[\s\S]*'RecordTdsRemittance'/);
    const reg=readFileSync(new URL('../src/clients/sync/registry.ts',import.meta.url),'utf8');assert.match(reg,/accountingTds, \/\/ ACC-2 tds lane/);
  });
}
