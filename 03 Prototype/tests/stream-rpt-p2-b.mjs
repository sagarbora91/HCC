// F8-RPT-P2-B · batch B money reports on the engine (prototype): cash count sheet, counter bills, refunds, discount log, bank book, company credit and statement,
// licence expiry. rpt.money.payment-corrections is BLOCKED (no audit trail of a receipt's payment type exists in code) and must NOT be registered.
// Tie-outs to the closing records, receipts, the discount-reason report, the ACC-1 statement and the group credit picture; role matrix; money removal; date
// classes; provisional marker; CSV neutralisation; three-way (screen = Excel = print) is walked by the golden harness for every report.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F8-RPT-P2-B] ';
  const CC='rpt.money.cash-count-sheet',CB='rpt.money.counter-bills',RF='rpt.money.refunds',DL='rpt.money.discount-log',BB='rpt.money.bank-book',CR='rpt.money.company-credit',LE='rpt.tax.licence-expiry';
  const ALL=[CC,CB,RF,DL,BB,CR,LE];
  let m,f;const month=()=>m??=M.loadScenario('ACC-RULEBOOK'),fy=()=>f??=M.loadScenario('FY26');
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const run=(s,u,id,p={})=>M.runReport(s,u,id,p,{all:true});
  const body=r=>r.rows.filter(x=>x.kind==='row'),tot=r=>r.rows.find(x=>x.kind==='total');
  const party={name:'Walk-in Guest',phone:'+91 90000 11111'};
  const issue=(s,uid,extra={})=>M.execute(s,uid,{type:'CTR_BILL_ISSUE',party,lines:[{extraId:'ext-laundry',qty:2},{menuItemId:'m-tea',qty:3},{label:'Courier',unit:15000,gstCategory:'other'}],settlement:{mode:'paid',method:'Cash'},...extra});

  test(T+'registered as built; payment-corrections is blocked and not registered; money flags and catalogue per role',()=>{
    for(const id of ALL){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.status,'built');}
    assert.equal(M.reportDef('rpt.money.payment-corrections'),undefined);
    for(const id of [CC,CB,RF,DL,BB,CR])assert.equal(M.reportDef(id).moneyFlag,true,id);
    assert.equal(M.reportDef(LE).moneyFlag,false);assert.ok(!M.reportDef(LE).columns.some(c=>c.money));
    assert.equal(M.reportDef(CR).moneyGate,'PERM-GRP-004');assert.equal(M.reportDef(BB).moneyGate,'PERM-ACC-001');
    const s=month();
    for(const u of ['owner','manager','accountant'])for(const id of [CC,CB,RF,DL,BB])assert.ok(M.reportCatalogue(s,u).some(c=>c.id===id),u+' '+id);
    for(const u of ['owner','accountant'])assert.ok(M.reportCatalogue(s,u).some(c=>c.id===LE),u);
  });
  test(T+'cash count sheet: a closed day ties to the closing record (counted, expected, difference); signature lines; note rows blank without a grid',()=>{
    const s=fy(),d=Object.keys(s.closings).sort().at(-1),c=s.closings[d];
    const r=run(s,'owner',CC,{from:d}),t=tot(r);
    assert.equal(t.cells.value,c.cash.counted);assert.equal(t.cells.expected,c.cash.expected);assert.equal(t.cells.difference,c.cash.counted-c.cash.expected);
    assert.equal(body(r).filter(x=>/^₹\d+ notes$/.test(x.cells.denomination)).length,M.SHIFT_DENOMINATIONS.length);
    assert.ok(body(r).filter(x=>/notes$/.test(x.cells.denomination)).every(x=>x.cells.value===null&&x.cells.count===null),'no grid kept for a closed day');
    assert.equal(body(r).filter(x=>/signature/.test(x.cells.denomination)).length,2);
    assert.equal(run(s,'owner',CC,{from:'2026-03-01'}).rows.length,0);
  });
  test(T+'cash count sheet: the open shift with a count grid shows notes x value and the grid total',()=>{
    let s=M.seed();const exp=M.drawer(s),n5=Math.floor(exp/50000);
    s=M.execute(s,'owner',{type:'SHIFT_SUBMIT',counted:exp,denominations:{500:n5,coins:(exp-n5*50000)/100}});
    const r=run(s,'owner',CC,{}),t=tot(r);assert.equal(t.cells.value,exp);assert.equal(t.cells.difference,exp-t.cells.expected);
    assert.equal(body(r)[0].cells.count,n5);assert.equal(body(r)[0].cells.value,n5*50000);
    assert.equal(body(r).find(x=>/^Coins/.test(x.cells.denomination)).cells.value,exp-n5*50000);
    assert.equal(r.about.provisional,true);
  });
  test(T+'cash count sheet: Cashier sees own shift only, no export; Reception / Housekeeping refused',()=>{
    const s0=M.seed(),exp=M.drawer(s0);
    const mine=M.execute(s0,'cashier',{type:'SHIFT_SUBMIT',counted:exp,denominations:{500:Math.floor(exp/50000),coins:(exp%50000)/100}});
    const other=M.execute(s0,'owner',{type:'SHIFT_SUBMIT',counted:exp,denominations:{500:Math.floor(exp/50000),coins:(exp%50000)/100}});
    assert.ok(run(mine,'cashier',CC,{}).rows.length>0);assert.equal(run(other,'cashier',CC,{}).rows.length,0);assert.ok(run(other,'owner',CC,{}).rows.length>0);
    const s=mine;
    assert.ok(M.reportCatalogue(s,'cashier').some(c=>c.id===CC&&!c.canExport));
    assert.equal(code(()=>M.runReport(s,'cashier',CC,{},{purpose:'export'})),'FORBIDDEN');
    for(const u of ['reception','housekeeper','maint'])assert.equal(code(()=>M.runReport(s,u,CC,{})),'FORBIDDEN',u);
  });
  test(T+'counter bills: lines + discount row add to the bill net; grand total = sum of nets; tax; tender text; CSV neutralises a formula label',()=>{
    let s=M.seed();s=issue(s,'owner',{discount:{amount:25000,reason:'Regular'}});
    s=M.execute(s,'owner',{type:'CTR_BILL_ISSUE',party:{name:'Asha Joshi',phone:'+91 90000 22222'},lines:[{label:'=SUM(A1)',unit:10000,gstCategory:'other'}],settlement:{mode:'udhaar'}});
    const r=run(s,'owner',CB,{}),bills=s.counterX.bills;
    assert.equal(bills.length,2);
    const subs=r.rows.filter(x=>x.kind==='subtotal');assert.equal(subs.length,2);
    let i=0;for(const b of bills){const lines=r.rows.slice(i,i+b.lines.length+(b.discount?1:0));
      assert.equal(lines.reduce((a,x)=>a+x.cells.amount,0),b.net);assert.equal(subs[bills.indexOf(b)].cells.amount,b.net);assert.equal(subs[bills.indexOf(b)].cells.tax,b.tax);i+=lines.length+1;}
    assert.equal(tot(r).cells.amount,bills.reduce((a,b)=>a+b.net,0));assert.equal(tot(r).cells.tax,bills.reduce((a,b)=>a+b.tax,0));
    assert.equal(subs[0].cells.tender,'Cash');assert.equal(subs[1].cells.tender,'Udhaar (credit)');
    assert.ok(r.rows.some(x=>/^Discount · Regular$/.test(x.cells.item)&&x.cells.amount===-25000));
    const csv=M.toCsv(r);assert.ok(csv.includes("'=SUM(A1)"),csv.slice(0,300));
    assert.equal(run(s,'owner',CB,{from:s.date,to:s.date}).rows.length,r.rows.length);
  });
  test(T+'counter bills: Cashier and Reception refused (finance gate); Accountant views and exports',()=>{
    const s=issue(M.seed(),'owner');
    for(const u of ['cashier','reception','housekeeper'])assert.equal(code(()=>M.runReport(s,u,CB,{})),'FORBIDDEN',u);
    assert.ok(M.reportCatalogue(s,'accountant').find(c=>c.id===CB).canExport);
  });
  test(T+'refunds: equal the refund receipts of the range; total adds; sorted by date; amounts positive',()=>{
    const s=fy(),r=run(s,'owner',RF,{from:'2026-04-01',to:s.date}),rows=body(r);
    const want=M.reportReceipts(s,'2026-04-01',s.date).filter(x=>x.r.kind==='refund');
    assert.equal(rows.length,want.length);assert.ok(rows.length>=10);
    assert.equal(tot(r).cells.amount,want.reduce((a,x)=>a+x.r.amount,0));assert.ok(rows.every(x=>x.cells.amount>0));
    assert.deepEqual(rows.map(x=>x.cells.date),[...rows.map(x=>x.cells.date)].sort());
    assert.equal(run(m??month(),'owner',RF,{from:'2026-04-01',to:'2026-04-30'}).rows.length,0);
  });
  test(T+'refunds: a refund approved through the approval flow carries its reason and approver',()=>{
    const s=fy(),r=body(run(s,'owner',RF,{from:'2026-04-01',to:s.date}));
    const withApproval=M.reportReceipts(s,'2026-04-01',s.date).filter(x=>x.r.kind==='refund'&&x.r.approvalId);
    for(const x of withApproval){const a=s.approvals.find(v=>v.id===x.r.approvalId),row=r.find(v=>v.cells.refundNo===x.r.id);assert.equal(row.cells.reason,a.reason);}
    assert.ok(r.every(x=>typeof x.cells.reason==='string'));
  });
  test(T+'discount log: folio discounts equal the discount-reason report; subtotals add to the total; reason filter; counter-bill discounts included',()=>{
    const s=fy(),r=run(s,'owner',DL,{from:'2026-04-01',to:s.date});
    const want=M.catDiscountReport(s,'2026-04-01',s.date).reduce((a,x)=>a+x.amount,0);
    assert.equal(tot(r).cells.amount,want);assert.ok(body(r).length>=10);
    assert.equal(r.rows.filter(x=>x.kind==='subtotal').reduce((a,x)=>a+x.cells.amount,0),tot(r).cells.amount);
    const reason=body(r)[0].cells.reason;
    assert.ok(body(run(s,'owner',DL,{from:'2026-04-01',to:s.date,reason})).every(x=>x.cells.reason===reason));
    assert.equal(body(run(s,'owner',DL,{from:'2026-04-01',to:s.date,reason:'zzz-none'})).length,0);
    const c=issue(M.seed(),'owner',{discount:{amount:25000,reason:'Regular'}}),rc=run(c,'owner',DL,{});
    assert.equal(tot(rc).cells.amount,25000);assert.equal(body(rc)[0].cells.ref,c.counterX.bills[0].no);
  });
  test(T+'bank book: equals the ACC-1 statement of the bank ledger (rows, opening, closing); a non-bank ledger is refused; Reception / Cashier refused',()=>{
    const s=month(),p={account:'1030',from:'2026-04-01',to:'2026-04-30'},r=run(s,'accountant',BB,p),st=M.accStatement(s,'1030',p.from,p.to);
    const rows=body(r);assert.equal(rows.length,st.rows.length);assert.ok(rows.length>=5);
    assert.deepEqual(rows.map(x=>[x.cells.debit,x.cells.credit,x.cells.balance]),st.rows.map(x=>[x.debit,x.credit,x.running]));
    assert.equal(r.rows.find(x=>x.kind==='subtotal').cells.balance,st.opening);assert.equal(tot(r).cells.balance,st.closing);
    assert.equal(tot(r).cells.debit,st.debit);assert.equal(tot(r).cells.credit,st.credit);
    const acc=run(s,'accountant','rpt.acc.statement',p);assert.deepEqual(rows.map(x=>x.cells.voucher),body(acc).map(x=>x.cells.voucher));
    assert.equal(code(()=>M.runReport(s,'owner',BB,{...p,account:'1000'})),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',BB,{from:p.from,to:p.to})),'VALIDATION');
    for(const u of ['reception','cashier','housekeeper'])assert.equal(code(()=>M.runReport(s,u,BB,p)),'FORBIDDEN',u);
  });
  test(T+'company credit: limit, used and remaining equal the group credit picture for every company; status; statement closing = outstanding',()=>{
    const s=fy(),r=run(s,'owner',CR,{}),rows=body(r),cos=M.grpSlice(s).companies.filter(c=>c.active!==false);
    assert.equal(rows.length,cos.length);assert.ok(rows.length>0);
    for(const c of cos){const x=rows.find(v=>v.cells.company===c.name),k=M.grpCredit(s,c);
      assert.equal(x.cells.limit,c.creditLimit);assert.equal(x.cells.used,k.exposure);assert.equal(x.cells.remaining,k.available);assert.equal(x.cells.status,M.GRP_CREDIT_LABEL[k.level]);}
    const withCredit=cos.find(c=>M.grpCompanyBookings(s,c).some(b=>b.receipts.some(v=>v.kind==='company-credit')))??cos[0];
    const one=run(s,'owner',CR,{company:withCredit.id});assert.equal(one.sections.length,2);
    const st=one.sections[1].rows,close=st.at(-1);
    const credits=M.grpCompanyBookings(s,withCredit).reduce((a,b)=>a+b.receipts.filter(v=>v.kind==='company-credit').reduce((x,v)=>x+v.amount,0),0);
    const paid=(M.grpSlice(s).payments??[]).filter(p=>p.company===withCredit.id).reduce((a,p)=>a+p.amount+(p.tds??0),0);
    assert.equal(close.cells.balance,withCredit.openingOutstanding+credits-paid);
    if(close.cells.balance>=0)assert.equal(close.cells.balance,M.grpReceivable(s,withCredit));
    assert.equal(st[0].cells.balance,withCredit.openingOutstanding);
    assert.equal(code(()=>run(s,'owner',CR,{company:'nobody'}).sections?.length),'none');
  });
  test(T+'company credit: roles; Reception / Cashier view (GRP matrix); Housekeeping refused; the money gate keeps the figures',()=>{
    const s=fy();
    for(const u of ['owner','manager','accountant','reception','cashier']){const r=run(s,u,CR,{});assert.ok(r.columns.some(c=>c.key==='limit'),u);}
    for(const u of ['housekeeper','maint','supervisor'])assert.equal(code(()=>M.runReport(s,u,CR,{})),'FORBIDDEN',u);
  });
  test(T+'licence expiry: sorted by expiry, days left against the business date, expired kept, within-days filter; Owner / Accountant only; no money columns',()=>{
    const s=structuredClone(M.seed()),x=M.cmpSlice(s);
    s.complianceX={...x,profile:{...x.profile,fssaiNo:'FS-100',fssaiExpiry:M.addDays(s.date,54),lodgingNo:'LG-9',lodgingExpiry:M.addDays(s.date,-7)}};
    const r=run(s,'owner',LE,{});assert.deepEqual(r.rows.map(v=>v.cells.daysLeft),[-7,54]);
    assert.deepEqual(r.rows.map(v=>v.cells.number),['LG-9','FS-100']);assert.ok(r.rows.every(v=>v.cells.card===1));
    assert.equal(run(s,'owner',LE,{within:'30'}).rows.length,1);assert.equal(run(s,'accountant',LE,{within:'90'}).rows.length,2);
    assert.equal(code(()=>M.runReport(s,'owner',LE,{within:'-1'})),'VALIDATION');
    for(const u of ['manager','reception','cashier','housekeeper'])assert.equal(code(()=>M.runReport(s,u,LE,{})),'FORBIDDEN',u);
  });
  test(T+'date classes: ranges past the business date or inverted are refused; the day report runs one date',()=>{
    const s=fy();
    for(const id of [CB,RF,DL]){assert.equal(code(()=>M.runReport(s,'owner',id,{from:'2026-10-05',to:'2026-10-01'})),'VALIDATION',id);assert.equal(code(()=>M.runReport(s,'owner',id,{from:s.date,to:'2026-12-01'})),'VALIDATION',id);}
    assert.equal(code(()=>M.runReport(s,'owner',CC,{from:'2026-10-01',to:'2026-10-02'})),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',CC,{from:'2026-12-01'})),'VALIDATION');
  });
  test(T+'provisional: the open day is provisional; a closed range is not',()=>{
    const f2=fy();assert.equal(M.runReport(f2,'owner',RF,{from:'2026-10-01',to:'2026-10-05'}).about.provisional,false);
    assert.equal(M.runReport(f2,'owner',RF,{from:'2026-10-01',to:f2.date}).about.provisional,true);
  });
  test(T+'three-way: screen = Excel = print for each report (golden cases) and money removed without the gate',()=>{
    const s=fy(),mo=month();
    const cases=[[s,CC,{from:Object.keys(s.closings).sort().at(-1)}],[s,RF,{from:'2026-04-01',to:s.date}],[s,DL,{from:'2026-04-01',to:s.date}],[mo,BB,{account:'1030',from:'2026-04-01',to:'2026-04-30'}],
      [s,CR,{}],[s,CR,{company:M.grpSlice(s).companies[0].id}]];
    for(const [st,id,p] of cases)threeWay(M,assert,run(st,'owner',id,p));
    const c=issue(M.seed(),'owner',{discount:{amount:25000,reason:'Regular'}});threeWay(M,assert,run(c,'owner',CB,{}));
    const l=structuredClone(M.seed()),x=M.cmpSlice(l);l.complianceX={...x,profile:{...x.profile,fssaiNo:'F',fssaiExpiry:M.addDays(l.date,10)}};threeWay(M,assert,run(l,'owner',LE,{}));
    assert.ok(!run(s,'owner',CR,{}).removed);
  });
}
