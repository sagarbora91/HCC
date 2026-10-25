// Stream C tests — payments, folio, refunds, company credit, corrections, approvals (BL-017..021).
export function register({test,M,assert}){
  const {execute,loadScenario,baseFixture,totals,drawer,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  /** New in-house stay in room 105 (24→26 Oct) with both nights posted. DEC-069 GST: 2 × ₹2,000 at 5 % = ₹4,200 (was ₹4,400 at the synthetic 10 %). */
  function stay(s=baseFixture()){
    s=execute(s,'reception',{type:'BOOK',guest:'Stream C Test (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    return [s,id];
  }

  // BL-018 mixed tender
  test('[C] Mixed tender creates one receipt per tender once; same-key retry creates none',()=>{
    let [s,id]=stay();const before=bk(s,id).receipts.length;
    const c={type:'PAYMENT',id,amount:300000,kind:'payment',tenders:[{method:'Cash',amount:100000},{method:'UPI',amount:150000,reference:'DEMO-UPI-77'},{method:'Card',amount:50000,reference:'DEMO-CARD-1'}],key:'pay:mixed-1'};
    s=execute(s,'cashier',c);
    const rs=bk(s,id).receipts.slice(before);
    assert.equal(rs.length,3);assert.deepEqual(rs.map(r=>r.method),['Cash','UPI','Card']);
    assert.equal(new Set(rs.map(r=>r.tenderGroup)).size,1);assert.ok(rs[0].tenderGroup);
    assert.equal(totals(bk(s,id)).balance,420000-300000); // DEC-069 GST: 440000 → 420000
    const again=execute(s,'cashier',c);assert.equal(again,s);assert.equal(bk(again,id).receipts.length,before+3);
  });
  test('[C] Mixed tender: sum ≠ amount, missing reference, or closed shift is rejected with no effect',()=>{
    const [s,id]=stay();const copy=JSON.stringify(s);
    assert.equal(code(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:300000,tenders:[{method:'Cash',amount:100000},{method:'UPI',amount:150000,reference:'R'}]})),'VALIDATION');
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:300000,tenders:[{method:'Cash',amount:100000},{method:'UPI',amount:150000,reference:'R'}]}),/must be equal/);
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:200000,tenders:[{method:'Cash',amount:100000},{method:'UPI',amount:100000}]}),/transaction reference/);
    const closed=execute(s,'owner',{type:'SHIFT_CLOSE',counted:drawer(s)});
    assert.throws(()=>execute(closed,'cashier',{type:'PAYMENT',id,amount:200000,tenders:[{method:'Cash',amount:100000},{method:'UPI',amount:100000,reference:'R'}]}),/shift is closed/);
    assert.equal(JSON.stringify(s),copy);
  });
  test('[C] Single-tender PAYMENT shape stays backward compatible',()=>{
    let [s,id]=stay();s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'UPI',reference:'DEMO-1'});
    const r=bk(s,id).receipts.at(-1);assert.equal(r.amount,50000);assert.equal(r.method,'UPI');assert.equal(r.tenderGroup,undefined);
  });

  // BL-019 refunds
  test('[C] Direct PAYMENT refund is rejected and points to the refund request',()=>{
    const e=(()=>{try{execute(baseFixture(),'owner',{type:'PAYMENT',id:'HCC-1041',amount:1000,method:'Cash',kind:'refund',reference:'x'});}catch(e){return e;}})();
    assert.equal(e.code,'INVALID_TRANSITION');assert.match(e.message,/Request refund/);
  });
  test('[C] Refund flow: request → approve (SoD) → execute once, against the original receipt',()=>{
    let s=baseFixture();const id='HCC-1041',rid='seed-pay-0';// ₹2,000 cash receipt
    const paid0=totals(bk(s,id)).paid,drawer0=drawer(s);
    assert.equal(code(()=>execute(s,'housekeeper',{type:'REFUND_REQUEST',id,receiptId:rid,amount:50000,reason:'Overcharge'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:50000,reason:''})),'VALIDATION');
    s=execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rid,amount:50000,reason:'Early departure goodwill',key:'rr:1'});
    const a=s.approvals.at(-1);
    assert.equal(a.kind,'refund');assert.equal(a.target,rid);assert.equal(a.meta.command,'APPROVAL');assert.equal(a.status,'Pending');
    // over-refund: remainder is ₹2,000 − ₹500 outstanding = ₹1,500
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:160000,reason:'Too much'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'cashier',{type:'APPROVAL',id:a.id,approve:true})),'FORBIDDEN');// cashier lacks approve
    s.grants.Reception=[...s.grants.Reception,'approve'];
    assert.throws(()=>execute(s,'reception',{type:'APPROVAL',id:a.id,approve:true}),/own request/);
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id})),'INVALID_TRANSITION');// not yet approved
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
    const ap=s.approvals.find(x=>x.id===a.id);
    assert.equal(ap.status,'Approved');assert.equal(ap.approver,'manager');assert.equal(ap.decidedAt,M.addMinutes(s.clock,-1));
    assert.equal(totals(bk(s,id)).paid,paid0);// approval moves no money
    assert.equal(code(()=>execute(s,'housekeeper',{type:'REFUND_EXECUTE',approvalId:a.id})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'REFUND_EXECUTE',approvalId:a.id})),'FORBIDDEN');// requester cannot execute (DEC-031, integration)
    s=execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id,key:'rx:1'});
    const r=bk(s,id).receipts.at(-1);
    assert.equal(r.kind,'refund');assert.equal(r.refundOf,rid);assert.equal(r.amount,50000);assert.equal(r.method,'Cash');
    assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');
    assert.equal(totals(bk(s,id)).paid,paid0-50000);assert.equal(drawer(s),drawer0-50000);
    assert.equal(execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id,key:'rx:1'}),s);// same key → same result
    assert.throws(()=>execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id}),/already been executed/);
    assert.equal(M.folioRefundable(s,bk(s,id),rid),150000);
  });
  test('[C] Refund approval is invalidated by a folio change; deposits cannot be refunded',()=>{
    let s=baseFixture();const id='HCC-1041';
    s=execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:'seed-pay-0',amount:10000,reason:'Test'});const aid=s.approvals.at(-1).id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:5000,method:'Cash',kind:'deposit'});
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:aid,approve:true}),/folio changed/);
    const dep=bk(s,id).receipts.at(-1).id;
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:dep,amount:100,reason:'x'})),'INVALID_TRANSITION');
  });

  // BL-020 company credit (S4)
  test('[C] S4 fixture: HCC-1052 in room 203, ₹6,300 due (GST 5 %; was ₹6,600), company and GSTIN placeholder, 22 Oct closed',()=>{
    const s=loadScenario('S4'),b=bk(s,'HCC-1052');
    assert.equal(b.room,'203');assert.equal(b.status,'In house');assert.equal(totals(b).balance,630000); // DEC-069 GST: 660000 → 630000
    assert.deepEqual(b.company,{name:'Demo Industries Pvt Ltd (fictional)',gstin:'27AAAAA0000A1Z5'});
    assert.ok(s.closedDates.includes('2026-10-22'));assert.deepEqual(M.folioNights(b),{agreed:2,posted:2});
  });
  test('[C] S4 company-credit checkout: request, manager approval, receivable receipt, invoice number',()=>{
    let s=loadScenario('S4');const id='HCC-1052';
    assert.throws(()=>execute(s,'reception',{type:'CHECKOUT',id}),/Settle the folio exactly/);
    assert.equal(code(()=>execute(baseFixture(),'reception',{type:'COMPANY_CREDIT_REQUEST',id:'HCC-1041'})),'VALIDATION');// no company
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id,key:'cc:1'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'company-credit');assert.equal(a.amount,630000);assert.equal(a.version,bk(s,id).version); // DEC-069 GST: 660000 → 630000
    assert.equal(code(()=>execute(s,'reception',{type:'CHECKOUT',id})),'INVALID_TRANSITION');// not yet approved
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
    assert.equal(s.approvals.find(x=>x.id===a.id).status,'Approved');
    s=execute(s,'reception',{type:'CHECKOUT',id,key:'checkout:'+id});
    const b=bk(s,id);
    assert.equal(b.status,'Departed');assert.equal(totals(b).balance,0);assert.equal(totals(b).paid,0);
    const r=b.receipts.at(-1);assert.equal(r.kind,'company-credit');assert.equal(r.amount,630000); // DEC-069 GST: 660000 → 630000
    assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');
    assert.equal(b.invoiceNo,'DEMO/2026-27/0001');
    assert.equal(drawer(s),drawer(loadScenario('S4')));// company credit is not cash
  });
  test('[C] Company credit above ₹10,000 needs an Owner; stale credit falls back to the settle rule',()=>{
    let s=loadScenario('S4');const id='HCC-1052';
    s=execute(s,'reception',{type:'CHARGE',id,label:'Conference room (fictional)',amount:400000});// DEC-069 GST: conference = banquet 18 % → +₹4,720 → ₹11,020 (was +₹4,400 → ₹11,000)
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});const a=s.approvals.at(-1);
    assert.equal(a.amount,1102000); // DEC-069 GST: 1100000 → 1102000
    const e=(()=>{try{execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});}catch(e){return e;}})();
    assert.equal(e.code,'FORBIDDEN');assert.match(e.message,/Owner/);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const stale=execute(s,'reception',{type:'CHARGE',id,label:'Late snack',amount:10000});
    assert.throws(()=>execute(stale,'reception',{type:'CHECKOUT',id}),/Settle the folio exactly/);
    s=execute(s,'reception',{type:'CHECKOUT',id});assert.equal(bk(s,id).status,'Departed');
  });

  // BL-021 corrections and documents
  test('[C] Correction: approved request posts a linked reversal; posted lines never edited',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry (wrong room)',amount:50000});
    const line=bk(s,id).lines.at(-1),orig=structuredClone(line),net0=totals(bk(s,id)).net;
    assert.equal(code(()=>execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:line.id,reason:''})),'VALIDATION');
    s=execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:line.id,reason:'Posted to wrong folio'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'correction');assert.equal(a.target,line.id);
    assert.equal(code(()=>execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:line.id,reason:'again'})),'INVALID_TRANSITION');
    s.grants.Reception=[...s.grants.Reception,'approve'];
    assert.throws(()=>execute(s,'reception',{type:'APPROVAL',id:a.id,approve:true}),/own request/);
    assert.equal(code(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true})),'FORBIDDEN');// DEC-063 G3: Owner approves corrections
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const b=bk(s,id),rev=b.lines.at(-1);
    assert.equal(rev.reversalOf,line.id);assert.equal(rev.amount,-50000);assert.equal(rev.taxBps,line.taxBps);assert.equal(rev.date,s.date);
    assert.deepEqual(b.lines.find(l=>l.id===line.id),orig);
    assert.equal(totals(b).net,net0-50000);
    assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');
    assert.equal(code(()=>execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:rev.id,reason:'undo'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:line.id,reason:'twice'})),'INVALID_TRANSITION');
  });
  test('[C] Invoice number allocated once per stay, sequentially, at checkout',()=>{
    let [s,id]=stay();s=execute(s,'cashier',{type:'PAYMENT',id,amount:420000,method:'UPI',reference:'DEMO-U'}); // DEC-069 GST: 440000 → 420000
    assert.equal(bk(s,id).invoiceNo,undefined);
    s=execute(s,'reception',{type:'CHECKOUT',id,key:'co:1'});assert.equal(bk(s,id).invoiceNo,'DEMO/2026-27/0001');
    assert.equal(execute(s,'reception',{type:'CHECKOUT',id,key:'co:1'}),s);
    assert.equal(code(()=>execute(s,'reception',{type:'CHECKOUT',id})),'INVALID_TRANSITION');
    assert.equal(s.invoiceSeqC,1);
    // a second stay gets the next number
    const other='HCC-1041';s=execute(s,'owner',{type:'POST_NIGHTS',id:other});const bal=totals(bk(s,other)).balance;
    s=execute(s,'cashier',{type:'PAYMENT',id:other,amount:bal,method:'UPI',reference:'DEMO-V'});
    s=execute(s,'reception',{type:'CHECKOUT',id:other});
    assert.equal(bk(s,other).invoiceNo,'DEMO/2026-27/0002');assert.equal(bk(s,id).invoiceNo,'DEMO/2026-27/0001');
  });
  test('[C] Credit note after departure needs approval, then enables a refund against the credit',()=>{
    let [s,id]=stay();s=execute(s,'cashier',{type:'PAYMENT',id,amount:420000,method:'UPI',reference:'DEMO-U'}); // DEC-069 GST: 440000 → 420000
    const rid=bk(s,id).receipts.at(-1).id;
    assert.equal(code(()=>execute(s,'reception',{type:'CREDIT_NOTE',id,amount:10000,reason:'x'})),'INVALID_TRANSITION');// still in house
    s=execute(s,'reception',{type:'CHECKOUT',id});
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:11000,reason:'Goodwill'})),'INVALID_TRANSITION');// needs credit note
    s=execute(s,'reception',{type:'CREDIT_NOTE',id,amount:10000,reason:'Noisy room goodwill'});const a=s.approvals.at(-1);
    assert.equal(a.kind,'credit-note');assert.equal(a.target,'DEMO/2026-27/0001');
    assert.equal(totals(bk(s,id)).balance,0);// nothing changes before approval
    assert.equal(code(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true})),'FORBIDDEN');// DEC-063 G3: Owner approves credit notes
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const b=bk(s,id);assert.equal(b.creditNotes.length,1);assert.equal(b.creditNotes[0].no,'DEMO-CN/2026-27/0001');
    assert.equal(totals(b).balance,-10500); // DEC-069 GST: credit note ₹100 + 5 % room-rate GST = −₹105 (was −₹110)
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:10501,reason:'Too much'})),'INVALID_TRANSITION'); // DEC-069 GST: 12000 → 10501 (one paisa above the ₹105 credit)
    s=execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rid,amount:10500,reason:'Credit note refund'});const rr=s.approvals.at(-1); // DEC-069 GST: 11000 → 10500
    s=execute(s,'manager',{type:'APPROVAL',id:rr.id,approve:true});s=execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:rr.id});
    assert.equal(totals(bk(s,id)).balance,0);assert.equal(bk(s,id).receipts.at(-1).refundOf,rid);
  });

  // APPROVAL rules
  test('[C] New approvals expire after 24 simulated hours; expired approve fails, dismiss marks Expired',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Goodwill'});
    const a=s.approvals.at(-1);assert.equal(a.expiresAt,M.addMinutes(M.addMinutes(s.clock,-1),24*60));
    assert.equal(a.meta.command,'APPROVAL');
    const late={...s,clock:'2026-10-25T09:00'};
    const e=(()=>{try{execute(late,'manager',{type:'APPROVAL',id:a.id,approve:true});}catch(e){return e;}})();
    assert.equal(e.code,'INVALID_TRANSITION');assert.match(e.message,/expired/);
    const dismissed=execute(late,'manager',{type:'APPROVAL',id:a.id,approve:false});
    assert.equal(dismissed.approvals.find(x=>x.id===a.id).status,'Expired');
    assert.equal(M.folioApprovalStatus(late,a),'Expired');
    // within the window the same approval still works and records approver/decidedAt
    const ok=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),d=ok.approvals.find(x=>x.id===a.id);
    assert.equal(d.status,'Approved');assert.equal(d.approver,'manager');assert.ok(d.decidedAt);
  });
  // DEC-069 GST: ₹4,400 → ₹4,265 (rooms 5 %, laundry 18 %, discount at the room rate 5 %); balance 340000 → 326500; UPI tender 140000 → 126500.
  test('[C] AC-006 ₹4,265 GST journey (was ₹4,400) with a mixed-tender settlement still holds',()=>{
    let s=loadScenario('S0');
    s=execute(s,'reception',{type:'BOOK',guest:'Rahul Verma (fictional)',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2});const id=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'DEMO-UPI-0001'});
    s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'reception',{type:'CHARGE',id,amount:50000,label:'Laundry service'});
    s=execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Service delay goodwill'});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.equal(totals(bk(s,id)).balance,326500);
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:326500,tenders:[{method:'Cash',amount:200000},{method:'UPI',amount:126500,reference:'DEMO-UPI-0002'}],key:'settle'});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return'});
    assert.deepEqual(totals(bk(s,id)),{net:400000,tax:26500,total:426500,paid:426500,deposit:0,balance:0});
    s=execute(s,'reception',{type:'CHECKOUT',id});assert.equal(bk(s,id).status,'Departed');assert.equal(bk(s,id).invoiceNo,'DEMO/2026-27/0001');
  });
  test('[C] Pending non-discount request blocks checkout; discount message unchanged',()=>{
    let [s,id]=stay();s=execute(s,'cashier',{type:'PAYMENT',id,amount:420000,method:'UPI',reference:'R'}); // DEC-069 GST: 440000 → 420000
    const withDiscount=execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'x'});
    assert.throws(()=>execute(withDiscount,'reception',{type:'CHECKOUT',id}),/pending discount request/);
    const rid=bk(s,id).receipts.at(-1).id;
    const withRefund=execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:1000,reason:'x'});
    assert.throws(()=>execute(withRefund,'reception',{type:'CHECKOUT',id}),/pending approval/);
  });
}
