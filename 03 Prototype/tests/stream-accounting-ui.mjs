// ACC-1 · acc1-ui — the accounting screens (desktop and phone), rendered server-side over the mock state with the contract stubs:
// registration of pages and modals, permission gating per role (Cashier never sees the books, DEC-079; housekeeping / maintenance never),
// the books (six-column trial balance, statements, day book), chart of accounts (Owner-only editing), parties, opening balances
// (Save for Owner / Manager / Accountant, Approve / Reopen for the Owner, the automatic 1400 line). Names prefixed [ACC1-UI].
import {build} from 'esbuild';
import {writeFile,readFile,readdir} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/acc-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {registration} from '../../src/features/accounting/register';
import {AccountingPage} from '../../src/features/accounting/Accounting';
import {AccountingMobile} from '../../src/features/accounting/Mobile';
import {TrialBalanceView,StatementView,DayBookView} from '../../src/features/accounting/books';
import {ChartView,PartiesView} from '../../src/features/accounting/chart';
import {OpeningView} from '../../src/features/accounting/opening';
import {AccLedgerModal,AccGroupModal,AccOpeningLineModal} from '../../src/features/accounting/parts';
import {navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS} from '../../src/app/navmap';
export {registration,navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS};
const C:any={AccountingPage,AccountingMobile,TrialBalanceView,StatementView,DayBookView,ChartView,PartiesView,OpeningView,AccLedgerModal,AccGroupModal,AccOpeningLineModal};
export function render(name:string,props:any,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'accounting',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/acc-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __auReq} from 'node:module';const require=__auReq(import.meta.url);"}});
  const X=await import('./.generated/acc-ui.mjs');const {render}=X;
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const USERS={owner:'Owner',manager:'Manager',accountant:'Accountant',cashier:'Cashier',reception:'Reception',housekeeper:'Housekeeping',maint:'Maintenance'};
  const BOOKS=['owner','manager','accountant'];
  const range={from:'2026-10-01',to:'2026-10-24'};
  const withAcc=(x)=>{const s=M.seed();return {...s,accountingX:{...(s.accountingX??{}),...x}};};
  const FY='2026-04-01';
  const lines=[{account:'1000/TILL-MAIN',debit:5000000,credit:0},{account:'3100',debit:0,credit:4000000}]; // unbalanced by ₹10,000 Dr → 1400 Cr
  const draft=(extra={})=>withAcc({opening:{asOf:FY,lines,status:'draft',by:'manager',at:'2026-10-24T09:00',...extra}});

  test('[ACC1-UI] registration: page, phone page and the accounting dialogs (existing + acc-ledger, acc-group, acc-opening-line)',()=>{
    const r=X.registration;assert.ok(r.pages.accounting);assert.ok(r.mobile.pages.accounting);
    for(const m of ['acc-voucher','acc-lock','acc-unlock','acc-map','acc-ledger','acc-group','acc-opening-line'])assert.equal(typeof r.modals[m],'function',m);
    assert.equal(r.nav.length,1);assert.equal(r.nav[0].item.id,'accounting');
  });
  test('[ACC1-UI] navigation: PAGE_CATEGORY keeps accounting; keywords gained the new views; books only for Owner / Manager / Accountant (DEC-079)',()=>{
    assert.equal(X.PAGE_CATEGORY.accounting,'accounts');
    for(const w of ['trial balance','statement','day book','chart of accounts','parties','opening balances'])assert.ok(X.PAGE_KEYWORDS.accounting.includes(w),w);
    const s=M.seed();
    for(const u of Object.keys(USERS))assert.equal(X.navRoleOk(s,u,'accounting'),BOOKS.includes(u)&&true,u);
  });
  test('[ACC1-UI] desktop page: Owner sees every tab; Cashier / housekeeping / maintenance get no books',()=>{
    const s=M.seed(),h=text(render('AccountingPage',{},s,'owner'));
    for(const t of ['Vouchers','Trial balance','Statements','Day book','Chart of accounts','Parties','Opening balances','Exceptions','Reconciliation','Mapping','Tally export'])assert.match(h,new RegExp(t),t);
    assert.match(h,/Opening balances pending|Opening balances\s*pending/);
    for(const u of ['cashier','housekeeper','maint','reception'])assert.match(text(render('AccountingPage',{},s,u)),/Not available for your role/,u);
    const acc=text(render('AccountingPage',{},s,'accountant'));assert.match(acc,/Tally export/);assert.doesNotMatch(text(render('AccountingPage',{},s,'manager')),/Tally export/);
  });
  test('[ACC1-UI] phone page: view chips (Day, Trial balance, Statements, Day book, Chart, Parties, Opening); the Cashier gets nothing',()=>{
    const s=M.seed(),h=text(render('AccountingMobile',{},s,'owner',true));
    for(const t of ['Day','Trial balance','Statements','Day book','Chart','Parties','Opening'])assert.match(h,new RegExp(t),t);
    assert.match(h,/Opening \(pending\)/);assert.match(text(render('AccountingMobile',{},s,'cashier',true)),/Not available for your role/);
  });
  test('[ACC1-UI] six-column trial balance: Opening / Period / Closing Dr and Cr, balanced pill, control vs detail toggle, CSV for the books roles only',()=>{
    const s=M.seed(),h=render('TrialBalanceView',{range,setRange:()=>{},onStatement:()=>{}},s,'owner'),t=text(h);
    assert.match(t,/Opening Period Closing/);assert.match(t,/Debits = credits in every column/);assert.match(t,/Control ledgers With parties/);assert.match(t,/Export CSV/);
    assert.match(t,/Opening balances pending/);assert.doesNotMatch(t,/-₹/);
    assert.doesNotMatch(text(render('TrialBalanceView',{range,setRange:()=>{},onStatement:()=>{}},s,'cashier')),/Export CSV/);
    const m=text(render('TrialBalanceView',{range,setRange:()=>{},phone:true,onStatement:()=>{}},s,'owner',true));
    assert.match(m,/Period Dr/);assert.match(m,/Today Last 7 days This month Financial year/);assert.doesNotMatch(render('TrialBalanceView',{range,setRange:()=>{},phone:true,onStatement:()=>{}},s,'owner',true),/<table/);
  });
  test('[ACC1-UI] statements: picker with search, running balance in Dr / Cr, counter ledger, CSV; phone uses cards',()=>{
    const s=M.seed();
    const empty=text(render('StatementView',{range,setRange:()=>{},account:'',setAccount:()=>{}},s,'owner'));
    assert.match(empty,/Choose a ledger or party/);assert.match(empty,/Search ledger or party/i);
    const h=render('StatementView',{range,setRange:()=>{},account:'1000',setAccount:()=>{}},s,'owner'),t=text(h);
    assert.match(t,/Opening balance/);assert.match(t,/Closing balance/);assert.match(t,/Counter ledger/);assert.match(t,/Export CSV/);assert.doesNotMatch(t,/-₹/);
    const m=render('StatementView',{range,setRange:()=>{},account:'1000',setAccount:()=>{},phone:true},s,'owner',true);
    assert.doesNotMatch(m,/<table/);assert.match(text(m),/Opening/);
  });
  test('[ACC1-UI] day book: voucher rows with ledger lines, Dr / Cr columns; phone cards',()=>{
    const s=M.seed(),t=text(render('DayBookView',{range,setRange:()=>{}},s,'owner'));
    assert.match(t,/Voucher Type Narration Ledger Debit Credit/);assert.match(t,/vouchers? ·/);
    assert.doesNotMatch(render('DayBookView',{range,setRange:()=>{},phone:true},s,'owner',true),/<table/);
  });
  test('[ACC1-UI] chart of accounts: the Tally groups and ledger rows with nature / control / active; Add and Edit only for the Owner',()=>{
    const s=M.seed(),o=render('ChartView',{},s,'owner'),to=text(o);
    for(const g of ['Current Assets','Sundry Debtors','Cash-in-Hand','Indirect Expenses','Sales Accounts'])assert.match(to,new RegExp(g),g);
    assert.match(to,/Code Ledger Group Nature Control Active/);assert.match(to,/Add ledger/);assert.match(to,/Add sub-group/);assert.match(to,/Edit/);assert.match(to,/History of changes/);
    for(const u of ['manager','accountant']){const t=text(render('ChartView',{},s,u));assert.doesNotMatch(t,/Add ledger/,u);assert.match(t,/Only the Owner can change the chart of accounts/,u);assert.match(t,/View/,u);}
    const p=render('ChartView',{phone:true},s,'owner',true);assert.doesNotMatch(p,/<table/);assert.match(text(p),/Add ledger/);assert.match(p,/m-sticky-footer/);
    assert.doesNotMatch(text(render('ChartView',{phone:true},s,'manager',true)),/Add ledger/);
  });
  test('[ACC1-UI] chart dialogs: Owner edits, others read; a built-in keeps its nature; a custom group is editable, a primary group is not',()=>{
    const s=M.seed();
    const o=render('AccLedgerModal',{id:'4000'},s,'owner'),to=text(o);
    assert.match(to,/4000 · Room revenue/);assert.match(to,/BUILT-IN/);assert.match(to,/Retire ledger/);assert.match(to,/Save ledger/);assert.match(to,/keeps its nature/);
    const m=text(render('AccLedgerModal',{id:'4000'},s,'manager'));assert.match(m,/Only the Owner can change the chart of accounts/);assert.doesNotMatch(m,/Save ledger|Retire ledger/);
    assert.match(text(render('AccLedgerModal',{id:'new'},s,'owner')),/Add ledger/);assert.match(text(render('AccLedgerModal',{id:'9990'},s,'owner')),/Ledger not found/);
    assert.match(text(render('AccGroupModal',{id:'bank'},s,'owner')),/Tally’s 28 primary groups/);
    const s2=withAcc({chart:{ledgers:[],groups:[{id:'g-laundry',name:'Laundry income',parent:'direct-income',nature:'Cr',side:'PL',custom:true}],history:[]}});
    const g=text(render('AccGroupModal',{id:'g-laundry'},s2,'owner'));assert.match(g,/Save sub-group/);assert.match(g,/Laundry income/);
    assert.match(text(render('ChartView',{},s2,'owner')),/Laundry income/);
  });
  test('[ACC1-UI] parties: grouped by control ledger with balances; phone cards',()=>{
    const s=M.seed(),t=text(render('PartiesView',{onStatement:()=>{}},s,'owner'));
    assert.match(t,/1110 · Company receivables/);assert.match(t,/1200 · Staff advances/);assert.match(t,/2200 · Vendor payables/);assert.match(t,/Statement/);
    assert.doesNotMatch(render('PartiesView',{phone:true,onStatement:()=>{}},s,'owner',true),/<table/);
  });
  test('[ACC1-UI] opening balances: nothing entered → pending; Manager / Accountant can save, only the Owner approves',()=>{
    const s=M.seed();
    const o=text(render('OpeningView',{},s,'owner'));assert.match(o,/Not entered/);assert.match(o,/Pending/);assert.match(o,/Add line/);
    for(const u of ['manager','accountant'])assert.match(text(render('OpeningView',{},s,u)),/Add line/,u);
    const d=draft();
    const ow=text(render('OpeningView',{},d,'owner'));assert.match(ow,/Draft/);assert.match(ow,/Save draft/);assert.match(ow,/Approve/);
    for(const u of ['manager','accountant']){const t=text(render('OpeningView',{},d,u));assert.match(t,/Save draft/,u);assert.doesNotMatch(t,/Approve/,u);}
  });
  test('[ACC1-UI] opening balances: the automatic 1400 difference line is shown with the amount that balances the entry',()=>{
    const t=text(render('OpeningView',{},draft(),'owner'));
    assert.match(t,/1400/);assert.match(t,/Opening-balance difference|Opening balance difference/);assert.match(t,/Added automatically/);assert.match(t,/₹10,000 parked on 1400/);
    const bal=withAcc({opening:{asOf:FY,lines:[{account:'1000/TILL-MAIN',debit:100000,credit:0},{account:'3100',debit:0,credit:100000}],status:'draft',by:'manager',at:'2026-10-24T09:00'}});
    const b=text(render('OpeningView',{},bal,'owner'));assert.match(b,/Balanced — 1400 is zero/);assert.doesNotMatch(b,/Added automatically/);
  });
  test('[ACC1-UI] opening balances: approved is read-only; the Owner sees Reopen, others do not; phone shows cards and one footer action',()=>{
    const a=draft({status:'approved',approvedBy:'owner',approvedAt:'2026-10-24T10:00'});
    const ow=text(render('OpeningView',{},a,'owner'));assert.match(ow,/Approved/);assert.match(ow,/Reopen/);assert.doesNotMatch(ow,/Save draft|Add line/);
    const mg=text(render('OpeningView',{},a,'manager'));assert.doesNotMatch(mg,/Reopen|Save draft|Approve\b/);
    const p=render('OpeningView',{phone:true},draft(),'manager',true);assert.doesNotMatch(p,/<table/);assert.match(p,/m-sticky-footer/);assert.match(text(p),/Add line/);
    const po=text(render('OpeningView',{phone:true},draft(),'owner',true));assert.match(po,/Approve/);
    assert.match(text(render('OpeningView',{phone:true},a,'owner',true)),/Reopen/);
  });
  test('[ACC1-UI] opening-line dialog: enter roles edit a draft line; an approved entry is locked',()=>{
    const d=draft();
    const t=text(render('AccOpeningLineModal',{id:'3100'},d,'accountant'));assert.match(t,/Edit opening line/);assert.match(t,/Remove line/);assert.match(t,/Save line/);
    assert.match(text(render('AccOpeningLineModal',{id:'new'},d,'manager')),/Add opening line/);
    const a=text(render('AccOpeningLineModal',{id:'3100'},draft({status:'approved'}),'owner'));assert.match(a,/The opening entry is approved/);assert.doesNotMatch(a,/Remove line/);
    assert.match(text(render('AccOpeningLineModal',{id:'nope'},d,'owner')),/Line not found/);
  });
  test('[ACC1-UI] source discipline: contract names come from _contract-stubs only; no hex colours in the accounting styles; phone targets 48 px',async()=>{
    const dir='src/features/accounting/';const files=(await readdir(dir)).filter(f=>/\.tsx?$/.test(f)&&f!=='_contract-stubs.ts');
    for(const f of files){const src=await readFile(dir+f,'utf8');
      assert.ok(!/from '\.\.\/\.\.\/domain\/streams\/accounting-/.test(src),f+' imports a lane file directly');
      assert.ok(!/\b(accChart|accGroups|accPartyLedgers|accOpening|accOpeningPending|accStatement|accTrialBalance6|accDayBook)\b[^'\n]*from '\.\.\/\.\.\/services\/mock'/.test(src),f);}
    const css=await readFile('src/styles/m-accounting.css','utf8');assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(css.replace(/\/\*[\s\S]*?\*\//g,'').replace(/var\(--[^)]*\)/g,'')),'hex colour in m-accounting.css');
    assert.match(css,/\.acc-m \.acc-range input\[type=date\]\{min-height:48px;font-size:16px/);
    const stubs=await readFile(dir+'_contract-stubs.ts','utf8');
    for(const n of ['accChart','accGroups','accPartyLedgers','accOpening','accOpeningPending','accStatement','accTrialBalance6','accDayBook','ACC_LEDGER_UPSERT','ACC_LEDGER_RETIRE','ACC_GROUP_UPSERT','ACC_OPENING_SAVE','ACC_OPENING_APPROVE','ACC_OPENING_REOPEN'])assert.ok(stubs.includes(n),n);
  });
}
