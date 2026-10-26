// RBAC matrix check (25 Oct 2026): every demo user's navigation, command authorization, money visibility and
// self-approval rules versus the documented access rules. Sources, in order of authority:
//   02 Planning/HCC_RBAC_Matrix_v1.md (§5 matrix, §6 limits, §7 routes, §8 SoD, §9 fields) · DEC-040/052/055/057/058 ·
//   HCC_Wave05_UI_Placement_v1.md · HCC_Prototype_Spec_v1.md §1.1 · module specs 13–26 (coarse permissions).
// Where the app and the documents disagree the documented behaviour is written as a known mismatch (ID F1 or RBAC-<n>).
// A known mismatch never fails the suite: it logs KNOWN-FAIL while the app still differs and XPASS once a fix lands (then
// remove it from KNOWN). Every ID is listed in 05 Delivery Evidence/Assembly/RBAC_Matrix_Check_2026-10-25.md.
// DEC-063 (25 Oct 2026): the owner's rulings G1-G13 resolved W05-1 and RBAC-1...25; the expected role sets below follow the
// revised matrix (HCC_RBAC_Matrix_v1.md "Revised 25 Oct 2026") and the [RBAC] G<n> tests check each ruling.
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
  rooms:['OWN MGR REC','RES-001; Spec §1.1 Front Desk (DEC-063 G7: not SUP/MNT/CSH/ACC)'],
  calendar:['OWN MGR REC','RES-001 (DEC-063 G7)'],
  reservations:['OWN MGR REC','RES-001..004 (DEC-063 G7)'],
  arrivals:['OWN MGR REC','STY-001, GST-002'],
  inhouse:['OWN MGR REC CSH','STY-002 + FOL-001 (Cashier works folios of in-house guests)'],
  customers:['OWN MGR REC CSH','GST-001 (CSH masked; DEC-063 G4: the Accountant does not need Customers)'],
  folios:['OWN MGR REC CSH ACC','FOL-001 (DEC-063 G4: Accountant read-only)'],
  cashier:['OWN MGR REC CSH','SHF-001, PAY-001'],
  cashbook:['OWN MGR CSH ACC','DEC-055 / Wave 0.5 placement (Cashier, Manager, Owner, Accountant)'],
  pos:['OWN MGR REC CSH','Module 23 + DEC-052 (shared till)'],
  expenses:['OWN MGR REC CSH ACC','EXP-001..003 (DEC-063 G4 Accountant; G6: SUP/MNT hand bills to REC/CSH)'],
  housekeeping:['OWN MGR SUP HKP MNT','HK-001 (DEC-063 G8: Reception uses the Room board)'],
  'gsv-my-requests':['OWN MGR SUP HKP MNT','Module 18 §2'],
  maintenance:['OWN MGR SUP HKP MNT','Module 15 §3'],
  inventory:['OWN MGR SUP HKP MNT','Module 16 §3'],
  handover:['OWN MGR REC CSH SUP HKP MNT','Module 19 R1'],
  'my-hr':[ALL,'Wave 0.5 placement: every staff member sees their own'],
  staff:['OWN MGR CSH ACC','Module 17 §2'],
  closing:['OWN MGR ACC','CLS-001/002 (DEC-063 G4: Accountant read-only; G8: not REC/CSH)'],
  reports:['OWN MGR CSH ACC','RPT-001/002 (DEC-063 G8: not REC/SUP)'],
  accounting:['OWN MGR CSH ACC','Module 20 §8 (Cashier view, no lock; DEC-063 G13: Tally tab Owner + Accountant only)'],
  assistant:['OWN MGR CSH ACC','Module 26 (reports)'],
  compliance:['OWN ACC','DEC-055: Owner and Accountant only'],
  yield:['OWN MGR CSH ACC','Module 13 roles table'],
  groups:['OWN MGR REC','Module 14'],channels:['OWN MGR REC','Module 21'],'booking-site':['OWN MGR REC','Module 22'],
  'guest-services':['OWN MGR REC','Module 18'],messages:['OWN MGR REC','Module 24 §7'],documents:['OWN MGR REC','Module 25 §7'],
  access:['OWN TAD','ADM-001/002 (DEC-063 G9: no Manager page)'],
  settings:['OWN TAD','ADM-003 + DEC-057/058 (DEC-063 G9: no Manager requests)'],
  approvals:['OWN MGR ACC','ADM-004 audit.view + APR-001 (DEC-063 G10: not the Cashier; Tech admin view at M4)'],
  backup:['OWN TAD','ADM-005 (DEC-063 G9: the Manager gets the Overview backup card instead)'],
  roadmap:['OWN MGR CSH ACC','Not in the matrix (informational page); current gate accepted'],
};
// Documented cells the app does not honour yet (page keys are 'page:<id>').
const P=ids=>ids.map(i=>'page:'+i);

