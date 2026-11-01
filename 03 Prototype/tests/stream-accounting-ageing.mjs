// ACC-3 acc3-ageing · outstanding with ageing + GST summary (contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §3; rulebook §7.6, §7.7, §8).
// Golden: the ACC-RULEBOOK scenario (April 2026, fictional). Same cases as 08 Production/tests/Hcc.Accounting.Tests/Ageing/AgeingRulesTests.cs.
export function register({test,M,assert}){
  const T='[ACC3-AGEING] ',R=r=>Math.round(r*100);
  let cache;const month=()=>cache??(cache=M.loadScenario('ACC-RULEBOOK'));

  test(T+'golden receivable: Deccan Agro ₹63,000 (₹60,000 + GST) open in 0–30 on 29 April, opening ₹40,000 paid first; nothing open on 30 April',()=>{
    const a=M.accAgeing(month(),'2026-04-29','receivable');
    assert.equal(a.rows.length,1);const r=a.rows[0];
    assert.equal(r.name,'Deccan Agro Ltd');assert.equal(r.control,'1110');
    assert.deepEqual([r.open,r.b0_30,r.b31_60,r.b61_90,r.b90,!!r.flagged],[R(63000),R(63000),0,0,0,false]);
    assert.ok(r.bills.every(b=>b.ref!=='opening'&&b.days<=30));
    assert.equal(a.totals.b0_30,R(63000));
    assert.equal(M.accAgeing(month(),'2026-04-30','receivable').rows.length,0);
  });
  test(T+'golden payables on 30 April: Morning ₹24,000 (opening then bill, oldest first) and Kaveri ₹42,000, both 0–30; totals tie to 2200',()=>{
    const a=M.accAgeing(month(),'2026-04-30','payable');
    assert.deepEqual(a.rows.map(r=>[r.name,r.open,r.b0_30,!!r.flagged]),[['Kaveri Textiles',R(42000),R(42000),false],['Morning Supplies',R(24000),R(24000),false]]);
    const m=a.rows[1].bills;assert.equal(m.length,1);assert.deepEqual([m[0].amount,m[0].paid,m[0].open,m[0].days],[R(59000),R(35000),R(24000),25]);
    assert.deepEqual([a.totals.open,a.totals.b0_30],[R(66000),R(66000)]);
    const tb=M.accTrialBalance6(month(),'2026-04-01','2026-04-30',false).rows.find(r=>r.account==='2200');
    assert.equal(tb.closingCredit,a.totals.open);
  });
  test(T+'allocation: explicit allocations win, the rest oldest-first (opening = oldest), a return goes to its bill',()=>{
    const bills=[{ref:'b2',date:'2026-04-10',amount:5000},{ref:'opening',date:'2026-04-01',amount:3000},{ref:'b3',date:'2026-04-20',amount:2000}];
    let x=M.accAgeingAllocate('2026-04-30',bills,[{key:'p',date:'2026-04-25',amount:4000}]);
    assert.deepEqual(x.bills.map(b=>[b.ref,b.open]),[['b2',4000],['b3',2000]]);assert.equal(x.advance,0);
    x=M.accAgeingAllocate('2026-04-30',bills,[{key:'p',date:'2026-04-25',amount:4000,explicit:[{ref:'b3',amount:2000}]}]);
    assert.deepEqual(x.bills.map(b=>[b.ref,b.open]),[['b2',5000],['opening',1000]]);
    x=M.accAgeingAllocate('2026-04-30',bills,[{key:'r',date:'2026-04-25',amount:1000,target:'b3'},{key:'p',date:'2026-04-26',amount:12000}]);
    assert.deepEqual([x.bills.length,x.advance],[0,3000]);
  });
  test(T+'buckets 0–30 / 31–60 / 61–90 / 90+ by days from the bill date; later as-of moves Morning to 61–90',()=>{
    assert.deepEqual([0,30,31,60,61,90,91].map(M.accAgeingBucket),['b0_30','b0_30','b31_60','b31_60','b61_90','b61_90','b90']);
    const m=M.accAgeing(month(),'2026-06-15','payable').rows.find(r=>r.name==='Morning Supplies');
    if(m)assert.equal(m.b61_90+m.b90+m.b31_60+m.b0_30,m.open);
  });
  test(T+'golden GST summary April 2026: output ₹29,000 at 5 % (B2B ₹3,000 on Deccan ₹60,000), ITC nil, TCS ₹100, challan ₹28,900; ties to GSTR-1 and the GST month',()=>{
    const g=M.accGstSummary(month(),'2026-04');
    assert.deepEqual([g.output.rate5,g.output.rate18,g.output.b2b,g.output.b2cs],[R(29000),0,R(3000),R(26000)]);
    assert.equal(g.outputTaxable.b2b,R(60000));
    assert.deepEqual(g.bookOutput,{igst:0,cgst:R(14500),sgst:R(14500)});
    assert.deepEqual(g.itc,{cgst:0,sgst:0,igst:0});
    assert.deepEqual([g.tcsCredit,g.cashLedgerBalance,g.setoff.payable,g.setoff.challan,g.cashPaid],[R(100),R(100),R(29000),R(28900),0]);
    assert.deepEqual(g.ties,{gstr1:true,gstr3b:true,differences:[]});
  });
  test(T+'GST summary lists a books ↔ GSTR-1 difference in words',()=>{
    const s=structuredClone(month());const b=s.bookings.find(b=>b.invoiceNo&&b.departure>='2026-04-01'&&b.departure<='2026-04-30');
    b.departure='2026-05-02';
    const g=M.accGstSummary(s,'2026-04');
    assert.equal(g.ties.gstr1,false);assert.ok(g.ties.differences.some(d=>/GSTR-1 working summary/.test(d)));
  });
}
