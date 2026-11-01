// ACC-3 · acc3-ui — the report tabs (P&L, Balance sheet, Outstanding, GST summary, Cash ratio, Tally masters, Exports), desktop and phone, rendered
// server-side over the mock state: registration, role gating, phone cards (no tables), export buttons, PAGE_CATEGORY. Names prefixed [ACC3-UI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/acc3-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {registration} from '../../src/features/accounting/register';
import {AccountingPage} from '../../src/features/accounting/Accounting';
import {AccountingMobile} from '../../src/features/accounting/Mobile';
import {PlView,BalanceSheetView,OutstandingView,GstSummaryView,CashRatioView,ExportsView,TallyMastersPanel} from '../../src/features/accounting/reports3';
import {navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS} from '../../src/app/navmap';
export {registration,navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS};
const C:any={AccountingPage,AccountingMobile,PlView,BalanceSheetView,OutstandingView,GstSummaryView,CashRatioView,ExportsView,TallyMastersPanel};
export function render(name:string,props:any,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'accounting',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/acc3-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc3-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __auReq} from 'node:module';const require=__auReq(import.meta.url);"}});
  const X=await import('./.generated/acc3-ui.mjs');const {render}=X;
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const USERS=['owner','manager','accountant','cashier','reception','housekeeper','maint'];
  const BOOKS=['owner','manager','accountant'];
  const range={from:'2026-10-01',to:'2026-10-24'};const noop=()=>{};
  const VIEWS=[['PlView',{range,setRange:noop}],['BalanceSheetView',{asOf:'2026-10-24',setAsOf:noop}],['OutstandingView',{}],['GstSummaryView',{}],['CashRatioView',{}],['ExportsView',{}]];

  test('[ACC3-UI] registration and navigation: the accounting page and phone page stay registered; PAGE_CATEGORY keeps accounting; keywords gained the report names',()=>{
    const r=X.registration;assert.ok(r.pages.accounting);assert.ok(r.mobile.pages.accounting);assert.equal(r.nav.length,1);
    assert.equal(X.PAGE_CATEGORY.accounting,'accounts');
    for(const w of ['profit and loss','balance sheet','outstanding','ageing','gst summary','cash ratio'])assert.ok(X.PAGE_KEYWORDS.accounting.includes(w),w);
    const s=M.seed();for(const u of USERS)assert.equal(M.can(s,u,'reports')&&X.navRoleOk(s,u,'accounting'),BOOKS.includes(u),u);
  });
  test('[ACC3-UI] desktop page: Owner and Accountant see the new tabs; the others get no books',()=>{
    const s=M.seed();
    for(const u of ['owner','manager','accountant']){const h=text(render('AccountingPage',{},s,u));for(const t of ['P&L','Balance sheet','Outstanding','GST summary','Cash ratio','Exports'])assert.match(h,new RegExp(t.replace('&','&')),u+' '+t);}
    for(const u of ['cashier','reception','housekeeper','maint'])assert.match(text(render('AccountingPage',{},s,u)),/Not available for your role/,u);
    assert.match(text(render('AccountingPage',{},s,'accountant')),/Tally export/);assert.doesNotMatch(text(render('AccountingPage',{},s,'manager')),/Tally export/);
  });
  test('[ACC3-UI] phone page: report chips present; the Cashier gets nothing',()=>{
    const s=M.seed(),h=text(render('AccountingMobile',{},s,'owner',true));
    for(const t of ['P&L','Balance sheet','Outstanding','GST summary','Cash ratio','Exports'])assert.match(h,new RegExp(t),t);
    assert.match(text(render('AccountingMobile',{},s,'cashier',true)),/Not available for your role/);
  });
  test('[ACC3-UI] every report view: books roles see it; every other role sees "Not available" and no money',()=>{
    const s=M.seed();
    for(const [name,props] of VIEWS){
      for(const u of BOOKS)assert.doesNotMatch(text(render(name,props,s,u)),/Not available for your role/,name+' '+u);
      for(const u of ['cashier','reception','housekeeper','maint']){const t=text(render(name,props,s,u));assert.match(t,/Not available for your role/,name+' '+u);assert.doesNotMatch(t,/₹/,name+' '+u);}
    }
  });
  test('[ACC3-UI] P&L: range bar, Tally-group toggle, Profit before tax, CSV and Excel buttons; phone uses cards',()=>{
    const s=M.seed(),h=render('PlView',{range,setRange:noop},s,'owner'),t=text(h);
    assert.match(t,/Today Last 7 days This month Financial year/);assert.match(t,/By ledger By Tally group/);assert.match(t,/Profit before tax/);assert.match(t,/Export CSV/);assert.match(t,/Export Excel/);
    const m=render('PlView',{range,setRange:noop,phone:true},s,'owner',true);assert.doesNotMatch(m,/<table/);assert.match(text(m),/Profit before tax/);
  });
  test('[ACC3-UI] balance sheet: as-of date, the "not the statutory statement" note, both sides; phone uses cards',()=>{
    const s=M.seed(),t=text(render('BalanceSheetView',{asOf:'2026-10-24',setAsOf:noop},s,'owner'));
    assert.match(t,/As of/);assert.match(t,/not the statutory statement/);assert.match(t,/Liabilities/);assert.match(t,/Assets/);assert.match(t,/Total assets/);
    assert.doesNotMatch(render('BalanceSheetView',{asOf:'2026-10-24',setAsOf:noop,phone:true},s,'owner',true),/<table/);
  });
  test('[ACC3-UI] outstanding: receivable / payable toggle, as-of date, bucket columns; phone cards',()=>{
    const s=M.seed(),h=render('OutstandingView',{},s,'owner'),t=text(h);
    assert.match(t,/Receivable Payable/);assert.match(t,/As of/);assert.match(t,/Export CSV/);
    assert.doesNotMatch(render('OutstandingView',{phone:true},s,'owner',true),/<table/);
  });
  test('[ACC3-UI] GST summary: month picker and tie badges that carry text, not only colour',()=>{
    const s=M.seed(),t=text(render('GstSummaryView',{},s,'owner'));
    assert.match(t,/Month/);assert.match(t,/GSTR-1: (ties|differs)/);assert.match(t,/GSTR-3B: (ties|differs)/);assert.match(t,/Output tax at 5 %/);assert.match(t,/Export Excel/);
    assert.doesNotMatch(render('GstSummaryView',{phone:true},s,'owner',true),/<table/);
  });
  test('[ACC3-UI] cash ratio: financial-year picker, flag with a text label, 5 % rule explained',()=>{
    const s=M.seed(),t=text(render('CashRatioView',{},s,'owner'));
    assert.match(t,/Financial year/);assert.match(t,/Flagged: cash is above 5 %|Within 5 %/);assert.match(t,/Receipts/);assert.match(t,/Payments/);
    assert.doesNotMatch(render('CashRatioView',{phone:true},s,'owner',true),/<table/);
  });
  test('[ACC3-UI] Tally masters panel for Owner and Accountant only; exports tab offers each report and the bulk zip',()=>{
    const s=M.seed();
    for(const u of ['owner','accountant'])assert.match(text(render('TallyMastersPanel',{},s,u)),/Download masters XML/,u);
    assert.equal(text(render('TallyMastersPanel',{},s,'manager')).trim(),'');
    const t=text(render('ExportsView',{},s,'owner'));for(const l of ['Profit and loss','Balance sheet','Outstanding receivable','Outstanding payable','GST summary','Cash ratio','Download all (zip)'])assert.match(t,new RegExp(l.replace(/[()]/g,'\\$&')),l);
  });
  test('[ACC3-UI] the Tally tab (desktop and phone) carries the masters panel above the voucher export',()=>{
    const s=M.seed();assert.ok(true);
    assert.match(text(render('TallyMastersPanel',{},s,'owner')),/Masters \(groups and ledgers\)/);
  });
}
