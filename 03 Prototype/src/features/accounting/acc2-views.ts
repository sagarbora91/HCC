// ACC-2 integration · UI view adapters (replaces the acc2-ui lane's `_contract-stubs-2.ts`). Thin wrappers that shape the lanes' REAL
// selectors (purchase, manual, tds, gst, flows) into the rows the ACC-2 screens draw, plus the UI-owned command payload types (as ACC-1's
// `commands.ts`). No rules live here: every figure comes from the domain export named beside it. Money = integer paise.
import {accManualProblems,accLoans,expVendorOpenBills,expVendorAgeing,expVendorName,expItcBlockRefusal,expVendorPayments,accAssetRegister,
  chnPayouts,accPartyBalance,accPartyRetireBlocker,tdsTable,tdsDeducteeReport,tdsDeducteeCsv,tdsReceivableReport,tdsForBill,accGstMonth,
  accCashGuard,mnyVendor,accIsParty,accControlOf,accPartyIdOf} from '../../services/mock';
import type {State,ACCManualVoucher,ACCManualLine,ACCManualTemplate,ACCAsset,ExpVendorPayment,CHNPayout} from '../../services/mock';
import {csvMinimal} from '../../domain/csv';

export type {ACCManualVoucher,ACCManualLine,ACCManualTemplate,ACCAsset,ExpVendorPayment,CHNPayout};
const rs=(p:number)=>(p/100).toFixed(2);
const csv=(rows:(string|number)[][])=>rows.map(r=>r.map(c=>csvMinimal(c)).join(',')).join('\r\n');
const newestFirst=<T extends {at:string}>(a:T,b:T)=>a.at<b.at?1:a.at>b.at?-1:0;

// ───────────────────────────── §2 manual vouchers (acc2-manual) ─────────────────────────────
/** Manual vouchers newest first (the slice the manual lane keeps). */
export const accManualList=(s:State):ACCManualVoucher[]=>[...(s.accountingX?.manual??[])].sort(newestFirst);
/** The §2 guard messages for the entry form (the manual lane's accManualProblems; the narration is checked by the form itself). [] = acceptable. */
export const accManualCheck=(s:State,v:{date:string;voucherType:ACCManualVoucher['type'];lines:ACCManualLine[]}):string[]=>
  accManualProblems(s,{date:v.date,type:v.voucherType,narration:'Manual voucher',lines:v.lines});
export type AccManualSave={type:'ACC_MANUAL_SAVE';id?:string;date:string;
  /** Contract §2 (amended, ruling 1): the voucher type travels as `voucherType` (the command's own `type` is its name). */
  voucherType:ACCManualVoucher['type'];narration:string;reference?:string;lines:ACCManualLine[];template?:ACCManualTemplate;key?:string};
export type AccManualDecide={type:'ACC_MANUAL_DECIDE';id:string;approve:boolean;reason?:string;key?:string};
export type AccManualVoid={type:'ACC_MANUAL_VOID';id:string;reason:string;key?:string};
export type AccLoanAdd={type:'ACC_LOAN_ADD';code:string;name:string;lender:string;accountLast4?:string;kind:'secured'|'unsecured';note?:string;key?:string};
/** A loan party row for pickers (ruling 2: `2400/<CODE>`; kind from its group). */
export type ACCLoan={id:string;code:string;name:string;lender:string;accountLast4?:string;kind:'secured'|'unsecured';active:boolean};
export const accLoanRows=(s:State):ACCLoan[]=>accLoans(s).map(l=>({id:l.code,code:l.code.slice(l.code.indexOf('/')+1),name:l.name,lender:l.lender,
  ...(l.accountLast4?{accountLast4:l.accountLast4}:{}),kind:l.group==='unsecured-loans'?'unsecured' as const:'secured' as const,active:l.active}));

