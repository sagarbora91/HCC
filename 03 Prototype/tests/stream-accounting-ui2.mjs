// ACC-2 · acc2-ui — the ACC-2 screens (desktop and phone), rendered server-side over the mock state: page / modal registration, permission gating per
// role (Cashier, housekeeping, maintenance and reception never see the books; Owner-only switches and approvals), the Manual vouchers tab and its guided
// templates, GST month with the month lock, TDS, Assets, Add loan, purchase fields, Vendor payments, OTA payouts, company payment TDS / retire, Apply deposit,
// the cash-guard messages and the firm-profile switches. ACC-2 integration: the stubs file is gone — the screens read the lanes' real selectors through the
// thin view adapters of src/features/accounting/acc2-views.ts (`views` below). Names prefixed [ACC2-UI].
import {build} from 'esbuild';
import {writeFile,readFile,readdir} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/acc-ui2-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {registration as accReg} from '../../src/features/accounting/register';
import {registration as closingReg} from '../../src/features/closing/register';
import {registration as chnReg} from '../../src/features/channels/register';
import {AccountingPage} from '../../src/features/accounting/Accounting';
import {AccountingMobile} from '../../src/features/accounting/Mobile';
import {ManualView,ManualModal,ManualDecideModal,ManualVoidModal} from '../../src/features/accounting/manual';
import {GstMonthView,GstPayModal,StatRemitModal,MonthLockModal,MonthUnlockModal} from '../../src/features/accounting/gst';
import {TdsView,AssetsView,TdsRemitModal,LoanModal} from '../../src/features/accounting/tds';
import {ChartView} from '../../src/features/accounting/chart';
import {CashGuardNote,CashDeductionBadge} from '../../src/features/accounting/cashguard';
import {MANUAL_TEMPLATES} from '../../src/features/accounting/manual-templates';
import * as views from '../../src/features/accounting/acc2-views';
import {ExpensePurchaseFields} from '../../src/features/expenses/purchase';
import {ExpVendors,VendorPayModal,VendorVoidModal,VendorReturnModal} from '../../src/features/expenses/vendors';
import {expSectionsFor,ExpRowExtras} from '../../src/features/expenses/Scc';
import {PayoutsTab,PayoutModal} from '../../src/features/channels/payout';
import {ChannelsPage} from '../../src/features/channels/Channels';
import {CompanyPaymentModal} from '../../src/features/groups/credit';
import {CompanyModal} from '../../src/features/groups/modals';
import {GroupsPage,CompaniesTab} from '../../src/features/groups/Groups';
import {ApplyDepositPanel,useOtaCredit} from '../../src/features/folio/deposit';
import {HRAdvanceModal} from '../../src/features/staff/modals';
import {CmpProfileModal} from '../../src/features/compliance/modals';
import {navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS} from '../../src/app/navmap';
function OtaCreditProbe({b,mobile}:{b:any;mobile?:boolean}){const o=useOtaCredit(b,mobile);return <>{o.node}</>;}
export {accReg,closingReg,chnReg,navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS,MANUAL_TEMPLATES,views,expSectionsFor};
const C:any={AccountingPage,AccountingMobile,ManualView,ManualModal,ManualDecideModal,ManualVoidModal,GstMonthView,GstPayModal,StatRemitModal,MonthLockModal,MonthUnlockModal,
  TdsView,AssetsView,TdsRemitModal,LoanModal,ChartView,CashGuardNote,CashDeductionBadge,ExpensePurchaseFields,ExpVendors,VendorPayModal,VendorVoidModal,VendorReturnModal,
  ExpRowExtras,PayoutsTab,PayoutModal,ChannelsPage,CompanyPaymentModal,CompanyModal,GroupsPage,CompaniesTab,ApplyDepositPanel,OtaCreditProbe,HRAdvanceModal,CmpProfileModal};
