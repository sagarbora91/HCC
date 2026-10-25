// RBAC matrix check (25 Oct 2026): every demo user's navigation, command authorization, money visibility and
// self-approval rules versus the documented access rules. Sources, in order of authority:
//   02 Planning/HCC_RBAC_Matrix_v1.md (§5 matrix, §6 limits, §7 routes, §8 SoD, §9 fields) · DEC-040/052/055/057/058 ·
//   HCC_Wave05_UI_Placement_v1.md · HCC_Prototype_Spec_v1.md §1.1 · module specs 13–26 (coarse permissions).
// Where the app and the documents disagree the product code is NOT changed: the documented behaviour is written as a
// known mismatch (ID W05-1, F1 or RBAC-<n>). A known mismatch never fails the suite: it logs KNOWN-FAIL while the app
// still differs and XPASS once a fix lands (then remove it from KNOWN). Every ID is listed in
// 05 Delivery Evidence/Assembly/RBAC_Matrix_Check_2026-10-25.md.
import {build} from 'esbuild';
import {writeFile,readFile} from 'node:fs/promises';
import {tillClear} from './_till.mjs';
await writeFile('tests/.generated/rbac-entry.ts',[
  "export {navItems} from '../../src/app/nav';",
  "export {NAV_CATEGORIES,pagesOfCategory,buildSearchEntries,visibleEntries,searchEntries,navFilterModules,DASHBOARD} from '../../src/app/navmap';",
  "export {registry} from '../../src/services/mock/handlers';"].join('\n'));
