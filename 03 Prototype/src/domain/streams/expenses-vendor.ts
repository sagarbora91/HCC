// ACC-2 · lane acc2-purchase — purchases, reverse charge, vendor payments / advances / returns and creditor ageing (02 Planning/agents/
// ACC2-POSTING-CONTRACT.md §3; rulebook §4.4–4.5, §5 C1, C3–C5, C8, C10; DEC-143 ITC only on bills directly for 18 % supplies, DEC-145 RCM
// flag on categories, DEC-147 expense when paid). Exported names start with 'exp' / 'EXP' (shared mock namespace).
//
// The posting itself lives in accLiveVouchers (accounting.ts, the expense source): it reads ONLY the fields stamped on the record
// (gstKind, rcm / rcmBps, capital, tdsPaise, itc), so a later change of a category or vendor master never moves a posted voucher.
// Fields are stamped when the bill is approved (expPurchaseStamp); entry-time guards (expPurchaseEntry) refuse ITC on a blocked category
// and vendor GST on a reverse-charge bill.
//
// LEAF-ish module: type imports, the money-master leaf and the GST split only — selectors.ts (drawer) and accounting.ts import it.
import type {State,Expense} from '../types';
import type {ExpVendorPayment} from './expenses-scc';
import {mnyCategory,mnyVendor,mnyIsCredit,mnyIsDrawer,mnyMethod,mnyNames} from './money-master';
import {gstSplit} from '../gst';

import {tdsForBill} from '../tds'; // ACC-2 integration: the acc2-tds lane's calculator (contract §5) replaces the purchase lane's stub

/** The hotel's own GST state (DEC-136: place of supply is the hotel). A vendor with another two-letter state code bills IGST. */
export const EXP_HOME_STATE='MH';
/** Mapping keys of the purchase lines (defaults in ACC_DEFAULT_MAP; server ChartOfAccounts.DefaultMap): IGST input credit 1330, RCM payable
 *  2140, TDS payable 2150 and the vendor payable a vendor payment / advance debits (2200, party = the vendor). */
export const EXP_KEY_IGST='tax:gst-input-igst';
export const EXP_KEY_RCM='tax:rcm-payable';
export const EXP_KEY_TDS='tax:tds-payable';
export const EXP_KEY_VENDOR='vendor:payable';
/** Ledgers whose ITC is always refused on a capital purchase (rulebook §4.5 / C10: motor vehicles, construction of immovable property). */
export const EXP_ITC_BLOCKED_ASSET_LEDGERS=['1500','1540'];
/** DEC-143 guard text (the ITC switch cannot be turned on for a blocked category). */
export const expItcBlockedMessage=(category:string)=>`ITC cannot be allowed on ${category}: the category is marked blocked (CGST s.17(5)) — DEC-143: ITC only on a bill directly for an 18 % supply; this GST stays in the cost.`;
export const EXP_RCM_GST_MESSAGE='A reverse-charge bill carries no vendor GST (DEC-145): the hotel pays the GST itself under reverse charge — clear the GST on the bill.';

// ── Small helpers ──
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
/** Integer rounding half away from zero of n × bps ÷ 10000 (as gstLineTax). */
export function expBpsOf(amount:number,bps:number):number{
  const n=Math.abs(amount)*Math.max(0,Math.trunc(bps||0)),q=Math.floor(n/10000),t=q+((n-q*10000)*2>=10000?1:0);return amount<0?-t:t;
}
/** Vendor master code of a stored vendor value (name, code or an earlier name); the trimmed value itself when it is not in the master. */
export const expVendorKey=(s:State,v:string|undefined)=>{const t=txt(v);return t?mnyVendor(s,t)?.code??t:'';};
/** Display name of a vendor key. */
export const expVendorName=(s:State,v:string)=>mnyVendor(s,v)?.name??v;
/** True for a vendor return / credit note record (contract §3.5): a negative bill linked to the bill it returns. */
export const expIsReturn=(e:Pick<Expense,'sourceRef'>)=>typeof e.sourceRef==='string'&&e.sourceRef.startsWith('ret:');
export const expReturnOf=(e:Pick<Expense,'sourceRef'>)=>expIsReturn(e)?String(e.sourceRef).slice(4):'';
const counts=(e:Expense)=>e.status==='Posted'||e.status==='Reversed';

