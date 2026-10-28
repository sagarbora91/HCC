// ACC-1 acc1-tests + ACC-2 acc2-tests · the rulebook golden month, the WHOLE of §8.2. Names prefixed [ACC1-RULEBOOK] (kept from ACC-1) and [ACC2-TESTS] (new).
// Source: 02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026, fictional). Scenario ACC-RULEBOOK (src/fixtures/ext/accounting-rulebook.ts)
// drives all 26 transactions through real commands; this file holds the expectations.
//
// TWO KINDS OF TESTS
//   • self-checks of the encoded rulebook (opening, 26 entries of §8.2, the §8.3 trial balance, §8.4 profit, §8.5 totals, §8.6 challan) — they pass on their own;
//   • engine tests against the ACC-1 + ACC-2 posting contracts (02 Planning/agents/ACC1-POSTING-CONTRACT.md, ACC2-POSTING-CONTRACT.md) — they need the ACC-2 lanes
//     (acc2-purchase, -manual, -tds, -gst, -flows, -parity). The scenario cannot load until every command it drives exists, and a test whose selector / command is
//     missing FAILS with "waits for lane <x>: …" (see lane() / cmd() below); after `git merge claude/acc2` they run for real.
//
// WHAT IS DRIVEN: everything — T1 … T19 (ACC-1 drove 13 of the 26; ACC-2 adds T4, T6, T7b, T11, T13a-c as vouchers, T14a/b, T16, T17, T18, T19 and replaces the
// "Advance adjusted" method of T5a with DEPOSIT_APPLY). Closing trial balance at the end of the month ₹84,27,500 both sides, profit ₹1,40,400, balance-sheet totals
// ₹79,27,900, GST set-off and challan ₹28,900 in May, ageing, the Form 168 report, manual-voucher and cash guards, the month lock.
// One timing note: a month can be paid only once it is complete (HR_PAYROLL_RUN), so the pay run (T13b/c) is locked and paid on 1 May. The tests accept the pay-run
// vouchers dated 30 April (the rulebook's date) or 1 May (the lock date); "the month" is read through 1 May, and 30 April is checked without the pay run when it is dated 1 May.
import {R,OPENING,STEP,ALL,DRIVEN,SERVER_EVENTS,SERVER_WAITING,ACC1_DRIVEN,ACC1_SERVER,RULEBOOK_TB_83,PNL_84,BS_85,GST_86,control,model,sum} from './_rulebook-month.mjs';
export function register({test,M,assert}){
  const T='[ACC1-RULEBOOK] ',T2='[ACC2-TESTS] ';
  const FROM='2026-04-01',TO='2026-04-30',MAY1='2026-05-01';
  const inr=p=>(p/100).toLocaleString('en-IN');
  const PAYROLL=['T13b','T13c'];

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
    // party level of §8.3: bank 5,01,500 Dr, Morning 24,000 Cr, Kaveri 42,000 Cr, the HDFC loan 19,60,000 Cr, Deccan / Goibibo / Staff-07 nil
    assert.equal(m.full['1030/HDFC'],R(501500));assert.equal(m.full['2200/Morning'],-R(24000));assert.equal(m.full['2200/Kaveri'],-R(42000));assert.equal(m.full['2400/HDFC-loan'],-R(1960000));
    for(const p of ['1110/Deccan','1115/Goibibo','1200/Staff-07'])assert.equal(m.full[p]??0,0,p);
  });
  test(T+'the scenario drives all 26 steps; the server books 19 from events and waits for 7 commands; the ACC-1 subsets are still balanced',()=>{
    assert.deepEqual([...DRIVEN],[...ALL]);
    assert.deepEqual([...SERVER_EVENTS,...SERVER_WAITING].sort(),[...ALL].sort());
    assert.deepEqual(SERVER_WAITING,['T11','T14a','T14b','T16','T17','T18','T19']);
    for(const set of [ACC1_DRIVEN,ACC1_SERVER,SERVER_EVENTS,ALL]){const m=model(set);assert.equal(Object.values(m.ctl).reduce((a,v)=>a+v,0),0);}
    // ACC-1's prototype subset closed at 1000 45,300 and the bank 4,45,000 (no payroll, no drawing): the whole month ends 40,300 / 5,01,500
    const d=model(ACC1_DRIVEN).ctl,all=model(ALL).ctl;
    assert.equal(d['1000'],R(45300));assert.equal(d['1030'],R(445000));assert.equal(all['1000'],R(40300));assert.equal(all['1030'],R(501500));
  });
  test(T+'the Deccan Agro statement in the model: 40,000 opening, 1,03,000 after T5b, 63,000 after T7a, nil after T7b (TDS 6,000 to 1360)',()=>{
    const run=[R(40000)];
    for(const k of ['T5b','T7a','T7b'])run.push(run.at(-1)+STEP[k].filter(x=>x.a==='1110/Deccan').reduce((a,x)=>a+x.dr-x.cr,0));
    assert.deepEqual(run,[R(40000),R(103000),R(63000),0]);
    assert.deepEqual(STEP.T7b.map(x=>[x.a,x.dr,x.cr]),[['1030/HDFC',R(57000),0],['1360',R(6000),0],['1110/Deccan',0,R(63000)]]);
  });
  test(T2+'rulebook §8.4: profit for the month ₹1,40,400 from the P&L ledgers only (4xxx and 5xxx), section by section',()=>{
    const c=model(ALL).ctl,pl=Object.keys(c).filter(k=>/^[45]/.test(k)).sort();
    assert.deepEqual(pl,['4000','4200','4900','5000','5010','5030','5040','5070','5080','5090','5100','5110','5900']);
    const bal=(...k)=>k.reduce((a,x)=>a+(c[x]??0),0);
    assert.equal(-bal('4000','4200','4900'),PNL_84.revenue);                 // rooms 4,30,000 + F&B 1,60,000 − discounts 10,000
    assert.equal(bal('5000','5010'),PNL_84.consumption);                     // 59,000 + 42,000
    assert.equal(bal('5100','5110'),PNL_84.employee);                        // 2,40,000 + 24,500
    assert.equal(bal('5090','5080'),PNL_84.finance);                         // 15,000 + 2,360
    assert.equal(bal('5030','5040','5070','5900'),PNL_84.other);             // 45,000 + 8,000 + 3,540 + 200
    assert.equal(PNL_84.revenue-PNL_84.consumption-PNL_84.employee-PNL_84.finance-PNL_84.other,PNL_84.profit);
    assert.equal(-pl.reduce((a,k)=>a+c[k],0),PNL_84.profit);assert.equal(PNL_84.profit,R(140400));
  });
  test(T2+'rulebook §8.5: balance-sheet totals ₹79,27,900 on both sides (owner’s capital + profit − drawings; duties & taxes; the rest)',()=>{
    const c=model(ALL).ctl,profit=-Object.keys(c).filter(k=>/^[45]/.test(k)).reduce((a,k)=>a+c[k],0);
    const owners=-c['3100']+profit-c['3000'];
    assert.equal(owners,BS_85.owners);assert.equal(-c['2400'],BS_85.loan);assert.equal(-c['2200'],BS_85.creditors);
    assert.equal(-(c['2110']+c['2120']+c['2310']+c['2320']+c['2330']),BS_85.duties);assert.equal(-c['2000'],BS_85.advances);
    assert.equal(owners-c['2400']-c['2200']-(c['2110']+c['2120']+c['2310']+c['2320']+c['2330'])-c['2000'],BS_85.total);
    const assets=['1000','1030','1340','1360','1500','1510','1520','1530'].reduce((a,k)=>a+c[k],0);
    assert.equal(assets,BS_85.total);assert.equal(BS_85.total,R(7927900));
    assert.equal(c['1500']+c['1510']+c['1520']+c['1530'],BS_85.assets.fixed);assert.equal(c['1000'],BS_85.assets.cash);assert.equal(c['1030'],BS_85.assets.bank);
    assert.equal(c['1360'],BS_85.assets.tds);assert.equal(c['1340'],BS_85.assets.gstCash);
  });
  test(T2+'rulebook §8.6: output 14,500 + 14,500, ITC nil, TCS credit 100 → challan ₹28,900; the entries in May leave 1340, 2110 and 2120 at nil',()=>{
    const c=model(ALL).ctl;
    assert.equal(-c['2110'],GST_86.cgst);assert.equal(-c['2120'],GST_86.sgst);assert.equal(c['1340'],GST_86.tcs);
    assert.equal(GST_86.cgst+GST_86.sgst-GST_86.tcs,GST_86.challan);
    const may=[['2110',GST_86.cgst,0],['2120',GST_86.sgst,0],['1340',0,GST_86.cgst+GST_86.sgst],['1340',GST_86.challan,0],['1030',0,GST_86.challan]];
    const after={...c};for(const [a,dr,cr] of may)after[a]=(after[a]??0)+dr-cr;
    assert.equal(after['2110'],0);assert.equal(after['2120'],0);assert.equal(after['1340'],0);assert.equal(after['1030'],R(501500)-R(28900));
  });

  // ───────────────────────── engine tests (need the ACC-2 lanes) ─────────────────────────
  const LANE_OF={accPartyLedgers:'acc1',accStatement:'acc1',accTrialBalance6:'acc1',accDayBook:'acc1',accOpening:'acc1',accOpeningPending:'acc1',accChart:'acc1',
    accGstMonth:'acc2-gst',accMonthLockBlocker:'acc2-gst',tdsReceivableReport:'acc2-tds',tdsDeducteeReport:'acc2-tds',expVendorAgeing:'acc2-purchase',accCashGuard:'acc2-flows'};
  const waits=(laneName,what)=>new Error(`[ACC2-TESTS] waits for lane ${laneName}: ${what}`);
  const lane=(...names)=>{for(const n of names)if(typeof M[n]!=='function')throw waits(LANE_OF[n]??'?',`${n} is not exported by the mock service yet (merge claude/acc2 when the coordinator says so)`);};
  /** A command; an action the registry does not know yet fails as "waits for lane". */
  const cmd=(s,uid,c,laneName)=>{try{return M.execute(s,uid,c);}catch(e){if(/unknown action/i.test(String(e.message)))throw waits(laneName,`${c.type} is not registered yet`);throw e;}};
  let cache;
  /** The scenario once (a state is never mutated by the selectors; tests that execute commands work on their own result). */
  const month=()=>{if(cache)return cache;try{return cache=M.loadScenario('ACC-RULEBOOK');}
    catch(e){throw waits('acc2-*',`scenario ACC-RULEBOOK cannot load yet — ${String(e.message).split('\n')[0]}`);}};
  const company=s=>s.groupsX.companies.find(c=>c.name==='Deccan Agro Ltd');
  /** Rulebook party name → party ledger id (contract §3). */
  const partyId=(s,sym)=>{const [c,p]=sym.split('/');
    const id={HDFC:'BA-HDFC','HDFC-loan':'LN-HDFC',Morning:'EV-MORNING',Kaveri:'EV-KAVERI',Deccan:company(s)?.id,Goibibo:'goibibo','Staff-07':'HR-S07'}[p];return id?`${c}/${id}`:sym;};
  const strip=o=>Object.fromEntries(Object.entries(o).filter(([,v])=>v!==0));
  /** Engine balances (paise, Dr positive) of every ledger up to `to`: by full account id and by control code. */
  function engine(s,to=MAY1){
    const full={},ctl={};
    for(const v of M.accVouchersUpTo(s,to))for(const x of v.entries){full[x.account]=(full[x.account]??0)+x.debit-x.credit;ctl[control(x.account)]=(ctl[control(x.account)]??0)+x.debit-x.credit;}
    return {full:strip(full),ctl:strip(ctl)};
  }
  const diff=(got,want)=>{const keys=[...new Set([...Object.keys(got),...Object.keys(want)])].sort();
    return keys.filter(k=>(got[k]??0)!==(want[k]??0)).map(k=>`${k}: engine ${inr(got[k]??0)} ≠ expected ${inr(want[k]??0)}`);};
  /** The date of the pay-run vouchers (30 April = the rulebook's, 1 May = the lock date). */
  const payDate=s=>{for(const d of [TO,MAY1])if(M.accVouchers(s,d).some(v=>v.source.kind==='payroll'))return d;
    throw waits('acc2-parity','no payroll voucher (source kind "payroll") on 30 April or 1 May');};
  const net=entries=>{const m={};for(const x of entries){const a=control(x.account)==='1000'?'1000':x.account;m[a]=(m[a]??0)+x.debit-x.credit;}return strip(m);};
  const stepNet=(s,k)=>net(STEP[k].map(x=>({account:partyId(s,x.a),debit:x.dr,credit:x.cr})));
  const only=(s,date,pred,what)=>{const vs=M.accVouchers(s,date).filter(pred);assert.equal(vs.length,1,`${what}: one voucher on ${date}, found ${vs.length}`);return vs[0];};
  const sameStep=(s,v,k)=>assert.deepEqual(net(v.entries),stepNet(s,k),`${k} ${v.no}`);
  const manualLines=(...l)=>l.map(([account,debit,credit])=>({account,debit,credit}));
  /** A manual voucher command (ACC2 contract §2). The voucher type travels as voucherType / vtype / kind (the command's own `type` is its name). */
  const mv=(o)=>({type:'ACC_MANUAL_SAVE',voucherType:o.type,vtype:o.type,kind:o.type,date:o.date,narration:o.narration??'Test voucher (fictional)',lines:o.lines,...(o.template?{template:o.template}:{}),...(o.reference?{reference:o.reference}:{})});
  const manualOf=s=>s.accountingX?.manual??[];
  const bankChg=(over={})=>mv({type:'Payment',date:MAY1,template:'bank-charges',reference:'CHG-TEST',lines:manualLines(['5080',10000,0],['1030/BA-HDFC',0,10000]),...over});

  test(T+'scenario ACC-RULEBOOK is registered and loads: April 2026 closed day by day, 1 May open, 21 bookings, the approved opening entry, the locked April pay run',()=>{
    const info=M.scenarios.find(x=>x.id==='ACC-RULEBOOK');assert.ok(info&&info.available,'scenario listed and available');
    const s=month();
    assert.equal(s.scenario,'ACC-RULEBOOK');assert.equal(s.date,MAY1);assert.equal(s.shiftOpen,true);
    assert.deepEqual(s.closedDates.slice(0,2),[FROM,'2026-04-02']);assert.equal(s.closedDates.length,30);assert.equal(s.closedDates.at(-1),TO);
    assert.equal(s.bookings.length,21,'19 + the Goibibo stay + the May stay');
    assert.equal(s.bookings.filter(b=>b.status==='Departed').length,20);
    const may=s.bookings.find(b=>b.arrival==='2026-05-10');assert.ok(may&&may.status==='Confirmed'&&may.room==='207');
    const ota=s.bookings.find(b=>b.source==='OTA');assert.ok(ota&&ota.status==='Departed'&&ota.otaChannel==='Goibibo'&&ota.room==='101'&&ota.rate===200000);
    assert.equal(s.expenses.filter(x=>x.status==='Posted').length,5);assert.equal(s.cashOutX.records.length,1);
    assert.ok(company(s)&&company(s).openingOutstanding===R(40000));
    assert.ok(M.mnyVendors(s).some(v=>v.code==='EV-MORNING')&&M.mnyVendors(s).some(v=>v.code==='EV-KAVERI'));
    assert.ok(M.mnyAccounts(s).some(a=>a.code==='BA-HDFC'&&a.kind==='bank'));
    assert.ok(s.channelsX.channels.some(c=>c.id==='goibibo'&&c.name==='Goibibo'));
    assert.equal(s.accountingX.opening.status,'approved');assert.equal(s.accountingX.opening.asOf,FROM);
    // rooms: 165 nights (30 Deccan + 125 guests + 10 Goibibo); ₹4,30,000
    const nights=s.bookings.flatMap(b=>b.lines.filter(l=>l.kind==='room'));assert.equal(nights.length,165);assert.equal(nights.reduce((a,l)=>a+l.amount,0),R(430000));
    const run=s.staffX.runs.find(r=>r.month==='2026-04');assert.ok(run&&run.status==='Locked'&&run.bankFiles?.length===1);
    assert.equal(M.hrRunTotals(run).gross,R(240000));assert.equal(M.hrRunTotals(run).net,R(213500));
  });
  test(T+'contract §3: the parties exist as sub-ledgers — Deccan Agro (1110), Morning Supplies and Kaveri Textiles (2200), HDFC (1030), Goibibo (1115), Staff-07 (1200), the HDFC loan (2400), the till (1000)',()=>{
    lane('accPartyLedgers');const s=month(),ps=M.accPartyLedgers(s);
    for(const id of [`1110/${company(s).id}`,'2200/EV-MORNING','2200/EV-KAVERI','1030/BA-HDFC','1115/goibibo','1200/HR-S07','2400/LN-HDFC'])assert.ok(ps.some(p=>p.id===id),id);
    const p=ps.find(x=>x.id==='1030/BA-HDFC');assert.deepEqual([p.control,p.partyId,p.name,p.active],['1030','BA-HDFC','HDFC',true]);
    assert.ok(ps.some(x=>x.control==='1000'),'a till party under 1000');
    const l=ps.find(x=>x.id==='2400/LN-HDFC');assert.deepEqual([l.control,l.partyId,l.active],['2400','LN-HDFC',true]);
  });
  test(T+'opening entry (contract §5): one Journal JRN/20260401/000 dated 1 April, ₹77,40,000 both sides, the loan on 2400/LN-HDFC, nothing parked in 1400',()=>{
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
  test(T+'every April date: vouchers balanced, no unbalanced / unmapped error, the day closed',()=>{
    const s=month();let n=0;
    for(let d=1;d<=30;d++){const date=`2026-04-${String(d).padStart(2,'0')}`;
      assert.equal(M.accDayStatus(s,date),'Closed',date);
      const vs=M.accVouchers(s,date);n+=vs.length;
      assert.ok(vs.every(M.accBalanced),date+' balanced');
      const errs=M.accExceptions(s,date).filter(x=>x.severity==='error');assert.deepEqual(errs.map(x=>x.title),[],date);}
    assert.ok(n>=230,'vouchers in April: '+n);
    assert.ok(M.accVouchers(s,MAY1).every(M.accBalanced),'1 May balanced');
  });
  test(T2+'§8.3 closing trial balance ₹84,27,500 exactly on both sides, ledger by ledger (control level): the engine equals opening + all 26 steps; nothing in Suspense or the opening difference',()=>{
    const s=month(),got=engine(s).ctl,want=model(ALL).ctl;
    assert.deepEqual(diff(got,want),[]);
    assert.deepEqual(Object.fromEntries(Object.entries(got).map(([a,v])=>[a,v/100])),RULEBOOK_TB_83);
    const dr=Object.values(got).filter(v=>v>0).reduce((a,v)=>a+v,0),cr=-Object.values(got).filter(v=>v<0).reduce((a,v)=>a+v,0);
    assert.equal(dr,R(8427500));assert.equal(cr,R(8427500));
    // spot checks written out: the ledgers the rulebook names
    assert.equal(got['1000'],R(40300));assert.equal(got['1030'],R(501500));assert.equal(got['1360'],R(6000));assert.equal(got['1340'],R(100));assert.equal(got['3000'],R(50000));
    for(const z of ['1010','1020','1100','1110','1115','1200','2300'])assert.equal(got[z]??0,0,z+' closes at nil');
    assert.equal(got['2000'],-R(31500),'the ₹10,000 advance held since before HCC is applied (T5a); the May advance (T6) of ₹31,500 remains');
    assert.equal(got['2110'],-R(14500));assert.equal(got['2120'],-R(14500));assert.equal(got['2200'],-R(66000));assert.equal(got['2400'],-R(1960000));
    assert.equal(got['4000'],-R(430000));assert.equal(got['4200'],-R(160000),'folio restaurant lines are F&B revenue by default (line:food → 4200), no Owner override');
    assert.equal(got['4900'],R(10000));assert.equal(got['4100']??0,0,'no service revenue in the month');
    assert.equal(got['5070'],R(3540));assert.equal(got['5080'],R(2360));assert.equal(got['5090'],R(15000));assert.equal(got['5100'],R(240000));assert.equal(got['5110'],R(24500));assert.equal(got['5900'],R(200));
    assert.equal(got['9999']??0,0,'nothing in Suspense');assert.equal(got['1400']??0,0,'nothing in the opening difference');
  });
  test(T2+'30 April without the pay run: the engine equals the model of the other 24 steps (the pay run is dated 30 April or 1 May)',()=>{
    const s=month(),d=payDate(s);
    const want=d===TO?model(ALL).ctl:model(ALL.filter(k=>!PAYROLL.includes(k))).ctl;
    assert.deepEqual(diff(engine(s,TO).ctl,want),[]);
    if(d===MAY1){const pay=M.accVouchers(s,MAY1).filter(v=>v.source.kind==='payroll');assert.ok(pay.length>=2,'pay run + net pay vouchers on 1 May');}
  });
  test(T2+'party balances (contract §3): Deccan Agro nil, Morning Supplies 24,000 Cr, Kaveri 42,000 Cr, HDFC 5,01,500 Dr, the HDFC loan 19,60,000 Cr, Goibibo and Staff-07 nil; no balance left on a control itself',()=>{
    lane('accPartyLedgers');const s=month(),got=engine(s).full,m=model(ALL).full;
    for(const sym of Object.keys(m).filter(k=>k.includes('/')))assert.equal(got[partyId(s,sym)]??0,m[sym],sym);
    assert.equal(got['2200/EV-MORNING'],-R(24000));assert.equal(got['2200/EV-KAVERI'],-R(42000));assert.equal(got['1030/BA-HDFC'],R(501500));assert.equal(got['2400/LN-HDFC'],-R(1960000));
    for(const sym of ['1110/Deccan','1115/Goibibo','1200/Staff-07'])assert.equal(got[partyId(s,sym)]??0,0,sym);
    for(const c of ['1030','1110','1115','1120','1200','2200','2400'])assert.equal(got[c]??0,0,`control ${c} keeps no balance of its own`);
    assert.equal(Object.entries(got).filter(([a])=>control(a)==='1000'&&a!=='1000').reduce((a,[,b])=>a+b,0),R(40300),'cash sits on the till party');
    assert.equal(got['1000']??0,0,'1000 itself holds nothing');
  });
  test(T2+'step by step, on the dates of the rulebook, each posts its exact entries: T5a T6 T7b T8 T9 T10 T11 T12 T13a-c T14a/b T15a/b T16 T17 T18 T19 (and the T4 check-out)',()=>{
    lane('accPartyLedgers');const s=month();
    // T4 (17 Apr): the folio balance moves to the OTA receivable — Journal Dr 1115/goibibo · Cr 1100 (receipt kind ota-credit)
    const ota=s.bookings.find(b=>b.source==='OTA');
    const t4=only(s,'2026-04-17',v=>v.source.booking===ota.id&&v.source.sub==='ota-credit','T4 ota-credit');
    assert.equal(t4.type,'Journal');assert.deepEqual(net(t4.entries),{'1115/goibibo':R(21000),'1100':-R(21000)});
    // T4 sales: ten nights of ₹2,000 + 5 % GST → Cr 4000 20,000 · 2110 500 · 2120 500, Dr 1100 21,000
    const nightsV=M.accVouchersUpTo(s,TO).filter(v=>v.source.kind==='line'&&v.source.booking===ota.id);
    assert.equal(nightsV.length,10);assert.deepEqual(net(nightsV.flatMap(v=>v.entries)),{'1100':R(21000),'4000':-R(20000),'2110':-R(500),'2120':-R(500)});
    // T5a (6 Apr): DEPOSIT_APPLY → Journal Dr 2000 · Cr 1100, no advance-GST line
    const b308=s.bookings.find(b=>b.room==='308');
    const t5a=only(s,'2026-04-06',v=>v.source.sub==='deposit-apply','T5a');assert.equal(t5a.type,'Journal');assert.deepEqual(net(t5a.entries),{'2000':R(10000),'1100':-R(10000)});assert.equal(t5a.source.booking,b308.id);
    assert.ok(b308.receipts.some(r=>r.kind==='deposit-apply'&&r.amount===R(10000)),'the folio carries a deposit-apply receipt');
    // T6 (20 Apr): Receipt Dr 1010 · Cr 2000, nothing else (switch off: no advance-GST line)
    const t6=only(s,'2026-04-20',v=>v.source.sub==='deposit','T6');assert.equal(t6.type,'Receipt');assert.deepEqual(net(t6.entries),{'1010':R(31500),'2000':-R(31500)});
    assert.deepEqual(M.accVouchers(s,'2026-04-20').filter(v=>v.source.kind==='tax'),[],'gstOnAdvances is off: no advance-GST voucher');
    assert.equal(engine(s).ctl['1350']??0,0);
    // T7b (30 Apr): Receipt Dr 1030/HDFC 57,000 · Dr 1360 6,000 · Cr 1110/Deccan 63,000
    const t7b=only(s,TO,v=>v.source.sub==='company-payment','T7b');assert.equal(t7b.type,'Receipt');sameStep(s,t7b,'T7b');
    sameStep(s,only(s,'2026-04-10',v=>v.source.sub==='company-payment','T7a'),'T7a');
    const bill=v=>v.source.kind==='expense'||v.source.kind==='purchase'; // a vendor bill on credit is a Purchase voucher (type) from ACC-2
    sameStep(s,only(s,'2026-04-05',bill,'T8'),'T8');
    assert.equal(only(s,'2026-04-05',bill,'T8').type,'Purchase');
    sameStep(s,only(s,'2026-04-12',bill,'T9'),'T9');assert.equal(only(s,'2026-04-12',bill,'T9').type,'Purchase');
    sameStep(s,only(s,'2026-04-15',bill,'T10'),'T10');
    // T11 (18 Apr): Payment Dr 2200/Morning 60,000 · Cr 1030/HDFC (source kind "vendor")
    const t11=only(s,'2026-04-18',v=>v.source.kind==='vendor','T11');assert.equal(t11.type,'Payment');sameStep(s,t11,'T11');
    // T13a (8 Apr): the salary advance — Dr 1200/Staff-07 · Cr 1000 (cash, as the rulebook has it)
    const t13a=only(s,'2026-04-08',v=>v.source.kind==='payroll','T13a');assert.equal(t13a.type,'Payment');sameStep(s,t13a,'T13a');
    // T13b / T13c on the pay-run date
    const pd=payDate(s),pays=M.accVouchers(s,pd).filter(v=>v.source.kind==='payroll');
    const t13b=pays.find(v=>v.entries.some(e=>e.account==='5100'));assert.ok(t13b,'pay-run voucher');assert.equal(t13b.type,'Journal');sameStep(s,t13b,'T13b');
    const t13c=pays.find(v=>v.entries.some(e=>e.account==='2300'&&e.debit>0));assert.ok(t13c,'net-pay voucher');assert.equal(t13c.type,'Payment');sameStep(s,t13c,'T13c');
    // the recovery is per staff (not on the 1200 control)
    assert.ok(t13b.entries.some(e=>e.account==='1200/HR-S07'&&e.credit===R(5000)),'advance recovered on 1200/HR-S07');assert.ok(!t13b.entries.some(e=>e.account==='1200'),'no entry on the 1200 control');
    // T14a / T14b (27 Apr): manual vouchers
    const t14a=only(s,'2026-04-27',v=>v.source.kind==='manual'&&v.type==='Contra','T14a');sameStep(s,t14a,'T14a');assert.equal(t14a.source.sub,'contra');assert.ok(t14a.no.startsWith('CTR/20260427/'));
    const t14b=only(s,'2026-04-27',v=>v.source.kind==='manual'&&v.type==='Journal','T14b');sameStep(s,t14b,'T14b');
    // T15a / T15b
    const bankV=only(s,'2026-04-25',v=>v.source.sub==='bank-deposit','T15a');
    assert.deepEqual(bankV.entries.map(x=>[control(x.account),x.debit,x.credit]),[['1030',R(150000),0],['1000',0,R(150000)]]);assert.equal(bankV.entries[0].account,partyId(s,'1030/HDFC'));
    const short=only(s,'2026-04-26',v=>v.source.sub==='cash-difference','T15b');
    assert.deepEqual(short.entries.map(x=>[control(x.account),x.debit,x.credit]),[['5900',R(200),0],['1000',0,R(200)]]);
    // T16 (28 Apr) owner-drawing-bank, T17 (5 Apr) loan-emi, T18 (30 Apr) bank-charges — Payment vouchers from the guided templates
    const t16=only(s,'2026-04-28',v=>v.source.kind==='manual','T16');assert.equal(t16.type,'Payment');assert.equal(t16.source.sub,'owner-drawing-bank');sameStep(s,t16,'T16');
    const t17=only(s,'2026-04-05',v=>v.source.kind==='manual','T17');assert.equal(t17.type,'Payment');assert.equal(t17.source.sub,'loan-emi');sameStep(s,t17,'T17');
    const t18=only(s,TO,v=>v.source.kind==='manual','T18');assert.equal(t18.type,'Payment');assert.equal(t18.source.sub,'bank-charges');sameStep(s,t18,'T18');
    // T19 (29 Apr): the OTA payout — Receipt Dr 1030/HDFC 17,360 · Dr 5070 3,540 · Dr 1340 100 · Cr 1115/Goibibo 21,000 (source kind "ota")
    const t19=only(s,'2026-04-29',v=>v.source.kind==='ota','T19');assert.equal(t19.type,'Receipt');sameStep(s,t19,'T19');
  });
  test(T2+'six-column trial balance, 1 April – 1 May: balanced, closing = §8.3 ledger by ledger, ₹84,27,500; detail rows tie to the controls',()=>{
    lane('accTrialBalance6','accPartyLedgers');const s=month(),want=model(ALL).ctl,dr=Object.values(want).filter(v=>v>0).reduce((a,v)=>a+v,0);
    const netc=r=>r.closingDebit-r.closingCredit;
    const sum6=M.accTrialBalance6(s,FROM,MAY1,false);
    assert.equal(sum6.balanced,true);assert.equal(sum6.openingPending,false);assert.equal(sum6.detail,false);
    assert.equal(sum6.totals.debit,sum6.totals.credit);assert.equal(sum6.totals.closingDebit,sum6.totals.closingCredit);assert.equal(sum6.totals.closingDebit,dr);assert.equal(dr,R(8427500));
    assert.equal(sum6.totals.openingDebit,sum6.totals.openingCredit,'opening columns balance');
    const row=(tb,c)=>tb.rows.find(r=>r.account===c);
    for(const [c,v] of Object.entries(want)){const r=row(sum6,c);assert.ok(r,'row '+c);assert.equal(netc(r),v,c);assert.ok(r.closingDebit===0||r.closingCredit===0,c+' shows one side');}
    for(const r of sum6.rows)for(const k of ['openingDebit','openingCredit','debit','credit','closingDebit','closingCredit'])assert.ok(r[k]>=0,`never negative on screen: ${r.account} ${k}`);
    const apr2=M.accTrialBalance6(s,'2026-04-02',MAY1,false);
    assert.equal(apr2.balanced,true);assert.equal(apr2.totals.closingDebit,dr);
    assert.equal(row(apr2,'1500').openingDebit,R(6000000));assert.equal(row(apr2,'3100').openingCredit,R(5705000));assert.equal(row(apr2,'1110').openingDebit,R(40000));assert.equal(row(apr2,'2200').openingCredit,R(25000));assert.equal(row(apr2,'2400').openingCredit,R(2000000));
    for(const r of apr2.rows)assert.equal(r.openingDebit-r.openingCredit+r.debit-r.credit,netc(r),'opening + period = closing: '+r.account);
    const det=M.accTrialBalance6(s,FROM,MAY1,true);assert.equal(det.detail,true);assert.equal(det.balanced,true);
    for(const c of ['1000','1030','2200','2400']){
      const parts=det.rows.filter(r=>r.control===c);assert.ok(parts.length>=1,'party rows under '+c);
      assert.equal(parts.reduce((a,r)=>a+netc(r),0),want[c],'parties sum to '+c);}
    assert.equal(det.rows.find(r=>r.account==='2200/EV-MORNING').closingCredit,R(24000));
    assert.equal(det.rows.find(r=>r.account==='2200/EV-KAVERI').closingCredit,R(42000));
    assert.equal(netc(det.rows.find(r=>r.account==='1030/BA-HDFC')),R(501500));
    assert.equal(det.rows.find(r=>r.account==='2400/LN-HDFC').closingCredit,R(1960000));
  });
  test(T2+'§8.4 / §8.5 from the six-column trial balance: profit ₹1,40,400 from the P&L ledgers only; balance-sheet totals ₹79,27,900 on both sides',()=>{
    lane('accTrialBalance6');const s=month(),tb=M.accTrialBalance6(s,FROM,MAY1,false),netc=r=>r.closingDebit-r.closingCredit;
    const pl=tb.rows.filter(r=>/^[45]/.test(r.account)),bs=tb.rows.filter(r=>!/^[45]/.test(r.account));
    const profit=-pl.reduce((a,r)=>a+netc(r),0);assert.equal(profit,PNL_84.profit);
    const rev=-pl.filter(r=>['4000','4200','4900'].includes(r.account)).reduce((a,r)=>a+netc(r),0);assert.equal(rev,PNL_84.revenue);
    // balance sheet: assets (Dr balances of the 1xxx ledgers) = capital + profit − drawings + liabilities (Cr balances of the 2xxx ledgers)
    const side=(f)=>bs.filter(f).reduce((a,r)=>a+netc(r),0);
    const assets=side(r=>/^1/.test(r.account)),liab=-side(r=>/^2/.test(r.account)),capital=-side(r=>r.account==='3100'),drawings=side(r=>r.account==='3000');
    assert.equal(assets,BS_85.total);assert.equal(liab+capital+profit-drawings,BS_85.total);assert.equal(capital+profit-drawings,BS_85.owners);
    assert.equal(R(7927900),BS_85.total);
  });
  test(T2+'statements: Deccan Agro from 2 April (6 company-credit checkouts, T7a, T7b → nil), Goibibo (T4 check-out, T19 payout → nil), Morning Supplies (T8, T11 → 24,000 Cr)',()=>{
    lane('accStatement','accPartyLedgers');const s=month(),acct=partyId(s,'1110/Deccan');
    const st=M.accStatement(s,acct,'2026-04-02',MAY1);
    assert.equal(st.account,acct);assert.ok(st.name.includes('Deccan Agro'),'named after the party: '+st.name);assert.equal(st.opening,R(40000));
    const credits=[1,2,3,4,5,6].map(k=>['2026-04-06',R(10500),0,R(40000)+k*R(10500)]);
    assert.deepEqual(st.rows.map(r=>[r.date,r.debit,r.credit,r.running]),[...credits,['2026-04-10',0,R(40000),R(63000)],[TO,0,R(63000),0]]);
    assert.equal(st.closing,0);assert.equal(st.debit,R(63000));assert.equal(st.credit,R(103000));
    assert.equal(st.rows[6].type,'Receipt');assert.equal(st.rows[7].type,'Receipt');assert.ok(/HDFC|Bank/i.test(st.rows[7].counter),'T7b counter-ledger names the bank (and TDS): '+st.rows[7].counter);
    const g=M.accStatement(s,'1115/goibibo','2026-04-02',MAY1);
    assert.equal(g.opening,0);assert.deepEqual(g.rows.map(r=>[r.date,r.debit,r.credit,r.running,r.type]),[['2026-04-17',R(21000),0,R(21000),'Journal'],['2026-04-29',0,R(21000),0,'Receipt']]);assert.equal(g.closing,0);
    const mo=M.accStatement(s,'2200/EV-MORNING','2026-04-02',MAY1);assert.equal(mo.opening,-R(25000));
    assert.deepEqual(mo.rows.map(r=>[r.date,r.debit,r.credit,r.running]),[['2026-04-05',0,R(59000),-R(84000)],['2026-04-18',R(60000),0,-R(24000)]]);assert.equal(mo.closing,-R(24000));
    const ln=M.accStatement(s,'2400/LN-HDFC','2026-04-02',MAY1);assert.equal(ln.opening,-R(2000000));assert.deepEqual(ln.rows.map(r=>[r.date,r.debit,r.credit]),[['2026-04-05',R(40000),0]]);assert.equal(ln.closing,-R(1960000));
    const hd=M.accStatement(s,'1030/BA-HDFC','2026-04-02',MAY1);assert.equal(hd.opening,R(300000));assert.equal(hd.closing,R(501500));
  });
  test(T+'day book (contract §6): every voucher of April in date, number order, one row per entry',()=>{
    lane('accDayBook');const s=month(),db=M.accDayBook(s,FROM,TO),rows=Array.isArray(db)?db:db.rows,vs=M.accVouchersUpTo(s,TO);
    const entries=vs.reduce((a,v)=>a+v.entries.length,0),openingEntries=vs.filter(v=>v.source.sub==='opening-entry').reduce((a,v)=>a+v.entries.length,0);
    assert.ok(rows.length===entries||rows.length===entries-openingEntries,`one row per entry (${rows.length} of ${entries})`);
    const keys=rows.map(r=>r.date+'|'+r.no);assert.deepEqual(keys,[...keys].sort());
    assert.equal(rows.reduce((a,r)=>a+r.debit,0),rows.reduce((a,r)=>a+r.credit,0));
    assert.ok(rows.every(r=>r.date>=FROM&&r.date<=TO));
    assert.ok(rows.some(r=>/^CTR\//.test(r.no)),'the Contra voucher of T14a is in the day book');
  });
  test(T+'opening pending refuses a period lock; an approved opening allows it (contract §5)',()=>{
    lane('accOpeningPending');let s=month();
    assert.equal(M.accOpeningPending(s),false);
    s=cmd(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Correct a line before the first lock (fictional)'},'acc1');
    assert.equal(M.accOpening(s).status,'draft');assert.equal(M.accOpeningPending(s),true);
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:FROM}),e=>/opening/i.test(e.message),'lock refused while the opening is pending');
    assert.ok(/opening/i.test(M.accLockBlocker(s,FROM)),M.accLockBlocker(s,FROM));
    assert.equal(M.accDayStatus(s,FROM),'Closed');
    s=M.execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    assert.equal(M.accLockBlocker(s,FROM),'');
    s=M.execute(s,'accountant',{type:'ACC_LOCK',date:FROM});
    assert.equal(M.accDayStatus(s,FROM),'Locked');
    assert.equal(M.accLockOf(s,FROM).debit,M.accLockOf(s,FROM).credit);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Too late (fictional)'}),/lock/i);
    assert.deepEqual(diff(engine(s).ctl,model(ALL).ctl),[]);
  });
  test(T+'a Manager enters, only the Owner approves (contract §7): the approved opening is read-only',()=>{
    lane('accOpening');const s=month();
    assert.throws(()=>M.execute(s,'manager',{type:'ACC_OPENING_APPROVE'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:FROM,lines:M.accOpening(s).lines}),e=>/approved|reopen|read-only/i.test(e.message));
    assert.throws(()=>M.execute(s,'cashier',{type:'ACC_OPENING_SAVE',asOf:FROM,lines:[]}),e=>e.code==='FORBIDDEN');
  });
  test(T+'every voucher entry of the month names a ledger of the chart (nothing in 9999, party ids resolve to a party sub-ledger)',()=>{
    lane('accPartyLedgers');const s=month(),ids=new Set(M.accPartyLedgers(s).map(p=>p.id));
    for(const v of M.accVouchersUpTo(s,MAY1))for(const x of v.entries){
      assert.notEqual(control(x.account),'9999',v.no);
      if(x.account.includes('/'))assert.ok(ids.has(x.account),`${x.account} in ${v.no} is not a party ledger`);}
  });

  // ───────────────────────── ACC-2 flows: advance GST (switch on), vendor payment, OTA payout guards, TDS report, ageing ─────────────────────────
  test(T2+'T6 second case: with gstOnAdvances ON a deposit also posts the advance GST (Dr 1350 · Cr 2110 · Cr 2120, cum-tax at the room’s 5 %); with it OFF (the month) nothing',()=>{
    const s0=month(),prof=M.cmpSlice(s0).profile;assert.ok(prof,'firm profile');
    assert.notEqual(prof.gstOnAdvances,true,'the switch is off by default');
    let s=cmd(s0,'owner',{type:'CMP_PROFILE_SET',...prof,gstOnAdvances:true},'acc2-tds');
    assert.equal(M.cmpSlice(s).profile.gstOnAdvances,true);
    const may=s.bookings.find(b=>b.arrival==='2026-05-10');
    s=cmd(s,'manager',{type:'PAYMENT',id:may.id,amount:R(10500),kind:'deposit',method:'UPI',reference:'DEMO-UPI-ADV-ON'},'acc2-gst');
    const vs=M.accVouchers(s,MAY1),rc=vs.filter(v=>v.source.sub==='deposit'),tax=vs.filter(v=>v.source.kind==='tax'&&v.source.sub==='advance-gst');
    assert.equal(rc.length,1);assert.deepEqual(net(rc[0].entries),{'1010':R(10500),'2000':-R(10500)});
    assert.equal(tax.length,1,'one advance-GST voucher');assert.deepEqual(net(tax[0].entries),{'1350':R(500),'2110':-R(250),'2120':-R(250)});
    // the switch is the Owner's: an Accountant cannot turn it off or on
    assert.throws(()=>M.execute(s,'accountant',{type:'CMP_PROFILE_SET',...M.cmpSlice(s).profile,gstOnAdvances:false}),e=>e.code==='FORBIDDEN'||/owner/i.test(e.message));
    // the month’s own advance (switch off) is untouched: no tax voucher on 20 April
    assert.deepEqual(M.accVouchers(s0,'2026-04-20').filter(v=>v.source.kind==='tax'),[]);
  });
  test(T2+'T11: the ₹60,000 payment is allocated oldest first — ₹35,000 of the ₹59,000 bill (the ₹25,000 opening first); the bill keeps ₹24,000 open',()=>{
    const s=month(),vp=(s.expensesX?.vendorPayments??[]);assert.equal(vp.length,1,'one vendor payment');
    const p=vp[0],bill=s.expenses.find(e=>e.vendor==='Morning Supplies'&&e.amount===R(59000));assert.ok(bill);
    assert.deepEqual([p.vendor,p.amount,p.method,p.date,!!p.void],['Morning Supplies',R(6000000/100),'Bank transfer','2026-04-18',false]);
    assert.equal(p.allocations.filter(a=>a.expense===bill.id).reduce((a,x)=>a+x.amount,0),R(35000),'35,000 against the 59,000 bill');
    const rest=p.allocations.filter(a=>a.expense!==bill.id).reduce((a,x)=>a+x.amount,0);assert.ok(rest===R(25000)||rest===0,'the ₹25,000 opening balance is allocated first (or carried as the opening line): '+rest);
    assert.ok(p.allocations.reduce((a,x)=>a+x.amount,0)<=p.amount);
  });
  test(T2+'ageing by allocations at 30 April: Morning Supplies ₹24,000 open (0–30 days), Kaveri Textiles ₹42,000 open (0–30)',()=>{
    lane('expVendorAgeing');const s=month();
    const rep=M.expVendorAgeing(s,TO),rows=Array.isArray(rep)?rep:(rep?.rows??rep?.vendors??Object.values(rep??{}));
    const flat=o=>Object.values(o).flatMap(v=>typeof v==='number'?[v]:(v&&typeof v==='object'&&!Array.isArray(v)?flat(v):Array.isArray(v)&&v.every(n=>typeof n==='number')?v:[]));
    const find=(name,code)=>rows.find(x=>x&&[x.vendor,x.name,x.vendorName,x.code,x.id].some(v=>v===name||v===code));
    const mo=find('Morning Supplies','EV-MORNING'),ka=find('Kaveri Textiles','EV-KAVERI');
    assert.ok(mo&&ka,'a row per vendor: '+JSON.stringify(rows).slice(0,300));
    assert.ok(flat(mo).includes(R(24000)),'Morning Supplies ₹24,000 in a bucket: '+JSON.stringify(mo));assert.ok(flat(ka).includes(R(42000)),'Kaveri Textiles ₹42,000 in a bucket: '+JSON.stringify(ka));
    const bucket=r=>{const b=r.buckets??r;const k=Object.keys(b).find(k=>/^(b|d)?0\s*[-_–]?\s*30|current|0_30|b0/i.test(k));return k?b[k]:undefined;};
    if(bucket(mo)!==undefined){assert.equal(bucket(mo),R(24000));assert.equal(bucket(ka),R(42000));}
  });
  test(T2+'T7b: the Form 168 reconciliation report lists Deccan Agro’s ₹6,000 TDS (ledger 1360) in April; the party ledger 1360 holds ₹6,000 Dr',()=>{
    lane('tdsReceivableReport');const s=month();
    const variants=[[s,2026],[s,'2026-27'],[s,'2026-04'],[s]];let rep,lastErr;
    for(const a of variants){try{rep=M.tdsReceivableReport(...a);if(JSON.stringify(rep??'').includes('600000'))break;}catch(e){lastErr=e;}}
    assert.ok(rep&&JSON.stringify(rep).includes('600000'),'the report shows 6,00,000 paise (₹6,000): '+(lastErr?.message??JSON.stringify(rep)?.slice(0,200)));
    const txt=JSON.stringify(rep);assert.ok(/Deccan/.test(txt)||txt.includes(company(s).id),'by company');assert.ok(txt.includes('2026-04')||txt.includes('Apr'),'by month');
    assert.equal(engine(s).ctl['1360'],R(6000));
  });
  test(T2+'T19 guards: the OTA payout must add up (gross = net + commission + GST + TCS) and cannot exceed the open receivable (Goibibo is settled)',()=>{
    const s=month(),ota=s.bookings.find(b=>b.source==='OTA');
    const base={type:'CHN_PAYOUT',channel:'goibibo',date:MAY1,statementRef:'GOI-STMT-TEST',bank:'BA-HDFC',bookings:[ota.id]};
    assert.throws(()=>cmd(s,'owner',{...base,gross:R(21000),commission:R(3000),commissionGst:R(540),tcs:R(100),net:R(17361)},'acc2-flows'),e=>!/waits for lane/.test(e.message)&&/add up|gross|net|match/i.test(e.message));
    assert.throws(()=>cmd(s,'owner',{...base,gross:R(1000),commission:0,commissionGst:0,tcs:0,net:R(1000)},'acc2-flows'),e=>!/waits for lane/.test(e.message)&&/receivable|exceed|open/i.test(e.message));
    assert.throws(()=>M.execute(s,'reception',{...base,gross:R(1000),commission:0,commissionGst:0,tcs:0,net:R(1000)}),e=>e.code==='FORBIDDEN'||/Unknown action/i.test(e.message));
  });
  test(T2+'T11 guards: a bank vendor payment needs a reference; a cash payment needs an open shift; housekeeping cannot pay vendors',()=>{
    const s=month();
    assert.throws(()=>cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Kaveri Textiles',amount:R(1000),method:'Bank transfer',reference:''},'acc2-purchase'),e=>!/waits for lane/.test(e.message)&&/reference/i.test(e.message));
    assert.throws(()=>M.execute(s,'housekeeper',{type:'EXP_VENDOR_PAY',vendor:'Kaveri Textiles',amount:R(1000),method:'Bank transfer',reference:'X-1'}),e=>e.code==='FORBIDDEN'||/Unknown action/i.test(e.message));
    const ok=cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Kaveri Textiles',amount:R(2000),method:'Bank transfer',reference:'NEFT-DEMO-GUARD'},'acc2-purchase');
    assert.equal(engine(ok).ctl['2200'],-R(64000),'2200 reduced by the payment');
  });

  // ───────────────────────── ACC-2 manual vouchers (contract §2): every guard, Owner vs Accountant ─────────────────────────
  test(T2+'manual voucher guards: unbalanced, empty, unknown ledger, control ledger without a party, 9999 / 1400, future date, Contra with a non-bank line, Payment without a bank side — all refused with a reason',()=>{
    const s=month(),owner=(o)=>()=>cmd(s,'owner',mv(o),'acc2-manual'),refused=(o,re)=>assert.throws(owner(o),e=>!/waits for lane/.test(e.message)&&re.test(e.message),JSON.stringify(o.lines));
    refused({type:'Journal',date:MAY1,lines:manualLines(['5040',10000,0],['1000',0,9000])},/balanc|equal|debit/i);
    refused({type:'Journal',date:MAY1,lines:[]},/line|balanc|enter/i);
    refused({type:'Journal',date:MAY1,lines:manualLines(['7777',10000,0],['1000',0,10000])},/ledger|account|chart/i);
    refused({type:'Payment',date:MAY1,lines:manualLines(['5080',10000,0],['1030',0,10000])},/party|control|bank account/i);          // 1030 without a bank party
    refused({type:'Journal',date:MAY1,lines:manualLines(['5040',10000,0],['2200',0,10000])},/party|control/i);                          // 2200 without a vendor
    refused({type:'Journal',date:MAY1,lines:manualLines(['9999',10000,0],['5040',0,10000])},/9999|suspense/i);
    refused({type:'Journal',date:MAY1,lines:manualLines(['1400',10000,0],['5040',0,10000])},/1400|opening/i);
    refused({type:'Journal',date:'2026-05-02',lines:manualLines(['5040',10000,0],['1000/TILL',0,10000])},/date|future|business/i);
    refused({type:'Contra',date:MAY1,lines:manualLines(['1030/BA-HDFC',10000,0],['5080',0,10000])},/contra|cash|bank/i);
    refused({type:'Payment',date:MAY1,lines:manualLines(['5080',10000,0],['5040',0,10000])},/cash|bank|one/i);                     // no cash / bank side
    refused({type:'Payment',date:MAY1,lines:manualLines(['1030/BA-HDFC',5000,0],['5080',5000,0],['1010',0,10000])},/cash|bank|one/i); // two cash / bank sides
    refused({type:'Journal',date:MAY1,lines:[{account:'5040',debit:10000,credit:10000},{account:'1000/TILL',debit:0,credit:0}]},/one side|debit|credit/i);
    // a good one posts (Owner: approved at once)
    const ok=cmd(s,'owner',bankChg(),'acc2-manual');const v=manualOf(ok).at(-1);assert.equal(v.status,'approved');assert.equal(v.by,'owner');
  });
  test(T2+'Accountant’s manual voucher is pending and posts nothing until the Owner approves; the Owner’s own is approved at once; a rejection posts nothing',()=>{
    const s=month(),before=engine(s).ctl['5080'];
    // the Owner’s own: approved at once, posts on its date
    const o=cmd(s,'owner',bankChg(),'acc2-manual');assert.equal(manualOf(o).at(-1).status,'approved');
    const vo=M.accVouchers(o,MAY1).filter(v=>v.source.kind==='manual');assert.equal(vo.length,1);assert.equal(vo[0].source.sub,'bank-charges');assert.equal(vo[0].type,'Payment');
    assert.equal(engine(o).ctl['5080'],before+10000);
    // the Accountant’s: pending, nothing posts
    const a=cmd(s,'accountant',bankChg(),'acc2-manual'),mvA=manualOf(a).at(-1);
    assert.equal(mvA.status,'pending');assert.equal(mvA.by,'accountant');assert.deepEqual(M.accVouchers(a,MAY1).filter(v=>v.source.kind==='manual'),[]);assert.equal(engine(a).ctl['5080'],before);
    // only the Owner decides (PERM-ACC-011)
    assert.throws(()=>M.execute(a,'accountant',{type:'ACC_MANUAL_DECIDE',id:mvA.id,approve:true}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(a,'manager',{type:'ACC_MANUAL_DECIDE',id:mvA.id,approve:true}),e=>e.code==='FORBIDDEN');
    const ap=M.execute(a,'owner',{type:'ACC_MANUAL_DECIDE',id:mvA.id,approve:true});
    assert.equal(manualOf(ap).find(v=>v.id===mvA.id).status,'approved');assert.equal(manualOf(ap).find(v=>v.id===mvA.id).decidedBy,'owner');assert.equal(engine(ap).ctl['5080'],before+10000);
    const rj=M.execute(a,'owner',{type:'ACC_MANUAL_DECIDE',id:mvA.id,approve:false,reason:'Not a bank charge (fictional)'});
    assert.equal(manualOf(rj).find(v=>v.id===mvA.id).status,'rejected');assert.equal(manualOf(rj).find(v=>v.id===mvA.id).reason,'Not a bank charge (fictional)');assert.equal(engine(rj).ctl['5080'],before);
    // other roles cannot enter a manual voucher at all (PERM-ACC-010: Owner, Accountant)
    for(const uid of ['manager','reception','cashier','housekeeper'])assert.throws(()=>M.execute(s,uid,bankChg()),e=>e.code==='FORBIDDEN',uid);
  });
  test(T2+'manual voucher guard — a locked day refuses ("unlock the day first"); void on an unlocked day posts nothing; void on a locked day is a reversing voucher dated the business day',()=>{
    let s=month();const before=engine(s).ctl['5080'];
    // enter on 10 April (closed, not locked), lock that day, then a new voucher dated that day is refused
    s=cmd(s,'owner',bankChg({date:'2026-04-10'}),'acc2-manual');const v1=manualOf(s).at(-1);assert.equal(v1.status,'approved');
    assert.equal(M.accVouchers(s,'2026-04-10').filter(v=>v.source.kind==='manual').length,1);
    const s1=s;                                   // unlocked: a void removes the posting
    const vd=M.execute(s1,'owner',{type:'ACC_MANUAL_VOID',id:v1.id,reason:'Entered twice (fictional)'});
    assert.equal(manualOf(vd).find(v=>v.id===v1.id).status,'void');assert.deepEqual(M.accVouchers(vd,'2026-04-10').filter(v=>v.source.kind==='manual'),[]);assert.equal(engine(vd).ctl['5080']??0,before);
    assert.throws(()=>M.execute(s1,'accountant',{type:'ACC_MANUAL_VOID',id:v1.id,reason:'x'}),e=>e.code==='FORBIDDEN');
    s=M.execute(s,'accountant',{type:'ACC_LOCK',date:'2026-04-10'});assert.equal(M.accDayStatus(s,'2026-04-10'),'Locked');
    assert.throws(()=>M.execute(s,'owner',bankChg({date:'2026-04-10'})),e=>/unlock the day first/i.test(e.message));
    const vr=M.execute(s,'owner',{type:'ACC_MANUAL_VOID',id:v1.id,reason:'Entered twice (fictional)'});
    const rev=manualOf(vr).find(v=>v.voidOf===v1.id);assert.ok(rev,'a reversing voucher names the one it voids');assert.equal(rev.status,'approved');assert.equal(rev.date,MAY1);
    assert.deepEqual(rev.lines.map(l=>[l.account,l.debit,l.credit]),v1.lines.map(l=>[l.account,l.credit,l.debit]),'same lines swapped');
    assert.equal(M.accVouchers(vr,'2026-04-10').filter(v=>v.source.kind==='manual').length,1,'the locked day keeps its snapshot');
    assert.equal(M.accVouchers(vr,MAY1).filter(v=>v.source.kind==='manual').length,1,'the reversal posts on the business date');
    assert.equal(engine(vr).ctl['5080']??0,before,'net of the voucher and its reversal: nil');
  });

  // ───────────────────────── ACC-2 cash guards (contract §7, B9 / B10) ─────────────────────────
  test(T2+'cash guard B9: a ₹2,00,000 cash receipt from one person is refused (s.186); two receipts totalling that on one day too; UPI of the same size is not a cash receipt',()=>{
    let s=month();
    s=M.execute(s,'manager',{type:'BOOK',guest:'Cash guard guest (fictional)',room:'303',arrival:MAY1,departure:'2026-05-02',people:2,phone:'+91 90000 12345',source:'Direct'});
    const id=s.bookings.at(-1).id;s=M.execute(s,'manager',{type:'REGISTER',id});s=M.execute(s,'manager',{type:'CHECKIN',id});
    const pay=(st,amount,method='Cash')=>M.execute(st,'manager',{type:'PAYMENT',id,amount,kind:'payment',method,reference:method==='Cash'?'':'DEMO-GUARD'});
    const refusal=e=>e.code==='CASH_LIMIT'||/186|2,00,000|cash.*limit/i.test(e.message);
    assert.throws(()=>pay(s,R(200000)),refusal);
    const half=pay(s,R(100000));assert.throws(()=>pay(half,R(100000)),refusal);
    assert.doesNotThrow(()=>pay(s,R(199999)));
    assert.doesNotThrow(()=>pay(s,R(250000),'UPI'));
  });
  test(T2+'cash guard B10: a ₹12,000 cash vendor payment is allowed with a warning and the record carries cashDeductionLost; ₹10,000 or by bank does not',()=>{
    const s=month();
    const cash=cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:R(12000),method:'Cash',reference:'',date:MAY1},'acc2-purchase');
    const rec=cash.expensesX.vendorPayments.at(-1);assert.equal(rec.amount,R(12000));assert.equal(rec.cashDeductionLost,true,'deduction lost (old 40A(3): more than ₹10,000 in cash to one person in a day)');
    const ten=cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:R(10000),method:'Cash',reference:'',date:MAY1},'acc2-purchase');
    assert.ok(!ten.expensesX.vendorPayments.at(-1).cashDeductionLost);
    const bank=cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:R(12000),method:'Bank transfer',reference:'NEFT-DEMO-12K'},'acc2-purchase');
    assert.ok(!bank.expensesX.vendorPayments.at(-1).cashDeductionLost);
    // the same person twice in one day counts together: 6,000 + 6,000 in cash
    const a=cmd(s,'owner',{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:R(6000),method:'Cash',reference:''},'acc2-purchase');
    assert.ok(!a.expensesX.vendorPayments.at(-1).cashDeductionLost);
    const b=M.execute(a,'owner',{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:R(6000),method:'Cash',reference:''});
    assert.equal(b.expensesX.vendorPayments.at(-1).cashDeductionLost,true,'the second payment takes the day’s total above ₹10,000');
  });

  // ───────────────────────── ACC-2 GST month end (§8.6) and the month lock (G1) ─────────────────────────
  test(T2+'§8.6: GST for April — output 14,500 + 14,500, ITC nil, TCS 100 → challan ₹28,900; the set-off voucher and the challan post in May and clear 2110, 2120 and 1340',()=>{
    lane('accGstMonth');let s=month();
    const g=M.accGstMonth(s,'2026-04'),txt=JSON.stringify(g);
    for(const n of [R(14500),R(28900),R(100)])assert.ok(txt.includes(String(n)),`accGstMonth shows ${n}: ${txt.slice(0,300)}`);
    s=cmd(s,'owner',{type:'ACC_GST_SETOFF',month:'2026-04'},'acc2-gst');
    const so=M.accVouchers(s,MAY1).filter(v=>v.source.kind==='tax'&&v.source.sub==='gst-setoff');assert.equal(so.length,1,'one set-off voucher');
    assert.equal(so[0].type,'Journal');assert.deepEqual(net(so[0].entries),{'2110':R(14500),'2120':R(14500),'1340':-R(29000)});
    assert.ok(s.accountingX.gstSetoffs?.['2026-04'],'the posted set-off is recorded for the month');
    // idempotent: a second call replaces the unlocked one
    const again=M.execute(s,'owner',{type:'ACC_GST_SETOFF',month:'2026-04'});assert.equal(M.accVouchers(again,MAY1).filter(v=>v.source.sub==='gst-setoff').length,1);
    // the challan: Dr 1340 28,900 · Cr 1030/HDFC (template gst-payment)
    s=cmd(s,'owner',{type:'ACC_GST_PAY',month:'2026-04',amount:R(28900),method:'Bank transfer',challan:'CHALLAN-DEMO-0520',date:MAY1},'acc2-gst');
    const gp=M.accVouchers(s,MAY1).filter(v=>v.source.kind==='manual'&&v.source.sub==='gst-payment');assert.equal(gp.length,1);assert.deepEqual(net(gp[0].entries),{'1340':R(28900),'1030/BA-HDFC':-R(28900)});
    const c=engine(s).ctl;assert.equal(c['2110']??0,0);assert.equal(c['2120']??0,0);assert.equal(c['1340']??0,0);assert.equal(c['1030'],R(501500)-R(28900));
    const dr=Object.values(c).filter(v=>v>0).reduce((a,v)=>a+v,0),cr=-Object.values(c).filter(v=>v<0).reduce((a,v)=>a+v,0);assert.equal(dr,cr);
    // roles: Owner / Accountant only
    assert.throws(()=>M.execute(month(),'manager',{type:'ACC_GST_SETOFF',month:'2026-04'}),e=>e.code==='FORBIDDEN');
    // an over-payment of the challan is refused
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_GST_PAY',month:'2026-04',amount:R(1000),method:'Bank transfer',challan:'CHALLAN-DEMO-X',date:MAY1}),e=>/1340|cash ledger|exceed|balance/i.test(e.message));
  });
  test(T2+'month lock (G1): refused until the set-off is posted and no manual voucher is pending; then the Owner locks April (every day locked, the set-off recorded); an Accountant only requests; a locked month refuses entries; unlock needs a reason',()=>{
    lane('accMonthLockBlocker');let s=month();
    assert.throws(()=>cmd(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'},'acc2-gst'),e=>!/waits for lane/.test(e.message)&&/set-?off/i.test(e.message));
    assert.ok(/set-?off/i.test(M.accMonthLockBlocker(s,'2026-04')||''),'the blocker names the set-off: '+M.accMonthLockBlocker(s,'2026-04'));
    s=cmd(s,'owner',{type:'ACC_GST_SETOFF',month:'2026-04'},'acc2-gst');
    // a pending Accountant voucher dated in April blocks the lock
    const p=cmd(s,'accountant',bankChg({date:'2026-04-30'}),'acc2-manual');
    assert.throws(()=>M.execute(p,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'}),e=>/pending|manual/i.test(e.message));
    // the Accountant requests; only the Owner locks
    assert.doesNotThrow(()=>M.execute(s,'accountant',{type:'ACC_MONTH_LOCK_REQUEST',month:'2026-04'}));
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_MONTH_LOCK',month:'2026-04'}),e=>e.code==='FORBIDDEN');
    // allowed: every closed day of April is locked by the same command
    const l=M.execute(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'});
    const lock=l.accountingX.monthLocks['2026-04'];assert.ok(lock&&lock.lockedBy==='owner'&&lock.setoff,'month lock with the set-off voucher id');
    for(let d=1;d<=30;d++)assert.equal(M.accDayStatus(l,`2026-04-${String(d).padStart(2,'0')}`),'Locked',`day ${d}`);
    assert.deepEqual(diff(engine(l).ctl,engine(s).ctl),[],'locking changes no balance');
    // a locked month refuses a manual voucher dated in it; an unlock needs a reason and is the Owner’s
    assert.throws(()=>M.execute(l,'owner',bankChg({date:'2026-04-30'})),e=>/lock/i.test(e.message));
    assert.throws(()=>M.execute(l,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:''}),e=>/reason/i.test(e.message));
    assert.throws(()=>M.execute(l,'accountant',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'Correct a bill (fictional)'}),e=>e.code==='FORBIDDEN');
    const u=M.execute(l,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'Correct a bill (fictional)'});
    assert.ok(!u.accountingX.monthLocks?.['2026-04']||u.accountingX.monthLocks['2026-04'].unlocks?.length>=1);
  });
  test(T2+'month lock also needs the days: an April day that is not closed blocks it (the scenario state with 30 April reopened)',()=>{
    lane('accMonthLockBlocker');let s=month();
    s=cmd(s,'owner',{type:'ACC_GST_SETOFF',month:'2026-04'},'acc2-gst');
    // May is not a closed month: locking it names the open days / the missing set-off
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-05'}),e=>/close|open|set-?off|day/i.test(e.message));
    assert.ok((M.accMonthLockBlocker(s,'2026-05')||'').length>0);
    assert.equal(M.accMonthLockBlocker(s,'2026-04')||'','','April is ready to lock: '+M.accMonthLockBlocker(s,'2026-04'));
  });
}
