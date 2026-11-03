// F7-R4 · batch 1 money reports on the engine (prototype): shift report (X / Z + hand-over), collection detail, credit bills, expense register,
// expenses by head. Tie-outs to the closing report pack (daily collections, expenses), the closing records and the dated ledgers; role matrix;
// money removal; Cashier own-shift view without export; three-way (screen = Excel = print) is walked by the golden harness for every report.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F7-R4 MONEY-A] ';
  const SH='rpt.money.shift-report',CD='rpt.money.collection-detail',CB='rpt.money.credit-bills',ER='rpt.money.expense-register',EH='rpt.money.expenses-by-head';
  let m,f;const month=()=>m??=M.loadScenario('ACC-RULEBOOK'),fy=()=>f??=M.loadScenario('FY26');
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const run=(s,u,id,p={})=>M.runReport(s,u,id,p,{all:true});
  const tab=(p,id)=>p.tabs.find(t=>t.id===id),met=(t,id)=>t.metrics.find(x=>x.id===id).value;
  const body=r=>r.rows.filter(x=>x.kind==='row'),tot=r=>r.rows.find(x=>x.kind==='total');

  test(T+'registered as built, money-gated, in the catalogue for Owner / Manager / Accountant',()=>{
    for(const id of [SH,CD,CB,ER,EH]){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.status,'built');assert.equal(d.moneyFlag,true);}
    const s=month();for(const u of ['owner','manager','accountant'])for(const id of [SH,CD,CB,ER,EH])assert.ok(M.reportCatalogue(s,u).some(c=>c.id===id),u+' '+id);
  });
  test(T+'shift report: every closed day ties to the closing record; opening + receipts - payments = expected (both months); totals add up',()=>{
    for(const s of [month(),fy()]){
      const r=run(s,'owner',SH,{from:'2026-04-01',to:s.date}),rows=body(r);
      assert.ok(rows.length>=30);
      for(const x of rows){const c=s.closings[x.cells.date];if(!c)continue;
        assert.equal(x.cells.opening,c.cash.opening);assert.equal(x.cells.expected,c.cash.expected);assert.equal(x.cells.counted,c.cash.counted);assert.equal(x.cells.difference,c.cash.difference);
        assert.equal(x.cells.opening+x.cells.receipts-x.cells.payments,x.cells.expected,x.cells.date);}
      assert.equal(tot(r).cells.receipts,rows.reduce((a,x)=>a+x.cells.receipts,0));
      const per=r.sections[1].rows;assert.equal(per.reduce((a,x)=>a+x.cells.receipts,0),tot(r).cells.receipts);assert.equal(per.reduce((a,x)=>a+x.cells.shifts,0),rows.length);
    }
  });
  test(T+'shift report: receipts / payments equal the closing pack drawer metrics for the range',()=>{
    const s=month(),r=run(s,'owner',SH,{from:'2026-04-01',to:'2026-04-30'}),ex=tab(M.reportPack(s,'2026-04-01','2026-04-30'),'expenses');
    assert.equal(tot(r).cells.receipts,met(ex,'cash-in'));
    assert.equal(tot(r).cells.payments,-met(ex,'cash-out')+met(ex,'cash')-body(r).reduce((a,x)=>a+M.cxDeskNet(s,x.cells.date),0));
  });
  test(T+'shift report: the open day is an X reading with no difference until counted; filters by cashier and shift',()=>{
    const s=M.seed(),r=run(s,'owner',SH,{});const x=body(r).at(-1);
    assert.equal(x.cells.date,s.date);assert.match(x.cells.shift,/X/);assert.equal(x.cells.expected,M.shiftExpected(s).expected);
    const f=fy(),all=body(run(f,'owner',SH,{from:'2026-10-01',to:f.date}));
    const who=all[0].cells.cashier;
    assert.ok(body(run(f,'owner',SH,{from:'2026-10-01',to:f.date,cashier:f.users.find(u=>u.name.startsWith(who)).id})).length>0);
    assert.equal(body(run(f,'owner',SH,{from:'2026-10-01',to:f.date,cashier:'nobody'})).length,0);
  });
  test(T+'Cashier: own-shift view and print of the shift report and the receipts register; no export; nothing else',()=>{
    const s=fy(),r=M.runReport(s,'cashier',SH,{from:'2026-10-01',to:s.date},{all:true});
    assert.ok(body(r).every(x=>x.cells.cashier==='Priya'));
    const cat=M.reportCatalogue(s,'cashier');assert.ok(cat.some(c=>c.id===SH&&!c.canExport));assert.ok(cat.some(c=>c.id===CD&&!c.canExport));
    for(const id of [CB,ER,EH])assert.equal(code(()=>M.runReport(s,'cashier',id,{})),'FORBIDDEN',id);
    assert.equal(code(()=>M.runReport(s,'cashier',SH,{},{purpose:'export'})),'FORBIDDEN');
    const cd=run(s,'cashier',CD,{from:'2026-10-01',to:s.date});assert.ok(body(cd).every(x=>x.cells.by==='Priya'));
  });
  test(T+'roles: Reception, Housekeeping, Maintenance refused on every report; Accountant views and exports',()=>{
    const s=month();
    for(const u of ['reception','housekeeper','supervisor','maint'])for(const id of [SH,CD,CB,ER,EH])assert.equal(code(()=>M.runReport(s,u,id,{})),'FORBIDDEN',u+' '+id);
    for(const id of [SH,CD,CB,ER,EH])assert.ok(M.reportCatalogue(s,'accountant').find(c=>c.id===id).canExport,id);
  });
  test(T+'rule M: all five reports are finance-gated; without PERM-RPT-002 they are refused (nothing to blank), and an export deny keeps the view',()=>{
    const s=structuredClone(month());s.policy=M.adminDefaultPolicy(s.clock);
    s.policy.exceptions.push({id:'X9',user:'manager',perm:'PERM-RPT-002',effect:'deny',reason:'test',expires:'2099-01-01',by:'owner',at:s.clock});
    for(const id of [SH,CD,CB,EH])assert.equal(code(()=>M.runReport(s,'manager',id,{})),'FORBIDDEN',id);
    const t=structuredClone(month());t.policy=M.adminDefaultPolicy(t.clock);
    t.policy.exceptions.push({id:'X8',user:'accountant',perm:'PERM-RPT-003',effect:'deny',reason:'test',expires:'2099-01-01',by:'owner',at:t.clock});
    for(const id of [SH,CD,CB,ER,EH]){assert.ok(M.runReport(t,'accountant',id,{}));assert.equal(code(()=>M.runReport(t,'accountant',id,{},{purpose:'export'})),'FORBIDDEN',id);}
  });
  test(T+'collection detail: receipts + corrections of stays equal Receipts & deposits net receipts; advances equal deposits in less returned (plus applied)',()=>{
    for(const s of [month(),fy()]){
      const f='2026-04-01',t=s.date,pack=M.reportPack(s,f,t),rc=tab(pack,'receipts');
      const stay=(inc)=>body(run(s,'owner',CD,{from:f,to:t,include:inc})).filter(x=>x.cells.source==='stay').reduce((a,x)=>a+x.cells.amount,0);
      assert.equal(stay('receipts')+stay('corrections'),met(rc,'net'));
      assert.equal(stay('advances'),met(rc,'deposit-in')+met(rc,'deposit-out')+M.reportReceipts(s,f,t).filter(x=>x.r.kind==='deposit-apply').reduce((a,x)=>a+x.r.amount,0));
    }
  });
  test(T+'collection detail: group subtotals add to the grand total; summary by tender equals it; group by bill / user keeps the total; TDS carried',()=>{
    const s=month(),p={from:'2026-04-01',to:'2026-04-30'};
    for(const g of ['tender','bill','user']){const r=run(s,'owner',CD,{...p,groupBy:g});
      assert.equal(r.rows.filter(x=>x.kind==='subtotal').reduce((a,x)=>a+x.cells.amount,0),tot(r).cells.amount,g);
      assert.equal(r.sections[1].rows.reduce((a,x)=>a+x.cells.amount,0),tot(r).cells.amount,g);}
    const r=run(s,'owner',CD,p);assert.ok(tot(r).cells.tds>0,'the April month has a TDS-deducted company payment');
    assert.equal(body(run(s,'owner',CD,{...p,tender:'Cash'})).every(x=>x.cells.tender==='Cash'),true);
  });
  test(T+'collection detail: guest names masked without billing / reservations; ID numbers never appear',()=>{
    const s=fy(),r=run(s,'owner',CD,{from:'2026-10-01',to:s.date});assert.ok(body(r).some(x=>x.cells.source==='stay'&&x.cells.guest&&!x.cells.guest.includes('•')));
    assert.ok(!JSON.stringify(M.toXlsx(r,'P')).match(/\d{4} ?\d{4} ?\d{4}/));
  });
  test(T+'credit bills: company credit equals the company-credit receipts; used-after is the running balance; limit from the company master',()=>{
    const s=month(),r=run(s,'owner',CB,{from:'2026-04-01',to:'2026-04-30'}),rows=body(r);
    const rec=M.reportReceipts(s,'2026-04-01','2026-04-30').filter(x=>x.r.kind==='company-credit').reduce((a,x)=>a+x.r.amount,0);
    assert.equal(rows.reduce((a,x)=>a+x.cells.credit,0),rec);assert.equal(tot(r).cells.credit,rec);
    for(let i=1;i<rows.length;i++)if(rows[i].cells.party===rows[i-1].cells.party)assert.equal(rows[i].cells.used-rows[i-1].cells.used,rows[i].cells.credit);
    const co=M.grpSlice(s).companies.find(c=>c.name===rows[0].cells.party);assert.equal(rows[0].cells.limit,co.creditLimit);
    assert.equal(body(run(s,'owner',CB,{from:'2026-04-01',to:'2026-04-30',party:'zzz'})).length,0);
  });
  test(T+'expense register: counted total equals the closing pack expenses (April month and FY26); filters; GST and ITC columns',()=>{
    for(const s of [month(),fy()]){
      const f='2026-04-01',t=s.date,r=run(s,'owner',ER,{from:f,to:t});
      assert.equal(tot(r).cells.amount,met(tab(M.reportPack(s,f,t),'expenses'),'total'));
      assert.equal(r.rows.filter(x=>x.kind==='subtotal').reduce((a,x)=>a+x.cells.amount,0),tot(r).cells.amount);
    }
    const s=month(),all=body(run(s,'owner',ER,{from:'2026-04-01',to:'2026-04-30'}));
    assert.ok(all.some(x=>x.cells.gst>0&&x.cells.gstRate>0&&['allowed','disallowed'].includes(x.cells.itc)));
    const cat=all[0].cells.category;assert.ok(body(run(s,'owner',ER,{from:'2026-04-01',to:'2026-04-30',category:cat})).every(x=>x.cells.category===cat));
    assert.equal(body(run(s,'owner',ER,{from:'2026-04-01',to:'2026-04-30',status:'Rejected'})).every(x=>x.cells.status==='Rejected'),true);
    assert.equal(code(()=>M.runReport(s,'owner',ER,{status:'Nope'})),'VALIDATION');
  });
  test(T+'expense register: a Pending expense is listed but not totalled',()=>{
    let s=M.seed();s=M.execute(s,'reception',{type:'EXPENSE',description:'Register test',vendor:'Test Vendor',category:'Supplies',amount:12300,method:'Cash'});
    const r=run(s,'owner',ER,{});const row=body(r).find(x=>x.cells.description==='Register test');assert.equal(row.cells.status,'Pending');
    assert.equal(tot(r).cells.amount,body(r).filter(x=>['Posted','Reversed'].includes(x.cells.status)).reduce((a,x)=>a+x.cells.amount,0));
  });
  test(T+'expenses by head: grand total equals the closing pack expenses; heads add to groups; shares add to 100; filters',()=>{
    for(const s of [month(),fy()]){
      const f='2026-04-01',t=s.date,r=run(s,'owner',EH,{from:f,to:t});
      assert.equal(tot(r).cells.amount,met(tab(M.reportPack(s,f,t),'expenses'),'total'));
      assert.equal(r.rows.filter(x=>x.kind==='subtotal').reduce((a,x)=>a+x.cells.amount,0),tot(r).cells.amount);
      const sh=body(r).reduce((a,x)=>a+x.cells.share,0);assert.ok(Math.abs(sh-100)<1,String(sh));
      assert.equal(tot(r).cells.entries,body(r).reduce((a,x)=>a+x.cells.entries,0));
    }
    const s=month(),h=body(run(s,'owner',EH,{from:'2026-04-01',to:'2026-04-30'}))[0].cells.head;
    assert.ok(body(run(s,'owner',EH,{from:'2026-04-01',to:'2026-04-30',head:h})).every(x=>x.cells.head===h));
    assert.equal(code(()=>M.runReport(s,'owner',EH,{group:'Nope'})),'VALIDATION');
  });
  test(T+'date classes: collection detail / shift / credit / register run to 366 days (D-B); ranges past the business date or inverted are refused',()=>{
    const s=fy();
    for(const id of [SH,CD,CB,ER,EH]){assert.equal(code(()=>M.runReport(s,'owner',id,{from:'2026-10-05',to:'2026-10-01'})),'VALIDATION',id);assert.equal(code(()=>M.runReport(s,'owner',id,{from:s.date,to:'2026-12-01'})),'VALIDATION',id);}
    assert.equal(code(()=>M.runReport(s,'owner',EH,{from:'2025-01-01',to:s.date})),'VALIDATION','over 366 days');
  });
  test(T+'provisional: today open day is provisional; a closed range is not',()=>{
    const s=M.seed();assert.equal(M.runReport(s,'owner',SH,{}).about.provisional,true);
    const f=fy();assert.equal(M.runReport(f,'owner',ER,{from:'2026-10-01',to:'2026-10-05'}).about.provisional,false);
  });
  test(T+'CSV: formula text in an expense description is neutralised in the register CSV; money in rupees',()=>{
    let s=M.seed();s=M.execute(s,'reception',{type:'EXPENSE',description:'=SUM(A1)',vendor:'V',category:'Supplies',amount:12300,method:'Cash'});
    const csv=M.toCsv(run(s,'owner',ER,{}));assert.ok(csv.includes("'=SUM(A1)"),csv.slice(0,400));assert.ok(csv.includes('123.00'));
  });
  test(T+'three-way: screen = Excel = print for each report on the April month and FY26 (golden cases)',()=>{
    for(const [s,p] of [[month(),{from:'2026-04-01',to:'2026-04-30'}],[fy(),{from:'2026-10-01',to:'2026-10-24'}]])
      for(const id of [SH,CD,CB,ER,EH])threeWay(M,assert,run(s,'owner',id,p));
  });
}
