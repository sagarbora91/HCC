// Module 20 · Accounting automation — pure helpers, selectors and the state slice type. Exported names MUST start with 'acc' / 'ACC' (shared mock namespace).
// Vouchers are DERIVED from source records (folio lines, receipts, expenses, closing records); nothing is keyed by hand.
// Spec: 02 Planning/modules/HCC_Module_20_Accounting_Spec.md. Money = integer paise.
import {csvMinimal} from '../csv';
import type {State,Booking,Line,Receipt,Expense} from '../types';
import {drawer} from '../selectors';
import {reportPack,expenseCounts,closingOutlet,closingReopened} from './closing';
import {posSlice,type POSReceipt} from './pos';
import {lineTax,gstSplit} from '../gst';
import {mnyLedgerDefaults,mnyIsCredit} from './money-master';
import {cxSlice} from './cashout';
import {sysParkedAccounting,SYS_STUCK_TITLE,sysStuckDetail} from './syshealth';
import {expItcPaise,expUdGst,expUdRevenueKey,expUdKind} from './expenses-scc';
import {accPost,accIsControl,accControlOf,accIsParty,accPartyIdOf,accPartyName,accUdhaarPartyId,type ACCPartyCtx} from './accounting-parties';
import {grpSlice} from './groups';
import {expUdDeskReceipts,expUdDeskDates} from './udhaar-desk';
import {accOpeningLockBlocker,accOpeningPosted} from './accounting-opening'; // ACC-1 opening (contract §5)
import {accChartIndex,accLedger,accLedgerLegacyGroup,accLedgerTallyName} from './accounting-chart'; // ACC-1 integration: Owner renames / Owner-added ledgers reach postings and names

// ── Mapping (source classification → ledger) ──
export const ACC_DEFAULT_MAP:Record<string,string>={
  'line:room':'4000','line:service':'4100','line:discount':'4900','line:adjustment':'4910','folio:guest':'1100','folio:cgst':'2110','folio:sgst':'2120',
  'method:Cash':'1000','method:UPI':'1010','method:Card':'1020','method:Bank transfer':'1030',
  'receipt:payment':'1100','receipt:refund':'1100','receipt:deposit':'2000','receipt:deposit-return':'2000','receipt:company-credit':'1110',
  'expense:Supplies':'5000','expense:Laundry':'5010','expense:Maintenance':'5020','expense:Utilities':'5030','expense:Petty cash':'5040',
  'funding:Cash':'1000','funding:UPI':'1010','funding:Card':'1020','funding:Bank transfer':'1030','funding:Vendor credit':'2200',
  // ACC-1 (contract §2): an accepted short posts Dr 5900 Cash short; an over posts Cr 4950 Cash over (key closing:cash-over).
  'closing:cash-difference':'5900','closing:cash-over':'4950',
  // Wave B1 (DEC-073): drawer cash banked (Dr bank / Cr cash) and taken by the Owner (Dr drawings / Cr cash); a till transfer books nothing.
  'closing:bank-deposit':'1030','closing:owner-drawing':'3000',
  // Restaurant / room-service bills settled at the outlet (shared till, DEC-052). Bills charged to a folio stay in the folio flow.
  // ACC-1 (contract §2): outlet sales move to 4200 Food & beverage revenue; attrition / retention charges to 4300 (server line kind).
  'pos:sales':'4200','pos:cgst':'2110','pos:sgst':'2120','line:attrition':'4300',
  // DEC-130: an Udhaar payment settles the customer's credit (Dr tender · Cr Udhaar receivables); DEC-135: GST of an expense bill marked ITC allowed.
  // ACC-1 (contract §2): the ITC splits half / half like output GST — CGST half to 1310 (tax:gst-input), SGST half to 1320 (tax:gst-input-sgst);
  // 1300 stays for locked snapshots only.
  'receipt:udhaar':'1120','tax:gst-input':'1310','tax:gst-input-sgst':'1320',
};
const KEY_LABEL:Record<string,string>={line:'Folio line kind',folio:'Folio',method:'Receipt method (tender)',receipt:'Receipt kind (counter-ledger)',expense:'Expense category',funding:'Expense funding',closing:'Closing record',pos:'Outlet (POS) bill',tax:'GST input credit (ITC)'};
export const accKeyGroup=(key:string)=>KEY_LABEL[key.split(':')[0]]??'Other';
export const accKeyValue=(key:string)=>key.slice(key.indexOf(':')+1);
export const ACC_KEY_PATTERN=/^(line|folio|method|receipt|expense|funding|closing|pos|tax):.+$/;
/** ACC-1 (contract §2): the mapping key of an accepted cash difference (counted − expected) — a short (< 0) → closing:cash-difference (5900),
 *  an over (> 0) → closing:cash-over (4950). */
export const accCashDiffKey=(diff:number)=>diff>0?'closing:cash-over':'closing:cash-difference';
/** ACC-1 (contract §2): an expense bill's ITC split half / half exactly as output GST (gstSplit; a reversal's negative ITC mirrors it):
 *  [key, paise] for the CGST half (tax:gst-input → 1310) and the SGST half (tax:gst-input-sgst → 1320). */
export const accItcSplit=(itc:number):[string,number][]=>{const g=gstSplit(itc);return [['tax:gst-input',g.cgst],['tax:gst-input-sgst',g.sgst]];};

// ── State slice ──
export type ACCEntry={account:string;debit:number;credit:number};
export type ACCVoucherType='Sales'|'Receipt'|'Payment'|'Journal'|'Purchase'|'Contra'; // ACC-2: Tally's Purchase and Contra (contract §1)
export type ACCSourceKind='line'|'receipt'|'expense'|'closing'|'pos'|'opening'|'manual'|'purchase'|'vendor'|'tax'|'ota'|'payroll'; // ACC-2 (contract §1) // ACC-1: 'opening' = the hand-entered opening entry (contract §5)
export type ACCVoucher={no:string;date:string;type:ACCVoucherType;narration:string;
  source:{kind:ACCSourceKind;id:string;sub:string;booking?:string};entries:ACCEntry[];unmapped:string[]};