await build({entryPoints:['tests/.generated/rbac-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rbac.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const X=await import('./.generated/rbac.mjs');
const coreSrc=await readFile(new URL('../src/services/mock/commands.ts',import.meta.url),'utf8');

// ───────────── Roles and demo users ─────────────
const ABBR={Owner:'OWN',Manager:'MGR',Reception:'REC',Cashier:'CSH',Supervisor:'SUP',Housekeeper:'HKP',Accountant:'ACC',Maintenance:'MNT','Tech admin':'TAD'};
const USERS=['owner','manager','reception','cashier','supervisor','housekeeper','housekeeper2','accountant','maint','techadmin'];
const ALL='OWN MGR REC CSH SUP HKP ACC MNT TAD';
const set=str=>new Set(str.split(' ').filter(Boolean));
const NO_MONEY_USERS=['housekeeper','housekeeper2','maint'];

/** Known mismatches. cells: [userId, key] pairs the hard tests skip; check: the documented behaviour (throws while the app differs). */
const KNOWN=[];
const known=(id,title,cells=[])=>{const k={id,title,cells:cells.map(([u,key])=>u+'|'+key)};KNOWN.push(k);return k;};
const isKnown=(u,key)=>KNOWN.some(k=>k.cells.includes(u+'|'+key));
const usersOf=(...roles)=>USERS.filter(u=>roles.includes(u==='housekeeper2'?'HKP':{owner:'OWN',manager:'MGR',reception:'REC',cashier:'CSH',supervisor:'SUP',housekeeper:'HKP',accountant:'ACC',maint:'MNT',techadmin:'TAD'}[u]));
const cellsFor=(roles,keys)=>usersOf(...roles.split(' ')).flatMap(u=>keys.map(k=>[u,k]));

// ───────────── 1. Navigation: who may open each page (documented) ─────────────
const PAGES={
  overview:['OWN MGR CSH ACC','Prototype Spec §1.1/§3 (Owner, Manager full; Cashier cash cards; Accountant; hidden for HK roles)'],
  rooms:['OWN MGR REC','RES-001; Spec §1.1 Front Desk (+SUP/MNT read: RBAC-1; CSH/ACC view: RBAC-2)'],
  calendar:['OWN MGR REC','RES-001 (CSH/ACC view: RBAC-2)'],
  reservations:['OWN MGR REC','RES-001..004 (CSH/ACC V, SUP M: RBAC-2)'],
  arrivals:['OWN MGR REC','STY-001, GST-002'],
  inhouse:['OWN MGR REC CSH','STY-002 + FOL-001 (Cashier works folios of in-house guests)'],
  customers:['OWN MGR REC CSH','GST-001 (CSH masked; ACC masked: RBAC-3)'],
  folios:['OWN MGR REC CSH','FOL-001 (ACC view: RBAC-3)'],
  cashier:['OWN MGR REC CSH','SHF-001, PAY-001'],
  cashbook:['OWN MGR CSH ACC','DEC-055 / Wave 0.5 placement (Cashier, Manager, Owner, Accountant)'],
  pos:['OWN MGR REC CSH','Module 23 + DEC-052 (shared till)'],
  expenses:['OWN MGR REC CSH','EXP-001 (ACC: RBAC-3; SUP/MNT submit own: RBAC-4)'],
  housekeeping:['OWN MGR SUP HKP MNT','HK-001 (REC view: RBAC-5)'],
  'gsv-my-requests':['OWN MGR SUP HKP MNT','Module 18 §2'],
  maintenance:['OWN MGR SUP HKP MNT','Module 15 §3'],
  inventory:['OWN MGR SUP HKP MNT','Module 16 §3'],
  handover:['OWN MGR REC CSH SUP HKP MNT','Module 19 R1'],
  'my-hr':[ALL,'Wave 0.5 placement: every staff member sees their own'],
  staff:['OWN MGR CSH ACC','Module 17 §2'],
  closing:['OWN MGR','CLS-001/002 (REC/CSH view: RBAC-6; ACC view: RBAC-3)'],
  reports:['OWN MGR CSH ACC','RPT-001/002 (REC/SUP operations view: RBAC-7)'],
  accounting:['OWN MGR CSH ACC','Module 20 §8 (Cashier view/export, no lock)'],
  assistant:['OWN MGR CSH ACC','Module 26 (reports)'],
  compliance:['OWN ACC','DEC-055: Owner and Accountant only'],
  yield:['OWN MGR CSH ACC','Module 13 roles table'],
  groups:['OWN MGR REC','Module 14'],channels:['OWN MGR REC','Module 21'],'booking-site':['OWN MGR REC','Module 22'],
  'guest-services':['OWN MGR REC','Module 18'],messages:['OWN MGR REC','Module 24 §7'],documents:['OWN MGR REC','Module 25 §7'],
  access:['OWN TAD','ADM-001/002 (Manager view-only per Spec §1.1: RBAC-8)'],
  settings:['OWN TAD','ADM-003 + DEC-057/058 (Manager request: RBAC-9)'],
  approvals:['OWN MGR ACC','ADM-004 audit.view + APR-001 (CSH sees it: RBAC-10; TAD view: RBAC-10)'],
  backup:['OWN TAD','ADM-005 (Manager view: RBAC-11)'],
  roadmap:['OWN MGR CSH ACC','Not in the matrix (informational page); current gate accepted'],
};
// Documented cells the app does not honour yet (page keys are 'page:<id>').
const P=ids=>ids.map(i=>'page:'+i);
const W051=known('W05-1','Compliance calendar is listed for Manager and Cashier (drawer, Modules Home tile, search) — DEC-055 says Owner + Accountant only',cellsFor('MGR CSH',P(['compliance'])));
const NAVK=[
  known('RBAC-1','Supervisor and Maintenance cannot open the Room board (Prototype Spec §1.1: read-only room board, no guest finance; RES-001 SUP = M)',cellsFor('SUP MNT',P(['rooms']))),
  known('RBAC-2','Cashier and Accountant cannot view reservations / room board / calendar (RES-001 = V); Supervisor masked reservation view (RES-001 = M)',[...cellsFor('CSH ACC',P(['rooms','calendar','reservations'])),...cellsFor('SUP',P(['reservations']))]),
  known('RBAC-3','Accountant cannot open Folios, Customers, Expenses or Day closing (Spec §1.1 "Folios (read), Expenses, Day closing (read)"; FOL-001 V, GST-001 M, EXP-001..003, CLS-001 V)',cellsFor('ACC',P(['folios','customers','expenses','closing']))),
  known('RBAC-4','Supervisor and Maintenance cannot reach Expenses to submit their own (EXP-001 = A·O)',cellsFor('SUP MNT',P(['expenses']))),
  known('RBAC-5','Reception cannot open the Housekeeping board (HK-001 = V)',cellsFor('REC',P(['housekeeping']))),
  known('RBAC-6','Reception and Cashier cannot open Day closing for the preflight (CLS-001 = V)',cellsFor('REC CSH',P(['closing']))),
  known('RBAC-7','Reception and Supervisor cannot open Reports (RPT-001 report.operations = V)',cellsFor('REC SUP',P(['reports']))),
  known('RBAC-8','Manager cannot open Access control read-only (Prototype Spec §1.1 "Access-control editing (view only)")',cellsFor('MGR',P(['access']))),
  known('RBAC-9','Manager cannot open Master configuration to request changes (ADM-003 MGR = R; §7 "sensitive config raised by TAD / MGR")',cellsFor('MGR',P(['settings']))),
  known('RBAC-10','Approvals & audit: Cashier can open it (ADM-004 and APR-001 = —); Tech admin cannot (ADM-004 = V)',[...cellsFor('CSH',P(['approvals'])),...cellsFor('TAD',P(['approvals']))]),
  known('RBAC-11','Manager cannot open Backup read-only (ADM-005 MGR = V)',cellsFor('MGR',P(['backup']))),
];

/** What each navigation surface shows a user — the same expressions as App.tsx, NavDrawer.tsx, ModulesHome.tsx and SearchPalette.tsx. */
function surfaces(M,s,uid){
  const allowed=new Set(X.navFilterModules(s,X.navItems).filter(n=>M.can(s,uid,n.p)).map(n=>n.id));
  const drawer=new Set([...(allowed.has(X.DASHBOARD.id)?[X.DASHBOARD.id]:[]),...X.NAV_CATEGORIES.flatMap(c=>X.navItems.filter(n=>X.pagesOfCategory(c.id).includes(n.id)&&allowed.has(n.id)).map(n=>n.id))]);
  const home=new Set([...(allowed.has(X.DASHBOARD.id)?[X.DASHBOARD.id]:[]),...X.NAV_CATEGORIES.flatMap(c=>X.pagesOfCategory(c.id).filter(p=>allowed.has(p)))]);
  const entries=X.visibleEntries(X.buildSearchEntries(X.navItems),allowed,p=>M.can(s,uid,p));
  const search=new Set(entries.filter(e=>e.kind==='page').map(e=>e.page));
  return {allowed,drawer,home,search,entries};
}

// ───────────── 2. Commands: roles the documents allow (guard level) ─────────────
const C=(roles,src)=>[roles,src];
const CMDS={
  BOOK:C('OWN MGR REC','RES-002'),CONFIRM:C('OWN MGR REC','RES-002'),CHANGE_DATES:C('OWN MGR REC','RES-003'),MOVE:C('OWN MGR REC','RES-003/STY-002'),CANCEL:C('OWN MGR REC','RES-004'),
  REGISTER:C('OWN MGR REC','GST-002'),GUEST_SAVE:C('OWN MGR REC','GST-002'),CHECKIN:C('OWN MGR REC','STY-001'),CHECKOUT:C('OWN MGR REC','STY-003'),
  COMPANY_CREDIT_REQUEST:C('OWN MGR REC','PAY-005 + §7 (raised by REC)'),
  CHARGE:C('OWN MGR REC CSH','FOL-002'),POST_NIGHTS:C('OWN MGR REC CSH','FOL-002'),DISCOUNT:C('OWN MGR REC','FOL-003'),
  CORRECTION_REQUEST:C('OWN MGR REC ACC','FOL-005'),CREDIT_NOTE:C('OWN MGR REC ACC','FOL-005'),
  PAYMENT:C('OWN MGR REC CSH','PAY-001/002'),REFUND_REQUEST:C('OWN MGR REC CSH','PAY-003'),REFUND_EXECUTE:C('OWN MGR CSH','PAY-004'),
  APPROVAL:C('OWN MGR','APR-001'),
  SHIFT_SUBMIT:C('OWN MGR REC CSH','SHF-001'),SHIFT_CLOSE:C('OWN MGR','legacy direct close = CLS-002 (counting goes through SHIFT_SUBMIT)'),SHIFT_DIFFERENCE_DECIDE:C('OWN MGR ACC','SHF-002'),
  EXPENSE:C('OWN MGR REC CSH SUP ACC MNT','EXP-001'),EXPENSE_SUBMIT:C('OWN MGR REC CSH SUP ACC MNT','EXP-001'),EXPENSE_APPROVE:C('OWN MGR ACC','EXP-002'),EXPENSE_REVERSE:C('OWN MGR CSH ACC','EXP-003'),
  NIGHT_POST:C('OWN MGR','CLS-002'),CLOSE_RUN:C('OWN MGR','CLS-002'),DAY_CLOSE:C('OWN MGR','CLS-002'),NEXT_DAY:C('OWN MGR','CLS-002'),
  CORRECTION_ADJUST:C('OWN MGR ACC','CLS-003'),CORRECTION_ADJUST_DECIDE:C('OWN','CLS-004'),
  TASK:C('OWN MGR SUP HKP MNT','HK-003 (+ MNT on its own assigned repair tasks, HK-001 A·A)'),STAYOVER_GENERATE:C('OWN MGR SUP','HK-002'),
  BLOCK:C('OWN MGR SUP','ROOM-002'),UNBLOCK:C('OWN MGR SUP','ROOM-002'),REPAIR_REQUEST:C('OWN MGR REC SUP HKP MNT','ROOM-001'),
  USER:C('OWN TAD','ADM-001'),USER_CREATE:C('OWN TAD','ADM-001'),USER_UPDATE:C('OWN TAD','ADM-001'),
  GRANT:C('OWN TAD','ADM-002 (TAD = request)'),POLICY_SET:C('OWN TAD','ADM-002 (TAD = request)'),USER_EXCEPTION:C('OWN TAD','ADM-002 (TAD = request)'),POLICY_DECIDE:C('OWN','ADM-002 + SOD-001'),
  SETTINGS:C('OWN','ADM-003 + DEC-058 (tax, limits)'),TARIFF_SCHEDULE:C('OWN','DEC-058 money'),TAX_SCHEDULE:C('OWN','DEC-058 money'),
  SETTING_SET:C('OWN MGR TAD','DEC-058 (TAD non-money groups) + ADM-003 MGR = R (request; app: RBAC-9b)'),CFG_IMPORT:C('OWN TAD','DEC-058'),CFG_REVERT:C('OWN TAD','DEC-058'),CFG_WITHDRAW:C('OWN TAD','DEC-058'),
  CFG_DECIDE:C('OWN MGR','DEC-058 + §7 (Manager decides Manager-approver requests)'),BACKUP_SIMULATE:C('OWN TAD','ADM-005'),
  ACC_LOCK:C('OWN MGR ACC','Module 20 §8'),ACC_UNLOCK:C('OWN','Module 20 §8'),ACC_MAP_SET:C('OWN','Module 20 §8 + matrix note: TAD holds no financial actions'),ACC_MAP_RESET:C('OWN','as ACC_MAP_SET'),
  AI_ASK:C(ALL,'Module 26: any user, answer filtered'),AI_CLEAR:C(ALL,'Module 26: own history'),AI_SETTINGS:C('OWN MGR','Module 26 (approve)'),
  CB_LOCK:C('OWN MGR ACC','Wave 0.5: day locked by a checker'),CB_REOPEN:C('OWN','Wave 0.5: owner reopen'),
  CMP_FILE:C('OWN ACC','DEC-055'),CMP_NA:C('OWN ACC','DEC-055'),CMP_CA_PACK:C('OWN ACC','DEC-055'),
  CMP_REOPEN:C('OWN','DEC-055 manage + DEC-058'),CMP_OBLIGATION_SAVE:C('OWN','DEC-055 manage + DEC-058'),CMP_OBLIGATION_TOGGLE:C('OWN','DEC-055 manage + DEC-058'),CMP_SETTINGS_SET:C('OWN','DEC-058 compliance Owner-only'),
  HR_PAYROLL_RUN:C('OWN ACC','Placement: hrCanPay'),HR_PAYROLL_LOCK:C('OWN ACC','Placement: hrCanPay (maker-checker)'),HR_PAYSLIP_SEND:C('OWN ACC','DEC-056'),HR_ADVANCE_ADD:C('OWN ACC','Module 17'),HR_FNF_SETTLE:C('OWN ACC','Placement'),
  HR_PAYROLL_UNLOCK:C('OWN','DEC-058 payroll Owner-only'),HR_RATES_SET:C('OWN','DEC-058'),HR_STAT_RATES_SET:C('OWN','DEC-058'),HR_LEAVE_POLICY_SET:C('OWN','DEC-058'),
  HR_LEAVE_DECIDE:C('OWN MGR','Placement: managers approve'),HR_STAFF_ACTIVE:C('OWN MGR','Module 17'),HR_ROSTER_SET:C('OWN MGR','Module 17'),HR_ROSTER_COPY:C('OWN MGR','Module 17'),HR_STAFF_SAVE:C('OWN MGR ACC','Module 17 §3'),
  HR_ATT_MARK:C(ALL,'Module 17 self check-in'),HR_LEAVE_REQUEST:C(ALL,'Placement: every staff member applies'),HR_LEAVE_CANCEL:C(ALL,'Module 17 own request'),
  YLD_PROPOSE:C('OWN MGR','Module 13'),YLD_DECIDE:C('OWN','Module 13'),
  ...Object.fromEntries(['YLD_DOW_SET','YLD_GUARD_SET','YLD_OCC_SAVE','YLD_OVERRIDE','YLD_PLAN_SAVE','YLD_PUBLISH','YLD_RESTRICTION_SAVE','YLD_SEASON_SAVE'].map(c=>[c,C('OWN','Module 13 (admin + reports)')])),
  ...Object.fromEntries(['GRP_BLOCK_ALLOT','GRP_BLOCK_CANCEL','GRP_BLOCK_CREATE','GRP_COMPANY_SAVE','GRP_RELEASE','GRP_ROOMING','GRP_SWEEP'].map(c=>[c,C('OWN MGR REC','Module 14')])),
  GRP_LIMIT_SET:C('OWN MGR REC','Module 14 (Owner sets; reservations holders request)'),GRP_LIMIT_DECIDE:C('OWN MGR','Module 14'),
  ...Object.fromEntries(['CHN_ACCEPT','CHN_PULL','CHN_PUSH','CHN_REJECT','CHN_STOP_SELL','CHN_WEB_CONFIRM','CHN_WEB_DECLINE','CHN_WEB_REQUEST'].map(c=>[c,C('OWN MGR REC','Module 21/22')])),
  CHN_CHANNEL_SAVE:C('OWN MGR','Module 21 (reception cannot change commission)'),CHN_CHANNEL_STATUS:C('OWN MGR','Module 21'),
  ...Object.fromEntries(['GSV_REQUEST_CREATE','GSV_REQUEST_CANCEL','GSV_REQUEST_ASSIGN','GSV_COMPLAINT_LOG','GSV_COMPLAINT_UPDATE','GSV_COMPLAINT_RESOLVE','GSV_COMPLAINT_FOLLOWUP','GSV_COMPLAINT_RECOVERY','GSV_LF_CLAIM','GSV_LF_HANDOVER','GSV_LF_DISPOSE'].map(c=>[c,C('OWN MGR REC','Module 18 §2')])),
  GSV_REQUEST_START:C('OWN MGR REC SUP HKP MNT','Module 18 (assignee / department)'),GSV_REQUEST_COMPLETE:C('OWN MGR REC SUP HKP MNT','Module 18 (assignee / department)'),
  GSV_LF_LOG:C('OWN MGR REC SUP HKP MNT','Module 18'),GSV_SETTINGS:C('OWN MGR','Module 18 (approve)'),
  ...Object.fromEntries(['HOV_ITEM','HOV_RUN_SUBMIT','HOV_NOTE_SUBMIT','HOV_NOTE_ACK','HOV_CARRY_DONE'].map(c=>[c,C('OWN MGR REC CSH SUP HKP MNT','Module 19 R1/R7')])),
  ...Object.fromEntries(['HOV_RUN_REOPEN','HOV_TEMPLATE_SAVE','HOV_TEMPLATE_TOGGLE','HOV_CLOCK'].map(c=>[c,C('OWN MGR','Module 19 R2 (approve)')])),
  ...Object.fromEntries(['INV_ISSUE','INV_USE','INV_ROOM_CHANGE','INV_LAUNDRY_SEND','INV_LAUNDRY_RECEIVE','INV_LOSS','INV_COUNT_SUBMIT'].map(c=>[c,C('OWN MGR SUP HKP MNT','Module 16 §3')])),
  INV_GRN:C('OWN MGR SUP','Module 16 (housekeeping + inspect/expenses)'),INV_COUNT_POST:C('OWN MGR SUP','Module 16 (inspect)'),INV_ITEM_SET:C('OWN MGR SUP','Module 16 (inspect)'),
  MNT_WO_CREATE:C('OWN MGR SUP MNT','Module 15 §4 (not Housekeeper)'),MNT_WO_FROM_REPAIR:C('OWN MGR SUP MNT','Module 15 §4 (inspect or assigned tech)'),
  ...Object.fromEntries(['MNT_WO_START','MNT_WO_HOLD','MNT_WO_PART','MNT_WO_COMPLETE'].map(c=>[c,C('OWN MGR SUP MNT','Module 15 §4 (assigned tech or inspect)')])),
  ...Object.fromEntries(['MNT_WO_ASSIGN','MNT_WO_BLOCK','MNT_WO_CANCEL','MNT_WO_VERIFY','MNT_ASSET_SAVE','MNT_PLAN_SAVE','MNT_VENDOR_SAVE','MNT_SPARE_RECEIVE','MNT_PM_GENERATE'].map(c=>[c,C('OWN MGR SUP','Module 15 §3/§4 (inspect)')])),
  MNT_AMC_SAVE:C('OWN MGR','Module 15 §3 (maintenance page + expenses)'),
  ...Object.fromEntries(['MSG_CONSENT','MSG_DOC_CONFIRM','MSG_DOC_DISCARD','MSG_DOC_EXTRACT','MSG_RETRY','MSG_RUN_RULES','MSG_SEND','MSG_SIMULATE'].map(c=>[c,C('OWN MGR REC','Module 24/25 §5')])),
  MSG_RULE:C('OWN MGR','Module 24 §7 (approve)'),MSG_TEMPLATE:C('OWN MGR','Module 24 §7 (approve)'),
  ...Object.fromEntries(['POS_ITEM_ADD','POS_ITEM_QTY','POS_KOT_SEND','POS_KOT_STATUS','POS_MENU_TOGGLE','POS_ORDER_OPEN','POS_SETTLE','POS_VOID_BILL','POS_VOID_ITEM'].map(c=>[c,C('OWN MGR REC CSH','Module 23 §2')])),
  POS_VOID_DECIDE:C('OWN MGR','Module 23 §2 (approve)'),
};
const K=k=>'cmd:'+k;
const CMDK=[
  known('RBAC-4b','Supervisor, Accountant and Maintenance cannot submit their own expenses (EXP-001 = A·O)',cellsFor('SUP ACC MNT',[K('EXPENSE'),K('EXPENSE_SUBMIT')])),
  known('RBAC-9b','Manager cannot request a Master configuration change (ADM-003 MGR = R)',cellsFor('MGR',[K('SETTING_SET')])),
  known('RBAC-12','Cashier can post folio discounts (DISCOUNT; complaint recovery runs DISCOUNT) — FOL-003 CSH = —',cellsFor('CSH',[K('DISCOUNT'),K('GSV_COMPLAINT_RECOVERY')])),
  known('RBAC-13','Folio correction / credit note: Cashier can request (FOL-005 CSH = —); Accountant cannot (FOL-005 ACC = R)',cellsFor('CSH ACC',[K('CORRECTION_REQUEST'),K('CREDIT_NOTE')])),
  known('RBAC-14','Reception can execute an approved refund (PAY-004 REC = —)',cellsFor('REC',[K('REFUND_EXECUTE')])),
  known('RBAC-15','Accountant cannot accept a cash difference (SHF-002 ACC = A; §7 route CSH → MGR / ACC / OWN)',cellsFor('ACC',[K('SHIFT_DIFFERENCE_DECIDE')])),
  known('RBAC-16','Accountant cannot approve or reverse expenses (EXP-002/003 = A); Cashier cannot reverse (EXP-003 = A·S)',[...cellsFor('ACC',[K('EXPENSE_APPROVE'),K('EXPENSE_REVERSE')]),...cellsFor('CSH',[K('EXPENSE_REVERSE')])]),
  known('RBAC-17','Accountant cannot request a closed-date correction (CLS-003 ACC = A)',cellsFor('ACC',[K('CORRECTION_ADJUST')])),
  known('RBAC-18','Tech admin can change the accounting ledger mapping (ACC_MAP_SET / ACC_MAP_RESET) — matrix note: TAD holds no financial actions',cellsFor('TAD',[K('ACC_MAP_SET'),K('ACC_MAP_RESET')])),
  known('RBAC-19','Tech admin can set a company credit limit directly (GRP_LIMIT_SET via admin) — financial power (plan §12; Module 14 says Owner)',cellsFor('TAD',[K('GRP_LIMIT_SET')])),
  known('RBAC-20','Reception and Cashier can save maintenance AMC contracts (MNT_AMC_SAVE needs only expenses) without access to the Maintenance page',cellsFor('REC CSH',[K('MNT_AMC_SAVE')])),
];

/** Representative valid payloads for commands whose empty-payload refusal is not a guard decision (validation runs first,
 * or the check depends on the record). roles = who passes the guard for this record per the documents. */
const OTHER=u=>u==='owner'?'manager':'owner';
const PAYLOADS={
  CB_LOCK:{roles:'OWN MGR ACC',run:(M,u)=>M.execute(M.seed(),u,{type:'CB_LOCK',date:'2026-10-23'})},
  CB_REOPEN:{roles:'OWN',run:(M,u)=>M.execute(M.execute(M.seed(),'accountant',{type:'CB_LOCK',date:'2026-10-23'}),u,{type:'CB_REOPEN',date:'2026-10-23',reason:'Banking slip on the wrong day'})},
  CFG_DECIDE:{roles:'OWN MGR',run:(M,u)=>{let s=M.seed();const raiser=u==='techadmin'?addTad(M,s):['techadmin',s];s=M.execute(raiser[1],raiser[0],{type:'SETTING_SET',setting:'inspection',value:false,reason:'trial'});return M.execute(s,u,{type:'CFG_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  CORRECTION_ADJUST_DECIDE:{roles:'OWN',run:(M,u)=>{let s=M.execute(M.seed(),OTHER(u),{type:'CORRECTION_ADJUST',id:'HCC-1041',date:'2026-10-23',amount:-10000,reason:'Wrong laundry charge'});return M.execute(s,u,{type:'CORRECTION_ADJUST_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  APPROVAL:{roles:'OWN MGR',run:(M,u)=>{let s=M.execute(M.seed(),'reception',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Service recovery'});return M.execute(s,u,{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});}},
  YLD_DECIDE:{roles:'OWN',run:(M,u)=>{const s=M.execute(M.seed(),'manager',{type:'YLD_PROPOSE',from:'2026-10-25',to:'2026-10-27',types:M.yldRoomTypes(M.seed())});return M.execute(s,u,{type:'YLD_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  POLICY_DECIDE:{roles:'OWN',run:(M,u)=>{const [ta,s0]=addTad(M,M.seed());const s=M.execute(s0,ta,{type:'GRANT',role:'Reception',permission:'reports'});return M.execute(s,u,{type:'POLICY_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  GRP_LIMIT_SET:{roles:'OWN MGR REC',run:(M,u)=>M.execute(M.seed(),u,{type:'GRP_LIMIT_SET',company:'CO-1',limit:20000000,reason:'More events booked'})},
  GSV_COMPLAINT_RECOVERY:{roles:'OWN MGR REC',run:(M,u)=>M.execute(M.seed(),u,{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:10000,reason:'Noise complaint'})},
  GSV_REQUEST_ASSIGN:{roles:'OWN MGR REC',run:(M,u)=>M.execute(M.seed(),u,{type:'GSV_REQUEST_ASSIGN',id:'SR-905',assignee:'reception'})},
  GSV_REQUEST_START:{roles:'OWN MGR REC',run:(M,u)=>M.execute(M.seed(),u,{type:'GSV_REQUEST_START',id:'SR-905'})},
  GSV_REQUEST_COMPLETE:{roles:'OWN MGR REC SUP HKP',run:(M,u)=>{const s=M.execute(M.seed(),'housekeeper',{type:'GSV_REQUEST_START',id:'SR-904'});return M.execute(s,u,{type:'GSV_REQUEST_COMPLETE',id:'SR-904',note:'Delivered'});},
    // SR-904 is assigned to Ravi (housekeeper): Kavya (housekeeper2) and Joseph (maint) are not its assignee.
    only:{housekeeper2:false,maint:false}},
  MNT_WO_START:{roles:'OWN MGR SUP MNT',run:(M,u)=>M.execute(M.seed(),u,{type:'MNT_WO_START',id:'WO-0901'})},
  MNT_WO_HOLD:{roles:'OWN MGR SUP MNT',run:(M,u)=>M.execute(M.seed(),u,{type:'MNT_WO_HOLD',id:'WO-0902',reason:'Waiting for the spare'})},
  MNT_WO_PART:{roles:'OWN MGR SUP MNT',run:(M,u)=>M.execute(M.seed(),u,{type:'MNT_WO_PART',id:'WO-0902',spareId:'SP-02',qty:1})},
  MNT_WO_COMPLETE:{roles:'OWN MGR SUP MNT',run:(M,u)=>M.execute(M.seed(),u,{type:'MNT_WO_COMPLETE',id:'WO-0902',note:'Fixed and tested'})},
  MNT_WO_FROM_REPAIR:{roles:'OWN MGR SUP MNT',run:(M,u)=>{const s=M.execute(M.seed(),'supervisor',{type:'REPAIR_REQUEST',room:'101',reason:'Tap leaking'});return M.execute(s,u,{type:'MNT_WO_FROM_REPAIR',taskId:s.tasks.at(-1).id});}},
  MNT_AMC_SAVE:{roles:'OWN MGR',run:(M,u)=>M.execute(M.seed(),u,{type:'MNT_AMC_SAVE',vendorId:'VEN-01',scope:'AC units',from:'2026-11-01',to:'2027-10-31',value:100000,visits:4})},
  DISCOUNT:{roles:'OWN MGR REC',run:(M,u)=>M.execute(M.seed(),u,{type:'DISCOUNT',id:'HCC-1041',amount:10000,reason:'Late room'})},
  REFUND_EXECUTE:{roles:'OWN MGR CSH',run:(M,u)=>{let s=M.execute(M.seed(),u==='cashier'?'reception':'cashier',{type:'REFUND_REQUEST',id:'HCC-1041',receiptId:'seed-pay-0',amount:1000,reason:'Overcharge'});
    s=M.execute(s,u==='manager'?'owner':'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});return M.execute(s,u,{type:'REFUND_EXECUTE',approvalId:s.approvals.at(-1).id,method:'UPI',reference:'RF-1'});}},
  SHIFT_DIFFERENCE_DECIDE:{roles:'OWN MGR ACC',run:(M,u)=>{let s=M.seed();s=M.execute(s,u==='cashier'?'reception':'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-100,reason:'Short by one rupee'});return M.execute(s,u,{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  ACC_MAP_SET:{roles:'OWN',run:(M,u)=>M.execute(M.seed(),u,{type:'ACC_MAP_SET',source:'line:service',account:'4000'})},
  CORRECTION_REQUEST:{roles:'OWN MGR REC ACC',run:(M,u)=>M.execute(M.seed(),u,{type:'CORRECTION_REQUEST',id:'HCC-1041',lineId:'seed-night-0',reason:'Wrong rate'})},
};
/** A second Tech admin, so one can decide what the other raised. */
function addTad(M,s){const n=M.execute(s,'owner',{type:'USER_CREATE',name:'Second Tadmin',role:'Tech admin'});return [n.users.at(-1).id,n];}
const outcome=fn=>{try{fn();return 'ok';}catch(e){return e?.code||('crash:'+e?.message);}};

// ───────────── 3. Money visibility: money-looking fields in selector output ─────────────
const MONEY_KEY=/^(amount|unitCost|cost|costs|rate|price|value|salary|gross|net|balance|total|deposit|paid|expected|counted|ctc|basic|monthly|daily)$/i;
function moneyFields(x,path='',out=[]){if(Array.isArray(x))x.forEach((v,i)=>moneyFields(v,path+'['+i+']',out));
  else if(x&&typeof x==='object')for(const [k,v] of Object.entries(x)){if(MONEY_KEY.test(k)&&typeof v==='number')out.push(path+'.'+k);else moneyFields(v,path+'.'+k,out);}return out;}

export function register({test,M,assert}){
  const s=M.seed();
  const roleOf=u=>ABBR[s.users.find(x=>x.id===u).role];
  const forbidden=c=>e=>e instanceof Error&&e.code==='FORBIDDEN';
  const knownTest=(k,check)=>test(`[RBAC] ${k.id} known mismatch (listed, not failing): ${k.title}`,()=>{
    let fixed=true,why='';try{check();}catch(e){fixed=false;why=e.message;}
    console.log(fixed?`XPASS ${k.id}: the app now matches the documents — remove it from KNOWN in tests/stream-rbac-matrix.mjs`:`KNOWN-FAIL ${k.id}: ${why.split('\n')[0].slice(0,160)}`);});

  // ── 1. Navigation ──
  test('[RBAC] the 10 demo users cover all 9 roles; every nav page has a documented role set',()=>{
    assert.deepEqual(s.users.map(u=>u.id),USERS);assert.equal(new Set(s.users.map(u=>u.role)).size,9);
    for(const n of X.navItems)assert.ok(PAGES[n.id],'nav page without a documented role set (add it to PAGES): '+n.id);
    for(const p of Object.keys(PAGES))assert.ok(X.navItems.some(n=>n.id===p),'PAGES names an unknown page: '+p);});
  for(const u of USERS)test(`[RBAC] nav · ${u} (${roleOf(u)}): drawer, Modules Home tiles and search palette show exactly the documented pages`,()=>{
    const v=surfaces(M,s,u),role=roleOf(u);
    for(const n of X.navItems){
      const app=v.allowed.has(n.id);
      assert.equal(v.drawer.has(n.id),app,`drawer ≠ allowed for ${n.id}`);assert.equal(v.home.has(n.id),app,`home ≠ allowed for ${n.id}`);assert.equal(v.search.has(n.id),app,`search ≠ allowed for ${n.id}`);
      if(isKnown(u,'page:'+n.id))continue;
      assert.equal(app,set(PAGES[n.id][0]).has(role),`${role} · ${n.id}: documents say ${set(PAGES[n.id][0]).has(role)?'visible':'hidden'} (${PAGES[n.id][1]})`);}
    for(const e of v.entries.filter(e=>e.kind==='action'))assert.ok(M.can(s,u,e.needs)&&v.allowed.has(e.page),e.id);});
  test('[RBAC] nav · housekeeping and maintenance reach only the no-money pages',()=>{
    for(const u of NO_MONEY_USERS)assert.deepEqual([...surfaces(M,s,u).allowed].sort(),['gsv-my-requests','handover','housekeeping','inventory','maintenance','my-hr'],u);});
  test('[RBAC] nav · compliance content is Owner + Accountant only: Due-this-week card, assistant answers, commands (DEC-055)',()=>{
    for(const u of USERS){const ok=['owner','accountant'].includes(u);assert.equal(M.cmpHasAccess(s,u),ok,u);assert.equal(M.aiAccess(s,u).compliance,ok,u);}});
  knownTest(W051,()=>{for(const u of ['manager','cashier']){const v=surfaces(M,s,u);
    assert.ok(!v.drawer.has('compliance'),u+' drawer lists Compliance');assert.ok(!v.home.has('compliance'),u+' Modules Home tile');
    assert.ok(!X.searchEntries(v.entries,'gst due date').some(h=>h.entry.page==='compliance'),u+' search finds Compliance');}});
  for(const k of NAVK)knownTest(k,()=>{for(const c of k.cells){const [u,key]=c.split('|'),page=key.slice(5);
    const doc=!set(PAGES[page][0]).has(roleOf(u));assert.equal(surfaces(M,s,u).allowed.has(page),doc,`${roleOf(u)} · ${page}: documents say ${doc?'visible':'hidden'}`);}});
  knownTest(known('RBAC-24','Accounting › Tally export tab is shown to Manager and Cashier (Wave 0.5 placement: Owner, Accountant)'),()=>{
    for(const u of USERS)assert.equal(surfaces(M,s,u).allowed.has('accounting'),['owner','accountant'].includes(u),u+' reaches the Tally tab');});
  knownTest(known('RBAC-25','Reports Excel/CSV export is reachable by the Cashier (RPT-003 report.export CSH = —)'),()=>{
    assert.equal(surfaces(M,s,'cashier').allowed.has('reports'),false,'cashier reaches the Reports export buttons');});

  // ── 2. Command authorization ──
  const ALLCMD=[...new Set([...Object.keys(X.registry),...[...coreSrc.matchAll(/case '([A-Z_]+)'/g)].map(m=>m[1])])].sort();
  test(`[RBAC] commands · every command in the registry (${ALLCMD.length}) has a documented role set`,()=>{
    assert.ok(ALLCMD.length>=185,'registry shrank: '+ALLCMD.length);
    for(const c of ALLCMD)assert.ok(CMDS[c],'command without a documented role set (add it to CMDS): '+c);
    for(const c of Object.keys(CMDS))assert.ok(ALLCMD.includes(c),'CMDS names an unknown command: '+c);
    for(const [c,p] of Object.entries(PAYLOADS))for(const r of set(p.roles))assert.ok(set(CMDS[c][0]).has(r),`payload roles for ${c} exceed the documented set (${r})`);});
  test('[RBAC] commands · a disabled, expired or unknown user is refused FORBIDDEN by every command (DEC-040 step 1, SOD-005)',()=>{
    const dis=structuredClone(s);dis.users.find(u=>u.id==='owner').enabled=false;
    const exp=structuredClone(s);exp.users.find(u=>u.id==='owner').expiresOn='2026-10-01';
    for(const c of ALLCMD){for(const [st,u] of [[dis,'owner'],[exp,'owner'],[s,'nobody']])assert.throws(()=>M.execute(st,u,{type:c}),forbidden(),c+' · '+u);}});
  for(const u of USERS)test(`[RBAC] commands · ${u} (${roleOf(u)}): every command is permitted or refused FORBIDDEN exactly as documented`,()=>{
    const role=roleOf(u),bad=[];
    for(const c of ALLCMD){
      if(isKnown(u,K(c)))continue;
      const doc=set(CMDS[c][0]).has(role),p=PAYLOADS[c];
      if(p){const want=p.only&&u in p.only?p.only[u]:set(p.roles).has(role);const o=outcome(()=>p.run(M,u));
        if((o!=='FORBIDDEN')!==want||o.startsWith('crash'))bad.push(`${c} (payload): ${want?'allowed':'FORBIDDEN'} expected, got ${o}`);continue;}
      const o=outcome(()=>M.execute(s,u,{type:c}));
      if(o.startsWith('crash'))bad.push(`${c}: untyped error ${o}`);
      else if(doc&&o==='FORBIDDEN')bad.push(`${c}: documented allow (${CMDS[c][1]}) but FORBIDDEN`);
      else if(!doc&&o!=='FORBIDDEN')bad.push(`${c}: documented deny (${CMDS[c][1]}) but the guard let it through (${o}) — add a PAYLOADS entry if validation runs first`);}
    assert.deepEqual(bad,[]);});
  test('[RBAC] commands · Tech admin passes the guard of no money command (plan §12) except the listed RBAC-18/19',()=>{
    const money=/^(PAYMENT|CHARGE|POST_NIGHTS|DISCOUNT|REFUND_|CREDIT_NOTE|CORRECTION_|APPROVAL|SHIFT_|EXPENSE|NIGHT_POST|CLOSE_RUN|DAY_CLOSE|NEXT_DAY|CB_|CMP_|HR_(PAYROLL|PAYSLIP|ADVANCE|FNF|RATES|STAT)|YLD_|ACC_|POS_|TARIFF_|TAX_|SETTINGS$)/;
    for(const c of ALLCMD.filter(c=>money.test(c)&&!isKnown('techadmin',K(c)))){const p=PAYLOADS[c];
      assert.equal(p?outcome(()=>p.run(M,'techadmin')):outcome(()=>M.execute(s,'techadmin',{type:c})),'FORBIDDEN',c);}});
  test('[RBAC] commands · Master configuration (DEC-058): Tech admin changes non-money settings, is refused money groups, needs approval where named',()=>{
    const ok=M.execute(s,'techadmin',{type:'SETTING_SET',setting:'brand.address',value:'14 Station Road, Latur (fictional)'});assert.equal(ok.approvals.length,s.approvals.length);
    assert.throws(()=>M.execute(s,'techadmin',{type:'SETTING_SET',setting:'fd.deposit',value:200000}),forbidden());
    for(const u of ['manager','reception','cashier','supervisor','housekeeper','accountant','maint'])assert.throws(()=>M.execute(s,u,{type:'SETTING_SET',setting:'brand.address',value:'X Road'}),forbidden(),u);
    const r=M.execute(s,'techadmin',{type:'SETTING_SET',setting:'sec.pinLock',value:true});assert.equal(r.approvals.at(-1).kind,'config');assert.equal(r.approvals.at(-1).status,'Pending');
    assert.throws(()=>M.execute(r,'manager',{type:'CFG_DECIDE',id:r.approvals.at(-1).id,approve:true}),forbidden(),'Owner-approver request decided by a Manager');
    assert.equal(M.execute(r,'owner',{type:'CFG_DECIDE',id:r.approvals.at(-1).id,approve:true}).approvals.at(-1).status,'Executed');});
  test('[RBAC] commands · Tech admin role-template edits are requests the Owner approves (ADM-002 = R)',()=>{
    const r=M.execute(s,'techadmin',{type:'GRANT',role:'Reception',permission:'reports'});
    assert.equal(r.approvals.at(-1).kind,'policy');assert.equal(r.approvals.at(-1).status,'Pending');assert.deepEqual(r.grants.Reception,s.grants.Reception);});
  for(const k of CMDK)knownTest(k,()=>{const bad=[];for(const c of k.cells){const [u,key]=c.split('|'),cmd=key.slice(4),doc=set(CMDS[cmd][0]).has(roleOf(u)),p=PAYLOADS[cmd];
      const o=p?outcome(()=>p.run(M,u)):outcome(()=>M.execute(s,u,{type:cmd}));if((o!=='FORBIDDEN')!==doc)bad.push(`${roleOf(u)} · ${cmd}: documents say ${doc?'allow':'FORBIDDEN'}, app ${o}`);}
    if(bad.length)throw new Error(bad.join('; '));});
  knownTest(known('RBAC-22','Discount limits are not per role: Owner (unlimited) and Manager (≤ ₹1,000) discounts above the ₹250 hotel threshold go to approval (RBAC §6)'),()=>{
    for(const [u,amt] of [['owner',50000],['manager',80000]]){const r=M.execute(s,u,{type:'DISCOUNT',id:'HCC-1041',amount:amt,reason:'Goodwill'});assert.equal(r.approvals.length,0,`${u} ${amt/100} rupees discount raised an approval`);}});
  knownTest(known('RBAC-23','A Manager approves discounts above the Manager limit of ₹1,000 (APR-001 = L; §6 → Owner)'),()=>{
    let r=M.execute(s,'owner',{type:'POST_NIGHTS',id:'HCC-1041'});r=M.execute(r,'reception',{type:'DISCOUNT',id:'HCC-1041',amount:150000,reason:'Long stay'});
    assert.throws(()=>M.execute(r,'manager',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),forbidden());});

  // ── 3. Money visibility ──
  test('[RBAC] money · housekeeping and maintenance hold no money permission and every money predicate is false',()=>{
    for(const u of NO_MONEY_USERS){
      for(const p of ['billing','payments','reports','expenses','approve','close','checkout','admin'])assert.equal(M.can(s,u,p),false,u+' '+p);
      assert.equal(M.mntCanSeeCost(s,u),false,u);assert.equal(M.invMoneyRole(s,u),false,u);assert.equal(M.hovSeesCash(s,u),false,u);assert.equal(M.hrCanPay(s,u),false,u);assert.equal(M.cmpHasAccess(s,u),false,u);
      const a=M.aiAccess(s,u);assert.equal(a.money||a.cash||a.expenses,false,u);}
    assert.equal(M.mntCanSeeCost(s,'owner'),true);assert.equal(M.hovSeesCash(s,'cashier'),true);});
  test('[RBAC] money · the assistant answers housekeeping and maintenance without a single rupee figure',()=>{
    const qs=[...M.aiTopics.map(t=>t.question),'how much cash is in the drawer','today collections','pending expenses','outstanding balances','ADR and RevPAR'];
    for(const u of NO_MONEY_USERS)for(const q of qs){const a=M.aiAnswer(s,u,q);const txt=JSON.stringify([a.text,a.figures,a.items]);assert.ok(!/₹|Rs\.?\s?\d/.test(txt),`${u} · "${q}" shows money: ${txt.slice(0,120)}`);}});
  test('[RBAC] money · guest requests, handover notes and checklists reachable by housekeeping/maintenance carry no money fields',()=>{
    for(const u of NO_MONEY_USERS){
      assert.deepEqual(moneyFields(M.gsvMyRequests(s,u)),[],u+' gsvMyRequests');assert.deepEqual(moneyFields(M.gsvVisibleOps(s,u)),[],u+' gsvVisibleOps');
      const notes=M.hovNotesFor(s,u);assert.ok(notes.every(n=>!n.cash),u+' handover cash line');assert.deepEqual(moneyFields(notes),[],u+' notes');}});
  test('[RBAC] money · My shifts & pay: housekeeping/maintenance see only their own payslip lines (R-01 own pay = open owner decision, not asserted)',()=>{
    for(const u of NO_MONEY_USERS){const me=M.hrStaffOfUser(s,u)?.id;assert.ok(me,u);assert.ok(M.hrVisiblePayslips(s,u).every(x=>x.line.staffId===me),u+' sees another person’s pay');}
    console.log('OPEN R-01: housekeeping/maintenance see their OWN pay on My shifts & pay — owner decision pending (not asserted either way)');});
  knownTest(known('RBAC-21','The maintenance work-order selector returns part unit costs to the Maintenance user (page hides them; selector / future API must strip money fields)'),()=>{
    const f=moneyFields(M.mntVisibleOrders(s,'maint'));if(f.length)throw new Error('maint receives '+f.join(', '));});

  // ── 4. Self-approval (PERM-SOD-001/004; DEC-031/041) ──
  const own=/own|different person|cannot also execute|prepared the payroll|raised this request/i;
  const refusedSelf=(fn,label)=>assert.throws(fn,e=>e.code==='FORBIDDEN'&&own.test(e.message),label);
  test('[RBAC] self-approval · discount: the requester cannot approve their own discount request',()=>{
    let r=M.execute(s,'manager',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Goodwill'});refusedSelf(()=>M.execute(r,'manager',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),'manager');
    r=M.execute(s,'owner',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Goodwill'});refusedSelf(()=>M.execute(r,'owner',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),'owner');});
  test('[RBAC] self-approval · refund: requester cannot approve it, and cannot execute it after someone else approves (DEC-031)',()=>{
    let r=M.execute(s,'manager',{type:'REFUND_REQUEST',id:'HCC-1041',receiptId:'seed-pay-0',amount:1000,reason:'Overcharge'});
    refusedSelf(()=>M.execute(r,'manager',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),'approve own');
    r=M.execute(r,'owner',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true});
    refusedSelf(()=>M.execute(r,'manager',{type:'REFUND_EXECUTE',approvalId:r.approvals.at(-1).id,method:'UPI',reference:'RF-2'}),'execute own');});
  test('[RBAC] self-approval · cash difference: the counter cannot accept their own difference; a cashier cannot accept any (SOD-004)',()=>{
    let r=M.execute(s,'manager',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-100,reason:'Short by one rupee'});refusedSelf(()=>M.execute(r,'manager',{type:'SHIFT_DIFFERENCE_DECIDE',id:r.approvals.at(-1).id,approve:true}),'manager');
    r=M.execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-100,reason:'Short by one rupee'});assert.throws(()=>M.execute(r,'cashier',{type:'SHIFT_DIFFERENCE_DECIDE',id:r.approvals.at(-1).id,approve:true}),forbidden());});
  test('[RBAC] self-approval · expense: the submitter cannot approve their own expense',()=>{
    const r=M.execute(s,'manager',{type:'EXPENSE',vendor:'Test Vendor',category:'Supplies',amount:10000,method:'Cash'});
    assert.throws(()=>M.execute(r,'manager',{type:'EXPENSE_APPROVE',id:r.expenses.at(-1).id,approve:true}),forbidden());});
  test('[RBAC] self-approval · pay-run lock: whoever prepared the payroll cannot lock it',()=>{
    for(const [maker,checker] of [['accountant','owner'],['owner','accountant']]){const r=M.execute(s,maker,{type:'HR_PAYROLL_RUN',month:'2026-09'});
      const id=M.hrSlice(r).runs.find(x=>x.month==='2026-09').id;
      refusedSelf(()=>M.execute(r,maker,{type:'HR_PAYROLL_LOCK',id}),maker);assert.notEqual(outcome(()=>M.execute(r,checker,{type:'HR_PAYROLL_LOCK',id})),'FORBIDDEN',checker);}});
  test('[RBAC] self-approval · leave: a manager cannot decide a leave request they raised',()=>{
    refusedSelf(()=>M.execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:'HR-LV-4',approve:true}),'HR-LV-4');assert.equal(outcome(()=>M.execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:'HR-LV-4',approve:true})),'ok');});
  test('[RBAC] self-approval · configuration and policy requests: the requester cannot decide them',()=>{
    let r=M.execute(s,'techadmin',{type:'SETTING_SET',setting:'sec.pinLock',value:true});refusedSelf(()=>M.execute(r,'techadmin',{type:'CFG_DECIDE',id:r.approvals.at(-1).id,approve:true}),'config');
    r=M.execute(s,'techadmin',{type:'GRANT',role:'Reception',permission:'reports'});refusedSelf(()=>M.execute(r,'techadmin',{type:'POLICY_DECIDE',id:r.approvals.at(-1).id,approve:true}),'policy');});
  test('[RBAC] self-approval · closed-date correction and tariff publish: the requester cannot decide them',()=>{
    let r=M.execute(s,'owner',{type:'CORRECTION_ADJUST',id:'HCC-1041',date:'2026-10-23',amount:-10000,reason:'Wrong laundry charge'});
    refusedSelf(()=>M.execute(r,'owner',{type:'CORRECTION_ADJUST_DECIDE',id:r.approvals.at(-1).id,approve:true}),'correction');
    r=M.execute(s,'manager',{type:'YLD_PROPOSE',from:'2026-10-25',to:'2026-10-27',types:M.yldRoomTypes(s)});refusedSelf(()=>M.execute(r,'manager',{type:'YLD_DECIDE',id:r.approvals.at(-1).id,approve:true}),'yield');});
  const closeDay=counter=>{let x=M.seed();for(const b of x.bookings.filter(b=>b.status==='Confirmed')){x=M.execute(x,'owner',{type:'REGISTER',id:b.id});x=M.execute(x,'owner',{type:'CHECKIN',id:b.id});}
    x=M.execute(x,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});x=M.execute(x,'owner',{type:'NIGHT_POST'});
    x=M.execute(x,counter,{type:'SHIFT_SUBMIT',counted:M.drawer(x)});x=tillClear(M,x);return M.execute(x,'owner',{type:'DAY_CLOSE'});};
  test('[RBAC] self-approval · cash book: the person who counted or closed the day cannot lock it (same business date)',()=>{
    const r=closeDay('manager');refusedSelf(()=>M.execute(r,'manager',{type:'CB_LOCK',date:r.date}),'counter');refusedSelf(()=>M.execute(r,'owner',{type:'CB_LOCK',date:r.date}),'closer');
    assert.equal(outcome(()=>M.execute(r,'accountant',{type:'CB_LOCK',date:r.date})),'ok');});
  knownTest(known('F1','Cash book: after the next day is opened, the person who counted the closed day can lock it (the counter is not kept in the closing record)'),()=>{
    let r=closeDay('manager');const d=r.date;r=M.execute(r,'owner',{type:'NEXT_DAY'});assert.throws(()=>M.execute(r,'manager',{type:'CB_LOCK',date:d}),forbidden());});

  test('[RBAC] known-mismatch register is unique',()=>{const ids=KNOWN.map(k=>k.id);assert.equal(new Set(ids).size,ids.length);
    console.log('RBAC known mismatches ('+ids.length+'): '+ids.join(', '));});
}