// ── GST kind (intra / inter) ──
/** GST kind of a bill from the vendor's state (contract §3: `state` two-letter code, MH = intra; none = intra). */
export function expGstKindOf(s:State,vendor:string|undefined):'intra'|'inter'{
  const st=txt(vendor)?String(mnyVendor(s,txt(vendor))?.state??'').trim().toUpperCase():'';return st&&st!==EXP_HOME_STATE?'inter':'intra';
}

// ── Entry and posting guards ──
/** Why ITC cannot be 'allowed' on this category ('' = it can): the s.17(5) flag (DEC-143), or a capital purchase of a vehicle / building. */
export function expItcBlockRefusal(s:State,category:string,itc:unknown):string{
  if(itc!=='allowed')return '';const c=mnyCategory(s,category);if(!c)return '';
  if(c.itcBlocked||(c.capital&&EXP_ITC_BLOCKED_ASSET_LEDGERS.includes(String(c.ledger??''))))return expItcBlockedMessage(c.name);
  return '';
}
/** The purchase fields a new / edited bill carries (contract §3), or the refusal: `gstKind` (sent, else the vendor's state — kept only when
 *  sent or inter), `rcm` + `rcmBps` for a reverse-charge category (the typed rate 0–28 %, else the category default); an RCM bill carries no vendor GST; ITC refused on a blocked
 *  category. `itc` = the flag the caller resolved; an RCM category keeps 'allowed' / 'disallowed' without GST (its ITC is the RCM GST). */
export function expPurchaseEntry(s:State,x:{category:string;vendor?:string;gstKind?:unknown;rcmBps?:unknown;gstPaise?:number;itc?:unknown}):
  {error?:string;fields:Pick<Expense,'gstKind'|'rcm'|'rcmBps'>&{itc?:'allowed'|'disallowed'}}{
  const c=mnyCategory(s,x.category),fields:Pick<Expense,'gstKind'|'rcm'|'rcmBps'>&{itc?:'allowed'|'disallowed'}={};
  if(x.gstKind!==undefined&&x.gstKind!==null&&x.gstKind!==''&&x.gstKind!=='intra'&&x.gstKind!=='inter')return {error:'Choose the GST on the bill: intra-state (CGST + SGST) or inter-state (IGST).',fields};
  const kind=x.gstKind==='intra'||x.gstKind==='inter'?x.gstKind:expGstKindOf(s,x.vendor);
  if(x.gstKind==='intra'||x.gstKind==='inter'||kind==='inter')fields.gstKind=kind;
  if(c?.rcm){
    if(x.gstPaise)return {error:EXP_RCM_GST_MESSAGE,fields};
    if(x.rcmBps!==undefined&&x.rcmBps!==null&&x.rcmBps!==''){const b=Number(x.rcmBps);if(!(Number.isInteger(b)&&b>=0&&b<=2800))return {error:'Reverse-charge GST rate: 0 to 28 %.',fields};fields.rcmBps=b;}
    else fields.rcmBps=Number(c.rcmBps??0);
    fields.rcm=true; // re-stamped from the category at approval (expPurchaseStamp)
    if(x.itc==='allowed'||x.itc==='disallowed')fields.itc=x.itc;
  }
  const why=expItcBlockRefusal(s,x.category,x.itc);if(why)return {error:why,fields};
  return {fields};
}
/** Fields stamped on a bill when it is approved (contract §3: "from the category flag at the time of posting"): rcm + rcmBps (the bill's own
 *  rate, else the category default), capital, TDS held back (tdsForBill, contract §5), the vendor's TDS section. Absent flags are
 *  not written (older records and ordinary bills stay unchanged). Refusal text when ITC is allowed on a blocked category. */
export function expPurchaseStamp(s:State,e:Expense):{error?:string;fields:Partial<Expense>}{
  const c=mnyCategory(s,e.category),f:Partial<Expense>={};
  const why=expItcBlockRefusal(s,e.category,e.itc);if(why)return {error:why,fields:f};
  if(c?.rcm){if(e.gstPaise)return {error:EXP_RCM_GST_MESSAGE,fields:f};f.rcm=true;f.rcmBps=e.rcmBps??Number(c.rcmBps??0);}
  if(c?.capital)f.capital=true;
  const t=tdsForBill(s,{vendor:txt(e.vendor),amount:e.amount,gstPaise:e.gstPaise??0,date:e.date,exclude:e.id});
  if(t.paise){f.tdsPaise=t.paise;const sec=t.section||mnyVendor(s,txt(e.vendor))?.tdsSection;if(sec)f.tdsSection=sec;}
  return {fields:f};
}

