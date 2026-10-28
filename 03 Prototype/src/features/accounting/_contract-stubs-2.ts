// ACC-2 · acc2-ui — CONTRACT STUBS. Every selector, type and command shape the ACC-2 screens need from the other lanes (purchase, manual, tds,
// gst, flows, parity), named exactly as 02 Planning/agents/ACC2-POSTING-CONTRACT.md names them where it names them, with plausible bodies so
// `npx tsc --noEmit` passes and the screens render. NOT rules: the integrator deletes this file and points the screens at the real exports
// (`from '../../services/mock'`). Screens import contract names ONLY from here. Money = integer paise.
import {accTrialBalance6,mnyCategory,mnyVendor,money,addDays} from '../../services/mock';
import type {State,ACCManualVoucher,ACCManualLine,ACCManualTemplate,ACCAsset,ACCMonthLock,ExpVendorPayment,CHNPayout,Expense} from '../../services/mock';
import {csvMinimal} from '../../domain/csv';

export type {ACCManualVoucher,ACCManualLine,ACCManualTemplate,ACCAsset,ACCMonthLock,ExpVendorPayment,CHNPayout};
const rs=(p:number)=>(p/100).toFixed(2);
const csv=(rows:(string|number)[][])=>rows.map(r=>r.map(c=>csvMinimal(c)).join(',')).join('\r\n');

// ───────────────────────────── §2 manual vouchers ─────────────────────────────
/** List of manual vouchers, newest first (state `s.accountingX.manual`). Real name: read the slice. */
export const accManualList=(s:State):ACCManualVoucher[]=>[...(s.accountingX?.manual??[])].sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:0);
/** The guard messages of §2, both sides the same (balanced, active ledgers, never 9999 / 1400 / a control without a party, Contra cash/bank only,
 *  Payment / Receipt exactly one cash-or-bank side, date ≤ business date, not a locked day). [] = acceptable. Stub: the balance, date and 9999/1400 rules. */
export function accManualProblems(s:State,v:{date:string;voucherType:ACCManualVoucher['type'];lines:ACCManualLine[]}):string[]{
  const out:string[]=[],dr=v.lines.reduce((a,l)=>a+l.debit,0),cr=v.lines.reduce((a,l)=>a+l.credit,0);
  if(!v.lines.length||dr<=0)out.push('Enter at least one line with an amount.');
  if(dr!==cr)out.push(`Debits ${money(dr)} must equal credits ${money(cr)}.`);
  if(v.lines.some(l=>l.debit>0&&l.credit>0))out.push('A line has one side only.');
  if(v.lines.some(l=>!l.account))out.push('Choose a ledger on every line.');
  if(v.lines.some(l=>l.account==='9999'||l.account==='1400'))out.push('Suspense (9999) and the opening difference (1400) take no manual entries.');
  if(v.date>s.date)out.push('The date cannot be after the business date.');
  if(s.accountingX?.locks?.[v.date])out.push('That day is locked — unlock the day first (Owner reopen).');
  return out;
}
export type AccManualSave={type:'ACC_MANUAL_SAVE';id?:string;date:string;
  /** The contract names this field `type`; the command discriminator already is `type`, so the payload carries it as `voucherType` (integration note). */
  voucherType:ACCManualVoucher['type'];narration:string;reference?:string;lines:ACCManualLine[];template?:ACCManualTemplate;key?:string};