export type ACCLock={date:string;lockedAt:string;lockedBy:string;vouchers:ACCVoucher[];debit:number;credit:number;count:number};
export type ACCMapChange={id:string;at:string;actor:string;key:string;before:string;after:string};
export type ACCLockEvent={id:string;at:string;actor:string;date:string;action:'lock'|'unlock';reason?:string};
// ── ACC-1 contract (02 Planning/agents/ACC1-POSTING-CONTRACT.md §1, §2, §5, §8) — shared by every ACC-1 lane; the coordinator owns this block ──
/** Tally's 28 predefined groups (contract §1). side: BS = balance sheet, PL = profit & loss; nature = normal balance. Custom sub-groups: id `g-<slug>`. */
export type ACCGroupDef={id:string;name:string;parent?:string;nature:'Dr'|'Cr';side:'BS'|'PL';custom?:boolean};
export const ACC_GROUPS:ACCGroupDef[]=[
  {id:'capital',name:'Capital Account',nature:'Cr',side:'BS'},{id:'reserves',name:'Reserves & Surplus',parent:'capital',nature:'Cr',side:'BS'},
  {id:'current-assets',name:'Current Assets',nature:'Dr',side:'BS'},{id:'bank',name:'Bank Accounts',parent:'current-assets',nature:'Dr',side:'BS'},
  {id:'cash',name:'Cash-in-Hand',parent:'current-assets',nature:'Dr',side:'BS'},{id:'deposits-asset',name:'Deposits (Asset)',parent:'current-assets',nature:'Dr',side:'BS'},
  {id:'loans-advances',name:'Loans & Advances (Asset)',parent:'current-assets',nature:'Dr',side:'BS'},{id:'stock',name:'Stock-in-Hand',parent:'current-assets',nature:'Dr',side:'BS'},
  {id:'debtors',name:'Sundry Debtors',parent:'current-assets',nature:'Dr',side:'BS'},
  {id:'current-liabilities',name:'Current Liabilities',nature:'Cr',side:'BS'},{id:'duties-taxes',name:'Duties & Taxes',parent:'current-liabilities',nature:'Cr',side:'BS'},
  {id:'provisions',name:'Provisions',parent:'current-liabilities',nature:'Cr',side:'BS'},{id:'creditors',name:'Sundry Creditors',parent:'current-liabilities',nature:'Cr',side:'BS'},
  {id:'fixed-assets',name:'Fixed Assets',nature:'Dr',side:'BS'},{id:'investments',name:'Investments',nature:'Dr',side:'BS'},
  {id:'loans',name:'Loans (Liability)',nature:'Cr',side:'BS'},{id:'bank-od',name:'Bank OD A/c',parent:'loans',nature:'Cr',side:'BS'},
  {id:'secured-loans',name:'Secured Loans',parent:'loans',nature:'Cr',side:'BS'},{id:'unsecured-loans',name:'Unsecured Loans',parent:'loans',nature:'Cr',side:'BS'},
  {id:'suspense',name:'Suspense A/c',nature:'Dr',side:'BS'},{id:'misc-expenses-asset',name:'Misc. Expenses (Asset)',nature:'Dr',side:'BS'},{id:'branches',name:'Branch / Divisions',nature:'Dr',side:'BS'},
  {id:'sales',name:'Sales Accounts',nature:'Cr',side:'PL'},{id:'purchases',name:'Purchase Accounts',nature:'Dr',side:'PL'},
  {id:'direct-income',name:'Direct Incomes',nature:'Cr',side:'PL'},{id:'direct-expenses',name:'Direct Expenses',nature:'Dr',side:'PL'},
  {id:'indirect-income',name:'Indirect Incomes',nature:'Cr',side:'PL'},{id:'indirect-expenses',name:'Indirect Expenses',nature:'Dr',side:'PL'},
];
/** Legacy group of a Tally group (contract §1): BS+Dr = Asset, BS+Cr = Liability, PL+Cr = Income, PL+Dr = Expense, suspense = Suspense. */
export const accLegacyGroup=(g:ACCGroupDef):ACCGroup=>g.id==='suspense'?'Suspense':g.side==='PL'?(g.nature==='Cr'?'Income':'Expense'):(g.nature==='Dr'?'Asset':'Liability');
/** A ledger of the chart as a master (contract §2). Built-ins are `system`; `control` ledgers hold party sub-ledgers `<code>/<partyId>` (contract §3). */
export type ACCLedger={code:string;name:string;group:string;system:boolean;control?:boolean;gst?:'none'|'input'|'output';tallyName?:string;active:boolean;note?:string};
export type ACCChartChange={id:string;at:string;actor:string;kind:'ledger'|'group';code:string;field:string;before:string;after:string};
/** Opening balances entered by hand (contract §5; DEC-139 / DEC-148). account = ledger code or party ledger id; paise, one side only. */
export type ACCOpeningLine={account:string;debit:number;credit:number};
export type ACCOpening={asOf:string;lines:ACCOpeningLine[];status:'draft'|'approved';by:string;at:string;approvedBy?:string;approvedAt?:string;note?:string};
// ── end ACC-1 contract block ──
// ── ACC-2 contract (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2, §3.4, §6, §10) — shared by every ACC-2 lane; the coordinator owns this block ──
export type ACCManualTemplate='journal'|'contra'|'capital-introduced'|'owner-drawing-bank'|'loan-received'|'loan-emi'|'bank-charges'|'interest-income'|'bad-debt'|'statutory-remittance'|'gst-payment'|'tds-remittance';
/** One line of a manual voucher (contract §2): a ledger code or party ledger id; paise; one side only. */
export type ACCManualLine={account:string;debit:number;credit:number;note?:string};
/** A manual voucher (contract §2): the Accountant's are pending until the Owner decides; the Owner's own are approved at once. */
export type ACCManualVoucher={id:string;date:string;type:'Journal'|'Contra'|'Payment'|'Receipt';narration:string;reference?:string;lines:ACCManualLine[];template?:ACCManualTemplate;
  status:'pending'|'approved'|'rejected'|'void';by:string;at:string;decidedBy?:string;decidedAt?:string;reason?:string;voidOf?:string;voidedBy?:string;bookedOn?:string};
/** Fixed-asset register row (contract §3.4): created when a capital purchase posts; no depreciation journal in HCC (DEC-140 / DEC-138). */
export type ACCAsset={id:string;no:string;ledger:string;name:string;cost:number;date:string;vendor:string;expense:string;itc:number;putToUse?:string;disposed?:{date:string;amount:number;voucher:string}};
/** Month lock (contract §6): every day of the month locked, the GST set-off posted, no pending manual voucher; mirrors the SCC expense month lock. */
export type ACCMonthLock={lockedAt:string;lockedBy:string;setoff?:string;unlocks?:{at:string;by:string;reason:string}[]};
// ── end ACC-2 contract block ──
// ── Chart of accounts (ACC-1: rulebook §3.2, posting contract §2; the chart as a master lives in accounting-chart.ts) ──
export type ACCGroup='Asset'|'Liability'|'Income'|'Expense'|'Suspense';
/** A built-in ledger as the existing selectors read it. `group` = the legacy group, derived from `groupId` (contract §1), so every existing
 *  selector keeps working; `groupId` = the Tally group of ACC_GROUPS; `system` = built-in; `control` = holds party sub-ledgers
 *  `<code>/<partyId>` (contract §3). Owner overrides and Owner-added ledgers: accLedger(s, code) / accChart(s) in accounting-chart.ts. */
export type ACCAccount={code:string;name:string;group:ACCGroup;groupId:string;system:boolean;control?:boolean;gst?:'none'|'input'|'output';note?:string};
export const ACC_SUSPENSE='9999';
const BL=(code:string,name:string,group:string,x:{control?:true;gst?:'input'|'output';note?:string}={}):ACCLedger=>
  ({code,name,group,system:true,...(x.control?{control:true}:{}),gst:x.gst??'none',active:true,...(x.note?{note:x.note}:{})});