export function render(name:string,props:any,s:any,uid:string,mobile=false,extra:any={}){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'accounting',tab:'inbox',setTab:noop,go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,...extra};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/acc-ui2-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc-ui2.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __au2Req} from 'node:module';const require=__au2Req(import.meta.url);"}});
  const X=await import('./.generated/acc-ui2.mjs');const {render}=X;
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const USERS={owner:'Owner',manager:'Manager',accountant:'Accountant',cashier:'Cashier',reception:'Reception',housekeeper:'Housekeeping',maint:'Maintenance'};
  const BOOKS=['owner','manager','accountant'];
  const clone=s=>JSON.parse(JSON.stringify(s));
  const withAcc=(x,s0=M.seed())=>{const s=clone(s0);s.accountingX={...(s.accountingX??{}),...x};return s;};
  const mv=(o)=>({id:'MV-001',date:'2026-10-20',type:'Payment',narration:'Bank charges for October (fictional)',lines:[{account:'5080',debit:50000,credit:0},{account:'1030/BANK-1',debit:0,credit:50000}],
    template:'bank-charges',status:'pending',by:'accountant',at:'2026-10-20T11:00',...o});
  const manual=()=>withAcc({manual:[mv({}),mv({id:'MV-002',template:'capital-introduced',type:'Receipt',narration:'Owner capital (fictional)',lines:[{account:'1030/BANK-1',debit:20000000,credit:0},{account:'3100',debit:0,credit:20000000}],status:'approved',by:'owner',decidedBy:'owner'}),
    mv({id:'MV-003',status:'void',reason:'Entered twice',by:'owner'}),mv({id:'MV-004',status:'rejected',reason:'Wrong month',by:'accountant',decidedBy:'owner'})]});

  test('[ACC2-UI] registration: the Accounting dialogs, the vendor / OTA dialogs and the phone page',()=>{
    for(const m of ['acc-manual','acc-manual-decide','acc-manual-void','acc-gst-pay','acc-stat-remit','acc-month-lock','acc-month-unlock','acc-tds-remit','acc-loan'])assert.equal(typeof X.accReg.modals[m],'function',m);
    for(const m of ['exp-vendor-pay','exp-vendor-void','exp-return'])assert.equal(typeof X.closingReg.modals[m],'function',m);
    assert.equal(typeof X.chnReg.modals['chn-payout'],'function');assert.ok(X.accReg.mobile.pages.accounting);assert.ok(X.accReg.pages.accounting);
    for(const m of ['acc-voucher','acc-lock','acc-ledger','acc-opening-line'])assert.equal(typeof X.accReg.modals[m],'function',m+' (ACC-1) is still there');
  });
  test('[ACC2-UI] navigation: PAGE_CATEGORY keeps accounting; the accounting keywords grew; books only for Owner / Manager / Accountant',()=>{
    assert.equal(X.PAGE_CATEGORY.accounting,'accounts');
    for(const w of ['manual voucher','gst month','set-off','month lock','tds','fixed assets','add loan','vendor payment','ota payout','trial balance','statement'])assert.ok(X.PAGE_KEYWORDS.accounting.includes(w),w);
    const s=M.seed();for(const u of Object.keys(USERS))assert.equal(M.can(s,u,'reports')&&X.navRoleOk(s,u,'accounting'),BOOKS.includes(u),u);
  });
  test('[ACC2-UI] desktop page: new tabs for the books roles; the Cashier, reception, housekeeping and maintenance get nothing',()=>{
    const s=manual(),h=text(render('AccountingPage',{},s,'owner'));
    for(const t of ['Manual vouchers','GST month','TDS','Assets'])assert.match(h,new RegExp(t),t);
    assert.match(h,/Manual vouchers 1/,'one voucher waits for approval (badge)');
    for(const u of ['cashier','housekeeper','maint','reception'])assert.match(text(render('AccountingPage',{},s,u)),/Not available for your role/,u);
    for(const u of ['manager','accountant'])assert.match(text(render('AccountingPage',{},s,u)),/GST month/,u);
  });
  test('[ACC2-UI] phone page: chips Manual vouchers (pending flag), GST month, TDS, Assets; the Cashier gets nothing',()=>{
    const h=text(render('AccountingMobile',{},manual(),'owner',true));
    for(const t of ['Manual \\(pending\\)','GST month','TDS','Assets'])assert.match(h,new RegExp(t),t);
    assert.match(text(render('AccountingMobile',{},M.seed(),'owner',true)),/Manual vouchers/);
    assert.match(text(render('AccountingMobile',{},M.seed(),'cashier',true)),/Not available for your role/);
  });

  test('[ACC2-UI] manual vouchers: status pills, templates and New voucher for Owner / Accountant, read-only for the Manager; the approval queue is the Owner’s',()=>{
    const s=manual();
    const o=text(render('ManualView',{},s,'owner'));
    assert.match(o,/New voucher/);for(const t of ['Capital introduced','Owner drawing from the bank','Loan received','Loan EMI','Bank charges','Interest income','Bad debt written off','Contra'])assert.match(o,new RegExp(t),t);
    assert.match(o,/Waiting for your approval \(1\)/);assert.match(o,/Approve/);assert.match(o,/Reject/);assert.match(o,/Pending approval/);assert.match(o,/Approved/);assert.match(o,/Void/);assert.match(o,/Rejected/);
    assert.match(o,/Void…/,'the Owner can void an approved voucher');assert.match(o,/Your own vouchers are approved at once/);
    const a=text(render('ManualView',{},s,'accountant'));assert.match(a,/New voucher/);assert.doesNotMatch(a,/Waiting for your approval/);assert.doesNotMatch(a,/Bad debt written off/);assert.doesNotMatch(a,/Void…/);assert.match(a,/wait for the Owner’s approval/);
    const m=text(render('ManualView',{},s,'manager'));assert.doesNotMatch(m,/New voucher/);assert.match(m,/entered by the Owner or the Accountant/);
    const p=render('ManualView',{phone:true},s,'owner',true);assert.doesNotMatch(p,/<table/);assert.match(text(p),/GUIDED ENTRIES/);assert.match(text(p),/Waiting for your approval/);
    assert.doesNotMatch(render('ManualView',{},s,'owner'),/-₹/);
    assert.match(text(render('ManualView',{},M.seed(),'owner')),/No manual vouchers/);
  });
  test('[ACC2-UI] guided templates fill the lines of contract §2 (capital, drawing, loan received, EMI, bank charges, interest, bad debt, contra)',()=>{
    const n=a=>k=>Math.round(Number(a[k]??0)*100);
    const T=id=>X.MANUAL_TEMPLATES.find(t=>t.id===id);const lines=(id,a)=>T(id).build(a,n(a));
    assert.deepEqual(lines('capital-introduced',{tender:'1030/BANK-1',amount:'200000'}),[{account:'1030/BANK-1',debit:20000000,credit:0},{account:'3100',debit:0,credit:20000000}]);
    assert.deepEqual(lines('owner-drawing-bank',{bank:'1030/BANK-1',amount:'5000'}),[{account:'3000',debit:500000,credit:0},{account:'1030/BANK-1',debit:0,credit:500000}]);
    assert.deepEqual(lines('loan-received',{bank:'1030/BANK-1',loan:'2400/LN-001',amount:'1000'}),[{account:'1030/BANK-1',debit:100000,credit:0},{account:'2400/LN-001',debit:0,credit:100000}]);
    assert.deepEqual(lines('loan-emi',{bank:'1030/BANK-1',loan:'2400/LN-001',principal:'8000',interest:'2000'}),[{account:'2400/LN-001',debit:800000,credit:0},{account:'5090',debit:200000,credit:0},{account:'1030/BANK-1',debit:0,credit:1000000}]);
    assert.deepEqual(lines('bank-charges',{bank:'1030/BANK-1',amount:'118'}),[{account:'5080',debit:11800,credit:0},{account:'1030/BANK-1',debit:0,credit:11800}]);
    assert.deepEqual(lines('interest-income',{bank:'1030/BANK-1',amount:'40'}),[{account:'1030/BANK-1',debit:4000,credit:0},{account:'4960',debit:0,credit:4000}]);
    assert.deepEqual(lines('bad-debt',{debtor:'1110/CO-1',amount:'500'}),[{account:'5300',debit:50000,credit:0},{account:'1110/CO-1',debit:0,credit:50000}]);
    assert.deepEqual(lines('contra',{from:'1000/TILL-MAIN',to:'1030/BANK-1',amount:'30000'}),[{account:'1030/BANK-1',debit:3000000,credit:0},{account:'1000/TILL-MAIN',debit:0,credit:3000000}]);
    assert.deepEqual(lines('capital-introduced',{amount:'10'}),[],'incomplete answers make no lines');
    for(const t of X.MANUAL_TEMPLATES)if(t.fields.length){const full={};for(const f of t.fields)full[f.key]=f.kind==='amount'?'100':'X';const L=t.build(full,n(full));assert.equal(L.reduce((a,l)=>a+l.debit,0),L.reduce((a,l)=>a+l.credit,0),t.id+' balances');}
    assert.equal(T('bad-debt').ownerOnly,true);
  });
  test('[ACC2-UI] manual voucher dialog: guided form for the Owner / Accountant, the Owner approves a pending one, Manager sees no form, bad debt is Owner-only',()=>{
    const s=manual();
    const g=text(render('ManualModal',{id:'new:loan-emi'},s,'owner'));assert.match(g,/Loan EMI/);assert.match(g,/Principal \(₹\)/);assert.match(g,/Interest \(₹\)/);assert.match(g,/Post voucher/);assert.match(g,/No loan yet/);
    assert.match(text(render('ManualModal',{id:'new:bank-charges'},s,'accountant')),/Send for approval/);assert.match(text(render('ManualModal',{id:'new:bank-charges'},s,'accountant')),/The Owner approves this voucher before it posts/);
    const j=text(render('ManualModal',{id:'new'},s,'owner'));assert.match(j,/Add line/);assert.match(j,/Debit \(₹\)/);assert.match(j,/Credit \(₹\)/);
    assert.match(text(render('ManualModal',{id:'new:bad-debt'},s,'accountant')),/entered by the Owner only|Owner or the Accountant|Owner only/);
    assert.doesNotMatch(text(render('ManualModal',{id:'new'},s,'accountant')),/Bad debt written off/);assert.match(text(render('ManualModal',{id:'new'},s,'owner')),/Bad debt written off/);
    const v=text(render('ManualModal',{id:'MV-001'},s,'owner'));assert.match(v,/Pending approval/);assert.match(v,/Approve/);assert.match(v,/Reject…/);assert.match(v,/Dr ₹500/);assert.match(v,/Cr ₹500/);
    const approved=text(render('ManualModal',{id:'MV-002'},s,'owner'));assert.match(approved,/Void…/);assert.doesNotMatch(approved,/Send for approval|Post voucher/);
    assert.match(text(render('ManualModal',{id:'new'},s,'manager')),/entered by the Owner or the Accountant/);
    assert.match(text(render('ManualModal',{id:'MV-999'},s,'owner')),/Voucher not found/);
    assert.match(text(render('ManualDecideModal',{id:'MV-001'},s,'owner')),/Reject MV-001/);assert.match(text(render('ManualDecideModal',{id:'MV-001'},s,'accountant')),/Only the Owner can decide/);
    const vo=text(render('ManualVoidModal',{id:'MV-002'},s,'owner'));assert.match(vo,/Void MV-002/);assert.match(vo,/not locked/);
    const locked=withAcc({manual:s.accountingX.manual,locks:{'2026-10-20':{date:'2026-10-20',lockedAt:'2026-10-21T09:00',lockedBy:'owner',vouchers:[],debit:0,credit:0,count:0}}});
    assert.match(text(render('ManualVoidModal',{id:'MV-002'},locked,'owner')),/reversing voucher/);
  });

  test('[ACC2-UI] GST month: the set-off working by head, post / pay / remit for Owner and Accountant only, the Manager reads',()=>{
    const s=M.seed();
    const o=text(render('GstMonthView',{},s,'owner'));
    for(const t of ['IGST','CGST','SGST','Output tax','Input credit','To pay in cash','Reverse-charge GST payable','section 49','Post set-off','Pay GST','Pay reverse-charge GST','PF / ESIC / PT remittance'])assert.match(o,new RegExp(t),t);
    assert.doesNotMatch(render('GstMonthView',{},s,'owner'),/-₹/);
    const m=text(render('GstMonthView',{},s,'manager'));assert.doesNotMatch(m,/Pay GST/);assert.match(m,/made by the Owner or the Accountant/);
    assert.match(text(render('GstMonthView',{},s,'accountant')),/Post set-off/);
    const p=render('GstMonthView',{phone:true},s,'owner',true);assert.doesNotMatch(p,/<table/);assert.match(text(p),/SET-OFF BY HEAD/);assert.match(text(p),/Payable in cash/);
  });
  test('[ACC2-UI] month lock: blocker list, Owner locks, Accountant requests, locked month shows who and the unlock with a reason',()=>{
    const s=withAcc({manual:[mv({date:'2026-10-05'})]});
    const o=render('GstMonthView',{},s,'owner'),t=text(o);
    assert.match(t,/Month lock/);assert.match(t,/cannot be locked yet/);assert.match(t,/The month 2026-10 is not over/); // ACC-2 integration: the gst lane's accMonthLockBlocker names the first blocker
    assert.match(o,/<button[^>]*disabled=""[^>]*>[^<]*(<[^>]*>)*\s*Lock month/,'Lock month disabled while something blocks it');
    assert.match(text(render('GstMonthView',{},s,'accountant')),/Request month lock/);assert.doesNotMatch(text(render('GstMonthView',{},s,'accountant')),/>Lock month/);
    assert.match(text(render('MonthLockModal',{id:'2026-10'},s,'owner')),/Lock October 2026\?/);
    const lk=withAcc({monthLocks:{'2026-10':{lockedAt:'2026-10-28T10:00',lockedBy:'owner',setoff:'JRN-1',unlocks:[{at:'2026-10-27T10:00',by:'owner',reason:'Fix challan'}]}}}); // ACC-2 integration: an earlier unlock, then locked again (accMonthLockOf: an unlock after the lock lifts it)
    const l=text(render('GstMonthView',{},lk,'owner'));assert.match(l,/Month locked/);assert.match(l,/Unlock month…/);assert.match(l,/Fix challan/);assert.doesNotMatch(l,/>Lock month/);
    assert.match(text(render('MonthUnlockModal',{id:'2026-10'},lk,'owner')),/Reason \(required\)/);assert.match(text(render('MonthUnlockModal',{id:'2026-10'},lk,'accountant')),/Only the Owner can unlock a month/);
    assert.match(text(render('MonthUnlockModal',{id:'2026-10'},s,'owner')),/not locked/);
  });
  test('[ACC2-UI] GST payment and statutory remittance dialogs (Dr 1340 / 2140 / 2310-2330)',()=>{
    const s=M.seed();
    const p=text(render('GstPayModal',{id:'2026-10'},s,'owner'));assert.match(p,/Pay GST/);assert.match(p,/Dr 1340/);assert.match(p,/Challan/);assert.match(p,/Record payment/);
    assert.match(text(render('GstPayModal',{id:'2026-10',room:'rcm'},s,'owner')),/Pay reverse-charge GST/);assert.match(text(render('GstPayModal',{id:'2026-10',room:'rcm'},s,'owner')),/never by set-off/);
    assert.match(text(render('GstPayModal',{id:'2026-10'},s,'manager')),/entered by the Owner or the Accountant/);
    const r=text(render('StatRemitModal',{id:'2026-10'},s,'accountant'));for(const t of ['Provident fund \\(PF\\)','ESIC','Professional tax','compliance card is marked done'])assert.match(r,new RegExp(t),t);
  });

  test('[ACC2-UI] TDS tab: dated rate table (CA to confirm), deductee report, Remit TDS for Owner / Accountant, CSV for the books roles',()=>{
    const s=clone(M.seed());s.expenses.push({id:'EXP-T9',vendor:'Deccan Agro (fictional)',category:'Repairs & maintenance',amount:11800000,method:'Vendor credit',date:'2026-10-10',status:'Posted',actor:'owner',gstPaise:1800000,tdsPaise:600000,tdsSection:'contractor'});
    const o=render('TdsView',{},s,'owner'),t=text(o);
    for(const x of ['Rate table','contractor|Contractor','Professional','Commission','CA to confirm','Deductee report','Deccan Agro','Remit TDS','Export CSV'])assert.match(t,new RegExp(x),x);
    assert.match(t,/₹60,000|₹6,000/,'the TDS amount is shown');
    assert.doesNotMatch(text(render('TdsView',{},s,'manager')),/Remit TDS/);assert.match(text(render('TdsView',{},s,'accountant')),/Remit TDS/);
    const off=clone(s);off.complianceX={...(off.complianceX??{}),profile:{name:'Firm',pan:'',gstin:'',entity:'proprietorship',gstType:'regular',hasEmployees:true,qrmp:false,notes:'',hotelDeductsTds:false}};
    assert.match(text(render('TdsView',{},off,'owner')),/does not<\/?strong>? deduct TDS|does not deduct TDS/);
    assert.doesNotMatch(render('TdsView',{phone:true},s,'owner',true),/<table/);assert.match(text(render('TdsView',{phone:true},s,'owner',true)),/Deccan Agro/);
    assert.match(text(render('TdsRemitModal',{id:'2026-10'},s,'owner')),/Dr TDS payable \(2150\)/);
  });
  test('[ACC2-UI] Assets tab: register with CSV; the empty state; phone cards',()=>{
    const s=withAcc({assets:[{id:'a1',no:'FA-2026-001',ledger:'1510',name:'Lobby sofa set (fictional)',cost:8500000,date:'2026-10-02',vendor:'Sample Furnishings',expense:'EXP-9',itc:0}]});
    const t=text(render('AssetsView',{},s,'owner'));for(const x of ['FA-2026-001','Lobby sofa set','₹85,000','Export CSV','depreciation'])assert.match(t,new RegExp(x),x);
    assert.match(text(render('AssetsView',{},M.seed(),'owner')),/No fixed assets yet/);
    assert.doesNotMatch(render('AssetsView',{phone:true},s,'owner',true),/<table/);
    assert.match(M.accAssetsCsv(s),/FA-2026-001,.*,1510,Lobby sofa set \(fictional\),85000\.00,0\.00,2026-10-02,,Sample Furnishings,EXP-9/); // ACC-2 integration: the purchase lane's register CSV
  });
  test('[ACC2-UI] chart screen: Add loan for the Owner only; the loan dialog',()=>{
    const s=M.seed();
    assert.match(text(render('ChartView',{},s,'owner')),/Add loan/);assert.doesNotMatch(text(render('ChartView',{},s,'manager')),/Add loan/);
    assert.match(text(render('ChartView',{phone:true},s,'owner',true)),/Add loan/);
    const l=text(render('LoanModal',{},s,'owner'));assert.match(l,/Add loan/);assert.match(l,/Lender/);assert.match(l,/last 4 digits/);assert.match(l,/Loan code/);assert.match(l,/Secured \(bank loan\)/);assert.match(l,/Unsecured/);
    assert.match(text(render('LoanModal',{},s,'manager')),/Only the Owner can add a loan/);
  });

  const cats=(s,flags)=>{const c=clone(s);const rows=M.mnyCategories(c).map(r=>flags[r.name]?{...r,...flags[r.name]}:r);c.mastersX={...(c.mastersX??{}),rows:{...(c.mastersX?.rows??{}),expenseCategories:rows}};return c;};
  test('[ACC2-UI] purchase fields: reverse charge with an editable rate, capital purchase, GST kind, the DEC-143 ITC guard message, the cash-guard note',()=>{
    const s0=M.seed(),names=M.mnyCategoryNames(s0);const [a,b,c]=[names[0],names[1],names[2]];
    const s=cats(s0,{[a]:{rcm:true,rcmBps:500},[b]:{capital:true,ledger:'1510'},[c]:{itcBlocked:true}});
    const f=(category,extra={})=>({category,vendor:'',method:'UPI',date:s.date,itc:'disallowed',gstKind:'intra',rcmBps:'',...extra});
    const rcm=text(render('ExpensePurchaseFields',{f:f(a),setF:()=>{},paise:100000,gst:0},s,'owner'));assert.match(rcm,/Reverse charge/);assert.match(rcm,/reverse charge” \(2140\)/);assert.match(rcm,/Reverse-charge GST rate/);
    const cap=text(render('ExpensePurchaseFields',{f:f(b),setF:()=>{},paise:100000,gst:0},s,'owner'));assert.match(cap,/Fixed-asset purchase/);assert.match(cap,/1510/);assert.match(cap,/register row/);
    const kind=text(render('ExpensePurchaseFields',{f:f(names[3]??a,{gstKind:'inter'}),setF:()=>{},paise:100000,gst:18000},s,'owner'));
    assert.match(kind,/Outside Maharashtra — IGST/);assert.match(kind,/Input IGST \(1330\)/);
    const blk=text(render('ExpensePurchaseFields',{f:f(c,{itc:'allowed'}),setF:()=>{},paise:100000,gst:18000},s,'owner'));assert.match(blk,/marked blocked \(CGST s\.17\(5\)\)/); /* ACC-2 integration: the purchase lane's expItcBlockRefusal wording */assert.match(blk,/DEC-143/);
    const ok=text(render('ExpensePurchaseFields',{f:f(c),setF:()=>{},paise:100000,gst:18000},s,'owner'));assert.doesNotMatch(ok,/ITC cannot be allowed/);
    assert.equal(text(render('ExpensePurchaseFields',{f:f(names[3]??a),setF:()=>{},paise:100000,gst:0},s0,'owner')).trim(),'','an ordinary expense shows nothing extra');
    const cash=text(render('ExpensePurchaseFields',{f:f(names[3]??a,{vendor:'Morning Supplies',method:'Cash'}),setF:()=>{},paise:2000000,gst:0},s0,'owner'));assert.match(cash,/Allowed, with a warning/);
  });
  test('[ACC2-UI] expense register: purchase badges on the card (IGST, reverse charge, fixed asset, TDS held back, cash deduction lost)',()=>{
    const e={id:'EXP-X',vendor:'Sample Vendor',category:'Repairs & maintenance',amount:1000000,method:'Cash',date:'2026-10-10',status:'Posted',actor:'owner',description:'Pump',gstKind:'inter',rcm:true,rcmBps:1800,capital:true,assetNo:'FA-2026-002',tdsPaise:20000,tdsSection:'contractor',cashDeductionLost:true};
    const t=text(render('ExpRowExtras',{e},M.seed(),'owner'));for(const x of ['Inter-state bill · IGST','Reverse charge 18 %','Fixed asset FA-2026-002','TDS held back ₹200','Cash deduction lost'])assert.match(t,new RegExp(x),x);
    assert.doesNotMatch(text(render('ExpRowExtras',{e:{...e,gstKind:undefined,rcm:undefined,capital:undefined,tdsPaise:undefined,cashDeductionLost:undefined}},M.seed(),'owner')),/Cash deduction lost|Fixed asset|Reverse charge/);
    assert.match(render('CashDeductionBadge',{lost:true},M.seed(),'owner'),/acc-cash-lost/);assert.equal(render('CashDeductionBadge',{lost:false},M.seed(),'owner'),'');
  });

  const vend=()=>{const s=clone(M.seed());
    s.expenses.push({id:'EXP-V1',vendor:'Morning Supplies',category:'Groceries',amount:2400000,method:'Vendor credit',date:'2026-10-05',status:'Posted',actor:'owner',receipt:'MS-101'},
      {id:'EXP-V2',vendor:'Kaveri Traders',category:'Groceries',amount:4200000,method:'Vendor credit',date:'2026-06-20',status:'Posted',actor:'owner',receipt:'KT-77'},
      {id:'EXP-V3',vendor:'Morning Supplies',category:'Groceries',amount:1000000,method:'Vendor credit',date:'2026-09-10',status:'Posted',actor:'owner',receipt:'MS-090'});
    s.expensesX={...(s.expensesX??{}),vendorPayments:[{id:'VP-1',vendor:'Morning Supplies',date:'2026-10-08',amount:300000,method:'UPI',reference:'UTR1',by:'owner',at:'2026-10-08T10:00',allocations:[{expense:'EXP-V3',amount:300000}]}]};return s;};
  test('[ACC2-UI] Vendor payments: open bills, ageing buckets 0–30 / 31–60 / 61–90 / 90+, payments; the section is for the books roles only',()=>{
    const s=vend();
    const o=render('ExpVendors',{},s,'owner'),t=text(o);
    assert.match(t,/Open bills/);assert.match(t,/Ageing/);assert.match(t,/Payments/);assert.match(t,/Pay Kaveri Traders…|Pay Morning Supplies…/);assert.match(t,/Owed to vendors/);
    assert.deepEqual(X.views.expOpenBillRows(s,'Morning Supplies').map(b=>[b.expense,b.open]),[['EXP-V3',700000],['EXP-V1',2400000]],'oldest first, net of the allocation');
    const ag=X.views.expVendorAgeingRows(s),ms=ag.find(r=>r.vendor==='Morning Supplies'),kt=ag.find(r=>r.vendor==='Kaveri Traders');
    assert.equal(ms.b0,2400000);assert.equal(ms.b31,700000);assert.equal(kt.b90,4200000);assert.equal(ms.total,3100000);
    assert.deepEqual(M.expVendorAllocate(s,'Morning Supplies',1000000),[{expense:'EXP-V3',amount:700000},{expense:'EXP-V1',amount:300000}],'the default allocation is oldest first');
    assert.equal(M.expVendorAdvance(s,'Morning Supplies'),0);
    assert.match(text(render('ExpVendors',{},s,'manager')),/Pay (Kaveri|Morning)/);
    assert.doesNotMatch(text(render('ExpVendors',{},s,'accountant')),/-₹/);
    assert.doesNotMatch(render('ExpVendors',{phone:true},s,'owner',true),/<table/);
    assert.match(text(render('ExpVendors',{},M.seed(),'owner')),/No open vendor bills/);
    assert.ok(X.expSectionsFor(true).some(x=>x[0]==='vendors'));assert.ok(!X.expSectionsFor(false).some(x=>x[0]==='vendors'),'the Cashier does not get the section');
  });
  test('[ACC2-UI] vendor payment dialog: oldest-first allocation editor, advance, reference rule, cash guard; void is the Owner’s; returns need a reason',()=>{
    const s=vend();
    const p=text(render('VendorPayModal',{id:'Morning Supplies'},s,'accountant'));
    for(const x of ['Pay Morning Supplies','Settles these bills \\(oldest first\\)','MS-090','MS-101','Allocated ₹31,000','no advance','Record payment'])assert.match(p,new RegExp(x),x);
    assert.match(text(render('VendorPayModal',{id:'Morning Supplies'},s,'reception')),/made by the Owner, Manager or Accountant/);
    assert.match(render('VendorPayModal',{id:'Morning Supplies'},s,'owner'),/aria-label="Allocate to MS-090"[^>]*value="7000"/);
    const adv=text(render('VendorPayModal',{id:'Nobody Ltd'},s,'owner'));assert.match(adv,/No open bills — the whole payment is an advance/);
    assert.match(text(render('VendorVoidModal',{id:'VP-1'},s,'owner')),/Void VP-1/);assert.match(text(render('VendorVoidModal',{id:'VP-1'},s,'manager')),/Only the Owner can void/);
    assert.match(text(render('VendorVoidModal',{id:'VP-9'},s,'owner')),/Payment not found/);
    const r=text(render('VendorReturnModal',{id:'EXP-V1'},s,'manager'));assert.match(r,/Return to Morning Supplies/);assert.match(r,/Request return/);assert.match(r,/the Owner approves it/);assert.match(text(render('VendorReturnModal',{id:'EXP-V1'},s,'owner')),/Record return/);
  });

  test('[ACC2-UI] OTA payout: Payouts tab for the books roles, the entry form with the equality check wording and the receivable',()=>{
    const s0=M.seed(),ch=M.chnSlice(s0).channels[0];
    const s=clone(s0);s.channelsX={...(s.channelsX??{}),payouts:[{id:'PO-1',channel:ch.id,date:'2026-10-20',statementRef:'STMT-77',gross:1000000,commission:150000,commissionGst:27000,tcs:1000,net:822000,bank:'BANK-1',bookings:['HCC-1041'],by:'accountant',at:'2026-10-20T10:00'}]};
    const t=text(render('PayoutsTab',{},s,'accountant'));for(const x of ['PO-1','STMT-77','₹10,000','₹1,500','₹270','₹10','₹8,220','Record a payout'])assert.match(t,new RegExp(x),x);
    assert.match(text(render('PayoutsTab',{},s,'reception')),/recorded by the Owner, Manager or Accountant/);
    assert.doesNotMatch(render('PayoutsTab',{phone:true},s,'owner',true),/<table/);assert.match(text(render('PayoutsTab',{phone:true},s,'owner',true)),/Posted/);
    const m=text(render('PayoutModal',{id:ch.id},s0,'owner'));for(const x of ['Statement reference','Gross bookings','Commission','GST on commission','TCS withheld','Net paid to the hotel','Bank it landed in','Dr bank \\(net\\)','owes the hotel'])assert.match(m,new RegExp(x),x);
    assert.match(text(render('PayoutModal',{id:ch.id},s0,'reception')),/recorded by the Owner, Manager or Accountant/);
    const tabs=text(render('ChannelsPage',{},s0,'accountant'));assert.match(tabs,/Payouts/);assert.doesNotMatch(text(render('ChannelsPage',{},s0,'reception')),/Payouts/);
  });
  test('[ACC2-UI] company payment: TDS field; company retire: account status, retired pill, picker filtering',()=>{
    const s=M.seed();
    const p=text(render('CompanyPaymentModal',{id:'CO-1'},s,'cashier'));assert.match(p,/TDS deducted by the company/);assert.match(p,/Form 16A/);assert.match(p,/No cash on company credit/);
    const c=text(render('CompanyModal',{id:'CO-1'},s,'owner'));assert.match(c,/Account status/);assert.match(c,/Active — can be picked/);
    const r=clone(s);r.groupsX.companies.find(x=>x.id==='CO-3').active=false;
    assert.match(text(render('CompanyModal',{id:'CO-3'},r,'owner')),/Retired: hidden from the pickers/);
    assert.match(text(render('CompaniesTab',{},r,'owner')),/Retired/);assert.doesNotMatch(text(render('CompaniesTab',{},s,'owner')),/Retired/);
  });
  test('[ACC2-UI] Apply deposit at check-out: shown for an in-house stay with a deposit and a balance; role / shift wording',()=>{
    const s=clone(M.seed());const b=s.bookings.find(x=>x.id==='HCC-1044');b.receipts.push({id:'R-dep',kind:'deposit',method:'Cash',amount:50000,reference:'dep',at:'2026-10-24T09:00'});
    const t=text(render('ApplyDepositPanel',{b},s,'reception'));assert.match(t,/Apply the deposit/);assert.match(t,/₹500 deposit is held/);assert.match(t,/Dr|no money changes hands/);assert.match(t,/Apply deposit/);
    assert.match(text(render('ApplyDepositPanel',{b},s,'housekeeper')),/Your role cannot apply a deposit/);
    assert.equal(render('ApplyDepositPanel',{b:s.bookings.find(x=>x.id==='HCC-1041')},s,'reception'),'','no deposit → nothing shown');
    assert.match(render('ApplyDepositPanel',{b,mobile:true},s,'reception',true),/mm-cc/);
  });
  test('[ACC2-UI] coordinator rulings: OTA check-out flag, deposit source, staff advance funding, vendor opening balance, loan code / kind',()=>{
    const s=clone(M.seed()),ch=M.chnSlice(s).channels.find(c=>c.status==='Connected');
    const b=s.bookings.find(x=>x.id==='HCC-1044');b.otaChannel=ch.name;
    const t=text(render('OtaCreditProbe',{b},s,'reception'));assert.match(t,new RegExp('Paid by the channel · '+ch.name.replace(/[()]/g,'.')));assert.match(t,/Move the balance to/);
    assert.equal(render('OtaCreditProbe',{b:s.bookings.find(x=>x.id==='HCC-1041')},s,'reception'),'','a stay not booked through a channel offers nothing');
    b.receipts.push({id:'R-dep',kind:'deposit',method:'Cash',amount:50000,reference:'dep',at:'2026-10-24T09:00'});
    assert.match(text(render('ApplyDepositPanel',{b},s,'manager')),/Opening balance carried in \(Manager \/ Owner\)/);assert.doesNotMatch(text(render('ApplyDepositPanel',{b},s,'reception')),/Opening balance carried in/);
    const adv=text(render('HRAdvanceModal',{},s,'owner'));assert.match(adv,/Paid from/);assert.match(adv,/Paid in cash from the drawer/);
    const v=vend();v.accountingX={...(v.accountingX??{}),opening:{asOf:'2026-04-01',lines:[{account:'2200/'+M.mnyVendor(v,'Morning Supplies').code,debit:0,credit:500000}],status:'approved',by:'owner',at:'2026-04-01T09:00'}};
    const bills=X.views.expOpenBillRows(v,'Morning Supplies');assert.equal(bills[0].expense,'opening');assert.equal(bills[0].open,500000);
    assert.deepEqual(M.expVendorAllocate(v,'Morning Supplies',600000),[{expense:'opening',amount:500000},{expense:'EXP-V3',amount:100000}],'the opening balance is the oldest bill');
    assert.match(text(render('VendorPayModal',{id:'Morning Supplies'},v,'owner')),/Opening balance/);
  });
  test('[ACC2-UI] firm profile: the three accounting switches, Owner only (the Accountant sees them disabled)',()=>{
    const s=M.seed();
    const o=render('CmpProfileModal',{},s,'owner'),t=text(o);
    for(const x of ['Accounting switches','Owner only','Charge GST on booking advances','The hotel deducts TDS from vendor bills','Deductor type'])assert.match(t,new RegExp(x),x);
    assert.doesNotMatch(o.match(/<fieldset[^>]*cmp-money-switches[^>]*>/)[0],/disabled/);
    const a=render('CmpProfileModal',{},s,'accountant');assert.match(a.match(/<fieldset[^>]*cmp-money-switches[^>]*>/)[0],/disabled/);assert.match(text(a),/Only the Owner changes these switches/);
    assert.match(o,/<input type="checkbox" checked=""[^>]*\/>The hotel deducts TDS/,'DEC-146 default: the hotel deducts TDS');
  });
  test('[ACC2-UI] cash guards: refusal / warning wording and thresholds (₹2,00,000 cash receipt refused; ₹12,000 cash payment warned)',()=>{
    const g=X.views.accCashVerdict,s=M.seed(); // ACC-2 integration: the flows lane's accCashGuard through the form's verdict adapter
    const r=g(s,{kind:'receipt',party:'1110/CO-1',amount:20000000,date:s.date});assert.equal(r.ok,false);assert.equal(r.code,'CASH_LIMIT');assert.match(r.refusal,/s\.186/);
    assert.equal(g(s,{kind:'receipt',party:'x',amount:19999900,date:s.date}).ok,true);
    const w=g(s,{kind:'payment',party:'Kaveri',amount:1200000,date:s.date});assert.equal(w.ok,true);assert.equal(w.deductionLost,true);assert.match(w.warning,/₹10,000/);
    assert.equal(g(s,{kind:'payment',party:'Kaveri',amount:1000000,date:s.date}).warning,undefined);
    assert.equal(g(s,{kind:'payment',party:'Transporter',amount:3000000,date:s.date,transport:true}).warning,undefined,'₹35,000 for goods transport');
    assert.match(text(render('CashGuardNote',{verdict:r},s,'owner')),/Cash refused/);assert.match(render('CashGuardNote',{verdict:r},s,'owner'),/role="alert"/);
    assert.match(text(render('CashGuardNote',{verdict:w},s,'owner')),/Allowed, with a warning/);assert.equal(render('CashGuardNote',{verdict:{ok:true}},s,'owner'),'');
  });

  test('[ACC2-UI] source discipline (ACC-2 integration): the stubs file is deleted; the screens take the lanes\' real selectors through acc2-views (thin adapters, no lane file imported); no hex colours; phone targets 48 px',async()=>{
    const dir='src/features/accounting/';assert.ok(!(await readdir(dir)).includes('_contract-stubs-2.ts'),'the stubs file is deleted at integration');
    const stubSrc=await readFile(dir+'acc2-views.ts','utf8');assert.ok(!/from '\.\.\/\.\.\/domain\/streams\/accounting-/.test(stubSrc),'the adapters import lane files directly');
    // every contract selector the screens use is a real export of the mock barrel; the invented names are thin adapters in acc2-views
    for(const n of ['accManualProblems','accLoans','expVendorOpenBills','expVendorAgeing','expVendorAllocate','expVendorAdvance','expVendorPayments','accAssetRegister','accGstMonth','accMonthLockBlocker','accMonthLockOf',
      'tdsTable','tdsDeducteeReport','tdsReceivableReport','tdsForBill','accCashGuard','accPartyRetireBlocker','chnPayouts','expItcBlockRefusal'])assert.equal(typeof M[n],'function',n+' is exported by the barrel');
    for(const n of ['accManualList','accManualCheck','accLoanRows','expOpenBillRows','expVendorAgeingRows','expVendorPaymentList','accAssetsList','accGstMonthView','tdsRateRows','tdsDeducteeRows','tdsReceivableRows',
      'tdsForBillPaise','accCashVerdict','accPartyRetireReason','chnPayoutList','chnOpenReceivable','expPurchaseItcGuard'])assert.equal(typeof X.views[n],'function',n+' adapter');
    for(const c of ['ACC_MANUAL_SAVE','ACC_MANUAL_DECIDE','ACC_MANUAL_VOID','ACC_LOAN_ADD','EXP_VENDOR_PAY','EXP_VENDOR_PAY_VOID','EXP_VENDOR_RETURN','CHN_PAYOUT','DEPOSIT_APPLY','ACC_TDS_REMIT','ACC_GST_SETOFF','ACC_GST_PAY','ACC_STAT_REMIT','ACC_MONTH_LOCK','ACC_MONTH_LOCK_REQUEST','ACC_MONTH_UNLOCK'])
      assert.ok(stubSrc.includes(`'${c}'`),c+' payload type');
    const screens=[[dir,['manual.tsx','gst.tsx','tds.tsx','cashguard.tsx']],['src/features/expenses/',['purchase.tsx','vendors.tsx']],['src/features/channels/',['payout.tsx']],['src/features/folio/',['deposit.tsx']]];
    for(const [d,fs] of screens)for(const f of fs){const src=await readFile(d+f,'utf8');
      assert.ok(!/_contract-stubs/.test(src),f+' still imports the stubs');
      assert.ok(!/from '\.\.\/\.\.\/domain\/streams\/accounting-/.test(src),f+' imports a lane file directly');}
    const css=await readFile('src/styles/m-accounting2.css','utf8').then(c=>c.replace(/\/\*[\s\S]*?\*\//g,''));assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(css.replace(/var\(--[^)]*\)/g,'')),'hex colour in m-accounting2.css');
    assert.match(css,/\.acc-m \.acc-man-q \.btn[^{]*\{min-height:48px;font-size:16px\}/);assert.match(css,/\.m-sheet \.acc-line select/);
    const nav=await readFile('src/app/navmap.ts','utf8');assert.equal((nav.match(/ACC-2/g)??[]).length,0,'navmap: keywords only, no other change');
  });
}
