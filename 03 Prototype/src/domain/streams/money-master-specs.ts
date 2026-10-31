// MA-3 · Registration of the four money masters (plan Part 2 §8 M15–M18) in the MA-0 framework, group "Money and tax".
// All four are money lists: every change is Owner-only (DEC-058; server PERM-MST-002). Rows, seeds and selectors: money-master.ts.
//   paymentMethods   (M15) code, name, kind, settles to (bankAccounts), needs reference, usable at, surcharge, ledger
//   bankAccounts     (M16) code, name, till / bank, bank, last 4 digits, IFSC, opening balance (setup only), default for, —
//   expenseCategories(M17) code, name, ledger (accounting default), needs bill, Manager approval limit override
//   expenseVendors   (M18, DEC-MA-6) code, name, type, GSTIN, PAN, TDS, contact, payment terms — separate from maintenance vendors
// Rules on top of the framework: a method's kind and "settles to" cannot change once records use it (old drawer and bank figures would
// move); cash settles to a till, credit to nothing, the rest to a bank account; an account that active methods settle to cannot be
// retired; one default account per method kind; the opening balance is setup-only (fixed once money settles through the account).
import {registerMaster,type MasterSpec,type FieldSpec,type MasterRow} from './masters';
import {ACC_CHART,accPartyRetireBlocker} from './accounting';
import {accIsFixedAssetLedger} from './accounting-assets'; // ACC-2 acc2-purchase: capital categories post to a fixed-asset block
import {EXP_ITC_BLOCKED_ASSET_LEDGERS} from './expenses-vendor';
import {mastersSeed} from '../../fixtures/ext/masters-seed';
import {tdsPayeeHint} from '../tds'; // ACC-2 integration (ruling 9)
import {
  MNY_PM,MNY_BA,MNY_EC,MNY_EV,MNY_KINDS,MNY_PLACES,MNY_VENDOR_TYPES,MNY_SEED_METHODS,MNY_SEED_ACCOUNTS,MNY_SEED_CATEGORIES,mnySeedVendors,
  mnyMethods,mnyAccounts,mnyMethodUsage,mnyAccountUsage,mnyCategoryUsage,mnyVendorUsage,mnyMethodRecords,
  type MnyMethod,type MnyAccount,type MnyCategory,type MnyVendor,
} from './money-master';

const base=(codeHelp:string,namePh:string):FieldSpec[]=>[
  {key:'code',label:'Code',type:'text',required:true,maxLength:20,help:codeHelp},
  {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:namePh,help:'Shown in pick lists and on records. Old records keep resolving after a rename.'},
];
const tail:FieldSpec[]=[{key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000,help:'Lower comes first in pick lists.'},
  {key:'active',label:'Active',type:'toggle',help:'Retire instead of deleting; old records keep the name.'}];
const ledgerOpts=(groups:string[])=>ACC_CHART.filter(a=>groups.includes(a.group)).map(a=>({value:a.code,label:`${a.code} · ${a.name}`}));
/** ACC-2 (contract §3.4): the fixed-asset blocks 15x0 a capital category posts to. */
const assetOpts=()=>ACC_CHART.filter(a=>a.groupId==='fixed-assets').map(a=>({value:a.code,label:`${a.code} · ${a.name} (fixed asset)`}));
/** ACC-2 (contract §5): the TDS rate-table rows a vendor can be deducted under (acc2-tds lane's TDS_SEED keys; CA to confirm). */
export const MNY_TDS_SECTIONS=[{value:'rent',label:'Rent (old 194-I)'},{value:'contractor',label:'Contractor (old 194C)'},{value:'professional',label:'Professional / technical fees (old 194J)'},{value:'commission',label:'Commission (old 194H)'}];
/** ACC-2 integration (ruling 9): the payee's TDS rate column. */
export const MNY_PAYEE_TYPES=[{value:'other',label:'Other payee (company, firm, LLP…) — default'},{value:'individual',label:'Individual / HUF'}];
export const MNY_STATE=/^[A-Z]{2}$/;
export const MNY_IFSC=/^[A-Z]{4}0[A-Z0-9]{6}$/;
export const MNY_GSTIN=/^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;
export const MNY_PAN=/^[A-Z]{5}\d{4}[A-Z]$/;
const old=<T extends MasterRow>(rows:T[],row:T)=>rows.find(r=>r.id===row.id);

