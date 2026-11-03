// ACC-4 · F5-U1 · golden figures of the April 2026 month (rulebook §8, T14a / T14b) as the screens and exports show them:
// year-end carry-forward = the §8.5 balance sheet, bank reconciliation difference 0 with the CSV equal to the screen, clearing 1010 / 1020 zero,
// and the PROVISIONAL — year reopened stamp on the report exports. Names prefixed [ACC4-GOLDEN].
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const T='[ACC4-GOLDEN] ',R=r=>Math.round(r*100),HDFC='1030/BA-HDFC';
  const CSV=readFileSync(new URL('./fixtures/bank-april-2026.csv',import.meta.url),'utf8');
  let base;const month=()=>base??=M.loadScenario('ACC-RULEBOOK');
  const run=(s,uid,cmd)=>M.execute(s,uid,cmd);
  const closedYear=()=>{
    const s=structuredClone(month());const dates=M.accDates(s).filter(d=>d<='2027-03-31');
    const locks={};for(const d of dates){const vs=M.accLiveVouchers(s,d),t=M.accTotals(vs);locks[d]={date:d,lockedAt:s.clock,lockedBy:'owner',vouchers:vs,debit:t.debit,credit:t.credit,count:vs.length};}
    const months={};for(const m of new Set(dates.map(d=>d.slice(0,7))))months[m]={lockedAt:s.clock,lockedBy:'owner'};
    s.accountingX={...s.accountingX,locks,monthLocks:months};s.date='2027-04-02';s.closedDates=[...new Set([...s.closedDates,...dates])];return s;
  };
  const reconciled=()=>{
    let s=run(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',fileName:'bank-april-2026.csv',text:CSV});
    s=run(s,'accountant',{type:'BANK_AUTO_MATCH',bankLedger:HDFC});
    const upi=s.accBankX.rows.filter(r=>/^UPI SETTL/.test(r.narration)),contra=M.accBankBookLines(s,HDFC,'2026-04-30').find(l=>/UPI-SETTLE-APR/.test(l.narration));
    s=run(s,'accountant',{type:'BANK_MATCH',rowIds:upi.map(r=>r.id),refs:[contra.ref]});
    s=run(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:s.accBankX.rows.find(r=>/^INTEREST/.test(r.narration)).id,ledger:'4960'});
    return run(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:s.accBankX.rows.find(r=>/^SMS/.test(r.narration)).id,ledger:'5080'});
  };

  test(T+'year end: preview = §8.5 (79,27,900 Dr = Cr; profit 1,40,400 and drawings 50,000 to 3100), all seven checks clear; lock → opening of 2027-28 equals the lines',()=>{
    let s=closedYear();const p=M.accYearPreview(s,'2026-27');
    assert.equal(p.checks.length,7);assert.ok(p.ready,p.checks.filter(c=>!c.ok).map(c=>c.label).join());
    assert.equal(p.draft.debit,R(7927900));assert.equal(p.draft.credit,R(7927900));assert.equal(p.draft.profitToCapital,R(140400));assert.equal(p.draft.drawingsToCapital,R(50000));
    s=run(s,'owner',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});const cf=s.accYearX.carryForwards.find(c=>c.status==='draft');
    assert.deepEqual(cf.lines,p.draft.lines);
    s=run(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id});assert.equal(M.accYearOf(s,'2026-27').status,'locked');
    assert.equal(M.accYearOpening(s,'2027-28').id,cf.id);
  });
  test(T+'bank: after auto-match, one manual and two created vouchers the difference is 0.00, the CSV equals the screen figures; 1010 / 1020 are zero',()=>{
    const s=reconciled(),r=M.accBankRecon(s,HDFC,'2026-04-30');
    assert.equal(r.difference,0);assert.equal(r.booksBalance,R(501561));assert.equal(r.statementBalance,R(546561));assert.equal(r.issuedTotal,R(45000));
    const csv=M.accBankReconCsv(r).split('\r\n').filter(Boolean);
    assert.ok(csv.some(l=>l.startsWith('Balance per books')&&l.endsWith('501561.00')));
    assert.ok(csv.some(l=>l.startsWith('Balance per statement')&&l.endsWith('546561.00')));assert.ok(csv.some(l=>l.startsWith('Difference')&&l.endsWith('0.00')));
    assert.ok(csv.some(l=>l.includes('issued, not presented')&&l.endsWith('45000.00')));
    assert.equal(M.accBankClearing(s,'1010','2026-04-30').balance,0);assert.equal(M.accBankClearing(s,'1020','2026-04-30').balance,0);
  });
  test(T+'exports: Tally masters and vouchers carry "PROVISIONAL — year reopened" only while the year is reopened (report CSV / Excel: stream-accounting-ui4)',()=>{
    let s=closedYear();s=run(s,'owner',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});const cf=s.accYearX.carryForwards.find(c=>c.status==='draft');s=run(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id});
    const pl=M.accPl(s,'2026-04-01','2026-04-30');assert.equal(pl.provisional,false,'locked year: no mark in the report');
    s=run(s,'owner',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'CA found a missed purchase bill'});
    assert.ok(M.accYearProvisional(s,'2026-04-30'));assert.ok(M.accYearProvisional(s,'2027-04-30'),'the next year too');assert.ok(!M.accYearProvisional(s,'2025-04-30'));
    const x=M.accTallyVouchers(s,'2026-04-01','2026-04-30');assert.match(x.xml,/PROVISIONAL — year reopened/);
    const m=M.accTallyMasters(s,'2026-27');assert.match(m.xml,/PROVISIONAL — year reopened/);assert.equal(m.provisional,true);
    s=run(s,'owner',{type:'ACC_YEAR_RELOCK',fy:'2026-27'});
    assert.doesNotMatch(M.accTallyVouchers(s,'2026-04-01','2026-04-30').xml,/year reopened/);
  });
}