export type AccManualDecide={type:'ACC_MANUAL_DECIDE';id:string;approve:boolean;reason?:string;key?:string};
export type AccManualVoid={type:'ACC_MANUAL_VOID';id:string;reason:string;key?:string};
export type AccLoanAdd={type:'ACC_LOAN_ADD';code:string;name:string;lender:string;accountLast4?:string;kind:'secured'|'unsecured';note?:string;key?:string};
/** Owner-added loan rows under the 2400 control (contract §2): `2400/<LN-code>`. Stub reads `chart.ledgers` rows with `kind:'loan'`. */
export type ACCLoan={id:string;code:string;name:string;lender:string;accountLast4?:string;kind:'secured'|'unsecured';active:boolean};
export const accLoans=(s:State):ACCLoan[]=>((s.accountingX?.chart?.ledgers??[]) as unknown as {code:string;name:string;kind?:string;loanKind?:'secured'|'unsecured';lender?:string;accountLast4?:string;active:boolean}[])
  .filter(l=>l.kind==='loan').map(l=>({id:(l.loanKind==='unsecured'?'2410/':'2400/')+l.code,code:l.code,name:l.name,lender:l.lender??'',accountLast4:l.accountLast4,kind:l.loanKind==='unsecured'?'unsecured' as const:'secured' as const,active:l.active}));

// ───────────────────────────── §3 purchases, vendor payments, assets ─────────────────────────────
export type ExpOpenBill={expense:string;date:string;bill:string;amount:number;paid:number;open:number;ageDays:number};
export type ExpAgeingRow={vendor:string;b0:number;b31:number;b61:number;b90:number;total:number;advance:number};
export const EXP_AGEING_BUCKETS=['0–30','31–60','61–90','90+'] as const;
const days=(a:string,b:string)=>Math.round((Date.parse(b+'T12:00:00')-Date.parse(a+'T12:00:00'))/86400000);
/** Vendor-credit bills of one vendor not fully paid by allocations (oldest first). Stub: Posted expenses with funding 'Vendor credit' minus allocations. */
export function expVendorOpenBills(s:State,vendor:string):ExpOpenBill[]{
  const pay=(s.expensesX?.vendorPayments??[]).filter(p=>!p.void&&p.vendor===vendor);
  const paid=new Map<string,number>();for(const p of pay)for(const a of p.allocations)paid.set(a.expense,(paid.get(a.expense)??0)+a.amount);
  const code=mnyVendor(s,vendor)?.code,op=s.accountingX?.opening,ol=code?op?.lines.find(l=>l.account==='2200/'+code&&l.credit>0):undefined;
  const openRow:ExpOpenBill[]=ol&&op?[{expense:'opening',date:op.asOf,bill:'Opening balance',amount:ol.credit,paid:paid.get('opening')??0,open:Math.max(0,ol.credit-(paid.get('opening')??0)),ageDays:days(op.asOf,s.date)}].filter(b=>b.open>0):[];
  const rows=s.expenses.filter(e=>e.vendor===vendor&&e.status==='Posted'&&e.method==='Vendor credit'&&!e.reversalOf&&!e.reversedBy)
    .map(e=>{const net=e.amount-(e.tdsPaise??0),p=paid.get(e.id)??0;return {expense:e.id,date:e.date,bill:e.receipt||e.id,amount:net,paid:p,open:Math.max(0,net-p),ageDays:days(e.date,s.date)};})
    .filter(b=>b.open>0);
  return [...openRow,...rows].sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.expense<b.expense?-1:1);
}
/** Advance paid to a vendor (payments minus allocations; the party ledger is Dr). */
export const expVendorAdvance=(s:State,vendor:string)=>(s.expensesX?.vendorPayments??[]).filter(p=>!p.void&&p.vendor===vendor).reduce((a,p)=>a+p.amount-p.allocations.reduce((x,y)=>x+y.amount,0),0);
/** Default allocation of `amount`: oldest open bill first; the rest is an advance. */
export function expVendorAllocate(s:State,vendor:string,amount:number):{expense:string;amount:number}[]{
  let left=amount;const out:{expense:string;amount:number}[]=[];
  for(const b of expVendorOpenBills(s,vendor)){if(left<=0)break;const a=Math.min(left,b.open);out.push({expense:b.expense,amount:a});left-=a;}
  return out;
}
/** Ageing by bill date over open bills (buckets 0–30 / 31–60 / 61–90 / 90+), plus the vendor's advance. */
export function expVendorAgeing(s:State):ExpAgeingRow[]{
  const vendors=[...new Set([...s.expenses.filter(e=>e.method==='Vendor credit'&&e.vendor).map(e=>e.vendor!),...(s.expensesX?.vendorPayments??[]).map(p=>p.vendor)])].sort();
  return vendors.map(v=>{const r:ExpAgeingRow={vendor:v,b0:0,b31:0,b61:0,b90:0,total:0,advance:expVendorAdvance(s,v)};
    for(const b of expVendorOpenBills(s,v)){r[b.ageDays<=30?'b0':b.ageDays<=60?'b31':b.ageDays<=90?'b61':'b90']+=b.open;r.total+=b.open;}return r;}).filter(r=>r.total>0||r.advance>0);
}
export const expVendorPayments=(s:State):ExpVendorPayment[]=>[...(s.expensesX?.vendorPayments??[])].sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:0);
export const expVendorAgeingCsv=(rows:ExpAgeingRow[])=>csv([['Vendor','0-30','31-60','61-90','90+','Open total','Advance'],...rows.map(r=>[r.vendor,rs(r.b0),rs(r.b31),rs(r.b61),rs(r.b90),rs(r.total),rs(r.advance)])]);
export type ExpVendorPay={type:'EXP_VENDOR_PAY';vendor:string;amount:number;method:string;reference:string;date?:string;/** `allocations` may include `{expense:'opening', amount}` for the vendor's opening balance (coordinator ruling). */allocations?:{expense:string;amount:number}[];key?:string};
export type ExpVendorPayVoid={type:'EXP_VENDOR_PAY_VOID';id:string;reason:string;key?:string};
export type ExpVendorReturn={type:'EXP_VENDOR_RETURN';expense:string;amount:number;gstPaise:number;reason:string;key?:string};
export const accAssetsList=(s:State):ACCAsset[]=>[...(s.accountingX?.assets??[])].sort((a,b)=>a.no<b.no?-1:1);
export const accAssetsCsv=(rows:ACCAsset[])=>csv([['Asset no','Ledger','Name','Vendor','Purchase date','Cost','ITC taken','Put to use','Expense','Disposed on','Disposal amount'],
  ...rows.map(a=>[a.no,a.ledger,a.name,a.vendor,a.date,rs(a.cost),rs(a.itc),a.putToUse??'',a.expense,a.disposed?.date??'',a.disposed?rs(a.disposed.amount):''])]);