export const MNY_PM_SPEC:MasterSpec<MnyMethod>={
  id:MNY_PM,label:'Payment methods',singular:'payment method',group:'money',prefix:'PMT',money:true,permission:'settings',
  help:'One list for the folio, the restaurant (POS), expenses, the cash book and closing. “Settles to” decides the drawer (till) or bank account.',
  fields:[...base('Short code, e.g. CASH, UPI, CARD. Cannot change once used.','UPI'),
    {key:'kind',label:'Kind',type:'select',required:true,options:MNY_KINDS},
    {key:'settlesTo',label:'Settles to',type:'select',master:MNY_BA,help:'Till (front-desk drawer) for cash; the bank account for UPI, card and transfers; none for credit.'},
    {key:'needsReference',label:'Needs a transaction reference',type:'toggle',help:'Guest payments (folio, POS) must carry the UPI / card / bank reference.'},
    {key:'usableAt',label:'Usable at',type:'multiselect',options:MNY_PLACES},
    {key:'surchargePct',label:'Surcharge',type:'percent',min:0,max:1000,money:true,help:'Shown to the cashier as a hint; not posted automatically. 0–10 %.'},
    {key:'ledger',label:'Ledger',type:'select',options:ledgerOpts(['Asset','Liability']),help:'Accounting default for this method (Accounting › Mapping can override).'},
    ...tail],
  inUse:(s,row)=>mnyMethodUsage(s,row),
  validate:(s,row,rows)=>{
    const acc=row.settlesTo?mnyAccounts(s).find(a=>a.code===row.settlesTo):undefined;
    if(row.kind==='cash'&&acc?.kind!=='till')return 'A cash method settles to a till (the front-desk drawer).';
    if(row.kind==='credit'&&row.settlesTo)return 'A credit method settles to no account — leave “Settles to” empty.';
    if(row.kind!=='cash'&&row.kind!=='credit'&&acc?.kind!=='bank')return 'Choose the bank account this method settles to.';
    const at=row.usableAt??[];
    if(row.kind==='credit'&&(at.includes('frontdesk')||at.includes('pos')))return 'Credit is not a guest payment: company credit goes through the approval on the folio. Use it for expenses only.';
    const o=old(rows,row);
    if(o&&(o.kind!==row.kind||(o.settlesTo??'')!==(row.settlesTo??''))){const n=mnyMethodRecords(s,o);
      if(n>0)return `Kind and “settles to” cannot change once the method is used (${n} records) — add a new method and retire this one.`;}
    return undefined;
  },
  canRetire:(s,row)=>{const left=mnyMethods(s).filter(m=>m.active&&m.id!==row.id&&(m.usableAt??[]).includes('frontdesk'));
    return (row.usableAt??[]).includes('frontdesk')&&!left.length?'Keep at least one active payment method for the front desk.':undefined;},
  seed:s=>mastersSeed(MNY_PM,s) as MnyMethod[],
};

export const MNY_BA_SPEC:MasterSpec<MnyAccount>={
  id:MNY_BA,label:'Bank accounts and tills',singular:'account',group:'money',prefix:'BAC',money:true,permission:'settings',
  help:'Where payment methods settle: the front-desk drawer (till) and the hotel bank accounts. Only the last four digits of an account number are kept.',
  fields:[...base('Short code, e.g. TILL, BANK-1.','Hotel current account'),
    {key:'kind',label:'Type',type:'select',required:true,options:[{value:'till',label:'Till (cash drawer)'},{value:'bank',label:'Bank account'}]},
    {key:'bank',label:'Bank',type:'text',maxLength:60,placeholder:'Bank name'},
    {key:'accountLast4',label:'Account number — last 4 digits',type:'text',maxLength:4,placeholder:'4321',help:'Masked everywhere as XXXX 4321; the full number is never stored here.'},
    {key:'ifsc',label:'IFSC',type:'text',maxLength:11,placeholder:'ABCD0123456'},
    {key:'openingBalance',label:'Opening balance',type:'money',money:true,help:'Setup only: fixed once money settles through this account.'},
    {key:'defaultFor',label:'Default for new methods of kind',type:'multiselect',options:MNY_KINDS.filter(k=>k.value!=='cash'&&k.value!=='credit')},
    ...tail],
  inUse:(s,row)=>mnyAccountUsage(s,row),
  validate:(s,row,rows)=>{
    if(row.kind==='till'&&(row.bank||row.accountLast4||row.ifsc))return 'A till has no bank, account number or IFSC.';
    if(row.kind==='bank'&&!row.bank)return 'Enter the bank name.';
    if(row.accountLast4&&!/^\d{4}$/.test(String(row.accountLast4)))return 'Account number: enter only the last 4 digits.';
    if(row.ifsc)row.ifsc=String(row.ifsc).toUpperCase(); // stored in capitals (the command saves the validated row)
    if(row.ifsc&&!MNY_IFSC.test(String(row.ifsc)))return 'IFSC: 4 letters, 0, then 6 letters or digits (e.g. ABCD0123456).';
    if(row.kind==='till'&&(row.defaultFor??[]).length)return 'A till is not a default for UPI, card or bank methods.';
    for(const k of row.defaultFor??[]){const other=rows.find(r=>r.id!==row.id&&r.active&&(r.defaultFor??[]).includes(k));
      if(other)return `${other.name} is already the default for ${MNY_KINDS.find(x=>x.value===k)?.label??k} — remove it there first.`;}
    const o=old(rows,row);
    if(o&&(o.openingBalance??0)!==(row.openingBalance??0)){const used=mnyMethods(s).filter(m=>m.settlesTo===o.code).reduce((a,m)=>a+mnyMethodRecords(s,m),0);
      if(used>0)return `The opening balance is setup-only: ${used} records already settle through ${o.name}.`;}
    if(o&&o.kind!==row.kind&&mnyAccountUsage(s,o).count)return 'Type cannot change while payment methods settle to this account.';
    return undefined;
  },
  canRetire:(s,row)=>{const ms=mnyMethods(s).filter(m=>m.active&&m.settlesTo===row.code);
    if(ms.length)return `Active payment methods settle to ${row.name} (${ms.map(m=>m.name).join(', ')}) — retire or move them first.`;
    // ACC-1 (contract §3, rulebook §3.3.1): a till (1000/<code>) or bank account (1030/<code>) with a non-zero ledger balance stays.
    return accPartyRetireBlocker(s,row.kind==='till'?'1000':'1030',row.code)||undefined;},
  seed:s=>mastersSeed(MNY_BA,s) as MnyAccount[],
};

