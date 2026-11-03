// F7-R8 · built reports moved onto the engine (prototype): closing-pack tabs (daily operations, occupancy, receipts and deposits, expenses and cash),
// cash book, closing records, banking / transfers / drawings, expenses month, restaurant day summary and closed bills, eight stock reports, audit log,
// export log, report dictionary, configuration history. Every figure ties to the old selector it wraps; three-way (screen = Excel = print) is walked
// by the golden harness for each report; role matrix, money removal and masks are checked here.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F7-R8 BUILT] ';
  const IDS=['rpt.guests.daily-ops','rpt.rooms.occupancy','rpt.money.receipts-deposits','rpt.money.expenses-cash','rpt.money.cash-book','rpt.money.closing-records','rpt.money.cash-out-register',
    'rpt.money.expense-month','rpt.rest.day-summary','rpt.rest.closed-bills','rpt.stock.stock-sheet','rpt.stock.reorder','rpt.stock.laundry','rpt.stock.counts','rpt.stock.losses',
    'rpt.stock.consumption','rpt.stock.par','rpt.stock.movements','rpt.admin.audit-log','rpt.admin.export-log','rpt.admin.report-dictionary','rpt.admin.config-history'];
  let m,f;const month=()=>m??=M.loadScenario('ACC-RULEBOOK'),fy=()=>f??=M.loadScenario('FY26');
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const run=(s,u,id,p={})=>M.runReport(s,u,id,p,{all:true});
  const body=r=>r.rows.filter(x=>x.kind==='row'),tot=r=>r.rows.find(x=>x.kind==='total');
  const tab=(p,id)=>p.tabs.find(t=>t.id===id),met=(t,id)=>t.metrics.find(x=>x.id===id).value;
  const APR={from:'2026-04-01',to:'2026-04-30'},OCT={from:'2026-10-01',to:'2026-10-24'};

  test(T+'all 22 reports are registered as built with their catalogue gates',()=>{
    for(const id of IDS){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.status,'built',id);}
    for(const id of IDS.slice(0,10).filter(i=>!/rest|closing-rec/.test(i)))assert.equal(M.reportDef(id).moneyGate,'PERM-RPT-002',id);
    assert.equal(M.reportDef('rpt.rest.day-summary').moneyGate,'PERM-POS-004');assert.equal(M.reportDef('rpt.stock.stock-sheet').moneyGate,'PERM-STK-004');
    assert.equal(M.reportDef('rpt.admin.export-log').moneyFlag,false);
  });
  test(T+'daily operations = pack tab daily (rows and tiles) for both months; occupancy total recomputes the ratios',()=>{
    for(const [s,p] of [[month(),APR],[fy(),OCT]]){
      const pack=M.reportPack(s,p.from,p.to),d=tab(pack,'daily'),r=run(s,'owner','rpt.guests.daily-ops',p);
      assert.equal(body(r).length,d.table.rows.length);
      body(r).forEach((x,i)=>{const t=d.table.rows[i];assert.deepEqual([x.cells.date,x.cells.arrivals,x.cells.departures,x.cells.occupied,x.cells.netRevenue,x.cells.netReceipts,x.cells.expenses],[t[0],t[2],t[3],t[4],t[5],t[6],t[7]]);});
      assert.equal(tot(r).cells.netRevenue,met(d,'net-revenue'));assert.equal(tot(r).cells.expenses,met(d,'expenses'));assert.equal(tot(r).cells.arrivals,body(r).reduce((a,x)=>a+x.cells.arrivals,0));
      const o=tab(pack,'occupancy'),ro=run(s,'owner','rpt.rooms.occupancy',p);
      assert.equal(tot(ro).cells.occupancy,o.ratios.find(x=>x.id==='occupancy').display);assert.equal(tot(ro).cells.revpar,o.ratios.find(x=>x.id==='revpar').display);
      assert.equal(tot(ro).cells.sellable,body(ro).reduce((a,x)=>a+x.cells.sellable,0));
    }
  });
  test(T+'receipts and deposits: a column per tender, totals = tiles; expenses and cash: cash expenses and tiles = pack tab expenses',()=>{
    const s=fy(),pack=M.reportPack(s,OCT.from,OCT.to),rc=tab(pack,'receipts'),r=run(s,'owner','rpt.money.receipts-deposits',OCT);
    const tenders=r.columns.filter(c=>c.key.startsWith('t:'));assert.ok(tenders.length>=3);
    assert.equal(tenders.reduce((a,c)=>a+tot(r).cells[c.key],0),tot(r).cells.net);assert.equal(tot(r).cells.depositsIn,met(rc,'deposit-in'));
    for(const c of tenders)assert.equal(body(r).reduce((a,x)=>a+x.cells[c.key],0),tot(r).cells[c.key]);
    const ex=tab(pack,'expenses'),re=run(s,'owner','rpt.money.expenses-cash',OCT);
    assert.equal(tot(re).cells.cashExpenses,met(ex,'cash'));assert.equal(re.sections[1].rows.length,ex.metrics.length);
    assert.equal(re.sections[1].rows.find(x=>x.cells.tile.startsWith('Cash into drawer')).cells.amount,met(ex,'cash-in'));
  });
  test(T+'cash book rows = cbRange, entries run to the carried balance; closing records = the stored records, newest first',()=>{
    for(const [s,p] of [[month(),APR],[fy(),OCT]]){
      const c=M.cbRange(s,p.from,p.to),r=run(s,'owner','rpt.money.cash-book',p);
      assert.equal(body(r).length,c.days.length);assert.equal(tot(r).cells.receipts,c.receipts);assert.equal(tot(r).cells.closing,c.closing);assert.equal(tot(r).cells.overShort,c.differences);
      const ent=r.sections[1].rows;assert.equal(ent.filter(x=>x.cells.entry==='Opening balance').length,c.days.length);
      assert.equal(ent.reduce((a,x)=>a+(x.cells.in??0),0),c.receipts);
    }
    const s=fy(),r=run(s,'owner','rpt.money.closing-records',{fy:'2026-27'});
    assert.equal(body(r).length,Object.values(s.closings).filter(x=>x.date>='2026-04-01').length);
    const d=body(r).map(x=>x.cells.date);assert.deepEqual(d,[...d].sort().reverse());
    const rec=s.closings[d[0]];assert.equal(body(r)[0].cells.netRevenue,rec.revenue.net);assert.equal(body(r)[0].cells.cashDifference,rec.cash.difference);
  });
  test(T+'banking register totals per kind = the records; expenses month = expMonthSummary',()=>{
    const s=month(),r=run(s,'owner','rpt.money.cash-out-register',{from:'2026-04-01',to:'2026-05-01'});
    const recs=M.cxSlice(s).records;assert.equal(body(r).length,recs.length);
    assert.equal(r.sections[1].rows.reduce((a,x)=>a+x.cells.amount,0),recs.filter(x=>x.status!=='Rejected').reduce((a,x)=>a+x.amount,0));
    assert.equal(body(run(s,'owner','rpt.money.cash-out-register',{from:'2026-04-01',to:'2026-05-01',kind:'owner drawing'})).length,recs.filter(x=>x.kind==='drawing').length);
    for(const [x,mo] of [[month(),'2026-04'],[fy(),'2026-10']]){const e=M.expMonthSummary(x,mo),rr=run(x,'owner','rpt.money.expense-month',{month:mo});
      assert.equal(tot(rr).cells.amount,e.expense);assert.equal(tot(rr).cells.entries,e.count);assert.equal(tot(rr).cells.itcAllowed,e.gstItc);
      assert.equal(body(rr).reduce((a,y)=>a+y.cells.amount,0),e.expense);}
  });
  test(T+'restaurant: day summary = posDaySummary; closed bills count and total = settled checks; Cashier sees only own settlements',()=>{
    const s=fy(),d=M.posDaySummary(s,'2026-10-22'),r=run(s,'owner','rpt.rest.day-summary',{from:'2026-10-22'});
    assert.equal(tot(r).cells.bills,d.checks);assert.equal(tot(r).cells.total,d.gross);assert.equal(r.sections[1].rows.reduce((a,x)=>a+x.cells.amount,0),d.gross);
    assert.equal(r.sections[2].rows.length,d.top.length);
    const cb=run(s,'owner','rpt.rest.closed-bills',{from:'2026-10-22'});assert.equal(body(cb).length,d.checks);assert.equal(tot(cb).cells.total,d.gross);
    const mine=run(s,'cashier','rpt.rest.closed-bills',{from:'2026-10-22'});assert.ok(body(mine).length<=body(cb).length);
    const mineN=M.posSlice(s).orders.filter(o=>o.status==='Settled'&&o.settlement.date==='2026-10-22'&&o.settlement.by==='cashier').length;assert.equal(body(mine).length,mineN);
    assert.equal(code(()=>M.runReport(s,'cashier','rpt.rest.day-summary',{from:'2026-10-22'})),'FORBIDDEN');
    assert.equal(code(()=>M.runReport(s,'cashier','rpt.rest.closed-bills',{from:'2026-10-22'},{purpose:'export'})),'FORBIDDEN');
    assert.equal(code(()=>M.runReport(s,'restaurant','rpt.rest.closed-bills',{from:'2026-10-22'})),'FORBIDDEN');
  });
  test(T+'stock: sheet value = sum of item values, reorder = invAlerts, losses / consumption / counts tie to the inventory selectors',()=>{
    const s=fy(),inv=M.invSlice(s),r=run(s,'owner','rpt.stock.stock-sheet');
    assert.equal(tot(r).cells.value,inv.items.reduce((a,i)=>a+M.invValue(i),0));assert.equal(body(r).length,inv.items.length);
    assert.equal(body(run(s,'owner','rpt.stock.reorder')).length,M.invAlerts(s).length);
    const lo=run(s,'owner','rpt.stock.losses',OCT);assert.equal(tot(lo).cells.value,inv.losses.filter(l=>l.date>=OCT.from&&l.date<=OCT.to).reduce((a,l)=>a+l.value,0));
    const c=M.invConsumption(s,OCT.from,OCT.to),co=run(s,'owner','rpt.stock.consumption',OCT);assert.equal(body(co).length,c.rows.length);assert.match(tot(co).cells.item,new RegExp('room-nights '+c.rn));
    assert.equal(body(run(s,'owner','rpt.stock.counts')).filter(x=>x.cells.system!==null).length,inv.counts.reduce((a,x)=>a+M.invCountVariance(x).length,0));
    assert.equal(body(run(s,'owner','rpt.stock.movements',OCT)).length,inv.moves.filter(x=>x.date>=OCT.from&&x.date<=OCT.to).length);
    assert.equal(body(run(s,'owner','rpt.stock.laundry')).length,inv.batches.length);
  });
  test(T+'stock roles: Housekeeper and Supervisor see quantities with the money columns removed; Maintenance, Reception and Cashier are refused',()=>{
    const s=fy();
    for(const u of ['housekeeper','supervisor']){const r=run(s,u,'rpt.stock.stock-sheet');assert.ok(!r.columns.some(c=>c.money),u);assert.ok(!r.columns.some(c=>c.key==='value'));assert.ok(r.removed.includes('value'));}
    for(const u of ['maint','reception','cashier','tech'])assert.equal(code(()=>M.runReport(s,u,'rpt.stock.stock-sheet')),'FORBIDDEN',u);
    assert.ok(!run(s,'housekeeper','rpt.stock.laundry').columns.some(c=>c.key==='charge'));
    assert.equal(code(()=>M.runReport(s,'housekeeper','rpt.stock.movements',OCT,{purpose:'export'})),'FORBIDDEN');
    assert.ok(run(s,'accountant','rpt.stock.stock-sheet').columns.some(c=>c.key==='value'));
  });
  test(T+'money reports: Reception, Housekeeping, Maintenance, Cashier refused; Accountant views and exports',()=>{
    const s=fy();
    for(const id of ['rpt.guests.daily-ops','rpt.rooms.occupancy','rpt.money.receipts-deposits','rpt.money.expenses-cash','rpt.money.cash-book','rpt.money.cash-out-register','rpt.money.expense-month'])
      for(const u of ['reception','housekeeper','maint','cashier'])assert.equal(code(()=>M.runReport(s,u,id,OCT.from?{...OCT,month:'2026-10'}:{})),'FORBIDDEN',u+' '+id);
    for(const id of IDS.filter(i=>!/export-log|config-history|closing-rec|stock|dictionary/.test(i)))assert.ok(M.reportCatalogue(s,'accountant').find(c=>c.id===id)?.canExport,id);
  });
  test(T+'audit log = avFilter rows; Accountant sees detail, Reception is refused; export log is Owner and Manager only and shows what RPT_LOG recorded',()=>{
    const s=fy(),f=M.avFilter(s,{from:OCT.from,to:OCT.to}),r=run(s,'owner','rpt.admin.audit-log',OCT);
    assert.equal(body(r).length,f.length);assert.equal(code(()=>M.runReport(s,'reception','rpt.admin.audit-log',OCT)),'FORBIDDEN');
    assert.equal(body(run(s,'owner','rpt.admin.audit-log',{...OCT,text:'zzzz-no-such'})).length,0);
    assert.equal(code(()=>M.runReport(s,'accountant','rpt.admin.export-log',{})),'FORBIDDEN');assert.equal(code(()=>M.runReport(s,'reception','rpt.admin.export-log',{})),'FORBIDDEN');
    const {execute}=M;const t=structuredClone(s);
    const out=execute(t,'owner',{type:'RPT_LOG',reportId:'rpt.money.cash-book',params:APR,format:'xlsx',rows:30,fileName:'x.xlsx'});
    const s2=out.state??out;
    const e=run(s2,'manager','rpt.admin.export-log',{from:s2.date,to:s2.date});
    assert.equal(body(e).length,1);assert.equal(body(e)[0].cells.report,'Cash book');assert.equal(body(e)[0].cells.format,'xlsx');assert.equal(body(e)[0].cells.rows,30);
    assert.equal(run(s,'owner','rpt.admin.report-dictionary').rows.length,M.reportDictionary.length);
  });
  test(T+'CSV neutralises a formula in an audit detail',()=>{
    const s=structuredClone(fy());s.audit.push({id:'ax1',at:s.date+'T10:00',actor:'Owner',action:'TEST',detail:'=HYPERLINK("x")'});
    const r=run(s,'owner','rpt.admin.audit-log',{from:s.date,to:s.date});assert.ok(M.toCsv(r).includes("'=HYPERLINK"));
  });
  test(T+'three-way (screen = Excel = print) for every report on its golden cases',()=>{
    let n=0;
    for(const id of IDS){const cases={'rpt.rest.day-summary':[fy(),'owner',{from:'2026-10-22'}],'rpt.rest.closed-bills':[fy(),'owner',{from:'2026-10-22'}],'rpt.money.closing-records':[fy(),'owner',{fy:'2026-27'}],'rpt.money.expense-month':[fy(),'owner',{month:'2026-10'}],
      'rpt.money.cash-out-register':[month(),'owner',{from:'2026-04-01',to:'2026-05-01'}]}[id]??[fy(),'owner',OCT];
      threeWay(M,assert,run(cases[0],cases[1],id,cases[2]));n++;}
    assert.equal(n,22);
  });
}