// ───────────────────────────── §4 OTA payout, deposit apply, profile switches ─────────────────────────────
export const chnPayouts=(s:State):CHNPayout[]=>[...(s.channelsX?.payouts??[])].sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:0);
/** Open receivable of a channel (1115/<channel> closing, Dr). Stub: the party row of the financial-year trial balance. */
export function chnOpenReceivable(s:State,channel:string):number{
  const y=+s.date.slice(0,4),m=+s.date.slice(5,7),fy=(m>=4?y:y-1)+'-04-01';
  const r=accTrialBalance6(s,fy,s.date,true).rows.find(x=>x.account==='1115/'+channel);return r?Math.max(0,r.closingDebit-r.closingCredit):0;
}
export type ChnPayoutCmd={type:'CHN_PAYOUT';channel:string;date:string;statementRef:string;gross:number;commission:number;commissionGst:number;tcs:number;net:number;bank:string;bookings:string[];key?:string};
/** `source: 'opening'` takes a carried-in opening deposit (Manager / Owner only); the default is the stay's own receipts. */
export type DepositApply={type:'DEPOSIT_APPLY';booking:string;amount:number;source?:'receipts'|'opening';key?:string};
/** OTA-paid stay: the check-out moves the folio balance to the channel receivable — no separate command, `CHECKOUT {otaCredit:true}` (coordinator ruling). */
export type CheckoutOtaCredit={otaCredit?:true};
/** Extension of CMP_PROFILE_SET (Owner-only money settings, §4): the three switches ride along the existing payload. */
export type CmpProfileSwitches={gstOnAdvances:boolean;hotelDeductsTds:boolean;tdsDeductorType:'individual'|'other'};
/** GRP_COMPANY_PAYMENT gains `tds` (§4, A9); GRP_COMPANY_SAVE gains `active` (§8). */
export type GrpCompanyPaymentTds={tds?:number};
export type GrpCompanySaveActive={active?:boolean};
/** Why a company (party) cannot be retired: a non-zero 1110/<company> balance, open stays or blocks (§8). '' = it can. Stub: the balance only. */
export function accPartyRetireBlocker(s:State,partyLedger:string):string{
  const r=accTrialBalance6(s,'2000-01-01',s.date,true).rows.find(x=>x.account===partyLedger);const bal=r?r.closingDebit-r.closingCredit:0;
  return bal?`${partyLedger} still has a balance of ${money(Math.abs(bal))} ${bal>0?'Dr':'Cr'} — settle it first.`:'';
}