export const MNY_EC_SPEC:MasterSpec<MnyCategory>={
  id:MNY_EC,label:'Expense categories',singular:'expense category',group:'money',prefix:'ECT',money:true,permission:'settings',
  help:'Categories on the expense form and reports; the ledger is the accounting default (Accounting › Mapping can override).',
  fields:[...base('Short code, e.g. SUPPLIES.','Kitchen gas'),
    {key:'ledger',label:'Ledger',type:'select',required:true,options:[...ledgerOpts(['Expense']),...assetOpts()],help:'An expense ledger; a fixed-asset block (15x0) only for a capital category.'},
    {key:'needsBill',label:'Needs a bill number',type:'toggle',help:'Submitting an expense in this category requires the receipt / bill number.'},
    {key:'approvalLimit',label:'Manager approval limit',type:'money',money:true,help:'Leave empty for the hotel limit (₹5,000). Above it the Owner approves.'},
    // ACC-2 (contract §3; DEC-143, DEC-145; rulebook §4.4–4.5, C8, C10). Owner-only like every money master (DEC-058).
    {key:'itcBlocked',label:'ITC blocked (s.17(5))',type:'toggle',help:'Food, outdoor catering, health, club membership, motor vehicles ≤ 13 seats, construction, personal use… The ITC switch stays off on these bills (DEC-143).'},
    {key:'rcm',label:'Reverse charge (RCM)',type:'toggle',help:'Security agency, goods transport, advocate, hired vehicle… (DEC-145): the bill carries no GST; HCC posts the GST to “GST payable under reverse charge” (2140), paid by challan.'},
    {key:'rcmBps',label:'Reverse-charge GST rate',type:'percent',min:0,max:2800,money:true,help:'Default rate of the GST the hotel pays under reverse charge (editable on the bill). CA to confirm.'},
    {key:'capital',label:'Capital purchase (fixed asset)',type:'toggle',help:'Furniture, AC, kitchen machine, computer, vehicle, building work: the bill goes to the fixed-asset block chosen as the ledger and into the asset register, never to expense budgets.'},
    // ACC-2 integration (ruling 10, contract §7): goods transport — the cash-payment limit is ₹35,000 a day instead of ₹10,000.
    {key:'transport',label:'Goods transport',type:'toggle',help:'Hire of goods carriages: cash paid to one transporter in a day may reach ₹35,000 (not ₹10,000) before the income-tax deduction is lost (old s.40A(3)). VERIFY section numbers.'},
    ...tail],
  inUse:(s,row)=>mnyCategoryUsage(s,row),
  // ACC-2: a capital category posts to a fixed-asset block and only a capital category does; an RCM category needs its rate.
  validate:(s,row)=>{
    const asset=accIsFixedAssetLedger(s,row.ledger);
    if(row.capital&&!asset)return 'A capital category posts to a fixed-asset block (15x0 — Buildings, Furniture, Plant & machinery, Computers, Vehicles).';
    if(!row.capital&&asset)return 'A fixed-asset block is the ledger of a capital category only — turn on “Capital purchase” or choose an expense ledger.';
    if(row.rcm&&!(Number(row.rcmBps)>0))return 'Enter the reverse-charge GST rate (e.g. 18 %; GTA 5 %).';
    if(!row.rcm&&row.rcmBps)row.rcmBps=undefined; // a rate without the flag is not kept
    for(const f of ['rcm','capital','itcBlocked','transport'] as const)if(!row[f])delete row[f]; // off = absent (rows before ACC-2 have none)
    if(row.capital&&EXP_ITC_BLOCKED_ASSET_LEDGERS.includes(String(row.ledger))&&!row.itcBlocked)row.itcBlocked=true; // vehicles / buildings: blocked (rulebook §4.5)
    return undefined;
  },
  seed:s=>mastersSeed(MNY_EC,s) as MnyCategory[],
};