// ── The purchase lines of a counted record (read by accLiveVouchers) ──
export type ExpPurchaseLines={
  /** ITC of the bill's GST (allowed): [mapping key, paise] — intra CGST / SGST halves (1310 / 1320), inter the whole to IGST (1330). */
  itc:[string,number][];itcTotal:number;
  /** TDS held back (Cr 2150; the funding side is credited net). */tds:number;
  /** RCM GST (Cr 2140) and its debit: the ITC keys when ITC is allowed, else none (the category ledger takes it). */rcm:number;rcmItc:[string,number][]};
const itcKeys=(paise:number,inter:boolean):[string,number][]=>{if(!paise)return [];if(inter)return [[EXP_KEY_IGST,paise]];const g=gstSplit(paise);return [['tax:gst-input',g.cgst],['tax:gst-input-sgst',g.sgst]];};
/** Purchase lines of a record from its own fields (a reversal / return record carries negative amounts, so every line mirrors). */
export function expPurchaseLines(e:Expense):ExpPurchaseLines{
  const inter=e.gstKind==='inter',itc=!e.rcm&&e.itc==='allowed'?e.gstPaise??0:0;
  const rcm=e.rcm?expBpsOf(e.amount,e.rcmBps??0):0;
  return {itc:itcKeys(itc,inter),itcTotal:itc,tds:e.tdsPaise??0,rcm,rcmItc:rcm&&e.itc==='allowed'?itcKeys(rcm,inter):[]};
}

// ── Vendor payments, advances and open bills (contract §3.2) ──
export const expVendorPayments=(s:State):ExpVendorPayment[]=>s.expensesX?.vendorPayments??[];
/** Who records vendor payments / returns (contract §9 PERM-EXP-0xx expenses.vendor.pay): the Owner, the Manager, the Accountant. */
export const EXP_VENDOR_PAY_ROLES=['Owner','Manager','Accountant'];
/** Drawer cash a vendor payment takes out on `date` (positive = out): cash payments dated `date` that are not voided the same day, minus
 *  earlier cash payments voided on `date` (their cash comes back). `voidAt` holds the business date of the void (as Udhaar write-offs). */
export function expVendorPayCash(s:State,date:string):number{
  let t=0;for(const p of expVendorPayments(s)){if(!mnyIsDrawer(s,p.method))continue;
    if(p.date===date&&!(p.void&&p.voidAt===date))t+=p.amount;else if(p.void&&p.voidAt===date&&p.date<date)t-=p.amount;}
  return t;
}
/** Coordinator ruling (ACC-2, rulebook T11): the allocation id of a vendor's opening balance (it has no expense record). */
export const EXP_OPENING='opening';
/** The vendor's opening balance from the APPROVED opening entry (contract ACC-1 §5): Σ credit − debit on `2200/<vendor>` (positive = the
 *  hotel owes), dated the entry's asOf; undefined when there is none. */
