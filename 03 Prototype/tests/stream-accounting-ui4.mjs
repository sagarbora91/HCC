// ACC-4 · F5-U1 · the Year end, Bank reconciliation and Clearing tabs (desktop + phone cards) and their dialogs, rendered over the mock state:
// registration, role gating (PERM-ACC-014…017; Manager reads the clearing view only; Housekeeping / Maintenance nothing), figures from the A1 selectors,
// provisional banner while a year is reopened, no table on the phone. Names prefixed [ACC4-UI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/acc4-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {registration} from '../../src/features/accounting/register';
import {AccountingPage,ACC_TAB_IDS} from '../../src/features/accounting/Accounting';
import {AccountingMobile} from '../../src/features/accounting/Mobile';
import {YearEndView,BankView,ClearingView,ProvisionalBanner,YearLockModal,YearRelockModal,YearReopenModal,BankIgnoreModal,BankCreateModal,BankSettleModal,BankLayoutModal} from '../../src/features/accounting/acc4-ui';
import {acc4Gates} from '../../src/features/accounting/acc4-views';
import {SUB_SCREENS,subScreenOpen} from '../../src/app/subscreens';
export {registration,ACC_TAB_IDS,acc4Gates,SUB_SCREENS,subScreenOpen};
const C:any={AccountingPage,AccountingMobile,YearEndView,BankView,ClearingView,ProvisionalBanner,YearLockModal,YearRelockModal,YearReopenModal,BankIgnoreModal,BankCreateModal,BankSettleModal,BankLayoutModal};
export function render(name:string,props:any,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'accounting',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/acc4-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc4-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __auReq} from 'node:module';const require=__auReq(import.meta.url);"}});
  const X=await import('./.generated/acc4-ui.mjs');const {render}=X;
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const T='[ACC4-UI] ',HDFC='1030/BA-HDFC',R=r=>Math.round(r*100);
  const CSV=readFileSync(new URL('./fixtures/bank-april-2026.csv',import.meta.url),'utf8');
  let base;const month=()=>base??=M.loadScenario('ACC-RULEBOOK');
  const closedYear=()=>{
    const s=structuredClone(month());const dates=M.accDates(s).filter(d=>d<='2027-03-31');
    const locks={};for(const d of dates){const vs=M.accLiveVouchers(s,d),t=M.accTotals(vs);locks[d]={date:d,lockedAt:s.clock,lockedBy:'owner',vouchers:vs,debit:t.debit,credit:t.credit,count:vs.length};}
    const months={};for(const m of new Set(dates.map(d=>d.slice(0,7))))months[m]={lockedAt:s.clock,lockedBy:'owner'};
    s.accountingX={...s.accountingX,locks,monthLocks:months};s.date='2027-04-02';s.closedDates=[...new Set([...s.closedDates,...dates])];return s;
  };
  const USERS=['owner','manager','accountant','cashier','reception','housekeeper','maint'];

  test(T+'registration: tabs year / bank / clearing in the tab list and the search palette; seven dialogs registered',()=>{
    for(const t of ['year','bank','clearing'])assert.ok(X.ACC_TAB_IDS.includes(t),t);
    assert.deepEqual(X.SUB_SCREENS.filter(x=>x.page==='accounting').map(x=>x.id),[...X.ACC_TAB_IDS],'the palette lists the same sub-screens in the same order');
    for(const m of ['acc-year-lock','acc-year-relock','acc-year-reopen','acc-bank-ignore','acc-bank-create','acc-bank-settle','acc-bank-layout'])assert.ok(X.registration.modals[m],m);
  });
  test(T+'role gates: year / bank = Owner + Accountant, clearing also Manager, layout + lock = Owner; nothing for the rest (PERM-ACC-014…017)',()=>{
    const g=r=>X.acc4Gates(r);
    assert.deepEqual([g('Owner').year,g('Owner').yearLock,g('Owner').bank,g('Owner').layout,g('Owner').clearing],[true,true,true,true,true]);
    assert.deepEqual([g('Accountant').year,g('Accountant').yearLock,g('Accountant').bank,g('Accountant').layout,g('Accountant').clearing],[true,false,true,false,true]);
    assert.deepEqual([g('Manager').year,g('Manager').bank,g('Manager').clearing],[false,false,true]);
    for(const r of ['Cashier','Reception','Housekeeping','Maintenance','Restaurant'])assert.deepEqual(Object.values(g(r)),[false,false,false,false,false],r);
    const s=month();for(const u of USERS)for(const [tab,roles] of [['year',['owner','accountant']],['bank',['owner','accountant']],['clearing',['owner','manager','accountant']]])
      assert.equal(X.subScreenOpen(s,u,'accounting',tab),roles.includes(u),u+tab);
  });
  test(T+'desktop page: Owner / Accountant get the new tabs, Manager only Clearing, others no books',()=>{
    const s=month();
    for(const u of ['owner','accountant']){const h=text(render('AccountingPage',{},s,u));for(const t of ['Year end','Bank reconciliation','Clearing'])assert.match(h,new RegExp(t),u+t);}
    const m=text(render('AccountingPage',{},s,'manager'));assert.match(m,/Clearing/);assert.doesNotMatch(m,/Year end|Bank reconciliation/);
    for(const u of ['cashier','reception','housekeeper','maint'])assert.match(text(render('AccountingPage',{},s,u)),/Not available for your role/,u);
  });
  test(T+'Year end: checklist, carry-forward preview with the golden figures, status; the Owner has Lock, the Accountant drafts only; Manager refused',()=>{
    const s=closedYear();const own=text(render('YearEndView',{},s,'owner'));
    assert.match(own,/2026-27/);assert.match(own,/Checks before the lock/);assert.match(own,/Every day of the year locked/);assert.match(own,/Draft carry-forward/);assert.match(own,/Lock 2026-27/);
    assert.match(own,/Profit ₹1,40,400/);assert.match(own,/drawings ₹50,000/);assert.match(own,/Debits equal credits/);
    const acc=render('YearEndView',{},s,'accountant');assert.match(text(acc),/Draft carry-forward/);assert.match(text(acc),/The Owner locks, reopens and relocks the year/);assert.doesNotMatch(acc,/>\s*<svg[^>]*><\/svg>\s*Lock 2026-27/);
    assert.match(text(render('YearEndView',{},s,'manager')),/Not available for your role/);
    const ph=render('YearEndView',{phone:true},s,'owner',true);assert.doesNotMatch(ph,/<table/);assert.match(text(ph),/CHECKS/);assert.match(text(ph),/CARRY-FORWARD/);
  });
  test(T+'Year end after the lock / reopen: locked status + Reopen; reopened shows the PROVISIONAL stamp and the banner',()=>{
    let s=closedYear();s=M.execute(s,'owner',{type:'ACC_YEAR_CARRY_FORWARD',fy:'2026-27'});
    const cf=s.accYearX.carryForwards.find(c=>c.status==='draft');s=M.execute(s,'owner',{type:'ACC_YEAR_LOCK',fy:'2026-27',carryForwardId:cf.id});
    let h=text(render('YearEndView',{},s,'owner'));assert.match(h,/Locked/);assert.match(h,/Reopen/);assert.match(h,/\(approved\)/);
    s=M.execute(s,'owner',{type:'ACC_YEAR_REOPEN',fy:'2026-27',reason:'CA found a missed purchase bill'});
    h=text(render('YearEndView',{},s,'owner'));assert.match(h,/PROVISIONAL — year reopened/);assert.match(h,/Lock again/);
    assert.match(text(render('ProvisionalBanner',{years:['2026-27']},s,'owner')),/PROVISIONAL — year reopened/);assert.equal(render('ProvisionalBanner',{years:[]},s,'owner'),'');
    assert.match(text(render('YearReopenModal',{id:'2026-27'},s,'owner')),/Reason \(at least 10 characters\)/);
    assert.match(text(render('YearLockModal',{id:'2026-27'},s,'owner')),/Lock 2026-27/);
  });
  test(T+'Bank: statement imported and matched → recon difference 0.00; unmatched list offers ignore / create / settlement; the Accountant cannot edit layouts',()=>{
    let s=M.execute(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',fileName:'bank-april-2026.csv',text:CSV});
    let h=text(render('BankView',{},s,'owner'));assert.match(h,/Import/);assert.match(h,/Match board/);assert.match(h,/Layouts/);assert.match(h,/Statement rows not matched \(15\)/);
    s=M.execute(s,'accountant',{type:'BANK_AUTO_MATCH',bankLedger:HDFC});
    const upi=s.accBankX.rows.filter(r=>/^UPI SETTL/.test(r.narration)),contra=M.accBankBookLines(s,HDFC,'2026-04-30').find(l=>/UPI-SETTLE-APR/.test(l.narration));
    s=M.execute(s,'accountant',{type:'BANK_MATCH',rowIds:upi.map(r=>r.id),refs:[contra.ref]});
    s=M.execute(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:s.accBankX.rows.find(r=>/^INTEREST/.test(r.narration)).id,ledger:'4960'});
    s=M.execute(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:s.accBankX.rows.find(r=>/^SMS/.test(r.narration)).id,ledger:'5080'});
    assert.equal(M.accBankRecon(s,HDFC,'2026-04-30').difference,0);
    s.date='2026-04-30';
    // the report section is a client state; the board lists matched items and the page renders without throwing for both roles
    h=text(render('BankView',{},s,'accountant'));assert.doesNotMatch(h,/Layouts/);assert.match(h,/Matched \(/);
    assert.match(text(render('BankView',{},s,'manager')),/Not available for your role/);
    const ph=render('BankView',{phone:true},s,'owner',true);assert.doesNotMatch(ph,/<table/);
    const row=s.accBankX.rows[0];
    for(const [n,re] of [['BankIgnoreModal',/Ignore this statement row/],['BankCreateModal',/Create a voucher from this row/]])assert.match(text(render(n,{id:row.id},month().accBankX?month():s,'owner')),re,n);
    const credit=M.execute(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:CSV}).accBankX.rows.find(r=>/^UPI SETTL/.test(r.narration));
    const fresh=M.execute(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:CSV});
    assert.match(text(render('BankSettleModal',{id:credit.id},fresh,'owner')),/Post a UPI \/ card settlement/);
    assert.match(text(render('BankLayoutModal',{id:'new'},fresh,'owner')),/New statement layout/);
  });
  test(T+'Clearing: 1010 / 1020 balances and ageing bands; Manager reads; phone shows cards',()=>{
    const s=month();s.date='2026-04-30';
    const v10=M.accBankClearing(s,'1010','2026-04-30'),v20=M.accBankClearing(s,'1020','2026-04-30');
    const h=text(render('ClearingView',{},s,'manager'));assert.match(h,/1010 · UPI clearing/);assert.match(h,/1020 · Card clearing/);
    for(const v of [v10,v20])assert.match(h,new RegExp(M.money(v.balance).replace(/[.₹]/g,'\\$&')));
    s.date='2026-05-30';const old=M.accBankClearing(s,'1010','2026-05-30');
    const h2=text(render('ClearingView',{},s,'owner'));if(old.open.length)assert.match(h2,/Over 7 days/);
    assert.doesNotMatch(render('ClearingView',{phone:true},s,'owner',true),/<table/);
    for(const u of ['cashier','reception','housekeeper','maint'])assert.match(text(render('ClearingView',{},s,u)),/Not available for your role/,u);
  });
}