/** The built-in (system) ledgers of contract §2 in code order. Existing codes keep their meaning; names follow the rulebook §3.2 table. */
export const ACC_LEDGERS:ACCLedger[]=[
  BL('1000','Cash in hand','cash',{control:true,note:'Front-desk drawer; one party per till (1000/<till code>)'}),
  BL('1005','Petty cash float','cash',{note:'SCC petty-cash float (expenses wave)'}),
  BL('1010','UPI clearing','bank',{note:'Settles to bank (simulated)'}),
  BL('1020','Card clearing','bank',{note:'Settles to bank net of MDR (simulated)'}),
  BL('1030','Bank accounts','bank',{control:true,note:'One party per row of the Bank accounts master (kind bank)'}),
  BL('1100','Guest ledger (in-house folios)','debtors',{note:'Open folio balances; zero after check-out'}),
  BL('1110','Company receivables (city ledger)','debtors',{control:true,note:'Authorised company credit; one party per company account'}),
  BL('1115','OTA receivables','debtors',{control:true,note:'One party per OTA channel (ACC-2 posts it)'}),
  BL('1120','Udhaar receivables (customer credit)','debtors',{control:true,note:'DEC-130 / DEC-144: one party per Udhaar customer'}),
  BL('1200','Staff advances','loans-advances',{control:true,note:'Salary advances recovered through payroll; one party per staff member'}),
  BL('1210','Recoverable from leavers (F&F)','loans-advances'),
  BL('1300','GST input credit (legacy, locked snapshots only)','duties-taxes',{gst:'input',note:'DEC-135 one-ledger ITC before ACC-1; new ITC goes to 1310 / 1320'}),
  BL('1310','Input CGST','duties-taxes',{gst:'input',note:'ITC marked allowed, CGST half'}),
  BL('1320','Input SGST','duties-taxes',{gst:'input',note:'ITC marked allowed, SGST half'}),
  BL('1330','Input IGST','duties-taxes',{gst:'input',note:'Bills from vendors outside Maharashtra (ACC-2)'}),
  BL('1340','GST electronic cash ledger','duties-taxes',{note:'Challan deposits with the government'}),
  BL('1350','GST paid on advances (adjustable)','duties-taxes'),
  BL('1360','TDS receivable','loans-advances',{note:'TDS deducted by companies / OTAs'}),
  BL('1370','Prepaid expenses','loans-advances'),
  BL('1400','Opening-balance difference','suspense',{note:'DEC-139: parks an unbalanced opening entry; must be zero before the first period lock'}),
  BL('1500','Buildings','fixed-assets'),
  BL('1510','Furniture, fittings & electrical fittings','fixed-assets'),
  BL('1520','Plant & machinery','fixed-assets'),
  BL('1530','Computers, software & POS hardware','fixed-assets'),
  BL('1540','Vehicles','fixed-assets'),
  BL('2000','Advance deposits (guest and group)','current-liabilities',{note:'Refundable deposits held; never income'}),
  BL('2100','Synthetic tax payable (legacy)','duties-taxes',{gst:'output',note:'Before DEC-069 only — kept for locked snapshots'}),
  BL('2110','Output CGST','duties-taxes',{gst:'output',note:'GST master, intra-state half (illustrative — CA to confirm)'}),
  BL('2120','Output SGST','duties-taxes',{gst:'output',note:'GST master, intra-state half (illustrative — CA to confirm)'}),
  BL('2130','Output IGST','duties-taxes',{gst:'output',note:'Normally zero (DEC-136 refuses inter-state supply)'}),
  BL('2140','GST payable under reverse charge','duties-taxes',{gst:'output'}),
  BL('2150','TDS payable','duties-taxes'),
  BL('2200','Vendor payables','creditors',{control:true,note:'Vendor-credit expenses; one party per expense vendor'}),
  BL('2300','Salaries payable (net)','current-liabilities',{note:'Net pay due to staff'}),
  BL('2310','PF payable','duties-taxes',{note:'Employee + employer PF (illustrative — CA to confirm)'}),
  BL('2320','ESIC payable','duties-taxes',{note:'Employee + employer ESIC (illustrative — CA to confirm)'}),
  BL('2330','Professional tax payable','duties-taxes'),
  BL('2340','Other payroll deductions','current-liabilities'),
  BL('2400','Bank loans (secured)','secured-loans',{control:true,note:'One party per loan (Owner-added loan rows, ACC-2)'}),
  BL('2410','Unsecured loans','unsecured-loans'),
  BL('3000',"Owner's drawings",'capital',{note:'Capital account (contra): cash the Owner takes from the drawer (DEC-073) — CA to confirm'}),
  BL('3100',"Owner's capital",'capital',{note:'Opening capital + profit − drawings'}),
  BL('4000','Room revenue','sales'),
  BL('4100','Other services revenue','sales',{note:'Laundry, extras and other services'}),
  BL('4200','Food & beverage revenue','sales',{note:'Outlet (POS) bills'}),
  BL('4300','Cancellation, no-show & attrition charges','sales'),
  BL('4400','Banquet & events revenue','sales'),
  BL('4900','Discounts allowed','sales',{note:'Contra revenue'}),
  BL('4910','Refunds, reversals & credit notes','sales',{note:'Contra revenue (folio adjustments)'}),
  BL('4950','Cash over','indirect-income',{note:'Accepted drawer overs (shorts go to 5900)'}),
  BL('4960','Interest & other income','indirect-income'),
  BL('5000','Housekeeping & guest supplies','direct-expenses'),
  BL('5010','Linen & laundry','direct-expenses'),
  BL('5015','Kitchen provisions & beverages','direct-expenses'),
  BL('5020','Repairs & maintenance','indirect-expenses'),
  BL('5030','Electricity, water & fuel','direct-expenses'),
  BL('5040','Petty cash & sundry','indirect-expenses'),
  BL('5050','General & administrative expenses','indirect-expenses'),
  BL('5060','Rent & licence fees','indirect-expenses'),
  BL('5070','OTA commission','indirect-expenses'),
  BL('5080','Bank & card charges','indirect-expenses'),
  BL('5090','Interest on loans','indirect-expenses'),
  BL('5100','Salaries & wages','indirect-expenses'),
  BL('5110','Employer PF & ESIC','indirect-expenses'),
  BL('5120','Staff welfare','indirect-expenses'),
  BL('5200','Depreciation','indirect-expenses'),
  BL('5300','Bad debts written off','indirect-expenses'),
  BL('5400','GST / TDS interest & late fees','indirect-expenses'),
  BL('5900','Cash short','indirect-expenses',{note:'Accepted drawer shorts (overs go to 4950)'}),
  BL(ACC_SUSPENSE,'Suspense (unmapped)','suspense',{note:'Never a mapping target'}),
];
/** A built-in group by id (ACC_GROUPS). Custom sub-groups: accGroupOf(s, id) in accounting-chart.ts. */
export const accGroupDef=(id:string)=>ACC_GROUPS.find(g=>g.id===id);
/** The built-in chart as ACCAccount rows (legacy group derived from the Tally group, contract §1). */
export const ACC_CHART:ACCAccount[]=ACC_LEDGERS.map(l=>({code:l.code,name:l.name,group:accLegacyGroup(accGroupDef(l.group)!),groupId:l.group,system:true,
  ...(l.control?{control:true}:{}),gst:l.gst,...(l.note?{note:l.note}:{})}));
/** A built-in ledger by code. A party ledger id `<control>/<party>` resolves to its control ledger (contract §3; the party's own name comes
 *  from accounting-parties.ts). Owner overrides and Owner-added ledgers: accLedger(s, code) in accounting-chart.ts. */
export const accAccount=(code:string):ACCAccount|undefined=>{const a=ACC_CHART.find(a=>a.code===code);if(a)return a;
  const i=code.indexOf('/');if(i<1)return undefined;const c=ACC_CHART.find(a=>a.code===code.slice(0,i));return c?.control?c:undefined;};
export const accLedgerName=(code:string)=>accAccount(code)?.name??code;
/** State slice for this module, stored at s.accountingX (optional; absent in old saves → treat as empty).
 *  Production build only (W4-7, clients/sync/accounting.ts): the server's own evaluation of each business date in a window
 *  (GET /accounting/days). When present, the selectors below read it instead of deriving vouchers on the device — the server is
 *  the truth. `opening` = balances before `from` (cumulative trial balance) and, dated on its own "as of" date, the imported opening
 *  trial balance when that date falls inside the window (N5). `accounts` = name and group of ledger codes outside ACC_CHART (imported
 *  ledgers without an HCC account, e.g. Owner's capital · Equity). Never set in the prototype. */
export type ACCServerDay={status:ACCDayStatus;vouchers:ACCVoucher[];exceptions:ACCException[];checks:ACCCheck[]};
export type ACCServer={from:string;to:string;days:Record<string,ACCServerDay>;opening:ACCVoucher[];mapRows:ACCMapRow[];accounts?:Record<string,{name:string;group:ACCTBGroup}>};
/** DEC-077: one recorded Tally export (the Download XML button): the dates it carried, which were re-exports and which provisional.
 *  A03b-05: `ids` = its vouchers carried a REMOTEID (absent on exports recorded before that: Tally may hold those days without ids). */
export type ACCTallyExport={id:string;at:string;by:string;from:string;to:string;dates:string[];reexport:string[];provisional:string[];count:number;ids?:boolean};
/** DEC-077: a date whose vouchers changed after they were exported (its day was reopened, `reopen` set); A03b-05: or a date exported
 *  while accounting events were pending (no `reopen`). The next export carries it again. */
export type ACCReexport={at:string;reason:string;reopen?:string};
export type ACCState={mapping?:Record<string,string>;locks?:Record<string,ACCLock>;mapHistory?:ACCMapChange[];lockHistory?:ACCLockEvent[];server?:ACCServer;
  /** ACC-1 (SCHEMA 11): the chart as a master (Owner overrides of built-ins, Owner-added ledgers, custom sub-groups, history) and the hand-entered opening balances. */
  chart?:{ledgers:ACCLedger[];groups:ACCGroupDef[];history:ACCChartChange[]};opening?:ACCOpening;
  /** ACC-2 (SCHEMA 12): manual vouchers, the fixed-asset register, month locks and the posted GST set-off per month (contract §2, §3.4, §6). */
  manual?:ACCManualVoucher[];assets?:ACCAsset[];monthLocks?:Record<string,ACCMonthLock>;gstSetoffs?:Record<string,string>;
  tallyExports?:ACCTallyExport[];reexport?:Record<string,ACCReexport>};
export const accEmpty=():ACCState=>({});
export const accSlice=(s:State):ACCState=>s.accountingX??accEmpty();
/** Effective mapping = defaults, then the ledgers of the money masters (MA-3: payment methods, expense categories; seed = the same
 *  defaults), overlaid with the slice's overrides (Accounting › Mapping wins). */
export function accMapping(s:State):Record<string,string>{return {...ACC_DEFAULT_MAP,...mnyLedgerDefaults(s),...(accSlice(s).mapping??{})};}
export const accLockOf=(s:State,date:string)=>accSlice(s).locks?.[date];
/** Roles that may lock a closed period (with the coarse 'reports' permission). */
export const ACC_LOCK_ROLES=['Owner','Manager','Accountant'];