// ───────────────────────────── §3 vendor payments, assets (acc2-purchase) ─────────────────────────────
export type ExpOpenBill={expense:string;date:string;bill:string;amount:number;paid:number;open:number;ageDays:number};
export type ExpAgeingRow={vendor:string;b0:number;b31:number;b61:number;b90:number;total:number;advance:number};
export const EXP_AGEING_BUCKETS=['0–30','31–60','61–90','90+'] as const;
/** Open bills of a vendor, oldest first (expVendorOpenBills; the opening balance is the oldest item, ruling 8). `amount` = payable less returns. */
export function expOpenBillRows(s:State,vendor:string):ExpOpenBill[]{
  return expVendorOpenBills(s,vendor).map(b=>({expense:b.expense,date:b.date,bill:b.expense==='opening'?'Opening balance':(s.expenses.find(e=>e.id===b.expense)?.receipt||b.expense),
    amount:b.payable-b.returned,paid:b.allocated,open:b.open,ageDays:b.days}));
}
/** Creditor ageing by allocations (expVendorAgeing): one row per vendor, by name (the pay dialog sends the name; the command resolves the master). */
export const expVendorAgeingRows=(s:State):ExpAgeingRow[]=>expVendorAgeing(s).map(r=>({vendor:r.name,b0:r.buckets['0–30'],b31:r.buckets['31–60'],b61:r.buckets['61–90'],b90:r.buckets['90+'],total:r.open,advance:r.advance}));
/** Vendor payments newest first, the vendor shown by name (the record keeps the master code). */
export const expVendorPaymentList=(s:State):ExpVendorPayment[]=>[...expVendorPayments(s)].sort(newestFirst).map(p=>({...p,vendor:expVendorName(s,p.vendor)}));
export const expVendorAgeingCsv=(rows:ExpAgeingRow[])=>csv([['Vendor','0-30','31-60','61-90','90+','Open total','Advance'],...rows.map(r=>[r.vendor,rs(r.b0),rs(r.b31),rs(r.b61),rs(r.b90),rs(r.total),rs(r.advance)])]);
export type ExpVendorPay={type:'EXP_VENDOR_PAY';vendor:string;amount:number;method:string;reference:string;date?:string;/** may include `{expense:'opening', amount}` (ruling 8). */allocations?:{expense:string;amount:number}[];key?:string};
export type ExpVendorPayVoid={type:'EXP_VENDOR_PAY_VOID';id:string;reason:string;key?:string};
export type ExpVendorReturn={type:'EXP_VENDOR_RETURN';expense:string;amount:number;gstPaise:number;reason:string;key?:string};
/** The fixed-asset register (accAssetRegister: newest first). */
export const accAssetsList=(s:State):ACCAsset[]=>accAssetRegister(s);
/** DEC-143 refusal for the expense form ('' = ITC may be allowed): the purchase lane's guard (category on the s.17(5) list). */
export const expPurchaseItcGuard=(s:State,i:{category:string;itc:'allowed'|'disallowed';gstBps?:number}):string=>expItcBlockRefusal(s,i.category,i.itc);
/** TDS the bill would hold back (the tds lane's tdsForBill, contract §5; whole rupees, payee column — ruling 9). */
export const tdsForBillPaise=(s:State,e:{vendor?:string;amount:number;gstPaise?:number;date:string}):number=>tdsForBill(s,{vendor:e.vendor??'',amount:e.amount,gstPaise:e.gstPaise??0,date:e.date}).paise;

// ───────────────────────────── §4 OTA payouts, deposit apply, company retire (acc2-flows) ─────────────────────────────
export const chnPayoutList=(s:State):CHNPayout[]=>[...chnPayouts(s)].sort(newestFirst);
/** What the channel owes the hotel: its party ledger 1115/<channel> (the CHN_PAYOUT guard uses the same balance). */
export const chnOpenReceivable=(s:State,channel:string):number=>Math.max(0,accPartyBalance(s,'1115/'+channel));
export type ChnPayoutCmd={type:'CHN_PAYOUT';channel:string;date:string;statementRef:string;gross:number;commission:number;commissionGst:number;tcs:number;net:number;bank:string;bookings:string[];key?:string};
/** Ruling 4: `source:'opening'` takes an advance carried in the opening balance (Manager / Owner); default = the stay's own deposit receipts. */
export type DepositApply={type:'DEPOSIT_APPLY';booking:string;amount:number;source?:'receipts'|'opening';key?:string};
/** Why a party (e.g. `1110/<company>`) cannot be retired: its ledger balance (accPartyRetireBlocker). '' = it can. */
export const accPartyRetireReason=(s:State,partyLedger:string):string=>accIsParty(partyLedger)?accPartyRetireBlocker(s,accControlOf(partyLedger),accPartyIdOf(partyLedger)):'';

// ───────────────────────────── §5 TDS (acc2-tds) ─────────────────────────────
export type TdsRateRow={section:string;label:string;oldSection:string;threshold:string;rateIndividual:number;rateOther:number;from:string;source:string;caConfirm:boolean};
/** The dated rate table in force on `date` (tdsTable). Every row is "CA to confirm"; versions start 2026-04-01 (TDS_SEED). */
export const tdsRateRows=(s:State,date:string):TdsRateRow[]=>date<'2026-04-01'?[]:tdsTable(s,date).map(r=>({section:r.code,label:r.label,oldSection:r.section,threshold:r.threshold,
  rateIndividual:r.individual,rateOther:r.other,from:'2026-04-01',source:r.source,caConfirm:true}));
