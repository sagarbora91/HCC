// DEC-076 (audit A05-01, A03a-05) · company credit limits: Owner only, enforced where company billing happens. Mock rules = production
// (08 Production CompanyCredit / CompanyCreditRules: same exposure definition, same refusal wording). Names prefixed [CREDIT].
// BASE figures used below (worked by hand):
//   CO-1 Deccan Pharma: limit ₹1,50,000; opening receivable ₹62,000 + its GB-1 members HCC-1044 and HCC-1048 (in house 23–26 Oct at ₹2,000,
//     the 23 Oct night posted at 5 %, ₹2,000 paid): each 3 × ₹2,100 − ₹2,000 = ₹4,300 → exposure ₹70,600. HCC-1044's folio balance ₹100.
//   CO-2 Godavari Agro: limit ₹1,00,000; opening receivable ₹88,000, no stays → ₹88,000 used, ₹12,000 remaining.
//   CO-3 Sahyadri Infra: limit ₹2,00,000, nothing used.
const LIMIT_TAIL=' Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).';
export function register({test,M,assert}){
  const {seed,execute,loadScenario,totals}=M;
  const co=(s,id)=>M.grpSlice(s).companies.find(c=>c.id===id);
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const fails=(fn)=>{try{fn();}catch(e){return e;}assert.fail('expected a refusal');};

  test('[CREDIT] exposure worked by hand: receivable + open stays, remaining, and a company payment frees credit',()=>{
    let s=seed();
    const c1=M.grpCredit(s,co(s,'CO-1'));
    assert.deepEqual([c1.exposure,c1.receivable,c1.stays,c1.available],[7060000,6200000,860000,7940000]);
    assert.equal(M.grpAccountOf(s,bk(s,'HCC-1044')).id,'CO-1'); // a member of the bill-to-company block GB-1
    assert.equal(M.grpAccountOf(s,bk(s,'HCC-1041')),undefined);
    // CO-1 pays ₹10,000 of its opening receivable by bank transfer: receivable ₹52,000, exposure ₹60,600, remaining ₹89,400.
    s=execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-1',amount:1000000,method:'Bank transfer',reference:'NEFT-DEMO-1',invoice:'demo/2627/0007'});
    const p=M.grpSlice(s).payments.at(-1);
    assert.deepEqual([p.company,p.amount,p.method,p.invoice,p.date],['CO-1',1000000,'Bank transfer','DEMO/2627/0007','2026-10-24']);
    const c2=M.grpCredit(s,co(s,'CO-1'));
    assert.deepEqual([c2.exposure,c2.receivable,c2.available],[6060000,5200000,8940000]);
    assert.match(s.audit[0].detail,/receivable ₹62,000 → ₹52,000/);
  });

  test('[CREDIT] company payment: non-cash, at most what the company owes, payments roles only',()=>{
    const s=seed();
    assert.equal(fails(()=>execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:100,method:'Cash',reference:'x'})).code,'VALIDATION');
    assert.equal(fails(()=>execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:100,method:'UPI',reference:''})).code,'VALIDATION');
    const over=fails(()=>execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:8800001,method:'UPI',reference:'UPI-1'}));
    assert.equal(over.message,'Payment cannot exceed what Godavari Agro Inputs (fictional) owes on company credit (₹88,000).');
    const none=fails(()=>execute(s,'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-3',amount:100,method:'UPI',reference:'UPI-1'}));
    assert.equal([none.code,none.message].join(' · '),'INVALID_TRANSITION · Sahyadri Infra Projects (fictional) owes nothing on company credit.');
    for(const u of ['housekeeper','accountant','supervisor'])assert.equal(fails(()=>execute(s,u,{type:'GRP_COMPANY_PAYMENT',company:'CO-2',amount:100,method:'UPI',reference:'UPI-1'})).code,'FORBIDDEN',u);
  });

  test('[CREDIT] only the Owner decides a credit-limit request; the Manager requests (A05-01)',()=>{
    let s=seed();
    // Before DEC-076 the Manager approved GLR-1 (Reception's request for CO-2, ₹1,00,000 → ₹1,50,000).
    const e=fails(()=>execute(s,'manager',{type:'GRP_LIMIT_DECIDE',id:'GLR-1',approve:true}));
    assert.equal([e.code,e.message].join(' · '),'FORBIDDEN · Only the Owner approves or rejects a company credit-limit request (DEC-076).');
    s=execute(s,'manager',{type:'GRP_LIMIT_SET',company:'CO-3',limit:25000000,reason:'Annual contract renewal'});
    assert.equal(co(s,'CO-3').creditLimit,20000000);assert.equal(M.grpSlice(s).requests.at(-1).status,'Pending');
    s=execute(s,'owner',{type:'GRP_LIMIT_DECIDE',id:'GLR-1',approve:true});assert.equal(co(s,'CO-2').creditLimit,15000000);
    s=execute(s,'owner',{type:'GRP_LIMIT_DECIDE',id:M.grpSlice(s).requests.at(-1).id,approve:false,note:'Next quarter'});assert.equal(co(s,'CO-3').creditLimit,20000000);
  });

  test('[CREDIT] company credit beyond the remaining credit is refused with limit, used and remaining; a payment frees it',()=>{
    let s=seed();
    // The Owner lowers CO-1 to ₹66,350. HCC-1044's ₹100 balance: used = ₹70,600 − ₹4,300 (the stay itself) = ₹66,300; 66,300 + 100 > 66,350.
    s=execute(s,'owner',{type:'GRP_LIMIT_SET',company:'CO-1',limit:6635000});
    const e=fails(()=>execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id:'HCC-1044'}));
    assert.equal(e.code,'INVALID_TRANSITION');
    assert.equal(e.message,'Credit limit exceeded for Deccan Pharma Distributors (fictional): limit ₹66,350, used ₹66,300, remaining ₹50; this stay on company credit needs ₹100.'+LIMIT_TAIL);
    // CO-1 pays ₹100: used ₹66,200; 66,200 + 100 = 66,300 ≤ 66,350 → the request goes through, the approval re-checks and passes.
    s=execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-1',amount:10000,method:'UPI',reference:'UPI-CO1-1'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id:'HCC-1044'});
    const a=s.approvals.at(-1);assert.deepEqual([a.kind,a.amount,a.meta.company],['company-credit',10000,'Deccan Pharma Distributors (fictional)']);
    // A ₹100 laundry charge on HCC-1048 (18 %: ₹118) raises the rest to ₹66,318: the approval is refused (re-check), remaining ₹32.
    const late=execute(s,'reception',{type:'CHARGE',id:'HCC-1048',label:'Laundry',amount:10000});
    assert.match(fails(()=>execute(late,'manager',{type:'APPROVAL',id:a.id,approve:true})).message,/used ₹66,318, remaining ₹32; this stay on company credit needs ₹100\./);
    assert.equal(execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}).approvals.at(-1).status,'Approved');
  });

  test('[CREDIT] company credit needs a booking billed to a company account; BOOKING_COMPANY links it within the limit',()=>{
    let s=seed();
    // HCC-1041 (in house, ₹100 due, no company) is billed to CO-3: the account's name and GSTIN go on the booking.
    s=execute(s,'owner',{type:'BOOKING_COMPANY',id:'HCC-1041',account:'CO-3'});
    assert.deepEqual(bk(s,'HCC-1041').company,{name:'Sahyadri Infra Projects (fictional)',gstin:'27ZZFIC0003F1Z3',account:'CO-3'});
    // Its estimate 3 × ₹2,100 − ₹2,000 = ₹4,300 is now CO-3's exposure.
    assert.equal(M.grpCredit(s,co(s,'CO-3')).exposure,430000);
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id:'HCC-1041'});assert.equal(s.approvals.at(-1).amount,10000);
    const t=seed();
    const free=execute(t,'owner',{type:'BOOK',guest:'Invoice Only Guest',room:'105',arrival:'2026-11-10',departure:'2026-11-12',people:1,company:{name:'Godavari Agro Inputs (fictional)'}});
    assert.equal(free.bookings.at(-1).company.account,undefined);
    assert.equal(M.grpCredit(free,co(free,'CO-2')).exposure,8800000); // a typed company name is invoice details, not billed to the account
    // S4's stay with the account link removed (invoice details only): company credit is refused until it is billed to an account again.
    const s4=execute(loadScenario('S4'),'reception',{type:'BOOKING_COMPANY',id:'HCC-1052',account:''});
    const r=fails(()=>execute(s4,'reception',{type:'COMPANY_CREDIT_REQUEST',id:'HCC-1052'}));
    assert.equal([r.code,r.message].join(' · '),'INVALID_TRANSITION · This booking is not billed to a company account. Link the booking to the company account before requesting company credit.');
    // Linking HCC-1042 (₹4,300) to CO-2 (₹12,000 left) fits; unlinking frees it again.
    let u=execute(t,'reception',{type:'BOOKING_COMPANY',id:'HCC-1042',account:'CO-2'});
    assert.equal(M.grpCredit(u,co(u,'CO-2')).exposure,9230000);
    u=execute(u,'reception',{type:'BOOKING_COMPANY',id:'HCC-1042',account:''});
    assert.equal(M.grpCredit(u,co(u,'CO-2')).exposure,8800000);assert.equal(bk(u,'HCC-1042').company.account,undefined);
    assert.equal(fails(()=>execute(t,'housekeeper',{type:'BOOKING_COMPANY',id:'HCC-1042',account:'CO-2'})).code,'FORBIDDEN');
  });

  test('[CREDIT] a booking billed to a company account is a guarantee: refused beyond the limit, amendments that raise it too',()=>{
    let s=seed();
    // Deluxe at ₹3,000 + 5 % = ₹3,150 a night; 10–12 Nov = ₹6,300. CO-2 has ₹12,000 left.
    s=execute(s,'reception',{type:'BOOK',guest:'Dealer One (fictional)',room:'103',arrival:'2026-11-10',departure:'2026-11-12',people:1,company:{account:'CO-2'}});
    const first=s.bookings.at(-1);
    assert.deepEqual(first.company,{name:'Godavari Agro Inputs (fictional)',gstin:'27ZZFIC0002F1Z4',account:'CO-2'});
    assert.equal(M.grpCredit(s,co(s,'CO-2')).exposure,9430000);
    // A second ₹6,300 booking: used ₹94,300, remaining ₹5,700 → refused, nothing booked.
    const e=fails(()=>execute(s,'reception',{type:'BOOK',guest:'Dealer Two (fictional)',room:'104',arrival:'2026-11-10',departure:'2026-11-12',people:1,company:{account:'CO-2'}}));
    assert.equal(e.message,'Credit limit exceeded for Godavari Agro Inputs (fictional): limit ₹1,00,000, used ₹94,300, remaining ₹5,700; this booking needs ₹6,300.'+LIMIT_TAIL);
    // Moving the first booking to 10–15 Nov (5 nights = ₹15,750): used ₹88,000 + 15,750 > ₹1,00,000 → refused; one night less always passes.
    assert.match(fails(()=>execute(s,'reception',{type:'CHANGE_DATES',id:first.id,room:'103',arrival:'2026-11-10',departure:'2026-11-15'})).message,/used ₹88,000, remaining ₹12,000; this booking needs ₹15,750\./);
    s=execute(s,'reception',{type:'CHANGE_DATES',id:first.id,room:'103',arrival:'2026-11-10',departure:'2026-11-11'});
    assert.equal(M.grpCredit(s,co(s,'CO-2')).exposure,9115000);
    // A limit of 0 is no credit: CO-4-like zero accounts refuse even ₹1.
    const z=execute(seed(),'owner',{type:'GRP_LIMIT_SET',company:'CO-3',limit:0});
    assert.match(fails(()=>execute(z,'reception',{type:'BOOK',guest:'Site Engineer (fictional)',room:'105',arrival:'2026-11-10',departure:'2026-11-11',people:1,company:{account:'CO-3'}})).message,
      /^Credit limit exceeded for Sahyadri Infra Projects \(fictional\): limit ₹0 \(no credit\), used ₹0, remaining ₹0; this booking needs ₹2,100\./);
  });

  test('[CREDIT] S4 checkout: company credit becomes the account receivable; re-checked at checkout; the company pays it',()=>{
    let s=loadScenario('S4');const id='HCC-1052';
    assert.equal(M.grpCredit(s,co(s,'CO-4')).exposure,630000); // ₹6,000 + 5 % = ₹6,300 due, nothing paid
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    // The Owner lowers CO-4 to ₹5,000 before checkout: the checkout's re-check refuses the credit (nothing changes).
    const low=execute(s,'owner',{type:'GRP_LIMIT_SET',company:'CO-4',limit:500000});
    assert.match(fails(()=>execute(low,'reception',{type:'CHECKOUT',id})).message,/limit ₹5,000, used ₹0, remaining ₹5,000; this stay on company credit needs ₹6,300\./);
    s=execute(s,'reception',{type:'CHECKOUT',id});
    assert.equal(bk(s,id).status,'Departed');assert.equal(totals(bk(s,id)).balance,0);
    const c=M.grpCredit(s,co(s,'CO-4'));assert.deepEqual([c.exposure,c.receivable,c.available],[630000,630000,4370000]);
    s=execute(s,'cashier',{type:'GRP_COMPANY_PAYMENT',company:'CO-4',amount:630000,method:'Bank transfer',reference:'NEFT-S4',invoice:bk(s,id).invoiceNo});
    assert.deepEqual([M.grpCredit(s,co(s,'CO-4')).exposure,M.grpCredit(s,co(s,'CO-4')).available],[0,5000000]);
  });

  test('[CREDIT] rooming list beyond the remaining credit is refused for every role (no override, DEC-076)',()=>{
    const s=seed(),rows=n=>Array.from({length:n},(_,i)=>({guest:'Dealer Guest '+(i+1),type:'Classic',people:1}));
    // Classic at the ₹1,800 block rate, 30 Oct–1 Nov: 2 × (₹1,800 + 5 %) = ₹3,780 a guest. 3 guests: 88,000 + 11,340 = ₹99,340 ≤ ₹1,00,000.
    assert.ok(execute(s,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(3)}));
    for(const u of ['reception','manager','owner']){
      const e=fails(()=>execute(s,u,{type:'GRP_ROOMING',block:'GB-2',rows:rows(4),override:true,overrideReason:'Advance cheque received (simulated)'}));
      assert.equal(e.code,'INVALID_TRANSITION',u);
      assert.equal(e.message,'Credit limit exceeded for Godavari Agro Inputs (fictional): limit ₹1,00,000, used ₹88,000, remaining ₹12,000; this rooming list needs ₹15,120.'+LIMIT_TAIL);
    }
    const n=execute(execute(s,'owner',{type:'GRP_LIMIT_SET',company:'CO-2',limit:12000000}),'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(4)});
    assert.equal(M.grpSlice(n).overrides.length,0);assert.equal(M.grpCredit(n,co(n,'CO-2')).exposure,8800000+4*378000);
  });

  test('[CREDIT] refusal text matches the server (CompanyCreditRules.Message)',()=>{
    const c={name:'Deccan Pharma',creditLimit:440000};
    assert.equal(M.grpCreditRefusal(c,430000,10000,'stay on company credit'),undefined); // exactly the limit passes
    assert.equal(M.grpCreditRefusal(c,430000,10001,'stay on company credit'),
      'Credit limit exceeded for Deccan Pharma: limit ₹4,400, used ₹4,300, remaining ₹100; this stay on company credit needs ₹100.01.'+LIMIT_TAIL);
    assert.equal(M.grpCreditRefusal(c,900000,0,'booking'),undefined); // nothing added: never refused
  });
}