// ── Voucher derivation ──
/** Signed credit amount → entry (positive = credit, negative = debit). */
const cr=(account:string,amount:number):ACCEntry=>amount>=0?{account,debit:0,credit:amount}:{account,debit:-amount,credit:0};
const dr=(account:string,amount:number):ACCEntry=>cr(account,-amount);
const RECEIPT_IN=['payment','deposit'];
const ymd=(d:string)=>d.split('-').join('');
const PREFIX:Record<ACCVoucherType,string>={Sales:'SAL',Receipt:'RCT',Payment:'PMT',Journal:'JRN',Purchase:'PUR',Contra:'CTR'};

/** All vouchers for a business date, derived live from the source records with the current mapping (pure). */
export function accLiveVouchers(s:State,date:string,mapping=accMapping(s)):ACCVoucher[]{
  const out:Omit<ACCVoucher,'no'>[]=[];
  // ACC-1 (chart lane, integration): a mapping target must be an ACTIVE ledger of the effective chart (Owner-added ledgers count, retired ones do not).
  const known=accChartIndex(s);
  const map=(key:string,miss:string[])=>{const a=mapping[key];if(a&&known.get(a)?.active&&a!==ACC_SUSPENSE)return a;miss.push(key);return ACC_SUSPENSE;};
  // ACC-1 (contract §3): a control ledger (1000, 1030, 1110, 1115, 1120, 1200, 2200) posts to `<code>/<party>` when the source names the party.
  const pt=(code:string,ctx:ACCPartyCtx)=>accPost(s,code,ctx);
  const push=(v:Omit<ACCVoucher,'no'|'entries'|'unmapped'>,miss:string[],entries:ACCEntry[])=>{
    const e=entries.filter(x=>x.debit||x.credit);if(e.length)out.push({...v,entries:e,unmapped:[...new Set(miss)]});};
  for(const b of s.bookings){
    for(const l of b.lines.filter(l=>l.date===date&&l.amount!==0)){
      const miss:string[]=[],t=lineTax(l),g=gstSplit(t);
      push({date,type:'Sales',narration:`${l.label} · ${b.guest} · room ${b.room}`,source:{kind:'line',id:`${b.id}/${l.id}`,sub:l.kind,booking:b.id}},miss,
        [dr(map('folio:guest',miss),l.amount+t),cr(map('line:'+l.kind,miss),l.amount),cr(map('folio:cgst',miss),g.cgst),cr(map('folio:sgst',miss),g.sgst)]);
    }
  }
  for(const b of s.bookings){
    for(const r of b.receipts.filter(r=>r.date===date&&r.amount!==0)){
      const miss:string[]=[],src={kind:'receipt' as const,id:`${b.id}/${r.id}`,sub:r.kind,booking:b.id},ctx:ACCPartyCtx={method:r.method,company:b.company?.account};
      const counter=pt(map('receipt:'+r.kind,miss),ctx);
      if(r.kind==='company-credit'){push({date,type:'Journal',narration:`Company credit · ${r.reference} · ${b.guest}`,source:src},miss,[dr(counter,r.amount),cr(map('folio:guest',miss),r.amount)]);continue;}
      const tender=pt(map('method:'+r.method,miss),ctx),inflow=RECEIPT_IN.includes(r.kind);
      const label=r.kind==='payment'?'Payment received':r.kind==='deposit'?'Deposit received':r.kind==='refund'?'Payment refunded':r.kind==='deposit-return'?'Deposit returned':r.kind;
      push({date,type:inflow?'Receipt':'Payment',narration:`${label} · ${r.method} · ${r.reference} · ${b.guest}`,source:src},miss,
        inflow?[dr(tender,r.amount),cr(counter,r.amount)]:[dr(counter,r.amount),cr(tender,r.amount)]);
    }
  }
  // One Sales voucher per outlet receipt: Dr tender, Cr outlet revenue + CGST + SGST. A void receipt (negative) reverses its sale.
  for(const r of posSlice(s).receipts.filter(r=>r.date===date&&r.amount!==0)){
    const miss:string[]=[],g=gstSplit(r.tax);
    push({date,type:'Sales',narration:`${r.kind==='void'?'Outlet bill void':'Outlet bill'} · ${r.outlet} · ${r.method} · ${r.reference} · ${r.order}`,source:{kind:'pos',id:r.id,sub:r.kind}},miss,
      [dr(pt(map('method:'+r.method,miss),{method:r.method}),r.amount),cr(map('pos:sales',miss),r.net),cr(map('pos:cgst',miss),g.cgst),cr(map('pos:sgst',miss),g.sgst)]);
  }
  for(const e of s.expenses.filter(e=>e.date===date&&expenseCounts(e)&&e.amount!==0)){
    // DEC-135: ITC allowed → the bill's GST goes to the GST input-credit ledger and the category takes the rest; disallowed → the whole bill
    // to the category (as before). A reversal record carries −amount and −GST, so it mirrors the split.
    const miss:string[]=[],head=map('expense:'+e.category,miss),itc=expItcPaise(e),fund=pt(map('funding:'+e.method,miss),{method:e.method,vendor:e.vendor}); // ACC-1: vendor credit → 2200/<vendor>
    push({date,type:mnyIsCredit(s,e.method)?'Journal':'Payment',narration:`${e.reversalOf?'Reversal of '+e.reversalOf+' · ':''}${e.vendor||e.description||'—'} · ${e.category} · ${e.method}`, // DEC-120: the vendor is optional
      source:{kind:'expense',id:e.id,sub:e.reversalOf?'reversal':'expense'}},miss,[dr(head,e.amount-itc),...(itc?accItcSplit(itc).map(([k,p])=>dr(map(k,miss),p)):[]),cr(fund,e.amount)]);
  }
  // ACC-1 (contract §4, rulebook A17, DEC-144): raising an Udhaar credit is a SALE booked the same day — Dr 1120/<customer> (gross) · Cr F&B,
  // room or other revenue (the credit's kind) · Cr CGST · Cr SGST (the GST inside the gross, expUdGst). Rows raised before ACC-1: other, no GST.
  // A credit later written off keeps this voucher (the write-off is the bad-debt journal A18, a later wave).
  for(const r of s.expensesX?.udhaar??[]){
    if(r.date!==date||!r.amount)continue;const miss:string[]=[],g=expUdGst(r),ctx={udhaar:accUdhaarPartyId(r)};
    push({date,type:'Sales',narration:`Udhaar credit · ${r.name} · ${expUdKind(r)}${r.gstBps?' · GST '+(r.gstBps/100)+' %':''} · credit ${r.id}`,source:{kind:'receipt',id:'udhaar:'+r.id,sub:'udhaar-credit'}},miss,
      [dr(pt(map('receipt:udhaar',miss),ctx),r.amount),cr(map(expUdRevenueKey(r),miss),g.taxable),cr(map('folio:cgst',miss),g.cgst),cr(map('folio:sgst',miss),g.sgst)]);
  }
  // DEC-130: an Udhaar payment taken as a desk receipt settles the customer's credit — Receipt, Dr the tender · Cr Udhaar receivables (never income).
  for(const {r,p} of expUdDeskReceipts(s,date)){
    if(!p.amount)continue;const miss:string[]=[],ctx={method:p.mode,udhaar:accUdhaarPartyId(r)};
    push({date,type:'Receipt',narration:`Udhaar received · ${r.name} · ${p.mode}${p.reference?' · '+p.reference:''} · payment ${p.id}`,source:{kind:'receipt',id:'udhaar-payment:'+p.id,sub:'udhaar-payment'}},miss,
      [dr(pt(map('method:'+p.mode,miss),ctx),p.amount),cr(pt(map('receipt:udhaar',miss),ctx),p.amount)]);
  }
  // ACC-1 (DEC-076, server SourceRules.CompanyPayment): money received from a company against its company-credit receivable — Receipt,
  // Dr the tender · Cr 1110/<company>. Sub company-payment, so the daily company-credit and collections checks are unchanged.
  for(const p of grpSlice(s).payments??[]){
    if(p.date!==date||!p.amount)continue;const miss:string[]=[],ctx={method:p.method,company:p.company};
    const name=grpSlice(s).companies.find(c=>c.id===p.company)?.name??p.company;
    push({date,type:'Receipt',narration:`Company payment received · ${name} · ${p.method} · ${p.reference} · payment ${p.id}`,source:{kind:'receipt',id:'company-payment:'+p.id,sub:'company-payment'}},miss,
      [dr(pt(map('method:'+p.method,miss),ctx),p.amount),cr(pt(map('receipt:company-credit',miss),ctx),p.amount)]);
  }
  // ── Wave B1 (DEC-073): cash leaving the drawer — same vouchers as the server projector (keys banking:{id}, banking:{id}/rejected, drawing:{id}) ──
  for(const r of cxSlice(s).records){
    const cash=(m:string[])=>pt(map('method:Cash',m),{method:'Cash',till:r.fromTill});
    if(r.kind==='banking'){
      if(r.date===date){const miss:string[]=[];push({date,type:'Journal',narration:`Cash deposited in bank · ${r.bank??''} · slip ${r.slip??''} · deposit ${r.id}`,source:{kind:'closing',id:'banking:'+r.id,sub:'bank-deposit'}},miss,
        [dr(pt(map('closing:bank-deposit',miss),{bank:r.bank??''}),r.amount),cr(cash(miss),r.amount)]);}
      if(r.status==='Rejected'&&r.decidedDate===date){const miss:string[]=[];push({date,type:'Journal',narration:`Bank deposit rejected · cash back in the drawer · ${r.bank??''} · slip ${r.slip??''} · deposit ${r.id}`,
        source:{kind:'closing',id:'banking:'+r.id+'/rejected',sub:'bank-deposit-rejected'}},miss,[dr(cash(miss),r.amount),cr(pt(map('closing:bank-deposit',miss),{bank:r.bank??''}),r.amount)]);}
    }else if(r.kind==='drawing'&&r.date===date){const miss:string[]=[];push({date,type:'Payment',narration:`Owner drawing · ${r.reason??''} · drawing ${r.id}`,source:{kind:'closing',id:'drawing:'+r.id,sub:'owner-drawing'}},miss,
      [dr(map('closing:owner-drawing',miss),r.amount),cr(cash(miss),r.amount)]);}
  }
  // ── end Wave B1 ──
  const diff=accCashDifference(s,date);
  if(diff){const miss:string[]=[],cash=pt(map('method:Cash',miss),{method:'Cash'}),acct=map(accCashDiffKey(diff),miss); // ACC-1: an over → closing:cash-over (4950), a short → 5900
    push({date,type:'Journal',narration:`Cash ${diff>0?'over':'short'} accepted at shift close · ${date}`,source:{kind:'closing',id:`CLOSE/${date}/cash-difference`,sub:'cash-difference'}},miss,
      diff>0?[dr(cash,diff),cr(acct,diff)]:[dr(acct,-diff),cr(cash,-diff)]);}
  const n:Record<string,number>={};
  return (['Sales','Receipt','Payment','Journal'] as ACCVoucherType[]).flatMap(t=>out.filter(v=>v.type===t)).map(v=>{
    n[v.type]=(n[v.type]??0)+1;return {...v,no:`${PREFIX[v.type]}/${ymd(date)}/${String(n[v.type]).padStart(3,'0')}`};});
}
/** Accepted cash difference for a date (counted − expected) from its closing record, or the closed shift of the open day. */
export function accCashDifference(s:State,date:string):number{
  const c=s.closings?.[date];if(c)return c.cash.difference;
  if(date===s.date&&s.shift?.date===date&&s.shift.status==='Closed')return s.shift.counted-s.shift.expected;
  return 0;
}
/** Vouchers shown for a date: the locked snapshot when locked, else the live derivation. */
export function accVouchers(s:State,date:string):ACCVoucher[]{const sv=accSlice(s).server;if(sv)return sv.days[date]?.vouchers??[];return accLockOf(s,date)?.vouchers??accLiveVouchers(s,date);}
export const accVoucherTotals=(v:ACCVoucher)=>({debit:v.entries.reduce((a,e)=>a+e.debit,0),credit:v.entries.reduce((a,e)=>a+e.credit,0)});
export const accBalanced=(v:ACCVoucher)=>{const t=accVoucherTotals(v);return t.debit===t.credit;};
export function accTotals(vs:ACCVoucher[]){return vs.reduce((a,v)=>{const t=accVoucherTotals(v);return {debit:a.debit+t.debit,credit:a.credit+t.credit};},{debit:0,credit:0});}

