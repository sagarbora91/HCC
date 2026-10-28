// ACC-2 acc2-gst · GST month end (s.49 set-off), statutory remittances, the month lock and GST on booking advances.
// Contract 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 (switch), §6, §9; rulebook §4.6, §4.12, §5 A6 / A6a / A6b, D5, E1–E5, §G1, §8.6.
// Names prefixed [ACC2-GST]. The six hand-worked set-off cases are the same as the server's GstSetOffTests.cs.
export function register({test,M,assert}){
  const T='[ACC2-GST] ',R=r=>Math.round(r*100);
  const H=(igst,cgst,sgst)=>({igst:R(igst),cgst:R(cgst),sgst:R(sgst)});
  const ex=(s,uid,cmd)=>M.execute(s,uid,cmd);
  const entries=v=>v.entries.map(e=>[e.account,e.debit/100,e.credit/100]);

  // ───────────── the six hand-worked cases (rulebook §4.12; s.49(5), 49A, 49B, Rule 88A) ─────────────
  const CASES=[
    // 1. Rulebook §8.6 (April 2026): output 14,500 + 14,500, ITC nil, TCS 100 in the cash ledger → challan 28,900.
    {name:'rulebook §8.6',out:H(0,14500,14500),cr:H(0,0,0),cash:R(100),cashHeads:H(0,14500,14500),left:H(0,0,0),challan:R(28900),
      lines:[['2110',14500,0],['2120',14500,0],['1340',0,29000]]},
    // 2. Intra-state, credit short of output: each head against its own credit, the rest in cash.
    {name:'partial ITC',out:H(0,10000,10000),cr:H(0,4000,3000),cash:0,cashHeads:H(0,6000,7000),left:H(0,0,0),challan:R(13000),
      lines:[['2110',10000,0],['2120',10000,0],['1310',0,4000],['1320',0,3000],['1340',0,13000]]},
    // 3. Credit above output: CGST and SGST never cross — CGST 3,000 left over cannot pay SGST.
    {name:'excess credit, no CGST↔SGST',out:H(0,5000,5000),cr:H(0,8000,6000),cash:0,cashHeads:H(0,0,0),left:H(0,3000,1000),challan:0,
      lines:[['2110',5000,0],['2120',5000,0],['1310',0,5000],['1320',0,5000]]},
    // 4. IGST credit spread to where the own credit falls short (CGST 9,000, SGST 3,000), then CGST / SGST credit.
    {name:'IGST credit to CGST / SGST',out:H(0,10000,10000),cr:H(12000,1000,5000),cash:0,cashHeads:H(0,0,2000),left:H(0,0,0),challan:R(2000),
      use:{igst:H(0,9000,3000),cgst:H(0,1000,0),sgst:H(0,0,5000)},
      lines:[['2110',10000,0],['2120',10000,0],['1330',0,12000],['1310',0,1000],['1320',0,5000],['1340',0,2000]]},
    // 5. IGST output: IGST credit, then CGST credit (after its own head), then SGST credit (only what CGST could not pay).
    {name:'IGST output, CGST before SGST',out:H(9000,2000,2000),cr:H(3000,6000,5000),cash:0,cashHeads:H(0,0,0),left:H(0,0,1000),challan:0,
      use:{igst:H(3000,0,0),cgst:H(4000,2000,0),sgst:H(2000,0,2000)},
      lines:[['2130',9000,0],['2110',2000,0],['2120',2000,0],['1330',0,3000],['1310',0,6000],['1320',0,4000]]},
    // 6. s.49A: the IGST credit is exhausted first even though CGST credit exists (CGST 1,000 left); cash in 1340 above the payable → no challan.
    {name:'IGST exhausted first, surplus cash',out:H(0,3000,3000),cr:H(5000,2000,0),cash:R(500),cashHeads:H(0,0,0),left:H(0,1000,0),challan:0,
      use:{igst:H(0,2000,3000),cgst:H(0,1000,0),sgst:H(0,0,0)},
      lines:[['2110',3000,0],['2120',3000,0],['1330',0,5000],['1310',0,1000]]},
  ];
  for(const [i,c] of CASES.entries())test(`${T}set-off case ${i+1} (${c.name}): s.49 order, cash per head, credit left, challan, journal lines`,()=>{
    const x=M.accGstSetoff(c.out,c.cr,c.cash);
    assert.deepEqual(x.cash,c.cashHeads);assert.deepEqual(x.creditLeft,c.left);assert.equal(x.challan,c.challan);
    if(c.use)assert.deepEqual(x.use,c.use);
    const e=M.accGstSetoffEntries(x);assert.deepEqual(e.map(e=>[e.account,e.debit/100,e.credit/100]),c.lines);
    assert.equal(e.reduce((a,e)=>a+e.debit,0),e.reduce((a,e)=>a+e.credit,0),'balanced');
    // never CGST ↔ SGST
    assert.equal(x.use.cgst.sgst,0);assert.equal(x.use.sgst.cgst,0);
  });
  test(T+'set-off inputs: negative heads count as nil; no output and no credit = no lines, challan 0',()=>{
    const x=M.accGstSetoff(H(0,-50,0),H(0,0,-10),R(5));
    assert.deepEqual([x.payable,x.challan,M.accGstSetoffEntries(x).length],[0,0,0]);
  });

  // ───────────── the month and the set-off journal on the rulebook month (scenario ACC-RULEBOOK, April 2026) ─────────────
  let cache;const month=()=>cache??(cache=M.loadScenario('ACC-RULEBOOK'));
  /** The rulebook month plus T4 (OTA stay) and T19 (Goibibo payout, TCS ₹100) put into the 29 April snapshot, as the acc2-tests lane drives them. */
  // ACC-2 integration: the tests lane's scenario now drives T4 (OTA stay, ota-credit) and T19 (CHN_PAYOUT) itself, so nothing is injected.
  function withOta(){return structuredClone(month());}
  test(T+'accGstMonth on the driven rulebook month (the whole of §8.2 since the merge): output 14,500 + 14,500 (T1–T4), no ITC, TCS 100 (T19), challan 28,900; nothing posted yet',()=>{
    const g=M.accGstMonth(month(),'2026-04');
    assert.deepEqual([g.from,g.to],['2026-04-01','2026-04-30']);
    assert.deepEqual(g.output,H(0,14500,14500));assert.deepEqual(g.credit,H(0,0,0));assert.equal(g.tcs,R(100));assert.equal(g.setoff.challan,R(28900));
    assert.equal(g.posted,undefined);assert.equal(g.locked,false);
  });
  test(T+'rulebook §8.6 through the books: with T4 + T19 the month is 14,500 + 14,500, TCS 100 → the set-off posts in May (Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000), challan 28,900',()=>{
    let s=withOta();const g=M.accGstMonth(s,'2026-04');
    assert.deepEqual(g.output,H(0,14500,14500));assert.equal(g.tcs,R(100));assert.equal(g.cashBalance,R(100));assert.equal(g.setoff.challan,R(28900));
    assert.throws(()=>ex(s,'cashier',{type:'ACC_GST_SETOFF',month:'2026-04'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'accountant',{type:'ACC_GST_SETOFF',month:'2026-05'}),e=>e.code==='INVALID_TRANSITION'&&/not over/.test(e.message));
    s=ex(s,'accountant',{type:'ACC_GST_SETOFF',month:'2026-04'});
    assert.equal(s.accountingX.gstSetoffs['2026-04'],'gst-setoff:2026-04@2026-05-01');
    assert.match(s.audit[0].detail,/GST set-off 2026-04 · output ₹14,500 CGST \+ ₹14,500 SGST \+ ₹0 IGST · ITC used ₹0 · cash ledger ₹29,000 · challan ₹28,900 · posted 2026-05-01/);
    const v=M.accLiveVouchers(s,'2026-05-01').filter(v=>v.source.sub==='gst-setoff');
    assert.equal(v.length,1);assert.deepEqual([v[0].type,v[0].source.kind,v[0].no.startsWith('JRN/20260501/')],['Journal','tax',true]);
    assert.deepEqual(entries(v[0]),[['2110',14500,0],['2120',14500,0],['1340',0,29000]]);
    // April's own trial balance keeps 2110 / 2120 at 14,500 Cr (§8.3); after the May set-off 1340 is 28,900 Cr until the challan.
    const bal=(to,code)=>M.accVouchersUpTo(s,to).flatMap(v=>v.entries).filter(e=>e.account===code).reduce((a,e)=>a+e.debit-e.credit,0);
    assert.deepEqual([bal('2026-04-30','2110'),bal('2026-05-01','2110'),bal('2026-05-01','1340')],[-R(14500),0,-R(28900)]);
    // Same day again: idempotent.
    const again=ex(s,'owner',{type:'ACC_GST_SETOFF',month:'2026-04'});
    assert.match(again.audit[0].detail,/unchanged/);assert.deepEqual(again.accountingX.gstSetoffs,s.accountingX.gstSetoffs);
    // The next month: the April set-off's cash use is carried (challan not booked yet → 1340 carries −28,900).
    const may=M.accGstMonth(s,'2026-05');assert.equal(may.cashBalance,-R(28900));assert.deepEqual(may.output,H(0,0,0));
  });
  test(T+'ACC_GST_SETOFF replaces an unlocked set-off on a later business date; refused when the earlier set-off\'s day is locked',()=>{
    const s=ex(withOta(),'owner',{type:'ACC_GST_SETOFF',month:'2026-04'});
    const later=structuredClone(s);later.date='2026-05-02';
    const r=ex(later,'accountant',{type:'ACC_GST_SETOFF',month:'2026-04'});
    assert.match(r.audit[0].detail,/replaces the set-off of 2026-05-01/);
    assert.equal(M.accLiveVouchers(r,'2026-05-01').filter(v=>v.source.sub==='gst-setoff').length,0);
    assert.equal(M.accLiveVouchers(r,'2026-05-02').filter(v=>v.source.sub==='gst-setoff').length,1);
    const locked=structuredClone(later);(locked.accountingX.locks??={})['2026-05-01']={date:'2026-05-01',lockedAt:'x',lockedBy:'owner',vouchers:[],debit:0,credit:0,count:0};
    assert.throws(()=>ex(locked,'accountant',{type:'ACC_GST_SETOFF',month:'2026-04'}),e=>e.code==='INVALID_TRANSITION'&&/posted on 2026-05-01, which is locked/.test(e.message));
  });
  test(T+'ACC_GST_PAY: challan through the gst-payment template (Dr 1340 · Cr the bank party; RCM Dr 2140 · Cr 1340) — Owner approved, Accountant pending (manual-voucher stub)',()=>{
    let s=ex(withOta(),'owner',{type:'ACC_GST_SETOFF',month:'2026-04'});
    const pay={type:'ACC_GST_PAY',month:'2026-04',amount:R(28900),method:'Bank transfer',challan:'CIN-DEMO-0520',date:'2026-05-01'};
    assert.throws(()=>ex(s,'manager',pay),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'owner',{...pay,challan:''}),/challan/);
    assert.throws(()=>ex(s,'owner',{...pay,date:'2026-04-30'}),/dated after the month/);
    assert.throws(()=>ex(s,'owner',{...pay,amount:0}),/amount/);
    const before=s; // ACC-2 integration: a challan beyond what the month still owes is refused (rulebook §8.6 test), so the Accountant's RCM case pays before the Owner's
    s=ex(s,'owner',pay);
    const mv=s.accountingX.manual.at(-1);
    assert.deepEqual([mv.template,mv.type,mv.status,mv.date,mv.reference],['gst-payment','Payment','approved','2026-05-01','CIN-DEMO-0520']);
    assert.deepEqual(mv.lines.map(l=>[l.account.split('/')[0],l.debit/100,l.credit/100]),[['1340',28900,0],['1030',0,28900]]);
    assert.ok(/^1030\//.test(mv.lines[1].account),'bank party: '+mv.lines[1].account);
    const acc=ex(before,'accountant',{...pay,amount:R(1000),rcm:R(400),challan:'CIN-DEMO-RCM'});
    assert.throws(()=>ex(s,'owner',{...pay,amount:R(1000),challan:'CIN-DEMO-OVER'}),/cash ledger \(1340\)/,'over the month’s challan');
    const m2=acc.accountingX.manual.at(-1);
    assert.equal(m2.status,'pending');assert.deepEqual(m2.lines.slice(2).map(l=>[l.account,l.debit/100,l.credit/100]),[['2140',400,0],['1340',0,400]]);
    assert.throws(()=>ex(s,'owner',{...pay,rcm:R(30000)}),/reverse-charge/);
  });

  // ───────────── the month lock (G1; contract §6; DEC-131) ─────────────
  test(T+'month lock: the blockers — month not over, set-off missing, a pending manual voucher, a day not closed',()=>{
    const base=M.seed();
    assert.match(M.accMonthLockBlocker(base,'2026-10'),/not over/);
    assert.match(M.accMonthLockBlocker(base,'2026-9'),/Choose a month/);
    assert.match(M.accMonthLockBlocker(base,'2026-09'),/Post the GST set-off for 2026-09/);
    let s=ex(base,'accountant',{type:'ACC_GST_SETOFF',month:'2026-09'});
    assert.equal(M.accLiveVouchers(s,s.date).filter(v=>v.source.sub==='gst-setoff').length,0,'a nil month posts no journal, but the set-off is recorded');
    assert.match(M.accMonthLockBlocker(s,'2026-09'),/^2026-09-01 is not closed/);
    const p=structuredClone(s);p.accountingX.manual=[{id:'MV-1',date:'2026-09-15',type:'Journal',narration:'x',lines:[],status:'pending',by:'accountant',at:'x'}];
    assert.match(M.accMonthLockBlocker(p,'2026-09'),/1 manual voucher is pending approval in 2026-09 \(MV-1\)/);
  });
  test(T+'month lock on the rulebook month: Accountant requests, Owner locks every April day + the expense month; locked month refuses the set-off; Owner unlocks with a reason',()=>{
    let s=ex(month(),'accountant',{type:'ACC_GST_SETOFF',month:'2026-04'});
    assert.equal(M.accMonthLockBlocker(s,'2026-04'),'');
    assert.throws(()=>ex(s,'accountant',{type:'ACC_MONTH_LOCK',month:'2026-04'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'manager',{type:'ACC_MONTH_LOCK_REQUEST',month:'2026-04'}),e=>e.code==='FORBIDDEN');
    s=ex(s,'accountant',{type:'ACC_MONTH_LOCK_REQUEST',month:'2026-04'});
    assert.deepEqual(M.accMonthLockRequests(s).map(r=>r.month),['2026-04']);
    const unlockedDays=M.accMonthDaysToLock(s,'2026-04').length;assert.ok(unlockedDays>0);
    s=ex(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'});
    assert.match(s.audit[0].detail,new RegExp(`Month 2026-04 locked · ${unlockedDays} day\\(s\\) locked now · set-off gst-setoff:2026-04@2026-05-01 · expense month locked`));
    assert.ok(M.accMonthDates('2026-04').every(d=>M.accLockOf(s,d)),'every April day locked');
    assert.equal(s.accountingX.monthLocks['2026-04'].setoff,'gst-setoff:2026-04@2026-05-01');
    assert.ok(M.accMonthLocked(s,'2026-04-17'));assert.equal(M.accMonthLocked(s,'2026-05-01'),false);
    assert.ok(M.expMonthLocked(s,'2026-04-10'),'SCC expense month locked too');
    assert.deepEqual(M.accMonthLockRequests(s),[]);
    assert.equal(M.accGstMonth(s,'2026-04').locked,true);
    assert.throws(()=>ex(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'}),/already locked/);
    assert.throws(()=>ex(s,'accountant',{type:'ACC_GST_SETOFF',month:'2026-04'}),e=>e.code==='INVALID_TRANSITION'&&/locked/.test(e.message));
    // unlock: Owner + reason; the expense month unlocks (DEC-131); day locks stay
    assert.throws(()=>ex(s,'accountant',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'Late bill'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'x'}),/reason/);
    s=ex(s,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'Late vendor bill'});
    assert.equal(M.accMonthLocked(s,'2026-04-17'),false);assert.equal(M.expMonthLocked(s,'2026-04-10'),false);
    assert.ok(M.accLockOf(s,'2026-04-17'),'day locks stay');
    assert.deepEqual(s.accountingX.monthLocks['2026-04'].unlocks.map(u=>u.reason),['Late vendor bill']);
    assert.throws(()=>ex(s,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'again'}),/not locked/);
    // lock again: the unlock history is kept
    s=ex(s,'owner',{type:'ACC_MONTH_LOCK',month:'2026-04'});
    assert.ok(M.accMonthLocked(s,'2026-04-17'));assert.equal(s.accountingX.monthLocks['2026-04'].unlocks.length,1);
    assert.match(s.audit[0].detail,/0 day\(s\) locked now/);
  });

  // ───────────── statutory remittance (D5) and the compliance card hook ─────────────
  test(T+'ACC_STAT_REMIT: template statutory-remittance Dr 2310 / 2320 / 2330 · Cr tender; the month\'s EPF card is marked filed with the challan as evidence',()=>{
    let s=M.seed();const card=M.cmpLegacyCard('EPF','2026-09');assert.deepEqual(card,{fy:2026,itemId:'oc9'});
    const remit={type:'ACC_STAT_REMIT',kind:'pf',month:'2026-09',amount:R(36000),method:'Cash',challan:'EPF-TRRN-0915',date:'2026-10-10'};
    assert.throws(()=>ex(s,'reception',remit),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'owner',{...remit,kind:'tds'}),/PF, ESIC or professional tax/);
    // The demo calendar already has the September PF card filed: the remittance leaves it as it is.
    const kept=ex(s,'accountant',remit);assert.match(kept.audit[0].detail,/compliance card oc9 was already filed \(left as it is\)/);
    // Reopened by the Owner, the remittance files it.
    s=ex(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc9',reason:'Re-file from the books'});
    s=ex(s,'accountant',remit);
    const mv=s.accountingX.manual.at(-1);
    assert.deepEqual([mv.template,mv.status,mv.lines[0].account,mv.lines[0].debit],['statutory-remittance','pending','2310',R(36000)]);
    assert.ok(/^1000/.test(mv.lines[1].account));
    const rec=M.cmpRecord(M.cmpSlice(s),2026,'oc9');
    assert.deepEqual([rec.done,rec.filedOn,rec.evidenceRef,rec.by],[true,'2026-10-10','EPF-TRRN-0915','accountant']);
    assert.match(rec.notes,/PF ₹36,000 remitted · Cash · manual voucher MV-/);
    assert.match(s.audit[0].detail,/compliance card oc9 marked filed \(evidence EPF-TRRN-0915\)/);
    for(const [k,code] of [['esic','2320'],['pt','2330']]){const x=ex(M.seed(),'owner',{...remit,kind:k});assert.equal(x.accountingX.manual.at(-1).lines[0].account,code);assert.equal(x.accountingX.manual.at(-1).status,'approved');}
  });

  // ───────────── GST on booking advances (A6 / A6a / A6b; DEC-142 default OFF) ─────────────
  const inHouse=s=>s.bookings.find(b=>b.status==='In house');
  test(T+'switch OFF (default, DEC-142): a deposit posts Dr tender · Cr 2000 only — no advance-GST voucher',()=>{
    const s0=M.seed();assert.equal(M.accGstOnAdvances(s0),false);
    const b=inHouse(s0),s=ex(s0,'cashier',{type:'PAYMENT',id:b.id,amount:R(10500),method:'Cash',kind:'deposit'});
    assert.equal(M.accLiveVouchers(s,s.date).filter(v=>v.source.kind==='tax').length,0);
    assert.ok(M.accLiveVouchers(s,s.date).some(v=>v.source.sub==='deposit'));
  });
  test(T+'ACC_GST_ADVANCES_SET: Owner only, needs the firm profile, refuses a no-op',()=>{
    const s=M.seed();
    assert.throws(()=>ex(s,'accountant',{type:'ACC_GST_ADVANCES_SET',on:true}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'manager',{type:'ACC_GST_ADVANCES_SET',on:true}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>ex(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:false}),/already off/);
    const on=ex(s,'owner',{type:'ACC_GST_ADVANCES_SET',on:true});
    assert.equal(M.accGstOnAdvances(on),true);assert.equal(on.complianceX.profile.name,s.complianceX.profile.name);
    assert.match(on.audit[0].detail,/switched ON/);
    const none=structuredClone(s);delete none.complianceX.profile;
    assert.throws(()=>ex(none,'owner',{type:'ACC_GST_ADVANCES_SET',on:true}),/firm profile/);
  });
  test(T+'switch ON: a deposit posts {kind:tax, sub:advance-gst} Dr 1350 · Cr 2110 · Cr 2120, cum-tax at the booked room\'s rate (5 % ≤ ₹7,500); a partial return reverses its share',()=>{
    let s=ex(M.seed(),'owner',{type:'ACC_GST_ADVANCES_SET',on:true});const b=inHouse(s);
    assert.equal(M.accAdvanceGstBps(s,b),500);
    s=ex(s,'cashier',{type:'PAYMENT',id:b.id,amount:R(10500),method:'Cash',kind:'deposit'});
    const r=s.bookings.find(x=>x.id===b.id).receipts.at(-1);
    const v=M.accLiveVouchers(s,s.date).filter(v=>v.source.kind==='tax');
    assert.equal(v.length,1);assert.deepEqual([v[0].type,v[0].source.sub,v[0].source.id,v[0].source.booking],['Journal','advance-gst',`advance-gst:${b.id}/${r.id}`,b.id]);
    assert.deepEqual(entries(v[0]),[['1350',500,0],['2110',0,250],['2120',0,250]]);
    assert.ok(M.accBalanced(v[0]));
    s=ex(s,'cashier',{type:'PAYMENT',id:b.id,amount:R(5250),method:'Cash',kind:'deposit-return'});
    const rev=M.accLiveVouchers(s,s.date).filter(v=>v.source.sub==='advance-gst-reversal');
    assert.equal(rev.length,1);assert.deepEqual(entries(rev[0]),[['2110',125,0],['2120',125,0],['1350',0,250]]);
    assert.equal(M.accExceptions(s,s.date).filter(x=>x.severity==='error'&&x.kind!=='stuck').length,0,'no unbalanced / unmapped error');
  });
  test(T+'switch ON: 18 % for a room above ₹7,500; deposit applied at check-out (receipt kind deposit-apply) reverses, capped by the GST held — an advance without GST reverses nothing',()=>{
    let s=ex(M.seed(),'owner',{type:'ACC_GST_ADVANCES_SET',on:true});const id=inHouse(s).id;
    const hi=structuredClone(s);hi.bookings.find(x=>x.id===id).rate=R(9000);
    assert.equal(M.accAdvanceGstBps(hi,hi.bookings.find(x=>x.id===id)),1800);
    const d=ex(hi,'cashier',{type:'PAYMENT',id,amount:R(11800),method:'Cash',kind:'deposit'});
    assert.deepEqual(entries(M.accLiveVouchers(d,d.date).find(v=>v.source.sub==='advance-gst')),[['1350',1800,0],['2110',0,900],['2120',0,900]]);
    // deposit-apply (acc2-flows' receipt kind) of more than the deposit: the reversal stops at the 1,800 held
    const a=structuredClone(d);a.bookings.find(x=>x.id===id).receipts.push({id:'RCPT-APPLY',amount:R(20000),method:'Advance',date:a.date,kind:'deposit-apply',reference:'apply',actor:'cashier'});
    assert.deepEqual(entries(M.accAdvanceGstVouchers(a,a.date).find(v=>v.source.sub==='advance-gst-reversal')),[['2110',900,0],['2120',900,0],['1350',0,1800]]);
    // an apply on a booking that never took a deposit (e.g. the opening advance, rulebook T5a) posts no reversal
    const o=structuredClone(s);o.bookings.find(x=>x.id===id).receipts.push({id:'RCPT-APPLY',amount:R(10000),method:'Advance',date:o.date,kind:'deposit-apply',reference:'apply',actor:'cashier'});
    assert.equal(M.accAdvanceGstVouchers(o,o.date).length,0);
  });
  test(T+'accCumTaxGst: cum-tax split half away from zero, signed; 0 bps = nil',()=>{
    assert.deepEqual(M.accCumTaxGst(R(10500),500),{tax:R(500),cgst:R(250),sgst:R(250)});
    assert.deepEqual(M.accCumTaxGst(R(31500),500),{tax:R(1500),cgst:R(750),sgst:R(750)});
    assert.deepEqual(M.accCumTaxGst(101,500),{tax:5,cgst:3,sgst:2});
    assert.deepEqual(M.accCumTaxGst(-101,500),{tax:-5,cgst:-3,sgst:-2});
    assert.deepEqual(M.accCumTaxGst(R(1000),0),{tax:0,cgst:0,sgst:0});
  });
  test(T+'with the switch ON the advance GST flows into the month: output CGST / SGST include it and 1350 carries the GST held',()=>{
    let s=ex(M.seed(),'owner',{type:'ACC_GST_ADVANCES_SET',on:true});const b=inHouse(s);
    s=ex(s,'cashier',{type:'PAYMENT',id:b.id,amount:R(10500),method:'Cash',kind:'deposit'});
    const off=M.accGstMonth(ex(M.seed(),'cashier',{type:'PAYMENT',id:b.id,amount:R(10500),method:'Cash',kind:'deposit'}),'2026-10'),on=M.accGstMonth(s,'2026-10');
    assert.equal(on.output.cgst-off.output.cgst,R(250));assert.equal(on.output.sgst-off.output.sgst,R(250));assert.equal(on.advanceGst,R(500));assert.equal(off.advanceGst,0);
  });
}
