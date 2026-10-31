// ACC-1 · lane acc1-parties: party sub-ledgers derived from the masters, vouchers posting to `<control>/<party>`, the no-party warning, the
// trial balance in summary (control) and detail (party) form, the Udhaar credit raised as a sale (A17, DEC-144), company payments, and the
// master retire guard. Contract: 02 Planning/agents/ACC1-POSTING-CONTRACT.md §3, §4. Names prefixed [ACC1-PARTIES].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const D=seed().date;
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  const net=(vs,acct)=>vs.reduce((a,v)=>a+v.entries.filter(e=>e.account===acct).reduce((b,e)=>b+e.debit-e.credit,0),0);
  const balanced=vs=>{const t=M.accTotals(vs);return t.debit===t.credit&&vs.every(M.accBalanced);};
  const inUse=e=>e?.reason==='MASTER_IN_USE';
  /** An in-house booking of the seed billed to company CO-1, with a company-credit receipt of ₹6,300 today (pure state, no command). */
  const withCompanyCredit=(account='CO-1')=>{const s=structuredClone(seed()),b=s.bookings.find(b=>b.status==='In house')??s.bookings[0];
    b.company={name:'Deccan Pharma Distributors (fictional)',...(account?{account}:{})};
    b.receipts.push({id:'RC-CC-1',amount:630000,method:'Company credit',date:s.date,kind:'company-credit',reference:'Deccan · APR-1',actor:'owner'});return [s,b];};

  test('[ACC1-PARTIES] party ledgers are derived from the masters (tills, banks, companies, OTA channels, staff, vendors), never stored',()=>{
    const s=seed(),ps=M.accPartyLedgers(s),ids=ps.map(p=>p.id);
    for(const id of ['1000/TILL','1030/BANK-1','1110/CO-1','1110/CO-2','1200/HR-S01'])assert.ok(ids.includes(id),id);
    assert.ok(ps.some(p=>p.control==='1115'),'OTA channels');assert.ok(ps.some(p=>p.control==='2200'&&p.name==='Morning Supplies'),'expense vendors');
    for(const p of ps){assert.equal(p.id,p.control+'/'+p.partyId);assert.ok(M.ACC_PARTY_CONTROLS.includes(p.control));assert.equal(M.accPartyName(s,p.id),p.name);}
    assert.equal(new Set(ids).size,ids.length,'unique ids');
    assert.equal(s.accountingX,undefined,'nothing stored');
    assert.deepEqual([M.accIsParty('1110/CO-1'),M.accIsParty('1110'),M.accControlOf('2200/V-X'),M.accPartyIdOf('2200/V-X'),M.accIsControl('1100')],[true,false,'2200','V-X',false]);
    // Udhaar customers: distinct (name, mobile) → UD-<10-digit mobile>, else UD-<slug of name>.
    assert.equal(M.accUdhaarPartyId({name:'Ramesh',mobile:'+91 98765 43210'}),'UD-9876543210');
    assert.equal(M.accUdhaarPartyId({name:'  Ramesh (fictional) ',mobile:''}),'UD-RAMESH-FICTIONAL');
  });

  test('[ACC1-PARTIES] cash posts to the till it settles to (1000/TILL), bank transfer to the bank account (1030/BANK-1); clearing ledgers stay plain',()=>{
    let s=seed();const id=s.bookings.find(b=>b.status==='In house').id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:10000,method:'Cash',kind:'payment'});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:20000,method:'Bank transfer',kind:'payment',reference:'NEFT-1'});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:30000,method:'UPI',kind:'payment',reference:'UPI-1'});
    const vs=M.accLiveVouchers(s,D),r=x=>vs.find(v=>v.source.kind==='receipt'&&v.entries[0].debit===x);
    assert.deepEqual(ent(r(10000)),[['1000/TILL',10000,0],['1100',0,10000]]);
    assert.deepEqual(ent(r(20000)),[['1030/BANK-1',20000,0],['1100',0,20000]]);
    assert.deepEqual(ent(r(30000)),[['1010',30000,0],['1100',0,30000]]);
    assert.ok(balanced(vs));assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    assert.equal(M.accExceptions(s,D).filter(x=>x.kind==='no-party').length,0);
  });

  test('[ACC1-PARTIES] company credit at checkout posts Dr 1110/<company>; without the company account it stays on 1110 with a no-party warning that never blocks the lock',()=>{
    const [s]=withCompanyCredit();
    const v=M.accLiveVouchers(s,D).find(v=>v.source.sub==='company-credit');
    assert.equal(v.type,'Journal');assert.deepEqual(ent(v),[['1110/CO-1',630000,0],['1100',0,630000]]);
    const [t]=withCompanyCredit(null),w=M.accLiveVouchers(t,D).find(v=>v.source.sub==='company-credit');
    assert.deepEqual(ent(w),[['1110',630000,0],['1100',0,630000]]);
    const x=M.accExceptions(t,D).filter(x=>x.kind==='no-party');
    assert.equal(x.length,1);assert.equal(x[0].severity,'warning');assert.equal(x[0].voucher,w.no);assert.match(x[0].title,/control ledger 1110 Company receivables/);
    assert.ok(!M.accBlocking(M.accExceptions(t,D)).some(e=>e.kind==='no-party'),'never an error');
  });

  test('[ACC1-PARTIES] vendor credit posts Cr 2200/<vendor code>; the vendor master row cannot be retired while its ledger has a balance',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',description:'Linen on credit',vendor:'Morning Supplies',category:'Supplies',amount:59000,method:'Vendor credit'});
    const eid=s.expenses.at(-1).id;s=execute(s,'manager',{type:'EXPENSE_APPROVE',id:eid,approve:true});
    const code=M.mnyVendor(s,'Morning Supplies').code,v=M.accLiveVouchers(s,D).find(v=>v.source.id===eid);
    assert.equal(v.type,'Purchase');assert.deepEqual(ent(v),[['5000',59000,0],['2200/'+code,0,59000]]); // ACC-2 contract §3.1: Purchase (was Journal)
    assert.equal(M.accPartyBalance(s,'2200/'+code),-59000);
    assert.match(M.accPartyRetireBlocker(s,'2200',code),/Vendor payables · Morning Supplies has a ledger balance of ₹590\.00 Cr/);
    const row=M.mnyVendor(s,'Morning Supplies');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'expenseVendors',id:row.id}),inUse);
    // A vendor with a zero balance retires as before.
    const idle=M.mnyVendors(s).find(x=>x.active&&!M.accPartyBalance(s,'2200/'+x.code)&&x.code!==code);
    if(idle)assert.doesNotThrow(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'expenseVendors',id:idle.id}));
  });

  test('[ACC1-PARTIES] a bank account with a ledger balance cannot be retired (methods moved away first); a till likewise',()=>{
    let s=seed();const id=s.bookings.find(b=>b.status==='In house').id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:20000,method:'Bank transfer',kind:'payment',reference:'NEFT-2'});
    assert.ok(M.accPartyBalance(s,'1030/BANK-1')!==0); // ACC-2 parity: the BASE salary advance HR-ADV-1 (bank transfer) also moves 1030/BANK-1
    assert.match(M.accPartyRetireBlocker(s,'1030','BANK-1'),/Bank accounts · Hotel current account has a ledger balance/);
    assert.match(M.accPartyRetireBlocker(s,'1000','TILL'),/Front-desk cash drawer has a ledger balance/);
    assert.equal(M.accPartyRetireBlocker(s,'1030','NO-SUCH'),'');
  });

  test('[ACC1-PARTIES] trial balance: summary sums parties into the control, detail shows a row per party under it; both tie',()=>{
    let [s]=withCompanyCredit();const id=s.bookings.find(b=>b.status==='In house').id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:20000,method:'Bank transfer',kind:'payment',reference:'NEFT-3'});
    const vs=M.accLiveVouchers(s,D),sum=M.accTrialBalance(vs),det=M.accTrialBalance(vs,undefined,true,s);
    assert.ok(sum.every(r=>!M.accIsParty(r.account)&&r.control===undefined),'summary: control rows only');
    const tot=rows=>rows.reduce((a,r)=>({d:a.d+r.debit,c:a.c+r.credit}),{d:0,c:0});
    assert.deepEqual(tot(sum),tot(det));
    for(const r of sum){const parts=det.filter(x=>M.accControlOf(x.account)===r.account);assert.equal(parts.reduce((a,x)=>a+x.balance,0),r.balance,r.account);}
    const co=det.find(r=>r.account==='1110/CO-1');assert.deepEqual([co.control,co.name,co.group,co.debit],['1110','Deccan Pharma Distributors (fictional)','Asset',630000]);
    const i=det.findIndex(r=>r.account==='1000/TILL');assert.ok(i>=0&&det.findIndex(r=>r.account==='1100')>i,'party rows sit in chart order under their control');
    assert.equal(M.accEntryName(s,'1110/CO-1'),'Company receivables (city ledger) · Deccan Pharma Distributors (fictional)');
    assert.equal(M.accEntryName(undefined,'1110/CO-9'),'Company receivables (city ledger) · CO-9');
  });

  test('[ACC1-PARTIES] Udhaar credit raised = Sales the same day: Dr 1120/<customer> gross · Cr revenue of the kind · Cr CGST · Cr SGST (A17, DEC-144)',()=>{
    let s=execute(seed(),'cashier',{type:'EXP_UD_ADD',name:'Suresh Patil (fictional)',mobile:'98765 43210',amount:105000,kind:'restaurant',gstBps:500});
    const r=M.expSlice(s).udhaar.at(-1);assert.deepEqual([r.kind,r.gstBps],['restaurant',500]);
    const map=M.accMapping(s),v=M.accLiveVouchers(s,D).find(v=>v.source.id==='udhaar:'+r.id);
    assert.deepEqual([v.type,v.source.kind,v.source.sub],['Sales','receipt','udhaar-credit']);
    assert.deepEqual(ent(v),[['1120/UD-9876543210',105000,0],[map['pos:sales'],0,100000],['2110',0,2500],['2120',0,2500]]);
    // Room folio: the room rate; other: other revenue; a credit without a kind (older screens, rows before ACC-1): other, no GST.
    s=execute(s,'cashier',{type:'EXP_UD_ADD',name:'Room guest (fictional)',amount:210000,kind:'room',gstBps:500});
    s=execute(s,'cashier',{type:'EXP_UD_ADD',name:'Old style (fictional)',amount:12345});
    const [room,old]=M.expSlice(s).udhaar.slice(-2),vs=M.accLiveVouchers(s,D);
    assert.deepEqual(ent(vs.find(v=>v.source.id==='udhaar:'+room.id)),[['1120/UD-ROOM-GUEST-FICTIONAL',210000,0],[map['line:room'],0,200000],['2110',0,5000],['2120',0,5000]]);
    assert.equal(old.kind,undefined);assert.equal(old.gstBps,undefined);
    assert.deepEqual(ent(vs.find(v=>v.source.id==='udhaar:'+old.id)),[['1120/UD-OLD-STYLE-FICTIONAL',12345,0],[map['line:service'],0,12345]]);
    assert.ok(balanced(vs));assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    assert.equal(M.accSourceOf(s,v).booking,undefined);
  });

  test('[ACC1-PARTIES] Udhaar: the kind defaults its GST from the GST master; bad kind / rate refused; paying in full clears the customer ledger',()=>{
    let s=execute(seed(),'cashier',{type:'EXP_UD_ADD',name:'Default rate (fictional)',amount:52500,kind:'restaurant'});
    assert.equal(M.expSlice(s).udhaar.at(-1).gstBps,M.gstForFood(s,D).bps);
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_ADD',name:'X',amount:100,kind:'bar'}),/restaurant bill, room folio or other/);
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_ADD',name:'X',amount:100,kind:'other',gstBps:3000}),/GST rate/);
    const r=M.expSlice(s).udhaar.at(-1),party='1120/'+M.accUdhaarPartyId(r);
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:r.id,amount:52500,mode:'UPI',reference:'UPI-UD'});
    const vs=M.accLiveVouchers(s,D);assert.equal(net(vs,party),0,'raised and paid: the customer owes nothing');
    assert.deepEqual(ent(vs.find(v=>v.source.sub==='udhaar-payment')),[['1010',52500,0],[party,0,52500]]);
    assert.ok(M.accMapRows(s).some(x=>x.key==='receipt:udhaar'&&x.uses===2));
  });

  test('[ACC1-PARTIES] expUdGst: the GST inside the gross, half away from zero, CGST takes the odd paisa; 0 % / absent = no GST',()=>{
    assert.deepEqual(M.expUdGst({amount:105000,gstBps:500}),{taxable:100000,tax:5000,cgst:2500,sgst:2500});
    assert.deepEqual(M.expUdGst({amount:100,gstBps:500}),{taxable:95,tax:5,cgst:3,sgst:2}); // 95.238 → 95
    assert.deepEqual(M.expUdGst({amount:21,gstBps:500}),{taxable:20,tax:1,cgst:1,sgst:0});   // 20.0 exactly
    assert.deepEqual(M.expUdGst({amount:118,gstBps:1800}),{taxable:100,tax:18,cgst:9,sgst:9});
    assert.deepEqual(M.expUdGst({amount:5000}),{taxable:5000,tax:0,cgst:0,sgst:0});
    for(const [g,b] of [[1,500],[999,1200],[123457,1800],[7,2800]]){const x=M.expUdGst({amount:g,gstBps:b});assert.equal(x.taxable+x.cgst+x.sgst,g);}
  });

  test('[ACC1-PARTIES] company payment = Receipt, Dr the tender · Cr 1110/<company> (sub company-payment; checks unchanged)',()=>{
    let s=execute(seed(),'owner',{type:'GRP_COMPANY_PAYMENT',company:'CO-1',amount:4000000,method:'Bank transfer',reference:'NEFT-DECCAN'});
    const p=M.grpSlice(s).payments.at(-1),v=M.accLiveVouchers(s,D).find(v=>v.source.id==='company-payment:'+p.id);
    assert.deepEqual([v.type,v.source.kind,v.source.sub],['Receipt','receipt','company-payment']);
    assert.deepEqual(ent(v),[['1030/BANK-1',4000000,0],['1110/CO-1',0,4000000]]);
    assert.match(v.narration,/^Company payment received · Deccan Pharma Distributors \(fictional\) · Bank transfer · NEFT-DECCAN/);
    assert.ok(M.accReconcile(s,D).every(c=>c.ok));assert.ok(M.accDates(s).includes(D));
    assert.ok(M.accMapRows(s).find(x=>x.key==='receipt:company-credit').uses>=1);
  });

  test('[ACC1-PARTIES] Tally XML carries the control ledger name (summary); the CSV names the party when the state is given',()=>{
    const [s]=withCompanyCredit(),x=M.accTallyXml(s,D,D);
    assert.match(x.xml,/<LEDGERNAME>Company receivables \(city ledger\)<\/LEDGERNAME><ISDEEMEDPOSITIVE>Yes<\/ISDEEMEDPOSITIVE><AMOUNT>-6300\.00<\/AMOUNT>/);
    assert.doesNotMatch(x.xml,/1110\/CO-1|1000\/TILL/);assert.equal(x.debit,x.credit);
    const vs=M.accLiveVouchers(s,D);
    assert.match(M.accCsv(vs,s),/,Company receivables \(city ledger\) · Deccan Pharma Distributors \(fictional\),6300\.00,/);
    assert.match(M.accCsv(vs),/,Cash in hand · TILL,/);
  });

  test('[ACC1-PARTIES] no-party warnings are not raised on a locked snapshot; locked vouchers are never re-pointed',()=>{
    const [t]=withCompanyCredit(null);
    const lock={date:D,lockedAt:t.clock,lockedBy:'owner',vouchers:M.accLiveVouchers(t,D),debit:0,credit:0,count:0};
    const u={...t,accountingX:{locks:{[D]:lock}}};
    assert.equal(M.accExceptions(u,D).filter(x=>x.kind==='no-party').length,0);
    assert.deepEqual(M.accVouchers(u,D),lock.vouchers);
  });
}