// ── Trial balance ──
/** Trial-balance group: the chart's groups, plus Equity for imported ledgers (production only). */
export type ACCTBGroup=ACCGroup|'Equity';
/** `control` (ACC-1, detail trial balance only): set on a party row `<control>/<party>` — the control ledger it sums into. */
export type ACCTBRow={account:string;name:string;group:ACCTBGroup;debit:number;credit:number;balance:number;control?:string};
/** Trial balance over a set of vouchers. balance = debit − credit (positive = debit balance). Chart order. `accounts` names codes
 *  outside the chart (production: imported ledgers, ACCServer.accounts). ACC-1 (contract §3): `detail` false (default) = one row per
 *  control ledger, its parties summed into it; true = a row per party (`control` set) right after its control's own row (the control row
 *  appears only when something posted to the control itself). Both tie. `s` names the parties (else the party id is the name). */
export function accTrialBalance(vs:ACCVoucher[],accounts?:ACCServer['accounts'],detail=false,s?:State):ACCTBRow[]{
  const m=new Map<string,{debit:number;credit:number}>();
  for(const v of vs)for(const e of v.entries){const k=detail?e.account:accControlOf(e.account);const x=m.get(k)??{debit:0,credit:0};x.debit+=e.debit;x.credit+=e.credit;m.set(k,x);}
  const order=(c:string)=>{const i=ACC_CHART.findIndex(a=>a.code===c);return i<0?999:i;};
  // ACC-1 integration: with `s`, the effective chart names / groups (Owner renames and Owner-added ledgers).
  const own=(a:string)=>s?accLedger(s,a):undefined;
  const name=(a:string)=>own(a)?.name??accAccount(a)?.name??accounts?.[a]?.name??a,group=(a:string):ACCTBGroup=>(s&&own(a)?accLedgerLegacyGroup(s,a):undefined)??accAccount(a)?.group??accounts?.[a]?.group??'Suspense';
  const key=(a:string)=>[order(accControlOf(a)),accControlOf(a),accIsParty(a)?1:0,a] as const;
  const cmp=(a:string,b:string)=>{const x=key(a),y=key(b);return x[0]-y[0]||(x[1]<y[1]?-1:x[1]>y[1]?1:0)||x[2]-y[2]||(x[3]<y[3]?-1:x[3]>y[3]?1:0);};
  return [...m.entries()].sort((a,b)=>cmp(a[0],b[0])).map(([account,x])=>{const c=accControlOf(account),party=accIsParty(account);
    return {account,name:party?(s&&accPartyName(s,account))||accPartyIdOf(account):name(account),group:group(c),debit:x.debit,credit:x.credit,balance:x.debit-x.credit,...(party?{control:c}:{})};});
}
/** ACC-1: display name of a voucher entry's ledger — a party ledger shows "<control name> · <party name>" (party id when the master row is gone). */
export function accEntryName(s:State|undefined,account:string):string{
  const nm=(code:string)=>(s&&accLedger(s,code)?.name)||accLedgerName(code); // ACC-1 integration: Owner renames / Owner-added ledgers
  if(!accIsParty(account))return nm(account);
  return `${nm(accControlOf(account))} · ${(s&&accPartyName(s,account))||accPartyIdOf(account)}`;
}
/** ACC-1 (contract §3, retire guard): closing balance (debit − credit, paise) of every ledger id up to and including `date` (default the
 *  business date) — party ids and plain codes as posted. */
