// DEC-065 · a locked cash-book day blocks changes. Names prefixed [CL].
// A lock normally needs a closed day; for the current open day it is set directly in state so every blocked command can be exercised.
export function register({test,M,assert}){
  const {seed,execute,loadScenario,cbIsLocked,cbSlice,cbDay,cbLockedMessage,cbCanLock,drawer,totals,posSlice}=M;
  const code=c=>e=>e.code===c;
  const blocked=d=>e=>e.code==='DAY_CLOSED'&&e.message.includes(d)&&/Owner must reopen/.test(e.message);
  const lockToday=s=>{const c=structuredClone(s),x=cbSlice(c);c.cashbookX={locks:{...x.locks,[c.date]:{lockedBy:'accountant',lockedAt:c.clock,log:[]}},history:x.history};return c;};
  const reopen=(s,d)=>execute(s,'owner',{type:'CB_REOPEN',date:d,reason:'Fix a wrong entry'});
  const fy=loadScenario('FY26');
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);

  test('[CL] cbIsLocked reads the lock slice; old saves and missing dates are unlocked; message names the date',()=>{
    const s=seed();assert.equal(cbIsLocked(s,s.date),false);assert.equal(cbIsLocked(lockToday(s),s.date),true);assert.equal(cbIsLocked(s,undefined),false);
    const old=structuredClone(s);delete old.cashbookX;assert.equal(cbIsLocked(old,old.date),false);
    assert.match(cbLockedMessage('2026-06-10','a correction'),/2026-06-10.*Owner must reopen/);});

  test('[CL] closed-date correction: request and approval refused on a locked day, rejection allowed, all allowed after Owner reopen',()=>{
    const b=fy.bookings.find(b=>['In house','Confirmed'].includes(b.status)),d='2026-06-10',cmd={type:'CORRECTION_ADJUST',id:b.id,date:d,amount:-1000,reason:'Rate error'};
    const locked=execute(fy,'accountant',{type:'CB_LOCK',date:d});
    assert.throws(()=>execute(locked,'manager',cmd),blocked(d));
    let u=execute(fy,'manager',cmd);const apr=u.approvals[u.approvals.length-1].id;
    u=execute(u,'accountant',{type:'CB_LOCK',date:d});
    assert.throws(()=>execute(u,'owner',{type:'CORRECTION_ADJUST_DECIDE',id:apr,approve:true}),blocked(d));
    assert.equal(execute(u,'owner',{type:'CORRECTION_ADJUST_DECIDE',id:apr,approve:false}).approvals.find(a=>a.id===apr).status,'Rejected');
    const linesBefore=bk(u,b.id).lines.length;assert.equal(bk(u,b.id).lines.length,linesBefore);
    u=execute(reopen(u,d),'owner',{type:'CORRECTION_ADJUST_DECIDE',id:apr,approve:true});
    assert.equal(u.approvals.find(a=>a.id===apr).status,'Approved');assert.equal(bk(u,b.id).lines.length,linesBefore+1);
    assert.doesNotThrow(()=>execute(reopen(locked,d),'manager',cmd));});

  test('[CL] a correction for an unlocked closed date and non-cash folio changes are unaffected by another locked day',()=>{
    const b=fy.bookings.find(b=>['In house','Confirmed'].includes(b.status));
    const s=execute(fy,'accountant',{type:'CB_LOCK',date:'2026-06-10'});
    assert.doesNotThrow(()=>execute(s,'manager',{type:'CORRECTION_ADJUST',id:b.id,date:'2026-06-11',amount:-1000,reason:'Other day'}));
    const t=seed(),id=t.bookings.find(x=>x.status==='In house').id,L=lockToday(t);
    assert.doesNotThrow(()=>execute(L,'owner',{type:'CHARGE',id,label:'Laundry',amount:30000,category:'Laundry'}));});

  test('[CL] expense reversal and approval dated on a locked day are refused; allowed after reopen; ledger figures unchanged',()=>{
    let s=seed();s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    const L=lockToday(s),before=JSON.stringify(L.expenses),drawerBefore=drawer(L);
    assert.throws(()=>execute(L,'owner',{type:'EXPENSE_REVERSE',id:'EXP-102',reason:'Wrong'}),blocked(s.date));
    assert.equal(JSON.stringify(L.expenses),before);assert.equal(drawer(L),drawerBefore);
    const r=execute(reopen(L,s.date),'owner',{type:'EXPENSE_REVERSE',id:'EXP-102',reason:'Wrong'});
    assert.equal(r.expenses.find(e=>e.id==='EXP-102').status,'Reversed');
    const p=seed(),pend=p.expenses.find(e=>e.status==='Pending'&&e.actor!=='owner');assert.ok(pend);
    assert.throws(()=>execute(lockToday(p),'owner',{type:'EXPENSE_APPROVE',id:pend.id,approve:true}),blocked(p.date));
    assert.doesNotThrow(()=>execute(reopen(lockToday(p),p.date),'owner',{type:'EXPENSE_APPROVE',id:pend.id,approve:true}));});

  test('[CL] refund: request and execution against a receipt dated on a locked day are refused; allowed after reopen',()=>{
    const s0=seed(),id='HCC-1041',rid='seed-pay-0',req={type:'REFUND_REQUEST',id,receiptId:rid,amount:50000,reason:'Goodwill'};
    const rdate=bk(s0,id).receipts.find(r=>r.id===rid).date;assert.equal(rdate,s0.date);
    assert.throws(()=>execute(lockToday(s0),'reception',req),blocked(rdate));
    let s=execute(s0,'reception',req);const a=s.approvals[s.approvals.length-1];s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
    const L=lockToday(s),paid=totals(bk(L,id)).paid;
    assert.throws(()=>execute(L,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id}),blocked(rdate));
    assert.equal(totals(bk(L,id)).paid,paid);assert.equal(L.approvals.find(x=>x.id===a.id).status,'Approved');
    const done=execute(reopen(L,rdate),'cashier',{type:'REFUND_EXECUTE',approvalId:a.id});assert.equal(done.approvals.find(x=>x.id===a.id).status,'Executed');});

  test('[CL] deposit return refused when the deposit was taken on a locked day; new payments and deposits on the open day unaffected',()=>{
    let s=seed();const id=s.bookings.find(b=>b.status==='In house').id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:5000,method:'Cash',kind:'deposit'});
    const ret={type:'PAYMENT',id,amount:5000,method:'Cash',kind:'deposit-return'};
    assert.throws(()=>execute(lockToday(s),'cashier',ret),blocked(s.date));
    assert.doesNotThrow(()=>execute(reopen(lockToday(s),s.date),'cashier',ret));
    // the open day is not locked: the same return and a fresh payment work
    assert.doesNotThrow(()=>execute(s,'cashier',ret));assert.doesNotThrow(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:1000,method:'Cash',kind:'payment'}));});

  test('[CL] POS: voiding a cash bill settled on a locked day is refused (direct and via approval); allowed after reopen',()=>{
    let s=seed();s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T3',covers:2});
    const id=posSlice(s).orders[posSlice(s).orders.length-1].id;
    s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-dal',qty:1});s=execute(s,'cashier',{type:'POS_KOT_SEND',id});
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'});
    const L=lockToday(s),v={type:'POS_VOID_BILL',id,reason:'Wrong table'},drawerBefore=drawer(L);
    assert.throws(()=>execute(L,'cashier',v),blocked(s.date));assert.equal(drawer(L),drawerBefore);
    const done=execute(reopen(L,s.date),'cashier',v);assert.equal(posSlice(done).orders.find(o=>o.id===id).status,'Void');
    // unlocked current day: the void works as before
    assert.doesNotThrow(()=>execute(s,'cashier',v));});

  test('[CL] cash-difference decision for a locked day is refused; allowed after reopen',()=>{
    let s=seed();s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)-500,reason:'Short by five rupees'});
    const apr=s.approvals.find(a=>a.kind==='cash-difference'&&a.status==='Pending').id,L=lockToday(s);
    assert.throws(()=>execute(L,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:apr,approve:true}),blocked(s.date));
    assert.equal(L.approvals.find(a=>a.id===apr).status,'Pending');
    assert.doesNotThrow(()=>execute(reopen(L,s.date),'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:apr,approve:true}));
    assert.doesNotThrow(()=>execute(s,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:apr,approve:true}));});

  test('[CL] reopen is audited with its reason, relock needs a non-counter checker again (F1), cash book figures unchanged throughout',()=>{
    const d='2026-06-10',fig=s=>JSON.stringify({...cbDay(s,d),locked:undefined});
    let s=execute(fy,'accountant',{type:'CB_LOCK',date:d});const f0=fig(fy);assert.equal(fig(s),f0);
    s=reopen(s,d);assert.equal(cbIsLocked(s,d),false);assert.equal(fig(s),f0);
    assert.ok(s.audit.some(a=>/reopened/.test(a.detail)&&/Fix a wrong entry/.test(a.detail)));
    assert.equal(cbSlice(s).history[d].length,2);
    // the closer of the historic day still cannot relock it
    assert.equal(cbCanLock(s,'manager',d).ok,false);assert.throws(()=>execute(s,'manager',{type:'CB_LOCK',date:d}),code('FORBIDDEN'));
    s=execute(s,'accountant',{type:'CB_LOCK',date:d});assert.equal(cbIsLocked(s,d),true);assert.equal(fig(s),f0);});
}
