// Module 20 · Accounting automation — domain/command tests. Names prefixed [ACC].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const D='2026-10-24',Y='2026-10-23';
  const allBalanced=vs=>vs.every(v=>{const t=M.accVoucherTotals(v);return t.debit===t.credit&&t.debit>0;});
  const checks=(s,d)=>Object.fromEntries(M.accReconcile(s,d).map(c=>[c.id,c]));
  // Close 24 Oct on the base fixture (check-in arrivals, approve expense, night post, exact cash count).
  function closeDay(s=M.seed()){
    for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=M.execute(s,'owner',{type:'REGISTER',id:b.id});s=M.execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=M.execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    s=M.execute(s,'owner',{type:'NIGHT_POST'});s=M.execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});
    s=tillClear(M,s);return M.execute(s,'owner',{type:'DAY_CLOSE'});
  }

  test('[ACC] module slice defaults to empty',()=>{const s=M.seed();assert.deepEqual(M.accSlice(s),{});assert.equal(M.accMapping(s)['line:room'],'4000');});
  test('[ACC] fixture 24 Oct: 10 receipt vouchers + 1 expense voucher, all balanced, collections ₹21,500',()=>{
    const s=M.seed(),vs=M.accVouchers(s,D);
    assert.equal(vs.filter(v=>v.type==='Receipt').length,10);assert.equal(vs.filter(v=>v.source.kind==='expense').length,1);
    assert.ok(allBalanced(vs));const t=M.accTotals(vs);assert.equal(t.debit,t.credit);
    const c=checks(s,D);assert.equal(c.collections.ledger,2150000);assert.equal(c.collections.report,M.metrics(s).collections);
    assert.ok(Object.values(c).every(x=>x.ok),JSON.stringify(Object.values(c).filter(x=>!x.ok)));
    // pending expense EXP-102 produces no voucher
    assert.ok(!vs.some(v=>v.source.id==='EXP-102'));
  });
  // DEC-069 GST: 10 % synthetic tax on ledger 2100 → 5 % room GST split CGST (2110) + SGST (2120): tax room/10 → room/20, each half room/40.
  test('[ACC] 23 Oct sales vouchers: Dr guest ledger = room revenue + 5 % GST (CGST + SGST halves); trial balance agrees',()=>{
    const s=M.seed(),vs=M.accVouchers(s,Y);assert.equal(vs.length,8);assert.ok(vs.every(v=>v.type==='Sales'&&v.no.startsWith('SAL/20261023/')));
    const tb=Object.fromEntries(M.accTrialBalance(vs).map(r=>[r.account,r]));
    const room=s.bookings.filter(b=>b.status==='In house').reduce((a,b)=>a+b.rate,0);
    assert.equal(tb['4000'].credit,room);assert.equal(tb['2100'],undefined);assert.equal(tb['2110'].credit,room/40);assert.equal(tb['2120'].credit,room/40);assert.equal(tb['1100'].debit,room+room/20);
    for(const v of vs){const e=Object.fromEntries(v.entries.map(x=>[x.account,x]));assert.equal(e['2110'].credit+e['2120'].credit,e['1100'].debit-e['4000'].credit,v.no);}
    assert.equal(M.accTrialBalance(vs).reduce((a,r)=>a+r.balance,0),0);
  });
  test('[ACC] every voucher traces to a stable, unique source ID and existing record',()=>{
    const s=M.seed(),vs=[...M.accVouchers(s,Y),...M.accVouchers(s,D)];
    assert.equal(new Set(vs.map(v=>v.source.id)).size,vs.length);
    for(const v of vs){const src=M.accSourceOf(s,v);assert.ok(src.line||src.receipt||src.expense||src.posReceipt,v.source.id);}
    assert.deepEqual(M.accLiveVouchers(s,D).map(v=>v.no+v.source.id),M.accLiveVouchers(s,D).map(v=>v.no+v.source.id));
  });
  test('[ACC] full day flow (charge, discount, deposit, return, refund, UPI) stays balanced and reconciles',()=>{
    let s=M.seed();const id='HCC-1041';
    s=M.execute(s,'owner',{type:'NIGHT_POST'});
    s=M.execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});
    s=M.execute(s,'owner',{type:'DISCOUNT',id,amount:20000,reason:'Goodwill'});
    s=M.execute(s,'cashier',{type:'PAYMENT',id,amount:300000,method:'Cash',kind:'deposit'});
    s=M.execute(s,'cashier',{type:'PAYMENT',id,amount:100000,method:'Cash',kind:'deposit-return'});
    s=M.execute(s,'cashier',{type:'PAYMENT',id,amount:55000,method:'Card',kind:'payment',reference:'DEMO-CARD'});
    s=M.execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:'seed-pay-0',amount:5000,reason:'Overcharge'});
    s=M.execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=M.execute(s,'owner',{type:'REFUND_EXECUTE',approvalId:s.approvals.at(-1).id});
    const vs=M.accVouchers(s,D);assert.ok(allBalanced(vs));
    const c=checks(s,D);assert.ok(Object.values(c).every(x=>x.ok),JSON.stringify(Object.values(c).filter(x=>!x.ok)));
    assert.equal(c.collections.report,M.metrics(s).collections);assert.equal(c.cash.ledger,M.drawer(s)-s.openingCash);
    const disc=vs.find(v=>v.source.sub==='discount');assert.deepEqual(disc.entries.map(e=>[e.account,e.debit,e.credit]),[['1100',0,21000],['4900',20000,0],['2110',500,0],['2120',500,0]]); // DEC-069 GST: discount ₹200 at the room rate 5 % = −₹10 (was −₹20 on 2100), split CGST/SGST
    const ref=vs.find(v=>v.source.sub==='refund');assert.equal(ref.type,'Payment');assert.deepEqual(ref.entries.map(e=>[e.account,e.debit,e.credit]),[['1100',5000,0],['1000',0,5000]]);
    assert.equal(M.accExceptions(s,D).filter(x=>x.severity==='error').length,0);
  });
  test('[ACC] closed day reconciles to the closing record, cash difference posts to over/short',()=>{
    let s=M.seed();s=M.execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-5000,reason:'Short by ₹50'});
    const apr=s.approvals.at(-1).id;s=M.execute(s,'owner',{type:'SHIFT_DIFFERENCE_DECIDE',id:apr,approve:true});
    const vs=M.accVouchers(s,D),j=vs.find(v=>v.source.kind==='closing');assert.ok(j,'cash difference voucher');
    assert.deepEqual(j.entries.map(e=>[e.account,e.debit,e.credit]),[['5900',5000,0],['1000',0,5000]]);
    assert.ok(Object.values(checks(s,D)).every(x=>x.ok));
  });
  test('[ACC] lock requires a closed day; lock freezes snapshot; second lock rejected; idempotent key',()=>{
    let s=M.seed();
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:D}),e=>e.code==='INVALID_TRANSITION'&&/Close the business day/.test(e.message));
    s=closeDay(s);assert.equal(M.accDayStatus(s,D),'Closed');
    const n=M.accLiveVouchers(s,D).length;
    let l=M.execute(s,'accountant',{type:'ACC_LOCK',date:D,key:'lock-1'});
    assert.equal(M.accDayStatus(l,D),'Locked');assert.equal(M.accLockOf(l,D).count,n);assert.equal(M.accLockOf(l,D).debit,M.accLockOf(l,D).credit);
    assert.deepEqual(M.execute(l,'accountant',{type:'ACC_LOCK',date:D,key:'lock-1'}),l);
    assert.throws(()=>M.execute(l,'accountant',{type:'ACC_LOCK',date:D}),/already locked/);
    // closing-record cash check present and OK
    assert.ok(checks(l,D).cash.ok);
  });
  test('[ACC] mapping change after lock does not alter locked vouchers but changes live unlocked ones',()=>{
    let s=closeDay();s=M.execute(s,'accountant',{type:'ACC_LOCK',date:D});
    const before=JSON.stringify(M.accVouchers(s,D));
    s=M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:service',account:'4000'});
    assert.equal(JSON.stringify(M.accVouchers(s,D)),before);
    assert.ok(!M.accExceptions(s,D).some(x=>x.kind==='drift'),'mapping change is not source drift');
    let t=M.execute(M.seed(),'owner',{type:'CHARGE',id:'HCC-1041',label:'Minibar',amount:30000});
    t=M.execute(t,'owner',{type:'ACC_MAP_SET',source:'line:service',account:'4000'});
    const v=M.accVouchers(t,D).find(v=>v.source.sub==='service');assert.ok(v.entries.some(e=>e.account==='4000'&&e.credit===30000));
    t=M.execute(t,'owner',{type:'ACC_MAP_RESET',source:'line:service'});assert.equal(M.accMapping(t)['line:service'],'4100');
    assert.equal(M.accSlice(t).mapHistory.length,2);
  });
  test('[ACC] unmapped classification → Suspense, error exception, blocks lock until mapped',()=>{
    let s=M.loadScenario('ACC');
    const ex=M.accExceptions(s,D).filter(x=>x.kind==='unmapped');assert.equal(ex.length,1);assert.equal(ex[0].key,'expense:Kitchen gas');
    const v=M.accVouchers(s,D).find(v=>v.source.id==='EXP-ACC-1');assert.equal(v.entries[0].account,'9999');assert.ok(M.accBalanced(v));
    assert.ok(M.accMapRows(s).some(r=>r.key==='expense:Kitchen gas'&&!r.mapped&&r.uses===1));
    // closed-day lock is blocked by the exception
    s.closedDates.push(D);assert.throws(()=>M.execute(s,'owner',{type:'ACC_LOCK',date:D}),e=>e.code==='VALIDATION'&&/exception/.test(e.message));
    s=M.execute(s,'owner',{type:'ACC_MAP_SET',source:'expense:Kitchen gas',account:'5050'});
    assert.equal(M.accExceptions(s,D).filter(x=>x.severity==='error').length,0);
    s=M.execute(s,'owner',{type:'ACC_LOCK',date:D});assert.equal(M.accDayStatus(s,D),'Locked');
    assert.equal(M.accDayStatus(s,Y),'Locked');
  });
  test('[ACC] mapping validation: admin only, no suspense, unknown key, no-op rejected',()=>{
    const s=M.seed();
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_MAP_SET',source:'line:room',account:'4100'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:room',account:'9999'}),/not Suspense/);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:room',account:'7777'}),/chart of accounts/);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MAP_SET',source:'bogus:x',account:'4000'}),e=>e.code==='NOT_FOUND');
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:room',account:'4000'}),/already maps/);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_MAP_RESET',source:'line:room'}),/default/);
  });
  test('[ACC] lock permissions: cashier/housekeeper denied; unlock is owner-only with a reason',()=>{
    let s=closeDay();
    assert.throws(()=>M.execute(s,'cashier',{type:'ACC_LOCK',date:D}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'housekeeper',{type:'ACC_LOCK',date:D}),e=>e.code==='FORBIDDEN');
    s=M.execute(s,'manager',{type:'ACC_LOCK',date:D});
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_UNLOCK',date:D,reason:'Fix'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_UNLOCK',date:D,reason:''}),/reason/);
    s=M.execute(s,'owner',{type:'ACC_UNLOCK',date:D,reason:'Reclassify laundry'});assert.equal(M.accDayStatus(s,D),'Closed');
    assert.deepEqual(M.accSlice(s).lockHistory.map(h=>h.action),['lock','unlock']);
    assert.throws(()=>M.execute(s,'owner',{type:'ACC_UNLOCK',date:D,reason:'again'}),/not locked/);
  });
  test('[ACC] snapshot drift warning when sources for a locked date change',()=>{
    let s=M.loadScenario('ACC');assert.equal(M.accExceptions(s,Y).length,0);
    s.bookings[0].lines.push({id:'manual-test',label:'Late line',amount:1000,date:Y,kind:'service',taxBps:1000});
    assert.ok(M.accExceptions(s,Y).some(x=>x.kind==='drift'&&x.severity==='warning'));
    assert.equal(M.accVouchers(s,Y).length,8,'snapshot kept');
  });
  test('[ACC] unbalanced voucher detection and expense reversal swaps sides',()=>{
    const v={no:'X',date:D,type:'Journal',narration:'',source:{kind:'closing',id:'x',sub:'x'},entries:[{account:'1000',debit:100,credit:0},{account:'5900',debit:0,credit:90}],unmapped:[]};
    assert.equal(M.accBalanced(v),false);
    let s=M.seed();s=M.execute(s,'manager',{type:'EXPENSE_REVERSE',id:'EXP-101',reason:'Duplicate entry'});
    const r=M.accVouchers(s,D).find(v=>v.source.sub==='reversal');
    assert.ok(r,'reversal voucher');{assert.deepEqual(r.entries.map(e=>[e.account,e.debit,e.credit]),[['5010',0,180000],['1000',180000,0]]);assert.ok(checks(s,D).expenses.ok);}
  });
  test('[ACC] CSV export: generic header, one row per entry, debit total = credit total',()=>{
    const s=M.seed(),vs=M.accVouchers(s,D),csv=M.accCsv(vs),rows=csv.split('\r\n');
    assert.equal(rows[0],'Date,Voucher No,Voucher Type,Ledger,Debit,Credit,Narration,Source ID');
    assert.equal(rows.length-1,vs.reduce((a,v)=>a+v.entries.length,0));
    const parse=r=>r.match(/("([^"]|"")*"|[^,]*)(,|$)/g).map(x=>x.replace(/,$/,''));
    let dr=0,cr=0;for(const r of rows.slice(1)){const c=parse(r);dr+=Number(c[4]||0);cr+=Number(c[5]||0);}
    assert.equal(Math.round(dr*100),Math.round(cr*100));assert.equal(Math.round(dr*100),M.accTotals(vs).debit);
    assert.ok(rows.slice(1).every(r=>r.startsWith(D+',')));
  });
  test('[ACC] base fixture figures unchanged by the module',()=>{const s=M.seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);assert.equal(s.accountingX,undefined);}); // DEC-069 GST: drawer 1075000 → 1072500 (POS-F01 cash ₹500+₹50 → ₹500+₹25)
}
