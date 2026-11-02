// ACC-4 · F5-A1 · year lock, carry-forward, reopen (02 Planning/agents/ACC4-CONTRACT.md §1, §6). Names prefixed [ACC4-YEAR].
// Golden: the rulebook April 2026 month (scenario ACC-RULEBOOK) taken as the whole FY → carry-forward = the §8.5 balance sheet.
export function register({test,M,assert}){
  const T='[ACC4-YEAR] ',R=r=>Math.round(r*100);
  const run=(s,uid,cmd)=>M.execute(s,uid,cmd);
  let base;const month=()=>base??=M.loadScenario('ACC-RULEBOOK');
  const ctl=a=>String(a).split('/')[0];
  const net=lines=>{const m={};for(const l of lines)m[l.ledger]=(m[l.ledger]??0)+l.dr-l.cr;return m;};
  /** The month as a closed year: business date moved past 31 March 2027, every activity day locked (real snapshots), April / May GST-locked. */
  const closedYear=()=>{
    const s=structuredClone(month());
    const dates=M.accDates(s).filter(d=>d<='2027-03-31');
    const locks={};for(const d of dates){const vs=M.accLiveVouchers(s,d),t=M.accTotals(vs);locks[d]={date:d,lockedAt:s.clock,lockedBy:'owner',vouchers:vs,debit:t.debit,credit:t.credit,count:vs.length};}
    const months={};for(const m of new Set(dates.map(d=>d.slice(0,7))))months[m]={lockedAt:s.clock,lockedBy:'owner'};
    s.accountingX={...s.accountingX,locks,monthLocks:months};
    s.date='2027-04-02';s.closedDates=[...new Set([...s.closedDates,...dates])];
    return s;
  };

  test(T+'golden: April 2026 as the FY → carry-forward = §8.5 balance sheet; profit 1,40,400 and drawings 50,000 to 3100; P&L ledgers and 3000 absent',()=>{
    const d=M.accYearCarryLines(month(),'2026-04-01','2026-05-01');
    assert.ok(d.balanced,'lines balance');assert.equal(d.debit,R(7927900));assert.equal(d.credit,R(7927900));
    assert.equal(d.profitToCapital,R(140400));assert.equal(d.drawingsToCapital,R(50000));
    const n=net(d.lines);
    const expect={'1000':R(40300),'1030':R(501500),'1360':R(6000),'1340':R(100),'1500':R(6000000),'1510':R(800000),'1520':R(500000),'1530':R(80000),
      '2200':-R(66000),'2000':-R(31500),'2110':-R(14500),'2120':-R(14500),'2310':-R(36000),'2320':-R(8000),'2330':-R(2000),'2400':-R(1960000),'3100':-R(5795400)};
    for(const [c,v] of Object.entries(expect))assert.equal(n[c]??0,v,'ledger '+c);
    assert.deepEqual(Object.keys(n).filter(c=>!(c in expect)),[],'nothing else carried');
    assert.ok(!d.lines.some(l=>/^[45]/.test(l.ledger)||l.ledger==='3000'),'no income / expense / drawings line');
    assert.equal(d.lines.filter(l=>l.ledger==='2200').length,2,'party sub-ledgers kept (Morning, Kaveri)');
    const close=d.close.reduce((a,e)=>a+e.debit-e.credit,0);assert.equal(close,0,'year-close entries balance');
  });
  test(T+'preview lists the failed checks on the open month; ACC_YEAR_LOCK refused ACC_YEAR_NOT_READY; Accountant cannot lock',()=>{
    let s=month();const p=M.accYearPreview(s,'2026-27');assert.equal(p.ready,false);
    assert.ok(p.checks.find(c=>c.id==='days-locked'&&!c.ok));assert.ok(p.checks.find(c=>c.id==='months-gst-locked'&&!c.ok));
    assert.ok(p.checks.find(c=>c.id==='clearing'&&c.ok),'1010 / 1020 settled in April');
    s=run(s,'accountant',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});const cf=s.accYearX.carryForwards[0];assert.equal(cf.status,'draft');
    assert.throws(()=>run(s,'accountant',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id}),/Owner/);
    assert.throws(()=>run(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id}),/ACC_YEAR_NOT_READY/);
    assert.throws(()=>run(s,'reception',{type:'ACC_YEAR_PREVIEW',fy:'2026-27'}),/Access denied/);
    assert.throws(()=>run(s,'owner',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2025-26'}),/2026-27/,'§1.3 no earlier year');
  });
  test(T+'lock: carry-forward approved as the FY27 opening; FY27 trial balance opens on the lines; P&L of FY27 empty; posts into FY26 refused',()=>{
    let s=closedYear();assert.ok(M.accYearPreview(s,'2026-27').ready,JSON.stringify(M.accYearPreview(s,'2026-27').checks.filter(c=>!c.ok)));
    s=run(s,'accountant',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});const cf=s.accYearX.carryForwards.at(-1);
    s=run(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id});
    assert.equal(M.accYearOf(s,'2026-27').status,'locked');assert.equal(M.accYearOpening(s,'2027-28').id,cf.id);
    const tb=M.accTrialBalance6(s,'2027-04-01','2027-04-02',true);
    const open={};for(const r of tb.rows){const n=r.openingDebit-r.openingCredit;if(n)open[ctl(r.account)]=(open[ctl(r.account)]??0)+n;}
    assert.deepEqual(open,Object.fromEntries(Object.entries(net(cf.lines)).filter(([,v])=>v)),'FY27 opening = carry-forward');
    const pl=M.accPl(s,'2027-04-01','2027-04-02');assert.equal(pl.profitBeforeTax,0);
    assert.equal(M.accPl(s,'2026-04-01','2026-05-01').profitBeforeTax,R(140400),'FY26 P&L unchanged by the close');
    assert.throws(()=>run(s,'owner',{type:'ACC_MANUAL_SAVE',date:'2026-04-20',voucherType:'Journal',narration:'late fix',lines:[{account:'5040',debit:100,credit:0},{account:'1030/BA-HDFC',debit:0,credit:100}]}),/ACC_YEAR_LOCKED/);
    assert.throws(()=>run(s,'owner',{type:'ACC_UNLOCK',date:'2026-04-20',reason:'fix it'}),/ACC_YEAR_LOCKED/);
    assert.throws(()=>run(s,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'fix it'}),/ACC_YEAR_LOCKED/);
    s=run(s,'owner',{type:'ACC_MANUAL_SAVE',date:'2027-04-02',voucherType:'Journal',narration:'Correction of FY 2026-27 sundry',refersToFy:'2026-27',
      lines:[{account:'5040',debit:100,credit:0},{account:'1030/BA-HDFC',debit:0,credit:100}]});
    assert.equal(s.accountingX.manual.at(-1).refersToFy,'2026-27');assert.equal(s.accountingX.manual.at(-1).date,'2027-04-02');
  });
  test(T+'reopen: reason ≥ 10, latest year only, exports provisional; relock supersedes and regenerates; blocked once FY27 has a locked month',()=>{
    let s=closedYear();s=run(s,'accountant',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});const cf=s.accYearX.carryForwards.at(-1);
    s=run(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id});
    assert.throws(()=>run(s,'owner',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'short'}),/10 characters/);
    assert.throws(()=>run(s,'accountant',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'CA found a missed bill'}),/Owner/);
    const blocked=structuredClone(s);blocked.accountingX.monthLocks={...blocked.accountingX.monthLocks,'2027-04':{lockedAt:s.clock,lockedBy:'owner'}};
    assert.throws(()=>run(blocked,'owner',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'CA found a missed bill'}),/ACC_YEAR_REOPEN_BLOCKED/);
    s=run(s,'owner',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'CA found a missed bill'});
    assert.equal(M.accYearOf(s,'2026-27').status,'reopened');assert.ok(M.accYearProvisional(s,'2026-04-10'));assert.ok(M.accYearProvisional(s,'2027-04-02'));
    // a correction inside the reopened year is accepted (its day is unlocked first)
    s=run(s,'owner',{type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'CA correction'});const ml=s.accountingX.monthLocks;s.accountingX={...s.accountingX,monthLocks:{...ml}};delete s.accountingX.monthLocks['2026-04'];
    s=run(s,'owner',{type:'ACC_UNLOCK',date:'2026-04-20',reason:'CA correction'});
    s=run(s,'owner',{type:'ACC_MANUAL_SAVE',date:'2026-04-20',voucherType:'Journal',narration:'Missed sundry bill',lines:[{account:'5040',debit:R(1000),credit:0},{account:'1030/BA-HDFC',debit:0,credit:R(1000)}]});
    const vs=M.accLiveVouchers(s,'2026-04-20'),t=M.accTotals(vs);s.accountingX.locks['2026-04-20']={date:'2026-04-20',lockedAt:s.clock,lockedBy:'owner',vouchers:vs,debit:t.debit,credit:t.credit,count:vs.length};s.accountingX.monthLocks['2026-04']={lockedAt:s.clock,lockedBy:'owner'};
    s=run(s,'owner',{type:'ACC_YEAR_RELOCK',fy:'2026-27'});
    const old=M.accCarryForward(s,cf.id),now=M.accYearOpening(s,'2027-28');
    assert.equal(old.status,'superseded');assert.equal(old.supersededBy,now.id);assert.equal(now.profitToCapital,R(139400));
    assert.equal(M.accYearProvisional(s,'2026-04-10'),false);assert.ok(s.accountingX.reexport['2027-04-01'],'FY27 opening marked changed (DEC-077)');
    assert.ok(M.accBalanceSheet(s,'2027-04-02').balanced);
  });
  test(T+'FY helpers: bounds, next, months',()=>{
    assert.deepEqual(M.accFyBounds('2026-27'),{from:'2026-04-01',to:'2027-03-31'});assert.equal(M.accFyNext('2026-27'),'2027-28');
    assert.equal(M.accFyOfDate('2027-03-31'),'2026-27');assert.equal(M.accFyOfDate('2027-04-01'),'2027-28');
    assert.equal(M.accFyMonths('2026-27').length,12);assert.equal(M.accFyMonths('2026-27')[11],'2027-03');assert.ok(!M.accFyValid('2026-28'));
  });
}