// ───────────────────────────── §5 TDS ─────────────────────────────
export type TdsRateRow={section:string;label:string;oldSection:string;threshold:string;rateIndividual:number;rateOther:number;from:string;source:string;caConfirm:boolean};
/** The dated TDS rate table (TDS_SEED) in force on `date`. Stub: the four seed rows of §5. Rates in basis points. */
export function tdsRateRows(_s:State,date:string):TdsRateRow[]{
  const from='2026-04-01',src='Income-tax Act 2025 (old section in brackets) — CA to confirm';
  return date<from?[]:[
    {section:'rent',label:'Rent — land / building (plant 2 %)',oldSection:'194-I',threshold:'₹50,000 a month',rateIndividual:1000,rateOther:1000,from,source:src,caConfirm:true},
    {section:'contractor',label:'Contractor (single ₹30,000 / year ₹1,00,000)',oldSection:'194C',threshold:'₹30,000 single · ₹1,00,000 year',rateIndividual:100,rateOther:200,from,source:src,caConfirm:true},
    {section:'professional',label:'Professional / technical fees',oldSection:'194J',threshold:'₹50,000 a year',rateIndividual:1000,rateOther:1000,from,source:src,caConfirm:true},
    {section:'commission',label:'Commission / brokerage',oldSection:'194H',threshold:'₹20,000 a year',rateIndividual:200,rateOther:200,from,source:src,caConfirm:true}];
}
export type TdsDeducteeRow={vendor:string;pan:string;section:string;taxable:number;tds:number;remitted:number};
/** Deductee report for a month (YYYY-MM) or financial year ('FY2026'): vendor, PAN, section, taxable, TDS, remitted. Stub: posted expenses with TDS. */
export function tdsDeducteeReport(s:State,period:string):TdsDeducteeRow[]{
  const hit=(d:string)=>period.length===7?d.startsWith(period):true;const by=new Map<string,TdsDeducteeRow>();
  for(const e of s.expenses){if(e.status!=='Posted'||!e.tdsPaise||!hit(e.date))continue;const sec=e.tdsSection??'',k=(e.vendor??'')+'|'+sec;
    const r=by.get(k)??{vendor:e.vendor??'',pan:'',section:sec,taxable:0,tds:0,remitted:0};r.taxable+=e.amount-(e.gstPaise??0);r.tds+=e.tdsPaise;by.set(k,r);}
  return [...by.values()].sort((a,b)=>a.vendor<b.vendor?-1:1);
}
export const tdsDeducteeCsv=(rows:TdsDeducteeRow[],period:string)=>csv([['TDS deducted by the hotel',period],[],['Vendor','PAN','Section','Taxable value','TDS deducted','Remitted'],
  ...rows.map(r=>[r.vendor,r.pan,r.section,rs(r.taxable),rs(r.tds),rs(r.remitted)]),['Total','','',rs(rows.reduce((a,r)=>a+r.taxable,0)),rs(rows.reduce((a,r)=>a+r.tds,0)),rs(rows.reduce((a,r)=>a+r.remitted,0))]]);
export type TdsReceivableRow={company:string;month:string;tds:number};
/** Form 168 reconciliation: 1360 entries by company and month (TDS deducted by companies, §4 A9). Stub: empty until the tds lane lands. */
export const tdsReceivableReport=(_s:State,_fy?:string):TdsReceivableRow[]=>[];
export type AccTdsRemit={type:'ACC_TDS_REMIT';month:string;section:string;amount:number;method:string;challan:string;date:string;key?:string};

