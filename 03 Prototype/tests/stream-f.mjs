// Stream F tests: cashier shift (BL-028), expenses (BL-029), day closing/resume/correction (BL-030), reports (BL-031).
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {execute,loadScenario,baseFixture,drawer,totals,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const nightsOn=(s,id,d)=>s.bookings.find(b=>b.id===id).lines.filter(l=>l.kind==='room'&&l.date===d).length;
  /** S1 with all routine blockers clear: shift open, no bookings. */
  const closeable=(s,u='manager')=>{s=execute(s,u,{type:'CLOSE_RUN'});s=execute(s,u,{type:'SHIFT_CLOSE',counted:drawer(s)});s=tillClear(M,s);return execute(s,u,{type:'DAY_CLOSE'});};

  // ── BL-028 Cashier shift ──
  test('[F] Shift: expected-cash formula parts equal drawer(); movements listed',()=>{
    const s=baseFixture(),x=M.shiftExpected(s);
    assert.equal(x.expected,drawer(s));assert.equal(x.expected,1072500);assert.equal(x.consistent,true); // DEC-069 GST: expected 1075000 → 1072500 (POS-F01 ₹550 → ₹525)
    assert.equal(x.payments,700000);assert.equal(x.expenses,180000);
    assert.equal(x.restaurant,52500);assert.equal(M.shiftMovements(s).length,6); // + POS-F01 cash breakfast ₹550 (shared till) // DEC-069 GST: restaurant cash 55000 → 52500
  });
  test('[F] Shift: exact SHIFT_SUBMIT by cashier closes the shift; cash receipts then rejected',()=>{
    let s=baseFixture();const exp=drawer(s);
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp});
    assert.equal(s.shiftOpen,false);assert.deepEqual(s.shiftClose,{expected:exp,counted:exp,countedBy:'cashier'});assert.equal(M.shiftStatus(s),'Closed');
    assert.equal(s.approvals.length,0);
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'}),/shift is closed/);
    assert.equal(code(()=>execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp})),'INVALID_TRANSITION');
  });
  test('[F] Shift: denial paths — housekeeper denied, negative count, grid mismatch, missing reason',()=>{
    const s=baseFixture(),exp=drawer(s);
    assert.equal(code(()=>execute(s,'housekeeper',{type:'SHIFT_SUBMIT',counted:exp})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:-1})),'VALIDATION');
    assert.equal(code(()=>execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp,denominations:{500:1}})),'VALIDATION');
    assert.throws(()=>execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp-10000}),/reason is required/);
    // A matching denomination grid is accepted: ₹10,725 (₹10,200 + ₹525 restaurant cash) = 21×500 + 1×200 + 1×20 + ₹5 coins
    // (DEC-069 GST: was ₹10,750 = 21×500 + 1×200 + 1×50 with the synthetic 10 %).
    const ok=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp,denominations:{500:21,200:1,20:1,coins:5}});
    assert.equal(ok.shift.denominations[500],21);
  });
  test('[F] Shift: difference → approval; receipts blocked; no self-approval; manager limit; owner approves with difference recorded',()=>{
    let s=baseFixture();const exp=drawer(s);
    s=execute(s,'manager',{type:'SHIFT_SUBMIT',counted:exp-30000,reason:'Short ₹300 · miscount suspected'});
    const a=s.approvals.at(-1);
    assert.equal(a.kind,'cash-difference');assert.equal(a.meta.command,'SHIFT_DIFFERENCE_DECIDE');assert.equal(a.target,s.date);
    assert.deepEqual([a.meta.expected,a.meta.counted],[exp,exp-30000]);assert.equal(a.status,'Pending');
    assert.equal(M.shiftStatus(s),'Submitted');
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'}),/shift is closed/);
    assert.equal(code(()=>execute(s,'reception',{type:'SHIFT_SUBMIT',counted:exp})),'INVALID_TRANSITION');
    assert.throws(()=>execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true}),/own request/);
    assert.equal(code(()=>execute(s,'cashier',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true})),'FORBIDDEN'); // no approve permission
    assert.throws(()=>execute(s,'owner',{type:'DAY_CLOSE'}),/pending/);
    s=execute(s,'owner',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true});
    assert.equal(s.approvals.at(-1).status,'Approved');assert.equal(s.shiftOpen,false);
    assert.deepEqual(s.shiftClose,{expected:exp,counted:exp-30000,countedBy:'manager'});
    assert.equal(code(()=>execute(s,'owner',{type:'SHIFT_DIFFERENCE_DECIDE',id:a.id,approve:true})),'INVALID_TRANSITION');
  });
  test('[F] Shift: manager accepts ≤ ₹200 but not above; cashier submits, manager approves; next opening float = counted',()=>{
    let s=loadScenario('S1');const exp=drawer(s);
    let big=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp+20100,reason:'Over ₹201'});
    assert.throws(()=>execute(big,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:big.approvals.at(-1).id,approve:true}),/Owner approval/);
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp-20000,reason:'Short ₹200'});
    s=execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'manager',{type:'DAY_CLOSE'});
    const rec=s.closings[s.date];
    assert.equal(rec.cash.difference,-20000);assert.equal(rec.cash.approvedBy,'manager');assert.match(rec.exceptions.join(' '),/Cash difference/);
    s=execute(s,'manager',{type:'NEXT_DAY'});
    assert.equal(s.openingCash,exp-20000);assert.equal(drawer(s),exp-20000);
  });
  test('[F] Shift: rejected difference reopens the shift for recount',()=>{
    let s=baseFixture();const exp=drawer(s);
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:exp-5000,reason:'Short'});
    s=execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1).id,approve:false,reason:'Recount please'});
    assert.equal(s.approvals.at(-1).status,'Rejected');assert.equal(s.shiftOpen,true);assert.equal(M.shiftStatus(s),'Open');
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'}); // cash accepted again
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});assert.equal(M.shiftStatus(s),'Closed');
  });

  // ── BL-029 Expenses ──
  test('[F] Expense: draft → submit → approve → reverse; drawer counted exactly once',()=>{
    let s=baseFixture();const d0=drawer(s);
    s=execute(s,'cashier',{type:'EXPENSE',vendor:'Aqua Water Cans',category:'Supplies',amount:30000,method:'Cash',draft:true,receipt:'aqua-0001.jpg',notes:'20 cans'});
    const id=s.expenses.at(-1).id;assert.equal(s.expenses.at(-1).status,'Draft');assert.equal(s.expenses.at(-1).receipt,'aqua-0001.jpg');assert.equal(drawer(s),d0);
    assert.equal(code(()=>execute(s,'reception',{type:'EXPENSE_SUBMIT',id})),'FORBIDDEN'); // not own draft
    assert.equal(code(()=>execute(s,'manager',{type:'EXPENSE_APPROVE',id,approve:true})),'INVALID_TRANSITION'); // draft not approvable
    s=execute(s,'cashier',{type:'EXPENSE_SUBMIT',id});assert.equal(s.expenses.find(e=>e.id===id).status,'Pending');
    assert.throws(()=>execute(s,'cashier',{type:'EXPENSE_APPROVE',id,approve:true}),/Access denied/);
    s=execute(s,'manager',{type:'EXPENSE_APPROVE',id,approve:true});assert.equal(drawer(s),d0-30000);
    assert.throws(()=>execute(s,'manager',{type:'EXPENSE_REVERSE',id}),/reason is required/);
    assert.equal(code(()=>execute(s,'cashier',{type:'EXPENSE_REVERSE',id,reason:'x'})),'FORBIDDEN');
    s=execute(s,'manager',{type:'EXPENSE_REVERSE',id,reason:'Supplier refunded cans'});
    const orig=s.expenses.find(e=>e.id===id),rev=s.expenses.at(-1);
    assert.equal(orig.status,'Reversed');assert.equal(rev.reversalOf,id);assert.equal(rev.amount,-30000);assert.equal(rev.status,'Posted');assert.equal(rev.date,s.date);
    assert.equal(drawer(s),d0);assert.equal(M.shiftExpected(s).expected,d0);
    assert.equal(code(()=>execute(s,'manager',{type:'EXPENSE_REVERSE',id,reason:'again'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'manager',{type:'EXPENSE_REVERSE',id:rev.id,reason:'reverse the reversal'})),'INVALID_TRANSITION');
    const p=M.reportPack(s,s.date,s.date),e=p.tabs.find(t=>t.id==='expenses');
    assert.equal(e.metrics.find(m=>m.id==='cash').value,180000); // EXP-101 only; the reversed pair nets to zero
  });
  test('[F] Expense: vendor credit has no drawer effect; manager limit; reject without reason still works; duplicate warning',()=>{
    let s=baseFixture();const d0=drawer(s);
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Paint Depot',category:'Maintenance',amount:600000,method:'Vendor credit'});
    const id=s.expenses.at(-1).id;
    assert.throws(()=>execute(s,'manager',{type:'EXPENSE_APPROVE',id,approve:true}),/Owner approval/);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});assert.equal(drawer(s),d0);
    s=execute(s,'manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:false});assert.equal(s.expenses.find(e=>e.id==='EXP-102').status,'Rejected');
    assert.equal(M.expenseDuplicates(s,'fresh linen services',180000,s.date).length,1);
    assert.equal(M.expenseDuplicates(s,'Morning Supplies',85000,s.date).length,0); // rejected items do not warn
    assert.equal(code(()=>execute(s,'reception',{type:'EXPENSE',vendor:'X',category:'Supplies',amount:100,method:'Bitcoin'})),'VALIDATION');
  });

  // ── BL-030 Day closing ──
  test('[F] Close: CLOSE_RUN interrupt/resume posts each night exactly once',()=>{
    let s=loadScenario('S0');const targets=M.closingNightTargets(s).map(b=>b.id);
    assert.equal(targets.length,8);
    s=execute(s,'manager',{type:'CLOSE_RUN',interruptAfter:3});
    assert.deepEqual(s.closeProgress.posted,targets.slice(0,3));assert.equal(s.closeProgress.interrupted,true);
    assert.deepEqual(targets.map(id=>nightsOn(s,id,s.date)),[1,1,1,0,0,0,0,0]);
    assert.throws(()=>execute(s,'manager',{type:'DAY_CLOSE'}),/Post room nights/);
    s=execute(s,'manager',{type:'CLOSE_RUN',interruptAfter:2});
    assert.equal(s.closeProgress.posted.length,5);
    s=execute(s,'manager',{type:'CLOSE_RUN'});
    assert.equal(s.closeProgress.interrupted,false);assert.equal(s.closeProgress.runs,3);
    assert.deepEqual(targets.map(id=>nightsOn(s,id,s.date)),[1,1,1,1,1,1,1,1]);
    const before=JSON.stringify(s.bookings);s=execute(s,'manager',{type:'CLOSE_RUN'});s=execute(s,'manager',{type:'NIGHT_POST'});
    assert.equal(JSON.stringify(s.bookings),before);
    assert.equal(code(()=>execute(s,'reception',{type:'CLOSE_RUN'})),'FORBIDDEN');
  });
  test('[F] Close: S5 resume posts only the remaining 5 nights',()=>{
    let s=loadScenario('S5');
    assert.equal(s.closeProgress.posted.length,3);assert.equal(s.closeProgress.interrupted,true);
    const lines=()=>s.bookings.flatMap(b=>b.lines).filter(l=>l.kind==='room'&&l.date===s.date).length;
    assert.equal(lines(),3);
    const versions=Object.fromEntries(s.bookings.map(b=>[b.id,b.version]));
    s=execute(s,'manager',{type:'CLOSE_RUN'});
    assert.equal(lines(),8);assert.equal(s.closeProgress.posted.length,8);
    for(const id of ['HCC-1041','HCC-1042','HCC-1043']){assert.equal(nightsOn(s,id,s.date),1);assert.equal(s.bookings.find(b=>b.id===id).version,versions[id]);}
    assert.match(s.audit[0].detail,/5 new night/);
    assert.match(M.closingSummary(s).exceptions.join(' '),/resumed/);
  });
  test('[F] Close: closing record is immutable; rerun and later activity leave it unchanged',()=>{
    let s=closeable(loadScenario('S1'));const d=s.date,rec=JSON.stringify(s.closings[d]);
    assert.equal(s.closings[d].date,d);assert.equal(s.closings[d].closedBy,'manager');
    assert.equal(code(()=>execute(s,'manager',{type:'DAY_CLOSE'})),'DAY_CLOSED');
    assert.equal(code(()=>execute(s,'manager',{type:'CLOSE_RUN'})),'DAY_CLOSED');
    s=execute(s,'manager',{type:'NEXT_DAY'});
    s=execute(s,'reception',{type:'EXPENSE',vendor:'Later Vendor',category:'Supplies',amount:5000,method:'Cash'});
    s=execute(s,'manager',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    assert.equal(JSON.stringify(s.closings[d]),rec);
    const p=M.reportPack(s,d,d);assert.equal(p.tabs.find(t=>t.id==='expenses').metrics.find(m=>m.id==='total').value,s.closings[d].expenses.total);
  });
  test('[F] Close: preflight lists blockers with drill links; expired hold blocks close',()=>{
    let s=baseFixture();const pf=M.closingPreflight(s);
    assert.equal(pf.find(x=>x.id==='arrivals').items.length,2);assert.equal(pf.find(x=>x.id==='arrivals').items[0].booking,'HCC-1049');
    assert.equal(pf.find(x=>x.id==='nights').items.length,8);assert.equal(pf.find(x=>x.id==='expenses').items.length,1);
    assert.equal(pf.find(x=>x.id==='shift').items[0].page,'cashier');
    s=loadScenario('S1');s.bookings.push({id:'HCC-9001',guest:'Hold Test (fictional)',email:'',people:1,room:'106',arrival:'2026-10-25',departure:'2026-10-26',rate:450000,source:'Phone',status:'Held',registered:false,lines:[],receipts:[],version:1,notes:'',holdUntil:'2026-10-24T07:30'});
    assert.equal(M.closingPreflight(s).find(x=>x.id==='holds').items.length,1);
    s=execute(s,'manager',{type:'SHIFT_CLOSE',counted:drawer(s)});
    assert.throws(()=>execute(s,'manager',{type:'DAY_CLOSE'}),/expired holds/);
  });
  test('[F] Close: closed-date correction needs Owner approval and posts a dated adjustment today',()=>{
    let s=loadScenario('S0');
    const before=totals(s.bookings.find(b=>b.id==='HCC-1041'));
    assert.equal(code(()=>execute(s,'manager',{type:'CORRECTION_ADJUST',id:'HCC-1041',date:'2026-10-24',amount:-10000,reason:'x'})),'VALIDATION'); // not closed
    s=execute(s,'manager',{type:'CORRECTION_ADJUST',id:'HCC-1041',date:'2026-10-23',amount:-10000,reason:'Rate keyed wrongly on 23 Oct'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'closed-date-correction');assert.equal(a.meta.command,'CORRECTION_ADJUST_DECIDE');assert.equal(a.target,'2026-10-23');
    assert.throws(()=>execute(s,'manager',{type:'CORRECTION_ADJUST_DECIDE',id:a.id,approve:true}),/own request/);
    assert.equal(code(()=>execute(s,'cashier',{type:'CORRECTION_ADJUST_DECIDE',id:a.id,approve:true})),'FORBIDDEN');
    // A second owner-only rule: a manager who did not request it still cannot approve.
    const s2=execute(s,'owner',{type:'CORRECTION_ADJUST',id:'HCC-1042',date:'2026-10-23',amount:5000,reason:'Missed minibar'});
    assert.throws(()=>execute(s2,'manager',{type:'CORRECTION_ADJUST_DECIDE',id:s2.approvals.at(-1).id,approve:true}),/Owner/);
    s=execute(s,'owner',{type:'CORRECTION_ADJUST_DECIDE',id:a.id,approve:true});
    const b=s.bookings.find(b=>b.id==='HCC-1041'),l=b.lines.at(-1);
    assert.equal(l.kind,'adjustment');assert.equal(l.date,s.date);assert.equal(l.correctionOf,'2026-10-23');assert.equal(l.amount,-10000);
    assert.equal(totals(b).net,before.net-10000);
    assert.equal(b.lines.filter(l=>l.date==='2026-10-23').length,1); // the closed-date line is untouched
  });

  // ── BL-031 Reports ──
  test('[F] Reports: S0 walkthrough — every total equals its drill-down rows; Room 105 figures; CSV header',()=>{
    const w=walkthrough();const s=w.s;
    // DEC-069 GST (old → new): drawers 1075000/1125000/1125000/1275000/1245000 → 1072500/1122500/1122500/1272500/1242500 (POS-F01 ₹550 → ₹525);
    // Room 105 tax 40000 → 26500 (rooms 5 %, laundry 18 %, discount −5 %), total/paid 440000 → 426500, final UPI 140000 → 126500.
    assert.deepEqual(w.drawers,[1072500,1122500,1122500,1272500,1242500]);
    assert.deepEqual(w.folio26,{net:400000,tax:26500,total:426500,paid:426500,deposit:0,balance:0});
    assert.equal(w.rerun,'DAY_CLOSED');
    const p=M.reportPack(s,'2026-10-24','2026-10-26');
    for(const t of p.tabs)assert.equal(new Set(t.metrics.map(m=>m.id)).size,t.metrics.length,'unique metric ids in '+t.id);
    for(const t of p.tabs)for(const m of t.metrics)assert.equal(m.value,m.rows.reduce((a,r)=>a+r.amount,0),`${t.id}/${m.id}`);
    const tab=(id)=>p.tabs.find(t=>t.id===id),val=(t,id)=>tab(t).metrics.find(m=>m.id===id);
    const r105=(t,id)=>val(t,id).rows.filter(r=>r.booking===w.id).reduce((a,r)=>a+r.amount,0);
    assert.equal(r105('revenue','net'),400000);assert.equal(r105('revenue','tax'),26500);assert.equal(r105('revenue','room'),400000);
    assert.equal(r105('receipts','deposit-in'),50000);assert.equal(r105('receipts','deposit-out'),-50000);assert.equal(r105('receipts','net'),426500);
    assert.equal(val('expenses','cash').rows.filter(r=>r.date==='2026-10-26').reduce((a,r)=>a+r.amount,0),30000);
    // closing records exist for each walkthrough date and agree with the report for that date
    for(const d of ['2026-10-24','2026-10-25','2026-10-26']){
      const one=M.reportPack(s,d,d),c=s.closings[d];
      assert.equal(one.tabs.find(t=>t.id==='revenue').metrics.find(m=>m.id==='net').value,c.revenue.net,d);
      assert.equal(one.tabs.find(t=>t.id==='receipts').metrics.find(m=>m.id==='net').value,c.receipts.net,d);
    }
    assert.deepEqual([s.closings['2026-10-24'].cash.counted,s.closings['2026-10-25'].cash.counted,s.closings['2026-10-26'].cash.counted],[1122500,1122500,1242500]); // DEC-069 GST: 1125000/1125000/1245000 → 1122500/1122500/1242500
    // per-date table rows sum to the range metric
    const rev=tab('revenue');assert.equal(rev.table.rows.reduce((a,r)=>a+r[5],0),val('revenue','net').value);
    const csv=M.reportCsv(s,p,'revenue');
    assert.match(csv,/DEMONSTRATION/);assert.match(csv,/"Scenario","S0"/);assert.match(csv,/"Business dates","2026-10-24","2026-10-26"/);
    assert.match(M.reportCsv(s,p,'dictionary'),/Occupancy %/);
    assert.match(M.reportDrillCsv(s,p,val('revenue','tax')),/"Total"/);
  });
  test('[F] Reports: occupancy dictionary edge cases (zero sellable / zero occupied → "—", 1 dp)',()=>{
    assert.equal(M.reportPct(8,23),'34.8%');assert.equal(M.reportPct(0,23),'0.0%');assert.equal(M.reportPct(1,0),'—');assert.equal(M.reportPct(1,3),'33.3%');assert.equal(M.reportPct(2,3),'66.7%');
    assert.equal(M.reportRatioMoney(0,0),'—');
    let s=loadScenario('S1');s.rooms.forEach(r=>{r.blocked=true;});
    const occ=M.reportPack(s,s.date,s.date).tabs.find(t=>t.id==='occupancy');
    assert.equal(occ.metrics.find(m=>m.id==='sellable').value,0);
    for(const id of ['occupancy','adr','revpar'])assert.equal(occ.ratios.find(r=>r.id===id).display,'—');
    assert.deepEqual(occ.table.rows[0].slice(3),['—',0,'—','—']);
    const base=M.reportPack(baseFixture(),'2026-10-23','2026-10-23').tabs.find(t=>t.id==='occupancy');
    assert.equal(base.ratios.find(r=>r.id==='occupancy').display,'34.8%');
    assert.equal(base.ratios.find(r=>r.id==='adr').display,M.money(Math.round(1950000/8)));
  });

  /** S0 three-day walkthrough using stream F commands (cashier submits exact counts, resumable night audit). */
  function walkthrough(){
    let s=loadScenario('S0');const X=(u,c)=>{s=execute(s,u,c);};const drawers=[drawer(s)];
    X('reception',{type:'BOOK',guest:'Rahul Verma (fictional)',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2,source:'Phone',key:'w-book'});
    const id=s.bookings.at(-1).id;
    X('reception',{type:'PAYMENT',id,amount:100000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0001',key:'w-adv'});
    X('reception',{type:'REGISTER',id});X('reception',{type:'CHECKIN',id});
    X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit',key:'w-dep'});drawers.push(drawer(s));
    X('manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});X('manager',{type:'CLOSE_RUN',interruptAfter:4});X('manager',{type:'CLOSE_RUN'});
    X('cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});drawers.push(drawer(s));
    X('reception',{type:'CHARGE',id,amount:50000,label:'Laundry service'});
    X('reception',{type:'DISCOUNT',id,amount:50000,reason:'Service delay goodwill'});
    X('manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});X('manager',{type:'CLOSE_RUN'});
    X('cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});
    X('cashier',{type:'PAYMENT',id,amount:200000,method:'Cash',kind:'payment'});
    X('cashier',{type:'PAYMENT',id,amount:126500,method:'UPI',kind:'payment',reference:'DEMO-UPI-0002'}); // DEC-069 GST: 140000 → 126500
    X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return'});drawers.push(drawer(s));
    const folio26=totals(s.bookings.find(b=>b.id===id));
    X('reception',{type:'CHECKOUT',id,key:'checkout:'+id});
    X('cashier',{type:'EXPENSE',vendor:'Aqua Water Cans',category:'Supplies',amount:30000,method:'Cash'});
    X('manager',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});drawers.push(drawer(s));
    X('manager',{type:'CLOSE_RUN'});X('cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});
    const rerun=code(()=>execute(s,'manager',{type:'DAY_CLOSE'}));
    return {s,id,folio26,drawers,rerun};
  }
}
