// ACC-2 · lane acc2-flows: deposit applied at check-out (A6a), the OTA credit at check-out and the OTA payout from the statement (A13,
// DEC-150), the cash guards (B9 refused, B10 warned + "deduction lost"), the mistaken Udhaar payment reversed (A17b) and company retire
// (DEC-157). Contract: 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §7, §8. Names prefixed [ACC2-FLOWS].
export function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const D=seed().date,R=r=>r*100; // rupees → paise
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  const balanced=vs=>{const t=M.accTotals(vs);return t.debit===t.credit&&vs.every(M.accBalanced);};
  const reason=r=>e=>e?.reason===r;
  const code=c=>e=>e?.code===c;
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  /** An in-house booking in room 105 (2 nights from the business date) with its nights posted; extra BOOK fields (source, otaChannel). */
  function stay(extra={},s=seed()){
    s=execute(s,'owner',{type:'BOOK',guest:'Flows Test (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:2,...extra});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    return [s,id];
  }
  const otaStay=(channel='BookNest')=>stay({source:'OTA',otaChannel:channel});

  // ── A6a · DEPOSIT_APPLY ──
  test('[ACC2-FLOWS] DEPOSIT_APPLY: receipt kind deposit-apply, deposit held and balance both fall, Journal Dr 2000 · Cr 1100; checkout then settles',()=>{
    let [s,id]=stay();const t0=totals(bk(s,id));
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:R(1000),method:'Cash',kind:'deposit'});
    assert.equal(totals(bk(s,id)).deposit,R(1000));
    s=execute(s,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(1000)});
    const r=bk(s,id).receipts.at(-1),t=totals(bk(s,id));
    assert.deepEqual([r.kind,r.amount,r.method,r.date],['deposit-apply',R(1000),'Deposit applied',D]);
    assert.deepEqual([t.deposit,t.balance,t.paid],[0,t0.balance-R(1000),R(1000)],'coordinator ruling: a deposit-apply receipt counts as paid');
    const v=M.accLiveVouchers(s,D).find(v=>v.source.id===`${id}/${r.id}`);
    assert.deepEqual([v.type,v.source.sub,ent(v)],['Journal','deposit-apply',[['2000',R(1000),0],['1100',0,R(1000)]]]);
    assert.ok(balanced(M.accLiveVouchers(s,D)));
    assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:t.balance,method:'UPI',kind:'payment',reference:'UPI-FLOWS-1'});
    s=execute(s,'owner',{type:'CHECKOUT',id});assert.equal(bk(s,id).status,'Departed');
    // Mapping table: no "receipt:deposit-apply" / "method:Deposit applied" rows — the voucher uses receipt:deposit and folio:guest.
    const keys=M.accMapRows(s).map(x=>x.key);
    assert.ok(!keys.includes('receipt:deposit-apply')&&!keys.includes('method:Deposit applied'),keys.join(','));
    assert.ok(M.accMapRows(s).filter(x=>x.uses).every(x=>x.mapped));
  });
  test('[ACC2-FLOWS] DEPOSIT_APPLY guards: payments permission, in house, ≤ deposit held, ≤ balance, an open shift',()=>{
    let [s,id]=stay();
    assert.throws(()=>execute(s,'housekeeper',{type:'DEPOSIT_APPLY',booking:id,amount:100}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'accountant',{type:'DEPOSIT_APPLY',booking:id,amount:100}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:100}),/no refundable deposit/);
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:R(500),method:'Cash',kind:'deposit'});
    assert.throws(()=>execute(s,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(600)}),/at most the deposit held \(₹500\)/);
    assert.throws(()=>execute(s,'cashier',{type:'DEPOSIT_APPLY',booking:'HCC-1049',amount:100}),/in-house stay/);
    const bal=totals(bk(s,id)).balance;
    let p=execute(s,'cashier',{type:'PAYMENT',id,amount:bal-R(100),method:'UPI',kind:'payment',reference:'UPI-FLOWS-2'});
    assert.throws(()=>execute(p,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(200)}),/at most the balance due \(₹100\)/);
    p=execute(p,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(100)});
    assert.deepEqual([totals(bk(p,id)).deposit,totals(bk(p,id)).balance],[R(400),0]);
    const closed=execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});
    assert.throws(()=>execute(closed,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(100)}),/shift is closed/);
  });

  test('[ACC2-FLOWS] DEPOSIT_APPLY source opening (Manager / Owner, rulebook T5a): an advance carried in the opening balance, ≤ the 2000 credit balance',()=>{
    let s=execute(seed(),'manager',{type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines:[{account:'1030/BANK-1',debit:R(5000),credit:0},{account:'2000',debit:0,credit:R(5000)}]});
    s=execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    let id;[s,id]=stay({},s);const t0=totals(bk(s,id));
    assert.throws(()=>execute(s,'cashier',{type:'DEPOSIT_APPLY',booking:id,amount:R(1000),source:'opening'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'manager',{type:'DEPOSIT_APPLY',booking:id,amount:R(1000)}),/no refundable deposit/);
    assert.throws(()=>execute(s,'manager',{type:'DEPOSIT_APPLY',booking:id,amount:R(1000),source:'bank'}),/Choose where the advance comes from/);
    assert.throws(()=>execute(seed(),'owner',{type:'DEPOSIT_APPLY',booking:'HCC-1041',amount:R(10),source:'opening'}),/No advance is held/);
    s=execute(s,'manager',{type:'DEPOSIT_APPLY',booking:id,amount:R(2000),source:'opening'});
    const r=bk(s,id).receipts.at(-1),t=totals(bk(s,id));
    assert.deepEqual([r.kind,r.depositSource,r.method],['deposit-apply','opening','Opening advance applied']);
    assert.deepEqual([t.paid,t.deposit,t.balance],[R(2000),0,t0.balance-R(2000)]);
    const v=M.accLiveVouchers(s,D).find(v=>v.source.id===`${id}/${r.id}`);
    assert.deepEqual([v.type,ent(v)],['Journal',[['2000',R(2000),0],['1100',0,R(2000)]]]);
    assert.equal(M.accLedgerBalanceMap(s).get('2000'),-R(3000));
    assert.throws(()=>execute(s,'owner',{type:'DEPOSIT_APPLY',booking:id,amount:R(3500),source:'opening'}),/at most the advances held in 2000 Advance deposits \(₹3,000\)/);
  });

  // ── A13 · OTA credit at check-out and the payout from the statement ──
  test('[ACC2-FLOWS] CHECKOUT {otaCredit}: the balance moves to 1115/<channel> (Journal Dr 1115/booknest · Cr 1100) — only for a connected OTA channel',()=>{
    let [s,id]=otaStay();const bal=totals(bk(s,id)).balance;assert.ok(bal>0);
    assert.equal(M.accOtaChannelOf(s,bk(s,id)),'booknest');assert.equal(M.chnChannelOfBooking(s,bk(s,id)).id,'booknest');
    s=execute(s,'owner',{type:'CHECKOUT',id,otaCredit:true});
    const r=bk(s,id).receipts.at(-1);
    assert.deepEqual([bk(s,id).status,r.kind,r.amount,totals(bk(s,id)).balance],['Departed','ota-credit',bal,0]);
    const v=M.accLiveVouchers(s,D).find(v=>v.source.id===`${id}/${r.id}`);
    assert.deepEqual([v.type,v.source.sub,ent(v)],['Journal','ota-credit',[['1115/booknest',bal,0],['1100',0,bal]]]);
    assert.equal(M.accPartyBalance(s,'1115/booknest'),bal);assert.equal(M.chnOtaOpen(s,'booknest'),bal);
    assert.deepEqual(M.chnOtaUnpaid(s,'booknest').map(x=>x.booking),[id]);
    assert.ok(!M.accExceptions(s,D).some(x=>x.kind==='no-party'&&x.source===v.source.id));
    // Refused: a direct booking; a channel that is not connected (TripKart: Error).
    const [d,did]=stay();assert.throws(()=>execute(d,'owner',{type:'CHECKOUT',id:did,otaCredit:true}),/did not come from an OTA channel/);
    const [e,eid]=otaStay('TripKart');assert.throws(()=>execute(e,'owner',{type:'CHECKOUT',id:eid,otaCredit:true}),/TripKart is error/);
  });
  /** A departed BookNest stay on OTA credit: [state, booking id, gross]. */
  function otaCredited(){let [s,id]=otaStay();const gross=totals(bk(s,id)).balance;s=execute(s,'owner',{type:'CHECKOUT',id,otaCredit:true});return [s,id,gross];}
  const payout=(gross,id,x={})=>({type:'CHN_PAYOUT',channel:'booknest',statementRef:'BN-STMT-OCT-01',gross,commission:R(100),commissionGst:R(18),tcs:R(5),net:gross-R(123),bank:'BANK-1',bookings:[id],...x});
  test('[ACC2-FLOWS] CHN_PAYOUT posts Receipt Dr 1030/<bank> net · Dr 5070 commission + GST · Dr 1340 TCS · Cr 1115/<channel> gross and clears the receivable',()=>{
    let [s,id,gross]=otaCredited();
    s=execute(s,'accountant',payout(gross,id));
    const p=s.channelsX.payouts.at(-1);
    assert.deepEqual([p.channel,p.statementRef,p.gross,p.net,p.bank,p.bookings,p.by,p.date],['booknest','BN-STMT-OCT-01',gross,gross-R(123),'BANK-1',[id],'accountant',D]);
    const v=M.accLiveVouchers(s,D).find(v=>v.source.kind==='ota');
    assert.deepEqual([v.type,v.source.id,v.source.sub],['Receipt','ota-payout:'+p.id,'ota-payout']);
    assert.deepEqual(ent(v),[['1030/BANK-1',gross-R(123),0],['5070',R(118),0],['1340',R(5),0],['1115/booknest',0,gross]]);
    assert.equal(M.accPartyBalance(s,'1115/booknest'),0);assert.equal(M.chnOtaOpen(s,'booknest'),0);assert.deepEqual(M.chnOtaUnpaid(s,'booknest'),[]);
    assert.ok(balanced(M.accLiveVouchers(s,D)));assert.ok(M.accDates(s).includes(D));
    assert.deepEqual(M.accSourceOf(s,v),{});
    const rows=M.accMapRows(s);for(const k of ['receipt:ota','receipt:ota-commission','tax:tcs'])assert.ok(rows.find(x=>x.key===k)?.mapped,k);
    assert.deepEqual(['receipt:ota','receipt:ota-commission','tax:tcs'].map(k=>M.ACC_DEFAULT_MAP[k]),['1115','5070','1340']);
  });
  test('[ACC2-FLOWS] CHN_PAYOUT guards: gross = net + commission + GST + TCS, gross ≤ open receivable, bank account, statement once, roles',()=>{
    const [s,id,gross]=otaCredited();
    assert.throws(()=>execute(s,'reception',payout(gross,id)),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'cashier',payout(gross,id)),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',payout(gross,id,{net:gross})),/must equal net/);
    assert.throws(()=>execute(s,'owner',payout(gross+R(10),id,{net:gross+R(10)-R(123)})),/more than BookNest's open receivable/);
    assert.throws(()=>execute(s,'owner',payout(gross,id,{bank:'TILL'})),/bank account/);
    assert.throws(()=>execute(s,'owner',payout(gross,id,{statementRef:''})),/statement reference/);
    assert.throws(()=>execute(s,'owner',payout(gross,id,{bookings:['HCC-1041']})),/HCC-1041 has no OTA credit/);
    const half=Math.floor(gross/2);
    let p=execute(s,'manager',payout(half,id,{net:half-R(123)}));
    assert.throws(()=>execute(p,'manager',payout(half,id,{net:half-R(123)})),/already recorded/);
    p=execute(p,'manager',payout(gross-half,id,{statementRef:'BN-STMT-OCT-02',commission:0,commissionGst:0,tcs:0,net:gross-half}));
    assert.equal(M.accPartyBalance(p,'1115/booknest'),0);
  });
  test('[ACC2-FLOWS] channel ITC allowed (Owner only): the commission GST goes to 1310 / 1320 half each, 5070 takes the commission alone',()=>{
    let [s,id,gross]=otaCredited();const c=s.channelsX.channels.find(c=>c.id==='booknest');
    const save=(uid,itc)=>execute(s,uid,{type:'CHN_CHANNEL_SAVE',channel:'booknest',commissionBps:c.commissionBps,markupBps:c.markupBps,mapping:c.mapping,itcAllowed:itc});
    assert.throws(()=>save('manager',true),code('FORBIDDEN'));
    s=save('owner',true);assert.equal(s.channelsX.channels.find(c=>c.id==='booknest').itcAllowed,true);
    s=execute(s,'owner',payout(gross,id,{commissionGst:R(19),net:gross-R(124)}));
    const v=M.accLiveVouchers(s,D).find(v=>v.source.kind==='ota');
    assert.deepEqual(ent(v),[['1030/BANK-1',gross-R(124),0],['5070',R(100),0],['1310',950,0],['1320',950,0],['1340',R(5),0],['1115/booknest',0,gross]]);
    assert.ok(M.accBalanced(v));
  });

  // ── B9 / B10 · cash guards ──
  test('[ACC2-FLOWS] B9: cash from one guest at or above ₹2,00,000 for a booking is refused (CASH_LIMIT, s.186); UPI is not limited',()=>{
    let [s,id]=stay();
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:R(200000),method:'Cash',kind:'payment'}),e=>e.reason==='CASH_LIMIT'&&e.code==='VALIDATION'&&/s\.186/.test(e.message)&&/VERIFY section numbers/.test(e.message));
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:R(150000),method:'Cash',kind:'deposit'});
    // A split tender counts only its cash rows; the booking total (deposit + this) reaches the limit.
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:R(60000),kind:'payment',tenders:[{method:'Cash',amount:R(50000)},{method:'UPI',amount:R(10000),reference:'UPI-SPLIT'}]}),reason('CASH_LIMIT'));
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:R(49999),method:'Cash',kind:'payment'});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:R(300000),method:'UPI',kind:'payment',reference:'UPI-BIG'});
    const g=M.accCashGuard(s,{kind:'receipt',party:'booking:'+id,scope:'booking:'+id,amount:R(1)});
    assert.deepEqual([g.refused,g.total,g.limit],[true,R(200000),R(200000)]);
    assert.equal(M.accCashGuard(s,{kind:'receipt',party:'booking:'+id,amount:R(1),method:'UPI'}).refused,false,'non-cash method passes');
  });
  test('[ACC2-FLOWS] B9: an outlet bill and an Udhaar settlement in cash are guarded too',()=>{
    let s=execute(seed(),'reception',{type:'EXP_UD_ADD',name:'Flows Udhaar (fictional)',mobile:'9800000001',amount:R(300000),kind:'other'});
    const ud=s.expensesX.udhaar.at(-1).id;
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_PAY',id:ud,amount:R(200000),mode:'Cash'}),reason('CASH_LIMIT'));
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:ud,amount:R(150000),mode:'Cash'});
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_PAY',id:ud,amount:R(50000),mode:'Cash'}),reason('CASH_LIMIT'));
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:ud,amount:R(50000),mode:'UPI',reference:'UPI-UD'});
    assert.equal(M.accCashFlows(s).filter(f=>f.party==='1120/UD-9800000001').reduce((a,f)=>a+f.amount,0),R(150000));
    const g=M.accCashGuard(seed(),{kind:'receipt',party:'pos:ORD-X',scope:'pos:ORD-X',amount:R(200000),name:'this outlet bill'});
    assert.ok(g.refused);assert.match(g.message,/from this outlet bill for this booking \/ transaction|from this outlet bill on/);
  });
  test('[ACC2-FLOWS] B10: cash above ₹10,000 to one vendor in a day posts with a warning and cashDeductionLost; ₹35,000 for transport',()=>{
    let s=seed();
    const exp=(st,amount,vendor='Morning Supplies')=>{st=execute(st,'reception',{type:'EXPENSE',description:'Flows cash test '+amount,vendor,category:'Supplies',amount,method:'Cash'});
      const id=st.expenses.at(-1).id;return [execute(st,'owner',{type:'EXPENSE_APPROVE',id,approve:true}),id];};
    let a,b;[s,a]=exp(s,R(6000));[s,b]=exp(s,R(5000));
    assert.equal(s.expenses.find(e=>e.id===a).cashDeductionLost,undefined);
    assert.equal(s.expenses.find(e=>e.id===b).cashDeductionLost,true);
    assert.match(s.audit[0].detail,/totals ₹11,000, more than ₹10,000: the income-tax deduction is lost \(old s\.40A\(3\)/);
    assert.deepEqual(M.accCashDeductionLost(s).map(x=>x.id),[b]);
    // UPI to the same vendor is not counted; a different vendor starts from zero.
    let c;[s,c]=exp(s,R(9000),'Fresh Farm Dairy (fictional)');assert.equal(s.expenses.find(e=>e.id===c).cashDeductionLost,undefined);
    const t=x=>M.accCashGuard(seed(),{kind:'payment',party:'2200/EV-T',amount:x,transport:true});
    assert.deepEqual([t(R(35000)).deductionLost,t(R(35001)).deductionLost,M.accCashGuard(seed(),{kind:'payment',party:'2200/EV-T',amount:R(10001)}).deductionLost],[false,true,true]);
  });
  test('[ACC2-FLOWS] cash limits are dated Owner settings (Master configuration › Money and tax): a lower receipt limit applies from its date',()=>{
    let s=seed();
    assert.deepEqual(M.ACC_CASH_KEYS,['cash.receiptLimit','cash.paymentLimit','cash.transportLimit']);
    assert.deepEqual(M.ACC_CASH_KEYS.map(k=>M.accCashLimitOn(s,k,D)),[R(200000),R(10000),R(35000)]);
    const def=M.cfgDef(s,'cash.receiptLimit');assert.ok(def&&def.money&&def.dated&&/VERIFY section numbers/.test(def.help),JSON.stringify(def));
    assert.throws(()=>execute(s,'manager',{type:'SETTING_SET',setting:'cash.receiptLimit',value:R(100000),effectiveFrom:D}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'SETTING_SET',setting:'cash.receiptLimit',value:R(100000),effectiveFrom:D});
    assert.equal(M.accCashLimitOn(s,'cash.receiptLimit',D),R(100000));assert.equal(M.accCashLimitOn(s,'cash.receiptLimit','2026-01-01'),R(200000));
    const id=s.bookings.find(b=>b.status==='In house').id;
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id,amount:R(100000),method:'Cash',kind:'payment'}),reason('CASH_LIMIT'));
  });

  // ── A17b · EXP_UD_PAY_REVERSE ──
  test('[ACC2-FLOWS] EXP_UD_PAY_REVERSE (Owner): a negative settlement on the business date — outstanding back, drawer back, Receipt reversed',()=>{
    let s=execute(seed(),'reception',{type:'EXP_UD_ADD',name:'Reversal Test (fictional)',mobile:'9800000002',amount:R(5000),kind:'restaurant'});
    const ud=s.expensesX.udhaar.at(-1).id,drawer0=M.drawer(s);
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:ud,amount:R(2000),mode:'Cash'});
    const pid=s.expensesX.udhaar.at(-1).settlements.at(-1).id;assert.equal(M.drawer(s),drawer0+R(2000));
    assert.throws(()=>execute(s,'manager',{type:'EXP_UD_PAY_REVERSE',receivable:ud,settlement:pid,reason:'Wrong customer'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'EXP_UD_PAY_REVERSE',receivable:ud,settlement:pid,reason:''}),/reason/);
    s=execute(s,'owner',{type:'EXP_UD_PAY_REVERSE',receivable:ud,settlement:pid,reason:'Wrong customer'});
    const r=s.expensesX.udhaar.find(u=>u.id===ud),rev=r.settlements.at(-1);
    assert.deepEqual([rev.amount,rev.reversalOf,rev.reason,rev.date,rev.desk],[-R(2000),pid,'Wrong customer',D,true]);
    assert.equal(r.settlements.find(z=>z.id===pid).reversedBy,rev.id);
    assert.equal(M.expUdOut(r),R(5000));assert.equal(M.drawer(s),drawer0);
    const vs=M.accLiveVouchers(s,D),pay=vs.find(v=>v.source.id==='udhaar-payment:'+pid),back=vs.find(v=>v.source.id==='udhaar-payment:'+rev.id);
    assert.deepEqual(ent(pay),[['1000/TILL',R(2000),0],['1120/UD-9800000002',0,R(2000)]]);
    assert.deepEqual([back.type,ent(back)],['Receipt',[['1000/TILL',0,R(2000)],['1120/UD-9800000002',R(2000),0]]]);
    assert.match(back.narration,/Udhaar payment reversed \(A17b\)/);
    assert.equal(M.accPartyBalance(s,'1120/UD-9800000002'),R(5000));assert.ok(balanced(vs));
    assert.throws(()=>execute(s,'owner',{type:'EXP_UD_PAY_REVERSE',receivable:ud,settlement:pid,reason:'Again'}),/already reversed/);
    assert.throws(()=>execute(s,'owner',{type:'EXP_UD_PAY_REVERSE',receivable:ud,settlement:rev.id,reason:'Again'}),/cannot itself be reversed/);
    assert.ok(M.accCashFlows(s).filter(f=>f.party==='1120/UD-9800000002').reduce((a,f)=>a+f.amount,0)===0,'cash flows net to zero');
  });

  // ── Company retire (contract §8, DEC-157) ──
  const coSave=(s,uid,id,active)=>{const c=s.groupsX.companies.find(c=>c.id===id);
    return execute(s,uid,{type:'GRP_COMPANY_SAVE',id,name:c.name,gstin:c.gstin,contact:c.contact,phone:c.phone,email:c.email,city:c.city,rates:c.rates,active});};
  /** A new company account with no stays, blocks or balance: [state, id]. */
  const newCo=(s=seed())=>{s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Flows Retire Co (fictional)',city:'Latur',creditLimit:R(100000)});return [s,s.groupsX.companies.at(-1).id];};
  test('[ACC2-FLOWS] GRP_COMPANY_SAVE active:false retires a company: hidden from pickers, no new booking or block; reactivation brings it back',()=>{
    let [s,co]=newCo();s=coSave(s,'manager',co,false);
    assert.equal(s.groupsX.companies.find(c=>c.id===co).active,false);assert.match(s.audit[0].detail,/retired \(hidden from pickers\)/);
    assert.ok(!M.grpActiveCompanies(s).some(c=>c.id===co));assert.ok(M.grpActiveCompanies(s,co).some(c=>c.id===co),'kept when already chosen');
    assert.throws(()=>execute(s,'owner',{type:'BOOK',guest:'Retired Co Guest',room:'105',arrival:s.date,departure:'2026-10-26',people:1,company:{account:co}}),reason('MASTER_RETIRED'));
    assert.throws(()=>execute(s,'owner',{type:'GRP_BLOCK_CREATE',name:'Retired block',company:co,billTo:'company',arrival:'2026-11-10',departure:'2026-11-12',cutoff:'2026-11-01',allot:{Classic:1}}),reason('MASTER_RETIRED'));
    s=coSave(s,'manager',co,true);assert.equal(s.groupsX.companies.find(c=>c.id===co).active,undefined);
    assert.ok(M.grpActiveCompanies(s).some(c=>c.id===co));
    // Editing without `active` leaves it unchanged; a new company cannot start retired.
    assert.throws(()=>execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'New Retired Co (fictional)',active:false}),/starts active/);
  });
  test('[ACC2-FLOWS] retire refused (MASTER_IN_USE): an open receivable, open blocks, open stays, a 1110/<company> ledger balance',()=>{
    assert.throws(()=>coSave(seed(),'manager','CO-1',false),e=>e.reason==='MASTER_IN_USE'&&/still owes ₹62,000/.test(e.message));
    assert.throws(()=>coSave(seed(),'manager','CO-3',false),e=>e.reason==='MASTER_IN_USE'&&/open group block/.test(e.message));
    let [s,co]=newCo();
    let b=execute(s,'owner',{type:'BOOK',guest:'Co Stay (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:1,company:{account:co}});
    assert.throws(()=>coSave(b,'manager',co,false),e=>e.reason==='MASTER_IN_USE'&&/open stay/.test(e.message));
    s=execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines:[{account:'1110/'+co,debit:R(1000),credit:0},{account:'3100',debit:0,credit:R(1000)}]});
    s=execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    assert.throws(()=>coSave(s,'manager',co,false),e=>e.reason==='MASTER_IN_USE'&&/ledger balance of ₹1000\.00 Dr/.test(e.message));
  });
}
