// ACC-3 lane acc3-pl — P&L, balance sheet, cash ratio (contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2; rulebook §7.4, §7.5, §7.9, §8.4, §8.5).
// Golden: scenario ACC-RULEBOOK (April 2026, fictional). The pay run may be dated 1 May (the lock date), so "the month" is read through the pay-run date.
import {R,PNL_84,BS_85} from './_rulebook-month.mjs';
export function register({test,M,assert}){
  const T='[ACC3-PL] ',FROM='2026-04-01';
  let cache;const month=()=>cache??=M.loadScenario('ACC-RULEBOOK');
  const payDate=s=>['2026-04-30','2026-05-01'].find(d=>M.accVouchers(s,d).some(v=>v.source.kind==='payroll'))??'2026-05-01';

  test(T+'§8.4 April 2026 P&L: revenue 5,80,000, sections as the rulebook, profit ₹1,40,400; Tally-group view sums to the same profit',()=>{
    const s=month(),p=M.accPl(s,FROM,payDate(s));
    assert.equal(p.revenue.total,PNL_84.revenue);
    assert.equal(p.expenses.consumption.total,PNL_84.consumption);assert.equal(p.expenses.employee.total,PNL_84.employee);
    assert.equal(p.expenses.finance.total,PNL_84.finance);assert.equal(p.expenses.other.total,PNL_84.other);
    assert.equal(p.expenses.depreciation.total,0);assert.equal(p.otherIncome.total,0);
    assert.equal(p.profitBeforeTax,PNL_84.profit);assert.equal(p.profitBeforeTax,R(140400));
    assert.equal(p.revenue.lines.find(l=>l.code==='4900').paise,-R(10000),'discount is a negative revenue line');
    assert.deepEqual(p.expenses.other.lines.map(l=>l.code),['5030','5040','5070','5900']);
    const inc=p.byTallyGroup.filter(g=>['sales','direct-income','indirect-income'].includes(g.group)).reduce((a,g)=>a+g.paise,0);
    const exp=p.byTallyGroup.filter(g=>!['sales','direct-income','indirect-income'].includes(g.group)).reduce((a,g)=>a+g.paise,0);
    assert.equal(inc-exp,R(140400));assert.equal(p.byTallyGroup.find(g=>g.group==='sales').paise,R(580000));
    assert.equal(p.warnings,undefined);
  });
  test(T+'§8.5 balance sheet on the pay-run date: both sides ₹79,27,900, balanced, nothing in suspense',()=>{
    const s=month(),b=M.accBalanceSheet(s,payDate(s));
    assert.equal(b.liabilities.total,BS_85.total);assert.equal(b.assets.total,BS_85.total);assert.equal(b.balanced,true);
    assert.equal(b.liabilities.ownersFunds.total,BS_85.owners);assert.equal(b.liabilities.nonCurrent.total,BS_85.loan);
    assert.equal(b.liabilities.current.total,BS_85.creditors+BS_85.duties+BS_85.advances);
    assert.equal(b.assets.nonCurrent.total,BS_85.assets.fixed);
    const ca=Object.fromEntries(b.assets.current.lines.map(l=>[l.code,l.paise]));
    assert.equal(ca['1000'],BS_85.assets.cash);assert.equal(ca['1030'],BS_85.assets.bank);assert.equal(ca['1360'],BS_85.assets.tds);assert.equal(ca['1340'],BS_85.assets.gstCash);
    assert.equal(b.suspense,0);assert.equal(b.openingDifference,0);
    assert.equal(b.liabilities.ownersFunds.lines.find(l=>l.code==='PL').paise,R(140400));
    assert.equal(b.liabilities.ownersFunds.lines.find(l=>l.code==='3000').paise,-R(50000));
  });
  test(T+'balance sheet balances on every day of April and on the seed',()=>{
    const s=month();
    for(const d of ['2026-04-01','2026-04-10','2026-04-20','2026-04-30']){const b=M.accBalanceSheet(s,d);assert.equal(b.balanced,true,d);}
    const seed=M.seed();const b=M.accBalanceSheet(seed,seed.date);assert.equal(b.liabilities.total,b.assets.total);
  });
  test(T+'P&L income and expense signs; an opening voucher is never P&L; P&L over an empty range is zero',()=>{
    const s=month(),p=M.accPl(s,'2026-03-01','2026-03-31');
    assert.equal(p.profitBeforeTax,0);assert.equal(p.revenue.lines.length,0);
    const day=M.accPl(s,FROM,FROM);assert.ok(day.revenue.lines.every(l=>l.code!=='3100'));
  });
  test(T+'cash ratio FY 2026-27: bps, cash on 1000 / 1005 only, flagged above 5 %',()=>{
    const s=month(),c=M.accCashRatio(s,'2026-27');
    assert.equal(c.fy,'2026-27');assert.ok(c.totalReceipts>0&&c.cashReceipts<=c.totalReceipts);
    assert.equal(c.receiptsPct,Math.round(c.cashReceipts*10000/c.totalReceipts));
    assert.equal(c.flagged,c.receiptsPct>500||c.paymentsPct>500);
    const z=M.accCashRatio(s,'2019-20');assert.deepEqual([z.totalReceipts,z.receiptsPct,z.flagged],[0,0,false]);
  });
  test(T+'FY helpers',()=>{
    assert.equal(M.accPlFyStart('2026-03-31'),'2025-04-01');assert.equal(M.accPlFyStart('2026-04-01'),'2026-04-01');assert.equal(M.accPlFyOf('2027-01-05'),'2026-27');
  });
}