/** What each navigation surface shows a user — the same expressions as App.tsx, NavDrawer.tsx, ModulesHome.tsx and SearchPalette.tsx. */
function surfaces(M,s,uid){
  const allowed=new Set(X.navFilterModules(s,X.navItems,uid).filter(n=>M.can(s,uid,n.p)).map(n=>n.id));
  const drawer=new Set([...(allowed.has(X.DASHBOARD.id)?[X.DASHBOARD.id]:[]),...X.NAV_CATEGORIES.flatMap(c=>X.navItems.filter(n=>X.pagesOfCategory(c.id).includes(n.id)&&allowed.has(n.id)).map(n=>n.id))]);
  const home=new Set([...(allowed.has(X.DASHBOARD.id)?[X.DASHBOARD.id]:[]),...X.NAV_CATEGORIES.flatMap(c=>X.pagesOfCategory(c.id).filter(p=>allowed.has(p)))]);
  const entries=X.visibleEntries(X.buildSearchEntries(X.navItems),allowed,p=>M.can(s,uid,p));
  const search=new Set(entries.filter(e=>e.kind==='page').map(e=>e.page));
  return {allowed,drawer,home,search,entries};
}

// ───────────── 2. Commands: roles the documents allow (guard level) ─────────────
const C=(roles,src)=>[roles,src];
const CMDS={
  BOOK:C('OWN MGR REC','RES-002'),CONFIRM:C('OWN MGR REC','RES-002'),CHANGE_DATES:C('OWN MGR REC','RES-003'),MOVE:C('OWN MGR REC','RES-003/STY-002'),STAY_EARLY_DEPARTURE:C('OWN MGR REC','STY-002 (EarlyDeparture)'),CANCEL:C('OWN MGR REC','RES-004'),
  REGISTER:C('OWN MGR REC','GST-002'),GUEST_SAVE:C('OWN MGR REC','GST-002'),GUEST_ID_VIEW:C('OWN MGR REC','GST-003 (DEC-068 Show ID)'),CHECKIN:C('OWN MGR REC','STY-001'),CHECKOUT:C('OWN MGR REC','STY-003'),
  COMPANY_CREDIT_REQUEST:C('OWN MGR REC','PAY-005 + §7 (raised by REC)'),
  CHARGE:C('OWN MGR REC CSH','FOL-002'),POST_NIGHTS:C('OWN MGR REC CSH','FOL-002'),DISCOUNT:C('OWN MGR REC','FOL-003'),
  CORRECTION_REQUEST:C('OWN MGR REC CSH ACC','FOL-005 (DEC-063 G3)'),CREDIT_NOTE:C('OWN MGR REC CSH ACC','FOL-005 (DEC-063 G3)'),
  PAYMENT:C('OWN MGR REC CSH','PAY-001/002'),REFUND_REQUEST:C('OWN MGR REC CSH','PAY-003'),REFUND_EXECUTE:C('OWN MGR REC CSH','PAY-004 (DEC-063 G2: REC own shift, never own request)'),
  APPROVAL:C('OWN MGR','APR-001'),
  SHIFT_SUBMIT:C('OWN MGR REC CSH','SHF-001'),SHIFT_CLOSE:C('OWN MGR','legacy direct close = CLS-002 (counting goes through SHIFT_SUBMIT)'),SHIFT_DIFFERENCE_DECIDE:C('OWN MGR ACC','SHF-002'),
  EXPENSE:C('OWN MGR REC CSH ACC','EXP-001 (DEC-063 G6: SUP/MNT via REC/CSH)'),EXPENSE_SUBMIT:C('OWN MGR REC CSH ACC','EXP-001'),EXPENSE_APPROVE:C('OWN MGR ACC','EXP-002'),EXPENSE_REVERSE:C('OWN MGR ACC','EXP-003 (DEC-063 G5: not the Cashier)'),
  NIGHT_POST:C('OWN MGR','CLS-002'),CLOSE_RUN:C('OWN MGR','CLS-002'),DAY_CLOSE:C('OWN MGR','CLS-002'),NEXT_DAY:C('OWN MGR','CLS-002'),
  CORRECTION_ADJUST:C('OWN MGR ACC','CLS-003'),CORRECTION_ADJUST_DECIDE:C('OWN','CLS-004'),
  TASK:C('OWN MGR SUP HKP MNT','HK-003 (+ MNT on its own assigned repair tasks, HK-001 A·A)'),STAYOVER_GENERATE:C('OWN MGR SUP','HK-002'),
  BLOCK:C('OWN MGR SUP','ROOM-002'),UNBLOCK:C('OWN MGR SUP','ROOM-002'),REPAIR_REQUEST:C('OWN MGR REC SUP HKP MNT','ROOM-001'),
  USER:C('OWN TAD','ADM-001'),USER_CREATE:C('OWN TAD','ADM-001'),USER_UPDATE:C('OWN TAD','ADM-001'),
  GRANT:C('OWN TAD','ADM-002 (TAD = request)'),POLICY_SET:C('OWN TAD','ADM-002 (TAD = request)'),USER_EXCEPTION:C('OWN TAD','ADM-002 (TAD = request)'),POLICY_DECIDE:C('OWN','ADM-002 + SOD-001'),
  SETTINGS:C('OWN','ADM-003 + DEC-058 (tax, limits)'),TARIFF_SCHEDULE:C('OWN','DEC-058 money'),TAX_SCHEDULE:C('OWN','DEC-058 money'),
  SETTING_SET:C('OWN TAD','DEC-058 (TAD non-money groups); DEC-063 G9: no Manager requests'),CFG_IMPORT:C('OWN TAD','DEC-058'),CFG_REVERT:C('OWN TAD','DEC-058'),CFG_WITHDRAW:C('OWN TAD','DEC-058'),
  CFG_DECIDE:C('OWN MGR','DEC-058 + §7 (Manager decides Manager-approver requests)'),BACKUP_SIMULATE:C('OWN TAD','ADM-005'),
  ACC_LOCK:C('OWN MGR ACC','Module 20 §8'),ACC_UNLOCK:C('OWN','Module 20 §8'),ACC_MAP_SET:C('OWN','Module 20 §8 + matrix note: TAD holds no financial actions'),ACC_MAP_RESET:C('OWN','as ACC_MAP_SET'),
  AI_ASK:C(ALL,'Module 26: any user, answer filtered'),AI_CLEAR:C(ALL,'Module 26: own history'),AI_SETTINGS:C('OWN MGR','Module 26 (approve)'),
  CB_LOCK:C('OWN MGR ACC','Wave 0.5: day locked by a checker'),CB_REOPEN:C('OWN','Wave 0.5: owner reopen'),
  CMP_FILE:C('OWN ACC','DEC-055'),CMP_NA:C('OWN ACC','DEC-055'),CMP_CA_PACK:C('OWN ACC','DEC-055'),
  CMP_REOPEN:C('OWN','DEC-055 manage + DEC-058'),CMP_OBLIGATION_SAVE:C('OWN','DEC-055 manage + DEC-058'),CMP_OBLIGATION_TOGGLE:C('OWN','DEC-055 manage + DEC-058'),CMP_SETTINGS_SET:C('OWN','DEC-058 compliance Owner-only'),
  HR_PAYROLL_RUN:C('OWN ACC','Placement: hrCanPay'),HR_PAYROLL_LOCK:C('OWN ACC','Placement: hrCanPay (maker-checker)'),HR_PAYSLIP_SEND:C('OWN ACC','DEC-056'),HR_ADVANCE_ADD:C('OWN ACC','Module 17'),HR_FNF_SETTLE:C('OWN ACC','Placement'),HR_FNF_CONFIRM:C('OWN ACC','F&F maker-checker: the other payroll user (Owner / Accountant)'),HR_FNF_DISCARD:C('OWN ACC','F&F draft discard (payroll users)'),HR_PAYROLL_BANK_FILE:C('OWN ACC','Payroll bank file (payroll users, DEC-058 money)'),
  HR_PAYROLL_UNLOCK:C('OWN','DEC-058 payroll Owner-only'),HR_RATES_SET:C('OWN','DEC-058'),HR_STAT_RATES_SET:C('OWN','DEC-058'),HR_LEAVE_POLICY_SET:C('OWN','DEC-058'),
  HR_LEAVE_DECIDE:C('OWN MGR','Placement: managers approve'),HR_STAFF_ACTIVE:C('OWN MGR','Module 17'),HR_ROSTER_SET:C('OWN MGR','Module 17'),HR_ROSTER_COPY:C('OWN MGR','Module 17'),HR_STAFF_SAVE:C('OWN MGR ACC','Module 17 §3'),
  HR_ATT_MARK:C(ALL,'Module 17 self check-in'),HR_LEAVE_REQUEST:C(ALL,'Placement: every staff member applies'),HR_LEAVE_CANCEL:C(ALL,'Module 17 own request'),
  YLD_PROPOSE:C('OWN MGR','Module 13'),YLD_DECIDE:C('OWN','Module 13'),
  ...Object.fromEntries(['YLD_DOW_SET','YLD_GUARD_SET','YLD_OCC_SAVE','YLD_OVERRIDE','YLD_PLAN_SAVE','YLD_PUBLISH','YLD_RESTRICTION_SAVE','YLD_SEASON_SAVE'].map(c=>[c,C('OWN','Module 13 (admin + reports)')])),
  ...Object.fromEntries(['GRP_BLOCK_ALLOT','GRP_BLOCK_CANCEL','GRP_BLOCK_CREATE','GRP_COMPANY_SAVE','GRP_RELEASE','GRP_ROOMING','GRP_SWEEP'].map(c=>[c,C('OWN MGR REC','Module 14')])),
  GRP_LIMIT_SET:C('OWN MGR REC','Module 14 (Owner sets; reservations holders request; DEC-063 G11: not the Tech admin)'),GRP_LIMIT_DECIDE:C('OWN MGR','Module 14'),
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
  MNT_AMC_SAVE:C('OWN MGR','Module 15 §3 (DEC-063 G12: inspect + expenses)'),
  ...Object.fromEntries(['MSG_CONSENT','MSG_DOC_CONFIRM','MSG_DOC_DISCARD','MSG_DOC_EXTRACT','MSG_RETRY','MSG_RUN_RULES','MSG_SEND','MSG_SIMULATE'].map(c=>[c,C('OWN MGR REC','Module 24/25 §5')])),
  MSG_RULE:C('OWN MGR','Module 24 §7 (approve)'),MSG_TEMPLATE:C('OWN MGR','Module 24 §7 (approve)'),
  ...Object.fromEntries(['POS_ITEM_ADD','POS_ITEM_QTY','POS_KOT_SEND','POS_KOT_STATUS','POS_MENU_TOGGLE','POS_ORDER_OPEN','POS_SETTLE','POS_VOID_BILL','POS_VOID_ITEM'].map(c=>[c,C('OWN MGR REC CSH','Module 23 §2')])),
  POS_VOID_DECIDE:C('OWN MGR','Module 23 §2 (approve)'),
};
const K=k=>'cmd:'+k;