export function accLedgerBalanceMap(s:State,date=s.date):Map<string,number>{
  const m=new Map<string,number>();for(const v of accVouchersUpTo(s,date))for(const e of v.entries)m.set(e.account,(m.get(e.account)??0)+e.debit-e.credit);return m;
}
/** Balance of one party ledger id (0 when nothing posted to it). */
export const accPartyBalance=(s:State,id:string,date=s.date)=>accLedgerBalanceMap(s,date).get(id)??0;
/** Why a master row whose party ledger has a balance cannot be retired ('' = it can; contract §3, MASTER_IN_USE). */
export function accPartyRetireBlocker(s:State,control:string,partyId:string):string{
  const id=`${control}/${partyId}`,b=accPartyBalance(s,id);if(!b)return '';
  return `${accEntryName(s,id)} has a ledger balance of ₹${(Math.abs(b)/100).toFixed(2)} ${b>0?'Dr':'Cr'} — settle it to zero before retiring it (rulebook §3.3.1).`;
}
/** Dates with any accounting activity, closed dates, locks and the business date — newest first. */
export function accDates(s:State):string[]{
  const sv=accSlice(s).server;if(sv)return [...new Set([s.date,...Object.keys(sv.days)])].filter(x=>/^\d{4}-\d{2}-\d{2}$/.test(x)&&x<=s.date).sort().reverse();
  const d=new Set<string>([s.date,...s.closedDates,...Object.keys(accSlice(s).locks??{}),...Object.keys(s.closings??{})]);
  s.bookings.forEach(b=>{b.lines.forEach(l=>d.add(l.date));b.receipts.forEach(r=>d.add(r.date));});
  posSlice(s).receipts.forEach(r=>d.add(r.date));
  s.expenses.filter(expenseCounts).forEach(e=>d.add(e.date));
  expUdDeskDates(s).forEach(x=>d.add(x)); // DEC-130
  (s.expensesX?.udhaar??[]).forEach(r=>d.add(r.date)); // ACC-1: Udhaar credit raised (A17)
  (grpSlice(s).payments??[]).forEach(p=>d.add(p.date)); // ACC-1: company payments (DEC-076)
  return [...d].filter(x=>/^\d{4}-\d{2}-\d{2}$/.test(x)&&x<=s.date).sort().reverse();
}
/** Vouchers for every date up to and including `date` (cumulative trial balance). */
export function accVouchersUpTo(s:State,date:string):ACCVoucher[]{const sv=accSlice(s).server;return [...(sv?sv.opening.filter(v=>v.date<date||(v.source.sub==='opening-import'&&v.date<=date)):[]),...accOpeningPosted(s).filter(v=>v.date<=date),...accDates(s).filter(d=>d<=date).sort().flatMap(d=>accVouchers(s,d))];}

// ── Status & exceptions ──
export type ACCDayStatus='Open'|'Closed'|'Locked';
export function accDayStatus(s:State,date:string):ACCDayStatus{const sv=accSlice(s).server;if(sv)return sv.days[date]?.status??(date<s.date?'Closed':'Open');
  // DEC-077: the day the Owner reopened counts as Open (live vouchers, not lockable) until it is closed again.
  return accLockOf(s,date)?'Locked':closingReopened(s)?.date===date?'Open':s.closedDates.includes(date)?'Closed':'Open';}
/** kind 'stuck' / 'pending' come from the server (parked / unprocessed outbox events); the demo raises 'stuck' for a parked accounting event (Wave C3). */
export type ACCException={id:string;severity:'error'|'warning'|'info';kind:'unbalanced'|'unmapped'|'drift'|'stuck'|'pending'|'open-day'|'no-party';title:string;detail:string;voucher?:string;source?:string;key?:string};
const fingerprint=(vs:ACCVoucher[])=>vs.map(v=>v.source.id+'='+accVoucherTotals(v).debit).sort().join('|');
export function accExceptions(s:State,date:string):ACCException[]{
  const sv=accSlice(s).server;if(sv)return sv.days[date]?.exceptions??[];
  const vs=accVouchers(s,date),out:ACCException[]=[];
  for(const v of vs){const t=accVoucherTotals(v);
    if(t.debit!==t.credit)out.push({id:'unbal:'+v.no,severity:'error',kind:'unbalanced',title:`Voucher ${v.no} is unbalanced`,detail:`Debits ₹${(t.debit/100).toFixed(2)} ≠ credits ₹${(t.credit/100).toFixed(2)}`,voucher:v.no,source:v.source.id});
    for(const k of v.unmapped)out.push({id:'unmap:'+v.no+':'+k,severity:'error',kind:'unmapped',title:`No ledger mapped for “${accKeyValue(k)}”`,detail:`${accKeyGroup(k)} → posted to Suspense in ${v.no}. Map it in Mapping, then lock.`,voucher:v.no,source:v.source.id,key:k});}
  // ACC-1 (contract §3): an entry on a control ledger itself — the source did not name the party (old record, method without a settles-to
  // account). A warning: never blocks the lock. Not raised on a locked snapshot (frozen as locked).
  if(!accLockOf(s,date))for(const v of vs)for(const c of [...new Set(v.entries.map(e=>e.account).filter(accIsControl))])
    out.push({id:'noparty:'+v.no+':'+c,severity:'warning',kind:'no-party',title:`${v.no} posts to the control ledger ${c} ${accLedgerName(c)}`,
      detail:'The source does not name the party (an old record, or a method with no settles-to account), so the amount sits on the control ledger, not on a party sub-ledger. It does not block the lock.',voucher:v.no,source:v.source.id});
  const t=accTotals(vs);
  if(t.debit!==t.credit)out.push({id:'unbal:day',severity:'error',kind:'unbalanced',title:'Trial balance does not agree',detail:`Debits ₹${(t.debit/100).toFixed(2)} ≠ credits ₹${(t.credit/100).toFixed(2)}`});
  const lock=accLockOf(s,date);
  if(lock&&fingerprint(lock.vouchers)!==fingerprint(accLiveVouchers(s,date)))out.push({id:'drift',severity:'warning',kind:'drift',title:'Live sources differ from the locked snapshot',detail:'Source records for this locked date changed after locking. The snapshot is kept; review and unlock if a correction is needed.'});
  // Wave C3: a parked accounting event (System health) blocks the lock of every unlocked date, as on the server.
  const stuck=lock?0:sysParkedAccounting(s);
  if(stuck)out.push({id:'stuck',severity:'error',kind:'stuck',title:SYS_STUCK_TITLE,detail:sysStuckDetail(stuck)});
  if(accDayStatus(s,date)==='Open')out.push({id:'open',severity:'info',kind:'open-day',title:'Business day not closed',detail:'Vouchers are provisional until the day is closed and locked.'});
  return out;
}
export const accBlocking=(xs:ACCException[])=>xs.filter(x=>x.severity==='error');
/** Why a date cannot be locked now ('' = it can). Permission is checked separately. */
export function accLockBlocker(s:State,date:string):string{
  if(accLockOf(s,date))return 'This date is already locked.';
  if(accDayStatus(s,date)==='Open')return 'Close the business day before locking it.';
  const ob=accOpeningLockBlocker(s,date);if(ob)return ob; // ACC-1 contract §5: "opening balances pending"
  const e=accBlocking(accExceptions(s,date));
  if(e.length)return `Resolve ${e.length} exception${e.length===1?'':'s'} first (${e[0].title}).`;
  return '';
}