export type TdsDeducteeRow={vendor:string;pan:string;section:string;taxable:number;tds:number;remitted:number};
/** The screen's period ('YYYY-MM' or 'FY2026') → the report's ('YYYY-MM' or '2026-27'). */
const tdsPeriodOf=(period:string)=>/^FY\d{4}$/.test(period)?`${period.slice(2)}-${String((+period.slice(2)+1)%100).padStart(2,'0')}`:period;
export const tdsDeducteeRows=(s:State,period:string):TdsDeducteeRow[]=>tdsDeducteeReport(s,tdsPeriodOf(period)).rows.map(r=>({vendor:r.vendor,pan:r.pan,section:r.section,taxable:r.taxable,tds:r.tds,remitted:r.remitted}));
/** The deductee CSV of the period (tdsDeducteeCsv over the real report). */
export const tdsDeducteeCsvOf=(s:State,period:string)=>tdsDeducteeCsv(tdsDeducteeReport(s,tdsPeriodOf(period)));
export type TdsReceivableRow={company:string;month:string;tds:number};
/** Form 168 reconciliation rows (tdsReceivableReport): TDS deducted by companies, by company and month. */
export const tdsReceivableRows=(s:State,fy?:string):TdsReceivableRow[]=>tdsReceivableReport(s,fy?tdsPeriodOf(fy):undefined).rows.map(r=>({company:r.name,month:r.month,tds:r.tds}));
export type AccTdsRemit={type:'ACC_TDS_REMIT';month:string;section:string;amount:number;method:string;challan:string;date:string;key?:string};

// ───────────────────────────── §6 GST month (acc2-gst) ─────────────────────────────
export type ACCGstHead='igst'|'cgst'|'sgst';
export type ACCGstSetoffStep={credit:ACCGstHead;against:ACCGstHead;amount:number};
export type ACCGstMonthView={month:string;output:Record<ACCGstHead,number>;input:Record<ACCGstHead,number>;
  /** 2140 reverse-charge GST still payable at month end. */rcm:number;tcs:number;cashLedger:number;
  setoff:ACCGstSetoffStep[];payable:Record<ACCGstHead,number>;payableTotal:number;
  /** What the challan must deposit: payable less the cash already in 1340. */challan:number;carried:Record<ACCGstHead,number>;posted?:string};
export const accGstHeads:ACCGstHead[]=['igst','cgst','sgst'];
/** The month's working for the GST tab, from accGstMonth (s.49 order, carried credit, 1340 cash, the posted set-off). */
export function accGstMonthView(s:State,month:string):ACCGstMonthView{
  const g=accGstMonth(s,month),steps:ACCGstSetoffStep[]=[];
  for(const c of accGstHeads)for(const a of accGstHeads){const n=g.setoff.use[c][a];if(n>0)steps.push({credit:c,against:a,amount:n});}
  return {month,output:{...g.output},input:{...g.input},rcm:Math.max(0,g.rcmBalance),tcs:g.tcs,cashLedger:g.cashBalance,setoff:steps,payable:{...g.setoff.cash},
    payableTotal:g.setoff.payable,challan:g.setoff.challan,carried:{...g.setoff.creditLeft},...(g.posted?{posted:g.posted.date}:{})};
}
export type AccGstSetoff={type:'ACC_GST_SETOFF';month:string;key?:string};
/** `rcm` = the reverse-charge part of the challan in paise (Dr 2140 · Cr 1340 inside the same voucher). */
export type AccGstPay={type:'ACC_GST_PAY';month:string;amount:number;method:string;challan:string;date:string;rcm?:number;key?:string};
export type AccStatRemit={type:'ACC_STAT_REMIT';kind:'pf'|'esic'|'pt';month:string;amount:number;method:string;challan:string;date:string;key?:string};
export type AccMonthLockCmd={type:'ACC_MONTH_LOCK';month:string;key?:string};
export type AccMonthLockRequest={type:'ACC_MONTH_LOCK_REQUEST';month:string;key?:string};
export type AccMonthUnlockCmd={type:'ACC_MONTH_UNLOCK';month:string;reason:string;key?:string};

// ───────────────────────────── §7 cash guards (acc2-flows) ─────────────────────────────
export type ACCCashGuardInput={kind:'receipt'|'payment';party:string;amount:number;date:string;transport?:boolean};
export type ACCCashGuardVerdict={ok:boolean;code?:'CASH_LIMIT';refusal?:string;warning?:string;deductionLost?:boolean};
/** The form's preview of the command's guard (accCashGuard: the party's day total with this amount). A vendor named by name or code is its
 *  party ledger 2200/<code>; a party ledger id passes through. */
export function accCashVerdict(s:State,i:ACCCashGuardInput):ACCCashGuardVerdict{
  const party=accIsParty(i.party)?i.party:'2200/'+(mnyVendor(s,i.party)?.code??i.party);
  const g=accCashGuard(s,{kind:i.kind,party,amount:i.amount,date:i.date,...(i.transport?{transport:true}:{})});
  return g.refused?{ok:false,code:'CASH_LIMIT',refusal:g.message}:g.deductionLost?{ok:true,warning:g.warning,deductionLost:true}:{ok:true};
}

// Real selectors the screens use as they are (re-exported so a screen imports its ACC-2 names from one place).
export {expVendorAdvance,expVendorAllocate,accMonthLockBlocker,accMonthLockOf,accAssetsCsv} from '../../services/mock';