export const MNY_EV_SPEC:MasterSpec<MnyVendor>={
  id:MNY_EV,label:'Expense vendors',singular:'expense vendor',group:'money',prefix:'EVN',money:true,permission:'settings',
  help:'Vendors on the expense form (DEC-MA-6: maintenance vendors are a separate list). Seeded from the vendors already on expenses.',
  fields:[...base('Short code, e.g. V-SAI-PROVISIONS.','Sai Provisions'),
    {key:'type',label:'Type',type:'select',required:true,options:MNY_VENDOR_TYPES},
    {key:'gstin',label:'GSTIN',type:'text',maxLength:15,placeholder:'27ABCDE1234F1Z5'},
    {key:'pan',label:'PAN',type:'text',maxLength:10,placeholder:'ABCDE1234F'},
    {key:'tds',label:'TDS applies',type:'toggle',help:'Flag for compliance (TDS on payments to this vendor). Needs the PAN.'},
    // ACC-2 (contract §3, §5): GST state (MH = intra-state; any other = IGST bills) and the TDS rate-table row.
    {key:'state',label:'GST state code',type:'text',maxLength:2,placeholder:'MH',help:'Two letters. MH (the hotel’s state) or empty = CGST + SGST; any other state = IGST bills (ITC to Input IGST).'},
    {key:'tdsSection',label:'TDS section',type:'select',options:MNY_TDS_SECTIONS,help:'Rate-table row the hotel deducts under when TDS applies (DEC-146; CA to confirm).'},
    // ACC-2 integration (ruling 9): the rate column follows the payee (old 194C: individual / HUF 1 %, others 2 %).
    {key:'payeeType',label:'TDS payee type',type:'select',options:MNY_PAYEE_TYPES,money:true,help:'Individual / HUF or other — picks the TDS rate column. Left empty, it follows the PAN (4th letter P or H = individual), else Other.'},
    {key:'contact',label:'Contact',type:'text',maxLength:100,placeholder:'Name · phone'},
    {key:'terms',label:'Payment terms',type:'text',maxLength:60,placeholder:'Immediate / 15 days'},
    ...tail],
  inUse:(s,row)=>mnyVendorUsage(s,row),
  validate:(_s,row)=>{
    const g=row.gstin?String(row.gstin).toUpperCase():'',p=row.pan?String(row.pan).toUpperCase():'';
    if(g)row.gstin=g;if(p)row.pan=p; // stored in capitals (the command saves the validated row)
    if(g&&!MNY_GSTIN.test(g))return 'GSTIN: 15 characters, e.g. 27ABCDE1234F1Z5.';
    if(p&&!MNY_PAN.test(p))return 'PAN: 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F).';
    if(g&&p&&g.slice(2,12)!==p)return 'The PAN inside the GSTIN (characters 3–12) does not match the PAN.';
    if(row.tds&&!p)return 'Enter the PAN when TDS applies.';
    const st=row.state?String(row.state).trim().toUpperCase():'';if(st)row.state=st;else if(row.state!==undefined)row.state=undefined;
    if(st&&!MNY_STATE.test(st))return 'GST state: two letters (e.g. MH, KA, GJ).';
    if(row.tdsSection&&!MNY_TDS_SECTIONS.some(x=>x.value===row.tdsSection))return 'Choose a TDS section from the list.';
    if(row.tdsSection&&!row.tds)return 'Turn on “TDS applies” to choose a TDS section.';
    if(row.payeeType&&!MNY_PAYEE_TYPES.some(x=>x.value===row.payeeType))return 'Choose the TDS payee type from the list.';
    if(!row.payeeType){const hint=tdsPayeeHint(p);if(hint)row.payeeType=hint;else delete row.payeeType;} // ruling 9: hint from the PAN, else absent = other
    return undefined;
  },
  // ACC-1 (contract §3, rulebook §3.3.1): a vendor whose party ledger 2200/<code> has a non-zero balance cannot be retired.
  canRetire:(s,row)=>accPartyRetireBlocker(s,'2200',row.code)||undefined,
  seed:s=>mastersSeed(MNY_EV,s) as MnyVendor[],
};

export const MNY_SPECS=[MNY_PM_SPEC,MNY_BA_SPEC,MNY_EC_SPEC,MNY_EV_SPEC] as const;
for(const spec of MNY_SPECS)registerMaster(spec as unknown as MasterSpec);
