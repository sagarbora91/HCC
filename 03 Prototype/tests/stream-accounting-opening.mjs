// ACC-1 · Opening balances entered by hand (contract §5, §7; rulebook §G2 and the §8.1 worked-month opening; DEC-139 / DEC-148).
// Names prefixed [ACC1-OPENING].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const D='2026-10-24',FY='2026-04-01',R=r=>r*100; // rupees → paise
  // Rulebook §8.1, 1 April 2026: ₹77,40,000 both sides. Party ledgers are plain `<control>/<party>` ids here.
  const RULEBOOK=[
    {account:'1000',debit:R(20000),credit:0},
    {account:'1030/BA-HDFC',debit:R(300000),credit:0},
    {account:'1500',debit:R(6000000),credit:0},
    {account:'1510',debit:R(800000),credit:0},
    {account:'1520',debit:R(500000),credit:0},
    {account:'1530',debit:R(80000),credit:0},
    {account:'1110/CMP-DECCAN',debit:R(40000),credit:0},
    {account:'2200/EV-MORNING',debit:0,credit:R(25000)},
    {account:'2000',debit:0,credit:R(10000)},
    {account:'2400/LN-HDFC',debit:0,credit:R(2000000)},
    {account:'3100',debit:0,credit:R(5705000)},
  ];
  const WITHOUT_CAPITAL=RULEBOOK.filter(l=>l.account!=='3100');
  const save=(s,uid,lines=RULEBOOK,extra={})=>M.execute(s,uid,{type:'ACC_OPENING_SAVE',asOf:FY,lines,...extra});
  const approve=(s,uid='owner')=>M.execute(s,uid,{type:'ACC_OPENING_APPROVE'});
  // Close 24 Oct on the base fixture (as stream-accounting.mjs): check in arrivals, approve the expense, post nights, count the cash.
  function closeDay(s=M.seed()){
    for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=M.execute(s,'owner',{type:'REGISTER',id:b.id});s=M.execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=M.execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    s=M.execute(s,'owner',{type:'NIGHT_POST'});s=M.execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});
    s=tillClear(M,s);return M.execute(s,'owner',{type:'DAY_CLOSE'});
  }
  const tb=vs=>Object.fromEntries(M.accTrialBalance(vs).map(r=>[r.account,r]));

  test('[ACC1-OPENING] no entry: nothing posted, pending, FY start of the business date, lock not gated (ledgers run from zero, DEC-148)',()=>{
    const s=M.seed();
    assert.equal(M.accOpening(s),undefined);assert.equal(M.accOpeningVoucher(s),undefined);assert.deepEqual(M.accOpeningPosted(s),[]);
    assert.equal(M.accOpeningPending(s),true);assert.equal(M.accOpeningLockBlocker(s,D),'');
    assert.equal(M.accFyStart(D),FY);assert.equal(M.accFyStart('2026-03-31'),'2025-04-01');assert.equal(M.accFyStart('2026-04-01'),FY);
    assert.match(M.accOpeningProblems(s)[0].message,/Opening balances pending/);
  });
  test('[ACC1-OPENING] rulebook §8.1 opening: ₹77,40,000 both sides, no 1400 line, journal JRN/20260401/000 dated 1 Apr 2026, draft not posted',()=>{
    const s=save(M.seed(),'manager'),o=M.accOpening(s);
    assert.deepEqual([o.asOf,o.status,o.by,o.lines.length],[FY,'draft','manager',11]);
    assert.deepEqual(M.accOpeningTotals(o),{debit:R(7740000),credit:R(7740000)});
    assert.ok(!o.lines.some(l=>l.account===M.ACC_OPENING_DIFF));assert.equal(M.accOpeningDifference(s),0);
    const v=M.accOpeningVoucher(s);
    assert.deepEqual([v.no,v.date,v.type,v.source.kind,v.source.id,v.source.sub],['JRN/20260401/000',FY,'Journal','opening','opening','opening-entry']);
    assert.ok(M.accBalanced(v));assert.match(v.narration,/draft/);
    // A draft never posts: not in the cumulative vouchers; still pending.
    assert.deepEqual(M.accOpeningPosted(s),[]);assert.ok(!M.accVouchersUpTo(s,D).some(x=>x.source.sub==='opening-entry'));
    assert.equal(M.accOpeningPending(s),true);
    assert.deepEqual(s.audit[0].action,'ACC OPENING SAVE');assert.match(s.audit[0].detail,/as of 2026-04-01 saved \(draft\) · 11 line\(s\)/);
  });
  test('[ACC1-OPENING] an unbalanced draft parks the gap in 1400 (added and shown); a 1400 line sent back is recomputed, never doubled',()=>{
    let s=save(M.seed(),'accountant',WITHOUT_CAPITAL);
    const o=M.accOpening(s),d=o.lines.find(l=>l.account==='1400');
    assert.deepEqual(d,{account:'1400',debit:0,credit:R(5705000)});
    assert.deepEqual(M.accOpeningTotals(o),{debit:R(7740000),credit:R(7740000)});assert.equal(M.accOpeningDifference(s),-R(5705000));
    assert.match(s.audit[0].detail,/1400 difference ₹57,05,000 Cr/);
    assert.ok(M.accOpeningProblems(s).some(p=>/₹57,05,000 Cr sits in 1400 Opening-balance difference/.test(p.message)));
    // The screen sends the stored lines back (with its 1400 line) plus a correction: 1400 is recomputed from the lines as entered.
    s=save(s,'accountant',[...o.lines,{account:'3100',debit:0,credit:R(5000000)}]);
    assert.deepEqual(M.accOpening(s).lines.filter(l=>l.account==='1400'),[{account:'1400',debit:0,credit:R(705000)}]);
    s=save(s,'accountant',[...M.accOpening(s).lines.filter(l=>l.account!=='3100'),{account:'3100',debit:0,credit:R(5705000)}]);
    assert.ok(!M.accOpening(s).lines.some(l=>l.account==='1400'));assert.equal(M.accOpeningDifference(s),0);
    // Over-stated credits park on the debit side.
    s=save(s,'accountant',[...RULEBOOK.filter(l=>l.account!=='3100'),{account:'3100',debit:0,credit:R(5805000)}]);
    assert.deepEqual(M.accOpening(s).lines.at(-1),{account:'1400',debit:R(100000),credit:0});
  });
  test('[ACC1-OPENING] Owner approval posts the journal: cumulative trial balance carries the opening, 1400 = 0 → no longer pending',()=>{
    let s=save(M.seed(),'manager');
    assert.throws(()=>approve(s,'manager'),e=>e.code==='FORBIDDEN');assert.throws(()=>approve(s,'accountant'),e=>e.code==='FORBIDDEN');
    s=approve(s);const o=M.accOpening(s);
    assert.deepEqual([o.status,o.approvedBy],['approved','owner']);assert.match(o.approvedAt,/^2026-10-24T\d\d:\d\d$/);
    assert.equal(s.audit[0].action,'ACC OPENING APPROVE');
    assert.equal(M.accOpeningPending(s),false);assert.equal(M.accOpeningVoucher(s).narration,'Opening balances as of 2026-04-01');
    const vs=M.accVouchersUpTo(s,D),op=vs.filter(v=>v.source.sub==='opening-entry');
    assert.equal(op.length,1);assert.equal(vs[0].no,'JRN/20260401/000');
    const t=tb(op);assert.equal(t['1500'].debit,R(6000000));assert.equal(t['3100'].credit,R(5705000));assert.equal(t['2400/LN-HDFC'].credit,R(2000000));
    const all=M.accTrialBalance(vs);assert.equal(all.reduce((a,r)=>a+r.balance,0),0);
    // Before asOf the opening is not counted.
    assert.deepEqual(M.accVouchersUpTo(s,'2026-03-31').filter(v=>v.source.sub==='opening-entry'),[]);
    assert.throws(()=>approve(s),e=>e.code==='INVALID_TRANSITION'&&/already approved/.test(e.message));
    assert.throws(()=>save(s,'owner'),e=>e.code==='INVALID_TRANSITION'&&/reopens them first/.test(e.message));
  });
  test('[ACC1-OPENING] approval with 1400 ≠ 0 is allowed (it balances after the 1400 line) but the entry stays pending',()=>{
    const s=approve(save(M.seed(),'manager',WITHOUT_CAPITAL));
    assert.equal(M.accOpening(s).status,'approved');assert.equal(M.accOpeningPending(s),true);
    assert.match(M.accOpeningLockBlocker(s,D),/Opening balances pending: ₹57,05,000 Cr sits in 1400/);
    assert.equal(tb(M.accOpeningPosted(s))['1400'].credit,R(5705000));
  });
  test('[ACC1-OPENING] period lock refused while the opening is a draft ("opening balances pending"), allowed after approval',()=>{
    let s=closeDay();s=save(s,'manager');
    assert.match(M.accLockBlocker(s,D),/^Opening balances pending: the opening entry as of 2026-04-01 is a draft/);
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:D}),e=>e.code==='INVALID_TRANSITION'&&/Opening balances pending/i.test(e.message));
    // A date before asOf is not gated.
    assert.equal(M.accOpeningLockBlocker(s,'2026-03-31'),'');
    s=approve(s);assert.equal(M.accLockBlocker(s,D),'');
    s=M.execute(s,'accountant',{type:'ACC_LOCK',date:D});assert.ok(M.accLockOf(s,D));
  });
  test('[ACC1-OPENING] period lock refused while an approved opening carries a 1400 difference',()=>{
    let s=closeDay();s=approve(save(s,'accountant',WITHOUT_CAPITAL));
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:D}),e=>e.code==='INVALID_TRANSITION'&&/1400 Opening-balance difference/.test(e.message));
    s=M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Owner capital missed'});s=approve(save(s,'accountant'));
    s=M.execute(s,'manager',{type:'ACC_LOCK',date:D});assert.ok(M.accLockOf(s,D));
  });
  test('[ACC1-OPENING] reopen: Owner only, with a reason, back to draft; refused once a period on or after asOf is locked',()=>{
    let s=closeDay();s=approve(save(s,'manager'));
    assert.throws(()=>M.execute(s,'manager',{type:'ACC_OPENING_REOPEN',reason:'Typo'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_OPENING_REOPEN',reason:'Typo'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:' '}),/reason is required/);
    const r=M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Bank balance per statement'});
    const o=M.accOpening(r);assert.deepEqual([o.status,o.approvedBy,o.approvedAt],['draft',undefined,undefined]);
    assert.equal(r.audit[0].action,'ACC OPENING REOPEN');assert.match(r.audit[0].detail,/reopened · Bank balance per statement/);
    assert.throws(()=>M.execute(r,'owner',{type:'ACC_OPENING_REOPEN',reason:'Again'}),e=>e.code==='INVALID_TRANSITION');
    assert.deepEqual(M.accOpeningPosted(r),[]);assert.equal(M.accOpeningPending(r),true);
    // After the first lock on or after asOf the approved entry is final (an Owner-approved journal corrects it, rulebook G2).
    s=M.execute(s,'accountant',{type:'ACC_LOCK',date:D});
    assert.deepEqual(M.accOpeningLocksFrom(s,FY),[D]);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Late correction'}),e=>e.code==='INVALID_TRANSITION'&&/locked on or after 2026-04-01 \(2026-10-24\)/.test(e.message));
    // Unlocking the period makes the reopen possible again.
    s=M.execute(s,'owner',{type:'ACC_UNLOCK',date:D,reason:'Opening correction'});
    s=M.execute(s,'owner',{type:'ACC_OPENING_REOPEN',reason:'Late correction'});assert.equal(M.accOpening(s).status,'draft');
  });
  test('[ACC1-OPENING] permissions per role: Owner, Manager and Accountant enter; Cashier, Reception, Supervisor, Housekeeper, Maintenance cannot',()=>{
    for(const uid of ['owner','manager','accountant'])assert.equal(M.accOpening(save(M.seed(),uid)).by,uid);
    for(const uid of ['cashier','reception','supervisor','housekeeper','maint'])assert.throws(()=>save(M.seed(),uid),e=>e.code==='FORBIDDEN',uid);
    // The Manager loses 'reports' → refused even though the role may enter.
    const s=M.execute(M.seed(),'owner',{type:'GRANT',role:'Manager',permission:'reports'});
    assert.throws(()=>save(s,'manager'),e=>e.code==='FORBIDDEN');
    // Approve / reopen: Owner only.
    const d=save(M.seed(),'manager');
    for(const uid of ['manager','accountant','cashier','reception'])assert.throws(()=>approve(d,uid),e=>e.code==='FORBIDDEN',uid);
  });
  test('[ACC1-OPENING] validation: FY start only, one side per line, one line per ledger/party, no 9999 / 1400, codes and party ids',()=>{
    const s=M.seed(),bad=(lines,re,extra)=>assert.throws(()=>save(s,'manager',lines,extra),e=>e.code==='VALIDATION'&&re.test(e.message),String(re));
    bad(RULEBOOK,/start of the business financial year \(2026-04-01\)/,{asOf:'2025-04-01'});
    bad(RULEBOOK,/start of the business financial year/,{asOf:'2026-05-01'});
    bad([],/at least one opening line/);bad('x',/Enter the opening lines/);
    bad([{account:'1400',debit:5,credit:0}],/at least one opening line/);
    bad([{account:'1000',debit:100,credit:100}],/either a debit or a credit/);
    bad([{account:'1000',debit:0,credit:0}],/either a debit or a credit/);
    bad([{account:'1000',debit:-5,credit:0}],/whole paise/);bad([{account:'1000',debit:10.5,credit:0}],/whole paise/);
    bad([{account:'1000',debit:5,credit:0},{account:'1000',debit:0,credit:5}],/entered twice/);
    bad([{account:'9999',debit:5,credit:0}],/Suspense/);bad([{account:'1400/X',debit:5,credit:0}],/filled automatically/);
    bad([{account:'CASH',debit:5,credit:0}],/ledger code or a party ledger/);bad([{account:'1110/',debit:5,credit:0}],/ledger code or a party ledger/);
    bad(RULEBOOK,/200 characters/,{note:'x'.repeat(201)});
    // asOf may be omitted (the business FY start); a party id is a plain string; an unknown code is a warning only.
    const ok=M.execute(s,'manager',{type:'ACC_OPENING_SAVE',lines:[{account:'1110/CMP-0007',debit:500,credit:0},{account:'1234',debit:0,credit:500}],note:'From the CA'});
    assert.deepEqual([M.accOpening(ok).asOf,M.accOpening(ok).note],[FY,'From the CA']);
    assert.ok(M.accOpeningLineProblems(M.accOpening(ok).lines).some(p=>p.severity==='warning'&&/1234 is not in the chart/.test(p.message)));
  });
  test('[ACC1-OPENING] idempotent replay with the same key; old saves without the field behave as before',()=>{
    const s=M.seed(),a=M.execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:FY,lines:RULEBOOK,key:'open-1'});
    assert.deepEqual(M.execute(a,'manager',{type:'ACC_OPENING_SAVE',asOf:FY,lines:WITHOUT_CAPITAL,key:'open-1'}),a);
    const old={...s,accountingX:undefined};assert.deepEqual(M.accOpeningPosted(old),[]);assert.equal(M.accOpeningLockBlocker(old,D),'');
    // Production: the server carries the approved entry in its cumulative balance; the device never adds it a second time.
    const prod=approve(save(s,'manager'));prod.accountingX={...prod.accountingX,server:{from:D,to:D,days:{},opening:[],mapRows:[]}};
    assert.deepEqual(M.accOpeningPosted(prod),[]);assert.ok(M.accOpeningVoucher(prod));
  });
}