// ───────────────────────────── §6 GST month, remittances, month lock ─────────────────────────────
export type ACCGstHead='igst'|'cgst'|'sgst';
export type ACCGstSetoffStep={credit:ACCGstHead;against:ACCGstHead;amount:number};
export type ACCGstMonth={month:string;output:Record<ACCGstHead,number>;input:Record<ACCGstHead,number>;rcm:number;tcs:number;cashLedger:number;
  setoff:ACCGstSetoffStep[];payable:Record<ACCGstHead,number>;payableTotal:number;carried:Record<ACCGstHead,number>;posted?:string};
const HEADS:ACCGstHead[]=['igst','cgst','sgst'];
/** The month's GST working (§6): output by head (2130 / 2110 / 2120), input by head (1330 / 1310 / 1320), RCM 2140, TCS credit and cash ledger 1340, the
 *  s.49 set-off and the net payable per head. Stub: sums the period columns of the trial balance and runs a simple s.49 pass. */
export function accGstMonth(s:State,month:string):ACCGstMonth{
  const from=month+'-01',to=addDays(addDays(from,31).slice(0,8)+'01',-1);const tb=accTrialBalance6(s,from,to>s.date?s.date:to,false);
  const get=(c:string,side:'credit'|'debit')=>tb.rows.find(r=>r.account===c)?.[side]??0;
  const output={igst:get('2130','credit')-get('2130','debit'),cgst:get('2110','credit')-get('2110','debit'),sgst:get('2120','credit')-get('2120','debit')};
  const input={igst:get('1330','debit')-get('1330','credit'),cgst:get('1310','debit')-get('1310','credit'),sgst:get('1320','debit')-get('1320','credit')};
  const cashRow=tb.rows.find(r=>r.account==='1340'),cashLedger=cashRow?cashRow.closingDebit-cashRow.closingCredit:0;
  const out={...output},inn={...input},setoff:ACCGstSetoffStep[]=[];
  const use=(credit:ACCGstHead,against:ACCGstHead)=>{const a=Math.min(Math.max(inn[credit],0),Math.max(out[against],0));if(a>0){inn[credit]-=a;out[against]-=a;setoff.push({credit,against,amount:a});}};
  use('igst','igst');use('igst','cgst');use('igst','sgst');use('cgst','cgst');use('cgst','igst');use('sgst','sgst');use('sgst','igst');
  const payable={igst:Math.max(out.igst,0),cgst:Math.max(out.cgst,0),sgst:Math.max(out.sgst,0)};
  return {month,output,input,rcm:get('2140','credit')-get('2140','debit'),tcs:Math.max(cashLedger,0),cashLedger,setoff,payable,payableTotal:payable.igst+payable.cgst+payable.sgst,
    carried:{igst:Math.max(inn.igst,0),cgst:Math.max(inn.cgst,0),sgst:Math.max(inn.sgst,0)},posted:s.accountingX?.gstSetoffs?.[month]};
}
export const accGstHeads=HEADS;
export const accMonthLockOf=(s:State,month:string):ACCMonthLock|undefined=>s.accountingX?.monthLocks?.[month];
/** What stops the month lock (§6): a day not closed / locked, the set-off not posted, a pending manual voucher. [] = nothing. (The real one may return a string.) */
export function accMonthLockBlocker(s:State,month:string):string[]{
  const out:string[]=[];if(!s.accountingX?.gstSetoffs?.[month])out.push(`The GST set-off for ${month} is not posted.`);
  const pend=(s.accountingX?.manual??[]).filter(v=>v.status==='pending'&&v.date.startsWith(month)).length;
  if(pend)out.push(`${pend} manual voucher${pend===1?' is':'s are'} pending approval in ${month}.`);
  if(month>=s.date.slice(0,7)&&s.date.slice(8)!==new Date(+s.date.slice(0,4),+s.date.slice(5,7),0).getDate().toString().padStart(2,'0'))out.push('The month is not over yet.');
  return out;
}
export type AccGstSetoff={type:'ACC_GST_SETOFF';month:string;key?:string};
export type AccGstPay={type:'ACC_GST_PAY';month:string;amount:number;method:string;challan:string;date:string;rcm?:boolean;key?:string};
export type AccStatRemit={type:'ACC_STAT_REMIT';kind:'pf'|'esic'|'pt';month:string;amount:number;method:string;challan:string;date:string;key?:string};
export type AccMonthLockCmd={type:'ACC_MONTH_LOCK';month:string;key?:string};
export type AccMonthLockRequest={type:'ACC_MONTH_LOCK_REQUEST';month:string;key?:string};
export type AccMonthUnlockCmd={type:'ACC_MONTH_UNLOCK';month:string;reason:string;key?:string};

