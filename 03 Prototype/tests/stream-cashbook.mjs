// W05-D · Cash book tests. Names prefixed [CB]. The cash book has no money state of its own: every figure must equal the cashier shift / closing records.
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {seed,execute,loadScenario,shiftMovements,shiftExpected,drawer,cbDay,cbRange,cbDates,cbCsv,cbSlice,cbCanLock,cbCanReopen}=M;
  const code=c=>e=>e.code===c;
  const fy=loadScenario('FY26');
  test('[CB] current day: entries equal the cashier shift movements, closing = drawer',()=>{
    const s=seed(),d=cbDay(s,s.date),mv=shiftMovements(s);
    assert.equal(d.entries.length,mv.length);assert.equal(d.opening,s.openingCash);
    assert.equal(d.receipts-d.payments,mv.reduce((a,m)=>a+m.amount,0));
    assert.equal(d.closing,drawer(s));assert.equal(d.closing,shiftExpected(s).expected);assert.equal(d.status,'Open');assert.equal(d.reconciles,true);});
  test('[CB] a cash payment, expense and deposit land on the right side and the book still equals the drawer',()=>{
    let s=seed();
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:10000,method:'Cash',kind:'payment'});
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1041',amount:5000,method:'Cash',kind:'deposit'});
    const d=cbDay(s,s.date);
    assert.equal(d.closing,drawer(s));
    assert.ok(d.entries.some(e=>e.kind==='payment'&&e.in===10000));assert.ok(d.entries.some(e=>e.kind==='deposit'&&e.in===5000));
    assert.ok(d.entries.some(e=>e.kind==='expense'&&e.out>0));});
  test('[CB] FY26: every closed day reconciles with its closing record and carries forward to the next opening',()=>{
    const dates=cbDates(fy).filter(d=>fy.closings[d]);assert.ok(dates.length>=180);
    let prev;
    for(const d of dates){const x=cbDay(fy,d),c=fy.closings[d];
      assert.equal(x.reconciles,true,d);assert.equal(x.opening,c.cash.opening,d);assert.equal(x.expected,c.cash.expected,d);assert.equal(x.counted,c.cash.counted,d);
      assert.equal(x.opening+x.receipts-(x.payments-x.banked),c.cash.expected,d);
      assert.equal(x.carried,c.cash.counted-(c.cash.banked??0),d);
      if(prev)assert.equal(x.opening,prev.carried,'carry '+d);
      prev=x;}
    assert.equal(prev.carried,fy.openingCash);});
  test('[CB] FY26: banking shows as a payment; current day opening equals the last carry',()=>{
    const d=cbDates(fy).find(d=>fy.closings[d]?.cash.banked>0),x=cbDay(fy,d);
    assert.ok(x.entries.some(e=>e.kind==='banking'&&e.out===fy.closings[d].cash.banked));
    assert.equal(cbDay(fy,fy.date).opening,fy.openingCash);});
  test('[CB] range totals are the sum of the days; empty range is empty',()=>{
    const r=cbRange(fy,'2026-06-01','2026-06-30');assert.equal(r.days.length,30);
    assert.equal(r.receipts,r.days.reduce((a,d)=>a+d.receipts,0));assert.equal(r.opening,r.days[0].opening);assert.equal(r.closing,r.days.at(-1).closing);
    assert.equal(cbRange(fy,'2030-01-01','2030-01-31').days.length,0);});
  test('[CB] lock: needs a closed day and a checker who did not count',()=>{
    assert.equal(cbCanLock(fy,'owner','2026-06-10').ok,true);
    assert.equal(cbCanLock(fy,'manager','2026-06-10').ok,false);               // manager closed the historic day
    assert.equal(cbCanLock(fy,'reception','2026-06-10').ok,false);
    assert.equal(cbCanLock(fy,'owner',fy.date).ok,false);                      // today is not closed
    assert.throws(()=>execute(fy,'manager',{type:'CB_LOCK',date:'2026-06-10'}),code('FORBIDDEN'));
    assert.throws(()=>execute(fy,'reception',{type:'CB_LOCK',date:'2026-06-10'}),code('FORBIDDEN'));
    assert.throws(()=>execute(fy,'owner',{type:'CB_LOCK',date:fy.date}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(fy,'owner',{type:'CB_LOCK',date:'bad'}),/date/);});
  test('[CB] lock then reopen: owner only, reason required, audited, figures unchanged',()=>{
    let s=execute(fy,'accountant',{type:'CB_LOCK',date:'2026-06-10'});
    const before=JSON.stringify({...cbDay(fy,'2026-06-10'),locked:undefined});
    const d=cbDay(s,'2026-06-10');assert.equal(d.locked.lockedBy,'accountant');assert.equal(JSON.stringify({...d,locked:undefined}),before);
    assert.throws(()=>execute(s,'owner',{type:'CB_LOCK',date:'2026-06-10'}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(s,'accountant',{type:'CB_REOPEN',date:'2026-06-10',reason:'Fix'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'CB_REOPEN',date:'2026-06-10',reason:' '}),/reason/);
    assert.throws(()=>execute(s,'owner',{type:'CB_REOPEN',date:'2026-06-11',reason:'Not locked'}),code('INVALID_TRANSITION'));
    s=execute(s,'owner',{type:'CB_REOPEN',date:'2026-06-10',reason:'Banking slip entered on the wrong day'});
    assert.equal(cbSlice(s).locks['2026-06-10'],undefined);assert.equal(cbSlice(s).history['2026-06-10'].length,2);
    assert.ok(s.audit.some(a=>/reopened/.test(a.detail)&&/Banking slip/.test(a.detail)));
    assert.equal(cbCanReopen(s,'owner','2026-06-10'),false);});
  test('[CB] the person who closed the day cannot lock it (current day after close)',()=>{
    let s=seed();for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:drawer(s)});
    s=tillClear(M,s);s=execute(s,'owner',{type:'DAY_CLOSE'});
    const x=cbDay(s,s.date);assert.equal(x.status,'Closed');assert.equal(x.reconciles,true);assert.equal(x.expected,s.closings[s.date].cash.expected);
    assert.equal(cbCanLock(s,'owner',s.date).ok,false);assert.equal(cbCanLock(s,'manager',s.date).ok,true);
    s=execute(s,'manager',{type:'CB_LOCK',date:s.date});assert.equal(cbDay(s,s.date).locked.lockedBy,'manager');});
  test('[CB] old saves without the slice read as nothing locked; idempotent key',()=>{
    const s=structuredClone(fy);delete s.cashbookX;assert.deepEqual(cbSlice(s),{locks:{},history:{}});
    const a=execute(fy,'owner',{type:'CB_LOCK',date:'2026-06-10',key:'cb-k1'}),b=execute(a,'owner',{type:'CB_LOCK',date:'2026-06-10',key:'cb-k1'});assert.deepEqual(a,b);});
  test('[CB] CSV: summary + entries, money in rupees, running balance ends at the closing balance',()=>{
    const csv=cbCsv(fy,'2026-06-10','2026-06-11'),d=cbDay(fy,'2026-06-10');
    assert.match(csv,/Cash book/);assert.match(csv,/Opening balance/);assert.match(csv,new RegExp('"'+(d.closing/100).toFixed(2)+'"'));
    assert.ok(csv.split('\r\n').length>10);});
  // F1 (W05 review): whoever counted the cash (SHIFT_SUBMIT, SHIFT_CLOSE) or accepted the difference must never lock that day — also after NEXT_DAY.
  const ready=()=>{let s=seed();for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});return execute(s,'owner',{type:'NIGHT_POST'});};
  const closeDay=s=>execute(tillClear(M,s),'owner',{type:'DAY_CLOSE'});
  test('[CB] F1-A: the manager who submitted an exact count cannot lock the day, before or after NEXT_DAY',()=>{
    let s=ready();s=execute(s,'manager',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=closeDay(s);const d=s.date;
    assert.equal(s.closings[d].cash.countedBy,'manager');assert.equal(cbCanLock(s,'manager',d).ok,false);
    s=execute(s,'owner',{type:'NEXT_DAY'});
    assert.equal(cbCanLock(s,'manager',d).ok,false);assert.throws(()=>execute(s,'manager',{type:'CB_LOCK',date:d}),code('FORBIDDEN'));
    assert.equal(cbCanLock(s,'owner',d).ok,false);                             // owner closed it
    s=execute(s,'accountant',{type:'CB_LOCK',date:d});assert.equal(cbDay(s,d).locked.lockedBy,'accountant');});
  test('[CB] F1-B: the manager who closed the shift with SHIFT_CLOSE cannot lock the day',()=>{
    let s=ready();s=execute(s,'manager',{type:'SHIFT_CLOSE',counted:drawer(s)});assert.equal(s.shiftClose.countedBy,'manager');
    s=closeDay(s);const d=s.date;assert.equal(cbCanLock(s,'manager',d).ok,false);
    s=execute(s,'owner',{type:'NEXT_DAY'});assert.equal(cbCanLock(s,'manager',d).ok,false);assert.equal(cbCanLock(s,'accountant',d).ok,true);});
  test('[CB] F1-C: the manager who accepted a cash difference cannot lock the day',()=>{
    let s=ready();s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)-500,reason:'Short by five rupees'});
    s=execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:s.shift.approval,approve:true});s=closeDay(s);const d=s.date;
    assert.equal(s.closings[d].cash.countedBy,'cashier');assert.equal(s.closings[d].cash.approvedBy,'manager');
    assert.equal(cbCanLock(s,'manager',d).ok,false);
    s=execute(s,'owner',{type:'NEXT_DAY'});assert.equal(cbCanLock(s,'manager',d).ok,false);assert.equal(cbCanLock(s,'accountant',d).ok,true);});
  test('[CB] F1 legacy: a closing record saved without countedBy can be locked by the Owner only (unless the Owner closed it)',()=>{
    const s=structuredClone(fy);delete s.closings['2026-06-10'].cash.countedBy;
    assert.equal(fy.closings['2026-06-10'].cash.countedBy,'cashier');
    assert.equal(cbCanLock(s,'accountant','2026-06-10').ok,false);assert.match(cbCanLock(s,'accountant','2026-06-10').why,/Owner/);
    assert.equal(cbCanLock(s,'owner','2026-06-10').ok,true);
    assert.throws(()=>execute(s,'accountant',{type:'CB_LOCK',date:'2026-06-10'}),code('FORBIDDEN'));});
  test('[CB] F7: a closed date without a closing record cannot be locked',()=>{
    const s=seed(),d=s.closedDates.find(d=>!s.closings?.[d]);assert.ok(d);
    assert.equal(cbCanLock(s,'manager',d).ok,false);assert.throws(()=>execute(s,'manager',{type:'CB_LOCK',date:d}),code('INVALID_TRANSITION'));});
}
