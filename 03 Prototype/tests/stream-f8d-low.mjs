// F8D-LOW · the LOW split-folio money-review items L2, L3, L4, L5, L9 on the prototype (05 Delivery Evidence/F8/F8-INT-DONE.md open items;
// review text F8-MONEY-REVIEW.md). L6 is a server-only item (Hcc.Billing.Tests). Fictional data on the BASE fixture; money in paise.
export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[F8D-LOW] ';
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const withLimit=(s,co,limit)=>({...s,groupsX:{...s.groupsX,companies:s.groupsX.companies.map(c=>c.id===co?{...c,creditLimit:limit}:c)}});
  const msgOf=fn=>{try{fn();}catch(e){return e.message;}return 'none';};
  /** In-house stay in room 105 for 3 nights with a company folio B (routes room), nights posted; folio A holds the extras. */
  function split(limit=5000000){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:limit});
    const co=s.groupsX.companies.at(-1).id;
    s=execute(s,'owner',{type:'BOOK',guest:'F8D Low Guest',room:'105',arrival:s.date,departure:addDays(s.date,3),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    return {s,id,co};
  }

  // ── L2: the departed-stay refund check counts only that folio's outstanding refunds ───────────────────────────
  test(T+'L2: a refund request on folio B does not use up folio A\'s credit after check-out (and each folio still caps its own)',()=>{
    let {s,id}=split();
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    let b=bk(s,id);
    assert.equal(b.lines.at(-1).folio,'A','food on the guest folio A');
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(b,'A')).balance,method:'UPI',reference:'UPI-L2-A',folio:'A'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'B')).balance,method:'Bank transfer',reference:'NEFT-L2-B',folio:'B'});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    // a credit note on each folio leaves a credit to refund on each
    s=execute(s,'reception',{type:'CREDIT_NOTE',id,folio:'A',amount:20000,reason:'Food complaint'});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'reception',{type:'CREDIT_NOTE',id,folio:'B',amount:50000,reason:'Rate agreed lower'});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    b=bk(s,id);
    const credA=-totals(M.sfView(b,'A')).balance,credB=-totals(M.sfView(b,'B')).balance;
    assert.deepEqual([credA,credB],[21000,52500],'A: ₹200 + 5 % food; B: ₹500 + 5 % room');
    const rA=b.receipts.find(r=>r.folio==='A'&&r.kind==='payment'),rB=b.receipts.find(r=>r.folio==='B'&&r.kind==='payment');
    s=execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rB.id,amount:credB,reason:'Return the company credit'});
    // before L2 the B request (₹525) was subtracted from A's ₹210 as well and this was refused
    s=execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rA.id,amount:credA,reason:'Return the guest credit'});
    assert.equal(s.approvals.filter(a=>a.booking===id&&a.kind==='refund'&&a.status==='Pending').length,2);
    // each folio still caps its own outstanding refunds
    assert.match(msgOf(()=>execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rA.id,amount:100,reason:'One more'})),/requires a credit note before a refund/);
    assert.match(msgOf(()=>execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rB.id,amount:100,reason:'One more'})),/requires a credit note before a refund/);
    // approve and execute the A refund: B's pending request does not block it at execution either
    const aA=s.approvals.find(a=>a.booking===id&&a.kind==='refund'&&a.target===rA.id);
    s=execute(s,'manager',{type:'APPROVAL',id:aA.id,approve:true});
    s=execute(s,'owner',{type:'REFUND_EXECUTE',approvalId:aA.id,method:'UPI',reference:'UPI-L2-REF'});
    assert.equal(totals(M.sfView(bk(s,id),'A')).balance,0,'folio A credit returned');
  });

  test(T+'L2: a single-folio stay keeps the per-booking count (I1)',()=>{
    const s=seed();
    const b={id:'X',receipts:[{id:'R1',kind:'payment',amount:1000}],lines:[],folios:undefined};
    const st={...s,approvals:[{id:'P1',booking:'X',kind:'refund',target:'R1',status:'Pending',amount:300,actor:'reception',version:1}]};
    assert.equal(M.folioOutstandingRefunds(st,'X'),300);
    assert.equal(M.folioOutstandingRefunds(st,'X','',b,'B'),300,'not split: the folio argument is ignored');
  });

  // ── L3: one pending request per folio, not per booking ───────────────────────────────────────────────────────
  test(T+'L3: a discount request pending on folio A does not block one on folio B; a second on A is still refused',()=>{
    let {s,id}=split();
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    s=execute(s,'reception',{type:'DISCOUNT',id,folio:'A',amount:30000,reason:'Late food'});
    assert.equal(s.approvals.at(-1).meta.folio,'A');
    s=execute(s,'reception',{type:'DISCOUNT',id,folio:'B',amount:30000,reason:'Noisy room'});
    assert.equal(s.approvals.at(-1).meta.folio,'B','B gets its own request');
    assert.match(msgOf(()=>execute(s,'reception',{type:'DISCOUNT',id,folio:'A',amount:30000,reason:'Again'})),/A discount request is already pending/);
    // the approval is still bound to the stay's version: deciding B makes A's open request stale (request again)
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.find(a=>a.booking===id&&a.meta?.folio==='B').id,approve:true});
    assert.match(msgOf(()=>execute(s,'owner',{type:'APPROVAL',id:s.approvals.find(a=>a.booking===id&&a.meta?.folio==='A'&&a.status==='Pending').id,approve:true})),/changed after this request/);
    // check-out still refuses while any folio has a pending request
    assert.match(msgOf(()=>execute(s,'owner',{type:'CHECKOUT',id})),/pending|Settle folio/);
  });

  test(T+'L3: two Udhaar folios can each have a guest-credit request pending at once',()=>{
    let s=seed();
    const gs=s.guests.filter(g=>g.phone&&String(g.phone).replace(/\D/g,'').length>=10).slice(0,2);
    assert.equal(gs.length,2);
    for(const g of gs)s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:5000000,note:'Regular'});
    s=execute(s,'owner',{type:'BOOK',guest:'F8D Udhaar Guest',room:'105',arrival:s.date,departure:addDays(s.date,2),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:gs[0].id},routes:[],reason:'First credit guest'});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:gs[1].id},routes:[],reason:'Second credit guest'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry',folio:'B'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry',folio:'C'});
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:'B',reason:'Regular'});
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:'C',reason:'Regular'});
    assert.deepEqual(s.approvals.filter(a=>a.booking===id&&a.kind==='guest-credit'&&a.status==='Pending').map(a=>a.meta.folio).sort(),['B','C']);
    assert.match(msgOf(()=>execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:'B',reason:'Again'})),/A guest-credit request is already pending/);
  });

  // ── L4: Udhaar receivable GST metadata from the settled folio ────────────────────────────────────────────────
  test(T+'L4: the guest-credit receivable of an extras-only Udhaar folio records that folio\'s rate (18 % laundry), not the stay\'s room rate',()=>{
    let s=seed();
    const g=s.guests.find(g=>g.phone&&String(g.phone).replace(/\D/g,'').length>=10);
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:5000000,note:'Regular'});
    s=execute(s,'owner',{type:'BOOK',guest:'F8D Udhaar L4',room:'105',arrival:s.date,departure:addDays(s.date,2),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:g.id},routes:['extras'],reason:'Credit guest pays the extras'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'UPI',reference:'UPI-L4-A',folio:'A'});
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:'B',reason:'Regular'});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    const ud=s.expensesX.udhaar.find(r=>r.booking===id);
    assert.ok(ud,'receivable raised');assert.equal(ud.amount,59000);
    assert.equal(ud.gstBps,1800,'the laundry folio\'s one rate');
    assert.ok(bk(s,id).lines.some(l=>l.kind==='room'&&l.taxBps!==1800),'the stay\'s room nights carry another rate');
  });

  test(T+'L4: an Udhaar folio with lines at two rates records 0 (no single rate, SFQ-20); a single-folio stay keeps its room rate (I1)',()=>{
    let s=seed();
    const g=s.guests.find(g=>g.phone&&String(g.phone).replace(/\D/g,'').length>=10);
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:5000000,note:'Regular'});
    s=execute(s,'owner',{type:'BOOK',guest:'F8D Udhaar L4b',room:'105',arrival:s.date,departure:addDays(s.date,2),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:g.id},routes:['extras'],reason:'Credit guest pays the extras'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'UPI',reference:'UPI-L4b-A',folio:'A'});
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:'B',reason:'Regular'});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    assert.equal(s.expensesX.udhaar.find(r=>r.booking===id).gstBps,0);
    // single-folio control: the whole stay on guest credit records the room rate as before
    let c=seed();
    c=execute(c,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:5000000,note:'Regular'});
    c=execute(c,'owner',{type:'BOOK',guest:g.name,guestId:g.id,room:'105',arrival:c.date,departure:addDays(c.date,2),people:1});
    const cid=c.bookings.at(-1).id;
    c=execute(c,'owner',{type:'REGISTER',id:cid});c=execute(c,'owner',{type:'CHECKIN',id:cid});
    c={...c,bookings:c.bookings.map(b=>b.id===cid?{...b,guestId:g.id}:b)}; // the stay guest is the credit guest
    c=execute(c,'owner',{type:'POST_NIGHTS',id:cid});
    c=execute(c,'owner',{type:'CHARGE',id:cid,label:'Laundry',amount:50000,gst:'laundry'});
    c=execute(c,'reception',{type:'GUEST_CREDIT_REQUEST',booking:cid,reason:'Regular'});
    c=execute(c,'manager',{type:'APPROVAL',id:c.approvals.at(-1).id,approve:true});
    c=execute(c,'owner',{type:'CHECKOUT',id:cid});
    const room=bk(c,cid).lines.find(l=>l.kind==='room');
    assert.equal(c.expensesX.udhaar.find(r=>r.booking===cid).gstBps,room.taxBps,'single folio: the first room line\'s rate, unchanged');
  });

  // ── L5: the company-business report uses sfCompanyLines ──────────────────────────────────────────────────────
  const CB='rpt.guests.company-business';
  const cbRow=(s,name,from,to)=>M.runReport(s,'owner',CB,{from,to,company:name},{all:true}).rows.find(r=>r.kind==='row'&&r.cells.company===name);
  test(T+'L5: on a split stay the company-business report counts the company folio only (unchanged by the switch to sfCompanyLines)',()=>{
    let {s,id,co}=split();
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    const b=bk(s,id),name=M.grpCompany(s,co).name;
    const r=cbRow(s,name,s.date,s.date),night=b.lines.find(l=>l.kind==='room'&&l.date===s.date);
    assert.ok(r&&night,'the company row and tonight\'s night');
    assert.equal(night.folio,'B');
    // the food (on A, same date) is not the company's: only tonight's night on B, the report cannot look past the business date
    assert.deepEqual([r.cells.roomNights,r.cells.tariff,r.cells.other,r.cells.total],[1,night.amount,0,night.amount]);
    assert.deepEqual(M.sfCompanyLines(s,b,co).map(l=>l.id),M.sfView(b,'B').lines.map(l=>l.id));
  });
  test(T+'L5: a stay picked up in a bill-to-company block counts for that company (missed by the old local copy)',()=>{
    const s0=seed();
    const blk=M.grpSlice(s0).blocks.find(x=>x.id==='GB-2');
    assert.ok(blk&&blk.billTo==='company'&&blk.company,'BASE block GB-2 is billed to a company');
    const s=structuredClone(execute(s0,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:[{guest:'Block L5 Guest',type:'Classic',people:1}],key:'f8d-l5'}));
    const b=s.bookings.at(-1),name=M.grpCompany(s,blk.company).name;
    assert.equal(b.company?.account,undefined,'linked to the company only through the block');
    b.lines.push({id:'line-l5',label:'Late booking charge',amount:180000,date:s.date,kind:'service',taxBps:1800}); // a crafted posted line on the business date
    const r=cbRow(s,name,s.date,s.date);
    assert.ok(r,'the block company has a row');
    const before=cbRow(structuredClone(execute(s0,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:[{guest:'Block L5 Guest',type:'Classic',people:1}],key:'f8d-l5'})),name,s.date,s.date);
    assert.equal(r.cells.other-(before?.cells.other??0),180000,'the block stay\'s line is counted for the block company');
    assert.deepEqual(M.sfCompanyLines(s,b,blk.company).map(l=>l.id),['line-l5']);
  });

  // ── L9: D7 hook after CHECKOUT does not subtract a Departed folio that grpExposure no longer counts ─────────────
  test(T+'L9: a line added to a Departed stay\'s company folio is checked against the full exposure (the folio is not subtracted twice)',()=>{
    let {s,id,co}=split();
    // settle: A has nothing, B goes on company credit (₹9,450) and becomes the company's receivable
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    const c0=M.grpCompany(s,co),recv=totals(M.sfView(bk(s,id),'B')).total;
    assert.equal(M.grpExposure(s,c0),recv,'exposure = the receivable of folio B; the departed stay itself is not counted again');
    // a crafted later line of ₹1,000 + 18 % on the departed company folio (no command posts one today; the hook is checked directly)
    // another ₹2,000 receivable of the company elsewhere: before L9 used = (that + B's receivable) − B's folio exposure, which hid the new line
    const other=s0=>({...s0,groupsX:{...s0.groupsX,companies:s0.groupsX.companies.map(c=>c.id===co?{...c,openingOutstanding:(c.openingOutstanding??0)+200000}:c)}});
    const withOther=other(s);
    const recvAll=M.grpExposure(withOther,M.grpCompany(withOther,co));
    assert.equal(recvAll,recv+200000);
    const input=withLimit(withOther,co,recvAll+118000-1);
    const next=structuredClone(input);
    bk(next,id).lines.push({id:'line-l9',label:'Late laundry',amount:100000,date:next.date,kind:'service',taxBps:1800,folio:'B'});
    assert.equal(totals(M.sfView(bk(next,id),'B')).balance,118000,'the new line is all that is open on folio B');
    // exposure after = everything the company already owes + ₹1,180 > limit → refused
    assert.throws(()=>M.sfAfterCommand(input,next,{type:'CHARGE',id}),e=>e.code==='INVALID_TRANSITION'&&/needs ₹1,180/.test(e.message));
    // exactly at the limit, it passes
    const roomy=withLimit(withOther,co,recvAll+118000),n2=structuredClone(roomy);
    bk(n2,id).lines.push({id:'line-l9',label:'Late laundry',amount:100000,date:n2.date,kind:'service',taxBps:1800,folio:'B'});
    M.sfAfterCommand(roomy,n2,{type:'CHARGE',id});
  });
}