// ── Reconciliation with existing reports ──
export type ACCCheck={id:string;label:string;report:number;ledger:number;ok:boolean;note:string};
const isTender=(id:string,mapping:Record<string,string>)=>{const a=accControlOf(id);return Object.entries(mapping).some(([k,v])=>k.startsWith('method:')&&v===a);}; // ACC-1: party ids sum into their control
export function accReconcile(s:State,date:string):ACCCheck[]{
  const sv=accSlice(s).server;if(sv)return sv.days[date]?.checks??[];
  const vs=accVouchers(s,date),map=accMapping(s),p=reportPack(s,date,date);
  const v=(tab:string,id:string)=>p.tabs.find(t=>t.id===tab)!.metrics.find(m=>m.id===id)?.value??0;
  const net=(code:string,f:(x:ACCVoucher)=>boolean=()=>true)=>vs.filter(f).reduce((a,x)=>a+x.entries.filter(e=>accControlOf(e.account)===code).reduce((b,e)=>b+e.credit-e.debit,0),0);
  const lines=(k:string)=>(x:ACCVoucher)=>x.source.kind==='line'&&x.source.sub===k;
  const tenderMove=(kinds:string[])=>vs.filter(x=>x.source.kind==='receipt'&&kinds.includes(x.source.sub)).reduce((a,x)=>a+x.entries.filter(e=>isTender(e.account,map)).reduce((b,e)=>b+e.debit-e.credit,0),0);
  // Expense vouchers: the last entry is the funding side (Cr for an expense, Dr for a reversal record) = the whole bill, also when DEC-135 splits
  // the debit into the category and the GST input-credit ledger.
  const expenseMove=vs.filter(x=>x.source.kind==='expense').reduce((a,x)=>{const f=x.entries[x.entries.length-1];return a+f.credit-f.debit;},0);
  const c=(id:string,label:string,report:number,ledger:number,note:string):ACCCheck=>({id,label,report,ledger,ok:report===ledger,note});
  const checks=[
    c('room','Room revenue',v('revenue','room'),net(map['line:room'],lines('room')),'Reports › Revenue · room lines'),
    c('service','Other revenue',v('revenue','service'),net(map['line:service'],lines('service')),'Reports › Revenue · service lines'),
    c('discount','Discounts (negative)',v('revenue','discount'),net(map['line:discount'],lines('discount')),'Reports › Revenue · discount lines'),
    c('adjustment','Adjustments & credit notes',v('revenue','adjustment'),net(map['line:adjustment'],lines('adjustment')),'Reports › Revenue · adjustment lines'),
    c('tax','GST (CGST + SGST)',v('revenue','tax'),[...new Set([map['folio:cgst'],map['folio:sgst']])].reduce((a,code)=>a+net(code,x=>x.source.kind==='line'),0),'Reports › Revenue · tax'),
    c('collections','Net receipts (payments − refunds)',v('receipts','net'),tenderMove(['payment','refund']),'Overview “collections” · Reports › Receipts'),
    c('deposits','Deposits net (in − out)',v('receipts','deposit-in')+v('receipts','deposit-out'),tenderMove(['deposit','deposit-return']),'Reports › Receipts · deposits'),
    c('expenses','Posted expenses',v('expenses','total'),expenseMove,'Reports › Expenses · total'),
    c('outlet','Outlet bills settled (net of voids)',closingOutlet(s,date).net,vs.filter(x=>x.source.kind==='pos').reduce((a,x)=>a+x.entries.filter(e=>isTender(e.account,map)).reduce((b,e)=>b+e.debit-e.credit,0),0),'Restaurant › Day summary · outlet receipts'),
  ];
  const cashLedger=-net(map['method:Cash']);
  const rec=s.closings?.[date];
  if(rec)checks.push(c('cash','Cash in hand movement',rec.cash.counted-rec.cash.opening,cashLedger,'Closing record · counted − opening float'));
  else if(date===s.date)checks.push(c('cash','Cash in hand movement',drawer(s)-s.openingCash+accCashDifference(s,date),cashLedger,'Cashier · expected drawer − opening float'));
  return checks;
}

// ── Mapping table rows (defaults + classifications discovered in data) ──
export type ACCMapRow={key:string;group:string;value:string;account:string;defaultAccount?:string;changed:boolean;uses:number;mapped:boolean};
export function accMapRows(s:State):ACCMapRow[]{
  const sv=accSlice(s).server;if(sv)return sv.mapRows;
  const map=accMapping(s),uses:Record<string,number>={};
  const add=(k:string)=>{uses[k]=(uses[k]??0)+1;};
  s.bookings.forEach(b=>{b.lines.forEach(l=>{add('line:'+l.kind);add('folio:guest');if(lineTax(l)){add('folio:cgst');add('folio:sgst');}});b.receipts.filter(r=>r.kind==='company-credit').forEach(()=>add('folio:guest'));b.receipts.forEach(r=>{add('receipt:'+r.kind);if(r.kind!=='company-credit')add('method:'+r.method);});});
  s.expenses.filter(expenseCounts).forEach(e=>{add('expense:'+e.category);add('funding:'+e.method);if(expItcPaise(e)){add('tax:gst-input');add('tax:gst-input-sgst');}});
  for(const r of s.expensesX?.udhaar??[])for(const p of r.settlements??[])if(p.desk){add('receipt:udhaar');add('method:'+p.mode);} // DEC-130
  for(const r of s.expensesX?.udhaar??[]){add('receipt:udhaar');add(expUdRevenueKey(r));if(expUdGst(r).tax){add('folio:cgst');add('folio:sgst');}} // ACC-1: credit raised
  for(const p of grpSlice(s).payments??[]){add('receipt:company-credit');add('method:'+p.method);} // ACC-1: company payments
  posSlice(s).receipts.forEach(r=>{add('pos:sales');if(r.tax){add('pos:cgst');add('pos:sgst');}add('method:'+r.method);});
  const keys=[...new Set([...Object.keys(ACC_DEFAULT_MAP),...Object.keys(uses)])];
  const order=Object.keys(KEY_LABEL);
  return keys.sort((a,b)=>order.indexOf(a.split(':')[0])-order.indexOf(b.split(':')[0])).map(key=>{const account=map[key]??'';
    return {key,group:accKeyGroup(key),value:accKeyValue(key),account,defaultAccount:ACC_DEFAULT_MAP[key],changed:!!account&&account!==ACC_DEFAULT_MAP[key],uses:uses[key]??0,mapped:!!account&&!!accLedger(s,account)?.active&&account!==ACC_SUSPENSE};});
}

// ── Export (generic voucher import format) ──
const csvCell=csvMinimal;
const rupees=(p:number)=>(p/100).toFixed(2);
export const ACC_CSV_HEADER=['Date','Voucher No','Voucher Type','Ledger','Debit','Credit','Narration','Source ID'];
/** One row per voucher entry. Generic import format (not Tally-specific); amounts in rupees, 2 decimals; CRLF. */
export function accCsv(vs:ACCVoucher[],s?:State):string{
  const rows:(string|number)[][]=[ACC_CSV_HEADER];
  for(const v of vs)for(const e of v.entries)rows.push([v.date,v.no,v.type,accEntryName(s,e.account),e.debit?rupees(e.debit):'',e.credit?rupees(e.credit):'',v.narration,v.source.id]);
  return rows.map(r=>r.map(csvCell).join(',')).join('\r\n');
}
/** Summary figures for a date (cards / stats). */
export function accDaySummary(s:State,date:string){
  const vs=accVouchers(s,date),t=accTotals(vs),ex=accExceptions(s,date);
  return {date,status:accDayStatus(s,date),count:vs.length,debit:t.debit,credit:t.credit,balanced:t.debit===t.credit,
    errors:accBlocking(ex).length,warnings:ex.filter(x=>x.severity==='warning').length,checks:accReconcile(s,date)};
}
/** Source record lookup for a voucher (dialog "trace to source"). */
export function accSourceOf(s:State,v:ACCVoucher):{booking?:Booking;line?:Line;receipt?:Receipt;expense?:Expense;posReceipt?:POSReceipt}{
  if(v.source.kind==='expense')return {expense:s.expenses.find(e=>e.id===v.source.id||'expense:'+e.id===v.source.id)};
  if(accSlice(s).server)return {}; // server source keys (charge:…, receipt:…, pos:…) are not prototype record ids
  if(v.source.kind==='pos')return {posReceipt:posSlice(s).receipts.find(r=>r.id===v.source.id)};
  if(v.source.kind==='closing'||v.source.sub==='udhaar-payment'||v.source.sub==='udhaar-credit'||v.source.sub==='company-payment')return {}; // DEC-130 / ACC-1: not folio records
  const i=v.source.id.indexOf('/'),bid=v.source.id.slice(0,i),rid=v.source.id.slice(i+1),booking=s.bookings.find(b=>b.id===bid);
  return v.source.kind==='line'?{booking,line:booking?.lines.find(l=>l.id===rid)}:{booking,receipt:booking?.receipts.find(r=>r.id===rid)};
}

// ── Tally-style XML preview (W05-E) ──
// Rule source (read-only): ETP Tally preparation — building or saving this XML is NOT "imported into Tally"; only Tally's own import confirms that.
// Here the preview is always SIMULATED: nothing is sent anywhere. Tally convention: debit = ISDEEMEDPOSITIVE Yes with a negative amount, credit = No with a positive amount.
export const TALLY_MAX_DAYS=31;
const xmlEsc=(t:string)=>t.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g,'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const tallyDate=(d:string)=>d.split('-').join('');
export type TallyPreview={xml:string;count:number;debit:number;credit:number;from:string;to:string;dates:number;simulated:true;error?:string;
  /** The business dates carried (incl. DEC-077 re-exports outside the range). */dateList:string[];
  /** DEC-077: dates carried again because they changed after an earlier export; provisional = the day is still reopened, or (A03b-05) a
   *  date that is not locked while accounting events are pending (`lagging`, `pending` = their count, production only). */reexport:string[];provisional:string[];
  /** DEC-077: the reopened day, when carried. */reopened:string[];lagging:string[];pending:number;
  /** A03b-05: carried dates an export recorded before vouchers carried a REMOTEID took to Tally. */legacy:string[]};
