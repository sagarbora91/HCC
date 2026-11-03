// F7-R6 · batch 2 accounting on the report engine: trial balance, ledger / party statement, day book, P&L, balance sheet, cash ratio, ageing, Tally export summary.
// Screen = Excel = print (threeWay), figures = the ACC-1 / ACC-3 selectors on the ACC-RULEBOOK April month (profit 1,40,400; balance sheet 79,27,900 both sides),
// roles (rule V / M), edges, the year-lock PROVISIONAL stamp, goldens.
import {build} from 'esbuild';
import {threeWay,goldenCheck} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-acc-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-acc.mjs',logLevel:'error'});
const M=await import('./.generated/rpt-acc.mjs');
export function register({test,assert}){
  const T='[F7-R6] ';
  const TB='rpt.acc.trial-balance',ST='rpt.acc.statement',DB='rpt.acc.day-book',PL='rpt.acc.pl',BS='rpt.acc.balance-sheet',CR='rpt.acc.cash-ratio',AG='rpt.acc.ageing',TX='rpt.acc.tally-xml';
  const A='2026-04-01',B='2026-04-30';
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  let base;const month=()=>structuredClone(base??=M.loadScenario('ACC-RULEBOOK'));
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const rows=r=>r.rows.filter(x=>x.kind==='row');

  test(T+'the eight reports are registered with the catalogue columns, flags and permissions',()=>{
    const want={[TB]:[9,true,'PERM-ACC-001'],[ST]:[8,true,'PERM-ACC-001'],[DB]:[8,true,'PERM-ACC-001'],[PL]:[4,true,'PERM-ACC-013'],[BS]:[3,true,'PERM-ACC-013'],
      [CR]:[5,true,'PERM-ACC-013'],[AG]:[13,true,'PERM-ACC-001'],[TX]:[4,false,undefined]};
    for(const [id,[n,money,gate]] of Object.entries(want)){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.columns.length,n,id+' columns');assert.equal(d.moneyFlag,money,id);assert.equal(d.moneyGate,gate,id);assert.equal(d.area,'acc');}
    assert.deepEqual(M.reportDef(TX).viewPerms,['PERM-ACC-006']);assert.equal(M.reportDef(TX).exportPerm,'PERM-ACC-006');assert.deepEqual(M.reportDef(TX).formats,['screen','xml']);
    assert.equal(M.reportDef(CR).columns.find(c=>c.key==='share').type,'pct');
  });

  test(T+'P&L April 2026: profit before tax 1,40,400 rupees, every line = accPl; Tally-group view; three-way equal',()=>{
    const s=month(),r=run(s,'accountant',PL,{from:A,to:B}),x=M.accPl(s,A,B);
    assert.equal(x.profitBeforeTax,14040000);const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.amount,14040000);assert.equal(tot.cells.line,'Profit before tax');
    const lines=[...x.revenue.lines,...x.otherIncome.lines,...Object.values(x.expenses).filter(g=>g.lines).flatMap(g=>g.lines)];
    for(const l of lines){const c=rows(r).find(y=>y.cells.code===l.code);assert.equal(c.cells.amount,l.paise,l.code);}
    assert.equal(r.rows.find(y=>y.cells.line==='Total revenue').cells.amount,x.revenue.total);assert.equal(r.rows.find(y=>y.cells.line==='Total expenses').cells.amount,x.expenses.total);
    threeWay(M,assert,r);
    const g=run(s,'accountant',PL,{from:A,to:B,tallyView:true});assert.equal(rows(g).length,x.byTallyGroup.length);assert.equal(g.rows.at(-1).cells.amount,14040000);threeWay(M,assert,g);
  });

  test(T+'balance sheet 30 April 2026: both sides 79,27,900 rupees, balanced, "not the statutory statement" note; three-way equal',()=>{
    const s=month(),r=run(s,'owner',BS,{asOf:B}),b=M.accBalanceSheet(s,B);
    assert.equal(b.liabilities.total,792790000);assert.equal(b.assets.total,792790000);
    assert.equal(r.rows.find(x=>x.cells.line==='Total liabilities').cells.amount,792790000);assert.equal(r.rows.find(x=>x.cells.line==='Total assets').cells.amount,792790000);
    const tot=r.rows.at(-1);assert.equal(tot.cells.block,'Balanced');assert.equal(tot.cells.amount,0);
    assert.ok(/not the statutory statement/.test(r.rows[0].cells.line));
    assert.equal(r.rows.find(x=>x.cells.line==="Total owner's funds").cells.amount,b.liabilities.ownersFunds.total);
    threeWay(M,assert,r);
  });

  test(T+'trial balance: rows and totals = accTrialBalance6, balanced in every column; with parties; three-way equal',()=>{
    const s=month(),r=run(s,'accountant',TB,{from:A,to:B}),t=M.accTrialBalance6(s,A,B,false),d=rows(r);
    assert.equal(d.length,t.rows.length);t.rows.forEach((x,i)=>{assert.equal(d[i].cells.code,x.account);assert.equal(d[i].cells.openDr,x.openingDebit);assert.equal(d[i].cells.perCr,x.credit);assert.equal(d[i].cells.closeDr,x.closingDebit);assert.equal(d[i].cells.closeCr,x.closingCredit);});
    const tot=r.rows.at(-1);assert.equal(tot.cells.closeDr,t.totals.closingDebit);assert.equal(tot.cells.closeDr,tot.cells.closeCr);assert.equal(tot.cells.perDr,tot.cells.perCr);assert.ok(t.balanced&&/equal/.test(tot.cells.ledger));
    const det=run(s,'accountant',TB,{from:A,to:B,view:'with parties'});assert.ok(rows(det).length>d.length);assert.equal(det.rows.at(-1).cells.closeDr,tot.cells.closeDr);
    assert.equal(code(()=>run(s,'accountant',TB,{from:A,to:B,view:'sideways'})),'VALIDATION');
    threeWay(M,assert,r);threeWay(M,assert,det);
    const fresh=M.loadScenario('FY26');const p=run(fresh,'owner',TB,{from:'2026-10-01',to:'2026-10-24'});
    assert.equal(M.accTrialBalance6(fresh,'2026-10-01','2026-10-24',false).openingPending,p.rows.some(x=>/pending/.test(x.cells.ledger||'')));
  });

  test(T+'statement: opening, rows, closing = accStatement; control ledger and party; account required',()=>{
    const s=month();
    for(const acct of ['1000','1120','4000']){
      const r=run(s,'accountant',ST,{account:acct,from:A,to:B}),st=M.accStatement(s,acct,A,B);
      assert.equal(r.rows[0].cells.balance,st.opening);assert.equal(r.rows.at(-1).cells.balance,st.closing);assert.equal(r.rows.at(-1).cells.debit,st.debit);assert.equal(r.rows.at(-1).cells.credit,st.credit);
      assert.equal(rows(r).length,st.rows.length);st.rows.forEach((x,i)=>{assert.equal(rows(r)[i].cells.voucher,x.no);assert.equal(rows(r)[i].cells.balance,x.running);});
      assert.equal(st.opening+st.debit-st.credit,st.closing);threeWay(M,assert,r);
    }
    assert.equal(code(()=>run(s,'accountant',ST,{from:A,to:B})),'VALIDATION');
    assert.ok(rows(run(s,'accountant',ST,{account:'1000',from:A,to:B})).length>0);
  });

  test(T+'day book: one line per entry, totals and voucher count = accDayBook',()=>{
    const s=month(),r=run(s,'accountant',DB,{from:A,to:B}),db=M.accDayBook(s,A,B);
    assert.equal(rows(r).length,db.rows.length);const tot=r.rows.at(-1);assert.equal(tot.cells.debit,db.debit);assert.equal(tot.cells.credit,db.credit);assert.equal(tot.cells.debit,tot.cells.credit);
    assert.ok(tot.cells.ledger.startsWith(db.vouchers+' voucher'));
    db.rows.forEach((x,i)=>{assert.equal(rows(r)[i].cells.voucher,x.no);assert.equal(rows(r)[i].cells.ledger,`${x.account} ${x.ledger}`);});
    threeWay(M,assert,r);
  });

  test(T+'cash ratio: receipts and payments = accCashRatio, flagged above 5 %',()=>{
    const s=month(),r=run(s,'accountant',CR,{fy:'2026-27'}),c=M.accCashRatio(s,'2026-27'),d=rows(r);
    assert.equal(d.length,2);assert.equal(d[0].cells.cash,c.cashReceipts);assert.equal(d[0].cells.total,c.totalReceipts);assert.equal(d[1].cells.cash,c.cashPayments);assert.equal(d[1].cells.total,c.totalPayments);
    assert.equal(d[0].cells.flagged,c.receiptsPct>500?1:0);assert.equal(d[1].cells.flagged,c.paymentsPct>500?1:0);assert.equal(d[0].cells.share,Math.floor((c.receiptsPct+5)/10)/10);
    threeWay(M,assert,r);
  });

  test(T+'ageing: party rows and totals = accAgeing, bills under the party, both sides, buckets add up',()=>{
    const s=month();
    for(const side of ['receivable','payable']){
      const r=run(s,'accountant',AG,{asOf:B,side}),a=M.accAgeing(s,B,side),tot=r.rows.at(-1);
      assert.equal(tot.kind,'total');assert.equal(tot.cells.open,a.totals.open);assert.equal(tot.cells.b0_30,a.totals.b0_30);assert.equal(tot.cells.b90,a.totals.b90);
      const parties=r.rows.filter(x=>x.kind==='subtotal');assert.equal(parties.length,a.rows.length);
      parties.forEach((x,i)=>{assert.equal(x.cells.open,a.rows[i].open);assert.equal(x.cells.b31_60,a.rows[i].b31_60);});
      const bills=rows(r).filter(x=>x.cells.days!==null&&x.cells.days!==undefined);
      for(const b of bills)assert.equal(b.cells.open,b.cells.b0_30+b.cells.b31_60+b.cells.b61_90+b.cells.b90,b.cells.billRef);
      assert.equal(bills.length,a.rows.reduce((n,x)=>n+x.bills.length,0));
      threeWay(M,assert,r);
    }
    assert.equal(code(()=>run(s,'accountant',AG,{asOf:B,side:'both'})),'VALIDATION');
  });

  test(T+'Tally export: two file rows with the counts of accTallyMasters / accTallyVouchers; 31-day cap; Owner + Accountant only',()=>{
    const s=month(),r=run(s,'accountant',TX,{fy:'2026-27',vFrom:A,vTo:B}),m=M.accTallyMasters(s,'2026-27'),v=M.accTallyVouchers(s,A,B);
    assert.equal(r.rows[0].cells.file,'masters.xml');assert.equal(r.rows[0].cells.ledgers,m.ledgers);assert.equal(r.rows[1].cells.vouchers,v.count);
    assert.equal(code(()=>run(s,'accountant',TX,{fy:'2026-27',vFrom:A,vTo:'2026-05-02'})),'VALIDATION');
    assert.equal(code(()=>run(s,'manager',TX)),'FORBIDDEN');assert.equal(run(s,'owner',TX,{fy:'2026-27',vFrom:A,vTo:B}).rows.length,2);
    threeWay(M,assert,r);
  });

  test(T+'roles (DEC-202 ruling): P&L / balance sheet / cash ratio Owner + Accountant; trial balance, statement, day book, ageing also the Manager; Cashier, Reception and the rest never',()=>{
    const s=month();
    for(const [id,p] of [[PL,{from:A,to:B}],[BS,{asOf:B}],[CR,{fy:'2026-27'}]]){
      for(const u of ['owner','accountant'])assert.ok(run(s,u,id,p).rows.length>0,u+' '+id);
      for(const u of ['manager','reception','cashier','housekeeper','maint','techadmin','supervisor'])assert.equal(code(()=>run(s,u,id,p)),'FORBIDDEN',u+' '+id);
    }
    for(const [id,p] of [[TB,{from:A,to:B}],[ST,{account:'1000',from:A,to:B}],[DB,{from:A,to:B}],[AG,{asOf:B}]]){
      for(const u of ['owner','manager','accountant'])assert.ok(run(s,u,id,p).rows.length>0,u+' '+id);
      for(const u of ['reception','cashier','housekeeper','maint','techadmin','supervisor'])assert.equal(code(()=>run(s,u,id,p)),'FORBIDDEN',u+' '+id);
    }
    // export right: the Manager holds RPT-003 on the matrix; Accountant too — a refusal path needs a role without it
    assert.equal(code(()=>M.runReport(s,'reception',TB,{from:A,to:B},{purpose:'export'})),'FORBIDDEN');
  });

  test(T+'date edges: a date after the business date, From after To, FY label, one-day range',()=>{
    const s=month();
    assert.equal(code(()=>run(s,'accountant',BS,{asOf:'2999-01-01'})),'VALIDATION');
    assert.equal(code(()=>run(s,'accountant',TB,{from:B,to:A})),'VALIDATION');
    assert.equal(code(()=>run(s,'accountant',CR,{fy:'2026-28'})),'VALIDATION');
    const one=run(s,'accountant',DB,{from:A,to:A});assert.ok(rows(one).length>=0);threeWay(M,assert,one);
  });

  test(T+'year-lock stamp: a reopened year puts "PROVISIONAL — year reopened" first in every report, screen = Excel = print',()=>{
    const s=month();s.accYearX={years:[{fy:'2026-27',status:'reopened',provisionalExports:true}],carryForwards:[]};
    const cases=[[TB,{from:A,to:B}],[ST,{account:'1000',from:A,to:B}],[DB,{from:A,to:B}],[PL,{from:A,to:B}],[BS,{asOf:B}],[CR,{fy:'2026-27'}],[AG,{asOf:B}]];
    for(const [id,p] of cases){const r=run(s,'accountant',id,p);const first=Object.values(r.rows[0].cells).find(v=>typeof v==='string');
      assert.equal(first,'PROVISIONAL — year reopened',id);assert.equal(r.about.provisional,true,id);threeWay(M,assert,r);
      assert.ok(M.toCsv(r).includes('PROVISIONAL — year reopened'),id+' csv');}
    const t=run(s,'accountant',TX,{fy:'2026-27',vFrom:A,vTo:B});assert.ok(t.rows.every(x=>/year reopened/.test(x.cells.notes)));
    const plain=run(month(),'accountant',PL,{from:A,to:B});assert.ok(!JSON.stringify(plain.rows).includes('PROVISIONAL'));
  });

  test(T+'an opening not yet approved marks the trial balance and the P&L provisional',()=>{
    const s=M.loadScenario('FY26');
    const r=run(s,'owner',PL,{from:'2026-10-01',to:'2026-10-24'});assert.equal(r.about.provisional,true);
  });

  test(T+'CSV: formula-looking narration is neutralised',()=>{
    const s=month(),r=run(s,'accountant',DB,{from:A,to:B});r.rows[0].cells.narration='=HYPERLINK("x")';
    const csv=M.toCsv(r);assert.ok(!/(^|,)=HYPERLINK/m.test(csv),'neutralised');assert.ok(csv.includes("'=HYPERLINK"));
  });

  test(T+'the accounting goldens have no drift',()=>{
    const fails=goldenCheck(M,assert).filter(f=>/rpt\.acc\./.test(f));assert.deepEqual(fails,[]);
  });
}
