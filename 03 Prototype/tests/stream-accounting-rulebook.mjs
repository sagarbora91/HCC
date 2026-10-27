// ACC-1 acc1-tests · the rulebook golden month, ACC-1 subset. Names prefixed [ACC1-RULEBOOK].
// Source: 02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026, fictional). Scenario ACC-RULEBOOK (src/fixtures/ext/accounting-rulebook.ts)
// drives the transactions ACC-1 can book through real commands; this file holds the expectations.
//
// TWO KINDS OF TESTS
//   • self-checks of the encoded rulebook (opening, 27 entries of §8.2, the §8.3 trial balance) — they pass on their own;
//   • engine tests against the posting contract (02 Planning/agents/ACC1-POSTING-CONTRACT.md) — they need the other ACC-1 lanes. A test whose
//     selector or command is not exported yet FAILS with "waits for lane <x>" (see lane() below); after `git merge claude/acc1` they run for real.
//
// WHAT IS DRIVEN (and what is not): T1 T2 T3 T5a T5b T5c T7a T8 T9 T10 T12 T15a T15b. Not drivable in ACC-1 (see ACC1-TESTS-DONE.md):
// T13a–c (the prototype books no payroll or salary-advance voucher), T16 (an Owner drawing is taken from the cash drawer; a drawing from the
// bank is rulebook B6 "new"). Left out by the brief (need ACC-2): T4, T6, T7b, T11, T14, T17, T18, T19.
export function register({test,M,assert}){
  const R=r=>Math.round(r*100);
  const T='[ACC1-RULEBOOK] ';
  const FROM='2026-04-01',TO='2026-04-30';
  // ───────────────────────── the rulebook, encoded ─────────────────────────
  // Account: ledger code, or `code/Party` with the party named as in the rulebook (resolved to ids by partyId()).
  const e=(a,dr,cr=0)=>({a,dr:R(dr),cr:R(cr)});
  const OPENING=[e('1000',20000),e('1030/HDFC',300000),e('1500',6000000),e('1510',800000),e('1520',500000),e('1530',80000),e('1110/Deccan',40000),
    e('2200/Morning',0,25000),e('2000',0,10000),e('2400',0,2000000),e('3100',0,5705000)];
  const STEP={
    T1:[e('1100',420000),e('4900',10000),e('4000',0,410000),e('2110',0,10000),e('2120',0,10000)],
    T2:[e('1100',105000),e('4200',0,100000),e('2110',0,2500),e('2120',0,2500)],
    T3:[e('1000',31500),e('1010',31500),e('4200',0,60000),e('2110',0,1500),e('2120',0,1500)],
    T4:[e('1115/Goibibo',21000),e('4000',0,20000),e('2110',0,500),e('2120',0,500)],
    T5a:[e('2000',10000),e('1100',0,10000)],
    T5b:[e('1110/Deccan',63000),e('1100',0,63000)],
    T5c:[e('1000',152000),e('1010',150000),e('1020',150000),e('1100',0,452000)],
    T6:[e('1010',31500),e('2000',0,31500)],
    T7a:[e('1030/HDFC',40000),e('1110/Deccan',0,40000)],
    T7b:[e('1030/HDFC',57000),e('1360',6000),e('1110/Deccan',0,63000)],
    T8:[e('5000',59000),e('2200/Morning',0,59000)],
    T9:[e('5010',42000),e('2200/Kaveri',0,42000)],
    T10:[e('5030',45000),e('1030/HDFC',0,45000)],
    T11:[e('2200/Morning',60000),e('1030/HDFC',0,60000)],
    T12:[e('5040',8000),e('1000',0,8000)],
    T13a:[e('1200/Staff-07',5000),e('1000',0,5000)],
    T13b:[e('5100',240000),e('5110',18000),e('5110',6500),e('2300',0,213500),e('2310',0,36000),e('2320',0,8000),e('2330',0,2000),e('1200/Staff-07',0,5000)],
    T13c:[e('2300',213500),e('1030/HDFC',0,213500)],
    T14a:[e('1030/HDFC',213000),e('1010',0,213000)],
    T14b:[e('1030/HDFC',148230),e('5080',1770),e('1020',0,150000)],
    T15a:[e('1030/HDFC',150000),e('1000',0,150000)],
    T15b:[e('5900',200),e('1000',0,200)],
    T16:[e('3000',50000),e('1030/HDFC',0,50000)],
    T17:[e('2400',40000),e('5090',15000),e('1030/HDFC',0,55000)],
    T18:[e('5080',590),e('1030/HDFC',0,590)],
    T19:[e('1030/HDFC',17360),e('5070',3540),e('1340',100),e('1115/Goibibo',0,21000)],
  };
  /** Steps ACC-1 drives and books today. */
  const DRIVEN=['T1','T2','T3','T5a','T5b','T5c','T7a','T8','T9','T10','T12','T15a','T15b'];
  /** The brief's subset: DRIVEN plus the payroll and drawing steps that wait for later lanes (the target when they can be driven). */
  const BRIEF=[...DRIVEN,'T13a','T13b','T13c','T16'];
  const ALL=Object.keys(STEP);
  /** §8.3, literally (control-level, rupees; Dr positive, Cr negative) — the independent check of the encoding above. */
  const RULEBOOK_TB_83={'1000':40300,'1030':501500,'1360':6000,'1340':100,'1500':6000000,'1510':800000,'1520':500000,'1530':80000,'3000':50000,'4900':10000,
    '5000':59000,'5010':42000,'5030':45000,'5040':8000,'5100':240000,'5110':24500,'5080':2360,'5900':200,'5090':15000,'5070':3540,
    '2200':-66000,'2000':-31500,'2110':-14500,'2120':-14500,'2310':-36000,'2320':-8000,'2330':-2000,'2400':-1960000,'3100':-5705000,'4000':-430000,'4200':-160000};

  const control=a=>a.split('/')[0];
  /** Balances (paise, Dr positive) of a list of {a,dr,cr} by full account and by control. */
  function model(steps,withOpening=true){
    const full={},ctl={};
    for(const x of [...(withOpening?OPENING:[]),...steps.flatMap(k=>STEP[k])]){
      full[x.a]=(full[x.a]??0)+x.dr-x.cr;ctl[control(x.a)]=(ctl[control(x.a)]??0)+x.dr-x.cr;
    }
    const strip=o=>Object.fromEntries(Object.entries(o).filter(([,v])=>v!==0));
    return {full:strip(full),ctl:strip(ctl)};
  }
  const sum=(xs,k)=>xs.reduce((a,x)=>a+x[k],0);
  const inr=p=>(p/100).toLocaleString('en-IN');

  // ───────────────────────── self-checks (no engine needed) ─────────────────────────
  test(T+'rulebook §8.1: the opening balances are ₹77,40,000 on both sides',()=>{
    assert.equal(sum(OPENING,'dr'),R(7740000));assert.equal(sum(OPENING,'cr'),R(7740000));
  });
  test(T+'rulebook §8.2: every one of the 26 entries balances, and opening + T1…T19 close to the §8.3 trial balance of ₹84,27,500',()=>{
    assert.equal(ALL.length,26);
    for(const k of ALL)assert.equal(sum(STEP[k],'dr'),sum(STEP[k],'cr'),k);
    const m=model(ALL);
    assert.deepEqual(Object.fromEntries(Object.entries(m.ctl).map(([a,v])=>[a,v/100])),RULEBOOK_TB_83);
    const dr=Object.values(m.ctl).filter(v=>v>0).reduce((a,v)=>a+v,0),cr=-Object.values(m.ctl).filter(v=>v<0).reduce((a,v)=>a+v,0);
    assert.equal(dr,R(8427500));assert.equal(cr,R(8427500));
    // party level of §8.3: bank 5,01,500 Dr, Morning 24,000 Cr, Kaveri 42,000 Cr, Deccan / Goibibo / Staff-07 nil
    assert.equal(m.full['1030/HDFC'],R(501500));assert.equal(m.full['2200/Morning'],-R(24000));assert.equal(m.full['2200/Kaveri'],-R(42000));
    for(const p of ['1110/Deccan','1115/Goibibo','1200/Staff-07'])assert.equal(m.full[p]??0,0,p);
  });
  test(T+'the driven subset and the brief subset: balanced, and they differ by exactly the payroll and drawing steps',()=>{
    for(const set of [DRIVEN,BRIEF]){const m=model(set);assert.equal(Object.values(m.ctl).reduce((a,v)=>a+v,0),0);}
    const d=model(DRIVEN).ctl,b=model(BRIEF).ctl;
    // 1000 −5,000 (T13a), 1030 −2,13,500 −50,000, 1200 nil after T13b, 2300 nil, 5100 + 5110, 2310 / 2320 / 2330, 3000
    assert.equal(b['1000']-d['1000'],-R(5000));assert.equal(b['1030']-d['1030'],-R(263500));assert.equal(b['3000'],R(50000));assert.equal(b['5100'],R(240000));assert.equal(b['5110'],R(24500));
    assert.equal(d['1000'],R(45300));assert.equal(d['1030'],R(445000));assert.equal(b['1000'],R(40300));assert.equal(b['1030'],R(181500));
    assert.equal(model(DRIVEN).full['1110/Deccan'],R(63000));assert.equal(model(DRIVEN).full['2200/Morning'],-R(84000));
  });
  test(T+'the Deccan Agro statement in the model: 40,000 opening, 1,03,000 after T5b, 63,000 after T7a (the brief’s 63,000 / 23,000 leave the opening out)',()=>{
    const run=[R(40000)];
    for(const k of ['T5b','T7a'])run.push(run.at(-1)+STEP[k].filter(x=>x.a==='1110/Deccan').reduce((a,x)=>a+x.dr-x.cr,0));
    assert.deepEqual(run,[R(40000),R(103000),R(63000)]);
    assert.equal(R(63000)-R(40000),R(23000));
  });

  // ───────────────────────── engine tests (need the ACC-1 lanes) ─────────────────────────
  const LANE_OF={accPartyLedgers:'acc1-parties',accStatement:'acc1-statements',accTrialBalance6:'acc1-statements',accDayBook:'acc1-statements',accOpening:'acc1-opening',accOpeningPending:'acc1-opening',accChart:'acc1-chart'};
  const lane=(...names)=>{for(const n of names)if(typeof M[n]!=='function')throw new Error(`[ACC1-RULEBOOK] waits for lane ${LANE_OF[n]??'?'}: ${n} is not exported by the mock service yet (merge claude/acc1 when the coordinator says so)`);};
  let cache;
  /** The scenario once (a state is never mutated by the selectors; tests that execute commands work on their own result). */
  const month=()=>cache??(cache=M.loadScenario('ACC-RULEBOOK'));
  const company=s=>s.groupsX.companies.find(c=>c.name==='Deccan Agro Ltd');
  /** Rulebook party name → party ledger id (contract §3). */
  const partyId=(s,sym)=>{const [c,p]=sym.split('/');
    const id={HDFC:'BA-HDFC',Morning:'EV-MORNING',Kaveri:'EV-KAVERI',Deccan:company(s)?.id,Goibibo:'goibibo','Staff-07':'HR-S07'}[p];return id?`${c}/${id}`:sym;};
  /** Engine balances (paise, Dr positive) of every ledger up to `to`: by full account id and by control code. */
  function engine(s,to=TO){
    const full={},ctl={};
    for(const v of M.accVouchersUpTo(s,to))for(const x of v.entries){full[x.account]=(full[x.account]??0)+x.debit-x.credit;ctl[control(x.account)]=(ctl[control(x.account)]??0)+x.debit-x.credit;}
    const strip=o=>Object.fromEntries(Object.entries(o).filter(([,v])=>v!==0));
    return {full:strip(full),ctl:strip(ctl)};
  }
  const diff=(got,want)=>{const keys=[...new Set([...Object.keys(got),...Object.keys(want)])].sort();
    return keys.filter(k=>(got[k]??0)!==(want[k]??0)).map(k=>`${k}: engine ${inr(got[k]??0)} ≠ expected ${inr(want[k]??0)}`);};

  test(T+'scenario ACC-RULEBOOK is registered and loads: April 2026 closed day by day, 1 May open, 19 stays, the approved opening entry',()=>{
    const info=M.scenarios.find(x=>x.id==='ACC-RULEBOOK');assert.ok(info&&info.available,'scenario listed and available');
    const s=month();
    assert.equal(s.scenario,'ACC-RULEBOOK');assert.equal(s.date,'2026-05-01');assert.equal(s.shiftOpen,true);
    assert.deepEqual(s.closedDates.slice(0,2),[FROM,'2026-04-02']);assert.equal(s.closedDates.length,30);assert.equal(s.closedDates.at(-1),TO);
    assert.equal(s.bookings.length,19);assert.ok(s.bookings.every(b=>b.status==='Departed'));
    assert.equal(s.expenses.filter(x=>x.status==='Posted').length,5);assert.equal(s.cashOutX.records.length,1);
    // fictional parties exist
    assert.ok(company(s)&&company(s).openingOutstanding===R(40000));
    assert.ok(M.mnyVendors(s).some(v=>v.code==='EV-MORNING')&&M.mnyVendors(s).some(v=>v.code==='EV-KAVERI'));
    assert.ok(M.mnyAccounts(s).some(a=>a.code==='BA-HDFC'&&a.kind==='bank'));
    assert.ok(s.channelsX.channels.some(c=>c.id==='goibibo'&&c.name==='Goibibo'));
    assert.equal(s.accountingX.opening.status,'approved');assert.equal(s.accountingX.opening.asOf,FROM);
    // rooms: 155 nights (30 Deccan + 125 guests) of 4,10,000
    const nights=s.bookings.flatMap(b=>b.lines.filter(l=>l.kind==='room'));assert.equal(nights.length,155);assert.equal(nights.reduce((a,l)=>a+l.amount,0),R(410000));
  });
  test(T+'contract §3: the parties exist as sub-ledgers — Deccan Agro (1110), Morning Supplies and Kaveri Textiles (2200), HDFC (1030), Goibibo (1115), Staff-07 (1200), the till (1000)',()=>{
    lane('accPartyLedgers');const s=month(),ps=M.accPartyLedgers(s);
    for(const id of [`1110/${company(s).id}`,'2200/EV-MORNING','2200/EV-KAVERI','1030/BA-HDFC','1115/goibibo','1200/HR-S07'])assert.ok(ps.some(p=>p.id===id),id);
    const p=ps.find(x=>x.id==='1030/BA-HDFC');assert.deepEqual([p.control,p.partyId,p.name,p.active],['1030','BA-HDFC','HDFC',true]);
    assert.ok(ps.some(x=>x.control==='1000'),'a till party under 1000');
  });
  test(T+'opening entry (contract §5): one Journal JRN/20260401/000 dated 1 April, ₹77,40,000 both sides, nothing parked in 1400',()=>{
    lane('accOpening');const s=month(),o=M.accOpening(s);
    assert.equal(o.status,'approved');assert.equal(o.asOf,FROM);assert.equal(o.approvedBy,'owner');assert.equal(o.by,'manager');
    const v=M.accVouchersUpTo(s,FROM).find(x=>x.source.sub==='opening-entry');assert.ok(v,'opening voucher on 1 April');
    assert.equal(v.no,'JRN/20260401/000');assert.equal(v.type,'Journal');assert.deepEqual(v.source,{kind:'opening',id:'opening',sub:'opening-entry'});
    const t=M.accVoucherTotals(v);assert.deepEqual([t.debit,t.credit],[R(7740000),R(7740000)]);
    assert.ok(!v.entries.some(x=>control(x.account)==='1400'));
    const got=Object.fromEntries(v.entries.map(x=>[x.account,x.debit-x.credit]));
    for(const x of OPENING){const id=x.a==='1000'?'1000/TILL':partyId(s,x.a);assert.equal(got[id],x.dr-x.cr,id);}
    assert.equal(M.accOpeningPending(s),false);
  });
  test(T+'every April date: vouchers balanced, no unbalanced / unmapped error, the day closed, 215-ish vouchers in all',()=>{
    const s=month();let n=0;
    for(let d=1;d<=30;d++){const date=`2026-04-${String(d).padStart(2,'0')}`;
      assert.equal(M.accDayStatus(s,date),'Closed',date);
      const vs=M.accVouchers(s,date);n+=vs.length;
      assert.ok(vs.every(M.accBalanced),date+' balanced');
      const errs=M.accExceptions(s,date).filter(x=>x.severity==='error');assert.deepEqual(errs.map(x=>x.title),[],date);}
    assert.ok(n>=200,'vouchers in April: '+n);
  });
  test(T+'closing ledger balances at 30 April equal opening + the 13 driven steps, ledger by ledger (control level)',()=>{
    const s=month(),got=engine(s).ctl,want=model(DRIVEN).ctl;
    assert.deepEqual(diff(got,want),[]);
    // spot checks written out: the ledgers the brief names
    assert.equal(got['1000'],R(45300));assert.equal(got['1030'],R(445000));assert.equal(got['1010'],R(181500));assert.equal(got['1020'],R(150000));
    assert.equal(got['1100']??0,0);assert.equal(got['2000'],-R(10000));assert.equal(got['2110'],-R(14000));assert.equal(got['2120'],-R(14000));
    assert.equal(got['4000'],-R(410000));assert.equal(got['4200'],-R(160000));assert.equal(got['4900'],R(10000));assert.equal(got['5900'],R(200));
    assert.equal(got['9999']??0,0,'nothing in Suspense');assert.equal(got['1400']??0,0,'nothing in the opening difference');
  });
  test(T+'party balances (contract §3): Deccan Agro 63,000 Dr, Morning Supplies 84,000 Cr, Kaveri Textiles 42,000 Cr, HDFC 4,45,000 Dr; no balance left on a control itself',()=>{
    lane('accPartyLedgers');const s=month(),got=engine(s).full,m=model(DRIVEN).full;
    for(const sym of ['1110/Deccan','2200/Morning','2200/Kaveri','1030/HDFC'])assert.equal(got[partyId(s,sym)]??0,m[sym],sym);
    assert.equal(got[partyId(s,'1110/Deccan')],R(63000));assert.equal(got['2200/EV-MORNING'],-R(84000));assert.equal(got['2200/EV-KAVERI'],-R(42000));assert.equal(got['1030/BA-HDFC'],R(445000));
    for(const sym of ['1115/Goibibo','1200/Staff-07'])assert.equal(got[partyId(s,sym)]??0,0,sym);
    for(const c of ['1030','1110','1115','1120','1200','2200'])assert.equal(got[c]??0,0,`control ${c} keeps no balance of its own`);
    assert.equal(Object.entries(got).filter(([a])=>control(a)==='1000'&&a!=='1000').reduce((a,[,b])=>a+b,0),R(45300),'cash sits on the till party');
    assert.equal(got['1000']??0,0,'1000 itself holds nothing');
  });
  test(T+'step by step, on the dates of the rulebook: T7a (10 Apr), T8 (5 Apr), T9 (12 Apr), T10 (15 Apr), T15a (25 Apr), T15b (26 Apr) post their exact entries',()=>{
    lane('accPartyLedgers');const s=month();
    const only=(date,step,pred)=>{const vs=M.accVouchers(s,date).filter(pred);assert.equal(vs.length,1,`${step}: one voucher on ${date}`);return vs[0];};
    const set=lines=>lines.map(l=>l.join('|')).sort();
    const same=(v,step)=>assert.deepEqual(set(v.entries.map(x=>[x.account,x.debit,x.credit])),set(STEP[step].map(x=>[partyId(s,x.a),x.dr,x.cr])),`${step} ${v.no}`);
    same(only('2026-04-10','T7a',v=>v.entries.some(x=>control(x.account)==='1110')),'T7a');
    same(only('2026-04-05','T8',v=>v.source.kind==='expense'),'T8');
    same(only('2026-04-12','T9',v=>v.source.kind==='expense'),'T9');
    same(only('2026-04-15','T10',v=>v.source.kind==='expense'),'T10');
    // T15a: Dr the bank party, Cr cash on the till (the till party id is the lane's: only the control is fixed by the rulebook)
    const bank=only('2026-04-25','T15a',v=>v.source.sub==='bank-deposit');
    assert.deepEqual(bank.entries.map(x=>[control(x.account),x.debit,x.credit]),[['1030',R(150000),0],['1000',0,R(150000)]]);
    assert.equal(bank.entries[0].account,partyId(s,'1030/HDFC'));
    const short=only('2026-04-26','T15b',v=>v.source.sub==='cash-difference');
    assert.deepEqual(short.entries.map(x=>[control(x.account),x.debit,x.credit]),[['5900',R(200),0],['1000',0,R(200)]]);
  });
  test(T+'six-column trial balance, 1–30 April (contract §6): balanced, closing = the expectation ledger by ledger, opening columns carry the opening entry, detail rows tie to the controls',()=>{
    lane('accTrialBalance6','accPartyLedgers');const s=month();
    const want=model(DRIVEN).ctl,dr=Object.values(want).filter(v=>v>0).reduce((a,v)=>a+v,0);
    const net=r=>r.closingDebit-r.closingCredit;
    const sum6=M.accTrialBalance6(s,FROM,TO,false);
    assert.equal(sum6.balanced,true);assert.equal(sum6.openingPending,false);assert.equal(sum6.detail,false);assert.equal(sum6.from,FROM);assert.equal(sum6.to,TO);
    assert.equal(sum6.totals.debit,sum6.totals.credit);assert.equal(sum6.totals.closingDebit,sum6.totals.closingCredit);assert.equal(sum6.totals.closingDebit,dr);
    assert.equal(sum6.totals.openingDebit,sum6.totals.openingCredit,'opening columns balance (the opening entry dated on 1 April counts as opening or as period, never half)');
    const row=(tb,c)=>tb.rows.find(r=>r.account===c);
    for(const [c,v] of Object.entries(want)){const r=row(sum6,c);assert.ok(r,'row '+c);assert.equal(net(r),v,c);assert.ok(r.closingDebit===0||r.closingCredit===0,c+' shows one side');}
    for(const r of sum6.rows)for(const k of ['openingDebit','openingCredit','debit','credit','closingDebit','closingCredit'])assert.ok(r[k]>=0,`never negative on screen: ${r.account} ${k}`);
    // from 2 April everything before is the opening: the opening entry plus the nights of 1 April
    const apr2=M.accTrialBalance6(s,'2026-04-02',TO,false);
    assert.equal(apr2.balanced,true);assert.equal(apr2.totals.closingDebit,dr);
    assert.equal(row(apr2,'1500').openingDebit,R(6000000));assert.equal(row(apr2,'3100').openingCredit,R(5705000));assert.equal(row(apr2,'1110').openingDebit,R(40000));assert.equal(row(apr2,'2200').openingCredit,R(25000));
    for(const r of apr2.rows)assert.equal(r.openingDebit-r.openingCredit+r.debit-r.credit,net(r),'opening + period = closing: '+r.account);
    // detail: a row per party under its control, summing to the control row
    const det=M.accTrialBalance6(s,FROM,TO,true);assert.equal(det.detail,true);assert.equal(det.balanced,true);
    for(const c of ['1000','1030','1110','2200']){
      const parts=det.rows.filter(r=>r.control===c);assert.ok(parts.length>=1,'party rows under '+c);
      assert.equal(parts.reduce((a,r)=>a+net(r),0),want[c],'parties sum to '+c);}
    assert.equal(net(det.rows.find(r=>r.account===partyId(s,'1110/Deccan'))),R(63000));
    assert.equal(det.rows.find(r=>r.account==='2200/EV-MORNING').closingCredit,R(84000));
    assert.equal(det.rows.find(r=>r.account==='2200/EV-KAVERI').closingCredit,R(42000));
    assert.equal(net(det.rows.find(r=>r.account==='1030/BA-HDFC')),R(445000));
  });
  test(T+'statement of the Deccan Agro party 1110/<company> (contract §6): opening before from, running balance, Dr / Cr display',()=>{
    lane('accStatement','accPartyLedgers');const s=month(),acct=partyId(s,'1110/Deccan');
    // from 2 April: the opening entry (1 April) is the opening balance, ₹40,000 Dr
    const st=M.accStatement(s,acct,'2026-04-02',TO);
    assert.equal(st.account,acct);assert.ok(st.name.includes('Deccan Agro'),'the statement is named after the party: '+st.name);assert.equal(st.opening,R(40000));
    assert.deepEqual(st.rows.map(r=>[r.date,r.debit,r.credit,r.running]),[['2026-04-06',R(63000),0,R(103000)],['2026-04-10',0,R(40000),R(63000)]]);
    assert.equal(st.debit,R(63000));assert.equal(st.credit,R(40000));assert.equal(st.closing,R(63000));
    assert.equal(st.rows[0].type,'Journal');assert.equal(st.rows[1].type,'Receipt');
    assert.ok(/HDFC|Bank/i.test(st.rows[1].counter),'T7a counter-ledger is the bank: '+st.rows[1].counter);
    assert.ok(/Guest ledger/i.test(st.rows[0].counter),'T5b counter-ledger is the guest ledger: '+st.rows[0].counter);
    // from 1 April: the opening journal is dated 1 April — it is the opening balance (rulebook §7.2) or the first row; either way the figures tie
    const full=M.accStatement(s,acct,FROM,TO);
    assert.equal(full.closing,R(63000));assert.equal(full.rows.at(-1).running,R(63000));assert.equal(full.opening+full.debit-full.credit,full.closing);
    assert.deepEqual(full.rows.filter(r=>r.no!=='JRN/20260401/000').map(r=>r.running),[R(103000),R(63000)]);
    // the same party after T5b only: 1,03,000; the control ledger statement ties to its parties
    const upTo9=M.accStatement(s,acct,'2026-04-02','2026-04-09');assert.equal(upTo9.closing,R(103000));
    const ctl=M.accStatement(s,'1110','2026-04-02',TO);assert.equal(ctl.closing,R(63000),'control statement = sum of its parties');
    // a creditor reads as Cr (negative running): Morning Supplies 84,000 Cr (opening 25,000 Cr + T8)
    const mo=M.accStatement(s,'2200/EV-MORNING','2026-04-02',TO);assert.equal(mo.opening,-R(25000));assert.equal(mo.closing,-R(84000));
    assert.deepEqual(mo.rows.map(r=>[r.date,r.credit,r.running]),[['2026-04-05',R(59000),-R(84000)]]);
  });
  test(T+'day book (contract §6): every voucher of April in date, number order, one row per entry',()=>{
    lane('accDayBook');const s=month(),db=M.accDayBook(s,FROM,TO),rows=Array.isArray(db)?db:db.rows,vs=M.accVouchersUpTo(s,TO);
    const entries=vs.reduce((a,v)=>a+v.entries.length,0),openingEntries=vs.filter(v=>v.source.sub==='opening-entry').reduce((a,v)=>a+v.entries.length,0);
    assert.ok(rows.length===entries||rows.length===entries-openingEntries,`one row per entry (${rows.length} of ${entries})`);
    const keys=rows.map(r=>r.date+'|'+r.no);assert.deepEqual(keys,[...keys].sort());
    assert.equal(rows.reduce((a,r)=>a+r.debit,0),rows.reduce((a,r)=>a+r.credit,0));
    assert.ok(rows.every(r=>r.date>=FROM&&r.date<=TO));
  });
  test(T+'opening pending refuses a period lock; an approved opening allows it (contract §5)',()=>{
    lane('accOpeningPending');let s=month();
    assert.equal(M.accOpeningPending(s),false);
    // the Owner reopens the approved entry (no lock exists yet) → draft → the lock of a date on/after asOf is refused
    s=M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Correct a line before the first lock (fictional)'});
    assert.equal(M.accOpening(s).status,'draft');assert.equal(M.accOpeningPending(s),true);
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:FROM}),e=>/opening/i.test(e.message),'lock refused while the opening is pending');
    assert.ok(/opening/i.test(M.accLockBlocker(s,FROM)),M.accLockBlocker(s,FROM));
    assert.equal(M.accDayStatus(s,FROM),'Closed');
    // approved again → the lock is allowed, and the locked day keeps the opening journal in its snapshot
    s=M.execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    assert.equal(M.accLockBlocker(s,FROM),'');
    s=M.execute(s,'accountant',{type:'ACC_LOCK',date:FROM});
    assert.equal(M.accDayStatus(s,FROM),'Locked');
    assert.equal(M.accLockOf(s,FROM).debit,M.accLockOf(s,FROM).credit);
    // after a lock on/after asOf the opening cannot be reopened
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Too late (fictional)'}),/lock/i);
    // the cumulative trial balance is unchanged by locking
    assert.deepEqual(diff(engine(s).ctl,model(DRIVEN).ctl),[]);
  });
  test(T+'a Manager enters, only the Owner approves (contract §7): the approved opening is read-only',()=>{
    lane('accOpening');const s=month();
    assert.throws(()=>M.execute(s,'manager',{type:'ACC_OPENING_APPROVE'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:FROM,lines:M.accOpening(s).lines}),e=>/approved|reopen|read-only/i.test(e.message));
    assert.throws(()=>M.execute(s,'cashier',{type:'ACC_OPENING_SAVE',asOf:FROM,lines:[]}),e=>e.code==='FORBIDDEN');
  });
  test(T+'every voucher entry of the month names a ledger of the chart (nothing in 9999, party ids resolve to a party sub-ledger)',()=>{
    lane('accPartyLedgers');const s=month(),ids=new Set(M.accPartyLedgers(s).map(p=>p.id));
    for(const v of M.accVouchersUpTo(s,TO))for(const x of v.entries){
      assert.notEqual(control(x.account),'9999',v.no);
      if(x.account.includes('/'))assert.ok(ids.has(x.account),`${x.account} in ${v.no} is not a party ledger`);}
  });
}