// ───────────────────────────── §7 cash guards ─────────────────────────────
export const CASH_GUARD_KEYS={receiptLimit:20000000,paymentLimit:1000000,transportLimit:3500000} as const; // cash.receiptLimit / paymentLimit / transportLimit (paise; dated table in the real one)
export type ACCCashGuardInput={kind:'receipt'|'payment';party:string;amount:number;date:string;transport?:boolean};
export type ACCCashGuardVerdict={ok:boolean;code?:'CASH_LIMIT';refusal?:string;warning?:string;deductionLost?:boolean};
/** §7: a cash receipt from one party of ₹2,00,000 or more on a day is refused (s.186); a cash payment above ₹10,000 (₹35,000 transport) is allowed with
 *  a warning and the record carries `cashDeductionLost`. Stub: the single amount only (the real one totals the party's day). */
export function accCashGuard(_s:State,i:ACCCashGuardInput):ACCCashGuardVerdict{
  if(i.kind==='receipt'&&i.amount>=CASH_GUARD_KEYS.receiptLimit)return {ok:false,code:'CASH_LIMIT',refusal:`Cash of ${money(CASH_GUARD_KEYS.receiptLimit)} or more from one party in a day cannot be taken (Income-tax Act 2025 s.186 — VERIFY the section number). Take it by bank or UPI.`};
  const lim=i.transport?CASH_GUARD_KEYS.transportLimit:CASH_GUARD_KEYS.paymentLimit;
  if(i.kind==='payment'&&i.amount>lim)return {ok:true,deductionLost:true,warning:`Cash above ${money(lim)} to one person in a day: the expense is allowed but the income-tax deduction is lost (the old 40A(3) rule — VERIFY the section number). Pay by bank or UPI next time.`};
  return {ok:true};
}

// ───────────────────────────── §3 purchase fields on the expense form (DEC-143, DEC-145, §3.3 / §3.4 / §5) ─────────────────────────────
/** DEC-143 guard message ('' = ITC may be allowed): ITC only on bills directly for 18 % supplies, never on a category on the s.17(5) list. Not a contract name:
 *  the purchase lane extends `expItcRefusal` with the category; the integrator points the form at the real guard. */
export function expPurchaseItcGuard(s:State,i:{category:string;itc:'allowed'|'disallowed';gstBps?:number}):string{
  if(i.itc!=='allowed')return '';
  if(mnyCategory(s,i.category)?.itcBlocked)return 'ITC cannot be allowed on this category — it is on the blocked-credit list (section 17(5)); input credit is claimed only on bills directly for 18 % supplies (DEC-143). Mark ITC disallowed.';
  if(i.gstBps&&i.gstBps!==1800)return 'ITC is allowed only on bills directly for 18 % supplies (DEC-143). Mark ITC disallowed, or correct the GST rate.';
  return '';
}
/** TDS the vendor bill would hold back (§5). The tds lane exports the real one; the purchase lane stubs it `(s, e) => 0` — so do we. */
export const tdsForBill=(_s:State,_e:Pick<Expense,'vendor'|'amount'|'gstPaise'|'date'>):number=>0;
export type { Expense };