/** DEC-077: the XML note naming a re-exported / provisional day (escaped; "--" cannot end the comment early). */
export const accTallyNote=(text:string)=>`<!-- ${xmlEsc(text).split('--').join('- -')} -->`;
/** A03b-05: the fixed comment line after the SIMULATED comment (server VoucherExport.TallyIdNote, same text). */
export const ACC_TALLY_ID_NOTE='<!-- VOUCHER IDS · every voucher carries a REMOTEID and the same GUID (HCC-hotel identity-source record) that never changes.'
  +' With "Overwrite vouchers when a voucher with same GUID exists" set to Yes in Tally\'s import (TallyPrime: Import › Transactions), importing'
  +' a voucher again replaces it instead of adding a second copy. ACTION stays Create; Tally assigns its own VCHKEY and MASTERID. -->';
/** A03b-05: the hotel's Tally identity in the demo (the server keeps one random GUID per property, acc.tally_identity). */
export const ACC_TALLY_DEMO_ID='DEMO';
/** A03b-05: a voucher's REMOTEID / GUID (server VoucherExport.RemoteId): the same for that voucher in every export, unique per voucher. */
export const accTallyRemoteId=(identity:string,sourceKey:string)=>`HCC-${identity}-${sourceKey}`;
// The export notes (server VoucherExport.ReexportNote / ReexportAfterProvisionalNote / ReopenedNote / PendingNote / LegacyNote, same text).
export const accTallyReexportNote=(d:string,reason:string)=>`RE-EXPORT · ${d} changed after it was exported (day reopened: ${reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds the new ones, so nothing needs deleting first.`;
export const accTallyReexportProvisionalNote=(d:string,reason:string)=>`RE-EXPORT · ${d} was exported while it was provisional (${reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds any that were missing.`;
export const accTallyReopenedNote=(d:string)=>`PROVISIONAL · ${d} is reopened and may still change; it is carried again by the first export after it is closed again, and importing that file replaces these vouchers by their REMOTEID.`;
export const accTallyPendingNote=(d:string,n:number)=>`PROVISIONAL · ${d} is not locked and ${n} accounting event(s) of this hotel are not yet in the vouchers (still being processed, or parked after an error), so this day may be incomplete. It is carried again by the first export after they are processed, and importing that file replaces these vouchers by their REMOTEID.`;
export const accTallyLegacyNote=(d:string)=>`LEGACY · ${d} was exported before vouchers carried a REMOTEID. If that earlier file was imported, delete that day's HCC vouchers from it in Tally before importing this file, or Tally will hold them twice.`;
/** A03b-05: the re-export flag reason of a date exported while accounting events were pending (server AccountingQueries.PendingFlagReason). */
export const accTallyPendingReason=(n:number)=>`${n} accounting event(s) were still pending`;
/** Production: accounting events of the property not yet in the vouchers, from the server's "pending" exception (same count on every unlocked date). */
export function accPendingEvents(s:State,dates:string[]):number{
  const sv=accSlice(s).server;if(!sv)return 0;
  return Math.max(0,...dates.flatMap(d=>(sv.days[d]?.exceptions??[]).filter(x=>x.kind==='pending').map(x=>Number(/^(\d+)/.exec(x.detail)?.[1]??1))));
}
/**
 * Vouchers of every business date from..to (inclusive, ≤ 31 days) as a Tally "Import Data" envelope. A03b-05: every voucher carries a
 * stable REMOTEID / GUID (accTallyRemoteId; demo identity DEMO, production preview SERVER — the downloaded file is the server's, with the
 * hotel's own identity), so importing it again replaces it in Tally. DEC-077: dates flagged changed after an earlier export
 * (accountingX.reexport) are carried again even outside the range, each with a RE-EXPORT note; the reopened day is PROVISIONAL, and so
 * (A03b-05, production) is every date that is not locked while accounting events are pending.
 */
export function accTallyXml(s:State,from:string,to:string,company?:string,identity?:string):TallyPreview{
  const empty={xml:'',count:0,debit:0,credit:0,from,to,dates:0,simulated:true as const,dateList:[],reexport:[],provisional:[],reopened:[],lagging:[],pending:0,legacy:[]};
  // F10 (W05 review): a real calendar date, not only the yyyy-mm-dd shape (2026-02-30 used to pass).
  const real=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d)&&!Number.isNaN(Date.parse(d+'T00:00:00Z'))&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
  if(!real(from)||!real(to)||from>to)return {...empty,error:'Choose a valid range: "from" on or before "to".'};
  const days=Math.round((Date.parse(to)-Date.parse(from))/86400000)+1;
  if(days>TALLY_MAX_DAYS)return {...empty,error:`Choose at most ${TALLY_MAX_DAYS} days.`};
  const flags=accSlice(s).reexport??{},open=closingReopened(s)?.date;
  const dates=[...new Set([...accDates(s).filter(d=>d>=from&&d<=to),...Object.keys(flags).filter(d=>accVouchers(s,d).length)])].sort(),vs=dates.flatMap(d=>accVouchers(s,d));
  const pending=accPendingEvents(s,dates),lagging=pending>0?dates.filter(d=>accDayStatus(s,d)!=='Locked'):[];
  const reexport=dates.filter(d=>flags[d]),reopened=dates.filter(d=>d===open),provisional=dates.filter(d=>d===open||lagging.includes(d));
  const legacy=dates.filter(d=>(accSlice(s).tallyExports??[]).some(e=>!e.ids&&e.dates.includes(d)));
  const notes=[...reexport.map(d=>accTallyNote(flags[d].reopen?accTallyReexportNote(d,flags[d].reason):accTallyReexportProvisionalNote(d,flags[d].reason))),
    ...reopened.map(d=>accTallyNote(accTallyReopenedNote(d))),...lagging.map(d=>accTallyNote(accTallyPendingNote(d,pending))),
    ...legacy.map(d=>accTallyNote(accTallyLegacyNote(d)))];
  const name=xmlEsc(company??s.settings.hotel),tag=identity??(accSlice(s).server?'SERVER':ACC_TALLY_DEMO_ID);
  const ids=vs.map(v=>accTallyRemoteId(tag,v.source.id));
  if(new Set(ids).size!==ids.length)throw new Error('Two vouchers in one Tally export share a source key; every REMOTEID must be unique.');
  const body=vs.map((v,i)=>`   <TALLYMESSAGE xmlns:UDF="TallyUDF"><VOUCHER REMOTEID="${xmlEsc(ids[i])}" VCHTYPE="${v.type}" ACTION="Create" OBJVIEW="Accounting Voucher View">
    <DATE>${tallyDate(v.date)}</DATE><GUID>${xmlEsc(ids[i])}</GUID><VOUCHERTYPENAME>${v.type}</VOUCHERTYPENAME><VOUCHERNUMBER>${xmlEsc(v.no)}</VOUCHERNUMBER><NARRATION>${xmlEsc(v.narration)}</NARRATION><EFFECTIVEDATE>${tallyDate(v.date)}</EFFECTIVEDATE>
${v.entries.map(e=>`    <ALLLEDGERENTRIES.LIST><LEDGERNAME>${xmlEsc(accLedgerTallyName(s,accControlOf(e.account)))}</LEDGERNAME><ISDEEMEDPOSITIVE>${e.debit?'Yes':'No'}</ISDEEMEDPOSITIVE><AMOUNT>${e.debit?'-'+rupees(e.debit):rupees(e.credit)}</AMOUNT></ALLLEDGERENTRIES.LIST>`).join('\n')}
   </VOUCHER></TALLYMESSAGE>`).join('\n');
  const t=accTotals(vs);
  const xml=`<?xml version="1.0" encoding="UTF-8"?>
<!-- SIMULATED PREVIEW · Hotel Control Centre prototype · fictional data · nothing was sent to Tally · saving this file does not mean it was imported -->
${ACC_TALLY_ID_NOTE}
${notes.map(n=>n+'\n').join('')}<ENVELOPE>
 <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>
 <BODY><IMPORTDATA>
  <REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME><STATICVARIABLES><SVCURRENTCOMPANY>${name}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
  <REQUESTDATA>
${body}
  </REQUESTDATA>
 </IMPORTDATA></BODY>
</ENVELOPE>
`;
  return {xml,count:vs.length,debit:t.debit,credit:t.credit,from,to,dates:dates.length,simulated:true,dateList:dates,reexport,provisional,reopened,lagging,pending,legacy};
}
