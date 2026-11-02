// LF lane lf-money-bill · contract §4 (A2 bill correction after check-out) and §5 (B7 GSTR-1 Table 13 legacy row, Table 12 SAC).
export function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[LF-BILL] ';
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  /** A departed, fully paid stay: 2 nights × ₹2,000 + laundry ₹500. */
  function departed(){
    let s=seed();s=execute(s,'owner',{type:'BOOK',guest:'Bill Test',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'UPI',kind:'payment',reference:'DEMO-UPI'});
    s=execute(s,'owner',{type:'CHECKOUT',id});return [s,id];
  }
  const laundry=(s,id)=>bk(s,id).lines.find(l=>l.label==='Laundry');

  test(T+'request → Owner approves: whole-invoice credit note, new invoice revision, refund due',()=>{
    let [s,id]=departed();const x0=bk(s,id),oldNo=x0.invoiceNo,oldNet=totals(x0).net;
    s=execute(s,'reception',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'Wrong rent and laundry',changes:[
      {kind:'room-rate',night:'2026-10-24',newRate:150000},{kind:'remove-line',lineId:laundry(s,id).id},{kind:'add-line',label:'Tea',amount:10000,gstCategory:'food',date:'2026-10-25'}]}); 
    const a=s.approvals.at(-1),c=s.billCorrectionX.corrections.at(-1);
    assert.equal(a.kind,'bill-correction');assert.equal(c.status,'Pending');assert.ok(c.delta<0);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),/approved by an Owner/);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const x=bk(s,id),r=s.billCorrectionX.corrections.at(-1);
    assert.equal(r.status,'Issued');assert.notEqual(x.invoiceNo,oldNo);assert.equal(r.newInvoiceNo,x.invoiceNo);assert.equal(r.oldInvoiceNo,oldNo);
    assert.ok(r.creditNoteNo);assert.equal(x.creditNotes.at(-1).no,r.creditNoteNo);assert.equal(x.creditNotes.at(-1).amount,oldNet);
    assert.deepEqual(x.invoiceRevisions.map(v=>v.no),[oldNo]);
    assert.equal(totals(x).net,150000+200000+10000);assert.equal(totals(x).balance,r.delta);assert.equal(r.settlement,'refund-due');
    assert.equal(s.approvals.find(v=>v.id===a.id).status,'Executed');
    assert.match(M.blcInvoiceNote(s,oldNo),/Superseded by/);assert.match(M.blcInvoiceNote(s,x.invoiceNo),/Supersedes/);
    // ledgers carry the delta only; every voucher balanced
    const vs=M.accLiveVouchers(s,s.date);assert.ok(vs.every(M.accBalanced));
    // the refund flow works because the credit note exists
    const rc=x.receipts.find(r=>r.kind==='payment');
    s=execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rc.id,amount:-r.delta,reason:'Bill corrected'});assert.equal(s.approvals.at(-1).kind,'refund');
  });
  test(T+'GST of a corrected night follows its new price slab; tax of the credit equals the old invoice tax',()=>{
    let [s,id]=departed();const oldTax=totals(bk(s,id)).tax;
    s=execute(s,'accountant',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'Upgrade billed late',changes:[{kind:'room-rate',night:'2026-10-25',newRate:900000}]});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    const x=bk(s,id),cn=x.lines.filter(l=>l.approvalId===x.creditNotes.at(-1).approvalId);
    assert.equal(-cn.reduce((a,l)=>a+M.lineTax(l),0),oldTax);
    const n=x.lines.find(l=>l.label==='Room night · 2026-10-25'&&l.amount===900000);assert.equal(n.taxBps,M.gstForRoomNight(s,'2026-10-25',900000).bps);
  });
  test(T+'balance due: PAYMENT on the departed folio for exactly that balance only',()=>{
    let [s,id]=departed();
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:100,method:'UPI',reference:'X'}));
    s=execute(s,'cashier',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'Missed tea',changes:[{kind:'add-line',label:'Tea',amount:20000,gstCategory:'food',date:'2026-10-24'}]});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    const bal=totals(bk(s,id)).balance;assert.ok(bal>0);assert.equal(s.billCorrectionX.corrections.at(-1).settlement,'balance-due');
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:bal-1,method:'UPI',reference:'X'}),/exactly the balance/);
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:bal,method:'UPI',reference:'X'});
    assert.equal(totals(bk(s,id)).balance,0);assert.match(bk(s,id).receipts.at(-1).reference,/Bill correction/);
  });
  test(T+'a second correction credits revision 2 and issues revision 3',()=>{
    let [s,id]=departed();
    s=execute(s,'reception',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'One',changes:[{kind:'remove-line',lineId:laundry(s,id).id}]});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});const net2=totals(bk(s,id)).net;
    s=execute(s,'reception',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'Two',changes:[{kind:'room-rate',night:'2026-10-24',newRate:100000}]});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    const x=bk(s,id);assert.equal(x.invoiceRevisions.length,2);assert.equal(totals(x).net,net2-100000);assert.equal(x.creditNotes.length,2);
    const g=M.gstr1Summary(s,'2026-10-01','2026-10-31');const inv=g.documents.find(d=>d.docType==='INV');
    assert.ok(inv.count>=3);assert.equal(inv.cancelled,0,'superseded invoices count as issued');
    assert.ok(g.documents.find(d=>d.docType==='CRN').count>=2);
  });
  test(T+'guards: in-house, no changes, room line removal, double pending, roles, s.34(2) window, stale',()=>{
    let s=seed();s=execute(s,'owner',{type:'BOOK',guest:'Xy Guest',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const ih=s.bookings.at(-1).id;
    assert.throws(()=>execute(s,'owner',{type:'BILL_CORRECTION_REQUEST',booking:ih,reason:'x',changes:[]}),/departed, invoiced/);
    let [t,id]=departed();
    assert.throws(()=>execute(t,'owner',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[]}),/at least one change/);
    assert.throws(()=>execute(t,'owner',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'',changes:[{kind:'add-line',label:'a',amount:1,gstCategory:'food',date:'2026-10-24'}]}),/reason/);
    const room=bk(t,id).lines.find(l=>l.kind==='room');
    assert.throws(()=>execute(t,'owner',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[{kind:'remove-line',lineId:room.id}]}),/room rent instead/);
    assert.throws(()=>execute(t,'owner',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[{kind:'room-rate',night:'2026-10-26',newRate:1}]}),/night of the stay/);
    assert.throws(()=>execute(t,'housekeeper',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[]}),e=>e.code==='FORBIDDEN');
    t=execute(t,'reception',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[{kind:'remove-line',lineId:laundry(t,id).id}]});
    assert.throws(()=>execute(t,'reception',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[{kind:'remove-line',lineId:laundry(t,id).id}]}),/already pending/);
    t=execute(t,'owner',{type:'APPROVAL',id:t.approvals.at(-1).id,approve:false});assert.equal(M.blcCorrections(t,id).at(-1).status,'Rejected');
    const late=structuredClone(t);late.date='2027-12-01';
    assert.throws(()=>execute(late,'owner',{type:'BILL_CORRECTION_REQUEST',booking:id,reason:'x',changes:[{kind:'remove-line',lineId:laundry(t,id).id}]}),/s\.34\(2\)/);
    assert.equal(M.blcDeadline('2026-10-26'),'2027-11-30');assert.equal(M.blcDeadline('2027-02-01'),'2027-11-30');
  });

  // ── B7 ──
  test(T+'GSTR-1 Table 13: legacy LGC row only in periods with legacy bills; Table 12 has no legacy',()=>{
    const s=seed();
    const may=M.gstr1Summary(s,'2026-05-01','2026-05-31',M.getLegacyBills);
    const lgc=may.documents.find(d=>d.docType==='LGC');assert.ok(lgc);assert.equal(lgc.first,'1204');assert.equal(lgc.count,1);assert.equal(lgc.cancelled,0);
    assert.equal(M.GSTR1_SERIES_LABEL.LGC,'Legacy Lodge bills (plain numbers, before cut-over)');
    const oct=M.gstr1Summary(s,'2026-10-01','2026-10-31',M.getLegacyBills);assert.ok(!oct.documents.some(d=>d.docType==='LGC'));
    assert.deepEqual(M.gstr1Summary(s,'2026-05-01','2026-05-31').documents.filter(d=>d.docType==='LGC'),[]);
    const rows=M.gstr1Table12(oct);assert.ok(rows.every(r=>r.uqc==='OTH'));
    const csv=M.gstr1Table12Csv(rows);assert.match(csv,/^SAC,Description,UQC,Quantity/);
  });
}