/** Representative valid payloads for commands whose empty-payload refusal is not a guard decision (validation runs first,
 * or the check depends on the record). roles = who passes the guard for this record per the documents. */
const OTHER=u=>u==='owner'?'manager':'owner';
const PAYLOADS={
  // A closed day counted by the Cashier and closed by someone other than the user under test (counter/closer never lock).
  CB_LOCK:{roles:'OWN MGR ACC',run:(M,u)=>{const x=closeDayBy(M,'cashier',u==='owner'?'manager':'owner');return M.execute(x,u,{type:'CB_LOCK',date:x.date});}},
  CB_REOPEN:{roles:'OWN',run:(M,u)=>{let x=closeDayBy(M,'cashier','owner');x=M.execute(x,'accountant',{type:'CB_LOCK',date:x.date});return M.execute(x,u,{type:'CB_REOPEN',date:x.date,reason:'Banking slip on the wrong day'});}},
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
  REFUND_EXECUTE:{roles:'OWN MGR REC CSH',run:(M,u)=>{let s=M.execute(M.seed(),u==='cashier'?'reception':'cashier',{type:'REFUND_REQUEST',id:'HCC-1041',receiptId:'seed-pay-0',amount:1000,reason:'Overcharge'});
    s=M.execute(s,u==='manager'?'owner':'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});return M.execute(s,u,{type:'REFUND_EXECUTE',approvalId:s.approvals.at(-1).id,method:'UPI',reference:'RF-1'});}},
  SHIFT_DIFFERENCE_DECIDE:{roles:'OWN MGR ACC',run:(M,u)=>{let s=M.seed();s=M.execute(s,u==='cashier'?'reception':'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-100,reason:'Short by one rupee'});return M.execute(s,u,{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1).id,approve:true});}},
  ACC_MAP_SET:{roles:'OWN',run:(M,u)=>M.execute(M.seed(),u,{type:'ACC_MAP_SET',source:'line:service',account:'4000'})},
  CORRECTION_REQUEST:{roles:'OWN MGR REC CSH ACC',run:(M,u)=>M.execute(M.seed(),u,{type:'CORRECTION_REQUEST',id:'HCC-1041',lineId:'seed-night-0',reason:'Wrong rate'})},
};
/** Seed day closed: every arrival checked in, EXP-102 approved, nights posted, drawer counted by `counter`, closed by `closer`. */
function closeDayBy(M,counter,closer){let x=M.seed();for(const b of x.bookings.filter(b=>b.status==='Confirmed')){x=M.execute(x,'owner',{type:'REGISTER',id:b.id});x=M.execute(x,'owner',{type:'CHECKIN',id:b.id});}
  x=M.execute(x,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});x=M.execute(x,'owner',{type:'NIGHT_POST'});
  x=M.execute(x,counter,{type:'SHIFT_SUBMIT',counted:M.drawer(x)});x=tillClear(M,x);return M.execute(x,closer,{type:'DAY_CLOSE'});}
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
  test('[RBAC] nav · Compliance calendar is not listed for Manager or Cashier: drawer, Modules Home, search (W05-1, DEC-055)',()=>{
    for(const u of ['manager','cashier']){const v=surfaces(M,s,u);
      assert.ok(!v.drawer.has('compliance'),u+' drawer lists Compliance');assert.ok(!v.home.has('compliance'),u+' Modules Home tile');
      assert.ok(!X.searchEntries(v.entries,'gst due date').some(h=>h.entry.page==='compliance'),u+' search finds Compliance');}});
  test('[RBAC] G13 · Accounting › Tally export tab: Owner + Accountant only; Manager and Cashier keep Accounting without it (Wave 0.5, DEC-055)',()=>{
    for(const u of USERS){assert.equal(M.rbacCanTally(s,u),['owner','accountant'].includes(u),u);
      if(['manager','cashier'].includes(u))assert.ok(surfaces(M,s,u).allowed.has('accounting'),u+' lost Accounting');}});
  test('[RBAC] G13 · Reports Excel/CSV export (RPT-003): Owner, Manager, Accountant; the Cashier still opens Reports and prints',()=>{
    for(const u of USERS)assert.equal(M.rbacCanExportReports(s,u),['owner','manager','accountant'].includes(u),u);
    assert.ok(surfaces(M,s,'cashier').allowed.has('reports'));});
  test('[RBAC] G13 · Accounting voucher CSV/Excel export: Owner, Manager, Accountant; the Cashier keeps Accounting without it (hidden and refused)',()=>{
    for(const u of USERS)assert.equal(M.rbacCanExportVouchers(s,u),['owner','manager','accountant'].includes(u),u);
    assert.ok(surfaces(M,s,'cashier').allowed.has('accounting'));});
  test('[RBAC] G4 · Day pack (printed day summary) follows the Day closing view: Owner, Manager, Accountant',()=>{
    for(const u of USERS)assert.equal(M.rbacCanDayPack(s,u),['owner','manager','accountant'].includes(u),u);});

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
  test('[RBAC] commands · Tech admin passes the guard of no money command (plan §12; DEC-063 G11)',()=>{
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
  // ── DEC-063 rulings G1–G13 (focused checks; every guard is exercised through execute(), not the UI) ──
  test('[RBAC] G1 · discount limits per role: Reception ≤ ₹250, Manager ≤ ₹1,000, Owner no limit; above → approval request',()=>{
    const s0=M.execute(s,'owner',{type:'POST_NIGHTS',id:'HCC-1041'});
    const d=(u,amount)=>M.execute(s0,u,{type:'DISCOUNT',id:'HCC-1041',amount,reason:'Goodwill'});
    const posted=r=>r.approvals.length===s0.approvals.length&&r.bookings.find(b=>b.id==='HCC-1041').lines.at(-1).kind==='discount';
    assert.ok(posted(d('reception',25000)),'reception ₹250');assert.equal(d('reception',25100).approvals.at(-1).status,'Pending');
    assert.ok(posted(d('manager',100000)),'manager ₹1,000');assert.equal(d('manager',100100).approvals.at(-1).status,'Pending');
    assert.ok(posted(d('owner',300000)),'owner ₹3,000');
    assert.equal(M.rbacDiscountLimit(s,'reception'),25000);assert.equal(M.rbacDiscountLimit(s,'manager'),100000);assert.equal(M.rbacDiscountLimit(s,'owner'),undefined);});
  test('[RBAC] G1 · the Cashier gives no discount, directly or through complaint recovery (command level)',()=>{
    assert.throws(()=>M.execute(s,'cashier',{type:'DISCOUNT',id:'HCC-1041',amount:100,reason:'x'}),forbidden());
    assert.throws(()=>M.execute(s,'cashier',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:100,reason:'x'}),forbidden());
    assert.equal(M.rbacCanDiscount(s,'cashier'),false);assert.equal(M.rbacCanDiscount(s,'reception'),true);});
  test('[RBAC] G1 · a Manager approves discount requests only up to ₹1,000; above goes to the Owner',()=>{
    let r=M.execute(s,'owner',{type:'POST_NIGHTS',id:'HCC-1041'});
    const low=M.execute(r,'reception',{type:'DISCOUNT',id:'HCC-1041',amount:100000,reason:'Long stay'});
    assert.equal(M.execute(low,'manager',{type:'APPROVAL',id:low.approvals.at(-1).id,approve:true}).approvals.at(-1).status,'Approved');
    r=M.execute(r,'reception',{type:'DISCOUNT',id:'HCC-1041',amount:150000,reason:'Long stay'});const id=r.approvals.at(-1).id;
    assert.throws(()=>M.execute(r,'manager',{type:'APPROVAL',id,approve:true}),forbidden());
    assert.match(M.adminDecideBlock(r,'manager',M.adminApprovalInbox(r).find(x=>x.a.id===id)),/approval limit/);
    assert.equal(M.execute(r,'owner',{type:'APPROVAL',id,approve:true}).approvals.at(-1).status,'Approved');});
  const refundApproved=(requester,approver)=>{const r=M.execute(s,requester,{type:'REFUND_REQUEST',id:'HCC-1041',receiptId:'seed-pay-0',amount:1000,reason:'Overcharge'});
    return M.execute(r,approver,{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true});};
  test('[RBAC] G2 · Reception pays out a refund someone else asked for and someone else approved',()=>{
    const r=refundApproved('cashier','manager');const x=M.execute(r,'reception',{type:'REFUND_EXECUTE',approvalId:r.approvals.at(-1).id,method:'UPI',reference:'RF-9'});
    assert.equal(x.approvals.at(-1).status,'Executed');assert.equal(x.approvals.at(-1).meta.executedBy,'reception');});
  test('[RBAC] G2 · Reception never pays out a refund they requested; a cash refund needs the open shift drawer',()=>{
    let r=refundApproved('reception','manager');
    assert.throws(()=>M.execute(r,'reception',{type:'REFUND_EXECUTE',approvalId:r.approvals.at(-1).id,method:'UPI',reference:'RF-9'}),e=>e.code==='FORBIDDEN'&&/cannot also execute/.test(e.message));
    r=refundApproved('cashier','manager');r.shiftOpen=false;
    assert.equal(outcome(()=>M.execute(r,'reception',{type:'REFUND_EXECUTE',approvalId:r.approvals.at(-1).id,method:'Cash'})),'INVALID_TRANSITION');});
  test('[RBAC] G3 · Reception, Cashier, Accountant, Manager may request a correction; only the Owner approves it',()=>{
    for(const u of ['cashier','accountant','reception','manager']){
      const r=M.execute(s,u,{type:'CORRECTION_REQUEST',id:'HCC-1041',lineId:'seed-night-0',reason:'Wrong rate'});const a=r.approvals.at(-1);
      assert.equal(a.kind,'correction',u);assert.equal(a.actor,u);
      if(u!=='manager'){assert.throws(()=>M.execute(r,'manager',{type:'APPROVAL',id:a.id,approve:true}),forbidden(),'manager approves '+u);
        assert.match(M.adminDecideBlock(r,'manager',M.adminApprovalInbox(r).find(x=>x.a.id===a.id)),/Owner/);}
      assert.equal(M.execute(r,'owner',{type:'APPROVAL',id:a.id,approve:true}).approvals.at(-1).status,'Executed',u);}
    for(const u of ['supervisor','housekeeper','maint','techadmin'])assert.throws(()=>M.execute(s,u,{type:'CORRECTION_REQUEST',id:'HCC-1041',lineId:'seed-night-0',reason:'x'}),forbidden(),u);
    assert.ok(M.RBAC_OWNER_DECIDES.includes('credit-note'));});
  test('[RBAC] G4 · Accountant decides a cash difference, approves and reverses expenses, requests a closed-date correction',()=>{
    let r=M.execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(s)-100,reason:'Short by one rupee'});
    assert.equal(M.adminDecideBlock(r,'accountant',M.adminApprovalInbox(r).find(x=>x.a.kind==='cash-difference')),undefined);
    assert.equal(M.execute(r,'accountant',{type:'SHIFT_DIFFERENCE_DECIDE',id:r.approvals.at(-1).id,approve:true}).shift.status,'Closed');
    r=M.execute(s,'accountant',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});assert.equal(r.expenses.find(e=>e.id==='EXP-102').status,'Posted');
    r=M.execute(r,'accountant',{type:'EXPENSE_REVERSE',id:'EXP-102',reason:'Duplicate bill'});assert.equal(r.expenses.find(e=>e.id==='EXP-102').status,'Reversed');
    r=M.execute(s,'accountant',{type:'CORRECTION_ADJUST',id:'HCC-1041',date:'2026-10-23',amount:-10000,reason:'Wrong laundry charge'});
    assert.equal(r.approvals.at(-1).kind,'closed-date-correction');assert.throws(()=>M.execute(r,'accountant',{type:'CORRECTION_ADJUST_DECIDE',id:r.approvals.at(-1).id,approve:true}),forbidden());
    assert.equal(M.adminExpenseBlock(s,'accountant','reception'),undefined);assert.match(M.adminExpenseBlock(s,'cashier','reception'),/approve/);});
  test('[RBAC] G4 · Accountant submits own expenses but cannot approve them, and gets no counter or approval power',()=>{
    let r=M.execute(s,'accountant',{type:'EXPENSE',vendor:'Stationery Mart',category:'Supplies',amount:20000,method:'UPI'});
    assert.throws(()=>M.execute(r,'accountant',{type:'EXPENSE_APPROVE',id:r.expenses.at(-1).id,approve:true}),forbidden());
    r=M.execute(s,'accountant',{type:'EXPENSE',vendor:'Stationery Mart',category:'Supplies',amount:20000,method:'UPI',draft:true});
    assert.equal(M.execute(r,'accountant',{type:'EXPENSE_SUBMIT',id:r.expenses.at(-1).id}).expenses.at(-1).status,'Pending');
    for(const c of [{type:'DISCOUNT',id:'HCC-1041',amount:100,reason:'x'},{type:'CHARGE',id:'HCC-1041',amount:100,label:'x'},{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'},{type:'DAY_CLOSE'},{type:'NIGHT_POST'}])
      assert.throws(()=>M.execute(s,'accountant',c),forbidden(),c.type);
    const d=M.execute(s,'reception',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Goodwill'});
    assert.throws(()=>M.execute(d,'accountant',{type:'APPROVAL',id:d.approvals.at(-1).id,approve:true}),forbidden());
    assert.equal(M.rbacCanViewFolios(s,'accountant'),true);assert.equal(M.can(s,'accountant','billing'),false,'read-only: no billing grant');
    const v=surfaces(M,s,'accountant');for(const p of ['folios','expenses','closing','approvals'])assert.ok(v.allowed.has(p),p);assert.ok(!v.allowed.has('customers'));});
  test('[RBAC] G5/G6 · the Cashier never reverses an expense; supervisor and maintenance enter no expenses (command level)',()=>{
    const r=M.execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    assert.throws(()=>M.execute(r,'cashier',{type:'EXPENSE_REVERSE',id:'EXP-102',reason:'Wrong'}),forbidden());
    for(const u of ['supervisor','maint'])assert.throws(()=>M.execute(s,u,{type:'EXPENSE',vendor:'Hardware Shop',category:'Repairs',amount:5000,method:'Cash'}),forbidden(),u);});
  test('[RBAC] G9 · Manager: no Access control, no setting requests (DEC-058), but a last-backup warning card with the Owner',()=>{
    assert.throws(()=>M.execute(s,'manager',{type:'SETTING_SET',setting:'brand.address',value:'X Road'}),forbidden());
    const v=surfaces(M,s,'manager');for(const p of ['access','settings','backup'])assert.ok(!v.allowed.has(p),p);
    for(const u of USERS)assert.equal(M.rbacSeesBackupStatus(s,u),['owner','manager'].includes(u),u);
    const b=M.hkBackupStatus(s);assert.equal(b.failed,false);assert.equal(b.hoursAgo,6);assert.equal(b.warn,false);assert.equal(b.recentFailures,1);
    const late=M.hkBackupStatus({...s,clock:'2026-10-24T01:00'});assert.equal(late.failed,true,'latest run (23 Oct night) failed');assert.equal(late.warn,true);});
  test('[RBAC] G10 · Approvals & audit: Owner, Manager, Accountant — not the Cashier, not the Tech admin until M4',()=>{
    for(const u of USERS)assert.equal(surfaces(M,s,u).allowed.has('approvals'),['owner','manager','accountant'].includes(u),u);});
  test('[RBAC] G11 · ledger mapping and company credit limits: Owner only, never the Tech admin',()=>{
    for(const c of [{type:'ACC_MAP_SET',source:'line:service',account:'4000'},{type:'ACC_MAP_RESET',source:'line:service'},{type:'GRP_LIMIT_SET',company:'CO-1',limit:20000000,reason:'More events'}])
      assert.throws(()=>M.execute(s,'techadmin',c),forbidden(),c.type);
    const o=M.execute(s,'owner',{type:'GRP_LIMIT_SET',company:'CO-1',limit:20000000,reason:'More events'});assert.equal(M.grpCompany(o,'CO-1').creditLimit,20000000);
    const m=M.execute(s,'manager',{type:'GRP_LIMIT_SET',company:'CO-1',limit:20000000,reason:'More events'});assert.equal(M.grpCompany(m,'CO-1').creditLimit,M.grpCompany(s,'CO-1').creditLimit,'manager only requests');
    assert.equal(M.rbacMoneyAdmin(s,'owner'),true);assert.equal(M.rbacMoneyAdmin(s,'techadmin'),false);});
  test('[RBAC] G12 · AMC contracts: Manager/Owner only; the maintenance selector output carries no part prices',()=>{
    const amc={type:'MNT_AMC_SAVE',vendorId:'VEN-01',scope:'AC units',from:'2026-11-01',to:'2027-10-31',value:100000,visits:4};
    for(const u of ['reception','cashier','supervisor','maint','accountant'])assert.throws(()=>M.execute(s,u,amc),forbidden(),u);
    assert.equal(outcome(()=>M.execute(s,'manager',amc)),'ok');
    for(const u of NO_MONEY_USERS)assert.deepEqual(moneyFields(M.mntVisibleOrders(s,u)),[],u);
    assert.ok(M.mntVisibleOrders(s,'owner').some(w=>w.parts.some(p=>typeof p.unitCost==='number')),'owner keeps part costs');
    assert.ok(M.mntVisibleOrders(s,'supervisor').every(w=>w.parts.every(p=>p.unitCost===undefined)),'supervisor (no money grant) receives no part costs');});

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

  // ── 4. Self-approval (PERM-SOD-001/004; DEC-031/041) ──
  const own=/own|different person|cannot also execute|prepared the payroll|raised this request/i;
  const refusedSelf=(fn,label)=>assert.throws(fn,e=>e.code==='FORBIDDEN'&&own.test(e.message),label);
  test('[RBAC] self-approval · discount: the requester cannot approve their own discount request (the Owner has no limit, so raises none)',()=>{
    let r=M.execute(s,'manager',{type:'DISCOUNT',id:'HCC-1041',amount:150000,reason:'Goodwill'});refusedSelf(()=>M.execute(r,'manager',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),'manager');
    r=M.execute(s,'reception',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Goodwill'});r.grants.Reception=[...r.grants.Reception,'approve'];
    refusedSelf(()=>M.execute(r,'reception',{type:'APPROVAL',id:r.approvals.at(-1).id,approve:true}),'reception');
    assert.equal(M.execute(s,'owner',{type:'DISCOUNT',id:'HCC-1041',amount:50000,reason:'Goodwill'}).approvals.length,s.approvals.length);});
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
  const closeDay=counter=>closeDayBy(M,counter,'owner');
  test('[RBAC] self-approval · cash book: the person who counted or closed the day cannot lock it (same business date)',()=>{
    const r=closeDay('manager');refusedSelf(()=>M.execute(r,'manager',{type:'CB_LOCK',date:r.date}),'counter');refusedSelf(()=>M.execute(r,'owner',{type:'CB_LOCK',date:r.date}),'closer');
    assert.equal(outcome(()=>M.execute(r,'accountant',{type:'CB_LOCK',date:r.date})),'ok');});
  test('[RBAC] self-approval · cash book: after the next day is opened, the person who counted the closed day still cannot lock it (F1)',()=>{
    let r=closeDay('manager');const d=r.date;r=M.execute(r,'owner',{type:'NEXT_DAY'});refusedSelf(()=>M.execute(r,'manager',{type:'CB_LOCK',date:d}),'counter');});

  test('[RBAC] known-mismatch register is unique',()=>{const ids=KNOWN.map(k=>k.id);assert.equal(new Set(ids).size,ids.length);
    console.log('RBAC known mismatches ('+ids.length+'): '+ids.join(', '));});
}