export function expVendorOpening(s:State,vendor:string):{amount:number;date:string}|undefined{
  const o=s.accountingX?.opening;if(o?.status!=='approved')return undefined;
  const id='2200/'+expVendorKey(s,vendor);let amount=0,hit=false;
  for(const l of o.lines)if(l.account===id){amount+=l.credit-l.debit;hit=true;}
  return hit?{amount,date:o.asOf}:undefined;
}
/** A payment stands on `asOf`: dated on or before it and not void — or voided on a later business day (a reversing void). */
export const expVendorPayLive=(p:ExpVendorPayment,asOf:string)=>p.date<=asOf&&(!p.void||(!!p.voidAt&&p.voidAt>asOf&&p.voidAt!==p.date));
/** A credit-funded bill (or return) record of the vendor that counts (Posted / Reversed originals and their records). */
const vendorRecords=(s:State,key:string)=>s.expenses.filter(e=>counts(e)&&expVendorKey(s,e.vendor)===key);
/** Payable of a bill to the vendor: bill − TDS held back (the vendor is credited net, contract §3.1). */
export const expBillPayable=(e:Expense)=>e.amount-(e.tdsPaise??0);
/** Σ allocated to one bill by payments that are not void. */
export function expBillAllocated(s:State,expenseId:string):number{
  let t=0;for(const p of expVendorPayments(s))if(!p.void)for(const a of p.allocations)if(a.expense===expenseId)t+=a.amount;return t;
}
export type ExpOpenBill={expense:string;date:string;vendor:string;category:string;description:string;payable:number;returned:number;allocated:number;open:number;days:number};
const days=(from:string,to:string)=>Math.round((Date.parse(to+'T00:00:00Z')-Date.parse(from+'T00:00:00Z'))/86400000);
/** Open bills of a vendor (credit-funded, posted, not reversed), oldest first: payable − posted returns − allocations (floored at 0). */
export function expVendorOpenBills(s:State,vendor:string,asOf=s.date,all=false):ExpOpenBill[]{
  const key=expVendorKey(s,vendor),recs=vendorRecords(s,key);
  const returned=new Map<string,number>();for(const r of recs)if(expIsReturn(r))returned.set(expReturnOf(r),(returned.get(expReturnOf(r))??0)-expBillPayable(r));
  // Coordinator ruling (ACC-2): the vendor's opening balance (approved opening entry, 2200/<vendor>) is the OLDEST open item, expense 'opening'.
  const op=expVendorOpening(s,key),opening:ExpOpenBill[]=[];
  if(op&&op.amount>0&&op.date<=asOf){let allocated=0;for(const p of expVendorPayments(s))if(!p.void&&expVendorKey(s,p.vendor)===key)for(const x of p.allocations)if(x.expense===EXP_OPENING)allocated+=x.amount; // per vendor
    opening.push({expense:EXP_OPENING,date:op.date,vendor:key,category:'Opening balance',description:'Opening balance',payable:op.amount,returned:0,allocated,open:Math.max(0,op.amount-allocated),days:days(op.date,asOf)});}
  return [...opening,...recs.filter(e=>e.status==='Posted'&&!e.reversalOf&&!expIsReturn(e)&&mnyIsCredit(s,e.method)&&e.amount>0&&e.date<=asOf)
    .sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.id<b.id?-1:a.id>b.id?1:0)
    .map(e=>{const payable=expBillPayable(e),ret=returned.get(e.id)??0,allocated=expBillAllocated(s,e.id);
      return {expense:e.id,date:e.date,vendor:key,category:e.category,description:e.description??e.vendor,payable,returned:ret,allocated,open:Math.max(0,payable-ret-allocated),days:days(e.date,asOf)};})]
    .filter(b=>all||b.open>0);
}
/** Oldest-first allocation of `amount` to the vendor's open bills (contract §3.2 default); the rest is an advance. */
export function expVendorAllocate(s:State,vendor:string,amount:number):{expense:string;amount:number}[]{
  const out:{expense:string;amount:number}[]=[];let left=Math.max(0,amount);
  for(const b of expVendorOpenBills(s,vendor)){if(left<=0)break;const a=Math.min(left,b.open);if(a>0){out.push({expense:b.expense,amount:a});left-=a;}}
  return out;
}
/** Refusal of an explicit allocation list ('' = fine): each line an open bill of this vendor, at most its open amount, Σ ≤ the payment. */
export function expVendorAllocationRefusal(s:State,vendor:string,amount:number,allocations:unknown):string{
  if(!Array.isArray(allocations))return 'Allocations: a list of {expense, amount}.';
  const open=new Map(expVendorOpenBills(s,vendor).map(b=>[b.expense,b.open]));let total=0;const seen=new Set<string>();
  for(const a of allocations as {expense?:unknown;amount?:unknown}[]){
    const id=txt(a?.expense),amt=a?.amount;
    if(!id||!open.has(id))return `Bill ${id||'(none)'} is not an open bill of this vendor.`;
    if(seen.has(id))return `Bill ${id} is allocated twice.`;seen.add(id);
    if(!(Number.isSafeInteger(amt)&&(amt as number)>0))return `Enter a positive amount for bill ${id}.`;
    if((amt as number)>open.get(id)!)return `Bill ${id} has only ${(open.get(id)!/100).toFixed(2)} open.`;
    total+=amt as number;
  }
  return total>amount?'The allocations add up to more than the payment.':'';
}
/** Balance of the vendor's party ledger 2200/<vendor> as the vouchers post it (positive = the hotel owes): Σ credit-funded counted records
 *  (bills, reversal and return records, each net of TDS) − Σ payments that are not void. */
