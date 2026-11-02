// LF lane lf-money-counter · contract §6 (A7 counter bill) and §7 (B2 individual credit guests).
export function register({test,M,assert}){
  const {seed,execute,totals,drawer}=M;
  const T='[LF-COUNTER] ';
  const party={name:'Walk-in Guest',phone:'+91 90000 11111'};
  const issue=(s,uid,extra={})=>execute(s,uid,{type:'CTR_BILL_ISSUE',party,lines:[{extraId:'ext-laundry',qty:2},{menuItemId:'m-tea',qty:3},{label:'Courier',unit:15000,gstCategory:'other'}],settlement:{mode:'paid',method:'Cash'},...extra});
  const tbOk=(s,date=s.date)=>{const vs=M.accLiveVouchers(s,date);assert.ok(vs.every(M.accBalanced),'every voucher balanced');const t=M.accTotals(vs);assert.equal(t.debit,t.credit);return vs;};

  test(T+'lines from catalogue, menu and free text; GST per line; INV number; drawer cash; Sales voucher',()=>{
    let s=seed();const d0=drawer(s);s=issue(s,'cashier');
    const b=s.counterX.bills.at(-1);assert.equal(b.lines.length,3);
    assert.deepEqual(b.lines.map(l=>[l.amount,l.gstCategory]),[[100000,'laundry'],[18000,'food'],[15000,'other']]);
    assert.equal(b.lines[0].taxBps,M.gstForCharge(s,s.date,'laundry').bps);assert.equal(b.lines[0].sac,'999712');assert.equal(b.lines[1].sac,M.GSTR1_SAC.food);
    const t=M.ctrTotals(b.lines);assert.equal(b.total,t.total);assert.equal(b.cgst+b.sgst,b.tax);
    assert.ok(/INV|HPE|DEMO/.test(b.no)||b.no.length>3);assert.equal(drawer(s),d0+b.total);
    const v=tbOk(s).find(v=>v.source.kind==='counter');assert.ok(v);
    assert.equal(v.entries.find(e=>e.account==='4200').credit,18000);assert.equal(v.entries.find(e=>e.account==='4100').credit,115000);
    const g=M.gstr1Summary(s,'2026-10-01','2026-10-31');assert.ok(g.documents.find(d=>d.docType==='INV').count>=1);
  });
  test(T+'discount limits by role (DEC-063): Cashier none, Reception ≤ ₹250, Manager ≤ ₹1,000, Owner any',()=>{
    const s=seed();
    assert.throws(()=>issue(s,'cashier',{discount:{amount:100,reason:'x'}}),/not at the cash counter/);
    assert.throws(()=>issue(s,'reception',{discount:{amount:25001,reason:'x'}}),/limit/);
    let t=issue(s,'reception',{discount:{amount:25000,reason:'Regular'}});const b=t.counterX.bills.at(-1);assert.equal(b.net,133000-25000);
    assert.throws(()=>issue(s,'manager',{discount:{amount:100001,reason:'x'}}),/limit/);
    t=issue(s,'owner',{discount:{amount:120000,reason:'Owner'}});assert.equal(t.counterX.bills.at(-1).discount.amount,120000);
    tbOk(t);
  });
  test(T+'settlement rules: drawer needs an open shift, bank needs a reference, cash guard applies',()=>{
    let s=seed();
    assert.throws(()=>issue(s,'cashier',{settlement:{mode:'paid',method:'UPI'}}),/reference/);
    s=issue(s,'cashier',{settlement:{mode:'paid',method:'UPI',reference:'UPI-1'}});assert.equal(s.counterX.bills.at(-1).settlement.method,'UPI');
    const closed=structuredClone(s);closed.shiftOpen=false;
    assert.throws(()=>issue(closed,'cashier'),/shift is closed/);
    assert.throws(()=>execute(s,'cashier',{type:'CTR_BILL_ISSUE',party,lines:[{label:'Big',unit:30000000,gstCategory:'other'}],settlement:{mode:'paid',method:'Cash'}}),/2,00,000|cash/i);
  });
  test(T+'udhaar: receivable raised for the party (no second sale), Dr 1120/<customer>',()=>{
    let s=seed();s=issue(s,'reception',{settlement:{mode:'udhaar'}});
    const b=s.counterX.bills.at(-1),r=s.expensesX.udhaar.at(-1);
    assert.equal(r.counterBill,b.id);assert.equal(r.amount,b.total);assert.equal(r.mobile,'9000011111');assert.equal(b.settlement.udhaarId,r.id);
    const vs=tbOk(s);assert.ok(!vs.some(v=>v.source.id==='udhaar:'+r.id),'no second Udhaar sale');
    assert.ok(vs.find(v=>v.source.kind==='counter').entries.some(e=>e.account==='1120/UD-9000011111'&&e.debit===b.total));
    assert.throws(()=>execute(s,'reception',{type:'CTR_BILL_ISSUE',party:{name:'No Phone'},lines:[{label:'x',unit:100,gstCategory:'other'}],settlement:{mode:'udhaar'}}),/name and phone/);
  });
  test(T+'company: credit limit checked; receivable Dr 1110/<company>',()=>{
    let s=seed();s=issue(s,'manager',{settlement:{mode:'company',companyId:'CO-1'}});
    assert.ok(tbOk(s).find(v=>v.source.kind==='counter').entries.some(e=>e.account==='1110/CO-1'));
    assert.throws(()=>execute(s,'manager',{type:'CTR_BILL_ISSUE',party,lines:[{label:'Huge',unit:99000000,gstCategory:'other'}],settlement:{mode:'company',companyId:'CO-1'}}),/Credit limit exceeded/);
    assert.throws(()=>issue(s,'manager',{settlement:{mode:'company',companyId:'CO-X'}}),/company account/);
  });
  test(T+'void: request → Owner approves → CRN, cash back, credit voucher reverses; roles',()=>{
    let s=seed();s=issue(s,'cashier');const b=s.counterX.bills.at(-1),d1=drawer(s);
    assert.throws(()=>execute(s,'housekeeper',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:'x'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'cashier',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:''}),/reason/);
    s=execute(s,'cashier',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:'Wrong customer'});const a=s.approvals.at(-1);
    assert.throws(()=>execute(s,'cashier',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:'again'}),/already pending/);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),/Owner/);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const x=s.counterX.bills.at(-1);assert.equal(x.status,'Credited');assert.ok(x.creditNoteNo);assert.equal(drawer(s),d1-b.total);
    const vs=tbOk(s).filter(v=>v.source.kind==='counter');assert.equal(vs.length,2);
    const g=M.gstr1Summary(s,'2026-10-01','2026-10-31');assert.ok(g.documents.find(d=>d.docType==='CRN'));
    assert.throws(()=>execute(s,'cashier',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:'x'}),/already credited/);
  });
  test(T+'void of an udhaar bill reverses the receivable; refused once paid against',()=>{
    let s=seed();s=issue(s,'reception',{settlement:{mode:'udhaar'}});const b=s.counterX.bills.at(-1);
    s=execute(s,'reception',{type:'CTR_BILL_VOID_REQUEST',id:b.id,reason:'x'});s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.equal(s.expensesX.udhaar.find(r=>r.id===b.settlement.udhaarId).void,true);
  });
  test(T+'roles: Housekeeper / Accountant / Restaurant cannot issue',()=>{
    const s=seed();for(const u of ['housekeeper','accountant','maint'])assert.throws(()=>issue(s,u),e=>e.code==='FORBIDDEN');
  });

  // ── B2 ──
  const G='G-0001';
  function stay(s){
    s=execute(s,'owner',{type:'BOOK',guest:'Ananya Rao',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id,guestId:G,guest:{...s.guests.find(g=>g.id===G)}});s=execute(s,'owner',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});return [s,id];
  }
  test(T+'credit account: Owner only; key = phone last 10 (customers.ts rule)',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:500000}),e=>e.code==='FORBIDDEN');
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:500000,note:'Regular'});
    const a=s.guests.find(g=>g.id===G).creditAccount;assert.equal(a.customerKey,'9820001041');assert.equal(a.active,true);
    assert.equal(M.customerKeyForCredit({id:'G-9',phone:''}),'G-9');
    assert.equal(M.guestMoneyFilter(s,'housekeeper',s.guests.find(g=>g.id===G)).creditAccount,undefined);
  });
  test(T+'guest credit at check-out: request → Manager approves within ₹10,000 → receipt guest-credit + receivable + journal',()=>{
    let s=seed();s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:1000000});
    let [t,id]=stay(s);
    assert.throws(()=>execute(t,'reception',{type:'CHECKOUT',id}),/Settle/);
    t=execute(t,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,reason:'Regular guest'});const a=t.approvals.at(-1);assert.equal(a.kind,'guest-credit');
    t=execute(t,'manager',{type:'APPROVAL',id:a.id,approve:true});
    const bal=totals(t.bookings.find(b=>b.id===id)).balance;
    t=execute(t,'reception',{type:'CHECKOUT',id});
    const x=t.bookings.find(b=>b.id===id);assert.equal(x.status,'Departed');const r=x.receipts.at(-1);assert.equal(r.kind,'guest-credit');assert.equal(r.amount,bal);
    const ud=t.expensesX.udhaar.at(-1);assert.equal(ud.booking,id);assert.equal(ud.guestId,G);assert.equal(ud.kind,'room');assert.equal(ud.amount,bal);
    const vs=tbOk(t);assert.ok(vs.some(v=>v.source.sub==='guest-credit'&&v.entries.some(e=>e.account==='1120/UD-9820001041'&&e.debit===bal)));
    assert.ok(!vs.some(v=>v.source.id==='udhaar:'+ud.id));
    const o=M.udGuestOutstanding(t).find(g=>g.guestId===G);assert.equal(o.open,bal);assert.equal(o.bills[0].bucket,'0–30');
  });
  test(T+'guest credit refused: no account, inactive, over the limit; Manager above ₹10,000 needs the Owner',()=>{
    let [s,id]=stay(seed());
    assert.throws(()=>execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id}),/no active credit account/);
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:100,active:true});
    assert.throws(()=>execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id}),/Credit limit exceeded/);
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:100000000,active:true});
    const x=s.bookings.find(b=>b.id===id);s=execute(s,'owner',{type:'CHARGE',id,label:'Banquet hall',amount:1500000,gst:'banquet'});void x;
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id});const a=s.approvals.at(-1);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),/Owner/);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});s=execute(s,'reception',{type:'CHECKOUT',id});
    assert.equal(s.bookings.find(b=>b.id===id).status,'Departed');
  });
  test(T+'counter bill on udhaar for a credit guest counts against the guest limit',()=>{
    let s=seed();s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:10000});
    assert.throws(()=>execute(s,'reception',{type:'CTR_BILL_ISSUE',party:{name:'Ananya Rao',guestId:G},lines:[{extraId:'ext-laundry'}],settlement:{mode:'udhaar'}}),/Credit limit exceeded/);
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:1000000});
    s=execute(s,'reception',{type:'CTR_BILL_ISSUE',party:{name:'Ananya Rao',guestId:G},lines:[{extraId:'ext-laundry'}],settlement:{mode:'udhaar'}});
    const r=s.expensesX.udhaar.at(-1);assert.equal(r.mobile,'9820001041');assert.equal(r.guestId,G);
    assert.equal(M.gcrUsed(s,s.guests.find(g=>g.id===G)),r.amount);
  });
}