export function expVendorBalance(s:State,vendor:string,asOf=s.date):number{
  const key=expVendorKey(s,vendor);let t=0;
  const op=expVendorOpening(s,key);if(op&&op.date<=asOf)t+=op.amount; // the approved opening balance (Cr = the hotel owes)
  for(const e of vendorRecords(s,key))if(e.date<=asOf&&mnyIsCredit(s,e.method))t+=expBillPayable(e);
  for(const p of expVendorPayments(s))if(expVendorPayLive(p,asOf)&&expVendorKey(s,p.vendor)===key)t-=p.amount;
  return t;
}
/** Advance with the vendor (the vendor owes the hotel, Dr balance): open bills − the ledger balance, when positive — the unallocated part of
 *  payments plus returns beyond what their bill still owed. */
export function expVendorAdvance(s:State,vendor:string,asOf=s.date):number{
  const open=expVendorOpenBills(s,vendor,asOf).reduce((a,b)=>a+b.open,0);return Math.max(0,open-expVendorBalance(s,vendor,asOf));
}
export type ExpAgeBucket='0–30'|'31–60'|'61–90'|'90+';
export const EXP_AGE_BUCKETS:ExpAgeBucket[]=['0–30','31–60','61–90','90+'];
export const expAgeBucket=(d:number):ExpAgeBucket=>d<=30?'0–30':d<=60?'31–60':d<=90?'61–90':'90+';
export type ExpVendorAgeRow={vendor:string;name:string;open:number;advance:number;net:number;buckets:Record<ExpAgeBucket,number>;bills:ExpOpenBill[]};
/** Creditor ageing by allocations (contract §3.2): per vendor, the open bills by bill-date bucket (0–30 / 31–60 / 61–90 / 90+ days to
 *  `asOf`), the advance and the net (= the party ledger balance 2200/<vendor>). Vendors with nothing open and no advance are left out; `vendor` limits to one. */
export function expVendorAgeing(s:State,asOf=s.date,vendor?:string):ExpVendorAgeRow[]{
  const keys=new Set<string>();
  for(const e of s.expenses)if(counts(e)&&txt(e.vendor))keys.add(expVendorKey(s,e.vendor));
  for(const l of s.accountingX?.opening?.status==='approved'?s.accountingX.opening.lines:[])if(l.account.startsWith('2200/'))keys.add(l.account.slice(5)); // opening balances
  for(const p of expVendorPayments(s))keys.add(expVendorKey(s,p.vendor));
  const want=vendor?expVendorKey(s,vendor):'';
  return [...keys].filter(k=>k&&(!want||k===want)).sort().map(k=>{
    const bills=expVendorOpenBills(s,k,asOf),buckets={'0–30':0,'31–60':0,'61–90':0,'90+':0} as Record<ExpAgeBucket,number>;
    for(const b of bills)buckets[expAgeBucket(b.days)]+=b.open;
    const open=bills.reduce((a,b)=>a+b.open,0),net=expVendorBalance(s,k,asOf),advance=Math.max(0,open-net);
    return {vendor:k,name:expVendorName(s,k),open,advance,net,buckets,bills};
  }).filter(r=>r.open||r.advance);
}

// ── Returns (contract §3.5) ──
/** Σ already returned (posted or pending, not void / rejected) against a bill: [amount, GST] as positive paise. */
export function expReturned(s:State,expenseId:string):{amount:number;gst:number}{
  let amount=0,gst=0;for(const r of s.expenses)if(expIsReturn(r)&&expReturnOf(r)===expenseId&&r.status!=='Void'&&r.status!=='Rejected'&&r.status!=='Reversed'&&!r.reversalOf){amount-=r.amount;gst-=r.gstPaise??0;}
  return {amount,gst};
}
/** The credit method a return is booked through (Dr 2200/<vendor>): the bill's own method when it is a credit method, else the first active
 *  credit method usable for expenses (seed: Vendor credit). */
export function expReturnMethod(s:State,e:Expense):string|undefined{
  if(mnyIsCredit(s,e.method))return mnyMethod(s,e.method)?.name??e.method;
  return mnyNames(s,'expenses').find(n=>mnyIsCredit(s,n));
}
